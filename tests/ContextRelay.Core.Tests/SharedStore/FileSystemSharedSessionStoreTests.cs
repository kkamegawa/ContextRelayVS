using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        var cancellationToken = TestContext.Current.CancellationToken;
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
        }, cancellationToken);

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
        }, cancellationToken);

        Assert.Single(result);
        Assert.Equal("new", result[0].Name);
        Assert.Equal("second", result[0].Snippet);
    }

    [Fact]
    public async Task AppendChatHistoryAsync_DeduplicatesAndTrimsToRetention()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = CreateStore(retentionCount: 3);

        var result = await store.AppendChatHistoryAsync(new[]
        {
            CreateChatItem("1", "2026-04-19T00:00:01Z", "first"),
            CreateChatItem("2", "2026-04-19T00:00:02Z", "second"),
            CreateChatItem("2", "2026-04-19T00:00:03Z", "second-updated"),
            CreateChatItem("3", "2026-04-19T00:00:04Z", "third"),
            CreateChatItem("4", "2026-04-19T00:00:05Z", "fourth")
        }, cancellationToken);

        Assert.Equal(new[] { "2", "3", "4" }, result.Select(item => item.Id).ToArray());
        Assert.Equal("second-updated", result[0].Text);
    }

    [Fact]
    public async Task AppendChatHistoryAsync_ReplacesMatchingTimestampWithIncomingItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = CreateStore();

        await store.AppendChatHistoryAsync(new[]
        {
            CreateChatItem("1", "2026-04-19T00:00:01Z", "first"),
            CreateChatItem("2", "2026-04-19T00:00:02Z", "second"),
            CreateChatItem("3", "2026-04-19T00:00:03Z", "third")
        }, cancellationToken);

        var result = await store.AppendChatHistoryAsync(new[]
        {
            CreateChatItem("2", "2026-04-19T00:00:02Z", "second-updated")
        }, cancellationToken);

        Assert.Equal(new[] { "1", "2", "3" }, result.Select(item => item.Id).ToArray());
        Assert.Equal("second-updated", result[1].Text);
        Assert.Equal("2026-04-19T00:00:02Z", result[1].Timestamp);
    }

    [Fact]
    public async Task UpsertHandoffIndexAsync_UsesWorkspaceScopedLastWriterWins()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = CreateStore();

        await store.UpsertHandoffIndexAsync(new[]
        {
            new SharedHandoffIndexItem
            {
                WorkspaceRoot = @"D:\GitHub\kkamegawa\oss\ContextRelayVS\",
                UpdatedAt = "2026-04-18T00:00:00Z",
                Docs = new HandoffDocumentPaths { Plan = ".contextrelay/PLAN-old.md" }
            }
        }, cancellationToken);

        var result = await store.UpsertHandoffIndexAsync(new[]
        {
            new SharedHandoffIndexItem
            {
                WorkspaceRoot = @"D:\GitHub\kkamegawa\oss\ContextRelayVS",
                UpdatedAt = "2026-04-19T00:00:00Z",
                Docs = new HandoffDocumentPaths { Plan = @".contextrelay\PLAN.md" }
            }
        }, cancellationToken);

        Assert.Single(result);
        Assert.Equal(@"D:\GitHub\kkamegawa\oss\ContextRelayVS", result[0].WorkspaceRoot);
        Assert.Equal(".contextrelay/PLAN.md", result[0].Docs.Plan);
    }

    [Fact]
    public async Task UpsertSnippetsAsync_WritesSchemaFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
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
        }, cancellationToken);

        var schemaPath = Path.Combine(tempDirectory, "schema.json");
        Assert.True(File.Exists(schemaPath));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(schemaPath, cancellationToken));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("vs", document.RootElement.GetProperty("updatedBy").GetString());
        Assert.Contains(
            "snippets.json",
            document.RootElement.GetProperty("files").EnumerateArray().Select(element => element.GetString()));
    }

    [Fact]
    public async Task UpsertSnippetsAsync_PreservesUnknownEnvelopeFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
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
            """,
            cancellationToken);

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
        }, cancellationToken);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(snippetsPath, cancellationToken));
        Assert.True(document.RootElement.TryGetProperty("futureField", out var futureField));
        Assert.True(futureField.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task GetSnippetsAsync_QuarantinesCorruptJsonAndReturnsEmptyList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(tempDirectory);
        var snippetsPath = Path.Combine(tempDirectory, "snippets.json");
        await File.WriteAllTextAsync(snippetsPath, "{ not valid json", cancellationToken);

        var store = CreateStore();
        var result = await store.GetSnippetsAsync(cancellationToken);

        Assert.Empty(result);
        Assert.True(File.Exists(snippetsPath));
        Assert.Single(Directory.GetFiles(tempDirectory, "snippets.json.bak.*"));
    }

    [Fact]
    public async Task UpsertSnippetsAsync_RegistersContentHashWithWatcher()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(tempDirectory);

        var options = new SharedStoreOptions
        {
            RootDirectory = tempDirectory,
            ProducerId = "vs",
            ProducerVersion = "0.1.0-test"
        };

        using var watcher = new SharedStoreWatcher(tempDirectory);
        var store = new FileSystemSharedSessionStore(options, clock, watcher);

        await store.UpsertSnippetsAsync(new[]
        {
            new SharedSnippetItem
            {
                Id = "s1",
                CreatedAt = "2026-04-19T00:00:00Z",
                UpdatedAt = "2026-04-19T00:00:00Z",
                Name = "Test",
                Source = "mail",
                Snippet = "Hello"
            }
        }, cancellationToken);

        var snippetsPath = Path.Combine(tempDirectory, "snippets.json");
        var fileContentHash = ReadContentHashFromFile(snippetsPath);
        var registeredHash = watcher.TryGetLastWrittenHash(SharedStoreFileKind.Snippets);

        Assert.NotNull(fileContentHash);
        Assert.NotNull(registeredHash);
        Assert.Equal(fileContentHash, registeredHash);
    }

    [Fact]
    public async Task AppendChatHistoryAsync_RegistersContentHashWithWatcher()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(tempDirectory);

        var options = new SharedStoreOptions
        {
            RootDirectory = tempDirectory,
            ProducerId = "vs",
            ProducerVersion = "0.1.0-test"
        };

        using var watcher = new SharedStoreWatcher(tempDirectory);
        var store = new FileSystemSharedSessionStore(options, clock, watcher);

        await store.AppendChatHistoryAsync(new[]
        {
            new SharedChatHistoryItem
            {
                Id = "c1",
                Timestamp = "2026-04-19T00:00:00Z",
                Role = "user",
                Text = "Hi",
                Metadata = new Dictionary<string, JsonElement>()
            }
        }, cancellationToken);

        var historyPath = Path.Combine(tempDirectory, "chat-history.json");
        var fileContentHash = ReadContentHashFromFile(historyPath);
        var registeredHash = watcher.TryGetLastWrittenHash(SharedStoreFileKind.ChatHistory);

        Assert.NotNull(fileContentHash);
        Assert.NotNull(registeredHash);
        Assert.Equal(fileContentHash, registeredHash);
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

    private static string? ReadContentHashFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty("contentHash", out var prop) ? prop.GetString() : null;
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
