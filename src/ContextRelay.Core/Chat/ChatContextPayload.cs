using System;
using System.Collections.Generic;
using ContextRelay.Core.Adapters;

namespace ContextRelay.Core.Chat;

public sealed class ChatContextPayload
{
    public CopilotChatSendOptions SendOptions { get; set; } = new();

    public IReadOnlyList<string> Labels { get; set; } = Array.Empty<string>();
}
