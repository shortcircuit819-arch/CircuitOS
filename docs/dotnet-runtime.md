# CircuitOS .NET Runtime

CircuitOS uses a self-contained .NET 9 Windows application built on WinForms,
WebView2, `HttpListener`, and `System.Text.Json`. It binds only to `127.0.0.1`
and hosts the existing Control Core inside its own application window.

## Source

The project is located at `tools\runtime\CircuitOS.Runtime.csproj`. Runtime
services are split into configuration/profile generation, inventory analytics
and Discord roles, backup/recovery, and HTTP routing.

## Published Application

The self-contained Windows x64 single-file application is:

`tools\admin\runtime\CircuitOS.exe`

In recipient packages the same executable is placed at the ZIP root. With no
arguments it discovers `App` and `Data` beside itself, so the recipient can
launch `CircuitOS.exe` directly.

It accepts these command-line options:

- `--data <folder>`: live CircuitOS data folder
- `--ui <folder>`: Control Core static files
- `--port <number>`: loopback HTTP port, default `8787`
- `--headless`: run the local API without opening the CircuitOS window
- `--no-browser`: legacy alias for `--headless`

Use `start-admin.vbs` for the owner's live data or `start-circuitos.vbs` for a
portable package. These launchers start CircuitOS without displaying a command
window.

## Build

With the .NET 9 SDK installed:

```powershell
dotnet publish tools\runtime\CircuitOS.Runtime.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output tools\admin\runtime `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false
```

## Prerequisites

CircuitOS requires two system components that are not bundled inside the EXE:

**Microsoft Edge WebView2 Runtime** — renders the CircuitOS window. Already
present on most Windows 11 machines via Microsoft Edge. If CircuitOS shows
_"DLL was not found — ensure the Microsoft Edge WebView2 Runtime is installed"_
on launch, search for **"Microsoft Edge WebView2 Runtime download"** and run
the Evergreen Bootstrapper from Microsoft's site.

**Windows x64** — the published application targets `win-x64` only.

## Compatibility

The .NET runtime preserves the existing `/api/config`, `/api/profile`,
`/api/analytics`, `/api/roles`, `/api/backups`, `/api/save`, and `/api/health`
contracts.

Inventory remains read-only to the administration runtime. Catalog, boost,
profile, and role-acknowledgement writes continue using validation, temporary
files, atomic replacement, and timestamped backups.
