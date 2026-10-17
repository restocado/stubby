using Stubby.Domain.Receipts.Flagging;

namespace Stubby.Web.Components;

/// <summary>The words the review screen uses for each flag reason.</summary>
internal static class FlagReasonText
{
    public static string Describe(FlagReason reason) => reason switch
    {
        FlagReason.TotalMissing => "The total could not be read.",
        FlagReason.TotalNotPositive => "The total is zero or negative.",
        FlagReason.TotalAtOrAboveLimit => "The total is unusually large.",
        FlagReason.SubtotalTaxMismatch => "The subtotal and tax do not add up to the total. This also happens when there is a tip.",
        FlagReason.DateMissing => "The date could not be read.",
        FlagReason.DateInFuture => "The date is in the future.",
        FlagReason.DateTooOld => "The date is more than a year old.",
        _ => reason.ToString(),
    };
}
