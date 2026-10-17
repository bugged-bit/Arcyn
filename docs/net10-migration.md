# ARCYN .NET 10 LTS migration and plan checklist

The application, headless regression runner and performance probe target `net10.0-windows`. `global.json` selects SDK 10.0.402; application/test publishing pins the self-contained .NET/Windows Desktop runtime to 10.0.13. WPF, configuration schema, native helper, cinematic choreography and rendering architecture are retained. Build scripts explicitly disable interactive SDK restore/publish behavior.

The previously validated .NET 8 source is commit `01b75e9`. Its release ZIP remains `publish/ARCYN-headless-verified-20261017.zip` (SHA256 `390B3BDFED5A52F74DD992A9062DA49D5211B6755250526D3792A6998D9E9160`). An additional source/ZIP backup and fingerprints of original personal/untracked files are under `.arcyn-backups/net8-before-net10-20261017/`. Migration does not rewrite user configuration or register a helper.

## Original implementation plan

| Requirement | Implemented | Evidence / remaining validation |
| --- | --- | --- |
| Preserve/reconcile existing work | Safe branch, source checkpoints, recoverable snapshots and upstream services integrated with original HUD | Original personal files retain their hashes; only intended ARCYN changes staged. No master merge or force push. |
| Configuration safety and workspace cards | One read/write path, canonical names, legacy/unknown fields retained, atomic/serialized saves, backup, external-edit detection, offline entries retained, visible card rebuilds | Isolated load/save/failure/rollback/legacy tests pass. Real update/install and interrupted-machine tests remain pending. |
| Launch parsing, lifecycle and idle handling | Structured ordered arguments/working directories, literal path priority, shell null-handle support, cancellation and cleanup, single instance, bounded nonfatal logs, monotonic idle deadline, modal suspension | Fake dispatch and activation tests pass. Reactivation now resets an expired idle deadline, preventing a reopened HUD from immediately timing out. Actual shell dispatch, foreground behavior and hotkeys remain pending. |
| Installation and shortcut dependencies | Self-contained ZIP, no application NuGet packages, no AutoHotkey, native telemetry, zero ARCYN resident process by default, optional Win32 message-loop helper | Runtime/dependency checks, native compile/import/parser checks and isolated shortcut preflights. Clean-machine startup and real registration/conflict checks remain pending. |
| Setup, editing and discovery | Existing setup, terminal append setup, ordered target editor, browsing and on-demand Windows application discovery | Draft/metadata/discovery tests; setup/loading/editor XAML instantiated without HWNDs. Real wizard, file picker and keyboard interaction remain pending. |
| Rendering and customization | Unified visual model, colours/fonts/geometry/spacing/row/compact/grid layouts/effects/motion, coalesced previews, Apply/Cancel, validated JSON sharing, theme resource reuse, transform-based particles/trails, hidden/minimized activity suspension | Headless theme/card/binding/resource/cleanup tests and detached bitmap. No sequence shortening or new frame loop. GPU/Ready CPU/RAM and interactive layout/accessibility profiling remain pending. |
| Tests and CI | Development-only package-free regression project, script/XAML checks, SDK pin, native helper and complete ZIP validation, probe build | Workflow runs on master, codex/arcyn-lightweight and PRs targeting master. Confirm the green run for the release commit in [branch Actions](https://github.com/bugged-bit/Arcyn/actions?query=branch%3Acodex%2Farcyn-lightweight). |
| Portability boundaries | Windows interop stays in existing services; WPF retained | Linux and framework migration remain intentionally outside scope. |

## Compatibility and validation

Microsoft's [.NET 10 compatibility notes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10) were checked, including DynamicResource type enforcement, empty XAML grid definitions, JSON metadata name conflicts and CLI interactivity; .NET 9 changes were checked for the intervening upgrade. No application compatibility switch or dependency was needed. The setup runtime badge is the only intentional visual text change.

**89 headless regression checks pass on .NET 10.0.13**, including all 87 prior checks, unshown setup/loading XAML and idle reactivation. The harness uses explicit shutdown to prevent its unshown fixture windows from ending the test dispatcher. All launcher/editor HWNDs remain zero. Six XAML documents and nine PowerShell scripts validate; fourteen shortcut preflights make no desktop/startup writes. Four invalid artifact runtime metadata cases are rejected before helper execution, then original metadata bytes are restored. Native `--self-test` exits before registration, named-event creation or dispatch. The probe builds without being loaded into ARCYN.

The detached HUD PNG has the same SHA256 on .NET 8 and .NET 10: `AF31FC4C35E8AA2F17E702C1FDEDB0770DBCFA59F8A85F1828B949AF915AA925`. Boot timing expressions match `01b75e9`; launch cadence and completion holds are unchanged. This covers the scripted offscreen state, not live composition, acrylic, DPI behavior or complete cinematics.

## Resource comparison and release limits

The [raw comparison](performance/20261017-net10-migration.json) records five alternating .NET 8/.NET 10 process pairs using the unchanged benchmark source, on the same i3-1115G4/four-logical-processor machine. CPU is harness-PID time; managed allocations are the calling thread's; private/working set includes fixtures and the runtime.

| Component / operation count | .NET 8 median | .NET 10 median |
| --- | ---: | ---: |
| Native telemetry / 10,000 samples | 229.27 ms | 67.64 ms |
| Deserialize/validate 10 workspaces / 1,000 | 76.97 ms | 69.11 ms |
| Prepare target, no dispatch / 10,000 | 1,836.65 ms | 1,823.86 ms |
| Unchanged theme / 20 | 0.221 ms | 0.174 ms |
| Sixty-particle updates, no presentation / 2,000 | 298.26 ms | 314.90 ms |
| First unshown HUD construction per process | 223.28 ms | 264.48 ms |
| Harness private memory | 38.91 MiB | 39.27 MiB |
| Harness working set | 81.35 MiB | 80.86 MiB |

The particle and construction medians are slower in this sample. Their ranges overlap (particles: 267–372 / 265–405 ms; construction: 178–373 / 246–332 ms). Source review found no added rendering loop, layout work or cinematic delay; only one activation-event subscription was added. No timing/effect reduction or speculative runtime switch was used to hide the result. These are signals for later controlled profiling, not proven whole-app regressions. Component managed allocations stayed similar or decreased; unchanged-theme allocation was 37,672 / 37,304 bytes per 20 calls. Unshown construction excludes application startup, first paint and boot; particle ticks exclude pacing/presentation. Desktop-use interference and quantized CPU timing prevent these numbers from becoming whole-app budgets.

The initial complete self-contained ZIP is 63.63 MiB versus 70.28 MiB for the preserved .NET 8 release (about 9.45% smaller); expanded payload is approximately 142.30 versus 162.69 MiB. Documentation refreshes can change the final ZIP by a few KiB. Both retain the full runtime and native helper, with no application NuGet dependencies. The exact packaged application assembly matches the 89-check tested assembly. The native helper still imports only USER32, SHELL32, ADVAPI32 and KERNEL32; its static CRT needs no separate utility/runtime installation.

No visible application, configured workspace target or real hotkey is exercised. Release readiness still requires a separate GUI session, clean Windows machine, real dispatch/activation/hotkey checks, DPI/font/layout/accessibility checks, complete cinematic comparison and real CPU/RAM/GPU/startup profiling. Self-contained runtime servicing requires republishing. Byte-identical builds are not established because source/debug paths and native link timestamps vary. Cancellation cannot recall an application already dispatched.
