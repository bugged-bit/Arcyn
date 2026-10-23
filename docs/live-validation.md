# ARCYN live validation — 22 October 2026

Historical record: locally validated release candidate on `codex/arcyn-lightweight` as of 22 October 2026. The later v1.6.0 packaged smoke and the limitations carried into the published release are recorded in the [release notes](release-notes.md). Product identity, configuration compatibility and cinematic timing are preserved.

## Live GUI and workflows

All workspace dispatch used an explicitly selected disposable configuration under `.arcyn-local`. The sole real target was a locally compiled, empty WinForms test window with an exact executable/title match. No personal workspace targets, window arrangements, Windows settings or real shortcuts were changed.

- The actual self-contained launcher, workspace editor, Targets/Layout/Actions pages, settings and workspace menus were inspected through Windows Computer Use screenshots and accessibility trees. The HUD remained readable with native rounded edges and desktop material; supporting windows had Windows title bars and system buttons. Light and dark settings previews were verified live. The DWM backdrop attribute reported **3 (Acrylic)** for the launcher on Windows build 26300, with hardware rendering tier 2 at 125% scale. This does not establish appearance on other Windows versions.
- Typography scale 1.8 remained readable with scrolling and accessible Apply/Cancel controls. Scrolling was exercised live. Narrow sizes, 1.0/1.4/1.8 text and contrast policy additionally have offscreen coverage; a physical mixed-DPI walkthrough and a screen-reader session were not performed.
- Setup was retested using the production `SetupWindow` in a temporary host whose configuration service pointed at empty test directories. Enter advanced Welcome and focused the workspace name; Apps focused its input and Folders focused Browse. **Skip worked for Apps, Folders and Websites**, reached review with zero targets, focused Finish and saved schema 2 in the disposable directory. Earlier checks also verified missing-name and incomplete-website errors. This is not the complete standalone first-run dispatch path.
- A real target launch reached observed window readiness. A second launch reused the same PID without creating a duplicate. Capture matched windows recorded exactly one placement for that test target, and the layout and a 15-second Wait action survived Save/reload. Relaunch exercised the saved placement before the wait. Cancel returned control with an explanation that dispatched work remains open; the target stayed open.
- A missing executable produced a repairable failure, then returned to Ready with persistent guidance to Workspace menu → Last run details. That native menu was inspected. Computer Use could not activate the popup item outside the HUD bounds and subsequently lost its foreground binding; opening the details dialog itself was not verified live. Dialog content, ownership and appearance have automated coverage.
- Settings preview changed the live app appearance and text scale; Cancel restored the original system theme/scale and left the saved test configuration unchanged. The final theme choices and action prerequisite names were confirmed readable in the accessibility tree. Remaining internal DataGrid row names were corrected in the same small change and covered by the final build/regression run.
- Turning off Always on top previously left a window hidden from both taskbar and Alt+Tab. Ordinary launcher windows now appear in the taskbar; floating default behavior is preserved. The ordinary window was retrieved and activated live. Toggle/rollback behavior also has regression checks.

The desktop walkthrough was interrupted by concurrent input in earlier sessions. A fresh isolated process resolved the stale-window input failures on this pass. All controlled test windows and helpers were closed afterward.

## Measured performance

Machine: Intel Core i3-1115G4, four logical processors, Windows 10.0.26300, .NET 10.0.13, hardware WPF tier 2, 125% DPI. Measurements used the self-contained application and a measurement-only startup hook; the hook blocked target dispatch and detached its rendering callback at Ready. No build/test workload ran during collection.

