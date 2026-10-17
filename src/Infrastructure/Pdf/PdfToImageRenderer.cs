using PDFtoImage;
using SkiaSharp;
using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.Pdf;

internal sealed class PdfToImageRenderer(int dpi) : IPdfRenderer
{
    public const int DefaultDpi = 150;
    public const int MinDpi = 72;
    public const int MaxDpi = 300;

    // PDFium is not thread-safe, so one render at a time across the whole process.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public async Task<IReadOnlyList<byte[]>> RenderAsync(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        if (pdf.IsEmpty)
        {
            throw new PdfRenderException("The PDF is empty.");
        }

        await Lock.WaitAsync(cancellationToken);
        try
        {
            // Rendering is CPU-bound, so it runs off the caller's thread.
            return await Task.Run(() => Render(pdf, cancellationToken), cancellationToken);
        }
        finally
        {
            Lock.Release();
        }
    }

    private List<byte[]> Render(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        var pages = new List<byte[]>();
        try
        {
            using var stream = new MemoryStream(pdf.ToArray(), writable: false);

            // Pages are rendered one at a time and each bitmap is disposed straight away.
#pragma warning disable CA1416 // PDFtoImage ships PDFium for Windows, Linux and macOS, the only platforms this app runs on.
            var bitmaps = Conversion.ToImages(stream, options: new RenderOptions(Dpi: dpi));
#pragma warning restore CA1416
            foreach (var bitmap in bitmaps)
            {
                using (bitmap)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    pages.Add(png.ToArray());
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PdfRenderException("The PDF could not be read.", ex);
        }

        if (pages.Count == 0)
        {
            throw new PdfRenderException("The PDF has no pages.");
        }

        return pages;
    }
}
