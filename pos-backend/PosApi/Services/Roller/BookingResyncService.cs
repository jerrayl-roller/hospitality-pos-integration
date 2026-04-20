using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dtos;
using PosApi.Models;
using System.Text.Json;

namespace PosApi.Services.Roller;

/// <summary>
/// Phase 4 (minimal) — replaces the planned <c>booking_updated</c> webhook flow with an
/// operator-triggered bulk resync. This service is intentionally self-contained so that it can
/// be deleted in one go once the real webhook pipeline lands.
/// </summary>
public interface IBookingResyncService
{
    Task<BookingResyncResult> ResyncAllTabsAsync(CancellationToken ct = default);
}

public record BookingResyncOutcome(
    Guid TabId,
    string? BookingUniqueId,
    string Status,        // updated | unchanged | errored | failed
    string? Detail        // free-form info (e.g. error message, payment count delta)
);

public record BookingResyncResult(
    int Processed,
    int Updated,
    int Errored,
    int Failed,
    IReadOnlyList<BookingResyncOutcome> Outcomes
);

public class BookingResyncService(
    IRollerApiClient rollerApi,
    IProductService productService,
    PosDbContext db) : IBookingResyncService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<BookingResyncResult> ResyncAllTabsAsync(CancellationToken ct = default)
    {
        var tabs = await db.Tabs
            .Include(t => t.Payments)
            .Where(t => t.BookingUniqueId != null
                && t.PaymentStatus != "settled"
                && t.PaymentStatus != "errored")
            .ToListAsync(ct);

        var productLookup = await productService.GetProductLookupAsync(ct);
        var outcomes = new List<BookingResyncOutcome>(tabs.Count);

        foreach (var tab in tabs)
        {
            try
            {
                var bookingDetail = await rollerApi.GetAsync<JsonElement>(
                    $"/bookings/{Uri.EscapeDataString(tab.BookingUniqueId!)}", ct);

                var rollerPayments = GetJsonArray(bookingDetail, "payments");
                var storedBookingPayments = tab.Payments.Count(p => p.Type == "booking_payment");

                if (rollerPayments.Count > storedBookingPayments)
                {
                    tab.PaymentStatus = "errored";
                    AppendAuditEntry(tab, new
                    {
                        at = DateTime.UtcNow,
                        kind = "resync_additional_payment",
                        storedPayments = storedBookingPayments,
                        rollerPayments = rollerPayments.Count
                    });
                    outcomes.Add(new BookingResyncOutcome(
                        tab.TabId,
                        tab.BookingUniqueId,
                        "errored",
                        $"ROLLER shows {rollerPayments.Count} booking payment(s); POS had {storedBookingPayments}."));
                    continue;
                }

                var rawItems = GetItemsArray(bookingDetail);
                var refreshedItems = rawItems
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

                var refreshedImportedJson = JsonSerializer.Serialize(refreshedItems);
                var changed = refreshedImportedJson != tab.ImportedItemsJson
                    || !string.Equals(tab.BookingReference, GetString(bookingDetail, "bookingReference"), StringComparison.Ordinal);

                tab.ImportedItemsJson = refreshedImportedJson;
                tab.BookingReference = GetString(bookingDetail, "bookingReference") ?? tab.BookingReference;

                var addedItems = JsonSerializer.Deserialize<List<TabLineItem>>(tab.AddedItemsJson, JsonOpts) ?? [];
                tab.GrandTotal = refreshedItems.Sum(i => i.UnitPrice * i.Quantity)
                    + addedItems.Sum(i => i.UnitPrice * i.Quantity);

                if (changed)
                    AppendAuditEntry(tab, new
                    {
                        at = DateTime.UtcNow,
                        kind = "resync_refreshed",
                        itemCount = refreshedItems.Count,
                        grandTotal = tab.GrandTotal
                    });

                outcomes.Add(new BookingResyncOutcome(
                    tab.TabId,
                    tab.BookingUniqueId,
                    changed ? "updated" : "unchanged",
                    changed ? $"Refreshed {refreshedItems.Count} imported item(s)." : null));
            }
            catch (Exception ex)
            {
                outcomes.Add(new BookingResyncOutcome(
                    tab.TabId,
                    tab.BookingUniqueId,
                    "failed",
                    ex.Message));
            }
        }

        await db.SaveChangesAsync(ct);

        return new BookingResyncResult(
            Processed: outcomes.Count,
            Updated: outcomes.Count(o => o.Status == "updated"),
            Errored: outcomes.Count(o => o.Status == "errored"),
            Failed: outcomes.Count(o => o.Status == "failed"),
            Outcomes: outcomes);
    }

    private static void AppendAuditEntry(Tab tab, object entry)
    {
        var log = JsonSerializer.Deserialize<List<JsonElement>>(tab.AuditLogJson, JsonOpts) ?? [];
        log.Add(JsonSerializer.SerializeToElement(entry));
        tab.AuditLogJson = JsonSerializer.Serialize(log);
    }

    private static string? GetString(JsonElement element, string key) =>
        element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

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
