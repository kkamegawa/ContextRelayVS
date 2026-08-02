---
name: wpf-fluent-vs-extension-ui
description: "Create design specifications, theme-token mappings, and review criteria for Fluent-inspired WPF buttons and multiline text inputs used by Visual Studio extensions. Use when planning WPF UI for .NET 10 or VisualStudio.Extensibility, translating Fluent UI Web design intent to WPF, integrating Visual Studio theme colors, or preventing selected text from becoming invisible. This skill is design-only: it must not write or modify code, debug failures, run builds or tests, or implement the UI."
---

# WPF Fluent UI Design for Visual Studio Extensions

## Purpose

Produce an implementation-ready UI design specification for a small set of WPF controls used by a Visual Studio extension:

- Buttons.
- Multiline text input based on the standard WPF `TextBox`.
- Visual Studio theme integration.
- Safe text-selection foreground and background pairing.

Use the Fluent UI Web repository at `https://github.com/microsoft/fluentui` as a visual and interaction reference. Do not treat it as a WPF component library or runtime dependency.

## Responsibility boundary

This is a design and specification skill only.

### Allowed responsibilities

- Read relevant project files to understand the extension architecture and existing UI conventions.
- Read current Microsoft and Fluent UI documentation.
- Identify architectural constraints that affect the UI design.
- Define control variants, visual states, dimensions, semantic resources, and theme mappings.
- Produce acceptance criteria and a manual review matrix for another implementation agent or developer.
- Review an existing design or proposed resource mapping at the specification level.

### Prohibited responsibilities

- Do not create, edit, patch, or delete source files.
- Do not generate production XAML, C#, project files, manifests, tests, or scripts.
- Do not implement or refactor controls.
- Do not run restore, build, test, packaging, deployment, or formatting commands.
- Do not attach a debugger, inspect runtime traces, or diagnose implementation failures.
- Do not fix XAML binding errors, resource lookup errors, compilation failures, or rendering defects.
- Do not claim that the design has been implemented, built, tested, or visually verified.
- Do not expand into general Visual Studio extension architecture beyond what is necessary to define the UI contract.

When the user asks for implementation or debugging, provide the design specification and handoff requirements only. State which work must be performed by a coding or debugging workflow.

## Scope

Design only the controls requested by the user. By default, limit the design system to:

- Standard button.
- Primary or accent button when needed.
- Multiline text input.
- The states required for those controls.
- Shared semantic theme resources used by those controls.

Do not design a complete Fluent UI framework, icon system, navigation system, dialog framework, or custom text editor unless separately requested.

## Accessibility policy

Preserve the behavior supplied by standard WPF controls. Do not introduce a separate accessibility program or custom automation framework.

The design must not remove or obstruct:

- Keyboard focus.
- Tab navigation.
- Button keyboard activation.
- Text editing and selection.
- Clipboard operations.
- Undo and redo.
- IME behavior.
- Standard WPF automation peers.

The selected-text visibility requirement in this skill is a functional theming invariant, not an additional accessibility initiative.

## Reference hierarchy

Use sources in this order:

1. The target repository's extension model, supported Visual Studio versions, theme mechanism, and existing UI conventions.
2. Current Microsoft documentation for Visual Studio extensibility, Remote UI, WPF hosting, and theme resources.
3. Current Fluent UI Web Button and Textarea design intent, states, spacing, and tokens.
4. Standard WPF control behavior.

Do not mechanically copy React component structure, Griffel styles, CSS selectors, DOM assumptions, or Web Component APIs into the WPF specification.

## Architecture assessment

Before designing control resources, classify the UI host. This assessment is descriptive only and must not modify the project.

### VisualStudio.Extensibility out-of-process extension

Document that:

- The extension process may use a supported modern .NET runtime such as .NET 10.
- Remote UI XAML is materialized in the Visual Studio process.
- The design must remain compatible with Remote UI restrictions.
- Bindings, commands, supported triggers, serializable data, and supported resource dictionaries should be preferred.
- The design must not depend on extension-local custom controls or code-behind behavior unless the target SDK explicitly supports them.

