using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

namespace ARCYN.UI;

internal static class WindowSurface
{
    public static void Prepare(Window window, ThemeConfig theme)
    {
        ThemeService.Apply(window, theme);
        window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextPrimaryBrush");
        window.SetResourceReference(Window.FontFamilyProperty, "UIFont");
        window.FontSize = 13 * theme.FontScale;
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new(0), ResizeBorderThickness = new(6), CornerRadius = new(theme.CornerRadius), UseAeroCaptionButtons = false });
        if (window.Content is not UIElement content) return;
        window.Content = null;
        var root = new Grid { Margin = new(7) };
        var shell = new Border { BorderThickness = new(1) };
        shell.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        shell.SetResourceReference(Border.BorderBrushProperty, "SurfaceLineBrush");
        shell.SetResourceReference(Border.CornerRadiusProperty, "SurfaceRadius");
        var body = new Grid(); shell.Child = body;
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new());
        var reflection = new Border { IsHitTestVisible = false };
        reflection.SetResourceReference(Border.BackgroundProperty, "GlassReflection"); reflection.SetResourceReference(Border.CornerRadiusProperty, "SurfaceRadius");
        root.Children.Add(shell); root.Children.Add(reflection);
        var header = new Grid { Margin = new(22, 14, 14, 0), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var caption = new TextBlock { Text = window.Title.Replace("ARCYN — ", ""), FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) window.DragMove(); };
        header.Children.Add(caption);
        var close = new Button { Content = "×", Width = 34, Height = 34, Padding = new(0), Margin = new(8, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close " + caption.Text);
        close.Click += (_, _) => window.Close(); Grid.SetColumn(close, 1); header.Children.Add(close);
        body.Children.Add(header); Grid.SetRow(content, 1); body.Children.Add(content); window.Content = root;
        window.SourceInitialized += (_, _) => RefreshBackdrop(window, theme);
    }
    public static void RefreshBackdrop(Window window, ThemeConfig theme)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        NativeMethods.EnableAcrylic(handle, theme.Acrylic && !theme.ReducedEffects);
        NativeMethods.RoundWindow(handle, theme.CornerRadius > 0);
    }

    internal static Window CreateDialog(Window? owner, string title, string message, string? confirm = null)
    {
        var window = new Window { Title = "ARCYN — " + title, Owner = owner, Width = 620, Height = 460, MinWidth = 400, MinHeight = 300,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner, ShowInTaskbar = owner == null };
        var content = new DockPanel { Margin = new(24, 14, 24, 24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        var cancel = new Button { Content = confirm == null ? "Close" : "Cancel", IsCancel = true, IsDefault = true };
        cancel.Click += (_, _) => window.DialogResult = false; buttons.Children.Add(cancel);
        if (confirm != null)
        {
            var accept = new Button { Content = confirm, Style = (Style)Application.Current.FindResource("PrimaryActionButton") };
            accept.Click += (_, _) => window.DialogResult = true; buttons.Children.Add(accept);
        }
        DockPanel.SetDock(buttons, Dock.Bottom); content.Children.Add(buttons);
        content.Children.Add(new TextBox { Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0), BorderThickness = new(0) });
        window.Content = content;
        Prepare(window, owner is MainWindow main ? main.CurrentTheme : new ThemeConfig());
        return window;
    }
    public static bool Show(Window? owner, string title, string message, string? confirm = null) => CreateDialog(owner, title, message, confirm).ShowDialog() == true;
}
