using System.IO;
namespace ARCYN.UI.Services;

public sealed class LogService : IDisposable
{
    private static readonly object Gate = new();
    public static string DirectoryPath { get; internal set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ARCYN");
    private bool _disposed;
    public void Write(string message) { if (!_disposed) WriteStatic(message); }
    public void Write(string format, params object?[] args) { if (!_disposed) WriteStatic(format, args); }
    public static void WriteStatic(string format, params object?[] args)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, "arcyn.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {(args.Length == 0 ? format : string.Format(format, args))}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* Logging must never prevent startup or recovery. */ }
    }
    public void Dispose() => _disposed = true;
}
