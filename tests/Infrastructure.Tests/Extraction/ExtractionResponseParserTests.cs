using Stubby.Application.Abstractions;
using Stubby.Infrastructure.Extraction;

namespace Stubby.Infrastructure.Tests.Extraction;

public class ExtractionResponseParserTests
{
    private const string FullReply =
        """{"merchant": "Lantern Noodle House", "date": "2026-10-02", "total": 32.62, "subtotal": 30.50, "tax": 2.12}""";

    [Fact]
    public void Full_reply_gives_every_field()
    {
        var parsed = ExtractionResponseParser.Parse(FullReply);

        Assert.Equal("Lantern Noodle House", parsed.Merchant);
        Assert.Equal(new DateOnly(2026, 10, 2), parsed.Date);
        Assert.Equal(32.62m, parsed.Total);
        Assert.Equal(30.50m, parsed.Subtotal);
        Assert.Equal(2.12m, parsed.Tax);
    }

    [Fact]
    public void Reply_in_a_markdown_fence_is_read()
    {
        var parsed = ExtractionResponseParser.Parse($"```json\n{FullReply}\n```");

        Assert.Equal(32.62m, parsed.Total);
    }

    [Fact]
    public void Reply_with_text_around_the_json_is_read()
    {
        var parsed = ExtractionResponseParser.Parse($"Here is the receipt:\n{FullReply}\nLet me know if you need more.");

        Assert.Equal("Lantern Noodle House", parsed.Merchant);
    }

    [Fact]
    public void Reply_without_a_date_has_an_empty_date_and_no_exception()
    {
        var parsed = ExtractionResponseParser.Parse("""{"merchant": "Cafe", "date": null, "total": 5.00}""");

        Assert.Null(parsed.Date);
        Assert.Equal(5.00m, parsed.Total);
    }

    [Theory]
    [InlineData("32/13/2026")]
    [InlineData("03/09/2026")]
    [InlineData("yesterday")]
    [InlineData("2026-02-30")]
    [InlineData("2026-10-02T10:00:00")]
    [InlineData("")]
    public void Date_that_is_not_yyyy_mm_dd_is_missing(string date)
    {
        var parsed = ExtractionResponseParser.Parse($$"""{"merchant": "Cafe", "date": "{{date}}", "total": 5.00}""");

        Assert.Null(parsed.Date);
    }

    [Fact]
    public void Missing_keys_are_missing_fields()
    {
        var parsed = ExtractionResponseParser.Parse("""{"merchant": "Cafe"}""");

        Assert.Equal("Cafe", parsed.Merchant);
        Assert.Null(parsed.Date);
        Assert.Null(parsed.Total);
        Assert.Null(parsed.Subtotal);
        Assert.Null(parsed.Tax);
    }

    [Theory]
    [InlineData("19.99", 19.99)]
    [InlineData("0.00", 0.00)]
    [InlineData("0", 0)]
    [InlineData("-5.00", -5.00)]
    [InlineData("1234567.89", 1234567.89)]
    public void Numbers_parse_to_exact_decimals(string json, double expected)
    {
        var parsed = ExtractionResponseParser.Parse($$"""{"merchant": "Cafe", "total": {{json}}}""");

        Assert.Equal((decimal)expected, parsed.Total);
    }

