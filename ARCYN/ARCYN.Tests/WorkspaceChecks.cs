using System.Diagnostics;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ARCYN.UI;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

internal static partial class Program
{
    private sealed class RecordedProgress : IProgress<LaunchProgress>
    {
        internal readonly List<LaunchProgress> Items = [];
        public void Report(LaunchProgress value) => Items.Add(value);
    }
    private sealed class FakeWindows : IWindowSystem
    {
        internal readonly List<WindowSnapshot> Items = [];
        internal readonly List<string> Operations = [];
        internal IReadOnlyList<MonitorSnapshot> Displays = [
            new("left", "Left · 150%", new(-1920, 0, 1920, 1080), new(-1920, 40, 1920, 1040), false),
            new("main", "Primary · 100%", new(0, 0, 2560, 1440), new(0, 0, 2560, 1400), true)];
        internal bool Responsive = true, Valid = true, RefusePlacement;
        internal string? FocusError;
        internal int Reads;
        public IReadOnlyList<WindowSnapshot> Windows() { Reads++; return Items.ToArray(); }
        public IReadOnlyList<MonitorSnapshot> Monitors() => Displays;
        public string? ResolveExecutable(string command) => command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? command : null;
        public bool IsCurrent(WindowSnapshot w) => Valid && w.ProcessStarted != 0 && Items.Any(x => x.Handle == w.Handle && x.ProcessId == w.ProcessId && x.ProcessStarted == w.ProcessStarted);
        public bool IsResponsive(WindowSnapshot w) => Responsive && IsCurrent(w);
        public string? Place(WindowSnapshot w, PixelRect bounds, WindowShowState state)
        {
            Operations.Add("place");
            if (!RefusePlacement) Items[Items.FindIndex(x => x.Handle == w.Handle)] = w with { NormalBounds = bounds, State = state };
            return null;
        }
        public string? Show(WindowSnapshot w, WindowShowState state) { Operations.Add(state.ToString()); Items[Items.FindIndex(x => x.Handle == w.Handle)] = w with { State = state }; return null; }
        public string? Focus(WindowSnapshot w) { Operations.Add("focus"); return FocusError; }
    }
    private static WindowSnapshot TestWindow(string executable, nint handle = 10, string title = "Project") =>
        new(handle, 100 + (int)handle, 42 + (int)handle, executable, null, Path.GetFileNameWithoutExtension(executable), "FixtureClass", title, new(-1800, 80, 900, 700), WindowShowState.Normal, 144);

