namespace Stubby.Application.Abstractions;

public interface IReceiptExtractor
{
    /// <summary>
    /// Reads a stored receipt file. Decides no flags, statuses or retries: the caller does that.
    /// </summary>
    /// <exception cref="ExtractionException">The gateway or the PDF renderer failed, or the reply was unusable.</exception>
    Task<ExtractionResult> ExtractAsync(ReceiptFile file, CancellationToken cancellationToken);
}
