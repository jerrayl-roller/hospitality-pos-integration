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

    // Temporary debug endpoint — remove before Phase 3
    [HttpGet("search/raw")]
    public async Task<IActionResult> SearchRaw([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest();
        var raw = await rollerApi.GetAsync<JsonElement>($"/bookings?keywords={Uri.EscapeDataString(q)}", ct);
        return Ok(raw);
    }

    [HttpGet("/api/guests/{customerId:int}")]
    public async Task<IActionResult> GetGuestDetails(int customerId, CancellationToken ct)
    {
        var details = await bookingService.GetGuestDetailsAsync(customerId, ct);
        return Ok(details);
    }
}
