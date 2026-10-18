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
    private static Window? _window;
    private static TextBlock? _phase;
    private static readonly System.ComponentModel.DependencyPropertyDescriptor PhaseDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    public static void Initialize()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARCYN_PROBE_OUTPUT"))) return;
        _ = Clock.ElapsedTicks;
        using (var process = Process.GetCurrentProcess()) _ageAtHook = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
        InputManager.Current.PreProcessInput += BlockInput;
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
        if (_window == null) { _window = window; window.Closed += (_, _) => { EndReady(); CompositionTarget.Rendering -= Render; InputManager.Current.PreProcessInput -= BlockInput; if (_phase != null) PhaseDescriptor.RemoveValueChanged(_phase, PhaseChanged); _phase = null; _window = null; }; }
        var dpi = VisualTreeHelper.GetDpi(window); _dpiX = dpi.DpiScaleX; _dpiY = dpi.DpiScaleY;
        if (window.FindName("PhaseLabel") is TextBlock phase && !Phases.ContainsKey(phase.Text))
        {
            Phases[phase.Text] = Clock.Elapsed.TotalMilliseconds;
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
                CompositionTarget.Rendering -= Render; // Do not keep WPF rendering during the idle measurement.
                _phase = phase; PhaseDescriptor.AddValueChanged(phase, PhaseChanged);
            }
        }
    }
    private static void BlockInput(object sender, PreProcessInputEventArgs args)
    { if (args.StagingItem.Input is KeyEventArgs or MouseButtonEventArgs) args.Cancel(); }
    private static void PhaseChanged(object? sender, EventArgs e) { if (_phase?.Text == "CLS") { Phases["CLS"] = Clock.Elapsed.TotalMilliseconds; EndReady(); } }
    private static void EndReady()
    {
        if (_readyAt <= 0 || _readyEndAt != null) return;
        using var process = Process.GetCurrentProcess();
        _readyEndAt = Clock.Elapsed.TotalMilliseconds; _readyEndCpu = process.TotalProcessorTime.TotalMilliseconds;
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
                BootFrameCallbackP95Ms = Intervals.Count > 0 ? Intervals[(int)((Intervals.Count - 1) * .95)] : (double?)null,
                PeakWorkingSetBytes = process.PeakWorkingSet64,
                RenderTier = RenderCapability.Tier >> 16
            };
            File.WriteAllText(Environment.GetEnvironmentVariable("ARCYN_PROBE_OUTPUT")!,
                JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Measurement must never change application exit behavior. */ }
    }
}
