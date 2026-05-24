using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ARCYN.UI.Models;

public sealed class ModeConfig : INotifyPropertyChanged
{
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
    public string ShortcutHint => $"[{Math.Max(Index, 1)}]";

    [JsonIgnore]
    public string SessionLabel => string.IsNullOrWhiteSpace(Session) ? $"SES {IndexLabel}" : Session.ToUpperInvariant();

    [JsonIgnore]
    public string StateLabel => string.IsNullOrWhiteSpace(State)
        ? Type switch
        {
            "study" => "FOCUS LOCK",
            "design" => "CREATIVE READY",
            "code" => "EXEC LIVE",
            _ => "TACTICAL READY"
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
    public int ProcessCount => Targets.Count(target => !string.IsNullOrWhiteSpace(target.Cmd));

    [JsonIgnore]
    public string ProcessLabel => $"{ProcessCount:D2} PROC";

    [JsonIgnore]
    public string LaunchCountLabel => $"{_launchCount:D2} RUN";

    [JsonIgnore]
    public string LastLaunchLabel => _lastLaunchedAt?.ToString("HH:mm") ?? "NONE";

    [JsonIgnore]
    public string LastOutcomeLabel => _lastLaunchOutcome;

    public void RecordLaunch(int launchedTargets, int totalTargets, bool fullSuccess)
    {
        _launchCount++;
        _lastLaunchedAt = DateTime.Now;
        _lastLaunchOutcome = fullSuccess
            ? "SYNCED"
            : launchedTargets > 0
                ? $"PARTIAL {launchedTargets:D2}/{totalTargets:D2}"
                : "FAULT";

        OnPropertyChanged(nameof(LaunchCountLabel));
        OnPropertyChanged(nameof(LastLaunchLabel));
        OnPropertyChanged(nameof(LastOutcomeLabel));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class TargetConfig
{
    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    [JsonIgnore]
    public string DisplayLabel
    {
        get
        {
            if (Uri.TryCreate(Cmd, UriKind.Absolute, out var uri))
            {
                var host = uri.Host;
                return string.IsNullOrWhiteSpace(host) ? Cmd : host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (Cmd.EndsWith(":", StringComparison.Ordinal))
                return Cmd.TrimEnd(':').ToUpperInvariant();

            if (Cmd.Contains('\\') || Cmd.Contains('/'))
                {
                    // If launching via Explorer with a folder argument, show friendly folder name.
                    if (Cmd.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && Args.Count > 0)
                    {
                        var folder = Args[0].TrimEnd('\\', '/');
                        var name = System.IO.Path.GetFileName(folder);
                        return string.IsNullOrEmpty(name) ? folder : name;
                    }
                    return System.IO.Path.GetFileNameWithoutExtension(Cmd);
                }

            return Cmd;
        }
    }
}