### In-process VSSDK extension

Document that:

- The Visual Studio 2022 in-process component normally cannot simply be retargeted to .NET 10.
- A modern .NET companion process may be required when .NET 10 functionality is mandatory.
- The WPF design must use theme resources available inside the Visual Studio process.

### External companion WPF process

Document that:

- A normal `net10.0-windows` WPF process does not automatically inherit Visual Studio application resources.
- The architecture needs an explicit theme-value bridge or equivalent mechanism.
- Foreground/background pairs must be updated atomically when the Visual Studio theme changes.

If the user's requested architecture conflicts with the detected host model, report the conflict as a design constraint. Do not resolve it by editing targets or packages.

## Fluent-to-WPF translation principles

Describe the intended result using WPF concepts without writing implementation code.

- Prefer standard WPF `Button` and `TextBox` behavior.
- Keep the design compact enough for Visual Studio tool windows.
- Define spacing, corner radius, border thickness, typography, minimum sizes, and state changes as reusable design tokens.
- Prefer setters and state triggers conceptually; recommend a full control template only when the required visual result cannot be achieved by ordinary styling.
- Preserve command behavior, default and cancel semantics, content presentation, focus indication, and disabled behavior.
- Avoid decorative animation unless the host UI already uses it consistently.
- Use Fluent UI Web as a source for visual intent, not as a source of WPF dependency choices.

For every non-obvious dimension or state decision, identify the Fluent component, token, story, or Visual Studio convention that informed it.

## Semantic resource contract

Define semantic resource names independent of version-specific Visual Studio resource keys. An implementation may use names equivalent to:

- `VsFluent.Brush.Control.Background.Rest`
- `VsFluent.Brush.Control.Background.Hover`
- `VsFluent.Brush.Control.Background.Pressed`
- `VsFluent.Brush.Control.Background.Disabled`
- `VsFluent.Brush.Control.Border.Rest`
- `VsFluent.Brush.Control.Border.Focus`
- `VsFluent.Brush.Text.Primary`
- `VsFluent.Brush.Text.Disabled`
- `VsFluent.Brush.Text.Selection.Background`
- `VsFluent.Brush.Text.Selection.Foreground`
- `VsFluent.Brush.Text.Caret`
- `VsFluent.CornerRadius.Control`
- `VsFluent.Thickness.ControlPadding`
- `VsFluent.Thickness.TextAreaPadding`

For each semantic resource, specify:

- Purpose.
- Applicable controls and states.
- Preferred Visual Studio theme token or resource family.
- Fallback resource family.
- Whether it must update dynamically when the theme changes.
- Any foreground/background resource that must be treated as an inseparable pair.

Never invent a Visual Studio resource-key name. Mark a mapping as `To be verified against the target SDK` when the exact key has not been confirmed from the supported Visual Studio version.

## Button design contract

Define only the variants required by the feature. Normally include:

- Standard button.
- Primary or accent button when a prominent action is needed.

For each variant, specify:

- Rest state.
- Pointer hover state.
- Pressed state.
- Keyboard focus state.
- Disabled state.
- Default-button state when applicable.
- Foreground, background, border, and focus-indicator semantic resources.
- Padding, minimum height, corner radius, border thickness, typography, and content alignment.

Required design rules:

- The focus indicator remains visible in every supported Visual Studio theme.
- Disabled foreground and fill come from a coherent theme family.
- Primary-button text uses the foreground explicitly paired with its accent fill.
- Fixed white or black foregrounds are not acceptable unless the target Visual Studio token contract explicitly resolves to them.
- The design does not encode feature-specific labels or icons into the control style.

## Multiline text-input design contract

Base the design on the standard WPF `TextBox` with multiline behavior.

Specify:

- Multiline input and return-key behavior.
- Text wrapping policy.
- Vertical scrolling policy.
- Horizontal scrolling policy when wrapping is disabled.
- Rest, hover when applicable, focused, active-input, read-only, and disabled states.
- Foreground, background, border, focus, caret, and selection resources.
- Padding, minimum height, corner radius, border thickness, and typography.
- Active and inactive selection behavior when the host supports both.

