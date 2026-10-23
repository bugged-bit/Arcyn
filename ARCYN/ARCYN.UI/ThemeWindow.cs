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
    private sealed record Choice(string Value, string Name)
    {
        public override string ToString() => Name;
    }
    private ThemeConfig _draft;
    private readonly BehaviorConfig _behavior;
    private readonly Action<ThemeConfig, BehaviorConfig> _preview;
    private readonly Func<ThemeConfig, BehaviorConfig, Task> _save;
    private readonly Dictionary<PropertyInfo, Control> _inputs = [];
    private readonly DispatcherTimer _coalesce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TabControl _form = new();
    private readonly CheckBox _topmost = new() { Content = "Always on top" }, _close = new() { Content = "Close after launching" };
    private readonly TextBox _idle = new();
    private bool _loading, _saving;
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["ColorMode"] = "App theme", ["SystemAccent"] = "Use Windows accent color",
        ["Accent"] = "Custom accent color", ["Background"] = "Background colour", ["Text"] = "Text colour",
        ["FontFamily"] = "Installed font family", ["FontScale"] = "Typography scale (0.7–1.8)",
        ["Width"] = "HUD width", ["Height"] = "HUD height", ["CornerRadius"] = "Shell corners (Custom theme)", ["BorderWidth"] = "Border width",
        ["CardHeight"] = "Minimum workspace height", ["Spacing"] = "Workspace spacing", ["Layout"] = "Workspace layout",
        ["Particles"] = "Particles (0–120)", ["CursorTrail"] = "Cursor trail", ["Acrylic"] = "Native materials (when available)", ["Vignette"] = "Vignette",
        ["MotionAmount"] = "Continuous motion amount (0–2)", ["GlowOpacity"] = "Glow intensity (0–1)", ["Scanlines"] = "Scanlines",
        ["Animations"] = "Animate ambient effects", ["ReducedEffects"] = "Reduced effects", ["CompactMode"] = "Compact workspace rows"
    };
    public ThemeWindow(ThemeConfig theme, BehaviorConfig behavior, Action<ThemeConfig, BehaviorConfig> preview, Func<ThemeConfig, BehaviorConfig, Task> save)
    {
        _draft = ThemeService.Clone(theme); _behavior = new BehaviorConfig { IdleTimeoutSeconds = behavior.IdleTimeoutSeconds, AlwaysOnTop = behavior.AlwaysOnTop, CloseOnLaunch = behavior.CloseOnLaunch, Extra = behavior.Extra };
        _preview = preview; _save = save;
        Title = "ARCYN settings"; Width = 760; Height = 720; MinWidth = 480; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(24, 16, 24, 20) };
        var heading = new TextBlock { Text = "Settings", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 18) };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "FontSize22");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new WrapPanel();
        Button Button(string title, Action action) { var b = new Button { Content = title, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4) }; b.Click += (_, _) => action(); buttons.Children.Add(b); return b; }
        Button("Cancel", () => Close()).IsCancel = true;
        var apply = Button("Apply changes", Save); apply.IsDefault = true; apply.SetResourceReference(StyleProperty, "PrimaryActionButton");
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        root.Children.Add(_form); Content = root;
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
        _loading = true; _form.Items.Clear(); _inputs.Clear();
        void Fields(Panel parent, string[] names)
        {
            foreach (var name in names)
            {
                var property = typeof(ThemeConfig).GetProperty(name)!;
                var row = new StackPanel { Margin = new(4, 5, 4, 5) };
                row.Children.Add(new TextBlock { Text = Labels[name], TextWrapping = TextWrapping.Wrap });
                Control input;
                if (property.PropertyType == typeof(bool))
                { row.Children.Clear(); var check = new CheckBox { Content = Labels[name], IsChecked = (bool)property.GetValue(_draft)! }; check.Checked += (_, _) => Schedule(); check.Unchecked += (_, _) => Schedule(); input = check; }
                else if (name is "Layout" or "ColorMode")
                {
                    var choices = name == "Layout" ? new[] { new Choice("rows", "Comfortable rows"), new Choice("compact", "Compact rows"), new Choice("grid", "Two columns") } : new[] { new Choice("system", "Use Windows setting"), new Choice("light", "Light"), new Choice("dark", "Dark"), new Choice("custom", "Custom colors and font") };
                    var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = property.GetValue(_draft) };
                    combo.SelectionChanged += (_, _) => Schedule(); input = combo;
                }
                else
                { var box = new TextBox { Text = Convert.ToString(property.GetValue(_draft), CultureInfo.InvariantCulture) }; box.TextChanged += (_, _) => Schedule(); input = box; }
                System.Windows.Automation.AutomationProperties.SetName(input, Labels[name]);
                _inputs[property] = input; row.Children.Add(input); parent.Children.Add(row);
            }
        }
        StackPanel Page(string title, string description)
        {
            var panel = new StackPanel { Margin = new(8,16,16,16) };
            panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,20) });
            var page = new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            _form.Items.Add(page); return panel;
        }
        var general = Page("General", "Choose how ARCYN fits into your day. Changes preview here; Apply saves them.");
        _topmost.IsChecked = _behavior.AlwaysOnTop; _close.IsChecked = _behavior.CloseOnLaunch; _idle.Text = _behavior.IdleTimeoutSeconds.ToString();
        general.Children.Add(_topmost); general.Children.Add(_close);
        general.Children.Add(new TextBlock { Text = "Close when idle (seconds; 0 keeps ARCYN open)", TextWrapping = TextWrapping.Wrap, Margin = new(0,12,0,0) }); general.Children.Add(_idle);
        Fields(general, ["ReducedEffects"]);
        var appearance = Page("Appearance", "Follow Windows or choose an app theme. Windows contrast settings always take priority.");
        Fields(appearance, ["ColorMode", "SystemAccent", "Accent", "Layout", "FontScale", "Acrylic"]);
        var colors = new StackPanel(); Fields(colors, ["Background", "Text", "FontFamily"]);
        var custom = new Expander { Header = "Custom colors and font", Content = colors, IsExpanded = _draft.ColorMode == "custom" };
        foreach (var name in new[] { "Background", "Text", "FontFamily" }) _inputs[typeof(ThemeConfig).GetProperty(name)!].Tag = custom;
        appearance.Children.Add(custom);
        appearance.Children.Add(new TextBlock { Text = "Custom colors and font apply when App theme is set to Custom. Other themes use Segoe UI Variable and Windows surfaces.", TextWrapping = TextWrapping.Wrap, Margin = new(0,12,0,0) });
        var tools = new WrapPanel { Margin = new(0,16,0,0) };
        foreach (var (label, action) in new (string, Action)[] { ("Import theme", Import), ("Export theme", Export), ("Reset theme", () => { _draft = new(); BuildForm(); Schedule(); }) })
        { var button = new Button { Content = label }; button.Click += (_, _) => action(); tools.Children.Add(button); }
        appearance.Children.Add(tools);
        var cinematic = Page("Motion", "Opening and launch sequences keep ARCYN’s deliberate timing. Ambient effects run only during those sequences.");
        var groups = new[] {
            (Title: "Workspace proportions", Fields: new[] { "Width", "Height", "CornerRadius", "BorderWidth", "CardHeight", "Spacing", "CompactMode" }),
            (Title: "Cinematic effects", Fields: new[] { "Vignette", "GlowOpacity", "Particles", "CursorTrail", "Scanlines", "Animations", "MotionAmount" }) };
        foreach (var group in groups)
        {
            var body = new StackPanel(); Fields(body, group.Fields);
            var disclosure = new Expander { Header = group.Title, Content = body, Margin = new(0,4,0,8) };
            foreach (var name in group.Fields) _inputs[typeof(ThemeConfig).GetProperty(name)!].Tag = disclosure;
            cinematic.Children.Add(disclosure);
        }
        foreach (TabItem page in _form.Items)
            foreach (var input in _inputs.Values.Where(input => IsWithin(input, page))) input.Resources["SettingsPage"] = page;
        SetPersonalizationAvailability();
        _loading = false;
    }
    private void SetPersonalizationAvailability()
    {
        _inputs[typeof(ThemeConfig).GetProperty("Accent")!].IsEnabled = !_draft.SystemAccent;
        foreach (var name in new[] { "Background", "Text", "FontFamily" }) _inputs[typeof(ThemeConfig).GetProperty(name)!].IsEnabled = _draft.ColorMode == "custom";
    }
    private static bool IsWithin(DependencyObject input, DependencyObject parent)
    {
        for (var current = input; current != null; current = LogicalTreeHelper.GetParent(current)) if (current == parent) return true;
        return false;
    }
    private void Schedule() { if (!_loading) { _coalesce.Stop(); _coalesce.Start(); } }
    private bool Preview()
    {
        try
        {
            var next = ThemeService.Clone(_draft);
            foreach (var (property, input) in _inputs)
            {
                input.ClearValue(BorderBrushProperty); input.ToolTip = null;
                try
                {
                    property.SetValue(next, input is CheckBox check ? check.IsChecked == true : input is ComboBox combo ? combo.SelectedValue : Convert.ChangeType(((TextBox)input).Text, property.PropertyType, CultureInfo.InvariantCulture));
                    ThemeService.Validate(next);
                }
                catch (Exception ex)
                {
                    input.BorderBrush = (SystemParameters.HighContrast ? SystemColors.HighlightBrush : System.Windows.Media.Brushes.Salmon); input.ToolTip = ex.Message;
                    if (input.Tag is Expander disclosure) disclosure.IsExpanded = true;
                    if (input.Resources["SettingsPage"] is TabItem page) _form.SelectedItem = page;
                    throw new ArgumentException(Labels[property.Name] + ": " + ex.Message);
                }
            }
            _idle.ClearValue(BorderBrushProperty); _idle.ToolTip = null;
            if (!int.TryParse(_idle.Text, out var timeout) || timeout < 0 || timeout > 86400)
            { _form.SelectedIndex = 0; _idle.BorderBrush = (SystemParameters.HighContrast ? SystemColors.HighlightBrush : System.Windows.Media.Brushes.Salmon); _idle.ToolTip = "Use 0–86400 seconds."; throw new ArgumentException("Idle timeout: use 0–86400 seconds."); }
            _draft = next; SetPersonalizationAvailability(); _behavior.IdleTimeoutSeconds = timeout; _behavior.AlwaysOnTop = _topmost.IsChecked == true; _behavior.CloseOnLaunch = _close.IsChecked == true;
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
