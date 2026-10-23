param([switch]$EnableHotkeyHelper, [switch]$DisableHotkeyHelper, [string]$Hotkey = 'ALT+SHIFT+D')
$ErrorActionPreference = 'Stop'
if ($EnableHotkeyHelper -and $DisableHotkeyHelper) { throw 'Choose either enable or disable for the optional helper.' }
$Hotkey = $Hotkey.ToUpperInvariant()
if ($EnableHotkeyHelper) {
    $parts = $Hotkey.Split('+')
    if ($Hotkey -cnotmatch '^(?:(?:ALT|CTRL|SHIFT)\+)+[A-Z0-9]$' -or $Hotkey -cnotmatch 'ALT|CTRL' -or @($parts | Select-Object -Unique).Count -ne $parts.Count) { throw 'Use unique Ctrl/Alt/Shift modifiers followed by A-Z/0-9; include Ctrl or Alt. Win combinations and F12 are reserved.' }
}
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
        $shortcut.IconLocation = $executable + ',0'
        $shortcut.Save()
    }
    $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'ARCYN Hotkey.lnk'
    $helper = Join-Path $directory 'ARCYN.Hotkey.exe'
    if ($DisableHotkeyHelper) {
        if (Test-Path -LiteralPath $startup) { Remove-Item -LiteralPath $startup }
        if (Test-Path -LiteralPath $helper) { & $helper --stop }
    }
    elseif ($EnableHotkeyHelper) {
        if (!(Test-Path -LiteralPath $helper)) { throw 'ARCYN.Hotkey.exe is missing.' }
        $shortcut = $shell.CreateShortcut($startup)
        $shortcut.TargetPath = $helper
        $shortcut.Arguments = '--hotkey ' + $Hotkey
        $shortcut.WorkingDirectory = $directory
        $shortcut.IconLocation = $executable + ',0'
        $shortcut.Save()
        Write-Output "Optional helper enabled for next sign-in with $Hotkey. No helper was started."
    }
    else { Write-Output 'Shortcuts created. Default mode leaves no ARCYN process running after the HUD closes.' }
}
finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
