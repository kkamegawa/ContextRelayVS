using System;

namespace ContextRelay.Core.Auth;

public sealed class ContextRelayAccessToken
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTimeOffset ExpiresOn { get; set; }

    public ContextRelayAccountInfo? Account { get; set; }
}
