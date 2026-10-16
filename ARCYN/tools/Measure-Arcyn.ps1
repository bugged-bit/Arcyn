param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\.arcyn-local\measurements'),
    [int]$Runs = 30,
    [int]$ExtractionColdRuns = 5,
    [string]$ConfigPath
)
$ErrorActionPreference = 'Stop'
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$hook = Join-Path $PSScriptRoot 'Probe\bin\Release\net8.0-windows\Probe.dll'
if (!(Test-Path -LiteralPath $hook)) { throw 'Build tools/Probe/Probe.csproj in Release first.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$results = @()
for ($run = 0; $run -lt ($Runs + $ExtractionColdRuns); $run++) {
    $kind = if ($run -lt $ExtractionColdRuns) { 'first-extraction' } else { 'warm' }
    $capture = Join-Path $OutputDirectory ('run-{0:D2}.json' -f $run)
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = Split-Path -Parent $Executable
    $start.Environment['DOTNET_STARTUP_HOOKS'] = $hook
    $start.Environment['ARCYN_PROBE_OUTPUT'] = $capture
    $cache = if ($kind -eq 'first-extraction') { "bundle-cold-$run" } else { 'bundle-warm' }
    $start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $OutputDirectory $cache
    if ($ConfigPath) { $start.ArgumentList.Add('--config'); $start.ArgumentList.Add([IO.Path]::GetFullPath($ConfigPath)) }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $privatePeak = 0L
    $workingPeak = 0L
    $handlePeak = 0
    while (!$process.WaitForExit(100)) {
        $process.Refresh()
        $privatePeak = [Math]::Max($privatePeak, $process.PrivateMemorySize64)
        $workingPeak = [Math]::Max($workingPeak, $process.WorkingSet64)
        $handlePeak = [Math]::Max($handlePeak, $process.HandleCount)
        if ($clock.Elapsed.TotalSeconds -gt 45) {
            $process.CloseMainWindow() | Out-Null
            throw "Run $run exceeded 45 seconds; no process was killed. Check the app window."
        }
    }
    $row = [ordered]@{ Run = $run; Kind = $kind; ExitCode = $process.ExitCode; ElapsedMs = $clock.Elapsed.TotalMilliseconds; PrivatePeakBytes = $privatePeak; WorkingPeakBytes = $workingPeak; HandlePeak = $handlePeak }
    $process.Dispose()
    if (Test-Path -LiteralPath $capture) { $row.Probe = Get-Content -LiteralPath $capture -Raw | ConvertFrom-Json }
    $results += [pscustomobject]$row
    $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'runs.json')
    Write-Output ('{0}: {1}, exit {2}, private peak {3:N1} MiB' -f $run, $kind, $row.ExitCode, ($privatePeak / 1MB))
}
[ordered]@{
    RecordedAt = (Get-Date).ToString('o'); Executable = $Executable
    SHA256 = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
    OS = [Environment]::OSVersion.VersionString; LogicalProcessors = [Environment]::ProcessorCount
    ColdDefinition = 'Fresh bundle extraction directory; OS caches are not cleared and the machine is not rebooted.'
    FrameMetric = 'WPF rendering callback cadence, not GPU presentation time.'
    Input = 'Probe suppresses keyboard and click dispatch; no targets are launched.'
    Runs = $results
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'baseline.json')
