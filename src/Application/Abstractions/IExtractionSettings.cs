namespace Stubby.Application.Abstractions;

/// <summary>
/// Settings for processing receipts. They are read on every call, so a change applies to the next receipt.
/// A read throws <see cref="InvalidOperationException"/> when a setting is missing or invalid.
/// </summary>
public interface IExtractionSettings
{
    /// <summary>The most extraction attempts a receipt gets before it is Failed. 1 means no retry (NFR-R1).</summary>
    int MaxAttempts { get; }

    /// <summary>A receipt whose total is at or above this is flagged.</summary>
    decimal FlagTotalLimit { get; }
}
