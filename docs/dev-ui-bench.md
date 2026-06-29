# CircuitOS UI Bench

CircuitOS UI Bench is a dev-only companion tool for planning admin UI changes
without touching user data, live profile files, cloud data, tokens, or production
source code.

The goal is to let the project owner import the current app shell, click around
inside a visual canvas, resize/reorder/hide pieces, edit labels and behavior
notes, then hand Claude or Codex a precise wiring ticket.

## Location

Prototype folder:

```text
tools/dev-ui-bench/
```

Open `tools/dev-ui-bench/index.html` directly in a browser. There is no build
step, package manager, server, or install process.

## Scope

UI Bench is proposal-only.

It can:

- import the current admin `index.html` screen scaffold into a visual canvas
- import and edit welcome / first-run wizard steps
- select real-looking panels, buttons, fields, toggles, and toolbars directly in the canvas
- render imported screens with the real imported `tools/admin/styles.css`
- hydrate common Overview runtime containers with fake data for layout accuracy
- move imported blocks in the exported proposal order without disturbing source-order preview accuracy
- resize imported blocks with presets: compact, half, wide, full
- hide imported blocks in the proposal without deleting source files
- add planned UI components
- edit labels, helper text, ids, states, and click behavior notes
- preview the rough CircuitOS visual style
- import or paste current CircuitOS CSS variables for preview/theme planning
- export a wiring ticket in Markdown
- save draft work to browser localStorage

It does not:

- edit `tools/admin/app.js`, `index.html`, or `styles.css`
- edit profile/user data
- read or write `inventory.json`
- read cloud data or secrets
- call Twitch, Appwrite, Streamer.bot, or CircuitOS APIs
- package or publish releases

## Workflow

1. Pick a screen, such as Welcome Step 1, Overview, or Game Profile.
2. Import the current build layout.
   - Use Current Layout Import > Import admin index.html, or paste the current
     screen/full `index.html` into Paste layout HTML.
   - Choose `tools/admin/index.html`.
   - UI Bench maps the selected screen to its matching `*View` section and
     renders static panels, toolbars, buttons, fields, and toggles as selectable
     canvas elements.
   - This is layout scaffolding, not live app execution. Controls created only at
     runtime by `app.js` may need to be added manually or imported in a later
     version of the bench.
3. Import the current app style.
   - Use Style Import > Import styles.css to choose `tools/admin/styles.css`.
   - UI Bench stores that stylesheet inside the isolated canvas so the imported
     screen uses the same grid, panel, button, and table styles as the real app.
   - Overview-only runtime sections such as Pull Rates, Collection Health, Event
     Timeline, Economy Pulse, and Top Collectors receive fake rows when the
     imported HTML has empty containers.
4. Click a canvas element to select it.
   - Size and hidden-state controls preview on the canvas.
   - Move Up / Move Down changes exported proposal order, but the canvas keeps
     source DOM order so it remains visually faithful to the current app.
5. Use the property editor:
   - label
   - suggested id
   - canvas size
   - hidden in proposal
   - helper text
   - style
   - visible when
   - disabled when
   - click behavior
   - success state
   - error state
6. Export the wiring ticket.
7. Hand the ticket to Claude/Codex for implementation.

## Wiring Ticket Format

Each ticket should be clear enough that an AI coding session can wire the
feature without rediscovering the UI intent.

```md
## UI Wiring Ticket

Screen: Twitch Settings
Component: Button
Label: Sync Reward
ID suggestion: syncTwitchRewardButton
Location: Twitch Settings > Reward Management panel
Style: Primary
Visible when: Twitch logged in
Disabled when: No live profile selected
Click behavior: POST /api/twitch/reward-sync
Success state: Show "Reward synced"
Error state: Show API error
Acceptance checks:
- Button is visible only when Twitch is configured.
- Failed API call shows a readable error.
- Reward status refreshes after success.

## Imported Theme Variables

Style source: styles.css
- --bg: #000d19
- --red: #ff1a24
```

## Safe Boundaries

Keep UI Bench focused on product intent and layout decisions. If a future version
gets an "apply" mode, it should be explicit, reversible, and limited to generated
patch notes or dedicated draft files first.

The first production feature to build from UI Bench tickets should be small:
sidebar theme coverage or one Overview layout fix. That will prove the workflow
before using it for larger Twitch settings screens.
