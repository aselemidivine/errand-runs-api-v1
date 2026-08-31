using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Users;

namespace ErrandRuns.UnitTests;

public sealed class UserPreferenceTests
{
    [Fact]
    public void Saved_location_validates_coordinates_and_keeps_distinct_categories()
    {
        var now = DateTimeOffset.UtcNow;
        var location = new SavedLocation(Guid.NewGuid(), Guid.NewGuid(), "Home",
            "Admiralty Way, Lekki Phase 1, Lagos", 6.4474m, 3.4723m,
            "Opposite the yellow filling station", "Call at the main gate",
            true, true, [ErrandCategory.Grocery, ErrandCategory.Grocery, ErrandCategory.Laundry], now);

        Assert.Equal(2, location.PreferredCategories.Count);
        Assert.True(location.IsDefault);
        Assert.Throws<DomainException>(() => new SavedLocation(Guid.NewGuid(), Guid.NewGuid(),
            "Invalid", "Lagos", 91m, 3m, null, null, false, false, [], now));
    }

    [Fact]
    public void Saved_location_normalizes_google_metadata_and_rejects_invalid_json()
    {
        var now = DateTimeOffset.UtcNow;
        var location = new SavedLocation(Guid.NewGuid(), Guid.NewGuid(), " Home ",
            " 1 Admiralty Way, Lagos ", 6.4474m, 3.4723m, null, null,
            false, false, [], now, " google-place-1 ", " { \"city\": \"Lagos\" } ");

        Assert.Equal("Home", location.Label);
        Assert.Equal("1 Admiralty Way, Lagos", location.Address);
        Assert.Equal("google-place-1", location.GooglePlaceId);
        Assert.Equal("{\"city\":\"Lagos\"}", location.AddressComponentsJson);
        Assert.Throws<DomainException>(() => new SavedLocation(Guid.NewGuid(), Guid.NewGuid(),
            "Home", "Lagos", 6m, 3m, null, null, false, false, [], now,
            "google-place-1", "not-json"));
    }

    [Fact]
    public async Task First_saved_location_becomes_default_and_a_new_default_replaces_it()
    {
        var userId = Guid.NewGuid();
        var repository = new InMemoryPreferences();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var service = new UserPreferenceService(repository, new TestUser(userId), clock);
        var ct = TestContext.Current.CancellationToken;

        var first = await service.Create(new SaveLocation("Home", "Lekki, Lagos", 6.45m, 3.47m,
            null, null, true, false, [ErrandCategory.Grocery]), ct);
        var second = await service.Create(new SaveLocation("Work", "Ikoyi, Lagos", 6.46m, 3.43m,
            null, "Ask for reception", false, true, [ErrandCategory.DocumentCollection]), ct);

        Assert.False((await service.Get(first.Id, ct)).IsDefault);
        Assert.True(second.IsDefault);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class TestUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
        public bool IsInRole(string role) => role == "Customer";
    }

    private sealed class InMemoryPreferences : IUserPreferenceRepository
    {
        private readonly List<SavedLocation> _locations = [];
        public Task<int> CountLocations(Guid userId, CancellationToken ct) => Task.FromResult(_locations.Count(x => x.UserId == userId));
        public Task<IReadOnlyList<SavedLocation>> ListLocations(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<SavedLocation>>(_locations.Where(x => x.UserId == userId).ToArray());
        public Task<SavedLocation?> FindLocation(Guid id, CancellationToken ct) => Task.FromResult(_locations.SingleOrDefault(x => x.Id == id));
        public Task<SavedLocation?> FindDefaultLocation(Guid userId, CancellationToken ct) => Task.FromResult(_locations.SingleOrDefault(x => x.UserId == userId && x.IsDefault));
        public void AddLocation(SavedLocation location) => _locations.Add(location);
        public void RemoveLocation(SavedLocation location) => _locations.Remove(location);
        public Task Save(CancellationToken ct) => Task.CompletedTask;
    }
}
