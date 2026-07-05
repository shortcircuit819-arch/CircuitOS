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

An in-app visual editor so a streamer can tune **everything** — colors, text labels, small accents,
spacing, pop-ups — by hand, without touching source:

- Edits write to an **overrides layer**: a JSON map of token/label overrides applied *over* the base
  tokens at runtime. The base skin is never edited, so there's no drift and overrides can be reset.
- Overrides persist (per profile, with a global option) alongside the rest of the profile config.
- Because the skin already reads from semantic tokens, Design Mode is "expose the tokens + a few text
  labels as editable fields," not a rewrite.

## 0.8 build order

1. **Token layer.** Formalize semantic tokens derived from the 7 streamer colors; replace ad-hoc color
   literals in `styles.css` with tokens. No visible change intended — this is the foundation.
2. **Re-skin.** Move the admin to the token system and this language: hairlines over cards, crisp
   geometry, segmented controls, the single accent glow.
3. **Contrast-aware theming.** Ensure legibility holds across accent/base combinations.
4. **Design Mode.** The overrides layer + the in-app editor.
