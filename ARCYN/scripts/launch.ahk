#SingleInstance
#Requires AutoHotkey v2.0

TraySetIcon("imageres.dll", 109)

!+d::
{
    exePath := A_ScriptDir "\..\ARCYN.exe"
    if FileExist(exePath)
        Run(exePath)
    else
        TrayTip("ARCYN not found at:" exePath)
}
Esc::
{
    WinClose("ahk_exe ARCYN.exe")
}
