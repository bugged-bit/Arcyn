using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ARCYN.UI.Models;

namespace ARCYN.UI;

public partial class LoadingWindow : Window, IDisposable
{
    private readonly ModeConfig _mode;
    private readonly int _modeIndex;
    private readonly DispatcherTimer _telemetryTimer = new();
    private DispatcherTimer? _dotTimer;
    private Storyboard? _spinnerStoryboard;
    private bool _disposed;
    private readonly CancellationTokenSource _cts = new();
    private readonly TelemetryMonitor _telemetry = new();
    private int _dotCount;

    public LoadingWindow(ModeConfig mode, int modeIndex)
    {
        InitializeComponent();
        _mode = mode;
        _modeIndex = modeIndex;
        Loaded += OnLoaded;
        Closed += (_, _) => Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Apply acrylic blur
        NativeMethods.EnableAcrylic(hwnd, 0xCC000000);
        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        ModeLabel.Text = $"MODE {_modeIndex + 1} · {_mode.Label}";
        StartSpinner();
        StartTelemetry();
    }

    private void StartSpinner()
    {
        var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.8)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        _spinnerStoryboard = new Storyboard();
        _spinnerStoryboard.Children.Add(anim);
        _spinnerStoryboard.Begin(SpinnerArc);
    }

    private void StartTelemetry()
    {
        _telemetryTimer.Interval = TimeSpan.FromMilliseconds(500);
        _telemetryTimer.Tick += UpdateTelemetry;
        _telemetryTimer.Start();

        _dotTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _dotTimer.Tick += (_, _)=>
        {
            _dotCount = (_dotCount + 1) % 4;
            StatusText.Text = "Launching" + new string('.', _dotCount);
        };
        _dotTimer.Start();
    }

    private void UpdateTelemetry(object? sender, EventArgs e)
    {
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        _telemetry.Sample();
        CpuText.Text = $"CPU {_telemetry.CpuPercent:F1}%";
        RamText.Text = $"RAM {_telemetry.RamPercent:F1}%";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _telemetryTimer.Stop();
        _dotTimer?.Stop();
        _spinnerStoryboard?.Stop(SpinnerArc);
        _cts.Cancel();
        _telemetry.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
