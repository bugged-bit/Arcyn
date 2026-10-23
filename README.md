# <img src="docs/images/arcyn-icon.png" width="40" height="40" alt="ARCYN icon"> ARCYN

**A Windows workspace launcher for the apps, folders, and websites you use together.**

[![ARCYN's workspace view, with Code, Research, and Writing workspaces ready to open](docs/images/workspaces.png)](docs/images/workspaces.png)

*Workspaces for coding, research, and writing.*

## Start with your workspace

Your editor, a project folder, a few reference pages. Save them as a workspace, then bring them together with a click or a number key. Keep another workspace for research, writing, or whatever comes next.

- **Open things together.** Group applications, folders, files, and websites around a task.
- **Use what's already open.** ARCYN can reuse a matching application window instead of launching another copy. You can also choose to always launch a new one.
- **Put windows in place.** Capture a layout, adjust it visually, and restore it when you open that workspace.

A cinematic opening leads into a keyboard-friendly workspace list, with familiar Windows controls throughout.

[![ARCYN's layout editor showing a saved arrangement for an editor and project files](docs/images/layout.png)](docs/images/layout.png)

*The layout editor changes your saved arrangement. Editing the preview doesn't move your windows.*

## Get v1.6.0

**[Download ARCYN v1.6.0 for Windows x64](https://github.com/bugged-bit/Arcyn/releases/download/v1.6.0/ARCYN-v1.6.0-win-x64.zip)** · [Release notes](https://github.com/bugged-bit/Arcyn/releases/tag/v1.6.0)

1. Extract **ARCYN-v1.6.0-win-x64.zip** to a permanent folder you can write to. A [SHA-256 file](https://github.com/bugged-bit/Arcyn/releases/download/v1.6.0/ARCYN-v1.6.0-win-x64.zip.sha256) accompanies the download; [verification instructions](docs/guide.md#verify-the-archive) are in the guide.
2. Run **ARCYN.exe**. Name your first workspace and add the apps, folders, and websites you want. Optional setup steps have **Skip**.
3. Choose the workspace or press its number to open it.

Keep the extracted files together. The ZIP includes its runtime; no separate .NET installation is needed. The build is unsigned, so Windows may warn or block it. Only run a download whose source you trust. [Download guidance and release status →](docs/guide.md#downloads-and-windows-warnings)

## Make it yours

ARCYN targets **Windows 10/11 x64**. Appearance follows Windows by default, with light, dark, accent, text-size, and motion settings available.

**Alt + Shift + D launches ARCYN from anywhere** when the optional helper is running. The helper is **disabled by default**. To activate it for your current session, open **ARCYN.Hotkey.exe** from the extracted application folder.

To start the helper automatically at your next Windows sign-in, open PowerShell in that folder and run:

```powershell
./scripts/Setup-Shortcuts.ps1 -EnableHotkeyHelper
```

See the [user guide for custom shortcuts and disabling the helper](docs/guide.md#appearance-and-shortcuts).

The shortcuts below work **inside ARCYN**:

| Key | Action |
| --- | --- |
| `1`–`9` | Open a workspace |
| Arrow keys, `Enter` | Select and open |
| `Esc` | Cancel a launch or close ARCYN |
| `F2` / `Ctrl+N` | Edit / create a workspace |
| `Ctrl+,` | Open settings |

[User guide](docs/guide.md) · [Release notes and testing status](docs/release-notes.md)

## Open source

Found a rough edge? [Report a problem](https://github.com/bugged-bit/Arcyn/issues/new?template=bug_report.md) or [suggest an improvement](https://github.com/bugged-bit/Arcyn/issues/new?template=feature_request.md). Contributions to code, documentation, and testing are welcome; start with [contributing](CONTRIBUTING.md).

ARCYN is [MIT licensed](LICENSE), built with .NET and WPF, and uses Microsoft's Fluent controls. [Third-party credits and licenses](https://github.com/bugged-bit/Arcyn/blob/master/ARCYN/Notices/README.txt) accompany the application.
