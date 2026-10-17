namespace Stubby.Domain.Receipts.Flagging;

public enum FlagReason
{
    TotalMissing,
    TotalNotPositive,
    TotalAtOrAboveLimit,
    SubtotalTaxMismatch,
    DateMissing,
    DateInFuture,
    DateTooOld,
}
