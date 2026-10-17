using SkiaSharp;
using Stubby.Eval.Features.Generate.Content;

namespace Stubby.Eval.Features.Generate.Layouts;

/// <summary>Draws a receipt as one bitmap for each page. Each layout has its own look, fonts and arrangement.</summary>
internal abstract class ReceiptLayout
{
    public static ReceiptLayout For(LayoutKind kind) => kind switch
    {
        LayoutKind.Thermal => new ThermalLayout(),
        LayoutKind.Cafe => new CafeLayout(),
        LayoutKind.Grocery => new GroceryLayout(),
        LayoutKind.Invoice => new InvoiceLayout(),
        LayoutKind.Restaurant => new RestaurantLayout(),
        _ => new FuelLayout(),
    };

    /// <summary>
    /// The items are split over pages, <paramref name="itemsPerPage"/> to a page. The header is on the first page and
    /// the summary on the last, as on a long paper receipt.
    /// </summary>
    public IReadOnlyList<SKBitmap> Render(ReceiptContent content, int itemsPerPage, int seed)
    {
        var chunks = content.Items.Chunk(itemsPerPage).ToList();
        var pages = new List<SKBitmap>();
        for (var i = 0; i < chunks.Count; i++)
        {
            pages.Add(DrawPage(content, chunks[i], i, chunks.Count, new Random(seed + i)));
        }

        return pages;
    }

    protected abstract SKBitmap DrawPage(ReceiptContent content, LineItem[] items, int pageIndex, int pageCount, Random random);

    protected static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    protected static string Amount(ReceiptContent content, decimal amount) => Sheet.Money(amount, content.ThousandsSeparators);
}

/// <summary>A narrow till roll in a typewriter font, centred header, dashed rules.</summary>
internal sealed class ThermalLayout : ReceiptLayout
{
    protected override SKBitmap DrawPage(ReceiptContent content, LineItem[] items, int pageIndex, int pageCount, Random random)
    {
        using var sheet = new Sheet(380, 22, paper: new SKColor(253, 252, 248));
        var title = Fonts.Mono(22, bold: true);
        var body = Fonts.Mono(14);
        var small = Fonts.Mono(12);
        var big = Fonts.Mono(18, bold: true);

        if (pageIndex == 0)
        {
            sheet.Text(content.Merchant.ToUpperInvariant(), title, Align.Center);
            content.HeaderLines.ToList().ForEach(line => sheet.Text(line, body, Align.Center));
            sheet.Gap(8);
            sheet.Columns(content.DateText ?? string.Empty, content.TimeText, body);
            sheet.Text($"RECEIPT #{content.ReceiptNumber}", body);
        }
        else
        {
            sheet.Text($"(continued) PAGE {pageIndex + 1} OF {pageCount}", body, Align.Center);
        }

        sheet.Rule(dashed: true);
        foreach (var item in items)
        {
            sheet.Columns(Truncate(item.Name, 24), Amount(content, item.Amount), body);
            if (item.Detail is not null)
            {
                sheet.Text("  " + item.Detail, small);
            }
        }

        sheet.Rule(dashed: true);
        if (pageIndex < pageCount - 1)
        {
            sheet.Text("CONTINUED ON NEXT PAGE", body, Align.Center);
            return sheet.ToBitmap();
        }

        foreach (var line in content.Summary)
        {
            sheet.Columns(line.Label, Amount(content, line.Amount), line.Kind == SummaryKind.Total ? big : body);
        }

        sheet.Gap(10);
        content.FooterLines.ToList().ForEach(line => sheet.Text(line, body, Align.Center));
        return sheet.ToBitmap();
    }
}

/// <summary>A modern café slip in a clean sans-serif, left aligned, with a large shop name.</summary>
internal sealed class CafeLayout : ReceiptLayout
{
    protected override SKBitmap DrawPage(ReceiptContent content, LineItem[] items, int pageIndex, int pageCount, Random random)
    {
        using var sheet = new Sheet(440, 30);
        var title = Fonts.Sans(30, bold: true);
        var body = Fonts.Sans(16);
        var small = Fonts.Sans(13);
        var total = Fonts.Sans(22, bold: true);

        if (pageIndex == 0)
        {
            sheet.Text(content.Merchant, title);
            content.HeaderLines.ToList().ForEach(line => sheet.Text(line, small));
            sheet.Gap(10);
            sheet.Columns($"Order {content.ReceiptNumber}", content.DateText is null ? content.TimeText : $"{content.DateText}  {content.TimeText}", small);
        }
        else
        {
            sheet.Text($"{content.Merchant} - page {pageIndex + 1} of {pageCount}", small);
        }

        sheet.Rule();
        foreach (var item in items)
        {
            sheet.Columns(item.Name, Amount(content, item.Amount), body);
            if (item.Detail is not null)
            {
                sheet.Text("   " + item.Detail, small);
            }
        }

        sheet.Rule();
        if (pageIndex < pageCount - 1)
        {
            sheet.Text("continued on the next page", small);
            return sheet.ToBitmap();
        }

        foreach (var line in content.Summary)
        {
            if (line.Kind == SummaryKind.Total)
            {
                sheet.Gap(6);
                sheet.Columns(line.Label, Amount(content, line.Amount), total);
            }
            else
            {
                sheet.Columns(line.Label, Amount(content, line.Amount), body);
            }
        }

        sheet.Gap(14);
        content.FooterLines.ToList().ForEach(line => sheet.Text(line, small, Align.Center));
        return sheet.ToBitmap();
    }
}

