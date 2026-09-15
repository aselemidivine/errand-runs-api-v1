using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Payments;

namespace ErrandRuns.Application;

public sealed class CustomerPaymentService(
    IPaymentRepository payments,
    IErrandRepository errands,
    IPaymentGateway gateway,
    IAuthenticationService accounts,
    INotificationPublisher notifications,
    ICurrentUser current,
    IClock clock)
{
    public async Task<ErrandPaymentDetails> Initialize(
        Guid errandId,
        InitializeErrandPayment request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        EnsureCustomer();
        var errand = await OwnedErrand(errandId, ct);
        if (errand.Status != ErrandStatus.PendingPayment)
            throw new DomainException("Only an errand awaiting payment can be paid.");

        var method = NormalizeMethod(request.PaymentMethod);
        var key = NormalizeIdempotencyKey(idempotencyKey);
        var payment = await payments.FindByIdempotencyKey(key, ct);
        if (payment is not null &&
            (payment.CustomerId != current.UserId || payment.ErrandId != errandId))
            throw new DomainException("Idempotency-Key has already been used for another payment.");

        payment ??= await payments.FindCurrentForErrand(errandId, ct);
        if (payment is null)
        {
            payment = new Payment(
                Guid.NewGuid(),
                errand.Id,
                current.UserId,
                new Money(errand.TotalEstimate, errand.Currency),
                key,
                clock.UtcNow);
            await payments.Add(payment, ct);
            await payments.Save(ct);
        }

        if (payment.Status != PaymentStatus.Pending ||
            !string.IsNullOrWhiteSpace(payment.ProviderReference))
            return Map(payment);

        var account = await accounts.GetAccount(current.UserId, ct)
            ?? throw new KeyNotFoundException("Customer account was not found.");
        var intent = await gateway.CreateIntent(
            payment.Id,
            errand.Id,
            payment.Amount,
            account.Email,
            method,
            ct);
        payment.SetProviderIntent(
            intent.Provider,
            method,
            intent.Reference,
            intent.CheckoutUrl,
            intent.AccessCode,
            clock.UtcNow);
        await payments.Save(ct);
        return Map(payment, intent.DevelopmentMode);
    }

    public async Task<ErrandPaymentDetails> Get(
        Guid errandId,
        Guid paymentId,
        CancellationToken ct)
    {
        EnsureCustomer();
        await OwnedErrand(errandId, ct);
        var payment = await OwnedPayment(errandId, paymentId, ct);
        return Map(payment);
    }

    public async Task<ErrandPaymentDetails> GetCurrent(Guid errandId, CancellationToken ct)
    {
        EnsureCustomer();
        await OwnedErrand(errandId, ct);
        var payment = await payments.FindCurrentForErrand(errandId, ct)
            ?? throw new KeyNotFoundException("Payment not found.");
        if (payment.CustomerId != current.UserId) throw new UnauthorizedAccessException();
        return Map(payment);
    }

    public async Task<ErrandPaymentDetails> Verify(
        Guid errandId,
        Guid paymentId,
        CancellationToken ct)
    {
        EnsureCustomer();
        var errand = await OwnedErrand(errandId, ct);
        var payment = await OwnedPayment(errandId, paymentId, ct);
        await VerifyAndApply(payment, errand, ct);
        return Map(payment);
    }

    public async Task ReconcileSuccessfulCharge(string providerReference, CancellationToken ct)
    {
        var payment = await payments.FindByReference(providerReference, ct);
        if (payment is null || payment.Status == PaymentStatus.Confirmed) return;
        var errand = await errands.Find(payment.ErrandId, ct);
        if (errand is null) return;
        await VerifyAndApply(payment, errand, ct);
    }

    private async Task VerifyAndApply(Payment payment, Errand errand, CancellationToken ct)
    {
        if (payment.Status == PaymentStatus.Confirmed) return;
        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
            throw new DomainException("Payment has not been initialized with the provider.");

        var result = await gateway.Verify(payment.ProviderReference, payment.Amount, ct);
        if (!string.Equals(result.Reference, payment.ProviderReference, StringComparison.Ordinal) ||
            result.Amount != payment.Amount.Amount ||
            !string.Equals(result.Currency, payment.Amount.Currency, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("The provider payment does not match this errand's amount or currency.");

        if (result.Succeeded)
        {
            payment.Confirm(result.Reference, clock.UtcNow);
            if (errand.Status == ErrandStatus.PendingPayment) errand.ConfirmPayment();
            else if (errand.Status != ErrandStatus.PaymentConfirmed)
                throw new DomainException("The errand can no longer accept this payment.");
            await payments.Save(ct);
            await notifications.Publish(
                payment.CustomerId,
                NotificationType.Payment,
                "Payment confirmed",
                "Your errand payment was confirmed and runner matching can begin.",
                payment.ErrandId,
                ct);
        }
        else if (result.IsFinalFailure)
        {
            payment.MarkFailed(clock.UtcNow);
            await payments.Save(ct);
        }
    }

    private async Task<Errand> OwnedErrand(Guid id, CancellationToken ct)
    {
        var errand = await errands.Find(id, ct)
            ?? throw new KeyNotFoundException("Errand not found.");
        if (errand.CustomerId != current.UserId) throw new UnauthorizedAccessException();
        return errand;
    }

    private async Task<Payment> OwnedPayment(Guid errandId, Guid paymentId, CancellationToken ct)
    {
        var payment = await payments.Find(paymentId, ct)
            ?? throw new KeyNotFoundException("Payment not found.");
        if (payment.CustomerId != current.UserId || payment.ErrandId != errandId)
            throw new UnauthorizedAccessException();
        return payment;
    }

    private void EnsureCustomer()
    {
        if (!current.IsInRole("Customer")) throw new UnauthorizedAccessException();
    }

    private static string NormalizeMethod(string? value) =>
        value?.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
        {
            "card" or "debit_card" or "credit_card" => "card",
            "bank" or "bank_transfer" => "bank_transfer",
            "ussd" => "ussd",
            _ => throw new DomainException("Payment method must be card, bank_transfer, or ussd.")
        };

    private static string NormalizeIdempotencyKey(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        if (key.Length is < 8 or > 120)
            throw new DomainException("Idempotency-Key header must contain 8 to 120 characters.");
        return key;
    }

    private static ErrandPaymentDetails Map(Payment payment, bool? developmentMode = null)
    {
        var reference = string.IsNullOrWhiteSpace(payment.ProviderReference)
            ? null
            : payment.ProviderReference;
        return new(
            payment.Id,
            payment.ErrandId,
            payment.Status,
            payment.Provider,
            payment.PaymentMethod,
            reference,
            payment.CheckoutUrl,
            payment.CheckoutUrl,
            payment.AccessCode,
            new MoneyDetails(payment.Amount.Amount, payment.Amount.Currency),
            developmentMode ?? payment.Provider.Equals("Development", StringComparison.OrdinalIgnoreCase),
            payment.CreatedAt,
            payment.UpdatedAt);
    }
}
