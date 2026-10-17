using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ARCYN.UI;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

internal static partial class Program
{
    private static FrameworkElement Detach(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Resources = window.Resources;
        TextElement.SetForeground(content, window.Foreground);
        TextElement.SetFontFamily(content, window.FontFamily);
        TextElement.SetFontSize(content, window.FontSize);
        return new Border { Background = window.Background, Child = content };
    }

    private static void RenderSurface(FrameworkElement content, string name, int width, int height)
    {
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "offscreen-" + name + ".png")); encoder.Save(file);
        if (file.Length < 2000) throw new Exception("Empty offscreen surface: " + name);
    }

    private static void VisualSurfaceChecks(ConfigService service)
    {
        var modes = new List<ModeConfig> {
            new() { Label = "Deep work", Subtitle = "A little room to think.", Apps = ["Code.exe", "Notes.exe"], Folders = [@"C:\Projects"], Index = 1 },
            new() { Label = "Make something", Subtitle = "Ideas, sketches and the next good thing.", Apps = ["Design.exe"], Websites = ["https://example.com"], Index = 2 },
            new() { Label = "Find your next idea", Subtitle = "Research with everything in reach.", Apps = ["Browser.exe"], Index = 3 } };
        using var window = new MainWindow(service, new() { Modes = modes });
        Call(window, "LoadConfig"); Field<AppState>(window, "_state").TransitionTo(AppPhase.Ready); Call(window, "BuildDashboard");
        ((FrameworkElement)window.FindName("BootOverlay")).Visibility = Visibility.Collapsed;
        ((UIElement)window.FindName("MainHUD")).Opacity = 1;
        var border = (Border)window.FindName("MainBorder"); border.Width = 888; border.Height = 466;
        var hud = Detach(window); RenderSurface(hud, "workspaces", 920, 490);
        Check(Descendants(hud).OfType<Button>().Count(b => b.Tag is ModeConfig) == 3, "three workspace cards render with target chips and accessible launch buttons");

        var menu = (ContextMenu)Call(window, "MakeWorkspaceMenu", modes[0])!;
        RenderSurface(menu, "menu", 310, 310);
        var windows = menu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "Windows");
        Check(windows.Items.Count == 5 && menu.Items.OfType<MenuItem>().First().InputGestureText == "F2" && !menu.IsOpen,
            "workspace menu groups lifecycle operations and retains keyboard hints without opening a popup");

        var setup = new SetupWindow(service); var setupContent = Detach(setup);
        foreach (var step in new[] { 0, 1, 2, 3, 4, 5 })
        { Call(setup, "ShowStep", step); RenderSurface(setupContent, "setup-" + step, 740, 600); }
        var settings = new ThemeWindow(new(), new(), (_, _) => { }, (_, _) => Task.CompletedTask);
        RenderSurface(Detach(settings), "appearance", 660, 760);
        var dialog = WindowSurface.CreateDialog(null, "Review configured commands", "Example.exe \"one argument\" \"\"\n\nWorking directory: C:\\Projects", "Run reviewed commands");
        var dialogContent = Detach(dialog); RenderSurface(dialogContent, "dialog", 620, 460);
        Check(Descendants(dialogContent).OfType<Button>().Single(b => b.IsDefault).IsCancel,
            "review dialog keeps Cancel as safe default and selectable command details");

        window.ApplyTheme(new() { ReducedEffects = true, FontFamily = "JetBrainsMono NF", CornerRadius = 0 });
        Check(((SolidColorBrush)window.FindResource("SurfaceBrush")).Color.A == 255 && Equals(window.FindResource("GlassReflection"), Brushes.Transparent) &&
            ((CornerRadius)window.FindResource("SurfaceRadius")).TopLeft == 0 && ((FontFamily)window.FindResource("UIFont")).Source.Contains("JetBrains"),
            "reduced effects use solid surfaces while preserving saved font and corner preferences");
        var buttonStyle = (Style)window.FindResource(typeof(Button));
        var buttonTemplate = (ControlTemplate)buttonStyle.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
        Check(buttonTemplate.Triggers.OfType<Trigger>().SelectMany(t => t.EnterActions.Concat(t.ExitActions)).OfType<System.Windows.Media.Animation.BeginStoryboard>()
            .SelectMany(b => b.Storyboard.Children.OfType<System.Windows.Media.Animation.DoubleAnimation>())
            .Where(a => System.Windows.Media.Animation.Storyboard.GetTargetProperty(a)?.Path.Contains("ScaleTransform") == true).All(a => a.To == 1),
            "reduced effects remove control scale motion while keeping state feedback");
        Check(new[] { (Window)window, setup, settings, dialog }.All(w => !w.IsVisible && new WindowInteropHelper(w).Handle == 0),
            "HUD, setup, settings and dialogs render without any launcher or editor HWND");
        setup.Close(); settings.Close(); dialog.Close();
    }
}
