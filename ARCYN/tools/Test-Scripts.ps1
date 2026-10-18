$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = [xml](Get-Content -LiteralPath (Join-Path $root 'ARCYN\ARCYN.UI\ARCYN.UI.csproj') -Raw)
$manifest = [xml](Get-Content -LiteralPath (Join-Path $root 'ARCYN\ARCYN.UI\app.manifest') -Raw)
if ($manifest.assembly.assemblyIdentity.version -ne ($project.Project.PropertyGroup | Where-Object Version).FileVersion) { throw 'Application manifest and file version disagree.' }
$scripts = @(Get-ChildItem -LiteralPath (Join-Path $root 'ARCYN\tools'), (Join-Path $root 'ARCYN\scripts') -Filter '*.ps1' -Recurse -File)
foreach ($script in $scripts) {
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    if ($errors) { throw ($script.Name + ': ' + ($errors.Message -join '; ')) }
}
$xaml = @(Get-ChildItem -LiteralPath (Join-Path $root 'ARCYN\ARCYN.UI') -Filter '*.xaml' -Recurse -File | Where-Object FullName -NotMatch '\\(bin|obj)\\')
$keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in $xaml) {
    $document = [xml][IO.File]::ReadAllText($file.FullName)
    foreach ($node in $document.SelectNodes('//*[@*[local-name()="Key"]]')) { $null = $keys.Add($node.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')) }
}
# Platform resource names are resolved against the shipped Fluent dictionary by the headless WPF checks.
foreach ($key in @('DefaultButtonStyle','AccentButtonStyle','DefaultTextBoxStyle','DefaultComboBoxStyle','DefaultCheckBoxStyle','DefaultRadioButtonStyle','DefaultExpanderStyle','DefaultDataGridStyle','DefaultListBoxStyle','DefaultTabControlStyle')) { $null = $keys.Add($key) }
foreach ($file in $xaml) {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName), '\{(?:StaticResource|DynamicResource)\s+([^{}]+)\}')) {
        if (!$keys.Contains($match.Groups[1].Value.Trim())) { throw ($file.Name + ': unresolved resource ' + $match.Value) }
    }
}
Write-Output "$($xaml.Count) XAML documents parse and their resource keys resolve."
$fixture = Join-Path $root ('.arcyn-local\script-checks\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') -Force | Out-Null
try {
    $setup = Join-Path $fixture 'scripts\Setup-Shortcuts.ps1'
    Copy-Item -LiteralPath (Join-Path $root 'ARCYN\scripts\Setup-Shortcuts.ps1') -Destination $setup
    # No executable exists here. Valid input must stop before COM or desktop/startup writes.
    foreach ($hotkey in @('ALT+SHIFT+D', 'alt+shift+d', 'CTRL+7', 'SHIFT+CTRL+ALT+Z')) {
        try { & $setup -EnableHotkeyHelper -Hotkey $hotkey; throw 'Fixture unexpectedly reached shortcut setup.' }
        catch { if ($_.Exception.Message -notlike 'ARCYN.exe is missing*') { throw } }
    }
    foreach ($hotkey in @('WIN+L', 'ALT+F12', 'D', 'ALT++D', '+ALT+D', 'ALT+D+', 'D+ALT', 'ALT+ALT+D', 'SHIFT+D')) {
        try { & $setup -EnableHotkeyHelper -Hotkey $hotkey; throw 'Invalid shortcut accepted.' }
        catch { if ($_.Exception.Message -notlike 'Use unique Ctrl/Alt/Shift*') { throw } }
    }
    try { & $setup -EnableHotkeyHelper -DisableHotkeyHelper; throw 'Conflicting switches accepted.' }
    catch { if ($_.Exception.Message -notlike 'Choose either enable or disable*') { throw } }
    Write-Output "$($scripts.Count) PowerShell scripts parse; 14 isolated shortcut preflight cases passed without COM, registration or desktop writes."
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (!$resolved.StartsWith((Join-Path $root '.arcyn-local\script-checks\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped its workspace.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
