using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ErrandRuns.Infrastructure.Configuration;

public static class ExternalServiceRegistration
{
    public static IServiceCollection AddExternalServiceConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Bind<GoogleMapsOptions>(services, configuration, GoogleMapsOptions.SectionName,
            x => !x.Enabled || HasValue(x.ApiKey), "Google Maps API key is required when enabled.");
        Bind<PaystackOptions>(services, configuration, PaystackOptions.SectionName,
            x => !x.Enabled || (HasValue(x.SecretKey) && HasValue(x.WebhookSecret)),
            "Paystack secret and webhook keys are required when enabled.");
        Bind<SendGridOptions>(services, configuration, SendGridOptions.SectionName,
            x => !x.Enabled || (HasValue(x.ApiKey) && HasValue(x.FromEmail)),
            "SendGrid API key and sender email are required when enabled.");
        Bind<TermiiOptions>(services, configuration, TermiiOptions.SectionName,
            x => !x.Enabled || HasValue(x.ApiKey), "Termii API key is required when enabled.");
        Bind<SmileIdentityOptions>(services, configuration, SmileIdentityOptions.SectionName,
            x => !x.Enabled || (HasValue(x.PartnerId) && HasValue(x.ApiKey) && HasValue(x.WebhookSecret)),
            "Smile Identity partner, API, and webhook credentials are required when enabled.");
        Bind<AzureBlobStorageOptions>(services, configuration, AzureBlobStorageOptions.SectionName,
            x => !x.Enabled || (HasValue(x.ConnectionString) && x.SignedUrlLifetimeMinutes is > 0 and <= 60),
            "Azure Blob credentials and a signed URL lifetime of 1-60 minutes are required when enabled.");
        Bind<FirebaseOptions>(services, configuration, FirebaseOptions.SectionName,
            x => !x.Enabled || (HasValue(x.ProjectId) && HasValue(x.ServiceAccountJsonBase64)),
            "Firebase project and service account are required when enabled.");
        Bind<OpenTelemetryOptions>(services, configuration, OpenTelemetryOptions.SectionName,
            x => !x.Enabled || Uri.TryCreate(x.OtlpEndpoint, UriKind.Absolute, out _),
            "A valid OpenTelemetry endpoint is required when enabled.");

        AddClient<GoogleMapsOptions>(services, "GoogleMaps", x => x.BaseUrl);
        AddClient<PaystackOptions>(services, "Paystack", x => x.BaseUrl);
        AddClient<SendGridOptions>(services, "SendGrid", x => x.BaseUrl);
        AddClient<TermiiOptions>(services, "Termii", x => x.BaseUrl);
        AddClient<SmileIdentityOptions>(services, "SmileIdentity", x => x.BaseUrl);
        return services;
    }

    private static void Bind<T>(IServiceCollection services, IConfiguration configuration,
        string section, Func<T, bool> validation, string failureMessage) where T : class
        => services.AddOptions<T>().Bind(configuration.GetSection(section))
            .Validate(validation, failureMessage).ValidateOnStart();

    private static void AddClient<T>(IServiceCollection services, string name, Func<T, string> baseUrl)
        where T : class
        => services.AddHttpClient(name, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<T>>().Value;
            client.BaseAddress = new Uri(baseUrl(options));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ErrandRuns-Api/1.0");
        });

    private static bool HasValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.StartsWith("replace-", StringComparison.OrdinalIgnoreCase);
}
