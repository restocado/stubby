using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using Stubby.Domain.Receipts.Flagging;
using Stubby.Eval.Answers;
using Stubby.Eval.Features.Generate;
using Stubby.Eval.Features.Generate.Content;
using Stubby.Infrastructure.Pdf;

namespace Stubby.Eval.Tests;

/// <summary>Generates the set once, into a temporary folder, for the tests that only read it.</summary>
public sealed class GeneratedSet : IDisposable
{
    public GeneratedSet()
    {
        Folder = Path.Combine(Path.GetTempPath(), "stubby-eval-" + Guid.NewGuid().ToString("N"));
        new GenerateTestSetCommandHandler().Handle(new GenerateTestSetCommand(new GenerateOptions(Folder)));
        Answers = AnswerFile.Parse(File.ReadAllText(Path.Combine(Folder, "answers.json")));
    }

    public string Folder { get; }

    public AnswerFile Answers { get; }

    public IEnumerable<string> ReceiptFiles => Directory.EnumerateFiles(Folder).Where(f => Path.GetFileName(f) != "answers.json");

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}

public class GeneratorTests(GeneratedSet set) : IClassFixture<GeneratedSet>
{
    private static readonly PdfToImageRenderer Renderer = new(PdfToImageRenderer.DefaultDpi);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void There_are_exactly_30_receipt_files_and_one_answer_file()
    {
        Assert.Equal(30, set.ReceiptFiles.Count());
        Assert.Equal(31, Directory.GetFiles(set.Folder).Length);
        Assert.Equal(30, set.Answers.Receipts.Count);
    }

    [Fact]
    public void The_seven_groups_have_the_specified_counts()
    {
        var counts = set.Answers.Receipts.GroupBy(r => r.Group).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(
            new Dictionary<string, int>
            {
                ["Clean"] = 14, ["NoTax"] = 4, ["TipDiscount"] = 4, ["MultiPagePdf"] = 2, ["Blurry"] = 4, ["OverLimit"] = 1, ["DateFlag"] = 1,
            },
            counts);
    }

    [Fact]
    public void About_twenty_are_pictures_and_about_ten_are_pdfs()
    {
        var pictures = set.ReceiptFiles.Count(f => f.EndsWith(".png", StringComparison.Ordinal) || f.EndsWith(".jpg", StringComparison.Ordinal));
        var pdfs = set.ReceiptFiles.Count(f => f.EndsWith(".pdf", StringComparison.Ordinal));

        Assert.InRange(pictures, 18, 22);
        Assert.InRange(pdfs, 8, 12);
        Assert.Equal(30, pictures + pdfs);
    }

    [Fact]
    public void Every_file_is_in_the_answer_file_and_every_answer_has_its_file()
    {
        var files = set.ReceiptFiles.Select(path => new FileInfo(path).Name).Order().ToList();
        var answered = set.Answers.Receipts.Select(r => r.File).Order().ToList();

        Assert.Equal(files, answered);
        Assert.Equal(30, answered.Distinct().Count());
    }

    [Fact]
    public void The_clean_group_uses_six_distinct_layouts()
    {
        var layouts = set.Answers.Receipts.Where(r => r.Group == "Clean").Select(r => r.Layout).Distinct().ToList();

        Assert.Equal(6, layouts.Count);
    }

    [Fact]
    public void Every_picture_decodes_and_every_png_and_jpeg_starts_with_its_signature()
    {
        foreach (var path in set.ReceiptFiles.Where(f => !f.EndsWith(".pdf", StringComparison.Ordinal)))
        {
            var bytes = File.ReadAllBytes(path);
            var isPng = path.EndsWith(".png", StringComparison.Ordinal);
            Assert.Equal(isPng ? [0x89, 0x50, 0x4E, 0x47] : [0xFF, 0xD8, 0xFF], bytes.Take(isPng ? 4 : 3));

            using var bitmap = SKBitmap.Decode(bytes);
            Assert.NotNull(bitmap);
            Assert.InRange(bitmap.Width, 300, 800);
            Assert.InRange(bitmap.Height, 250, 1200);
        }
    }

    [Fact]
    public async Task Every_pdf_is_valid_and_renders_and_the_two_long_ones_have_two_and_three_pages()
    {
        var pageCounts = new Dictionary<string, int>();
        foreach (var path in set.ReceiptFiles.Where(f => f.EndsWith(".pdf", StringComparison.Ordinal)))
        {
            var pages = await Renderer.RenderAsync(await File.ReadAllBytesAsync(path, Token), Token);
            Assert.All(pages, page => Assert.Equal([0x89, 0x50, 0x4E, 0x47], page.Take(4)));
            pageCounts[Path.GetFileName(path)] = pages.Count;
        }

        Assert.Equal(10, pageCounts.Count);
        var multiPage = set.Answers.Receipts.Where(r => r.Group == "MultiPagePdf").Select(r => pageCounts[r.File]).Order().ToList();
        Assert.Equal([2, 3], multiPage);
        Assert.Equal(8, pageCounts.Where(p => !p.Key.Contains("multipagepdf", StringComparison.Ordinal)).Count(p => p.Value == 1));
    }

