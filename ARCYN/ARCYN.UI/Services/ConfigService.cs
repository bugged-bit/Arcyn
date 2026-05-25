using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ARCYN.UI.Models;

namespace ARCYN.UI.Services;

public sealed class ConfigService
{
    private const string ConfigFileName = "arcyn.json";
    private const string OldConfigFileName = "protocoll.json";
    private const string AppDataFolder = "ARCYN";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ArcynConfig? Load()
    {
        var path = ResolvePath();
        if (path == null)
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ArcynConfig>(json, JsonOptions);
            if (config == null) return null;

            for (int i = 0; i < config.Modes.Count; i++)
                config.Modes[i].Index = i + 1;

            return config;
        }
        catch (Exception ex)
        {
            LogService.WriteStatic("Config load error: {0}", ex.Message);
            return null;
        }
    }

    public string? ResolvePath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataFolder,
            ConfigFileName);

        if (File.Exists(appData))
            return appData;

        var local = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (File.Exists(local))
            return local;

        return TryMigrateOldConfig();
    }

    public string GetOrCreatePath()
    {
        var local = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataFolder,
            ConfigFileName);

        if (File.Exists(local))
            return local;

        return appData;
    }

    public void Save(ArcynConfig config)
    {
        var path = GetOrCreatePath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        for (int i = 0; i < config.Modes.Count; i++)
            config.Modes[i].Index = i + 1;

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    private string? TryMigrateOldConfig()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, OldConfigFileName),
            Path.Combine(AppContext.BaseDirectory, "..", OldConfigFileName),
            Path.Combine(Environment.CurrentDirectory, OldConfigFileName)
        ];

        foreach (var oldPath in candidates)
        {
            if (!File.Exists(oldPath))
                continue;

            try
            {
                var json = File.ReadAllText(oldPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array)
                    continue;

                var config = new ArcynConfig();

                foreach (var modeEl in root.EnumerateArray())
                {
                    var mode = new ModeConfig
                    {
                        Name = GetString(modeEl, "label") ?? "MODE",
                        Description = GetString(modeEl, "subtitle") ?? string.Empty,
                        Accent = "#D64545"
                    };

                    if (modeEl.TryGetProperty("targets", out var targets))
                    {
                        foreach (var t in targets.EnumerateArray())
                        {
                            var cmd = GetString(t, "cmd") ?? string.Empty;
                            var args = GetStringArray(t, "args");

                            if (string.IsNullOrWhiteSpace(cmd))
                                continue;

                            if (cmd.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && args.Count > 0)
                            {
                                mode.Folders.Add(args[0]);
                            }
                            else if (cmd.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                     cmd.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                mode.Websites.Add(cmd);
                            }
                            else
                            {
                                mode.Apps.Add(cmd);
                            }
                        }
                    }

                    if (mode.Apps.Count > 0 || mode.Websites.Count > 0 || mode.Folders.Count > 0)
                        config.Modes.Add(mode);
                }

                if (config.Modes.Count > 0)
                {
                    var newPath = GetOrCreatePath();
                    var dir = Path.GetDirectoryName(newPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    Save(config);

                    LogService.WriteStatic("Migrated config from {0} → {1}", oldPath, newPath);
                    return newPath;
                }
            }
            catch
            {
                // ignore migration failures
            }
        }

        return null;
    }

    private static string GetString(JsonElement el, string key)
    {
        return el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;
    }

    private static List<string> GetStringArray(JsonElement el, string key)
    {
        var result = new List<string>();
        if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    result.Add(item.GetString() ?? string.Empty);
            }
        }
        return result;
    }
}
