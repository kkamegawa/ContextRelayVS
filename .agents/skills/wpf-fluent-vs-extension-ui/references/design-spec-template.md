# Design Specification Template

## 1. Scope

- Requested controls:
- Required variants:
- Supported Visual Studio versions:
- Supported themes:
- Non-goals:

## 2. Extension architecture

- Detected host model:
- Target framework context:
- Theme-resource availability:
- Remote UI or process-boundary constraints:
- Assumptions requiring confirmation:

## 3. Design foundations

| Token | Intended value or rule | Source/reference | Verification status |
|---|---|---|---|
| Control height |  |  |  |
| Horizontal padding |  |  |  |
| Vertical padding |  |  |  |
| Corner radius |  |  |  |
| Border thickness |  |  |  |
| Typography |  |  |  |

## 4. Button variants

### Standard button

| State | Foreground | Background | Border | Focus indicator | Notes |
|---|---|---|---|---|---|
| Rest |  |  |  |  |  |
| Hover |  |  |  |  |  |
| Pressed |  |  |  |  |  |
| Focused |  |  |  |  |  |
| Disabled |  |  |  |  |  |
| Default |  |  |  |  |  |

### Primary/accent button

Use the same state table when this variant is required.

## 5. Multiline text input

| State | Text | Background | Border | Caret | Selection pair | Notes |
|---|---|---|---|---|---|---|
| Rest |  |  |  |  |  |  |
| Hover |  |  |  |  |  |  |
| Focused |  |  |  |  |  |  |
| Read-only |  |  |  |  |  |  |
| Disabled |  |  |  |  |  |  |

Behavior:

- Return-key behavior:
- Text wrapping:
- Vertical scrolling:
- Horizontal scrolling:
- Inactive selection:

## 6. Semantic resource map

| Semantic resource | Purpose | Preferred Visual Studio resource/token | Fallback | Dynamic | Verification status |
|---|---|---|---|---|---|
| `VsFluent.Brush.Control.Background.Rest` |  |  |  |  |  |
| `VsFluent.Brush.Control.Background.Hover` |  |  |  |  |  |
| `VsFluent.Brush.Control.Background.Pressed` |  |  |  |  |  |
| `VsFluent.Brush.Control.Border.Rest` |  |  |  |  |  |
| `VsFluent.Brush.Control.Border.Focus` |  |  |  |  |  |
| `VsFluent.Brush.Text.Primary` |  |  |  |  |  |
| `VsFluent.Brush.Text.Disabled` |  |  |  |  |  |
| `VsFluent.Brush.Text.Selection.Background` |  |  |  | Yes |  |
| `VsFluent.Brush.Text.Selection.Foreground` |  |  |  | Yes |  |
| `VsFluent.Brush.Text.Caret` |  |  |  | Yes |  |

## 7. Selected-text safety contract

- Selection background:
- Selection foreground:
- Caret foreground:
- Preferred Visual Studio pair:
- Fallback pair:
- Active selection behavior:
- Inactive selection behavior:
- Theme-change behavior:
- Prohibited pairings:

## 8. Review matrix

| Theme | Button states | Text-input states | Mouse selection | Keyboard selection | `Ctrl+A` | Theme switch |
|---|---|---|---|---|---|---|
| Light |  |  |  |  |  |  |
| Dark |  |  |  |  |  |  |
| Blue/standard alternate |  |  |  |  |  |  |
| High Contrast smoke review |  |  |  |  |  |  |

## 9. Acceptance criteria

- [ ] Required control variants and states are fully specified.
- [ ] Every meaningful color uses a semantic resource.
- [ ] Exact Visual Studio resource names are verified or marked for verification.
- [ ] Selection foreground and background are defined as an inseparable pair.
- [ ] Caret remains visible in all supported themes.
- [ ] Theme-switch behavior is specified.
- [ ] Standard WPF keyboard and editing behavior is preserved.
- [ ] No web runtime or Fluent UI Web package is required.

## 10. Implementation handoff

- Files or resource dictionaries likely affected:
- Existing theme mechanism to reuse:
- Architectural constraints:
- Decisions left to the implementation owner:
- Manual review required after implementation:

## 11. Responsibility statement

This output is a design specification only. No source code was created or modified, and no build, test, runtime verification, or debugging activity was performed.
