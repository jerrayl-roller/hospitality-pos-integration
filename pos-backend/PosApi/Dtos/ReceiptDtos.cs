namespace PosApi.Dtos;

public record ReceiptLineItem(
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    decimal GstAmount
);

public record ReceiptPayment(
    string Method,
    string? Reference,
    decimal Amount,
    bool IsTip
);

public class ReceiptData
{
    public Guid TabId { get; init; }
    public string ReceiptNumber { get; init; } = "";
    public string VenueName { get; init; } = "ROLLER Venue";
    public string AbnPlaceholder { get; init; } = "XX XXX XXX XXX";
    public DateTime IssuedAt { get; init; }
    public string? GuestName { get; init; }
    public List<ReceiptLineItem> LineItems { get; init; } = [];
    public decimal SubtotalExclGst { get; init; }
    public decimal GstTotal { get; init; }
    public decimal GrandTotal { get; init; }
    public decimal TipTotal { get; init; }
    public List<ReceiptPayment> Payments { get; init; } = [];
}
