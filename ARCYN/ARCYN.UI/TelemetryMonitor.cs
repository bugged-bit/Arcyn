using System.Runtime.InteropServices;
namespace ARCYN.UI;

// System-wide HUD readings; performance reports attribute ARCYN separately by PID.
public sealed class TelemetryMonitor : IDisposable
{
    private ulong _idle, _kernel, _user;
    private bool _primed, _disposed;
    public float CpuPercent { get; private set; } = float.NaN;
    public float RamPercent { get; private set; } = float.NaN;
    public TelemetryMonitor() => Sample();
    public void Sample()
    {
        if (_disposed) return;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel - _kernel + user - _user;
            if (_primed && total > 0) CpuPercent = (float)Math.Clamp(100.0 * (total - Math.Min(total, idle - _idle)) / total, 0, 100);
            _idle = idle; _kernel = kernel; _user = user; _primed = true;
        }
        else CpuPercent = float.NaN;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        RamPercent = GlobalMemoryStatusEx(ref memory) ? memory.Load : float.NaN;
    }
    public void Dispose() => _disposed = true;
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}
