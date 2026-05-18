using System;
using ContextRelay.Core.Chat;
using ContextRelay.Core.FileContext;
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
    public void Build_TrimsFileResourceUrisBeforeSending()
    {
        var payload = ChatContextPayloadBuilder.Build(new[]
        {
            new SharedSnippetItem
            {
                Name = "Design doc",
                Source = "sharepoint",
                SourceUrl = "  https://contoso.sharepoint.com/sites/eng/Shared%20Documents/design.docx  ",
                Snippet = "Design details"
            }
        });

        Assert.Equal("https://contoso.sharepoint.com/sites/eng/Shared%20Documents/design.docx", payload.SendOptions.ContextualResources?.Files[0].Uri);
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
    public void Build_ReportsAccurateTruncatedCharacterCount()
    {
        var longText = new string('x', ChatContextPayloadBuilder.MaxChatContextChars + 100);
        var payload = ChatContextPayloadBuilder.Build(System.Array.Empty<SharedSnippetItem>(), longText);
        var truncated = payload.SendOptions.AdditionalContext[0].Text;
        var markerIndex = truncated.IndexOf("\n[truncated ", StringComparison.Ordinal);

        Assert.True(markerIndex > 0);

        var marker = truncated.Substring(markerIndex);
        var omittedCharsText = marker.Replace("\n[truncated ", string.Empty, StringComparison.Ordinal)
            .Replace(" chars]", string.Empty, StringComparison.Ordinal);
        var omittedChars = int.Parse(omittedCharsText, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(longText.Length - omittedChars, markerIndex);
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

    [Fact]
    public void Build_MergesSharePointAndLocalFileResources()
    {
        var payload = ChatContextPayloadBuilder.Build(
            new[]
            {
                new SharedSnippetItem
                {
                    Name = "Design doc",
                    Source = "sharepoint",
                    SourceUrl = "https://contoso.sharepoint.com/sites/eng/design.docx",
                    Snippet = "Design"
                }
            },
            localFiles: new[]
            {
                new ResolvedFileMention
                {
                    AbsolutePath = @"C:\repo\README.md",
                    WorkspaceRoot = @"C:\repo",
                    RelativePath = "README.md",
                    Uri = "file:///C:/repo/README.md"
                }
            });

        Assert.Equal(2, payload.SendOptions.ContextualResources?.Files.Count);
        Assert.Contains("Design doc", payload.Labels);
        Assert.Contains("Local file: README.md", payload.Labels);
    }
}
