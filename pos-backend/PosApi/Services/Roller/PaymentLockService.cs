namespace PosApi.Services.Roller;

public interface IPaymentLockService
{
    Task AcquireLockAsync(string bookingUniqueId, CancellationToken ct = default);
    Task ReleaseLockAsync(string bookingUniqueId, CancellationToken ct = default);
}

public class PaymentLockFailedException(string detail) : Exception($"payment_lock_failed: {detail}")
{
    public string Detail { get; } = detail;
}

public class PaymentLockService(
    IRollerApiClient rollerApi,
    ILogger<PaymentLockService> logger) : IPaymentLockService
{
    public async Task AcquireLockAsync(string bookingUniqueId, CancellationToken ct = default)
    {
        try
        {
            await rollerApi.PostAsync<object>(
                $"/bookings/{Uri.EscapeDataString(bookingUniqueId)}/payment-lock", new { }, ct);

            logger.LogInformation("Payment lock acquired for booking {BookingUniqueId}", bookingUniqueId);
        }
        catch (RollerApiException ex)
        {
            logger.LogWarning("Failed to acquire payment lock for booking {BookingUniqueId}: {Status} {Error}",
                bookingUniqueId, ex.StatusCode, ex.Error);
            throw new PaymentLockFailedException(ex.Detail);
        }
    }

    public async Task ReleaseLockAsync(string bookingUniqueId, CancellationToken ct = default)
    {
        try
        {
            await rollerApi.DeleteAsync(
                $"/bookings/{Uri.EscapeDataString(bookingUniqueId)}/payment-lock", ct: ct);

            logger.LogInformation("Payment lock released for booking {BookingUniqueId}", bookingUniqueId);
        }
        catch (RollerApiException ex) when (ex.StatusCode == 404)
        {
            logger.LogWarning("No active lock found for booking {BookingUniqueId} during release — treating as released", bookingUniqueId);
        }
    }
}
