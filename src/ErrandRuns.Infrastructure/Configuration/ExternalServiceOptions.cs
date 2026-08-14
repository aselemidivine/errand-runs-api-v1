namespace ErrandRuns.Infrastructure.Configuration;

public sealed class GoogleMapsOptions
{
    public const string SectionName = "ExternalServices:GoogleMaps";
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://maps.googleapis.com";
}

public sealed class PaystackOptions
{
    public const string SectionName = "ExternalServices:Paystack";
    public bool Enabled { get; init; }
    public string SecretKey { get; init; } = string.Empty;
    public string PublicKey { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.paystack.co";
}

public sealed class SendGridOptions
{
    public const string SectionName = "ExternalServices:SendGrid";
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string FromEmail { get; init; } = string.Empty;
    public string FromName { get; init; } = "ErrandRuns";
    public string BaseUrl { get; init; } = "https://api.sendgrid.com";
}

public sealed class TermiiOptions
{
    public const string SectionName = "ExternalServices:Termii";
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string SenderId { get; init; } = "ErrandRuns";
    public string BaseUrl { get; init; } = "https://api.ng.termii.com";
}

public sealed class SmileIdentityOptions
{
    public const string SectionName = "ExternalServices:SmileIdentity";
    public bool Enabled { get; init; }
    public string PartnerId { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.smileidentity.com";
}

public sealed class AzureBlobStorageOptions
{
    public const string SectionName = "ExternalServices:AzureBlobStorage";
    public bool Enabled { get; init; }
    public string ConnectionString { get; init; } = string.Empty;
    public string PrivateContainer { get; init; } = "errandruns-private";
    public int SignedUrlLifetimeMinutes { get; init; } = 10;
}

public sealed class FirebaseOptions
{
    public const string SectionName = "ExternalServices:Firebase";
    public bool Enabled { get; init; }
    public string ProjectId { get; init; } = string.Empty;
    public string ServiceAccountJsonBase64 { get; init; } = string.Empty;
}

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "ExternalServices:OpenTelemetry";
    public bool Enabled { get; init; }
    public string OtlpEndpoint { get; init; } = string.Empty;
    public string OtlpHeaders { get; init; } = string.Empty;
}
