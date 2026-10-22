# Handoff — editor UI review: concepts, not records

A read-only review of the mission editor's Properties views, the Mission panel, the Toolbox, File > New mission and the Problems list. It lists where the UI shows how the `.MSN` stores something instead of what a mission designer authors. The variant table and range-over-a-table views (`VariantView`, `ConditionView`'s range branch for a table) are left out: they are being redesigned as one "Random choice" view. A finding that touches them says how it should line up with that design.

Paths are under `Herculan/src/Herculan.Engine.Editor/` unless they say otherwise. Findings run from most to least impact. Each one gives the current text, why a designer would not understand it, and a proposed rewrite. Wording in a proposal is a suggestion and still needs the user's approval. **Invention** marks a proposal that adds something retail does not have.

## Cross-cutting

### 1. One thing called "counter" in one place and "flag" in another

- **Where:** `ActionView.cs:361` "Counter writes:", `:387` "Counter"; `ObjectiveView.cs:236` "The first time it comes true:", `:266` "Counter"; `PropertiesPanel.cs:577` "counter 12: set to 3"; `ActionView.cs:102` "counter N: +1". Then `CampaignStateView.cs:69` "Flag 30" and `ConditionView.cs:102` "Flag".
- **Why:** Counters and flags are the same 1000-entry array: `ActionEditing.Counters`, `ObjectiveEditing.Counters` and `ConditionEditing.Flags` are all `MissionGenerator.CampaignFlagCount`. An action that writes "counter 30" sets the "flag 30" that a later mission's Present when tests. A designer cannot tell the two names mean one thing, which leaves the campaign layer impossible to author. The labels that `CampaignState.Label` gives flags 0 to 10 never show beside a counter.
- **Proposal:** Use "campaign flag" everywhere. Write each operation as a sentence: "Set campaign flag 30 to 1", "Add 1 to campaign flag 30", "Clear campaign flag 30". Put the `CampaignState.Label` text after the number. Head the lists "Sets campaign flags" (action), "When first achieved, sets campaign flags" (objective) and "When out of the fight, sets campaign flags" (object and group). Under each flag field, add a short "Read by:" list of the Present whens in this mission that test that flag. This one change also covers the "Counter overview" slice in `handoff-editor.md`.

### 2. Records named by number, and pickers that list only numbers

- **Where:** `ActionChoice.cs:14,20` lists "Action 0" to "Action N" with nothing else. It is used by the group view's "Waits on" (`PropertiesPanel.cs:420`), the object's "When engaged/defeated, fires" (`:464`), the timer's "Starts when" (`TimerView.cs:61`), its fired list (`:112`), "Add an action" (`TimerView.cs:141`) and the order's "Ends when" (`OrderSlotsView.cs:126`). The titles are bare numbers too: `ActionView.cs:48` "Action 12", `TimerView.cs:41` "Timer 3", `TriggerAreaView.cs:50` "Trigger area 4", `RouteView.cs:46` "Route 2", `ObjectiveView.cs:57` "Objective 5", and the route pickers `OrderSlotsView.cs:157,164` and `ObjectiveView.cs:156` "Route 4".
- **Why:** A designer choosing what ends an order has to open every action to find the right one. A number is a storage position, and deleting a record renumbers the rest.
- **Proposal:** Name each action in its pickers and title by what triggers it and what it does, built the same way the outliner row is (`MissionOutliner.cs:318`). For example: "Player enters Area 3: brings in Cybrid hercs (group 5)", or "Timer runs out (30 s after start): ends Group 2's order". Name a route by who walks it and its shape ("Route 2: loop walked by Group 4"). Keep the number in a dimmed suffix so links still read. **Invention:** an editor-only name field for actions, routes, areas and timers, kept in a sidecar file beside the `.MSN`, if the user wants names that survive a save.

### 3. "This state of the mission"

