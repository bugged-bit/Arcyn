using System.Text.Json.Serialization;

namespace ARCYN.UI.Models;

public class ModeConfig
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("cmd")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];
}
