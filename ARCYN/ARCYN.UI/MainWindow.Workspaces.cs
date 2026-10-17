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
            await SaveWorkspaces(before);
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
            var copy = System.Text.Json.JsonSerializer.Deserialize<ModeConfig>(System.Text.Json.JsonSerializer.Serialize(mode, Services.ConfigService.JsonOptions), Services.ConfigService.JsonOptions)!;
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
            await SaveWorkspaces(before);
        }
        finally { _editing = false; ResetIdle(); }
    }
    private async Task SaveWorkspaces(List<ModeConfig> before)
    {
        try { await _configService.SaveAsync(_config); }
        catch (Exception ex)
        {
            _modes.Clear(); _modes.AddRange(before);
            WorkspaceError("Workspace save failed. Previous configuration is retained.\n" + ex.Message);
        }
        _state.SelectedModeIndex = -1;
        for (var i = 0; i < _modes.Count; i++) _modes[i].Index = i + 1;
        BuildDashboard(); UpdateOperationalChrome();
    }
}
