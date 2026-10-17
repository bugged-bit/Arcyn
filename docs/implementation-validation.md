# ARCYN implementation and validation

ARCYN retains WPF, its cinematic HUD and default choreography. The application, test runner and performance probe target `net10.0-windows`; `global.json` pins SDK 10.0.402 and self-contained application/test publishing pins .NET/Windows Desktop 10.0.13. There are no application NuGet packages or separately installed runtime/utility requirements.

## Implemented scope

| Requirement | Implementation and headless evidence |
| --- | --- |
| Configuration and workspace safety | One read/write path; legacy/unknown fields retained; canonical aliases; atomic serialized saves with one previous-good configuration backup; external-edit detection; malformed/newer files preserved; offline targets retained; visible card rebuilds; ordered editing, duplication, reordering and deletion with rollback on failure. |
| Launching and lifecycle | Literal-path priority; exact arguments/working directories; shell dispatch without requiring a process handle; background dispatch; cancellation and failure recovery; single-instance activation; bounded nonfatal logs; monotonic idle deadline reset on input/reactivation and suspended during editors/menus. |
| Installation and shortcuts | Complete standalone ZIP; native telemetry without WMI/counters; zero ARCYN background processes by default; optional native `RegisterHotKey` helper with a blocking message wait, conflict reporting and clean stop. Static CRT and Windows-only imports; no AutoHotkey. |
| Setup and discovery | First-run wizard, terminal append setup, ordered target editor, browsing and on-demand Windows Start-menu/App Paths discovery. No background discovery. |
| Customization and rendering | One visual model for colours, typography, geometry, spacing, rows/compact/grid, effects and motion; coalesced live previews; Apply/Cancel rollback; validated visual JSON sharing; resource reuse; transform-based particles/trails; hidden/minimized activity suspension. Default cinematic durations/easings/sequencing remain unchanged. |
| Testing and portability | One development-only package-free regression runner, script/XAML validation, native helper checks, complete artifact validation and SDK-pinned Windows CI. Windows interop stays in existing services; no framework/Linux migration. |

Legacy configuration readers remain because they preserve real user files. The unused standalone `LoadingWindow`, unused service members and unused Win32 declarations have been removed: the active launch sequence lives in `MainWindow`. Git retains the old implementations. Three overlapping validation reports are consolidated here; original measurements remain in `performance/`.

## Recovery and repository hygiene

The known-good .NET 8 source checkpoint is Git tag `arcyn-net8-validated` at `01b75e9`; the original HUD checkpoint `827bbb0` remains in history. One validated .NET 8 ZIP is retained locally at `publish/ARCYN-headless-verified-20261017.zip` (SHA256 `390B3BDFED5A52F74DD992A9062DA49D5211B6755250526D3792A6998D9E9160`). Repeated workspace/source archives, stale test/publish trees and duplicate ZIPs are disposable. User configurations, the untracked showcase video, session notes and unrelated root files are protected. No new filesystem backup is needed for this cleanup. Future source recovery uses Git; preserve a separate file only when an actual destructive operation requires it.

Configuration `.bak` files serve atomic-save recovery and are retained; they are distinct from redundant development workspace backups. The old personal single-file executable and PDB are obsolete build outputs, superseded by the retained checkpoint and standalone releases. The earlier extraction-path observation did not reproduce an end-to-end configuration reset; verified save/path defects justified the reliability fixes.

## Headless validation and CI

The .NET 10 migration passed **89 headless checks**, including 200 deterministic Windows/Unicode argument round trips, configuration preservation/recovery, unavailable targets, shared workspace edits, fake dispatch/cancellation, idle reactivation, isolated duplicate signaling, theme preview/Apply/Cancel persistence and rollback, binding/resource checks, native telemetry and cleanup. Unshown setup/HUD/editor HWNDs remain zero. Removing the unused loading-window fixture retains the setup initialization check.

