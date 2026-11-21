using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErrandRuns.Domain.Common;
using ErrandRuns.Domain.Payments;
using ErrandRuns.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErrandRuns.IntegrationTests;

public sealed class NotificationAndAccountDeletionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    private readonly WebApplicationFactory<Program> app;

    public NotificationAndAccountDeletionTests(WebApplicationFactory<Program> factory)
    {
        app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "InMemory",
                    ["ExternalServices:Paystack:Enabled"] = "false"
                }));
        });
        client = app.CreateClient();
    }

    [Fact]
    public async Task Customer_and_runner_receive_matched_and_stop_notifications()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var customerToken = await Register("customers", cancellation);
        var runnerToken = await Register("runners", cancellation);
        using var runnerClient = app.CreateClient();
        runnerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", runnerToken);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", customerToken);

        using var creation = await client.PostAsJsonAsync("/api/v1/errands", new
        {
            title = "One-stop delivery",
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

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErrandRunsDbContext>();
            var errand = await db.Errands.SingleAsync(value => value.Id == errandId, cancellation);
            errand.ConfirmPayment();
            await db.SaveChangesAsync(cancellation);
        }

        using var submitted = await runnerClient.PostAsync(
            "/api/v1/runners/me/verification/submit", null, cancellation);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var runnerAccount = await runnerClient.GetAsync("/api/v1/auth/me", cancellation);
        using var runner = JsonDocument.Parse(await runnerAccount.Content.ReadAsStringAsync(cancellation));
        var runnerId = runner.RootElement.GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErrandRunsDbContext>();
            var profile = await db.Runners.SingleAsync(value => value.UserId == runnerId, cancellation);
            profile.Approve();
            await db.SaveChangesAsync(cancellation);
        }
        using var online = await runnerClient.PutAsJsonAsync(
            "/api/v1/runners/me/availability", new { available = true }, cancellation);
        Assert.Equal(HttpStatusCode.OK, online.StatusCode);

        using var matched = await client.PostAsync($"/api/v1/errands/{errandId}/match", null, cancellation);
        Assert.Equal(HttpStatusCode.OK, matched.StatusCode);
        using var accepted = await runnerClient.PostAsync(
            $"/api/v1/runners/me/jobs/{errandId}/accept", null, cancellation);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var journey = await runnerClient.PostAsync(
            $"/api/v1/runners/me/jobs/{errandId}/start-journey", null, cancellation);
        Assert.Equal(HttpStatusCode.OK, journey.StatusCode);

        using var detailsResponse = await client.GetAsync($"/api/v1/errands/{errandId}", cancellation);
        using var details = JsonDocument.Parse(await detailsResponse.Content.ReadAsStringAsync(cancellation));
        var stopId = details.RootElement.GetProperty("stops")[0].GetProperty("id").GetGuid();
        using var started = await runnerClient.PostAsync(
            $"/api/v1/runners/me/jobs/{errandId}/stops/{stopId}/start", null, cancellation);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var completed = await runnerClient.PostAsync(
            $"/api/v1/runners/me/jobs/{errandId}/stops/{stopId}/complete", null, cancellation);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        var customerTitles = await NotificationTitles(client, cancellation);
        Assert.Contains("Runner matched", customerTitles);
        Assert.Contains("Runner reached a stop", customerTitles);
        Assert.Contains("Errand stop completed", customerTitles);
        Assert.Contains("Confirm your errand", customerTitles);
        Assert.Contains("New errand assignment", await NotificationTitles(runnerClient, cancellation));
    }

    private async Task<string> Register(string kind, CancellationToken cancellation)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/auth/{kind}/register",
            new
            {
                displayName = "Test Account",
                email = $"{Guid.NewGuid():N}@example.com",
                password = "ValidPass123"
            }, cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return document.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<string?[]> NotificationTitles(
        HttpClient client, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(
            "/api/v1/notifications?page=1&pageSize=50", cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return document.RootElement.GetProperty("items").EnumerateArray()
            .Select(value => value.GetProperty("title").GetString()).ToArray();
    }

    [Fact]
    public async Task Runner_must_settle_payout_and_withdraw_earnings_before_deleting_account()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var token = await Register("runners", cancellation);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var accountResponse = await client.GetAsync("/api/v1/auth/me", cancellation);
        using var account = JsonDocument.Parse(await accountResponse.Content.ReadAsStringAsync(cancellation));
        var runnerId = account.RootElement.GetProperty("id").GetGuid();
        var payout = new RunnerPayout(Guid.NewGuid(), runnerId, new Money(1000),
            new Money(50), "deletion-safeguard", DateTimeOffset.UtcNow);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErrandRunsDbContext>();
            db.RunnerPayouts.Add(payout);
            await db.SaveChangesAsync(cancellation);
        }
        using var unsettled = await client.SendAsync(DeleteRequest("ValidPass123"), cancellation);
        Assert.Equal(HttpStatusCode.Conflict, unsettled.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErrandRunsDbContext>();
            db.RunnerPayouts.Remove(await db.RunnerPayouts.SingleAsync(
                value => value.Id == payout.Id, cancellation));
            db.RunnerLedger.Add(new RunnerLedgerEntry(Guid.NewGuid(), runnerId, null, null,
                RunnerLedgerEntryType.Earning, new Money(1000), "Test earnings", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(cancellation);
        }
        using var balance = await client.SendAsync(DeleteRequest("ValidPass123"), cancellation);
        Assert.Equal(HttpStatusCode.Conflict, balance.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErrandRunsDbContext>();
            db.RunnerLedger.Add(new RunnerLedgerEntry(Guid.NewGuid(), runnerId, null, payout.Id,
                RunnerLedgerEntryType.Payout, new Money(1000), "Test payout", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(cancellation);
        }
        using var deleted = await client.SendAsync(DeleteRequest("ValidPass123"), cancellation);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
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
