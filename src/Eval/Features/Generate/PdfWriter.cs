using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Stubby.Eval.Features.Generate;

/// <summary>
/// Writes a PDF with one page for each bitmap, each page holding its picture as a JPEG. The extractor renders PDFs to
/// pictures anyway, so a picture of the page is enough to test reading a PDF, its page order and a multi-page file.
/// The bytes depend only on the pictures, so the same pages give the same file.
/// </summary>
internal static class PdfWriter
{
    // A pixel is three quarters of a point, so a page is about as large as a 96 dpi screen shows it.
    private const float PointsPerPixel = 0.75f;

    public static byte[] Write(IReadOnlyList<SKBitmap> pages)
    {
        using var file = new MemoryStream();
        var offsets = new List<long>();

        void Text(string text) => file.Write(Encoding.ASCII.GetBytes(text));

        void Object(int number, Action body)
        {
            offsets.Add(file.Position);
            Text(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n"));
            body();
            Text("endobj\n");
        }

        Text("%PDF-1.4\n");
        file.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        // 1 is the catalogue, 2 the page list, then a page, its picture and its drawing instructions for each page.
        Object(1, () => Text("<< /Type /Catalog /Pages 2 0 R >>\n"));
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => string.Create(CultureInfo.InvariantCulture, $"{3 + (3 * i)} 0 R")));
        Object(2, () => Text(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\n")));

        for (var i = 0; i < pages.Count; i++)
        {
            var pageNumber = 3 + (3 * i);
            var width = Points(pages[i].Width);
            var height = Points(pages[i].Height);
            var jpeg = Jpeg(pages[i]);
            var drawing = string.Create(CultureInfo.InvariantCulture, $"q {width} 0 0 {height} 0 0 cm /Im0 Do Q");

            Object(pageNumber, () => Text(string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /XObject << /Im0 {pageNumber + 1} 0 R >> >> /Contents {pageNumber + 2} 0 R >>\n")));
            Object(pageNumber + 1, () =>
            {
                Text(string.Create(
                    CultureInfo.InvariantCulture,
                    $"<< /Type /XObject /Subtype /Image /Width {pages[i].Width} /Height {pages[i].Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n"));
                file.Write(jpeg);
                Text("\nendstream\n");
            });
            Object(pageNumber + 2, () => Text(string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Length {drawing.Length} >>\nstream\n{drawing}\nendstream\n")));
        }

        // Every entry is exactly 20 bytes, as the format requires.
        var crossReference = file.Position;
        Text(string.Create(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n"));
        Text("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Text(string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n"));
        }

        Text(string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{crossReference}\n%%EOF\n"));
        return file.ToArray();
    }

    private static string Points(int pixels) => (pixels * PointsPerPixel).ToString("0.##", CultureInfo.InvariantCulture);

    private static byte[] Jpeg(SKBitmap page)
    {
        using var image = SKImage.FromBitmap(page);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}
