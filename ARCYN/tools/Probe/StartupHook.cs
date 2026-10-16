using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

// Measurement-only startup hook; never included in the application package.
internal static class StartupHook
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<double> Intervals = [];
    private static readonly Dictionary<string, double> Phases = [];
    private static double _lastFrame;
    private static double _readyCpu;
    private static double _readyAt;
    private static double? _firstFrame;
    private static double? _readyEndAt, _readyEndCpu;
    private static double _ageAtHook;
    private static double _dpiX = 1, _dpiY = 1;
    private static string[] _labels = [];

    public static void Initialize()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARCYN_PROBE_OUTPUT"))) return;
        _ = Clock.ElapsedTicks;
        using (var process = Process.GetCurrentProcess()) _ageAtHook = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        InputManager.Current.PreProcessInput += (_, args) => {
            if (args.StagingItem.Input is KeyEventArgs or MouseButtonEventArgs) args.Cancel();
        };
        CompositionTarget.Rendering += Render;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Save();
    }

    private static void Render(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame) return;
        var now = frame.RenderingTime.TotalMilliseconds;
        if (now == _lastFrame) return;
        _firstFrame ??= Clock.Elapsed.TotalMilliseconds;
        if (now > _lastFrame && _lastFrame > 0) Intervals.Add(now - _lastFrame);
        _lastFrame = now;
        var window = Application.Current?.MainWindow;
        if (window == null) return;
        var dpi = VisualTreeHelper.GetDpi(window); _dpiX = dpi.DpiScaleX; _dpiY = dpi.DpiScaleY;
        if (window.FindName("PhaseLabel") is TextBlock phase && !Phases.ContainsKey(phase.Text))
        {
            Phases[phase.Text] = Clock.Elapsed.TotalMilliseconds;
            if (phase.Text == "CLS" && _readyAt > 0)
            {
                using var process = Process.GetCurrentProcess();
                _readyEndAt = Clock.Elapsed.TotalMilliseconds; _readyEndCpu = process.TotalProcessorTime.TotalMilliseconds;
            }
            if (phase.Text == "RDY")
            {
                using var process = Process.GetCurrentProcess();
                _readyCpu = process.TotalProcessorTime.TotalMilliseconds;
                _readyAt = Clock.Elapsed.TotalMilliseconds;
                if (window.FindName("DashboardGrid") is System.Windows.Controls.Panel panel)
                    _labels = panel.Children.OfType<ContentControl>()
                        .Select(control => control.Content?.GetType().GetProperty("Label")?.GetValue(control.Content)?.ToString()
                            ?? control.Content?.GetType().GetProperty("Name")?.GetValue(control.Content)?.ToString() ?? "")
                        .ToArray();
            }
        }
    }

    private static void Save()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            Intervals.Sort();
            var output = new
            {
                ProcessId = Environment.ProcessId, Runtime = Environment.Version.ToString(),
                BaseDirectory = AppContext.BaseDirectory, ProcessPath = Environment.ProcessPath,
                WorkingDirectory = Environment.CurrentDirectory, LogicalProcessors = Environment.ProcessorCount,
                FirstRenderCallbackMs = _firstFrame, Phases,
                ProcessAgeAtHookMs = _ageAtHook, DpiScaleX = _dpiX, DpiScaleY = _dpiY,
                WorkspaceLabels = _labels,
                CpuMs = process.TotalProcessorTime.TotalMilliseconds,
                ReadyCpuMs = _readyAt > 0 ? (_readyEndCpu ?? process.TotalProcessorTime.TotalMilliseconds) - _readyCpu : (double?)null,
                ReadyDurationMs = _readyAt > 0 ? (_readyEndAt ?? Clock.Elapsed.TotalMilliseconds) - _readyAt : (double?)null,
                FrameCallbackP95Ms = Intervals.Count > 0 ? Intervals[(int)((Intervals.Count - 1) * .95)] : (double?)null,
                PeakWorkingSetBytes = process.PeakWorkingSet64,
                RenderTier = RenderCapability.Tier >> 16
            };
            File.WriteAllText(Environment.GetEnvironmentVariable("ARCYN_PROBE_OUTPUT")!,
                JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Measurement must never change application exit behavior. */ }
    }
}
