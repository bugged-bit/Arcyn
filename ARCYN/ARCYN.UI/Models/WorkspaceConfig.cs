using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using ARCYN.UI.Services;

namespace ARCYN.UI.Models;

public enum TargetKind { Auto, Application, Url, File, Folder }
public enum InstancePolicy { ReuseOrLaunch, ReuseOnly, LaunchNew }
public enum Readiness { Automatic, Window, Dispatch }
public enum WindowShowState { Normal, Maximized, Minimized }
public enum WindowOperation { ApplyLayout, Minimize, Restore, Focus }

public sealed class WindowMatch
{
    [JsonPropertyName("executable")] public string? Executable { get; set; }
    [JsonPropertyName("app_id")] public string? AppId { get; set; }
    [JsonPropertyName("class")] public string? ClassName { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("title_contains")] public bool TitleContains { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class WindowLayout
{
    [JsonPropertyName("target_id")] public string TargetId { get; set; } = "";
    [JsonPropertyName("monitor")] public string Monitor { get; set; } = "";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; } = 1;
    [JsonPropertyName("height")] public double Height { get; set; } = 1;
    [JsonPropertyName("dpi")] public uint Dpi { get; set; } = 96;
    [JsonPropertyName("state")] public WindowShowState State { get; set; }
    [JsonPropertyName("focus")] public bool Focus { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WindowActionConfig), "window")]
[JsonDerivedType(typeof(WaitActionConfig), "wait")]
[JsonDerivedType(typeof(CommandActionConfig), "command")]
public abstract class WorkspaceActionConfig
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("depends_on")] public List<string> DependsOn { get; set; } = [];
    [JsonPropertyName("timeout_ms")] public int TimeoutMs { get; set; } = 30000;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
public sealed class WindowActionConfig : WorkspaceActionConfig
{
    [JsonPropertyName("target_id")] public string TargetId { get; set; } = "";
    [JsonPropertyName("operation")] public WindowOperation Operation { get; set; }
}
public sealed class WaitActionConfig : WorkspaceActionConfig
{
    [JsonPropertyName("target_id")] public string? TargetId { get; set; }
    [JsonPropertyName("delay_ms")] public int DelayMs { get; set; } = 500;
}
public sealed class CommandActionConfig : WorkspaceActionConfig
{
    [JsonPropertyName("cmd")] public string Cmd { get; set; } = "";
    [JsonPropertyName("args")] public List<string> Args { get; set; } = [];
    [JsonPropertyName("working_directory")] public string? WorkingDirectory { get; set; }
    [JsonPropertyName("elevate")] public bool Elevate { get; set; }
}

