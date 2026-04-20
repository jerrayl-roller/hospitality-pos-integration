using PosApi.Models;
using System.Text.Json;

namespace PosApi.Dtos;

public record CreateTabRequest(
    string? GuestName,
    string? GuestEmail,
    string? GuestPhone
);

public record ImportBookingRequest(
    string BookingId,
    string? GuestName,
    string? GuestEmail,
    string? GuestPhone
);

public record GuestDetailsDto(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone
);

public record BookingItemPreview(string ProductName, int Quantity);

public record BookingSummaryDto(
    string BookingId,
    string? BookingReference,
    string? GuestName,
    string? BookingDate,
    string? Status,
    decimal TotalAmount,
    int LineItemCount,
    IReadOnlyList<BookingItemPreview> Items,
    int? CustomerId,
    bool IsImported
);

public record TabSummaryDto(
    Guid TabId,
    string? BookingId,
    string? GuestName,
    int ItemCount,
    decimal GrandTotal,
    decimal AmountRemaining,
    string? PreAuthCardType,
    string? PreAuthCardLast4,
    string PaymentStatus,
    DateTime OpenedAt
)
{
    public static TabSummaryDto FromTab(Tab tab)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var added = JsonSerializer.Deserialize<List<TabLineItem>>(tab.AddedItemsJson, opts) ?? [];
        var imported = JsonSerializer.Deserialize<List<TabLineItem>>(tab.ImportedItemsJson, opts) ?? [];
        var paid = tab.Payments.Where(p => p.Type != "pre_auth" && p.Status == "success").Sum(p => p.Amount);
        var preAuth = tab.Payments.FirstOrDefault(p => p.Type == "pre_auth");
        var last4 = preAuth?.CardNumber?.Split('-').LastOrDefault();
        return new TabSummaryDto(tab.TabId, tab.BookingId, tab.GuestName, added.Count + imported.Count, tab.GrandTotal, tab.GrandTotal - paid, preAuth?.Method, last4, tab.PaymentStatus, tab.OpenedAt);
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
    public List<TabLineItem> ImportedItems { get; init; } = [];
    public List<TabLineItem> AddedItems { get; init; } = [];
    public decimal GrandTotal { get; init; }
    public decimal AmountRemaining { get; init; }
    public string? PreAuthCardType { get; init; }
    public string PaymentStatus { get; init; } = "open";
    public string PreAuthStatus { get; init; } = "none";
    public string? PreAuthCardNumber { get; init; }
    public bool HasPendingConflict { get; init; }
    public DateTime OpenedAt { get; init; }
    public DateTime? SettledAt { get; init; }

    public static TabDto FromTab(Tab tab)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var addedItems = JsonSerializer.Deserialize<List<TabLineItem>>(tab.AddedItemsJson, opts) ?? [];
        var importedItems = JsonSerializer.Deserialize<List<TabLineItem>>(tab.ImportedItemsJson, opts) ?? [];

        var paid = tab.Payments.Where(p => p.Type != "pre_auth" && p.Status == "success").Sum(p => p.Amount);
        var preAuth = tab.Payments.FirstOrDefault(p => p.Type == "pre_auth");
        return new TabDto
        {
            TabId = tab.TabId,
            BookingId = tab.BookingId,
            GuestName = tab.GuestName,
            GuestEmail = tab.GuestEmail,
            GuestPhone = tab.GuestPhone,
            ImportedItems = importedItems,
            AddedItems = addedItems,
            GrandTotal = tab.GrandTotal,
            AmountRemaining = tab.GrandTotal - paid,
            PreAuthCardType = preAuth?.Method,
            PaymentStatus = tab.PaymentStatus,
            PreAuthStatus = tab.PreAuthStatus,
            PreAuthCardNumber = tab.PreAuthCardNumber,
            HasPendingConflict = tab.HasPendingConflict,
            OpenedAt = tab.OpenedAt,
            SettledAt = tab.SettledAt
        };
    }
}
