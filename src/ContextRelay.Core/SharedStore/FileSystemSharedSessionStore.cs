using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ContextRelay.Core.Utilities;

namespace ContextRelay.Core.SharedStore;

public sealed class FileSystemSharedSessionStore : ISharedSessionStore
{
    private static readonly IReadOnlyList<string> ManagedFiles = new[]
    {
        "schema.json",
        "snippets.json",
        "chat-history.json",
        "handoff-index.json"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IClock clock;
    private readonly SharedStoreOptions options;

    public FileSystemSharedSessionStore(SharedStoreOptions options, IClock? clock = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            throw new ArgumentException("Shared store root directory must be configured.", nameof(options));
        }

        this.clock = clock ?? SystemClock.Instance;
    }

    public async Task<IReadOnlyList<SharedSnippetItem>> GetSnippetsAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await ReadEnvelopeAsync<SharedSnippetItem>(SharedStoreFileKind.Snippets, cancellationToken).ConfigureAwait(false);
        return envelope.Items;
    }

    public async Task<IReadOnlyList<SharedSnippetItem>> UpsertSnippetsAsync(IEnumerable<SharedSnippetItem> snippets, CancellationToken cancellationToken = default)
    {
        var merged = await UpdateFileAsync<SharedSnippetItem>(
            SharedStoreFileKind.Snippets,
            current => MergeSnippets(current, snippets ?? Array.Empty<SharedSnippetItem>()),
            cancellationToken).ConfigureAwait(false);

        return merged.Items;
    }

    public async Task<IReadOnlyList<SharedChatHistoryItem>> GetChatHistoryAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await ReadEnvelopeAsync<SharedChatHistoryItem>(SharedStoreFileKind.ChatHistory, cancellationToken).ConfigureAwait(false);
        return envelope.Items;
    }

    public async Task<IReadOnlyList<SharedChatHistoryItem>> AppendChatHistoryAsync(IEnumerable<SharedChatHistoryItem> historyItems, CancellationToken cancellationToken = default)
    {
        var merged = await UpdateFileAsync<SharedChatHistoryItem>(
            SharedStoreFileKind.ChatHistory,
            current => MergeChatHistory(current, historyItems ?? Array.Empty<SharedChatHistoryItem>()),
            cancellationToken).ConfigureAwait(false);

        return merged.Items;
    }

    public async Task<IReadOnlyList<SharedHandoffIndexItem>> GetHandoffIndexAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await ReadEnvelopeAsync<SharedHandoffIndexItem>(SharedStoreFileKind.HandoffIndex, cancellationToken).ConfigureAwait(false);
        return envelope.Items;
    }

    public async Task<IReadOnlyList<SharedHandoffIndexItem>> UpsertHandoffIndexAsync(IEnumerable<SharedHandoffIndexItem> handoffEntries, CancellationToken cancellationToken = default)
    {
        var merged = await UpdateFileAsync<SharedHandoffIndexItem>(
            SharedStoreFileKind.HandoffIndex,
            current => MergeHandoffIndex(current, handoffEntries ?? Array.Empty<SharedHandoffIndexItem>()),
            cancellationToken).ConfigureAwait(false);

        return merged.Items;
    }

    public Task ClearAsync(SharedStoreFileKind fileKind, CancellationToken cancellationToken = default)
    {
        return UpdateFileAsync<object>(fileKind, _ => Array.Empty<object>(), cancellationToken);
    }

    internal static string? TryReadContentHash(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.TryGetProperty("contentHash", out var hashProperty))
        {
            return hashProperty.GetString();
        }

        return null;
    }

    private async Task<SharedStoreEnvelope<TItem>> UpdateFileAsync<TItem>(
        SharedStoreFileKind fileKind,
        Func<IReadOnlyList<TItem>, IReadOnlyList<TItem>> merge,
        CancellationToken cancellationToken)
    {
        var path = GetDataFilePath(fileKind);
        Directory.CreateDirectory(options.RootDirectory);
        using var lockHandle = await AcquireLockAsync(path, cancellationToken).ConfigureAwait(false);

        var current = await ReadEnvelopeInternalAsync<TItem>(path, fileKind, cancellationToken).ConfigureAwait(false);
        var nextItems = merge(current.Items);
        var envelope = CreateEnvelope(nextItems, current.ExtensionData);
        await WriteEnvelopeAsync(path, envelope, cancellationToken).ConfigureAwait(false);
        await WriteSchemaFileAsync(cancellationToken).ConfigureAwait(false);
        return envelope;
    }

    private Task<SharedStoreEnvelope<TItem>> ReadEnvelopeAsync<TItem>(SharedStoreFileKind fileKind, CancellationToken cancellationToken)
    {
        var path = GetDataFilePath(fileKind);
        return ReadEnvelopeInternalAsync<TItem>(path, fileKind, cancellationToken);
    }

    private async Task<SharedStoreEnvelope<TItem>> ReadEnvelopeInternalAsync<TItem>(string path, SharedStoreFileKind fileKind, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return CreateEnvelope(Array.Empty<TItem>(), extensionData: null);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var envelope = await JsonSerializer.DeserializeAsync<SharedStoreEnvelope<TItem>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
                if (envelope is null || envelope.SchemaVersion != SharedStoreOptions.CurrentSchemaVersion)
                {
                    return await QuarantineAndResetAsync<TItem>(path, fileKind, cancellationToken).ConfigureAwait(false);
                }

                envelope.Items ??= new List<TItem>();
                envelope.ExtensionData ??= new Dictionary<string, JsonElement>();
                return envelope;
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return await QuarantineAndResetAsync<TItem>(path, fileKind, cancellationToken).ConfigureAwait(false);
            }
        }

        return await QuarantineAndResetAsync<TItem>(path, fileKind, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SharedStoreEnvelope<TItem>> QuarantineAndResetAsync<TItem>(string path, SharedStoreFileKind fileKind, CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(path))
            {
                var backupPath = $"{path}.bak.{clock.UtcNow:yyyyMMddTHHmmssfffZ}";
                File.Copy(path, backupPath, overwrite: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var emptyEnvelope = CreateEnvelope(Array.Empty<TItem>(), extensionData: null);
        await WriteEnvelopeAsync(path, emptyEnvelope, cancellationToken).ConfigureAwait(false);
        await WriteSchemaFileAsync(cancellationToken).ConfigureAwait(false);
        return emptyEnvelope;
    }

    private async Task WriteEnvelopeAsync<TItem>(string path, SharedStoreEnvelope<TItem> envelope, CancellationToken cancellationToken)
    {
        await WriteJsonAtomicAsync(path, envelope, cancellationToken).ConfigureAwait(false);
    }

    private SharedStoreEnvelope<TItem> CreateEnvelope<TItem>(IEnumerable<TItem> items, Dictionary<string, JsonElement>? extensionData)
    {
        var normalizedItems = items.ToList();
        return new SharedStoreEnvelope<TItem>
        {
            SchemaVersion = SharedStoreOptions.CurrentSchemaVersion,
            UpdatedAt = clock.UtcNow.ToString("O"),
            UpdatedBy = options.ProducerId,
            ProducerVersion = options.ProducerVersion,
            Items = normalizedItems,
            ContentHash = ComputeContentHash(normalizedItems),
            ExtensionData = extensionData is null
                ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>(extensionData)
        };
    }

    private async Task<FileStream> AcquireLockAsync(string dataFilePath, CancellationToken cancellationToken)
    {
        var lockPath = $"{dataFilePath}.lock";
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath) ?? options.RootDirectory);

        for (var attempt = 0; attempt < options.LockRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < options.LockRetryCount - 1)
            {
                await Task.Delay(options.LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException($"Failed to acquire shared-store lock for '{dataFilePath}'.");
    }

    private string GetDataFilePath(SharedStoreFileKind fileKind)
    {
        return Path.Combine(options.RootDirectory, GetFileName(fileKind));
    }

    private string GetSchemaFilePath()
    {
        return Path.Combine(options.RootDirectory, "schema.json");
    }

    private static string GetFileName(SharedStoreFileKind fileKind)
    {
        return fileKind switch
        {
            SharedStoreFileKind.Snippets => "snippets.json",
            SharedStoreFileKind.ChatHistory => "chat-history.json",
            SharedStoreFileKind.HandoffIndex => "handoff-index.json",
            _ => throw new ArgumentOutOfRangeException(nameof(fileKind), fileKind, "Unknown shared-store file kind.")
        };
    }

    private IReadOnlyList<SharedSnippetItem> MergeSnippets(IReadOnlyList<SharedSnippetItem> current, IEnumerable<SharedSnippetItem> incoming)
    {
        var cutoff = clock.UtcNow.Subtract(options.TombstoneRetention);
        var merged = current
            .Concat(incoming)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(GetSnippetUpdatedAt)
                .ThenByDescending(item => string.IsNullOrWhiteSpace(item.DeletedAt) ? 0 : 1)
                .First())
            .Where(item => item.DeletedAt is null || ParseDateTime(item.DeletedAt) >= cutoff)
            .OrderByDescending(GetSnippetUpdatedAt)
            .ToList();

        return merged;
    }

    private IReadOnlyList<SharedChatHistoryItem> MergeChatHistory(IReadOnlyList<SharedChatHistoryItem> current, IEnumerable<SharedChatHistoryItem> incoming)
    {
        var merged = current
            .Concat(incoming)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(GetChatTimestamp).First())
            .OrderBy(GetChatTimestamp)
            .ToList();

        var skipCount = Math.Max(0, merged.Count - options.ChatHistoryRetentionCount);
        return merged.Skip(skipCount).ToList();
    }

    private IReadOnlyList<SharedHandoffIndexItem> MergeHandoffIndex(IReadOnlyList<SharedHandoffIndexItem> current, IEnumerable<SharedHandoffIndexItem> incoming)
    {
        return current
            .Concat(incoming)
            .Where(item => !string.IsNullOrWhiteSpace(item.WorkspaceRoot))
            .GroupBy(item => NormalizeWorkspaceRoot(item.WorkspaceRoot), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(GetHandoffUpdatedAt).First())
            .Select(NormalizeHandoffIndexItem)
            .OrderBy(item => item.WorkspaceRoot, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task WriteSchemaFileAsync(CancellationToken cancellationToken)
    {
        var path = GetSchemaFilePath();
        using var lockHandle = await AcquireLockAsync(path, cancellationToken).ConfigureAwait(false);
        var existing = await ReadSchemaMetadataAsync(path, cancellationToken).ConfigureAwait(false);
        var metadata = new SharedStoreSchemaMetadata
        {
            SchemaVersion = SharedStoreOptions.CurrentSchemaVersion,
            UpdatedAt = clock.UtcNow.ToString("O"),
            UpdatedBy = options.ProducerId,
            ProducerVersion = options.ProducerVersion,
            Files = ManagedFiles.ToList(),
            ExtensionData = existing?.ExtensionData is null
                ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>(existing.ExtensionData)
        };

        await WriteJsonAtomicAsync(path, metadata, cancellationToken).ConfigureAwait(false);
    }

    private static SharedHandoffIndexItem NormalizeHandoffIndexItem(SharedHandoffIndexItem item)
    {
        var docs = item.Docs ?? new HandoffDocumentPaths();
        return new SharedHandoffIndexItem
        {
            WorkspaceRoot = NormalizeWorkspaceRoot(item.WorkspaceRoot),
            UpdatedAt = item.UpdatedAt,
            Docs = new HandoffDocumentPaths
            {
                Plan = NormalizeRelativeDocPath(docs.Plan),
                Tasks = NormalizeRelativeDocPath(docs.Tasks),
                TestPlan = NormalizeRelativeDocPath(docs.TestPlan),
                Handoff = NormalizeRelativeDocPath(docs.Handoff),
                ExtensionData = new Dictionary<string, JsonElement>(docs.ExtensionData)
            },
            ExtensionData = new Dictionary<string, JsonElement>(item.ExtensionData)
        };
    }

    private static string? NormalizeRelativeDocPath(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value)
            ? value
            : value.Replace('\\', '/');
    }

    private async Task<SharedStoreSchemaMetadata?> ReadSchemaMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await JsonSerializer.DeserializeAsync<SharedStoreSchemaMetadata>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? options.RootDirectory;
        Directory.CreateDirectory(directory);
        var tempPath = $"{path}.tmp.{Process.GetCurrentProcess().Id}.{Guid.NewGuid():N}";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var lastException = default(IOException);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                return;
            }
            catch (IOException ex) when (attempt < 2)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
        }

        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        throw lastException ?? new IOException($"Failed to atomically replace '{path}'.");
    }

    private static string ComputeContentHash<TItem>(IReadOnlyList<TItem> items)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(items, SerializerOptions);
        using var document = JsonDocument.Parse(jsonBytes);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonicalJson(document.RootElement, writer);
        }

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(buffer.ToArray());
        return $"sha256:{ToLowerHex(hashBytes)}";
    }

    private static void WriteCanonicalJson(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(item, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static DateTimeOffset GetSnippetUpdatedAt(SharedSnippetItem item) => ParseDateTime(item.UpdatedAt);

    private static DateTimeOffset GetChatTimestamp(SharedChatHistoryItem item) => ParseDateTime(item.Timestamp);

    private static DateTimeOffset GetHandoffUpdatedAt(SharedHandoffIndexItem item) => ParseDateTime(item.UpdatedAt);

    private static DateTimeOffset ParseDateTime(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
    }

    private static string NormalizeWorkspaceRoot(string value)
    {
        return value
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ToLowerHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            chars[index * 2] = GetHexChar(value >> 4);
            chars[index * 2 + 1] = GetHexChar(value & 0xF);
        }

        return new string(chars);
    }

    private static char GetHexChar(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
    }
}
