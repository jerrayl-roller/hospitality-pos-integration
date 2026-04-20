using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PosApi.Services.Roller;

public interface IRollerApiClient
{
    Task<T> GetAsync<T>(string path, CancellationToken ct = default);
    Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default);
    Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default);
    Task DeleteAsync(string path, object? body = null, CancellationToken ct = default);
}

public class RollerApiClient(IHttpClientFactory httpClientFactory, RollerTokenService tokenService) : IRollerApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        SendWithRetryAsync<T>(HttpMethod.Get, path, null, ct);

    public Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default) =>
        SendWithRetryAsync<T>(HttpMethod.Post, path, body, ct);

    public Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default) =>
        SendWithRetryAsync<T>(HttpMethod.Put, path, body, ct);

    public Task DeleteAsync(string path, object? body = null, CancellationToken ct = default) =>
        SendWithRetryAsync<object?>(HttpMethod.Delete, path, body, ct);

    private async Task<T> SendWithRetryAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = await tokenService.GetTokenAsync(ct);
        var (response, content) = await SendAsync(method, path, body, token, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenService.InvalidateToken();
            token = await tokenService.GetTokenAsync(ct);
            (response, content) = await SendAsync(method, path, body, token, ct);
        }

        if (!response.IsSuccessStatusCode)
            ThrowRollerException(response, content);

        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(content))
            return default!;

        return JsonSerializer.Deserialize<T>(content, JsonOpts)!;
    }

    private async Task<(HttpResponseMessage response, string content)> SendAsync(
        HttpMethod method, string path, object? body, string token, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("Roller");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
            request.Content = JsonContent.Create(body);

        var response = await client.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return (response, content);
    }

    private static void ThrowRollerException(HttpResponseMessage response, string content)
    {
        string error = "api_error";
        string detail = content;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString() ?? error;
            if (doc.RootElement.TryGetProperty("message", out var m)) detail = m.GetString() ?? detail;
            else if (doc.RootElement.TryGetProperty("detail", out var d)) detail = d.GetString() ?? detail;
        }
        catch { }

        throw new RollerApiException((int)response.StatusCode, error, detail);
    }
}
