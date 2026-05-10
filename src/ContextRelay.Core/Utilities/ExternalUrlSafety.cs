using System;

namespace ContextRelay.Core.Utilities;

/// <summary>
/// Validates and normalizes external URLs before they are opened by the host.
/// </summary>
public static class ExternalUrlSafety
{
    /// <summary>
    /// Tries to normalize a candidate URL into a safe absolute URL.
    /// Only HTTP, HTTPS, and mailto schemes are allowed.
    /// </summary>
    /// <param name="candidate">The candidate URL string.</param>
    /// <param name="normalizedUrl">The normalized safe URL when validation succeeds.</param>
    /// <returns><see langword="true"/> when the URL is safe; otherwise <see langword="false"/>.</returns>
    public static bool TryNormalizeExternalUrl(string? candidate, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate?.Trim();
        if (trimmed is null || trimmed.Length == 0)
        {
            return false;
        }

        if (ContainsControlCharacter(trimmed))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.IsFile)
        {
            return false;
        }

        if (!IsAllowedScheme(parsed.Scheme))
        {
            return false;
        }

        normalizedUrl = parsed.AbsoluteUri;
        return true;
    }

    private static bool ContainsControlCharacter(string value)
    {
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedScheme(string scheme)
    {
        return scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }
}
