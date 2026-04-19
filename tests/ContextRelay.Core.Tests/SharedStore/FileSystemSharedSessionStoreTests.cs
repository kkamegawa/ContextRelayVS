using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Utilities;
using Xunit;

namespace ContextRelay.Core.Tests.SharedStore;

public sealed class FileSystemSharedSessionStoreTests : IDisposable
{
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 4, 19, 12, 45, 0, TimeSpan.Zero));
    private readonly string tempDirectory;

    public FileSystemSharedSessionStoreTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "ContextRelayVS.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task UpsertSnippetsAsync_UsesLastWriterWinsAndPrunesExpiredTombstones()
    {
        var store = CreateStore();

        await store.UpsertSnippetsAsync(new[]
        {
            new SharedSnippetItem
            {
                Id = "snippet-1",
                CreatedAt = "2026-04-18T00:00:00Z",
                UpdatedAt = "2026-04-18T00:00:00Z",
                Name = "old",
                Source = "mail",
                Snippet = "first"
            },
            new SharedSnippetItem
            {
                Id = "snippet-2",
                CreatedAt = "2026-04-01T00:00:00Z",
                UpdatedAt = "2026-04-01T00:00:00Z",
                DeletedAt = "2026-04-05T00:00:00Z",
                Name = "expired tombstone",
                Source = "teams",
                Snippet = "deleted"
            }
        });

        var result = await store.UpsertSnippetsAsync(new[]
        {
            new SharedSnippetItem
            {
                Id = "snippet-1",
                CreatedAt = "2026-04-18T00:00:00Z",
                UpdatedAt = "2026-04-19T00:00:00Z",
                Name = "new",
                Source = "mail",
                Snippet = "second"
            }
        });

        Assert.Single(result);
        Assert.Equal("new", result[0].Name);
        Assert.Equal("second", result[0].Snippet);
    }

    [Fact]
    public async Task AppendChatHistoryAsync_DeduplicatesAndTrimsToRetention()
    {
        var store = CreateStore(retentionCount: 3);

        var result = await store.AppendChatHistoryAsync(new[]
        {
            CreateChatItem("1", "2026-04-19T00:00:01Z", "first"),
            CreateChatItem("2", "2026-04-19T00:00:02Z", "second"),
            CreateChatItem("2", "2026-04-19T00:00:03Z", "second-updated"),
            CreateChatItem("3", "2026-04-19T00:00:04Z", "third"),
            CreateChatItem("4", "2026-04-19T00:00:05Z", "fourth")
        });

        Assert.Equal(new[] { "2", "3", "4" }, result.Select(item => item.Id).ToArray());
        Assert.Equal("second-updated", result[0].Text);
    }

    [Fact]
    public async Task UpsertHandoffIndexAsync_UsesWorkspaceScopedLastWriterWins()
    {
        var store = CreateStore();

        await store.UpsertHandoffIndexAsync(new[]
        {
            new SharedHandoffIndexItem
            {
                WorkspaceRoot = @"D:\GitHub\kkamegawa\oss\ContextRelayVS\",
                UpdatedAt = "2026-04-18T00:00:00Z",
                Docs = new HandoffDocumentPaths { Plan = ".contextrelay/PLAN-old.md" }
            }
        });

        var result = await store.UpsertHandoffIndexAsync(new[]
        {
            new SharedHandoffIndexItem
            {
                WorkspaceRoot = @"D:\GitHub\kkamegawa\oss\ContextRelayVS",
                UpdatedAt = "2026-04-19T00:00:00Z",
                Docs = new HandoffDocumentPaths { Plan = @".contextrelay\PLAN.md" }
            }
        });

        Assert.Single(result);
        Assert.Equal(@"D:\GitHub\kkamegawa\oss\ContextRelayVS", result[0].WorkspaceRoot);
        Assert.Equal(".contextrelay/PLAN.md", result[0].Docs.Plan);
    }

    [Fact]
    public async Task UpsertSnippetsAsync_WritesSchemaFile()
    {
        var store = CreateStore();

        await store.UpsertSnippetsAsync(new[]
        {
            new SharedSnippetItem
            {
                Id = "snippet-1",
                CreatedAt = "2026-04-18T00:00:00Z",
                UpdatedAt = "2026-04-19T00:00:00Z",
                Name = "snippet",
                Source = "mail",
                Snippet = "hello"
            }
        });

        var schemaPath = Path.Combine(tempDirectory, "schema.json");
        Assert.True(File.Exists(schemaPath));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(schemaPath));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("vs", document.RootElement.GetProperty("updatedBy").GetString());
        Assert.Contains(
            "snippets.json",
            document.RootElement.GetProperty("files").EnumerateArray().Select(element => element.GetString()));
    }

    [Fact]
    public async Task UpsertSnippetsAsync_PreservesUnknownEnvelopeFields()
    {
        Directory.CreateDirectory(tempDirectory);
        var snippetsPath = Path.Combine(tempDirectory, "snippets.json");
        await File.WriteAllTextAsync(
            snippetsPath,
            """
            {
              "schemaVersion": 1,
              "updatedAt": "2026-04-18T00:00:00Z",
              "updatedBy": "vscode",
              "producerVersion": "0.1.0",
              "contentHash": "sha256:test",
              "futureField": { "enabled": true },
              "items": [
                {
                  "id": "snippet-1",
                  "createdAt": "2026-04-18T00:00:00Z",
                  "updatedAt": "2026-04-18T00:00:00Z",
                  "name": "old",
                  "source": "mail",
                  "snippet": "before"
                }
              ]
            }
            """);

        var store = CreateStore();
        await store.UpsertSnippetsAsync(new[]
        {
            new SharedSnippetItem
            {
                Id = "snippet-1",
                CreatedAt = "2026-04-18T00:00:00Z",
                UpdatedAt = "2026-04-19T00:00:00Z",
                Name = "new",
                Source = "mail",
                Snippet = "after"
            }
        });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(snippetsPath));
        Assert.True(document.RootElement.TryGetProperty("futureField", out var futureField));
        Assert.True(futureField.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task GetSnippetsAsync_QuarantinesCorruptJsonAndReturnsEmptyList()
    {
        Directory.CreateDirectory(tempDirectory);
        var snippetsPath = Path.Combine(tempDirectory, "snippets.json");
        await File.WriteAllTextAsync(snippetsPath, "{ not valid json");

        var store = CreateStore();
        var result = await store.GetSnippetsAsync();

        Assert.Empty(result);
        Assert.True(File.Exists(snippetsPath));
        Assert.Single(Directory.GetFiles(tempDirectory, "snippets.json.bak.*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private FileSystemSharedSessionStore CreateStore(int retentionCount = 200)
    {
        return new FileSystemSharedSessionStore(
            new SharedStoreOptions
            {
                RootDirectory = tempDirectory,
                ProducerId = "vs",
                ProducerVersion = "0.1.0-test",
                ChatHistoryRetentionCount = retentionCount
            },
            clock);
    }

    private static SharedChatHistoryItem CreateChatItem(string id, string timestamp, string text)
    {
        return new SharedChatHistoryItem
        {
            Id = id,
            Timestamp = timestamp,
            Role = "user",
            Text = text,
            Metadata = new Dictionary<string, System.Text.Json.JsonElement>()
        };
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
