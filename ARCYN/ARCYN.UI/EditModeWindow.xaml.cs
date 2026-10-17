using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

namespace ARCYN.UI;

public partial class EditModeWindow : Window
{
    public ModeConfig Result { get; }
    private readonly IWindowSystem _windows;
    private readonly ThemeConfig _theme;
    private readonly ObservableCollection<TargetRow> _rows;
    private readonly ObservableCollection<LayoutRow> _layouts;
    private readonly ObservableCollection<ActionRow> _actions;
    private IReadOnlyList<MonitorSnapshot> _monitors = [];
    private bool _loading, _dragging, _closed;
    private sealed record StepChoice(string Id, string Label);
    private sealed record Option<T>(T Value, string Label);
    private static void Options<T>(ComboBox input, params (T Value, string Label)[] values)
    {
        input.SelectedValuePath = "Value"; input.DisplayMemberPath = "Label";
        input.ItemsSource = values.Select(v => new Option<T>(v.Value, v.Label)).ToArray();
    }
    public EditModeWindow(ModeConfig mode, IWindowSystem? windows = null, ThemeConfig? theme = null)
    {
        _windows = windows ?? new WindowsWindowSystem();
        _theme = ThemeService.Clone(theme ?? new());
        Result = WorkspaceDefinition.Clone(mode); Result.RetainLaunchHistory(mode);
        _rows = new(mode.LaunchTargets.Select(t => new TargetRow(t)));
        _layouts = new(Result.Layout.Select(l => MakeLayoutRow(l)));
        _actions = new(Result.Actions.Select(a => new ActionRow(a)));
        InitializeComponent();
        NameInput.Text = mode.Name; DescInput.Text = mode.Description; TypeInput.Text = mode.Type;
        SessionInput.Text = mode.Session; StateInput.Text = mode.State; AccentInput.Text = mode.Accent;
        Options(KindInput, (TargetKind.Auto, "Detect automatically"), (TargetKind.Application, "Application"), (TargetKind.Url, "Website / protocol"), (TargetKind.File, "File"), (TargetKind.Folder, "Folder"));
        Options(PolicyInput, (InstancePolicy.ReuseOrLaunch, "Reuse or launch"), (InstancePolicy.ReuseOnly, "Reuse only"), (InstancePolicy.LaunchNew, "Launch new"));
        Options(ReadinessInput, (Readiness.Automatic, "Automatic"), (Readiness.Window, "Window available"), (Readiness.Dispatch, "Dispatch only"));
        Options(OperationInput, (WindowOperation.ApplyLayout, "Apply saved layout"), (WindowOperation.Minimize, "Minimize"), (WindowOperation.Restore, "Restore ARCYN-minimized"), (WindowOperation.Focus, "Focus at completion"));
        LayoutStateColumn.ItemsSource = Enum.GetValues<WindowShowState>();
        TargetsGrid.ItemsSource = _rows; LayoutGrid.ItemsSource = _layouts; ActionsGrid.ItemsSource = _actions;
        LayoutGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(DrawMap);
        Closed += (_, _) => _closed = true;
        if (_rows.Count > 0) TargetsGrid.SelectedIndex = 0;
        WindowSurface.Prepare(this, _theme);
    }

