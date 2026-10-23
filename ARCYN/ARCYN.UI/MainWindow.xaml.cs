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
    private readonly LaunchOrchestrator _orchestrator;
    private readonly Dictionary<string, string> _runReports = [];
    private readonly RenderService _render;
    private readonly List<ModeConfig> _modes;
    private readonly ModeService _modeService;
    private readonly ConfigService _configService;
    private readonly ArcynConfig _config;
    private readonly IdleDeadline _idle = new();
    private bool _editing;
    private bool _menuOpen;
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

    private bool _disposed;
    private bool _isClosing;
    private bool _isLaunching;
    private bool _mouseMoved;

    private long _accelAmbient;
    private long _accelParticle;
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
        _modeService = new ModeService(_state, _modes);
        _currentTheme = _config.Theme;
        InitializeComponent();
        WindowSurface.Attach(this, _config.Theme, SurfaceRole.Launcher);
        WorkspaceError = message => WindowSurface.Show(this, "Workspace details", message);
        ApplyWindowBehavior(_config.Behavior);
        PreviewMouseDown += (_, _) => ResetIdle();
        PreviewMouseWheel += (_, _) => ResetIdle();
        Activated += (_, _) => ResetIdle();

        _log = new LogService();
        _orchestrator = new LaunchOrchestrator(_log) { AuthorizeCommands = ReviewCommandsAsync };
        _render = new RenderService();
        _lifeCts = new CancellationTokenSource();
        _launchCts = new CancellationTokenSource();

        _state.PhaseChanged += OnPhaseChanged;
        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
        Closed += (_, _) => Dispose();
        IsVisibleChanged += (_, _) => RefreshActivity();
        StateChanged += (_, _) => RefreshActivity();
        ApplyTheme(_config.Theme);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyWindowBehavior(_config.Behavior);
        NativeMethods.SetWindowPos(hwnd, Topmost ? NativeMethods.HWND_TOPMOST : IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        _log.Write("OnLoaded: start");
        Activate();
        Focus();
        Keyboard.Focus(this);

        _accentBrightBrush = TryFindResource("AccentBrightBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x80));
        _accentLightBrush = TryFindResource("AccentLightBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0xF7, 0xA0, 0xA0));
        _textPrimaryBrush = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;

        ApplyTheme(_config.Theme);
        LoadConfig();

        _particles = new ParticleEngine(ParticleCanvas);

        _render.Subscribe(this);
        _render.Start();
        _particles.Configure(_currentTheme.ReducedEffects ? 0 : _currentTheme.Particles, ThemeService.Map(Color.FromRgb(0xE8, 0x60, 0x60), _currentTheme));
        _particles.Start();
        _runtimeStarted = true;
        RefreshActivity();
        InitCursorTrail();
        UpdateOperationalChrome();
        StartIdleTimer();

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

        var effects = _currentTheme.Animations && !_currentTheme.ReducedEffects;
        _accelParticle += dt;
        var particleInterval = Math.Clamp((long)(30 / _modeParticleMul), 8, 60);
        if (effects && _accelParticle >= particleInterval)
        {
            _accelParticle -= particleInterval;
            _particles?.Tick(_currentTheme.MotionAmount);
        }

        _accelAmbient += dt;
        var ambientInterval = Math.Clamp((long)(30 / _modeAmbientMul), 10, 50);
        if (effects && _accelAmbient >= ambientInterval)
        {
            _accelAmbient = 0;
            _ambientPhase += 0.025 * _modeGlowMul * _currentTheme.MotionAmount;
            var baseGlow = 0.26 * _modeGlowMul;
            AmbientGlow.Opacity = Math.Clamp(Math.Clamp(baseGlow + Math.Sin(_ambientPhase) * 0.12 * _modeGlowMul, 0.08, 0.45) * _currentTheme.GlowOpacity / .28, 0, 1);
        }

        _accelTrail += dt;
        if (effects && _currentTheme.CursorTrail && _accelTrail >= 16 && _state.Phase != AppPhase.Ready)
        {
            _accelTrail = 0;
            UpdateCursorTrail();
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
        RefreshActivity();

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

        await TypeText(BootSubtitle, "ARCYN WORKSPACE  · " + App.ProductVersion, BootSubtitleIntervalMs, ct);
        await DelaySafe(BootSubtitleHoldMs, ct);
        if (ct.IsCancellationRequested) return;

        var progressAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        ((ScaleTransform)BootProgress.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, progressAnim);

        string[] bootMessages =
        [
            "Bringing things together…",
            "A little room to focus.",
            "Your workspaces await.",
            "Make space to work.",
            "Ready to begin."
        ];
        int[] typingBudgets = [26, 24, 22, 19, 17]; // Preserve each original typing + hold interval as the copy changes.
        string[] bootPercentages = ["24%", "47%", "68%", "89%", "100%"];

        foreach (var (message, index) in bootMessages.Select((value, idx) => (value, idx)))
        {
            if (ct.IsCancellationRequested)
                return;

            BootLog.Text += "\n";
            await TypeText(BootLog, message, 12, ct);
            BootPercent.Text = bootPercentages[index];
            await DelaySafe(100 + Math.Max(0, typingBudgets[index] - message.Length) * 12, ct);
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

        var targetWidth = Math.Max(ShellHost.ActualWidth, Width - 40);
        var targetHeight = Math.Max(ShellHost.ActualHeight, Height - 40);

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
        if (index < 0 || index >= _modes.Count || _isLaunching || _editing || _menuOpen || _isClosing) return;
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
            LaunchModeLabel.Text = mode.Label;
            LaunchSessionLabel.Text = string.IsNullOrWhiteSpace(mode.Session) ? mode.Subtitle : $"{mode.Session} · {mode.StateLabel}";
            var currentStage = "Opening workspace";
            var acceptProgress = true;
            LaunchStatus.Text = currentStage;
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
                LaunchStatus.Text = currentStage + new string('.', _launchDotIndex);
            };
            _launchDotTimer.Start();
            LaunchTargetCount.Text = $"{mode.ProcessCount + mode.Actions.Count} steps";
            LaunchFeedText.Text = "";
            var progress = new Progress<LaunchProgress>(p => {
                if (_disposed || token.IsCancellationRequested || !acceptProgress) return;
                currentStage = p.StepCompleted ? "Opening workspace" : p.CurrentLabel + " · " + (p.Evidence ?? "Opening");
                LaunchStatus.Text = currentStage;
                if (p.StepCompleted)
                {
                    LaunchFeedText.Text += $"{p.CurrentLabel} · {p.Evidence}{(p.Error == null ? "" : "  " + p.Error)}\n";
                    LaunchFeedScroll.ScrollToBottom();
                }
                SetLaunchProgress(p.CompletedTargets, p.TotalTargets);
            });
            var result = await _orchestrator.LaunchModeAsync(mode, token, progress);
            acceptProgress = false;
            _runReports[mode.Id] = FormatReport(result);
            ShowRunFeedback(result.Canceled ? "Canceled. Dispatched work remains open. Workspace menu → Last run details." : result.Success ? "Workspace ready." : "Workspace needs attention. Workspace menu → Last run details.");
            _launchDotTimer.Stop();
            if (result.Canceled || token.IsCancellationRequested) { await ReturnToReady(); return; }
            LaunchStatus.Text = result.Success ? "Complete" : result.CompletedSteps > 0 ? "Partial" : "Fault";
            LaunchStatus.Foreground = result.Success ? _textPrimaryBrush : _accentLightBrush;
            mode.RecordLaunch(result.CompletedSteps, result.TotalTargets, result.Success);
            UpdateOperationalChrome();
            await DelaySafe(result.Success ? 2000 : 5000, token);
            if (token.IsCancellationRequested || !_config.Behavior.CloseOnLaunch || !result.Success) await ReturnToReady();
            else if (!_disposed) Close();
        }
        catch (OperationCanceledException) { ShowRunFeedback("Canceled. Dispatched work remains open."); await ReturnToReady(); }
        catch (Exception ex) { _log.Write("Launch error: {0}", ex); ShowRunFeedback("Could not finish opening the workspace: " + ex.Message); await ReturnToReady(); }
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
        FocusModeButton(_state.SelectedModeIndex);
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
            var transform = (TransformGroup)particle.Element.RenderTransform;
            var scale = (ScaleTransform)transform.Children[0];
            scale.ScaleX = scale.ScaleY = size / 2.5;
            var position = (TranslateTransform)transform.Children[1];
            position.X = particle.X; position.Y = particle.Y;
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

        newParticle.Element.RenderTransform = new TransformGroup { Children = new TransformCollection {
            new ScaleTransform(1, 1), new TranslateTransform(newParticle.X, newParticle.Y) } };
        TrailCanvas.Children.Add(newParticle.Element);
        _cursorTrail.Add(newParticle);

        while (_cursorTrail.Count > 25)
        {
            var old = _cursorTrail[0];
            TrailCanvas.Children.Remove(old.Element);
            _cursorTrail.RemoveAt(0);
        }
    }

    private void StartIdleTimer()
    {
        _idle.Reset();
        _idleTimer?.Stop();
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += (_, _) => {
            var timeout = _config.Behavior.IdleTimeoutSeconds;
            FooterIdle.Text = _state.Phase == AppPhase.Ready && timeout > 0 && !_editing && !_menuOpen ? $"Closes in {Math.Max(0, timeout - (int)(_idle.Elapsed / 1000))}s" : "";

            if (_state.Phase == AppPhase.Ready && !_editing && !_menuOpen && _idle.Expired(_config.Behavior.IdleTimeoutSeconds)) CloseWithAnimation();
        };
        RefreshActivity();
    }
    private void ResetIdle()
    {
        _idle.Reset();
        FooterIdle.Text = string.Empty;
        RefreshActivity();
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
        if (HandleEditorKey(e)) { e.Handled = true; return; }
        if (_editing) return;

        if (e.Key == Key.Escape)
        {
            if (_isLaunching || _state.Phase is AppPhase.Launching or AppPhase.Selecting)
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
            if (focused is Button { Tag: not ModeConfig }) return;
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

        if (e.Key == Key.Enter && _state.Phase == AppPhase.Ready && FocusManager.GetFocusedElement(this) is Button { Tag: ModeConfig } focusedButton)
        {
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
        {
            var empty = new TextBlock { Text = "Your workspace starts here.\nAdd the apps, websites and folders you use together, then open them in one deliberate move.", TextWrapping = TextWrapping.Wrap, Margin = new(18, 24, 18, 24), FontSize = 16 };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush"); DashboardGrid.Children.Add(empty); return;
        }

        var columns = _currentTheme.Layout == "grid" ? 2 : 1;
        for (var c = 0; c < columns; c++) DashboardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var template = (DataTemplate)FindResource("ModeCardTemplate");

        for (int i = 0; i < _modes.Count; i++)
        {
            if (i % columns == 0) DashboardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var wrapper = new ContentControl
            {
                Content = _modes[i],
                ContentTemplate = template,
                Margin = new Thickness(0, 0, columns > 1 && i % columns == 0 ? _currentTheme.Spacing : 0, _currentTheme.Spacing),
                Opacity = _state.Phase == AppPhase.Boot ? 0 : 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };

            Grid.SetColumn(wrapper, i % columns);
            wrapper.ContextMenu = MakeWorkspaceMenu(_modes[i]);
            Grid.SetRow(wrapper, i / columns);
            DashboardGrid.Children.Add(wrapper);
            _cardWrappers.Add(wrapper);
        }

        DashboardGrid.UpdateLayout();
        foreach (var wrapper in _cardWrappers) wrapper.Loaded += (_, _) => ResolveCardButtons();
        ResolveCardButtons();
    }

    private void ResolveCardButtons()
    {
        _cardButtons.Clear();

        foreach (var wrapper in _cardWrappers)
        {
            if (FindVisualChild<Button>(wrapper) is { } button)
                _cardButtons.Add(button);
        }
    }

    private void UpdateOperationalChrome()
    {
        FooterHint.Text = $"{ShortcutSpan()} open  ·  F2 edit  ·  Ctrl+N new  ·  Ctrl+, settings  ·  Esc close";
    }

    private string ShortcutSpan()
    {
        if (_modes.Count == 0)
            return "[1]";

        return _modes.Count == 1 ? "[1]" : $"[1-{Math.Min(_modes.Count, 9)}]";
    }

    private void SetLaunchProgress(int completed, int total)
    {
        var safeTotal = Math.Max(total, 1);
        var ratio = Math.Clamp((double)completed / safeTotal, 0, 1);
        LaunchProgressScale.ScaleX = ratio;
        LaunchProgressText.Text = $"{Math.Min(completed, safeTotal)} of {safeTotal} steps";
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

    internal const int BootSubtitleIntervalMs = 15;
    internal const int BootSubtitleHoldMs = 75; // Retain the original 435 ms subtitle phase after removing "-rc.1".

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
        _state.TransitionTo(AppPhase.Closing);
        _log.Write("ARCYN disposed");

        _lifeCts?.Cancel();
        _lifeCts?.Dispose();
        _launchCts?.Cancel();
        _launchCts?.Dispose();
        _idleTimer?.Stop();
        _launchDotTimer?.Stop();
        _render.Dispose();
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
