using System.Security.Claims;
using ErrandRuns.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ErrandRuns.Infrastructure.Identity;

public sealed class GoogleIdentityVerifier : IGoogleIdentityVerifier
{
    private const string MetadataAddress =
        "https://accounts.google.com/.well-known/openid-configuration";
    private readonly IConfiguration configuration;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> metadata =
        new(MetadataAddress, new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });

    public GoogleIdentityVerifier(IConfiguration configuration) =>
        this.configuration = configuration;

    public async Task<VerifiedGoogleIdentity?> Verify(string idToken, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("GoogleAuthentication:Enabled"))
            throw new ExternalServiceException("Google authentication is not configured.", statusCode: 503);
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 10000) return null;
        var audiences = (configuration.GetSection("GoogleAuthentication:ClientIds").Get<string[]>() ?? [])
            .Concat((configuration["GoogleAuthentication:ClientIdsCsv"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        if (audiences.Length == 0)
            throw new ExternalServiceException("Google authentication client IDs are not configured.", statusCode: 503);

        try
        {
            var provider = await metadata.GetConfigurationAsync(ct);
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = provider.SigningKeys,
                    ValidateIssuer = true,
                    ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
                    ValidateAudience = true,
                    ValidAudiences = audiences,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromMinutes(2)
                });
            if (!result.IsValid || result.ClaimsIdentity is null) return null;
            var subject = Claim(result.ClaimsIdentity, "sub");
            var email = Claim(result.ClaimsIdentity, "email");
            var verified = Claim(result.ClaimsIdentity, "email_verified");
            var name = Claim(result.ClaimsIdentity, "name");
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) ||
                !bool.TryParse(verified, out var isVerified) || !isVerified)
                return null;
            return new VerifiedGoogleIdentity(subject, email,
                string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            if (error is SecurityTokenException) return null;
            throw new ExternalServiceException("Google authentication is temporarily unavailable.", error);
        }
    }

    private static string? Claim(ClaimsIdentity identity, string type) =>
        identity.FindFirst(type)?.Value;
}
