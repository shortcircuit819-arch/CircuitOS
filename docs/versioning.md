# CircuitOS versioning

Current stable release: **1.0.4**. Version 1.0.0 shipped August 19, 2026. Application versions and saved-data schema versions are tracked separately.

## Version format

Use three-part MAJOR.MINOR.PATCH versions with Velopack. Patch releases cover fixes and hardening; minor releases cover compatible feature additions. Assess compatibility explicitly before major releases. The historical four-part prerelease scheme is no longer used for packages.

The 1.x line is current. Shop and Currency Workshop remains the proposed 2.0 direction; planned features have no promised ship dates.

## Release checklist

1. Review user-visible changes and saved-data compatibility.
2. Update the runtime .csproj product/file/assembly versions, Program.cs health version, CircuitService.Modules.cs export version, and README together.
3. Run the checks in [CONTRIBUTING.md](../CONTRIBUTING.md); validate fresh installs and updates with disposable data.
4. Build with tools/package/Build-CircuitOSVelopack.ps1. Verify installer, portable ZIP, update packages, and feed metadata. Packages must contain no user saves or credentials.
5. Publish patch notes and SHA-256 checksums; update the maintainer handoff log. Historical patch-note filenames vary: link to the actual file.
6. Verify the public release and update feed after publishing.

Current releases are unsigned. Signing is optional until a certificate is available; never describe unsigned builds as signed. See [release signing](release-signing.md). Documentation-only changes do not require a new application version or binary release.
