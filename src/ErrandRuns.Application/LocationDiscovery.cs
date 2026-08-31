using System.Net;
using ErrandRuns.Domain.Common;

namespace ErrandRuns.Application;

public sealed record LocationAutocompleteSuggestion(
    string PlaceId,
    string PrimaryText,
    string SecondaryText,
    string FullText);

public sealed record AddressComponentDetails(
    string LongText,
    string ShortText,
    IReadOnlyList<string> Types);

public sealed record CoordinateDetails(decimal Latitude, decimal Longitude);
public sealed record ViewportDetails(CoordinateDetails Low, CoordinateDetails High);

public sealed record GooglePlaceDetails(
    string PlaceId,
    string DisplayName,
    string FormattedAddress,
    decimal Latitude,
    decimal Longitude,
    IReadOnlyList<AddressComponentDetails> AddressComponents,
    ViewportDetails? Viewport);

public sealed record ReverseGeocodeDetails(
    string PlaceId,
    decimal Latitude,
    decimal Longitude,
    IReadOnlyList<AddressComponentDetails> AddressComponents,
    string FormattedAddress);

public sealed record ApproximateIpLocation(
    decimal Latitude,
    decimal Longitude,
    string? City,
    string? Region,
    string? Country,
    bool Approximate = true);

public interface IGooglePlacesProvider
{
    Task<IReadOnlyList<LocationAutocompleteSuggestion>> Autocomplete(
        string query, string sessionToken, CancellationToken ct);
    Task<GooglePlaceDetails> Details(string placeId, string sessionToken, CancellationToken ct);
}

public interface IGoogleGeocodingProvider
{
    Task<ReverseGeocodeDetails> Reverse(decimal latitude, decimal longitude, CancellationToken ct);
}

public interface IIpGeolocationProvider
{
    Task<ApproximateIpLocation> Locate(IPAddress address, CancellationToken ct);
}

public sealed class ExternalServiceException(
    string message,
    Exception? inner = null,
    int statusCode = 502)
    : Exception(message, inner)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class LocationDiscoveryService(
    IGooglePlacesProvider places,
    IGoogleGeocodingProvider geocoding,
    IIpGeolocationProvider ipGeolocation)
{
    public Task<IReadOnlyList<LocationAutocompleteSuggestion>> Autocomplete(
        string? query, string? sessionToken, CancellationToken ct) =>
        places.Autocomplete(NormalizeQuery(query), NormalizeSessionToken(sessionToken), ct);

    public Task<GooglePlaceDetails> Details(
        string? placeId, string? sessionToken, CancellationToken ct) =>
        places.Details(NormalizePlaceId(placeId), NormalizeSessionToken(sessionToken), ct);

    public Task<ReverseGeocodeDetails> Reverse(
        decimal latitude, decimal longitude, CancellationToken ct)
    {
        _ = new ErrandRuns.Domain.Errands.GeoPoint(latitude, longitude);
        return geocoding.Reverse(latitude, longitude, ct);
    }

    public Task<ApproximateIpLocation> Locate(IPAddress address, CancellationToken ct) =>
        ipGeolocation.Locate(address, ct);

    public static string NormalizeQuery(string? value)
    {
        var normalized = NormalizeWhitespace(value);
        if (normalized.Length is < 2 or > 100)
            throw new ArgumentException("Query must contain between 2 and 100 characters.");
        return normalized;
    }

    public static string NormalizeSessionToken(string? value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 36 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("Session token must be URL-safe and no longer than 36 characters.");
        return value;
    }

    public static string NormalizePlaceId(string? value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is < 3 or > 255 || value.Any(char.IsWhiteSpace))
            throw new ArgumentException("Place ID is invalid.");
        return value;
    }

    private static string NormalizeWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
