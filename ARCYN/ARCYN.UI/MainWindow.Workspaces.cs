using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ARCYN.UI.Models;
namespace ARCYN.UI;

public partial class MainWindow
{
    internal Action<string> WorkspaceError { get; set; } = null!;
    private bool HandleEditorKey(KeyEventArgs e)
    {
        if (_state.Phase != AppPhase.Ready || _isLaunching || _editing) return false;
        var selected = FocusManager.GetFocusedElement(this) is Button { Tag: ModeConfig mode } ? mode : null;
        if (e.Key == Key.F2) { EditWorkspace(selected ?? _modes.FirstOrDefault()); return true; }
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { EditWorkspace(null); return true; }
        if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control) { EditTheme(); return true; }
        return false;
    }
    private ContextMenu MakeWorkspaceMenu(ModeConfig mode)
    {
        var menu = new ContextMenu();
        void Item(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Edit workspace (F2)", () => EditWorkspace(mode));
        Item("Attach running windows", () => ManageWorkspace(mode, null));
        Item("Reapply saved layout", () => ManageWorkspace(mode, WindowOperation.ApplyLayout));
        Item("Minimize managed windows", () => ManageWorkspace(mode, WindowOperation.Minimize));
        Item("Restore windows minimized by ARCYN", () => ManageWorkspace(mode, WindowOperation.Restore));
        Item("Release managed windows", () => { if (!_isLaunching && !_editing) _orchestrator.Release(mode.Id); });
        Item("Last run details", () => WorkspaceError(_runReports.GetValueOrDefault(mode.Id, "No run in this ARCYN session.")));
        Item("New workspace (Ctrl+N)", () => EditWorkspace(null));
        Item("Duplicate workspace", () => DuplicateWorkspace(mode));
        Item("Move up", () => MoveWorkspace(mode, -1));
        Item("Move down", () => MoveWorkspace(mode, 1));
        Item("Delete workspace", () => DeleteWorkspace(mode));
        Item("Theme and settings (Ctrl+,)", EditTheme);
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
            var editor = new EditModeWindow(mode ?? new ModeConfig()) { Owner = this };
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
            copy.Name += " COPY";
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
            if (MessageBox.Show(this, $"Delete workspace '{mode.Label}'? Its applications and files remain on disk.", "ARCYN", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
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
        return Task.FromResult(MessageBox.Show(this, text, "ARCYN — authorize configured commands", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK);
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
            LaunchRecent.Text = result.Success ? $"{result.CompletedSteps} window operations complete" : "Workspace needs attention — Last run details";
            if (!result.Success && !result.Canceled) WorkspaceError(FormatReport(result));
        }
        finally { _isLaunching = false; ResetIdle(); }
    }
}
