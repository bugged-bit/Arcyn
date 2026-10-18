# ARCYN implementation and validation

## Current candidate — 18 October 2026

**1.6.0-rc.1 remains a release candidate.** One product and branch, `codex/arcyn-lightweight`. The Fluent implementation is commit `3607312`. No master merge, visible application launch, real target, hotkey registration or public release is part of this validation.

## Windows interface foundation

[FluentFlyout](https://github.com/unchihugo/FluentFlyout) was studied through its actual App/MainWindow/SettingsWindow XAML, project dependencies and interface images. Its coherent controls, system appearance, typography and window behavior matter more than decorative translucency. No FluentFlyout source is incorporated.

WPF UI and a WinUI migration were evaluated against [Microsoft's shipped Fluent WPF theme](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100). ARCYN uses that platform theme: its control templates already include WPF UI contributions and ship in the runtime we bundle. A separate UI package would duplicate this foundation; a migration would add substantial scope without solving an outstanding requirement. The runtime's `PresentationFramework.Fluent.dll` is byte-identical to the baseline, 980,776 bytes. The corresponding MIT contribution notice is included.

Microsoft's [typography](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography) and [layering](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/layering) guidance informs the hierarchy. Segoe UI Variable/Segoe UI and Fluent/MDL2 icon fallbacks come from Windows. Supporting windows use standard title bars, system menus and resizing. Inputs, buttons, lists, tabs, menus and focus states use the platform templates; ARCYN only supplies sizing and a few workspace-specific styles.

Windows light/dark appearance and accent are the defaults. Light, Dark and Custom remain available. Existing palette/font values are retained; select Custom to apply them, or disable Windows accent to use the saved accent. The optional `color_mode` and `system_accent` fields do not change execution schema 2. High contrast uses system controls/colors without rewriting saved preferences. Small legacy scale values no longer reduce interface text below 12 DIPs.

The existing event-driven native surface boundary requests Mica for supporting work windows and Acrylic for transient surfaces on supported Windows 11 builds. It exposes transparent client composition only after successful native initialization. Unsupported/failed composition, reduced effects and contrast policy use opaque surfaces. Windows may itself supply an opaque material despite API success. No blur shader, per-card blur, rendering framework or permanent worker was added. Real materials, chrome and GPU cost remain unverified.

## Usability and behavior

The workspace view exposes creation, Settings and card actions, with readable target summaries and retained keyboard shortcuts. Normal defaults fit three workspace rows; compact rows omit the description. Setup preserves name casing. Settings separate General, Appearance and Motion, retain advanced customization, reveal the relevant page/disclosure on invalid input and keep Apply/Cancel rollback behavior.

Targets holds workspace metadata and selected-target options. Layout and Actions use the space for their actual tasks. Inspectors stack and scroll at narrow widths; the monitor canvas retains a usable minimum height, captions stay separate, and numeric placement editing remains available. Pickers have explicit Select/Cancel actions, Enter and double-click selection. Workflow order cannot be visually sorted away from execution order. Failure/cancellation details stay available after returning to the workspace view.

Exact arguments, command review/UAC, dependencies, readiness evidence, cancellation, managed-window ownership, unknown configuration fields, external-edit protection and atomic saves are unchanged. Default boot typing/hold intervals, shell expansion, 350-ms dispatch cadence and completion holds remain. Unused HUD telemetry updates are removed. Cinematic effects and render callbacks stop at Ready; disabling idle closing also stops its timer. No idle orchestration polling is introduced.

## Headless evidence

**178 checks pass**, preserving earlier behavioral coverage while replacing assertions tied to the discarded styling. A **516-state render matrix** covers normal/narrow sizes, typography 1.0/1.4/1.8, light/dark/custom/system-contrast policy, boot/ready/launch views, three editor tabs, scrolled inspectors/layouts, three settings tabs, pickers, review dialogs, menus/submenus and six setup steps. Focused renders additionally cover expanded matching, window/wait actions and motion settings. Binding/resource diagnostics are clean; launcher/editor HWNDs remain zero and fake window operations remain untouched.

Six XAML documents and nine PowerShell scripts parse; 14 isolated shortcut preflight cases avoid COM and desktop writes. The native helper self-test exits before registration/dispatch. The performance probe builds without loading it into ARCYN; its live measurement path requires `-AllowVisibleLauncher`. Complete artifact checks verify runtime, example/schema resolution, original icon, product/file/informational metadata, notices and development-artifact exclusion. Eight malformed-runtime/product/notice/development-artifact cases are rejected and original fixture bytes restored.

[Workspace view](previews/hud-offscreen.png), [layout](previews/layout-offscreen.png) and [settings](previews/appearance-offscreen.png) are actual opaque WPF renders using synthetic content. They omit native nonclient chrome and do not demonstrate desktop glass, GPU presentation, assistive usability or live choreography.

## Resource comparison

[Raw results](performance/20261018-fluent-ui.json) record five alternating process pairs against validated `ac80eb9`, with unchanged `Benchmarks.cs`. Desktop activity and cache state were uncontrolled. Medians:

| Headless harness metric | Baseline | Fluent |
| --- | ---: | ---: |
| Elapsed / process CPU | 7.13 / 6.72 s | 8.38 / 7.05 s |
| Sampled peak private / working set | 43.52 / 87.69 MiB | 45.58 / 89.91 MiB |
| Sampled peak handles | 357 | 357 |
| Unshown construction | 817.42 ms | 739.65 ms |
| Measured component allocations | 46,290,192 bytes | 46,432,808 bytes |
| Application assembly | 2,838,528 bytes | 2,823,680 bytes |
| Standalone ZIP | 67,096,134 bytes | 67,048,437 bytes |
| Added UI packages / runtime DLLs | — | 0 / 0 |

The approximately **2.1 MiB additional sampled private memory** is consistent with the added Fluent resource/control foundation (an inference from the loaded styles, not a heap profile); the application assembly is about 14.5 KiB smaller. That bounded cost buys maintained native control behavior and replaces bespoke control templates and window scaffolding. Resources reuse the application dictionary and shared light/dark/contrast palettes instead of rebuilding templates per window/edit.

Elapsed ranges overlap substantially: 6.66–9.22 seconds baseline, 6.02–12.53 seconds Fluent. Most time is in unchanged target preparation/particle fixtures, which also varied. The extra configuration fields account for 56 KB across 1,000 deserializations; longer fixture-directory strings account for 80 KB across 10,000 preparations. Unshown construction now includes initial theme application, previously deferred until Loaded. These figures establish neither a whole-app speed improvement nor an idle/GPU budget. CPU deltas are quantized, transient handle peaks vary, and the harness includes runtime, JIT and fixtures.

The local standalone ZIP contains 426 files, passes artifact and eight rejection checks, and is about 0.07% smaller than the preceding package. Its SHA256 is `5FAE1810FD1F9B538A057A18E02C2DC6440C57C3DF518B81A4A08F9B0CADA8AD`. It was built from the implementation commit before this measurement record was added; final CI metadata/documentation bytes can differ.

## Release gates

A later authorized GUI session must approve Windows 10/11 opaque/native-material appearance, real title-bar/system-menu/snap behavior, keyboard and screen-reader navigation, focus visibility, DPI/text scaling, real reuse/broker behavior, layout capture/restore across mixed DPI/disconnected monitors, focus denial/UIPI/UAC, and complete cinematic playback. Measure startup, Ready CPU/private memory/working set, GPU presentation and process/handle cleanup with controlled hardware, DPI, configuration and artifact. Sequential launches do not establish cold startup. Zero ARCYN processes after default-mode close remains a release requirement, not a headless whole-app measurement.

The SDK/runtime remain pinned to .NET 10.0.402/10.0.13. A self-contained runtime update requires republishing matching binaries/notices. Use Git for source recovery; atomic configuration `.bak` files remain. Untracked video/session notes and personal configuration are preserved. Historical measurements remain in [performance](performance/), including [orchestration](performance/20261018-workspace-orchestration.json), [the preceding candidate](performance/20261018-release-candidate.json) and [the .NET migration](performance/20261017-net10-migration.json).
