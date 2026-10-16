using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
    private static bool _inputLocked;
    private static string[] _labels = [];

    public static void Initialize()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARCYN_PROBE_OUTPUT"))) return;
        _ = Clock.ElapsedTicks;
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
        if (!_inputLocked)
        {
            _inputLocked = true;
            window.PreviewKeyDown += (_, args) => args.Handled = true;
            window.PreviewMouseDown += (_, args) => args.Handled = true;
        }
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
                WorkspaceLabels = _labels,
                CpuMs = process.TotalProcessorTime.TotalMilliseconds,
                ReadyCpuMs = _readyAt > 0 ? process.TotalProcessorTime.TotalMilliseconds - _readyCpu : (double?)null,
                ReadyDurationMs = _readyAt > 0 ? Clock.Elapsed.TotalMilliseconds - _readyAt : (double?)null,
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
