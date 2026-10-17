using Microsoft.Extensions.Logging;
using Stubby.Application.Abstractions;

namespace Stubby.Infrastructure.Storage;

internal sealed class FileSystemImageStore(string rootPath, ILogger<FileSystemImageStore> logger) : IImageStore
{
    private readonly string _root = Path.GetFullPath(rootPath);

    public async Task<string> SaveAsync(ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var path = Guid.NewGuid().ToString("N") + ExtensionFor(contentType);
        var fullPath = Path.Combine(_root, path);

        Directory.CreateDirectory(_root);
        try
        {
            await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
        }
        catch
        {
            await DeleteAsync(path, CancellationToken.None);
            throw;
        }

        return path;
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_root, path));
            if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Path is outside the storage root.");
            }

            File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete stored file {Path}.", path);
        }

        return Task.CompletedTask;
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "application/pdf" => ".pdf",
        _ => throw new ArgumentException($"Unsupported content type '{contentType}'.", nameof(contentType)),
    };
}