    public sealed class TargetRow
    {
        public TargetConfig Target { get; private set; } = new();
        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string Label => string.IsNullOrWhiteSpace(Target.Cmd) ? Command : Target.DisplayLabel;
        public string? WorkingDirectory { get; set; }
        public string? MatchExecutable { get; set; }
        public string? MatchAppId { get; set; }
        public string? MatchClass { get; set; }
        public string? MatchTitle { get; set; }
        public bool TitleContains { get; set; }
        public TargetRow() { }
        public TargetRow(TargetConfig target)
        {
            Target = System.Text.Json.JsonSerializer.Deserialize<TargetConfig>(System.Text.Json.JsonSerializer.Serialize(target, ConfigService.JsonOptions), ConfigService.JsonOptions)!;
            if (target.LegacyCommandLine)
                try { (Target.Cmd, Target.Args) = LaunchService.ParseTarget(target); }
                catch (ArgumentException) { /* Keep ambiguous text available for manual repair. */ }
            Command = Target.Cmd; Arguments = string.Join(" ", Target.Args.Select(LaunchService.QuoteArgument));
            WorkingDirectory = Target.WorkingDirectory;
            MatchExecutable = Target.Match?.Executable; MatchAppId = Target.Match?.AppId;
            MatchClass = Target.Match?.ClassName; MatchTitle = Target.Match?.Title; TitleContains = Target.Match?.TitleContains ?? false;
        }
        public TargetConfig Commit()
        {
            Target.Cmd = Command.Trim();
            Target.Args = string.IsNullOrWhiteSpace(Arguments) ? [] : LaunchService.SplitCommandLine("target " + Arguments).Skip(1).ToList();
            Target.WorkingDirectory = WorkingDirectory;
            var match = Target.Match ?? new();
            match.Executable = Empty(MatchExecutable); match.AppId = Empty(MatchAppId); match.ClassName = Empty(MatchClass); match.Title = Empty(MatchTitle); match.TitleContains = TitleContains;
            Target.Match = match.Executable == null && match.AppId == null && match.ClassName == null && match.Title == null ? null : match;
            return Target;
        }
        private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public sealed class ActionRow(WorkspaceActionConfig action)
    {
        public WorkspaceActionConfig Action { get; } = action;
        public string Kind => Action is CommandActionConfig ? "command" : Action is WindowActionConfig ? "window" : "wait";
        public string? TargetId { get => Action is WindowActionConfig w ? w.TargetId : (Action as WaitActionConfig)?.TargetId;
            set { if (Action is WindowActionConfig w) w.TargetId = value ?? ""; if (Action is WaitActionConfig wait) wait.TargetId = value; } }
        public WindowOperation Operation { get => (Action as WindowActionConfig)?.Operation ?? WindowOperation.ApplyLayout; set { if (Action is WindowActionConfig w) w.Operation = value; } }
        public int DelayMs { get => (Action as WaitActionConfig)?.DelayMs ?? 0; set { if (Action is WaitActionConfig w) w.DelayMs = value; } }
        public string Command { get => (Action as CommandActionConfig)?.Cmd ?? ""; set { if (Action is CommandActionConfig c) c.Cmd = value; } }
        public string Arguments { get; set; } = ActionArguments(action);
        private static string ActionArguments(WorkspaceActionConfig a) => a is CommandActionConfig c ? string.Join(" ", c.Args.Select(LaunchService.QuoteArgument)) : "";
        public string? WorkingDirectory { get => (Action as CommandActionConfig)?.WorkingDirectory; set { if (Action is CommandActionConfig c) c.WorkingDirectory = value; } }
        public bool Elevate { get => (Action as CommandActionConfig)?.Elevate ?? false; set { if (Action is CommandActionConfig c) c.Elevate = value; } }
        public WorkspaceActionConfig Commit()
        {
            if (Action is CommandActionConfig c) c.Args = string.IsNullOrWhiteSpace(Arguments) ? [] : LaunchService.SplitCommandLine("target " + Arguments).Skip(1).ToList();
            return Action;
        }
    }

