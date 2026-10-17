using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ARCYN.UI;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

internal static partial class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance)!.SetValue(target, value);
    private static void Await(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = Task.WhenAny(task, Task.Delay(5000)).ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        if (!task.IsCompleted) throw new TimeoutException("Headless operation did not complete.");
        task.GetAwaiter().GetResult();
    }
    private static void ReviewChecks(string directory, ConfigService service, ArcynConfig config, MainWindow window)
    {
        var missing = Path.Combine(directory, "offline app", "tool.exe");
        var row = new EditModeWindow.TargetRow(new TargetConfig { Cmd = $"\"{missing}\" \"a b\" \"\"", LegacyCommandLine = true });
        var repaired = row.Commit();
        Check(repaired.Cmd == missing && repaired.Args.SequenceEqual(new[] { "a b", "" }), "editing unavailable legacy commands preserves quoting and arguments");
        Directory.CreateDirectory(Path.GetDirectoryName(missing)!); File.WriteAllText(missing, "never executed");
        Check(LaunchService.TryPrepare(repaired, out _, out _), "edited offline command works when its executable returns");
        var variable = "ARCYN_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, Path.GetDirectoryName(missing));
        try
        {
            var variableRow = new EditModeWindow.TargetRow(new TargetConfig { Cmd = $"%{variable}%\\tool.exe", LegacyCommandLine = true });
            Check(variableRow.Commit().Cmd.Contains('%') && LaunchService.TryPrepare(variableRow.Commit(), out _, out _), "editing retains environment variables while launch preparation expands them");
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
        Check(LaunchService.TryPrepare(new TargetConfig { Cmd = "\"" + Path.GetDirectoryName(missing) + "\"" }, out var psi, out _) && psi.FileName == "explorer.exe", "quoted literal folders are supported");
        Check(!LaunchService.TryPrepare(new TargetConfig { Cmd = "\"\"" }, out _, out _), "quoted empty commands are rejected before dispatch");
        Check(new TargetConfig { Cmd = missing }.DisplayLabel == "tool" && new TargetConfig { Cmd = "explorer.exe", Args = [Path.GetDirectoryName(missing)!] }.DisplayLabel == "offline app", "target chips show executable and folder names");
        var random = new Random(42); const string alphabet = "ab \\\"\tΩ";
        for (var i = 0; i < 200; i++)
        {
            var args = Enumerable.Range(0, 6).Select(_ => new string(Enumerable.Range(0, random.Next(20)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray())).ToArray();
            if (!LaunchService.SplitCommandLine("target " + string.Join(" ", args.Select(LaunchService.QuoteArgument))).Skip(1).SequenceEqual(args)) throw new Exception("Random argument round trip failed.");
        }
        Check(true, "200 deterministic Unicode and Windows quoting round trips");

        foreach (var json in new[] { "{\"schema_version\":0}", "{\"schema_version\":2}", "{\"modes\":[null]}", "{\"modes\":[{\"targets\":[{\"args\":[null]}]}]}", "{\"theme\":{\"width\":0}}" })
        {
            var path = Path.Combine(directory, "invalid.json"); File.WriteAllText(path, json);
            try { new ConfigService(path).Load(); throw new Exception("Invalid configuration accepted."); }
            catch (InvalidDataException) { if (File.ReadAllText(path) != json) throw new Exception("Invalid config was modified."); }
        }
        Check(true, "invalid schemas, null entries and invalid themes remain untouched");
        foreach (var aliases in new[] { "{\"name\":\"A\",\"label\":\"B\"}", "{\"description\":\"A\",\"subtitle\":\"B\"}" })
        {
            var path = Path.Combine(directory, "conflicting.json"); var json = "{\"Modes\":[" + aliases + "]}"; File.WriteAllText(path, json);
            try { new ConfigService(path).Load(); throw new Exception("Conflicting aliases silently accepted."); }
            catch (InvalidDataException) { if (File.ReadAllText(path) != json) throw new Exception("Conflicting config was modified."); }
        }
        Check(true, "conflicting legacy field aliases require repair without losing either value");
        var legacyDirectory = Path.Combine(directory, "import"); Directory.CreateDirectory(legacyDirectory);
        var legacyPath = Path.Combine(legacyDirectory, "protocoll.json"); const string legacyJson = "[{\"label\":\"RETAIN\",\"targets\":[]}]";
        File.WriteAllText(legacyPath, legacyJson, new System.Text.UTF8Encoding(true));
        var originalBytes = File.ReadAllBytes(legacyPath);
        var imported = new ConfigService(null, Path.Combine(legacyDirectory, "user"), legacyDirectory);
        imported.Save(imported.Load()!);
        Check(File.ReadAllBytes(legacyPath).SequenceEqual(originalBytes) && imported.GetOrCreatePath().EndsWith("user" + Path.DirectorySeparatorChar + "arcyn.json"), "BOM legacy import retains original bytes and establishes user storage");
        var envelope = Path.Combine(directory, "portable-empty.json"); const string envelopeJson = "{\"theme\":{\"accent\":\"#123456\"},\"behavior\":{}}";
        File.WriteAllText(envelope, envelopeJson);
        try { ThemeService.Export(envelope, new()); throw new Exception("Empty configuration overwritten by theme."); }
        catch (InvalidDataException) { Check(File.ReadAllText(envelope) == envelopeJson, "theme export protects configurations without modes"); }
        try { ThemeService.Import(envelope); throw new Exception("Configuration imported as theme."); }
        catch (InvalidDataException) { Check(true, "theme import rejects configuration envelopes without modes"); }

        var modes = Field<List<ModeConfig>>(window, "_modes"); var before = modes.ToList();
        SetField(window, "_editing", true);
        foreach (var method in new[] { "EditWorkspace", "DuplicateWorkspace", "DeleteWorkspace" }) Call(window, method, modes[0]);
        Call(window, "MoveWorkspace", modes[0], 1); Call(window, "EditTheme");
        Check(modes.SequenceEqual(before), "pending edits block every reentrant editor action without dialogs");
        SetField(window, "_editing", false);
        var diskPath = service.GetOrCreatePath(); service.Load(); File.AppendAllText(diskPath, "\n "); var diskBytes = File.ReadAllBytes(diskPath);
        var errors = new List<string>(); window.WorkspaceError = errors.Add;
        modes.Add(new() { Label = "UNSAVED" }); Await((Task)Call(window, "SaveWorkspaces", before)!);
        Check(errors.Count == 1 && modes.SequenceEqual(before) && File.ReadAllBytes(diskPath).SequenceEqual(diskBytes), "failed workspace transaction restores cards and retains external disk edits");
        var oldTheme = config.Theme; var oldBehavior = config.Behavior;
        try { Await(window.SaveThemeAsync(new() { Accent = "#456789" }, new() { IdleTimeoutSeconds = 99 })); throw new Exception("External edits overwritten by theme save."); }
        catch (InvalidDataException) { Check(ReferenceEquals(config.Theme, oldTheme) && ReferenceEquals(config.Behavior, oldBehavior), "failed theme Apply rolls back both configuration references"); }
        service.Load();
        modes.Add(new() { Label = "ADDED", Apps = ["tool.exe"], Websites = ["https://example.com"], Folders = [directory] });
        Await((Task)Call(window, "SaveWorkspaces", before)!);
        Check(service.Load()!.Modes.Count == modes.Count && Field<List<ContentControl>>(window, "_cardWrappers").All(c => c.Opacity == 1), "successful workspace transaction persists and rebuilds visible cards");
        var appliedTheme = ThemeService.Clone(config.Theme); appliedTheme.Accent = "#46789A";
        var appliedBehavior = JsonSerializer.Deserialize<BehaviorConfig>(JsonSerializer.Serialize(config.Behavior, ConfigService.JsonOptions), ConfigService.JsonOptions)!;
        appliedBehavior.IdleTimeoutSeconds = 27; appliedBehavior.CloseOnLaunch = false;
        Await(window.SaveThemeAsync(appliedTheme, appliedBehavior));
        var applied = service.Load()!;
        Check(applied.Theme.Accent == "#46789A" && applied.Behavior.IdleTimeoutSeconds == 27 && !applied.Behavior.CloseOnLaunch && applied.Modes.Count == modes.Count && applied.Theme.Extra!["future"].GetInt32() == 7, "successful theme Apply persists behavior and retains workspaces and unknown fields");
        var template = (DataTemplate)window.FindResource("ModeCardTemplate");
        var presenter = new ContentControl { Content = modes[^1], ContentTemplate = template };
        presenter.Measure(new Size(800, 500)); presenter.Arrange(new Rect(0, 0, 800, 500)); presenter.UpdateLayout();
        var card = Descendants(presenter).OfType<Button>().Single();
        var chips = Descendants(card).OfType<ItemsControl>().Single();
        chips.GetBindingExpression(ItemsControl.ItemsSourceProperty)!.UpdateTarget();
        card.GetBindingExpression(System.Windows.Automation.AutomationProperties.NameProperty)!.UpdateTarget();
        Check(chips.Items.Count == 3 && System.Windows.Automation.AutomationProperties.GetName(card) == "ADDED", $"all legacy target types populate accessible HUD cards ({chips.Items.Count} targets; {System.Windows.Automation.AutomationProperties.GetName(card)})");
        Check(new ModeConfig { Index = 10 }.ShortcutHint == "", "cards do not advertise unsupported numeric shortcuts");
        var trail = (Canvas)window.FindName("TrailCanvas"); trail.Children.Add(new System.Windows.Shapes.Ellipse());
        window.ApplyTheme(new() { Animations = false });
        Check(trail.Children.Count == 0, "disabling ambient animations removes frozen cursor trails");
        window.ApplyTheme(new() { Accent = "#4580D6", Layout = "compact" });
        Check((double)window.FindResource("CardHeight") == 72 && ((RadialGradientBrush)((System.Windows.Shapes.Rectangle)window.FindName("AmbientGlow")).Fill).GradientStops[0].Color.B > 0x80, "compact geometry and gradient accents update together");
        var style = window.FindResource("WorkstationPanelStyle");
        window.ApplyTheme(new() { Accent = "#4580D6", Layout = "compact" });
        Check(ReferenceEquals(style, window.FindResource("WorkstationPanelStyle")), "unchanged previews reuse existing theme resources");
        ThemeEditorChecks(config.Theme, config.Behavior);

        using var disposedRender = new RenderService(); disposedRender.Start(); disposedRender.Dispose(); disposedRender.Start();
        Check(!Field<bool>(disposedRender, "_running"), "disposed render services cannot subscribe again");
        var logDirectory = LogService.DirectoryPath; var blockedLog = Path.Combine(directory, "blocked-log"); File.WriteAllText(blockedLog, "fixture"); LogService.DirectoryPath = blockedLog;
        LogService.WriteStatic("A logging failure remains nonfatal."); LogService.DirectoryPath = logDirectory;
        Check(File.ReadAllText(blockedLog) == "fixture", "logging failure neither throws nor overwrites another file");
        Check(Application.Current.Windows.Cast<Window>().All(w => !w.IsVisible && new WindowInteropHelper(w).Handle == IntPtr.Zero), "headless checks never create a launcher or editor HWND");
        window.Dispose();
        Check(!Field<bool>(Field<RenderService>(window, "_render"), "_running") && Field<AppState>(window, "_state").Phase == AppPhase.Closing, "HUD disposal closes its state and leaves render subscriptions stopped");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void ThemeEditorChecks(ThemeConfig theme, BehaviorConfig behavior)
    {
        var previews = new List<string>(); var saves = 0;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var editor = new ThemeWindow(theme, behavior, (t, _) => previews.Add(t.Accent), async (_, _) => { saves++; await completion.Task; });
        var inputs = Field<Dictionary<PropertyInfo, Control>>(editor, "_inputs");
        var accent = (TextBox)inputs.Single(p => p.Key.Name == "Accent").Value;
        accent.Text = "#456789"; Check((bool)Call(editor, "Preview")!, "valid editor draft previews without persistence");
        Check(theme.Accent != "#456789" && previews[^1] == "#456789" && saves == 0, "live previews leave the saved theme untouched");
        accent.Text = "invalid"; var previewCount = previews.Count;
        Check(!(bool)Call(editor, "Preview")! && previews.Count == previewCount, "invalid theme input retains the last valid preview");
        accent.Text = "#456789";
        var apply = editor.ApplyAsync(); var duplicateApply = editor.ApplyAsync(); var closed = false;
        editor.Closed += (_, _) => closed = true; editor.Close();
        Check(!editor.IsEnabled && !closed && duplicateApply.Result == false && saves == 1 && !Field<DispatcherTimer>(editor, "_coalesce").IsEnabled, "Apply serializes persistence, stops previews and prevents closing mid-save");
        completion.SetResult(); Await(apply);
        Check(apply.Result && editor.IsEnabled, "successful Apply completes and restores editor state"); editor.Close();
        Check(closed && !Field<DispatcherTimer>(editor, "_coalesce").IsEnabled, "closing theme controls releases the preview timer");
        var failed = new ThemeWindow(theme, behavior, (_, _) => { }, (_, _) => Task.FromException(new IOException("Fixture save failure")));
        var failedApply = failed.ApplyAsync(); Await(failedApply);
        Check(!failedApply.Result && failed.IsEnabled && Field<TextBlock>(failed, "_status").Text.Contains("Fixture save failure"), "failed Apply stays editable and reports the save error");
        failed.Close();
        var canceled = new ThemeWindow(theme, behavior, (_, _) => { }, (_, _) => { saves++; return Task.CompletedTask; });
        ((TextBox)Field<Dictionary<PropertyInfo, Control>>(canceled, "_inputs").Single(p => p.Key.Name == "Accent").Value).Text = "#987654";
        canceled.Close();
        Check(saves == 1 && theme.Accent != "#987654" && !Field<DispatcherTimer>(canceled, "_coalesce").IsEnabled, "Cancel discards pending draft updates without saving");
    }
}
