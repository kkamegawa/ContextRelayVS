using System.IO;
using System.Text;
using System.Threading.Tasks;
using ContextRelay.Core.Adapters;
using Xunit;

namespace ContextRelay.Core.Tests.Adapters;

public sealed class CopilotChatStreamParserTests
{
    [Fact]
    public async Task ParseAsync_UsesLatestNonEmptyAssistantSnapshot()
    {
        var stream = ToStream("""
            data: {"messages":[{"text":"Write a plan."},{"text":"First part"}]}
            id:1

            data: {"messages":[]}
            id:2

            data: {"messages":[{"text":"Write a plan."},{"text":"First part. Second part."}]}
            id:3

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Write a plan.", TestContext.Current.CancellationToken);

        Assert.Equal("First part. Second part.", result.Text);
        Assert.Equal(3, result.StreamEventCount);
        Assert.Equal(2, result.MessageCount);
    }

    [Fact]
    public async Task ParseAsync_AcceptsDocumentedMultilineDataEvent()
    {
        var stream = ToStream("""
            data: {
              "messages": [
                { "text": "Prompt" },
                { "text": "Assistant reply." }
              ]
            }
            id:1

            """);

        var result = await CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken);

        Assert.Equal("Assistant reply.", result.Text);
        Assert.Equal(1, result.StreamEventCount);
    }

    [Fact]
    public async Task ParseAsync_ThrowsForMalformedJsonEvent()
    {
        var stream = ToStream("""
            data: {"messages":[

            """);

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            () => CopilotChatStreamParser.ParseAsync(stream, "Prompt", TestContext.Current.CancellationToken));
    }

    private static Stream ToStream(string value)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n")));
    }
}
