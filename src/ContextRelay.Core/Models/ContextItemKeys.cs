using ContextRelay.Core.Router;

namespace ContextRelay.Core.Models;

public static class ContextItemKeys
{
    public static string Build(
        ContextSource source,
        string? title,
        string? url,
        string? timestamp,
        string? snippet)
    {
        var discriminator =
            (url ?? string.Empty).Trim();

        if (discriminator.Length == 0)
        {
            discriminator = (timestamp ?? string.Empty).Trim();
        }

        if (discriminator.Length == 0)
        {
            discriminator = (snippet ?? string.Empty).Trim();
        }

        return $"{source}::{discriminator}::{(title ?? string.Empty).Trim()}";
    }

    public static string Build(ContextItem item)
    {
        return Build(item.Source, item.Title, item.Url, item.Timestamp, item.Snippet);
    }
}
