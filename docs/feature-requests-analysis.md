# Feature Request Analysis (Discord #feature-requests)

Captured 2026-06-29 while the maintainer was away. These two need a product decision before
implementation, so they're written up here rather than guessed at. Pick an option and I'll build it.

The third request, **"test feature on commands,"** is **done** — the Game Profile page now has a
"Test a command" box (sandbox viewer, no live data touched). See commit `e6cd7aa`.

---

## 1. Bot chat account ("botsefer")  — CLARIFIED 2026-06-29

**Corrected understanding (my first writeup was wrong — it was about inventory merging; it's not).**
This is a **separate bot chat account**, like MixItUp / Streamer.bot: the bot's replies and pull
announcements post **from a dedicated bot account** (e.g. `botsefer`) instead of from the broadcaster
account (e.g. `Moosefer`). So `!inventory` is answered by **botsefer**, not the streamer. Some
streamers prefer this so the bot is visibly a bot. Inventory/currency are unaffected — only the
**chat sender identity** changes.

**How it works on Twitch:** `POST /helix/chat/messages` takes `broadcaster_id` (the channel) and
`sender_id` (who's talking). Today both are the broadcaster. To send as the bot, log the bot account
in separately and send with `sender_id` = bot's user-id, using the **bot's** token.

**Scope (implementable, well-bounded):**
- A **second, optional Twitch login** for the bot account — reuse the device flow (a "Connect a bot
  account" button on the Twitch page). Store its tokens separately (e.g. `twitch-bot-tokens.local.json`,
  DPAPI-encrypted like the main token).
- Bot login scopes: `user:write:chat`, `user:read:chat`, `user:bot`; the broadcaster also needs
  `channel:bot` granted (add to the main login's scopes) so the bot may post in the channel.
- `TwitchHelix.SendChatMessage`: when a bot session exists, send with `sender_id` = bot id (bot token);
  otherwise behave exactly as now (send as broadcaster). Redemption intake, reward management, and
  EventSub all stay on the **broadcaster** account.
- UI: show "Bot: @botsefer (connected)" with connect/disconnect; falls back to broadcaster when absent.

**Open question for you:** does the bot also **read** chat (i.e. run the `!command` listener under the
bot identity), or does the broadcaster keep listening and only *replies* go out as the bot? Simplest =
broadcaster keeps EventSub/chat-read, bot only sends. That's the recommended default.

---

## 2. Cross-profile currencies

**Goal (ambiguous — needs you to pick the intent):** "cross profile currencies." Three plausible
meanings, very different scope:

| Interpretation | What it means | Scope |
|----------------|---------------|-------|
| **A. Shared balance** | A viewer's currency is one balance across all profiles (earn Scrap in game 1, spend in game 2). | Largest — currency must move out of per-profile inventory into a shared store keyed by viewer; every balance/salvage path changes. |
| **B. Transfer command** | A command/admin action moves currency from one profile to another for a viewer. | Medium — a transfer operation + UI; balances stay per-profile. |
| **C. Merged view** | A command shows a viewer's balances across all profiles in one readout. | Smallest — read-only aggregation across profiles. |

**Decision 2026-06-29:** deferred — not a 0.7 item. The maintainer wants this tied to the **shops /
2.0 update** (a true cross-game economy gives useful data there), targeting roughly **0.7.9 or 0.8.5**.
Likely lands as **A (shared balance)** since that's what a cross-game shop economy needs, but the
intent is confirmed only when the shops work starts. Park until then.

---

## Status

- Built this session: command tester (`e6cd7aa`), inline Twitch login polish (`e8c78df`).
- Awaiting a decision on the two above before implementing.
