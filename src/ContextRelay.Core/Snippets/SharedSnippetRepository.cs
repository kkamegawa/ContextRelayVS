using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Utilities;

namespace ContextRelay.Core.Snippets;

public sealed class SharedSnippetRepository : ISnippetRepository
{
    private readonly IClock clock;
    private readonly ISharedSessionStore sharedSessionStore;
    private readonly SharedStoreWatcher? watcher;
    private readonly bool ownsWatcher;

    public SharedSnippetRepository(
        SharedStoreOptions sharedStoreOptions,
        IClock? clock = null)
        : this(
            new FileSystemSharedSessionStore(sharedStoreOptions, clock),
            new SharedStoreWatcher(sharedStoreOptions.RootDirectory, sharedStoreOptions.WatcherDebounceMilliseconds),
            clock,
            ownsWatcher: true)
    {
    }

    public SharedSnippetRepository(
        ISharedSessionStore sharedSessionStore,
        SharedStoreWatcher? watcher = null,
        IClock? clock = null,
        bool ownsWatcher = false)
    {
        this.sharedSessionStore = sharedSessionStore;
        this.watcher = watcher;
        this.clock = clock ?? SystemClock.Instance;
        this.ownsWatcher = ownsWatcher;

        if (this.watcher is not null)
        {
            this.watcher.Changed += OnWatcherChanged;
            this.watcher.Start();
        }
    }

    public event EventHandler? Changed;

    public async Task<IReadOnlyList<SharedSnippetItem>> GetAllAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var items = await sharedSessionStore.GetSnippetsAsync(cancellationToken).ConfigureAwait(false);
        var filtered = includeDeleted
            ? items
            : items.Where(item => string.IsNullOrWhiteSpace(item.DeletedAt)).ToArray();

        return filtered
            .OrderByDescending(item => ParseDateTime(item.UpdatedAt))
            .ToArray();
    }

    public async Task<SharedSnippetItem> SaveAsync(SaveSnippetRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Snippet name must not be empty.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Snippet))
        {
            throw new ArgumentException("Snippet text must not be empty.", nameof(request));
        }

        var existingItems = await sharedSessionStore.GetSnippetsAsync(cancellationToken).ConfigureAwait(false);
        var existing = existingItems.FirstOrDefault(item => item.Id == request.Id);
        var timestamp = clock.UtcNow.ToString("O");
        var snippet = new SharedSnippetItem
        {
            Id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id!,
            CreatedAt = existing?.CreatedAt ?? timestamp,
            UpdatedAt = timestamp,
            DeletedAt = null,
            Name = request.Name.Trim(),
            Source = ToSchemaValue(request.Source),
            SourceUrl = request.SourceUrl?.Trim(),
            Snippet = request.Snippet,
            Metadata = new Dictionary<string, System.Text.Json.JsonElement>(request.Metadata),
            ExtensionData = existing?.ExtensionData is null
                ? new Dictionary<string, System.Text.Json.JsonElement>()
                : new Dictionary<string, System.Text.Json.JsonElement>(existing.ExtensionData)
        };

        var snippets = await sharedSessionStore.UpsertSnippetsAsync(new[] { snippet }, cancellationToken).ConfigureAwait(false);
        return snippets.Single(item => item.Id == snippet.Id);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Snippet id must not be empty.", nameof(id));
        }

        var items = await sharedSessionStore.GetSnippetsAsync(cancellationToken).ConfigureAwait(false);
        var existing = items.FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return false;
        }

        var deleted = new SharedSnippetItem
        {
            Id = existing.Id,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = clock.UtcNow.ToString("O"),
            DeletedAt = clock.UtcNow.ToString("O"),
            Name = existing.Name,
            Source = existing.Source,
            SourceUrl = existing.SourceUrl,
            Snippet = existing.Snippet,
            Metadata = new Dictionary<string, System.Text.Json.JsonElement>(existing.Metadata),
            ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>(existing.ExtensionData)
        };

        await sharedSessionStore.UpsertSnippetsAsync(new[] { deleted }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return sharedSessionStore.ClearAsync(SharedStoreFileKind.Snippets, cancellationToken);
    }

    public void Dispose()
    {
        if (watcher is null)
        {
            return;
        }

        watcher.Changed -= OnWatcherChanged;
        if (ownsWatcher)
        {
            watcher.Dispose();
        }
    }

    private void OnWatcherChanged(object? sender, SharedStoreChangedEventArgs e)
    {
        if (e.FileKind == SharedStoreFileKind.Snippets)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string ToSchemaValue(SnippetSource source)
    {
        return source switch
        {
            SnippetSource.Mail => "mail",
            SnippetSource.Teams => "teams",
            SnippetSource.SharePoint => "sharepoint",
            SnippetSource.OneDrive => "onedrive",
            SnippetSource.Connectors => "connectors",
            SnippetSource.Chat => "chat",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown snippet source.")
        };
    }

    private static DateTimeOffset ParseDateTime(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
    }
}
