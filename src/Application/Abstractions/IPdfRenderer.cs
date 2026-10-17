namespace Stubby.Application.Abstractions;

public interface IPdfRenderer
{
    /// <summary>Renders every page of a PDF to a PNG, in page order. Nothing is written to disk.</summary>
    /// <exception cref="PdfRenderException">The file is corrupt, is not a PDF, or has no pages.</exception>
    Task<IReadOnlyList<byte[]>> RenderAsync(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken);
}
