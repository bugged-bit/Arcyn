using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using ARCYN.UI.Models;
using ARCYN.UI.Services;
namespace ARCYN.UI;

public partial class EditModeWindow : Window
{
    public ModeConfig Result { get; }
    private readonly ObservableCollection<TargetRow> _rows;
    public EditModeWindow(ModeConfig mode)
    {
        Result = JsonSerializer.Deserialize<ModeConfig>(JsonSerializer.Serialize(mode, ConfigService.JsonOptions), ConfigService.JsonOptions)!;
        Result.RetainLaunchHistory(mode);
        InitializeComponent();
        NameInput.Text = mode.Name; DescInput.Text = mode.Description; TypeInput.Text = mode.Type;
        SessionInput.Text = mode.Session; StateInput.Text = mode.State; AccentInput.Text = mode.Accent;
        _rows = new(mode.LaunchTargets.Select(t => new TargetRow(t)));
        TargetsGrid.ItemsSource = _rows;
    }
    public sealed class TargetRow
    {
        internal TargetConfig Target = new();
        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string? WorkingDirectory { get; set; }
        public TargetRow() { }
        public TargetRow(TargetConfig target)
        {
            Target = JsonSerializer.Deserialize<TargetConfig>(JsonSerializer.Serialize(target, ConfigService.JsonOptions), ConfigService.JsonOptions)!;
            if (target.LegacyCommandLine)
                try { (Target.Cmd, Target.Args) = LaunchService.ParseTarget(target); }
                catch (ArgumentException) { /* Keep ambiguous text available for manual repair. */ }
            Command = Target.Cmd; Arguments = string.Join(" ", Target.Args.Select(LaunchService.QuoteArgument));
            WorkingDirectory = Target.WorkingDirectory;
        }
        public TargetConfig Commit()
        {
            Target.Cmd = Command.Trim();
            Target.Args = string.IsNullOrWhiteSpace(Arguments) ? [] : LaunchService.SplitCommandLine("target " + Arguments).Skip(1).ToList();
            Target.WorkingDirectory = WorkingDirectory;
            return Target;
        }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!TargetsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true) ||
                !TargetsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true))
                throw new ArgumentException("Finish editing the target before saving.");
            if (string.IsNullOrWhiteSpace(NameInput.Text)) throw new ArgumentException("Enter a workspace name.");
            ThemeService.ValidateColor(AccentInput.Text);
            Result.Name = NameInput.Text.Trim(); Result.Description = DescInput.Text;
            Result.Type = TypeInput.Text.Trim(); Result.Session = SessionInput.Text; Result.State = StateInput.Text; Result.Accent = AccentInput.Text;
            Result.Targets = _rows.Select(r => r.Commit()).ToList();
            Result.Apps.Clear(); Result.Folders.Clear(); Result.Websites.Clear();
            DialogResult = true;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Select application, shortcut or document", Multiselect = true };
        if (dialog.ShowDialog(this) == true) foreach (var path in dialog.FileNames) _rows.Add(new TargetRow(new() { Cmd = path }));
    }
    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) _rows.Add(new TargetRow(new() { Cmd = "explorer.exe", Args = [dialog.FolderName] }));
    }
    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button) return;
        button.IsEnabled = false; Status.Text = "Discovering installed applications…";
        try
        {
            var apps = await Task.Run(() => ApplicationDiscovery.Find());
            if (!IsLoaded) return;
            var list = new System.Windows.Controls.ListBox { ItemsSource = apps, DisplayMemberPath = "Name", Margin = new Thickness(12) };
            var picker = new Window { Title = "Choose an application — double-click to add", Owner = this, Width = 550, Height = 480, Content = list, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is DiscoveredApp app) { _rows.Add(new TargetRow(new() { Cmd = app.Command })); picker.Close(); } };
            list.KeyDown += (_, key) => { if (key.Key == System.Windows.Input.Key.Enter && list.SelectedItem is DiscoveredApp app) { _rows.Add(new TargetRow(new() { Cmd = app.Command })); picker.Close(); } };
            picker.ShowDialog(); Status.Text = $"{apps.Count} applications found. Manual browsing is always available.";
        }
        catch (Exception ex) { Status.Text = "Discovery: " + ex.Message; }
        finally { button.IsEnabled = true; }
    }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta) { var i = TargetsGrid.SelectedIndex; if (i >= 0 && i < _rows.Count && i + delta >= 0 && i + delta < _rows.Count) { _rows.Move(i, i + delta); TargetsGrid.SelectedIndex = i + delta; } }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (TargetsGrid.SelectedItem is TargetRow row) _rows.Remove(row); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
