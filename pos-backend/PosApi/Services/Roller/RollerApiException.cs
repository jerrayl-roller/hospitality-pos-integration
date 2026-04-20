namespace PosApi.Services.Roller;

public class RollerApiException(int statusCode, string error, string detail)
    : Exception($"ROLLER API {statusCode}: {error} — {detail}")
{
    public int StatusCode { get; } = statusCode;
    public string Error { get; } = error;
    public string Detail { get; } = detail;
}
