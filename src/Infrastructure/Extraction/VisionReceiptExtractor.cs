using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.Extraction;

internal sealed class VisionReceiptExtractor(IAiGateway gateway, IPdfRenderer pdfRenderer) : IReceiptExtractor
{
    private const string Pdf = "application/pdf";

    public async Task<ExtractionResult> ExtractAsync(ReceiptFile file, CancellationToken cancellationToken)
    {
        var images = await ImagesAsync(file, cancellationToken);

        string reply;
        try
        {
            // One call for the whole receipt, with every page in order. There is no retry here: the worker owns the attempt budget.
            reply = await gateway.CompleteAsync(new AiRequest(ExtractionPrompt.Text, images), cancellationToken);
        }
        catch (AiGatewayException ex)
        {
            throw new ExtractionException(ex.Message, ex);
        }

        var parsed = ExtractionResponseParser.Parse(reply);
        return new ExtractionResult(
            parsed.Merchant,
            parsed.Date,
            parsed.Total,
            parsed.Subtotal,
            parsed.Tax,
            gateway.Model,
            ExtractionPrompt.Version);
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<byte>>> ImagesAsync(ReceiptFile file, CancellationToken cancellationToken)
    {
        if (file.ContentType is "image/jpeg" or "image/png")
        {
            return [file.Content];
        }

        if (file.ContentType != Pdf)
        {
            throw new ArgumentException($"Unsupported content type '{file.ContentType}'.", nameof(file));
        }

        try
        {
            var pages = await pdfRenderer.RenderAsync(file.Content, cancellationToken);
            return pages.Select(page => (ReadOnlyMemory<byte>)page).ToList();
        }
        catch (PdfRenderException ex)
        {
            throw new ExtractionException(ex.Message, ex);
        }
    }
}
