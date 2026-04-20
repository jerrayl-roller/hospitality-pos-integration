using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dtos;
using PosApi.Services;
using PosApi.Services.Roller;

namespace PosApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController(
    PosDbContext db,
    IProductService productService,
    IPaymentLockService paymentLockService,
    IBookingResyncService bookingResyncService) : ControllerBase
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

    /// <summary>
    /// Force-releases the payment lock in ROLLER for every tab that was created from a ROLLER booking.
    /// </summary>
    [HttpDelete("tabs/locks")]
    public async Task<IActionResult> ForceReleaseAllLocks(CancellationToken ct)
    {
        var tabs = await db.Tabs
            .Where(t => t.BookingUniqueId != null)
            .ToListAsync(ct);

        var released = new List<string>();
        var failed = new List<object>();

        foreach (var tab in tabs)
        {
            try
            {
                await paymentLockService.ReleaseLockAsync(tab.BookingUniqueId!, ct);
                released.Add(tab.BookingUniqueId!);
            }
            catch (Exception ex)
            {
                failed.Add(new { bookingUniqueId = tab.BookingUniqueId, tabId = tab.TabId, error = ex.Message });
            }
        }

        return Ok(new { released, failed });
    }

    /// <summary>
    /// Phase 4 (minimal): pulls fresh booking state from ROLLER for every booking-linked tab and
    /// refreshes imported items / totals in-place. Tabs where ROLLER reports additional
    /// booking-level payments since import are marked <c>errored</c> rather than updated.
    /// </summary>
    [HttpPost("resync-bookings")]
    public async Task<IActionResult> ResyncBookings(CancellationToken ct)
    {
        var result = await bookingResyncService.ResyncAllTabsAsync(ct);
        return Ok(result);
    }
}
