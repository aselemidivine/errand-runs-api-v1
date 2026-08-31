using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ErrandRuns.IntegrationTests;

public sealed class LocationSearchApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestKey = "integration-test-server-key-never-return";
    private readonly HttpClient client;

    public LocationSearchApiTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "InMemory",
                    ["Jwt:Issuer"] = "ErrandRuns",
                    ["Jwt:Audience"] = "ErrandRuns.Mobile",
                    ["Jwt:SigningKey"] = "integration-test-signing-key-at-least-32-bytes",
                    ["GoogleMaps:Enabled"] = "true",
                    ["GoogleMaps:ServerApiKey"] = TestKey
                }));
        }).CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/location-search/autocomplete?query=Lagos&sessionToken=session_1")]
    [InlineData("/api/v1/location-search/places/place-1?sessionToken=session_1")]
    [InlineData("/api/v1/location-search/reverse-geocode?latitude=6.5&longitude=3.4")]
    [InlineData("/api/v1/location-search/ip")]
    public async Task Location_discovery_routes_require_authentication(string route)
    {
        using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_lists_location_routes_without_exposing_the_server_key()
    {
        var document = await client.GetStringAsync(
            "/swagger/v1/swagger.json", TestContext.Current.CancellationToken);

        Assert.Contains("/api/v1/location-search/autocomplete", document);
        Assert.Contains("/api/v1/location-search/places/{placeId}", document);
        Assert.Contains("/api/v1/location-search/reverse-geocode", document);
        Assert.Contains("/api/v1/location-search/ip", document);
        Assert.DoesNotContain(TestKey, document);
        Assert.DoesNotContain("ServerApiKey", document, StringComparison.OrdinalIgnoreCase);
    }
}
