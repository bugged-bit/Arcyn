# ARCYN v1.6.0

**Stable version prepared · Not yet published · Unsigned · Windows 10/11 x64**

The complete distribution is named **ARCYN-v1.6.0-win-x64.zip**, accompanied by a SHA-256 file. Product and informational versions are `1.6.0`; assembly and file versions remain `1.6.0.0`. The native helper uses the same versions with no prerelease flag. No public release has been published. [Build access and download guidance](guide.md#downloads-and-windows-warnings) explain how to inspect an available CI artifact.

## What's changed

- **A Windows-native interface.** The workspace list, setup, editors, and settings use Fluent controls, system appearance and accent colors, with readable opaque fallbacks when native materials are unavailable.
- **Workspaces you can manage visually.** Add apps, folders, files, and websites; reuse matching application windows; save layouts; and add optional waits and reviewed commands.
- **A clearer first run.** Optional setup steps offer Skip. Settings preview changes before saving, and Cancel restores the previous appearance.
- **Keyboard access throughout.** Workspace number keys, editing shortcuts, readable control labels, and familiar supporting windows keep common actions close.
- **The cinematic opening remains.** The shorter stable-version subtitle has a 75 ms remaining hold, preserving its 435 ms typing phase. Other opening animations and launch cadence are unchanged. Ambient rendering stops at the workspace view, and the global hotkey helper remains optional.
- **A complete standalone package.** The ZIP includes the .NET/WPF runtime, original ARCYN icon, example configuration, schema, documentation, and redistribution notices.

## Before installing

Back up your configuration before upgrading. Saves use schema 2, which older versions cannot read. Extract the complete ZIP into a separate folder and keep its files together. See the [user guide](guide.md#configuration-upgrades-and-recovery).

Local checks cover configuration safety, workspace workflows, offscreen interface states, and selected live interactions on one Windows host. Clean Windows first run, physical mixed-DPI and broader Windows/assistive testing, Last run details keyboard completion, actual presented-frame pacing, and longer endurance testing remain open. The executables are unsigned; signing and SmartScreen reputation are not established.

The [live validation report](live-validation.md) records completed checks and remaining gates. [Implementation evidence](implementation-validation.md) retains historical regression and measurement results with their original version labels; changing the version does not close those acceptance gates. Interface previews are production WPF renders with sample content, not demonstrations of native desktop materials.
