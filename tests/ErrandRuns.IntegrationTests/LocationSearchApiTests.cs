using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task Customer_can_create_a_single_stop_grocery_errand_without_delivery()
    {
        var email = $"grocery-{Guid.NewGuid():N}@example.com";
        using var registration = await client.PostAsJsonAsync(
            "/api/v1/auth/customers/register",
            new
            {
                displayName = "Grocery Customer",
                email,
                password = "ValidPass123"
            }, TestContext.Current.CancellationToken);
        registration.EnsureSuccessStatusCode();
        using var authentication = JsonDocument.Parse(
            await registration.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var accessToken = authentication.RootElement.GetProperty("accessToken").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/errands")
        {
            Content = JsonContent.Create(new
            {
                title = "Weekly grocery run",
                category = 0,
                stops = new[]
                {
                    new
                    {
                        sequence = 1,
                        type = 1,
                        address = "Lekki market, Lagos",
                        latitude = 6.45m,
                        longitude = 3.47m,
                        instructions = "Buy the listed groceries"
                    }
                },
                merchandiseEstimate = 5000m,
                currency = "NGN",
                items = new[] { new { name = "Milk", quantity = 1 } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 but received {(int)response.StatusCode}. " +
            $"WWW-Authenticate: {string.Join(", ", response.Headers.WwwAuthenticate)}. Body: {responseBody}");
        Assert.DoesNotContain("at least two stops", responseBody, StringComparison.OrdinalIgnoreCase);
        using var errand = JsonDocument.Parse(responseBody);
        Assert.Equal(1, errand.RootElement.GetProperty("stopCount").GetInt32());
    }
}
