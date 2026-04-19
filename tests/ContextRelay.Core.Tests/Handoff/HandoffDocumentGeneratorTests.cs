using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContextRelay.Core.Handoff;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Utilities;
using Xunit;

namespace ContextRelay.Core.Tests.Handoff;

public sealed class HandoffDocumentGeneratorTests : IDisposable
{
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 4, 19, 14, 15, 0, TimeSpan.Zero));
    private readonly string tempDirectory;

    public HandoffDocumentGeneratorTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "ContextRelayVS.HandoffTests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task GenerateAsync_WritesAllDocumentsAndUpdatesSharedStoreIndex()
    {
        var workspaceRoot = Path.Combine(tempDirectory, "workspace");
        var store = CreateStore();
        var generator = new HandoffDocumentGenerator(store, clock);

        var result = await generator.GenerateAsync(
            new HandoffContext
            {
                SearchSummary = "Reviewed current architecture decisions.",
                Snippets = new[]
                {
                    new SharedSnippetItem
                    {
                        Id = "snippet-1",
                        Name = "Architecture",
                        Source = "sharepoint",
                        SourceUrl = "https://contoso.sharepoint.com/sites/engineering",
                        Snippet = "Important design note",
                        UpdatedAt = "2026-04-19T14:10:00Z"
                    }
                }
            },
            new HandoffGenerationOptions
            {
                WorkspaceRoot = workspaceRoot
            });

        Assert.Equal(Path.Combine(workspaceRoot, ".contextrelay"), result.OutputDirectory);
        Assert.Equal(4, result.WrittenFiles.Count);
        Assert.Contains("## Update (2026-04-19T14:15:00Z)", await File.ReadAllTextAsync(result.PlanPath));
        Assert.Contains("### Saved Handoff Excerpts", await File.ReadAllTextAsync(result.HandoffPath!));

        var index = await store.GetHandoffIndexAsync();
        Assert.Single(index);
        Assert.Equal(Path.GetFullPath(workspaceRoot), index[0].WorkspaceRoot);
        Assert.Equal(".contextrelay/PLAN.md", index[0].Docs.Plan);
        Assert.Equal(".contextrelay/TASKS.md", index[0].Docs.Tasks);
        Assert.Equal(".contextrelay/TEST_PLAN.md", index[0].Docs.TestPlan);
        Assert.Equal(".contextrelay/HANDOFF.md", index[0].Docs.Handoff);
    }

    [Fact]
    public async Task GenerateAsync_AppendsUpdatesWhenFilesAlreadyExist()
    {
        var generator = new HandoffDocumentGenerator(clock: clock);
        var outputDirectory = Path.Combine(tempDirectory, "handoff");

        var options = new HandoffGenerationOptions
        {
            OutputDirectory = outputDirectory,
            IncludeHandoffDocument = false
        };

        await generator.GenerateAsync(new HandoffContext(), options);
        await generator.GenerateAsync(new HandoffContext
        {
            SearchSummary = "Second update"
        }, options);

        var planContents = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "PLAN.md"));
        Assert.Equal(2, CountOccurrences(planContents, "## Update (2026-04-19T14:15:00Z)"));
        Assert.Equal(3, Directory.GetFiles(outputDirectory).Length);
        Assert.Contains("_No snippets saved._", planContents);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private FileSystemSharedSessionStore CreateStore()
    {
        return new FileSystemSharedSessionStore(
            new SharedStoreOptions
            {
                RootDirectory = Path.Combine(tempDirectory, "shared"),
                ProducerId = "vs",
                ProducerVersion = "0.1.0-test"
            },
            clock);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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
