# OBS Lower-Quarter Tracker

The tracker displays the latest successful redemption for 20 seconds. It shows
the viewer, component, collection, unique progress, duplicate count, completion,
rare label, any item variant tags (e.g. SHINY, LARGE), the rarity tier label,
and active featured boost.

## Deploy Browser Files

As of 0.5.0.7, CircuitOS publishes the overlay files automatically. On startup
and after every profile switch it copies `index.html`, `styles.css`, and
`overlay.js` into the active profile's overlay folder:

`<DataPath>\profiles\<profile-id>\overlay\`

For the default install that is:

`C:\Users\nicho\Documents\CircuitOS\Data\profiles\circuit-components\overlay`

You no longer create this folder or copy files by hand. The Streamer.bot
redemption action writes `overlay-state.json` into the same folder after the
first successful pull. (The sample `overlay-state.json` in
`overlays/lower-quarter` is for local preview only — do not deploy it.)

## OBS Browser Source

The admin panel's **Overlay Editor** shows an **OBS Setup** panel with the exact
Local-file path and a Copy button — use that rather than typing the path.

1. Add a Browser source.
2. Enable `Local file`.
3. Paste the path from the Overlay Editor's OBS Setup panel — it points at
   `…\profiles\<profile-id>\overlay\index.html`.
4. Set width to `1920` and height to `300`.
5. Position the source along the bottom of the scene.

The page background is transparent. The source checks for new state twice per
second and automatically hides after the timestamp written by the redemption
action.

## Preview

For a browser preview using the sample state, serve the source folder locally
and open `index.html?preview=1`. Preview mode ignores the expiration timestamp.

Overlay publishing is noncritical. If its directory or state file cannot be
updated, Streamer.bot logs a warning while preserving the successful inventory
save and normal redemption messages.

After a test redemption, confirm that the modified time on
`overlay\overlay-state.json` changes immediately. If it does not, the installed
redemption action is older than the overlay publisher or Streamer.bot logged an
overlay update warning.
