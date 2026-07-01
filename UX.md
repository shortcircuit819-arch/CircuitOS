# CircuitOS UX Punch List (first-timer / non-technical pass)

Findings from a full walkthrough of the app as a brand-new, not-especially-technical user
(2026-06-29). Treat like the old `UI.md`: active product direction, worked top-down by tier.
Tier 1 = actively misleading a new user; Tier 2 = jargon; Tier 3 = polish.

## Tier 1 — Actively misleading (do first)

The systemic issue: native Twitch is the zero-config default now, but the app still frames
everything around Streamer.bot, and dangles cloud mode as a place you can't reach.

- [x] **First-run completion points the wrong way.** Now navigates to Twitch and says "connect your Twitch account to go live" (Streamer.bot noted as the alternative).
- [x] **"Launch cloud mode to go live" is wrong.** Native Mode tile now reads "You're in local mode — redemptions and chat go live right here, no extra setup."
- [x] **Save toast assumes Streamer.bot.** Now just "Game profile saved."
- [x] **Messages page is Streamer.bot-only in its wording.** Reworded: "the lines your bot posts in chat… Native Twitch applies them as soon as you save; if you use Streamer.bot, regenerate its actions."
- [x] **Game Profile help leads with Streamer.bot.** Now "…the overlay, chat replies, and (if you use it) Streamer.bot."
- [x] **Nav treats Streamer.bot as co-equal.** Now labelled "Streamer.bot · optional".
- [x] **Cloud mode is now reachable — new Settings page.** Local/Cloud choice, Appwrite connection form (write-only API key), Test connection, and restart-to-apply. Startup reads the saved choice and falls back to local with a reason if cloud can't start. (User confirmed cloud is a wanted feature, 2026-06-29.) Still a future home for data-folder/port/hidden-card prefs.

## Tier 2 — Jargon a non-technical streamer won't parse — DONE 2026-07-01

- [x] **Wizard "Game identity" step** now has example placeholders on every field and a plain-language line explaining redemption name + why singular/plural exist. Also dropped the Streamer.bot framing from the wizard intro.
- [x] **"Stable IDs are generated automatically"** → "Just type your starter items — one per line."
- [x] **Events UTC** — reworded: "…add their items to pulls only while enabled and within their scheduled window. Times are in UTC — set them a little wide if you're unsure of your offset." (A true local-time picker with conversion is a deeper follow-up if wanted.)
- [x] **"Featured Boost"** now explained in plain terms ("temporarily makes chosen collections more likely to be pulled — handy for featuring a set…").
- [x] **Leaky internals:** search placeholder → "Search collections or items". (Collections help is already rendered with the user's own item/collection terms in `renderProfile`, so "parts" was only the static fallback — no change needed.)

## Tier 3 — Inconsistencies & polish — DONE 2026-07-01

- [x] **Same field, two names:** wizard now uses "Panel nickname" to match the profile (with a tooltip).
- [x] **Confirm names a button that doesn't exist:** reset-profile confirm now says "…choose Save" (matches the button) and reads plainly.
- [x] **"Local data" / footer button** — already carries a dynamic "Click for session details" tooltip conveying it's clickable; added a static fallback too. Acceptable.
- [x] **Dev troubleshooting shown pre-login** — the Twitch permissions card now reads as onboarding before login ("Permissions CircuitOS uses… asks Twitch for just these"), and only shows the refresh-scopes troubleshooting once logged in.
- [x] **Multiple "Save" buttons** — topbar Save now has a tooltip ("Save catalog changes — collections, items, rates, and boost"); per-page buttons are already contextually labelled (e.g. "Save Profile").
- [x] **Collection Health table truncates names** — widened the name column and tightened the numeric columns; names fit without clipping.
- [~] **Cryptic ALL-CAPS kickers** (ECONOMY PULSE, CATALOG REACH, DISTRIBUTION CHECK, RECOVERY CONTROL) — left as intentional brand flavor; each panel also has a plain `<h2>` title, so meaning isn't lost. Revisit only if it tests poorly.

## Keep (good non-technical writing — the bar for the rest)
Action Center health summary; Rate Lab "?" help-tips; Backups "Viewer inventory is safe" copy.