Five remaining XAML documents parse and resource keys resolve; nine PowerShell scripts parse. Fourteen shortcut preflight cases stop before COM or desktop writes. Four invalid runtime-metadata cases are rejected before helper execution and original metadata bytes are restored. The native helper builds with `/W4 /WX /O1 /MT`; `--self-test` exits before registration, named-event creation or dispatch. Its imports are USER32, SHELL32, ADVAPI32 and KERNEL32. The probe builds without being loaded into ARCYN.

The migration's [clean GitHub Actions run](https://github.com/bugged-bit/Arcyn/actions/runs/39051318631) passed at `5ba35e1`, including standalone publishing and ZIP upload. CI runs on master, codex/arcyn-lightweight and PRs targeting master; check [branch Actions](https://github.com/bugged-bit/Arcyn/actions?query=branch%3Acodex%2Farcyn-lightweight) for subsequent commits. Functional clean-build reproducibility is established; byte-identical builds are not, because debug paths and native link timestamps vary.

The scripted offscreen HUD PNG matched across .NET 8 and .NET 10 (SHA256 `AF31FC4C35E8AA2F17E702C1FDEDB0770DBCFA59F8A85F1828B949AF915AA925`). Boot timing expressions, launch cadence and completion holds are unchanged. This verifies the detached scripted state, not live composition or complete cinematic playback.

## Performance evidence and limits

Hardware: i3-1115G4, four logical processors. Measurements attribute CPU/memory to ARCYN or the headless harness PID; applications it opens require separate attribution, including reused browser/Explorer processes. HUD telemetry displays system-wide readings.

- [Original partial baseline](performance/20261017-headless-and-partial-baseline.json): four source-build runs before the desktop no-launch restriction; private-memory peaks 111.6–118.5 MiB, sampled working-set peaks 170.7–181.2 MiB, first callback 0.80–3.24 seconds and Ready 6.61–9.06 seconds from the hook. Desktop interference, missing DPI, one missed Ready interval and closing CPU make these incomplete, not release budgets. Callback cadence is not GPU presentation time. No post-change whole-app improvement is claimed.
- [Resource-reuse component comparison](performance/20261017-validation-components.json): five harness processes per variant; 20 unchanged-theme applications fell from median 891.39 ms / 46,703,304 managed bytes to 0.758 ms / 37,672 bytes by avoiding repeated template construction. Changed themes still apply fully.
- [.NET 8/10 component comparison](performance/20261017-net10-migration.json): five alternating process pairs with unchanged benchmark source. Telemetry, deserialization and target preparation medians improved; particle-update median rose 298.26 to 314.90 ms and unshown-construction median rose 223.28 to 264.48 ms. Ranges overlap substantially. Harness private memory was 38.91/39.27 MiB; working set 81.35/80.86 MiB. These noisy results establish neither whole-app savings nor a proven runtime regression. CPU deltas are quantized; zero is below measurement resolution.

The migration's self-contained ZIP was approximately 63.64 MiB versus 70.28 MiB for the retained .NET 8 checkpoint, about 9.45% smaller. Documentation/source cleanup can change final bytes. Unshown construction excludes startup/first paint/boot; particle ticks exclude pacing/presentation; harness memory includes runtime and fixtures. Budgets must follow controlled baselines, never guessed thresholds.

## Pending release gates

No visible application, configured workspace target or real hotkey is exercised by headless checks. A separate GUI session must cover clean-machine startup, wizard/file-picker/keyboard interaction, real dispatch/errors/activation/hotkey conflicts, DPI/font/layout/accessibility extremes and complete cinematic comparison. Measure five cold and thirty warm launches, Ready CPU/private memory/working set, presentation/frame intervals and process/handle cleanup with consistent hardware, DPI, configuration and artifact. `Measure-Arcyn.ps1` requires explicit `-AllowVisibleLauncher`; do not run it while the desktop must remain uninterrupted.

Zero ARCYN processes after default-mode close is a firm requirement; resource budgets await representative measurements. Self-contained runtime servicing requires republishing. Cancellation cannot recall an application already dispatched.
