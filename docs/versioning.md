# CircuitOS Versioning

CircuitOS separates application and data-schema versions. The application version
describes the shipped desktop experience; the data-schema version changes only
when its contract changes.

## Versioning scheme — 3-part SemVer (from 0.9)

**As of 0.9, CircuitOS uses 3-part SemVer2 (`MAJOR.MINOR.PATCH`).** This is required by the distribution
tooling: Velopack / the in-app updater packages releases as SemVer2 and rejects a 4-part string outright
(`0.9.0.1` is invalid; `0.9.1` is valid). Since the installer + auto-updater are now the shipping path,
the version has to be what they accept.

- The **milestone** (minor, second part — `0.7`, `0.8`, `0.9`) is a major feature area; it advances only
  when the current milestone is fully satisfactory.
- The **patch** (third part) covers everything within a milestone: sub-features, hotfixes, and hardening
  iterations alike. Bump it for each meaningful shipped change (`0.9.1`, `0.9.2`, …).

### Pre-0.9 history (retired 4-part scheme)

Milestones 0.3–0.8 used a **four-part** version (`0.6.0.1`, `0.7.3.1`, `0.9.0.1`): third part = sub-feature,
fourth = an iteration/hotfix within it. Those tags stay in history; `0.9.1` supersedes the interim
`0.9.0.1`. The 4-part scheme predates the SemVer-based installer and is not used going forward.
- Early `0.3.x` patch releases contained bug fixes, UI polish, performance,
  documentation, diagnostics, or packaging improvements without changing the
  saved-data contract or introducing a major new workflow.
- Update packages remain data-free and must preserve existing installations.

The current milestone is **0.9 — Distribution & Release Candidate** (shipped as
`0.9.0`: Velopack installer, in-app auto-updates, and a signed release pipeline).
Preceding milestones: **0.7 — Native Twitch + Cloud Foundation** (`0.7.3.1`) and
**0.8 — Design & Identity** (`0.8.1`). The next release is **1.0** — see below.

## Stable Releases

Version `1.0.0` is reserved for the first signed stable public release. It
requires a feature freeze, fresh-install and update testing, saved-data migration
coverage, complete onboarding and recovery documentation, and trusted code
signing.

Version `2.0.0` is reserved for Shop and Currency Workshop functionality,
including configurable currencies, purchases, perks, currency sinks, fulfillment
workflows, audit history, and economy safeguards.

## Release Checklist

1. Select the version based on the user-visible and compatibility impact.
2. Update the application version string in all four locations: the runtime
   `.csproj` (product/file/assembly), `Program.cs` (`/api/health`),
   `CircuitService.Modules.cs` (`circuitosVersion`), and `README.md`.
3. Build and test the self-contained Windows x64 executable.
4. Build the fresh and data-free update archives.
5. Verify `version.json`, executable metadata, and ZIP data boundaries.
6. Write a `docs/patch-notes/v<version>.md` entry and update `HANDOFF.md`.
7. Sign and timestamp public stable releases before distribution.
