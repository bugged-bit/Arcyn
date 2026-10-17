# ARCYN

ARCYN is a WPF workspace launcher for Windows 10/11 x64. Its cinematic boot, red HUD, particles, mode-specific atmosphere and launch choreography remain the defaults. The application has no NuGet package dependencies.

Extract the **complete standalone ZIP** and run ARCYN.exe. Keep its runtime files beside it; no separate .NET runtime, AutoHotkey or VC redistributable installation is required. First run opens the setup wizard. Distribution includes .NET/WPF, so the ZIP is larger than the old runtime-dependent executable.

Default mode leaves **zero ARCYN processes** after closing. Ordinary Desktop, Start-menu and pinned Windows shortcuts need no resident ARCYN helper.

- 1–9 launch a workspace; arrows select; Enter launches; Esc cancels or closes.
- F2 edits a workspace. Ctrl+N adds one. Right-click a card to edit, duplicate, reorder or delete it.
- Ctrl+, opens HUD controls: colours, font, scale, geometry, spacing, rows/compact/grid, particles, glow, acrylic, scanlines, vignette, trail and continuous motion.
- Live previews are coalesced while the editor is open. Apply saves; Cancel restores. Import/export uses visual JSON only.
- Reduced effects and continuous-motion controls preserve boot/launch durations and choreography.
- Workspace editing supports ordered targets, exact arguments, working directories, file/folder browsing and application discovery on demand through Start-menu shortcuts and Windows App Paths. There is no background discovery.
- CPU/RAM on the HUD are **system-wide** readings. Applications launched by ARCYN have their own resource costs.

Run `scripts/Setup-Shortcuts.ps1` to create Desktop and Start-menu shortcuts. This does not start a process or install a helper. Windows also supports assigning Ctrl+Alt combinations in shortcut Properties.

For dependable Alt+Shift+D, optionally run `ARCYN.Hotkey.exe` beside ARCYN.exe. It uses native RegisterHotKey and a blocking message wait. Change the combination with `--hotkey CTRL+ALT+D`; stop with `--stop`. Conflicts are reported. Win combinations and F12 are reserved. To enable it at the next sign-in, run `scripts/Setup-Shortcuts.ps1 -EnableHotkeyHelper`; remove it with `-DisableHotkeyHelper`. It is never enabled automatically.

Configuration uses one selected path for both reads and saves:

1. Explicit `--config "path/to/arcyn.json"`.
2. Existing `%APPDATA%\ARCYN\arcyn.json`.
3. Existing arcyn.json beside the executable.
4. Legacy protocoll.json beside the executable, in its ARCYN child folder or its parent folder. Legacy data stays intact; subsequent edits save to AppData unless explicitly selected.

An explicit path must already exist. A new installation saves to AppData. The current working directory does not choose a configuration. `--config-path` prints the selected save location. Atomic saves retain the previous file as .bak. Malformed or newer-schema files are preserved and reported, never replaced by setup defaults. Copy a known-good .bak over the damaged file after keeping a copy of the damaged file.

Both legacy workspace arrays and upstream apps/websites/folders remain readable. Prefer ordered targets for new work:

Saved workspaces use `label` and `subtitle`. The older `name` and `description` aliases remain readable; if both forms occur, their values must agree.

```json
{
  "schema_version": 1,
  "modes": [
    {
      "label": "CODE",
      "subtitle": "Development workspace",
      "type": "code",
      "session": "SES 089",
      "state": "EXEC LIVE",
      "targets": [
        { "cmd": "wt.exe", "args": [] },
        { "cmd": "https://github.com", "args": [] }
      ]
    }
  ]
}
```
Unavailable targets stay saved and can be repaired. Existing literal paths take precedence over command-line parsing. Ambiguous legacy strings require editing; shell metacharacters are not interpreted as a shell script. A program that needs a command interpreter must select that interpreter explicitly.

Use `ARCYN --setup` in a terminal for line-based setup (appends to existing workspaces; EOF exits). Other flags: --help, --version, --config-path, --config. Duplicate startup activates the existing window. Logs are bounded and nonfatal under %LOCALAPPDATA%\ARCYN.

Development requires the SDK pinned in global.json. C++ build tools are needed only to build the optional native helper. From the repository root:
```powershell
./ARCYN/tools/Test-Arcyn.ps1
./ARCYN/tools/Publish-Arcyn.ps1
```
The regression runner opens no windows or target applications. Publishing creates and checks a complete standalone ZIP. CI runs these commands for master, codex/arcyn-lightweight and pull requests targeting master.

See [validation and performance notes](docs/implementation-validation.md). WPF remains the framework; Windows interop stays in the existing services. Linux support and a framework migration are outside this change.

The standalone artifact bundles .NET 10 LTS runtime 10.0.13; the development SDK is pinned to 10.0.402. Updating a self-contained release requires republishing its bundled runtime. The validated .NET 8 artifact remains separately recoverable. See the [migration and original-plan checklist](docs/net10-migration.md) for measured comparisons and release-readiness limits.
