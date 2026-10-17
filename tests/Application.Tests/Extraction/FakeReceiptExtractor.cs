using Stubby.Application.Abstractions;

namespace Stubby.Application.Tests.Extraction;

/// <summary>A receipt extractor for tests: returns set results, or throws on demand.</summary>
internal sealed class FakeReceiptExtractor : IReceiptExtractor
{
    public static ExtractionResult SampleResult { get; } = new(
        "Fake Cafe", new DateOnly(2026, 10, 2), 12.50m, Subtotal: null, Tax: null, "fake-model:1b", "v1");

    /// <summary>What every call returns, unless <see cref="Failure"/> is set.</summary>
    public ExtractionResult Result { get; set; } = SampleResult;

    /// <summary>When set, every call throws it.</summary>
    public Exception? Failure { get; set; }

    public List<ReceiptFile> Files { get; } = [];

    public Task<ExtractionResult> ExtractAsync(ReceiptFile file, CancellationToken cancellationToken)
    {
        Files.Add(file);
        return Failure is null ? Task.FromResult(Result) : Task.FromException<ExtractionResult>(Failure);
    }
}
