using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Services.Roller;

namespace PosApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController(PosDbContext db, IProductService productService) : ControllerBase
{
    [HttpPost("resync-products")]
    public IActionResult ResyncProducts()
    {
        productService.InvalidateCache();
        return Ok(new { message = "Product cache cleared — next catalogue load will pull fresh data from ROLLER." });
    }

    [HttpPost("clear-data")]
    public async Task<IActionResult> ClearData(CancellationToken ct)
    {
        await db.Payments.ExecuteDeleteAsync(ct);
        await db.Tabs.ExecuteDeleteAsync(ct);
        return Ok(new { message = "All tabs and payments permanently deleted." });
    }
}
