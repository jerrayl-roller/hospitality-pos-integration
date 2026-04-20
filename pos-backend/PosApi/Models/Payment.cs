namespace PosApi.Models;

public class Payment
{
    public Guid PaymentId { get; set; }
    public Guid TabId { get; set; }
    public Tab Tab { get; set; } = null!;
    public string Type { get; set; } = "";
    public string Method { get; set; } = "";
    public string? CardNumber { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "AUD";
    public string Status { get; set; } = "";
    public bool IsTip { get; set; } = false;
    public string RollerPushStatus { get; set; } = "not_pushed";
    public string? RollerGiftCardTransactionId { get; set; }
    public DateTime CreatedAt { get; set; }
}
