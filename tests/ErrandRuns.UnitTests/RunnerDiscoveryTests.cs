using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Communications;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Runners;

namespace ErrandRuns.UnitTests;

public sealed class RunnerDiscoveryTests
{
    [Fact]
    public void Only_verified_runners_can_publish_valid_locations()
    {
        var applicant = new RunnerProfile(Guid.NewGuid());
        Assert.Throws<DomainException>(() => applicant.UpdateLocation(6.45m, 3.47m, DateTimeOffset.UtcNow));

        applicant.SubmitVerification();
        applicant.Approve();
        applicant.UpdateLocation(6.45m, 3.47m, DateTimeOffset.UtcNow);

        Assert.Equal(6.45m, applicant.CurrentLatitude);
        Assert.Throws<DomainException>(() => applicant.UpdateLocation(91m, 3.47m, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Customer_discovery_filters_exact_radius_and_orders_by_distance()
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = new[]
        {
            new NearbyRunnerCandidate(Guid.NewGuid(), "Near Runner", 6.451m, 3.471m,
                4.2m, 10, now, true),
            new NearbyRunnerCandidate(Guid.NewGuid(), "Far Runner", 6.70m, 3.70m,
                5m, 50, now, false)
        };
        var repository = new FakeRunners(candidates);
        var service = new CustomerRunnerDiscoveryService(repository,
            new FakeCurrent(Guid.NewGuid(), "Customer"), new FixedClock(now));

        var result = await service.Nearby(6.45m, 3.47m, 10m, 1, 20,
            TestContext.Current.CancellationToken);

        var runner = Assert.Single(result.Items);
        Assert.Equal("Near Runner", runner.DisplayName);
        Assert.NotNull(runner.ProfilePictureUrl);
        Assert.True(runner.DistanceKm < 1m);
    }

    [Fact]
    public async Task Declining_an_offer_hides_it_only_for_that_runner()
    {
        var runner = new RunnerProfile(Guid.NewGuid());
        runner.SubmitVerification();
        runner.Approve();
        runner.SetAvailable(true);
        var errand = new Errand(Guid.NewGuid(), Guid.NewGuid(), "Nearby delivery", ErrandCategory.Custom);
        errand.AddStop(new ErrandStop(Guid.NewGuid(), 1, StopType.Pickup,
            "Lekki", new GeoPoint(6.45m, 3.47m), null));
        errand.RequestEstimate();
        errand.SetEstimate(new Money(2500), new Money(0));
        errand.ConfirmPayment();
        var runners = new FakeRunners([], runner);
        var service = new RunnerOfferService(new FakeErrands(errand), runners,
            new RunnerCompensationPolicy(80), new NoNotifications(),
            new FakeCurrent(runner.UserId, "Runner"), new FixedClock(DateTimeOffset.UtcNow));

        await service.Decline(errand.Id, TestContext.Current.CancellationToken);

        Assert.Contains(errand.Id, await runners.DeclinedOfferIds(runner.UserId,
            TestContext.Current.CancellationToken));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeCurrent(Guid id, string role) : ICurrentUser
    {
        public Guid UserId => id;
        public bool IsInRole(string value) => value == role;
    }

    private sealed class FakeRunners(
        IReadOnlyList<NearbyRunnerCandidate> candidates,
        RunnerProfile? profile = null) : IRunnerRepository
    {
        private readonly HashSet<Guid> declined = [];
        public Task<RunnerProfile?> Find(Guid id, CancellationToken ct) =>
            Task.FromResult(profile?.UserId == id ? profile : null);
        public Task<IReadOnlyList<RunnerProfile>> Available(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RunnerProfile>>(profile is null ? [] : [profile]);
        public Task<IReadOnlyList<NearbyRunnerCandidate>> SearchNearby(decimal minLatitude,
            decimal maxLatitude, decimal minLongitude, decimal maxLongitude,
            DateTimeOffset locationFreshAfter, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<NearbyRunnerCandidate>>(candidates.Where(value =>
                value.Latitude >= minLatitude && value.Latitude <= maxLatitude &&
                value.Longitude >= minLongitude && value.Longitude <= maxLongitude &&
                value.LocationUpdatedAt >= locationFreshAfter).Take(take).ToList());
        public Task<IReadOnlySet<Guid>> DeclinedOfferIds(Guid runnerId, CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<Guid>>(declined);
        public Task<bool> HasDeclinedOffer(Guid runnerId, Guid errandId, CancellationToken ct) =>
            Task.FromResult(declined.Contains(errandId));
        public void AddDeclinedOffer(RunnerDeclinedOffer decline) => declined.Add(decline.ErrandId);
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeErrands(Errand errand) : IErrandRepository
    {
        public Task Add(Errand value, CancellationToken ct) => Task.CompletedTask;
        public Task<Errand?> Find(Guid id, CancellationToken ct) =>
            Task.FromResult<Errand?>(id == errand.Id ? errand : null);
        public Task<IReadOnlyList<Errand>> ListForUser(Guid userId, bool runner, bool? active,
            int skip, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<Errand>>([]);
        public Task<int> CountForUser(Guid userId, bool runner, bool? active, CancellationToken ct) => Task.FromResult(0);
        public Task<IReadOnlyList<Errand>> SearchForCustomer(Guid customerId, string query,
            int skip, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<Errand>>([]);
        public Task<int> CountSearchForCustomer(Guid customerId, string query, CancellationToken ct) => Task.FromResult(0);
        public void Remove(Errand value) { }
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoNotifications : INotificationPublisher
    {
        public Task Publish(Guid recipientId, NotificationType type, string title,
            string body, Guid? errandId, CancellationToken ct) => Task.CompletedTask;
    }
}
