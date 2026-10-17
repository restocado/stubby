using Stubby.Application.Abstractions;
using Stubby.Eval.Answers;

namespace Stubby.Eval.Features.Run;

/// <summary>How one reading is judged against the correct answer.</summary>
public static class EvalScorer
{
    /// <summary>
    /// The merchant matches when the letters and digits are the same, ignoring case, spacing and punctuation:
    /// <c>ACME Foods, Inc.</c> matches <c>acme foods inc</c>, and <c>ACME Food</c> does not.
    /// </summary>
    public static bool MerchantMatches(string expected, string? actual) =>
        actual is not null && Normalize(expected) == Normalize(actual);

    /// <summary>Exact, as decimals: <c>12.5</c> equals <c>12.50</c>, and a missing total is wrong.</summary>
    public static bool TotalMatches(decimal expected, decimal? actual) => actual == expected;

    /// <summary>Exact. A receipt with no printed date is read correctly only when no date comes back.</summary>
    public static bool DateMatches(DateOnly? expected, DateOnly? actual) => expected == actual;

    /// <summary>
    /// Scores one receipt. A failure counts as wrong on every field, and the total counts as wrong and not flagged.
    /// </summary>
    /// <param name="flagged">Whether the real flag rules flag what was read. Ignored for a failure.</param>
    public static ReceiptResult Score(ReceiptAnswer expected, ExtractionResult? result, string? failure, bool flagged)
    {
        if (result is null)
        {
            return new ReceiptResult(expected, Failure: failure ?? "The receipt could not be read.", null, null, null, false, false, false, false);
        }

        return new ReceiptResult(
            expected,
            Failure: null,
            result.Merchant,
            result.Date,
            result.Total,
            MerchantMatches(expected.Merchant, result.Merchant),
            DateMatches(expected.Date, result.Date),
            TotalMatches(expected.Total, result.Total),
            flagged);
    }

    private static string Normalize(string value) =>
        new([.. value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}

/// <summary>What was read from one receipt, and whether each field was right.</summary>
/// <param name="Failure">Why the receipt could not be read, or null when it was read.</param>
/// <param name="Flagged">Whether the flag rules flag what was read.</param>
public sealed record ReceiptResult(
    ReceiptAnswer Expected,
    string? Failure,
    string? ActualMerchant,
    DateOnly? ActualDate,
    decimal? ActualTotal,
    bool MerchantCorrect,
    bool DateCorrect,
    bool TotalCorrect,
    bool Flagged)
{
    public bool Failed => Failure is not null;
}
