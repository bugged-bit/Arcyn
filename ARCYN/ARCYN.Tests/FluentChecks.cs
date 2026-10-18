using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ARCYN.UI;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

internal static partial class Program
{
    private static void FluentChecks(ConfigService service)
    {
        Check(!ThemeService.IsDark(new(), true) && ThemeService.IsDark(new(), false) &&
            ThemeService.IsDark(new() { ColorMode = "dark" }, true) && !ThemeService.IsDark(new() { ColorMode = "light" }, false),
            "system appearance follows Windows while explicit light and dark override it");
        try { ThemeService.Validate(new() { ColorMode = "unknown" }); throw new Exception("Invalid appearance accepted"); }
        catch (ArgumentException) { Check(true, "invalid appearance is rejected at the configuration boundary"); }
        var surface = new Border();
        for (var i = 0; i < 20; i++) ThemeService.Apply(surface, new() { ColorMode = i % 2 == 0 ? "light" : "dark" });
        Check(surface.Resources.MergedDictionaries.Count == 1 && ((SolidColorBrush)surface.FindResource("WindowBrush")).Color.R == 32,
            "theme changes replace a shared platform palette without accumulating dictionaries");
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(CheckBox), typeof(RadioButton), typeof(Expander), typeof(DataGrid), typeof(ListBox), typeof(TabControl) })
        {
            var style = (Style)surface.FindResource(type);
            if (style.BasedOn == null || style.Setters.OfType<Setter>().Any(s => s.Property == Control.TemplateProperty)) throw new Exception("Handwritten template for " + type.Name);
        }
        Check(true, "every input, list and tab derives from the platform Fluent control template");
        ThemeService.Apply(surface, new() { FontScale = .7 });
        Check((double)surface.FindResource("FontSize12") >= 12 && (double)surface.FindResource("ControlContentThemeFontSize") >= 12,
            "small legacy scale values never shrink interface text below twelve DIPs");
        ThemeService.Apply(surface, new() { ColorMode = "light", SystemAccent = false, Accent = "#2266AA" });
        var custom = ((SolidColorBrush)surface.FindResource("AccentButtonBackground")).Color;
        var foreground = ((SolidColorBrush)surface.FindResource("AccentButtonForeground")).Color;
        Check(custom == ThemeService.ParseColor("#2266AA") && ThemeService.Contrast(custom, foreground) >= 4.5, "custom accent actions retain readable text");
        ThemeService.Apply(surface, new());
        Check(!surface.Resources.Keys.Cast<object>().Contains("AccentButtonBackground") && ((SolidColorBrush)surface.FindResource("AccentBrush")).Color == SystemColors.AccentColor,
            "returning to Windows accent removes custom control overrides");
        ThemeService.Apply(surface, new(), true);
        Check(surface.Resources.MergedDictionaries.Single().Source.ToString().Contains("Fluent.HC.xaml") &&
            ((SolidColorBrush)surface.FindResource("TextPrimaryBrush")).Color == SystemColors.WindowTextColor,
            "contrast policy selects the platform HC controls as well as app surfaces");
        var settings = new ThemeWindow(new(), new(), (_, _) => { }, (_, _) => System.Threading.Tasks.Task.CompletedTask);
        var inputs = Field<Dictionary<PropertyInfo, Control>>(settings, "_inputs");
        Check(inputs.Count == typeof(ThemeConfig).GetProperties().Count(p => p.Name != "Extra") &&
            inputs.Values.All(c => !string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(c))),
            "all saved personalization remains editable with accessible labels");
        Check(settings.WindowStyle == WindowStyle.SingleBorderWindow && WindowChrome.GetWindowChrome(settings) == null,
            "settings use the Windows title bar, resize behavior and system menu");
        Check(!inputs.Single(p => p.Key.Name == "Accent").Value.IsEnabled && ((ComboBox)inputs.Single(p => p.Key.Name == "ColorMode").Value).SelectedValue as string == "system",
            "Windows defaults disable inapplicable custom color controls without losing values");
        var setup = new SetupWindow(service); Call(setup, "ShowStep", 1);
        Check(!(bool)Call(setup, "CommitCurrentMode")! && ((TextBlock)setup.FindName("StatusBar")).Text.Contains("required"), "setup retains its draft and identifies a missing workspace name");
        RenderSurface(Detach(setup), "setup-validation", 740, 600);
        ((TextBox)setup.FindName("ModeNameInput")).Text = "Deep work"; Call(setup, "CommitCurrentMode");
        Check(Field<ModeConfig>(setup, "_currentMode").Name == "Deep work", "setup preserves the user's chosen name casing");
        setup.Close();
        var list = new ListBox { ItemsSource = new[] { "Notes", "Browser" } };
        var picker = WindowSurface.CreatePicker(null, new(), "Choose application", list, () => throw new Exception("Selection dispatched during render"));
        var content = Detach(picker);
        var accept = Descendants(content).OfType<Button>().Single(b => b.IsDefault);
        Check(!accept.IsEnabled, "picker selection action is disabled until an item is selected");
        list.SelectedIndex = 0;
        Check(accept.IsEnabled && picker.WindowStyle == WindowStyle.SingleBorderWindow && new WindowInteropHelper(picker).Handle == 0,
            "picker selection exposes a native default button without creating a window");
        RenderSurface(content, "picker", 650, 500); picker.Close(); settings.Close();
        foreach (var scale in new[] { 1d, 1.8 })
        {
            var theme = new ThemeConfig { ColorMode = "dark", FontScale = scale };
            var target = new TargetConfig { Cmd = "never-dispatched.exe", Match = new() { Executable = @"C:\Fixtures\never-dispatched.exe", Title = "Notes" } };
            var editor = new EditModeWindow(new() { Label = "Notes", Targets = [target], Actions = [
                new WindowActionConfig { Label = "Arrange Notes", TargetId = target.Id }, new WaitActionConfig { Label = "Pause", DelayMs = 250 }] }, new FakeWindows(), theme);
            ((Expander)editor.FindName("TargetAdvanced")).IsExpanded = true;
            ((Expander)editor.FindName("MatchDisclosure")).IsExpanded = true;
            var body = Detach(editor);
            RenderSurface(body, "target-matching-" + scale, 980, 740);
            var tabs = (TabControl)editor.FindName("EditorTabs"); tabs.SelectedIndex = 2;
            for (var i = 0; i < 2; i++) { ((DataGrid)editor.FindName("ActionsGrid")).SelectedIndex = i; RenderSurface(body, "action-kind-" + i + "-" + scale, 980, 740); }
            editor.Close();
            var motion = new ThemeWindow(theme, new(), (_, _) => { }, (_, _) => System.Threading.Tasks.Task.CompletedTask);
            var motionTabs = Field<TabControl>(motion, "_form"); motionTabs.SelectedIndex = 2;
            var motionBody = Detach(motion); RenderSurface(motionBody, "motion-" + scale, 760, 740);
            foreach (var disclosure in Descendants(motionBody).OfType<Expander>()) disclosure.IsExpanded = true;
            RenderSurface(motionBody, "motion-expanded-" + scale, 760, 740); motion.Close();
        }
        Check(Application.Current.Windows.Cast<Window>().All(window => !window.IsVisible && new WindowInteropHelper(window).Handle == 0),
            "expanded matching, window/wait actions and motion settings render without native windows");
    }
}
