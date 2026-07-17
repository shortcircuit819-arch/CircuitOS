# Feature Request Analysis (Discord #feature-requests)

Captured 2026-06-29 while the maintainer was away. These two need a product decision before
implementation, so they're written up here rather than guessed at. Pick an option and I'll build it.

The third request, **"test feature on commands,"** is **done** — the Game Profile page now has a
"Test a command" box (sandbox viewer, no live data touched). See commit `e6cd7aa`.

---

## 1. Bot chat account ("botsefer")  — BUILT 2026-07-16 (pre-0.9, for 1.0)

**Shipped as designed below**, with the recommended default: the broadcaster keeps EventSub/chat-read;
only *sends* go out as the bot. Connect/disconnect lives on the Twitch page ("Bot chat account
(optional)" card, device-code login — sign in as the BOT account in a private window). Tokens in
`twitch-bot-tokens.local.json` (DPAPI). No bot connected = exactly the old behavior. Broadcaster scopes
gained `channel:bot`; an existing login grants it with one refresh (or mod the bot).

### Original analysis — CLARIFIED 2026-06-29

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

## 3. Per-state overlay images / GIFs

**Goal:** upload a different background image/GIF per pull state, so e.g. completing a collection
shows a celebratory GIF instead of the normal background.

**Very doable — direct parallel to the per-state colors already shipped (C4).** Scope:
- Storage: per-state files (`bg-rare.<ext>`, `bg-complete.<ext>`, `bg-duplicate.<ext>`) beside the
  global `bg.<ext>`; GIFs already supported.
- Config: add `backgroundImage` to each state override block (next to the per-state colors).
- Upload: `/api/overlay-image` takes a `state` param; per-state slot in the State Overrides panel.
- Render: extend `applyStateColors()` in overlay.js to also swap `--bg-image` for the active state.
- **Default:** states without their own image fall back to the global background (upload only what you
  want special). Confirmed as the intended behavior.

Fully testable in preview. Parked (features on back burner as of 2026-06-29).

## 4. Hosted cloud (one backend for everyone) — analyzed 2026-07-01

**The question:** "can't I just use my own Appwrite connection for all users? The tables are keyed by
userId anyway." The data model *is* multi-tenant (every row is keyed by `userId` = Twitch id +
`profileId`), so one project *can* hold everyone's data. The blocker is **auth, not the schema.**

**Why you can't just ship your key:** `AppwriteDataStore` connects with a **server API key** — a
master key that can read/modify/delete *every* row for *every* user and touch the whole project.
That's fine today because each user brings their *own* project + key (a leak only hurts them). Ship
*your* key in the desktop app and any user can extract it (same lesson as the Twitch client secret) →
one leak = everyone's data readable/wipeable + your bill. **Never ship a master key in a client.**

**The two correct designs (both = the "hosted phase"):**
1. **User-scoped auth + row-level permissions.** Bridge "logged in with Twitch" → an Appwrite session
   (Appwrite Custom Token, minted by a Function), give every row per-user read/write permissions, and
   have the client use the *user's session* — never an admin key. Requires reworking `AppwriteDataStore`
   off the server key onto the client/session model + a Twitch→Appwrite token bridge.
2. **Thin backend/function layer.** The app calls your hosted Function/API (which holds the key
   server-side) and does reads/writes on behalf of the authenticated user. Key never leaves the server.

**The three hats you'd be putting on** (it's a business decision, not a config flip): **cost** (you pay
for everyone's usage), **uptime** (everyone's data depends on your project), **responsibility/privacy**
(you hold all users' data; a leak is on you).

**Recommendation:** legitimate eventual direction, schema is ready — but it's a security-sensitive
design project, not a switch. Ship position stays: **Local default + bring-your-own-Appwrite (advanced)**,
which is what 0.7.0.2 does. Build hosted cloud as its own milestone with the auth designed properly.

## 5. Local ↔ Cloud sync & migration — surfaced live 2026-07-02

**How it came up:** the maintainer reported "two of my local profiles are gone." They weren't — local
and cloud are **independent `IDataStore` backends** (`LocalFileDataStore` vs `AppwriteDataStore`).
Local held all three profiles; cloud only ever held a single older *Circuit Components* from when cloud
was first enabled. Switching backends swaps the whole dataset, so two profiles appeared to vanish.
Workaround was manual: export each local profile, import into cloud — which then created a **duplicate
"Circuit Components"** (import generates a unique id but keeps the name verbatim), and two
identically-named profiles made the profile switcher look broken (switch tracks by unique id, but every
card/summary renders only the name, so a correct switch was visually invisible until the twin was
deleted). Data was never at risk; a safety copy of all three profiles was made at
`C:\Users\nicho\Documents\CircuitOS-profiles-backup-20260702_185132`.

**Why this matters:** the moment hosted cloud (1.1) exists, "which copy is the real one?" becomes a
first-class problem. It's really **three** problems, and only one blocks 1.1:

1. **Source of truth (design decision).** Recommended model: **when logged in, cloud is the truth and
   local is an offline cache.** Avoids co-equal-copy ambiguity. The alternative (reconcile two peers)
   is much harder and not worth it for launch.
2. **Migration (the 1.1 deliverable).** A one-click **"push local profiles to cloud"** that is
   **identity-aware** — matches on profile id/name and updates in place instead of blind-copying into a
   twin. This is the exact failure hit by hand above, so de-dupe is a hard requirement, not a nicety.
3. **True bidirectional sync (defer to 1.1+ / later).** Multi-device edits, offline reconciliation,
   per-record conflict resolution. Most streamers run one machine; do not let this block hosted cloud.

**CircuitOS-specific constraint that must shape the schema *before* 1.1:** **inventory is high-churn**
(written constantly mid-stream) while **catalog/config is low-churn** (edited occasionally). Inventory
wants last-write-wins or append semantics, and a stale local cache must **never** clobber a live cloud
inventory. Bake this distinction into the sync/schema design up front.

**Near-term UX mitigations (cheap, worth doing before 1.1 — candidates for the 0.8 design pass):**
- Always-visible indicator of **which backend is active** and that local ≠ cloud.
- **Import name de-dupe:** when an imported profile name collides with an existing one, auto-suffix
  (e.g. `Circuit Components (2)`) so no silent twins — a ~5-line fix in `CircuitService.Modules.cs`.
- Show **Created date / short id** in the profile switcher and summary line (not just the grid card) so
  same-named profiles are always tellable apart.
- Hint on the active profile's card — *"Switch to another profile to delete this one"* — since the
  active profile intentionally hides Delete and users read that as "edit only."

## Status

- Built this session: command tester (`e6cd7aa`), inline Twitch login polish (`e8c78df`).
- Awaiting a decision on §1 and §2 before implementing.
- §5 (sync/migration) is a confirmed 1.1 requirement; the near-term UX mitigations are 0.8 candidates.
