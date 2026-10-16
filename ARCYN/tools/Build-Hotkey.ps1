param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\native\bin'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Building the optional helper requires the Visual Studio C++ build tools (development only).' }
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\native\hotkey.c'))
$environmentScript = Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
$buildScript = Join-Path $OutputDirectory 'build.cmd'
@"
@echo off
call "$environmentScript" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b 1
cl /nologo /W4 /WX /O1 /MT /D_CRT_SECURE_NO_WARNINGS "$source" /Fo"$OutputDirectory\hotkey.obj" /Fe"$OutputDirectory\ARCYN.Hotkey.exe" /link /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT user32.lib shell32.lib advapi32.lib
exit /b %errorlevel%
"@ | Set-Content -LiteralPath $buildScript -Encoding ascii
& $env:ComSpec /d /c $buildScript
if ($LASTEXITCODE) { throw 'Native helper compilation failed.' }
& (Join-Path $OutputDirectory 'ARCYN.Hotkey.exe') --self-test
if ($LASTEXITCODE) { throw 'Hotkey parsing self-test failed.' }
