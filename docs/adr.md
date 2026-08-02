# Architecture Decision Records

## 2026-08-02 — Issue #164: Synchronize Remote UI suggestion selection by index

- Context: Binding the Remote UI `ListBox.SelectedItem` to a suggestion object updated the extension-side selection but did not reliably activate the WPF container's `IsSelected` state because object-reference identity is not guaranteed across the Remote UI boundary.
- Decision: Expose the selected suggestion's visible scalar index through the serialized ViewModel contract and bind it one-way to `ListBox.SelectedIndex`. Keep the existing suggestion object and keyboard command behavior for applying commands and help text.
- Reason: The selected container must enter `IsSelected` for the dynamic Visual Studio selection background and foreground triggers to render.
- Consequence: This intentionally changes the original implementation constraint that the ViewModel would remain untouched, but does not change the public extension API or keyboard assignments.
