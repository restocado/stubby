using Microsoft.Extensions.Configuration;
using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.Extraction;

/// <summary>Reads the <c>Extraction</c> settings from configuration on every access, so a change applies to the next receipt.</summary>
internal sealed class ConfigurationExtractionSettings(IConfiguration configuration) : IExtractionSettings
{
    public int MaxAttempts
    {
        get
        {
            var value = configuration.GetValue<int?>("Extraction:MaxAttempts")
                ?? throw new InvalidOperationException("Setting 'Extraction:MaxAttempts' is required.");
            return value >= 1
                ? value
                : throw new InvalidOperationException("Setting 'Extraction:MaxAttempts' must be at least 1.");
        }
    }

    public decimal FlagTotalLimit
    {
        get
        {
            var value = configuration.GetValue<decimal?>("Extraction:FlagTotalLimit")
                ?? throw new InvalidOperationException("Setting 'Extraction:FlagTotalLimit' is required.");
            return value > 0
                ? value
                : throw new InvalidOperationException("Setting 'Extraction:FlagTotalLimit' must be greater than 0.");
        }
    }

    /// <summary>Fails with a clear message at startup when a setting is missing or invalid.</summary>
    public void Validate()
    {
        _ = MaxAttempts;
        _ = FlagTotalLimit;
    }
}
