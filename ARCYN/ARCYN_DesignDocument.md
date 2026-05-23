# ARCYN – Autonomous Response & Command Yield Nexus

## Overview
ARCYN is a Windows‑only desktop utility built with WinUI 3 (.NET 8). It provides a minimalist, text‑only HUD that appears when the user presses **Alt + Shift + D**. The HUD displays a set of user‑defined modes (e.g., *Study*, *Code*, *Design*). Selecting a mode launches the associated commands and shows a lightweight loading overlay with telemetry.

## Project Structure
```
D:\protocoll\ARCYN\
│─ ARCYN.sln                         # Visual Studio solution
│─ ARCYN.UI\                        # WinUI 3 project
│   │─ App.xaml / App.xaml.cs
│   │─ MainWindow.xaml / MainWindow.xaml.cs   # Choice HUD
│   │─ LoadingWindow.xaml / LoadingWindow.xaml.cs   # Loading overlay
│   │─ Assets\
│   │   └─ Fonts\JetBrainsMonoNerd.ttf
│   └─ protocoll.json               # User‑defined mode configuration
│─ scripts\
│   └─ launch.ahk                    # AutoHotkey hot‑key script
```

## Naming & Branding
- Full name: **ARCYN – Autonomous Response & Command Yield Nexus**
- Executable name: `ARCYN.exe`
- No icons are used; the UI consists solely of text.

## Font
JetBrains Mono Nerd Font is embedded in `Assets/Fonts` and applied globally via `App.xaml`.

## Configuration (`protocoll.json`)
Located at `%LOCALAPPDATA%\ARCYN\protocoll.json`. Example content:
```json
[
  {
    "label": "Study",
    "cmd": "cmd",
    "args": ["/c","start https://ncert.nic.in && start ms-clock: && start https://www.selfstudys.com/"]
  },
  {
    "label": "Code",
    "cmd": "cmd",
    "args": ["/c","start wt.exe && start D:\ && start https://chat.openai.com && start ollama://"]
  },
  {
    "label": "Design",
    "cmd": "cmd",
    "args": ["/c","start https://www.canva.com && start https://www.pinterest.com && start https://effect.app/"]
  }
]
```
The file can contain any number of entries. Malformed entries are ignored at runtime.

## Choice HUD (MainWindow)
- Size: 800 × 450 px (16:9), centered on the primary monitor.
- Borderless, top‑most, hidden from the taskbar.
- Background: black acrylic with ~92 % opacity, rounded corners (16 px radius).
- Accent color: deep red.
- Content: a `UniformGrid` (or custom radial panel) that creates a button for each entry in the configuration file.
- Button style: red foreground text, subtle outer glow, hover effect that slightly scales the button and fades it in.
- Keyboard navigation: arrow keys to move focus, **Enter** to activate, **Esc** to dismiss.
- HUD fades in/out over ~0.2 seconds.

## Loading Overlay (LoadingWindow)
- Same size and ratio as the HUD, semi‑transparent black background.
- Centered rotating red arc created with a `Path` and animated via a `Storyboard` (continuous 0‑360° rotation).
- Telemetry displayed at the bottom:
  - **CPU %** (from `PerformanceCounter`)
  - **RAM %** (from `PerformanceCounter`)
  - **STATUS** (e.g., “Launching…”, “Ready”)
  - Optional **Ping/Latency** (lightweight ping to 8.8.8.8, configurable).
- Telemetry updates every 500 ms.
- Overlay fades in/out (0.2 s) and automatically closes after a short launch delay (default 2 s) or when the launched processes exit.

## Process Launching
Commands are started asynchronously using `Process.Start` with `UseShellExecute = true`. All targets defined for a mode are launched concurrently. Errors are caught silently; the STATUS text can be updated to indicate failure if desired.

## Global Hot‑Key (AutoHotkey v2)
File `scripts/launch.ahk`:
```ahk
!+d::
{
    Run "D:\protocoll\ARCYN\ARCYN.exe"
}
Esc::
{
    WinClose "ahk_exe ARCYN.exe"
}
```
Running this script registers **Alt + Shift + D** as the global shortcut that shows the HUD, and **Esc** closes the application.

## Build & Deployment
Run the following command from `D:\protocoll\ARCYN`:
```bat
dotnet publish -c Release -r win10-x64 ^
  /p:PublishSingleFile=true ^
  /p:PublishTrimmed=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true
```
The command produces a single, portable executable (`ARCYN.exe`) placed in the `publish` folder. Copy the executable (and the AutoHotkey script) to `D:\protocoll\ARCYN` for final use.

## Validation Checklist
1. **Hot‑key** summons the HUD instantly.
2. HUD appears centered, top‑most, with the correct size, background, and font.
3. Buttons correspond to the entries in `protocoll.json`.
4. Selecting a button launches all associated commands.
5. Loading overlay appears, shows the rotating red arc, and updates CPU, RAM, STATUS (and optional ping) every 500 ms.
6. Overlay closes automatically after launch or when processes exit.
7. Pressing **Esc** dismisses the HUD or loading overlay at any time.
8. No icons are displayed; UI is text‑only.
9. Startup time from hot‑key is ≤ 300 ms on a typical Windows 10 machine.

## Open Items for Confirmation
- Include optional ping/latency telemetry? (Yes/No)
- Desired final installation path (`D:\protocoll\ARCYN` is confirmed?)
- Any additional command‑line arguments or debug options needed?

Once these items are confirmed, the implementation can proceed.
