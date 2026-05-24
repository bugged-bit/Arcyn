using System;
using System.Diagnostics;
using System.Management;
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
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramAvailCounter;
    private double _totalRamMb;
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
        InitCounters();
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

    private void InitCounters()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _cpuCounter.NextValue();
        }
        catch { _cpuCounter = null; }

        try { _ramAvailCounter = new PerformanceCounter("Memory", "Available MBytes"); }
        catch { _ramAvailCounter = null; }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
                _totalRamMb = Convert.ToDouble(obj["TotalPhysicalMemory"]) / (1024.0 * 1024.0);
        }
        catch { _totalRamMb = 16384; }
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
        try
        {
            if (_cpuCounter != null)
                CpuText.Text = $"CPU {_cpuCounter.NextValue():F1}%";
            if (_ramAvailCounter != null && _totalRamMb > 0)
            {
                var used = _totalRamMb - _ramAvailCounter.NextValue();
                var pct = Math.Clamp(used / _totalRamMb * 100, 0, 100);
                RamText.Text = $"RAM {pct:F1}%";
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _telemetryTimer.Stop();
        _dotTimer?.Stop();
        _spinnerStoryboard?.Stop(SpinnerArc);
        _cts.Cancel();
        _cpuCounter?.Dispose();
        _ramAvailCounter?.Dispose();
        GC.SuppressFinalize(this);
    }
}
