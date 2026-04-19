using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;

namespace ContextRelay.Core.Adapters;

public interface IContextSearchAdapter
{
    ContextSource Source { get; }

    Task<IReadOnlyList<ContextItem>> SearchAsync(
        string accessToken,
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}
