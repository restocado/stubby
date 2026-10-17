using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.AiGateway;

internal sealed class AiGateway(
    IEnumerable<IAiProviderAdapter> adapters,
    AiGatewayOptions options,
    ILogger<AiGateway> logger) : IAiGateway
{
    public string Model => options.Model;

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var model = options.Model;
        var imageCount = request.Images.Count;
        var timer = Stopwatch.StartNew();

        try
        {
            var provider = options.ProviderFor(model);
            var adapter = adapters.FirstOrDefault(a => a.Name == provider)
                ?? throw new AiGatewayException($"No adapter for provider '{provider}'.");

            var reply = await adapter.SendAsync(model, request.Prompt, request.Images, cancellationToken);

            // Only metadata is logged, never the prompt, the images or the reply.
            logger.LogInformation(
                "AI call finished. Model {Model}, outcome {Outcome}, {DurationMs} ms, {ImageCount} images, tokens in {PromptTokens} out {ResponseTokens}.",
                model, "Success", timer.ElapsedMilliseconds, imageCount, reply.PromptTokens, reply.ResponseTokens);
            return reply.Text;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogFailure(model, "Cancelled", timer, imageCount);
            throw;
        }
        catch (Exception ex)
        {
            var failure = ex as AiGatewayException ?? new AiGatewayException(ReasonFor(ex), ex);
            LogFailure(model, failure.Message, timer, imageCount);
            throw failure;
        }
    }

    private void LogFailure(string model, string outcome, Stopwatch timer, int imageCount) =>
        logger.LogWarning(
            "AI call finished. Model {Model}, outcome {Outcome}, {DurationMs} ms, {ImageCount} images.",
            model, outcome, timer.ElapsedMilliseconds, imageCount);

    private static string ReasonFor(Exception ex) => ex switch
    {
        BrokenCircuitException => "The circuit is open after repeated failures.",
        TimeoutRejectedException => "The call timed out.",
        RateLimiterRejectedException => "The rate limit was exceeded.",
        HttpRequestException { StatusCode: { } status } => $"The provider returned HTTP {(int)status}.",
        HttpRequestException => "The provider could not be reached.",
        _ => "The provider's reply could not be read.",
    };
}
