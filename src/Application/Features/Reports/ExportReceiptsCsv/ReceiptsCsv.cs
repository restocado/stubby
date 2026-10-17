using System.Buffers;
using System.Globalization;
using System.Text;
using Stubby.Application.Abstractions;
using Stubby.Domain.Receipts;

namespace Stubby.Application.Features.Reports.ExportReceiptsCsv;

/// <summary>Writes the CSV by hand, so that nothing depends on the server's culture.</summary>
internal static class ReceiptsCsv
{
    public const string Header = "TransactionNo,Date,Merchant,Category,Total";

    private static readonly SearchValues<char> NeedsQuotes = SearchValues.Create(",\"\r\n");

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static byte[] Build(IEnumerable<ApprovedReceiptRow> rows)
    {
        var csv = new StringBuilder(Header).Append("\r\n");
        foreach (var row in rows)
        {
            csv.Append(TransactionNumber.Format(row.TransactionNo)).Append(',')
                .Append(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(Field(DefuseFormula(row.Merchant))).Append(',')
                .Append(row.Category.ToString().ToLowerInvariant()).Append(',')
                .Append(row.Total.ToString("F2", CultureInfo.InvariantCulture)).Append("\r\n");
        }

        // GetPreamble is the byte order mark, EF BB BF.
        return [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(csv.ToString())];
    }

    // A value that starts like a formula is run as one by Excel, so it gets a leading quote and is shown as text.
    private static string DefuseFormula(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    // A field with a comma, a quote or a line break is quoted, with inner quotes doubled.
    private static string Field(string value) =>
        value.AsSpan().IndexOfAny(NeedsQuotes) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
}
