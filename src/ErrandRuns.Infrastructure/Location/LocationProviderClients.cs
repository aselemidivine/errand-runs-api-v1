using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrandRuns.Application;
using ErrandRuns.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace ErrandRuns.Infrastructure.Location;

public sealed class GooglePlacesClient(
    HttpClient http,
    IOptions<GoogleMapsOptions> configured) : IGooglePlacesProvider
{
    private const string AutocompleteMask =
        "suggestions.placePrediction.placeId," +
        "suggestions.placePrediction.text.text," +
        "suggestions.placePrediction.structuredFormat.mainText.text," +
        "suggestions.placePrediction.structuredFormat.secondaryText.text";
    private const string DetailsMask =
        "id,displayName,formattedAddress,location,addressComponents,viewport";

    public async Task<IReadOnlyList<LocationAutocompleteSuggestion>> Autocomplete(
        string query, string sessionToken, CancellationToken ct)
    {
        var options = RequiredOptions();
        var payload = new
        {
            input = query,
            sessionToken,
            includedRegionCodes = new[] { "ng" },
            regionCode = "ng",
            languageCode = "en",
            locationBias = new
            {
                circle = new
                {
                    center = new
                    {
                        latitude = options.LagosLatitude,
                        longitude = options.LagosLongitude
                    },
                    radius = options.LagosBiasRadiusMeters
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/places:autocomplete")
        {
            Content = JsonContent.Create(payload)
        };
        AddGoogleHeaders(request, options.ServerApiKey, AutocompleteMask);
        using var response = await Send(request, ct);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        if (!document.RootElement.TryGetProperty("suggestions", out var suggestions)) return [];
        return suggestions.EnumerateArray()
            .Where(value => value.TryGetProperty("placePrediction", out _))
            .Select(value =>
            {
                var prediction = value.GetProperty("placePrediction");
                return new LocationAutocompleteSuggestion(
                    Text(prediction, "placeId"),
                    NestedText(prediction, "structuredFormat", "mainText"),
                    NestedText(prediction, "structuredFormat", "secondaryText"),
                    NestedText(prediction, "text"));
            })
            .Where(value => value.PlaceId.Length > 0 && value.FullText.Length > 0)
            .ToArray();
    }

    public async Task<GooglePlaceDetails> Details(
        string placeId, string sessionToken, CancellationToken ct)
    {
        var options = RequiredOptions();
        var path = $"v1/places/{Uri.EscapeDataString(placeId)}?sessionToken={Uri.EscapeDataString(sessionToken)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddGoogleHeaders(request, options.ServerApiKey, DetailsMask);
        using var response = await Send(request, ct);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = document.RootElement;
        var location = RequiredObject(root, "location");
        return new GooglePlaceDetails(
            Text(root, "id"),
            NestedText(root, "displayName"),
            Text(root, "formattedAddress"),
            Decimal(location, "latitude"),
            Decimal(location, "longitude"),
            Components(root),
            Viewport(root));
    }

    private GoogleMapsOptions RequiredOptions()
    {
        var options = configured.Value;
        if (!options.Enabled)
            throw new ExternalServiceException(
                "Google location discovery is not configured.", statusCode: 503);
        return options;
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode) return response;
            response.Dispose();
            throw new ExternalServiceException("Google Places could not complete the location request.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExternalServiceException("Google Places timed out.");
        }
        catch (HttpRequestException error)
        {
            throw new ExternalServiceException("Google Places is temporarily unavailable.", error);
        }
    }

    private static void AddGoogleHeaders(HttpRequestMessage request, string key, string mask)
    {
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", key);
        request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", mask);
    }

    internal static IReadOnlyList<AddressComponentDetails> Components(JsonElement root)
    {
        if (!root.TryGetProperty("addressComponents", out var values)) return [];
        return values.EnumerateArray().Select(value => new AddressComponentDetails(
            Text(value, "longText"), Text(value, "shortText"),
            value.TryGetProperty("types", out var types)
                ? types.EnumerateArray().Select(type => type.GetString() ?? string.Empty).Where(type => type.Length > 0).ToArray()
                : [])).ToArray();
    }

    internal static ViewportDetails? Viewport(JsonElement root)
    {
        if (!root.TryGetProperty("viewport", out var viewport)
            || !viewport.TryGetProperty("low", out var low)
            || !viewport.TryGetProperty("high", out var high)) return null;
        return new ViewportDetails(
            new(Decimal(low, "latitude"), Decimal(low, "longitude")),
            new(Decimal(high, "latitude"), Decimal(high, "longitude")));
    }

    internal static string Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? value.GetString()?.Trim() ?? string.Empty : string.Empty;

    private static string NestedText(JsonElement root, params string[] path)
    {
        foreach (var property in path)
            if (!root.TryGetProperty(property, out root)) return string.Empty;
        return Text(root, "text");
    }

    internal static decimal Decimal(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.TryGetDecimal(out var number)
            ? number
            : throw new ExternalServiceException("The location provider returned incomplete coordinates.");

    private static JsonElement RequiredObject(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value)
            ? value
            : throw new ExternalServiceException("The location provider returned incomplete details.");
}

public sealed class GoogleGeocodingClient(
    HttpClient http,
    IOptions<GoogleMapsOptions> configured) : IGoogleGeocodingProvider
{
    private const string FieldMask =
        "results.placeId,results.formattedAddress,results.location," +
        "results.addressComponents,results.types,results.granularity";

    public async Task<ReverseGeocodeDetails> Reverse(
        decimal latitude, decimal longitude, CancellationToken ct)
    {
        var options = configured.Value;
        if (!options.Enabled)
            throw new ExternalServiceException(
                "Google reverse geocoding is not configured.", statusCode: 503);

        var path = string.Create(CultureInfo.InvariantCulture,
            $"v4/geocode/location?location.latitude={latitude}&location.longitude={longitude}&languageCode=en&regionCode=ng");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", options.ServerApiKey);
        request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", FieldMask);

        using var response = await Send(request, ct);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("results", out var results)
            || results.GetArrayLength() == 0)
            throw new KeyNotFoundException("No deliverable address was found for these coordinates.");

        var best = results.EnumerateArray()
            .OrderBy(Rank)
            .First();
        var location = best.TryGetProperty("location", out var returned) ? returned : default;
        var returnedLatitude = location.ValueKind == JsonValueKind.Object
            ? GooglePlacesClient.Decimal(location, "latitude") : latitude;
        var returnedLongitude = location.ValueKind == JsonValueKind.Object
            ? GooglePlacesClient.Decimal(location, "longitude") : longitude;
        return new ReverseGeocodeDetails(
            GooglePlacesClient.Text(best, "placeId"), returnedLatitude, returnedLongitude,
            GooglePlacesClient.Components(best), GooglePlacesClient.Text(best, "formattedAddress"));
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode) return response;
            response.Dispose();
            throw new ExternalServiceException("Google Geocoding could not complete the location request.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExternalServiceException("Google Geocoding timed out.");
        }
        catch (HttpRequestException error)
        {
            throw new ExternalServiceException("Google Geocoding is temporarily unavailable.", error);
        }
    }

    private static int Rank(JsonElement result)
    {
        if (!result.TryGetProperty("types", out var types)) return 10;
        var values = types.EnumerateArray().Select(value => value.GetString()?.ToLowerInvariant()).ToArray();
        if (values.Contains("street_address") || values.Contains("subpremise")) return 0;
        if (values.Contains("premise")) return 1;
        if (values.Contains("route")) return 2;
        return 10;
    }
}

public sealed class IpApiCoGeolocationClient(
    HttpClient http,
    IOptions<IpGeolocationOptions> configured) : IIpGeolocationProvider
{
    public async Task<ApproximateIpLocation> Locate(IPAddress address, CancellationToken ct)
    {
        var options = configured.Value;
        if (!options.Enabled)
            throw new ExternalServiceException(
                "IP geolocation is not configured.", statusCode: 503);
        try
        {
            var path = $"{Uri.EscapeDataString(address.ToString())}/json/";
            using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceException("The IP-geolocation provider could not complete the request.");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True)
                throw new ExternalServiceException("The IP-geolocation provider rejected the request.");
            return new ApproximateIpLocation(
                GooglePlacesClient.Decimal(root, "latitude"),
                GooglePlacesClient.Decimal(root, "longitude"),
                GooglePlacesClient.Text(root, "city"),
                GooglePlacesClient.Text(root, "region"),
                GooglePlacesClient.Text(root, "country_name"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExternalServiceException("IP geolocation timed out.");
        }
        catch (HttpRequestException error)
        {
            throw new ExternalServiceException("IP geolocation is temporarily unavailable.", error);
        }
    }
}
