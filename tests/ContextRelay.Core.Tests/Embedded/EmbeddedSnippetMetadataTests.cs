using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ContextRelay.Core.Chat;
using ContextRelay.Core.Embedded;
using ContextRelay.Core.Handoff;
using ContextRelay.Core.Models;
using ContextRelay.Core.Router;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Snippets;
using ContextRelay.Core.Utilities;
using Xunit;

namespace ContextRelay.Core.Tests.Embedded;

public sealed class EmbeddedSnippetMetadataTests
{
    private const string ContainerTypeId = "4a9c1d2e-0b3f-4c5d-8e6f-7a8b9c0d1e2f";

    [Fact]
    public void ContextSourceSchema_RoundTripsEverySource()
    {
        foreach (var source in Enum.GetValues(typeof(ContextSource)).Cast<ContextSource>())
        {
            var value = ContextSourceSchema.ToSchemaValue(source);

            Assert.True(ContextSourceSchema.TryParse(value, out var parsed));
            Assert.Equal(source, parsed);
            Assert.True(ContextSourceSchema.TryParse(value.ToUpperInvariant(), out _));
            ContextSourceSchema.ToSnippetSource(source);
        }

        Assert.Equal("sharepoint-embedded", ContextSourceSchema.ToSchemaValue(ContextSource.SharePointEmbedded));
        Assert.False(ContextSourceSchema.TryParse("SharePointEmbedded", out _));
        Assert.False(ContextSourceSchema.TryParse(null, out _));
    }

    [Fact]
    public void ContextItemKeys_ForEmbedded_IncludesLowercaseContainerTypeId()
    {
        var item = CreateItem();
        item.Metadata[EmbeddedMetadataKeys.ContainerTypeId] = ContainerTypeId.ToUpperInvariant();

        var key = ContextItemKeys.Build(item);

        Assert.Equal($"sharepoint-embedded::{ContainerTypeId}::https://contoso.example/app/doc/1::Renewal terms", key);
        Assert.NotEqual(
            key,
            ContextItemKeys.Build(ContextSource.SharePointEmbedded, item.Title, item.Url, null, null, "11111111-2222-3333-4444-555555555555"));
        Assert.Equal("Mail::https://contoso.example/app/doc/1::Renewal terms", ContextItemKeys.Build(ContextSource.Mail, item.Title, item.Url, null, null, ContainerTypeId));
    }

    [Fact]
    public void BuildContextText_JoinsAllExtractsAndFallsBackToSnippet()
    {
        var item = CreateItem();

        Assert.Equal("First extract.\n\nSecond extract.", EmbeddedSnippetMetadata.BuildContextText(item));

        item.Extracts.Clear();
        Assert.Equal("First extract.", EmbeddedSnippetMetadata.BuildContextText(item));
    }

    [Fact]
    public void AddPinMetadata_RoundTripsThroughReadExtracts()
    {
        var item = CreateItem();
        var metadata = new Dictionary<string, JsonElement>();

        EmbeddedSnippetMetadata.AddPinMetadata(item, metadata);
        var snippet = new SharedSnippetItem
        {
            Source = ContextSourceSchema.SharePointEmbedded,
            Metadata = metadata
        };

        Assert.Equal("Contoso Legal", EmbeddedSnippetMetadata.TryGetString(snippet, EmbeddedMetadataKeys.SourceName));
        Assert.Equal(ContainerTypeId, EmbeddedSnippetMetadata.TryGetString(snippet, EmbeddedMetadataKeys.ContainerTypeId));
        Assert.Equal("Confidential", EmbeddedSnippetMetadata.TryGetString(snippet, EmbeddedMetadataKeys.SensitivityLabelDisplayName));
        Assert.False(metadata.ContainsKey(EmbeddedMetadataKeys.SensitivityLabelId));

        var extracts = EmbeddedSnippetMetadata.ReadExtracts(snippet);
        Assert.Equal(2, extracts.Count);
        Assert.Equal("First extract.", extracts[0].Text);
        Assert.Equal(0.4, extracts[0].RelevanceScore);
        Assert.Null(extracts[1].RelevanceScore);
        Assert.False(metadata[EmbeddedMetadataKeys.Extracts][1].TryGetProperty("relevanceScore", out _));
    }

