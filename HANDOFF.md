# ARCYN

## TODO
- Verify boot sequence visually after latest z-order/focus fix. Confirm BootOverlay fully owns first phase, then clean fade to HUD.
- Verify `Esc` behavior in all active phases: boot/ready should close, launching should cancel and return ready.
- Refresh `HANDOFF.md` architecture notes. Current doc still describes old exe path, old close behavior, older card layout/history.
- Confirm runtime log location after single-file publish. Current launcher no longer leaves obvious `arcyn_launch.log` beside `ARCYN.exe`.
- Run one full interaction pass: keyboard focus on open, arrow navigation, Enter launch, idle auto-close, root click close, AHK hotkey path.
- If boot still feels wrong, tune boot timings and fade overlap only. Do not rework main HUD layout again.

Windows desktop HUD launcher. Alt+Shift+D → dot→line→container opening → boot → HUD (tactical header + dashboard grid + waveform). Single-window state-driven. Red-on-black tactical aesthetic. Three workspace modes (study/design/code) with per-mode visual intensity.

## Build
```powershell
cd ARCYN\ARCYN.UI
dotnet build -c Release
# Output: bin\Release\net8.0-windows\win-x64\ARCYN.dll

dotnet publish -c Release
# Published EXE: bin\Release\net8.0-windows\win-x64\publish\ARCYN.exe (3.9MB single-file)
# Deploy: copy publish\ARCYN.exe to target dir
```

EXE is framework-dependent (SelfContained=false). Requires .NET 8 runtime. Single-file publish (PublishSingleFile=true).

## Structure
```
D:\protocoll\
  HANDOFF.md, .gitignore
  ARCYN.exe                          Published launcher (copied from publish output)
  ARCYN/
    ARCYN.sln, protocoll.json        Mode definitions (STUDY/DESIGN/CODE)
    scripts/launch.ahk               Alt+Shift+D, Win+F12 kill, refs ..\ARCYN.exe
    ARCYN.UI/
      App.xaml(.cs)                  Startup + global exception handlers
      AppState.cs                    BOOT→READY→SELECT→LAUNCH→CLOSE (guarded)
      TelemetryMonitor.cs            CPU/RAM via PerformanceCounter + WMI
      ParticleEngine.cs              60 red pooled particles
      NativeMethods.cs               Acrylic + Win32 P/Invoke
      MainWindow.xaml(.cs)           HUD, render loop, dashboard (940 lines)
      Services/
        LogService.cs                Async StreamWriter logger
        AnimationService.cs          Fade/Resize/Scale helpers
        RenderService.cs             CompositionTarget wrapper, subscriber pattern
      Models/ModeConfig.cs           label + subtitle + type + targets[]
      Styles/Theme.xaml              Accent (#D64545), WorkstationPanelStyle
      Assets/Fonts/                  JetBrainsMonoNerd.ttf
```

## Architecture

### State Machine
```
BOOT → READY → SELECTING → LAUNCHING → READY
             ↘ CLOSING
```
Guarded transitions. PhaseChanged drives panels + logging. Re-entry guard `_isLaunching` prevents double-launch.

### Single Window
No LoadingWindow. ModePanel/LaunchPanel crossfade in MainHUD. No Hide/Show.
All cleanup in `OnPhaseChanged(Closing)`.

### Telemetry Header
Full-width header bar: status dot, ARCYN label, mode count, phase label, CPU/RAM/SYS/ONLINE, uptime, clock.

### Dashboard (BuildDashboard)
Code-behind Grid generation. 1 column, 3 rows (each `1*`). Cards are horizontal workspace bays filling full width. No telemetry strip.

### Mode Cards (ModeCardTemplate in Window.Resources)
Button with WorkstationPanelStyle. Wide horizontal bay, 4-row internal layout:

```
┌─────────────────────────────────────────────────────────────────┐
│ ● STUDY    focus workspace                          4 items     │  20px Bold, 12px subtitle
│ ▸ ncert.nic.in  ▸ tiwariacademy.com  ▸ ms-clock  ▸ D:\study    │  11px, WrapPanel
│                                                                  │  * spacer
│ ────────────────────────────────────────────────────  [LAUNCH]  │  9px footer
└─────────────────────────────────────────────────────────────────┘
```

