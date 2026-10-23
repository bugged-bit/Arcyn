using System.Threading;
namespace ARCYN.UI.Services;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private readonly RegisteredWaitHandle? _wait;
    public bool IsFirst { get; }
    public SingleInstance(Action activate, string? name = null)
    {
        var applicationInstance = name == null;
        name ??= "Local\\ARCYN." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".activate");
        _mutex = new Mutex(false, name, out var created);
        IsFirst = created;
        if (created) _wait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => activate(), null, Timeout.Infinite, false);
        else
        {
            if (applicationInstance)
            {
                var window = NativeMethods.FindWindow(null, "ARCYN");
                if (window != IntPtr.Zero) { NativeMethods.GetWindowThreadProcessId(window, out var process); NativeMethods.AllowSetForegroundWindow(process); }
            }
            _activation.Set();
        }
    }
    public void Dispose() { _wait?.Unregister(null); _activation.Dispose(); _mutex.Dispose(); }
}
