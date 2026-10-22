# ARCYN 1.6.0-rc.1

Release candidate for Windows 10/11 x64. One ARCYN product, on `codex/arcyn-lightweight`.

- Windows-native interface across workspaces, editors, settings, setup, pickers and dialogs. Microsoft's shipped .NET 10 Fluent control templates replace handwritten controls. Segoe UI Variable, Fluent icons, system light/dark appearance, system accent and contrast policy form the defaults; custom preferences remain available.
- Standard Windows title bars, system menus and resizing for supporting windows. Mica/Acrylic requests remain event-driven and fall back to readable opaque surfaces. Offscreen renders cannot prove native material appearance.
- Quieter workspace presentation, visible creation/settings/actions, readable target summaries and retained keyboard shortcuts. Setup preserves name casing. Selected-item disclosures, scrollable narrow editors and a monitor canvas with a usable minimum height improve everyday editing.
- General/Appearance/Motion settings, field-specific errors, explicit picker Select/Cancel actions and retained failure/cancellation guidance. Atomic saves, exact commands, review/UAC, dependencies and managed-window ownership remain intact.
- Setup offers Skip on the optional Apps, Folders and Websites steps. Next retains pending entries; incomplete website addresses stay editable. Primary keyboard actions and step focus support a keyboard walkthrough. Supporting dialogs inherit their owner's app theme.
- Original opening/launch intervals and completion holds remain. Unused HUD telemetry updates are removed; ambient rendering stops at Ready. The runtime already includes Fluent, so no UI dependency or framework migration is added.
- Complete self-contained .NET/WPF 10.0.13 package, original ARCYN icon, consistent metadata and redistribution notices including Fluent's WPF UI contributions. Packaging checks require the Fluent runtime and reject development artifacts.
- Native-helper build and package checks wait for the actual self-test exit code; a failing helper blocks packaging.
- Non-floating launcher windows stay available from the taskbar. Accessible settings choices, prerequisites and editor rows use readable labels. Live testing verified isolated launch/reuse, layout capture, cancellation, light/dark preview, setup Skip and a reversible global hotkey lifecycle.

Headless evidence and measured trade-offs are in [validation notes](implementation-validation.md). The committed previews are actual opaque WPF renders, not native-glass demonstrations. The [live validation report](live-validation.md) records measured startup/idle resources and completed GUI workflows. Clean Windows first run, physical mixed-DPI/fallback/assistive testing, popup-dialog completion, presented-frame pacing and longer endurance testing remain release gates. No master merge or public release is included.
