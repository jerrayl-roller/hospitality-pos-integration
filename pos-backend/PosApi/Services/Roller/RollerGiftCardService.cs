using System.Text.Json;

namespace PosApi.Services.Roller;

public interface IRollerGiftCardService
{
    Task<(string? error, string? detail)> CheckBalanceAsync(string giftCardNumber, decimal requiredAmount, CancellationToken ct = default);
    Task<(string? transactionId, string? error)> DeductAsync(string giftCardNumber, decimal amount, Guid tabId, Guid? bookingUniqueId, CancellationToken ct = default);
}

public class RollerGiftCardService(
    IRollerApiClient rollerApi,
    ILogger<RollerGiftCardService> logger) : IRollerGiftCardService
{
    private record BalanceResponse(bool Exists, decimal? Balance, bool Expired);
    private record DeductResponse(string TransactionId, decimal Amount, decimal Balance);

    public async Task<(string? error, string? detail)> CheckBalanceAsync(
        string giftCardNumber, decimal requiredAmount, CancellationToken ct = default)
    {
        BalanceResponse response;
        try
        {
            response = await rollerApi.GetAsync<BalanceResponse>(
                $"/giftcards/{Uri.EscapeDataString(giftCardNumber)}/balance", ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gift card balance check failed for card ending {Last4}", Last4(giftCardNumber));
            return ("gift_card_check_failed", null);
        }

        if (!response.Exists) return ("gift_card_not_found", null);
        if (response.Expired) return ("gift_card_expired", null);
        if (response.Balance is null || response.Balance < requiredAmount)
            return ("gift_card_insufficient_balance", response.Balance?.ToString("0.00") ?? "0.00");

        return (null, null);
    }

    public async Task<(string? transactionId, string? error)> DeductAsync(
        string giftCardNumber, decimal amount, Guid tabId, Guid? bookingUniqueId, CancellationToken ct = default)
    {
        var body = new { amount, transactionId = tabId.ToString(), bookingUniqueId };

        try
        {
            var response = await rollerApi.PostAsync<DeductResponse>(
                $"/giftcards/{Uri.EscapeDataString(giftCardNumber)}/deduct", body, ct);
            return (response.TransactionId, null);
        }
        catch (RollerApiException ex)
        {
            var code = ParseConflictErrorCode(ex.Detail);
            logger.LogWarning(ex, "Gift card deduction failed with code {Code}", code);

            // Idempotency replay — prior deduction succeeded; treat as success.
            if (code == "GIFT_CARD_TRANSACTION_ID_ALREADY_USED")
                return (tabId.ToString(), null);

            return code switch
            {
                "GIFT_CARD_NOT_FOUND"            => (null, "gift_card_not_found"),
                "GIFT_CARD_INACTIVE"             => (null, "gift_card_inactive"),
                "GIFT_CARD_EXPIRED"              => (null, "gift_card_expired"),
                "GIFT_CARD_INSUFFICIENT_BALANCE" => (null, "gift_card_insufficient_balance"),
                _                                => (null, "gift_card_deduct_failed")
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error during gift card deduction");
            return (null, "gift_card_deduct_failed");
        }
    }

    // ROLLER returns 409 Conflict as a FluentValidation failures array:
    // [{ "errorCode": "GIFT_CARD_...", "errorMessage": "..." }]
    private static string? ParseConflictErrorCode(string detail)
    {
        try
        {
            using var doc = JsonDocument.Parse(detail);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                if (first.TryGetProperty("errorCode", out var c)) return c.GetString();
                if (first.TryGetProperty("ErrorCode", out c)) return c.GetString();
            }
        }
        catch { }
        return null;
    }

    private static string Last4(string s) => s.Length >= 4 ? s[^4..] : s;
}
