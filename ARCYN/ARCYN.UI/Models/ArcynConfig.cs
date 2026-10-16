using System.Text.Json.Serialization;
using System.Text.Json;

namespace ARCYN.UI.Models;

public sealed class ArcynConfig
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonPropertyName("theme")]
    public ThemeConfig Theme { get; set; } = new();

    [JsonPropertyName("behavior")]
    public BehaviorConfig Behavior { get; set; } = new();

    [JsonPropertyName("modes")]
    public List<ModeConfig> Modes { get; set; } = [];
}

public sealed class ThemeConfig
{
    [JsonPropertyName("background")] public string Background { get; set; } = "#0A0A0A";
    [JsonPropertyName("text")] public string Text { get; set; } = "#FFFFFF";
    [JsonPropertyName("font_family")] public string FontFamily { get; set; } = "JetBrainsMono NF";
    [JsonPropertyName("font_scale")] public double FontScale { get; set; } = 1;
    [JsonPropertyName("width")] public double Width { get; set; } = 920;
    [JsonPropertyName("height")] public double Height { get; set; } = 490;
    [JsonPropertyName("corner_radius")] public double CornerRadius { get; set; } = 9;
    [JsonPropertyName("border_width")] public double BorderWidth { get; set; } = 1;
    [JsonPropertyName("card_height")] public double CardHeight { get; set; } = 94;
    [JsonPropertyName("spacing")] public double Spacing { get; set; } = 6;
    [JsonPropertyName("layout")] public string Layout { get; set; } = "rows";
    [JsonPropertyName("particles")] public int Particles { get; set; } = 60;
    [JsonPropertyName("cursor_trail")] public bool CursorTrail { get; set; } = true;
    [JsonPropertyName("acrylic")] public bool Acrylic { get; set; } = true;
    [JsonPropertyName("vignette")] public bool Vignette { get; set; } = true;
    [JsonPropertyName("motion_amount")] public double MotionAmount { get; set; } = 1;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "#D64545";

    [JsonPropertyName("glow_opacity")]
    public double GlowOpacity { get; set; } = 0.28;

    [JsonPropertyName("scanlines")]
    public bool Scanlines { get; set; } = true;

    [JsonPropertyName("animations")]
    public bool Animations { get; set; } = true;

    [JsonPropertyName("reduced_effects")]
    public bool ReducedEffects { get; set; }

    [JsonPropertyName("compact_mode")]
    public bool CompactMode { get; set; }
}

public sealed class BehaviorConfig
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonPropertyName("idle_timeout_seconds")]
    public int IdleTimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("always_on_top")]
    public bool AlwaysOnTop { get; set; } = true;

    [JsonPropertyName("close_on_launch")]
    public bool CloseOnLaunch { get; set; } = true;
}
