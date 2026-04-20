using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dtos;
using PosApi.Models;
using System.Text.Json;

namespace PosApi.Services.Roller;

public interface IBookingService
{
    Task<IEnumerable<BookingSummaryDto>> SearchBookingsAsync(string q, CancellationToken ct = default);
    Task<GuestDetailsDto> GetGuestDetailsAsync(int customerId, CancellationToken ct = default);
    Task<TabDto> ImportBookingAsync(string bookingUniqueId, string? guestName, string? guestEmail, string? guestPhone, CancellationToken ct = default);
}

public class TabAlreadyOpenException(Guid existingTabId) : Exception("tab_already_open")
{
    public Guid ExistingTabId { get; } = existingTabId;
}

public class BookingAlreadyImportedException() : Exception("booking_already_imported");

public class BookingFullyPrepaidException() : Exception("booking_fully_prepaid");

public class BookingService(
    IRollerApiClient rollerApi,
    IProductService productService,
    PosDbContext db,
    IPaymentLockService paymentLockService) : IBookingService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<BookingSummaryDto>> SearchBookingsAsync(string q, CancellationToken ct = default)
    {
        var response = await rollerApi.GetAsync<JsonElement>(
            $"/bookings?keywords={Uri.EscapeDataString(q)}", ct);

        if (!response.TryGetProperty("bookings", out var bookingsArr) || bookingsArr.ValueKind != JsonValueKind.Array)
            return [];

        var lookup = await productService.GetProductLookupAsync(ct);

        var uniqueIds = bookingsArr.EnumerateArray()
            .Select(b => GetString(b, "uniqueId"))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

        var importedIds = await db.Tabs.AsNoTracking()
            .Where(t => t.BookingUniqueId != null && uniqueIds.Contains(t.BookingUniqueId))
            .Select(t => t.BookingUniqueId!)
            .ToHashSetAsync(ct);

        var results = new List<BookingSummaryDto>();
        foreach (var b in bookingsArr.EnumerateArray())
        {
            var bookingUniqueId = GetString(b, "uniqueId");
            if (string.IsNullOrEmpty(bookingUniqueId)) continue;

            var rawItems = GetItemsArray(b);
            string? bookingDate = null;
            if (rawItems.Count > 0 && rawItems[0].TryGetProperty("bookingDate", out var bd))
                bookingDate = bd.GetString();

            var itemPreviews = rawItems
                .Select(item =>
                {
                    var pid = item.TryGetProperty("productId", out var p)
                        ? p.ValueKind == JsonValueKind.Number ? p.GetInt64().ToString() : (p.GetString() ?? "")
                        : "";
                    var name = lookup.TryGetValue(pid, out var n) ? n : $"Product #{pid}";
                    var qty = item.TryGetProperty("quantity", out var qp) ? qp.GetInt32() : 1;
                    return new BookingItemPreview(name, qty);
                })
                .Where(i => !string.IsNullOrEmpty(i.ProductName))
                .ToList();

            int? customerId = null;
            if (b.TryGetProperty("customerId", out var cid) && cid.ValueKind == JsonValueKind.Number)
                customerId = cid.GetInt32();

            results.Add(new BookingSummaryDto(
                BookingUniqueId: bookingUniqueId,
                BookingReference: GetString(b, "bookingReference"),
                GuestName: GetString(b, "name"),
                BookingDate: bookingDate,
                Status: GetString(b, "status"),
                TotalAmount: GetDecimal(b, "total"),
                LineItemCount: rawItems.Count,
                Items: itemPreviews,
                CustomerId: customerId,
                IsImported: importedIds.Contains(bookingUniqueId)
            ));
        }

        return results;
    }

    public async Task<GuestDetailsDto> GetGuestDetailsAsync(int customerId, CancellationToken ct = default)
    {
        var response = await rollerApi.GetAsync<JsonElement>($"/guests/{customerId}", ct);
        return new GuestDetailsDto(
            FirstName: GetString(response, "firstName"),
            LastName: GetString(response, "lastName"),
            Email: GetString(response, "email"),
            Phone: GetString(response, "phone")
        );
    }

    public async Task<TabDto> ImportBookingAsync(string bookingUniqueId, string? guestName, string? guestEmail, string? guestPhone, CancellationToken ct = default)
    {
        // T2.4: prevent duplicate tabs for the same booking
        var existing = await db.Tabs.AsNoTracking()
            .FirstOrDefaultAsync(t => t.BookingUniqueId == bookingUniqueId, ct);
        if (existing is not null)
        {
            if (existing.PaymentStatus is "open" or "pending_lock")
                throw new TabAlreadyOpenException(existing.TabId);
            throw new BookingAlreadyImportedException();
        }

        var bookingDetail = await rollerApi.GetAsync<JsonElement>(
            $"/bookings/{Uri.EscapeDataString(bookingUniqueId)}", ct);

        var remainder = GetDecimal(bookingDetail, "remainder");
        if (remainder < 0.01m)
            throw new BookingFullyPrepaidException();

        var productLookup = await productService.GetProductLookupAsync(ct);

        var rawItems = GetItemsArray(bookingDetail);
        var importedItems = rawItems
            .Select(item =>
            {
                var productIdStr = item.TryGetProperty("productId", out var pid)
                    ? pid.ValueKind == JsonValueKind.Number
                        ? pid.GetInt64().ToString()
                        : (pid.GetString() ?? "")
                    : "";

                var productName = productLookup.TryGetValue(productIdStr, out var name)
                    ? name
                    : $"Product #{productIdStr}";

                var qty = item.TryGetProperty("quantity", out var qProp) ? qProp.GetInt32() : 1;
                var cost = item.TryGetProperty("cost", out var cProp) ? cProp.GetDecimal() : 0m;

                return new TabLineItem(productIdStr, productName, qty, cost);
            })
            .Where(i => !string.IsNullOrEmpty(i.ProductId))
            .ToList();

        var importedTotal = importedItems.Sum(i => i.UnitPrice * i.Quantity);
        var (preAuthCard, preAuthCardType) = GeneratePreAuthCard();

        // T3.1: create tab in pending_lock state first so it can be cleaned up if the lock fails
        var tab = new Tab
        {
            TabId = Guid.NewGuid(),
            BookingUniqueId = bookingUniqueId,
            BookingReference = GetString(bookingDetail, "bookingReference"),
            GuestName = guestName?.Trim() is { Length: > 0 } n ? n : GetString(bookingDetail, "name"),
            GuestEmail = guestEmail?.Trim(),
            GuestPhone = guestPhone?.Trim(),
            ImportedItemsJson = JsonSerializer.Serialize(importedItems),
            AddedItemsJson = "[]",
            PreAuthCardNumber = preAuthCard,
            PreAuthStatus = "simulated",
            GrandTotal = importedTotal,
            PaymentStatus = "pending_lock",
            OpenedAt = DateTime.UtcNow
        };

        var preAuthPayment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            TabId = tab.TabId,
            Type = "pre_auth",
            Method = preAuthCardType,
            CardNumber = preAuthCard,
            Amount = importedTotal,
            Currency = "AUD",
            Status = "success",
            RollerPushStatus = "not_pushed",
            CreatedAt = DateTime.UtcNow
        };

        var existingPayments = GetJsonArray(bookingDetail, "payments")
            .Select(p => new Payment
            {
                PaymentId = Guid.NewGuid(),
                TabId = tab.TabId,
                Type = "booking_payment",
                Method = GetString(p, "paymentMethod") ?? "other",
                CardNumber = GetString(p, "transactionId"),
                Amount = GetDecimal(p, "total"),
                Currency = "AUD",
                Status = "success",
                RollerPushStatus = "not_applicable",
                CreatedAt = DateTime.TryParse(GetString(p, "createdDate"), out var cd)
                    ? cd.ToUniversalTime() : DateTime.UtcNow
            })
            .ToList();

        db.Tabs.Add(tab);
        db.Payments.AddRange(existingPayments);
        db.Payments.Add(preAuthPayment);
        await db.SaveChangesAsync(ct);

        // T3.1: acquire payment lock; roll back the pending tab if the lock cannot be obtained
        try
        {
            await paymentLockService.AcquireLockAsync(bookingUniqueId, ct);
            tab.PaymentStatus = "open";
            await db.SaveChangesAsync(ct);
        }
        catch (PaymentLockFailedException)
        {
            db.Payments.RemoveRange(
                await db.Payments.Where(p => p.TabId == tab.TabId).ToListAsync(ct));
            db.Tabs.Remove(tab);
            await db.SaveChangesAsync(ct);
            throw;
        }

        await db.Entry(tab).Collection(t => t.Payments).LoadAsync(ct);
        return TabDto.FromTab(tab);
    }

    private static (string cardNumber, string cardType) GeneratePreAuthCard()
    {
        var types = new[] { "visa", "mastercard", "amex" };
        var cardType = types[Random.Shared.Next(types.Length)];
        var cardNumber = string.Join("-", Enumerable.Range(0, 4).Select(_ => Random.Shared.Next(1000, 9999).ToString()));
        return (cardNumber, cardType);
    }

    private static string? GetString(JsonElement element, string key) =>
        element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static decimal GetDecimal(JsonElement element, string key) =>
        element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDecimal()
            : 0m;

    private static List<JsonElement> GetJsonArray(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return [.. arr.EnumerateArray()];
    }

    private static List<JsonElement> GetItemsArray(JsonElement element)
    {
        if (!element.TryGetProperty("items", out var items))
            return [];

        if (items.ValueKind == JsonValueKind.Array)
            return [.. items.EnumerateArray()];

        if (items.ValueKind == JsonValueKind.Object)
            return [items];

        return [];
    }
}