- **Where:** `PropertiesPanel.cs:220,257,278`; `ActionView.cs:545`; `ConditionView.cs:287`; `MissionTextView.cs:72`; `ObjectiveView.cs:223`; and reason strings in Core: `GroupOrders.cs:124`, `ActionWiring.cs:30,51`, `ObjectiveEditing.cs:183`, `ActionEditing.cs:127`, `RouteEditing.cs:145`.
- **Why:** "State of the mission" means the records that the campaign flags and random picks the editor previews leave in. That idea is never introduced, and the designer cannot see which records it leaves out or why.
- **Proposal:** Introduce it once, in the Mission view's campaign section (finding 5), as "the version of the mission previewed". Then say "N more appear only in other versions of this mission (other campaign flags or random picks)". When a reason refuses an edit because a record is left out, say how to show it: "It is not in the version previewed: change the campaign flags in the Mission view to show it."

### 4. Identity strings built from roster slots and enum names

- **Where:** `MissionText.cs:25,30` give "Group 0 (player squad)" and "Group 5 (Mech, Cybrid)". `MissionText.cs:18` gives "SCARAB (mech slot 3)". `MissionText.cs:44-46` gives "Herc slot 4 (not placed)". `ActionView.cs:554-555` and `PanelLinks.cs:69` give "Herc slot 3" and "flyer slot 2 (not placed)". The same strings appear in the outliner (`MissionOutliner.cs:263,272`) and in every link.
- **Why:** "Mech" and "Base" are enum names: the UI says Herc and Structure everywhere else. A roster slot is a storage index. "(not placed)" links to something with no object in the scene and does not say why it has none.
- **Proposal:** Name a group by its team and members: "Cybrid hercs: SCARAB x2, DIABLO (group 5)", "Player's squad". Name an object by its type and group: "SCARAB in Cybrid hercs (group 5)", or "SCARAB 2 of 3" when the group has several of one type. In place of "slot N (not placed)", say what is missing: "a herc that only appears in other versions of this mission" (see finding 3), as plain text rather than a link.

## Mission logic: actions, timers, objectives, orders

### 5. The campaign section reads as generator internals

- **Where:** `CampaignStateView.cs:26` "Campaign state"; `:28` "Loaded as practice or INSTANT ACTION: every flag starts at 0 but flags 3 to 6."; `:29` "Shown as at a career with no history..."; `:46` "Draw again"; `:52` "Draw again loads the mission again with new random draws, flags 4 to 6 among them..."; `:54` "N variant table(s) show a chosen alternative in place of their draw, until Draw again." The "Holds/Fails in the campaign state shown" lines are at `ConditionView.cs:78-80` and `PresentWhenView.cs:112-114`.
- **Why:** The designer needs to know that the editor shows one possible load of the mission, that they can choose which load, and that in play each load picks again. The text talks instead about draws, flags 3 to 6 and tables.
- **Proposal:** Retitle the section "Preview". Lead with: "The editor shows one possible version of this mission. In play, the campaign flags below come from earlier missions, and the random choices are picked again at every load." Rename "Draw again" to "Pick random choices again". List each flag by its label first ("Last mission's outcome (flag 0)"), with "Tested by: ..." links. Hide flags 4 to 6: they are only the random picks, which the button already rerolls. Put the "chosen alternative" count next to the button: "2 random choices are fixed to the option you picked; Pick again releases them." Replace "Holds in the campaign state shown" with "Present in this preview" / "Absent in this preview" (emphasised), each followed by a link to the Mission view's Preview.

### 6. The action view mirrors the record instead of reading as "when this happens, do that"

