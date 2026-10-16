param([switch]$EnableHotkeyHelper, [switch]$DisableHotkeyHelper, [string]$Hotkey = 'ALT+SHIFT+D')
$ErrorActionPreference = 'Stop'
$directory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable = Join-Path $directory 'ARCYN.exe'
if (!(Test-Path -LiteralPath $executable)) { throw "ARCYN.exe is missing from $directory. Extract the complete release ZIP first." }
$shell = New-Object -ComObject WScript.Shell
try {
    foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
        $shortcut = $shell.CreateShortcut((Join-Path $folder 'ARCYN.lnk'))
        $shortcut.TargetPath = $executable
        $shortcut.WorkingDirectory = $directory
        $shortcut.Description = 'ARCYN workspace launcher'
        $shortcut.Save()
    }
    $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'ARCYN Hotkey.lnk'
    $helper = Join-Path $directory 'ARCYN.Hotkey.exe'
    if ($DisableHotkeyHelper) {
        if (Test-Path -LiteralPath $startup) { Remove-Item -LiteralPath $startup }
        if (Test-Path -LiteralPath $helper) { & $helper --stop }
    }
    elseif ($EnableHotkeyHelper) {
        if ($Hotkey -notmatch '^(?:(?:ALT|CTRL|SHIFT)\+)+[A-Z0-9]$' -or $Hotkey -notmatch 'ALT|CTRL') { throw 'Use Ctrl or Alt with A-Z/0-9; Win combinations and F12 are reserved.' }
        if (!(Test-Path -LiteralPath $helper)) { throw 'ARCYN.Hotkey.exe is missing.' }
        $shortcut = $shell.CreateShortcut($startup)
        $shortcut.TargetPath = $helper
        $shortcut.Arguments = '--hotkey ' + $Hotkey
        $shortcut.WorkingDirectory = $directory
        $shortcut.Save()
        Write-Output 'Optional helper enabled for next sign-in. Double-click ARCYN.Hotkey.exe to start it now with Alt+Shift+D.'
    }
    else { Write-Output 'Shortcuts created. Default mode leaves no ARCYN process running after the HUD closes.' }
}
finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
