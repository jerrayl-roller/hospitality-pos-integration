using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Services.Roller;

namespace PosApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController(PosDbContext db, IProductService productService) : ControllerBase
{
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        await db.Payments.ExecuteDeleteAsync(ct);
        await db.Tabs.ExecuteDeleteAsync(ct);

        productService.InvalidateCache();

        return Ok(new { cleared = true, message = "All tabs and payments deleted. Product cache cleared — next catalogue load will resync from ROLLER." });
    }
}
