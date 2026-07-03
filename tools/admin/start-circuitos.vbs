Option Explicit
Dim shell, fso, basePath, runtimePath, dataPath, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
basePath = fso.GetParentFolderName(WScript.ScriptFullName)
runtimePath = fso.BuildPath(basePath, "runtime\CircuitOS.exe")
dataPath = fso.GetAbsolutePathName(fso.BuildPath(basePath, "..\..\data"))
command = Quote(runtimePath) & " --data " & Quote(dataPath) & " --ui " & Quote(basePath)
shell.Run command, 1, False

Function Quote(value)
    Quote = Chr(34) & value & Chr(34)
End Function
