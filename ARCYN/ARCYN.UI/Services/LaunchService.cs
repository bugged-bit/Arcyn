using System.Diagnostics;
using System.IO;
using ARCYN.UI.Models;

namespace ARCYN.UI.Services;

public static class LaunchService
{
    public static ProcessStartInfo CreateStartInfo(TargetItem target)
    {
        if (target.Kind == TargetKind.Folder)
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = QuotePath(target.LaunchArg),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = ResolveWorkingDir(target)
            };
        }

        if (target.Kind == TargetKind.Website)
        {
            return new ProcessStartInfo
            {
                FileName = target.LaunchCmd,
                Arguments = string.Empty,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = AppContext.BaseDirectory
            };
        }

        var cmd = target.LaunchCmd.Trim();

        if (cmd.Length >= 3 && cmd[1] == ':' && (cmd.EndsWith("\\") || cmd.EndsWith("/")))
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = QuotePath(cmd),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = ResolveWorkingDir(target)
            };
        }

        if (Directory.Exists(cmd))
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = QuotePath(cmd),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = ResolveWorkingDir(target)
            };
        }

        var dir = ResolveWorkingDir(target);

        var ext = System.IO.Path.GetExtension(cmd);
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || File.Exists(cmd))
        {
            return new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = string.Empty,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = dir
            };
        }

        return new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = string.Empty,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
            WorkingDirectory = dir
        };
    }

    private static string ResolveWorkingDir(TargetItem target)
    {
        if (target.Kind == TargetKind.Folder && !string.IsNullOrEmpty(target.LaunchArg))
        {
            if (Directory.Exists(target.LaunchArg))
                return target.LaunchArg;
        }

        if (target.Kind == TargetKind.App)
        {
            var cmd = target.LaunchCmd?.Trim();
            if (!string.IsNullOrEmpty(cmd) && File.Exists(cmd))
                return System.IO.Path.GetDirectoryName(cmd) ?? AppContext.BaseDirectory;
        }

        return AppContext.BaseDirectory;
    }

    public static string QuotePath(string path)
    {
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }
}
