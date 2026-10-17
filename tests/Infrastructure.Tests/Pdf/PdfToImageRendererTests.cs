using Stubby.Application.Abstractions;
using Stubby.Infrastructure.Pdf;

namespace Stubby.Infrastructure.Tests.Pdf;

public class PdfToImageRendererTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // At 72 DPI one point is one pixel, so an image's width says which page it is.
    private static readonly PdfToImageRenderer Renderer = new(dpi: 72);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_page_pdf_returns_one_png()
    {
        var pages = await Renderer.RenderAsync(PdfBuilder.Build(1), Token);

        var page = Assert.Single(pages);
        Assert.Equal(PngSignature, page.Take(8));
        Assert.Equal(PdfBuilder.PageWidth(1), PdfBuilder.PngWidth(page));
    }

    [Fact]
    public async Task Five_page_pdf_returns_five_pngs_in_page_order()
    {
        var pages = await Renderer.RenderAsync(PdfBuilder.Build(5), Token);

        Assert.Equal(5, pages.Count);
        Assert.Equal(
            Enumerable.Range(1, 5).Select(PdfBuilder.PageWidth),
            pages.Select(PdfBuilder.PngWidth));
    }

    [Fact]
    public async Task Every_image_starts_with_the_png_signature()
    {
        var pages = await Renderer.RenderAsync(PdfBuilder.Build(3), Token);

        Assert.All(pages, page => Assert.Equal(PngSignature, page.Take(8)));
    }

    [Fact]
    public async Task Dpi_setting_scales_the_image()
    {
        var doubled = await new PdfToImageRenderer(dpi: 144).RenderAsync(PdfBuilder.Build(1), Token);

        Assert.Equal(2 * PdfBuilder.PageWidth(1), PdfBuilder.PngWidth(Assert.Single(doubled)));
    }

    [Fact]
    public async Task Pdf_with_no_pages_throws()
    {
        var failure = await Assert.ThrowsAsync<PdfRenderException>(
            () => Renderer.RenderAsync(PdfBuilder.Build(0), Token));

        Assert.NotEmpty(failure.Message);
    }

    [Fact]
    public async Task Empty_input_throws()
    {
        await Assert.ThrowsAsync<PdfRenderException>(() => Renderer.RenderAsync(ReadOnlyMemory<byte>.Empty, Token));
    }

    [Fact]
    public async Task Text_that_is_not_a_pdf_throws()
    {
        await Assert.ThrowsAsync<PdfRenderException>(
            () => Renderer.RenderAsync("This is not a PDF."u8.ToArray(), Token));
    }

    [Fact]
    public async Task Random_bytes_throw()
    {
        var bytes = new byte[2048];
        new Random(7).NextBytes(bytes);

        await Assert.ThrowsAsync<PdfRenderException>(() => Renderer.RenderAsync(bytes, Token));
    }

    [Fact]
    public async Task Truncated_pdf_throws()
    {
        var whole = PdfBuilder.Build(3);

        await Assert.ThrowsAsync<PdfRenderException>(() => Renderer.RenderAsync(whole.AsMemory(0, whole.Length / 2), Token));
    }

    [Fact]
    public async Task Renderer_still_works_after_a_corrupt_file()
    {
        await Assert.ThrowsAsync<PdfRenderException>(() => Renderer.RenderAsync(new byte[] { 1, 2, 3 }, Token));

        var pages = await Renderer.RenderAsync(PdfBuilder.Build(2), Token);

        Assert.Equal(2, pages.Count);
    }

    [Fact]
    public async Task Concurrent_calls_on_different_pdfs_each_return_their_own_pages()
    {
        var pageCounts = Enumerable.Range(0, 20).Select(i => 1 + (i % 5)).ToList();

        var results = await Task.WhenAll(pageCounts.Select(
            count => Task.Run(async () => (Count: count, Pages: await Renderer.RenderAsync(PdfBuilder.Build(count), Token)), Token)));

        foreach (var (count, pages) in results)
        {
            Assert.Equal(
                Enumerable.Range(1, count).Select(PdfBuilder.PageWidth),
                pages.Select(PdfBuilder.PngWidth));
        }
    }

    [Fact]
    public async Task Cancelled_call_throws_cancellation_and_not_a_render_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Renderer.RenderAsync(PdfBuilder.Build(1), cancelled.Token));
    }

    [Fact]
    public async Task Rendering_leaves_no_file_on_disk()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "stubby-pdf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var originalTmp = Environment.GetEnvironmentVariable("TMP");
        var originalTemp = Environment.GetEnvironmentVariable("TEMP");
        try
        {
            // The temp folder is redirected to an empty one, so anything written there would show up.
            Environment.SetEnvironmentVariable("TMP", tempRoot);
            Environment.SetEnvironmentVariable("TEMP", tempRoot);

            await Renderer.RenderAsync(PdfBuilder.Build(3), Token);

            Assert.Empty(Directory.GetFileSystemEntries(tempRoot));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMP", originalTmp);
            Environment.SetEnvironmentVariable("TEMP", originalTemp);
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
