using ContextRelay.Core.Router;

namespace ContextRelay.Core.Models;

public static class ContextItemKeys
{
    public static string Build(
        ContextSource source,
        string? title,
        string? url,
        string? timestamp,
        string? snippet,
        string? containerTypeId = null)
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

        var normalizedTitle = (title ?? string.Empty).Trim();
        if (source == ContextSource.SharePointEmbedded)
        {
            // The same document can surface from different container types, so the key keeps the
            // container type to stay stable across preview, pin, unpin, and the shared store.
            return $"{ContextSourceSchema.SharePointEmbedded}::{(containerTypeId ?? string.Empty).Trim().ToLowerInvariant()}::{discriminator}::{normalizedTitle}";
        }

        return $"{source}::{discriminator}::{normalizedTitle}";
    }

    public static string Build(ContextItem item)
    {
        item.Metadata.TryGetValue(EmbeddedMetadataKeys.ContainerTypeId, out var containerTypeId);
        return Build(item.Source, item.Title, item.Url, item.Timestamp, item.Snippet, containerTypeId);
    }
}
