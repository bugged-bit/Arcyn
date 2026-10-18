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
    private static int _passed;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        _passed++; Console.WriteLine("PASS " + label);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--benchmark"]) return Benchmark();
        if (args is ["--workspace-benchmark"]) return WorkspaceBenchmark();
        try { Run(); Console.WriteLine($"{_passed} checks passed on .NET {Environment.Version}; no launcher windows or applications opened."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Run()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        LogService.DirectoryPath = Path.Combine(directory, "logs");
        try
        {
            var user = Path.Combine(directory, "user"); var exe = Path.Combine(directory, "exe");
            Directory.CreateDirectory(user); Directory.CreateDirectory(exe);
            var json = """{"theme":{"accent":"#123456","glow_opacity":0.4,"future":7},"behavior":{"idle_timeout_seconds":42,"close_on_launch":false},"future":{"keep":true},"modes":[{"name":"OFFLINE","folders":["Z:\\Unavailable"],"future":3}]}""";
            File.WriteAllText(Path.Combine(user, "arcyn.json"), json);
            File.WriteAllText(Path.Combine(exe, "arcyn.json"), "{}");
            Task.Run(() => ThemeService.Validate(new())).GetAwaiter().GetResult();
            Check(typeof(ThemeService).GetField("_baseResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) == null,
                "background configuration validation leaves Fluent UI resources uninitialized");
            var service = new ConfigService(null, user, exe);
            var config = service.Load()!;
            Check(config.Modes.Count == 1 && config.Modes[0].Folders.Count == 1, "unavailable folders and their cards survive loading");
            config.Modes[0].Name = "RENAMED";
            service.Save(config);
            var loaded = service.Load()!;
            using (var canonical = JsonDocument.Parse(File.ReadAllText(service.GetOrCreatePath())))
                Check(!canonical.RootElement.GetProperty("modes")[0].TryGetProperty("name", out _) && canonical.RootElement.GetProperty("modes")[0].GetProperty("label").GetString() == "RENAMED", "saves write one canonical name instead of conflicting aliases");
            Check(loaded.Theme.Accent == "#123456" && loaded.Behavior.IdleTimeoutSeconds == 42 && !loaded.Behavior.CloseOnLaunch, "workspace saves preserve all settings");
            Check(loaded.Extra!["future"].GetProperty("keep").GetBoolean() && loaded.Theme.Extra!["future"].GetInt32() == 7 && loaded.Modes[0].Extra!["future"].GetInt32() == 3, "unknown settings round-trip");
            Check(File.ReadAllText(Path.Combine(exe, "arcyn.json")) == "{}" && service.GetOrCreatePath() == Path.Combine(user, "arcyn.json"), "read and write resolve the same user configuration");
            Check(File.ReadAllText(service.GetOrCreatePath() + ".bak") == json, "atomic replacement keeps previous-good backup");
            var savedBytes = File.ReadAllText(service.GetOrCreatePath());
            using (var locked = new FileStream(service.GetOrCreatePath(), FileMode.Open, FileAccess.Read, FileShare.Read))
                try { service.Save(config); throw new Exception("Locked destination replaced"); }
                catch (IOException) { Check(File.ReadAllText(service.GetOrCreatePath()) == savedBytes, "failed replacement preserves valid destination"); }
            Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.SaveAsync(config))).GetAwaiter().GetResult();
            Check(service.Load()!.Modes[0].Label == "RENAMED" && Directory.GetFiles(user, "*.tmp").Length == 0, "serialized saves leave valid JSON and no temporary files");
            File.AppendAllText(service.GetOrCreatePath(), "\n ");
            var externallyEdited = File.ReadAllText(service.GetOrCreatePath());
            try { service.Save(config); throw new Exception("External change overwritten"); }
            catch (InvalidDataException) { Check(File.ReadAllText(service.GetOrCreatePath()) == externallyEdited, "external configuration changes are preserved"); }
            service.Load();
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
            Check(LaunchService.TryPrepare(new TargetConfig { Cmd = "explorer.exe", Args = ["/select,", literal] }, out psi, out _) && psi.ArgumentList[0] == "/select,", "explicit Explorer switches remain supported");
            Check(!LaunchService.TryPrepare(new TargetConfig { Cmd = "explorer.exe", Args = ["Z:\\Unavailable"] }, out _, out var error) && error!.Contains("unavailable"), "unavailable target reports repairable failure");
            long now = 100; var idle = new IdleDeadline(() => now); idle.Reset(); now += 10001;
            Check(idle.Expired(10) && !idle.Expired(0), "monotonic idle timeout and disabled timeout");
            idle.Reset(); Check(!idle.Expired(10), "returning to Ready resets idle deadline");
            var workspaces = new List<ModeConfig> { new() { Name = "ONE" }, new() { Name = "TWO" } };
            var manager = new ModeService(new AppState(), workspaces);
            manager.Add(new() { Name = "THREE" }); manager.MoveUp(2); manager.RemoveAt(0);
            Check(workspaces.Count == 2 && workspaces[0].Name == "THREE" && workspaces.Select(m => m.Index).SequenceEqual(new[] { 1, 2 }), "workspace add, reorder and delete preserve the shared configuration list");
            LaunchChecks().GetAwaiter().GetResult();
            OrchestrationChecks(directory).GetAwaiter().GetResult();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            using var bindingOutput = new StringWriter();
            using var bindingTrace = new TextWriterTraceListener(bindingOutput);
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingTrace);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/ARCYN;component/Styles/Theme.xaml", UriKind.Relative) });
            var setup = new SetupWindow(service);
            Check(new System.Windows.Interop.WindowInteropHelper(setup).Handle == IntPtr.Zero,
                "setup XAML initializes without a window or runtime resource errors");
            setup.Close();
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
            var render = (RenderService)typeof(MainWindow).GetField("_render", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            typeof(MainWindow).GetField("_runtimeStarted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            render.Start();
            typeof(MainWindow).GetMethod("RefreshActivity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
            Check(!(bool)typeof(RenderService).GetField("_running", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(render)!, "hidden HUD unsubscribes from rendering callbacks");
            var expanding = new Border { Width = 6, Height = 6 };
            var expansionClock = (System.Windows.Media.Animation.AnimationClock)new System.Windows.Media.Animation.DoubleAnimation(6, 888, TimeSpan.FromMilliseconds(280)).CreateClock(true);
            expanding.ApplyAnimationClock(FrameworkElement.WidthProperty, expansionClock);
            expansionClock.Controller!.SeekAlignedToLastTick(TimeSpan.FromMilliseconds(300), System.Windows.Media.Animation.TimeSeekOrigin.BeginTime);
            AnimationService.ResizeTo(expanding, 888, 466, 280);
            Check(expanding.Width == 888, "second boot expansion starts at the held width");
            expanding.BeginAnimation(FrameworkElement.WidthProperty, null); expanding.BeginAnimation(FrameworkElement.HeightProperty, null);
            window.ApplyTheme(new ThemeConfig());
            Check(((FontFamily)window.FindResource("UIFont")).Source.Contains("Segoe UI Variable"), "default interface uses Windows variable typography");
            Check(((SolidColorBrush)window.FindResource("AccentBrush")).Color == SystemColors.AccentColor, "default interface follows the Windows accent");
            window.ApplyTheme(new ThemeConfig { GlowOpacity = 1 });
            typeof(MainWindow).GetField("_ambientPhase", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, Math.PI / 2);
            ((RenderService.ISubscriber)window).OnRenderTick(30);
            Check(((UIElement)window.FindName("AmbientGlow")).Opacity is >= 0 and <= 1, "maximum glow customization stays within valid render bounds");
            window.ApplyTheme(new ThemeConfig { Accent = "#4580D6", SystemAccent = false, FontScale = 1.2, Layout = "grid", Width = 1000, Spacing = 10, Particles = 0 });
            Check(((SolidColorBrush)window.FindResource("AccentBrush")).Color == Color.FromRgb(0x45, 0x80, 0xD6) && Math.Abs((double)window.FindResource("FontSize15") - 21.6) < .001 && dashboard.ColumnDefinitions.Count == 2, "live theme covers colour, typography, geometry and layout");
            window.ApplyTheme(config.Theme);
            Check(((SolidColorBrush)window.FindResource("AccentBrush")).Color == SystemColors.AccentColor, "preview rollback restores system accent preference");
            var themePath = Path.Combine(directory, "theme.json");
            ThemeService.Export(themePath, new ThemeConfig { Accent = "#4580D6", CursorTrail = false, Particles = 12 });
            Check(ThemeService.Import(themePath).Particles == 12 && !ThemeService.Import(themePath).CursorTrail, "theme import/export round-trips effects");
            window.ApplyTheme(new ThemeConfig { ReducedEffects = true });
            Check(((Border)window.FindName("MainBorder")).Effect == null, "reduced effects remove the full HUD shader");
            try { ThemeService.Export(service.GetOrCreatePath(), new ThemeConfig()); throw new Exception("Configuration overwritten by theme"); }
            catch (InvalidDataException) { Check(service.Load()!.Modes[0].Name == "RENAMED", "theme export cannot overwrite workspace configuration"); }
            try { ThemeService.Validate(new ThemeConfig { FontFamily = "https://host/font.ttf" }); throw new Exception("External font accepted"); }
            catch (ArgumentException) { Check(true, "theme cannot load external font resources"); }
            var quoted = target.Args.Select(LaunchService.QuoteArgument);
            Check(LaunchService.SplitCommandLine("target " + string.Join(" ", quoted)).Skip(1).SequenceEqual(target.Args), "argument editor round-trips quotes, spaces, empties and trailing slashes");
            migrated.RecordLaunch(1, 1, true);
            var modeDraft = new EditModeWindow(migrated);
            Check(modeDraft.Result.LaunchCountLabel == migrated.LaunchCountLabel && modeDraft.Result.LastLaunchLabel == migrated.LastLaunchLabel, "editing preserves session launch history");
            modeDraft.Result.Name = "DRAFT";
            Check(migrated.Name == "LEGACY", "workspace editor drafts do not mutate the loaded configuration");
            Directory.CreateDirectory(Path.Combine(directory, "apps"));
            File.WriteAllText(Path.Combine(directory, "apps", "Fixture.lnk"), "not executed");
            Check(ApplicationDiscovery.Find([Path.Combine(directory, "apps")]).Single().Name == "Fixture", "on-demand discovery uses Windows shortcuts without launching them");
            using var signal = new ManualResetEventSlim();
            var instanceName = "Local\\ARCYN.tests." + Guid.NewGuid().ToString("N");
            using (var first = new SingleInstance(() => signal.Set(), instanceName))
            using (var second = new SingleInstance(() => { }, instanceName))
                Check(first.IsFirst && !second.IsFirst && signal.Wait(TimeSpan.FromSeconds(2)), "duplicate instance signals activation without another HUD");
            var sampleClock = Stopwatch.StartNew();
            using var telemetry = new TelemetryMonitor();
            for (var i = 0; i < 1000; i++) telemetry.Sample();
            sampleClock.Stop();
            Check(telemetry.RamPercent is >= 0 and <= 100 && (float.IsNaN(telemetry.CpuPercent) || telemetry.CpuPercent is >= 0 and <= 100), "native telemetry returns bounded readings");
            Console.WriteLine($"Native telemetry: initialization + 1000 samples {sampleClock.Elapsed.TotalMilliseconds:F2} ms (component microbenchmark, not application CPU).");
            var canvas = new Canvas { Width = 800, Height = 480 };
            using var particles = new ParticleEngine(canvas);
            particles.Start(); canvas.Measure(new Size(800, 480)); canvas.Arrange(new Rect(0, 0, 800, 480));
            var shape = (System.Windows.Shapes.Ellipse)canvas.Children[0];
            var position = (TranslateTransform)shape.RenderTransform; var beforeX = position.X;
            for (var i = 0; i < 70; i++) particles.Tick();
            Check(position.X != beforeX && canvas.IsArrangeValid, "particle motion updates transforms without invalidating canvas layout");
            particles.Configure(0, Colors.Red);
            Check(canvas.Children.Count == 0, "reduced effects release particle visuals");
            window.ApplyTheme(new ThemeConfig());
            var hud = (Grid)window.Content;
            ((FrameworkElement)window.FindName("BootOverlay")).Visibility = Visibility.Collapsed;
            ((UIElement)window.FindName("MainHUD")).Opacity = 1;
            var border = (Border)window.FindName("MainBorder"); border.Width = 888; border.Height = 466;
            window.Content = null; hud.Resources = window.Resources;
            hud.Measure(new Size(920, 490)); hud.Arrange(new Rect(0, 0, 920, 490)); hud.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(920, 490, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(hud);
            var imagePath = Path.Combine(AppContext.BaseDirectory, "offscreen-hud.png");
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var stream = File.Create(imagePath)) encoder.Save(stream);
            Check(new FileInfo(imagePath).Length > 5000, "HUD templates render offscreen without opening a window");
            ReviewChecks(directory, service, config, window);
            WorkspaceEditorChecks(directory);
            VisualSurfaceChecks(service);
            ReleaseCandidateChecks(service);
            FluentChecks(service);
            bindingTrace.Flush();
            Check(bindingOutput.ToString().Length == 0, "offscreen HUD and editor bindings produce no warnings or errors");
            PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingTrace);
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
        using var release = new ManualResetEventSlim(); using var started = new ManualResetEventSlim();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pendingCancel = new CancellationTokenSource();
        orchestrator.Dispatch = _ => { started.Set(); release.Wait(); finished.SetResult(); return null; };
        var pending = orchestrator.LaunchModeAsync(mode, pendingCancel.Token);
        Check(started.Wait(TimeSpan.FromSeconds(2)), "dispatch runs outside the calling thread");
        pendingCancel.Cancel();
        try { result = await pending.WaitAsync(TimeSpan.FromSeconds(2)); Check(result.Canceled, "cancellation restores control during a blocked shell dispatch"); }
        finally { release.Set(); }
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        orchestrator.Dispatch = _ => { calls++; return null; }; calls = 0;
        var recovery = new ModeConfig { Targets = [new() { Cmd = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid() + ".exe") }, new() { Cmd = "ms-clock:" }] };
        result = await orchestrator.LaunchModeAsync(recovery, CancellationToken.None);
        Check(result.Failures.Count == 1 && result.LaunchedTargets == 1 && calls == 1 && !result.Success, "an unavailable target reports failure while remaining targets continue");
        orchestrator.Dispatch = _ => throw new System.ComponentModel.Win32Exception("Fixture shell failure");
        result = await orchestrator.LaunchModeAsync(mode, CancellationToken.None);
        Check(result.Failures.Count == 2 && result.LaunchedTargets == 0 && !result.Success, "shell exceptions become recoverable per-target failures");
        result = await orchestrator.LaunchModeAsync(new ModeConfig(), CancellationToken.None);
        Check(!result.Success && result.Failures.Single().Contains("no targets"), "empty workspaces report an actionable repair message");
    }
}
