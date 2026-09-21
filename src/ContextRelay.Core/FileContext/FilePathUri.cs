using System;
using System.IO;
using System.Linq;

namespace ContextRelay.Core.FileContext;

/// <summary>
/// Converts absolute filesystem paths to file URIs on every supported platform.
/// </summary>
/// <remarks>
/// <see cref="Uri"/> only treats some absolute paths as implicit file paths, and a Unix path such as
/// <c>/tmp/file.md</c> is not an absolute URI, so <c>new Uri(path).AbsoluteUri</c> cannot be used for
/// paths that may come from a non-Windows host. This helper builds the URI explicitly instead.
/// </remarks>
public static class FilePathUri
{
    /// <summary>
    /// Builds the file URI for an absolute filesystem path.
    /// </summary>
    /// <param name="absolutePath">The absolute path to convert, using either separator.</param>
    /// <returns>The absolute file URI, with the path component escaped.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="absolutePath"/> is empty.</exception>
    public static string FromPath(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            throw new ArgumentException("An absolute path is required.", nameof(absolutePath));
        }

        var normalized = absolutePath.Replace(Path.DirectorySeparatorChar, '/');
        if (Path.AltDirectorySeparatorChar != '/')
        {
            normalized = normalized.Replace(Path.AltDirectorySeparatorChar, '/');
        }

        normalized = normalized.Replace('\\', '/');

        var host = string.Empty;
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            // UNC share: //server/share/file.md becomes file://server/share/file.md.
            var remainder = normalized.Substring(2);
            var separatorIndex = remainder.IndexOf('/');
            if (separatorIndex < 0)
            {
                host = remainder;
                normalized = "/";
            }
            else
            {
                host = remainder.Substring(0, separatorIndex);
                normalized = remainder.Substring(separatorIndex);
            }
        }
        else if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            // Drive-letter paths such as C:/dir/file.md need the leading separator.
            normalized = "/" + normalized;
        }

        if (host.Length > 0)
        {
            // UriBuilder drops the authority for file URIs, so compose the UNC form directly.
            var escapedPath = string.Join("/", normalized.Split('/').Select(Uri.EscapeDataString));
            return Uri.UriSchemeFile + "://" + host + escapedPath;
        }

        var builder = new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = string.Empty,
            Path = normalized
        };

        return builder.Uri.AbsoluteUri;
    }
}
