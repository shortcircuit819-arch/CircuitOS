# CircuitOS UI Bench

Dev-only static workbench for visually editing CircuitOS admin UI proposals and
exporting Markdown wiring tickets.

Open `index.html` directly in a browser.

Use Current Layout Import to choose or paste `tools/admin/index.html`. The
selected app screen or welcome step renders as an editable canvas: click
elements, edit labels, hide items, apply size presets, and drag imported blocks
to reorder proposal intent.

Use Style Import to choose or paste the current `tools/admin/styles.css`
when you want the canvas to match the live app layout. The full stylesheet is
stored inside the isolated canvas, while color variables are included in
exported wiring tickets. It does not edit the real app stylesheet.

## Boundaries

- Proposal-only.
- No API calls.
- No user data access.
- No secrets.
- No production source edits.
- Drafts are stored in browser localStorage.

Use exported tickets as input for Claude/Codex implementation sessions.
