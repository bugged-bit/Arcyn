using System.Diagnostics;
using System.Management;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ARCYN.UI.Models;

namespace ARCYN.UI;

public partial class LoadingWindow : Window, IDisposable
{
    private readonly ModeConfig _mode;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramAvailCounter;
    private double _totalRamMb;
    private readonly DispatcherTimer _telemetryTimer = new();
    private Storyboard? _spinStoryboard;
    private bool _disposed;
    private CancellationTokenSource? _launchCts;

    public LoadingWindow(ModeConfig mode, int modeIndex)
    {
        InitializeComponent();
        _mode = mode;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.EnableAcrylic(hwnd, 0xCC000000);

        var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TOOLWINDOW);

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

        StartSpinner();
        InitCounters();
        StartTelemetry();
        _ = LaunchCommandsAsync();
    }

    private void StartSpinner()
    {
        var anim = new DoubleAnimation
        {
            From = 0, To = 360,
            Duration = new Duration(TimeSpan.FromSeconds(1)),
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        _spinStoryboard = new Storyboard();
        _spinStoryboard.Children.Add(anim);
        _spinStoryboard.Begin(SpinnerArc);
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
        StatusText.Text = "Launching...";
    }

    private void UpdateTelemetry(object? sender, EventArgs e)
    {
        try
        {
            if (_cpuCounter != null)
                CpuText.Text = $"CPU {_cpuCounter.NextValue():F1}%";
            if (_ramAvailCounter != null && _totalRamMb > 0)
            {
                var usedPct = ((_totalRamMb - _ramAvailCounter.NextValue()) / _totalRamMb) * 100;
                RamText.Text = $"RAM {usedPct:F1}%";
            }
        }
        catch { }
    }

    private async Task LaunchCommandsAsync()
    {
        _launchCts = new CancellationTokenSource();
        StatusText.Text = "Launching...";

        var tasks = _mode.Args.Select(arg => Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _mode.Command,
                    Arguments = arg,
                    UseShellExecute = true,
                    Verb = "open",
                    WindowStyle = ProcessWindowStyle.Normal
                });
            }
            catch { }
        }));

        await Task.WhenAll(tasks);
        StatusText.Text = "Ready";

        try { await Task.Delay(2000, _launchCts.Token); }
        catch (TaskCanceledException) { }

        if (!_disposed)
            Dispatcher.Invoke(CloseWithFade);
    }

    private void CloseWithFade()
    {
        var anim = new DoubleAnimation
        {
            From = 1, To = 0,
            Duration = new Duration(TimeSpan.FromSeconds(0.2))
        };
        anim.Completed += (_, _) =>
        {
            Owner?.Show();
            Close();
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _telemetryTimer.Stop();
        _telemetryTimer.Tick -= UpdateTelemetry;
        _spinStoryboard?.Stop(SpinnerArc);
        _launchCts?.Cancel();
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _telemetryTimer.Stop();
        _telemetryTimer.Tick -= UpdateTelemetry;
        _cpuCounter?.Dispose();
        _ramAvailCounter?.Dispose();
        _launchCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
