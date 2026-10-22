param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
if (!('ArcynArtifactResources' -as [type])) {
    Add-Type @'
using System;
using System.Linq;
using System.Runtime.InteropServices;
public static class ArcynArtifactResources {
    private delegate bool NameCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern bool EnumResourceNames(IntPtr module, IntPtr type, NameCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    private static byte[] Read(IntPtr module, IntPtr name, int type) {
        var resource = FindResource(module, name, new IntPtr(type));
        if (resource == IntPtr.Zero) return Array.Empty<byte>();
        var data = LockResource(LoadResource(module, resource));
        if (data == IntPtr.Zero) return Array.Empty<byte>();
        var bytes = new byte[SizeofResource(module, resource)]; Marshal.Copy(data, bytes, 0, bytes.Length); return bytes;
    }
    public static bool MatchesIcon(string path, byte[] icon) {
        var sizes = new[] { 16,20,24,32,40,48,64,96,128,256 };
        if (icon.Length < 6 + sizes.Length * 16 || BitConverter.ToUInt16(icon, 2) != 1 || BitConverter.ToUInt16(icon, 4) != sizes.Length) return false;
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x22); if (module == IntPtr.Zero) return false;
        try {
            bool found = false;
            EnumResourceNames(module, new IntPtr(14), (m,t,n,p) => {
                var group = Read(m, n, 14);
                if (group.Length != 6 + sizes.Length * 14 || BitConverter.ToUInt16(group, 4) != sizes.Length) return true;
                for (int i = 0; i < sizes.Length; i++) {
                    int source = 6 + i * 16, entry = 6 + i * 14;
                    int dimension = sizes[i] == 256 ? 0 : sizes[i];
                    if (icon[source] != dimension || icon[source + 1] != dimension || !icon.Skip(source).Take(12).SequenceEqual(group.Skip(entry).Take(12))) return true;
                    var frame = Read(m, new IntPtr(BitConverter.ToUInt16(group, entry + 12)), 3);
                    int length = BitConverter.ToInt32(icon, source + 8), offset = BitConverter.ToInt32(icon, source + 12);
                    if (offset < 0 || length < 0 || offset > icon.Length - length || frame.Length != length || !frame.SequenceEqual(icon.Skip(offset).Take(length))) return true;
                }
                found = true; return false;
            }, IntPtr.Zero);
            return found;
        }
        finally { FreeLibrary(module); }
    }
}
'@
}
$project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\ARCYN.UI\ARCYN.UI.csproj') -Raw)
$metadata = $project.Project.PropertyGroup | Where-Object Version
$icon = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\ARCYN.UI\Assets\ARCYN.ico'))
foreach ($binary in @('ARCYN.exe', 'ARCYN.dll', 'ARCYN.Hotkey.exe')) {
    $path = [IO.Path]::GetFullPath((Join-Path $Directory $binary))
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    if ($version.ProductName -ne 'ARCYN' -or $version.FileVersion -ne $metadata.FileVersion -or $version.ProductVersion -notlike ($metadata.Version + '*')) { throw "Incorrect ARCYN product/version metadata: $binary" }
    if ($binary -ne 'ARCYN.dll' -and ![ArcynArtifactResources]::MatchesIcon($path, $icon)) { throw "Missing or changed ARCYN icon frames: $binary" }
}
foreach ($notice in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\Notices') -File) {
    $copy = Join-Path $Directory ('Notices\' + $notice.Name)
    if (!(Test-Path -LiteralPath $copy) -or (Get-FileHash -LiteralPath $copy).Hash -ne (Get-FileHash -LiteralPath $notice.FullName).Hash) { throw ('Missing or changed redistribution notice: ' + $notice.Name) }
}
$excluded = Get-ChildItem -LiteralPath $Directory -Recurse -File | Where-Object { $_.Extension -in @('.pdb','.ahk','.mp4','.obj','.res') -or $_.Name -match '^(Probe|ARCYN.Tests|session-ses_)' -or $_.Name -in @('hotkey.c','hotkey.rc','build.cmd') }
if ($excluded) { throw ('Development artifacts in release: ' + ($excluded.Name -join ', ')) }
foreach ($configFile in @('example.arcyn.json', 'arcyn.schema.json')) {
    $configPath = Join-Path $Directory $configFile
    if (!(Test-Path -LiteralPath $configPath)) { throw "Standalone artifact is missing $configFile" }
    Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json | Out-Null
}
foreach ($image in @('arcyn-icon.png','arcyn.svg','social-preview.png','social-preview.svg','social-workspaces.png')) {
    if (!(Test-Path -LiteralPath (Join-Path $Directory ('docs\images\' + $image)))) { throw "Standalone artwork is missing $image" }
}
$example = Get-Content -LiteralPath (Join-Path $Directory 'example.arcyn.json') -Raw | ConvertFrom-Json
if ($example.'$schema' -ne './arcyn.schema.json') { throw 'Example configuration must resolve its bundled schema.' }
foreach ($file in @('ARCYN.exe','ARCYN.dll','ARCYN.runtimeconfig.json','ARCYN.deps.json','coreclr.dll','hostfxr.dll','PresentationCore.dll','PresentationFramework.dll','PresentationFramework.Fluent.dll','PresentationNative_cor3.dll','vcruntime140_cor3.dll','wpfgfx_cor3.dll','PenImc_cor3.dll','D3DCompiler_47_cor3.dll','ARCYN.Hotkey.exe','README.md','CONTRIBUTING.md','LICENSE','docs\guide.md','docs\images\workspaces.png','docs\images\layout.png')) {
    if (!(Test-Path -LiteralPath (Join-Path $Directory $file))) { throw "Standalone artifact is missing $file" }
}
$runtime = Get-Content -LiteralPath (Join-Path $Directory 'ARCYN.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!$runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) { throw 'Artifact still requires an installed runtime.' }
$frameworks = @($runtime.runtimeOptions.includedFrameworks)
if ($frameworks.Count -ne 2 -or @($frameworks | Where-Object name -eq 'Microsoft.NETCore.App').Count -ne 1 -or @($frameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App').Count -ne 1) { throw 'Standalone artifact must include both .NET and Windows Desktop runtimes.' }
if ($frameworks | Where-Object version -ne $metadata.RuntimeFrameworkVersion) { throw 'Bundle the validated .NET 10 servicing runtime matching the included redistribution notices.' }
$dependencies = Get-Content -LiteralPath (Join-Path $Directory 'ARCYN.deps.json') -Raw | ConvertFrom-Json
$packages = $dependencies.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' }
if ($packages) { throw ('Unexpected application packages: ' + ($packages.Name -join ', ')) }
foreach ($personal in @('protocoll.json','arcyn.json')) {
    if (Test-Path -LiteralPath (Join-Path $Directory $personal)) { throw 'Do not ship personal configuration.' }
}
if (Get-ChildItem -LiteralPath $Directory -Recurse -Filter '*.ahk') { throw 'AutoHotkey dependency remains.' }
if ((Start-Process -FilePath (Join-Path $Directory 'ARCYN.Hotkey.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru).ExitCode) { throw 'Native helper self-test failed.' }
Write-Output 'Complete standalone runtime and package-free application verified without opening ARCYN.'
