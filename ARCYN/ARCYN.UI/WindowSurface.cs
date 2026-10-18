using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ARCYN.UI.Models;
using ARCYN.UI.Services;

namespace ARCYN.UI;

internal enum SurfaceRole { Launcher, Workspace, Dialog }
internal enum MaterialMode { Opaque = 1, Mica = 2, Acrylic = 3 }

internal static class WindowSurface
{
    private static readonly object StateKey = new();
    private sealed class State(Window window, ThemeConfig theme, SurfaceRole role)
    {
        internal ThemeConfig Theme = theme;
        internal readonly SurfaceRole Role = role;
        internal readonly double MinimumWidth = window.MinWidth, MinimumHeight = window.MinHeight;
        internal HwndSource? Source;
        internal PropertyChangedEventHandler? SettingsChanged;
        internal HwndSourceHook? Hook;
        internal bool Closed, RefreshPending;
        internal MaterialMode Material;
    }

    internal static MaterialMode PreferredMaterial(ThemeConfig theme, SurfaceRole role, bool highContrast, bool supported)
    {
        if (!supported || highContrast || theme.ReducedEffects || !theme.Acrylic) return MaterialMode.Opaque;
        var material = role == SurfaceRole.Workspace ? MaterialMode.Mica : MaterialMode.Acrylic;
        return ThemeService.ShellOpacity(ThemeService.ParseColor(theme.Background), material) == 255 ? MaterialMode.Opaque : material;
    }
    internal static MaterialMode InitializedMaterial(MaterialMode requested, bool succeeded) => succeeded ? requested : MaterialMode.Opaque;

