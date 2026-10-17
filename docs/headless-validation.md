# ARCYN headless validation — 2026-10-17

This pass did not run ARCYN startup, show a HUD/editor/setup window, launch a configured target, register a hotkey, or change desktop/startup shortcuts. The regression harness constructs unshown WPF objects and renders a detached bitmap; it asserts that every launcher/editor HWND remains zero. All configuration fixtures and test logs live under the test output directory. Original personal configuration, ignored executable, notes and video still match the verified backup.

## Verified fixes

- Guard workspace and theme actions against overlapping edits/saves. Failed workspace saves restore the original list and report the error; failed theme saves restore both configuration references. Setup disables its content while persistence is pending.
- Share command normalization between launch preparation and editing. Unavailable quoted legacy commands keep their arguments; environment variables remain in saved commands. Quoted folder paths work, and quoted empty commands fail before dispatch.
- Display all supported legacy target types on HUD cards. Repair friendly executable/folder labels, unsupported shortcut hints above nine, two damaged text glyphs, a misplaced launch indicator, and long-name trimming. Add accessible control/card names and standard theme Apply/Cancel keyboard actions.
- Clear frozen cursor trails when ambient animation is disabled. Prevent disposed render services from resubscribing and put disposed HUD state into Closing. Remove unused scale/preset/colour-map code and an unnecessary rendering-event wrapper.
- Protect configuration envelopes without a `modes` property from theme import/export. Reject unsupported schema versions without changing their files.
- Save canonical `label`/`subtitle` fields once while still reading `name`/`description`. Conflicting aliases now require repair instead of silently discarding one value. Empty workspaces get an actionable launch error; per-target shell failures remain recoverable.
- Align native/helper shortcut grammar: case-insensitive keys, unique modifiers, no empty tokens or modifiers after the key. Validate setup input before any shortcut writes, and describe custom shortcut activation accurately.
- Reuse theme resources when a preview is unchanged; changed themes and rollback still rebuild correctly. This preserves cinematic timing and avoids allocating fresh WPF templates for repeated Apply/preview calls.

## Executed checks

- **87 headless assertions**, including 200 deterministic Windows/Unicode argument round trips, configuration preservation and failure recovery, canonical aliases, BOM legacy import, unavailable targets, shared workspace changes, cancellation during blocked fake dispatch, per-target failure recovery, idle deadlines, duplicate-instance signaling with unique test names, theme preview/Apply/Cancel, actual persistence/rollback, cleanup and telemetry bounds.
- Offscreen HUD rendering and card template instantiation. WPF binding tracing reports no warnings/errors in exercised views. All six XAML documents parse and resource references resolve. Boot timing expressions match preserved checkpoint `827bbb0`; launch cadence/holds retain their existing values.
- Eight PowerShell scripts parse. Fourteen shortcut preflight cases run in a temporary directory with no executable, so execution stops before COM or desktop writes. Native helper builds with `/W4 /WX`; its parser self-test exits before registration, event creation or launch. Its imports are Windows DLLs only and its C runtime is static.
- The probe project builds with zero warnings/errors. Standalone checks inspect bundled .NET/WPF 8.0.32, runtime/dependency metadata, native WPF binaries and absence of personal configuration/AutoHotkey. Two source-only build directories exercise clean restore/build/publish; packaging results are recorded with the generated artifact.
- CI includes script/XAML checks and uses Node 24 action versions. [GitHub retired Node 20 on Actions runners](https://github.blog/changelog/2026-09-23-node-20-is-no-longer-available-in-github-actions/). No remote workflow was run because nothing was pushed.

Fresh builds succeed without copying `bin`/`obj`. This is functional build reproducibility, not byte-for-byte determinism: managed binaries include source-location-dependent debug metadata, and the native linker stamps a build timestamp. Different source-directory builds produced different hashes; reproducible binary hashes have not been established.

## Measured component costs

[Raw measurements and method](performance/20261017-validation-components.json) contain five separate harness runs before and after resource reuse, on an i3-1115G4, four logical processors, SDK 8.0.419 and bundled runtime 8.0.32. Each component gets one warm-up. CPU uses the harness PID; allocations use its calling managed thread.

For **20 applications of the same theme**, median wall time changed from **891.39 ms to 0.758 ms**, and managed allocations from **46,703,304 to 37,672 bytes**. The saved work is repeated template/resource construction, outside the frame loop. Changed themes still require a full application.

Other components were measured: native telemetry, configuration deserialization/validation, target preparation without dispatch, sixty-particle updates without presentation, and unshown window construction. Their wall times varied substantially while the desktop and clean builds were active; these differences are not reported as gains or regressions. CPU deltas are quantized; zero is below measurement resolution, not proof of zero CPU.

Harness private memory/working set includes the runtime and fixtures. Unshown window construction excludes OnLoaded, first paint and boot. Particle updates exclude dispatcher pacing and GPU presentation. None of these numbers establishes whole-application startup, Ready CPU/RAM, frame-time budgets or a before/after launcher comparison. The earlier partial live baseline remains qualified in [implementation-validation.md](implementation-validation.md).

## Requires a later GUI session

Clean-machine application startup; first-run wizard interaction; real workspace dispatch and error dialogs; global hotkey registration/conflicts and foreground activation; DPI/font/layout extremes; screen-reader interaction; visible cinematic comparison; real Ready CPU/RAM, startup and presented-frame measurements. No whole-app resource improvement is claimed here. An already dispatched application cannot be recalled by cancellation.

The standalone runtime still needs servicing with future releases. The existing [.NET 8 support deadline is November 10, 2026](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core); a separate WPF/.NET 10 compatibility update remains due before then. No framework migration was introduced.
