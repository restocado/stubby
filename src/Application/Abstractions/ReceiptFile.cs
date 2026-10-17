namespace Stubby.Application.Abstractions;

/// <summary>A stored receipt file: a JPG, a PNG or a PDF.</summary>
public sealed record ReceiptFile(ReadOnlyMemory<byte> Content, string ContentType);
