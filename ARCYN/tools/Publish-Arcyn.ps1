param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\publish\ARCYN-win-x64'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose an empty output directory to keep the artifact free of stale files.' }
$project = Join-Path $PSScriptRoot '..\ARCYN.UI\ARCYN.UI.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $OutputDirectory
if ($LASTEXITCODE) { throw 'Publish failed.' }
& (Join-Path $PSScriptRoot 'Build-Hotkey.ps1') -OutputDirectory (Join-Path $OutputDirectory 'native-build')
Copy-Item -LiteralPath (Join-Path $OutputDirectory 'native-build\ARCYN.Hotkey.exe') -Destination $OutputDirectory
# Keep build intermediates outside the distributable; ZIP an explicit release staging directory.
$release = $OutputDirectory + '-release'
if (Test-Path -LiteralPath $release) { throw 'Release staging directory already exists.' }
New-Item -ItemType Directory -Path $release | Out-Null
Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $release
foreach ($folder in Get-ChildItem -LiteralPath $OutputDirectory -Directory | Where-Object Name -ne 'native-build') { Copy-Item -LiteralPath $folder.FullName -Destination $release -Recurse }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\scripts') -Destination $release -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\LICENSE'), (Join-Path $PSScriptRoot '..\..\README.md') -Destination $release
New-Item -ItemType Directory -Path (Join-Path $release 'docs\performance') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\implementation-validation.md') -Destination (Join-Path $release 'docs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\headless-validation.md') -Destination (Join-Path $release 'docs')
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\..\docs\performance') -Filter '*.json' -File | Copy-Item -Destination (Join-Path $release 'docs\performance')
& (Join-Path $PSScriptRoot 'Test-Artifact.ps1') -Directory $release
Compress-Archive -LiteralPath $release -DestinationPath ($release + '.zip')
Write-Output ('Complete standalone ZIP: ' + $release + '.zip')
