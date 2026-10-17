namespace Stubby.Application.Abstractions;

/// <summary>A PDF could not be rendered. The message is a short reason that is safe to store and show.</summary>
public sealed class PdfRenderException(string reason, Exception? innerException = null)
    : Exception(reason, innerException);