- Title row: dot + 20px Bold label + 12px subtitle + count badge — all one line
- Targets: WrapPanel with 11px ▸ bullets at 0.5 opacity, 14px gap between items
- Footer: separator + `[LAUNCH]` — clean, no bordered button
- Margins: 14px sides, 6px top, 8px bottom
- Single accent color (ARCYN red #D64545) across all cards — no per-mode colors

Hover: border #77→#EED64545, left accent 0.5→1.0, glow 0.45.
Focus: border #CCD64545, accent 1.0, glow 0.30.
Pressed: flash border #FFF08080 + glow 0.7 (40ms).

### Launch System
Each mode launches all targets concurrently via `cmd /c start "" <target>`.
Non-URL targets resolved via shell (PATH, App Paths, .lnk).
Args auto-quoted. Progress via spinner + dots (DispatcherTimer 400ms) + progress bar.

### Mode Visuals
Per-type intensity multiplier:
- STUDY:  0.5× particles, 0.8× glow, calmer
- DESIGN: 1.3× particles, 1.3× glow, expressive
- CODE:   1.5× particles, 1.1× glow, dense + verbose telemetry labels

### Render Loop (RenderService)
OnRenderTick dispatches at fixed intervals:
| System        | Interval | Notes                             |
|---------------|----------|-----------------------------------|
| Particles     | variable | × mode multiplier, min 8ms max 60ms |
| Ambient pulse | variable | × mode multiplier, sin pulse      |
| Spinner       | every    | RotateTransform during Launching  |
| Waveform      | 60ms     | 80-point harmonic + noise buffer  |
| Cursor trail  | ~16ms    | Only in Ready, max 25             |
| Telemetry     | 1000ms   | CPU/RAM sample + UI update        |
| Time+uptime   | 1000ms   | clock + UP display                |

### Close Triggers
- 10s idle timeout (Ready only, 1s DispatcherTimer)
- Window deactivated (focus loss, not during Boot/Closing)
- Background click on RootGrid (not during Boot)
- Esc cancels in-flight launch only

### Opening Sequence
1. Dot→line: OuterShell W 6→920 (300ms ease-out) + flash
2. Line→container: H 6→490 (300ms ease-out)
3. Boot text: "A R C Y N" → "SYSTEM BOOT SEQUENCE · v1.0" → progress bar (900ms) → 5 log lines at 100ms each
4. SYSTEM READY pulse → telemetry sample → BootOverlay fade (150ms) → MainHUD fade (120ms) → cards stagger (30ms each) → footer → focus first card
All interrupt-safe via CancellationToken.

## Config
Exe directory `protocoll.json`:
```json
[
  {
    "label": "STUDY",
    "subtitle": "focus-oriented workspace",
    "type": "study",
    "targets": [
      {"cmd": "https://ncert.nic.in"},
      {"cmd": "explorer.exe", "args": ["D:\\study material"]}
    ]
  }
]
```
Fallback to hardcoded defaults in `GetDefaultModes()` if no config found.

## Refactor History (2026-05-24)

### Session 1: Major stabilization + cleanup
- Added `SafeDelay` helper → replaces fragile try/catch Delay patterns
- Added `_isLaunching` re-entry guard in `SelectMode`
- Moved launch-dot animation from render loop to `DispatcherTimer`
- Cached brush resources at init → removed `FindResource` from per-frame loop
- Many layout tightness improvements (margins, padding, gaps)
- Theme cleanup: removed unused brushes, reduced scanlines/ambient opacities
- Removed `ShellHost` container, redundant glow, reduced header columns 11→9

### Session 2: Dashboard grid + typography sharpening
- Sharpened typography: glow BlurRadius 4→2 and 2→1, lowered opacities, added `TextFormattingMode="Display"` on Window + borders
- Fixed orphaned XAML subtitle attributes (lines 262-265)
- Replaced WrapPanel with UniformGrid→WrapPanel ItemWidth="306" + VerticalAlignment="Top"
- Card backgrounds: 5%→78% opaque (#C8080808), border #22→#66
- Removed ScaleTransform hover animations (caused blur)
- Card target row Height="*" → Height="Auto" (content-sized)
- Extracted ModeCardTemplate into Window.Resources
- Replaced ItemsControl with Grid (DashboardGrid) + code-behind BuildDashboard()
- Added WidgetPanelStyle + CreateSysWidget() telemetry panel (CPU/RAM/SYS/UP)
- 2 equal columns (2\*/2\*/1\*) dashboard layout
- Project cleanup: removed 513 files, 27 dirs, updated .gitignore
- Published ARCYN.exe (3.9MB single-file) to root launcher path

### Session 3: Telemetry widget visual unification
- Added left accent bar (3px, opacity 0.5) to telemetry widget (matches mode card style)
- Improved widget typography: header 10→12px SemiBold, data rows 10→11px, uptime 9→11px
- Added GlowAccentSoft effect to widget status dot
- Added `System.Windows.Media.Effects` import for Effect resource cast
- Rebuilt and republished ARCYN.exe

### Session 4: Modes-first hierarchy + compact tactical modules
- **Dashboard**: 3-column layout (1\*/1\*/1\*) — modes dominate, telemetry removed from equal footing
- **Telemetry**: stripped from dashboard panel → compact 24px horizontal strip below modes (SysStripStyle, #33D64545 subdued border)
- **WorkstationPanelStyle**: borders sharpened (#77→#EE on hover, #CC focus, #FFF08080 pressed flash), HoverBg #18→#22D64545, response times tightened
- **ModeCardTemplate**: targets switched from bulleted list to WrapPanel chip tags, status bar simplified to separator + [LAUNCH] action hint, title 24px Bold
- **WidgetPanelStyle** removed (unused), replaced by SysStripStyle

### Session 5: Card redesign — per-mode accent colors (3 iterations, see Session 6 for final)
- Iterated through compact chip layouts, pill badges, section headers — all produced dashboard-widget feel

### Session 6: Cinematic workspace launcher — cards as immersive workspace bays
- **Telemetry strip removed** from dashboard (`SysStripStyle`, `CreateSysStrip`, widget fields deleted). Header-only system status.
- **Dashboard**: single row `*` height, 3 columns. Tall vertical cards.
- **Card template**: 7 rows — 28px Bold title, TARGETS section header, bulleted list, bordered LAUNCH button. Cinematic negative space via `*` spacer.
- **Theme**: left accent 3px→4px, GlowOverlay `#66`→`#88D64545`.

### Session 7: Horizontal workspace bays + unified theme
- **Layout flipped**: 1 column, 3 rows (each `1*`) — cards are wide horizontal bars filling full width.
- **Card template**: 4 rows — title+subtitle inline, WrapPanel target list, `*` spacer, footer.
- **Title**: dot + 20px Bold label + 12px subtitle + N items count — all one horizontal line. Clean hierarchy.
- **Targets**: WrapPanel with 11px ▸ bullets at 0.5 opacity, 14px inter-item gap. Flows to 2 lines if needed.
- **Footer**: separator + `[LAUNCH]` — no bordered button, just clean text.
- **Margins**: 14px horizontal, 6px top, 8px bottom — consistent breathing room.
- **Single accent**: removed all per-mode accent colors (`AccentStudyBrush`, `AccentDesignBrush`, `AccentCodeBrush`, `ModeAccentBrush`). One ARCYN red `#D64545` for all cards. LeftAccent back to `{StaticResource AccentBrush}`.
- **BuildDashboard**: simplified — single-column loop, no per-mode `ResourceDictionary` setup.

## Constraints
- .NET 8 runtime required
- WPF-only (no WinUI 3/MAUI)
- Framework-dependent publish (SelfContained=false, trimming breaks WPF)
- RuntimeIdentifier=win-x64 in csproj
- Cached `_cardWrappers`/`_cardButtons` lists replace `ItemContainerGenerator` for focus + stagger
