using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ARCYN.UI;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

internal static partial class Program
{
    // Component timings only: this never runs App.Startup, Show, registration or real dispatch.
    private static int Benchmark()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "bench-runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); LogService.DirectoryPath = Path.Combine(directory, "logs");
        try
        {
            using var process = Process.GetCurrentProcess();
            var results = new List<object>();
            void Measure(string name, int iterations, Action action)
            {
                action(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var cpu = process.TotalProcessorTime;
                var clock = Stopwatch.StartNew();
                for (var i = 0; i < iterations; i++) action();
                clock.Stop(); process.Refresh();
                results.Add(new { name, iterations, wallMs = clock.Elapsed.TotalMilliseconds,
                    cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated });
            }
            var first = Stopwatch.StartNew(); using var telemetry = new TelemetryMonitor(); first.Stop();
            var telemetryFirstMs = first.Elapsed.TotalMilliseconds;
            Measure("native telemetry sample", 10000, telemetry.Sample);
            var config = new ArcynConfig { Modes = Enumerable.Range(0, 10).Select(i => new ModeConfig { Label = "Workspace " + i, Targets = [new() { Cmd = "never-dispatched.exe", Args = ["a b", ""] }] }).ToList() };
            var json = JsonSerializer.Serialize(config, ConfigService.JsonOptions);
            Measure("deserialize and validate ten workspaces", 1000, () => ConfigService.Validate(JsonSerializer.Deserialize<ArcynConfig>(json, ConfigService.JsonOptions)!));
            var target = new TargetConfig { Cmd = "never-dispatched.exe", Args = ["a b", ""] };
            Measure("prepare target without dispatch", 10000, () => LaunchService.TryPrepare(target, out _, out _));
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/ARCYN;component/Styles/Theme.xaml", UriKind.Relative) });
            first.Restart(); using var window = new MainWindow(new ConfigService(Path.Combine(directory, "config.json")), config); first.Stop();
            var unshownWindowConstructionMs = first.Elapsed.TotalMilliseconds;
            Measure("apply theme to unshown HUD", 20, () => window.ApplyTheme(config.Theme));
            var canvas = new Canvas { Width = 800, Height = 480 }; using var particles = new ParticleEngine(canvas);
            particles.Start(); canvas.Measure(new Size(800, 480)); canvas.Arrange(new Rect(0, 0, 800, 480));
            Measure("tick sixty particles without presentation", 2000, () => particles.Tick());
            process.Refresh();
            Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount,
                pid = Environment.ProcessId, telemetryFirstMs, unshownWindowConstructionMs,
                harnessPrivateBytes = process.PrivateMemorySize64, harnessWorkingSetBytes = process.WorkingSet64, results,
                limitations = "Headless harness resource usage includes the runtime and test fixtures. No whole-app startup, Ready CPU/RAM or presented-frame measurements." }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(directory, true); }
    }
}
