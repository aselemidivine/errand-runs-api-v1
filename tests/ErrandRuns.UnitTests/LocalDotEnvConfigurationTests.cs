using ErrandRuns.Api;
using Microsoft.Extensions.Configuration;

namespace ErrandRuns.UnitTests;

public sealed class LocalDotEnvConfigurationTests
{
    [Fact]
    public void Parser_supports_comments_quotes_and_values_containing_equals()
    {
        var values = LocalDotEnvConfiguration.Parse(new[]
        {
            "# comment",
            "GOOGLE_MAPS_ENABLED=true",
            "GOOGLE_MAPS_API_KEY='key=with=equals'"
        });

        Assert.Equal("true", values["GOOGLE_MAPS_ENABLED"]);
        Assert.Equal("key=with=equals", values["GOOGLE_MAPS_API_KEY"]);
    }

    [Fact]
    public void Loader_maps_the_legacy_google_key_without_exposing_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "errandruns-dotenv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllLines(Path.Combine(directory, ".env"), new[]
            {
                "GOOGLE_MAPS_ENABLED=true",
                "GOOGLE_MAPS_API_KEY=legacy-secret-value"
            });
            var configuration = new ConfigurationManager();
            configuration["GoogleMaps:Enabled"] = "false";

            configuration.LoadLocalDotEnv(directory);

            Assert.Equal("true", configuration["GoogleMaps:Enabled"]);
            Assert.Equal("legacy-secret-value", configuration["GoogleMaps:ServerApiKey"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
