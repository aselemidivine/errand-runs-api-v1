using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErrandRuns.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ErrandRuns.IntegrationTests;

public sealed class RunnerVerificationAndOffersTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> app;

    public RunnerVerificationAndOffersTests(WebApplicationFactory<Program> factory) =>
        app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "InMemory",
                    ["ExternalServices:Paystack:Enabled"] = "false",
                    ["PrivateDocuments:RootPath"] = Path.Combine(Path.GetTempPath(), "errandruns-tests", Guid.NewGuid().ToString("N"))
                }));
        });

    [Fact]
    public async Task Verified_online_runner_can_find_and_accept_nearby_paid_errand()
    {
        var ct = TestContext.Current.CancellationToken;
        using var runner = app.CreateClient();
        runner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await Register(runner, "runners", ct));

        using (var identity = new MultipartFormDataContent())
        {
            identity.Add(new StringContent("DriversLicense"), "documentType");
            identity.Add(Png(), "document", "license.png");
            using var response = await runner.PostAsync("/api/v1/runners/me/verifications/identity", identity, ct);
            Assert.True(response.IsSuccessStatusCode,
                $"Identity upload failed ({response.StatusCode}): {await response.Content.ReadAsStringAsync(ct)}");
        }
        using (var vehicle = new MultipartFormDataContent())
        {
            vehicle.Add(new StringContent("Motorbike"), "vehicleType");
            vehicle.Add(new StringContent("LAG-123-XY"), "plateNumber");
            vehicle.Add(new StringContent("Honda CG125"), "makeModel");
            vehicle.Add(Png(), "ownershipDocument", "ownership.png");
            using var response = await runner.PostAsync("/api/v1/runners/me/verifications/vehicle", vehicle, ct);
            Assert.True(response.IsSuccessStatusCode,
                $"Vehicle upload failed ({response.StatusCode}): {await response.Content.ReadAsStringAsync(ct)}");
        }
        using (var response = await runner.PostAsync("/api/v1/runners/me/verifications/submit", null, ct))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Guid runnerId;
        using (var meResponse = await runner.GetAsync("/api/v1/auth/me", ct))
        using (var me = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync(ct)))
            runnerId = me.RootElement.GetProperty("id").GetGuid();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErrandRunsDbContext>();
            (await db.RunnerVerifications.SingleAsync(x => x.RunnerId == runnerId, ct)).Approve(DateTimeOffset.UtcNow);
            (await db.Runners.SingleAsync(x => x.UserId == runnerId, ct)).Approve();
            await db.SaveChangesAsync(ct);
        }
        using (var response = await runner.PutAsJsonAsync("/api/v1/runners/me/availability", new { available = true }, ct))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var customer = app.CreateClient();
        customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await Register(customer, "customers", ct));
        using var createdResponse = await customer.PostAsJsonAsync("/api/v1/errands", new
        {
            title = "Lekki grocery run",
            category = 0,
            stops = new[] { new { sequence = 1, type = 1, address = "Lekki Market", latitude = 6.45m, longitude = 3.47m } },
            merchandiseEstimate = 5000m,
            currency = "NGN"
        }, ct);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        using var created = JsonDocument.Parse(await createdResponse.Content.ReadAsStringAsync(ct));
        var errandId = created.RootElement.GetProperty("id").GetGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErrandRunsDbContext>();
            (await db.Errands.SingleAsync(x => x.Id == errandId, ct)).ConfirmPayment();
            await db.SaveChangesAsync(ct);
        }

        using var offersResponse = await runner.GetAsync(
            "/api/v1/runners/me/offers?latitude=6.45&longitude=3.47&radiusKm=10&page=1&pageSize=20", ct);
        Assert.Equal(HttpStatusCode.OK, offersResponse.StatusCode);
        using var offers = JsonDocument.Parse(await offersResponse.Content.ReadAsStringAsync(ct));
        Assert.Contains(offers.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("errandId").GetGuid() == errandId);

        using var accepted = await runner.PostAsync($"/api/v1/runners/me/offers/{errandId}/accept", null, ct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var result = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(ct));
        Assert.Equal(runnerId, result.RootElement.GetProperty("runnerId").GetGuid());
        Assert.Equal(6, result.RootElement.GetProperty("status").GetInt32());
    }

    private static ByteArrayContent Png()
    {
        var content = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    private static async Task<string> Register(HttpClient client, string kind, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/auth/{kind}/register", new
        {
            displayName = "Test Account",
            email = $"{Guid.NewGuid():N}@example.com",
            password = "ValidPass123"
        }, ct);
        Assert.True(response.IsSuccessStatusCode,
            $"Registration failed ({response.StatusCode}): {await response.Content.ReadAsStringAsync(ct)}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }
}
