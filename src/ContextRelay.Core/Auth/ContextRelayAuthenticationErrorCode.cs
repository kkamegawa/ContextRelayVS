namespace ContextRelay.Core.Auth;

public enum ContextRelayAuthenticationErrorCode
{
    MissingClientId,
    InteractiveAuthenticationRequired,
    InteractiveAuthenticationFailed,
    ServiceError,
    CacheInitializationFailed,

    /// <summary>
    /// The dedicated SharePoint Embedded token could not be issued because consent for
    /// FileStorageContainer.Selected is missing or the scope is not configured on the app registration.
    /// </summary>
    EmbeddedConsentRequired
}
