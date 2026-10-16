using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ARCYN.UI;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        _passed++; Console.WriteLine("PASS " + label);
    }
    [STAThread]
    private static int Main()
    {
        try { Run(); Console.WriteLine($"{_passed} checks passed; no launcher windows or applications opened."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var user = Path.Combine(directory, "user"); var exe = Path.Combine(directory, "exe");
            Directory.CreateDirectory(user); Directory.CreateDirectory(exe);
            var json = """{"theme":{"accent":"#123456","glow_opacity":0.4,"future":7},"behavior":{"idle_timeout_seconds":42,"close_on_launch":false},"future":{"keep":true},"modes":[{"name":"OFFLINE","folders":["Z:\\Unavailable"],"future":3}]}""";
            File.WriteAllText(Path.Combine(user, "arcyn.json"), json);
            File.WriteAllText(Path.Combine(exe, "arcyn.json"), "{}");
            var service = new ConfigService(null, user, exe);
            var config = service.Load()!;
            Check(config.Modes.Count == 1 && config.Modes[0].Folders.Count == 1, "unavailable folders and their cards survive loading");
            config.Modes[0].Name = "RENAMED";
            service.Save(config);
            var loaded = service.Load()!;
            Check(loaded.Theme.Accent == "#123456" && loaded.Behavior.IdleTimeoutSeconds == 42 && !loaded.Behavior.CloseOnLaunch, "workspace saves preserve all settings");
            Check(loaded.Extra!["future"].GetProperty("keep").GetBoolean() && loaded.Theme.Extra!["future"].GetInt32() == 7 && loaded.Modes[0].Extra!["future"].GetInt32() == 3, "unknown settings round-trip");
            Check(File.ReadAllText(Path.Combine(exe, "arcyn.json")) == "{}" && service.GetOrCreatePath() == Path.Combine(user, "arcyn.json"), "read and write resolve the same user configuration");
            Check(File.ReadAllText(service.GetOrCreatePath() + ".bak") == json, "atomic replacement keeps previous-good backup");
            Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.SaveAsync(config))).GetAwaiter().GetResult();
            Check(service.Load()!.Modes[0].Label == "RENAMED" && Directory.GetFiles(user, "*.tmp").Length == 0, "serialized saves leave valid JSON and no temporary files");
            File.WriteAllText(Path.Combine(exe, "portable.json"), """[{"label":"LEGACY","type":"study","session":"SES 42","state":"FOCUS","targets":[{"cmd":"tool.exe","args":["a b","","C:\\end\\"],"working_directory":"C:\\","future":8}]}]""");
            var portable = new ConfigService(Path.Combine(exe, "portable.json"), user, exe);
            var legacy = portable.Load()!; portable.Save(legacy);
            var migrated = portable.Load()!.Modes[0];
            Check(migrated.Type == "study" && migrated.Session == "SES 42" && migrated.Targets[0].Args.SequenceEqual(new[] { "a b", "", "C:\\end\\" }) && migrated.Targets[0].Extra!["future"].GetInt32() == 8, "legacy migration preserves metadata, arguments and order");
            Check(portable.GetOrCreatePath() == Path.Combine(exe, "portable.json"), "explicit portable path retained");
            File.WriteAllText(Path.Combine(exe, "broken.json"), "{");
            try { new ConfigService(Path.Combine(exe, "broken.json")).Load(); throw new Exception("Corrupt config accepted"); }
            catch (InvalidDataException) { Check(File.ReadAllText(Path.Combine(exe, "broken.json")) == "{", "corrupt config never becomes first-run defaults"); }
            var literal = Path.Combine(directory, "folder with spaces", "app.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(literal)!); File.WriteAllText(literal, "test fixture, never executed");
            var target = new TargetConfig { Cmd = literal, Args = ["a b", "", "C:\\end\\", "a\"b"] };
            Check(LaunchService.TryPrepare(target, out var psi, out _) && psi.FileName == literal && psi.ArgumentList.SequenceEqual(target.Args), "literal spaced paths and exact argument list");
            Check(LaunchService.TryPrepare(new TargetConfig { Cmd = literal, LegacyCommandLine = true }, out psi, out _) && psi.FileName == literal, "legacy spaced executable is checked before parsing");
            Check(LaunchService.TryPrepare(new TargetConfig { Cmd = "tool.exe \"a b\" \"\"", LegacyCommandLine = true }, out psi, out _) && psi.ArgumentList.SequenceEqual(new[] { "a b", "" }), "legacy command-line parsing handles empty and spaced arguments");
            Check(LaunchService.TryPrepare(new TargetConfig { Cmd = "ms-clock:" }, out _, out _), "Windows protocol dispatch supported");
            Check(!LaunchService.TryPrepare(new TargetConfig { Cmd = "explorer.exe", Args = ["Z:\\Unavailable"] }, out _, out var error) && error!.Contains("unavailable"), "unavailable target reports repairable failure");
            long now = 100; var idle = new IdleDeadline(() => now); idle.Reset(); now += 10001;
            Check(idle.Expired(10) && !idle.Expired(0), "monotonic idle timeout and disabled timeout");
            idle.Reset(); Check(!idle.Expired(10), "returning to Ready resets idle deadline");
            LaunchChecks().GetAwaiter().GetResult();
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/ARCYN;component/Styles/Theme.xaml", UriKind.Relative) });
            using var window = new MainWindow(service, config);
            var dashboard = (Grid)window.FindName("DashboardGrid");
            typeof(MainWindow).GetMethod("LoadConfig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
            var state = (AppState)typeof(MainWindow).GetField("_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            state.TransitionTo(AppPhase.Ready);
            typeof(MainWindow).GetMethod("BuildDashboard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
            Check(dashboard.Children.Cast<ContentControl>().All(c => c.Opacity == 1), "cards rebuilt during Ready are visible");
            var boot = (System.Windows.Shapes.Rectangle)window.FindName("BootProgress");
            boot.Measure(new Size(200, 10)); boot.Arrange(new Rect(0, 0, 200, 10));
            Check(boot.HorizontalAlignment == HorizontalAlignment.Stretch && boot.ActualWidth > 0 && boot.RenderTransform is ScaleTransform, "progress fill has width and an animatable transform");
            Check(window.Width == 920 && window.Height == 490, "default HUD geometry retained");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task LaunchChecks()
    {
        using var log = new LogService();
        var orchestrator = new LaunchOrchestrator(log);
        var mode = new ModeConfig { Targets = [new() { Cmd = "https://example.com" }, new() { Cmd = "ms-clock:" }] };
        var calls = 0; orchestrator.Dispatch = _ => { calls++; return null; }; orchestrator.Cadence = _ => Task.CompletedTask;
        var result = await orchestrator.LaunchModeAsync(mode, CancellationToken.None);
        Check(result.Success && calls == 2, "successful shell dispatch needs no process handle");
        using var cancel = new CancellationTokenSource(); calls = 0;
        orchestrator.Dispatch = _ => { calls++; cancel.Cancel(); return null; };
        result = await orchestrator.LaunchModeAsync(mode, cancel.Token);
        Check(result.Canceled && calls == 1, "cancellation stops remaining dispatches");
        cancel.Dispose();
        using var preCanceled = new CancellationTokenSource(); preCanceled.Cancel(); calls = 0;
        result = await orchestrator.LaunchModeAsync(mode, preCanceled.Token);
        Check(result.Canceled && calls == 0, "lifetime cancellation prevents first dispatch");
    }
}
