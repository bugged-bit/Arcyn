using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using ARCYN.UI.Models;

namespace ARCYN.UI.Services;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}
public sealed record MonitorSnapshot(string Id, string Name, PixelRect Bounds, PixelRect WorkArea, bool Primary);
public sealed record WindowSnapshot(nint Handle, int ProcessId, long ProcessStarted, string? Executable, string? AppId,
    string ProcessName, string ClassName, string Title, PixelRect NormalBounds, WindowShowState State, uint Dpi);

public interface IWindowSystem
{
    IReadOnlyList<WindowSnapshot> Windows();
    IReadOnlyList<MonitorSnapshot> Monitors();
    string? ResolveExecutable(string command);
    bool IsCurrent(WindowSnapshot window);
    bool IsResponsive(WindowSnapshot window);
    string? Place(WindowSnapshot window, PixelRect bounds, WindowShowState state);
    string? Show(WindowSnapshot window, WindowShowState state);
    string? Focus(WindowSnapshot window);
}

public static class WindowMatching
{
    public static TargetKind Kind(TargetConfig target)
    {
        if (target.Kind != TargetKind.Auto) return target.Kind;
        var (cmd, _) = LaunchService.ParseTarget(target);
        if (Uri.TryCreate(cmd, UriKind.Absolute, out var uri) && !uri.IsFile) return TargetKind.Url;
        if (Directory.Exists(Environment.ExpandEnvironmentVariables(cmd)) || cmd.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) return TargetKind.Folder;
        return !Path.HasExtension(cmd) || Path.GetExtension(cmd).ToLowerInvariant() is ".exe" or ".lnk" ? TargetKind.Application : TargetKind.File;
    }
    public static WindowMatch? Identity(TargetConfig target, IWindowSystem system)
    {
        if (target.Match != null) return target.Match;
        if (Kind(target) != TargetKind.Application) return null;
        var (cmd, _) = LaunchService.ParseTarget(target);
        var executable = system.ResolveExecutable(cmd);
        return executable == null ? null : new() { Executable = executable };
    }
    public static (WindowSnapshot? Window, string? Error) Select(WindowMatch match, IEnumerable<WindowSnapshot> windows, ISet<nint>? exclude = null)
    {
        var path = match.Executable == null ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(match.Executable));
        var candidates = windows.Where(w => exclude?.Contains(w.Handle) != true).ToList();
        if (candidates.Any(w => w.Executable == null &&
            (path != null && (w.ProcessName.Length == 0 || w.ProcessName.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)) ||
             match.AppId != null && w.AppId == null && (match.ClassName == null || match.ClassName == w.ClassName) &&
             (match.Title == null || (match.TitleContains ? w.Title.Contains(match.Title, StringComparison.OrdinalIgnoreCase) : w.Title.Equals(match.Title, StringComparison.OrdinalIgnoreCase))))))
            return (null, "A possible application window has inaccessible identity; select a stronger application identity or repair access.");
        candidates = candidates.Where(w => (path == null || string.Equals(path, w.Executable, StringComparison.OrdinalIgnoreCase)) &&
            (match.AppId == null || string.Equals(match.AppId, w.AppId, StringComparison.Ordinal)) &&
            (match.ClassName == null || match.ClassName == w.ClassName) &&
            (match.Title == null || (match.TitleContains ? w.Title.Contains(match.Title, StringComparison.OrdinalIgnoreCase) : w.Title.Equals(match.Title, StringComparison.OrdinalIgnoreCase)))).ToList();
        return candidates.Count switch { 0 => (null, null), 1 => (candidates[0], null), _ => (null, "Multiple windows match. Select a window class/title in Targets; nothing was launched or moved.") };
    }
}

public static class WorkspaceLayout
{
    public static MonitorSnapshot MonitorFor(PixelRect rect, IReadOnlyList<MonitorSnapshot> monitors) => monitors
        .OrderByDescending(m => Math.Max(0L, Math.Min((long)rect.Right, m.Bounds.Right) - Math.Max(rect.X, m.Bounds.X)) *
                                Math.Max(0L, Math.Min((long)rect.Bottom, m.Bounds.Bottom) - Math.Max(rect.Y, m.Bounds.Y)))
        .ThenByDescending(m => m.Primary).First();

    public static WindowLayout Capture(string targetId, WindowSnapshot window, IReadOnlyList<MonitorSnapshot> monitors)
    {
        if (monitors.Count == 0) throw new InvalidOperationException("No desktop monitors are available.");
        var m = MonitorFor(window.NormalBounds, monitors); var r = window.NormalBounds; var w = m.WorkArea;
        return new() { TargetId = targetId, Monitor = m.Id, X = (double)(r.X - w.X) / w.Width, Y = (double)(r.Y - w.Y) / w.Height,
            Width = (double)r.Width / w.Width, Height = (double)r.Height / w.Height, State = window.State, Dpi = window.Dpi };
    }

