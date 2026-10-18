using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ARCYN.UI.Models;
namespace ARCYN.UI.Services;

public sealed class ThemeService
{
    public static void ValidateColor(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$"))
            throw new ArgumentException("Use a hexadecimal colour: #RRGGBB or #AARRGGBB.");
    }
    public static Color ParseColor(string hex) { ValidateColor(hex); return (Color)ColorConverter.ConvertFromString(hex); }
    public static SolidColorBrush HexToBrush(string hex) { var b = new SolidColorBrush(ParseColor(hex)); b.Freeze(); return b; }
    public static ThemeConfig Clone(ThemeConfig theme) => JsonSerializer.Deserialize<ThemeConfig>(JsonSerializer.Serialize(theme, ConfigService.JsonOptions), ConfigService.JsonOptions)!;
    public static void Validate(ThemeConfig t)
    {
        ValidateColor(t.Accent); ValidateColor(t.Background); ValidateColor(t.Text);
        if (t.ColorMode is not ("system" or "light" or "dark" or "custom")) throw new ArgumentException("Appearance: system, light, dark or custom.");
        static void Range(double n, double min, double max, string field) { if (!double.IsFinite(n) || n < min || n > max) throw new ArgumentException($"{field}: use {min}–{max}."); }
        Range(t.FontScale, .7, 1.8, "Font scale"); Range(t.Width, 640, 1920, "Width"); Range(t.Height, 360, 1080, "Height");
        Range(t.CornerRadius, 0, 30, "Corners"); Range(t.BorderWidth, 0, 4, "Border width");
        Range(t.CardHeight, 64, 160, "Card height"); Range(t.Spacing, 0, 24, "Spacing");
        Range(t.Particles, 0, 120, "Particles"); Range(t.GlowOpacity, 0, 1, "Glow opacity"); Range(t.MotionAmount, 0, 2, "Motion amount");
        if (t.Layout is not ("rows" or "compact" or "grid")) throw new ArgumentException("Layout: rows, compact or grid.");
        if (string.IsNullOrWhiteSpace(t.FontFamily) || t.FontFamily.Length > 100 || t.FontFamily.IndexOfAny(['/', ':', '\\']) >= 0)
            throw new ArgumentException("Use an installed font family name. External font resources are unsupported.");
    }
    public static ThemeConfig Import(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("Theme exceeds 64 KiB.");
        var json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        if (IsConfiguration(document.RootElement)) throw new InvalidDataException("Import a visual theme, rather than a workspace configuration.");
        var theme = JsonSerializer.Deserialize<ThemeConfig>(json, ConfigService.JsonOptions) ?? throw new InvalidDataException("Empty theme.");
        Validate(theme); return theme;
    }
    public static void Export(string path, ThemeConfig theme)
    {
        Validate(theme);
        if (Path.GetFileName(path).Equals("arcyn.json", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("protocoll.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a theme filename; arcyn.json and protocoll.json are reserved for workspace configurations.");
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (IsConfiguration(document.RootElement)) throw new InvalidDataException("Theme export cannot overwrite a workspace configuration.");
        }
        var json = JsonSerializer.Serialize(theme, ConfigService.JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024) throw new InvalidDataException("Theme exceeds 64 KiB.");
        File.WriteAllText(path, json);
    }
    private static bool IsConfiguration(JsonElement root) => root.ValueKind != JsonValueKind.Object ||
        root.EnumerateObject().Any(p => p.Name.ToLowerInvariant() is "modes" or "theme" or "behavior" or "schema_version");

    public static Color Map(Color color, ThemeConfig theme)
    {
        var accent = ParseColor(theme.Accent);
        static byte Channel(int n) => (byte)Math.Clamp(n, 0, 255);
        if (color.R > color.G * 1.2 && Math.Abs(color.G - color.B) < 8)
            return Color.FromArgb(color.A, Channel(accent.R + color.R - 214), Channel(accent.G + color.G - 69), Channel(accent.B + color.B - 69));
        if (color.R == color.G && color.G == color.B)
        {
            if (color.R >= 170) { var text = ParseColor(theme.Text); return Color.FromArgb(color.A, text.R, text.G, text.B); }
            if (color.R > 0 && color.R <= 10) { var bg = ParseColor(theme.Background); var delta = color.R - 10; return Color.FromArgb(color.A, Channel(bg.R + delta), Channel(bg.G + delta), Channel(bg.B + delta)); }
        }
        return color;
    }
    private static readonly Dictionary<string, ResourceDictionary> FluentThemes = [];
    private static ResourceDictionary? _baseResources;
    private static ResourceDictionary BaseResources => _baseResources ??= Application.Current?.Resources.MergedDictionaries.FirstOrDefault(
        dictionary => dictionary.Source?.OriginalString.EndsWith("Styles/Theme.xaml", StringComparison.OrdinalIgnoreCase) == true)
        ?? new() { Source = new Uri("/ARCYN;component/Styles/Theme.xaml", UriKind.Relative) };
    private static readonly object PaletteKey = new();

    internal static bool SystemUsesLightTheme()
    {
        try { using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is not int value || value != 0; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return true; }
    }
    internal static bool IsDark(ThemeConfig theme, bool? systemLight = null) => theme.ColorMode switch {
        "dark" => true, "light" => false, "custom" => Contrast(ParseColor(theme.Background), Colors.White) > Contrast(ParseColor(theme.Background), Colors.Black),
        _ => !(systemLight ?? SystemUsesLightTheme()) };
    internal static Color Background(ThemeConfig theme) => theme.ColorMode == "custom" ? ParseColor(theme.Background) : IsDark(theme) ? Color.FromRgb(32,32,32) : Color.FromRgb(243,243,243);

    public static void Apply(FrameworkElement target, ThemeConfig theme, bool? highContrast = null)
    {
        Validate(theme);
        var mode = (highContrast ?? SystemParameters.HighContrast) ? "HC" : IsDark(theme) ? "Dark" : "Light";
        if (!FluentThemes.TryGetValue(mode, out var fluent))
            FluentThemes[mode] = fluent = mode == "Light" ? BaseResources.MergedDictionaries[0].MergedDictionaries[0]
                : new() { Source = new Uri($"/PresentationFramework.Fluent;component/Themes/Fluent.{mode}.xaml", UriKind.Relative) };
        if (target.Resources[PaletteKey] is ResourceDictionary previous) target.Resources.MergedDictionaries.Remove(previous);
        target.Resources.MergedDictionaries.Add(fluent); target.Resources[PaletteKey] = fluent;
        foreach (var key in BaseResources.Keys.Cast<object>())
        {
            var value = BaseResources[key];
            if (value is Color color) target.Resources[key] = Map(color, theme);
            else if (value is SolidColorBrush brush) { var mapped = new SolidColorBrush(Map(brush.Color, theme)); mapped.Freeze(); target.Resources[key] = mapped; }
            else if (key is string name && name.StartsWith("FontSize") && value is double size) target.Resources[key] = Math.Max(12, size * theme.FontScale);
            else if (value is DropShadowEffect) target.Resources[key] = null;
        }
        // Keep the few sizing styles; all interaction templates remain the platform's.
        foreach (var key in new object[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(CheckBox), typeof(RadioButton), typeof(Expander), typeof(DataGrid), typeof(ListBox), typeof(TabControl), "PrimaryActionButton", "SubtleButton", "WorkstationPanelStyle" })
            target.Resources[key] = BaseResources[key];
        target.Resources["UIFont"] = theme.ColorMode == "custom" ? theme.FontFamily == "JetBrainsMono NF" ? BaseResources["ReadoutFont"] : new FontFamily(theme.FontFamily) : new FontFamily("Segoe UI Variable Text, Segoe UI");
        target.Resources["ControlContentThemeFontSize"] = Math.Max(12, 14 * theme.FontScale);
        target.Resources["ContentControlFontSize"] = Math.Max(12, 14 * theme.FontScale);
        target.Resources["WorkspaceDescriptionVisibility"] = theme.CompactMode || theme.Layout == "compact" ? Visibility.Collapsed : Visibility.Visible;
        target.Resources["CardHeight"] = theme.CompactMode || theme.Layout == "compact" ? Math.Min(theme.CardHeight, 72) : theme.CardHeight;
        target.Resources["SurfaceRadius"] = new CornerRadius(theme.ColorMode == "custom" ? theme.CornerRadius : 8);
        target.Resources["CardRadius"] = new CornerRadius(8); target.Resources["ControlRadius"] = new CornerRadius(4);
        target.Resources["GlassReflection"] = Brushes.Transparent;
        if (target is Window window) window.FontSize = Math.Max(12, 14 * theme.FontScale);
        ApplySurfaces(target, theme, MaterialMode.Opaque, highContrast ?? SystemParameters.HighContrast);
    }

    internal static double Contrast(Color a, Color b)
    {
        static double L(Color c) { static double C(byte n) { var v = n / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); } return .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B); }
        var x = L(a); var y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    internal static Color Blend(Color background, Color foreground, double amount) => Color.FromRgb(
        (byte)Math.Round(background.R + (foreground.R - background.R) * amount),
        (byte)Math.Round(background.G + (foreground.G - background.G) * amount),
        (byte)Math.Round(background.B + (foreground.B - background.B) * amount));

