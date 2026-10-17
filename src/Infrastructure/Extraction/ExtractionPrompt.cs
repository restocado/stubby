namespace Stubby.Infrastructure.Extraction;

/// <summary>
/// The prompt sent with every receipt. Change the text only together with <see cref="Version"/>: it is stored
/// with each receipt, so a result can be traced to the wording that produced it. See docs/extraction-prompt.md.
/// </summary>
internal static class ExtractionPrompt
{
    public const string Version = "v1";

    // Joined with "\n" from lines, so the text does not depend on the line endings of this file.
    public static readonly string Text = string.Join(
        "\n",
        "You are reading a receipt. The attached images are the pages of one receipt, in order.",
        "",
        "Reply with a single JSON object and nothing else. Do not use markdown. Use exactly these keys:",
        "{\"merchant\": string or null, \"date\": string or null, \"total\": number or null, \"subtotal\": number or null, \"tax\": number or null}",
        "",
        "Rules:",
        "- merchant: the name of the shop or business, as printed on the receipt.",
        "- date: the purchase date as YYYY-MM-DD. If the order of day and month is ambiguous, use null.",
        "- total: the final amount paid, including tax and any tip, after any discount. If several lines look like totals, choose the amount actually paid.",
        "- subtotal: the amount before tax, only if it is printed on the receipt.",
        "- tax: the total tax, only if it is printed on the receipt.",
        "- Numbers use a dot as the decimal separator and have no currency symbol and no thousands separator.",
        "- Use null for anything that is not on the receipt or that you cannot read. Never guess and never invent a value.");
}
