using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ContextRelay.Core.SharedStore;
using ContextRelay.Core.Snippets;
using ContextRelay.Core.Utilities;
using Xunit;

namespace ContextRelay.Core.Tests.Snippets;

public sealed class SharedSnippetRepositoryTests : IDisposable
{
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 4, 19, 13, 10, 0, TimeSpan.Zero));
    private readonly string tempDirectory;

    public SharedSnippetRepositoryTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "ContextRelayVS.SnippetTests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task SaveAsync_PersistsSnippetInSharedStore()
    {
        using var repository = CreateRepository();

        var saved = await repository.SaveAsync(new SaveSnippetRequest
        {
            Name = "Architecture",
            Source = SnippetSource.SharePoint,
            Snippet = "Important design note",
            SourceUrl = "https://contoso.sharepoint.com/sites/engineering"
        });

        var all = await repository.GetAllAsync();

        Assert.Single(all);
        Assert.Equal(saved.Id, all[0].Id);
        Assert.Equal("sharepoint", all[0].Source);
    }

    [Fact]
    public async Task DeleteAsync_WritesTombstoneAndHidesSnippetFromDefaultReads()
    {
        using var repository = CreateRepository();
        var saved = await repository.SaveAsync(new SaveSnippetRequest
        {
            Name = "Mail snippet",
            Source = SnippetSource.Mail,
            Snippet = "Budget decision"
        });

        var deleted = await repository.DeleteAsync(saved.Id);

        var active = await repository.GetAllAsync();
        var withDeleted = await repository.GetAllAsync(includeDeleted: true);

        Assert.True(deleted);
        Assert.Empty(active);
        Assert.Single(withDeleted);
        Assert.False(string.IsNullOrWhiteSpace(withDeleted[0].DeletedAt));
    }

    [Fact]
    public async Task ClearAsync_RemovesAllSnippets()
    {
        using var repository = CreateRepository();
        await repository.SaveAsync(new SaveSnippetRequest
        {
            Name = "OneDrive snippet",
            Source = SnippetSource.OneDrive,
            Snippet = "Quarterly report"
        });

        await repository.ClearAsync();

        Assert.Empty(await repository.GetAllAsync(includeDeleted: true));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private SharedSnippetRepository CreateRepository()
    {
        var options = new SharedStoreOptions
        {
            RootDirectory = tempDirectory,
            ProducerId = "vs",
            ProducerVersion = "0.1.0-test"
        };

        return new SharedSnippetRepository(options, clock);
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
