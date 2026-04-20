namespace PosApi.Models;

public class Tab
{
    public Guid TabId { get; set; }
    public string? BookingId { get; set; }
    public string? GuestName { get; set; }
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }
    public string ImportedItemsJson { get; set; } = "[]";
    public string AddedItemsJson { get; set; } = "[]";
    public string AuditLogJson { get; set; } = "[]";
    public decimal GrandTotal { get; set; }
    public string? PreAuthCardNumber { get; set; }
    public string PreAuthStatus { get; set; } = "none";
    public string PaymentStatus { get; set; } = "open";
    public string? RollerLockId { get; set; }
    public bool StuckLock { get; set; }
    public bool HasPendingConflict { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
