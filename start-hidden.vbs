' RaiDrive AdGuard - opens the ad popup window without any visual flash
' Keeps the WindowStyle hidden until it is safe to show, then closes if it is an ad.
Option Explicit

Dim fso, shell, exePath, proc, guard
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

exePath = fso.BuildPath(fso.GetParentFolderName(WScript.ScriptFullName), "RaiDriveAdGuard.exe")
If Not fso.FileExists(exePath) Then
    WScript.Echo "RaiDriveAdGuard.exe not found: " & exePath
    WScript.Quit 1
End If

shell.Run Chr(34) & exePath & Chr(34), 0, False
