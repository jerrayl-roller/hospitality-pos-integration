using Microsoft.Extensions.Caching.Memory;
using PosApi.Dtos;
using System.Text.Json;

namespace PosApi.Services.Roller;

public interface IProductService
{
    Task<IEnumerable<ProductDto>> GetProductsAsync(CancellationToken ct = default);
    void InvalidateCache();
}

public class ProductService(IRollerApiClient rollerApi, IMemoryCache cache) : IProductService
{
    private const string CacheKey = "fnb_products";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<ProductDto>> GetProductsAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out IEnumerable<ProductDto>? cached) && cached is not null)
            return cached;

        var products = await FetchAllFromRollerAsync(ct);
        cache.Set(CacheKey, products, CacheDuration);
        return products;
    }

    public void InvalidateCache() => cache.Remove(CacheKey);

    private async Task<IEnumerable<ProductDto>> FetchAllFromRollerAsync(CancellationToken ct)
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

        // Build a lookup of productId → name across ALL products (parent + variation)
        var parentLookup = all
            .Where(p => !string.IsNullOrEmpty(p.ProductId))
            .ToDictionary(p => p.ProductId!, p => p.Name ?? "", StringComparer.OrdinalIgnoreCase);

        // Only return variations (have a parentProductId) that match the F&B filter
        return all
            .Where(p =>
                !string.IsNullOrEmpty(p.ParentProductId) &&
                string.Equals(p.ProductType, "AddOn", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ProductStatus, "Published", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ProductSubType, "Stock", StringComparison.OrdinalIgnoreCase))
            .Select(p => new ProductDto(
                p.ProductId ?? "",
                p.Name ?? "",
                parentLookup.TryGetValue(p.ParentProductId!, out var parent) ? parent : "",
                p.Price ?? 0m,
                p.ProductType ?? "",
                p.ProductSubType ?? "",
                p.ReportingCategoryName,
                p.ImageUrl));
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
