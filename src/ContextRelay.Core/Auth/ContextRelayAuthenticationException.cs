using System;

namespace ContextRelay.Core.Auth;

public sealed class ContextRelayAuthenticationException : Exception
{
    public ContextRelayAuthenticationException(
        ContextRelayAuthenticationErrorCode errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public ContextRelayAuthenticationErrorCode ErrorCode { get; }
}
