using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErrandRuns.Application;
using ErrandRuns.Domain.Common;
using ErrandRuns.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace ErrandRuns.Infrastructure.Payments;

public sealed class PaystackPaymentGateway(
    IHttpClientFactory clients,
    IOptions<PaystackOptions> configured) : IPaymentGateway
{
    public async Task<PaymentIntent> CreateIntent(
        Guid paymentId,
        Guid errandId,
        Money amount,
        string customerEmail,
        string paymentMethod,
        CancellationToken ct)
    {
        if (amount.Currency != "NGN")
            throw new DomainException("Paystack checkout currently requires NGN.");

        var options = RequiredOptions();
        var reference = $"errand-payment-{paymentId:N}";
        var channel = paymentMethod switch
        {
            "card" => "card",
            "bank_transfer" => "bank_transfer",
            "ussd" => "ussd",
            _ => throw new DomainException("Unsupported Paystack payment method.")
        };
        var payload = new Dictionary<string, object?>
        {
            ["email"] = customerEmail,
            ["amount"] = checked((long)(amount.Amount * 100m)),
            ["currency"] = amount.Currency,
            ["reference"] = reference,
            ["channels"] = new[] { channel },
            ["metadata"] = JsonSerializer.Serialize(new
            {
                errandId,
                paymentId,
                product = "ErrandRuns errand payment"
            })
        };
        if (!string.IsNullOrWhiteSpace(options.CallbackUrl))
            payload["callback_url"] = options.CallbackUrl;

        using var response = await Client(options).PostAsJsonAsync("transaction/initialize", payload, ct);
        var root = await Read(response, "Paystack could not initialize checkout.", ct);
        var data = root.GetProperty("data");
        return new PaymentIntent(
            "Paystack",
            RequiredText(data, "reference"),
            RequiredText(data, "authorization_url"),
            RequiredText(data, "access_code"));
    }

    public async Task<PaymentVerification> Verify(
        string providerReference,
        Money expectedAmount,
        CancellationToken ct)
    {
        var options = RequiredOptions();
        using var response = await Client(options).GetAsync(
            $"transaction/verify/{Uri.EscapeDataString(providerReference)}", ct);
        var root = await Read(response, "Paystack could not verify the payment.", ct);
        var data = root.GetProperty("data");
        var reference = RequiredText(data, "reference");
        var status = RequiredText(data, "status").ToLowerInvariant();
        var currency = RequiredText(data, "currency").ToUpperInvariant();
        var subunits = data.TryGetProperty("amount", out var amountValue)
            && amountValue.TryGetDecimal(out var value)
                ? value
                : throw new ExternalServiceException("Paystack returned an invalid payment amount.");
        return new(reference, status, subunits / 100m, currency);
    }

    private PaystackOptions RequiredOptions()
    {
        var options = configured.Value;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.SecretKey))
            throw new ExternalServiceException("Paystack customer payments are not configured.", statusCode: 503);
        return options;
    }

    private HttpClient Client(PaystackOptions options)
    {
        var client = clients.CreateClient("Paystack");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.SecretKey);
        return client;
    }

    private static async Task<JsonElement> Read(
        HttpResponseMessage response,
        string fallback,
        CancellationToken ct)
    {
        JsonElement root;
        try
        {
            root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new ExternalServiceException(fallback, error);
        }

        if (!response.IsSuccessStatusCode ||
            !root.TryGetProperty("status", out var success) ||
            success.ValueKind != JsonValueKind.True)
        {
            var message = root.TryGetProperty("message", out var detail)
                ? detail.GetString()
                : null;
            throw new ExternalServiceException(
                string.IsNullOrWhiteSpace(message) ? fallback : message);
        }
        return root;
    }

    private static string RequiredText(JsonElement value, string property) =>
        value.TryGetProperty(property, out var result) &&
        !string.IsNullOrWhiteSpace(result.GetString())
            ? result.GetString()!.Trim()
            : throw new ExternalServiceException($"Paystack did not return {property}.");
}

public sealed class DevelopmentPaymentGateway : IPaymentGateway
{
    public Task<PaymentIntent> CreateIntent(
        Guid paymentId,
        Guid errandId,
        Money amount,
        string customerEmail,
        string paymentMethod,
        CancellationToken ct) =>
        Task.FromResult(new PaymentIntent(
            "Development",
            $"dev-payment-{paymentId:N}",
            null,
            null,
            DevelopmentMode: true));

    public Task<PaymentVerification> Verify(
        string providerReference,
        Money expectedAmount,
        CancellationToken ct)
    {
        if (!providerReference.StartsWith("dev-payment-", StringComparison.Ordinal))
            throw new DomainException("Invalid development payment reference.");
        return Task.FromResult(new PaymentVerification(
            providerReference,
            "success",
            expectedAmount.Amount,
            expectedAmount.Currency));
    }
}

public sealed class UnavailablePaymentGateway : IPaymentGateway
{
    public Task<PaymentIntent> CreateIntent(
        Guid paymentId,
        Guid errandId,
        Money amount,
        string customerEmail,
        string paymentMethod,
        CancellationToken ct) =>
        throw new ExternalServiceException(
            "Customer payment provider is not configured.", statusCode: 503);

    public Task<PaymentVerification> Verify(
        string providerReference,
        Money expectedAmount,
        CancellationToken ct) =>
        throw new ExternalServiceException(
            "Customer payment provider is not configured.", statusCode: 503);
}
