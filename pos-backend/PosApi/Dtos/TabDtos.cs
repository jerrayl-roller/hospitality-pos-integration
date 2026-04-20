using PosApi.Models;
using System.Text.Json;

namespace PosApi.Dtos;

public record CreateTabRequest(
    string? GuestName,
    string? GuestEmail,
    string? GuestPhone
);

public record TabSummaryDto(
    Guid TabId,
    string? BookingId,
    string? GuestName,
    int ItemCount,
    decimal GrandTotal,
    string PaymentStatus,
    DateTime OpenedAt
)
{
    public static TabSummaryDto FromTab(Tab tab)
    {
        var items = JsonSerializer.Deserialize<List<TabLineItem>>(tab.AddedItemsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return new TabSummaryDto(tab.TabId, tab.BookingId, tab.GuestName, items.Count, tab.GrandTotal, tab.PaymentStatus, tab.OpenedAt);
    }
}

public record AddItemRequest(
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice
);

public record TabLineItem(
    string ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice
);

public class TabDto
{
    public Guid TabId { get; init; }
    public string? BookingId { get; init; }
    public string? GuestName { get; init; }
    public string? GuestEmail { get; init; }
    public string? GuestPhone { get; init; }
    public List<TabLineItem> AddedItems { get; init; } = [];
    public decimal GrandTotal { get; init; }
    public string PaymentStatus { get; init; } = "open";
    public string PreAuthStatus { get; init; } = "none";
    public string? PreAuthCardNumber { get; init; }
    public bool HasPendingConflict { get; init; }
    public DateTime OpenedAt { get; init; }
    public DateTime? SettledAt { get; init; }

    public static TabDto FromTab(Tab tab)
    {
        var items = JsonSerializer.Deserialize<List<TabLineItem>>(tab.AddedItemsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        return new TabDto
        {
            TabId = tab.TabId,
            BookingId = tab.BookingId,
            GuestName = tab.GuestName,
            GuestEmail = tab.GuestEmail,
            GuestPhone = tab.GuestPhone,
            AddedItems = items,
            GrandTotal = tab.GrandTotal,
            PaymentStatus = tab.PaymentStatus,
            PreAuthStatus = tab.PreAuthStatus,
            PreAuthCardNumber = tab.PreAuthCardNumber,
            HasPendingConflict = tab.HasPendingConflict,
            OpenedAt = tab.OpenedAt,
            SettledAt = tab.SettledAt
        };
    }
}