- **Where:** `ActionView.cs:39-65`. The "Fires when:" part is split into "Type" (`:123`), "Target" (`:164`) and "Trigger areas" (`:201`). "Then:" (`:57`) lists the waiting groups, the orders it ends and the timers it starts as read-only links (`:313-356`), because those are set on the group, order and timer views. Below that come "Verb" (`:293`), "Counter writes" (`:361`) and "Message" (`:435`).
- **Why:** A designer authors a trigger as one sentence: "when the player enters this area, bring in group 5 by drop pod, start a 30 s timer, and send a message". Here the "who" is split into a type number and a target. Half of the "then" can only be changed from other records' views, and the field that sets how groups arrive is called "Verb".
- **Proposal:** Make one Trigger view. **Who:** a single picker: "The player / Anyone in the player's squad / Any Cybrid unit / ... / A particular herc..." and, for the last four, the object or group picked in the same control. That merges Type and Target and drops the "7:" codes. **Enters:** the areas. Show the other sources, a timer running out or an object engaged or defeated, as "Also fired by" links. **Then:** an editable list with an add menu: "Bring in a group" (writes that group's deployment action), "Move a group on from its order" (writes the order's action ref), "Start a timer", "Set a campaign flag", "Send a message". Put "Arrive by: drop pod (wide) / drop pod (narrow) / on foot from behind / on foot ahead / in place" inside the "Bring in" entry, with the note "All groups this trigger brings in arrive the same way." That sentence is true because the verb belongs to the action. The reverse fields on the group and order views stay as they are. This is the action equivalent of the Random choice merge.

### 7. Codes shown in front of choice labels

- **Where:** `ActionView.cs:148` "7: its target herc"; `ActionView.cs:310` "2: on foot, from behind the player" and "other (9): in place"; `ObjectiveView.cs:149` "3: is written off".
- **Why:** These are the stored codes. They mean nothing to the designer and push the real label off the 340-pixel panel.
- **Proposal:** Drop the "N:" prefix. Show a value with no case only as "Unknown value (no effect)".

### 8. Arrival mode shown even when no group waits

- **Where:** `ActionView.cs:293-306`, "Verb" with the note "How a group waiting on it arrives...".
- **Why:** Most actions bring in no group, so the field has no effect in play, and nothing says so.
- **Proposal:** Show the field only inside the "Bring in" entry (finding 6). Until that exists, disable it with the note "No group waits on this trigger, so this has no effect."

### 9. Message picker by line number and resource file

- **Where:** `ActionView.cs:437` combo preview "Line 12"; `:445` "Line 12: text..."; `:461-462` "From str\COMMAND0.STR, as every mission but training missions 1 to 4. The pilot and squad channel shows it, signed HQ."; `:465` "It is posted once for each counter write, and not at all when the action writes none."; `:110` "never posted: the action writes no counter".
- **Why:** The designer picks a message by what it says. The file name is storage. The posting rule is a real retail effect that the designer must see, but it is written in counter terms.
- **Proposal:** Show the message text in the preview, cut to fit. Keep "Line 12" and the file name for an Advanced line. Reword the rule after finding 1: "HQ sends this message once for each campaign flag the trigger sets, so with no flag set it is never sent." When the message is never sent, keep that line as the emphasised note.

### 10. "Text line N ... Unused"

- **Where:** `ActionView.cs:492-493`, "Text line 3: ..." / "Unused: the simulator reads it and drops it."
- **Why:** It is set in 1 of the 338 retail records, has no effect, and the designer cannot change it.
- **Proposal:** Remove it, or move it to Advanced as "Leftover text (no effect in play): ...".

### 11. Timer view: empty slots and bare action lists

- **Where:** `TimerView.cs:41` "Timer 3"; `:81` "Delay (s)"; `:103` "When it runs out, fires:"; `:116` "(empty)" for a gap slot; `:139-141` "Add an action" listing "Action N". In the outliner, `MissionOutliner.cs:131` "Timer 2: 30 s after action 4 fires, fires 5, 6".
- **Why:** "(empty)" shows a gap in the record's slot array, which a designer never made. "fires 5, 6" is a list of bare numbers.
- **Proposal:** Present a timer as a delay step in a trigger's Then list ("Wait 30 s, then fire: ..."), and also as its own view for timers that run from the start. Write the title as "Countdown: 30 s after <trigger name>". Leave gap slots out, or merge them on the next edit. Name actions as in finding 2.

### 12. Objectives listed in record shorthand, apart from the lines the player reads

