namespace Stubby.Application.Abstractions;

/// <summary>A gateway call failed. The message is a short reason that is safe to store and show.</summary>
public sealed class AiGatewayException(string reason, Exception? innerException = null)
    : Exception(reason, innerException);
