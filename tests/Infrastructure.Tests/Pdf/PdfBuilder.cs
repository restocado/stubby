using System.Text;

namespace Stubby.Infrastructure.Tests.Pdf;

/// <summary>Builds small valid PDFs in memory, so no sample files are needed.</summary>
internal static class PdfBuilder
{
    /// <summary>Width in points of page <paramref name="number"/> (1-based). Every page differs, so order can be checked.</summary>
    public static int PageWidth(int number) => 200 + (10 * number);

    public const int PageHeight = 300;

    public static byte[] Build(int pageCount)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pageCount).Select(i => $"{4 + (2 * i)} 0 R"))}] /Count {pageCount} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
        };

        for (var i = 0; i < pageCount; i++)
        {
            var content = $"BT /F1 12 Tf 20 {PageHeight - 40} Td (Page {i + 1}) Tj ET";
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth(i + 1)} {PageHeight}] "
                + $"/Resources << /Font << /F1 3 0 R >> >> /Contents {5 + (2 * i)} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append($"{offset:D10} 00000 n \n");
        }

        pdf.Append($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    /// <summary>The width in pixels, read from the PNG header.</summary>
    public static int PngWidth(byte[] png) => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
}
