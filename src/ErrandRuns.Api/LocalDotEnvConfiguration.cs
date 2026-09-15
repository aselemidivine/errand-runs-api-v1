namespace ErrandRuns.Api;

public static class LocalDotEnvConfiguration
{
    public static void LoadLocalDotEnv(
        this ConfigurationManager configuration,
        string contentRootPath)
    {
        var path = CandidatePaths(contentRootPath).FirstOrDefault(File.Exists);
        if (path is null) return;

        var values = Parse(File.ReadLines(path));
        Apply(configuration, values, "GoogleMaps:Enabled", "GOOGLE_MAPS_ENABLED");
        Apply(configuration, values, "GoogleMaps:ServerApiKey",
            "GOOGLE_MAPS_SERVER_API_KEY", "GOOGLE_MAPS_API_KEY");
        Apply(configuration, values, "GoogleMaps:PlacesBaseUrl", "GOOGLE_MAPS_PLACES_BASE_URL");
        Apply(configuration, values, "GoogleMaps:GeocodingBaseUrl", "GOOGLE_MAPS_GEOCODING_BASE_URL");
        Apply(configuration, values, "IpGeolocation:Enabled", "IP_GEOLOCATION_ENABLED");
        Apply(configuration, values, "IpGeolocation:Provider", "IP_GEOLOCATION_PROVIDER");
        Apply(configuration, values, "IpGeolocation:BaseUrl", "IP_GEOLOCATION_BASE_URL");
        Apply(configuration, values, "ExternalServices:Paystack:Enabled", "PAYSTACK_ENABLED");
        Apply(configuration, values, "ExternalServices:Paystack:SecretKey", "PAYSTACK_SECRET_KEY");
        Apply(configuration, values, "ExternalServices:Paystack:PublicKey", "PAYSTACK_PUBLIC_KEY");
        Apply(configuration, values, "ExternalServices:Paystack:WebhookSecret", "PAYSTACK_WEBHOOK_SECRET");
        Apply(configuration, values, "ExternalServices:Paystack:BaseUrl", "PAYSTACK_BASE_URL");
        Apply(configuration, values, "ExternalServices:Paystack:CallbackUrl", "PAYSTACK_CALLBACK_URL");
    }

    private static IEnumerable<string> CandidatePaths(string contentRootPath)
    {
        yield return Path.Combine(contentRootPath, ".env");
        yield return Path.GetFullPath(Path.Combine(contentRootPath, "..", "..", ".env"));
        yield return Path.Combine(Directory.GetCurrentDirectory(), ".env");
    }

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            result[name] = value;
        }
        return result;
    }

    private static void Apply(
        ConfigurationManager configuration,
        IReadOnlyDictionary<string, string> values,
        string configurationKey,
        params string[] environmentNames)
    {
        // Real process environment variables take precedence over the local file.
        var hierarchicalName = configurationKey.Replace(":", "__", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(hierarchicalName))) return;

        foreach (var name in environmentNames)
        {
            var environmentValue = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                configuration[configurationKey] = environmentValue;
                return;
            }
        }

        foreach (var name in environmentNames)
        {
            if (values.TryGetValue(name, out var fileValue) && !string.IsNullOrWhiteSpace(fileValue))
            {
                configuration[configurationKey] = fileValue;
                return;
            }
        }
    }
}
