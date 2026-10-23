# ARCYN v1.6.0

**Stable release · 23 October 2026 · Unsigned · Windows 10/11 x64**

Download **[ARCYN-v1.6.0-win-x64.zip](https://github.com/bugged-bit/Arcyn/releases/download/v1.6.0/ARCYN-v1.6.0-win-x64.zip)** and its **[SHA-256 file](https://github.com/bugged-bit/Arcyn/releases/download/v1.6.0/ARCYN-v1.6.0-win-x64.zip.sha256)** from the [v1.6.0 release](https://github.com/bugged-bit/Arcyn/releases/tag/v1.6.0). Product and informational versions are `1.6.0`; assembly and file versions remain `1.6.0.0`. The native helper uses the same versions with no prerelease flag. See the [installation and Windows warning guidance](guide.md#downloads-and-windows-warnings).

The published ZIP is the verified CI package from `250c74e`, with SHA-256 `1d15ea4566a2b821b1f8e7937685003a7387190e167da5fabd3c086833e32590`. Its bundled documentation predates publication; use the online guide for current download instructions.

## What's changed

- **A Windows-native interface.** The workspace list, setup, editors, and settings use Fluent controls, system appearance and accent colors, with readable opaque fallbacks when native materials are unavailable.
- **Workspaces you can manage visually.** Add apps, folders, files, and websites; reuse matching application windows; save layouts; and add optional waits and reviewed commands.
- **A clearer first run.** Optional setup steps offer Skip. Settings preview changes before saving, and Cancel restores the previous appearance.
- **Keyboard access throughout.** Workspace number keys, editing shortcuts, readable control labels, and familiar supporting windows keep common actions close.
- **The cinematic opening remains.** The shorter stable-version subtitle has a 75 ms remaining hold, preserving its 435 ms typing phase. Other opening animations and launch cadence are unchanged. Ambient rendering stops at the workspace view, and the global hotkey helper remains optional.
- **A complete standalone package.** The ZIP includes the .NET/WPF runtime, original ARCYN icon, example configuration, schema, documentation, and redistribution notices.

## Before installing

Back up your configuration before upgrading. Saves use schema 2, which older versions cannot read. Extract the complete ZIP into a separate folder and keep its files together. See the [user guide](guide.md#configuration-upgrades-and-recovery).

The verified package completed first-run setup from a fresh extraction with disposable profile directories, launched a safe local workspace, and opened Last run details with the correct result. Pointer closing worked; the final launcher shut down normally with exit code 0 and no ARCYN or helper processes remaining. CI also passed regression, script, native-helper, metadata-rejection and complete package checks.

Testing used an existing Windows host, not a clean Windows installation. Clean-VM/shortcut onboarding, physical mixed-DPI and broader Windows/assistive testing, Last run details keyboard navigation/closing/focus return, actual presented-frame pacing, and longer endurance testing remain unverified. Computer Use could not reliably target the owned details dialog; no human keyboard result was reported and no application defect was confirmed. These limitations are carried into this release. The executables are unsigned; signing and SmartScreen reputation are not established. Follow your device's security policy rather than disabling protections.

The [live validation report](live-validation.md) and [implementation evidence](implementation-validation.md) retain historical checks and measurements with their original dates and version labels. Publication does not turn unperformed checks into passes. Interface previews are production WPF renders with sample content, not demonstrations of native desktop materials.