    public sealed class LayoutRow(WindowLayout layout, string label, Action changed) : INotifyPropertyChanged
    {
        public WindowLayout Layout { get; } = layout;
        public string Label { get; } = label;
        public string Monitor { get => Layout.Monitor; set { Layout.Monitor = value; Notify(); } }
        public bool Focus { get => Layout.Focus; set { Layout.Focus = value; Notify(); } }
        public double X { get => Layout.X * 100; set { Layout.X = value / 100; Notify(); } }
        public double Y { get => Layout.Y * 100; set { Layout.Y = value / 100; Notify(); } }
        public double Width { get => Layout.Width * 100; set { Layout.Width = value / 100; Notify(); } }
        public double Height { get => Layout.Height * 100; set { Layout.Height = value / 100; Notify(); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify() { PropertyChanged?.Invoke(this, new(null)); changed(); }
    }
    private LayoutRow MakeLayoutRow(WindowLayout layout) => new(layout, _rows.FirstOrDefault(r => r.Target.Id == layout.TargetId)?.Label ?? "Unavailable target", DrawMap);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitEditing();
            if (string.IsNullOrWhiteSpace(NameInput.Text)) throw new ArgumentException("Enter a workspace name.");
            ThemeService.ValidateColor(AccentInput.Text);
            Result.Name = NameInput.Text.Trim(); Result.Description = DescInput.Text;
            Result.Type = TypeInput.Text.Trim(); Result.Session = SessionInput.Text; Result.State = StateInput.Text; Result.Accent = AccentInput.Text;
            Result.Targets = _rows.Select(r => r.Commit()).ToList(); Result.Apps.Clear(); Result.Folders.Clear(); Result.Websites.Clear();
            Result.Layout = _layouts.Select(r => r.Layout).ToList(); Result.Actions = _actions.Select(r => r.Commit()).ToList();
            WorkspaceDefinition.Validate(Result);
            DialogResult = true;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void CommitEditing()
    {
        foreach (var grid in new[] { TargetsGrid, LayoutGrid, ActionsGrid })
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("Finish editing the row before continuing.");
        foreach (var input in Descendants(this).OfType<TextBox>()) input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (Descendants(this).Any(Validation.GetHasError)) throw new ArgumentException("Repair invalid field values before continuing.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private void Target_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != TargetsGrid || TargetInspector == null) return;
        TargetInspector.DataContext = TargetsGrid.SelectedItem as TargetRow; RefreshChoices();
    }
    private void Action_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != ActionsGrid || ActionInspector == null) return;
        ActionInspector.DataContext = ActionsGrid.SelectedItem as ActionRow;
        ActionTargetInput.ItemsSource = _rows.ToList();
        ActionTargetPanel.Visibility = ActionsGrid.SelectedItem is ActionRow { Kind: "command" } ? Visibility.Collapsed : Visibility.Visible;
        RefreshChoices();
    }
    private void RefreshChoices()
    {
        if (TargetDependencies == null || ActionDependencies == null) return;
        _loading = true;
        try
        {
            var choices = _rows.Select(r => new StepChoice(r.Target.Id, r.Label)).Concat(_actions.Select(r => new StepChoice(r.Action.Id, string.IsNullOrWhiteSpace(r.Action.Label) ? r.Kind : r.Action.Label))).ToList();
            void Populate(ListBox list, string? self, List<string>? dependencies)
            {
                list.ItemsSource = choices.Where(c => c.Id != self).ToList();
                foreach (var choice in list.Items.Cast<StepChoice>()) if (dependencies?.Contains(choice.Id) == true) list.SelectedItems.Add(choice);
            }
            var target = TargetsGrid.SelectedItem as TargetRow; Populate(TargetDependencies, target?.Target.Id, target?.Target.DependsOn);
            var action = ActionsGrid.SelectedItem as ActionRow; Populate(ActionDependencies, action?.Action.Id, action?.Action.DependsOn);
        }
        finally { _loading = false; }
    }
    private void Dependencies_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (sender == TargetDependencies && TargetsGrid.SelectedItem is TargetRow t) t.Target.DependsOn = TargetDependencies.SelectedItems.Cast<StepChoice>().Select(x => x.Id).ToList();
        if (sender == ActionDependencies && ActionsGrid.SelectedItem is ActionRow a) a.Action.DependsOn = ActionDependencies.SelectedItems.Cast<StepChoice>().Select(x => x.Id).ToList();
    }
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != EditorTabs || EditorTabs.SelectedIndex != 1 || LayoutMap == null) return;
        try { CommitEditing(); RefreshDisplays(); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    internal void RefreshDisplays() { _monitors = _windows.Monitors(); LayoutMonitorColumn.ItemsSource = DisplayMonitors(); DrawMap(); }
    private void Displays_Click(object sender, RoutedEventArgs e) { try { RefreshDisplays(); } catch (Exception ex) { Status.Text = ex.Message; } }

    internal async Task CaptureLayoutAsync()
    {
        CommitEditing(); foreach (var row in _rows) row.Commit();
        var snapshot = await Task.Run(() => (Windows: _windows.Windows(), Monitors: _windows.Monitors()));
        if (_closed) return;
        _monitors = snapshot.Monitors;
        var errors = new List<string>(); var assigned = new HashSet<nint>(); var count = 0;
        foreach (var row in _rows)
        {
            var identity = WindowMatching.Identity(row.Target, _windows);
            if (identity == null) continue;
            var selected = WindowMatching.Select(identity, snapshot.Windows, assigned);
            if (selected.Error != null) { errors.Add(row.Command + ": " + selected.Error); continue; }
            if (selected.Window is not { } window) { errors.Add(row.Command + ": no matching window."); continue; }
            assigned.Add(window.Handle);
            var layout = WorkspaceLayout.Capture(row.Target.Id, window, _monitors);
            if (_layouts.FirstOrDefault(l => l.Layout.TargetId == row.Target.Id) is { } old) { layout.Focus = old.Layout.Focus; _layouts.Remove(old); }
            _layouts.Add(MakeLayoutRow(layout)); count++;
        }
        LayoutMonitorColumn.ItemsSource = DisplayMonitors(); DrawMap();
        Status.Text = count + " placements captured into draft. " + string.Join(" ", errors);
    }
    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return; button.IsEnabled = false;
        try { await CaptureLayoutAsync(); } catch (Exception ex) { Status.Text = ex.Message; }
        finally { button.IsEnabled = true; }
    }
    private async void Bind_Click(object sender, RoutedEventArgs e)
    {
        if (TargetsGrid.SelectedItem is not TargetRow row) { Status.Text = "Select a target first."; return; }
        try
        {
            var windows = await Task.Run(_windows.Windows); if (_closed) return;
            var choices = windows.Where(w => w.Executable != null || w.AppId != null).Select(w => new { Window = w, Label = w.Title + "\n" + (w.AppId ?? w.Executable) }).ToList();
            var list = new ListBox { ItemsSource = choices, DisplayMemberPath = "Label", Margin = new(12) };
            var picker = new Window { Title = "ARCYN — Choose matching window", Owner = this, Width = 650, Height = 480, Content = list, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            WindowSurface.Prepare(picker, _theme);
            void Select()
            {
                if (list.SelectedIndex < 0) return; var window = choices[list.SelectedIndex].Window;
                row.MatchExecutable = window.Executable; row.MatchAppId = window.AppId; row.MatchClass = window.ClassName; row.MatchTitle = window.Title; row.TitleContains = false;
                row.Target.Readiness = Readiness.Window;
                TargetInspector.DataContext = null; TargetInspector.DataContext = row; picker.Close();
                Status.Text = "Exact window identity selected. Use title-contains only when its stable portion identifies the intended document.";
            }
            list.MouseDoubleClick += (_, _) => Select();
            list.KeyDown += (_, key) => { if (key.Key == System.Windows.Input.Key.Enter) Select(); };
            picker.ShowDialog();
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private void AddLayout_Click(object sender, RoutedEventArgs e)
    {
        var row = TargetsGrid.SelectedItem as TargetRow ?? _rows.FirstOrDefault();
        if (row == null) { Status.Text = "Add a target first."; return; }
        if (_layouts.Any(l => l.Layout.TargetId == row.Target.Id)) { Status.Text = "Target already has a placement."; return; }
        var monitor = DisplayMonitors().First();
        _layouts.Add(MakeLayoutRow(new() { TargetId = row.Target.Id, Monitor = monitor.Id }));
        LayoutMonitorColumn.ItemsSource = DisplayMonitors(); DrawMap();
    }
    private void RemoveLayout_Click(object sender, RoutedEventArgs e) { if (LayoutGrid.SelectedItem is LayoutRow row) _layouts.Remove(row); DrawMap(); }
    private IReadOnlyList<MonitorSnapshot> DisplayMonitors()
    {
        var displays = _monitors.ToList();
        foreach (var id in _layouts.Select(l => l.Layout.Monitor).Distinct())
            if (!displays.Any(m => m.Id == id))
            {
                var x = displays.Count == 0 ? 0 : displays.Max(m => m.Bounds.Right) + 80;
                displays.Add(new(id, "Saved / disconnected", new(x, 0, 1920, 1080), new(x, 0, 1920, 1080), displays.Count == 0));
            }
        if (displays.Count == 0) displays.Add(new("", "Primary display fallback", new(0, 0, 1920, 1080), new(0, 0, 1920, 1080), true));
        return displays;
    }
    private void Map_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMap();
    internal void DrawMap()
    {
        if (LayoutMap == null || _dragging) return;
        LayoutMap.Children.Clear();
        var displays = DisplayMonitors(); var left = displays.Min(m => m.Bounds.X); var top = displays.Min(m => m.Bounds.Y);
        var totalW = displays.Max(m => m.Bounds.Right) - left; var totalH = displays.Max(m => m.Bounds.Bottom) - top;
        var scale = Math.Max(.001, Math.Min(Math.Max(1, LayoutMap.ActualWidth - 30) / totalW, Math.Max(1, LayoutMap.ActualHeight - 30) / totalH));
        foreach (var monitor in displays)
        {
            var mx = (LayoutMap.ActualWidth - totalW * scale) / 2 + (monitor.Bounds.X - left) * scale;
            var my = (LayoutMap.ActualHeight - totalH * scale) / 2 + (monitor.Bounds.Y - top) * scale;
            var frame = new Border { Width = monitor.Bounds.Width * scale, Height = monitor.Bounds.Height * scale, BorderThickness = new(1), CornerRadius = new(12),
                Child = new TextBlock { Text = monitor.Name, FontSize = 10, Foreground = Brushes.Gray, Margin = new(4) } };
            frame.SetResourceReference(Border.BorderBrushProperty, "SurfaceLineBrush");
            Canvas.SetLeft(frame, mx); Canvas.SetTop(frame, my); LayoutMap.Children.Add(frame);
            foreach (var row in _layouts.Where(l => l.Layout.Monitor == monitor.Id))
            {
                var layout = row.Layout; var work = monitor.WorkArea;
                if (!double.IsFinite(layout.X + layout.Y + layout.Width + layout.Height)) continue;
                var box = new Border { Width = Math.Max(18, work.Width * layout.Width * scale), Height = Math.Max(18, work.Height * layout.Height * scale), CornerRadius = new(9),
                    BorderThickness = new(1), ToolTip = row.Label };
                box.SetResourceReference(Border.BackgroundProperty, "AccentSurfaceBrush"); box.SetResourceReference(Border.BorderBrushProperty, "AccentBrightBrush");
                var content = new Grid(); box.Child = content;
                content.Children.Add(new TextBlock { Text = row.Label, Margin = new(4), FontSize = 10, Foreground = Brushes.White, IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis });
                var template = new ControlTemplate(typeof(Thumb)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
                var move = new Thumb { Template = template, Cursor = System.Windows.Input.Cursors.SizeAll, Focusable = false };
                var resize = new Thumb { Template = template, Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = System.Windows.Input.Cursors.SizeNWSE, Focusable = false };
                content.Children.Add(new Border { Width = 8, Height = 8, Background = Brushes.IndianRed, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
                content.Children.Add(move); content.Children.Add(resize);
                void Position() { Canvas.SetLeft(box, mx + (work.X - monitor.Bounds.X + layout.X * work.Width) * scale); Canvas.SetTop(box, my + (work.Y - monitor.Bounds.Y + layout.Y * work.Height) * scale); }
                void Start(object s, DragStartedEventArgs e) { _dragging = true; LayoutGrid.SelectedItem = row; }
                void End(object s, DragCompletedEventArgs e) { _dragging = false; DrawMap(); }
                move.DragStarted += Start; move.DragCompleted += End; resize.DragStarted += Start; resize.DragCompleted += End;
                move.DragDelta += (_, e) => { row.X = Math.Clamp(layout.X + e.HorizontalChange / (work.Width * scale), 0, Math.Max(0, 1 - layout.Width)) * 100; row.Y = Math.Clamp(layout.Y + e.VerticalChange / (work.Height * scale), 0, Math.Max(0, 1 - layout.Height)) * 100; Position(); };
                resize.DragDelta += (_, e) => { row.Width = Math.Clamp(layout.Width + e.HorizontalChange / (work.Width * scale), .05, Math.Max(.05, 1 - layout.X)) * 100; row.Height = Math.Clamp(layout.Height + e.VerticalChange / (work.Height * scale), .05, Math.Max(.05, 1 - layout.Y)) * 100; box.Width = work.Width * layout.Width * scale; box.Height = work.Height * layout.Height * scale; };
                Position(); LayoutMap.Children.Add(box);
            }
        }
    }

    private void AddAction(WorkspaceActionConfig action) { var row = new ActionRow(action); _actions.Add(row); ActionsGrid.SelectedItem = row; RefreshChoices(); }
    private void AddWindow_Click(object sender, RoutedEventArgs e) => AddAction(new WindowActionConfig { Label = "Window operation", TargetId = (TargetsGrid.SelectedItem as TargetRow ?? _rows.FirstOrDefault())?.Target.Id ?? "" });
    private void AddWait_Click(object sender, RoutedEventArgs e) => AddAction(new WaitActionConfig { Label = "Wait" });
    private void AddCommand_Click(object sender, RoutedEventArgs e) => AddAction(new CommandActionConfig { Label = "Command" });
    private void FixedWait_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is ActionRow { Action: WaitActionConfig wait }) { wait.TargetId = null; ActionInspector.DataContext = null; ActionInspector.DataContext = ActionsGrid.SelectedItem; }
    }
    private bool Referenced(string id) => _rows.Any(r => r.Target.DependsOn.Contains(id)) || _actions.Any(r => r.Action.DependsOn.Contains(id) || r.TargetId == id);
    private void RemoveAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionRow row) return;
        if (Referenced(row.Action.Id)) { Status.Text = "Remove references to this action first."; return; }
        _actions.Remove(row); RefreshChoices();
    }
    private void ActionUp_Click(object sender, RoutedEventArgs e) => MoveAction(-1);
    private void ActionDown_Click(object sender, RoutedEventArgs e) => MoveAction(1);
    private void MoveAction(int delta) { var i = ActionsGrid.SelectedIndex; if (i >= 0 && i + delta >= 0 && i + delta < _actions.Count) { _actions.Move(i, i + delta); ActionsGrid.SelectedIndex = i + delta; } }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Select application, shortcut or document", Multiselect = true };
        if (dialog.ShowDialog(this) == true) foreach (var path in dialog.FileNames) _rows.Add(new TargetRow(new() { Cmd = path }));
        RefreshChoices();
    }
    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) _rows.Add(new TargetRow(new() { Cmd = "explorer.exe", Args = [dialog.FolderName], Kind = TargetKind.Folder }));
        RefreshChoices();
    }
    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        button.IsEnabled = false; Status.Text = "Discovering installed applications…";
        try
        {
            var apps = await Task.Run(() => ApplicationDiscovery.Find()); if (_closed) return;
            var list = new ListBox { ItemsSource = apps, DisplayMemberPath = "Name", Margin = new(12) };
            var picker = new Window { Title = "ARCYN — Choose application", Owner = this, Width = 550, Height = 480, Content = list, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            WindowSurface.Prepare(picker, _theme);
            void Select() { if (list.SelectedItem is DiscoveredApp app) { _rows.Add(new TargetRow(new() { Cmd = app.Command })); picker.Close(); } }
            list.MouseDoubleClick += (_, _) => Select();
            list.KeyDown += (_, key) => { if (key.Key == System.Windows.Input.Key.Enter) Select(); };
            picker.ShowDialog(); RefreshChoices(); Status.Text = apps.Count + " applications found.";
        }
        catch (Exception ex) { Status.Text = "Discovery: " + ex.Message; }
        finally { button.IsEnabled = true; }
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta) { var i = TargetsGrid.SelectedIndex; if (i >= 0 && i < _rows.Count && i + delta >= 0 && i + delta < _rows.Count) { _rows.Move(i, i + delta); TargetsGrid.SelectedIndex = i + delta; } }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (TargetsGrid.SelectedItem is not TargetRow row) return;
        if (Referenced(row.Target.Id)) { Status.Text = "Remove action/dependency references to this target first."; return; }
        foreach (var placement in _layouts.Where(l => l.Layout.TargetId == row.Target.Id).ToArray()) _layouts.Remove(placement);
        _rows.Remove(row); RefreshChoices(); DrawMap();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
