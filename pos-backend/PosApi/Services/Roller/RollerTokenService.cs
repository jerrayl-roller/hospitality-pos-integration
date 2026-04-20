using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace PosApi.Services.Roller;

public class RollerTokenService(IConfiguration config, IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    private const string CacheKey = "roller_access_token";

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out string? token) && token is not null)
            return token;

        return await FetchAndCacheTokenAsync(ct);
    }

    public void InvalidateToken() => cache.Remove(CacheKey);

    private async Task<string> FetchAndCacheTokenAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        var endpoint = config["Roller:TokenEndpoint"]!;
        var body = new { client_id = config["Roller:ClientId"], client_secret = config["Roller:ClientSecret"] };

        var response = await client.PostAsJsonAsync(endpoint, body, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var cacheExpiry = TimeSpan.FromSeconds(expiresIn - 60);

        cache.Set(CacheKey, accessToken, cacheExpiry);
        return accessToken;
    }
}
