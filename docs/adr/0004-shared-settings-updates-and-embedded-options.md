# ADR 0004: Update shared settings with read-merge-write and add an Embedded Options page

[日本語](0004-shared-settings-updates-and-embedded-options_ja.md)

## Record Metadata

- Status: accepted
- Owner: unknown
- Created: 2026-10-25
- Last updated: 2026-10-25
- Documentation approval: approved (2026-10-25, user approval at the #224 design gate)
- Tracking: [Issue #223](https://github.com/kkamegawa/ContextRelayVS/issues/223), [Issue #227](https://github.com/kkamegawa/ContextRelayVS/issues/227)
- Related records: [Design plan §8](../plan.md#8-options-dialogpage), [ADR 0001](0001-sharepoint-embedded-retrieval.md)

## Context and Requirements

- Verified implementation today:
  - `ContextRelayOptionsPage.CreateSettingsSnapshot()` builds a complete snapshot from the page and saves it over `%AppData%\ContextRelay\settings.json`.
  - The out-of-process host also writes that file, for example when the UI language changes.
  - `ContextRelaySettingsStore` replaces the file atomically with `File.Replace`, which prevents torn writes but not lost updates between processes.
  - The `PatchPkgdefFile` build task replaces every `@="#1"` with `General` and sets `IsInUnifiedSettings` to 0, so it supports only one page. `build/EnableUnifiedSettings.ps1` hardcodes `ContextRelay\General`.
  - The net48 package has no `UseWPF`, and the General page uses hard-coded English `DisplayName` and `Category` attributes.
- Embedded sources are a list of records with per-field validation, which a property grid edits poorly.
- `UIElementDialogPage` hosts WPF content in an Options page with keyboard navigation (S1). `DialogPage.OnApply` lets a page cancel Apply (S2).

## Alternatives

| Option | Requirement fit | Benefits | Costs / risks | Evidence |
|--------|-----------------|----------|---------------|----------|
| Read-merge-write under a named mutex; WPF Embedded page | Full | No lost updates; usable list editing and validation | Adds WPF to the net48 package | S1, S2 |
| Keep full-snapshot saves; add Embedded to the property grid | Poor | Smallest change | Two pages in one OK click overwrite each other; list editing is awkward | Current code |
| Unified Settings registration | Partial | Modern settings UI | Not adopted by this extension yet; larger migration | Current pkgdef patch |

## Current Decision

- Add an update API to `ContextRelaySettingsStore` that reads the latest file, applies only the caller's changes and saves atomically. A cross-process named mutex guards the whole read-change-save sequence. Every writer uses it: both Options pages in devenv and the out-of-process host.
- The General and Embedded pages each change only their own fields. Full-snapshot saves are removed.
- Add a SharePoint Embedded page under Tools > Options > ContextRelay, hosted by `UIElementDialogPage` in the net48 package with `UseWPF`. It contains the enable checkbox, a prerequisites notice (preview, pay-as-you-go, Global only, owning-app grant), the source list with Add / Edit / Remove, per-field validation and a warning when the cloud is not Global.
- Apply is cancelled and errors are shown when validation fails. Cancel discards edits and reloads the saved values.
- The options and runtime share one validator: names are 1-64 characters after trimming and unique ignoring case; container type IDs are GUIDs, normalized to lowercase `D` format and unique.
- Change `PatchPkgdefFile` to set display names per page key and generalize `EnableUnifiedSettings.ps1` so both pages keep `IsInUnifiedSettings=0`.
- Move the General page to `Localized*` attributes with English and Japanese resources, grouped as General, Chat, Authentication, Caching, Search sources, Diagnostics and Privacy.

## Consequences and Review Triggers

- Concurrent saves from devenv and the host no longer lose each other's changes.
- `InProcPackageVsixPackagingTests` must verify both page registrations and display names in the built VSIX.
- A released extension that still writes full snapshots can erase Embedded settings, so validation runs with the released extension disabled.
- Reconsider when the extension adopts Unified Settings.

## Sources

| ID | Title | Exact URL | Checked on | Relevant claim / section | Relevance to this decision |
|----|-------|-----------|------------|--------------------------|----------------------------|
| S1 | UIElementDialogPage Class | https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.uielementdialogpage | 2026-10-25 | Hosts WPF content in a dialog page with tabbing and keyboard navigation | Embedded page host |
| S2 | DialogPage.OnApply Method | https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.dialogpage.onapply | 2026-10-25 | Handles Apply; overriding implementations control saving | Validation-gated Apply |

## Change History

This record uses approved in-place updates rather than append-only replacement ADRs. Preserve all history rows.

| Date | Previous decision summary | New decision summary | Reason | Source IDs | Approval reference |
|------|---------------------------|----------------------|--------|------------|--------------------|
| 2026-10-25 | Initial proposal | Read-merge-write settings updates and a WPF Embedded Options page | Lost updates between two pages and the host; list editing for Embedded sources | S1, S2 | User approval, 2026-10-25 (#224) |
