using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.SharedStore;

public interface ISharedSessionStore
{
    Task<IReadOnlyList<SharedSnippetItem>> GetSnippetsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SharedSnippetItem>> UpsertSnippetsAsync(IEnumerable<SharedSnippetItem> snippets, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SharedChatHistoryItem>> GetChatHistoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SharedChatHistoryItem>> AppendChatHistoryAsync(IEnumerable<SharedChatHistoryItem> historyItems, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SharedHandoffIndexItem>> GetHandoffIndexAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SharedHandoffIndexItem>> UpsertHandoffIndexAsync(IEnumerable<SharedHandoffIndexItem> handoffEntries, CancellationToken cancellationToken = default);

    Task ClearAsync(SharedStoreFileKind fileKind, CancellationToken cancellationToken = default);
}