- **Where:** `MissionText.cs:153-176` builds "Objective 3, mandatory: mech slot 2 lost", "failure: group 4 order on route 2 done" and "base slot 7 clear of threats", which the outliner (`MissionOutliner.cs:171`) and every objective link use. The player-facing lines are listed apart at `MissionOutliner.cs:159` "Objective lines (F11), edited in the Mission view:" and edited in `MissionTextView.cs:30`.
- **Why:** "mech slot", "base slot" and "failure" are record terms. A designer thinks of an objective as one thing: the line the player reads plus the test that decides it. In retail the two really are unrelated, and the UI never says so in words a designer would follow.
- **Proposal:** Write each objective as a sentence: "Must happen: Cybrid hercs (group 4) are written off", "Must not happen: the Supply depot is destroyed". Head the outliner section "Win and lose conditions". Under it, a separate "Objective list the player reads (F11)" with the note: "These lines are only text: the game does not tick them off. Win and lose conditions decide the mission."

### 13. "Failure text" on a mandatory objective

- **Where:** `ObjectiveView.cs:190` "Failure text:", `:208` "Add a failure text", `:192` "The status alert shows it while this is the first mandatory objective not yet true...".
- **Why:** The note says it is shown while the objective is still outstanding, so it is a progress hint, not a failure message. On a "must not happen" objective the note says it is never shown, yet the field stays editable.
- **Proposal:** Rename it "Status alert text" ("Shown in the status alert while this is the next goal not yet met"). On a "must not happen" objective, disable it with the note "Never shown for a lose condition."

### 14. Objective role labels

- **Where:** `ObjectiveView.cs:62,67` "Mandatory" / "Failure condition".
- **Why:** These are close to the record's terms. "Failure condition" also clashes with the field the view calls "Condition".
- **Proposal:** Use "Must happen (win condition)" / "Must not happen (lose condition)". Rename the "Condition" picker (`:130`) to "What happens" and the "Subject" picker (`:89`) to "Who".

### 15. Order slots

- **Where:** `OrderSlotsView.cs:77` "Order 0"; `:138` "Verb"; `:182` "Subject"; the verb names at `MissionText.cs:91-97`: "search/destroy", "ramming", "sleeping", "travelling", "following". Also `:173` "Never walked: a group walks only its first order's route." while the route picker stays enabled.
- **Why:** The numbering starts at 0, and "Verb" and "Subject" are record field names. A route on a later order has no effect on movement, yet it is offered as an ordinary choice. The only thing that reads it is an objective's "finished its order" test.
- **Proposal:** Write "1st order, 2nd order...", "Does: Search and destroy / Ram / Guard / Patrol / Sleep / Travel / Follow", "Target: ...". On later orders, disable Route unless an objective reads it, with the note "No effect on movement: a group only walks its first order's route. Used by Objective N's 'finished its order' test." when one does.

### 16. Deployment: "Waits on" and the arrival sentence

- **Where:** `PropertiesPanel.cs:416` "Waits on:" with an "Action N" picker; `:427` "Not in the mission until that action fires; then it arrives drop pod, within 90° of the player's heading." `:410,429` "In the mission from the start."
- **Why:** "arrives drop pod" does not read as English. The arrival mode is set on the action and shared by every group waiting on it, and the view does not say so.
- **Proposal:** "Appears: [At the start / When <trigger name> fires]", then "It arrives by drop pod, within 90° of the player's heading (set on that trigger, for every group it brings in)." `MissionText.Arrival` should return phrases that follow "by" or "on": "by drop pod...", "on foot...".

### 17. "Engaged" distance in world units

- **Where:** `PropertiesPanel.cs:450`, "Engaged: an enemy within 50,000 units spots it, or shoots it."
- **Why:** World units mean nothing to a designer. Elsewhere the panel shows metres beside units (`MissionText.Distance`).
- **Proposal:** Use `MissionText.Distance(50000)`, or metres alone.

## Conditions and Present when

### 18. Present when exposes condition records

