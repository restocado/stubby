namespace Stubby.Application.Abstractions;

/// <summary>How a change to an existing receipt ended. Nothing was written unless it is <see cref="Done"/>.</summary>
public enum ReceiptWriteOutcome
{
    Done,
    NotFound,

    /// <summary>The receipt is not in a state that allows the change.</summary>
    Conflict,
}
