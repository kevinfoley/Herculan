# Handoff — editor UI redesign plan

The plan for reworking the editor's panels so a mission designer who has never seen the `.MSN` format can use them. The evidence, finding by finding, is [handoff-editor-ux-review.md](handoff-editor-ux-review.md); numbers below (R1 to R42) are its findings. Part of the [editor handoff](handoff-editor.md).

## The rule

One view per thing a designer authors, the way one friendly dialog in Dynamix's own editor would have written several records at once. Storage details (variant keys, roll sizes, GUIDs, condition numbers, field codes, roster slots) are hidden, or kept under a collapsed **Advanced** header for matching against the file. Lists show what tells their entries apart. A note names a control by its label. When something has no effect in play, the view says so.

## Vocabulary

| Term | Means | Replaces |
|---|---|---|
| **campaign variable** | one of the 1000 numbers carried from mission to mission; the game writes a few after every mission, missions write the rest | "flag" in conditions and the campaign list, "counter" in the writes (R1) |
| **random choice**, **option** | a variant table and its alternatives, and a random roll and its outcome ranges: one roll, several options, each with a chance | "variant table", "alternative", "draw", "range", "draws below" (R19) |
| **trigger** | an action: who enters which areas, then what happens | "action", "type", "verb" (R6) |
| **countdown** | an action timer: a delay, from the mission's start or a trigger, after which it fires triggers | "timer" (R11) |
| **win condition**, **lose condition** | a mandatory objective, a failure objective | "mandatory", "failure condition" (R12, R14) |
| **play area** | the mission box: the extent of the mission's positions, which the heads-down map frames, past which the player is warned and then the mission aborted | "mission box", "abort line" (R34) |

Numbering a designer sees starts at 1: records, orders, waypoints after the start, hardpoints, squadmates (R15, R29, R31, R32).

## Designer tasks

What a designer must be able to do in the editor without reading any doc. Every phase is judged by whether these get easier, and the redesign is done when all four can be done that way:

1. Add two Cybrid hercs that patrol a route.
2. Make reinforcements drop in when the player reaches an area.
3. Add a win condition: destroy the base.
4. Make a herc appear on only some loads of the mission.

## Phases

Each view of phases 2 to 6 reads the `.MSN`'s layout ([Selection and panels](handoff-editor-viewing.md#selection-and-panels)) and is laid out as its phase describes; one view or one phase's views go in per commit, each tested by the user in the app before it is committed.

### 1. No rolling (done)

