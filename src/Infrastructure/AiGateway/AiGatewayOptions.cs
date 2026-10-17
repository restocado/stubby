namespace Stubby.Infrastructure.AiGateway;

/// <summary>Bound from the <c>AiGateway</c> settings section. Read once at startup.</summary>
internal sealed class AiGatewayOptions
{
    public const string SectionName = "AiGateway";
    public const string OllamaCloudProvider = "OllamaCloud";

    /// <summary>The model to use. It must be listed in <see cref="Models"/>.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Model name to provider name, so changing the model needs no code change. A list and not a dictionary,
    /// because a model name such as <c>gemma4:31b</c> has a colon, which configuration reads as a section separator.
    /// </summary>
    public List<ModelMapping> Models { get; set; } = [];

    public OllamaCloudOptions OllamaCloud { get; set; } = new();

    /// <summary>A call may take this long, plus <see cref="TimeoutPerImageSeconds"/> for each image.</summary>
    public int TimeoutBaseSeconds { get; set; } = 30;

    public int TimeoutPerImageSeconds { get; set; } = 15;

    public int RateLimitPerMinute { get; set; } = 30;

    /// <summary>How many calls may go out at once before the rate limit makes the next ones wait.</summary>
    public int RateLimitBurst { get; set; } = 5;

    /// <summary>The circuit opens when this many calls fail with no success in the sampling window.</summary>
    public int CircuitFailureThreshold { get; set; } = 5;

    public int CircuitSamplingSeconds { get; set; } = 300;

    public int CircuitBreakSeconds { get; set; } = 60;

    public string? ProviderFor(string model) =>
        Models.FirstOrDefault(m => m.Name == model)?.Provider;

    public TimeSpan TimeoutFor(int imageCount) =>
        TimeSpan.FromSeconds(TimeoutBaseSeconds + (TimeoutPerImageSeconds * (double)imageCount));
}

internal sealed class OllamaCloudOptions
{
    public string BaseUrl { get; set; } = "https://ollama.com";

    /// <summary>From user-secrets or an environment variable. Never in source control (NFR-S3).</summary>
    public string? ApiKey { get; set; }
}

internal sealed class ModelMapping
{
    public string Name { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;
}
