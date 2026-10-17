using Microsoft.Extensions.Configuration;
using Stubby.Web.Workers;

namespace Stubby.Integration.Tests;

public class ExtractionWorkerOptionsTests
{
    private static IConfiguration Configuration(string? seconds) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(seconds is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Extraction:PollIntervalSeconds"] = seconds })
            .Build();

    [Theory]
    [InlineData("2", 2.0)]
    [InlineData("0.2", 0.2)]
    [InlineData("3600", 3600.0)]
    public void Poll_interval_is_read_in_seconds(string setting, double expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ExtractionWorkerOptions.From(Configuration(setting)).PollInterval);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("3601")]
    public void A_missing_or_invalid_poll_interval_fails_at_startup(string? setting)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => ExtractionWorkerOptions.From(Configuration(setting)));

        Assert.Contains("Extraction:PollIntervalSeconds", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_web_settings_give_a_poll_interval()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "web-appsettings.json"))
            .Build();

        Assert.Equal(TimeSpan.FromSeconds(2), ExtractionWorkerOptions.From(configuration).PollInterval);
    }
}