/// <summary>A supermarket till roll: capitals, a store number, item counts and a barcode.</summary>
internal sealed class GroceryLayout : ReceiptLayout
{
    protected override SKBitmap DrawPage(ReceiptContent content, LineItem[] items, int pageIndex, int pageCount, Random random)
    {
        using var sheet = new Sheet(400, 20);
        var title = Fonts.Mono(18, bold: true);
        var body = Fonts.Mono(14);
        var small = Fonts.Mono(12);
        var bold = Fonts.Mono(16, bold: true);

        if (pageIndex == 0)
        {
            sheet.Text(content.Merchant.ToUpperInvariant(), title, Align.Center);
            sheet.Text($"STORE {content.ReceiptNumber[..2]}   {string.Join("  ", content.HeaderLines)}", small, Align.Center);
            sheet.Gap(4);
            sheet.Text(content.DateText is null ? content.TimeText : $"{content.DateText} {content.TimeText}", body, Align.Center);
        }
        else
        {
            sheet.Text($"{content.Merchant.ToUpperInvariant()}  {pageIndex + 1}/{pageCount}", body, Align.Center);
        }

        sheet.Rule(2);
        foreach (var item in items)
        {
            sheet.Columns(Truncate(item.Name.ToUpperInvariant(), 26), Amount(content, item.Amount), body, leading: 1.25f);
            if (item.Detail is not null)
            {
                sheet.Text("   " + item.Detail.Replace('x', '@'), small, leading: 1.25f);
            }
        }

        sheet.Rule(2);
        if (pageIndex < pageCount - 1)
        {
            sheet.Text($"-- PAGE {pageIndex + 1} OF {pageCount} --", body, Align.Center);
            return sheet.ToBitmap();
        }

        foreach (var line in content.Summary)
        {
            sheet.Columns(line.Label, Amount(content, line.Amount), line.Kind == SummaryKind.Total ? bold : body);
        }

        sheet.Gap(6);
        sheet.Text($"ITEMS SOLD  {content.Items.Count}", small, Align.Center);
        sheet.Gap(6);
        sheet.Bars(46, random);
        sheet.Text(content.FooterLines[0], body, Align.Center);
        return sheet.ToBitmap();
    }
}

/// <summary>A wide serif invoice: details left and right, a table, and a boxed total.</summary>
internal sealed class InvoiceLayout : ReceiptLayout
{
    protected override SKBitmap DrawPage(ReceiptContent content, LineItem[] items, int pageIndex, int pageCount, Random random)
    {
        using var sheet = new Sheet(600, 44);
        var title = Fonts.Serif(24, bold: true);
        var body = Fonts.Serif(16);
        var small = Fonts.Serif(14);
        var bold = Fonts.Serif(16, bold: true);
        var total = Fonts.Serif(22, bold: true);

        sheet.TextAt(content.Merchant, title, sheet.Left);
        sheet.TextAt("RECEIPT", title, sheet.Right, Align.Right);
        sheet.NextLine(title, 1.5f);

        if (pageIndex == 0)
        {
            sheet.TextAt(content.HeaderLines[0], small, sheet.Left);
            sheet.TextAt($"No. {content.ReceiptNumber}", small, sheet.Right, Align.Right);
            sheet.NextLine(small);
            sheet.TextAt(content.HeaderLines[1], small, sheet.Left);
            if (content.DateText is not null)
            {
                sheet.TextAt($"Date: {content.DateText}", small, sheet.Right, Align.Right);
            }

            sheet.NextLine(small);
        }
        else
        {
            sheet.TextAt($"Page {pageIndex + 1} of {pageCount}", small, sheet.Right, Align.Right);
            sheet.NextLine(small);
        }

        sheet.Gap(10);
        sheet.TextAt("Description", bold, sheet.Left);
        sheet.TextAt("Amount", bold, sheet.Right, Align.Right);
        sheet.NextLine(bold);
        sheet.Rule(1.5f);
        foreach (var item in items)
        {
            sheet.Columns(item.Name, Amount(content, item.Amount), body);
            if (item.Detail is not null)
            {
                sheet.Text("    " + item.Detail, small);
            }
        }

        sheet.Rule(1.5f);
        if (pageIndex < pageCount - 1)
        {
            sheet.Text("Continued on the next page", small, Align.Right);
            return sheet.ToBitmap();
        }

        // Wide enough for the longest label beside a four-digit amount.
        const float blockWidth = 340;
        foreach (var line in content.Summary)
        {
            if (line.Kind == SummaryKind.Total)
            {
                sheet.Gap(6);
                var top = sheet.Y;
                sheet.TextAt(line.Label, total, sheet.Right - blockWidth + 10);
                sheet.TextAt(Amount(content, line.Amount), total, sheet.Right - 10, Align.Right);
                sheet.NextLine(total, 1.5f);
                sheet.Box(sheet.Right - blockWidth, top, blockWidth, sheet.Y - top);
            }
            else
            {
                sheet.TextAt(line.Label, body, sheet.Right - blockWidth + 10);
                sheet.TextAt(Amount(content, line.Amount), body, sheet.Right - 10, Align.Right);
                sheet.NextLine(body);
            }
        }

        sheet.Gap(16);
        sheet.Text(content.FooterLines[0], small);
        return sheet.ToBitmap();
    }
}