    [Fact]
    public void Every_pdf_has_a_cross_reference_table_whose_offsets_point_at_their_objects()
    {
        foreach (var path in set.ReceiptFiles.Where(f => f.EndsWith(".pdf", StringComparison.Ordinal)))
        {
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
            var start = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF").Groups[1].Value);
            Assert.StartsWith("xref", text[start..], StringComparison.Ordinal);

            var entries = Regex.Matches(text[start..], @"(?m)^(\d{10}) (\d{5}) ([nf]) \n");
            Assert.True(entries.Count > 3);
            for (var i = 1; i < entries.Count; i++)
            {
                var offset = int.Parse(entries[i].Groups[1].Value);
                Assert.StartsWith($"{i} 0 obj", text[offset..], StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void A_receipt_with_tax_has_a_subtotal_and_tax_that_add_up_to_the_total_except_where_a_tip_or_discount_is_built_in()
    {
        foreach (var answer in set.Answers.Receipts.Where(r => r.Subtotal is not null && r.Tax is not null))
        {
            var adds = answer.Subtotal + answer.Tax == answer.Total;
            var builtToMismatch = answer.Group == "TipDiscount" && answer.File.Contains("restaurant", StringComparison.Ordinal)
                || answer.File.Contains("tipdiscount-grocery", StringComparison.Ordinal);

            Assert.Equal(!builtToMismatch, adds);
        }
    }

    [Fact]
    public void The_no_tax_group_shows_neither_a_subtotal_nor_a_tax()
    {
        Assert.All(set.Answers.Receipts.Where(r => r.Group == "NoTax"), r =>
        {
            Assert.Null(r.Subtotal);
            Assert.Null(r.Tax);
        });
    }

    [Fact]
    public void The_over_limit_receipt_is_at_or_above_the_limit_and_expected_to_be_flagged()
    {
        var answer = Assert.Single(set.Answers.Receipts, r => r.Group == "OverLimit");

        Assert.True(answer.Total >= set.Answers.TotalLimit);
        Assert.True(answer.ExpectedFlagged);
        Assert.Contains("TotalAtOrAboveLimit", answer.ExpectedFlagReasons);
    }

    [Fact]
    public void The_date_receipt_has_no_date_and_is_expected_to_be_flagged()
    {
        var answer = Assert.Single(set.Answers.Receipts, r => r.Group == "DateFlag");

        Assert.Null(answer.Date);
        Assert.True(answer.ExpectedFlagged);
        Assert.Contains("DateMissing", answer.ExpectedFlagReasons);
    }

    [Fact]
    public void Receipts_built_to_be_fine_are_expected_to_be_unflagged_and_the_flags_are_the_real_rules()
    {
        foreach (var answer in set.Answers.Receipts)
        {
            var flags = ReceiptFlagEvaluator.Evaluate(
                answer.Total, answer.Subtotal, answer.Tax, answer.Date, set.Answers.TotalLimit, set.Answers.Today);

            Assert.Equal(flags.IsFlagged, answer.ExpectedFlagged);
            Assert.Equal(flags.Reasons.Select(r => r.ToString()), answer.ExpectedFlagReasons);
            if (answer.Group is "Clean" or "NoTax" or "Blurry" or "MultiPagePdf")
            {
                Assert.False(answer.ExpectedFlagged, $"{answer.File} should not be flagged.");
            }
        }
    }

    [Fact]
    public void Tips_and_discounts_are_flagged_as_a_subtotal_and_tax_mismatch_as_the_flag_rule_says()
    {
        var tip = Assert.Single(set.Answers.Receipts, r => r.File.Contains("tipdiscount-restaurant", StringComparison.Ordinal));
        var discount = Assert.Single(set.Answers.Receipts, r => r.File.Contains("tipdiscount-grocery", StringComparison.Ordinal));

        Assert.Equal([nameof(FlagReason.SubtotalTaxMismatch)], tip.ExpectedFlagReasons);
        Assert.Equal([nameof(FlagReason.SubtotalTaxMismatch)], discount.ExpectedFlagReasons);
    }

    [Fact]
    public void Dates_are_in_the_past_within_a_year_and_never_ambiguous()
    {
        foreach (var receipt in TestSetPlan.Create(GenerateOptions.DefaultSeed, GenerateOptions.DefaultToday))
        {
            if (receipt.Date is not { } date)
            {
                Assert.Null(receipt.Content.DateText);
                continue;
            }

            Assert.True(date < GenerateOptions.DefaultToday, $"{receipt.FileName} is dated {date}.");
            Assert.True(date >= GenerateOptions.DefaultToday.AddYears(-1));

            // A number such as 03/09/2026 could be day first or month first, so a numeric date has a day over 12.
            var numeric = Regex.Match(receipt.Content.DateText!, @"^(\d{2})[./](\d{2})[./]\d{4}$");
            if (numeric.Success)
            {
                Assert.True(
                    int.Parse(numeric.Groups[1].Value) > 12 || int.Parse(numeric.Groups[2].Value) > 12,
                    $"{receipt.Content.DateText} is ambiguous.");
            }
        }
    }

    [Fact]
    public void The_answer_file_is_utf8_without_a_byte_order_mark_with_lf_line_ends_and_holds_the_fixed_today()
    {
        var bytes = File.ReadAllBytes(Path.Combine(set.Folder, "answers.json"));
        var text = Encoding.UTF8.GetString(bytes);

        Assert.NotEqual([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 10, 17), set.Answers.Today);
        Assert.Equal(3000m, set.Answers.TotalLimit);
        Assert.Equal(GenerateOptions.DefaultSeed, set.Answers.Seed);
        Assert.Equal(set.Answers.ToJson(), text);
    }

    [Fact]
    public void Nothing_that_could_be_real_is_in_the_set()
    {
        var plan = TestSetPlan.Create(GenerateOptions.DefaultSeed, GenerateOptions.DefaultToday);
        var json = File.ReadAllText(Path.Combine(set.Folder, "answers.json"));

        // Telephone numbers are in the 555-01xx range that is kept for fiction, and no long run of digits could be a
        // card or an account number.
        Assert.All(plan, r => Assert.Matches(@"^Tel 555-01\d\d$", r.Content.HeaderLines[1]));
        Assert.DoesNotMatch(@"\d{13,}", json);
        Assert.DoesNotMatch(@"@|https?://|www\.", json);
        Assert.All(plan, r => Assert.DoesNotMatch(@"\d{9,}", string.Join(' ', r.Content.HeaderLines.Append(r.Content.ReceiptNumber))));
    }

    [Fact]
    public void Money_in_the_answer_file_is_exact_to_two_decimals()
    {
        Assert.All(set.Answers.Receipts, r =>
        {
            Assert.Equal(decimal.Round(r.Total, 2), r.Total);
            Assert.True(r.Total > 0);
        });
    }
}

public class GeneratorDeterminismTests
{
    private static string Generate(int seed)
    {
        var folder = Path.Combine(Path.GetTempPath(), "stubby-eval-" + Guid.NewGuid().ToString("N"));
        new GenerateTestSetCommandHandler().Handle(new GenerateTestSetCommand(new GenerateOptions(folder, seed)));
        return folder;
    }

    private static Dictionary<string, byte[]> Read(string folder) =>
        Directory.GetFiles(folder).ToDictionary(path => new FileInfo(path).Name, File.ReadAllBytes);

    [Fact]
    public void The_same_seed_gives_identical_files_and_an_identical_answer_file()
    {
        var first = Generate(GenerateOptions.DefaultSeed);
        var second = Generate(GenerateOptions.DefaultSeed);
        try
        {
            var a = Read(first);
            var b = Read(second);

            Assert.Equal(a.Keys.Order(), b.Keys.Order());
            Assert.Equal(31, a.Count);
            foreach (var name in a.Keys)
            {
                Assert.True(a[name].AsSpan().SequenceEqual(b[name]), $"{name} differs between two runs with the same seed.");
            }
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void A_different_seed_gives_different_values()
    {
        var first = Generate(1);
        var second = Generate(2);
        try
        {
            var a = AnswerFile.Parse(File.ReadAllText(Path.Combine(first, "answers.json")));
            var b = AnswerFile.Parse(File.ReadAllText(Path.Combine(second, "answers.json")));

            Assert.NotEqual(a.Receipts.Select(r => r.Total), b.Receipts.Select(r => r.Total));
            Assert.NotEqual(a.Receipts.Select(r => r.Date), b.Receipts.Select(r => r.Date));
            Assert.Equal(30, b.Receipts.Count);
        }
        finally
        {
            Directory.Delete(first, recursive: true);
            Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void Generating_again_replaces_the_earlier_files_and_leaves_other_files_alone()
    {
        var folder = Generate(GenerateOptions.DefaultSeed);
        try
        {
            File.WriteAllText(Path.Combine(folder, "99-stale-thing.png"), "left over from an earlier layout");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");

            new GenerateTestSetCommandHandler().Handle(new GenerateTestSetCommand(new GenerateOptions(folder)));

            Assert.False(File.Exists(Path.Combine(folder, "99-stale-thing.png")));
            Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
            Assert.Equal(32, Directory.GetFiles(folder).Length);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
