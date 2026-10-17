namespace Stubby.Web.Workers;

public sealed class ExtractionWorkerOptions
{
    /// <summary>How long the worker waits when the queue is empty, or after an unexpected error.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Reads <c>Extraction:PollIntervalSeconds</c>. A missing or invalid value fails at startup.</summary>
    public static ExtractionWorkerOptions From(IConfiguration configuration)
    {
        var seconds = configuration.GetValue<double?>("Extraction:PollIntervalSeconds")
            ?? throw new InvalidOperationException("Setting 'Extraction:PollIntervalSeconds' is required.");
        if (seconds is not > 0 or > 3600)
        {
            throw new InvalidOperationException("Setting 'Extraction:PollIntervalSeconds' must be more than 0 and at most 3600.");
        }

        return new ExtractionWorkerOptions { PollInterval = TimeSpan.FromSeconds(seconds) };
    }
}