    public static (PixelRect Bounds, bool Fallback) Restore(WindowLayout layout, IReadOnlyList<MonitorSnapshot> monitors)
    {
        if (monitors.Count == 0) throw new InvalidOperationException("No desktop monitors are available.");
        var saved = monitors.FirstOrDefault(m => m.Id == layout.Monitor);
        var m = saved ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors[0]; var w = m.WorkArea;
        var width = (int)Math.Clamp(Math.Round(layout.Width * w.Width), 1, w.Width);
        var height = (int)Math.Clamp(Math.Round(layout.Height * w.Height), 1, w.Height);
        var x = (int)Math.Clamp(Math.Round(w.X + layout.X * w.Width), w.X, w.Right - width);
        var y = (int)Math.Clamp(Math.Round(w.Y + layout.Y * w.Height), w.Y, w.Bottom - height);
        return (new(x, y, width, height), saved == null);
    }
}

public sealed class WindowsWindowSystem : IWindowSystem
{
    internal static PixelRect? WorkAreaFor(nint window)
    {
        var monitor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>(), Device = "" };
        return Native.GetMonitorInfo(Native.MonitorFromWindow(window, 2), ref monitor) ? monitor.Work.Pixels : null;
    }
    internal static void ConstrainToWorkArea(nint window, PixelRect area)
    {
        if (!Native.GetWindowRect(window, out var rectangle)) return;
        var bounds = rectangle.Pixels;
        var x = Math.Clamp(bounds.X, area.X, Math.Max(area.X, area.Right - bounds.Width));
        var y = Math.Clamp(bounds.Y, area.Y, Math.Max(area.Y, area.Bottom - bounds.Height));
        if (x != bounds.X || y != bounds.Y) NativeMethods.SetWindowPos(window, 0, x, y, 0, 0, 0x0015); // Keep size, Z order and activation.
    }
    public IReadOnlyList<MonitorSnapshot> Monitors()
    {
        var result = new List<MonitorSnapshot>();
        var succeeded = Native.EnumDisplayMonitors(0, 0, (nint handle, nint _, ref Native.Rect rect, nint data) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>(), Device = "" };
            if (!Native.GetMonitorInfo(handle, ref info)) return true;
            var device = new Native.DisplayDevice { Size = Marshal.SizeOf<Native.DisplayDevice>() };
            var id = Native.EnumDisplayDevices(info.Device, 0, ref device, 1) && !string.IsNullOrWhiteSpace(device.Id) ? device.Id : info.Device;
            result.Add(new(id, info.Device, info.Monitor.Pixels, info.Work.Pixels, (info.Flags & 1) != 0)); return true;
        }, 0);
        if (!succeeded) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot enumerate desktop monitors.");
        return result;
    }

    public IReadOnlyList<WindowSnapshot> Windows()
    {
        var result = new List<WindowSnapshot>();
        var identities = new Dictionary<int, (string? Exe, string? AppId, string Name, long Started)>();
        var succeeded = Native.EnumWindows((handle, _) =>
        {
            if (!Native.IsWindowVisible(handle) || Native.GetWindow(handle, 4) != 0 || (NativeMethods.GetWindowLong(handle, -20) & 0x80) != 0) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var pid);
            if (pid == 0 || pid == Environment.ProcessId) return true;
            if (Native.DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var className = new StringBuilder(256); Native.GetClassName(handle, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (!identities.TryGetValue((int)pid, out var identity))
            {
                identity = Identity((int)pid); identities[(int)pid] = identity;
            }
            var placement = new Native.Placement { Length = Marshal.SizeOf<Native.Placement>() };
            if (!Native.GetWindowPlacement(handle, ref placement)) return true;
            var monitor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>(), Device = "" };
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(handle, 2), ref monitor)) return true;
            var rect = placement.Normal.Pixels;
            rect = rect with { X = rect.X + monitor.Work.Left - monitor.Monitor.Left, Y = rect.Y + monitor.Work.Top - monitor.Monitor.Top };
            if (rect.Width <= 0 || rect.Height <= 0) return true;
            var title = new StringBuilder(1024); Native.GetWindowText(handle, title, title.Capacity);
            result.Add(new(handle, (int)pid, identity.Started, identity.Exe, identity.AppId, identity.Name, className.ToString(), title.ToString(), rect,
                Native.IsIconic(handle) ? WindowShowState.Minimized : Native.IsZoomed(handle) ? WindowShowState.Maximized : WindowShowState.Normal,
                Math.Max(96u, Native.GetDpiForWindow(handle))));
            return true;
        }, 0);
        if (!succeeded) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot enumerate application windows; nothing was launched.");
        return result;
    }

    private static (string? Exe, string? AppId, string Name, long Started) Identity(int pid)
    {
        string? exe = null, appId = null; string name = ""; long started = 0;
        var handle = Native.OpenProcess(0x1000, false, pid);
        if (handle != 0)
            try
            {
                var buffer = new StringBuilder(32768); uint size = (uint)buffer.Capacity;
                if (Native.QueryFullProcessImageName(handle, 0, buffer, ref size)) exe = buffer.ToString();
                uint length = 0;
                if (Native.GetApplicationUserModelId(handle, ref length, null) == 122 && length is > 0 and < 32768)
                {
                    var id = new StringBuilder((int)length);
                    if (Native.GetApplicationUserModelId(handle, ref length, id) == 0) appId = id.ToString();
                }
                if (Native.GetProcessTimes(handle, out var creation, out _, out _, out _)) started = creation;
            }
            finally { Native.CloseHandle(handle); }
        try { using var process = Process.GetProcessById(pid); name = process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return (exe, appId, name, started);
    }

    public string? ResolveExecutable(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim('"'));
        if (command.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(command))
        {
            object? link = null;
            try
            {
                link = new Native.ShellLink(); ((IPersistFile)link).Load(command, 0);
                var buffer = new StringBuilder(32768); ((Native.IShellLink)link).GetPath(buffer, buffer.Capacity, 0, 0);
                command = buffer.ToString();
            }
            catch (COMException) { return null; }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); }
        }
        if (Path.IsPathRooted(command)) return File.Exists(command) && Path.GetExtension(command).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(command) : null;
        var name = Path.HasExtension(command) ? command : command + ".exe";
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + name);
            if (key?.GetValue("") is string path && File.Exists(path.Trim('"'))) return Path.GetFullPath(path.Trim('"'));
        }
        var found = new StringBuilder(32768);
        return Native.SearchPath(null, command, ".exe", (uint)found.Capacity, found, 0) is > 0 and < 32768 &&
               Path.GetExtension(found.ToString()).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? found.ToString() : null;
    }

    public bool IsCurrent(WindowSnapshot w)
    {
        NativeMethods.GetWindowThreadProcessId(w.Handle, out var pid);
        if (pid != w.ProcessId || w.ProcessStarted == 0) return false;
        var identity = Identity((int)pid);
        var name = new StringBuilder(256); Native.GetClassName(w.Handle, name, name.Capacity);
        return identity.Started == w.ProcessStarted && string.Equals(identity.Exe, w.Executable, StringComparison.OrdinalIgnoreCase) && name.ToString() == w.ClassName;
    }
    public bool IsResponsive(WindowSnapshot w) => IsCurrent(w) && Native.SendMessageTimeout(w.Handle, 0, 0, 0, 2 | 0x20, 100, out _) != 0;
    public string? Place(WindowSnapshot w, PixelRect bounds, WindowShowState state)
    {
        if (!IsCurrent(w)) return "Window identity changed; reattach it.";
        var monitors = Monitors(); if (monitors.Count == 0) return "No desktop monitors are available.";
        var m = WorkspaceLayout.MonitorFor(bounds, monitors);
        var placement = new Native.Placement { Length = Marshal.SizeOf<Native.Placement>(), Flags = 4, Show = state == WindowShowState.Maximized ? 3 : state == WindowShowState.Minimized ? 7 : 4,
            Normal = new Native.Rect { Left = bounds.X - m.WorkArea.X + m.Bounds.X, Top = bounds.Y - m.WorkArea.Y + m.Bounds.Y,
                Right = bounds.Right - m.WorkArea.X + m.Bounds.X, Bottom = bounds.Bottom - m.WorkArea.Y + m.Bounds.Y } };
        return Native.SetWindowPlacement(w.Handle, ref placement) ? null : Error("Placement denied");
    }
    public string? Show(WindowSnapshot w, WindowShowState state) => Place(w, w.NormalBounds, state);
    public string? Focus(WindowSnapshot w)
    {
        if (!IsCurrent(w)) return "Window identity changed; reattach it.";
        return Native.SetForegroundWindow(w.Handle) ? null : "Windows denied foreground focus.";
    }
    private static string Error(string message) => message + " (Windows error " + Marshal.GetLastWin32Error() + ").";

    private static class Native
    {
        internal delegate bool EnumProc(nint handle, nint data);
        internal delegate bool MonitorProc(nint monitor, nint dc, ref Rect rect, nint data);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public readonly PixelRect Pixels => new(Left, Top, Right - Left, Bottom - Top); }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint window, out Rect rectangle);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Placement { public int Length, Flags, Show; public Point Min, Max; public Rect Normal; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public int Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumWindows(EnumProc callback, nint data);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc callback, nint data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool EnumDisplayDevices(string device, uint number, ref DisplayDevice info, uint flags);
        [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint window, uint flags);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint window, uint command);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(nint window);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder text, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder text, int size);
        [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(nint window, ref Placement placement);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPlacement(nint window, ref Placement placement);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
        [DllImport("user32.dll")] internal static extern nint SendMessageTimeout(nint window, uint message, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder? id);
        [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern uint SearchPath(string? path, string file, string extension, uint size, StringBuilder result, nint part);
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] internal class ShellLink { }
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellLink { void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, nint data, uint flags); }
    }
}