Do not propose a custom text editor for a basic text-area requirement.

## Selected-text safety invariant

Selected text must never disappear because its foreground and selection background resolve to the same or visually indistinguishable color.

The design specification must define these three resources together:

- Selection background.
- Selection foreground.
- Caret foreground.

Mandatory rules:

1. `SelectionBrush` and `SelectionTextBrush` are an explicit foreground/background pair.
2. The pair comes from the same Visual Studio semantic token family whenever possible.
3. On Visual Studio versions that expose them, prefer the selected-text background token and its corresponding text-on-selected-background token, such as `AccentFillSelectedTextBackground` paired with `TextOnAccentFillSelectedText`; exact availability and key form must be verified against the target SDK.
4. When no reliable Visual Studio pair is available, specify the WPF system highlight background and system highlight text foreground as a pair.
5. Never pair a Visual Studio selection background with an unrelated normal text foreground merely because both look acceptable in one theme.
6. Never reuse the ordinary control background as the selected-text foreground.
7. Require dynamic theme updates for all three resources when supported by the host.
8. Require review in focused and unfocused selection states when inactive selection highlighting is used.

The acceptance criteria must explicitly require selection review using:

- Mouse drag.
- `Shift+Arrow`.
- `Ctrl+A`.
- Light theme.
- Dark theme.
- Blue or another standard supported theme.
- High Contrast as a compatibility smoke review when supported by the product.

This skill defines the review requirement but does not execute the application or perform the review itself.

## Keyboard-selected popup/list-item safety invariant

For every keyboard-navigable popup, suggestion list, or ListBox, the item represented by the bound `SelectedItem` or `IsSelected` state MUST expose a visible full-row selection background and an explicitly paired selection foreground. The implementation MUST NOT rely only on a caret, keyboard focus border, pointer hover, or a parent background showing through a nested Button or content template. If nested content renders the row, both the selected background and selected foreground MUST be applied through supported properties on that surface or explicitly inherited from the selected item. Do not assign `Foreground` directly to a WPF `ContentPresenter`, because it does not expose that member; propagate text color through the containing control's `Foreground`, text-element inheritance, or another property verified against the target WPF type and Remote UI XAML support.

For Remote UI data contracts, the implementation MUST NOT assume that object-reference identity is preserved when synchronizing `SelectedItem`. Unless identity preservation is explicitly verified, drive the rendered selection through a serialized per-item selected-state property consumed by a `DataTrigger`, and verify that the resulting row actually repaints.

Do NOT drive the rendered selection through `Selector.SelectedIndex`/`ListBoxItem.IsSelected` when the bound `ItemsSource` collection is replaced with a new instance on each update (for example, on every keystroke). WPF's `Selector` resets `SelectedIndex` to `-1` via `SetCurrentValue` whenever `ItemsSource` changes; this only overrides the effective value, not a data-bound base value, so a `Mode=OneWay` (or even `TwoWay`) binding that later re-pushes the same index is a no-op because WPF only re-evaluates a binding on a *changed* source value — the `-1` reset sticks and `IsSelected` never becomes `true` again. This failure mode is silent (no binding error, no exception) and was the cause of issue #164's keyboard-selection row never highlighting even after switching from `SelectedItem` to `SelectedIndex`. Verify the fix by confirming the selected row's background/foreground under repeated Up/Down after the source collection has been replaced at least once, not just on first render.

Do NOT rely on mutating a `[DataMember]` property on an item that is *already inside a previously sent collection* (e.g. flipping a per-item `IsSelected` bool and expecting its own `PropertyChanged` to reach the UI) as the sole mechanism for updating a rendered row, unless this has been explicitly verified to work for nested/child objects over the Remote UI boundary in the target Visual Studio version. This exact pattern was tried for issue #164 (a serialized per-item `IsSelected` plus a `DataTrigger`) and still did not repaint on keyboard navigation in practice, even though the same mechanism worked correctly for the *initial* render (when the item was freshly constructed with the flag already set) and for mouse hover (native `IsMouseOver`, not data-bound at all). The reliable pattern, proven throughout this codebase (`ChatHistory`, `SearchResults`, `Snippets`, and this same suggestion list), is: whenever any item's rendered state needs to change, reassign the *entire bound collection property* to a new array/list instance (with per-item flags already baked in) and raise that property's own change notification — never assume an in-place mutation on an already-transmitted nested item will be observed. Verify by testing repeated Up/Down selection moves that stay *within* the same visible scroll window (i.e., cases where nothing else about the collection's shape changes), not just cases that also trigger a scroll/window change, since those already force a full collection resend and can mask this bug.

