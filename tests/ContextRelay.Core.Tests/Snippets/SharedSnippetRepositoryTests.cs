using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
        var cancellationToken = TestContext.Current.CancellationToken;
        using var repository = CreateRepository();

        var saved = await repository.SaveAsync(new SaveSnippetRequest
        {
            Name = "Architecture",
            Source = SnippetSource.SharePoint,
            Snippet = "Important design note",
            SourceUrl = "https://contoso.sharepoint.com/sites/engineering"
        }, cancellationToken);

        var all = await repository.GetAllAsync(cancellationToken: cancellationToken);

        Assert.Single(all);
        Assert.Equal(saved.Id, all[0].Id);
        Assert.Equal("sharepoint", all[0].Source);
    }

    [Fact]
    public async Task DeleteAsync_WritesTombstoneAndHidesSnippetFromDefaultReads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var repository = CreateRepository();
        var saved = await repository.SaveAsync(new SaveSnippetRequest
        {
            Name = "Mail snippet",
            Source = SnippetSource.Mail,
            Snippet = "Budget decision"
        }, cancellationToken);

        var deleted = await repository.DeleteAsync(saved.Id, cancellationToken);

        var active = await repository.GetAllAsync(cancellationToken: cancellationToken);
        var withDeleted = await repository.GetAllAsync(includeDeleted: true, cancellationToken);

        Assert.True(deleted);
        Assert.Empty(active);
        Assert.Single(withDeleted);
        Assert.False(string.IsNullOrWhiteSpace(withDeleted[0].DeletedAt));
    }

    [Fact]
    public async Task ClearAsync_RemovesAllSnippets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var repository = CreateRepository();
        await repository.SaveAsync(new SaveSnippetRequest
        {
            Name = "OneDrive snippet",
            Source = SnippetSource.OneDrive,
            Snippet = "Quarterly report"
        }, cancellationToken);

        await repository.ClearAsync(cancellationToken);

        Assert.Empty(await repository.GetAllAsync(includeDeleted: true, cancellationToken));
    }

    [Fact]
    public void Constructor_WiresOwnedWatcherIntoStore()
    {
        using var repository = CreateRepository();

        var repositoryWatcher = ReadPrivateField<SharedStoreWatcher>(repository, "watcher");
        var sharedSessionStore = ReadPrivateField<ISharedSessionStore>(repository, "sharedSessionStore");
        var storeWatcher = ReadPrivateField<SharedStoreWatcher>(sharedSessionStore, "watcher");

        Assert.Same(repositoryWatcher, storeWatcher);
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

    private static T ReadPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return Assert.IsAssignableFrom<T>(field!.GetValue(instance));
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
