param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$configPath = Join-Path ([IO.Path]::GetFullPath($Directory)) 'ARCYN.runtimeconfig.json'
$original = [IO.File]::ReadAllBytes($configPath)
try {
    foreach ($kind in @('old-major','old-patch','missing-desktop','duplicate-core')) {
        $runtime = [Text.Encoding]::UTF8.GetString($original) | ConvertFrom-Json
        switch ($kind) {
            'old-major' { foreach ($framework in $runtime.runtimeOptions.includedFrameworks) { $framework.version = '8.0.32' } }
            'old-patch' { foreach ($framework in $runtime.runtimeOptions.includedFrameworks) { $framework.version = '10.0.12' } }
            'missing-desktop' { $runtime.runtimeOptions.includedFrameworks = @($runtime.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App') }
            'duplicate-core' { $runtime.runtimeOptions.includedFrameworks[1].name = 'Microsoft.NETCore.App' }
        }
        $runtime | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding utf8
        try { & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory; throw 'Invalid artifact metadata accepted.' }
        catch { if ($_.Exception.Message -notmatch 'Bundle the validated|must include both') { throw } }
    }
}
finally { [IO.File]::WriteAllBytes($configPath, $original) }
$executable = Join-Path $Directory 'ARCYN.exe'
$originalExe = [IO.File]::ReadAllBytes($executable)
try {
    [IO.File]::WriteAllBytes($executable, [IO.File]::ReadAllBytes((Join-Path $Directory 'coreclr.dll')))
    try { & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory; throw 'Invalid product metadata accepted.' }
    catch { if ($_.Exception.Message -notlike 'Incorrect ARCYN product/version metadata*') { throw } }
}
finally { [IO.File]::WriteAllBytes($executable, $originalExe) }
$icon = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\ARCYN.UI\Assets\ARCYN.ico'))
$frameOffset = [BitConverter]::ToInt32($icon, 18)
$frame = $icon[$frameOffset..($frameOffset + 31)]
$location = -1
for ($i = 0; $i -lt $originalExe.Length - $frame.Length; $i++) {
    if ($originalExe[$i] -ne $frame[0]) { continue }
    $matches = $true
    for ($j = 1; $j -lt $frame.Length; $j++) { if ($originalExe[$i + $j] -ne $frame[$j]) { $matches = $false; break } }
    if ($matches) { $location = $i; break }
}
if ($location -lt 0) { throw 'Icon corruption fixture could not locate the first embedded frame.' }
try {
    $changedExe = $originalExe.Clone(); $changedExe[$location + 32] = $changedExe[$location + 32] -bxor 1
    [IO.File]::WriteAllBytes($executable, $changedExe)
    try { & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory; throw 'Changed icon accepted.' }
    catch { if ($_.Exception.Message -ne 'Missing or changed ARCYN icon frames: ARCYN.exe') { throw } }
}
finally { [IO.File]::WriteAllBytes($executable, $originalExe) }
$notice = Join-Path $Directory 'Notices\JetBrains-Mono-2.304.txt'
$originalNotice = [IO.File]::ReadAllBytes($notice)
try {
    Remove-Item -LiteralPath $notice
    try { & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory; throw 'Missing notice accepted.' }
    catch { if ($_.Exception.Message -notlike 'Missing or changed redistribution notice*') { throw } }
}
finally { [IO.File]::WriteAllBytes($notice, $originalNotice) }
$probe = Join-Path $Directory 'Probe.dll'
if (Test-Path -LiteralPath $probe) { throw 'Negative fixture requires a clean release.' }
try {
    [IO.File]::WriteAllBytes($probe, @())
    try { & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory; throw 'Probe in release accepted.' }
    catch { if ($_.Exception.Message -notlike 'Development artifacts in release*') { throw } }
}
finally { Remove-Item -LiteralPath $probe }
$fluent = Join-Path $Directory 'PresentationFramework.Fluent.dll'
$originalFluent = [IO.File]::ReadAllBytes($fluent)
try {
    Remove-Item -LiteralPath $fluent
    try { & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory; throw 'Missing Fluent runtime accepted.' }
    catch { if ($_.Exception.Message -ne 'Standalone artifact is missing PresentationFramework.Fluent.dll') { throw } }
}
finally { [IO.File]::WriteAllBytes($fluent, $originalFluent) }
& {
    function Start-Process { [pscustomobject]@{ ExitCode = 42 } }
    try {
        & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory
        throw 'Failing helper self-test accepted.'
    }
    catch { if ($_.Exception.Message -ne 'Native helper self-test failed.') { throw } }
}
Write-Output 'Ten invalid runtime/product/icon/notice/development-artifact/helper cases rejected; original bytes restored.'