Reassigning the bound collection property is NOT sufficient by itself when the new array still contains previously transmitted item instances: Remote UI de-duplicates already-known objects by identity and does not re-serialize their current property values, so in-place state changes on those items never reach the rendered rows even though the collection notification fires. The item objects themselves MUST be newly constructed on every update, with the rendered state (e.g. `IsSelected`) baked in at construction time — exactly as `ChatHistory`, `SearchResults`, and `Snippets` already construct fresh item view models on every assignment. This was the final root cause of issue #164: the visible window array was rebuilt per keystroke, but from the same master `SlashCommandSuggestion` instances, so the keyboard highlight never repainted.

The selected visual state MUST update synchronously when `Up` or `Down` changes the selection, remain readable in Light, Dark, Blue, and High Contrast themes, and use a verified Visual Studio foreground/background pair or the WPF system highlight pair. Fixed white or black colors are prohibited unless the target theme contract explicitly requires them.

Acceptance review MUST include keyboard navigation with the pointer outside the popup, a list containing more items than the visible viewport, and a Visual Studio theme change while the popup remains open.

## Theme-integration guidance

### Remote UI

Specify that the implementation should:

- Use Visual Studio Shell styles and colors available in the Visual Studio process.
- Use only resource-dictionary mechanisms supported by the target Remote UI SDK.
- Avoid unsupported extension-local control types and code-behind assumptions.
- Keep theme-dependent values dynamic where supported.

### In-process WPF

Specify that the implementation should:

- Reuse the repository's existing Visual Studio theme abstraction.
- Avoid introducing a second competing theme system.
- Use supported Visual Studio WPF resources or an already-adopted toolkit integration.
- Update control resources when the Visual Studio theme changes.

### External WPF process

Specify that the implementation should:

- Receive a semantic theme model through the established process boundary.
- Update paired foreground/background resources atomically.
- Avoid assuming that Visual Studio resource dictionaries are directly visible outside the Visual Studio process.

## Workflow

1. Read the user's requirements and limit the control scope.
2. Inspect repository metadata and UI files only as needed to classify the host and identify existing conventions.
3. Consult current Microsoft and Fluent UI references when exact architecture or token behavior matters.
4. Produce the architecture assessment.
5. Produce the control inventory and state matrices.
6. Produce the semantic resource map.
7. Produce the selected-text safety contract.
8. Produce the theme review matrix and acceptance criteria.
9. Produce a handoff section for the implementation owner.
10. Stop without editing files, generating code, running commands, or debugging.

## Required output

Use the template in `references/design-spec-template.md` when available.

The final design specification must include:

- Scope and non-goals.
- Detected or assumed extension architecture.
- Architecture constraints and unresolved assumptions.
- Button variants and state matrix.
- Multiline text-input state matrix.
- Dimensions and typography tokens.
- Semantic resource map.
- Visual Studio theme-token mapping with verification status.
- Selected-text foreground/background/caret contract.
- Theme and interaction review matrix.
- Acceptance criteria.
- Implementation handoff notes.
- A responsibility statement confirming that no code, build, test, or debugging work was performed.

## Completion criteria

The skill's work is complete when an implementation owner has an unambiguous design contract that answers:

- Which controls and variants are required.
- Which visual states must exist.
- Which semantic resources drive each state.
- How those resources map to Visual Studio themes.
- How selected text remains visible in every supported theme.
- Which architectural limitations apply to the selected extension model.
- How an implementation team will review the result.

Completion does not require and must not include source-code changes, debugging, builds, tests, packaging, or runtime verification.
