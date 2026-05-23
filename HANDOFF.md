# ARCYN

Windows desktop HUD launcher. Alt+Shift+D → cinematic overlay → pick mode (1-6) → launches apps with loading telemetry. Auto-closes after 15s idle. TARS × Nothing OS × Mr. Robot aesthetic.

## Build
`dotnet build -c Release` = **0 errors** (framework-dependent, needs .NET 8 runtime)

## Structure
```
D:\protocoll\
  HANDOFF.md
  .gitignore
  ARCYN/
    ARCYN.sln
    protocoll.json                6 modes
    scripts/
      launch.ahk                  Alt+Shift+D. Win+F12 kills.
      launch.bat / launch.ps1
    ARCYN.UI/
      App.xaml(.cs)               Hides console
      MainWindow.xaml(.cs)        800×480 HUD, acrylic, matrix + particles BG
      LoadingWindow.xaml(.cs)     Spinner + CPU/RAM + progress bar
      MatrixOverlay.cs            Hex columns (80ms), random brightness cascades
      ParticleEngine.cs           40 teal/cyan floating particles (30ms)
      NativeMethods.cs            DWM acrylic, Win32 styles
      Models/ModeConfig.cs
      Styles/Theme.xaml           Teal palette, button style, scanlines, vignette
      Assets/Fonts/               JetBrainsMonoNerd.ttf
```

## UI — Cinematic Teal

**Palette:** `#5FA3B3` on near-black `#080808`. Pulsing ARCYN title, glowing border, staggered button entrance (300ms → fade in one by one), scanlines + vignette overlay, 40 floating particles, hex matrix with random cascades.

**Dismiss:** 15s idle auto-close, click background, or press `1-6` to launch a mode. No Esc needed.

## Fixes This Session
- **Launch fix:** Args joined with spaces (`string.Join`) instead of spawning per-arg — apps now actually open
- **BorderSubtleBrush:** Missing resource added (`#225FA3B3`) — would crash at runtime
- **.gitignore:** Excludes bin/, obj/, *.dll — build artifacts removed from tracking
- **Esc removed:** No Esc handler in WPF. AHK hotkey changed to `Win+F12`.

## Session Changes
- **Idle timer:** 15s inactivity → fade out. Resets on mouse/key.
- **1-6 keys:** Quick-select modes. Arrow keys + Enter navigate.
- **Staggered entrance:** Separator lines draw from center → buttons fade in (70ms stagger) → footer
- **Pulsing elements:** ARCYN title breathes opacity. Border glow breathes (DropShadowEffect).
- **More particles:** 40 particles (was 25), 2-4px, teal/cyan variation, per-particle drift
- **Matrix cascades:** Random columns flash bright teal every ~2s
- **LoadingWindow:** Mode label, animated dots `"Launching..."`, animated progress bar on "Ready"
- **AHK:** `launch.ahk` — removed Esc (conflict), added `Win+F12` as kill switch

## Config
`%LOCALAPPDATA%\ARCYN\protocoll.json` or same dir as exe.
```json
[{ "label": "NAME", "cmd": "exe", "args": ["arg"] }]
```

## Git
- `6ad0e09` — fixes + gitignore
- No remote configured

## Constraints
- Framework-dependent publish only
- .NET 8 runtime required (SDK at `C:\Program Files\dotnet`)
- AHK v2 at `C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe`
- WPF only (WinUI 3 / MAUI unavailable)

## Commands
```powershell
dotnet build ARCYN.UI\ARCYN.UI.csproj -c Release
dotnet publish ARCYN.UI\ARCYN.UI.csproj -c Release -r win-x64
git remote add origin <url>; git push -u origin master
```
