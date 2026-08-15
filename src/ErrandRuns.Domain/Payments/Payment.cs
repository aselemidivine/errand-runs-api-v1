using ErrandRuns.Domain.Common;
namespace ErrandRuns.Domain.Payments;

public enum PaymentStatus
{
    Pending,
    Authorized,
    Confirmed,
    Failed,
    Refunded
}
public sealed class Payment
{
    private Payment() { ProviderReference = string.Empty; IdempotencyKey = string.Empty; }
    public Payment(Guid id, Guid errandId, Guid customerId, Money amount, string idempotencyKey) { Id = id; ErrandId = errandId; CustomerId = customerId; Amount = amount; IdempotencyKey = idempotencyKey; ProviderReference = string.Empty; }
    public Guid Id { get; private set; }
    public Guid ErrandId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Money Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string ProviderReference { get; private set; }
    public string IdempotencyKey { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void Confirm(string reference) { if (Status == PaymentStatus.Confirmed) return; if (Status != PaymentStatus.Pending) throw new DomainException("Payment cannot be confirmed."); ProviderReference = reference; Status = PaymentStatus.Confirmed; }
}
