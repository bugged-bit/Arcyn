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
Write-Output 'Four invalid runtime metadata cases rejected before helper execution; original bytes restored.'
