using System.IO;
using Microsoft.Win32;
namespace ARCYN.UI.Services;

public sealed record DiscoveredApp(string Name, string Command);
public static class ApplicationDiscovery
{
    public static List<DiscoveredApp> Find(IEnumerable<string>? folders = null)
    {
        var apps = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders ?? new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var path in Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                apps.TryAdd(path, new(Path.GetFileNameWithoutExtension(path), path));
        }
        if (folders == null)
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    try
                    {
                        using var root = RegistryKey.OpenBaseKey(hive, view);
                        using var paths = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                        if (paths == null) continue;
                        foreach (var name in paths.GetSubKeyNames())
                        {
                            using var item = paths.OpenSubKey(name);
                            if (item?.GetValue("") is string cmd && File.Exists(cmd.Trim('"')))
                            { cmd = cmd.Trim('"'); apps.TryAdd(cmd, new(Path.GetFileNameWithoutExtension(name), cmd)); }
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
