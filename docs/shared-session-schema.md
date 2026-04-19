# ContextRelay Shared-Session Schema v1

This document defines the on-disk format used by **both** the VS Code extension (`ContextRelay`) and the Visual Studio extension (`ContextRelayVS`) to share per-user session data on the same machine.

Both extensions MUST implement this schema identically. When either side changes the schema, bump `schemaVersion` and land a corresponding PR in the other repository **before** shipping.

---

## 1. Scope

Shared:

- Pinned snippets
- Chat / search history (recent N items)
- Handoff-document pointer index (per solution/workspace)

**Not** shared:

- MSAL tokens or any authentication material
- Graph API responses (raw payloads)
- Cache of search results
- Settings / configuration

## 2. Storage location (Windows, user scope)

```
%LocalAppData%\ContextRelay\shared\
├─ schema.json          # schema metadata
├─ snippets.json
├─ chat-history.json
└─ handoff-index.json
```

On macOS (VS Code only): `~/Library/Application Support/ContextRelay/shared/`
On Linux (VS Code only): `${XDG_DATA_HOME:-~/.local/share}/ContextRelay/shared/`

Permissions: the store MUST remain under the user's profile directory with OS-default ACLs. The VS extension and VS Code extension MUST NOT change ACLs.

## 2.1 File: `schema.json`

`schema.json` is a lightweight metadata file written whenever either extension updates the shared store.

```jsonc
{
  "schemaVersion": 1,
  "updatedAt": "2026-04-19T12:34:56.789Z",
  "updatedBy": "vs",
  "producerVersion": "0.1.0",
  "files": [
    "schema.json",
    "snippets.json",
    "chat-history.json",
    "handoff-index.json"
  ]
}
```

- `files` is the authoritative list of managed files under the shared directory.
- Unknown top-level fields in `schema.json` MUST be preserved by future writers, just like the data envelopes.

## 3. Common envelope

Every JSON file in the shared store has this shape:

```jsonc
{
  "schemaVersion": 1,
  "updatedAt": "2026-04-19T12:34:56.789Z",   // ISO 8601 UTC
  "updatedBy": "vs",                          // "vs" | "vscode"
  "producerVersion": "0.1.0",                  // extension version that wrote the file
  "contentHash": "sha256:...",                 // SHA-256 of canonical serialization of payload (see §7)
  "items": [ /* payload, file-specific */ ]
}
```

## 4. File: `snippets.json`

```jsonc
{
  "schemaVersion": 1,
  "updatedAt": "...",
  "updatedBy": "vs",
  "producerVersion": "0.1.0",
  "contentHash": "sha256:...",
  "items": [
    {
      "id": "01J7Z3K7Q2VYP3H5A5N1W3E5YT",          // ULID or UUIDv7, stable identity
      "createdAt": "2026-04-18T09:15:00Z",
      "updatedAt": "2026-04-19T12:34:56Z",
      "name": "Weekly Q2 planning notes",
      "source": "teams",                             // "mail" | "teams" | "sharepoint" | "onedrive" | "connectors" | "chat"
      "sourceUrl": "https://teams.microsoft.com/...",
      "snippet": "…user-visible extract…",
      "metadata": {
        "authorDisplayName": "Jane Doe",
        "lastModifiedDateTime": "2026-04-17T21:00:00Z",
        "channelId": "19:...@thread.tacv2"
      },
      "deletedAt": null                              // tombstone; see §11
    }
  ]
}
```

- `id` MUST be stable across edits. ULID (Crockford base32, 26 chars) or UUIDv7 is required.
- `source` values are a closed set. New sources require a schema-version bump.
- `metadata` is a free-form object; unknown fields MUST be preserved by consumers.

## 5. File: `chat-history.json`

Append-only log, trimmed to at most 200 items (default) or the value configured per extension.

```jsonc
{
  "schemaVersion": 1,
  "items": [
    {
      "id": "01J7Z3M1S...",
      "timestamp": "2026-04-19T12:34:56Z",
      "role": "user",                       // "user" | "assistant" | "system"
      "text": "/mail Q2 kickoff",
      "metadata": { "slash": "mail" }
    }
  ]
}
```

