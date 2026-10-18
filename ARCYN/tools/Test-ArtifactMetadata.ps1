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
Write-Output 'Eight invalid runtime/product/notice/development-artifact cases rejected before helper execution; original bytes restored.'
