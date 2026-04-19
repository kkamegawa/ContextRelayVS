using System;
namespace ContextRelay.Core.Adapters;

public static class RetrievalSearchUtils
{
    public static string EscapeODataString(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }

    public static string StripSearchMarkup(string value)
    {
        return (value ?? string.Empty)
            .Replace("<ddd/>", "...")
            .Replace("<c0>", string.Empty)
            .Replace("</c0>", string.Empty)
            .Replace("<c1>", string.Empty)
            .Replace("</c1>", string.Empty)
            .Replace("<c2>", string.Empty)
            .Replace("</c2>", string.Empty)
            .Trim();
    }

    public static bool IsOneDriveUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        return parsed.Host.IndexOf("-my.sharepoint.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
            parsed.AbsolutePath.IndexOf("/personal/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string BuildSearchSnippet(string? summary, string? description, string? webUrl)
    {
        var normalizedSummary = StripSearchMarkup(summary ?? string.Empty);
        if (normalizedSummary.Length > 0)
        {
            return normalizedSummary;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            return description!.Trim();
        }

        return FormatLocationSnippet(null, webUrl);
    }

    public static string FormatLocationSnippet(string? path, string? webUrl)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            var normalized = path!.Replace("/drive/root:", string.Empty).Replace("/", " / ").Trim();
            return normalized.Length > 0 ? normalized : "Location available";
        }

        if (string.IsNullOrWhiteSpace(webUrl))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var parsed))
        {
            return webUrl!;
        }

        return $"{parsed.Host}{parsed.AbsolutePath}";
    }

    public static string? GetTitleFromUrl(string webUrl)
    {
        if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        var segments = parsed.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? null : segments[segments.Length - 1];
    }
}