public static class WorkspaceDefinition
{
    internal static List<string> Dependencies(WorkspaceActionConfig action) => action.DependsOn
        .Concat(action is WindowActionConfig window ? [window.TargetId] : Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
    public static ModeConfig Clone(ModeConfig mode) => JsonSerializer.Deserialize<ModeConfig>(JsonSerializer.Serialize(mode, ConfigService.JsonOptions), ConfigService.JsonOptions)!;

    public static ModeConfig Duplicate(ModeConfig mode)
    {
        var copy = Clone(mode);
        copy.Targets = copy.LaunchTargets.ToList(); copy.Apps.Clear(); copy.Websites.Clear(); copy.Folders.Clear();
        var ids = copy.Targets.Select(t => t.Id).Concat(copy.Actions.Select(a => a.Id)).ToDictionary(id => id, _ => Guid.NewGuid().ToString("N"));
        copy.Id = Guid.NewGuid().ToString("N");
        foreach (var t in copy.Targets) { t.Id = ids[t.Id]; t.DependsOn = t.DependsOn.Select(id => ids[id]).ToList(); }
        foreach (var a in copy.Actions)
        {
            a.Id = ids[a.Id]; a.DependsOn = a.DependsOn.Select(id => ids[id]).ToList();
            if (a is WindowActionConfig w) w.TargetId = ids[w.TargetId];
            if (a is WaitActionConfig { TargetId: not null } wait) wait.TargetId = ids[wait.TargetId];
        }
        foreach (var l in copy.Layout) l.TargetId = ids[l.TargetId];
        return copy;
    }

    public static void Validate(ModeConfig mode)
    {
        if (string.IsNullOrWhiteSpace(mode.Id) || mode.Layout == null || mode.Actions == null) throw new InvalidDataException("Workspace identity, layout and actions must be valid.");
        var targets = mode.LaunchTargets.ToList();
        var dependencies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string id, List<string> deps, int timeout)
        {
            if (string.IsNullOrWhiteSpace(id) || deps == null || deps.Any(string.IsNullOrWhiteSpace) || !dependencies.TryAdd(id, deps))
                throw new InvalidDataException("Target/action IDs must be unique and dependencies must be IDs.");
            if (timeout is < 1 or > 300000) throw new InvalidDataException("Timeout must be 1–300000 ms.");
        }
        foreach (var t in targets)
        {
            Add(t.Id, t.DependsOn, t.TimeoutMs);
            if (!Enum.IsDefined(t.Kind) || !Enum.IsDefined(t.InstancePolicy) || !Enum.IsDefined(t.Readiness)) throw new InvalidDataException("Unknown target policy.");
            if (t.Readiness == Readiness.Dispatch && t.InstancePolicy != InstancePolicy.ReuseOrLaunch) throw new InvalidDataException("Instance policies require window readiness.");
            if (t.Match is { } m && string.IsNullOrWhiteSpace(m.Executable) && string.IsNullOrWhiteSpace(m.AppId))
                throw new InvalidDataException("Window matching requires executable path or application identity.");
            if (t.Match?.Executable is { } executable && !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(executable)))
                throw new InvalidDataException("Matching executable must be a full path; environment variables are supported.");
        }
        if (mode.Actions.Count == 0 && mode.Layout.Count == 0 && targets.All(t => t.DependsOn.Count == 0)) return;
        var targetIds = targets.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var a in mode.Actions)
        {
            if (a == null) throw new InvalidDataException("Actions must be objects.");
            if (a.DependsOn == null) throw new InvalidDataException("Action prerequisites must be a list.");
            Add(a.Id, Dependencies(a), a.TimeoutMs);
            if (a is WindowActionConfig w)
            {
                if (!targetIds.Contains(w.TargetId) || !Enum.IsDefined(w.Operation)) throw new InvalidDataException("Window action requires a valid target and operation.");
            }
            else if (a is WaitActionConfig wait)
            {
                if (wait.DelayMs is < 0 or > 300000 || wait.TargetId == null && wait.DelayMs > wait.TimeoutMs || wait.TargetId != null && !targetIds.Contains(wait.TargetId)) throw new InvalidDataException("Wait requires a valid target or delay within its timeout (0–300000 ms).");
            }
            else if (a is CommandActionConfig c)
            {
                if (string.IsNullOrWhiteSpace(c.Cmd) || c.Args == null || c.Args.Any(x => x == null)) throw new InvalidDataException("Command requires executable and valid arguments.");
            }
            else throw new InvalidDataException("Unknown action type.");
        }
        foreach (var deps in dependencies.Values)
            if (deps.Any(id => !dependencies.ContainsKey(id))) throw new InvalidDataException("Dependency refers to a missing target/action.");
        var remaining = dependencies.ToDictionary(p => p.Key, p => p.Value.ToHashSet(StringComparer.Ordinal));
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(p => p.Value.Count == 0);
            if (next.Key == null) throw new InvalidDataException("Workspace dependencies contain a cycle.");
            remaining.Remove(next.Key); foreach (var deps in remaining.Values) deps.Remove(next.Key);
        }
        var layoutIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in mode.Layout)
        {
            if (l == null || !targetIds.Contains(l.TargetId) || !layoutIds.Add(l.TargetId) || l.Monitor == null || !Enum.IsDefined(l.State) || l.Dpi == 0 ||
                !double.IsFinite(l.X) || !double.IsFinite(l.Y) || !double.IsFinite(l.Width) || !double.IsFinite(l.Height) ||
                Math.Abs(l.X) > 10 || Math.Abs(l.Y) > 10 || l.Width is <= 0 or > 10 || l.Height is <= 0 or > 10)
                throw new InvalidDataException("Layout requires unique valid targets and finite, positive geometry.");
        }
        if (mode.Layout.Count(l => l.Focus) > 1) throw new InvalidDataException("Only one layout window may receive final focus.");
        var needsWindow = mode.Layout.Select(l => l.TargetId).Concat(mode.Actions.OfType<WindowActionConfig>().Select(a => a.TargetId)).ToHashSet(StringComparer.Ordinal);
        if (targets.Any(t => needsWindow.Contains(t.Id) && t.Readiness == Readiness.Dispatch)) throw new InvalidDataException("Saved placements and window operations require window readiness; change the target's Dispatch setting.");
        if (mode.Actions.OfType<WindowActionConfig>().Any(a => a.Operation == WindowOperation.ApplyLayout && !layoutIds.Contains(a.TargetId))) throw new InvalidDataException("Apply-layout action requires a saved placement for its target.");
    }
}
