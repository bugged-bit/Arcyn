using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

namespace ARCYN.UI;

public partial class MainWindow : Window, IDisposable, RenderService.ISubscriber
{
    private readonly AppState _state = new();
    private readonly LogService _log;
    private readonly RenderService _render;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly List<ModeConfig> _modes;
    private readonly ConfigService _configService;
    private readonly ArcynConfig _config;
    private readonly IdleDeadline _idle = new();
    private bool _editing;
    private readonly List<ContentControl> _cardWrappers = [];
    private readonly List<Button> _cardButtons = [];
    private readonly List<TrailParticle> _cursorTrail = [];

    private Brush _accentBrightBrush = null!;
    private Brush _accentLightBrush = null!;
    private Brush _textPrimaryBrush = null!;
    private CancellationTokenSource? _lifeCts;
    private CancellationTokenSource? _launchCts;
    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _launchDotTimer;
    private ParticleEngine? _particles;
    private TelemetryMonitor? _telemetry;

    private bool _disposed;
    private bool _isClosing;
    private bool _isLaunching;
    private bool _mouseMoved;

    private long _accelAmbient;
    private long _accelParticle;
    private long _accelTelemetry;
    private long _accelTime;
    private long _accelTrail;


    private double _ambientPhase;
    private double _spinnerAngle;
    private int _launchDotIndex;
    private int _trailSkip;

    private Point _lastMousePos;
    private string? _activeModeType;

    private double _modeParticleMul = 1.0;
    private double _modeGlowMul = 1.0;
    private double _modeAmbientMul = 1.0;



    public MainWindow() : this(new ConfigService(), null) { }

