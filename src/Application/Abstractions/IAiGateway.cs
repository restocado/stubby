namespace Stubby.Application.Abstractions;

/// <summary>
/// The only way to reach a model provider. Sends a prompt and images, returns the reply text.
/// Knows nothing about receipts. Does not retry: the caller owns the attempt budget.
/// </summary>
public interface IAiGateway
{
    /// <summary>The configured model that calls are sent to.</summary>
    string Model { get; }

    /// <exception cref="AiGatewayException">The call failed: provider error, timeout, rate limit or open circuit.</exception>
    Task<string> CompleteAsync(AiRequest request, CancellationToken cancellationToken);
}
