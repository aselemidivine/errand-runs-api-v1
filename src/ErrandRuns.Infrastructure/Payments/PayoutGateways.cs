using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace ErrandRuns.Infrastructure.Payments;

public sealed class PaystackPayoutGateway(
    IHttpClientFactory clients,
    IOptions<PaystackOptions> options) : IPayoutGateway
{
    public async Task<PayoutRecipient> CreateRecipient(
        string bankCode, string accountNumber, string name, CancellationToken ct)
    {
        var client = Client();
        using var resolve = await client.GetAsync(
            $"/bank/resolve?account_number={Uri.EscapeDataString(accountNumber)}&bank_code={Uri.EscapeDataString(bankCode)}", ct);
        var resolved = await Read(resolve, ct);
        var accountName = resolved.GetProperty("data").GetProperty("account_name").GetString()
            ?? throw new DomainException("Bank account could not be resolved.");

        using var response = await client.PostAsJsonAsync("/transferrecipient", new
        {
            type = "nuban",
            name = string.IsNullOrWhiteSpace(accountName) ? name : accountName,
            account_number = accountNumber,
            bank_code = bankCode,
            currency = "NGN"
        }, ct);
        var result = await Read(response, ct);
        var code = result.GetProperty("data").GetProperty("recipient_code").GetString()
            ?? throw new DomainException("Payout recipient could not be created.");
        return new(code, accountName, accountNumber[^4..]);
    }

    public async Task<PayoutSubmission> Submit(
        Guid payoutId, Money amount, string recipientCode, CancellationToken ct)
    {
        if (amount.Currency != "NGN") throw new DomainException("Paystack payouts currently require NGN.");
        using var response = await Client().PostAsJsonAsync("/transfer", new
        {
            source = "balance",
            amount = checked((long)(amount.Amount * 100m)),
            recipient = recipientCode,
            reason = "ErrandRuns runner earnings",
            reference = $"runner-payout-{payoutId:N}"
        }, ct);
        var result = await Read(response, ct);
        var reference = result.GetProperty("data").GetProperty("reference").GetString()
            ?? throw new DomainException("Payout reference was not returned.");
        return new(reference);
    }

    private HttpClient Client()
    {
        var client = clients.CreateClient("Paystack");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.Value.SecretKey);
        return client;
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response, CancellationToken ct)
    {
        var value = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!response.IsSuccessStatusCode ||
            !value.TryGetProperty("status", out var status) || !status.GetBoolean())
        {
            var message = value.TryGetProperty("message", out var detail)
                ? detail.GetString() : "Payout provider request failed.";
            throw new DomainException(message ?? "Payout provider request failed.");
        }
        return value;
    }
}

public sealed class DevelopmentPayoutGateway : IPayoutGateway
{
    public Task<PayoutRecipient> CreateRecipient(string bankCode, string accountNumber, string name, CancellationToken ct) =>
        Task.FromResult(new PayoutRecipient("dev-recipient-" + Guid.NewGuid().ToString("N"), name.Trim(), accountNumber[^4..]));

    public Task<PayoutSubmission> Submit(Guid payoutId, Money amount, string recipientCode, CancellationToken ct) =>
        Task.FromResult(new PayoutSubmission("dev-transfer-" + payoutId.ToString("N")));
}

public sealed class UnavailablePayoutGateway : IPayoutGateway
{
    public Task<PayoutRecipient> CreateRecipient(string bankCode, string accountNumber, string name, CancellationToken ct) =>
        throw new DomainException("Payout provider is not configured.");
    public Task<PayoutSubmission> Submit(Guid payoutId, Money amount, string recipientCode, CancellationToken ct) =>
        throw new DomainException("Payout provider is not configured.");
}
