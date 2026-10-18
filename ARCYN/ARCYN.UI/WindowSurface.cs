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
        return ThemeService.ShellOpacity(ThemeService.Background(theme), material) == 255 ? MaterialMode.Opaque : material;
    }
    internal static MaterialMode InitializedMaterial(MaterialMode requested, bool succeeded) => succeeded ? requested : MaterialMode.Opaque;

    internal static void Attach(Window window, ThemeConfig theme, SurfaceRole role)
    {
        if (window.Resources[StateKey] is State) return;
        var state = new State(window, theme, role); window.Resources[StateKey] = state;
        window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
        window.SetResourceReference(Window.ForegroundProperty, "TextPrimaryBrush");
        window.SetResourceReference(Window.FontFamilyProperty, "UIFont");
        window.FontSize = Math.Max(12, 14 * theme.FontScale);
        if (role == SurfaceRole.Launcher) WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new(0),
            ResizeBorderThickness = new(window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize ? 0 : 6),
            CornerRadius = new(8), UseAeroCaptionButtons = false });
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
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.BorderThickness = new(0);
        window.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
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
                if (chrome != null) chrome.GlassFrameThickness = new(material == MaterialMode.Opaque ? 0 : -1);
                var dark = ThemeService.IsDark(theme);
                material = InitializedMaterial(material, NativeMethods.SetBackdrop(handle, (int)material, dark));
                if (material == MaterialMode.Opaque) { if (chrome != null) chrome.GlassFrameThickness = new(0); NativeMethods.SetBackdrop(handle, 1, dark); }
                NativeMethods.RoundWindow(handle, true);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            { material = MaterialMode.Opaque; if (WindowChrome.GetWindowChrome(window) is { } chrome) chrome.GlassFrameThickness = new(0); LogService.WriteStatic("Material fallback: {0}", ex.Message); }
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
        var content = new DockPanel { Margin = new(24) };
        var heading = new TextBlock { Text = title }; heading.SetResourceReference(FrameworkElement.StyleProperty, "PageTitle"); DockPanel.SetDock(heading, Dock.Top); content.Children.Add(heading);
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
    internal static Window CreatePicker(Window? owner, ThemeConfig theme, string title, ListBox list, Action select)
    {
        var window = new Window { Title = title + " — ARCYN", Owner = owner, Width = 650, Height = 500, MinWidth = 400, MinHeight = 300,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(24) };
        var heading = new TextBlock { Text = title }; heading.SetResourceReference(FrameworkElement.StyleProperty, "PageTitle");
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0,16,0,0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var accept = new Button { Content = "Select", IsDefault = true, IsEnabled = list.SelectedIndex >= 0 };
        accept.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryActionButton");
        void Choose() { if (list.SelectedIndex < 0) return; select(); window.DialogResult = true; }
        accept.Click += (_, _) => Choose(); list.SelectionChanged += (_, _) => accept.IsEnabled = list.SelectedIndex >= 0;
        list.MouseDoubleClick += (_, e) => { if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) != null) Choose(); };
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Choose(); e.Handled = true; } };
        footer.Children.Add(cancel); footer.Children.Add(accept); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        list.Margin = new(0); root.Children.Add(list); window.Content = root; Prepare(window, theme); return window;
    }
    public static bool Show(Window? owner, string title, string message, string? confirm = null) => CreateDialog(owner, title, message, confirm).ShowDialog() == true;
}
