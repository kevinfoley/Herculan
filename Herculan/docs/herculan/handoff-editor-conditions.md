# Handoff — preview, conditions, variants and Present when in the editor

How the editor previews one version of a mission, and edits row 1's flag tests and draws, variant tables, and which versions a record is present in. Part of the [editor handoff](handoff-editor.md). How conditions and draws resolve, and what moves the draws, is [Conditions and variants](handoff-editor-document.md#conditions-and-variants); which records each tool writes is [The tools](handoff-editor-document.md#the-tools).

## The preview

The editor shows one version of a mission, the preview: the campaign flags the load is handed and the load's random draws decide which records are generated. An open (File > Load from VOL, File > New mission) draws through `MissionOpener` from a generator whose every step is 0 (`SimRandom.Lowest`, the editor's choice), so a mission opens the same way every time: every row-1 draw at 0, so each range over a draw holding 0 holds; every variant table on the option a draw of 0 picks, the first in row-1 order for each of the 252 retail tables under a key; flags 4 to 6 at 0; and a training load's new career and squad build drawn from the same zeros. The game's own launches draw from `ShellTrainingLaunch.StartupRandom` as before.

The Mission view, after the theater and time of day, shows it under **Preview** (`MissionPreview`, drawn by `MissionPreviewView`):

- a lead, "The editor shows one version of this mission. In play, campaign variables come from earlier missions and random choices are picked at every load.", then how the mission was loaded: in training mode (practice, `INSTANT ACTION`, a new mission, one `gam\career.dat` does not list) every flag starts at 0; at a campaign position it is shown as at a career with no history, every flag an earlier mission writes reading 0 until set here;
- every campaign flag a type-0 row-1 record tests, whatever that record's own condition ([`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md#the-conditions--row-1)), in index order, but 4 to 6. Each shows its label first where the retail docs say what it holds ("Last mission's outcome (variable 0)"; 1 and 2 the career position's stage and mission, [`../retail/shell/campaign-loop.md`](../retail/shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store); 8 the Hercs scrapped, 9 the pilots lost and 10 the squadmates the player put out of the fight, [`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md#the-debriefs-loss-line)), else "Campaign variable N"; a typed field holding the value the generation hands the load; and "Tested by:" with a link to each condition testing it. A mission testing no flag shows a note instead. Flags 4 to 6 are drawn below 12 at every load, practice and campaign alike, so they are not listed, and a mission testing one gets a note saying so; no retail mission tests them (`HercWorks.Query flag 4`, 5 and 6);
- what the random draws are: each on its first option, or as one simulated load picked them;
- **Simulate a mission load** and **Back to first options**, and beside them how many variant tables show an option picked in their own view.

Four things change the preview:

- **A flag field**, applied when it lets go.
- **Simulate a mission load**, which opens the mission again as its open loaded it, in the same mode, from the `.MSN` and `.ENG` in memory, into a scratch folder inside the handoff's (so a failed reopen leaves the handoff as it was), drawing from a new `ShellTrainingLaunch.StartupRandom` (`MissionOpener.OpenAgain`, through `MissionFileLaunch.OpenAt`). Every draw the launch makes is made again in its own order: a training load's new career, flags 4 to 6, row 1's draws, the variant picks and the squad build (`MissionSquad`). Every flag keeps its value but 4 to 6.
- **Back to first options**, the same reopen drawing from `SimRandom.Lowest` as the open did; disabled while the draws are an open's and no table shows a picked option.
- **Shown in the editor**, in a variant table's view ([Variants](#variants)): a pick forced in the generation (`MissionGeneration.WithPick`), which still makes the table's draw, so every later draw is unchanged. It survives a flag change and every edit, is ignored while its table or range is gone, forgotten when its table is deleted, and cleared by both reopens.

