using Microsoft.AspNetCore.Mvc;
using PosApi.Dtos;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/tabs")]
public class TabsController(TabService tabService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateTab(CancellationToken ct)
    {
        var tab = await tabService.CreateTabAsync(ct);
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
}
