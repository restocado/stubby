using System.Globalization;
using System.Text;

namespace Stubby.Eval.Features.Run;

/// <summary>One accuracy figure and the target it has to reach.</summary>
public sealed record Measure(string Name, int Correct, int Count, int TargetPercent)
{
    /// <summary>There was nothing to measure, for example flag coverage when no total was wrong.</summary>
    public bool NotApplicable => Count == 0;

    // Whole numbers only, so that 27 of 30 is exactly 90 per cent and 26 of 30 is not.
    public bool Passed => NotApplicable || Correct * 100 >= TargetPercent * Count;

    public string Display => NotApplicable
        ? "not applicable"
        : string.Create(CultureInfo.InvariantCulture, $"{Correct}/{Count} ({100m * Correct / Count:0.0}%)");
}

public sealed record GroupResult(string Group, int Count, int TotalCorrect, int MerchantCorrect, int DateCorrect, int Failed);

/// <summary>The outcome of a run: what was read, how it scored, and whether the targets were met.</summary>
public sealed record EvalReport(
    string Model,
    string PromptVersion,
    int Seed,
    DateOnly Today,
    decimal FlagTotalLimit,
    IReadOnlyList<ReceiptResult> Results)
{
    public const int TotalTarget = 90;
    public const int MerchantTarget = 85;
    public const int DateTarget = 85;
    public const int FlagCoverageTarget = 95;

    public Measure Total => new("Total", Results.Count(r => r.TotalCorrect), Results.Count, TotalTarget);

    public Measure Merchant => new("Merchant", Results.Count(r => r.MerchantCorrect), Results.Count, MerchantTarget);

    public Measure Date => new("Date", Results.Count(r => r.DateCorrect), Results.Count, DateTarget);

    /// <summary>
    /// How many of the wrong totals were flagged, so that the user is told to check them. When no total is wrong
    /// there is nothing to cover, and it is not applicable.
    /// </summary>
    public Measure FlagCoverage
    {
        get
        {
            var wrong = Results.Where(r => !r.TotalCorrect).ToList();
            return new Measure("Flag coverage", wrong.Count(r => r.Flagged), wrong.Count, FlagCoverageTarget);
        }
    }

    public IReadOnlyList<Measure> Measures => [Total, Merchant, Date, FlagCoverage];

    public bool Passed => Measures.All(m => m.Passed);

    /// <summary>0 when every target is met and 1 when one is missed.</summary>
    public int ExitCode => Passed ? 0 : 1;

    public IReadOnlyList<GroupResult> Groups =>
    [
        .. Results.GroupBy(r => r.Expected.Group).Select(g => new GroupResult(
            g.Key,
            g.Count(),
            g.Count(r => r.TotalCorrect),
            g.Count(r => r.MerchantCorrect),
            g.Count(r => r.DateCorrect),
            g.Count(r => r.Failed))),
    ];

    public string ToMarkdown()
    {
        var text = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        text.AppendLine("# Eval report").AppendLine();
        text.AppendLine(inv, $"- Model: {Model}");
        text.AppendLine(inv, $"- Prompt version: {PromptVersion}");
        text.AppendLine(inv, $"- Receipts: {Results.Count} (seed {Seed}, today {Today:yyyy-MM-dd}, flag limit {FlagTotalLimit:0.##})");
        text.AppendLine(inv, $"- Result: **{(Passed ? "PASS" : "FAIL")}**").AppendLine();

        text.AppendLine("## Accuracy").AppendLine();
        text.AppendLine("| Measure | Result | Target | |").AppendLine("|---|---|---|---|");
        foreach (var measure in Measures)
        {
            text.AppendLine(inv, $"| {measure.Name} | {measure.Display} | at least {measure.TargetPercent}% | {(measure.Passed ? "pass" : "FAIL")} |");
        }

        text.AppendLine().AppendLine("## Groups").AppendLine();
        text.AppendLine("| Group | Receipts | Total | Merchant | Date | Not read |").AppendLine("|---|---|---|---|---|---|");
        foreach (var group in Groups)
        {
            text.AppendLine(inv, $"| {group.Group} | {group.Count} | {group.TotalCorrect}/{group.Count} | {group.MerchantCorrect}/{group.Count} | {group.DateCorrect}/{group.Count} | {group.Failed} |");
        }

        text.AppendLine().AppendLine("## Wrong fields").AppendLine();
        var wrong = Results.Where(r => !r.Failed).SelectMany(WrongFields).ToList();
        if (wrong.Count == 0)
        {
            text.AppendLine("None.");
        }

        wrong.ForEach(line => text.AppendLine(line));

        text.AppendLine().AppendLine("## Receipts that could not be read").AppendLine();
        var failed = Results.Where(r => r.Failed).ToList();
        if (failed.Count == 0)
        {
            text.AppendLine("None.");
        }

        failed.ForEach(r => text.AppendLine(inv, $"- {r.Expected.File} ({r.Expected.Group}): {r.Failure}"));
        return text.ToString();
    }

    private static IEnumerable<string> WrongFields(ReceiptResult r)
    {
        var inv = CultureInfo.InvariantCulture;
        var head = $"- {r.Expected.File} ({r.Expected.Group}):";

        if (!r.TotalCorrect)
        {
            var got = r.ActualTotal is { } t ? t.ToString("F2", inv) : "none";
            yield return string.Create(inv, $"{head} total expected {r.Expected.Total:F2}, got {got}{(r.Flagged ? " (flagged)" : " (not flagged)")}");
        }

        if (!r.MerchantCorrect)
        {
            yield return string.Create(inv, $"{head} merchant expected \"{r.Expected.Merchant}\", got {(r.ActualMerchant is null ? "none" : $"\"{r.ActualMerchant}\"")}");
        }

        if (!r.DateCorrect)
        {
            var expected = r.Expected.Date is { } e ? e.ToString("yyyy-MM-dd", inv) : "no date";
            var got = r.ActualDate is { } a ? a.ToString("yyyy-MM-dd", inv) : "none";
            yield return $"{head} date expected {expected}, got {got}";
        }
    }
}
