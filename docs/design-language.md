# CircuitOS Design Language

The reference for the 0.8 "Design & Identity" re-skin. It captures the decisions made while
workshopping the look, so the re-skin is intentional rather than improvised. If a change to the admin
panel's visuals isn't consistent with this doc, either the change or this doc is wrong — reconcile
before shipping it.

## Intent

- **Intentional, not generic.** The panel should read as a designed product, not an assembled one — no
  "vibe-coded" feel where elements look accidental.
- **Genuinely composed under any color theme.** Streamers pick their own colors. The layout, hierarchy,
  and legibility must hold up for *any* accent/base combination — the structure can't depend on the red.
- **Calm.** The admin is a workbench. Motion and decoration are restrained; the OBS overlay is where
  things move.

## Principles

1. **Hairlines over cards.** Structure comes from 1px lines and subtle surface shifts, not from heavy
   bordered-and-shadowed boxes. Cards were reading as unintentional; hairline-bounded regions read as
   designed. Reserve stronger separation for genuinely distinct areas.
2. **Crisp geometry — no pills.** Small, consistent corner radii. No fully-rounded ("pill") shapes, and
   no chunky square toggles. Apple-clean, minus the round pills.
3. **One soft element: the accent glow.** The single soft touch is the faint accent glow (the radial
   accent-at-low-alpha wash). Everything else is crisp edges and hairlines. No decorative left-accent
   bars (removed) or other soft flourishes competing with it.
4. **Hue-independent bones.** The visual system is defined in *semantic* tokens (surface, hairline,
   text, accent…), derived so that swapping the streamer's colors recolors the skin without breaking the
   structure. Status colors are the exception (below).
5. **Contrast-aware.** Text and controls stay legible across the accent/base range a streamer might pick;
   the design shouldn't assume a dark-on-light or light-on-dark that a theme could invert.

## Token model (two layers)

**Layer 1 — streamer colors (input).** The 7 colors a streamer already sets on their profile:
`background`, `panel`, `panelAlt`, `line`, `accent`, `text`, `muted`. This is their brand.

**Layer 2 — semantic tokens (what the UI uses).** The admin should reference *semantic* tokens, not raw
streamer colors, so intent is explicit and derivation is centralized:

- `--surface`, `--surface-raised` — from `panel` / `panelAlt`
- `--hairline` — from `line`
- `--text`, `--text-muted` — from `text` / `muted`
- `--accent`, `--accent-soft` (low-alpha wash), `--accent-line` (low-alpha border), `--accent-glow`
- **Status hues are fixed, not themed:** `--danger`, `--positive`, `--warning`, `--info` keep stable,
  legible colors regardless of the streamer's theme, because "error" must never depend on the accent.

Derived tokens (`accent-soft`, `accent-line`, `accent-glow`, hover surfaces) are computed once from the
streamer colors so any combination stays composed. Today these are ad-hoc `rgba(255,26,36,…)` literals;
0.8 formalizes them.

## Theming model — curated base + accent (decided 2026-07-04)

The app owns the *bones*; the streamer tints *one* thing.

- **The app ships a few designed base themes** — each a crafted set of the structural colors (background,
  surface, surface-raised, hairline, text, muted). These are not free-form user fields; they're designed
  so the product always looks intentional (the Apple model). Start with the current dark theme; add a
  couple more (e.g. a lighter one) over time.
- **The streamer picks their accent color** — their brand pop — and nothing else. The accent is applied
  **contrast-safely** (`--accent-readable` derives a legible variant per surface), so any accent stays
  usable without letting a bad pick break the UI.
- **Why:** readable ≠ tasteful. Guaranteeing legibility across 7 arbitrary colors is possible but can't
  guarantee a *designed* result, and it dilutes the CircuitOS identity. Owning the surfaces + tinting the
  accent gives brand identity without foot-guns — and is less theming engineering long-term.
- **The contrast work already built is the accent-safety layer** — repurposed, not discarded.
- **Optional escape hatch:** full per-color control can live behind an "Advanced" door for power users
  who accept they can make it ugly; the default path stays curated + accent.