    private static async Task OrchestrationChecks(string directory)
    {
        var executable = Path.Combine(directory, "workspace-fixture.exe");
        File.WriteAllText(executable, "fixture, never dispatched");
        using var log = new LogService();
        long now = 0; int calls = 0;
        LaunchOrchestrator Engine(FakeWindows windows)
        {
            var engine = new LaunchOrchestrator(log, windows) { Cadence = _ => Task.CompletedTask, Milliseconds = () => now,
                Delay = (ms, token) => { token.ThrowIfCancellationRequested(); now += ms; return Task.CompletedTask; },
                Dispatch = _ => { calls++; return null; } };
            return engine;
        }
        TargetConfig Target() => new() { Cmd = executable, TimeoutMs = 400 };
        var platform = new FakeWindows(); var original = TestWindow(executable); platform.Items.Add(original);
        var match = new WindowMatch { Executable = executable.ToUpperInvariant(), ClassName = "FixtureClass", Title = "proj", TitleContains = true };
        Check(WindowMatching.Select(match, platform.Items).Window == original, "window matching combines case-insensitive executable identity, class and title");
        var target = Target(); var mode = new ModeConfig { Label = "WORK", Targets = [target] }; var engine = Engine(platform);
        var result = await engine.LaunchModeAsync(mode, default);
        Check(result.Success && calls == 0 && result.Outcomes.Single().Evidence.StartsWith("Reused"), "running responsive application reused without dispatch");
        platform.Items.Add(original with { Handle = 11 });
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 0 && result.Failures.Single().Contains("Multiple"), "ambiguous windows stop target without duplicate launch");
        platform.Items.Clear(); platform.Items.Add(original with { Executable = null });
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 0 && result.Failures.Single().Contains("inaccessible"), "inaccessible possible identity blocks blind launch");
        Check(WindowMatching.Select(new() { AppId = "fixture.app", ClassName = original.ClassName }, platform.Items).Error != null,
            "inaccessible application identity with matching class cannot trigger blind duplication");
        platform.Items.Clear(); target.InstancePolicy = InstancePolicy.ReuseOnly;
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 0, "reuse-only target never dispatches when missing");
        target.InstancePolicy = InstancePolicy.ReuseOrLaunch;
        engine = Engine(platform); result = await engine.LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 1 && result.Outcomes.Single().Evidence == "Timed out", "dispatch without window evidence never reports application readiness");
        result = await engine.LaunchModeAsync(mode, default);
        Check(calls == 1 && result.Failures.Single().Contains("unknown"), "uncertain dispatch is not automatically retried");
        platform.Items.Add(original); platform.Responsive = false; calls = 0;
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 0, "unresponsive existing window times out without dispatching duplicate");
        platform.Responsive = true; target.InstancePolicy = InstancePolicy.LaunchNew; calls = 0;
        engine = Engine(platform); result = await engine.LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 1, "single-instance redirection cannot satisfy distinct-new-window policy");
        engine = Engine(platform); engine.Dispatch = _ => { calls++; platform.Items.Add(original with { Handle = 20, ProcessId = 999, ProcessStarted = 999 }); return null; };
        result = await engine.LaunchModeAsync(mode, default);
        Check(result.Success && result.Outcomes.Single().Evidence == "Window available", "broker handoff accepts distinct matching window in another process");
        platform.Items.Clear(); platform.Items.Add(original); calls = 0;
        var website = new TargetConfig { Cmd = "https://example.com", Kind = TargetKind.Url, Match = new() { Executable = executable }, Readiness = Readiness.Window };
        engine = Engine(platform); engine.Dispatch = _ => { calls++; platform.Items.Add(original with { Handle = 21 }); return null; };
        result = await engine.LaunchModeAsync(new() { Targets = [website] }, default);
        Check(result.Success && calls == 1 && result.Outcomes.Single().Evidence == "Window available",
            "configured website readiness does not suppress URL dispatch merely because a browser window exists");
        platform.Items.Clear(); platform.Items.Add(original); target.InstancePolicy = InstancePolicy.ReuseOrLaunch;
        var capture = WorkspaceLayout.Capture(target.Id, original, platform.Displays);
        Check(capture.Monitor == "left" && capture.Dpi == 144 && Math.Abs(capture.Y - 40d / 1040) < .00001, "capture uses monitor work area, negative coordinates and physical bounds");
        var restored = WorkspaceLayout.Restore(capture, platform.Displays);
        Check(restored.Bounds == original.NormalBounds && !restored.Fallback, "captured layout round-trips without taskbar-offset creep");
        var fallback = WorkspaceLayout.Restore(capture, platform.Displays.Where(m => m.Primary).ToArray());
        Check(fallback.Fallback && fallback.Bounds.X >= 0 && fallback.Bounds.Right <= 2560 && capture.Monitor == "left", "disconnected display falls back visibly without rewriting saved monitor");
        var changedDpiDisplay = new[] { new MonitorSnapshot("left", "Resized", new(-1280, 0, 1280, 720), new(-1280, 30, 1280, 690), true) };
        var scaled = WorkspaceLayout.Restore(capture, changedDpiDisplay);
        Check(scaled.Bounds.Width == 600 && scaled.Bounds.Height == 464, "layout scales by work-area proportions without double-applying DPI");
        var offscreen = new WindowLayout { TargetId = target.Id, Monitor = "main", X = 4, Y = -2, Width = 3, Height = 2 };
        Check(WorkspaceLayout.Restore(offscreen, platform.Displays).Bounds == platform.Displays[1].WorkArea, "oversized and offscreen placements constrain to current work area");
        capture.Focus = true; mode.Layout = [capture]; platform.FocusError = "Focus denied"; engine = Engine(platform);
        result = await engine.LaunchModeAsync(mode, default);
        Check(!result.Success && result.Warnings.Single() == "Focus denied" && platform.Operations.Count(x => x == "focus") == 1, "final focus attempted once and denial retained");
        platform.FocusError = null; platform.Operations.Clear(); platform.RefusePlacement = true; capture.X = .5;
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(!result.Success && result.Failures.Single().Contains("adjusted"), "accepted placement request still requires observed geometry");
        platform.RefusePlacement = false; mode.Layout.Clear();

        var delay = new WaitActionConfig { Label = "Prerequisite", DelayMs = 25 };
        var url = new TargetConfig { Cmd = "https://example.com", DependsOn = [delay.Id] };
        mode = new() { Targets = [url], Actions = [delay] }; engine = Engine(platform); calls = 0;
        result = await engine.LaunchModeAsync(mode, default);
        Check(result.Success && calls == 1 && result.Outcomes[0].Id == delay.Id && result.Outcomes[1].Evidence == "Dispatched", "stable dependency order executes prerequisite before earlier target");
        delay.DependsOn = [url.Id]; calls = 0;
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 0 && result.Failures.Single().Contains("cycle"), "dependency cycles rejected before any dispatch");
        delay.DependsOn = ["missing"];
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 0 && result.Failures.Single().Contains("missing"), "missing dependency rejected before side effects");
        var failed = new TargetConfig { Cmd = Path.Combine(directory, "absent.exe"), Readiness = Readiness.Dispatch };
        var dependent = new TargetConfig { Cmd = "ms-clock:", DependsOn = [failed.Id] };
        mode = new() { Targets = [dependent, failed, new() { Cmd = "ms-clock:" }] }; calls = 0;
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(!result.Success && calls == 1 && result.CompletedSteps == 1 && result.Outcomes.Any(o => o.Evidence == "Skipped"), "failed prerequisite skips dependent while independent target continues");
        platform.Operations.Clear();
        using (var canceled = new CancellationTokenSource())
        {
            mode = new() { Targets = [new() { Cmd = "ms-clock:", DependsOn = [delay.Id] }], Actions = [new WaitActionConfig { Id = delay.Id, DelayMs = 1 }] };
            engine = Engine(platform); calls = 0;
            engine.Delay = (_, _) => { canceled.Cancel(); return Task.FromCanceled(canceled.Token); };
            result = await engine.LaunchModeAsync(mode, canceled.Token);
            Check(result.Canceled && result.Outcomes.Single().Evidence == "Canceled" && calls == 0 && platform.Operations.Count == 0, "cancellation during prerequisite wait stops future dispatch and records canceled step");
        }

        var command = new CommandActionConfig { Cmd = executable, Args = ["original argument", ""], Elevate = true };
        mode = new() { Targets = [new() { Cmd = "ms-clock:" }], Actions = [command] }; engine = Engine(platform); calls = 0;
        result = await engine.LaunchModeAsync(mode, default);
        Check(result.Canceled && calls == 0, "commands default-deny authorization before all workspace side effects");
        bool elevated = false;
        engine = Engine(platform);
        engine.AuthorizeCommands = (review, _) => { elevated = review.Single().Elevate && review[0].Arguments[0] == "original argument"; command.Args[0] = "changed after review"; return Task.FromResult(true); };
        engine.CommandExit = (info, _) => { Check(info.ArgumentList[0] == "original argument" && info.Verb == "runas" && info.UseShellExecute, "authorized command executes immutable reviewed arguments and explicit UAC request"); return Task.FromResult(0); };
        result = await engine.LaunchModeAsync(mode, default);
        Check(result.Success && elevated, "authorized typed command completion requires observed exit status");
        engine.CommandExit = (_, _) => Task.FromResult(7); result = await engine.LaunchModeAsync(mode, default);
        Check(!result.Success && result.Failures.Single().Contains("code 7"), "command nonzero exit remains actionable failure");
        mode = new() { Targets = [new() { Cmd = "cmd.exe", Kind = TargetKind.File, Readiness = Readiness.Dispatch }] }; calls = 0;
        result = await Engine(platform).LaunchModeAsync(mode, default);
        Check(result.Canceled && calls == 0, "interpreter target cannot bypass command review by changing kind");
        var variable = "ARCYN_COMMAND_FIXTURE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, "python.exe");
        try
        {
            mode.Targets[0].Cmd = "%" + variable + "%";
            result = await Engine(platform).LaunchModeAsync(mode, default);
            Check(result.Canceled && calls == 0, "environment-expanded interpreter requires review even when labeled as a file");
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
        mode = new() { Actions = [new CommandActionConfig { Cmd = executable }] }; engine = Engine(platform);
        engine.AuthorizeCommands = (_, _) => Task.FromResult(true);
        result = await engine.LaunchModeAsync(mode, default);
        Check(!result.Success && result.Failures.Single().Contains("no process handle"), "null command process receipt cannot claim successful exit");

        target = Target(); mode = new() { Targets = [target], Layout = [new() { TargetId = target.Id, Monitor = "left" }],
            Actions = [new WindowActionConfig { TargetId = target.Id, DependsOn = [target.Id], Operation = WindowOperation.ApplyLayout }] };
        var duplicate = WorkspaceDefinition.Duplicate(mode);
        Check(duplicate.Id != mode.Id && duplicate.Targets[0].Id != target.Id && duplicate.Layout[0].TargetId == duplicate.Targets[0].Id &&
            ((WindowActionConfig)duplicate.Actions[0]).TargetId == duplicate.Targets[0].Id && duplicate.Actions[0].DependsOn.Single() == duplicate.Targets[0].Id, "workspace duplication regenerates and remaps all target/action/layout identities");
        var path = Path.Combine(directory, "orchestration.json"); var service = new ConfigService(path);
        service.Save(new() { Modes = [mode] }); var loaded = service.Load()!;
        Check(loaded.SchemaVersion == 2 && loaded.Modes[0].Targets[0].Id == target.Id && loaded.Modes[0].Actions[0] is WindowActionConfig, "new schema round-trips typed actions and stable identities");
        Check(System.Text.Json.JsonSerializer.Deserialize<WorkspaceActionConfig>("{\"delay_ms\":25,\"kind\":\"wait\"}", ConfigService.JsonOptions) is WaitActionConfig { DelayMs: 25 },
            "editable action JSON accepts kind after ordinary fields");
        var invalid = WorkspaceDefinition.Clone(mode); invalid.Layout[0].Width = double.NaN;
        try { WorkspaceDefinition.Validate(invalid); throw new Exception("NaN layout accepted"); } catch (InvalidDataException) { Check(true, "nonfinite layout rejected at trust boundary"); }
        invalid = WorkspaceDefinition.Clone(mode); invalid.Targets[0].Match = new() { Title = "Project" };
        try { WorkspaceDefinition.Validate(invalid); throw new Exception("Title-only match accepted"); } catch (InvalidDataException) { Check(true, "title-only selectors rejected at trust boundary"); }
        invalid = WorkspaceDefinition.Clone(mode); invalid.Targets[0].Readiness = Readiness.Dispatch;
        try { WorkspaceDefinition.Validate(invalid); throw new Exception("Dispatch-only layout accepted"); } catch (InvalidDataException) { Check(true, "layout cannot silently upgrade an explicit dispatch-only target"); }
        invalid.Layout.Clear(); invalid.Actions.Clear(); invalid.Targets[0].InstancePolicy = InstancePolicy.ReuseOnly;
        invalid.Targets.Insert(0, new() { Cmd = "ms-clock:" }); calls = 0;
        result = await Engine(platform).LaunchModeAsync(invalid, default);
        Check(!result.Success && calls == 0, "invalid dispatch-only reuse policy fails preflight before independent side effects");

        var prerequisite = new WaitActionConfig { DelayMs = 1 };
        target.DependsOn = [prerequisite.Id];
        var windowAction = new WindowActionConfig { TargetId = target.Id, Operation = WindowOperation.Focus };
        mode = new() { Targets = [target], Actions = [windowAction, prerequisite] };
        platform.Items.Clear(); platform.Items.Add(original); platform.Operations.Clear(); calls = 0;
        var progress = new RecordedProgress();
        result = await Engine(platform).LaunchModeAsync(mode, default, progress);
        Check(result.Success && result.Outcomes.Select(o => o.Id).SequenceEqual(new[] { prerequisite.Id, target.Id, windowAction.Id }),
            "window actions implicitly wait for referenced targets even when their prerequisite appears later");
        Check(progress.Items.Any(p => !p.StepCompleted && p.Evidence == "Reusing · waiting for window") && progress.Items.Count(p => p.StepCompleted) == 3,
            "active progress distinguishes observed readiness stages from completed steps");
        target.DependsOn.Clear();

        platform.Items.Clear(); platform.Items.Add(original with { State = WindowShowState.Maximized }); platform.Operations.Clear();
        mode.Layout.Clear(); mode.Actions.Clear(); engine = Engine(platform); calls = 0;
        result = await engine.ManageAsync(mode, null, default);
        Check(result.Success && calls == 0 && platform.Operations.Count == 0, "explicit attachment discovers ownership without dispatch or window changes");
        result = await engine.ManageAsync(mode, WindowOperation.Minimize, default);
        Check(result.Success && platform.Items[0].State == WindowShowState.Minimized, "managed minimization observes requested state");
        result = await engine.ManageAsync(mode, WindowOperation.Restore, default);
        Check(result.Success && platform.Items[0].State == WindowShowState.Maximized, "selective restore retains prior maximized state");
        var other = new ModeConfig { Targets = [Target()] };
        await engine.ManageAsync(other, null, default); platform.Operations.Clear();
        result = await engine.ManageAsync(mode, WindowOperation.Minimize, default);
        Check(!result.Success && result.Warnings.Count == 1 && platform.Operations.Count == 0, "shared windows excluded from workspace minimization");
        engine.Release(other.Id); await engine.ManageAsync(mode, WindowOperation.Minimize, default);
        engine.Release(mode.Id); platform.Operations.Clear();
        result = await engine.ManageAsync(mode, WindowOperation.Restore, default);
        Check(!result.Success && platform.Operations.Count == 0 && platform.Items[0].State == WindowShowState.Minimized, "release drops ownership without restoring, closing or terminating windows");
        await engine.ManageAsync(mode, null, default); platform.Operations.Clear();
        platform.Items[0] = platform.Items[0] with { ProcessId = 9999, ProcessStarted = 9999 };
        result = await engine.ManageAsync(mode, WindowOperation.Minimize, default);
        Check(!result.Success && platform.Operations.Count == 0, "recycled window identity cannot inherit previous session ownership");
        result = await Engine(platform).ManageAsync(mode, WindowOperation.Restore, default);
        Check(!result.Success && platform.Operations.Count == 0, "fresh ARCYN engine trusts no persisted HWND ownership");
    }

    private static void WorkspaceEditorChecks(string directory)
    {
        var exe = Path.Combine(directory, "workspace-fixture.exe");
        var target = new TargetConfig { Cmd = exe, Match = new() { Executable = exe, ClassName = "FixtureClass" } };
        var mode = new ModeConfig { Label = "WORKSPACE", Targets = [target], Actions = [new WaitActionConfig { Label = "Wait", DelayMs = 30 }] };
        var fake = new FakeWindows(); fake.Items.Add(TestWindow(exe));
        var editor = new EditModeWindow(mode, fake);
        Await(editor.CaptureLayoutAsync());
        Check(mode.Layout.Count == 0 && fake.Operations.Count == 0 && Field<ObservableCollection<EditModeWindow.LayoutRow>>(editor, "_layouts").Count == 1, "editor captures layout into isolated draft without moving real or fake windows");
        var tabs = (TabControl)editor.FindName("EditorTabs");
        var hud = Detach(editor);
        hud.Measure(new Size(980, 740)); hud.Arrange(new Rect(0, 0, 980, 740)); hud.UpdateLayout();
        foreach (var index in new[] { 0, 1, 2 })
        {
            tabs.SelectedIndex = index;
            if (index == 2) ((DataGrid)editor.FindName("ActionsGrid")).SelectedIndex = 0;
            hud.Measure(new Size(980, 740)); hud.Arrange(new Rect(0, 0, 980, 740)); hud.UpdateLayout(); editor.DrawMap(); hud.UpdateLayout();
            if (index == 0) Check(Descendants(hud).OfType<TextBlock>().Any(t => t.Text == "Reuse or launch"),
                "shared choice template displays friendly policy labels instead of record internals");
            if (index == 0) Check(tabs.Items.Cast<TabItem>().All(t => t.ActualHeight >= t.Padding.Top + t.Padding.Bottom + 13),
                "workspace tab headers have room for text and padding without clipping");
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(980, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(hud);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "offscreen-editor-" + index + ".png")); encoder.Save(file);
        }
        Check(fake.Operations.Count == 0 && new System.Windows.Interop.WindowInteropHelper(editor).Handle == 0 && !editor.IsVisible, "Targets, Layout and Actions render offscreen without desktop operations or HWND");
        var row = new EditModeWindow.TargetRow(target) { MatchTitle = "stable", TitleContains = true };
        Check(row.Commit().Match is { Title: "stable", TitleContains: true } && target.Match!.Title == null, "target editor retains matching policies without mutating original");
        editor.Close();
    }

    private static int WorkspaceBenchmark()
    {
        var windows = Enumerable.Range(1, 100).Select(i => TestWindow(@"C:\Fixtures\app" + i + ".exe", i)).ToArray();
        var match = new WindowMatch { Executable = windows[49].Executable };
        var monitors = new FakeWindows().Displays;
        var layout = WorkspaceLayout.Capture("fixture", windows[0], monitors);
        using var process = Process.GetCurrentProcess();
        var results = new List<object>();
        void Measure(string name, int iterations, Action action)
        {
            action(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var bytes = GC.GetAllocatedBytesForCurrentThread(); var cpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++) action();
            clock.Stop(); results.Add(new { name, iterations, wallMs = clock.Elapsed.TotalMilliseconds, cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytes });
        }
        Measure("match one target among 100 synthetic windows", 10000, () => WindowMatching.Select(match, windows));
        Measure("restore layout across two synthetic monitors", 10000, () => WorkspaceLayout.Restore(layout, monitors));
        Console.WriteLine(JsonSerializer.Serialize(new { results, handles = process.HandleCount, limitations = "Synthetic data only; no native enumeration, window operations, applications or HWNDs." }));
        return 0;
    }
}