    [Fact]
    public void Nineteen_ninety_nine_is_exact_and_not_a_binary_approximation()
    {
        var parsed = ExtractionResponseParser.Parse("""{"merchant": "Cafe", "total": 19.99}""");

        Assert.Equal("19.99", parsed.Total!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("19.99", 19.99)]
    [InlineData("$19.99", 19.99)]
    [InlineData("1,234.50", 1234.50)]
    [InlineData("32.62 USD", 32.62)]
    [InlineData(" 7 ", 7)]
    public void Money_in_text_is_read_with_the_invariant_culture(string text, double expected)
    {
        var parsed = ExtractionResponseParser.Parse($$"""{"merchant": "Cafe", "total": "{{text}}"}""");

        Assert.Equal((decimal)expected, parsed.Total);
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("\"\"")]
    [InlineData("\"1.234,50\"")]
    [InlineData("true")]
    [InlineData("[1, 2]")]
    [InlineData("{\"amount\": 5}")]
    public void Money_that_cannot_be_read_is_missing(string json)
    {
        var parsed = ExtractionResponseParser.Parse($$"""{"merchant": "Cafe", "total": {{json}}}""");

        Assert.Null(parsed.Total);
    }

    [Fact]
    public void More_than_two_decimals_are_rounded_to_two()
    {
        var parsed = ExtractionResponseParser.Parse("""{"merchant": "Cafe", "total": 12.345, "tax": 1.004}""");

        Assert.Equal(12.35m, parsed.Total);
        Assert.Equal(1.00m, parsed.Tax);
    }

    [Fact]
    public void Amount_too_large_for_the_database_is_missing()
    {
        var parsed = ExtractionResponseParser.Parse("""{"merchant": "Cafe", "total": 99999999999999.99}""");

        Assert.Null(parsed.Total);
    }

    [Fact]
    public void Keys_are_not_case_sensitive_and_unknown_keys_are_ignored()
    {
        var parsed = ExtractionResponseParser.Parse("""{"Merchant": "Cafe", "TOTAL": 5, "currency": "USD", "notes": "x"}""");

        Assert.Equal("Cafe", parsed.Merchant);
        Assert.Equal(5m, parsed.Total);
    }

    [Fact]
    public void Merchant_is_trimmed_and_an_empty_one_is_missing()
    {
        Assert.Equal("Cafe", ExtractionResponseParser.Parse("""{"merchant": "  Cafe  ", "total": 5}""").Merchant);
        Assert.Null(ExtractionResponseParser.Parse("""{"merchant": "   ", "total": 5}""").Merchant);
    }

    [Fact]
    public void Trailing_commas_and_comments_are_tolerated()
    {
        var parsed = ExtractionResponseParser.Parse("{\"merchant\": \"Cafe\", // the shop\n \"total\": 5,}");

        Assert.Equal(5m, parsed.Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I cannot read this receipt.")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"merchant\": ")]
    [InlineData("{not json}")]
    public void Reply_that_is_not_a_json_object_fails(string reply)
    {
        Assert.Throws<ExtractionException>(() => ExtractionResponseParser.Parse(reply));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"merchant": null, "date": null, "total": null}""")]
    [InlineData("""{"error": "could not read the image"}""")]
    [InlineData("""{"merchant": "", "date": "soon", "total": "n/a", "subtotal": 4, "tax": 1}""")]
    public void Reply_with_no_usable_merchant_date_or_total_fails(string reply)
    {
        Assert.Throws<ExtractionException>(() => ExtractionResponseParser.Parse(reply));
    }

    [Fact]
    public void Failure_message_never_holds_the_reply()
    {
        const string secret = "SECRET-RECEIPT-CONTENT";

        var failure = Assert.Throws<ExtractionException>(
            () => ExtractionResponseParser.Parse($$"""{"merchant": null, "note": "{{secret}}"}"""));
        var invalid = Assert.Throws<ExtractionException>(
            () => ExtractionResponseParser.Parse($$"""{"merchant": "{{secret}}", """));

        Assert.DoesNotContain(secret, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_usable_field_is_enough()
    {
        Assert.Equal("Cafe", ExtractionResponseParser.Parse("""{"merchant": "Cafe"}""").Merchant);
        Assert.Equal(new DateOnly(2026, 1, 2), ExtractionResponseParser.Parse("""{"date": "2026-01-02"}""").Date);
        Assert.Equal(5m, ExtractionResponseParser.Parse("""{"total": 5}""").Total);
    }
}
