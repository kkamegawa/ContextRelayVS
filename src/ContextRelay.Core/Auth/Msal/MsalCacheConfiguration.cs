using System;
using System.IO;

namespace ContextRelay.Core.Auth.Msal;

public sealed class MsalCacheConfiguration
{
    public string CacheFileName { get; set; } = "msal.cache";

    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ContextRelayVS");

    public static MsalCacheConfiguration CreateDefault()
    {
        return new MsalCacheConfiguration();
    }
}