    public MainWindow(ConfigService service, ArcynConfig? config)
    {
        _configService = service;
        _config = config ?? service.Load() ?? new ArcynConfig();
        _modes = _config.Modes;
        InitializeComponent();
        Topmost = _config.Behavior.AlwaysOnTop;
        PreviewMouseDown += (_, _) => ResetIdle();
        PreviewMouseWheel += (_, _) => ResetIdle();

        _log = new LogService();
        _render = new RenderService();
        _lifeCts = new CancellationTokenSource();
        _launchCts = new CancellationTokenSource();

        _state.PhaseChanged += OnPhaseChanged;
        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
        Closed += (_, _) => Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowPos(hwnd, Topmost ? NativeMethods.HWND_TOPMOST : IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        try
        {
            NativeMethods.EnableAcrylic(hwnd, 0xE20A0A0A);
        }
        catch
        {
            _log.Write("Acrylic enable skipped");
        }

        _log.Write("OnLoaded: start");
        Activate();
        Focus();
        Keyboard.Focus(this);

        _accentBrightBrush = TryFindResource("AccentBrightBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x80));
        _accentLightBrush = TryFindResource("AccentLightBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0xF7, 0xA0, 0xA0));
        _textPrimaryBrush = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

        LoadConfig();

        _telemetry = new TelemetryMonitor();
        _particles = new ParticleEngine(ParticleCanvas);

        _render.Subscribe(this);
        _render.Start();
        _particles.Start();
        InitCursorTrail();
        UpdateOperationalChrome();

        _log.Write("OnLoaded: runtime initialized");

        await PlayStartupSequence(_lifeCts!.Token);
        _log.Write("OnLoaded: boot sequence done");
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Log deactivation; do **not** close ARCYN on loss of focus.
        _log.Write("Window deactivated while Phase={0}", _state.Phase);
        // Keep ARCYN alive regardless of phase (except explicit closing via Escape).
        // No action taken.
    }

    void RenderService.ISubscriber.OnRenderTick(long dt)
    {
        if (_disposed || _state.Phase == AppPhase.Closing)
            return;

        _accelParticle += dt;
        var particleInterval = Math.Clamp((long)(30 / _modeParticleMul), 8, 60);
        if (_accelParticle >= particleInterval)
        {
            _accelParticle -= particleInterval;
            _particles?.Tick();
        }

        _accelAmbient += dt;
        var ambientInterval = Math.Clamp((long)(30 / _modeAmbientMul), 10, 50);
        if (_accelAmbient >= ambientInterval)
        {
            _accelAmbient = 0;
            _ambientPhase += 0.025 * _modeGlowMul;
            var baseGlow = 0.26 * _modeGlowMul;
            AmbientGlow.Opacity = Math.Clamp(baseGlow + Math.Sin(_ambientPhase) * 0.12 * _modeGlowMul, 0.08, 0.45);
        }

        _accelTrail += dt;
        if (_accelTrail >= 16 && _state.Phase == AppPhase.Ready)
        {
            _accelTrail = 0;
            UpdateCursorTrail();
        }

        _accelTelemetry += dt;
        if (_accelTelemetry >= 1000 && _state.Phase != AppPhase.Boot)
        {
            _accelTelemetry -= 1000;
            _telemetry?.Sample();
            UpdateTelemetryUI();
        }

        _accelTime += dt;
        if (_accelTime >= 1000)
        {
            _accelTime -= 1000;
            TimeDisplay.Text = DateTime.Now.ToString("HH:mm:ss");
            HeaderUptime.Text = $"UP {_uptime.Elapsed:hh\\:mm\\:ss}";
            RuntimeStrip.Text = $"RUNTIME {_uptime.Elapsed:hh\\:mm\\:ss}";
        }

        if (_state.Phase == AppPhase.Launching)
        {
            _spinnerAngle = (_spinnerAngle + dt * 0.15) % 360;
            SpinnerRotation.Angle = _spinnerAngle;
        }
    }

    private void OnPhaseChanged(AppPhase previous, AppPhase next)
    {
        _log.Write("State: {0} -> {1}", previous, next);
        PhaseLabel.Text = _state.PhaseLabel;
        UpdateOperationalChrome();

        if (next == AppPhase.Ready)
        {
            ModePanel.Opacity = 1;
            ModePanel.IsHitTestVisible = true;
            LaunchPanel.Visibility = Visibility.Collapsed;
        }

        if (next == AppPhase.Closing)
        {
            _particles?.Stop();
            _render.Stop();
        }
    }

    private async Task PlayStartupSequence(CancellationToken ct)
    {
        _state.TransitionTo(AppPhase.Boot);

        var flashAnim = new DoubleAnimation(0.08, 0, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        StartupFlash.BeginAnimation(UIElement.OpacityProperty, flashAnim);

        BootOverlay.Visibility = Visibility.Visible;
        BootOverlay.Opacity = 0;
        MainHUD.Opacity = 0;
        FooterHint.Opacity = 0;
        FooterIdle.Opacity = 0;

        await PlayShellExpansion(ct);
        if (ct.IsCancellationRequested) return;

        AnimationService.FadeIn(BootOverlay, 120);
        await DelaySafe(80, ct);
        if (ct.IsCancellationRequested) return;

        await TypeText(BootTitle, "A R C Y N", 25, ct);
        if (ct.IsCancellationRequested) return;

        await TypeText(BootSubtitle, "SYSTEM BOOT SEQUENCE  -  v1.0", 15, ct);
        if (ct.IsCancellationRequested) return;

        var progressAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        ((ScaleTransform)BootProgress.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, progressAnim);

        string[] bootMessages =
        [
            "LINKING LAUNCH PROFILES...",
            "TELEMETRY CHANNEL ONLINE",
            "PROCESS TABLE VERIFIED",
            "TACTICAL HUD STABLE",
            "SYSTEM CONFIGURED"
        ];
        string[] bootPercentages = ["24%", "47%", "68%", "89%", "100%"];

        foreach (var (message, index) in bootMessages.Select((value, idx) => (value, idx)))
        {
            if (ct.IsCancellationRequested)
                return;

            BootLog.Text += "\n> ";
            await TypeText(BootLog, message, 12, ct);
            BootPercent.Text = bootPercentages[index];
            await DelaySafe(100, ct);
        }

        if (ct.IsCancellationRequested)
            return;

        AnimationService.FadeIn(BootReady, 200);
        var pulseAnim = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        BootReady.BeginAnimation(UIElement.OpacityProperty, pulseAnim);

        await DelaySafe(350, ct);
        if (ct.IsCancellationRequested)
            return;

        BootReady.BeginAnimation(UIElement.OpacityProperty, null);

        _telemetry?.Sample();
        BootCpu.Text = $"CPU: {_telemetry?.CpuPercent ?? 0:F1}%";
        BootRam.Text = $"RAM: {_telemetry?.RamPercent ?? 0:F1}%";
        UpdateTelemetryUI();

        AnimationService.FadeOut(BootOverlay, 150);
        await DelaySafe(160, ct);
        if (ct.IsCancellationRequested)
            return;

        BootOverlay.Visibility = Visibility.Collapsed;
        MainHUD.Opacity = 1;
        AnimationService.FadeIn(MainHUD, 120);
        await DelaySafe(40, ct);
        if (ct.IsCancellationRequested)
            return;

        foreach (var wrapper in _cardWrappers)
        {
            if (ct.IsCancellationRequested)
                return;

            AnimationService.FadeIn(wrapper, 120);
            await DelaySafe(25, ct);
        }

        AnimationService.FadeIn(FooterHint, 120);
        AnimationService.FadeIn(FooterIdle, 120);

        _state.TransitionTo(AppPhase.Ready);
        StartIdleTimer();

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_cardButtons.Count > 0)
                _cardButtons[0].Focus();
        }, DispatcherPriority.ContextIdle);
    }

    private async Task PlayShellExpansion(CancellationToken ct)
    {
        UpdateLayout();

        var targetWidth = Math.Max(ShellHost.ActualWidth, 880);
        var targetHeight = Math.Max(ShellHost.ActualHeight, 450);

        MainBorder.Width = 6;
        MainBorder.Height = 6;

        AnimationService.ResizeTo(MainBorder, targetWidth, 6, 280, EasingMode.EaseOut);
        await DelaySafe(300, ct);
        if (ct.IsCancellationRequested) return;

        AnimationService.ResizeTo(MainBorder, targetWidth, targetHeight, 280, EasingMode.EaseOut);
        await DelaySafe(320, ct);
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModeConfig mode })
            return;

