# ARCYN live validation — 21 October 2026

Status: release candidate; public release remains blocked on the unfinished live checks below. All work stays on `codex/arcyn-lightweight`, with the existing product identity and cinematic timing.

## Completed live observations

The actual setup window was exercised through Windows UI Automation and inspected in screenshots, using the application's `SetupWindow` in a temporary test host. Its `ConfigService` used empty user/executable directories under `.arcyn-local`, never personal AppData. This validates the setup surface, not the standalone application's entire first-run startup path.

- Native Windows title bar, system buttons, rounded window edge, dark Fluent controls, readable wrapping and accessible field names were visible at the machine's current scale.
- Enter advanced Welcome and focused the workspace name. A missing name stayed editable with a useful error.
- Apps, Folders and Websites screens were inspected. A pending application path survived Next; no configured target application was launched.
- An incomplete `https://` address was rejected without leaving the website step or clearing its text.
- Apps/Folders keyboard focus landed on the whole scrolling pane. The step transition now focuses the appropriate input or browse button. The corrected focus behavior still needs a live retest.

Desktop automation repeatedly detected concurrent user input. The user chose to finish non-GUI checks first. Further GUI actions and visible performance runs are deferred; these observations are not approval of native materials or the whole application.

## Changes covered by regression checks

- Skip appears only on the optional Apps, Folders and Websites steps. It keeps added items and excludes unsubmitted text, including an invalid pending website. Name and final review remain required.
- Next commits a pending application path or valid website exactly once. Invalid website input preserves the draft and step.
- Enter invokes the visible primary action. Step transitions set useful keyboard focus.
- Dialog appearance derives from the attached owner surface, including supporting windows.
- Configuration format, workspace orchestration and cinematic durations are unchanged. No dependencies were added.

## Automated verification

- 187 regression checks passed on bundled .NET 10.0.13, including 516 offscreen interface states. Those states cover narrow scrolling, text scales 1.0/1.4/1.8, light/dark/custom colors and contrast policy. No launcher HWND or target application was created by the regression runner.
- Six XAML documents and nine PowerShell scripts passed parsing/resource checks; 14 isolated shortcut preflight cases passed without creating real shortcuts or registering a hotkey.
- The standalone package passed runtime, product metadata, icon, notice, configuration-schema and development-artifact checks. Its new validation report is included with the documentation.
- Both native-helper self-test callers now wait for its exit code. A real disposable WinExe returning 42 reproduced the previous false pass and verified rejection after the fix. The artifact regression script also injects a nonzero helper result; all nine negative artifact cases pass.
- Local restore reported NU1900 because NuGet vulnerability metadata was unreachable. The application has no NuGet package dependencies; this warning is not a successful online vulnerability audit.

The final extracted-ZIP checks and branch CI results are recorded in the completion report. Runtime performance numbers are deliberately absent until live measurement is possible.

## Release gates still open

| Gate | Required evidence |
| --- | --- |
| Final GUI walkthrough | Retest the updated setup, including Skip, then exercise the HUD, settings, editors, menus, dialogs and errors in the final standalone build. |
| Windows materials and accessibility | Inspect actual Mica/Acrylic and fallbacks, shadows, light/dark appearance, keyboard traversal and assistive navigation. Offscreen renders cannot establish native desktop glass quality. |
| Real orchestration | Use dedicated disposable target windows to verify launch/reuse, capture/restore, actions, cancellation and recovery without touching personal windows. |
| Real performance | Measure process-start-to-Ready, idle CPU/RAM, GPU usage, frame presentation/cadence, handles, repeated launches, a sustained session and default zero-background exit. No clean live performance sample has been obtained during this pass. |
| Hotkey lifecycle | Register a temporary unused combination against an isolated stub, verify activation/conflict/cleanup, then confirm no helper remains. |
| Clean environment | Exercise the extracted standalone distribution and shortcut creation in a disposable Windows environment. Do not change the host installation or actual shortcuts. |

The existing package checks verify bundled runtime files, metadata, icons, notices, example/schema JSON and exclusion of personal configuration/development files. They do not prove first-run installation UX, SmartScreen/signing reputation or the remaining live gates.

Windows integration is judged against [Microsoft's Mica guidance](https://learn.microsoft.com/en-us/windows/apps/design/style/mica) and [FluentFlyout's native desktop integration](https://github.com/unchihugo/FluentFlyout). No visual parity claim is made from source or offscreen renders.
