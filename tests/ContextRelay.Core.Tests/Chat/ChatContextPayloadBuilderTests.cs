using ContextRelay.Core.Chat;
using ContextRelay.Core.SharedStore;
using Xunit;

namespace ContextRelay.Core.Tests.Chat;

public sealed class ChatContextPayloadBuilderTests
{
    [Fact]
    public void Build_UsesFileResourcesForSharePointAndOneDriveHttpsSnippets()
    {
        var payload = ChatContextPayloadBuilder.Build(new[]
        {
            new SharedSnippetItem
            {
                Name = "Design doc",
                Source = "sharepoint",
                SourceUrl = "https://contoso.sharepoint.com/sites/eng/Shared%20Documents/design.docx",
                Snippet = "Design details"
            },
            new SharedSnippetItem
            {
                Name = "Personal plan",
                Source = "onedrive",
                SourceUrl = "https://contoso-my.sharepoint.com/personal/user/Documents/plan.docx",
                Snippet = "Plan details"
            }
        });

        Assert.Empty(payload.SendOptions.AdditionalContext);
        Assert.Equal(2, payload.SendOptions.ContextualResources?.Files.Count);
        Assert.Contains("Design doc", payload.Labels);
        Assert.Contains("Personal plan", payload.Labels);
    }

    [Fact]
    public void Build_FallsBackToBoundedAdditionalContextForTextSnippetsAndSearchSummary()
    {
        var longText = new string('x', ChatContextPayloadBuilder.MaxChatContextChars + 100);
        var payload = ChatContextPayloadBuilder.Build(new[]
        {
            new SharedSnippetItem
            {
                Name = "Mail thread",
                Source = "mail",
                SourceUrl = "https://outlook.office.com/mail/read/id",
                Snippet = longText
            }
        }, "Latest search summary");

        Assert.Single(payload.SendOptions.AdditionalContext);
        Assert.True(payload.SendOptions.AdditionalContext[0].Text.Length <= ChatContextPayloadBuilder.MaxChatContextChars);
        Assert.Contains("[truncated", payload.SendOptions.AdditionalContext[0].Text);
        Assert.Null(payload.SendOptions.ContextualResources);
        Assert.Contains("Mail thread", payload.Labels);
        Assert.DoesNotContain("Latest ContextRelay search summary", payload.Labels);
    }

    [Fact]
    public void Build_AddsSearchSummaryWhenBudgetAllows()
    {
        var payload = ChatContextPayloadBuilder.Build(System.Array.Empty<SharedSnippetItem>(), "Found two architecture docs.");

        Assert.Single(payload.SendOptions.AdditionalContext);
        Assert.Equal("Latest ContextRelay search summary", payload.SendOptions.AdditionalContext[0].Description);
        Assert.Contains("Found two architecture docs.", payload.SendOptions.AdditionalContext[0].Text);
        Assert.Contains("Latest ContextRelay search summary", payload.Labels);
    }
}
