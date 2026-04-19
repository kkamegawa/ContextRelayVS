namespace ContextRelay.Core.Auth;

public enum ContextRelayAuthenticationErrorCode
{
    MissingClientId,
    InteractiveAuthenticationRequired,
    InteractiveAuthenticationFailed,
    ServiceError,
    CacheInitializationFailed
}
