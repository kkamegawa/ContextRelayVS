using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.SharedStore;

namespace ContextRelay.Core.Snippets;

public interface ISnippetRepository : IDisposable
{
    event EventHandler? Changed;

    Task<IReadOnlyList<SharedSnippetItem>> GetAllAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);

    Task<SharedSnippetItem> SaveAsync(SaveSnippetRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
