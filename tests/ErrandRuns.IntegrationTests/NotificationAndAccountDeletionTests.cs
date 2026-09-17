using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ErrandRuns.IntegrationTests;

public sealed class NotificationAndAccountDeletionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public NotificationAndAccountDeletionTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "InMemory",
                    ["ExternalServices:Paystack:Enabled"] = "false"
                }));
        }).CreateClient();
    }

    [Fact]
    public async Task Customer_receives_lifecycle_notifications_and_can_delete_account_after_removing_unpaid_errand()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var email = $"notification-delete-{Guid.NewGuid():N}@example.com";
        const string password = "ValidPass123";
        using var registration = await client.PostAsJsonAsync(
            "/api/v1/auth/customers/register",
            new { displayName = "Notification Customer", email, password }, cancellation);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var account = JsonDocument.Parse(await registration.Content.ReadAsStringAsync(cancellation));
        var token = account.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var creation = await client.PostAsJsonAsync("/api/v1/errands", new
        {
            title = "Grocery notification test",
            category = 0,
            stops = new[] { new
            {
                sequence = 1, type = 1, address = "Lekki Market, Lagos",
                latitude = 6.45m, longitude = 3.47m
            } },
            merchandiseEstimate = 5000m,
            currency = "NGN"
        }, cancellation);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        using var created = JsonDocument.Parse(await creation.Content.ReadAsStringAsync(cancellation));
        var errandId = created.RootElement.GetProperty("id").GetGuid();

        using var list = await client.GetAsync("/api/v1/notifications?page=1&pageSize=20", cancellation);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var notifications = JsonDocument.Parse(await list.Content.ReadAsStringAsync(cancellation));
        var titles = notifications.RootElement.GetProperty("items").EnumerateArray()
            .Select(value => value.GetProperty("title").GetString()).ToArray();
        Assert.Contains("Welcome to ErrandRuns", titles);
        Assert.Contains("Errand created", titles);
        Assert.Contains("Payment required", titles);

        using var wrongPassword = await client.SendAsync(DeleteRequest("WrongPass123"), cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, wrongPassword.StatusCode);
        using var activeErrand = await client.SendAsync(DeleteRequest(password), cancellation);
        Assert.Equal(HttpStatusCode.Conflict, activeErrand.StatusCode);

        using var deleteErrand = await client.DeleteAsync($"/api/v1/errands/{errandId}", cancellation);
        Assert.Equal(HttpStatusCode.NoContent, deleteErrand.StatusCode);
        using var deleteAccount = await client.SendAsync(DeleteRequest(password), cancellation);
        Assert.Equal(HttpStatusCode.NoContent, deleteAccount.StatusCode);

        using var oldToken = await client.GetAsync("/api/v1/auth/me", cancellation);
        Assert.Equal(HttpStatusCode.Unauthorized, oldToken.StatusCode);
        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { emailOrPhone = email, password }, cancellation);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    private static HttpRequestMessage DeleteRequest(string password) =>
        new(HttpMethod.Delete, "/api/v1/auth/me")
        {
            Content = JsonContent.Create(new { currentPassword = password })
        };
}
