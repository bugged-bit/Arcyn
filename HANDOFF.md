# ARCYN — Handoff

Windows desktop HUD launcher. Alt+Shift+D → overlay → pick mode → launches apps with loading telemetry.

---

## Current State — BUILDING CLEAN

- .NET 8 WPF, framework-dependent publish (148KB exe + 2.4MB dll)
- NuGet: `System.Management` 9.0.0 (CPU/RAM telemetry)
- Font: JetBrainsMono Nerd Font (embedded in Assets/)
- `dotnet build -c Release` = 0 errors

## Project Structure

```
D:\protocoll\
  HANDOFF.md             ← YOU ARE HERE
  .gitignore             ← excludes bin/, obj/, *.dll, etc.

  ARCYN\
    ARCYN.sln            VS solution
    protocoll.json       6 modes (STUDY/CODE/DESIGN/BROWSE/TERM/FILES)
    scripts/
      launch.ahk         Alt+Shift+D (AHK v2, tray icon)
      launch.bat         Batch fallback
      launch.ps1         PowerShell fallback

    ARCYN.UI/            Source project
      App.xaml(.cs)       Hides console, launches MainWindow
      MainWindow.xaml(.cs) 800x480 HUD, acrylic, matrix BG, 3×2 button grid
      LoadingWindow.xaml(.cs) Spinner + CPU/RAM telemetry (500ms), fade transitions
      MatrixOverlay.cs      Scrolling hex columns (80ms tick)
      ParticleEngine.cs     25 floating teal particles (30ms tick)
      NativeMethods.cs      DWM acrylic blur, Win32 styles (topmost, toolwindow)
      Models/ModeConfig.cs  JSON config model
      Styles/Theme.xaml     Teal palette, HudButton style, scanlines, vignette
      Assets/Fonts/         JetBrainsMonoNerd.ttf
```

## UI Theme: Teal Cinematic

Palette: `#5FA3B3` (teal) on near-black `#080808`

| Token | Value |
|-------|-------|
| Teal | `#5FA3B3` |
| TealBright | `#8ECCDB` |
| TealLight | `#B8E6F2` |
| TealDark | `#2A5C6E` |
| BgSurface | `#F7080808` |
| TextPrimary | `#F8FFFFFF` |
| TextSecondary | `#CCF0FFFF` |
| TextDim | `#775FA3B3` |
| BorderSubtle | `#225FA3B3` |

Layers (back to front):
1. Matrix hex digits (`MatrixOverlay`, Z=-10, ~57 columns)
2. Floating particles (`ParticleEngine`, 25 dots, Z=0)
3. Scanlines (3px repeating pattern, 15% opacity)
4. Semi-transparent surface border
5. Button grid (3×2 UniformGrid)
6. Vignette (radial gradient, dark edges)

Animations:
- Matrix hex scrolls down, fades in/out by vertical position
- Particles float upward with sine-wave drift, fade in/out over life
- Buttons scale 1.0→1.04 on hover, border glow, color shift (0.12-0.15s)
- Window fade in/out 0.25s

## Config

Search order:
1. `%LOCALAPPDATA%\ARCYN\protocoll.json`
2. Same dir as ARCYN.exe
3. Parent dir of ARCYN.exe

```json
[{ "label": "NAME", "cmd": "exe", "args": ["arg"] }]
```

## Build & Run

```
# Build
dotnet build ARCYN.UI\ARCYN.UI.csproj -c Release

# Publish (framework-dependent)
dotnet publish ARCYN.UI\ARCYN.UI.csproj -c Release -r win-x64

# Run
ARCYN\ARCYN.UI\bin\Release\net8.0-windows\ARCYN.exe

# Or via launcher
ARCYN\scripts\launch.ahk     # requires AHK v2
ARCYN\scripts\launch.bat
ARCYN\scripts\launch.ps1
```

Requires: .NET 8 runtime (x64), Windows 10/11

## Git

- Initial commit created (`edc6c8f`)
- `.gitignore` excludes bin/, obj/, *.dll
- Build artifacts removed from tracking
- No remote configured yet

## Issues Fixed This Session

1. **ParticleEngine.cs: missing `using System.Windows.Shapes`** — `Ellipse` type not found. Added `using System.Windows.Shapes` + `using System.Windows.Media.Animation`. Build 0 errors now.

2. **Theme.xaml: missing `BorderSubtleBrush` resource** — referenced by MainWindow.xaml and LoadingWindow.xaml for border backgrounds/brushes but never defined. Would crash at runtime. Added `SolidColorBrush` with `#225FA3B3`.

3. **No .gitignore** — bin/, obj/, and loose NuGet DLLs were tracked in initial commit. Created .gitignore and removed them from tracking.

4. **ParticleEngine.cs: unreachable `using` for `System.Windows.Media.Animation`** — leftover from earlier edit. Not harmful but cleaned up.

## Remaining Tasks / Wants

- [ ] **Set up GitHub remote** — no remote configured; user wants to push to GitHub
- [ ] **Tune visual effects** — particle density, speed, color intensity if user requests
- [ ] **Add more modes** — currently 6 hardcoded defaults; config file supports custom
- [ ] **User may want** — more visual depth, different animation styles, additional HUD features

## Key Constraints

- Framework-dependent publish only (self-contained = 150MB + crash bug)
- .NET SDK 8.0.419 at `C:\Program Files\dotnet`
- AHK v2 at `C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe`
- No files created outside `D:\protocoll\` unless user permits
- WinUI 3 / MAUI unavailable — WPF is the UI framework

## Commands Reference

```powershell
# Add remote & push
git remote add origin <url>
git push -u origin master

# Build
dotnet build ARCYN\ARCYN.UI\ARCYN.UI.csproj -c Release

# Publish
dotnet publish ARCYN\ARCYN.UI\ARCYN.UI.csproj -c Release -r win-x64

# Run published
ARCYN\ARCYN.UI\bin\Release\net8.0-windows\win-x64\publish\ARCYN.exe
```
