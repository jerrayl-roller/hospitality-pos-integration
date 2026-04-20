using PosApi.Dtos;
using PosApi.Models;

namespace PosApi.Services.Roller;

public interface IRollerSyncService
{
    Task<string?> PushItemsAsync(string bookingUniqueId, List<TabLineItem> addedItems, CancellationToken ct = default);
    Task<string?> PushPaymentsAsync(string bookingUniqueId, List<Payment> payments, CancellationToken ct = default);
}

public class RollerSyncService(
    IRollerApiClient rollerApi,
    ILogger<RollerSyncService> logger) : IRollerSyncService
{
    private static int MapPaymentType(string method) => method switch
    {
        "cash" => 3,
        "gift_card" => 7,
        "visa" or "mastercard" or "amex" or "credit_card" => 1,
        _ => 6
    };

    public async Task<string?> PushItemsAsync(
        string bookingUniqueId, List<TabLineItem> addedItems, CancellationToken ct = default)
    {
        if (addedItems.Count == 0) return null;

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var newItems = addedItems.Select(i => new
        {
            productId = int.Parse(i.ProductId),
            quantity = i.Quantity,
            bookingDate = today,
            priceOverride = (decimal?)null
        }).ToList();

        try
        {
            await rollerApi.PutAsync<object>(
                $"/bookings/{Uri.EscapeDataString(bookingUniqueId)}",
                new { newItems },
                ct);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push items to ROLLER for booking {BookingUniqueId}", bookingUniqueId);
            return "roller_items_sync_failed";
        }
    }

    public async Task<string?> PushPaymentsAsync(
        string bookingUniqueId, List<Payment> payments, CancellationToken ct = default)
    {
        var nonTipPayments = payments
            .Where(p => !p.IsTip && p.Type != "pre_auth" && p.Type != "booking_payment" && p.Status == "success" && p.RollerPushStatus != "pushed")
            .ToList();

        var tipTotal = payments
            .Where(p => p.IsTip && p.Status == "success")
            .Sum(p => p.Amount);

        bool anyFailed = false;

        foreach (var payment in nonTipPayments)
        {
            var tipForPayment = nonTipPayments.Count == 1 ? tipTotal : 0m;
            var cardLast4 = GetLast4(payment.CardNumber);

            var body = new
            {
                id = payment.PaymentId.ToString("N"),
                paymentType = MapPaymentType(payment.Method),
                amount = payment.Amount + tipForPayment,
                tip = tipForPayment > 0 ? (decimal?)tipForPayment : null,
                tipNote = (string?)null,
                cardLast4Digits = cardLast4,
                paymentBrand = IsCardMethod(payment.Method) ? payment.Method : (string?)null
            };

            try
            {
                await rollerApi.PostAsync<object>(
                    $"/bookings/{Uri.EscapeDataString(bookingUniqueId)}/payments",
                    body, ct);
                payment.RollerPushStatus = "pushed";
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to push payment {PaymentId} to ROLLER", payment.PaymentId);
                payment.RollerPushStatus = "failed";
                anyFailed = true;
            }
        }

        return anyFailed ? "roller_payments_sync_failed" : null;
    }

    private static string? GetLast4(string? cardNumber)
    {
        if (cardNumber is null) return null;
        var parts = cardNumber.Split('-');
        var segment = parts.Length == 4 ? parts[3] : cardNumber;
        return segment.Length > 4 ? segment[^4..] : segment;
    }

    private static bool IsCardMethod(string method) =>
        method is "visa" or "mastercard" or "amex" or "credit_card";
}
