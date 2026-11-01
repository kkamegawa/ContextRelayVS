using System;
using ContextRelay.Core.Utilities;

namespace ContextRelay.VSExtension.ToolWindows;

/// <summary>
/// Decides which links the tool window may open: only absolute https URLs that pass
/// <see cref="ExternalUrlSafety"/>. Everything else is shown as plain text.
/// </summary>
internal static class SafeLinks
{
    internal static bool TryGetOpenableUrl(string? candidate, out string url)
    {
        if (ExternalUrlSafety.TryNormalizeExternalUrl(candidate, out var normalized) &&
            normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = normalized;
            return true;
        }

        url = string.Empty;
        return false;
    }
}
