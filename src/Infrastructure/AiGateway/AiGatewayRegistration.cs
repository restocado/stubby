using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.RateLimiting;
using Polly.Timeout;
using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.AiGateway;

internal static class AiGatewayRegistration
{
    public static IServiceCollection AddAiGateway(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AiGatewayOptions.SectionName).Get<AiGatewayOptions>() ?? new AiGatewayOptions();
        Validate(options);

        services.AddSingleton(options);
        services.AddTransient<IAiGateway, AiGateway>();
        services.AddTransient<IAiProviderAdapter>(sp => sp.GetRequiredService<OllamaCloudAdapter>());
        services.AddHttpClient<OllamaCloudAdapter>(client =>
            {
                client.BaseAddress = new Uri(options.OllamaCloud.BaseUrl, UriKind.Absolute);

                // The pipeline's timeout strategy sets the limit for each call, from the image count.
                client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            })
            .AddResilienceHandler("ai-gateway", builder => ConfigurePipeline(builder, options));
        return services;
    }

    // There is deliberately no retry strategy: the worker owns the attempt budget (NFR-R1).
    // Outermost first, so the breaker sees timeouts, and the timeout covers any wait for the rate limiter.
    private static void ConfigurePipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder, AiGatewayOptions options)
    {
        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            // Polly's breaker is ratio-based: a ratio of 1 with this throughput means a run of
            // failures with no success in the sampling window.
            FailureRatio = 1.0,
            MinimumThroughput = options.CircuitFailureThreshold,
            SamplingDuration = TimeSpan.FromSeconds(options.CircuitSamplingSeconds),
            BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakSeconds),
        });

        builder.AddTimeout(new TimeoutStrategyOptions
        {
            TimeoutGenerator = args =>
            {
                var timeout = args.Context.GetRequestMessage() is { } request
                    && request.Options.TryGetValue(OllamaCloudAdapter.TimeoutKey, out var perCall)
                    ? perCall
                    : options.TimeoutFor(imageCount: 0);
                return ValueTask.FromResult(timeout);
            },
        });

        // A call over the limit waits for a free slot. The timeout above cancels the wait.
        var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.RateLimitBurst,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromSeconds(60.0 / options.RateLimitPerMinute),
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
        builder.AddRateLimiter(new RateLimiterStrategyOptions
        {
            RateLimiter = args => limiter.AcquireAsync(permitCount: 1, args.Context.CancellationToken),
        });
    }

    private static void Validate(AiGatewayOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new InvalidOperationException($"Setting '{AiGatewayOptions.SectionName}:Model' is required.");
        }

        var provider = options.ProviderFor(options.Model);
        if (provider is null)
        {
            throw new InvalidOperationException(
                $"Model '{options.Model}' is not listed in '{AiGatewayOptions.SectionName}:Models'.");
        }

        if (provider != AiGatewayOptions.OllamaCloudProvider)
        {
            throw new InvalidOperationException($"Unknown AI provider '{provider}' for model '{options.Model}'.");
        }

        if (!Uri.TryCreate(options.OllamaCloud.BaseUrl, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Setting '{AiGatewayOptions.SectionName}:OllamaCloud:BaseUrl' must be an absolute https URL (NFR-S2).");
        }

        RequireAtLeast(options.TimeoutBaseSeconds, 1, nameof(options.TimeoutBaseSeconds));
        RequireAtLeast(options.TimeoutPerImageSeconds, 0, nameof(options.TimeoutPerImageSeconds));
        RequireAtLeast(options.RateLimitPerMinute, 1, nameof(options.RateLimitPerMinute));
        RequireAtLeast(options.RateLimitBurst, 1, nameof(options.RateLimitBurst));
        RequireAtLeast(options.CircuitFailureThreshold, 2, nameof(options.CircuitFailureThreshold));
        RequireAtLeast(options.CircuitSamplingSeconds, 1, nameof(options.CircuitSamplingSeconds));
        RequireAtLeast(options.CircuitBreakSeconds, 1, nameof(options.CircuitBreakSeconds));
    }

    private static void RequireAtLeast(int value, int minimum, string name)
    {
        if (value < minimum)
        {
            throw new InvalidOperationException($"Setting '{AiGatewayOptions.SectionName}:{name}' must be at least {minimum}.");
        }
    }
}
