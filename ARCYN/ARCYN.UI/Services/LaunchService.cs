using System.Diagnostics;
using System.IO;
using ARCYN.UI.Models;

namespace ARCYN.UI.Services;

public static class LaunchService
{
    public static ProcessStartInfo CreateStartInfo(TargetItem target)
    {
        var workingDir = Environment.CurrentDirectory;

        if (target.Kind == TargetKind.Folder)
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = QuotePath(target.LaunchArg),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = workingDir
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
                WorkingDirectory = workingDir
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
                WorkingDirectory = workingDir
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
                WorkingDirectory = workingDir
            };
        }

        var ext = System.IO.Path.GetExtension(cmd);
        if (File.Exists(cmd) && ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var dir = System.IO.Path.GetDirectoryName(cmd) ?? workingDir;
            return new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = string.Empty,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = dir
            };
        }

        if (File.Exists(cmd))
        {
            var dir = System.IO.Path.GetDirectoryName(cmd) ?? workingDir;
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
            WorkingDirectory = workingDir
        };
    }

    public static string QuotePath(string path)
    {
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }
}
