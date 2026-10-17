namespace Stubby.Application.Abstractions;

/// <summary>
/// What the model read. Every field the model could not read is null. Subtotal and tax exist
/// only so the flag check can use them: they are never stored.
/// </summary>
public sealed record ExtractionResult(
    string? Merchant,
    DateOnly? Date,
    decimal? Total,
    decimal? Subtotal,
    decimal? Tax,
    string Model,
    string PromptVersion);
