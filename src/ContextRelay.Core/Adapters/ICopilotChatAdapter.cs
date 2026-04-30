using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Adapters;

public interface ICopilotChatAdapter
{
    Task<string> AskAsync(string accessToken, string prompt, CancellationToken cancellationToken = default);

    Task<string> CreateConversationAsync(string accessToken, CancellationToken cancellationToken = default);

    Task<string> SendMessageAsync(
        string accessToken,
        string conversationId,
        string message,
        CopilotChatSendOptions? options = null,
        CancellationToken cancellationToken = default);
}
