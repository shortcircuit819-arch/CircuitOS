# Achievements and badges

CircuitOS 1.1.0 adds permanent achievement records, optional Scrap rewards, and celebrations in chat and on the OBS overlay. Each game profile has its own settings and viewer awards.

## Existing viewers are included

When CircuitOS starts, it checks every profile, including profiles that are not Live. Existing viewers do not need to redeem again. The check uses items they currently own and saved collection-completion records to award any eligible missing badges.

Historical awards are recorded silently: no chat announcement or overlay celebration is replayed. Any configured reward is credited once with the award. The default automatic badge reward is **0**, so the initial upgrade does not add Scrap unless you have configured a reward.

Viewer Inventory labels these as historical awards and shows when they were recorded. It does not invent the original earning date. Lifetime pull tracking starts at zero for existing viewers; current holdings cannot tell us how many items they previously pulled, salvaged, or removed.

## Automatic collection badges

Open **Achievements** to configure the selected profile. **Automatic collection badges** awards four milestones for each nonempty collection:

- First unique item.
- 25% collected.
- 50% collected.
- Collection complete.

Percentage targets round up to whole items. In a four-item collection, the first item earns both the first-item and 25% badges. In a one-item collection, one item earns all four. Each is a separate badge, so a configured automatic reward is paid for each newly awarded milestone.

Duplicates do not increase unique-item progress. A saved completion record also qualifies a viewer for that collection's milestones, even if they no longer hold every item.

## Custom goals

Choose **Add Achievement**, enter a name and optional description, then select a goal:

| Goal | What counts |
| --- | --- |
| Collection completion (%) | Unique items owned in one selected collection, or its saved completion record. Target: 1–100%. |
| Unique items owned | Distinct catalog items currently owned, across all collections or one selected collection. |
| Completed collections | Collections with a saved completion record, plus collections whose items are all currently owned. Each collection counts once. |
| Lifetime pulls (since tracking began) | Successfully saved pulls recorded after achievement tracking started. Earlier pulls are not estimated. |

Set a positive whole-number target and a reward from **0 to 1,000,000** in your profile's currency. Zero creates a badge without a currency payout. You can create up to 100 custom goals.

The **Stable ID** identifies the achievement independently of its name. Choose it before the first save; the editor locks saved IDs. Renaming a goal or changing its target/reward does not rewrite an existing award or pay it again. Removing or disabling a goal also leaves previously earned records intact. Reusing an old ID does not create a new award for viewers who already earned it.

Click **Save Achievements** to apply changes. Saving checks existing viewers in the selected profile immediately, including any newly eligible custom goals, and silently pays their configured rewards once. Unsaved goals do not affect live pulls.

## Rewards and records

Awards and their currency rewards are saved together in the viewer inventory. Existing inventory is backed up before atomic replacement. Retrying the same check does not grant the same award again.

Changing the automatic reward later does not top up badges already earned at zero or another amount. It applies to future missing awards. The same rule applies to custom rewards.

Earned badges remain after normal collection edits, goal changes, or salvage. They are part of the inventory data: a deliberate viewer reset or restoration of an older inventory backup changes that data too. Keep backups when moving or restoring a game.

## Viewer progress and chat

Select a viewer in **Viewer Inventory**, then expand **Achievements · N earned** to see earned badges, progress toward goals, tracked pulls, and historical-award labels. This section starts collapsed to keep the inventory view compact.

The default chat command is:

```text
!badges
!badges 2
```

The optional number selects a page, with up to three earned badge names per page. Viewers see their own awards. The command name is configurable alongside the profile's other chat commands.

During upgrade, CircuitOS preserves existing command names and chooses an unused badge alias for each older profile. If `!badges` is already taken, a profile may receive `!badges-2` or another numbered alias. Check that profile's command settings before telling viewers which command to use.

## Announcements and overlay

**Announce new awards in chat** and **Celebrate new awards on the overlay** are independent switches. They apply to newly reached achievements during a live pull. Disabling either presentation option does not disable earning or rewards.

The overlay shows up to three achievement cards with the pull reveal and summarizes additional awards. The Overlay Editor's achievement preview uses sample data; it does not grant badges or rewards. See [OBS setup](obs-lower-quarter.md) for the Browser Source instructions.

Turn off **Automatic collection badges** to use only custom goals, or turn off **Enable achievements** to pause new awards. Existing awards remain visible. Successful pulls continue to be tracked while awards are disabled.

## If a historical check fails

The Achievements screen lists profiles that need attention and offers **Retry historical awards for all profiles**. Save or discard any unsaved achievement edits before retrying. Retry checks all profiles; it does not require a viewer redemption.

If settings were saved but an inventory write failed, the error says so. Once the underlying problem is fixed, retry or restart CircuitOS. Corrupt inventory, malformed achievement data, or a reward that would overflow a balance is left unchanged rather than partially granting a badge. Other healthy profiles can still finish their checks.

For damaged files, use the [installation and recovery guide](installation-and-updates.md) and your backups. Do not delete a viewer's achievement records to retry a failed check: those records prevent duplicate rewards.
