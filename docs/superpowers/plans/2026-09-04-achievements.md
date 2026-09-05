# Achievements Implementation Plan

**Goal:** Implement the approved achievements system including existing viewers.
**Architecture:** Pure achievement evaluation over JSON; integration saves awards and rewards
through the existing inventory transaction. Vanilla UI and existing overlay consume safe JSON.
**Tech Stack:** .NET 9, vanilla JavaScript, existing smoke and Node test harnesses.
**Spec:** ../specs/2026-09-04-achievements-design.md

## Constraints
No new dependencies, no live-data testing, no hosted backend. Preserve backup-before-write,
local API protections, profile isolation and the redemption workflow. Release versions increase.

- [x] Engine: AchievementEngine.cs with validated definitions, permanent records, progress and
  checked rewards. AchievementTests.cs covers collection thresholds, backfill and idempotence.
- [x] Integration: CircuitService.Achievements.cs for settings, startup migration and retry;
  Core.cs for redemption/chat; AnalyticsRoles.cs for progress; Program.cs for routes/startup.
  Link new sources in existing test projects; test startup all profiles and failed saves.
- [x] UI: achievement rule editor, progress and command fields in admin; dirty-state guards;
  overlay celebration with sample preview and reduced motion. Run admin tests and browser QA.
- [x] Review: inventory-safety/security review of writes, validation and imports; smoke, cloud,
  Twitch and admin regressions. Fix any findings and document usage and compatibility.
- [x] Package: bump four version locations for compatible feature release, build and inspect
  data-free installer. Commit reviewed work. Public publication remains a separate action.
