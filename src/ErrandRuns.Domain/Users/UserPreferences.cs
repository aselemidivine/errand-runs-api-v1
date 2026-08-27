using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Errands;

namespace ErrandRuns.Domain.Users;

public sealed class SavedLocation
{
    private readonly List<SavedLocationCategory> _preferredCategories = [];

    private SavedLocation()
    {
        Label = string.Empty;
        Address = string.Empty;
    }

    public SavedLocation(
        Guid id,
        Guid userId,
        string label,
        string address,
        decimal latitude,
        decimal longitude,
        string? landmark,
        string? deliveryInstructions,
        bool isFavorite,
        bool isDefault,
        IEnumerable<ErrandCategory> preferredCategories,
        DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        Label = string.Empty;
        Address = string.Empty;
        CreatedAt = now;
        Update(label, address, latitude, longitude, landmark, deliveryInstructions,
            isFavorite, isDefault, preferredCategories, now);
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Label { get; private set; }
    public string Address { get; private set; }
    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }
    public string? Landmark { get; private set; }
    public string? DeliveryInstructions { get; private set; }
    public bool IsFavorite { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<SavedLocationCategory> PreferredCategories => _preferredCategories;

    public void Update(
        string label,
        string address,
        decimal latitude,
        decimal longitude,
        string? landmark,
        string? deliveryInstructions,
        bool isFavorite,
        bool isDefault,
        IEnumerable<ErrandCategory> preferredCategories,
        DateTimeOffset now)
    {
        label = Required(label, 60, "Location label");
        address = Required(address, 500, "Address");
        _ = new GeoPoint(latitude, longitude);

        if (landmark?.Trim().Length > 240)
            throw new DomainException("Landmark cannot exceed 240 characters.");
        if (deliveryInstructions?.Trim().Length > 1000)
            throw new DomainException("Delivery instructions cannot exceed 1000 characters.");

        var categories = preferredCategories?.Distinct().ToArray() ?? [];
        if (categories.Length > 6)
            throw new DomainException("A location cannot have more than six preferred categories.");

        Label = label;
        Address = address;
        Latitude = latitude;
        Longitude = longitude;
        Landmark = Clean(landmark);
        DeliveryInstructions = Clean(deliveryInstructions);
        IsFavorite = isFavorite;
        IsDefault = isDefault;
        UpdatedAt = now;

        _preferredCategories.Clear();
        _preferredCategories.AddRange(categories.Select(category => new SavedLocationCategory(Id, category)));
    }

    public void SetDefault(bool value, DateTimeOffset now)
    {
        IsDefault = value;
        UpdatedAt = now;
    }

    private static string Required(string value, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{field} is required.");
        value = value.Trim();
        if (value.Length > maximum) throw new DomainException($"{field} cannot exceed {maximum} characters.");
        return value;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SavedLocationCategory
{
    private SavedLocationCategory() { }

    public SavedLocationCategory(Guid savedLocationId, ErrandCategory category)
    {
        SavedLocationId = savedLocationId;
        Category = category;
    }

    public Guid SavedLocationId { get; private set; }
    public ErrandCategory Category { get; private set; }
}

public sealed class PhoneVerificationChallenge
{
    private PhoneVerificationChallenge() { PhoneNumber = string.Empty; }

    public PhoneVerificationChallenge(Guid id, Guid userId, string phoneNumber,
        DateTimeOffset sentAt, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        PhoneNumber = phoneNumber;
        SentAt = sentAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string PhoneNumber { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool CanResend(DateTimeOffset now, TimeSpan cooldown) => SentAt + cooldown <= now;
    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && ExpiresAt > now && FailedAttempts < 5;

    public void Resent(DateTimeOffset now, DateTimeOffset expiresAt)
    {
        SentAt = now;
        ExpiresAt = expiresAt;
        FailedAttempts = 0;
        ConsumedAt = null;
    }

    public void RecordFailure() => FailedAttempts++;
    public void Consume(DateTimeOffset now) => ConsumedAt ??= now;
    public void DeliveryFailed(DateTimeOffset now, TimeSpan cooldown)
    {
        SentAt = now - cooldown;
        ExpiresAt = now;
    }
}
