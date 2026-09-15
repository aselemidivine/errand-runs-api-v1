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
    private Payment()
    {
        ProviderReference = string.Empty;
        IdempotencyKey = string.Empty;
        Provider = string.Empty;
        PaymentMethod = string.Empty;
    }

    public Payment(
        Guid id,
        Guid errandId,
        Guid customerId,
        Money amount,
        string idempotencyKey,
        DateTimeOffset createdAt)
    {
        if (amount.Amount <= 0) throw new DomainException("Payment amount must be greater than zero.");
        var normalizedKey = idempotencyKey?.Trim() ?? string.Empty;
        if (normalizedKey.Length is < 8 or > 120)
            throw new DomainException("Idempotency-Key must contain 8 to 120 characters.");

        Id = id;
        ErrandId = errandId;
        CustomerId = customerId;
        Amount = amount;
        IdempotencyKey = normalizedKey;
        ProviderReference = string.Empty;
        Provider = string.Empty;
        PaymentMethod = string.Empty;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }
    public Guid Id { get; private set; }
    public Guid ErrandId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Money Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string ProviderReference { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string Provider { get; private set; }
    public string PaymentMethod { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public string? AccessCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void SetProviderIntent(
        string provider,
        string paymentMethod,
        string reference,
        string? checkoutUrl,
        string? accessCode,
        DateTimeOffset now)
    {
        if (Status != PaymentStatus.Pending) throw new DomainException("Payment can no longer be initialized.");
        if (!string.IsNullOrEmpty(ProviderReference) && ProviderReference != reference)
            throw new DomainException("Payment already has a different provider transaction.");
        Provider = Required(provider, "Payment provider", 40);
        PaymentMethod = Required(paymentMethod, "Payment method", 40);
        ProviderReference = Required(reference, "Payment reference", 160);
        CheckoutUrl = Optional(checkoutUrl, 2048);
        AccessCode = Optional(accessCode, 160);
        UpdatedAt = now;
    }

    public void Confirm(string reference, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Confirmed) return;
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Authorized))
            throw new DomainException("Payment cannot be confirmed.");
        if (string.IsNullOrWhiteSpace(ProviderReference) || ProviderReference != reference)
            throw new DomainException("Payment reference does not match.");
        Status = PaymentStatus.Confirmed;
        CheckoutUrl = null;
        AccessCode = null;
        UpdatedAt = now;
    }

    public void MarkFailed(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Confirmed) throw new DomainException("A confirmed payment cannot fail.");
        Status = PaymentStatus.Failed;
        CheckoutUrl = null;
        AccessCode = null;
        UpdatedAt = now;
    }

    private static string Required(string? value, string name, int maximum)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw new DomainException($"{name} is required.");
        if (normalized.Length > maximum) throw new DomainException($"{name} is too long.");
        return normalized;
    }

    private static string? Optional(string? value, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximum) throw new DomainException("Payment provider value is too long.");
        return normalized;
    }
}
