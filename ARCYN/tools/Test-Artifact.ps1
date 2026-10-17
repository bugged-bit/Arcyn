param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
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
if ($frameworks | Where-Object { $version = [version]$_.version; $version.Major -ne 10 -or $version.Minor -ne 0 -or $version -lt [version]'10.0.13' }) { throw 'Bundle the validated .NET 10 LTS servicing runtime (10.0.13 or later within 10.0).' }
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
