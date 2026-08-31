using ErrandRuns.Application;
using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Domain.Runners;

namespace ErrandRuns.UnitTests;

public sealed class CustomerErrandListTests
{
    [Fact]
    public async Task List_normalizes_page_number_and_page_size()
    {
        var repository = new QueryRepository(NewErrand());
        var service = Create(repository);

        var result = await service.List(true, 0, 0, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(0, repository.LastSkip);
        Assert.Equal(1, repository.LastTake);
    }

    [Fact]
    public async Task Search_requires_two_characters_and_returns_paginated_matches()
    {
        var repository = new QueryRepository(NewErrand());
        var service = Create(repository);
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() => service.Search("x", 1, 20, ct));
        var result = await service.Search("grocery", 1, 20, ct);

        Assert.Single(result.Items);
        Assert.Equal("grocery", repository.LastSearch);
    }

    private static Errand NewErrand()
    {
        var errand = new Errand(Guid.NewGuid(), TestCurrent.User, "Weekly grocery run", ErrandCategory.Grocery);
        errand.AddStop(new ErrandStop(Guid.NewGuid(), 1, StopType.Shopping, "Lekki market", new(6.45m, 3.47m), null));
        errand.AddStop(new ErrandStop(Guid.NewGuid(), 2, StopType.Delivery, "Home", new(6.46m, 3.48m), null));
        return errand;
    }

    private static ErrandService Create(IErrandRepository repository) => new(repository,
        new EmptyRunners(), new EmptyFinance(), new EmptyMatching(), new PricingService(),
        new RunnerCompensationPolicy(80), new EmptyNotifications(), new TestCurrent(), new TestClock());

    private sealed class QueryRepository(Errand errand) : IErrandRepository
    {
        public int LastSkip { get; private set; }
        public int LastTake { get; private set; }
        public string? LastSearch { get; private set; }
        public Task Add(Errand value, CancellationToken ct) => Task.CompletedTask;
        public Task<Errand?> Find(Guid id, CancellationToken ct) => Task.FromResult<Errand?>(errand);
        public Task<IReadOnlyList<Errand>> ListForUser(Guid userId, bool runner, bool? active, int skip, int take, CancellationToken ct) { LastSkip = skip; LastTake = take; return Task.FromResult<IReadOnlyList<Errand>>([errand]); }
        public Task<int> CountForUser(Guid userId, bool runner, bool? active, CancellationToken ct) => Task.FromResult(1);
        public Task<IReadOnlyList<Errand>> SearchForCustomer(Guid customerId, string query, int skip, int take, CancellationToken ct) { LastSearch = query; return Task.FromResult<IReadOnlyList<Errand>>([errand]); }
        public Task<int> CountSearchForCustomer(Guid customerId, string query, CancellationToken ct) => Task.FromResult(1);
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyRunners : IRunnerRepository
    {
        public Task<RunnerProfile?> Find(Guid id, CancellationToken ct) => Task.FromResult<RunnerProfile?>(null);
        public Task<IReadOnlyList<RunnerProfile>> Available(CancellationToken ct) => Task.FromResult<IReadOnlyList<RunnerProfile>>([]);
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyFinance : IRunnerFinanceRepository
    {
        public Task<RunnerPayoutAccount?> GetPayoutAccount(Guid id, CancellationToken ct) => Task.FromResult<RunnerPayoutAccount?>(null);
        public void AddPayoutAccount(RunnerPayoutAccount account) { }
        public Task<bool> HasEarning(Guid id, CancellationToken ct) => Task.FromResult(false);
        public void AddLedgerEntry(RunnerLedgerEntry entry) { }
        public Task<decimal> Balance(Guid id, string currency, CancellationToken ct) => Task.FromResult(0m);
        public Task<IReadOnlyList<RunnerLedgerEntry>> ListLedger(Guid id, int skip, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<RunnerLedgerEntry>>([]);
        public Task<int> CountLedger(Guid id, CancellationToken ct) => Task.FromResult(0);
        public Task<RunnerPayout?> FindPayout(Guid id, string key, CancellationToken ct) => Task.FromResult<RunnerPayout?>(null);
        public Task<RunnerPayout?> FindPayoutByReference(string reference, CancellationToken ct) => Task.FromResult<RunnerPayout?>(null);
        public Task<bool> HasLedgerEntry(Guid id, RunnerLedgerEntryType type, CancellationToken ct) => Task.FromResult(false);
        public void AddPayout(RunnerPayout payout) { }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class EmptyMatching : IRunnerMatchingService
    {
        public Task<Guid?> FindRunner(Errand errand, CancellationToken ct) => Task.FromResult<Guid?>(null);
    }

    private sealed class EmptyNotifications : INotificationPublisher
    {
        public Task Publish(Guid recipientId, NotificationType type, string title, string body, Guid? errandId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestCurrent : ICurrentUser
    {
        public static readonly Guid User = Guid.NewGuid();
        public Guid UserId => User;
        public bool IsInRole(string role) => role == "Customer";
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
