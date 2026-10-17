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

    /// <summary>Runs at the start of every call, for example to cancel the caller's token mid-extraction.</summary>
    public Action<CancellationToken>? OnExtract { get; set; }

    public List<ReceiptFile> Files { get; } = [];

    public Task<ExtractionResult> ExtractAsync(ReceiptFile file, CancellationToken cancellationToken)
    {
        Files.Add(file);
        OnExtract?.Invoke(cancellationToken);
        return Failure is null ? Task.FromResult(Result) : Task.FromException<ExtractionResult>(Failure);
    }
}

internal sealed class FakeExtractionSettings : IExtractionSettings
{
    public int MaxAttempts { get; set; } = 1;

    public decimal FlagTotalLimit { get; set; } = 3000m;

    /// <summary>When set, every read throws it, as a missing or invalid setting does.</summary>
    public Exception? Failure { get; set; }

    int IExtractionSettings.MaxAttempts => Failure is null ? MaxAttempts : throw Failure;

    decimal IExtractionSettings.FlagTotalLimit => Failure is null ? FlagTotalLimit : throw Failure;
}

/// <summary>A clock that only moves when a test says so. Local time is UTC, so "today" is the UTC date.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
