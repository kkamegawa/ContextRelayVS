using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ContextRelay.Core.Chat;
using ContextRelay.Core.FileContext;
using ContextRelay.Core.SharedStore;
using Xunit;

namespace ContextRelay.Core.Tests.Chat;

public sealed class ChatContextPayloadBuilderTests
{
    [Fact]
    public async Task BuildAsync_ReadsLocalAttachmentsBeforePinsAndKeepsSearchSummaryUngroundedOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "contextrelay-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "notes.md");
            File.WriteAllText(path, "local details");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));

            var payload = await ChatContextPayloadBuilder.BuildAsync(
                new[] { attachment! },
                new[] { new SharedSnippetItem { Name = "Pinned", Source = "mail", Snippet = "pinned details" } },
                "search orientation",
                TestContext.Current.CancellationToken);

            Assert.True(payload.HasGroundingContext);
            Assert.Equal(ChatContextPayloadBuilder.GroundingInstructionText, payload.GroundingInstruction);
            Assert.NotNull(payload.SendOptions.WebContext);
            Assert.False(payload.SendOptions.WebContext!.IsWebEnabled);
            Assert.Equal("notes.md", payload.SendOptions.AdditionalContext[0].Description);
            Assert.Contains("local details", payload.SendOptions.AdditionalContext[0].Text);
            Assert.Equal("Pinned", payload.SendOptions.AdditionalContext[1].Description);
            Assert.Contains("Latest ContextRelay search summary", payload.Labels);
            Assert.Equal(new[] { attachment!.Id }, payload.IncludedAttachmentIds);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_BoundsEachLocalFileAndSharedContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "contextrelay-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "large.md");
            File.WriteAllText(path, new string('x', ChatContextPayloadBuilder.MaxLocalAttachmentChars + 100));
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));
            var payload = await ChatContextPayloadBuilder.BuildAsync(
                new[] { attachment! },
                Array.Empty<SharedSnippetItem>(),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(payload.SendOptions.AdditionalContext[0].Text.Length <= ChatContextPayloadBuilder.MaxLocalAttachmentChars + 30);
            Assert.Contains("additional file content omitted", payload.SendOptions.AdditionalContext[0].Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_ReportsPinnedSnippetsOmittedWhenAttachmentBudgetIsExhausted()
    {
        var root = Path.Combine(Path.GetTempPath(), "contextrelay-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var attachments = new List<ResolvedAttachment>();
            for (var index = 0; index < 6; index++)
            {
                var path = Path.Combine(root, $"context-{index}.md");
                File.WriteAllText(path, new string('x', ChatContextPayloadBuilder.MaxLocalAttachmentChars + 100));
                Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));
                attachments.Add(attachment!);
            }

            var payload = await ChatContextPayloadBuilder.BuildAsync(
                attachments,
                new[] { new SharedSnippetItem { Name = "Omitted pinned snippet", Source = "mail", Snippet = "pinned details" } },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.DoesNotContain(payload.SendOptions.AdditionalContext, context => context.Description == "Omitted pinned snippet");
            Assert.Equal(0, payload.IncludedPinnedSnippetCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_UsesSelectedAttachmentLines()
    {
        var root = Path.Combine(Path.GetTempPath(), "contextrelay-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "notes.md");
            File.WriteAllText(path, "before\nselected\nafter");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));
            attachment!.SelectionStartLine = 2;
            attachment.SelectionEndLine = 2;

            var payload = await ChatContextPayloadBuilder.BuildAsync(
                new[] { attachment },
                Array.Empty<SharedSnippetItem>(),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.DoesNotContain("before", payload.SendOptions.AdditionalContext[0].Text);
            Assert.Contains("selected", payload.SendOptions.AdditionalContext[0].Text);
            Assert.DoesNotContain("after", payload.SendOptions.AdditionalContext[0].Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_DoesNotReportUnreadableAttachmentAsIncluded()
    {
        var root = Path.Combine(Path.GetTempPath(), "contextrelay-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "deleted.md");
            File.WriteAllText(path, "temporary");
            Assert.True(WorkspaceFileAttachmentResolver.TryResolve(path, new[] { root }, out var attachment));
            File.Delete(path);

            var payload = await ChatContextPayloadBuilder.BuildAsync(
                new[] { attachment! },
                Array.Empty<SharedSnippetItem>(),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(payload.IncludedAttachmentIds);
            Assert.Empty(payload.SendOptions.AdditionalContext);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
        Assert.False(payload.HasGroundingContext);
        Assert.Null(payload.GroundingInstruction);
        Assert.Null(payload.SendOptions.WebContext);
    }

    [Fact]
    public void Build_DoesNotTreatLocalFileLikeRemoteFileResource()
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
                },
                new SharedSnippetItem
                {
                    Name = "Local file: README.md",
                    Source = "local-file",
                    SourceUrl = "file:///C:/repo/README.md",
                    Snippet = "[File: README.md]\nLocal readme content"
                }
            });

        Assert.NotNull(payload.SendOptions.ContextualResources);
        Assert.Single(payload.SendOptions.ContextualResources!.Files);
        Assert.Single(payload.SendOptions.AdditionalContext);
        Assert.Contains("Design doc", payload.Labels);
        Assert.Contains("Local file: README.md", payload.SendOptions.AdditionalContext[0].Description);
        Assert.Contains("Source: local-file (file:///C:/repo/README.md)", payload.SendOptions.AdditionalContext[0].Text);
    }
}
