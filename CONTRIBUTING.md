# Contributing to CircuitOS

Bug reports, documentation improvements, and focused pull requests are welcome. Search existing issues first. For a large feature or new dependency, open an issue to discuss scope before building it.

## Source layout

- tools/runtime: .NET 9 Windows Forms runtime and local API.
- tools/admin: vanilla JavaScript admin interface; no frontend framework or build step.
- overlays/lower-quarter: OBS browser overlay.
- tools/package: installer and release scripts.
- docs: user guides and patch notes.

## Development and checks

Use Windows, the .NET 9 SDK, Node.js 22 or newer, and Microsoft Edge WebView2. Run from the repository root:

```powershell
dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release
dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data
dotnet run --project tools/cloud.tests/CircuitOS.Cloud.Tests.csproj -c Release -- --runtime tools/runtime/bin/Release/net9.0-windows/CircuitOS.dll
dotnet run --project tools/twitch.tests/CircuitOS.Twitch.Tests.csproj -c Release
node --test tools/admin.tests/editor-state.test.cjs
```

These match the checks in the [build workflow](.github/workflows/build.yml). Use disposable data for manual testing. Launch with an explicit --data path to that copy; never test against a streamer's live inventory. For UI-only changes, also verify the relevant screen manually.

## Pull requests

Explain the problem, resulting behavior, and validation. Keep changes focused. For documentation changes, verify paths, links, and instructions against the current release; a new binary version is unnecessary.

Preserve backup-before-write, validation, locking, and atomic inventory writes. Keep the admin API bound to 127.0.0.1 and retain its access protections. Do not commit tokens, credentials, private viewer data, or generated release packages. Disclose AI assistance where relevant and review generated changes before submission.

The core manual check is redemption → item pull → inventory save → chat and overlay announcement. Exercise it with disposable data when changing that workflow. See [versioning](docs/versioning.md) before preparing a release.

Contributions are provided under the project's [MIT License](LICENSE).
