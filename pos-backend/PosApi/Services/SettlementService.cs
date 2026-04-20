using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dtos;
using PosApi.Models;
using PosApi.Services.Roller;
using System.Text.Json;

namespace PosApi.Services;

public class SettlementService(
    PosDbContext db,
    IPaymentLockService paymentLockService,
    IRollerGiftCardService giftCardService,
    ILogger<SettlementService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] ValidMethods = ["pre_auth_card", "new_card", "cash", "gift_card"];

    public async Task<(TabDto? tab, string? error, string? detail)> AddPaymentAsync(
        Guid tabId, AddPaymentRequest req, CancellationToken ct = default)
    {
        if (!ValidMethods.Contains(req.Method))
            return (null, "invalid_method", null);
        if (req.Amount <= 0)
            return (null, "invalid_amount", null);
        if (req.TipAmount < 0)
            return (null, "invalid_tip", null);
        if (req.Method == "gift_card" && string.IsNullOrWhiteSpace(req.GiftCardNumber))
            return (null, "gift_card_number_required", null);

        var tab = await db.Tabs.Include(t => t.Payments)
            .FirstOrDefaultAsync(t => t.TabId == tabId, ct);
        if (tab is null) return (null, "not_found", null);
        if (tab.PaymentStatus != "open") return (null, "tab_not_open", null);
        if (req.Method == "pre_auth_card" && tab.PreAuthCardNumber is null)
            return (null, "no_pre_auth_card", null);

        var paid = tab.Payments
            .Where(p => p.Type != "pre_auth" && p.Status == "success" && !p.IsTip)
            .Sum(p => p.Amount);
        var outstanding = tab.GrandTotal - paid;

        if (req.Amount > outstanding + 0.005m)
            return (null, "exceeds_outstanding", null);

        string? giftCardTransactionId = null;

        if (req.Method == "gift_card")
        {
            var giftCardNumber = req.GiftCardNumber!.Trim();

            var (balanceError, balanceDetail) = await giftCardService.CheckBalanceAsync(giftCardNumber, req.Amount, ct);
            if (balanceError is not null)
                return (null, balanceError, balanceDetail);

            Guid? bookingUniqueId = Guid.TryParse(tab.BookingUniqueId, out var parsedId) ? parsedId : null;
            var (txnId, deductError) = await giftCardService.DeductAsync(giftCardNumber, req.Amount, tabId, bookingUniqueId, ct);
            if (deductError is not null)
                return (null, deductError, null);

            giftCardTransactionId = txnId;
        }

        var (cardNumber, method) = req.Method switch
        {
            "pre_auth_card" => ResolvePreAuth(tab),
            "new_card" => GenerateCard(),
            "cash" => ((string?)null, "cash"),
            "gift_card" => (req.GiftCardNumber?.Trim(), "gift_card"),
            _ => throw new InvalidOperationException("unreachable")
        };

        var now = DateTime.UtcNow;
        db.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            TabId = tabId,
            Type = "payment",
            Method = method,
            CardNumber = cardNumber,
            Amount = req.Amount,
            Currency = "AUD",
            Status = "success",
            IsTip = false,
            RollerGiftCardTransactionId = giftCardTransactionId,
            RollerPushStatus = "not_pushed",
            CreatedAt = now
        });

        if (req.TipAmount > 0)
        {
            db.Payments.Add(new Payment
            {
                PaymentId = Guid.NewGuid(),
                TabId = tabId,
                Type = "payment",
                Method = method,
                CardNumber = cardNumber,
                Amount = req.TipAmount,
                Currency = "AUD",
                Status = "success",
                IsTip = true,
                RollerPushStatus = "not_applicable",
                CreatedAt = now.AddMilliseconds(1)
            });
        }

        await db.SaveChangesAsync(ct);
        await db.Entry(tab).Collection(t => t.Payments).LoadAsync(ct);
        return (TabDto.FromTab(tab), null, null);
    }

    public async Task<(TabDto? tab, string? error)> SettleTabAsync(
        Guid tabId, CancellationToken ct = default)
    {
        var tab = await db.Tabs.Include(t => t.Payments)
            .FirstOrDefaultAsync(t => t.TabId == tabId, ct);
        if (tab is null) return (null, "not_found");
        if (tab.PaymentStatus != "open") return (null, "tab_not_open");

        var paid = tab.Payments
            .Where(p => p.Type != "pre_auth" && p.Status == "success" && !p.IsTip)
            .Sum(p => p.Amount);

        if (paid < tab.GrandTotal - 0.005m)
            return (null, "not_fully_paid");

        if (tab.BookingUniqueId is not null)
        {
            using var lockCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lockCts.CancelAfter(TimeSpan.FromSeconds(5));
            try { await paymentLockService.ReleaseLockAsync(tab.BookingUniqueId, lockCts.Token); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Lock release failed for tab {TabId} — settling anyway", tabId);
            }
        }

        tab.PaymentStatus = "settled";
        tab.SettledAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (TabDto.FromTab(tab), null);
    }

    public async Task<ReceiptData?> GetReceiptAsync(Guid tabId, CancellationToken ct = default)
    {
        var tab = await db.Tabs.AsNoTracking()
            .Include(t => t.Payments)
            .FirstOrDefaultAsync(t => t.TabId == tabId, ct);
        if (tab is null) return null;

        var imported = Deserialize(tab.ImportedItemsJson);
        var added = Deserialize(tab.AddedItemsJson);
        var allItems = imported.Concat(added).ToList();

        var lineItems = allItems.Select(i =>
        {
            var total = Math.Round(i.UnitPrice * i.Quantity, 2);
            var gst = Math.Round(total - (total / 1.1m), 2);
            return new ReceiptLineItem(i.ProductName, i.Quantity, i.UnitPrice, total, gst);
        }).ToList();

        var grandTotal = lineItems.Sum(l => l.LineTotal);
        var gstTotal = lineItems.Sum(l => l.GstAmount);

        var payments = tab.Payments
            .Where(p => p.Type != "pre_auth" && p.Status == "success")
            .OrderBy(p => p.CreatedAt)
            .Select(p => new ReceiptPayment(
                MethodLabel(p.Method),
                FormatRef(p.Method, p.CardNumber),
                p.Amount,
                p.IsTip))
            .ToList();

        return new ReceiptData
        {
            TabId = tabId,
            ReceiptNumber = tabId.ToString("N")[..8].ToUpper(),
            IssuedAt = tab.SettledAt ?? DateTime.UtcNow,
            GuestName = tab.GuestName,
            LineItems = lineItems,
            SubtotalExclGst = Math.Round(grandTotal - gstTotal, 2),
            GstTotal = Math.Round(gstTotal, 2),
            GrandTotal = grandTotal,
            TipTotal = tab.Payments.Where(p => p.IsTip && p.Status == "success").Sum(p => p.Amount),
            Payments = payments
        };
    }

    private static (string? cardNumber, string method) ResolvePreAuth(Tab tab)
    {
        var preAuth = tab.Payments.FirstOrDefault(p => p.Type == "pre_auth");
        return (tab.PreAuthCardNumber, preAuth?.Method ?? "card");
    }

    private static (string? cardNumber, string method) GenerateCard()
    {
        var types = new[] { "visa", "mastercard", "amex" };
        var cardType = types[Random.Shared.Next(types.Length)];
        var cardNumber = string.Join("-", Enumerable.Range(0, 4)
            .Select(_ => Random.Shared.Next(1000, 9999).ToString()));
        return (cardNumber, cardType);
    }

    private static string MethodLabel(string method) => method switch
    {
        "visa" => "Visa",
        "mastercard" => "Mastercard",
        "amex" => "American Express",
        "cash" => "Cash",
        "gift_card" => "Gift Card",
        _ => method
    };

    private static string? FormatRef(string method, string? cardNumber)
    {
        if (method is "cash") return null;
        if (cardNumber is null) return null;
        var parts = cardNumber.Split('-');
        return parts.Length == 4 ? $"**** **** **** {parts[3]}" : cardNumber;
    }

    private static List<TabLineItem> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<TabLineItem>>(json, JsonOpts) ?? [];
}