| Measurement | Observed result |
| --- | --- |
| First WPF render callback from process start | 2.02 s initial; 1.41–1.57 s in two warm runs |
| Ready from process start | 6.45 s initial; 5.95–5.96 s warm, including the preserved cinematic sequence |
| Boot callback interval p95 | 33.33 ms across three startup runs; 17.24 ms in the later sustained run |
| Settled idle CPU | 312.5 ms CPU over 21.20 s: 1.47% of one logical processor / **0.37% total machine CPU** |
| Settled private memory / working set | 86.09–86.35 MiB / 158.60–159.15 MiB |
| Settled handles | 547 → 540 |
| ARCYN GPU busiest engine | 0.031–0.068% in the settled interval; excludes shared DWM cost |
| Repeated launch/close | Three short runs plus one 38-second Ready interval; all four exited with status 0 |
| After closing | No ARCYN, ARCYN.Hotkey or disposable Target process remained; default mode has no resident helper |

The whole Ready interval includes post-startup/JIT activity and consumes more CPU than the settled interval; both are retained in the [raw measurement report](performance/20261022-live-validation.json). Initial means first in this session, not reboot/cold-cache startup. Callback intervals do **not** measure actual presented frames. This short run cannot prove multi-hour leak freedom, and there is no comparable pre-change live baseline. No unsupported performance improvement claim is made.

## Hotkey, distribution and regression checks

- An isolated copy of the native helper registered **Ctrl+Alt+Shift+Q**, beside an ARCYN-named marker stub. The combination was available before testing, a competing registration failed with Windows error 1409 while the helper owned it, one real keyboard activation wrote one marker, `--stop` exited successfully, and a fresh registration succeeded afterward. No helper existed before testing or remained afterward. The helper snapshot used 7.73 MiB working set, 1.32 MiB private memory and 90 handles.
- A shell shortcut round-tripped executable, arguments and working directory inside the test directory, with the product icon selected. Desktop/Start Menu/Startup shortcuts were untouched. Fourteen isolated shortcut-script preflight cases passed.
- **192 regression checks** and **516 offscreen interface states** passed on the final source. Six XAML documents and nine PowerShell scripts passed parsing/resource checks. Configuration migration, atomic saves, external-edit protection, add/edit/delete, discovery, ordering, matching, layout geometry, action dependencies, command review, cancellation and ownership safety are included.
- The standalone ZIP includes its .NET/WPF runtime, product icon, metadata, notices, schema, example and documentation. Package validation rejects personal configuration, development artifacts and incorrect runtime/metadata; all nine negative artifact cases passed on the extracted final ZIP. Native-helper self-tests are awaited, so a nonzero result blocks packaging.
- The measurement hook now records Ready/end resources and the actual native backdrop attribute, and omits an empty pre-phase label that prevented PowerShell from reading its JSON. It is excluded from the product distribution.
- Local restore reported NU1900 because online NuGet vulnerability metadata was unreachable. ARCYN has no NuGet package dependencies; this is not a successful online vulnerability audit. Final branch CI is checked separately against the pushed commit.

## Checks still open on 22 October

| Gate | Remaining evidence |
| --- | --- |
| Clean Windows first run | Windows Sandbox is unavailable. Fresh ZIP extraction and bundled-runtime checks passed, but a clean Windows VM must still exercise standalone onboarding and the actual shortcut setup script. |
| Windows/platform acceptance | Physical mixed-DPI/multiple-monitor behavior, Windows 10 opaque fallback, live Windows contrast/reduced-motion settings and assistive navigation need dedicated acceptance testing without changing this user's setup. |
| Presentation and endurance | Actual GPU frame presentation/pacing and a longer interactive leak/handle soak remain unmeasured. The short settled sample is encouraging, not a substitute. |
| Dialog completion | Open Last run details through a human or functioning popup automation and verify keyboard return/focus. The automation failure is not evidence of an application crash; the tested process exited normally. |
| Distribution trust | Executables are unsigned. Signing/SmartScreen reputation and download/install trust have not been validated. |

No master merge or public release was performed. Native integration was judged using [Microsoft's Mica guidance](https://learn.microsoft.com/en-us/windows/apps/design/style/mica) and [FluentFlyout](https://github.com/unchihugo/FluentFlyout), while retaining ARCYN's existing design. No parity claim is made for untested platform combinations.