- **Where:** `PresentWhenView.cs:42-71`. The picker lists "Condition 5: flag 30 > 0" (`:169`). The buttons "New flag test" and "New draw range" (`:59,64`) carry the tooltip "Adds a draw, which changes what every variant pick comes out as." (`:69`). The link "Condition 5" is at `:110`. The descriptions come from `ConditionEditing.Describe` (`HercWorks.Core/Data/File/Msn/ConditionEditing.cs:188-207`): "draw below 100", "draw 0 to 49 of condition 87", "flag 30 > 0 if condition 12".
- **Why:** A designer wants to say "only after mission 3 was won" or "in about half the loads". Instead they get a list of numbered records, and two buttons that each create a record they then have to open somewhere else. The tooltip uses generator terms, and its consequence has no effect in play: every load picks at random either way. Only the preview changes.
- **Proposal:** Build Present when as a sentence: "Present: [Always / Only when / Only in some loads]". "Only when" shows "[campaign flag (with label)] [is / is not / is at least / is less than ...] [value]" and an optional "and also when ...". "Only in some loads" shows "[N]% of loads" and, when other records should appear or disappear together, "together with ..." (a shared random choice, which is the draw-and-ranges model and lines up with the Random choice view). The editor creates and reuses the condition records behind it. The numbered conditions move to an Advanced "Conditions" list (finding 20). Replace the tooltip with "The preview picks again" or drop it.

### 19. Draw and range-over-a-draw views

- **Where:** `ConditionView.cs:134` "Below"; `:138` "It draws a number from 0 to N."; `:140` "Drawn in the campaign state shown: N"; `:145` "Adding or deleting a draw, or changing whether it is made, changes what every later draw and variant pick comes out as."; `:148` "New range over this draw"; `:166-205` "Draw", "From", "To", "It never holds: condition N draws 0 to M.", "A range requires nothing else..."; `:264` the delete note.
- **Why:** This is the same generator vocabulary the user rejected in the variant views. A draw and its ranges are one concept: a random roll whose outcomes switch different records on.
- **Proposal:** Fold them into the Random choice view: "Random choice: Option A 25% (shows Group 5), Option B 75% (shows Area 3 and Action 4), nothing 0%". It differs from a variant table only in that its options switch records on and off rather than swapping a herc's type. Drop the "every later draw" notes: they describe the preview, not play.

### 20. The Conditions list as a top-level section

- **Where:** `MissionOutliner.cs:180-211`, "Conditions (N)" with "New flag test" and "New draw" (`:187,192`), and rows "Condition 87: draw below 100".
- **Why:** It lists storage records as if they were mission content. "New flag test" here creates a condition that nothing uses.
- **Proposal:** Once Present when builds its own conditions (finding 18), move this list under an "Advanced" header, collapsed and without the create buttons. Until then, retitle it "Present-when rules" and name each row by its sentence and its users: "Only when flag 30 is at least 1: Group 5, Area 3".

### 21. Flag test, operator and "Requires"

- **Where:** `ConditionView.cs:102` "Flag" (number field); `:113-115` operator picker "flag = value", "flag != value", "flag < value"; `:129` "Holds while flag 30 > 0."; `:210` "Requires" with "Condition N: ..." entries; `:231` "It holds only while that condition holds too."; `:234` "Only a condition before it can be required."; and the Core reason at `ConditionEditing.cs:255` "...the conditions are read in order, so one can require only a condition before it."
- **Why:** The operators are symbols built from field names. "Before it" means file order, which the designer never sees.
- **Proposal:** Write operators as words ("is", "is not", "is less than", "is at least"...). Rename "Requires" to "And also when". If file order has to stay a constraint, the editor should move the new condition after the one it requires instead of refusing (**Invention**: a reorder). Failing that, say "Only rules listed above it in the Conditions list can be chosen."

### 22. Present when with different records

- **Where:** `PresentWhenView.cs:45` "(differs by record)"; `:122` "Record 1: always"; `:125` "Its records carry different conditions, so it is not set here: setting one alone could leave another making a half-filled copy of it where the first is absent."; `:137-139` "Some of these fields come from a record present only when ..."
- **Why:** "Records" and "a half-filled copy" describe how the `.MSN` builds one object out of several rows. The designer sees one object.
- **Proposal:** "This herc's details change between versions of the mission, so its presence can't be set as one rule." Behind Advanced, add "Its type comes from one rule and its weapons from another." Name the fields affected by what they are (type, weapons, position) instead of "some of these fields".

