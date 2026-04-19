using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Adapters;

public interface ICopilotChatAdapter
{
    Task<string> AskAsync(string accessToken, string prompt, CancellationToken cancellationToken = default);
}
