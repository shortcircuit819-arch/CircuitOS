# CircuitOS Roadmap — the road to the Shop (2.0)

*Set 2026-08-19; priority order updated 2026-09-04 after 1.0.4. This is the source of truth for planned feature work.*

## Direction

CircuitOS is a **local-first** streamer tool with a deepening **economy** — not a hosted
platform. All data stays on the streamer's machine (local by default; bring-your-own Appwrite
remains the advanced option). The destination is **2.0 — the Shop & Currency Workshop**: a place
for viewers to spend the currency (Scrap) they earn, so it stops piling up with nowhere to go.

Decisions that shape this roadmap:
- **No hosted cloud.** CircuitOS does not become a multi-tenant service. This parks the features
  that needed a shared backend (see Parked, below).
- **Per-profile wallet.** Scrap is earned and spent within one streamer's game. A shared
  cross-game balance is a bigger, later question that would require the backend we're not building.
- **The shop sells custom gameplay perks** (see 2.0).

## What's already built (the foundation)

The store is a shorter hop than it looks — three of the four pieces exist:

- **The wallet** — each viewer's currency lives in `wallet["scrap"]` inside `inventory.json`, per profile.
- **The earn side** — salvage duplicate items → Scrap (`!salvage`); read with `!balance`.
- **Inventory write safeguards** — existing locking, atomic file replacement, and backups are
  the foundation. Purchases still need validation and tests proving that deductions and rewards
  commit together and cannot be repeated accidentally.

The missing piece is the **spend side** — that's what this roadmap builds toward.

## The path (all local)

**Next priority: achievements & badges, then streamer analytics.** The owner selected this
order after 1.0.4. The existing milestone labels below are retained for reference: work proceeds
through **1.2 → 1.1 → 1.3 → 1.4 → 2.0**. These labels are planning references, not an instruction
to publish application versions out of order; release versions will continue increasing.

**Critical path to the store: `1.3 → 1.4 → 2.0`.** Achievements and analytics are standalone
features and do not gate the store.

### 1.2 — Achievements & badges
Local viewer collection milestones. Standalone. A natural future economy hook (achievements could
grant Scrap or unlock shop items).

### 1.1 — Streamer analytics *(after achievements & badges)*
Economy visibility: insights on pulls, viewer engagement, and **economy health** — where the
"1500+ Scrap and climbing" inflation becomes a dashboard, and (once the store ships) where you see
whether the sinks work. Standalone; no prerequisites. Review existing economy analytics before
scoping additional dashboards.

### 1.3 — Trading & gifting  *(critical path)*
Local, atomic transfers of items and currency between viewers in the channel. A strong engagement
driver on its own — and it builds the exact **"give something to another viewer"** plumbing the
store needs, because the shop lets you buy a perk **for a viewer**, not just yourself. A transfer is
a transaction that must commit both viewers' changes together; locking each write separately
is not enough to guarantee an atomic transfer.

### 1.4 — Shops readiness  *(critical path)*
The wallet graduates from a bare inventory field into a real **economy ledger**: configurable
currencies and earning rules, a **transaction / audit history** (who bought what, when), and abuse
safeguards (rate limits, negative-balance and price validation). This is the plumbing the store
sits on.

### 2.0 — Shop & Currency Workshop  *(the destination)*
A streamer-configurable store where viewers spend Scrap:

- **The perk shop (the differentiator).** The streamer defines buyable **gameplay modifiers** —
  e.g. "no-dupe pulls" (guaranteed unique for a stretch), "no-unique pulls", pull boosts —
  purchasable **for yourself or gifted to a viewer** (rides 1.3's transfer semantics). A purchase is
  an atomic Scrap deduction plus the pull engine honoring the active perk. Buyable pull modifiers
  are the planned signature feature.
- **Sinks for the whales.** Optional gambling / lottery / mystery-pull sinks aimed at viewers
  sitting on huge balances — the direct answer to runaway inflation.
- **Item buys.** Optionally spend Scrap to acquire a specific component you're missing (closes the
  salvage → buy loop).
- **Fulfillment.** Streamer-defined perks/roles fulfilled via a purchase queue (reusing the existing
  Discord role-award queue pattern).
- **Economy controls + audit.** Price tuning, per-item stock / cooldowns, purchase history — the
  operator's dashboard for a healthy economy.

### The genuinely new design piece: the perk system
Buyable pull-modifiers are new, so 2.0's design must answer:
- How does a purchased perk **attach** to a viewer (a field on their inventory record, or a separate
  perks store)?
- How long does it **last** — N pulls, a time window, or until consumed?
- How does **`PullEngine`** apply it at pull time, and how do stacked/conflicting perks resolve?

This deserves its own design pass once 1.4 lands and the exact perk catalog is chosen.

## Parked (would need a hosted backend — deliberately not built)

- **Hosted cloud** — one shared multi-tenant backend. Dropped; local + bring-your-own-Appwrite stays
  the model. (Rationale in `docs/feature-requests-analysis.md` §4: never ship a master key in a client.)
- **Online viewer profiles** — needed hosted cloud.
- **Online module marketplace** — needed a host. *File-based `.circuitmodule` share/import already
  works locally, so sharing itself isn't lost — only the online catalog.*

## Stretch goal (kept in view)

- **Twitch Extension** — CircuitOS on the channel page for every viewer (deep panel + glanceable
  video overlay). The biggest viewer-reach feature; it would still need some backend, so it's held
  as a someday-goal rather than cut.
