# ARCYN

Windows desktop HUD launcher. Alt+Shift+D → pick mode → launches apps.

## Structure
```
ARCYN.sln
ARCYN.exe / ARCYN.dll    148KB + 2.4MB (needs .NET 8 runtime)
protocoll.json            6 modes config

ARCYN.UI/                 .NET 8 WPF source
├─ App.xaml(.cs)          Entry
├─ MainWindow.xaml(.cs)   800x480 HUD, teal cinematic
├─ LoadingWindow.xaml(.cs) Overlay + CPU/RAM telemetry
├─ MatrixOverlay.cs       Floating hex digits BG
├─ ParticleEngine.cs      Floating teal particles
├─ NativeMethods.cs       DWM acrylic blur
├─ Models/ModeConfig.cs
├─ Styles/Theme.xaml      Teal palette, animations
└─ Assets/Fonts/          JetBrainsMonoNerd.ttf

scripts/
├─ launch.ahk             Alt+Shift+D (AHK v2)
├─ launch.bat             Batch launcher
└─ launch.ps1             PowerShell launcher
```

## UI
Teal/cyan palette on near-black. Cinematic, not neon, not boring.
- Animated floating hex digits + particles
- Scanlines + vignette overlay
- Hover scale + glow on buttons
- Fade in/out 0.25s

## Config
`%LOCALAPPDATA%\ARCYN\protocoll.json`
```json
[{ "label": "NAME", "cmd": "exe", "args": ["arg"] }]
```

## Build
```
dotnet publish ARCYN.UI\ARCYN.UI.csproj -c Release -r win-x64
```
