using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dtos;
using PosApi.Models;
using System.Text.Json;

namespace PosApi.Services;

public class TabService(PosDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<TabDto> CreateTabAsync(CreateTabRequest req, CancellationToken ct = default)
    {
        var tab = new Tab
        {
            TabId = Guid.NewGuid(),
            GuestName = req.GuestName?.Trim(),
            GuestEmail = req.GuestEmail?.Trim(),
            GuestPhone = req.GuestPhone?.Trim(),
            OpenedAt = DateTime.UtcNow
        };
        db.Tabs.Add(tab);
        await db.SaveChangesAsync(ct);
        return TabDto.FromTab(tab);
    }

    public async Task<TabDto?> GetTabAsync(Guid tabId, CancellationToken ct = default)
    {
        var tab = await db.Tabs.AsNoTracking()
            .Include(t => t.Payments)
            .FirstOrDefaultAsync(t => t.TabId == tabId, ct);
        return tab is null ? null : TabDto.FromTab(tab);
    }

    public async Task<TabDto?> AddItemAsync(Guid tabId, AddItemRequest req, CancellationToken ct = default)
    {
        var tab = await db.Tabs.FirstOrDefaultAsync(t => t.TabId == tabId, ct);
        if (tab is null) return null;

        var items = DeserializeItems(tab.AddedItemsJson);
        var existing = items.FirstOrDefault(i => i.ProductId == req.ProductId);

        if (existing is not null)
        {
            var idx = items.IndexOf(existing);
            items[idx] = existing with { Quantity = existing.Quantity + req.Quantity };
        }
        else
        {
            items.Add(new TabLineItem(req.ProductId, req.ProductName, req.Quantity, req.UnitPrice));
        }

        tab.AddedItemsJson = JsonSerializer.Serialize(items);
        tab.GrandTotal = ComputeTotal(items);
        await db.SaveChangesAsync(ct);
        return TabDto.FromTab(tab);
    }

    public async Task<TabDto?> RemoveItemAsync(Guid tabId, string productId, CancellationToken ct = default)
    {
        var tab = await db.Tabs.FirstOrDefaultAsync(t => t.TabId == tabId, ct);
        if (tab is null) return null;

        var items = DeserializeItems(tab.AddedItemsJson);
        var existing = items.FirstOrDefault(i => i.ProductId == productId);

        if (existing is not null)
        {
            var idx = items.IndexOf(existing);
            if (existing.Quantity <= 1)
                items.RemoveAt(idx);
            else
                items[idx] = existing with { Quantity = existing.Quantity - 1 };
        }

        tab.AddedItemsJson = JsonSerializer.Serialize(items);
        tab.GrandTotal = ComputeTotal(items);
        await db.SaveChangesAsync(ct);
        return TabDto.FromTab(tab);
    }

    public async Task<TabDto?> RestoreItemsAsync(Guid tabId, List<TabLineItem> items, CancellationToken ct = default)
    {
        var tab = await db.Tabs.FirstOrDefaultAsync(t => t.TabId == tabId, ct);
        if (tab is null) return null;

        tab.AddedItemsJson = JsonSerializer.Serialize(items);
        tab.GrandTotal = ComputeTotal(items);
        await db.SaveChangesAsync(ct);
        return TabDto.FromTab(tab);
    }

    public async Task<IEnumerable<TabSummaryDto>> GetAllTabsAsync(CancellationToken ct = default)
    {
        var tabs = await db.Tabs.AsNoTracking()
            .OrderByDescending(t => t.OpenedAt)
            .ToListAsync(ct);
        return tabs.Select(TabSummaryDto.FromTab);
    }

    public async Task<(bool success, string? error)> DeleteTabAsync(Guid tabId, CancellationToken ct = default)
    {
        var tab = await db.Tabs.FirstOrDefaultAsync(t => t.TabId == tabId, ct);
        if (tab is null) return (false, "not_found");

        if (tab.AddedItemsJson != "[]" && tab.AddedItemsJson != "[ ]")
        {
            var items = DeserializeItems(tab.AddedItemsJson);
            if (items.Count > 0)
                return (false, "tab_has_items");
        }

        if (tab.PaymentStatus != "open")
            return (false, "tab_not_open");

        db.Tabs.Remove(tab);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    private static List<TabLineItem> DeserializeItems(string json) =>
        JsonSerializer.Deserialize<List<TabLineItem>>(json, JsonOpts) ?? [];

    private static decimal ComputeTotal(List<TabLineItem> items) =>
        items.Sum(i => i.UnitPrice * i.Quantity);
}
