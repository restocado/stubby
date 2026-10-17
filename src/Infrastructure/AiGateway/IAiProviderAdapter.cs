namespace Stubby.Infrastructure.AiGateway;

internal interface IAiProviderAdapter
{
    string Name { get; }

    Task<ProviderReply> SendAsync(
        string model,
        string prompt,
        IReadOnlyList<ReadOnlyMemory<byte>> images,
        CancellationToken cancellationToken);
}

internal sealed record ProviderReply(string Text, int? PromptTokens, int? ResponseTokens);
