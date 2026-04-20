using Microsoft.AspNetCore.Mvc;
using PosApi.Services.Roller;

namespace PosApi.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet("fnb")]
    public async Task<IActionResult> GetFnbProducts(CancellationToken ct)
    {
        var products = await productService.GetProductsAsync(ct);
        return Ok(products);
    }
}
