using System.Net.Http.Json;
using ErrandRuns.Application;
using ErrandRuns.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace ErrandRuns.Infrastructure.Identity;

public sealed class TermiiPhoneOtpSender(
    IHttpClientFactory clients,
    IOptions<TermiiOptions> configured) : IPhoneOtpSender
{
    public async Task Send(string phoneNumber, string code, CancellationToken ct)
    {
        var options = configured.Value;
        var payload = new
        {
            api_key = options.ApiKey,
            to = phoneNumber.TrimStart('+'),
            from = options.SenderId,
            sms = $"Your ErrandRuns verification code is {code}. It expires in 10 minutes.",
            type = "plain",
            channel = "dnd"
        };

        using var response = await clients.CreateClient("Termii")
            .PostAsJsonAsync("api/sms/send", payload, ct);
        response.EnsureSuccessStatusCode();
    }
}

// Identity still generates and validates the code in Development. Returning it
// in the API response avoids sending real SMS while keeping the full flow testable.
public sealed class DevelopmentPhoneOtpSender : IPhoneOtpSender
{
    public Task Send(string phoneNumber, string code, CancellationToken ct) => Task.CompletedTask;
}

public sealed class UnavailablePhoneOtpSender : IPhoneOtpSender
{
    public Task Send(string phoneNumber, string code, CancellationToken ct) =>
        throw new InvalidOperationException("Phone verification delivery is not configured.");
}
