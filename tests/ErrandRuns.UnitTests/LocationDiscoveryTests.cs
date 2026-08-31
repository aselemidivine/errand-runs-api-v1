using System.Net;
using System.Text;
using ErrandRuns.Application;
using ErrandRuns.Infrastructure.Configuration;
using ErrandRuns.Infrastructure.Location;
using Microsoft.Extensions.Options;

namespace ErrandRuns.UnitTests;

public sealed class LocationDiscoveryTests
{
    [Fact]
    public async Task Service_normalizes_autocomplete_input()
    {
        var places = new CapturingPlaces();
        var service = new LocationDiscoveryService(places, new NoGeocoding(), new NoIp());

        await service.Autocomplete("  Admiralty   Way  ", "session_1", TestContext.Current.CancellationToken);

        Assert.Equal("Admiralty Way", places.Query);
        Assert.Equal("session_1", places.SessionToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains spaces")]
    [InlineData("token!")]
    public void Session_token_validation_rejects_invalid_values(string token) =>
        Assert.Throws<ArgumentException>(() => LocationDiscoveryService.NormalizeSessionToken(token));

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    public async Task Reverse_geocode_accepts_coordinate_boundaries(decimal latitude, decimal longitude)
    {
        var geocoding = new CapturingGeocoding();
        var service = new LocationDiscoveryService(new CapturingPlaces(), geocoding, new NoIp());

        await service.Reverse(latitude, longitude, TestContext.Current.CancellationToken);

        Assert.Equal(latitude, geocoding.Latitude);
        Assert.Equal(longitude, geocoding.Longitude);
    }

    [Fact]
    public async Task Google_autocomplete_contract_restricts_Nigeria_and_biases_Lagos()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
            return Json("""
                {"suggestions":[{"placePrediction":{"placeId":"lagos-place","text":{"text":"Admiralty Way, Lekki, Lagos"},"structuredFormat":{"mainText":{"text":"Admiralty Way"},"secondaryText":{"text":"Lekki, Lagos"}}}}]}
                """);
        });
        var client = new GooglePlacesClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://places.googleapis.com/")
        }, Options.Create(GoogleOptions()));

        var results = await client.Autocomplete("Admiralty", "session_1", TestContext.Current.CancellationToken);

        Assert.Single(results);
        Assert.Equal("lagos-place", results[0].PlaceId);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/v1/places:autocomplete", captured.RequestUri!.PathAndQuery);
        Assert.True(captured.Headers.Contains("X-Goog-Api-Key"));
        Assert.Contains("suggestions.placePrediction.placeId", captured.Headers.GetValues("X-Goog-FieldMask").Single());
        Assert.Contains("\"includedRegionCodes\":[\"ng\"]", body);
        Assert.Contains("\"latitude\":6.5244", body);
        Assert.DoesNotContain("test-server-key", body);
    }

    [Fact]
    public async Task Place_details_contract_uses_session_and_minimal_field_mask()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Task.FromResult(Json("""
                {"id":"place-1","displayName":{"text":"Home"},"formattedAddress":"1 Admiralty Way, Lagos","location":{"latitude":6.45,"longitude":3.47},"addressComponents":[],"viewport":{"low":{"latitude":6.44,"longitude":3.46},"high":{"latitude":6.46,"longitude":3.48}}}
                """));
        });
        var client = new GooglePlacesClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://places.googleapis.com/")
        }, Options.Create(GoogleOptions()));

        var result = await client.Details("place-1", "session_1", TestContext.Current.CancellationToken);

        Assert.Equal("place-1", result.PlaceId);
        Assert.Equal("/v1/places/place-1?sessionToken=session_1", captured!.RequestUri!.PathAndQuery);
        Assert.Equal(
            "id,displayName,formattedAddress,location,addressComponents,viewport",
            captured.Headers.GetValues("X-Goog-FieldMask").Single());
    }

    [Fact]
    public async Task Reverse_geocoding_prefers_a_street_address()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json("""
            {"results":[
              {"placeId":"area","formattedAddress":"Lekki, Lagos","location":{"latitude":6.4,"longitude":3.4},"types":["locality"]},
              {"placeId":"street","formattedAddress":"1 Admiralty Way, Lekki, Lagos","location":{"latitude":6.45,"longitude":3.47},"addressComponents":[],"types":["street_address"]}
            ]}
            """)));
        var client = new GoogleGeocodingClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://geocode.googleapis.com/")
        }, Options.Create(GoogleOptions()));

        var result = await client.Reverse(6.4m, 3.4m, TestContext.Current.CancellationToken);

        Assert.Equal("street", result.PlaceId);
        Assert.Equal("1 Admiralty Way, Lekki, Lagos", result.FormattedAddress);
    }

    private static GoogleMapsOptions GoogleOptions() => new()
    {
        Enabled = true,
        ServerApiKey = "test-server-key"
    };

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class CapturingPlaces : IGooglePlacesProvider
    {
        public string? Query { get; private set; }
        public string? SessionToken { get; private set; }
        public Task<IReadOnlyList<LocationAutocompleteSuggestion>> Autocomplete(
            string query, string sessionToken, CancellationToken ct)
        {
            Query = query;
            SessionToken = sessionToken;
            return Task.FromResult<IReadOnlyList<LocationAutocompleteSuggestion>>([]);
        }
        public Task<GooglePlaceDetails> Details(string placeId, string sessionToken, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingGeocoding : IGoogleGeocodingProvider
    {
        public decimal Latitude { get; private set; }
        public decimal Longitude { get; private set; }
        public Task<ReverseGeocodeDetails> Reverse(decimal latitude, decimal longitude, CancellationToken ct)
        {
            Latitude = latitude;
            Longitude = longitude;
            return Task.FromResult(new ReverseGeocodeDetails("id", latitude, longitude, [], "address"));
        }
    }

    private sealed class NoGeocoding : IGoogleGeocodingProvider
    {
        public Task<ReverseGeocodeDetails> Reverse(decimal latitude, decimal longitude, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class NoIp : IIpGeolocationProvider
    {
        public Task<ApproximateIpLocation> Locate(IPAddress address, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
