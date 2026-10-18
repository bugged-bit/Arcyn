using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ARCYN.UI.Models;
using ARCYN.UI.Services;
namespace ARCYN.UI;

internal sealed class ThemeWindow : Window
{
    private ThemeConfig _draft;
    private readonly BehaviorConfig _behavior;
    private readonly Action<ThemeConfig, BehaviorConfig> _preview;
    private readonly Func<ThemeConfig, BehaviorConfig, Task> _save;
    private readonly Dictionary<PropertyInfo, Control> _inputs = [];
    private readonly DispatcherTimer _coalesce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly StackPanel _form = new();
    private readonly CheckBox _topmost = new() { Content = "Always on top" }, _close = new() { Content = "Close after launching" };
    private readonly TextBox _idle = new();
    private bool _loading, _saving;
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["Accent"] = "Accent colour", ["Background"] = "Background colour", ["Text"] = "Text colour",
        ["FontFamily"] = "Installed font family", ["FontScale"] = "Typography scale (0.7–1.8)",
        ["Width"] = "HUD width", ["Height"] = "HUD height", ["CornerRadius"] = "Corner radius", ["BorderWidth"] = "Border width",
        ["CardHeight"] = "Minimum workspace height", ["Spacing"] = "Workspace spacing", ["Layout"] = "Workspace layout",
        ["Particles"] = "Particles (0–120)", ["CursorTrail"] = "Cursor trail", ["Acrylic"] = "Native materials (when available)", ["Vignette"] = "Vignette",
        ["MotionAmount"] = "Continuous motion amount (0–2)", ["GlowOpacity"] = "Glow intensity (0–1)", ["Scanlines"] = "Scanlines",
        ["Animations"] = "Animate ambient effects", ["ReducedEffects"] = "Reduced effects", ["CompactMode"] = "Compact workspace rows"
    };
    public ThemeWindow(ThemeConfig theme, BehaviorConfig behavior, Action<ThemeConfig, BehaviorConfig> preview, Func<ThemeConfig, BehaviorConfig, Task> save)
    {
        _draft = ThemeService.Clone(theme); _behavior = new BehaviorConfig { IdleTimeoutSeconds = behavior.IdleTimeoutSeconds, AlwaysOnTop = behavior.AlwaysOnTop, CloseOnLaunch = behavior.CloseOnLaunch, Extra = behavior.Extra };
        _preview = preview; _save = save;
        Title = "ARCYN — Appearance & behavior"; Width = 660; Height = 760; MinWidth = 520; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.Black; Foreground = System.Windows.Media.Brushes.White;
        var root = new DockPanel { Margin = new Thickness(14) };
        var heading = new TextBlock { Text = "Make room for your own rhythm. Changes preview as you edit.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 18) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new WrapPanel();
        Button Button(string title, Action action) { var b = new Button { Content = title, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4) }; b.Click += (_, _) => action(); buttons.Children.Add(b); return b; }
        Button("Import theme", Import); Button("Export theme", Export);
        Button("Reset theme", () => { _draft = new(); BuildForm(); Schedule(); });
        Button("Cancel", () => Close()).IsCancel = true;
        var apply = Button("Apply changes", Save); apply.IsDefault = true; apply.SetResourceReference(StyleProperty, "PrimaryActionButton");
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = _form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        _topmost.Checked += (_, _) => Schedule(); _topmost.Unchecked += (_, _) => Schedule();
        _close.Checked += (_, _) => Schedule(); _close.Unchecked += (_, _) => Schedule();
        _idle.TextChanged += (_, _) => Schedule();
        System.Windows.Automation.AutomationProperties.SetName(_idle, "Idle timeout in seconds (0 disables)");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
        BuildForm();
        WindowSurface.Prepare(this, theme);
        _coalesce.Tick += (_, _) => { _coalesce.Stop(); Preview(); };
        Closed += (_, _) => _coalesce.Stop();
    }
    private void BuildForm()
    {
        _loading = true; _form.Children.Clear(); _inputs.Clear();
        void Fields(Panel parent, string[] names)
        {
            foreach (var name in names)
            {
                var property = typeof(ThemeConfig).GetProperty(name)!;
                var row = new StackPanel { Margin = new(4, 5, 4, 5) };
                row.Children.Add(new TextBlock { Text = Labels[name], TextWrapping = TextWrapping.Wrap });
                Control input;
                if (property.PropertyType == typeof(bool))
                { var check = new CheckBox { IsChecked = (bool)property.GetValue(_draft)! }; check.Checked += (_, _) => Schedule(); check.Unchecked += (_, _) => Schedule(); input = check; }
                else if (name == "Layout")
                {
                    var choices = new[] { new { Value = "rows", Name = "Comfortable rows" }, new { Value = "compact", Name = "Compact rows" }, new { Value = "grid", Name = "Two columns" } };
                    var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = _draft.Layout };
                    combo.SelectionChanged += (_, _) => Schedule(); input = combo;
                }
                else
                { var box = new TextBox { Text = Convert.ToString(property.GetValue(_draft), CultureInfo.InvariantCulture) }; box.TextChanged += (_, _) => Schedule(); input = box; }
                System.Windows.Automation.AutomationProperties.SetName(input, Labels[name]);
                _inputs[property] = input; row.Children.Add(input); parent.Children.Add(row);
            }
        }
        Fields(_form, ["Accent", "Layout", "FontScale", "ReducedEffects"]);
        _topmost.IsChecked = _behavior.AlwaysOnTop; _close.IsChecked = _behavior.CloseOnLaunch; _idle.Text = _behavior.IdleTimeoutSeconds.ToString();
        _form.Children.Add(_topmost); _form.Children.Add(_close);
        _form.Children.Add(new TextBlock { Text = "Idle timeout in seconds (0 disables)", Margin = new(4, 6, 4, 4) }); _form.Children.Add(_idle);
        var groups = new[] {
            (Title: "Color & type", Fields: new[] { "Background", "Text", "FontFamily" }),
            (Title: "Workspace proportions", Fields: new[] { "Width", "Height", "CornerRadius", "BorderWidth", "CardHeight", "Spacing", "CompactMode" }),
            (Title: "Motion & depth", Fields: new[] { "Acrylic", "Vignette", "GlowOpacity", "Particles", "CursorTrail", "Scanlines", "Animations", "MotionAmount" }) };
        foreach (var group in groups)
        {
            var body = new StackPanel(); Fields(body, group.Fields);
            var disclosure = new Expander { Header = group.Title, Content = body, Margin = new(4, 14, 4, 0) };
            foreach (var name in group.Fields) _inputs[typeof(ThemeConfig).GetProperty(name)!].Tag = disclosure;
            _form.Children.Add(disclosure);
        }
        _form.Children.Add(new TextBlock { Text = "Boot and launch choreography keep their original timing in every effect mode. Theme files contain visual settings only.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 12, 4, 12) });
        _loading = false;
    }
    private void Schedule() { if (!_loading) { _coalesce.Stop(); _coalesce.Start(); } }
    private bool Preview()
    {
        try
        {
            var next = ThemeService.Clone(_draft);
            foreach (var (property, input) in _inputs)
            {
                input.SetResourceReference(BorderBrushProperty, "SurfaceLineBrush"); input.ToolTip = null;
                try
                {
                    property.SetValue(next, input is CheckBox check ? check.IsChecked == true : input is ComboBox combo ? combo.SelectedValue : Convert.ChangeType(((TextBox)input).Text, property.PropertyType, CultureInfo.InvariantCulture));
                    ThemeService.Validate(next);
                }
                catch (Exception ex)
                {
                    input.BorderBrush = (SystemParameters.HighContrast ? SystemColors.HighlightBrush : System.Windows.Media.Brushes.Salmon); input.ToolTip = ex.Message;
                    if (input.Tag is Expander disclosure) disclosure.IsExpanded = true;
                    throw new ArgumentException(Labels[property.Name] + ": " + ex.Message);
                }
            }
            _idle.SetResourceReference(BorderBrushProperty, "SurfaceLineBrush"); _idle.ToolTip = null;
            if (!int.TryParse(_idle.Text, out var timeout) || timeout < 0 || timeout > 86400)
            { _idle.BorderBrush = (SystemParameters.HighContrast ? SystemColors.HighlightBrush : System.Windows.Media.Brushes.Salmon); _idle.ToolTip = "Use 0–86400 seconds."; throw new ArgumentException("Idle timeout: use 0–86400 seconds."); }
            _draft = next; _behavior.IdleTimeoutSeconds = timeout; _behavior.AlwaysOnTop = _topmost.IsChecked == true; _behavior.CloseOnLaunch = _close.IsChecked == true;
            ThemeService.Apply(this, next); WindowSurface.RefreshBackdrop(this, next);
            _preview(_draft, _behavior); _status.Text = "Preview active. Apply saves; Cancel restores."; return true;
        }
        catch (Exception ex) { _status.Text = ex.Message; return false; }
    }
    private async void Save()
    {
        if (await ApplyAsync()) DialogResult = true;
    }
    internal async Task<bool> ApplyAsync()
    {
        if (_saving) return false;
        _coalesce.Stop();
        if (!Preview()) return false;
        _saving = true; IsEnabled = false;
        try { await _save(_draft, _behavior); return true; }
        catch (Exception ex) { _status.Text = "Save failed; previous configuration retained. " + ex.Message; return false; }
        finally { _saving = false; IsEnabled = true; }
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    { if (_saving) e.Cancel = true; base.OnClosing(e); }
    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "ARCYN theme (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { _draft = ThemeService.Import(dialog.FileName); BuildForm(); Schedule(); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void Export()
    {
        if (!Preview()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "ARCYN theme (*.json)|*.json", FileName = "arcyn-theme.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { ThemeService.Export(dialog.FileName, _draft); _status.Text = "Theme exported."; }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
}
