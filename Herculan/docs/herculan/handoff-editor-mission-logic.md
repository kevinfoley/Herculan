# Handoff — orders, actions, timers, objectives and text in the editor

A group's orders, the actions and timers that drive a mission, the wiring between them, objectives and the mission's text. Part of the [editor handoff](handoff-editor.md). Which records each tool writes is in [The tools](handoff-editor-document.md#the-tools).

## Orders

Every group's view lists its filled order slots (`OrderSlotsView`), the player's squad's included, whose slot-0 route is the player's route. Each slot offers:

- its verb, the seven named as [`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc) names their states;
- its route, any route of the mission or none; a later slot's route notes that a group walks only its first order's route;
- its subject: none, a group, a herc or a structure (a flyer, which no retail order names, is not offered);
- the action that ends it ([Wiring](#wiring));
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

- **A group** offers the action it waits on, with how it then arrives; the squad has none.
- **A herc, flyer, structure or ground vehicle** outside the squad offers the action it fires when engaged and when defeated, a flyer's engaged action included, which DBSIM reads though no retail flyer sets it.
- **An order slot** offers the action that ends it.

Each is none or any action, with a link. A field a variant supplies offers no None, since an overlay cannot carry the unset value, so a variant-supplied engaged or defeated action cannot be taken away.

## Actions

**New action**, at the top of the outliner's Actions list, and **New action testing this area**, in a trigger area's view, add one and select it. The action view (`ActionView`, through `ActionEditor`) offers:

- **Type**, every one of the eleven `Actions_EvaluateTriggers` has a case for, worded by whose position its areas test ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#trigger-areas--actions_evaluatetriggers-00426b70)). For types 7 to 10 a **target**, a choice of the mission's hercs, flyers, structures or groups by type, with a link; for the others a note that the target is unused. A change to 7 to 10 takes the first record of that kind as target, whatever the action tested before, and is refused when the mission has none; a change to 0 to 6 unsets it, as 334 retail records have it (the editor's choices).
- **Trigger areas**, up to four, each a choice with Remove and a link, and Add after the last; a remove moves later areas up, since the load stops at the first `-1`, and an area already tested is refused. Slots 4 to 7, which no retail record fills, stay `-1`.
- The timers and objects that fire it, as links.
- **Verb**, 0 to 5, worded from the arrival table ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4)), any other value shown as it is; then the groups waiting on it, the orders it ends and the timers it starts, as links.
- **Counter writes**, up to ten, each a counter (0 to 999) and one of the two operations `Action_Activate` knows, clear (5) and +1 (6), with Add after the last (counter 0, +1, as all 66 retail records that write one have it) and Remove, kept filled from the first.
- **Message**, none or a line of the `COMMAND<n>.STR` the mission speaks from, `n` its header's training mission number, which row 2 patches and no launch writes over ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#the-training-mission-number)), each line offered with its text and written as its id plus one in `0x4E`. A training mission's lines are its instructor's sentences, offered by id, each with the sentences after it joined as the training port shows them ([`../retail/simulation/cockpit-messages.md`](../retail/simulation/cockpit-messages.md#the-training-port)). The view says the message is posted once per counter write and not at all when the action writes none, in bold when that leaves it never posted.
- The text ref at `0x44`, which one retail record sets and DBSIM drops, read-only.
- **Delete**, refused while a timer starts on or fires it, a group waits on it, an object fires it (variant alternatives included) or an order ends on it ("Timer 3", "Group 12", "Herc slot 4", "Group 4, order 1").

After each edit every area is coloured again by what tests it.

Gaps: the text line (`0x44`) is not edited, and an action's records' conditions are not shown, so a field a conditioned record supplies holds an edit only in the states it survives in.

## Timers

**New timer**, at the top of the outliner's Timers list, adds one and selects it. The timer view (`TimerView`, through `TimerEditor`) offers:

- the action that starts it; none means it counts down from mission start, as the view says;
- the delay in seconds, 0 to 32767: `ActionTimer_SetDelay` (`004679c0`) sign-extends the stored word into its 32-bit countdown, so a larger one would read as negative and fire at once;
- the actions it fires, up to ten, each with Remove and a link, and Add after the last, kept filled from the first (DBSIM fires every set slot);
- Delete, never refused, since nothing references a timer.

## Objectives

**New objective**, at the top of the outliner's Objectives list, adds one whose subject is the selection, and **Add objective** under Related objectives in a group's or object's view adds one about it; both are disabled, with the reason, for any other selection, a flyer among them, which no retail objective asks about (the user's choice). A new objective (the editor's default) is mandatory, condition 1 (the subject is destroyed or written off), with an empty failure text the author fills in; it is selected.

The objective view (`ObjectiveView`, through `ObjectiveEditor`) offers:

- its Present when;
- mandatory or not;
- the subject, a group, a herc or a structure (boxed in the scene), with a link; a record holding a flyer or a kind no case reads shows it as it is;
- the condition, every code `Mission_EvaluateObjectives` has a case for (0 to 4 and 6 to 10), with a note for 3, 4, 9 and 10, which test the player's data link and not the subject. For condition 0 the route (highlighted), from the routes the subject's group's orders run (the object's own group for an object), and the order slot it reads; a change to condition 0 with no route takes the group's slot-0 route;
- the failure text's three lines, applied when a field lets go, with a note on when the status alert shows them, or Add a failure text when there is none ([The mission's text](#the-missions-text));
- the counter writes, up to ten, each a counter (0 to 999) and one of the objective layer's four operations (set, clear, +1, -1), with Add and Remove, kept filled from the first;
- Delete.

Row 17 has no GUID and nothing references an objective: the load renumbers no ref to one, and DBSIM's objective array (`004a9ec4`, its count `004a9ec0`) is referenced, by `es2_xref.py`, only in `DBSim_SpawnMissionObjects`, which fills it, and `Mission_EvaluateObjectives`, which walks it. An edit writes the record itself, a conditioned one included (retail's carry conditions 79 and 80). A mission file that ends inside its objectives (`DEMO2`) adds and deletes none.

Gaps:

- A subject change leaves a condition-0 objective's route as it was; the view then says it never comes true when the new subject's group runs no order on that route.
- A new objective goes last in row 17, so it is asked after every other, and nothing reorders them; the first mandatory objective not yet true is the one whose failure text the status alert shows.
- The condition ref, which the campaign layer authors, is not edited.

## The mission's text

The Mission view (`MissionView`, its text drawn by `MissionTextView` through `MissionTextEditor`) lists the text package's objective lines (the F11 panel's, up to 10, of which the panel lists 7), briefing lines and intelligence lines (up to 30 each; the mission screen shows them in a campaign, joined into one text). Each is a field with Up, Down and Remove, and Add a line after the last; the lists stay filled from their first slot, as block 13 assumes. A mission with no text package (`TRAIN1`, `TRAIN3`) gets one on its first line, as New mission writes one.

A line, here or in a failure text, writes the `.ENG` record whose text the generated `mission.str` line holds. When another field references the same id (another objective, a text package slot, an action, a debrief slot, whatever its condition) the edited field first gets its own copy, and the view says when a line is shared. A failure text's ids are its id and the next two in the `.ENG`'s order. A removed line takes its id's records when nothing references the id any more. An add, or a shared line's copy, that would take `mission.str` past the 50 lines DBSIM reads is refused, with the reason under the button.

A field draws only the characters the game's fonts have glyphs for, codes 32 to 248; anything else typed shows in red and is left out when the field lets go, and the view notes that version 1.0's mission screen shows a letter past code 127 as a space ([`../retail/shell/mission-screen.md`](../retail/shell/mission-screen.md#the-summary-text-box)). Undo puts the `.ENG` back too. The rebuild writes the generated `mission.str` over the handoff's first, so the outliner's objective lines and an objective's failure text follow.

Gaps:

- A line is edited in the state the generation shows: a conditioned replacement that fails there is neither shown nor written, and a line whose id has no record surviving in it shows a note in place of its field.
- Only row 4's first record, which block 13 is written from, is edited; its briefing movie (`0x8E`) is not set.
- Nothing checks a line's length: the mission screen's wrap asserts on a word wider than its box ([`../retail/shell/mission-screen.md`](../retail/shell/mission-screen.md#the-summary-text-box)), and the status alert's and F11 panel's labels are not measured.
- A line's trailing newline, which ends a paragraph on the mission screen, is kept as the record had it and cannot be added or removed; a new line has none.
- The debrief (row 5) and the movies (row 3) are not shown.
