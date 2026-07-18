using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ContextRelay.Core.Adapters;

internal static class CopilotChatStreamParser
{
    public static async Task<CopilotChatAdapter.CopilotChatTurnResult> ParseAsync(
        Stream stream,
        string requestMessage,
        CancellationToken cancellationToken = default)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        var dataLines = new List<string>();
        var bestText = string.Empty;
        var bestPartLengths = Array.Empty<int>();
        var bestMessageCount = 0;
        var eventCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                ConsumeEvent(dataLines, requestMessage, ref bestText, ref bestPartLengths, ref bestMessageCount, ref eventCount);
                dataLines.Clear();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLines.Add(line.Substring("data:".Length).TrimStart());
                continue;
            }

            if (dataLines.Count > 0 &&
                !line.StartsWith("id:", StringComparison.Ordinal) &&
                !line.StartsWith("event:", StringComparison.Ordinal) &&
                !line.StartsWith("retry:", StringComparison.Ordinal))
            {
                dataLines.Add(line);
            }
        }

        ConsumeEvent(dataLines, requestMessage, ref bestText, ref bestPartLengths, ref bestMessageCount, ref eventCount);
        return new CopilotChatAdapter.CopilotChatTurnResult(bestText, bestMessageCount, bestPartLengths, eventCount);
    }

    private static void ConsumeEvent(
        IReadOnlyList<string> dataLines,
        string requestMessage,
        ref string bestText,
        ref int[] bestPartLengths,
        ref int bestMessageCount,
        ref int eventCount)
    {
        if (dataLines.Count == 0)
        {
            return;
        }

        eventCount++;
        var payload = string.Join("\n", dataLines).Trim();
        if (payload.Length == 0 || string.Equals(payload, "[DONE]", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("messages", out var messagesElement) ||
            messagesElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var messages = messagesElement.EnumerateArray().ToArray();
        var parts = ExtractAssistantParts(messages, requestMessage);
        if (parts.Count == 0)
        {
            return;
        }

        var candidateText = CopilotChatAdapter.JoinResponseParts(parts);
        if (candidateText.Length >= bestText.Length)
        {
            bestText = candidateText;
            bestPartLengths = parts.Select(part => part.Length).ToArray();
            bestMessageCount = messages.Length;
        }
    }

    private static IReadOnlyList<string> ExtractAssistantParts(JsonElement[] messages, string requestMessage)
    {
        var hasCreatedDate = messages.Any(message => TryGetCreatedDate(message, out _));
        var orderedMessages = messages
            .Select((message, index) => new IndexedStreamMessage(message, index))
            .OrderBy(item => hasCreatedDate && TryGetCreatedDate(item.Message, out var createdDate)
                ? createdDate
                : DateTimeOffset.MaxValue)
            .ThenBy(item => item.Index);

        var parts = new List<string>();
        var seenParts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in orderedMessages)
        {
            var text = TryGetText(item.Message)?.Trim();
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text, requestMessage, StringComparison.Ordinal) ||
                !seenParts.Add(text!))
            {
                continue;
            }

            parts.Add(text!);
        }

        return parts;
    }

    private static string? TryGetText(JsonElement message)
    {
        return message.TryGetProperty("text", out var textElement) &&
            textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString()
                : null;
    }

    private static bool TryGetCreatedDate(JsonElement message, out DateTimeOffset createdDate)
    {
        createdDate = default;
        return message.TryGetProperty("createdDateTime", out var createdDateElement) &&
            createdDateElement.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(createdDateElement.GetString(), out createdDate);
    }

    private sealed class IndexedStreamMessage
    {
        public IndexedStreamMessage(JsonElement message, int index)
        {
            Message = message;
            Index = index;
        }

        public JsonElement Message { get; }

        public int Index { get; }
    }
}
