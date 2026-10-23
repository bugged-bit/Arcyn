param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
function Find-Bytes([byte[]]$Bytes, [byte[]]$Pattern) {
    for ($i = 0; $i -le $Bytes.Length - $Pattern.Length; $i++) {
        if ($Bytes[$i] -ne $Pattern[0]) { continue }
        $matches = $true
        for ($j = 1; $j -lt $Pattern.Length; $j++) { if ($Bytes[$i + $j] -ne $Pattern[$j]) { $matches = $false; break } }
        if ($matches) { return $i }
    }
    throw 'Binary metadata fixture pattern not found.'
}
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
$helper = Join-Path $Directory 'ARCYN.Hotkey.exe'
$originalHelper = [IO.File]::ReadAllBytes($helper)
$fixedInfo = Find-Bytes $originalHelper ([byte[]]@(0xBD, 0x04, 0xEF, 0xFE))
try {
    foreach ($flag in @(1, 2, 4, 8, 32)) {
        $changedHelper = $originalHelper.Clone()
        [BitConverter]::GetBytes([uint32]$flag).CopyTo($changedHelper, $fixedInfo + 28)
        [IO.File]::WriteAllBytes($helper, $changedHelper)
        try { & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory; throw 'Unstable helper flag accepted.' }
        catch { if ($_.Exception.Message -ne 'Unstable ARCYN version flags: ARCYN.Hotkey.exe') { throw } }
    }
}
finally { [IO.File]::WriteAllBytes($helper, $originalHelper) }
try {
    $product = Find-Bytes $originalHelper ([Text.Encoding]::Unicode.GetBytes("1.6.0`0"))
    $changedHelper = $originalHelper.Clone(); $changedHelper[$product + 10] = 0x2D
    [IO.File]::WriteAllBytes($helper, $changedHelper)
    if (![Diagnostics.FileVersionInfo]::GetVersionInfo([IO.Path]::GetFullPath($helper)).ProductVersion.StartsWith('1.6.0-')) { throw 'Stale product-version fixture was not created.' }
    try { & (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $Directory; throw 'Version prefix accepted instead of exact stable version.' }
    catch { if ($_.Exception.Message -ne 'Incorrect ARCYN product/version metadata: ARCYN.Hotkey.exe') { throw } }
}
finally { [IO.File]::WriteAllBytes($helper, $originalHelper) }
$icon = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\ARCYN.UI\Assets\ARCYN.ico'))
$frameOffset = [BitConverter]::ToInt32($icon, 18)
$frame = $icon[$frameOffset..($frameOffset + 31)]
$location = Find-Bytes $originalExe $frame
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
Write-Output 'Sixteen invalid runtime/product/stable-flag/icon/notice/development-artifact/helper cases rejected; original bytes restored.'