Each replaces the document's generation (`MissionDocument.SetGeneration`), writes the handoff's `mission.var` as the launch writes it and the flags as the load leaves them, and rebuilds ([Rebuilding after an edit](handoff-editor-document.md#rebuilding-after-an-edit)), so records conditioned in gain objects and those conditioned out lose them, the selection kept. When the generated header then picks another world, the scene is dressed in it as after a theater change (`TheaterChanger.Follow`). None is a step of the undo history: the title gains no `*` and Save writes what it wrote before. Opening another mission starts from that mission's own open.

A condition's view and a Present when's chosen condition say "In this preview" or, emphasised, "Not in this preview", followed by a "Change the preview" link to the Mission view (`PanelLinks.InPreview`). A record, text line or reference the preview leaves out is "not in this preview".

## Conditions

The outliner's last list, Conditions, shows every row-1 record in file order as "Condition <GUID>: <test>" (`ConditionEditing.Describe`: "flag 100 > 0", "draw below 11", "draw 0 to 9 of condition 66", "variant table 170 below 100", with "if condition 65" after one that requires another), each operator worded as the load tests flag against value, flag on the left. A condition that fails in the preview is dimmed; `MissionGenerator.Conditions` reports this per record, with the number each draw drew. **New flag test** and **New draw**, at the top of the list, append one and select it; a draw's view adds a range over it.

The view (`ConditionView`, through `ConditionEditor`) opens with "Condition <GUID>" and its kind, then:

- a flag test's flag (0 to 999, labelled as the preview labels it), operator and value;
- a draw's bound and the number it drew in the preview, or that it is not drawn because what it requires fails; adding or deleting a draw, or changing whether one is made, changes every later draw and pick, and a draw's view says so;
- a range's draw (a choice of the draws before it, with a link) and its two ends, with a note when the ends lie past the draw's bound;
- Requires: none or any condition before it, with a link (a range's draw is its requirement, and it has no other). Row 1 is read in order, each record against the survivors before it, so a later one is refused with the reason and not offered;
- whether it is in the preview;
- Used by: every record whose condition references the GUID, in rows 1 to 17 (row 5's 64-byte records by their first word) and the `.ENG`, one short line each, a link where a view exists ("Condition 7", "Group 12", "Herc slot 4", "Group 4, order 1") and plain text otherwise ("Header patch 1", "Debrief 0", "Text line 204"), with the records the preview leaves out counted by kind and the variant alternatives it conditions counted apart, since no version places an alternative;
- Delete, refused while Used by lists anything.

No retail mission carries a row-1 GUID twice, and the view edits no condition whose GUID is. A variant table's view is the table's. A range over one is shown as the alternative it chooses (`VariantView.DrawRange`): "Alternative 2 of Condition 0: 50%, <what it is>", with a link to the table, where its chance is set, then the records that drew it in the preview as links; a range choosing no alternative the table reads is shown as any other range, read-only. The preview's flag list follows the flag tests.

## Variants

A variant table's condition view is the table's (`VariantView`, through `VariantEditor`):

- its key, the bound it draws below, and what it requires, with a link;
- each alternative on a line: its chance, a whole percent in a typed field; what it is, as its kind's view titles it (a herc's chassis, a flyer's type, a structure's or ground vehicle's type and name); Remove;
- what is left over;
- Add alternative;
- Shown in the editor: As drawn or one of the alternatives ([The preview](#the-preview));
- Drawn by: each record whose variant key is the table's, one identity link per line with the alternative it drew in the preview ("Herc slot 4: alternative 2"), the records the preview leaves out counted;
- any other table under the same key, as links;
- Delete.

**Chances.** An alternative's share is the draws its range holds that no range before it holds, out of the bound. A retail table opens with its shares rounded to whole percents adding up to the rounded total, largest fractions first, with a note giving them to one decimal where they are not whole; the first chance edit writes them as shown, with the bound 100. A draw below any bound of 1 or more is one step of VSHELL's generator (`ShellRandom_Below`, [`../retail/shell/campaign-loop.md`](../retail/shell/campaign-loop.md#the-shells-generator)), so writing the bound moves no later draw, and no retail table draws below less than 1. The chances add up to 100% at most. A draw in what is left over takes no alternative, so a record drawing it keeps its own fields, and the point, heading and actions those reference are not looked up, since a keyed record's refs are resolved only in what it copies (`MissionGenerator.ReadHercs` and its siblings), as the view says.

**Add alternative** copies the alternative the editor shows (the one chosen to show, else the one the first drawing record drew, else the first) and gives it half the largest chance, rounded down, taken from that alternative, the first of a tie (the editor's choice); it is refused when no chance is 2% or more. **Remove** gives the alternative's chance to the one before it, or after it for the first (the editor's choice), and is refused for the last. **Delete** is refused while any record, in any row and whatever its condition, has the table's key, or anything else references the table.

**Which tables are edited.** A table is edited when each of its ranges conditions one GUID `-1` record of row 12, 13 or 14 that draws nothing itself, all of one row, no other table carries its key, and the squad draws nothing from it. Any other is shown with the reason and no control: route tables, tables under a shared key (`C1_05`'s conditions 36 and 40 share key 172, the second requiring a flag test and reached only by draws the first leaves uncovered), and the training missions' squad tables. Of the 783 retail ranges over a table, 694 choose a herc, 30 a flyer, 12 a structure and 47 a route (`HercWorks.Query conditions`).

**Drawn from.** The view of a herc, flyer, structure or ground vehicle outside the squad shows, after its Present when, Drawn from: None, any edited table whose alternatives are of its row, or New table from this herc (flyer, structure, ground vehicle), with a link to the table and the alternative it draws in the preview, or that it draws none and keeps its own fields. A choice is refused for a GUID two of whose records carry a key. New table's single alternative is a copy of what the record generates from, so it looks the same. None leaves the record as shown, keeping as its own the span it copied (the editor's choice). Each of the three changes whether the record draws, so every later draw changes, as the view says.

**Editing a drawn record.** A record drawing from a table takes its position and its engaged and defeated actions from the alternative, so a move or an action set writes an overlay of its own. Its type, and a herc's weapons and guidance, are the alternative's: where a field comes from an edited table's alternative (`VariantEditing.WhyNoDrawnField`), the view says "Changes the alternative: <N> records draw it", counting the records that draw it in the preview (`VariantEditing.DrawingNow`), and an edit writes the alternative itself, so each of them changes. "Only this herc" (flyer, structure, ground vehicle), unticked whenever another record's view is shown and not saved, writes the record's overlay instead, by the squad's rules for a value an overlay cannot carry ([The player's squad](handoff-editor-roster.md#the-players-squad)). `RosterFit` (Core, `Data/File/Msn/RosterFit.cs`) routes each field on its own, so a field the record carries itself is written there; a herc view whose fields are partly drawn and partly its own says so.

Gaps:

- Only a type and a herc's weapons and guidance are written in the alternative; its other fields are not edited.
- Shown in the editor is offered for an edited table only.
- A table's requirement is shown and not set; a new table requires nothing.
- A route keeps the waypoint list it draws, edited as the alternative every route drawing it shares; route and squad tables are read-only.

## Present when

The view of a herc, flyer, structure or ground vehicle, a group and an objective shows, after its name and group, which versions of the mission the record is present in, and each order slot after its other fields (`PresentWhenView`, through `PresenceEditor`): Always, or one of the mission's flag tests and ranges over a draw, named as the Conditions list names them ("Condition 5: flag 30 > 0"), with a link and whether it is in the preview. A draw, which holds whenever it is made, and a variant table and a range over one, which the keys draw by, are not offered. **New flag test** and **New draw range** make a condition with the Conditions list's defaults and choose it, the draw going ahead of every variant pick, as the tooltip says. When the record is then not in the preview it leaves the scene and the outliner, and the condition it carries is selected in its place, its Used by counting it among the records left out.

A GUID can have several records, a base and the overlays or replacements that apply in other versions ([`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md#repeated-guids)): when they carry the same condition, a set writes every one; when they differ the choice is disabled, each record's condition is listed on its own line ("Record 2: condition 5 (flag 30 > 0)"), and a note says why: writing one alone could leave another making a half-filled copy where the first is absent. A variant alternative (GUID `-1`) is never written: a keyed record's condition is its own word, outside the span it copies ([`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md#variants)), and a record that fails its condition draws no variant, so the view notes that every later pick changes where it is absent.

An order another slot runs is first given its own copy. Where an order is absent its slot holds none and later orders do not move up, so a first order is refused where its group cannot be left without one (`GroupOrders.WhyNeedsFirstOrder`, the rule that refuses removing a last order), and a later one notes that the group stops at the order before it.

While a record carries a condition, the view lists what loses its reference to it where it is absent, one identity link per line, from the delete checks' lists: an object's objectives, actions and orders (`RosterDeletion.Check`) and the groups whose member slots reference it; a group's objectives, actions and orders (`RosterDeletion.GroupNamers`); the group slots running an order. When a record of the GUID other than the one that creates the generated record supplies some fields and carries its own condition (`SlotProvenance.Suppliers`), a note says an edit to those fields holds only while that condition does.

No Present when:

- the player's squad, its orders and the hercs its member slots reference, since the squad build reads group 0's members (the editor's choice);
- a route, as the user chose: every conditioned retail route record is a variant alternative, GUID `-1` (`HercWorks.Query values --row 8 --field GUID --where ConditionRef!=-1`), so every route is present in every version;
- a trigger area, an action and a timer: no retail record of rows 9 to 11 carries a condition (`HercWorks.Query values --row 9 --field ConditionRef`, and rows 10 and 11), and what VSHELL does with one there is not checked.
