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
- [ ] **Cloud mode is a dead-end (bigger, deferred):** no UI to enable cloud or enter Appwrite creds — it's `--cloud` only. Near-term = stop the UI advertising it (above). Real fix = a Settings page (Cloud toggle + Appwrite connection form + Test connection + restart-to-apply), built **when cloud is productized** (deployment shape still undecided). Also the natural home for data-folder/port/hidden-card prefs.

## Tier 2 — Jargon a non-technical streamer won't parse

- [ ] **Wizard "Game identity" step** dumps 9 fields with no examples — Item singular/plural, Collection singular/plural, Redemption name, Currency name. Add example placeholders + a one-line "what's a redemption name."
- [ ] **"Stable IDs are generated automatically"** (wizard) — drop the ID jargon.
- [ ] **Events use UTC** ("inside their UTC window") — streamers think local time. Show local equivalent / explain.
- [ ] **"Featured Boost" is unexplained** — nav + "Multipliers change relative collection weight." Add plain "what/when."
- [ ] **Leaky internals:** Collections search "…by collection, key, item, or ID"; Collections help says "parts." Use the user's own item/collection terms; drop key/ID.

## Tier 3 — Inconsistencies & polish

- [ ] **Same field, two names:** "Control panel name" (wizard) vs "Panel nickname" (profile). Pick one.
- [ ] **Confirm names a button that doesn't exist:** reset says "choose Save System Profile"; button is "Save."
- [ ] **"Local data" / "Connecting…" footer button** — unclear it's clickable / what it opens.
- [ ] **Dev troubleshooting shown pre-login** on the Twitch page (scope-refresh note).
- [ ] **Cryptic ALL-CAPS kickers** (ECONOMY PULSE, CATALOG REACH, DISTRIBUTION CHECK, RECOVERY CONTROL).
- [ ] **Multiple "Save" buttons** (global vs per-page) — unclear which persists what.
- [ ] **Collection Health table truncates names** ("Advanced Collecti…").

## Keep (good non-technical writing — the bar for the rest)
Action Center health summary; Rate Lab "?" help-tips; Backups "Viewer inventory is safe" copy.
