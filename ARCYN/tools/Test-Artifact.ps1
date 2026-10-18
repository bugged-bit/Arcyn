param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
if (!('ArcynArtifactResources' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ArcynArtifactResources {
    private delegate bool NameCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern bool EnumResourceNames(IntPtr module, IntPtr type, NameCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    public static bool HasIcon(string path) {
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x22); if (module == IntPtr.Zero) return false;
        try { bool found = false; EnumResourceNames(module, new IntPtr(14), (m,t,n,p) => { found = true; return false; }, IntPtr.Zero); return found; }
        finally { FreeLibrary(module); }
    }
}
'@
}
$project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\ARCYN.UI\ARCYN.UI.csproj') -Raw)
$metadata = $project.Project.PropertyGroup | Where-Object Version
foreach ($binary in @('ARCYN.exe', 'ARCYN.dll', 'ARCYN.Hotkey.exe')) {
    $path = [IO.Path]::GetFullPath((Join-Path $Directory $binary))
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    if ($version.ProductName -ne 'ARCYN' -or $version.FileVersion -ne $metadata.FileVersion -or $version.ProductVersion -notlike ($metadata.Version + '*')) { throw "Incorrect ARCYN product/version metadata: $binary" }
    if ($binary -ne 'ARCYN.dll' -and ![ArcynArtifactResources]::HasIcon($path)) { throw "Missing ARCYN icon resource: $binary" }
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
$example = Get-Content -LiteralPath (Join-Path $Directory 'example.arcyn.json') -Raw | ConvertFrom-Json
if ($example.'$schema' -ne './arcyn.schema.json') { throw 'Example configuration must resolve its bundled schema.' }
foreach ($file in @('ARCYN.exe','ARCYN.dll','ARCYN.runtimeconfig.json','ARCYN.deps.json','coreclr.dll','hostfxr.dll','PresentationCore.dll','PresentationFramework.dll','PresentationNative_cor3.dll','vcruntime140_cor3.dll','wpfgfx_cor3.dll','PenImc_cor3.dll','D3DCompiler_47_cor3.dll','ARCYN.Hotkey.exe')) {
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
& (Join-Path $Directory 'ARCYN.Hotkey.exe') --self-test
if ($LASTEXITCODE) { throw 'Native helper self-test failed.' }
Write-Output 'Complete standalone runtime and package-free application verified without opening ARCYN.'
