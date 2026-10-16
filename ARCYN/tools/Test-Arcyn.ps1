param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\.arcyn-local\tests'))
$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot '..\ARCYN.Tests\ARCYN.Tests.csproj') -c Release -r win-x64 --self-contained true -o $OutputDirectory
if ($LASTEXITCODE) { throw 'Test build failed.' }
& (Join-Path $OutputDirectory 'ARCYN.Tests.exe')
if ($LASTEXITCODE) { throw 'Headless regression checks failed.' }
