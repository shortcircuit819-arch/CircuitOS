# CircuitOS 0.7 Launch Punch List

This is the manually updated UI/UX ask list for the 0.7 launch path. Treat this
file as active product direction, not as a completed historical checklist.

## Global

- [Verified] Theme customization now feeds sidebar/topbar chrome variables and visually recolors the navigation shell.


## Overview

- [Verified] Action Center now has explicit spacing below it before Pull Rates and Catalog Reach.
- [Verified] Pull Rates slider is editable without card-level redirect; Tune in Rate Lab button still navigates.
- [Verified] Pull Rates slider fill now uses the raw range percentage without thumb-width compensation, preventing colored-fill overshoot past the handle.

## Configure

- Game Profile Circuit Components redemption name can be replaced with a dropdown from the user's redemptions or given the option to create a new one.
- [Done] Save Profile now also saves the catalog in the same click if catalog changes are pending. No more two-step save.
- [Done] Eyebrow label removed (was orphaned — only appeared in the Game Profile live preview, wired to nothing). Panel nickname kept (wired to sidebar profile widget + view title fallback).
- [Done] Overlay editor now has a State Overrides panel: switching to Rare/Complete/Duplicate preview tabs reveals per-state Accent, Label, and Bar Fill color pickers that override the global colors for that state only.
- [Done] Commands redesigned as a clean vertical label-row list. No more 4-column grid.



## Backups

- [Done] Backups section labels clarified ("What's covered" / "Available backups"), guide card copy simplified, empty state explains what to do.

## Twitch

- [Done in source + live-tested] Twitch settings has account status, reward dropdown attach/create/sync/edit/delete, persisted reward IDs, and native multi-profile routing.
- [Done in source] Login/logout, scope/re-login guidance, and attach-only reward explanation are present on the Twitch page.