- The editor never rolls: an open draws every random value at its lowest, so every random choice shows its first option, the same on every open ([What the editor shows](handoff-editor-conditions.md#what-the-editor-shows)).
- A record that version leaves out is shown in full from the layout, in its view as in the scene ([Selection and panels](handoff-editor-viewing.md#selection-and-panels)).
- Covers R3, R5.

### 2. Random choice view

- Variant tables (done, [Variants](handoff-editor-conditions.md#variants)): one view for a table and its options (R19), reached from the Mission panel's Random choices list and from any of its conditions, which open it with that option highlighted.
- Options with their chances as percents, one decimal allowed (12.5%), over whichever roll size keeps them exact; what each option is, with what tells it apart (a herc option's weapons when the chassis is the same); "Nothing" with what is left over (done).
- **Used by**: who picks from it, in game terms (the player's HERC, Squadmate 2's HERC, SCARAB (group 5)), and the option each shows in the editor; a record with no object says why (done).
- A warning when the choice has no effect in play, such as a squad loadout on a practice mission the practice screen's HERC replaces (done).
- **Advanced**: key, roll size, condition numbers and ranges (done).
- A herc's view that picks from a choice offers "Random choice:" and "Changes apply to: that option, wherever it is picked (N hercs) / only this herc"; a route's says its waypoints are an option of the choice (R24, R33, done).
- The Conditions list is under Advanced, without its create buttons (R20, done).
- A random roll (a draw) and its ranges keep their own views: every retail roll picks only a debrief loss line, which the Debrief view lists read-only, and they join the Random choice view with the loss line's switch (phase 7's step 4).

### 3. Campaign variables and Present when as sentences (done)

- Writes everywhere read "Add 1 to campaign variable 22 (ATC50 units the debrief adds to the armory)", in one shared list, headed by what writes them: "Sets campaign variables", "The first time it comes true, sets campaign variables", "When destroyed or immobilized, sets campaign variables" and its flyer, structure and group forms; each variable picked from the picker's list for a write, which names those the debrief reads by what it does with them (R1, [Campaign variable writes](handoff-editor-mission-logic.md#campaign-variable-writes)).
- Present when (done): "Always / Only when a campaign variable... / In some loads...", each clause a row, each variable picked from a list by what it holds or by its uses in the mission; the editor makes, reuses and cleans up the conditions behind it (R18, R21), and a record whose details change between versions names what changes (R22) ([Present when](handoff-editor-conditions.md#present-when)).
- A variable the game writes reads by what it holds ("the number of Hercs scrapped in the previous mission"); one missions write is "campaign variable 30"; where it comes from is not looked up across missions (done in Present when).
- Used by entries the editor has no view for (R23), named by effect, "(not shown in the editor)" after each (done, [Conditions](handoff-editor-conditions.md#conditions)).

### 4. Triggers and countdowns (done)

- The action view is the trigger view, on the layout ([Triggers](handoff-editor-mission-logic.md#triggers)): **Fires when** (one choice of whose position its areas test, the herc, flyer, structure or group of the four particular kinds in a second choice under it), the areas it **enters**, **Also fired by**, and **Then**, an editable list with an Add... menu: bring in a group, with how every group it brings in arrives, shown only when one does; move a group on from its order; start a countdown, a new one included; set a campaign variable; send a message. Each entry writes the record that holds the link (R6, R8, R16).
- No code prefixes on choices (R7); messages by their text, the file and id under Advanced, the sending rule in campaign variable terms (R9); the unused text line under Advanced as leftover text (R10).
- A timer is a countdown, "Countdown 2: 30 s after trigger 4", its fired list with no empty slots (R11). The "Engaged" distance in metres (R17).
- Every label, link and refusal reason says trigger and countdown.

### 5. Names instead of numbers (done)

- Triggers, routes, areas, countdowns, groups and objects named by what they are and do, the number dimmed after, in titles, links, pickers, outliner rows, delete blockers and the Toolbox's Join (R2, R4, R37, R41's order numbering, R42); a long name cut to fit, whole on hover ([Names](handoff-editor-mission-logic.md#names), done).
- Objectives as win and lose conditions, "Must happen: Cybrid hercs (group 4) are written off", with Role, Who and What happens; the F11 lines listed apart, with a note that they are only text (R12 to R14); the status alert text renamed and disabled on a lose condition (R13) ([Objectives](handoff-editor-mission-logic.md#objectives), done).
- Orders as "1st order" with Does, Route, Target and Ends when, each verb with what it does; a later order's route disabled unless an objective reads it, which is listed as a link (R15); the shared-order note dropped (R38's order note) ([Orders](handoff-editor-mission-logic.md#orders), done).

### 6. Sweep (done)

- Every record number a designer sees from 1 ([Decided](#decided)); waypoints as the cockpit numbers them, "Start" and then "Waypoint 1" on, each row with its leg from the one before ("Waypoint 2: 420 m from waypoint 1"), in the route view, the scene and Insert after and Remove (R32, [Routes](handoff-editor-placement.md#routes)); the mission box as the play area, with what grows with it in play (R34, [Moving](handoff-editor-placement.md#moving)); the Problems list in the editor's own words by finding kind, the lint's em dash fixed (R35, [Mission check](handoff-editor-viewing.md#mission-check)); notes naming controls by their labels (R36); copy-on-write notes dropped (R38); the text-line budget, with what shares it, and accents (R39, [The mission's text](handoff-editor-mission-logic.md#the-missions-text)); zones with their size and the retail missions that use them (R40, [New mission](handoff-editor-viewing.md#new-mission)); a structure titled by its kind and MFD name, its type number under Advanced (R4).
- Done, with the object and group views on the layout: the object view's type number, model, triangles, texture and hit radius under Advanced, "Kind" and "Roster slot" gone (R25); the group's formation under Advanced, since no retail file names a formation, and its paints-ground flag as "Base pad: lays its concrete pad on the ground tile under it, N square" (R26); "Starts at:" (R27); "Also on this spot, moving with it:" with a link per sharer, in the group, route and trigger area views (R28); a group's own route field as "its own route (not editable yet)" in a route's delete blockers (R41's own route) ([Groups](handoff-editor-viewing.md#groups), [Moving](handoff-editor-placement.md#moving)).
- Done, with the squad and the hardpoints on the layout: the squad's views without "group 0" or "member slot", "Player's HERC" and "Squadmate 1" to "Squadmate 3" by place in the list (R29); in a campaign mission each HERC's chassis and weapons under a collapsed "HERC and weapons (not used in the campaign)" header, with what the squad's list still sets (R30) ([The player's squad](handoff-editor-roster.md#the-players-squad)); mounts as "Hardpoint 1" up, an empty one "(empty)" (R31); a Cybrid's weapons as dropdowns of what retail missions fit ([A Cybrid's weapons](handoff-editor-roster.md#a-cybrids-weapons)). R24's "Changes apply to:" wording was already phase 2's.

### 7. Debrief

Retail varies a debrief only through its `.ENG` text and its row-3 movie: 54 missions carry one unconditioned row-5 record and 8 none, and a line's replacements and the movie's records are keyed on the same conditions (`C1_01`: lines 238 and 239 and movie GUID 240 on conditions 44 to 52, over campaign variables 0 and 250). So the view is by outcome, the user's choice:

- **Outcomes**: one entry per condition the debrief's replacements and movie records use, headed by its "When" clauses as Present when words them, with the debrief text it shows (each line the outcome replaces, the rest from Otherwise) and its movie; then **Otherwise**, the unconditioned text and movie. Add and remove an outcome; edit its clauses, text and movie. The editor writes the `.ENG` replacements and row-3 records behind it.
- **Losses line**: one switch, "Report Hercs and squadmates lost", which adds or removes the standard eleven-record line chapters 1 to 3 carry ([The debrief's loss line](../retail/formats/msn-mission-file.md#the-debriefs-loss-line)); its cases listed read-only, their texts editable, its roll shown in the Random choice view (phase 2's last bullet).
- Steps, each tested in the app before its commit: (1) the view, outcomes and losses line shown, an outcome's lines editable (done, [The debrief](handoff-editor-mission-logic.md#the-debrief)); (2) the movie; (3) adding and removing outcomes and editing their clauses; (4) the losses switch and its roll in the Random choice view.

## Decided

- "Campaign variable" names the campaign flag array everywhere in the UI.
- No editor-only names for triggers, routes, areas and timers (R2).
- No cross-mission "Set by" lookup for campaign variables.
- Every record number a designer sees counts from 1: groups, triggers, countdowns, routes, areas, objectives and the rest, as orders and waypoints do (phase 6). The number is the record's place among its row's identities, not its index in the file, so nothing matches against the file through it.
- A Cybrid herc's weapons are editable, each mount offering the weapons retail missions fit on that mount of that chassis, read from the install's missions; a mount no retail mission fits stays read-only (phase 6).