## 6. File: `handoff-index.json`

Map from workspace/solution root path (normalized to a platform-native absolute path) to the most recently generated handoff documents.

```jsonc
{
  "schemaVersion": 1,
  "items": [
    {
      "workspaceRoot": "D:\\GitHub\\kkamegawa\\oss\\ContextRelayVS",
      "updatedAt": "2026-04-19T12:34:56Z",
      "docs": {
        "plan":      ".contextrelay/PLAN.md",
        "tasks":     ".contextrelay/TASKS.md",
        "testPlan":  ".contextrelay/TEST_PLAN.md",
        "handoff":   ".contextrelay/HANDOFF.md"
      }
    }
  ]
}
```

Paths under `docs` are **relative to `workspaceRoot`** using `/` separators (canonicalized) so the entry remains valid if the workspace moves.

## 7. Canonical serialization and `contentHash`

To make `contentHash` deterministic across platforms and serializers:

1. Start from the `items` array (not the envelope).
2. Serialize as UTF-8 JSON with:
   - Object keys sorted lexicographically.
   - No insignificant whitespace (no indentation, no trailing newline).
   - `\uXXXX` escapes only when required by JSON.
3. Compute SHA-256 over the resulting bytes. Encode as `"sha256:" + lowercase hex`.

## 8. Atomic write protocol

1. Acquire an exclusive lock on `<name>.lock` in the same directory (`FileShare.None` in .NET, `proper-lockfile` in Node). Retry up to 1 second with jitter; then fail.
2. Write the new payload to `<name>.tmp.<pid>.<rand>`.
3. `fsync` the temp file.
4. Replace the target atomically. On .NET this means `File.Replace(tmp, target, ...)` when the target already exists and `File.Move(tmp, target)` on first write; on Node it is `fs.renameSync(tmp, target)`.
5. Release the lock.

Readers open the target file with read-share; on transient rename collisions they retry up to 3 times with 50 ms backoff before treating the file as corrupt.

## 9. Corruption handling

If JSON parsing fails or `schemaVersion` is unknown and cannot be migrated:

1. Copy the bad file to `<name>.bak.<ISO8601>`.
2. Re-initialize with an empty `items` array and the local process's envelope.
3. Log a warning to the extension's debug output and expose a user-visible notification.

## 10. Change notification

- VS: `FileSystemWatcher` on the shared directory, filter `*.json`, `NotifyFilters.LastWrite | FileName`, debounce 200 ms.
- VS Code: `fs.watch` on the shared directory with equivalent debounce.
- Self-write suppression: after every write, the producer records the just-written `contentHash`. When the watcher fires and the target file's hash matches the last write, the event is ignored.

## 11. Conflict resolution

- **Snippets**: `id`-scoped last-writer-wins using `updatedAt`. Tombstones (`deletedAt != null`) are retained for 7 days and then pruned by either side on next write.
- **Chat history**: append-only, deduped by `id`. Items older than the configured retention (default 200) are dropped oldest-first on write.
- **Handoff index**: `workspaceRoot`-scoped last-writer-wins.

## 12. Schema evolution

- Unknown fields on reading: **preserve** and write back unchanged (forward-compatible for additive changes).
- Breaking changes: bump `schemaVersion` and ship a migration routine in both extensions **before** any writer emits the new version.

## 13. Privacy and data minimization

- Snippet `snippet` text SHOULD be a short extract. Full-document content MUST NOT be stored.
- `chat-history.json` SHOULD exclude system-prompt text that contains sensitive organizational data.
- Users can wipe the shared store via `ContextRelay: Clear Snippets` or by deleting the `shared/` directory.

## 14. Migration from legacy stores

- **VS Code**: on first run after adoption, if `globalState` contains `contextRelay.snippets` and the shared `snippets.json` has no items from this machine, copy the legacy snippets (assigning fresh `id`s if missing) and set a migration flag in `globalState` so the migration runs at most once.
- **VS**: no legacy store exists in the initial release.
