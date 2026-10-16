using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using ARCYN.UI.Models;
namespace ARCYN.UI.Services;

public sealed class ConfigService
{
    public static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _userPath, _exeDirectory;
    private readonly string? _explicitPath;
    private string? _activePath, _sourcePath;
    private byte[]? _expectedHash;
    public string? SourcePath => _sourcePath;
    public ConfigService(string? explicitPath = null, string? userDirectory = null, string? executableDirectory = null)
    {
        _exeDirectory = executableDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        _userPath = Path.Combine(userDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ARCYN"), "arcyn.json");
        _explicitPath = explicitPath == null ? null : Path.GetFullPath(explicitPath);
    }
    public string? ResolvePath()
    {
        if (_activePath != null) return _sourcePath;
        var candidates = new[] { _userPath, Path.Combine(_exeDirectory, "arcyn.json"),
            Path.Combine(_exeDirectory, "protocoll.json"), Path.Combine(_exeDirectory, "ARCYN", "protocoll.json"),
            Path.GetFullPath(Path.Combine(_exeDirectory, "..", "protocoll.json")) };
        _sourcePath = _explicitPath ?? candidates.FirstOrDefault(File.Exists);
        _activePath = _explicitPath ?? (_sourcePath?.EndsWith("protocoll.json", StringComparison.OrdinalIgnoreCase) == true ? _userPath : _sourcePath) ?? _userPath;
        return _sourcePath;
    }
    public string GetOrCreatePath() { ResolvePath(); return _activePath!; }
    public ArcynConfig? Load()
    {
        var path = ResolvePath();
        if (path == null) return null;
        if (!File.Exists(path)) throw new FileNotFoundException("Selected configuration does not exist.", path);
        try
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Configuration exceeds 4 MiB.");
            var bytes = File.ReadAllBytes(path);
            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
            var json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var config = doc.RootElement.ValueKind == JsonValueKind.Array
                ? new ArcynConfig { Modes = JsonSerializer.Deserialize<List<ModeConfig>>(json, JsonOptions)! }
                : JsonSerializer.Deserialize<ArcynConfig>(json, JsonOptions) ?? throw new InvalidDataException("Empty configuration.");
            Validate(config);
            _expectedHash = string.Equals(path, _activePath, StringComparison.OrdinalIgnoreCase) ? SHA256.HashData(bytes) : null;
            LogService.WriteStatic("Configuration read: {0}; saves: {1}", path, _activePath);
            return config;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException)
        { throw new InvalidDataException($"Cannot read {path}. Your file was preserved. Previous save: {path}.bak. {ex.Message}", ex); }
    }
    public static void Validate(ArcynConfig config)
    {
        if (config.SchemaVersion > 1) throw new InvalidDataException("This configuration needs a newer ARCYN version.");
        if (config.Modes == null || config.Modes.Any(m => m == null)) throw new InvalidDataException("Modes must be a list of objects.");
        config.Theme ??= new(); config.Behavior ??= new();
        ThemeService.Validate(config.Theme);
        if (config.Behavior.IdleTimeoutSeconds is < 0 or > 86400) throw new InvalidDataException("Idle timeout must be 0–86400 seconds.");
        for (var i = 0; i < config.Modes.Count; i++)
        {
            var mode = config.Modes[i];
            if (mode.Label == null || mode.Subtitle == null) throw new InvalidDataException("Workspace names and descriptions must be strings.");
            mode.Targets ??= []; mode.Apps ??= []; mode.Folders ??= []; mode.Websites ??= [];
            if (mode.Targets.Any(t => t == null || t.Cmd == null || t.Args == null || t.Args.Any(a => a == null)) ||
                mode.Apps.Any(a => a == null) || mode.Websites.Any(a => a == null) || mode.Folders.Any(a => a == null))
                throw new InvalidDataException("Targets and arguments must contain valid objects and strings.");
            mode.Index = i + 1;
        }
        // Availability is a launch concern; offline drives, duplicates and empty workspaces remain editable.
    }
    public void Save(ArcynConfig config) => SaveAsync(config).GetAwaiter().GetResult();
    public async Task SaveAsync(ArcynConfig config)
    {
        Validate(config);
        var json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Configuration exceeds 4 MiB; previous file was retained.");
        await _saveLock.WaitAsync().ConfigureAwait(false);
        string? temporary = null;
        try
        {
            var path = GetOrCreatePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(json).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
            {
                if (_expectedHash == null || !SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(_expectedHash))
                    throw new InvalidDataException("Configuration changed on disk. Reopen ARCYN before saving; the external changes were preserved.");
                File.Replace(temporary, path, path + ".bak");
            }
            else
            {
                if (_expectedHash != null) throw new InvalidDataException("Configuration was removed externally. Reopen ARCYN before saving.");
                File.Move(temporary, path);
            }
            _sourcePath = path;
            _expectedHash = SHA256.HashData(json);
        }
        finally
        {
            try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            finally { _saveLock.Release(); }
        }
    }
}
