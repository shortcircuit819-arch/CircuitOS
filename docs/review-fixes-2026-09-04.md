# Deep review fixes — September 4, 2026

These changes address the 15 findings from the deeper code review and are included in CircuitOS 1.0.2.

| Finding | Result |
| --- | --- |
| Portable data discovery after migration | Startup recognizes the profiles directory and preserves the existing data root. |
| Competing Twitch refresh tokens | Sessions share a synchronized token cache per token file; refresh, encrypted persistence, logout, and account replacement are coordinated. |
| Inventory operations during profile switching | Remove, reset, and restore capture an independent profile view before waiting for the inventory lock. Backups and writes use that same view. |
| Empty cloud startup | A reachable cloud profile without a catalog falls back to the local app and exposes the cloud error in Settings. |
| Appwrite results beyond the first page | Reads, profile scans, deletion, and migration use cursor pagination. |
| Stale cloud rolling backups | Backup lookup requires the current server snapshot timestamp. Refresh the backup list when a rolling snapshot has changed. |
| Failed required EventSub subscription | Subscription failures propagate into the listener retry loop. |
| EventSub reconnect handover | The old connection remains available until the replacement welcome, then drains pending notifications with a bounded close grace period. |
| Redemption exceptions before persistence | Managed rewards are refunded after pre-write failures. Ambiguous inventory writes and failures after commit do not trigger refunds. |
| Update restart arguments | Restart carries effective data, UI, overlay, port, headless, and explicit cloud options. |
| Saves completing after further edits | Catalog, profile, overlay, and role saves only clear the submitted revision's dirty state. |
| Role polling overwriting drafts | Periodic updates preserve the role-name input elements and unsaved text. |
| Rate Lab empty tiers | Preview odds exclude empty tiers, matching the runtime denominator. |
| Overlay-only unsaved changes | Refresh and close guards include overlay drafts (and role-name drafts). |
| Missing or corrupt profile metadata | Existing profiles can be renamed and selected after metadata recovery; catalog and inventory remain intact. |

## Validation

Run from the repository root on Windows with .NET 9 and Node.js 22:

```powershell
dotnet build tools/runtime/CircuitOS.Runtime.csproj -c Release
dotnet run --project tools/runtime.tests/CircuitOS.Runtime.SmokeTests.csproj -c Release -- data
dotnet run --project tools/cloud.tests/CircuitOS.Cloud.Tests.csproj -c Release -- --runtime tools/runtime/bin/Release/net9.0-windows/CircuitOS.dll
dotnet run --project tools/twitch.tests/CircuitOS.Twitch.Tests.csproj -c Release
node --test tools/admin.tests/editor-state.test.cjs
git diff --check
```

The build workflow runs these suites before packaging. The cloud suite uses a localhost Appwrite mock and launches an isolated runtime for the empty-cloud startup case. Twitch tests use fake HTTP/WebSocket peers and temporary encrypted token files. They require no live credentials. Runtime regression fixtures use temporary data; pass the repository starter `data` directory to the smoke suite, not a live data directory.

Verified locally: Release build with zero warnings/errors; runtime smoke suite including seven new regressions; nine cloud/startup cases; twelve Twitch cases; thirteen admin cases. Independent tester, inventory-safety, and security reviews found no remaining blocker in these changes. Browser checks using an isolated starter profile confirmed first-run setup, role draft preservation across polling, role save, and the overlay unsaved-refresh guard.

Live Twitch/Appwrite services, an installed WebView2 session, and an actual installer/update round trip were not exercised. No live inventory or credentials were used during verification. Release publication is recorded separately in HANDOFF.md.