### 23. ConditionView's "Used by" list names things the editor has no view for

- **Where:** `ConditionEditor.cs:135-187` builds "Header patch N", "Movie value N", "Text package N", "Debrief N", "Point N" and "Heading N" as plain text with no link.
- **Why:** These are row names. The designer cannot select or act on any of them, yet they block a delete.
- **Proposal:** Name them by effect: "the mission's start settings", "a briefing movie", "the mission text", "the debrief", "a position", "a heading". Add "(not shown in the editor)" so a refused delete is explained.

## Roster and squad

### 24. "Only this herc" / "Changes the alternative"

- **Where:** `RosterFitView.cs:106` "Only this herc" (or flyer and so on); `:112-113` "Changes this herc alone." / "Changes the alternative: N records draw it."; `:66` "A field set for this herc alone stays its own."
- **Why:** This puts the variant model into the object view: "the alternative", "records draw it". A designer clicked one herc and needs to know whether the edit reaches other hercs.
- **Proposal:** Say: "This herc is one option of a random choice (link, named as in the Random choice view). Apply changes to: (•) that option, wherever it can appear (3 hercs) ( ) only this herc." Replace "A field set for this herc alone stays its own." with "Weapons you set for this herc alone are kept." Wording should match the Random choice view once that lands.

### 25. Debugging data in the object view

- **Where:** `PropertiesPanel.cs:138` "Kind: Mech"; `:140` "Type index: N"; `:143` "Roster slot: N"; `:149-153` "Model: <key>", "Triangles: N", "Texture: N frames (WxH)"; `:159` "Hit radius: N units".
- **Why:** None of these is something a designer authors. "Kind: Mech" repeats the title in the enum's name. The triangle and texture counts are renderer data.
- **Proposal:** Remove "Kind" and "Roster slot". Move type index, model, triangles, texture and hit radius under a collapsed "Advanced" header.

### 26. Group view raw fields

- **Where:** `PropertiesPanel.cs:511` "Formation: 3 (mech table)"; `:512` "Paints ground: yes/no"; `:521-523` "Pad: tile x to y, ... material N. Only which tile the authored point falls in matters; a move within the tile changes nothing."
- **Why:** These are a formation id and table name and a flag name. "Authored point" is editor jargon.
- **Proposal:** Name the formation if the formation table's shapes are known; otherwise move it to Advanced. Rename "Paints ground" to something like "Lays a base pad". The pad line becomes: "Base pad: 40 m square. It snaps to the terrain tile under the group's position, so small moves don't shift it."

### 27. Group start point

- **Where:** `PropertiesPanel.cs:478-491`, "Point, in world units:" with the note "from its own point" / "from its route's first waypoint" / "from no point and no route: the origin"; `:493` "It stands at x, y: painting its ground moves it...".
- **Why:** "From its own point" says nothing, and "the origin" is a coordinate term.
- **Proposal:** "Starts at: its own position / the first waypoint of its route / the map's corner (0, 0): give it a position or a route."

### 28. Shared-point notes

- **Where:** `PropertiesPanel.cs:542-546` "Its point is referenced by N other record(s) (trigger areas, groups, waypoints), which move with it. Alt+drag moves the group alone."; the same at `TriggerAreaView.cs:133-134` and `RouteView.cs:158-161`.
- **Why:** Sharing a point record is a storage detail. A count of "records" does not tell the designer what moves.
- **Proposal:** List what is there as links: "Also on this spot, moving with it: Area 3 (corner), Route 2 waypoint 1. Alt+drag to move it alone."

### 29. The squad panel in group-0 terms

- **Where:** `SquadMachinePanel.cs:110` "Group 0 references no herc record."; `:133` "...the Hercs below, group 0's members after the player's machine."; `:159` "Group 0's member slot N references no herc record."; `:85` "Group 0 lists no squadmate for this Herc."; `:155` "Squadmate {member}", numbered by member slot; `:101` "Player's machine".
- **Why:** The designer knows "the player's squad", not "group 0" or "member slot".
- **Proposal:** "The player's squad has no Herc for the player." / "Squadmates pilot the Hercs listed below." / "Squadmate 1, 2, 3", by list position. Use "Player's Herc" for "Player's machine".