        var index = _modes.IndexOf(mode);
        if (index >= 0)
            SelectMode(index);
    }

    private void ApplyModeVisuals(string? type)
    {
        (_modeParticleMul, _modeGlowMul, _modeAmbientMul) = type switch
        {
            "study" => (0.45, 0.78, 0.70),
            "design" => (1.15, 1.12, 1.08),
            "code" => (1.35, 0.96, 0.92),
            _ => (1.0, 1.0, 1.0)
        };
    }

    private async void SelectMode(int index)
    {
        if (index < 0 || index >= _modes.Count || _isLaunching || _editing || _isClosing) return;
        if (!_state.TransitionTo(AppPhase.Selecting)) return;
        _isLaunching = true;
        _state.SelectedModeIndex = index;
        _launchCts?.Dispose();
        _launchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifeCts!.Token);
        var token = _launchCts.Token;
        try
        {
            var mode = _modes[index];
            _activeModeType = mode.Type;
            ApplyModeVisuals(mode.Type);
            UpdateOperationalChrome();
            AnimationService.FadeOut(ModePanel, 100);
            await DelaySafe(60, token);
            token.ThrowIfCancellationRequested();
            ModePanel.IsHitTestVisible = false;
            ModePanel.Visibility = Visibility.Collapsed;
            LaunchModeLabel.Text = mode.Label.ToUpperInvariant();
            LaunchSessionLabel.Text = $"{mode.SessionLabel}  -  {mode.StateLabel}";
            LaunchStatus.Text = "Launching";
            LaunchStatus.Foreground = _textPrimaryBrush;
            LaunchPanel.Opacity = 0;
            LaunchPanel.Visibility = Visibility.Visible;
            AnimationService.FadeIn(LaunchPanel, 100);
            SetLaunchProgress(0, Math.Max(mode.ProcessCount, 1));
            _state.TransitionTo(AppPhase.Launching);
            _launchDotTimer?.Stop();
            _launchDotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _launchDotTimer.Tick += (_, _) => {
                _launchDotIndex = (_launchDotIndex + 1) % 4;
                LaunchStatus.Text = "Launching" + new string('.', _launchDotIndex);
            };
            _launchDotTimer.Start();
            LaunchTargetCount.Text = $"{mode.ProcessCount} TARGETS";
            LaunchFeedText.Text = "";
            var progress = new Progress<LaunchProgress>(p => {
                if (_disposed || token.IsCancellationRequested) return;
                LaunchFeedText.Text += $"> {p.CurrentLabel}{(p.Error == null ? "" : "  [" + p.Error + "]")}\n";
                LaunchFeedScroll.ScrollToBottom();
                SetLaunchProgress(p.CompletedTargets, p.TotalTargets);
            });
            var result = await new LaunchOrchestrator(_log).LaunchModeAsync(mode, token, progress);
            _launchDotTimer.Stop();
            if (result.Canceled || token.IsCancellationRequested) { await ReturnToReady(); return; }
            LaunchStatus.Text = result.Success ? "Ready" : result.LaunchedTargets > 0 ? "Partial" : "Fault";
            LaunchStatus.Foreground = result.Success ? _textPrimaryBrush : _accentLightBrush;
            mode.RecordLaunch(result.LaunchedTargets, result.TotalTargets, result.Success);
            UpdateOperationalChrome();
            if (result.Failures.Count > 0) LaunchRecent.Text = "Issue: " + string.Join(", ", result.Failures);
            await DelaySafe(result.Success ? 2000 : 5000, token);
            if (token.IsCancellationRequested || !_config.Behavior.CloseOnLaunch) await ReturnToReady();
            else if (!_disposed) Close();
        }
        catch (OperationCanceledException) { await ReturnToReady(); }
        catch (Exception ex) { _log.Write("Launch error: {0}", ex); await ReturnToReady(); }
        finally { _launchDotTimer?.Stop(); _isLaunching = false; }
    }

    private async Task ReturnToReady()
    {
        if (!_state.TransitionTo(AppPhase.Ready))
            return;

        _launchDotTimer?.Stop();

        AnimationService.FadeOut(LaunchPanel, 100);
        await DelaySafe(60);

        LaunchPanel.Visibility = Visibility.Collapsed;
        ModePanel.Visibility = Visibility.Visible;
        ModePanel.Opacity = 0;
        AnimationService.FadeIn(ModePanel, 100);
        await DelaySafe(40);

        _activeModeType = null;
        ApplyModeVisuals(null);
        UpdateOperationalChrome();
        ResetIdle();
    }

    private void InitCursorTrail()
    {
        PreviewMouseMove += (_, e) =>
        {
            var position = e.GetPosition(TrailCanvas);
            if (position == _lastMousePos)
                return;

            _lastMousePos = position;
            _mouseMoved = true;
            ResetIdle();
        };
    }

    private void UpdateCursorTrail()
    {
        for (int i = _cursorTrail.Count - 1; i >= 0; i--)
        {
            var particle = _cursorTrail[i];
            particle.Life++;
            particle.X += particle.Vx;
            particle.Y += particle.Vy;

            if (particle.Life >= particle.MaxLife)
            {
                TrailCanvas.Children.Remove(particle.Element);
                _cursorTrail.RemoveAt(i);
                continue;
            }

            var ratio = particle.Life / particle.MaxLife;
            particle.Element.Opacity = 0.6 * (1 - ratio);
            var size = 2.5 * (1 - ratio * 0.6);
            particle.Element.Width = size;
            particle.Element.Height = size;
            Canvas.SetLeft(particle.Element, particle.X);
            Canvas.SetTop(particle.Element, particle.Y);
        }

        if (!_mouseMoved)
            return;

        _mouseMoved = false;
        _trailSkip++;
        if (_trailSkip % 3 != 0)
            return;

        var newParticle = new TrailParticle
        {
            Element = new Ellipse
            {
                Width = 2.5,
                Height = 2.5,
                Fill = _accentBrightBrush,
                IsHitTestVisible = false,
                Opacity = 0.6
            },
            Life = 0,
            MaxLife = 20,
            X = _lastMousePos.X,
            Y = _lastMousePos.Y,
            Vx = (Random.Shared.NextDouble() - 0.5) * 0.4,
            Vy = (Random.Shared.NextDouble() - 0.5) * 0.4
        };

        Canvas.SetLeft(newParticle.Element, newParticle.X);
        Canvas.SetTop(newParticle.Element, newParticle.Y);
        TrailCanvas.Children.Add(newParticle.Element);
        _cursorTrail.Add(newParticle);

        while (_cursorTrail.Count > 25)
        {
            var old = _cursorTrail[0];
            TrailCanvas.Children.Remove(old.Element);
            _cursorTrail.RemoveAt(0);
        }
    }

    private void UpdateTelemetryUI()
    {
        if (_telemetry == null)
            return;

        var cpu = _telemetry.CpuPercent;
        var ram = _telemetry.RamPercent;

        HeaderCpu.Text = $"CPU {cpu,4:F1}%";
        HeaderRam.Text = $"RAM {ram,4:F1}%";
        HeaderSys.Text = cpu < 75 && ram < 85 ? "ONLINE" : "LOAD";
        HeaderSys.Foreground = cpu < 75 && ram < 85 ? _accentBrightBrush : _accentLightBrush;

        SystemStrip.Text = cpu < 75 && ram < 85
            ? $"SYS {HeaderSys.Text}  -  HUD STABLE"
            : $"SYS {HeaderSys.Text}  -  CPU {cpu:F0}% / RAM {ram:F0}%";

        if (_state.Phase == AppPhase.Launching)
        {
            LaunchCpu.Text = $"CPU {cpu:F1}%";
            LaunchRam.Text = $"RAM {ram:F1}%";
        }

        var idleSeconds = _state.Phase == AppPhase.Ready ? (int)(_idle.Elapsed / 1000) : 0;
        FooterIdle.Text = idleSeconds > 0 ? $"idle {idleSeconds}s" : string.Empty;
    }

    private void StartIdleTimer()
    {
        _idle.Reset();
        _idleTimer?.Stop();
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += (_, _) => {
            if (_state.Phase == AppPhase.Ready && !_editing && _idle.Expired(_config.Behavior.IdleTimeoutSeconds)) CloseWithAnimation();
        };
        _idleTimer.Start();
    }
    private void ResetIdle()
    {
        _idle.Reset();
        FooterIdle.Text = string.Empty;
    }

    private async void CloseWithAnimation()
    {
        if (_isClosing || _disposed)
            return;

        _isClosing = true;
        _state.TransitionTo(AppPhase.Closing);
        _lifeCts?.Cancel();
        _launchCts?.Cancel();
        _idleTimer?.Stop();

        AnimationService.FadeOut(BootOverlay, 80);
        AnimationService.FadeOut(MainHUD, 80);
        await DelaySafe(60);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        BeginAnimation(OpacityProperty, fade);

        await DelaySafe(200);
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ResetIdle();

        if (e.Key == Key.Escape)
        {
            if (_state.Phase is AppPhase.Launching or AppPhase.Selecting)
            {
                LaunchStatus.Text = "Cancelling...";
                _launchCts?.Cancel();
            }
            else if (_state.Phase != AppPhase.Closing)
            {
                CloseWithAnimation();
            }

            e.Handled = true;
            return;
        }

        int? modeIndex = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            _ => null
        };

        if (modeIndex.HasValue)
        {
            SelectMode(modeIndex.Value);
            e.Handled = true;
            return;
        }

        if (_state.Phase == AppPhase.Ready && _modes.Count > 0 && ModePanel.Visibility == Visibility.Visible)
        {
            var focused = FocusManager.GetFocusedElement(this);
            var currentIndex = -1;
            if (focused is Button button && button.Tag is ModeConfig mode)
                currentIndex = _modes.IndexOf(mode);

            if (e.Key is Key.Down or Key.Right)
            {
                var nextIndex = (currentIndex + 1 + _modes.Count) % _modes.Count;
                FocusModeButton(nextIndex);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Up or Key.Left)
            {
                var previousIndex = currentIndex <= 0 ? _modes.Count - 1 : currentIndex - 1;
                FocusModeButton(previousIndex);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter && _state.Phase == AppPhase.Ready)
        {
            if (FocusManager.GetFocusedElement(this) is Button focusedButton)
                ModeButton_Click(focusedButton, new RoutedEventArgs());

            e.Handled = true;
            return;
        }
    }

    private void FocusModeButton(int index)
    {
        if (index < 0 || index >= _cardButtons.Count)
            return;

        _cardButtons[index].Focus();
        _cardButtons[index].BringIntoView();
    }

    private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_state.Phase == AppPhase.Closing)
            return;

        var position = e.GetPosition(MainBorder);
        var insideMainBorder =
            position.X >= 0 &&
            position.Y >= 0 &&
            position.X <= MainBorder.ActualWidth &&
            position.Y <= MainBorder.ActualHeight;

        if (!insideMainBorder)
            CloseWithAnimation();
    }

    private void LoadConfig()
    {
        ConfigService.Validate(_config);
        BuildDashboard();
        UpdateOperationalChrome();
    }

    private void BuildDashboard()
    {
        DashboardGrid.Children.Clear();
        DashboardGrid.ColumnDefinitions.Clear();
        DashboardGrid.RowDefinitions.Clear();
        _cardWrappers.Clear();
        _cardButtons.Clear();

        if (_modes.Count == 0)
            return;

        DashboardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var template = (DataTemplate)FindResource("ModeCardTemplate");

        for (int i = 0; i < _modes.Count; i++)
        {
            DashboardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var wrapper = new ContentControl
            {
                Content = _modes[i],
                ContentTemplate = template,
                Margin = new Thickness(0, 0, 0, 6),
                Opacity = _state.Phase == AppPhase.Boot ? 0 : 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };

            Grid.SetColumn(wrapper, 0);
            Grid.SetRow(wrapper, i);
            DashboardGrid.Children.Add(wrapper);
            _cardWrappers.Add(wrapper);
        }

        DashboardGrid.UpdateLayout();

        foreach (var wrapper in _cardWrappers)
        {
            if (FindVisualChild<Button>(wrapper) is { } button)
                _cardButtons.Add(button);
        }
    }

    private void UpdateOperationalChrome()
    {
        var totalProcesses = _modes.Sum(mode => mode.ProcessCount);
        var activeMode = _state.SelectedModeIndex >= 0 && _state.SelectedModeIndex < _modes.Count
            ? _modes[_state.SelectedModeIndex].Label.ToUpperInvariant()
            : "STANDBY";

        ModeCountRun.Text = _modes.Count.ToString("D2");
        ProcessStrip.Text = $"PROC {totalProcesses:D2}  -  ACTIVE {activeMode}";
        FooterHint.Text = $"{ShortcutSpan()} select  -  ENTER launch  -  ESC close";
        LaunchRecent.Text = _state.SelectedModeIndex >= 0 && _state.SelectedModeIndex < _modes.Count
            ? $"Recent: {_modes[_state.SelectedModeIndex].LastLaunchLabel}"
            : "Recent: none";
    }

    private string ShortcutSpan()
    {
        if (_modes.Count == 0)
            return "[1]";

        return _modes.Count == 1 ? "[1]" : $"[1-{_modes.Count}]";
    }

    private void SetLaunchProgress(int completed, int total)
    {
        var safeTotal = Math.Max(total, 1);
        var ratio = Math.Clamp((double)completed / safeTotal, 0, 1);
        LaunchProgressScale.ScaleX = ratio;
        LaunchProgressText.Text = $"PROC {Math.Min(completed, safeTotal):D2}/{safeTotal:D2}";
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private static async Task TypeText(TextBlock target, string text, int intervalMs, CancellationToken ct = default)
    {
        foreach (char character in text)
        {
            if (ct.IsCancellationRequested)
                return;

            target.Text += character;
            await DelaySafe(intervalMs, ct);
        }
    }

    private static async Task DelaySafe(int milliseconds, CancellationToken ct = default)
    {
        try
        {
            await Task.Delay(milliseconds, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _log.Write("ARCYN disposed");

        _lifeCts?.Cancel();
        _lifeCts?.Dispose();
        _launchCts?.Cancel();
        _launchCts?.Dispose();
        _idleTimer?.Stop();
        _launchDotTimer?.Stop();
        _render.Dispose();
        _telemetry?.Dispose();
        _particles?.Dispose();
        _log.Dispose();
    }

    private sealed class TrailParticle
    {
        public required Ellipse Element { get; init; }
        public double Life { get; set; }
        public double MaxLife { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Vx { get; init; }
        public double Vy { get; init; }
    }
}
