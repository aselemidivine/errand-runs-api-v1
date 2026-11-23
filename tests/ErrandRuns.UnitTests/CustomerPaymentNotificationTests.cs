using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Domain.Runners;

namespace ErrandRuns.UnitTests;

public sealed class CustomerPaymentNotificationTests
{
    [Fact]
    public async Task Verified_charge_confirms_errand_and_notifies_customer_only_once()
    {
        var customerId = Guid.NewGuid();
        var errand = new Errand(Guid.NewGuid(), customerId, "Groceries", ErrandCategory.Grocery);
        errand.AddStop(new ErrandStop(Guid.NewGuid(), 1, StopType.Shopping,
            "Lekki, Lagos", new GeoPoint(6.45m, 3.47m), null));
        errand.RequestEstimate();
        errand.SetEstimate(new Money(2500), new Money(5000));
        var payment = new Payment(Guid.NewGuid(), errand.Id, customerId,
            new Money(errand.TotalEstimate), "idempotent-payment-123", DateTimeOffset.UtcNow);
        payment.SetProviderIntent("Paystack", "card", "payment-ref-123",
            "https://checkout.paystack.com/example", "access-code", DateTimeOffset.UtcNow);
        var notifications = new RecordingNotifications();
        var service = new CustomerPaymentService(
            new PaymentRepository(payment), new ErrandRepository(errand),
            new EmptyRunners(), new SuccessfulGateway(), null!, notifications, null!, new TestClock());

        await service.ReconcileSuccessfulCharge(payment.ProviderReference, CancellationToken.None);
        await service.ReconcileSuccessfulCharge(payment.ProviderReference, CancellationToken.None);

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(ErrandStatus.PaymentConfirmed, errand.Status);
        Assert.Single(notifications.Messages);
        Assert.Equal(customerId, notifications.Messages[0].RecipientId);
        Assert.Equal(NotificationType.Payment, notifications.Messages[0].Type);
        Assert.Equal("Payment confirmed", notifications.Messages[0].Title);
    }

    private sealed class PaymentRepository(Payment payment) : IPaymentRepository
    {
        public Task<Payment?> Find(Guid id, CancellationToken ct) =>
            Task.FromResult<Payment?>(id == payment.Id ? payment : null);
        public Task<Payment?> FindByReference(string reference, CancellationToken ct) =>
            Task.FromResult<Payment?>(reference == payment.ProviderReference ? payment : null);
        public Task<Payment?> FindByIdempotencyKey(string key, CancellationToken ct) =>
            Task.FromResult<Payment?>(key == payment.IdempotencyKey ? payment : null);
        public Task<Payment?> FindCurrentForErrand(Guid id, CancellationToken ct) =>
            Task.FromResult<Payment?>(id == payment.ErrandId ? payment : null);
        public Task Add(Payment value, CancellationToken ct) => Task.CompletedTask;
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ErrandRepository(Errand errand) : IErrandRepository
    {
        public Task Add(Errand value, CancellationToken ct) => Task.CompletedTask;
        public Task<Errand?> Find(Guid id, CancellationToken ct) =>
            Task.FromResult<Errand?>(id == errand.Id ? errand : null);
        public Task<IReadOnlyList<Errand>> ListForUser(Guid userId, bool runner,
            bool? active, int skip, int take, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<int> CountForUser(Guid userId, bool runner, bool? active,
            CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<Errand>> SearchForCustomer(Guid customerId,
            string query, int skip, int take, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<int> CountSearchForCustomer(Guid customerId, string query,
            CancellationToken ct) => throw new NotImplementedException();
        public void Remove(Errand value) => throw new NotImplementedException();
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SuccessfulGateway : IPaymentGateway
    {
        public Task<PaymentIntent> CreateIntent(Guid paymentId, Guid errandId,
            Money amount, string customerEmail, string paymentMethod,
            CancellationToken ct) => throw new NotImplementedException();
        public Task<PaymentVerification> Verify(string reference, Money expectedAmount,
            CancellationToken ct) => Task.FromResult(new PaymentVerification(
                reference, "success", expectedAmount.Amount, expectedAmount.Currency));
    }

    private sealed class EmptyRunners : IRunnerRepository
    {
        public Task<RunnerProfile?> Find(Guid id, CancellationToken ct) =>
            Task.FromResult<RunnerProfile?>(null);
        public Task<IReadOnlyList<RunnerProfile>> Available(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RunnerProfile>>([]);
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingNotifications : INotificationPublisher
    {
        public List<(Guid RecipientId, NotificationType Type, string Title)> Messages { get; } = [];
        public Task Publish(Guid recipientId, NotificationType type, string title,
            string body, Guid? errandId, CancellationToken ct)
        {
            Messages.Add((recipientId, type, title));
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
