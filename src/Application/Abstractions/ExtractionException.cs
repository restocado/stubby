namespace Stubby.Application.Abstractions;

/// <summary>
/// A receipt could not be read. The message is a short reason that is safe to store and show:
/// it never holds receipt content.
/// </summary>
public sealed class ExtractionException(string reason, Exception? innerException = null)
    : Exception(reason, innerException);