### 30. Squad loadouts that a campaign does not use stay editable

- **Where:** `SquadMachinePanel.cs:125`, "In campaign missions, the default HERCs and loadouts selected here are not used." The chassis and weapon pickers below it stay live.
- **Why:** The note is right, but every control under it still invites an edit that has no effect in play.
- **Proposal:** When the mission loads with the career squad, put the pickers under a collapsed header: "Practice-mode squad (not used in the campaign)".

### 31. Weapon mounts called "Slot"

- **Where:** `MachineFitView.cs:73,115` "Slot 0:"; `:125` "(no slot)" for an empty mount; `:40` "Chassis".
- **Why:** The numbering starts at 0, and "(no slot)" reads as a missing mount when it means an empty one. The game's armory screen has its own words for these.
- **Proposal:** Use "Mount 1..N", or whatever the shell's Weapons screen calls them, and "(empty)".

## Places, routes, the map

### 32. Waypoint list as raw coordinates, numbered from 0

- **Where:** `RouteView.cs:75` "0: 12345, 67890"; `:117,122` "Insert after 0", "Remove 0"; `:144` "Waypoint 0, in world units:"; `:81` "The heads-down map marks waypoints 1 to 9 of the player's route...".
- **Why:** The rows give no sense of place. On the player's route, the waypoint marked on the heads-down map does not carry the number the editor shows.
- **Proposal:** Number from 1, and call waypoint 1 "Start" on any route. On the player's route, label the later waypoints as the nav points the map marks, after checking against `docs/retail/simulation/heads-down-display.md#markers`. Show the distance from the previous waypoint ("Nav 2: 420 m from Nav 1") in place of raw X and Y.

### 33. A route's waypoint list shared with a random choice

- **Where:** `RouteView.cs:98`, "Its waypoint list is a variant's, shared by every route that can draw it: an edit here changes them all."
- **Why:** This is variant jargon in the route view.
- **Proposal:** "This route is one option of a random choice (link). Editing it changes it wherever that option is picked." Wording should follow the Random choice view.

### 34. "Mission box" warning

- **Where:** `PointFields.cs:67-68`, "Outside the mission box as opened: the box, the heads-down map's frame and the abort line grow to take it in."
- **Why:** "Mission box" and "abort line" are coined terms that are never introduced.
- **Proposal:** "Outside the mission's play area: the play area, the map and the edge where the player is told to turn back will grow to include it." Use the in-game wording for the abort warning if the shell docs give it.

## Panels, lists and messages

### 35. Problems list in `script.dat` terms

- **Where:** `HercWorks.Core/Data/File/Msn/Script/ScriptDatLint.cs:226,228` "<label>: 7 is past the end of the 5 trigger areas." / "...is not a valid index (-1 means unset)."; `:64-213` labels such as "Trigger area 3 point ref", "Action timer 2 sequence refs", "Group 4 member slots", "Order 6 subject ref"; `:82,89,191,219,242` "DBSIM faults loading it", "writes the rest over the globals that follow"; `:209` "Group N roster is 3 — only 0 (hercs), 1 (flyers) and 2 (bases) exist." Shown by `MissionOutliner.cs:96-110`.
- **Why:** Ref names, -1 and "DBSIM" are file and executable terms. Line 209 also has an em dash, which the ImGui font draws as "?" (Latin-1 rule).
- **Proposal:** Keep the lint's own wording for the HercWorks tools. Have the editor phrase each finding kind itself: "Trigger 3 tests an area that no longer exists", "Group 4 crashes the game when the mission loads: give it a first order", "Too many hercs: the game holds 100, and the rest corrupt the mission". Fix the em dash in either case.

### 36. Notes that name a view or list by something other than its label