    internal static byte ShellOpacity(Color background, MaterialMode material)
    {
        var alpha = material == MaterialMode.Opaque ? (byte)255 : material == MaterialMode.Mica ? (byte)215 : (byte)190;
        var shell = Blend(background, Colors.White, .025);
        var dark = Blend(Colors.Black, shell, alpha / 255d); var light = Blend(Colors.White, shell, alpha / 255d);
        return Math.Max(Math.Min(Contrast(Colors.Black, dark), Contrast(Colors.Black, light)), Math.Min(Contrast(Colors.White, dark), Contrast(Colors.White, light))) < 4.5 ? (byte)255 : alpha;
    }

    internal static void ApplySurfaces(FrameworkElement target, ThemeConfig theme, MaterialMode material, bool highContrast)
    {
        var background = highContrast ? SystemColors.WindowColor : Background(theme);
        var dark = IsDark(theme);
        var text = highContrast ? SystemColors.WindowTextColor : theme.ColorMode == "custom" ? ParseColor(theme.Text) : dark ? Colors.White : Color.FromRgb(26,26,26);
        var accent = highContrast ? SystemColors.HighlightColor : theme.SystemAccent ? SystemColors.AccentColor : ParseColor(theme.Accent);
        var shellAlpha = highContrast ? (byte)255 : ShellOpacity(background, material);
        var shell = background;
        var surface = highContrast ? background : Blend(background, Colors.White, dark ? .035 : .7);
        double Legibility(Color color) => Math.Min(Math.Min(Contrast(color, background), Contrast(color, surface)), Math.Min(Contrast(color, Blend(Colors.Black, shell, shellAlpha / 255d)), Contrast(color, Blend(Colors.White, shell, shellAlpha / 255d))));
        if (!highContrast && Math.Max(Legibility(Colors.Black), Legibility(Colors.White)) < 4.5) { shellAlpha = 255; surface = background; }
        Color Readable(Color desired)
        {
            var best = Legibility(Colors.Black) > Legibility(Colors.White) ? Colors.Black : Colors.White;
            for (var i = 0; i <= 10; i++) { var candidate = Blend(desired, best, i / 10d); if (Legibility(candidate) >= 4.5) return candidate; }
            return best;
        }
        text = highContrast ? text : Readable(text);
        SolidColorBrush Brush(Color color, byte alpha = 255) { var brush = new SolidColorBrush(Color.FromArgb(alpha,color.R,color.G,color.B)); brush.Freeze(); return brush; }
        var selection = highContrast ? accent : Blend(background, accent, dark ? .22 : .12);
        target.Resources["WindowBrush"] = Brush(background); target.Resources["ShellBrush"] = Brush(shell, shellAlpha);
        target.Resources["SurfaceBrush"] = Brush(surface); target.Resources["RaisedSurfaceBrush"] = Brush(surface);
        target.Resources["FieldBrush"] = Brush(highContrast ? background : dark ? Color.FromRgb(45,45,45) : Colors.White);
        target.Resources["SurfaceLineBrush"] = Brush(text, highContrast ? (byte)255 : (byte)18);
        target.Resources["HighlightBrush"] = Brush(text, 12);
        target.Resources["AccentSurfaceBrush"] = Brush(selection);
        target.Resources["TextPrimaryBrush"] = target.Resources["TextBrightBrush"] = target.Resources["TextSecondaryBrush"] = Brush(text);
        var secondary = highContrast ? text : Readable(Blend(background, text, .72));
        target.Resources["TextDimBrush"] = target.Resources["TextMutedBrush"] = Brush(secondary);
        target.Resources["AccentBrush"] = Brush(accent); target.Resources["AccentBrightBrush"] = Brush(highContrast ? text : Readable(accent));
        target.Resources["AccentLightBrush"] = Brush(highContrast ? text : Readable(accent));
        target.Resources["SelectionTextBrush"] = Brush(highContrast ? SystemColors.HighlightTextColor : Contrast(text, selection) >= 4.5 ? text : Contrast(Colors.Black, selection) > Contrast(Colors.White, selection) ? Colors.Black : Colors.White);
        if (!theme.SystemAccent && !highContrast)
        {
            var onAccent = Contrast(Colors.Black, accent) > Contrast(Colors.White, accent) ? Colors.Black : Colors.White;
            foreach (var key in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed" }) target.Resources[key] = Brush(accent);
            foreach (var key in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" }) target.Resources[key] = Brush(onAccent);
        }
        else foreach (var key in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed", "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" }) target.Resources.Remove(key);
        target.Resources["GlassReflection"] = Brushes.Transparent;
    }
}
