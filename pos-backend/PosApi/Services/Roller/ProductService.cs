using PosApi.Dtos;
using System.Text.Json;

namespace PosApi.Services.Roller;

public interface IProductService
{
    Task<IEnumerable<ProductDto>> GetProductsAsync(CancellationToken ct = default);
}

public class ProductService(IRollerApiClient rollerApi) : IProductService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<ProductDto>> GetProductsAsync(CancellationToken ct = default)
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

        return all
            .Where(p =>
                string.Equals(p.ProductType, "AddOn", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ProductStatus, "Published", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.ProductSubType, "Stock", StringComparison.OrdinalIgnoreCase))
            .Select(p => new ProductDto(
                p.ProductId ?? "",
                p.Name ?? "",
                p.Price ?? 0m,
                p.ProductType ?? "",
                p.ProductSubType ?? "",
                p.ReportingCategoryName));
    }

    private static List<RollerProduct> ExtractProducts(JsonElement json)
    {
        // Handle array response directly
        if (json.ValueKind == JsonValueKind.Array)
            return json.Deserialize<List<RollerProduct>>(JsonOpts) ?? [];

        // Handle wrapped response: { "products": [...] } or { "data": [...] }
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
        public string? Name { get; set; }
        public decimal? Price { get; set; }
        public string? ProductType { get; set; }
        public string? ProductSubType { get; set; }
        public string? ProductStatus { get; set; }
        public string? ReportingCategoryName { get; set; }
    }
}
