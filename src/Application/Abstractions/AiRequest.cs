namespace Stubby.Application.Abstractions;

public sealed record AiRequest(string Prompt, IReadOnlyList<ReadOnlyMemory<byte>> Images);
