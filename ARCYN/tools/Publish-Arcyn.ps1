param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\publish\ARCYN-win-x64'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $PSScriptRoot '..\ARCYN.UI\ARCYN.UI.csproj'
$metadata = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup | Where-Object Version
$release = Join-Path (Split-Path -Parent $OutputDirectory) ('ARCYN-v' + $metadata.Version + '-win-x64')
$archive = $release + '.zip'
foreach ($target in @($OutputDirectory, $release, $archive, ($archive + '.sha256'))) {
    if (Test-Path -LiteralPath $target) { throw 'Choose an empty output directory to keep the artifact free of stale files.' }
}
dotnet publish $project -c Release -r win-x64 --self-contained true --interactive false -p:PublishSingleFile=false -o $OutputDirectory
if ($LASTEXITCODE) { throw 'Publish failed.' }
& (Join-Path $PSScriptRoot 'Build-Hotkey.ps1') -OutputDirectory (Join-Path $OutputDirectory 'native-build')
Copy-Item -LiteralPath (Join-Path $OutputDirectory 'native-build\ARCYN.Hotkey.exe') -Destination $OutputDirectory
# Keep build intermediates outside the distributable; ZIP an explicit release staging directory.
New-Item -ItemType Directory -Path $release | Out-Null
Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $release
foreach ($folder in Get-ChildItem -LiteralPath $OutputDirectory -Directory | Where-Object Name -ne 'native-build') { Copy-Item -LiteralPath $folder.FullName -Destination $release -Recurse }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\scripts') -Destination $release -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\LICENSE'), (Join-Path $PSScriptRoot '..\..\README.md'), (Join-Path $PSScriptRoot '..\..\CONTRIBUTING.md') -Destination $release
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Notices') -Destination $release -Recurse
New-Item -ItemType Directory -Path (Join-Path $release 'docs\performance') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\release-notes.md') -Destination (Join-Path $release 'docs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\guide.md') -Destination (Join-Path $release 'docs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\images') -Destination (Join-Path $release 'docs') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\implementation-validation.md'), (Join-Path $PSScriptRoot '..\..\docs\live-validation.md') -Destination (Join-Path $release 'docs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\previews') -Destination (Join-Path $release 'docs') -Recurse
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\performance') -Filter '*.json' -File | Copy-Item -Destination (Join-Path $release 'docs\performance')
& (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $release
Compress-Archive -LiteralPath $release -DestinationPath $archive
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $files = @(Get-ChildItem -LiteralPath $release -Recurse -File)
    if (@($zip.Entries | Where-Object Name).Count -ne $files.Count) { throw 'ZIP file count differs from the validated package.' }
    foreach ($file in $files) {
        $name = [IO.Path]::GetFileName($release) + '/' + [IO.Path]::GetRelativePath($release, $file.FullName).Replace('\', '/')
        $entry = $zip.GetEntry($name)
        if (!$entry) { throw "ZIP is missing $name" }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "ZIP content differs: $name" }
    }
}
finally { $zip.Dispose() }
$checksum = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
($checksum + '  ' + [IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
Write-Output ("Verified $($files.Count) packaged files. Complete standalone ZIP: " + $archive)
Write-Output ('SHA-256: ' + $checksum)
