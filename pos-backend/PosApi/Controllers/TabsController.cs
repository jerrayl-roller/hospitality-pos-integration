using Microsoft.AspNetCore.Mvc;
using PosApi.Dtos;
using PosApi.Services;
using PosApi.Services.Roller;

namespace PosApi.Controllers;

[ApiController]
[Route("api/tabs")]
public class TabsController(
    TabService tabService,
    IBookingService bookingService,
    SettlementService settlementService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListTabs(CancellationToken ct)
    {
        var tabs = await tabService.GetAllTabsAsync(ct);
        return Ok(tabs);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTab([FromBody] CreateTabRequest req, CancellationToken ct)
    {
        var tab = await tabService.CreateTabAsync(req, ct);
        return Ok(tab);
    }

    [HttpGet("{tabId:guid}")]
    public async Task<IActionResult> GetTab(Guid tabId, CancellationToken ct)
    {
        var tab = await tabService.GetTabAsync(tabId, ct);
        return tab is null ? NotFound() : Ok(tab);
    }

    [HttpPost("{tabId:guid}/items")]
    public async Task<IActionResult> AddItem(Guid tabId, [FromBody] AddItemRequest req, CancellationToken ct)
    {
        var tab = await tabService.AddItemAsync(tabId, req, ct);
        return tab is null ? NotFound() : Ok(tab);
    }

    [HttpPut("{tabId:guid}/items")]
    public async Task<IActionResult> RestoreItems(Guid tabId, [FromBody] List<TabLineItem> items, CancellationToken ct)
    {
        var tab = await tabService.RestoreItemsAsync(tabId, items, ct);
        return tab is null ? NotFound() : Ok(tab);
    }

    [HttpDelete("{tabId:guid}/items/{productId}")]
    public async Task<IActionResult> RemoveItem(Guid tabId, string productId, CancellationToken ct)
    {
        var tab = await tabService.RemoveItemAsync(tabId, productId, ct);
        return tab is null ? NotFound() : Ok(tab);
    }

    [HttpDelete("{tabId:guid}")]
    public async Task<IActionResult> DeleteTab(Guid tabId, CancellationToken ct)
    {
        var (success, error) = await tabService.DeleteTabAsync(tabId, ct);

        if (!success && error == "not_found") return NotFound();
        if (!success) return Conflict(new { error });

        return NoContent();
    }

    [HttpPost("from-booking")]
    public async Task<IActionResult> ImportFromBooking([FromBody] ImportBookingRequest req, CancellationToken ct)
    {
        try
        {
            var tab = await bookingService.ImportBookingAsync(req.BookingUniqueId, req.GuestName, req.GuestEmail, req.GuestPhone, ct);
            return Ok(tab);
        }
        catch (TabAlreadyOpenException ex)
        {
            return Conflict(new { error = "tab_already_open", existingTabId = ex.ExistingTabId });
        }
        catch (BookingAlreadyImportedException)
        {
            return Conflict(new { error = "booking_already_imported" });
        }
        catch (BookingFullyPrepaidException)
        {
            return Conflict(new { error = "booking_fully_prepaid" });
        }
        catch (PaymentLockFailedException ex)
        {
            return StatusCode(503, new { error = "payment_lock_failed", detail = ex.Detail });
        }
    }

    [HttpPost("{tabId:guid}/payments")]
    public async Task<IActionResult> AddPayment(Guid tabId, [FromBody] AddPaymentRequest req, CancellationToken ct)
    {
        var (tab, error, detail) = await settlementService.AddPaymentAsync(tabId, req, ct);
        if (tab is null && error == "not_found") return NotFound();
        if (tab is null) return Conflict(new { error, detail });
        return Ok(tab);
    }

    [HttpPost("{tabId:guid}/settle")]
    public async Task<IActionResult> SettleTab(Guid tabId, CancellationToken ct)
    {
        var (tab, error) = await settlementService.SettleTabAsync(tabId, ct);
        if (tab is null && error == "not_found") return NotFound();
        if (tab is null) return Conflict(new { error });
        return Ok(tab);
    }

    [HttpPost("{tabId:guid}/retry-sync")]
    public async Task<IActionResult> RetrySync(Guid tabId, CancellationToken ct)
    {
        var (tab, error) = await settlementService.RetrySyncAsync(tabId, ct);
        if (tab is null && error == "not_found") return NotFound();
        if (tab is null) return Conflict(new { error });
        return Ok(tab);
    }

    [HttpGet("{tabId:guid}/receipt")]
    public async Task<IActionResult> GetReceipt(Guid tabId, CancellationToken ct)
    {
        var receipt = await settlementService.GetReceiptAsync(tabId, ct);
        return receipt is null ? NotFound() : Ok(receipt);
    }
}
