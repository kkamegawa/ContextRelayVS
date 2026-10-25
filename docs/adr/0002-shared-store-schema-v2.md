# ADR 0002: Bump the shared store to schema version 2 for the Embedded source

[日本語](0002-shared-store-schema-v2_ja.md)

## Record Metadata

- Status: accepted
- Owner: unknown
- Created: 2026-10-25
- Last updated: 2026-10-25
- Documentation approval: approved (2026-10-25, user approval at the #224 design gate)
- Tracking: [Issue #223](https://github.com/kkamegawa/ContextRelayVS/issues/223), [Issue #225](https://github.com/kkamegawa/ContextRelayVS/issues/225)
- Related records: [Shared-session schema](../shared-session-schema.md), [ADR 0001](0001-sharepoint-embedded-retrieval.md)

## Context and Requirements

- Pinned Embedded snippets need a new `source` value. The schema document says `source` is a closed set and that a new source requires a `schemaVersion` bump.
- Verified implementation today:
  - `SharedStoreOptions.CurrentSchemaVersion` is 1 and is shared by every store file.
  - `FileSystemSharedSessionStore` quarantines and resets a file whose `schemaVersion` differs.
  - `SharedSnippetRepository` already writes `onenote`, `planner` and `todo`. Commit `bbc2223` added them without a bump, and the schema document still lists only six values.
  - The tool window parses stored values with `Enum.TryParse<ContextSource>`, which cannot parse a hyphenated value and would silently fall back to the default member.
- The VS Code extension stores snippets in `workspaceState` and does not use the shared store yet (S1), so a bump does not affect it today.
- The project is in early development. Backward compatibility and data migration are not required.
- On 2026-10-25 the user was told that keeping version 1 would avoid any reset, and chose the bump.

## Alternatives

| Option | Requirement fit | Benefits | Costs / risks | Evidence |
|--------|-----------------|----------|---------------|----------|
| Bump to version 2 without migration | Full | Follows the documented rule; older readers cannot misread the new value | Pins, chat history and the handoff index reset once | Schema §4, §12 |
| Keep version 1 and relax the rule | Partial | No reset | Breaks the documented contract a second time; older readers mislabel the value | `bbc2223` |
| Bump with a migration routine | Full | Keeps existing data | Migration work that early development does not require | User instruction |

## Current Decision

- Add `sharepoint-embedded` to the `source` set and raise `schemaVersion` to 2.
- Correct the documented `source` set to the values already written: `mail`, `teams`, `sharepoint`, `onedrive`, `connectors`, `onenote`, `planner`, `todo`, `sharepoint-embedded` and `chat`.
- Do not ship a migration. Every store file written with version 1 is quarantined and reset once, by the existing corruption handling.
- Embedded snippet metadata uses `sourceName`, `containerTypeId`, `extracts` (an array of `{text, relevanceScore?}`), `sensitivityLabelDisplayName` and `sensitivityLabelId`. The first two keep the upstream names.
- Readers map `sharepoint-embedded` to the Embedded source explicitly instead of relying on enum name parsing.

## Consequences and Review Triggers

- Users lose existing pins, chat history and handoff pointers once. The release notes state this.
- A released version 1 extension and a version 2 build quarantine each other's files. Settings and the shared store are shared by the Experimental Instance and normal Visual Studio, so the validation guide tells testers to disable the released extension.
- When the VS Code extension adopts the shared store, it starts at version 2 with this source set.
- Reconsider before any public release that must keep user data: a later bump then needs a migration routine.

## Sources

| ID | Title | Exact URL | Checked on | Relevant claim / section | Relevance to this decision |
|----|-------|-----------|------------|--------------------------|----------------------------|
| S1 | ContextRelay `src/snippets/snippetStore.ts` | https://github.com/kkamegawa/ContextRelay/blob/main/src/snippets/snippetStore.ts | 2026-10-25 | Snippets are stored in `workspaceState` | The bump does not affect the VS Code extension |

## Change History

This record uses approved in-place updates rather than append-only replacement ADRs. Preserve all history rows.

| Date | Previous decision summary | New decision summary | Reason | Source IDs | Approval reference |
|------|---------------------------|----------------------|--------|------------|--------------------|
| 2026-10-25 | Initial proposal | Schema version 2 with `sharepoint-embedded`, no migration | New closed-set source value; user reconfirmed the bump | S1 | User approval, 2026-10-25 (#224) |
