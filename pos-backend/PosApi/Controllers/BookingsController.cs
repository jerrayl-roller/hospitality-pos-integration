using Microsoft.AspNetCore.Mvc;
using PosApi.Services.Roller;
using System.Text.Json;

namespace PosApi.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController(IBookingService bookingService, IRollerApiClient rollerApi) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 3)
            return BadRequest(new { error = "query_too_short", detail = "Search query must be at least 3 characters." });

        var results = await bookingService.SearchBookingsAsync(q, ct);
        return Ok(results);
    }

    [HttpGet("/api/guests/{customerId:int}")]
    public async Task<IActionResult> GetGuestDetails(int customerId, CancellationToken ct)
    {
        var details = await bookingService.GetGuestDetailsAsync(customerId, ct);
        return Ok(details);
    }
}
