# ARCYN implementation and validation — 2026-10-17

The subsequent [headless validation pass](headless-validation.md) adds fixes, 87 assertions and component performance evidence. The results below describe the original implementation pass.

Implemented on `codex/arcyn-lightweight`, retaining the current cinematic HUD and integrating upstream services. Nothing was pushed. The root contains ARCYN and its repository notes; unrelated projects were not merged. Original HEAD `4c07547` and upstream `aa6add2` are both ancestors of the implementation. Git cannot establish why the original remote configuration was absent.

Recoverable backup: `D:\protocoll\.arcyn-backups\20261016-232017\workspace.zip`, SHA256 `AF6777B5E36BED03A22830F9756DBCB1CD189EADF045AC45A72264C3F1CD23AC`. It includes tracked, untracked, ignored and Git files, with a verified per-file manifest. Checkpoint `827bbb0` retains the original HUD edits. The showcase video, session notes, root notes, personal protocoll.json and original EXE retain their original bytes. New outputs are separate.

Implemented and checked:

- Complete configuration preservation, one active read/write path, legacy metadata and ordered arguments, unknown-field round trips, unique atomic temporary files, previous-good backup, serialized saves and detection of external edits. Missing targets remain repairable. Malformed/newer configurations cannot trigger silent setup replacement.
- Workspace creation, target editing/browsing, duplication, reordering and deletion. Rebuilt cards appear in Ready; keyboard focus resolves after template creation. Application discovery reads Start-menu shortcuts and App Paths only on demand.
- Existing literal executable paths precede legacy parsing; explicit arguments use ArgumentList and correct Windows quoting. Shell dispatch can succeed without a process handle. Preparation/dispatch run outside the UI thread. Cancellation restores control even when a shell dispatch blocks, and prevents remaining dispatches.
- Monotonic idle deadline, input resets, suspension during editors/menus, single-instance signaling and clean CLI exits. Logging is bounded and nonfatal. Foreground grants are scoped to explicit duplicate activation/hotkey launch.
- No application NuGet packages, WMI, performance counters or AutoHotkey. The standalone folder bundles the runtime. Default shortcuts have no ARCYN resident process; the optional native helper uses RegisterHotKey and a blocking message wait, reports conflicts, and supports clean stop. Its imports are only Windows DLLs; its C runtime is static.
- Theme controls cover colours, font/scale, geometry, spacing, rows/compact/grid, glow, particles, trail, acrylic, overlays and continuous motion. Draft previews are coalesced, Apply persists, Cancel restores, and sharing uses validated visual JSON. External font resources are rejected.
- Particle/trail motion uses transforms instead of layout changes. Hidden/minimized rendering stops. Reduced effects remove the root shader. Default boot/launch durations, easings, typewriter cadence, two-stage expansion and completion holds remain. Progress fill sizing and animation ownership are fixed; the upstream held-width reset was corrected.
- CI targets master and PRs, pins the SDK, executes the headless regression harness and publishes/checks the complete ZIP. A remote CI run has not occurred because no push was authorized.

Validation: **48 assertions passed on .NET 8.0.32**, including configuration failure/recovery boundaries, parsing, argument round trips, cancellation, idle timing, shared workspace mutations, rebuilt cards, held expansion width, hidden rendering, theme bounds/rollback/sharing, native telemetry, duplicate signaling and particle layout stability. An offscreen WPF bitmap rendered successfully. Native helper compilation uses warnings as errors and its parser self-test passes. Standalone checks inspect included runtimes, native WPF DLLs, application dependency metadata and absence of personal configuration/AHK. These structural checks do not replace a clean-machine launch.

The old published EXE carried the compatibility-extraction flag, and a runtime hook captured an extraction directory as its base directory. An end-to-end published configuration reset remains unproven. Reproduced configuration/save defects drove the fixes; executable-directory lookup now uses the process path and personal configuration is excluded from the bundle.

Measured evidence is recorded in [the performance data](performance/20261017-headless-and-partial-baseline.json). Before changes, four source-build runs on an i3-1115G4 / four logical processors / .NET 8.0.25 / WPF tier 2 showed:

| Metric | Partial baseline |
| --- | --- |
| Peak private memory | 111.6–118.5 MiB |
| Sampled peak working set | 170.7–181.2 MiB |
| First render callback since hook | 0.80–3.24 seconds |
| Ready since hook, three captured runs | 6.61–9.06 seconds |
| Ready-to-exit CPU, machine normalized | 12.7–20.2% |
| Rendering callback p95 | 33–83 ms |
| Peak handles | 449–468 |

This is incomplete and noisy: the desktop was in use, one run missed Ready, DPI was not captured, hook timing excludes earlier process initialization, and the initial CPU interval included close/exit. Callback cadence is not GPU presentation time. These values are **not release budgets**. No post-change whole-application performance result is claimed.

A separate headless component comparison verified that both legacy counters worked: first telemetry initialization was 1,581 ms legacy / 1.76 ms native; subsequent construction medians were approximately 14.24 ms / 0.032 ms. It used five constructions and 100 samples per provider in one process. Sampling times were noisy and are included in the raw data. This evidence concerns telemetry only, not total launch time, application memory, or opened applications.

The user asked to keep ARCYN closed while working. No further visible launches, real target dispatch, hotkey registration, foreground tests, clean-VM startup, DPI interaction or live cinematic comparisons were performed. The revised benchmark requires `-AllowVisibleLauncher`, captures DPI/process age and ends Ready sampling at Closing. Its measurements cover only ARCYN's PID. Helper mode and opened or reused browser/Explorer processes need separate attribution.

Next validation session: record five explicitly defined cold launches and thirty warm launches for both the preserved source build and final artifact, with the same DPI, configuration, display mode and hardware. Record initialization versus cinematic duration, Ready CPU/private memory/working set, frame callbacks versus presentation metrics, and handle/process cleanup. Establish practical budgets from variance, then accept repeatable improvements without changing default choreography. Zero ARCYN processes after default-mode close remains a firm requirement. A fresh extraction directory alone is not an OS cold launch. Retain the default root glow until a live rendering trace justifies further changes.

The bundled servicing runtime is pinned to 8.0.32. [.NET 8 support ends November 10, 2026](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core); complete a separate .NET 10 LTS compatibility update and visible Windows checks before that deadline. WPF remains in place. Self-contained deployments require publishing updated runtime files with future servicing releases. Cancellation cannot recall an application already handed to Windows.