    internal static void Attach(Window window, ThemeConfig theme, SurfaceRole role)
    {
        if (window.Resources[StateKey] is State) return;
        var state = new State(window, theme, role); window.Resources[StateKey] = state;
        window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextPrimaryBrush");
        window.SetResourceReference(Window.FontFamilyProperty, "UIFont");
        window.FontSize = 13 * theme.FontScale;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new(0),
            ResizeBorderThickness = new(window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize ? 0 : 6),
            CornerRadius = new(theme.CornerRadius), UseAeroCaptionButtons = false });
        void QueueRefresh()
        {
            if (state.Closed || state.RefreshPending || window.Dispatcher.HasShutdownStarted) return;
            state.RefreshPending = true;
            window.Dispatcher.BeginInvoke(() => {
                state.RefreshPending = false; if (state.Closed) return;
                if (window is MainWindow main) main.RefreshSystemTheme(); else if (window is SetupWindow setup) setup.RefreshSystemTheme(); else ThemeService.Apply(window, state.Theme);
                RefreshBackdrop(window, state.Theme); FitWindow(window);
            });
        }
        window.SourceInitialized += (_, _) => {
            window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/ARCYN;component/Assets/ARCYN.ico")); // Decode only when a native window needs its icon.
            state.Source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            state.SettingsChanged = (_, _) => QueueRefresh();
            SystemParameters.StaticPropertyChanged += state.SettingsChanged;
            state.Hook = (nint hwnd, int message, nint wparam, nint lparam, ref bool handled) => {
                if (message is 0x001A or 0x007E or 0x0218 or 0x031E or 0x0320) QueueRefresh();
                return 0;
            };
            state.Source?.AddHook(state.Hook);
            RefreshBackdrop(window, state.Theme); FitWindow(window);
        };
        window.Activated += (_, _) => RefreshBackdrop(window, state.Theme);
        window.DpiChanged += (_, _) => { RefreshBackdrop(window, state.Theme); FitWindow(window); };
        window.Closed += (_, _) => {
            state.Closed = true;
            if (state.SettingsChanged != null) SystemParameters.StaticPropertyChanged -= state.SettingsChanged;
            if (state.Hook != null) state.Source?.RemoveHook(state.Hook);
            state.Source = null; state.Hook = null; state.SettingsChanged = null;
        };
    }

    public static void Prepare(Window window, ThemeConfig theme, SurfaceRole role = SurfaceRole.Workspace)
    {
        ThemeService.Apply(window, theme); Attach(window, theme, role);
        window.WindowStyle = WindowStyle.None;
        if (window.Content is not UIElement content) return;
        window.Content = null;
        var root = new Grid { Margin = new(7) };
        var shell = new Border { BorderThickness = new(1) };
        shell.SetResourceReference(Border.BackgroundProperty, "ShellBrush");
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
        caption.SetResourceReference(TextBlock.FontSizeProperty, "FontSize14");
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) window.DragMove(); };
        header.Children.Add(caption);
        var close = new Button { Content = "×", Width = 34, Height = 34, Padding = new(0), Margin = new(8, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close " + caption.Text);
        close.Click += (_, _) => window.Close(); Grid.SetColumn(close, 1); header.Children.Add(close);
        body.Children.Add(header); Grid.SetRow(content, 1); body.Children.Add(content); window.Content = root;
    }

    public static void RefreshBackdrop(Window window, ThemeConfig theme)
    {
        if (window.Resources[StateKey] is not State state || state.Closed) return;
        state.Theme = theme;
        var handle = new WindowInteropHelper(window).Handle;
        var material = PreferredMaterial(theme, state.Role, SystemParameters.HighContrast, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621));
        if (handle == 0) material = MaterialMode.Opaque;
        else
        {
            try
            {
                var chrome = WindowChrome.GetWindowChrome(window);
                chrome.GlassFrameThickness = new(material == MaterialMode.Opaque ? 0 : -1);
                var dark = ThemeService.Contrast(ThemeService.ParseColor(theme.Background), Colors.White) > ThemeService.Contrast(ThemeService.ParseColor(theme.Background), Colors.Black);
                material = InitializedMaterial(material, NativeMethods.SetBackdrop(handle, (int)material, dark));
                if (material == MaterialMode.Opaque) { chrome.GlassFrameThickness = new(0); NativeMethods.SetBackdrop(handle, 1, dark); }
                NativeMethods.RoundWindow(handle, theme.CornerRadius > 0);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            { material = MaterialMode.Opaque; WindowChrome.GetWindowChrome(window).GlassFrameThickness = new(0); LogService.WriteStatic("Material fallback: {0}", ex.Message); }
        }
        state.Material = material;
        ThemeService.ApplySurfaces(window, theme, material, SystemParameters.HighContrast);
        if (material == MaterialMode.Opaque) window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        else window.Background = Brushes.Transparent;
        if (state.Source?.CompositionTarget is { } composition)
            composition.BackgroundColor = material == MaterialMode.Opaque ? ((SolidColorBrush)window.FindResource("WindowBrush")).Color : Colors.Transparent;
    }

    internal static Size FitSize(Size requested, Size workArea) => new(Math.Min(requested.Width, Math.Max(1, workArea.Width - 16)), Math.Min(requested.Height, Math.Max(1, workArea.Height - 16)));
    private static void FitWindow(Window window)
    {
        if (window.Resources[StateKey] is not State state || state.Source == null) return;
        var handle = new WindowInteropHelper(window.Owner ?? window).Handle;
        if (handle == 0 || WindowsWindowSystem.WorkAreaFor(handle) is not { } area) return;
        var dpi = VisualTreeHelper.GetDpi(window);
        var available = new Size(area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
        var desired = window is MainWindow main ? new Size(main.CurrentTheme.Width, main.CurrentTheme.Height) : new Size(window.Width, window.Height);
        var fit = FitSize(desired, available);
        window.MinWidth = Math.Min(state.MinimumWidth, fit.Width); window.MinHeight = Math.Min(state.MinimumHeight, fit.Height);
        window.MaxWidth = Math.Max(1, available.Width - 16); window.MaxHeight = Math.Max(1, available.Height - 16);
        window.Width = fit.Width; window.Height = fit.Height;
        WindowsWindowSystem.ConstrainToWorkArea(new WindowInteropHelper(window).Handle, area);
    }

    internal static Window CreateDialog(Window? owner, string title, string message, string? confirm = null)
    {
        var window = new Window { Title = "ARCYN — " + title, Owner = owner, Width = 620, Height = 460, MinWidth = 360, MinHeight = 260,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner, ShowInTaskbar = owner == null };
        var content = new DockPanel { Margin = new(24, 14, 24, 24) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        var cancel = new Button { Content = confirm == null ? "Close" : "Cancel", IsCancel = true, IsDefault = true };
        cancel.Click += (_, _) => window.DialogResult = false; buttons.Children.Add(cancel);
        if (confirm != null)
        {
            var accept = new Button { Content = confirm }; accept.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryActionButton");
            accept.Click += (_, _) => window.DialogResult = true; buttons.Children.Add(accept);
        }
        DockPanel.SetDock(buttons, Dock.Bottom); content.Children.Add(buttons);
        content.Children.Add(new TextBox { Text = message, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0), BorderThickness = new(0) });
        window.Content = content;
        Prepare(window, owner is MainWindow main ? main.CurrentTheme : new ThemeConfig(), SurfaceRole.Dialog);
        return window;
    }
    public static bool Show(Window? owner, string title, string message, string? confirm = null) => CreateDialog(owner, title, message, confirm).ShowDialog() == true;
}
