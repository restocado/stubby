namespace Stubby.Application.Abstractions;

public interface IImageStore
{
    /// <summary>Stores the file and returns its path relative to the storage root.</summary>
    Task<string> SaveAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>Deletes the file. A missing file or a failed delete is logged, not thrown.</summary>
    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
