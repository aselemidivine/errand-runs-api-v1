namespace ErrandRuns.Domain.Common;

public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }
    public Money(decimal amount, string currency = "NGN")
    {
        if (amount < 0) throw new DomainException("Money cannot be negative.");
        Amount = decimal.Round(amount, 2, MidpointRounding.ToEven);
        Currency = string.IsNullOrWhiteSpace(currency) ? throw new DomainException("Currency is required.") : currency.ToUpperInvariant();
    }
    public static Money operator +(Money a, Money b) => a.Currency == b.Currency ? new(a.Amount + b.Amount, a.Currency) : throw new DomainException("Currency mismatch.");
}
