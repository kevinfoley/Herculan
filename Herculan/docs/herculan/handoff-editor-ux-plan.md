# Handoff — editor UI redesign plan

The plan for reworking the editor's panels so a mission designer who has never seen the `.MSN` format can use them. The evidence, finding by finding, is [handoff-editor-ux-review.md](handoff-editor-ux-review.md); numbers below (R1 to R42) are its findings. Part of the [editor handoff](handoff-editor.md).

## The rule

One view per thing a designer authors, the way one friendly dialog in Dynamix's own editor would have written several records at once. Storage details (variant keys, roll sizes, GUIDs, condition numbers, field codes, roster slots) are hidden, or kept under a collapsed **Advanced** header for matching against the file. Lists show what tells their entries apart. A note names a control by its label. When something has no effect in play, the view says so.

## Vocabulary

| Term | Means | Replaces |
|---|---|---|
| **campaign variable** | one of the 1000 numbers carried from mission to mission; the game writes a few after every mission, missions write the rest | "flag" in conditions and the campaign list, "counter" in counter writes (R1) |
| **random choice**, **option** | a variant table and its alternatives, and a random roll and its outcome ranges: one roll, several options, each with a chance | "variant table", "alternative", "draw", "range", "draws below" (R19) |
| **trigger** | an action: who enters which areas, then what happens | "action", "type", "verb" (R6) |
| **win condition**, **lose condition** | a mandatory objective, a failure objective | "mandatory", "failure condition" (R12, R14) |

Numbering a designer sees starts at 1: orders, waypoints, weapon mounts, squadmates (R15, R29, R31, R32).

## Designer tasks

What a designer must be able to do in the editor without reading any doc. Every phase is judged by whether these get easier, and the redesign is done when all four can be done that way:

1. Add two Cybrid hercs that patrol a route.
2. Make reinforcements drop in when the player reaches an area.
3. Add a win condition: destroy the base.
4. Make a herc appear on only some loads of the mission.

## Phases

Phases 2 to 6 are carried out by [handoff-editor-msn-scene.md](handoff-editor-msn-scene.md#steps)'s step 6, which rewrites each view once, on the layout and in its new design, one view or one phase's views per commit, each tested by the user in the app before it is committed.

### 1. No rolling (done)

- The editor never rolls: an open draws every random value at its lowest, so every random choice shows its first option, the same on every open ([What the editor shows](handoff-editor-conditions.md#what-the-editor-shows)).
- A record that version leaves out is "not shown in the editor", in views and refusal reasons alike.
- Covers R3, R5.

### 2. Random choice view

- One view for a variant table and its alternatives, and for a random roll and the ranges over it (R19), reached from its own outliner list and from any of its conditions, which open it with that option highlighted.
- Options with their chances as percents, one decimal allowed (12.5%), whatever internal roll size keeps them exact; what each option is, with what tells it apart (a herc option's weapons when the chassis is the same); "Nothing" with what is left over.
- **Used by**: who picks from it, in game terms (your HERC, the squadmate's HERC, Cybrid hercs (group 5)), and the option each shows in the editor. A record with no object says why (left behind on practice missions).
- A warning when the choice has no effect in play (a squad loadout on a practice mission the practice screen's HERC replaces).
- **Advanced**: key, roll size, condition numbers and ranges.
- A herc's or route's view that draws from a choice says "One option of <choice>; changes apply to: that option wherever it is picked (N hercs) / only this herc" (R24, R33).
- The Conditions list moves under Advanced, without its create buttons (R20).
- Replaces the range view `VariantView.DrawRange` draws.

### 3. Campaign variables and Present when as sentences

- Counter writes everywhere read "Add 1 to campaign variable 30 (label)", headed by what writes them: "Sets campaign variables", "When first achieved, sets campaign variables", "When out of the fight, sets campaign variables" (R1).
- Present when (done): "Always / Only when a campaign variable... / In some loads...", each clause a row, each variable picked from a list by what it holds or by its uses in the mission; the editor makes, reuses and cleans up the conditions behind it (R18, R21), and a record whose details change between versions names what changes (R22) ([Present when](handoff-editor-conditions.md#present-when)).
- A variable the game writes reads by what it holds ("the number of Hercs scrapped in the previous mission"); one missions write is "campaign variable 30"; where it comes from is not looked up across missions (done in Present when).
- Used by entries the editor has no view for (R23), named by effect.

### 4. Triggers and countdowns

- The action view becomes a trigger: **Who** (one picker merging type and target), **Enters** (the areas), **Also fired by**, **Then** (an editable list: bring in a group, with how it arrives; move a group on from its order; start a countdown; add to a campaign variable; send a message) (R6, R8, R16).
- No code prefixes on choices (R7); messages by their text, the file and line under Advanced, the posting rule in variable terms (R9); the unused text line under Advanced (R10).
- A timer is a countdown, "Countdown: 30 s after <trigger>", with no empty slots (R11). "Engaged" range in metres (R17).

### 5. Names instead of numbers

- Triggers, routes, groups and objects named by what they are and do, the number dimmed after (R2, R4, R37, R42); delete blockers likewise (R41).
- Objectives as win and lose conditions, "Must happen: Cybrid hercs (group 4) are written off"; the F11 lines listed apart, with a note that they are only text (R12 to R14); the status-alert text renamed and disabled on a lose condition (R13).
- Orders as "1st order: Search and destroy, target ...", a later order's route disabled unless an objective reads it (R15).

### 6. Sweep

- Object view's debug data under Advanced (R25); group fields (R26: the formation id under Advanced, since the game names no formation; the paints-ground flag as "Base pad: lays its concrete pad on the ground and snaps to the pad's tile"; R27); shared-point notes as links (R28); the squad without "group 0" (R29), its loadouts collapsed in a campaign (R30); weapon mounts (R31); waypoints from 1, the first being 1, with distances (R32); the play area (R34); the Problems list in the editor's own words, the em dash fixed (R35); notes naming controls by their labels (R36); copy-on-write notes dropped (R38); text-line budget and accents (R39); zones with the missions that use them (R40).

## Decided

- "Campaign variable" names the campaign flag array everywhere in the UI.
- No editor-only names for triggers, routes, areas and timers (R2).
- No cross-mission "Set by" lookup for campaign variables.
