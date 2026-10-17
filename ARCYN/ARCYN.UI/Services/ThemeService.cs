using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    public static void Apply(FrameworkElement target, ThemeConfig theme)
    {
        Validate(theme);
        // One palette rebuild per edit, never per frame. Fresh templates keep hover/focus colours coherent.
        var source = new ResourceDictionary { Source = new Uri("/ARCYN;component/Styles/Theme.xaml", UriKind.Relative) };
        foreach (var key in source.Keys.Cast<object>().ToArray())
        {
            var value = source[key];
            if (value is Color c) target.Resources[key] = Map(c, theme);
            else if (value is SolidColorBrush brush) { var b = new SolidColorBrush(Map(brush.Color, theme)); b.Freeze(); target.Resources[key] = b; }
            else if (value is DropShadowEffect shadow)
            {
                if (theme.ReducedEffects || theme.GlowOpacity == 0) { target.Resources[key] = null; continue; }
                var effect = shadow.Clone(); effect.Color = Map(effect.Color, theme);
                effect.Opacity *= theme.GlowOpacity / .28;
                effect.Freeze(); target.Resources[key] = effect;
            }
            else if (key is string name && name.StartsWith("FontSize") && value is double size) target.Resources[key] = size * theme.FontScale;
        }
        target.Resources["UIFont"] = theme.FontFamily == "JetBrainsMono NF" ? source["ReadoutFont"] : new FontFamily(theme.FontFamily);
        var background = ParseColor(theme.Background); var text = ParseColor(theme.Text); var accent = ParseColor(theme.Accent);
        SolidColorBrush Surface(double lift, byte alpha = 255)
        {
            byte Channel(byte value) => (byte)Math.Round(value + (255 - value) * lift);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, Channel(background.R), Channel(background.G), Channel(background.B))); brush.Freeze(); return brush;
        }
        SolidColorBrush Tint(Color color, byte alpha) { var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)); brush.Freeze(); return brush; }
        target.Resources["WindowBrush"] = Tint(background, 255);
        target.Resources["SurfaceBrush"] = Surface(.035, theme.ReducedEffects || SystemParameters.HighContrast ? (byte)255 : (byte)245);
        target.Resources["RaisedSurfaceBrush"] = Surface(.085);
        target.Resources["FieldBrush"] = Surface(.015, 235);
        target.Resources["SurfaceLineBrush"] = Tint(text, 35);
        target.Resources["AccentSurfaceBrush"] = Tint(accent, 45);
        target.Resources["TextDimBrush"] = Tint(text, 220);
        target.Resources["TextMutedBrush"] = Tint(text, 180);
        target.Resources["SurfaceRadius"] = new CornerRadius(theme.CornerRadius);
        target.Resources["CardRadius"] = new CornerRadius(theme.CornerRadius * .8);
        target.Resources["ControlRadius"] = new CornerRadius(theme.CornerRadius * .5);
        target.Resources["GlassReflection"] = theme.ReducedEffects || SystemParameters.HighContrast ? Brushes.Transparent : source["GlassReflection"];
        target.Resources["CardHeight"] = theme.CompactMode || theme.Layout == "compact" ? Math.Min(theme.CardHeight, 72) : theme.CardHeight;
        // Templates are freshly loaded and have not yet been applied/sealed.
        var style = (Style)source["WorkstationPanelStyle"];
        foreach (var setter in style.Setters.OfType<Setter>())
            if (setter.Value is SolidColorBrush brush)
            { var mapped = new SolidColorBrush(Map(brush.Color, theme)); mapped.Freeze(); setter.Value = mapped; }
        void PrepareMotion(Style controlStyle)
        {
            var template = (ControlTemplate)controlStyle.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
            foreach (var trigger in template.Triggers.OfType<Trigger>())
                foreach (var action in trigger.EnterActions.Concat(trigger.ExitActions).OfType<BeginStoryboard>())
                {
                    var storyboard = action.Storyboard.Clone();
                    foreach (var animation in storyboard.Children.OfType<ColorAnimation>())
                        if (animation.To is Color to) animation.To = Map(to, theme);
                    if (theme.ReducedEffects || !theme.Animations || theme.MotionAmount == 0 || !SystemParameters.ClientAreaAnimation)
                        foreach (var animation in storyboard.Children.OfType<DoubleAnimation>())
                            if (Storyboard.GetTargetProperty(animation)?.Path.Contains("ScaleTransform", StringComparison.Ordinal) == true) animation.To = 1;
                    action.Storyboard = storyboard;
                }
        }
        PrepareMotion(style);
        target.Resources["WorkstationPanelStyle"] = style;
        var button = (Style)source[typeof(Button)]; PrepareMotion(button);
        target.Resources[typeof(Button)] = button;
        target.Resources["PrimaryActionButton"] = source["PrimaryActionButton"];
    }
}
