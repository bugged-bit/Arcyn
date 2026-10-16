using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ARCYN.UI.Models;
using ARCYN.UI.Services;
namespace ARCYN.UI;

public partial class MainWindow
{
    private ThemeConfig _currentTheme = new();
    private bool _runtimeStarted;
    private void EditTheme()
    {
        if (_state.Phase != AppPhase.Ready || _isLaunching) return;
        _editing = true;
        try
        {
            var editor = new ThemeWindow(_config.Theme, _config.Behavior,
                (theme, behavior) => { ApplyTheme(theme); Topmost = behavior.AlwaysOnTop; },
                async (theme, behavior) => {
                    var before = (_config.Theme, _config.Behavior);
                    _config.Theme = theme; _config.Behavior = behavior;
                    try { await _configService.SaveAsync(_config); }
                    catch { (_config.Theme, _config.Behavior) = before; throw; }
                }) { Owner = this };
            editor.ShowDialog();
        }
        finally { ApplyTheme(_config.Theme); Topmost = _config.Behavior.AlwaysOnTop; _editing = false; ResetIdle(); }
    }
    internal void ApplyTheme(ThemeConfig theme)
    {
        ThemeService.Apply(this, theme);
        _currentTheme = ThemeService.Clone(theme);
        Width = Math.Min(theme.Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(theme.Height, SystemParameters.WorkArea.Height);
        MainBorder.CornerRadius = new CornerRadius(theme.CornerRadius);
        MainBorder.BorderThickness = new Thickness(theme.BorderWidth);
        ScanlineLayer.Visibility = theme.Scanlines && !theme.ReducedEffects ? Visibility.Visible : Visibility.Collapsed;
        VignetteLayer.Visibility = theme.Vignette ? Visibility.Visible : Visibility.Collapsed;
        if (!theme.Animations || theme.ReducedEffects) AmbientGlow.Opacity = theme.ReducedEffects ? 0 : .26 * theme.GlowOpacity / .28;
        if (_state.Phase != AppPhase.Boot)
        {
            MainBorder.BeginAnimation(WidthProperty, null); MainBorder.BeginAnimation(HeightProperty, null);
            MainBorder.Width = Math.Max(1, Width - 32); MainBorder.Height = Math.Max(1, Height - 24);
        }
        _accentBrightBrush = (Brush)FindResource("AccentBrightBrush");
        _accentLightBrush = (Brush)FindResource("AccentLightBrush");
        _textPrimaryBrush = (Brush)FindResource("TextPrimaryBrush");
        foreach (var particle in _cursorTrail) particle.Element.Fill = _accentBrightBrush;
        _particles?.Configure(theme.ReducedEffects ? 0 : theme.Particles, ThemeService.Map(Color.FromRgb(0xE8, 0x60, 0x60), theme));
        if (!theme.CursorTrail || theme.ReducedEffects) { TrailCanvas.Children.Clear(); _cursorTrail.Clear(); }
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            try
            {
                var bg = ThemeService.ParseColor(theme.Background);
                var acrylicColor = 0xE2000000u | (uint)bg.B << 16 | (uint)bg.G << 8 | bg.R;
                NativeMethods.EnableAcrylic(handle, acrylicColor, theme.Acrylic && !theme.ReducedEffects);
            }
            catch (Exception ex) { _log.Write("Acrylic: {0}", ex.Message); }
        if (_runtimeStarted || _state.Phase == AppPhase.Ready) BuildDashboard();
        RefreshActivity();
    }
    private void RefreshActivity()
    {
        if (!_runtimeStarted || _disposed) return;
        var visible = IsVisible && WindowState != WindowState.Minimized && _state.Phase != AppPhase.Closing;
        if (visible) _idleTimer?.Start(); else _idleTimer?.Stop();
        var animate = visible && (_state.Phase is AppPhase.Boot or AppPhase.Selecting or AppPhase.Launching ||
            _currentTheme.Animations && !_currentTheme.ReducedEffects &&
            (_currentTheme.Particles > 0 || _currentTheme.CursorTrail || _currentTheme.GlowOpacity > 0));
        if (animate) _render.Start(); else _render.Stop();
        if (!visible) _launchDotTimer?.Stop();
        else if (_state.Phase == AppPhase.Launching) _launchDotTimer?.Start();
    }
}
