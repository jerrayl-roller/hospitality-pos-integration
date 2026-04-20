using Microsoft.Extensions.Caching.Memory;
using PosApi.Dtos;
using System.Text.Json;

namespace PosApi.Services.Roller;

public interface IProductService
{
    Task<IEnumerable<ProductDto>> GetProductsAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetProductLookupAsync(CancellationToken ct = default);
    void InvalidateCache();
}

public class ProductService(IRollerApiClient rollerApi, IMemoryCache cache) : IProductService
{
    private const string CatalogueCacheKey = "fnb_products";
    private const string LookupCacheKey = "all_products_lookup";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<ProductDto>> GetProductsAsync(CancellationToken ct = default)
    {
        await EnsureCacheAsync(ct);
        return cache.Get<IEnumerable<ProductDto>>(CatalogueCacheKey)!;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetProductLookupAsync(CancellationToken ct = default)
    {
        await EnsureCacheAsync(ct);
        return cache.Get<IReadOnlyDictionary<string, string>>(LookupCacheKey)!;
    }

    public void InvalidateCache()
    {
        cache.Remove(CatalogueCacheKey);
        cache.Remove(LookupCacheKey);
    }

    private async Task EnsureCacheAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CatalogueCacheKey, out _)) return;

        var all = await FetchAllRawAsync(ct);

        // Full lookup: every product by ID → display name.
        // For variants, use "Parent — Variant" so booking items show meaningful names.
        var nameLookup = all
            .Where(p => !string.IsNullOrEmpty(p.ProductId))
            .ToDictionary(p => p.ProductId!, p => p.Name ?? "", StringComparer.OrdinalIgnoreCase);

        var displayLookup = all
            .Where(p => !string.IsNullOrEmpty(p.ProductId))
            .ToDictionary(
                p => p.ProductId!,
                p => !string.IsNullOrEmpty(p.ParentProductId) && nameLookup.TryGetValue(p.ParentProductId, out var parent)
                    ? $"{parent} — {p.Name}"
                    : (p.Name ?? ""),
                StringComparer.OrdinalIgnoreCase);

        // F&B catalogue: AddOn + Published, excluding Donation and ExternalGiftCard subtypes.
        var catalogue = all
            .Where(p =>
                !string.IsNullOrEmpty(p.ParentProductId) &&
                string.Equals(p.ProductType, "AddOn", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ProductStatus, "Published", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ProductSubType, "Stock", StringComparison.OrdinalIgnoreCase))
            .Select(p => new ProductDto(
                p.ProductId ?? "",
                p.Name ?? "",
                nameLookup.TryGetValue(p.ParentProductId!, out var parent) ? parent : "",
                p.Price ?? 0m,
                p.ProductType ?? "",
                p.ProductSubType ?? "",
                p.ReportingCategoryName,
                p.ImageUrl))
            .ToList();

        cache.Set(CatalogueCacheKey, (IEnumerable<ProductDto>)catalogue, CacheDuration);
        cache.Set(LookupCacheKey, (IReadOnlyDictionary<string, string>)displayLookup, CacheDuration);
    }

    private async Task<List<RollerProduct>> FetchAllRawAsync(CancellationToken ct)
    {
        var all = new List<RollerProduct>();
        int page = 1;
        const int pageSize = 100;

        while (true)
        {
            var json = await rollerApi.GetAsync<JsonElement>(
                $"/data/products?pageNumber={page}&pageSize={pageSize}", ct);

            var pageProducts = ExtractProducts(json);
            if (pageProducts.Count == 0) break;

            all.AddRange(pageProducts);
            if (pageProducts.Count < pageSize) break;
            page++;
        }

        return all;
    }

    private static List<RollerProduct> ExtractProducts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.Deserialize<List<RollerProduct>>(JsonOpts) ?? [];

        foreach (var key in new[] { "products", "data", "items", "results" })
        {
            if (json.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.Deserialize<List<RollerProduct>>(JsonOpts) ?? [];
        }

        return [];
    }

    private sealed class RollerProduct
    {
        public string? ProductId { get; set; }
        public string? ParentProductId { get; set; }
        public string? Name { get; set; }
        public decimal? Price { get; set; }
        public string? ProductType { get; set; }
        public string? ProductSubType { get; set; }
        public string? ProductStatus { get; set; }
        public string? ReportingCategoryName { get; set; }
        public string? ImageUrl { get; set; }
    }
}
