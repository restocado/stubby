namespace Stubby.Domain.Receipts.Flagging;

public static class ReceiptFlagEvaluator
{
    public static FlagResult Evaluate(
        decimal? total,
        decimal? subtotal,
        decimal? tax,
        DateOnly? date,
        decimal totalLimit,
        DateOnly today)
    {
        var reasons = new List<FlagReason>();

        if (total is null)
        {
            reasons.Add(FlagReason.TotalMissing);
        }
        else
        {
            if (total <= 0)
            {
                reasons.Add(FlagReason.TotalNotPositive);
            }

            if (total >= totalLimit)
            {
                reasons.Add(FlagReason.TotalAtOrAboveLimit);
            }

            if (subtotal is not null && tax is not null && subtotal + tax != total)
            {
                reasons.Add(FlagReason.SubtotalTaxMismatch);
            }
        }

        if (date is null)
        {
            reasons.Add(FlagReason.DateMissing);
        }
        else if (date > today)
        {
            reasons.Add(FlagReason.DateInFuture);
        }
        else if (date < today.AddYears(-1))
        {
            reasons.Add(FlagReason.DateTooOld);
        }

        return new FlagResult(reasons);
    }
}
