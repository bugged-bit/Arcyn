using System.IO;
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
    private static void ReleaseCandidateChecks(ConfigService service)
    {
        var theme = new ThemeConfig();
        Check(App.ProductVersion == "1.6.0-rc.1" && ("ARCYN WORKSPACE  · " + App.ProductVersion).Length == 29, "release identity appears consistently without changing boot typing duration");
        Check(WindowSurface.PreferredMaterial(theme, SurfaceRole.Launcher, false, true) == MaterialMode.Acrylic &&
            WindowSurface.PreferredMaterial(theme, SurfaceRole.Workspace, false, true) == MaterialMode.Mica,
            "supported material roles distinguish transient HUD from long-lived editor");
        Check(new[] { WindowSurface.PreferredMaterial(theme, SurfaceRole.Dialog, true, true), WindowSurface.PreferredMaterial(theme, SurfaceRole.Dialog, false, false),
            WindowSurface.PreferredMaterial(new() { ReducedEffects = true }, SurfaceRole.Launcher, false, true),
            WindowSurface.PreferredMaterial(new() { Acrylic = false }, SurfaceRole.Workspace, false, true),
            WindowSurface.InitializedMaterial(MaterialMode.Acrylic, false) }.All(m => m == MaterialMode.Opaque) && WindowSurface.InitializedMaterial(MaterialMode.Mica, true) == MaterialMode.Mica,
            "unsupported, high contrast, disabled effects and failed native requests select opaque fallback");
        var palette = new Border();
        Color ColorOf(string key) => ((SolidColorBrush)palette.FindResource(key)).Color;
        foreach (var bg in new[] { "#101318", "#F3F0EB", "#696969", "#804090" })
        foreach (var mode in Enum.GetValues<MaterialMode>())
        {
            var custom = new ThemeConfig { ColorMode = "custom", Background = bg, Text = bg, Accent = "#E4DAF0" };
            ThemeService.ApplySurfaces(palette, custom, mode, false);
            var shell = ColorOf("ShellBrush");
            foreach (var underlay in new[] { Colors.Black, Colors.White })
            {
                var composite = ThemeService.Blend(underlay, shell, shell.A / 255d);
                foreach (var key in new[] { "TextPrimaryBrush", "TextDimBrush", "TextMutedBrush" })
                    if (ThemeService.Contrast(ColorOf(key), composite) < 4.5) throw new Exception(bg + " unreadable " + key);
            }
            if (ThemeService.Contrast(ColorOf("SelectionTextBrush"), ColorOf("AccentSurfaceBrush")) < 4.5) throw new Exception("Unreadable selection");
            if (custom.Text != bg) throw new Exception("Saved palette was rewritten");
        }
        Check(true, "custom palettes maintain readable shell and selection contrast over dark and light underlays without rewriting preferences");
        ThemeService.ApplySurfaces(palette, theme, MaterialMode.Acrylic, true);
        Check(ColorOf("ShellBrush") == SystemColors.WindowColor && ColorOf("FieldBrush") == SystemColors.WindowColor && ColorOf("TextPrimaryBrush") == SystemColors.WindowTextColor &&
            ColorOf("AccentSurfaceBrush") == SystemColors.HighlightColor && ColorOf("SelectionTextBrush") == SystemColors.HighlightTextColor,
            "high contrast surfaces and selections use exact system colors");
        Check(WindowSurface.FitSize(new(980, 740), new(800, 600)) == new Size(784, 584) && WindowSurface.FitSize(new(640, 360), new(1920, 1080)) == new Size(640, 360),
            "surface sizing constrains to usable monitor area without enlarging smaller windows");

        using var hudWindow = new MainWindow(service, new() { Modes = [] });
        Call(hudWindow, "LoadConfig"); Field<AppState>(hudWindow, "_state").TransitionTo(AppPhase.Ready); Call(hudWindow, "BuildDashboard");
        var empty = (Grid)hudWindow.FindName("DashboardGrid");
        Check(empty.Children.OfType<TextBlock>().Single().Text.Contains("Add the apps"), "empty HUD explains the first useful action");
        Check(((UIElement)hudWindow.FindName("AmbientGlow")).Visibility == Visibility.Collapsed && ((UIElement)hudWindow.FindName("ScanlineLayer")).Visibility == Visibility.Collapsed, "Ready suppresses cinematic layers without an idle render loop");
        Call(hudWindow, "ShowRunFeedback", "Canceled. Dispatched work remains open."); Call(hudWindow, "UpdateOperationalChrome");
        Check(((TextBlock)hudWindow.FindName("RunFeedback")).Text.Contains("Canceled") && ((TextBlock)hudWindow.FindName("RunFeedback")).Visibility == Visibility.Visible,
            "operational refresh retains actionable canceled-run feedback");
        Check(WindowChrome.GetWindowChrome(hudWindow).GlassFrameThickness == new Thickness(0) && new WindowInteropHelper(hudWindow).Handle == 0,
            "uninitialized surfaces remain opaque without forcing an HWND");
        var settings = new ThemeWindow(new(), new(), (_, _) => { }, (_, _) => Task.CompletedTask);
        var inputs = Field<Dictionary<PropertyInfo, Control>>(settings, "_inputs");
        var layout = (ComboBox)inputs.Single(p => p.Key.Name == "Layout").Value;
        Check(layout.Items.Count == 3 && layout.DisplayMemberPath == "Name" && layout.SelectedValue as string == "rows", "appearance offers named layout choices with compatible stored values");
        var width = (TextBox)inputs.Single(p => p.Key.Name == "Width").Value; width.Text = "invalid";
        Check(!(bool)Call(settings, "Preview")! && width.ToolTip != null && width.Tag is Expander { IsExpanded: true } && Field<TabControl>(settings, "_form").SelectedIndex == 2 && Field<TextBlock>(settings, "_status").Text.Contains("HUD width"), "invalid appearance input identifies its field and opens its disclosure");
        RenderSurface(Detach(settings), "appearance-error", 660, 760);
        settings.Close();
        var state = hudWindow.Resources.Keys.Cast<object>().Select(k => hudWindow.Resources[k]).Single(v => v?.GetType().Name == "State")!;
        hudWindow.Close();
        Check(Field<bool>(state, "Closed") && Field<object?>(state, "SettingsChanged") == null && Field<object?>(state, "Hook") == null && Field<object?>(state, "Source") == null,
            "closing an unshown surface leaves no system or native subscriptions");

        var fixture = new ModeConfig { Label = "Make room", Subtitle = "Everything you need, together.", Targets = [new() { Cmd = "never-dispatched.exe" }],
            Actions = [new CommandActionConfig { Label = "Reviewed command", Cmd = "never-dispatched.exe" }] };
        var fake = new FakeWindows();
        fixture.Layout.Add(new() { TargetId = fixture.Targets[0].Id, Monitor = fake.Displays[0].Id, X = .1, Y = .1, Width = .7, Height = .7, Focus = true });
        var rendersBefore = _surfaceRenderCount;
        foreach (var scale in new[] { 1d, 1.4, 1.8 })
        foreach (var narrow in new[] { false, true })
        foreach (var policy in new[] { "dark", "light", "custom", "high-contrast" })
        {
            var variant = new ThemeConfig { ColorMode = policy is "light" or "dark" ? policy : "custom", FontScale = scale, ReducedEffects = true,
                Background = policy == "light" ? "#F3F0EB" : policy == "custom" ? "#804090" : "#101318", Text = policy == "light" ? "#202530" : "#F4F6FA", Accent = "#C8666E" };
            var tag = policy + "-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + (narrow ? "-narrow" : "-normal");
            void Render(Window window, string name, int width, int height)
            {
                ThemeService.Apply(window, variant, policy == "high-contrast");
                ThemeService.ApplySurfaces(window, variant, MaterialMode.Opaque, policy == "high-contrast");
                window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
                var content = Detach(window);
                RenderSurface(content, "rc-" + name + "-" + tag, width, height);
                if (new WindowInteropHelper(window).Handle != 0 || window.IsVisible) throw new Exception("Offscreen test created a window");
            }
            var w = narrow ? 760 : 980; var h = narrow ? 600 : 740;
            using var launcher = new MainWindow(service, new() { Theme = variant, Modes = policy == "custom" ? [] : [fixture] });
            Call(launcher, "LoadConfig"); Field<AppState>(launcher, "_state").TransitionTo(AppPhase.Ready); Call(launcher, "BuildDashboard");
            ((FrameworkElement)launcher.FindName("BootOverlay")).Visibility = Visibility.Collapsed; ((FrameworkElement)launcher.FindName("MainHUD")).Opacity = 1;
            var border = (Border)launcher.FindName("MainBorder"); border.Width = w - 32; border.Height = h - 24;
            Render(launcher, "hud", w, h);
            ((FrameworkElement)launcher.FindName("ModePanel")).Visibility = Visibility.Collapsed;
            var launchPanel = (FrameworkElement)launcher.FindName("LaunchPanel"); launchPanel.Visibility = Visibility.Visible; launchPanel.Opacity = 1;
            ((TextBlock)launcher.FindName("LaunchModeLabel")).Text = fixture.Label;
            ((TextBlock)launcher.FindName("LaunchStatus")).Text = "Design application · Reusing · waiting for a responsive window";
            ((TextBlock)launcher.FindName("LaunchFeedText")).Text = "Notes · Ready\nDesign application · Waiting for window";
            RenderSurface((FrameworkElement)((Border)launcher.FindName("MainBorder")).Parent, "rc-launch-" + tag, w, h);
            ((UIElement)launcher.FindName("MainHUD")).Opacity = 0;
            var boot = (FrameworkElement)launcher.FindName("BootOverlay"); boot.Visibility = Visibility.Visible; boot.Opacity = 1;
            ((TextBlock)launcher.FindName("BootTitle")).Text = "ARCYN";
            ((TextBlock)launcher.FindName("BootSubtitle")).Text = "ARCYN WORKSPACE  · " + App.ProductVersion;
            ((TextBlock)launcher.FindName("BootLog")).Text = "Bringing things together…\nA little room to focus.\nYour workspaces await.\nMake space to work.\nReady to begin.";
            RenderSurface((FrameworkElement)border.Parent, "rc-boot-" + tag, w, h);

            var editor = new EditModeWindow(fixture, fake, variant);
            var tabs = (TabControl)editor.FindName("EditorTabs");
            if (((DataGrid)editor.FindName("TargetsGrid")).CanUserSortColumns || ((DataGrid)editor.FindName("ActionsGrid")).CanUserSortColumns) throw new Exception("Workflow order can be visually sorted away from execution order");
            ThemeService.Apply(editor, variant, policy == "high-contrast");
            ThemeService.ApplySurfaces(editor, variant, MaterialMode.Opaque, policy == "high-contrast");
            var editorContent = Detach(editor);
            foreach (var index in new[] { 0, 1, 2 })
            {
                tabs.SelectedIndex = index; if (index == 2) ((DataGrid)editor.FindName("ActionsGrid")).SelectedIndex = 0;
                RenderSurface(editorContent, "rc-editor-" + index + "-" + tag, w, h);
                if (index == 2 && ((DataGrid)editor.FindName("ActionsGrid")).Columns[0].ActualWidth < 100 * scale) throw new Exception("Action kind labels are clipped");
                if (index == 1 && (((Canvas)editor.FindName("LayoutMap")).ActualHeight < 180 || ((FrameworkElement)editor.FindName("WorkspaceFields")).Visibility != Visibility.Collapsed)) throw new Exception("Layout view lost its monitor canvas space");
                if (index == 0 && narrow && Grid.GetRow(((Grid)editor.FindName("TargetsPane")).Children[1]) != 1) throw new Exception("Narrow inspector did not stack");
                if (index == 0 && !narrow && scale == 1 && ((ScrollViewer)((Grid)editor.FindName("TargetsPane")).Parent).VerticalScrollBarVisibility != ScrollBarVisibility.Disabled) throw new Exception("Wide editor unnecessarily scrolls its actions out of view");
                if (narrow)
                {
                    var page = (ScrollViewer)((TabItem)tabs.Items[index]).Content;
                    page.ScrollToBottom(); editorContent.UpdateLayout();
                    RenderSurface(editorContent, "rc-editor-" + index + "-bottom-" + tag, w, h);
                    if (page.ExtentHeight > page.ViewportHeight && page.VerticalOffset <= 0) throw new Exception("Narrow editor cannot reach its inspector");
                }

            }
            if (new WindowInteropHelper(editor).Handle != 0 || fake.Operations.Count != 0) throw new Exception("Editor touched desktop");
            editor.Close();
            var appearance = new ThemeWindow(variant, new(), (_, _) => { }, (_, _) => Task.CompletedTask);
            ThemeService.Apply(appearance, variant, policy == "high-contrast");
            var appearanceContent = Detach(appearance); var appearanceTabs = Field<TabControl>(appearance, "_form");
            for (var tab = 0; tab < 3; tab++) { appearanceTabs.SelectedIndex = tab; RenderSurface(appearanceContent, "rc-appearance-" + tab + "-" + tag, narrow ? 480 : 760, h); }
            appearance.Close();
            var choices = new ListBox { ItemsSource = new[] { "Notes", "Browser" }, SelectedIndex = 0 };
            var picker = WindowSurface.CreatePicker(null, variant, "Choose application", choices, () => throw new Exception("Picker changed selection during render")); Render(picker, "picker", narrow ? 480 : 660, h); picker.Close();
            var review = WindowSurface.CreateDialog(null, "Review commands", "No command runs until you approve its exact arguments.\n\nnever-dispatched.exe \"a b\"", "Run reviewed commands"); Render(review, "review", narrow ? 480 : 660, narrow ? 400 : 500); review.Close();
            var menu = (ContextMenu)Call(launcher, "MakeWorkspaceMenu", fixture)!; menu.Resources = launcher.Resources; RenderSurface(menu, "rc-menu-" + tag, narrow ? 360 : 480, 600);
            foreach (var group in menu.Items.OfType<MenuItem>().Where(item => item.Header as string is "Windows" or "Organize"))
            {
                var submenu = new ContextMenu { Resources = launcher.Resources };
                foreach (var item in group.Items.Cast<object>().ToArray()) { group.Items.Remove(item); submenu.Items.Add(item); }
                RenderSurface(submenu, "rc-menu-" + group.Header + "-" + tag, narrow ? 360 : 480, 600);
                if (submenu.IsOpen) throw new Exception("Offscreen test opened a popup");
            }
            var setup = new SetupWindow(service); ThemeService.Apply(setup, variant, policy == "high-contrast"); ThemeService.ApplySurfaces(setup, variant, MaterialMode.Opaque, policy == "high-contrast"); var setupContent = Detach(setup);
            for (var step = 0; step < 6; step++) { Call(setup, "ShowStep", step); RenderSurface(setupContent, "rc-setup-" + step + "-" + tag, w, h); }
            setup.Close();
            launcher.Close();
        }
        Check(_surfaceRenderCount - rendersBefore == 516, "516 offscreen states cover all interfaces, narrow scrolling, 1.0/1.4/1.8 text, light/dark/custom/contrast policies without HWNDs or desktop mutations");
    }
}