- **Where:** `MissionOutliner.cs:159` "edited in the Mission view"; `PropertiesPanel.cs:120-121` "...or pick from the Mission list." (the panel's title is "Mission", and its first row is "Mission"); `PropertiesPanel.cs:120` "Click a mech, flyer, building, trigger area or waypoint"; `HercWorks.Core/.../ConditionEditing.cs:222` "A variant table and its ranges are edited in the table's view."; `HercGroups.cs:162` "The player's squad takes squadmates in its own view."
- **Why:** The memory rule is to name a control exactly as it is labelled. "Mech" and "building" also break the UI's Herc and Structure terms.
- **Proposal:** "Edited under Text when you click Mission at the top of the Mission panel." / "...or pick one in the Mission panel on the left." / "Click a herc, flyer, structure, ...". Make "Squadmates are added in the Player's squad view (click the squad in the Mission panel)" a link.

### 37. Outliner rows

- **Where:** `MissionOutliner.cs:263` "Group 3 (Mech, Cybrid): 4, waits on action 2"; `:293` "Area 4: box, action 3, 5"; `:315-318` "Action 3: 2 area(s), brings in 1 group(s)" and "no area" for one fired by a timer; `:340` "Route 2: loop, groups 1, 4".
- **Why:** ": 4" is an unlabelled count. "no area" makes a timer- or object-fired action look broken. "action 3, 5" is bare numbers.
- **Proposal:** Use the names from findings 2 and 4: "Cybrid hercs (4), appear when <trigger>", "Area 4 (box): starts <trigger names>", "When Timer 1 runs out: brings in 1 group".

### 38. Copy-on-write notes

- **Where:** `OrderSlotsView.cs:101` "Another group has this order too: a change here gives this group its own copy."; `MissionTextView.cs:97` "Another record uses this line too: a change here gives this slot its own copy."; `ObjectiveView.cs:218` "Another record uses this text too...".
- **Why:** The editor handles the sharing on its own. The note describes storage, and "record" and "slot" are storage words.
- **Proposal:** Drop these notes. If the user wants to keep one, name the sharer as a link and say what an edit does: "Shared with Group 3: editing here changes only this group."

### 39. Mission text counts and encoding

- **Where:** `MissionTextView.cs:28` "N of the 50 lines the simulator reads."; `:46` "Letters past code 127 show as spaces on version 1.0's mission screen."
- **Why:** The 50-line budget is shared with failure texts and other text, which the note does not say. "Code 127" is an encoding term.
- **Proposal:** "Text used: N of 50 lines (briefing, objective lines, intelligence report and status alerts together)." / "Accented letters show as spaces in the original 1.0 release."

### 40. Zones offered as "ZONE12"

- **Where:** `MissionZones.cs:13` gives the label `ZONE{n}`, used by `NewMissionDialog.cs:142` and `MissionView.cs:34` "Zone: ZONE12".
- **Why:** The designer has no way to tell what terrain a zone holds before creating a mission on it.
- **Proposal:** List each zone with the retail missions that use it ("Zone 12: used by missions 3, 17, 40") from `HercWorks.Query`, and its size. **Invention:** a small height-map thumbnail beside the list.

### 41. Delete blockers

- **Where:** `PropertiesPanel.cs:265-273` "Group 5: order 0, order 2, own route"; `ActionView.cs:540` "Group 3, order 1".
- **Why:** "own route" is the group record's route field, which no view shows or edits (`handoff-editor.md`, row 16). The blocker names something the designer can never clear.
- **Proposal:** Number orders from 1. Write "own route" as "its starting route (not editable yet)" until the group's route field gets a control. That gap is already in the handoff's "Edit roster and group fields" slice.

### 42. Toolbox join checkbox

- **Where:** `EditorToolbox.cs:166`, "Join the selected group 5".
- **Why:** It uses the group's number, as finding 4 describes.
- **Proposal:** "Join Cybrid hercs (group 5)".

## Open

- **Open:** findings 6, 18 and 19 regroup records into one view and should be designed alongside the Random choice view, so that the three share a vocabulary ("option", "random choice", "version of the mission").
- **Open:** findings 26 and 32 need retail facts before their wording is final: formation names, what "paints ground" does in play, and which waypoint the heads-down map numbers 1.
- **Open:** finding 2's editor-only names would be this engine's invention. Whether to add them, and where to store them, is the user's call.