/// <summary>A restaurant bill: centred, with a table and a cover count.</summary>
internal sealed class RestaurantLayout : ReceiptLayout
{
    protected override SKBitmap DrawPage(ReceiptContent content, LineItem[] items, int pageIndex, int pageCount, Random random)
    {
        using var sheet = new Sheet(400, 28);
        var title = Fonts.Sans(26, bold: true);
        var body = Fonts.Sans(16);
        var small = Fonts.Sans(13);
        var total = Fonts.Sans(21, bold: true);

        if (pageIndex == 0)
        {
            sheet.Text(content.Merchant, title, Align.Center);
            content.HeaderLines.ToList().ForEach(line => sheet.Text(line, small, Align.Center));
            sheet.Gap(8);
            sheet.Text($"Table {int.Parse(content.ReceiptNumber[..2]) % 20 + 1}   Guests {int.Parse(content.ReceiptNumber[2..]) % 6 + 1}", body, Align.Center);
            sheet.Text(content.DateText is null ? content.TimeText : $"{content.DateText}  {content.TimeText}", small, Align.Center);
        }
        else
        {
            sheet.Text($"{content.Merchant} - page {pageIndex + 1} of {pageCount}", small, Align.Center);
        }

        sheet.Rule();
        foreach (var item in items)
        {
            sheet.Columns(item.Name, Amount(content, item.Amount), body);
            if (item.Detail is not null)
            {
                sheet.Text("  " + item.Detail, small);
            }
        }

        sheet.Rule();
        if (pageIndex < pageCount - 1)
        {
            sheet.Text("continued", small, Align.Center);
            return sheet.ToBitmap();
        }

        foreach (var line in content.Summary)
        {
            if (line.Kind == SummaryKind.Total)
            {
                sheet.Gap(4);
                sheet.Columns(line.Label, Amount(content, line.Amount), total);
            }
            else
            {
                sheet.Columns(line.Label, Amount(content, line.Amount), body);
            }
        }

        sheet.Gap(14);
        sheet.Text("Thank you for dining with us", small, Align.Center);
        sheet.Text(content.FooterLines[0], small, Align.Center);
        return sheet.ToBitmap();
    }
}

/// <summary>A forecourt slip: a bold banner, a pump line, details in a typewriter font.</summary>
internal sealed class FuelLayout : ReceiptLayout
{
    protected override SKBitmap DrawPage(ReceiptContent content, LineItem[] items, int pageIndex, int pageCount, Random random)
    {
        using var sheet = new Sheet(380, 24);
        var banner = Fonts.Sans(24, bold: true);
        var body = Fonts.Mono(14);
        var bold = Fonts.Mono(17, bold: true);
        var small = Fonts.Mono(12);

        if (pageIndex == 0)
        {
            var top = sheet.Y - 4;
            sheet.Text(content.Merchant.ToUpperInvariant(), banner, Align.Center);
            sheet.Box(sheet.Left - 6, top, sheet.Width - (2 * sheet.Margin) + 12, sheet.Y - top + 2, 3);
            sheet.Gap(8);
            content.HeaderLines.ToList().ForEach(line => sheet.Text(line, body, Align.Center));
            sheet.Text($"PUMP {int.Parse(content.ReceiptNumber[..1]) % 8 + 1}", bold, Align.Center);
            sheet.Columns(content.DateText ?? string.Empty, content.TimeText, body);
        }
        else
        {
            sheet.Text($"PAGE {pageIndex + 1} OF {pageCount}", body, Align.Center);
        }

        sheet.Rule(2);
        foreach (var item in items)
        {
            sheet.Text(item.Name.ToUpperInvariant(), body);
            sheet.Columns(item.Detail is null ? string.Empty : "  " + item.Detail, Amount(content, item.Amount), body);
        }

        sheet.Rule(2);
        if (pageIndex < pageCount - 1)
        {
            return sheet.ToBitmap();
        }

        foreach (var line in content.Summary)
        {
            sheet.Columns(line.Label, Amount(content, line.Amount), line.Kind == SummaryKind.Total ? bold : body);
        }

        sheet.Gap(8);
        sheet.Text("PAID BY CARD", small, Align.Center);
        sheet.Text(content.FooterLines[0], body, Align.Center);
        return sheet.ToBitmap();
    }
}
