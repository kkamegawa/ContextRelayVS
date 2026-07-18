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

        using var reader = new TokenAwareLineReader(stream, Encoding.UTF8, bufferSize: 1024);
        var dataLines = new List<string>();
        var latestText = string.Empty;
        var latestPartLengths = Array.Empty<int>();
        var latestMessageCount = 0;
        var eventCount = 0;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                ConsumeEvent(dataLines, requestMessage, ref latestText, ref latestPartLengths, ref latestMessageCount, ref eventCount);
                dataLines.Clear();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLines.Add(line.Substring("data:".Length).TrimStart());
                continue;
            }

            // SSE comments and unknown fields do not contribute to event data.
        }

        ConsumeEvent(dataLines, requestMessage, ref latestText, ref latestPartLengths, ref latestMessageCount, ref eventCount);
        return new CopilotChatAdapter.CopilotChatTurnResult(latestText, latestMessageCount, latestPartLengths, eventCount);
    }

    private static void ConsumeEvent(
        IReadOnlyList<string> dataLines,
        string requestMessage,
        ref string latestText,
        ref int[] latestPartLengths,
        ref int latestMessageCount,
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
        latestText = candidateText;
        latestPartLengths = parts.Select(part => part.Length).ToArray();
        latestMessageCount = messages.Length;
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

    private sealed class TokenAwareLineReader : IDisposable
    {
        private readonly Stream stream;
        private readonly Decoder decoder;
        private readonly byte[] byteBuffer;
        private readonly char[] charBuffer;
        private readonly StringBuilder lineBuilder = new();
        private int charIndex;
        private int charCount;
        private bool endOfStream;
        private bool hasSeenFirstCharacter;
        private bool skipNextLineFeed;

        public TokenAwareLineReader(Stream stream, Encoding encoding, int bufferSize)
        {
            this.stream = stream;
            decoder = encoding.GetDecoder();
            byteBuffer = new byte[bufferSize];
            charBuffer = new char[encoding.GetMaxCharCount(bufferSize)];
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (charIndex >= charCount)
                {
                    if (endOfStream)
                    {
                        return lineBuilder.Length == 0 ? null : FlushLine();
                    }

                    await FillBufferAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var value = charBuffer[charIndex++];
                if (skipNextLineFeed)
                {
                    skipNextLineFeed = false;
                    if (value == '\n')
                    {
                        continue;
                    }
                }

                if (!hasSeenFirstCharacter)
                {
                    hasSeenFirstCharacter = true;
                    if (value == '\uFEFF')
                    {
                        continue;
                    }
                }

                if (value == '\r')
                {
                    skipNextLineFeed = true;
                    return FlushLine();
                }

                if (value == '\n')
                {
                    return FlushLine();
                }

                lineBuilder.Append(value);
            }
        }

        public void Dispose()
        {
            stream.Dispose();
        }

        private async Task FillBufferAsync(CancellationToken cancellationToken)
        {
            var bytesRead = await stream.ReadAsync(byteBuffer, 0, byteBuffer.Length, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            charIndex = 0;

            if (bytesRead == 0)
            {
                charCount = decoder.GetChars(byteBuffer, 0, 0, charBuffer, 0, flush: true);
                endOfStream = true;
                return;
            }

            charCount = decoder.GetChars(byteBuffer, 0, bytesRead, charBuffer, 0, flush: false);
        }

        private string FlushLine()
        {
            var line = lineBuilder.ToString();
            lineBuilder.Clear();
            return line;
        }
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