    [Fact]
    public void ReadExtracts_IgnoresMissingOrMalformedValues()
    {
        var snippet = new SharedSnippetItem
        {
            Source = ContextSourceSchema.SharePointEmbedded,
            Metadata = new Dictionary<string, JsonElement>
            {
                [EmbeddedMetadataKeys.Extracts] = JsonSerializer.SerializeToElement(new object[] { "plain", new { text = 3 }, new { text = "kept" } })
            }
        };

        var extracts = EmbeddedSnippetMetadata.ReadExtracts(snippet);

        Assert.Equal("kept", Assert.Single(extracts).Text);
        Assert.Empty(EmbeddedSnippetMetadata.ReadExtracts(new SharedSnippetItem()));
    }

    [Fact]
    public void DescribeSource_NamesTheEmbeddedSourceAndKeepsOtherSourcesUnchanged()
    {
        Assert.Equal("SharePoint Embedded: Contoso Legal", EmbeddedSnippetMetadata.DescribeSource(CreatePin()));
        Assert.Equal("SharePoint Embedded", EmbeddedSnippetMetadata.DescribeSource(new SharedSnippetItem { Source = ContextSourceSchema.SharePointEmbedded }));
        Assert.Equal("mail", EmbeddedSnippetMetadata.DescribeSource(new SharedSnippetItem { Source = "mail" }));
    }

    [Fact]
    public async Task ChatPayload_SendsEmbeddedPinAsTextWithSourceNameAndNoFileResource()
    {
        var payload = await ChatContextPayloadBuilder.BuildAsync(
            Array.Empty<ContextRelay.Core.FileContext.ResolvedAttachment>(),
            new[] { CreatePin() },
            cancellationToken: TestContext.Current.CancellationToken);

        var context = Assert.Single(payload.SendOptions.AdditionalContext);
        Assert.Contains("Source: SharePoint Embedded: Contoso Legal (https://contoso.sharepoint.com/sites/legal/doc.docx)", context.Text);
        Assert.Contains("First extract.", context.Text);
        Assert.True(payload.SendOptions.ContextualResources is null || payload.SendOptions.ContextualResources.Files.Count == 0);
    }

    [Fact]
    public void AskPrompt_NamesTheEmbeddedSource()
    {
        var prompt = AskPromptBuilder.Build("summarize", new[] { CreatePin() });

        Assert.Contains("Source: SharePoint Embedded: Contoso Legal - https://contoso.sharepoint.com/sites/legal/doc.docx", prompt);
        Assert.Contains("First extract.", prompt);
    }

    [Fact]
    public async Task Handoff_NamesTheEmbeddedSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "ContextRelayVS.EmbeddedHandoffTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemSharedSessionStore(
                new SharedStoreOptions { RootDirectory = Path.Combine(root, "store"), ProducerId = "vs", ProducerVersion = "0.1.0-test" },
                new FakeClock(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero)));
            var generator = new HandoffDocumentGenerator(store, new FakeClock(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero)));

            var result = await generator.GenerateAsync(
                new HandoffContext { Snippets = new[] { CreatePin() } },
                new HandoffGenerationOptions { WorkspaceRoot = Path.Combine(root, "workspace") },
                cancellationToken);

            var handoff = await File.ReadAllTextAsync(result.HandoffPath!, cancellationToken);
            Assert.Contains("- **Source**: SharePoint Embedded: Contoso Legal", handoff);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ContextItem CreateItem()
    {
        return new ContextItem
        {
            Source = ContextSource.SharePointEmbedded,
            Title = "Renewal terms",
            Snippet = "First extract.",
            Url = "https://contoso.example/app/doc/1",
            Metadata = new Dictionary<string, string>
            {
                [EmbeddedMetadataKeys.SourceName] = "Contoso Legal",
                [EmbeddedMetadataKeys.ContainerTypeId] = ContainerTypeId,
                [EmbeddedMetadataKeys.SensitivityLabelDisplayName] = "Confidential"
            },
            Extracts = new List<ContextItemExtract>
            {
                new() { Text = "First extract.", RelevanceScore = 0.4 },
                new() { Text = "Second extract." }
            }
        };
    }

    private static SharedSnippetItem CreatePin()
    {
        var item = CreateItem();
        var metadata = new Dictionary<string, JsonElement>();
        EmbeddedSnippetMetadata.AddPinMetadata(item, metadata);
        return new SharedSnippetItem
        {
            Id = "pin-1",
            Name = item.Title,
            Source = ContextSourceSchema.SharePointEmbedded,
            SourceUrl = "https://contoso.sharepoint.com/sites/legal/doc.docx",
            Snippet = EmbeddedSnippetMetadata.BuildContextText(item),
            UpdatedAt = "2026-10-25T09:00:00Z",
            Metadata = metadata
        };
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
