# Feature Request Analysis (Discord #feature-requests)

Captured 2026-06-29 while the maintainer was away. These two need a product decision before
implementation, so they're written up here rather than guessed at. Pick an option and I'll build it.

The third request, **"test feature on commands,"** is **done** — the Game Profile page now has a
"Test a command" box (sandbox viewer, no live data touched). See commit `e6cd7aa`.

---

## 1. Link alternate accounts (e.g. "botsefer")

**Goal:** a streamer's alt/bot account (and viewers' alts) credit the main account's inventory,
so pulls/duplicates/currency don't get split across accounts.

**The core problem:** inventory is keyed by **Twitch user-id** (the redemption event carries
`user_id`). But a streamer adding a link only knows **usernames**. So we must resolve a username to
a user-id somewhere. Three options:

| Option | How it works | Pros | Cons |
|--------|--------------|------|------|
| **A. Resolve at add-time via Helix (recommended)** | When you add "alt → main" in the panel, CircuitOS looks both usernames up via `helix/users?login=` and stores `altUserId → mainUserId`. Dispatch redirects by user-id. | Robust; survives display-name changes; clean dispatch. | Requires Twitch connected when adding a link (you already are — zero-config login). |
| **B. Resolve lazily by login** | Store `altLogin → mainLogin`; at redemption, match the event's login and redirect to the main's inventory entry (found by its stored display-name). | No Helix call. | Fails if the main hasn't redeemed yet; brittle if a login changes; messier merge logic. |
| **C. Manual user-id entry** | You paste both Twitch user-ids. | No Helix, fully explicit. | Awful UX — nobody knows their user-id. |

**Recommendation: Option A.** Scope:
- New per-profile data `account-links.json`: `{ links: { "<altUserId>": { mainUserId, altLogin, mainLogin } } }`.
- New endpoint `POST /api/twitch/links` (add/remove) that Helix-resolves the two logins.
- `DispatchRuntimeAction`: resolve `viewerId`/`viewerName` through the link map before reading/writing
  inventory (one small hook, fully smoke-testable).
- Admin UI: a "Linked accounts" panel on the Twitch page (add alt→main, list, remove).
- Decision needed from you: **per-profile links or global** (one alt→main map shared across all your
  profiles)? Global is simpler for the streamer; per-profile is more flexible. I lean global.

---

## 2. Cross-profile currencies

**Goal (ambiguous — needs you to pick the intent):** "cross profile currencies." Three plausible
meanings, very different scope:

| Interpretation | What it means | Scope |
|----------------|---------------|-------|
| **A. Shared balance** | A viewer's currency is one balance across all profiles (earn Scrap in game 1, spend in game 2). | Largest — currency must move out of per-profile inventory into a shared store keyed by viewer; every balance/salvage path changes. |
| **B. Transfer command** | A command/admin action moves currency from one profile to another for a viewer. | Medium — a transfer operation + UI; balances stay per-profile. |
| **C. Merged view** | A command shows a viewer's balances across all profiles in one readout. | Smallest — read-only aggregation across profiles. |

**Recommendation:** start with **C (merged view)** if the goal is visibility, or **A (shared balance)**
if the goal is a true cross-game economy — but A is a meaningful data-model change and should be its
own milestone. **B** is the middle ground. I can't pick this one for you; it changes the whole economy
model. Tell me which intent and I'll scope it properly.

---

## Status

- Built this session: command tester (`e6cd7aa`), inline Twitch login polish (`e8c78df`).
- Awaiting a decision on the two above before implementing.
