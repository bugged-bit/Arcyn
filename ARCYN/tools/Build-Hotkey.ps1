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
$project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\ARCYN.UI\ARCYN.UI.csproj') -Raw)
$metadata = $project.Project.PropertyGroup | Where-Object Version
$version = [string]$metadata.FileVersion
$productVersion = [string]$metadata.Version
$icon = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\ARCYN.UI\Assets\ARCYN.ico')).Replace('\','\\')
@"
#include <windows.h>
1 ICON "$icon"
1 VERSIONINFO
FILEVERSION $($version.Replace('.',','))
PRODUCTVERSION $($version.Replace('.',','))
FILEFLAGSMASK 0x3fL
FILEFLAGS 0x0L
FILEOS VOS_NT_WINDOWS32
FILETYPE VFT_APP
BEGIN
 BLOCK "StringFileInfo"
 BEGIN
  BLOCK "040904b0"
  BEGIN
   VALUE "FileDescription", "ARCYN optional hotkey helper\0"
   VALUE "FileVersion", "$version\0"
   VALUE "ProductName", "ARCYN\0"
   VALUE "ProductVersion", "$productVersion\0"
   VALUE "OriginalFilename", "ARCYN.Hotkey.exe\0"
  END
 END
 BLOCK "VarFileInfo"
 BEGIN
  VALUE "Translation", 0x409, 1200
 END
END
"@ | Set-Content -LiteralPath (Join-Path $OutputDirectory 'hotkey.rc') -Encoding ascii
@"
@echo off
call "$environmentScript" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b 1
rc /nologo /fo "$OutputDirectory\hotkey.res" "$OutputDirectory\hotkey.rc"
if errorlevel 1 exit /b 1
cl /nologo /W4 /WX /O1 /MT /D_CRT_SECURE_NO_WARNINGS "$source" "$OutputDirectory\hotkey.res" /Fo"$OutputDirectory\hotkey.obj" /Fe"$OutputDirectory\ARCYN.Hotkey.exe" /link /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT user32.lib shell32.lib advapi32.lib
exit /b %errorlevel%
"@ | Set-Content -LiteralPath $buildScript -Encoding ascii
& $env:ComSpec /d /c $buildScript
if ($LASTEXITCODE) { throw 'Native helper compilation failed.' }
if ((Start-Process -FilePath (Join-Path $OutputDirectory 'ARCYN.Hotkey.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru).ExitCode) { throw 'Hotkey parsing self-test failed.' }
