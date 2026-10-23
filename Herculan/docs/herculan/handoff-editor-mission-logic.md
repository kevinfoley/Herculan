# Handoff — orders, triggers, countdowns, objectives and text in the editor

A group's orders, the triggers and countdowns that drive a mission, the wiring between them, the campaign variables a mission sets, objectives and the mission's text. Part of the [editor handoff](handoff-editor.md). Which records each tool writes is in [The tools](handoff-editor-document.md#the-tools).

The editor's UI calls a row-10 action a **trigger** and a row-11 action timer a **countdown**, in every label, link and refusal reason; the code keeps the retail names (`ActionEditor`, `TimerEditor`, `ActionSelection`). A trigger and a countdown are still named by number, "Trigger 4" and "Countdown 2", their place in rows 10 and 11.

## Orders

Every group's view lists its filled order slots (`OrderSlotsView`), the player's squad's included, whose slot-0 route is the player's route. Each slot offers:

- its verb, the seven named as [`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc) names their states;
- its route, any route of the mission or none; a later slot's route notes that a group walks only its first order's route;
- its subject: none, a group, a herc or a structure (a flyer, which no retail order names, is not offered);
- the trigger that ends it ([Wiring](#wiring));
- its Present when ([Present when](handoff-editor-conditions.md#present-when)).

Add an order fills the slot after the last, Remove moves every later order up, and Up and Down trade places, so the slots stay filled from slot 0: `Group_OrderTick` moves on only to a next slot holding an order. A new order is the editor's choice of a retail shape (`GroupOrders.NewOrder`), on slot 0's route:

- the squad: patrol, no subject (36 of the 63 slot-0 orders of the retail squad's records);
- a convoy: travel, no subject (11 of the 12 retail convoys' slot-0 orders; `C2_08`'s two supply transports, the one retail convoy with later orders, guard and then travel);
- any other group: search/destroy on the squad.

7 of the 609 order GUIDs the retail groups name are named by two or three groups, each in the same slot, none within one group. Editing a slot whose order another slot names first gives it its own copy, and the view says when an order is shared. A removed order's records go only when no slot names it any more.

Shapes no retail order has are not made:

- a first order with no route in a group with no point of its own, which stands on that route (each of the 35 retail group records whose slot-0 order names no route names a point, 8 of them no heading): its Route offers no None, with the reason;
- search/destroy, ramming or following with no subject (all 385, 17 and 9 retail orders of those verbs name one, and `Group_IsOrderComplete` reads search/destroy's): a verb change to one of them on an order with no subject gives it the squad as subject, as the view notes, and Subject offers no None.

Removing a group's last order is refused, with the reason, where DBSIM faults without one: a group with no heading of its own, and a herc group other than the squad (the mission check's two group findings). A flyer group keeps its last order too, since every retail one has one and what its AI does with none is not established (the editor's rule). A route an order names cannot be deleted.

Not edited: an order's formation and point.

## Wiring

Three records wait on a trigger, each through a field of its own, and each is set from both ends: from the record's view, and from the trigger's Then list ([Triggers](#triggers)).

- **A group** offers the trigger it waits on ("Waits on"), with how it then arrives; the squad has none.
- **A herc, flyer, structure or ground vehicle** outside the squad offers the trigger it fires when engaged and when defeated ("When engaged, fires", "When defeated, fires"), a flyer's engaged one included, which DBSIM reads though no retail flyer sets it. The note under them gives the engagement distance in metres (`Detection.EngagementRange`, 300 m) ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6)).
- **An order slot** offers the trigger that ends it ("Ends when").

Each is None or any trigger of the layout, "Trigger N" (`ActionChoice`), with a link. A field a variant supplies offers no None, since an overlay cannot carry the unset value, so a variant-supplied engaged or defeated trigger cannot be taken away.

## Campaign variable writes

A trigger, an objective, and a herc, flyer, structure, ground vehicle or group put out of action each set up to ten campaign variables, the array a later mission's [Present when](handoff-editor-conditions.md#present-when) tests ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4)). Every view lists them the same way (`CampaignVariableWrites`), one sentence per write: "Add 1 to campaign variable 100", "Set campaign variable 100 to 0", "Subtract 1 from campaign variable 100", with the variable's meaning after its number where the game sets or reads it ([the picker](handoff-editor-conditions.md#the-campaign-variable-picker)): "Add 1 to campaign variable 22 (ATC50 units the debrief adds to the armory)". A code the record's layer has no case for reads "Unknown operation on campaign variable N (no effect in play)". The three layers number their operations differently, and a list offers only its own:

| Writes | Heading | Operations, by code |
|---|---|---|
| a trigger ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#the-four-ways-an-action-activates)) | Sets campaign variables | Add 1 (6), Set to 0 (5) |
| an objective, win or lose condition alike, the first time its condition holds ([`../retail/simulation/mission-objectives.md`](../retail/simulation/mission-objectives.md#the-record)) | The first time it comes true, sets campaign variables | Set to 1 (4), Set to 0 (5), Add 1 (6), Subtract 1 (7) |
| a herc, the first time it is destroyed or immobilized ([`../retail/simulation/component-damage.md`](../retail/simulation/component-damage.md#going-out-of-the-fight)) | When destroyed or immobilized, sets campaign variables | Set to 0 (1), Add 1 (2), Set to 1 to 4 (`0x0d` to `0x10`) ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#the-out-of-action-report)) |
| a flyer, when its component 0 goes; a structure or a ground vehicle, when it is destroyed, a ground vehicle sharing the structure's damage path ([`../retail/simulation/structure-behaviour.md`](../retail/simulation/structure-behaviour.md#taking-damage--base_applydamage-00404d70)) | When destroyed, sets campaign variables | as a herc's |
| a group, when its last member to go finds every other destroyed or immobilized, a disarmed one counting as standing | When every member is destroyed or immobilized, sets campaign variables | as a herc's |

In the trigger and objective views a write is a sentence, then a choice of the layer's operations and a variable from the picker's list for a write, with Remove; Add follows the last, up to ten, and the list stays filled from the first. Choosing a variable keeps the operation, or makes it Add 1 when the layer has no case for it. A new trigger write is campaign variable 0, Add 1, as all 66 retail action writes are, each sending the trigger's message ([Triggers](#triggers)); a new objective write is campaign variable 52, Add 1, the first write of 4 of the 31 retail objectives that write one. An objective's list with no write says "It sets no campaign variable."; a trigger's Then list shows its writes only when it sets one, and its Add... menu's Set a campaign variable adds the first.

The other three are read-only and shown only when they list a write. A HERC of the player's squad sets none of its own in play, since its slots start empty ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#the-out-of-action-report)): where its record lists writes, its view says "None in play" under the heading instead.

## Triggers

**New trigger**, at the top of the outliner's Triggers list, and **New trigger testing this area**, in a trigger area's view, add one and select it. The trigger view (`TriggerView`, through `ActionEditor`) reads the layout and `MissionIndex` by GUID, so it shows any trigger of the `.MSN`, and reads top to bottom as "when this happens, do that":

- **Fires when**, one choice of the eleven kinds `Actions_EvaluateTriggers` has a case for, each worded by whose position its areas test: "The player's HERC", "Any HERC of the player's squad", "Any member of a human (Cybrid, herc, flyer, structure) group that has arrived", "A particular herc" (flyer, "structure or ground vehicle"), "Any member of a particular group" ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70)). The five group kinds note that a group still waiting to arrive is not tested. For the four particular kinds a second choice under it picks which one, from the layout's records of that row by name and group number (a group by its number, roster and team), with a link; "No target chosen." in bold when it has none. A change to a particular kind takes the first record of that kind the generation exports as target, whatever the trigger tested before, and is refused when the mission has none; a change to any other kind unsets it, as 334 retail records have it (the editor's choices). A stored kind with no case reads "An unknown kind (12)", with its value.
- **enters one of these areas**, up to four, each a choice of the mission's areas with Remove and a link, and Add an area after the last; a remove moves later areas up, since the load stops at the first `-1`, and an area already tested is refused. Slots 4 to 7, which no retail record fills, stay `-1`. With none, a note says only a countdown or an object can fire it.
- **Also fired by**: the countdowns whose fired list holds it ("when it runs out") and the objects that fire it when engaged or defeated, as links. They come from `MissionIndex.Links`, which `TriggerWiring` builds from the layout's base fields: every countdown, every placed herc, flyer, structure and ground vehicle outside the squad (a squad HERC is built from `player.mec` and fires nothing of its record's, and a record no group places is never spawned), every group's waiting trigger and every order slot. With no area and nothing else firing it, a bold note says nothing fires it and names the three places that can.
- **Then**, with **Add...** beside it, a menu of Bring in a group, Move a group on from its order, Start a countdown (New countdown, or one of the mission's), Set a campaign variable and Send a message. An entry that links another record to the trigger is written in that record, through the same edit its own view makes (`TriggerFollowers`): a group's deployment action (`ActionWiring.SetDeployment`), an order slot's ending action (`GroupOrders.SetAction`, which copies an order another slot shares first) or a countdown's starting action (`TimerEditing.SetStart`). Its Remove unsets that one field, with the consequence on hover: the group is then in the mission from the start, the order no longer ends on a trigger, the countdown counts down from mission start. A menu entry for a group or order already wired to another trigger says so ("now waits on Trigger 2"); choosing it moves it to this one. The entries, each shown only when there is one:
  - **Brings in**: each group waiting on it, a link with Remove, and under them **Arrives**: "By drop pod, in front of the player" (within 90° of the player's heading), "By drop pod, straight ahead of the player" (within 22.5°), "On foot, from behind the player", "On foot, from ahead of the player" or "In place" (verbs 2, 3, 4, 5 and 1), with a note of how far from the player and on what bearing it arrives, or that it goes live where it stands ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4)). The arrival is one field of the trigger, so the view says every group it brings in arrives the same way. `Group_DeploymentCheck` treats every value but 2 to 5 as in place, so a stored 0 or a value past 5 shows as In place, a value past 5 with a note giving it, and choosing In place over either writes nothing (the editor's choice).
  - **Moves these groups on to their next order**: each order slot ending on it, as "Group 3, from its 1st order (search/destroy)", with Remove.
  - **Starts these countdowns**: each countdown it starts, a link with Remove, its delay as a field ([Countdowns](#countdowns)) and the triggers it fires, as links. New countdown makes one started by the trigger, of 10 s and firing nothing, as a new countdown is made from the outliner (the editor's choice), and stays in the trigger's view.
  - **Sets campaign variables**, up to ten ([Campaign variable writes](#campaign-variable-writes)).
  - **Sends a message**: a choice of the lines of the `COMMAND<n>.STR` the mission speaks from, `n` its header's training mission number (row 2's base, `TriggerMessages`), which no launch writes over ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#the-training-mission-number)), each offered by its text cut to fit, the whole line under the choice when it is cut, with Remove; written as its id plus one in `0x4E`. A training mission's lines are its instructor's sentences, each with the sentences after it joined as the training port shows them ([`../retail/simulation/cockpit-messages.md`](../retail/simulation/cockpit-messages.md#the-training-port)). The note says HQ sends it on the pilot and squad channel (a training mission's instructor shows it) once for each campaign variable the trigger sets, and in bold that it is never sent when the trigger sets none ([`../retail/simulation/cockpit-messages.md`](../retail/simulation/cockpit-messages.md#its-speakerless-set)).
- **Delete**, refused while a countdown starts on or fires it, a group waits on it, an object fires it or an order ends on it, in any record whatever its condition: each a short identity link ("Countdown 3", "Group 12", "SCARAB (group 5)", "Group 4, 2nd order"), and counts of the options of random choices and the orders no group runs that reference it.
- **Advanced**, collapsed and shown only when there is something under it: the text ref at `0x44` as "Leftover text (no effect in play): ...", which one retail record sets and DBSIM drops, and the message's id and file.

A trigger has no Present when: no retail row-10 record carries a condition ([Present when](handoff-editor-conditions.md#present-when)). A trigger area's view lists each trigger testing it the same way read-only, its Then list as one line per entry (`TriggerView.DrawSummary`). After each edit every area is colored again by what tests it.

Gaps: the leftover text is not edited, and a trigger's records under a condition other than its first record's (none in retail) are not shown, so an edit writes the base.

## Countdowns

**New countdown**, at the top of the outliner's Countdowns list, adds one and selects it; a trigger's Add... menu adds one that trigger starts ([Triggers](#triggers)). The countdown view (`CountdownView`, through `TimerEditor`) reads the layout by GUID and offers:

- its title, "Countdown 2: 30 s after trigger 4" or "Countdown 2: 30 s from mission start";
- **Starts when**: "The mission starts" or a trigger, with a link;
- **Delay (s)**, 0 to 32767: `ActionTimer_SetDelay` (`004679c0`) sign-extends the stored word into its 32-bit countdown, so a larger one would read as negative and fire at once;
- **When it runs out, fires**: each trigger it fires, a choice with Remove and a link, and Add a trigger after the last, up to ten. Only filled slots are shown, a record with a gap included; an add goes after the last filled slot and a remove moves the later ones up, so an edited list stays filled from the first (DBSIM fires every set slot);
- Delete, never refused, since nothing references a countdown.

## Objectives

**New objective**, at the top of the outliner's Objectives list, adds one whose subject is the selection, and **Add objective** under Related objectives in a group's or object's view adds one about it; both are disabled, with the reason, for any other selection, a flyer among them, which no retail objective asks about (the user's choice). A new objective (the editor's default) is mandatory, condition 1 (the subject is destroyed or written off), with an empty failure text the author fills in; it is selected.

The objective view (`ObjectiveView`, through `ObjectiveEditor`) offers:

- its Present when;
- mandatory or not;
- the subject, a group, a herc or a structure (boxed in the scene), with a link; a record holding a flyer or a kind no case reads shows it as it is;
- the condition, every code `Mission_EvaluateObjectives` has a case for (0 to 4 and 6 to 10), with a note for 3, 4, 9 and 10, which test the player's data link and not the subject. For condition 0 the route (highlighted), from the routes the subject's group's orders run (the object's own group for an object), and the order slot it reads; a change to condition 0 with no route takes the group's slot-0 route;
- the failure text's three lines, applied when a field lets go, with a note on when the status alert shows them, or Add a failure text when there is none ([The mission's text](#the-missions-text));
- the campaign variables it sets the first time it comes true, up to ten ([Campaign variable writes](#campaign-variable-writes));
- Delete.

Row 17 has no GUID and nothing references an objective: the load renumbers no ref to one, and DBSIM's objective array (`004a9ec4`, its count `004a9ec0`) is referenced, by `es2_xref.py`, only in `DBSim_SpawnMissionObjects`, which fills it, and `Mission_EvaluateObjectives`, which walks it. An edit writes the record itself, a conditioned one included (retail's carry conditions 79 and 80). A mission file that ends inside its objectives (`DEMO2`) adds and deletes none.

Gaps:

- A subject change leaves a condition-0 objective's route as it was; the view then says it never comes true when the new subject's group runs no order on that route.
- A new objective goes last in row 17, so it is asked after every other, and nothing reorders them; the first mandatory objective not yet true is the one whose failure text the status alert shows.
- The condition ref, which the campaign layer authors, is not edited.

## The mission's text

The Mission view (`MissionView`, its text drawn by `MissionTextView` through `MissionTextEditor`) lists the text package's objective lines (the F11 panel's, up to 10, of which the panel lists 7), briefing lines and intelligence lines (up to 30 each; the mission screen shows them in a campaign, joined into one text). Each is a field with Up, Down and Remove, and Add a line after the last; the lists stay filled from their first slot, as block 13 assumes. A mission with no text package (`TRAIN1`, `TRAIN3`) gets one on its first line, as New mission writes one.

A line, here or in a failure text, writes the `.ENG` record whose text the generated `mission.str` line holds. When another field references the same id (another objective, a text package slot, a trigger, a debrief slot, whatever its condition) the edited field first gets its own copy, and the view says when a line is shared. A failure text's ids are its id and the next two in the `.ENG`'s order. A removed line takes its id's records when nothing references the id any more. An add, or a shared line's copy, that would take `mission.str` past the 50 lines DBSIM reads is refused, with the reason under the button.

A field draws only the characters the game's fonts have glyphs for, codes 32 to 248; anything else typed shows in red and is left out when the field lets go, and the view notes that version 1.0's mission screen shows a letter past code 127 as a space ([`../retail/shell/mission-screen.md`](../retail/shell/mission-screen.md#the-summary-text-box)). Undo puts the `.ENG` back too. The rebuild writes the generated `mission.str` over the handoff's first, so an objective's failure text follows; the outliner's objective lines read the `.ENG` itself.

Gaps:

- A line is edited in the state the generation shows: a conditioned replacement that fails there is neither shown nor written, and a line whose id has no record surviving in it shows a note in place of its field.
- Only row 4's first record, which block 13 is written from, is edited; its briefing movie (`0x8E`) is not set.
- Nothing checks a line's length: the mission screen's wrap asserts on a word wider than its box ([`../retail/shell/mission-screen.md`](../retail/shell/mission-screen.md#the-summary-text-box)), and the status alert's and F11 panel's labels are not measured.
- A line's trailing newline, which ends a paragraph on the mission screen, is kept as the record had it and cannot be added or removed; a new line has none.
- The debrief (row 5) and the movies (row 3) are not shown.