**Implications to build (0.8):**
- **Data model:** a profile stores `theme` (base-theme id) + `accent` (one color), not 7 colors. Migrate
  existing profiles: map saved colors to the nearest base theme (or a one-off "Custom" theme) and carry
  the accent across.
- **Appearance page:** replace the 7 color pickers with a **theme selector** + an **accent picker**.
- **Status hues** stay fixed regardless of theme.

## Controls

- **Toggles → segmented control or checkbox.** A two-option choice is a **segmented control** — two flat
  labels split by a thin `|` divider, the active one marked by the accent (not a filled pill, not a
  sliding switch, not a square box). A boolean is a **checkbox**. Round pills and square toggles are both
  out — this was tried and rejected.
- **Buttons.** Primary = accent fill; secondary = hairline outline; danger = danger hue. Small radius,
  crisp.
- **Inputs.** Hairline border; focus shown with an accent-line, not a heavy glow.
- **Sliders.** The fill is tied to the handle by construction (see the rate-slider box-shadow approach),
  not a fragile calc.

## Surfaces & spacing

- Panels: a subtle raised surface bounded by a hairline; avoid the border+shadow "card" stack.
- One spacing scale, applied consistently (kickers, titles, body, control rows).
- ALL-CAPS section kickers are brand flavor and stay, but every panel also carries a plain title so
  meaning is never carried by the kicker alone.

## Design Mode (0.8 deliverable)

An in-app visual editor so a streamer can tune the look without touching source: **base theme + accent**
(above), plus text **labels**, spacing, and specific small accents. (Raw per-color editing is the
Advanced escape hatch, not the default.)

- Edits write to an **overrides layer**: a JSON map of token/label overrides applied *over* the base
  tokens at runtime. The base skin is never edited, so there's no drift and overrides can be reset.
- Overrides persist (per profile, with a global option) alongside the rest of the profile config.
- Because the skin already reads from semantic tokens, Design Mode is "expose the tokens + a few text
  labels as editable fields," not a rewrite.

## 0.8 build order

1. **Token layer** ✓ — semantic tokens + accent literals routed through `var(--accent)`.
2. **Contrast-aware theming** ✓ — `--accent-readable`, auto-legible text, chrome derived from the panel.
3. **Re-skin** ✓ (structural) — the admin uses the token system and this language: hairlines over cards,
   crisp geometry, the single accent glow. Overview was the reference; the data-row treatment
   (`.health-table-row` / `overview-stats`: transparent rows, one hairline divider, no per-row border +
   fill) is now rolled onto Economy, Leaderboard, RateLab simulations, Viewer metrics, Role awards,
   Backups (guide strip + file/backup lists), boost multipliers, and Twitch reward mappings. Message
   editor cards were unified to the standard panel treatment. The first-run **Wizard** kept its
   ceremonial shell + selectable preset choice cards, but its summary/review fact tiles were flattened
   to a divider strip. **Controls:** booleans are now crisp **checkboxes** (the sliding pill switch is
   gone) and every fully-rounded pill chip/badge/token was de-pilled to a small crisp radius. **Kept as
   cards** (genuinely distinct areas): collection cards, the Settings backend option cards, the Twitch
   account + utility cards, and the message editors. **Kept round** (not pills): status dots, and the
   help/step round icon markers. **Deliberately left:** deep form internals (part/variant/field grids).
4. **Curated theming model** ✓ — base themes (Midnight/Slate/Carbon) + a `resolveThemeColors` resolver;
   Appearance reworked to a theme selector + accent picker (with a "Custom" card for legacy profiles);
   profile data model carries `theme` + `accent`, and `NormalizeProfile` rewrites the effective `colors`
   from the chosen base palette so the overlay + engine follow. Legacy profiles stay "Custom" until they
   adopt a base theme.
5. **Design Mode.** The overrides layer + the in-app editor (theme + accent + labels/spacing).

**Segmented controls:** deferred, not skipped. The genuine two-option choices are already handled
(booleans → checkboxes; data backend Local/Cloud → selectable option cards). The remaining `<select>`s
(import mode/target/format, backup filter) are legitimately multi-option, so converting them to a
segmented control would be a needless functional refactor. If a true two-option control appears later,
build the segmented control then (or as part of Design Mode).
