using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ARCYN.UI.Models;
namespace ARCYN.UI;

public partial class MainWindow
{
    private void CloseHud_Click(object sender, RoutedEventArgs e)
    {
        if (_isLaunching) { _launchCts?.Cancel(); LaunchStatus.Text = "Canceling…"; }
        else CloseWithAnimation();
    }
    private void NewWorkspace_Click(object sender, RoutedEventArgs e) => EditWorkspace(null);
    private void Appearance_Click(object sender, RoutedEventArgs e) => EditTheme();
    private void WorkspaceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id } button || _modes.FirstOrDefault(m => m.Id == id) is not { } mode) return;
        var menu = MakeWorkspaceMenu(mode); menu.PlacementTarget = button; menu.IsOpen = true; e.Handled = true;
    }
    private void ShowRunFeedback(string text) { RunFeedback.Text = text; RunFeedback.Visibility = Visibility.Visible; }
    internal Action<string> WorkspaceError { get; set; } = null!;
    private bool HandleEditorKey(KeyEventArgs e)
    {
        if (_state.Phase != AppPhase.Ready || _isLaunching || _editing) return false;
        var tag = (FocusManager.GetFocusedElement(this) as Button)?.Tag;
        var selected = tag as ModeConfig ?? (tag is string id ? _modes.FirstOrDefault(m => m.Id == id) : null);
        if (e.Key == Key.F2) { EditWorkspace(selected ?? _modes.FirstOrDefault()); return true; }
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { EditWorkspace(null); return true; }
        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control) { EditTheme(); return true; }
        return false;
    }
    private ContextMenu MakeWorkspaceMenu(ModeConfig mode)
    {
        var menu = new ContextMenu { Resources = Resources };
        void Item(ItemsControl parent, string text, Action action, string gesture = "") { var item = new MenuItem { Header = text, InputGestureText = gesture }; item.Click += (_, _) => action(); parent.Items.Add(item); }
        Item(menu, "Edit workspace", () => EditWorkspace(mode), "F2");
        Item(menu, "New workspace", () => EditWorkspace(null), "Ctrl+N");
        var windows = new MenuItem { Header = "Windows" }; menu.Items.Add(windows);
        Item(windows, "Attach running windows", () => ManageWorkspace(mode, null));
        Item(windows, "Reapply saved layout", () => ManageWorkspace(mode, WindowOperation.ApplyLayout));
        Item(windows, "Minimize managed windows", () => ManageWorkspace(mode, WindowOperation.Minimize));
        Item(windows, "Restore ARCYN-minimized windows", () => ManageWorkspace(mode, WindowOperation.Restore));
        Item(windows, "Release managed windows", () => { if (!_isLaunching && !_editing) _orchestrator.Release(mode.Id); });
        Item(menu, "Last run details", () => WorkspaceError(_runReports.GetValueOrDefault(mode.Id, "No run in this ARCYN session.")));
        var organize = new MenuItem { Header = "Organize" }; menu.Items.Add(organize);
        Item(organize, "Duplicate workspace", () => DuplicateWorkspace(mode));
        Item(organize, "Move up", () => MoveWorkspace(mode, -1));
        Item(organize, "Move down", () => MoveWorkspace(mode, 1));
        Item(organize, "Delete workspace", () => DeleteWorkspace(mode));
        Item(menu, "Settings", EditTheme, "Ctrl+,");
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => { _menuOpen = false; ResetIdle(); };
        return menu;
    }
    private async void EditWorkspace(ModeConfig? mode)
    {
        if (_state.Phase != AppPhase.Ready || _isLaunching || _editing || _isClosing) return;
        _editing = true;
        try
        {
            var editor = new EditModeWindow(mode ?? new ModeConfig(), theme: _config.Theme) { Owner = this };
            if (editor.ShowDialog() != true) return;
            var before = _modes.ToList();
            if (mode == null) _modeService.Add(editor.Result);
            else _modes[_modes.IndexOf(mode)] = editor.Result;
            if (await SaveWorkspaces(before) && mode != null) _orchestrator.Release(mode.Id);
        }
        finally { _editing = false; ResetIdle(); }
    }
    private async void MoveWorkspace(ModeConfig mode, int delta)
    {
        if (_state.Phase != AppPhase.Ready || _isLaunching || _editing || _isClosing) return;
        var i = _modes.IndexOf(mode);
        if (i < 0 || i + delta < 0 || i + delta >= _modes.Count) return;
        _editing = true;
        var before = _modes.ToList();
        try { if (delta < 0) _modeService.MoveUp(i); else _modeService.MoveDown(i); await SaveWorkspaces(before); }
        finally { _editing = false; ResetIdle(); }
    }
    private async void DuplicateWorkspace(ModeConfig mode)
    {
        if (_state.Phase != AppPhase.Ready || _isLaunching || _editing || _isClosing) return;
        _editing = true;
        try
        {
            var before = _modes.ToList();
            var copy = WorkspaceDefinition.Duplicate(mode);
            copy.Name += " copy";
            _modeService.Insert(_modes.IndexOf(mode) + 1, copy);
            await SaveWorkspaces(before);
        }
        finally { _editing = false; ResetIdle(); }
    }
    private async void DeleteWorkspace(ModeConfig mode)
    {
        if (_state.Phase != AppPhase.Ready || _isLaunching || _editing || _isClosing) return;
        _editing = true;
        try
        {
            if (!WindowSurface.Show(this, "Delete workspace?", $"Remove '{mode.Label}' from ARCYN? Its applications, windows and files remain untouched.", "Delete workspace")) return;
            var before = _modes.ToList(); _modeService.RemoveAt(_modes.IndexOf(mode));
            if (await SaveWorkspaces(before)) _orchestrator.Release(mode.Id);
        }
        finally { _editing = false; ResetIdle(); }
    }
    private async Task<bool> SaveWorkspaces(List<ModeConfig> before)
    {
        var saved = false;
        try { await _configService.SaveAsync(_config); saved = true; }
        catch (Exception ex)
        {
            _modes.Clear(); _modes.AddRange(before);
            WorkspaceError("Workspace save failed. Previous configuration is retained.\n" + ex.Message);
        }
        _state.SelectedModeIndex = -1;
        for (var i = 0; i < _modes.Count; i++) _modes[i].Index = i + 1;
        BuildDashboard(); UpdateOperationalChrome();
        return saved;
    }

    private Task<bool> ReviewCommandsAsync(IReadOnlyList<Services.CommandReview> commands, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var text = "These configured operations can change files or system state. Review exact executable and arguments before continuing. Cancel launches nothing.\n\n" +
            string.Join("\n\n", commands.Select(c => (c.Elevate ? "[ADMINISTRATOR — UAC required]\n" : "") + Services.LaunchService.QuoteArgument(c.Executable) + " " +
                string.Join(" ", c.Arguments.Select(Services.LaunchService.QuoteArgument)) + "\nWorking directory: " + c.WorkingDirectory));
        return Task.FromResult(WindowSurface.Show(this, "Review configured commands", text, "Run reviewed commands"));
    }
    private static string FormatReport(Services.LaunchResult result) =>
        (result.Canceled ? "Canceled. Already-dispatched work was not undone.\n" : "") +
        string.Join("\n", result.Outcomes.Select(o => o.Label + " — " + o.Evidence + (o.Error == null ? "" : ": " + o.Error))) +
        (result.Failures.Count + result.Warnings.Count == 0 ? "" : "\n\n" + string.Join("\n", result.Failures.Concat(result.Warnings)));
    private async void ManageWorkspace(ModeConfig mode, WindowOperation? operation)
    {
        if (_state.Phase != AppPhase.Ready || _isLaunching || _editing || _isClosing) return;
        _isLaunching = true;
        _launchCts?.Dispose(); _launchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifeCts!.Token);
        try
        {
            var result = await _orchestrator.ManageAsync(mode, operation, _launchCts.Token);
            _runReports[mode.Id] = FormatReport(result);
            ShowRunFeedback(result.Canceled ? "Canceled. Dispatched work remains open. Workspace menu → Last run details." : result.Success ? "Window operations complete." : "Workspace needs attention. Workspace menu → Last run details.");
            if (!result.Success && !result.Canceled) WorkspaceError(FormatReport(result));
        }
        finally { _isLaunching = false; ResetIdle(); }
    }
}
