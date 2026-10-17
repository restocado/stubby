using System.Security.Cryptography;
using Stubby.Application.Abstractions;
using Stubby.Eval.Answers;
using Stubby.Eval.Features.Run;

namespace Stubby.Eval.Tests;

/// <summary>
/// An extractor that knows the answer key. It recognises a receipt by its bytes and returns the correct reading,
/// which a test can then spoil receipt by receipt. It never touches the network.
/// </summary>
internal sealed class AnswerKeyExtractor : IReceiptExtractor
{
    private readonly Dictionary<string, ReceiptAnswer> _byHash;
    private int _inFlight;
    private int _maxInFlight;

    public AnswerKeyExtractor(string folder, AnswerFile answers)
    {
        _byHash = answers.Receipts.ToDictionary(
            a => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, a.File)))),
            a => a);
    }

    /// <summary>Changes the correct reading of a receipt. Return it unchanged to leave it right.</summary>
    public Func<ReceiptAnswer, ExtractionResult, ExtractionResult>? Spoil { get; set; }

    /// <summary>File names that fail to be read, as a gateway timeout would.</summary>
    public HashSet<string> Failing { get; } = [];

    public List<string> Calls { get; } = [];

    public int MaxInFlight => _maxInFlight;

    public async Task<ExtractionResult> ExtractAsync(ReceiptFile file, CancellationToken cancellationToken)
    {
        var answer = _byHash[Convert.ToHexString(SHA256.HashData(file.Content.Span))];
        Calls.Add(answer.File);
        var now = Interlocked.Increment(ref _inFlight);
        _maxInFlight = Math.Max(_maxInFlight, now);
        try
        {
            // A real pause, so that two reads at once would overlap and be seen.
            await Task.Delay(2, cancellationToken);
            if (Failing.Contains(answer.File))
            {
                throw new ExtractionException("The call timed out.");
            }

            var correct = new ExtractionResult(answer.Merchant, answer.Date, answer.Total, answer.Subtotal, answer.Tax, "fake-model:1b", "v1");
            return Spoil?.Invoke(answer, correct) ?? correct;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

public class EvalScorerTests
{
    [Theory]
    [InlineData("ACME Foods, Inc.", "acme foods inc")]
    [InlineData("ACME Foods, Inc.", "ACME FOODS INC.")]
    [InlineData("Maple & Pine Cafe", "maple pine cafe")]
    [InlineData("Maple & Pine Cafe", "  Maple   &   Pine   Cafe  ")]
    [InlineData("Corner Bakery No. 9", "corner bakery no 9")]
    [InlineData("Café Müller", "CAFÉ MÜLLER")]
    public void A_merchant_matches_ignoring_case_spacing_and_punctuation(string expected, string actual)
    {
        Assert.True(EvalScorer.MerchantMatches(expected, actual));
    }

    [Theory]
    [InlineData("ACME Foods, Inc.", "ACME Food")]
    [InlineData("ACME Foods, Inc.", "ACME Foods Ltd")]
    [InlineData("Corner Bakery No. 9", "Corner Bakery No. 8")]
    [InlineData("Maple & Pine Cafe", "Maple and Pine Cafe")]
    [InlineData("Café Müller", "Cafe Muller")]
    [InlineData("ACME Foods", "")]
    public void A_different_word_or_number_does_not_match(string expected, string actual)
    {
        Assert.False(EvalScorer.MerchantMatches(expected, actual));
    }

    [Fact]
    public void A_missing_merchant_is_wrong()
    {
        Assert.False(EvalScorer.MerchantMatches("ACME Foods", null));
    }

    [Theory]
    [InlineData("12.50", "12.5", true)]
    [InlineData("12.50", "12.50", true)]
    [InlineData("12.50", "12.51", false)]
    [InlineData("12.50", "125.0", false)]
    public void A_total_is_compared_as_an_exact_number(string expected, string actual, bool matches)
    {
        var e = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        var a = decimal.Parse(actual, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(matches, EvalScorer.TotalMatches(e, a));
    }

    [Fact]
    public void A_missing_total_is_wrong()
    {
        Assert.False(EvalScorer.TotalMatches(12.50m, null));
    }

    [Fact]
    public void Dates_must_be_identical_and_a_receipt_with_no_date_is_right_only_when_none_comes_back()
    {
        var date = new DateOnly(2026, 10, 2);

        Assert.True(EvalScorer.DateMatches(date, date));
        Assert.False(EvalScorer.DateMatches(date, date.AddDays(1)));
        Assert.False(EvalScorer.DateMatches(date, null));
        Assert.True(EvalScorer.DateMatches(null, null));
        Assert.False(EvalScorer.DateMatches(null, date));
    }
}

public class RunEvalTests(GeneratedSet set) : IClassFixture<GeneratedSet>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<(EvalReport Report, AnswerKeyExtractor Extractor)> RunAsync(
        Action<AnswerKeyExtractor>? configure = null, string? reportPath = null)
    {
        var extractor = new AnswerKeyExtractor(set.Folder, set.Answers);
        configure?.Invoke(extractor);
        var options = new RunEvalOptions(set.Folder, set.Answers.TotalLimit, reportPath, "fake-model:1b");
        var report = await new RunEvalCommandHandler(extractor).Handle(new RunEvalCommand(options), Token);
        return (report, extractor);
    }

    private static IEnumerable<string> FilesOf(AnswerFile answers, string group) =>
        answers.Receipts.Where(r => r.Group == group).Select(r => r.File);

    [Fact]
    public async Task An_extractor_that_returns_every_answer_exactly_scores_100_per_cent_and_passes()
    {
        var (report, extractor) = await RunAsync();

        Assert.Equal(30, report.Results.Count);
        Assert.Equal(30, extractor.Calls.Count);
        Assert.All(report.Measures.Where(m => m.Name != "Flag coverage"), m => Assert.Equal((30, 30), (m.Correct, m.Count)));
        Assert.True(report.FlagCoverage.NotApplicable);
        Assert.True(report.Passed);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public async Task Receipts_are_read_one_at_a_time_in_the_order_of_the_answer_file()
    {
        var (_, extractor) = await RunAsync();

        Assert.Equal(1, extractor.MaxInFlight);
        Assert.Equal(set.Answers.Receipts.Select(r => r.File), extractor.Calls);
    }

    [Theory]
    [InlineData(3, true, 0)]
    [InlineData(4, false, 1)]
    public async Task Twenty_seven_correct_totals_of_thirty_pass_and_twenty_six_fail_with_a_non_zero_exit_code(int wrong, bool passes, int exitCode)
    {
        var spoiled = set.Answers.Receipts.Take(wrong).Select(r => r.File).ToHashSet();

        var (report, _) = await RunAsync(e => e.Spoil = (answer, result) =>
            spoiled.Contains(answer.File) ? result with { Total = answer.Total + 1.00m } : result);

        Assert.Equal(30 - wrong, report.Total.Correct);
        Assert.Equal(passes, report.Total.Passed);

        // Every wrong total here also breaks the subtotal and tax, so each one is flagged and coverage stays full.
        Assert.Equal(passes, report.Passed);
        Assert.Equal(exitCode, report.ExitCode);
    }

    [Fact]
    public async Task Four_wrong_totals_with_three_flagged_give_75_per_cent_coverage_and_fail()
    {
        var wrong = FilesOf(set.Answers, "Clean").Take(4).ToList();

        var (report, _) = await RunAsync(e => e.Spoil = (answer, result) =>
        {
            var index = wrong.IndexOf(answer.File);
            return index switch
            {
                // A missing total is flagged.
                >= 0 and < 3 => result with { Total = null },

                // A wrong total with no subtotal or tax to contradict it, a plausible date and a normal size: not flagged.
                3 => result with { Total = answer.Total + 1.00m, Subtotal = null, Tax = null },
                _ => result,
            };
        });

        Assert.Equal(4, report.FlagCoverage.Count);
        Assert.Equal(3, report.FlagCoverage.Correct);
        Assert.Equal("3/4 (75.0%)", report.FlagCoverage.Display);
        Assert.False(report.FlagCoverage.Passed);

        // Four wrong totals is 26 of 30, which also misses the total target, so the coverage miss is not the only one.
        Assert.False(report.Total.Passed);
        Assert.False(report.Passed);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public async Task With_no_wrong_totals_flag_coverage_is_not_applicable_and_does_not_fail()
    {
        var (report, _) = await RunAsync();

        Assert.True(report.FlagCoverage.NotApplicable);
        Assert.Equal("not applicable", report.FlagCoverage.Display);
        Assert.True(report.FlagCoverage.Passed);
    }

    [Fact]
    public async Task A_known_number_of_wrong_totals_merchants_and_dates_gives_the_matching_percentages()
    {
        var files = set.Answers.Receipts.Select(r => r.File).ToList();
        var totals = files.Skip(0).Take(3).ToHashSet();
        var merchants = files.Skip(3).Take(2).ToHashSet();
        var dates = files.Skip(5).Take(4).Where(f => set.Answers.Receipts.Single(r => r.File == f).Date is not null).ToHashSet();

        var (report, _) = await RunAsync(e => e.Spoil = (answer, result) =>
        {
            if (totals.Contains(answer.File))
            {
                result = result with { Total = answer.Total + 5.00m };
            }

            if (merchants.Contains(answer.File))
            {
                result = result with { Merchant = answer.Merchant + " Group" };
            }

            return dates.Contains(answer.File) ? result with { Date = answer.Date!.Value.AddDays(-1) } : result;
        });

        Assert.Equal("27/30 (90.0%)", report.Total.Display);
        Assert.Equal("28/30 (93.3%)", report.Merchant.Display);
        Assert.Equal($"{30 - dates.Count}/30", report.Date.Display[..report.Date.Display.IndexOf(' ')]);
        Assert.Equal(4, dates.Count);
    }

    [Fact]
    public async Task A_weak_group_does_not_fail_the_run_when_the_overall_targets_pass()
    {
        // All four photos wrong in the total: 4 of 30 is 86.7 per cent, so spoil only three of them to stay at 90.
        var photos = FilesOf(set.Answers, "Blurry").Take(3).ToHashSet();

        var (report, _) = await RunAsync(e => e.Spoil = (answer, result) =>
            photos.Contains(answer.File) ? result with { Total = null } : result);

        var blurry = report.Groups.Single(g => g.Group == "Blurry");
        Assert.Equal((4, 1), (blurry.Count, blurry.TotalCorrect));
        Assert.True(report.Passed);
    }

    [Fact]
    public async Task An_exception_on_one_receipt_does_not_stop_the_run_and_the_receipt_is_listed()
    {
        var failing = set.Answers.Receipts[9].File;

        var (report, extractor) = await RunAsync(e => e.Failing.Add(failing));

        Assert.Equal(30, report.Results.Count);
        Assert.Equal(30, extractor.Calls.Count);
        var failed = Assert.Single(report.Results, r => r.Failed);
        Assert.Equal(failing, failed.Expected.File);
        Assert.Equal("The call timed out.", failed.Failure);
        Assert.False(failed.MerchantCorrect);
        Assert.False(failed.DateCorrect);
        Assert.False(failed.TotalCorrect);
        Assert.False(failed.Flagged);

        // The wrong total of the failed receipt was not flagged, so it is the one wrong total and it is uncovered.
        Assert.Equal((0, 1), (report.FlagCoverage.Correct, report.FlagCoverage.Count));
        Assert.False(report.Passed);
        Assert.Contains($"- {failing} ({failed.Expected.Group}): The call timed out.", report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unexpected_exception_is_scored_as_a_failure_without_its_message()
    {
        var (report, _) = await RunAsync(e => e.Spoil = (answer, _) =>
            answer.File == set.Answers.Receipts[0].File ? throw new InvalidOperationException("SECRET-DETAIL") : throw new NotSupportedException());

        Assert.All(report.Results, r => Assert.True(r.Failed));
        Assert.DoesNotContain("SECRET-DETAIL", report.ToMarkdown(), StringComparison.Ordinal);
        Assert.Contains("An unexpected InvalidOperationException stopped the reading.", report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_report_shows_the_model_the_prompt_version_and_all_seven_groups()
    {
        var (report, _) = await RunAsync();

        var markdown = report.ToMarkdown();

        Assert.Contains("- Model: fake-model:1b", markdown, StringComparison.Ordinal);
        Assert.Contains("- Prompt version: v1", markdown, StringComparison.Ordinal);
        Assert.Contains("seed 20261017, today 2026-10-17, flag limit 3000", markdown, StringComparison.Ordinal);
        Assert.Contains("**PASS**", markdown, StringComparison.Ordinal);
        foreach (var group in new[] { "Clean", "NoTax", "TipDiscount", "MultiPagePdf", "Blurry", "OverLimit", "DateFlag" })
        {
            Assert.Contains($"| {group} |", markdown, StringComparison.Ordinal);
        }

        Assert.Equal(7, report.Groups.Count);
    }

    [Fact]
    public async Task Every_wrong_field_is_listed_with_the_expected_and_the_actual_value()
    {
        var target = set.Answers.Receipts.First(r => r.Date is not null);

        var (report, _) = await RunAsync(e => e.Spoil = (answer, result) => answer.File == target.File
            ? result with { Total = answer.Total + 0.01m, Merchant = "Wrong Name", Date = null }
            : result);

        var markdown = report.ToMarkdown();
        Assert.Contains($"- {target.File} ({target.Group}): total expected {target.Total:F2}, got {target.Total + 0.01m:F2}", markdown, StringComparison.Ordinal);
        Assert.Contains($"merchant expected \"{target.Merchant}\", got \"Wrong Name\"", markdown, StringComparison.Ordinal);
        Assert.Contains($"date expected {target.Date:yyyy-MM-dd}, got none", markdown, StringComparison.Ordinal);
        Assert.Contains("**PASS**", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fail_is_shown_as_a_fail_in_the_report()
    {
        var (report, _) = await RunAsync(e => e.Spoil = (_, result) => result with { Total = null });

        Assert.False(report.Passed);
        Assert.Contains("**FAIL**", report.ToMarkdown(), StringComparison.Ordinal);
        Assert.Contains("| Total | 0/30 (0.0%) | at least 90% | FAIL |", report.ToMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_report_is_also_written_to_the_file_asked_for()
    {
        var path = Path.Combine(set.Folder, "report-under-test.md");

        var (report, _) = await RunAsync(reportPath: path);

        Assert.Equal(report.ToMarkdown(), await File.ReadAllTextAsync(path, Token));
        File.Delete(path);
    }

    [Fact]
    public async Task A_missing_answer_file_says_to_make_the_test_set_first()
    {
        var empty = Path.Combine(Path.GetTempPath(), "stubby-eval-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var handler = new RunEvalCommandHandler(new AnswerKeyExtractor(set.Folder, set.Answers));

            var failure = await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                await handler.Handle(new RunEvalCommand(new RunEvalOptions(empty, 3000m)), Token));

            Assert.Contains("generate", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    [Fact]
    public async Task Cancelling_stops_the_run_and_is_not_scored_as_a_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new RunEvalCommandHandler(new AnswerKeyExtractor(set.Folder, set.Answers));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await handler.Handle(new RunEvalCommand(new RunEvalOptions(set.Folder, 3000m)), cancelled.Token));
    }

    [Fact]
    public async Task The_model_comes_from_the_options_when_no_receipt_could_be_read()
    {
        var (report, _) = await RunAsync(e =>
        {
            foreach (var answer in set.Answers.Receipts)
            {
                e.Failing.Add(answer.File);
            }
        });

        Assert.Equal("fake-model:1b", report.Model);
        Assert.Equal("unknown", report.PromptVersion);
        Assert.All(report.Results, r => Assert.True(r.Failed));
        Assert.False(report.Passed);
    }

    [Fact]
    public async Task The_flag_coverage_uses_the_configured_limit_and_the_stored_today()
    {
        // The over-limit receipt read correctly is not a wrong total, so it adds nothing. Read its total as 100 instead,
        // under the limit and with no subtotal or tax to contradict it: wrong and not flagged.
        var target = set.Answers.Receipts.Single(r => r.Group == "OverLimit").File;

        var (report, _) = await RunAsync(e => e.Spoil = (answer, result) =>
            answer.File == target ? result with { Total = 100m, Subtotal = null, Tax = null } : result);

        Assert.Equal((0, 1), (report.FlagCoverage.Correct, report.FlagCoverage.Count));

        // With a limit of 50 the same wrong reading is at or above it, so it is flagged and covered.
        var covered = await new RunEvalCommandHandler(new AnswerKeyExtractor(set.Folder, set.Answers) { Spoil = (answer, result) =>
                answer.File == target ? result with { Total = 100m, Subtotal = null, Tax = null } : result })
            .Handle(new RunEvalCommand(new RunEvalOptions(set.Folder, FlagTotalLimit: 50m)), Token);

        Assert.Equal((1, 1), (covered.FlagCoverage.Correct, covered.FlagCoverage.Count));
        Assert.True(covered.Results.Single(r => r.Expected.File == target).Flagged);
    }
}
