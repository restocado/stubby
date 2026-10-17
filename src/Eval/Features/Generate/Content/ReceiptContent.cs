namespace Stubby.Eval.Features.Generate.Content;

internal enum SummaryKind
{
    Subtotal,
    Tax,
    Tip,
    Discount,

    /// <summary>The amount actually paid. There is exactly one on a receipt.</summary>
    Total,

    /// <summary>A line that looks like a total but is not: savings, the amount handed over, the change.</summary>
    Other,
}

/// <param name="Detail">A second line under the item, such as "2 x 3.50", or null.</param>
internal sealed record LineItem(string Name, decimal Amount, string? Detail = null);

internal sealed record SummaryLine(string Label, decimal Amount, SummaryKind Kind);

/// <summary>Everything a layout prints. Every name, address and number in it is invented.</summary>
/// <param name="DateText">The date as it is printed, or null when the receipt shows no date.</param>
internal sealed record ReceiptContent(
    string Merchant,
    IReadOnlyList<string> HeaderLines,
    string? DateText,
    string TimeText,
    string ReceiptNumber,
    IReadOnlyList<LineItem> Items,
    IReadOnlyList<SummaryLine> Summary,
    IReadOnlyList<string> FooterLines,
    bool ThousandsSeparators);
