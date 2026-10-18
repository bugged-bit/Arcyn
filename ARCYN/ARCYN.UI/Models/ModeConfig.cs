using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Windows.Media;

namespace ARCYN.UI.Models;

public sealed class ModeConfig : INotifyPropertyChanged
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("layout")] public List<WindowLayout> Layout { get; set; } = [];
    [JsonPropertyName("actions")] public List<WorkspaceActionConfig> Actions { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonPropertyName("name")] public string Name { get => Label; set => Label = value; }
    [JsonPropertyName("description")] public string Description { get => Subtitle; set => Subtitle = value; }
    [JsonPropertyName("accent")] public string Accent { get; set; } = "#D64545";
    [JsonIgnore] public SolidColorBrush AccentBrush => CreateAccent(255);
    [JsonIgnore] public SolidColorBrush AccentTintBrush => CreateAccent(42);
    private SolidColorBrush CreateAccent(byte alpha)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(Accent); }
        catch { color = Color.FromRgb(0xD6, 0x45, 0x45); }
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)); brush.Freeze(); return brush;
    }
    [JsonPropertyName("apps")] public List<string> Apps { get; set; } = [];
    [JsonPropertyName("websites")] public List<string> Websites { get; set; } = [];
    [JsonPropertyName("folders")] public List<string> Folders { get; set; } = [];
    [JsonIgnore] public IEnumerable<TargetConfig> LaunchTargets => Targets
        .Concat(Apps.Select((a, i) => new TargetConfig { Id = Id + ".app." + i, Cmd = a, LegacyCommandLine = true }))
        .Concat(Websites.Select((a, i) => new TargetConfig { Id = Id + ".url." + i, Cmd = a, Kind = TargetKind.Url }))
        .Concat(Folders.Select((a, i) => new TargetConfig { Id = Id + ".folder." + i, Cmd = "explorer.exe", Args = [a], Kind = TargetKind.Folder }));
    private string _label = string.Empty;
    private string _subtitle = string.Empty;
    private string _type = "default";
    private List<TargetConfig> _targets = [];
    private string _session = string.Empty;
    private string _state = string.Empty;
    private int _index;
    private int _launchCount;
    private DateTime? _lastLaunchedAt;
    private string _lastLaunchOutcome = "STANDBY";

    [JsonPropertyName("label")]
    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    [JsonPropertyName("subtitle")]
    public string Subtitle
    {
        get => _subtitle;
        set => SetField(ref _subtitle, value);
    }

    [JsonPropertyName("type")]
    public string Type
    {
        get => _type;
        set
        {
            if (!SetField(ref _type, value))
                return;

            RaiseDerivedProperties();
        }
    }

    [JsonPropertyName("targets")]
    public List<TargetConfig> Targets
    {
        get => _targets;
        set
        {
            if (!SetField(ref _targets, value))
                return;

            RaiseDerivedProperties();
        }
    }

    [JsonPropertyName("session")]
    public string Session
    {
        get => _session;
        set
        {
            if (!SetField(ref _session, value))
                return;

            OnPropertyChanged(nameof(SessionLabel));
        }
    }

    [JsonPropertyName("state")]
    public string State
    {
        get => _state;
        set
        {
            if (!SetField(ref _state, value))
                return;

            RaiseDerivedProperties();
        }
    }

    [JsonIgnore]
    public int Index
    {
        get => _index;
        set
        {
            if (!SetField(ref _index, value))
                return;

            OnPropertyChanged(nameof(IndexLabel));
            OnPropertyChanged(nameof(ShortcutHint));
            OnPropertyChanged(nameof(SessionLabel));
        }
    }

    [JsonIgnore]
    public string IndexLabel => Index <= 0 ? "--" : Index.ToString("D2");

    [JsonIgnore]
    public string ShortcutHint => Index is >= 1 and <= 9 ? $"[{Index}]" : "";

    [JsonIgnore]
    public string SessionLabel => string.IsNullOrWhiteSpace(Session) ? $"SES {IndexLabel}" : Session.ToUpperInvariant();

    [JsonIgnore]
    public string StateLabel => string.IsNullOrWhiteSpace(State)
        ? Type switch
        {
            "study" => "Room to focus",
            "design" => "Ready to create",
            "code" => "Ready to build",
            _ => "Ready when you are"
        }
        : State.ToUpperInvariant();

    [JsonIgnore]
    public string StatusTag => Type switch
    {
        "study" => "FOCUS",
        "design" => "CREATE",
        "code" => "DEPLOY",
        _ => "ACTIVE"
    };

    [JsonIgnore]
    public string TypeLabel => string.IsNullOrWhiteSpace(Type) ? "DEFAULT" : Type.ToUpperInvariant();

    [JsonIgnore]
    public int ProcessCount => LaunchTargets.Count();

    [JsonIgnore]
    public string ProcessLabel => $"{ProcessCount} targets";

    [JsonIgnore] public string TargetSummary => string.Join(" · ", LaunchTargets.Select(t => t.DisplayLabel));

    [JsonIgnore]
    public string LaunchCountLabel => $"{_launchCount:D2} RUN";

    [JsonIgnore]
    public string LastLaunchLabel => _lastLaunchedAt?.ToString("HH:mm") ?? "NONE";

    [JsonIgnore]
    public string LastOutcomeLabel => _lastLaunchOutcome switch { "STANDBY" => "Not opened yet", "COMPLETE" => "Last run complete", "PARTIAL" => "Needs attention", "CANCELED" => "Last run canceled", _ => _lastLaunchOutcome };

    public void RecordLaunch(int launchedTargets, int totalTargets, bool fullSuccess)
    {
        _launchCount++;
        _lastLaunchedAt = DateTime.Now;
        _lastLaunchOutcome = fullSuccess
            ? "COMPLETE"
            : launchedTargets > 0
                ? $"PARTIAL {launchedTargets:D2}/{totalTargets:D2}"
                : "FAULT";

        OnPropertyChanged(nameof(LaunchCountLabel));
        OnPropertyChanged(nameof(LastLaunchLabel));
        OnPropertyChanged(nameof(LastOutcomeLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void RetainLaunchHistory(ModeConfig source)
    {
        _launchCount = source._launchCount;
        _lastLaunchedAt = source._lastLaunchedAt;
        _lastLaunchOutcome = source._lastLaunchOutcome;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void RaiseDerivedProperties()
    {
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(StatusTag));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(ProcessCount));
        OnPropertyChanged(nameof(ProcessLabel));
        OnPropertyChanged(nameof(LaunchTargets));
        OnPropertyChanged(nameof(TargetSummary));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class TargetConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("kind")] public TargetKind Kind { get; set; }
    [JsonPropertyName("instance_policy")] public InstancePolicy InstancePolicy { get; set; }
    [JsonPropertyName("match")] public WindowMatch? Match { get; set; }
    [JsonPropertyName("readiness")] public Readiness Readiness { get; set; }
    [JsonPropertyName("timeout_ms")] public int TimeoutMs { get; set; } = 30000;
    [JsonPropertyName("depends_on")] public List<string> DependsOn { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    [JsonPropertyName("working_directory")] public string? WorkingDirectory { get; set; }
    [JsonIgnore] public bool LegacyCommandLine { get; set; }
    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    [JsonIgnore]
    public string DisplayLabel
    {
        get
        {
            if (Cmd.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && Args.Count > 0)
            {
                var folder = Args[0].TrimEnd('\\', '/');
                var name = Path.GetFileName(folder);
                return string.IsNullOrEmpty(name) ? folder : name;
            }
            if (Uri.TryCreate(Cmd, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                var host = uri.Host;
                return string.IsNullOrWhiteSpace(host) ? Cmd : host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (Cmd.EndsWith(":", StringComparison.Ordinal))
                return Cmd.TrimEnd(':').ToUpperInvariant();

            if (Cmd.Contains('\\') || Cmd.Contains('/'))
                return Path.GetFileNameWithoutExtension(Cmd);

            return Cmd;
        }
    }
}
