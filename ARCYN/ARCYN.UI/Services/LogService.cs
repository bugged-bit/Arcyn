using System.IO;

namespace ARCYN.UI.Services;

public sealed class LogService : IDisposable
{
    private readonly string _path;
    private readonly StreamWriter _writer;
    private readonly object _writeLock = new();
    private bool _disposed;

    public LogService(string fileName = "arcyn_launch.log")
    {
        _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        _writer = new StreamWriter(stream);
        _writer.AutoFlush = true;
    }

    public void Write(string message)
    {
        if (_disposed) return;
        try
        {
            lock (_writeLock)
            {
                _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
        }
        catch { }
    }

    public void Write(string format, params object?[] args)
    {
        if (_disposed) return;
        try
        {
            lock (_writeLock)
            {
                _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {string.Format(format, args)}");
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_writeLock)
        {
            _writer.Flush();
            _writer.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
