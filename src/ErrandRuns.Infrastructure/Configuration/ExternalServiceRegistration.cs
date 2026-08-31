using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ErrandRuns.Application;
using ErrandRuns.Infrastructure.Location;

namespace ErrandRuns.Infrastructure.Configuration;

public static class ExternalServiceRegistration
{
    public static IServiceCollection AddExternalServiceConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        Bind<GoogleMapsOptions>(services, configuration, GoogleMapsOptions.SectionName,
            x => !x.Enabled || (HasValue(x.ServerApiKey)
                && Uri.TryCreate(x.PlacesBaseUrl, UriKind.Absolute, out _)
                && Uri.TryCreate(x.GeocodingBaseUrl, UriKind.Absolute, out _)
                && x.LagosLatitude is >= -90 and <= 90
                && x.LagosLongitude is >= -180 and <= 180
                && x.LagosBiasRadiusMeters is > 0 and <= 50000
                && x.TimeoutSeconds is > 0 and <= 30),
            "GoogleMaps:ServerApiKey, valid provider URLs, Lagos bias, and a 1-30 second timeout are required when Google Maps is enabled.");
        Bind<IpGeolocationOptions>(services, configuration, IpGeolocationOptions.SectionName,
            x => !x.Enabled || (x.Provider == "IpApiCo"
                && Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out _)
                && x.TimeoutSeconds is > 0 and <= 30),
            "A supported IP-geolocation provider, valid URL, and a 1-30 second timeout are required when enabled.");
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

        services.AddHttpClient<GooglePlacesClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<GoogleMapsOptions>>().Value;
            client.BaseAddress = new Uri(options.PlacesBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ErrandRuns-Api/1.0");
        });
        services.AddScoped<IGooglePlacesProvider>(provider => provider.GetRequiredService<GooglePlacesClient>());
        services.AddHttpClient<GoogleGeocodingClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<GoogleMapsOptions>>().Value;
            client.BaseAddress = new Uri(options.GeocodingBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ErrandRuns-Api/1.0");
        });
        services.AddScoped<IGoogleGeocodingProvider>(provider => provider.GetRequiredService<GoogleGeocodingClient>());
        services.AddHttpClient<IpApiCoGeolocationClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<IpGeolocationOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ErrandRuns-Api/1.0");
        });
        services.AddScoped<IIpGeolocationProvider>(provider => provider.GetRequiredService<IpApiCoGeolocationClient>());
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
