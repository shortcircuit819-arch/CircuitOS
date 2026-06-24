# CircuitOS Versioning

CircuitOS separates application, data-schema, and Streamer.bot integration
versions. The application version describes the shipped desktop experience;
the other versions change only when their contracts change.

## Pre-1.0 Releases

- The **milestone** number (second part — `0.5`, `0.6`, `0.7`) represents a
  major feature area. It only advances when the current milestone is fully
  satisfactory, not on a fixed schedule.
- Work within a milestone uses a **four-part** version: `0.6.0.1`, `0.6.0.2`,
  and so on. The third part is a sub-feature; the fourth is an iteration within
  that sub-feature (including hotfixes).
- Early `0.3.x` patch releases contained bug fixes, UI polish, performance,
  documentation, diagnostics, or packaging improvements without changing the
  saved-data contract or introducing a major new workflow.
- Update packages remain data-free and must preserve existing installations.

The current milestone is **0.6 — Item Variants and Rarity Tiers** (complete as
of `0.6.0.6`). The next milestone is **0.7 — Cloud Platform + Twitch
Integration**.

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
2. Update the application version string in all five locations: the runtime
   `.csproj` (product/file/assembly), `Program.cs` (`/api/health`),
   `CircuitService.Core.cs` (`integrationVersion`), `CircuitService.Modules.cs`
   (`circuitosVersion`), and `README.md`.
3. Build and test the self-contained Windows x64 executable.
4. Build the fresh and data-free update archives.
5. Verify `version.json`, executable metadata, and ZIP data boundaries.
6. Write a `docs/patch-notes/v<version>.md` entry and update `HANDOFF.md`.
7. Sign and timestamp public stable releases before distribution.
