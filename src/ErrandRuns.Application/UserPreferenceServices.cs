using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;
using ErrandRuns.Domain.Users;

namespace ErrandRuns.Application;

public interface IUserPreferenceRepository
{
    Task<int> CountLocations(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<SavedLocation>> ListLocations(Guid userId, CancellationToken ct);
    Task<SavedLocation?> FindLocation(Guid id, CancellationToken ct);
    Task<SavedLocation?> FindDefaultLocation(Guid userId, CancellationToken ct);
    void AddLocation(SavedLocation location);
    void RemoveLocation(SavedLocation location);
    Task Save(CancellationToken ct);
}

public sealed record SaveLocation(
    string Label,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string? Landmark,
    string? DeliveryInstructions,
    bool IsFavorite,
    bool IsDefault,
    IReadOnlyList<ErrandCategory>? PreferredCategories,
    string? GooglePlaceId = null,
    string? AddressComponentsJson = null);

public sealed record SavedLocationDetails(
    Guid Id,
    string Label,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string? Landmark,
    string? DeliveryInstructions,
    bool IsFavorite,
    bool IsDefault,
    IReadOnlyList<ErrandCategory> PreferredCategories,
    string? GooglePlaceId,
    string? AddressComponentsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class UserPreferenceService(
    IUserPreferenceRepository preferences,
    ICurrentUser current,
    IClock clock)
{
    public async Task<IReadOnlyList<SavedLocationDetails>> List(CancellationToken ct) =>
        (await preferences.ListLocations(current.UserId, ct)).Select(ToDetails).ToArray();

    public async Task<IReadOnlyList<SavedLocationDetails>> Search(string query, CancellationToken ct)
    {
        query = query.Trim();
        return (await preferences.ListLocations(current.UserId, ct))
            .Where(x => Contains(x.Label, query) || Contains(x.Address, query) || Contains(x.Landmark, query))
            .Take(10)
            .Select(ToDetails)
            .ToArray();
    }

    public async Task<SavedLocationDetails> Get(Guid id, CancellationToken ct) =>
        ToDetails(await Owned(id, ct));

    public async Task<SavedLocationDetails> Create(SaveLocation request, CancellationToken ct)
    {
        if (await preferences.CountLocations(current.UserId, ct) >= 20)
            throw new DomainException("A user cannot save more than 20 locations.");

        var makeDefault = request.IsDefault || await preferences.CountLocations(current.UserId, ct) == 0;
        if (makeDefault) await ClearCurrentDefault(null, ct);

        var location = new SavedLocation(Guid.NewGuid(), current.UserId, request.Label, request.Address,
            request.Latitude, request.Longitude, request.Landmark, request.DeliveryInstructions,
            request.IsFavorite, makeDefault, request.PreferredCategories ?? [], clock.UtcNow,
            request.GooglePlaceId, request.AddressComponentsJson);
        preferences.AddLocation(location);
        await preferences.Save(ct);
        return ToDetails(location);
    }

    public async Task<SavedLocationDetails> Update(Guid id, SaveLocation request, CancellationToken ct)
    {
        var location = await Owned(id, ct);
        if (request.IsDefault) await ClearCurrentDefault(id, ct);
        location.Update(request.Label, request.Address, request.Latitude, request.Longitude,
            request.Landmark, request.DeliveryInstructions, request.IsFavorite,
            request.IsDefault || location.IsDefault, request.PreferredCategories ?? [], clock.UtcNow,
            request.GooglePlaceId, request.AddressComponentsJson);
        await preferences.Save(ct);
        return ToDetails(location);
    }

    public async Task<SavedLocationDetails> SetDefault(Guid id, CancellationToken ct)
    {
        var location = await Owned(id, ct);
        await ClearCurrentDefault(id, ct);
        location.SetDefault(true, clock.UtcNow);
        await preferences.Save(ct);
        return ToDetails(location);
    }

    public async Task Delete(Guid id, CancellationToken ct)
    {
        var location = await Owned(id, ct);
        preferences.RemoveLocation(location);
        await preferences.Save(ct);

        if (!location.IsDefault) return;
        var replacement = (await preferences.ListLocations(current.UserId, ct)).FirstOrDefault();
        if (replacement is null) return;
        replacement.SetDefault(true, clock.UtcNow);
        await preferences.Save(ct);
    }

    private async Task ClearCurrentDefault(Guid? exceptId, CancellationToken ct)
    {
        var existing = await preferences.FindDefaultLocation(current.UserId, ct);
        if (existing is not null && existing.Id != exceptId)
            existing.SetDefault(false, clock.UtcNow);
    }

    private async Task<SavedLocation> Owned(Guid id, CancellationToken ct)
    {
        var location = await preferences.FindLocation(id, ct)
            ?? throw new KeyNotFoundException("Saved location was not found.");
        if (location.UserId != current.UserId) throw new UnauthorizedAccessException();
        return location;
    }

    private static SavedLocationDetails ToDetails(SavedLocation value) => new(
        value.Id, value.Label, value.Address, value.Latitude, value.Longitude,
        value.Landmark, value.DeliveryInstructions, value.IsFavorite, value.IsDefault,
        value.PreferredCategories.Select(x => x.Category).Order().ToArray(),
        value.GooglePlaceId, value.AddressComponentsJson,
        value.CreatedAt, value.UpdatedAt);

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}

public sealed record GlobalSearchResults(
    string Query,
    PagedErrands Errands,
    IReadOnlyList<SavedLocationDetails> Locations,
    IReadOnlyList<ErrandCategoryDetails> Categories);

public static class ErrandCategoryCatalog
{
    public static readonly IReadOnlyList<ErrandCategoryDetails> All =
    [
        new(ErrandCategory.Grocery, "Grocery Run", "Groceries and household items from a preferred store.", true, true, false),
        new(ErrandCategory.Laundry, "Laundry Pickup", "Wash-and-fold or dry-cleaning pickup and delivery.", true, true, false),
        new(ErrandCategory.Pharmacy, "Pharmacy", "Prescription or over-the-counter pharmacy collection.", true, true, true),
        new(ErrandCategory.DocumentCollection, "Document Collection", "Secure pickup and delivery of documents.", false, false, false),
        new(ErrandCategory.Custom, "Custom Errand", "A detailed customer-defined task or multi-stop route.", true, true, false)
    ];

    public static IReadOnlyList<ErrandCategoryDetails> Search(string query) => All
        .Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || x.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
        .ToArray();
}
