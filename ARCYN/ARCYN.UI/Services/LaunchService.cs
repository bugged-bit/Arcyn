using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ARCYN.UI.Models;
namespace ARCYN.UI.Services;

public static class LaunchService
{
    public static bool TryPrepare(TargetConfig target, out ProcessStartInfo psi, out string? error)
    {
        psi = null!; error = null;
        try
        {
            var cmd = Environment.ExpandEnvironmentVariables(target.Cmd).Trim();
            var arguments = target.Args.ToList();
            if (cmd.Length == 0) throw new ArgumentException("Empty command. Edit this target.");
            if (cmd.StartsWith('"') && cmd.EndsWith('"') && File.Exists(cmd[1..^1])) cmd = cmd[1..^1];
            if (target.LegacyCommandLine && !File.Exists(cmd) && !Directory.Exists(cmd) && !IsProtocol(cmd))
            {
                if (Path.IsPathRooted(cmd) && !cmd.StartsWith('"') && cmd.Contains(' '))
                    throw new ArgumentException("Ambiguous path. Select an executable and enter its arguments separately.");
                var parts = SplitCommandLine(cmd);
                cmd = parts[0]; arguments.InsertRange(0, parts.Skip(1));
            }
            var directory = Environment.ExpandEnvironmentVariables(target.WorkingDirectory ?? "");
            if (directory.Length > 0 && !Directory.Exists(directory)) throw new DirectoryNotFoundException("Working directory is unavailable.");
            if (Directory.Exists(cmd)) { arguments.Insert(0, cmd); cmd = "explorer.exe"; }
            if (cmd.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && arguments.Count > 0 && !Directory.Exists(Environment.ExpandEnvironmentVariables(arguments[0])))
                throw new DirectoryNotFoundException("Folder is unavailable. It remains saved for repair.");
            if (Path.IsPathRooted(cmd) && !File.Exists(cmd)) throw new FileNotFoundException("Target is unavailable.", cmd);
            psi = new ProcessStartInfo(cmd) { UseShellExecute = true, WorkingDirectory = directory.Length > 0 ? directory :
                File.Exists(cmd) ? Path.GetDirectoryName(Path.GetFullPath(cmd))! : Path.GetDirectoryName(Environment.ProcessPath)! };
            foreach (var argument in arguments) psi.ArgumentList.Add(argument);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { error = ex.Message; return false; }
    }
    private static bool IsProtocol(string cmd) => Uri.TryCreate(cmd, UriKind.Absolute, out var uri) && !uri.IsFile;
    public static string[] SplitCommandLine(string command)
    {
        var pointer = CommandLineToArgvW(command, out var count);
        if (pointer == IntPtr.Zero) throw new ArgumentException("Invalid command line.");
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(pointer); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}

