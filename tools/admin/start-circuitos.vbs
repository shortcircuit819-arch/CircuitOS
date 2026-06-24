Option Explicit
Dim shell, fso, basePath, runtimePath, dataPath, actionPath, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
basePath = fso.GetParentFolderName(WScript.ScriptFullName)
runtimePath = fso.BuildPath(basePath, "runtime\CircuitOS.exe")
dataPath = fso.GetAbsolutePathName(fso.BuildPath(basePath, "..\..\data"))
actionPath = fso.GetAbsolutePathName(fso.BuildPath(basePath, "..\..\streamerbot-actions"))
command = Quote(runtimePath) & " --data " & Quote(dataPath) & " --ui " & Quote(basePath) & " --actions " & Quote(actionPath)
shell.Run command, 1, False

Function Quote(value)
    Quote = Chr(34) & value & Chr(34)
End Function
