Option Explicit
Dim shell, fso, basePath, runtimePath, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
basePath = fso.GetParentFolderName(WScript.ScriptFullName)
runtimePath = fso.BuildPath(basePath, "runtime\CircuitOS.exe")
command = Quote(runtimePath) & _
    " --data " & Quote("C:\Users\nicho\OneDrive\Documents\CircuitComponents") & _
    " --ui " & Quote(basePath)
shell.Run command, 1, False

Function Quote(value)
    Quote = Chr(34) & value & Chr(34)
End Function
