namespace Stubby.Domain.Receipts.Flagging;

public sealed record FlagResult(IReadOnlyList<FlagReason> Reasons)
{
    public bool IsFlagged => Reasons.Count > 0;

    /// <summary>Comma-separated reason names for <c>Receipt.FlagReasons</c>, or null when not flagged.</summary>
    public string? ReasonCodes => IsFlagged ? string.Join(',', Reasons) : null;
}
