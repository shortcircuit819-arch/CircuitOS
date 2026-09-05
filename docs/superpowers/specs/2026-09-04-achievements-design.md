# Achievements and badges

Approved scope: collection badges, custom achievement rules, configurable Scrap rewards,
lifetime pull milestones, viewer progress, configurable badges chat command, and overlay
celebrations. Achievements precede the analytics milestone. No new dependencies or hosted service.

## Rules and records
Each profile has achievements settings in system-profile.json: enabled, collectionBadges,
announce, overlay, defaultReward, and definitions. Custom rules select collection percentage,
unique items (one collection or whole catalog), completed collections, or tracked lifetime pulls.
IDs are stable and awards are permanent. Builtins award first item, 25%, 50%, and completion
for each nonempty collection. A tiny collection can satisfy several milestones at once.

Viewer inventory records contain an achievements map and achievementStats. Awards snapshot
name, description, reward, recorded time, earning time when known, and backfill provenance.
Reward and award commit with the inventory in one existing locked atomic write with backup.
Changing or removing a rule never pays an already awarded ID again.

## Existing viewers
Startup scans every profile independently, without changing editing/live selection. Current
ownership and saved completion records establish eligibility. Backfilled awards and configured
rewards are saved silently and idempotently; failures remain visible and retry on restart or
explicit retry. No false historical dates or inferred lifetime counts. Lifetime tracking starts
at upgrade unless a validated tracked count already exists. General profile GET is read-only.

## Surfaces
Achievements editor supports rules, rewards, enable/disable, chat/overlay toggles and retry.
Viewer Inventory displays badges and progress, including historical provenance and tracked pulls.
The configurable badges command lists earned names with bounded chat output. Overlay celebrations
accompany successful new pulls, preserve the existing reveal, and respect reduced motion.

## Validation
Bound definition count, identifiers, text lengths, thresholds and rewards. Reject malformed
stored award/wallet records rather than resetting them. Unknown removed collections cannot break
ongoing pulls. Never emit celebration before a successful inventory commit. Protect profile
isolation and existing command collision rules. Test local backfill, corrupt data, retry,
concurrency, reward overflow, disabled rules, deletion, import, and UI save/refresh behavior.
