param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
foreach ($file in @('ARCYN.exe','ARCYN.dll','ARCYN.runtimeconfig.json','ARCYN.deps.json','coreclr.dll','hostfxr.dll','PresentationCore.dll','PresentationFramework.dll','PresentationNative_cor3.dll','vcruntime140_cor3.dll','wpfgfx_cor3.dll','PenImc_cor3.dll','D3DCompiler_47_cor3.dll','ARCYN.Hotkey.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $Directory $file))) { throw "Standalone artifact is missing $file" }
}
$runtime = Get-Content -LiteralPath (Join-Path $Directory 'ARCYN.runtimeconfig.json') -Raw | ConvertFrom-Json
if (!$runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) { throw 'Artifact still requires an installed runtime.' }
if ($runtime.runtimeOptions.includedFrameworks | Where-Object { [version]$_.version -lt [version]'8.0.32' }) { throw 'Bundled runtime is older than the validated servicing patch.' }
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
