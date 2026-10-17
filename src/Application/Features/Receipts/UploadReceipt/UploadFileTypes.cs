namespace Stubby.Application.Features.Receipts.UploadReceipt;

internal static class UploadFileTypes
{
    private sealed record FileType(string ContentType, string[] Extensions, byte[] Signature);

    private static readonly FileType[] Allowed =
    [
        new("image/jpeg", [".jpg", ".jpeg"], [0xFF, 0xD8, 0xFF]),
        new("image/png", [".png"], [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        new("application/pdf", [".pdf"], "%PDF-"u8.ToArray()),
    ];

    public static bool IsAllowedContentType(string contentType) =>
        Allowed.Any(t => t.ContentType == contentType);

    public static bool ExtensionMatches(string contentType, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return Allowed.Any(t => t.ContentType == contentType
            && t.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    public static bool SignatureMatches(string contentType, ReadOnlySpan<byte> content)
    {
        foreach (var type in Allowed)
        {
            if (type.ContentType == contentType)
            {
                return content.StartsWith(type.Signature);
            }
        }

        return false;
    }
}
