# Mission editor design

Why the mission editor (`Herculan.Engine.Editor`, with its `.MSN` editing in `HercWorks.Core`'s `Data/File/Msn/`) works the way it does: the retail facts and the decisions each part of it rests on. What it does for a mission designer is the [manual](editor-manual.md); how a class does its part is that class's doc comment. "The editor's choice" marks behavior this engine chose where retail gives no answer, and "the user's choice" one the user decided. Retail counts are over the 62 `.MSN` files with campaign conditions not applied, from `HercWorks.Query`.

## The `.MSN` is the document

The editor edits a mission as its `.MSN` and `.ENG`, the files the shell reads, never as a `script.dat`. VSHELL writes `data\script.dat` afresh from the `.MSN` each time it loads a mission, dropping every record whose condition fails and renumbering what is left ([How missions are loaded](editor-manual.md#how-missions-are-loaded)), so a view built on a `script.dat` could not show a record one load drops, and would hold block indices an edit renumbers. For the same reason the editor never opens a `script.dat` such as a save slot's: there is no `.MSN` behind it to edit (the user's choice).

A selection and an undo step name `.MSN` records, by row and GUID, an objective by its index in row 17 and a line of text by its `.ENG` id, so neither needs remapping after an edit.

The editor generates a `script.dat` from the `.MSN` twice: when it opens a mission, to load the world (zone, theater, time of day, models, terrain) through the game's own load, and for File > Export. An exported `script.dat` does not change a campaign mission, which the shell builds from the `.MSN` again; it reaches only what reads the file as it is, DBSIM run directly over `data\` and a save slot ([`../retail/formats/save-games.md`](../retail/formats/save-games.md#the-slot-handoff)).

Every list in a record that the editor edits stays filled from its first slot, with no gaps: a group moves on only to a next order slot that holds an order ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#the-tick--group_ordertick-00423a74)), the load stops reading a trigger's areas at its first empty slot, and the mission's text lists are read the same way. A remove therefore moves every later entry up.

### The layout

Everything the editor draws, lists, selects and checks reads one view of the document, `MissionLayout`, with every record of the `.MSN` in it, built again after every edit, undo and redo. It tests no condition, draws nothing and renumbers nothing.

- **One entry per thing a designer sees**, keyed as the `.MSN` keys it, and references between entries stay GUIDs.
- **The base.** A thing is shown as its base: the records of its key that carry the same condition as its first record, merged in file order by the game's own overlay-or-replace rule for that row. A record of the key under any other condition is a variation, which the scene and the views do not show (the user's choice); the views say where one changes a field ([Present when](#present-when)).
- **The first option.** A record that picks from a random table is shown with the table's first option in row-1 order, on every open (the user's choice).
- **Each field's supplier**: the record whose value the layout shows, which is the record an edit writes ([Which record an edit writes](#which-record-an-edit-writes)).

The merge spans, unset values and template copies are one table, `MergeRules`, which `MissionGenerator` merges by too, so the editor's picture of a record and the game's load cannot disagree on how its records merge.

The editor shows every record of a mission, the same on every open, and offers nothing to choose which campaign variables or options it shows (the user's choice). A unit some loads leave out still stands in the scene, marked as not always present, and its view opens with its Present when, so one taken out can be found and brought back.

### Which record an edit writes

An edit writes the field's supplier, whether or not a given load keeps that record. Where that would not give the result the designer sees, the edit writes elsewhere:

- **A field a random table's template supplies** goes into a record of the user's own GUID under the base's condition, which overlays the field whatever the roll picks. Not into the template (GUID `-1`), which every user that can pick it shares, and not into an unconditioned record, which in a load where every other record of the GUID fails would create the unit alone with every other field unset. The Loadout section is the exception: it edits an option on purpose, so it writes the option's template and changes every user that picks it ([Random tables](#random-tables)).
- **A route's waypoint list a template supplies** is written in the template, changing every route that picks it: row 8 replaces rather than overlays, so an appended record cannot carry one waypoint, and giving the route its own list would take a roll away ([What moves the random draws](#what-moves-the-random-draws)).
- **An unset value** (a member slot emptied, a trigger taken away) is written into every record of the GUID that sets the field, whatever its condition, since an overlay cannot carry an unset value. For the same reason a field a template supplies cannot be unset, and its choice offers no "None". A unit's position, a heading and a starting condition are unset in the base's records only, so a variation keeps the value it states.
- **An order another group also runs**, and **an `.ENG` id another field also references**, are copied under a new GUID or id first, so the edit changes only the field edited. A new `.ENG` id is one past the largest any record has or any field references, since VSHELL matches text ids by equality alone.
- **A record with no GUID** (row 4's text package, row 17's objectives) is named by nothing, so the record itself is written, whatever its condition.

Where a variation sets a field, an edit of that field does not show in the versions of the mission the variation applies to; the views of units, groups and orders say so.

Every record a tool appends is unconditioned, carries no random table key and takes its row's largest GUID plus one, so an add never moves a draw.

### What moves the random draws

VSHELL draws a mission's random values in file order: row 1's draws first, then a roll for each record of rows 8, 12, 13 and 14 that picks from a random table, as it is read. So an edit that adds or removes a draw shifts which option every later table picks on a given load, though it changes no odds:

- **Moves them**: deleting a record that picks from a table, or changing which loads it is present in; giving a record a table or taking one away; adding or deleting a row-1 draw, or changing whether one is made.
- **Moves none**: appending records with no table key, which is every add; a table's chances, its bound, adding or removing an option. A roll below any bound of 1 or more is one step of the shell's generator ([`../retail/shell/campaign-loop.md`](../retail/shell/campaign-loop.md#the-shells-generator)).

The views do not mention a shift, since it changes nothing the designer can see or rely on (the editor's choice). The editor's own open draws from a generator whose every step is 0 (`SimRandom.Lowest`, the editor's choice), so every open of a mission resolves it the same way, and no edit moves those draws, since each is 0 whatever comes before it. The document keeps that generation for its life, so nothing reshuffles on an edit. The game's own launches draw at random.

### Saving and playing

Save writes the mission as loose `MSN\<name>.MSN` and `.ENG` files into the install, the text taking the extension the shell's language reads. A loose file is read before any archive ([`../retail/formats/vol-archive.md`](../retail/formats/vol-archive.md#loose-files-on-disk-carry-no-prefix)), so the game plays the saved mission in place of the archive's, and deleting the loose files goes back to the archive's.

Play plays the saved files, so unsaved changes are saved first. It writes the game's mission handoff into a temporary folder rather than the install's `DATA\`, so the install's own handoff is left alone.

A campaign mission is shown on its stage's theater, since the campaign launch writes the theater over the mission's own. Every other mission is shown, and played from the editor, on its own theater and time of day ([`herculan-command-line.md`](herculan-command-line.md#what-runs), `--own-world`): a departure from retail the user chose, since retail's practice and `INSTANT ACTION` loads write theater 1 and the practice screen's time of day over them ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#header-format)).

## Positions

Several things share one position on purpose. In 45 of the 62 retail missions a route's waypoint is the same point as a trigger area's center or corner, which is how a mission activates a trigger when a waypoint is reached: `C1_01`'s player route runs through points 119 and 121 to 124, its drop-pod triggers are circles about 121 to 124, and the groups those triggers bring in stand on 119. So a plain drag moves the point and everything on it, which the views list, and Alt+drag gives the dragged thing a point of its own.

- **A unit** moved gets a point of its own, which retail honors over its group's formation spot ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 2). Ctrl+drag of a structure group also moves its members' own points, the editor's convenience: a plain drag moves only the group's point, and members standing on points of their own stay behind.
- **Waypoints and trigger area corners** are written at height 0, as 1487 of the 1488 retail waypoints are. DBSIM reads a waypoint's height only as a flight's starting altitude and for the heads-down map's route markers ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once)), so only a flyer group's height handle sets it.
- **Removing a waypoint, route or area** removes each point nothing names any more, so the mission zone shrinks back ([The mission zone](editor-manual.md#the-mission-zone)).
- **Mission size.** DBSIM allocates a `script.dat`'s point, route and area blocks from their counts, so a mission longer than any retail one loads. Its fixed arrays are the per-roster slot flags and maps, past which it writes over neighboring globals ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps)); those are the roster limits ([Group sizes and roster limits](#group-sizes-and-roster-limits)).

### Waiting groups

A group arriving by drop pod or on foot is not in the scene (the user's choice): retail never uses its position, bringing it in near the player when its trigger activates ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4)), so a marker where it was placed would mislead. A structure group stays in the scene whatever its arrival, since DBSIM builds a held-back structure where it is placed, solid from the first frame ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#held-back-structures-stand-from-the-start)).

## Units and groups

### New units

A new unit's records take the shape most retail records of their kind have, and the builders behind each Toolbox tab (`HercGroups`, `FlyerGroups`, `VehicleGroups`, `NewGroups`, `StructurePlacer`) cite the count behind each field. Where retail has no majority, the editor chooses:

- **A new HERC group's first order** depends on its team (the editor's choice). A Cybrid group hunts the player's squad. A Human group patrols with no target, since "Search and destroy" attacks only its target ([`../retail/simulation/ai-navigation.md`](../retail/simulation/ai-navigation.md#searchdestroy-12--mech_behavioursearchdestroythink-0041d60c)) and the AI never targets its own side ([`../retail/simulation/ai-targeting.md`](../retail/simulation/ai-targeting.md#is-it-a-target-at-all--ai_istargetable-00411e80)), so a Human group hunting the squad would never fight, while a patrolling leader attacks any enemy it finds. Retail's Human HERC groups outside the squad have no majority first order.
- **A new Cybrid HERC's weapons** are the fit most retail records of its chassis carry, the first in mission and record order on a tie (the editor's choice): no shell file fits a Cybrid chassis, and every mission fits its Cybrids in its own records. A Human chassis takes its stock fit from `gam\ini_*.dat`.
- **A new flyer** stands on a waypoint at height 0, so DBSIM starts it 5,000 units (30 m) up, as every retail flyer group starts ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 5). `HOVTANK` and `DROPSHIP` are not offered, since neither has the data to fly or be shot ([`../retail/simulation/ai-flyers.md`](../retail/simulation/ai-flyers.md)), nor are the five vehicle types DBSIM builds no object for ([`../retail/simulation/structure-behaviour.md`](../retail/simulation/structure-behaviour.md#five-classes-one-switch)).
- **A ground vehicle** is added as a convoy: a structure group whose first member is a vehicle, with a "Travel" order. A vehicle joined to a group led by a building stands as a building does, as the vehicles of the 39 retail base groups led by a building do.
- **A base** is one of eleven ready-made structure groups, one per formation of `dat\BFORMS.DAT` that lays a pad, each holding the buildings retail places in that formation most often. Their names are the editor's own, since no retail file names a formation. A base faces heading 0, since its pad is painted on a map-aligned ground tile while its members' spots turn with the group's heading ([`../retail/rendering/terrain-texturing.md`](../retail/rendering/terrain-texturing.md#base-formation-pads)).
- **A type change** stays within the unit's kind: a building never becomes a vehicle or the reverse, so a convoy stays a convoy (the editor's choice).
- **Loadout presets** are this engine's invention, the editor's own data rather than a mission's.

### Group sizes and roster limits

How many members a group can take comes from how DBSIM spreads and leads them:

- **A HERC group** takes eight, the places `dat\MFORMS.DAT` spreads.
- **A flyer group** takes four, the places `dat\FFORMS.DAT` gives beside the leader; no retail flyer group has more than two members.
- **A convoy** takes four, since the pick of the vehicle that drives the route reaches only a group's first four members (`Group_FirstAbleMember`, `0046a4b8`).
- **A structure group** takes its formation's places; members past them stand on the group's position.

DBSIM holds 100 HERCs, 50 flyers, and 140 structures and ground vehicles together, the sizes of its fixed roster arrays ([Positions](#positions)). The editor counts them as it counts every budget ([Budgets](editor-manual.md#budgets)): every GUID of the roster's row whatever its condition, since it does not resolve which records a load keeps, plus a HERC slot per squad position, which `player.mec` fills.

### A Cybrid's weapons

No retail file says which weapons a Cybrid's hardpoints can take: the weapons screen fits only the player's chassis, and each mission fits its Cybrids in its own records. So the Editor Settings' "Cybrid Loadouts" picks what the hardpoints offer (the user's choice):

- **Vanilla**, the default, offers on each hardpoint the weapons the install's retail missions fit in that slot of that chassis, read from the archives as they shipped, templates included and no condition tested. A record that picks from a random table carries no fit of its own, so each retail fit counts once. A hardpoint no retail record fits, such as MIRIMAC's sixth, is read-only.
- **Unrestricted** offers the weapons screen's whole list on every chassis but PITBULL, which alone is offered BEMP, BPBW and BMSL: retail fits BEMP and BMSL on PITBULL alone, and BPBW on no record.

A Cybrid's hardpoints are numbered from 1 by fit slot, as the squad's are (the editor's choice, since no retail screen shows a Cybrid's). A weapon chosen takes the ammunition type retail records carry with it in that slot most often, the lowest on a tie (the editor's choice): the filler 5 for every weapon that takes no guidance but BMSL, and for a missile rack its usual guidance, so DIABLO's MSL10 takes the filler 5, which DBSIM reads as SARH ([`../retail/simulation/weapon-mounts.md`](../retail/simulation/weapon-mounts.md#the-join--mechloadout_constructweaponmounts-0040fff8)).

### Deleting

A delete is refused while any record, whatever its condition, references what it removes, rather than clearing the reference: the generator resolves a GUID with no record to `-1`, so a dangling reference would silently become no trigger, no target or no subject in the game's load. The view lists each referrer as a link. A group left with no member goes with its last one. The templates a deleted record picked from stay, and deleting a record that picks from a table moves every later roll ([What moves the random draws](#what-moves-the-random-draws)).

### The player's squad

The squad is the first group of row 16. DBSIM builds its machines from `player.mec`, not from their records, so the editor gives the squad no triggers, no Present when and no choice of random table: none of the 138 retail squad records sets one either.

Which HERCs the squad pilots depends on how the mission is launched ([`../retail/shell/main-menu.md`](../retail/shell/main-menu.md#starting-a-practice-mission), [`../retail/shell/campaign-loop.md`](../retail/shell/campaign-loop.md#loading-the-careers-mission)), and the squad's views say which applies:

- On practice rows 0 to 3, and for every mission `gam\career.dat` does not list, played as `INSTANT ACTION`, the squad pilots the HERCs its records give. On practice rows 4 to 7 the player pilots a stock fit of the practice screen's Herc Type instead. Either way the last squadmate listed stays behind, a retail bug this engine keeps ([`../../KNOWN_ISSUES.md`](../../KNOWN_ISSUES.md)).
- In a campaign mission the squad pilots the career's HERCs, and the squad's records set only how many squadmates can go.

The scene shows every HERC of the squad's records whatever the launch (the user's choice). The squad holds at most three squadmates, the squad members the player structure points at ([`../retail/shell/squad-and-crew.md`](../retail/shell/squad-and-crew.md#the-rows)). Its weapons are offered from each chassis's arming layout, `gam\arm_*.dat`, with no unlock or armory stock applied, since a mission's machine is fitted from no stock. A launcher whose stock fit carries the filler 5 is written SARH (0), which DBSIM reads a 5 as anyway (the editor's choice).

## Orders

A group walks only its first order's route, whatever its later orders name ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#the-route-cursor-is-loaded-once)). A later order's route is read only by an objective asking whether the group has finished its order on that route ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#what-else-reads-an-order)), so the editor offers it only where such an objective reads that slot.

- **No shapes retail never uses.** A first order with no route, in a group with no position of its own, which stands on that route, is not made; nor is "Search and destroy", "Ram" or "Follow" with no target, which every retail order of those verbs names and which "Search and destroy"'s completion test reads. A change to one of those verbs on an order with no target gives it the squad as target.
- **"Ram" and "Sleep" never finish by themselves** ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#when-an-order-is-finished--group_isordercomplete-004239fc)), so a group reaches the order after one only through "Ends when".
- **A last order is kept** where DBSIM faults without one: a group with no heading of its own, and a HERC group other than the squad ([`../retail/simulation/ai-goals.md`](../retail/simulation/ai-goals.md#a-group-with-no-order-at-all)). A flyer group keeps its last order too, as every retail flyer group has one (the editor's rule).
- **A group's own route field** follows its first order's route when an edit changes that route and the field named the old one, as 377 of the 433 retail HERC group records keep the two the same (the editor's choice).
- **A shared order**, one GUID run by several groups (7 of the 609 order GUIDs the retail groups name, each in the same slot), is copied before an edit ([Which record an edit writes](#which-record-an-edit-writes)).

## Triggers and countdowns

The editor's UI calls a row-10 action a **trigger** and a row-11 action timer a **countdown**; the code keeps the retail names.

- **A trigger's message** is sent once for each campaign variable the trigger sets, and never when it sets none ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#the-four-ways-an-action-activates)). So a new write adds 1 to campaign variable 0, as all 66 retail action writes do, and the mission check finds a message with no write.
- **An arrival** other than the four drop-pod and on-foot values acts as "In place" ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#arrival--group_deploymentcheck-004236c4)), so the editor shows any other stored value as "In place", and choosing "In place" over one writes nothing.
- **A countdown's own trigger** is the trigger it activates that nothing else can: no area, no unit's engagement or defeat, no other countdown, and not the trigger that starts the countdown. In retail every trigger a countdown activates is of this kind and no countdown activates two, so the editor treats the pair as one step ("after this long, do that"): the trigger's Then list draws the countdown with its own trigger's list inside it, and removes the two together. It nests two deep, which is what the Properties panel's width holds; the training missions chain countdowns up to eight deep.
- **Campaign variable writes** are numbered differently by each layer that makes them: a trigger, an objective, and a unit's or group's out-of-action report ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#the-mission-counters--dat_004a9ef4)), so each list offers only its own layer's operations. A squad HERC's report writes nothing in play, since its slots start empty ([`../retail/simulation/mission-deployment.md`](../retail/simulation/mission-deployment.md#the-out-of-action-report)).

## Objectives

The UI calls a row-17 objective a **win condition** when it is mandatory and a **lose condition** otherwise.

- **Nothing references an objective**: row 17 has no GUID, and DBSIM reads its objectives only to fill and walk its objective array, so an edit writes the record itself.
- **Their order matters.** The status alert the player raises with Q shows the first win condition not yet met ([`../retail/simulation/alert-panels.md`](../retail/simulation/alert-panels.md#its-text)), and a new one goes last.
- **Codes 4 and 10 ask what 3 and 9 ask** ([`../retail/simulation/mission-objectives.md`](../retail/simulation/mission-objectives.md#what-each-condition-asks)), so each pair is offered once, and a record holding 4 or 10 shows as the one it repeats (the editor's choice).
- **The F11 objective lines are text only**: nothing ticks them off or ties them to a win condition ([`../retail/simulation/mission-objectives.md`](../retail/simulation/mission-objectives.md#the-objectives-panel)), so the editor lists them apart, with the mission's text.
- **No objective asks about a flyer** in retail, so the editor offers none (the user's choice).

## The mission's text

- **The 50-line budget.** DBSIM reads the first 50 lines of `mission.str`, which holds every `.ENG` record the load keeps, whichever field references it, the debrief's included ([`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md#the-eng-string-table)). The editor counts one line per `.ENG` id over every version of the mission at once, and refuses an add past 50.
- **Characters.** A field takes only the codes the game's fonts have glyphs for, 32 to 248, and version 1.0's mission screen shows a letter past 127 as a space ([`../retail/shell/mission-screen.md`](../retail/shell/mission-screen.md#the-summary-text-box)).
- **The debrief** is laid out by outcome (the user's choice). Its conditions on campaign variables 0, 8, 9 and 10 read as the mission just played, since the debrief writes them before it loads the mission's text again ([`../retail/shell/campaign-loop.md`](../retail/shell/campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)). Only a campaign mission shows one: a training debrief never loads the mission's text ([`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md#the-debriefs-loss-line)).

## Random tables

The editor edits a random table when each of its options copies one template of row 12, 13 or 14 that picks from no table itself, all from one row, no other table carries its key, and no squad HERC picks from it. Every other table is shown with the reason and no control: route tables, tables under a shared key (`C1_05`'s conditions 36 and 40 share key 172, the second reached only by the rolls the first leaves over) and the training missions' squad tables. Of the 783 retail options, 694 choose a HERC, 30 a flyer, 12 a structure and 47 a route.

- **Chances.** A chance edit rewrites every option's range, contiguous from 0, over a bound of 100 when every chance is a whole percent and 1000 otherwise, which moves no later roll ([What moves the random draws](#what-moves-the-random-draws)). Adding or removing an option keeps the share left over as it was (the editor's choice). A roll in the share left over picks no option, so the record keeps its own fields.
- **Editing an option** writes its template, changing every record that picks it. Only its type and a HERC's weapons and guidance are edited there.
- **Fixed and Random.** Making a fixed unit random gives it a table of one option copying what it shows, so nothing changes in the scene. Making it fixed again gives it, as its own, everything a pick of the selected option copies, and deletes the table only when it is still as Random made it and nothing else uses it (the editor's choice).
- **A squad HERC's pick has no effect** where the launch gives the squad other HERCs or leaves that one out ([The player's squad](#the-players-squad)), and the table's view says so.

## Present when

A unit's, group's, order's or objective's "Present when" is written as a chain of row-1 conditions, each requiring the one before, and the record names the outermost.

- **The chance** is a draw that requires the campaign variable tests, so the roll is made only when they hold, as each retail draw requires its test. A new chance is a new draw below 100, so a percent is one step (the editor's choice; every retail draw is below 11). Another record's draw is never reused, since two records on one roll appear together. Each condition of the old chain is deleted once nothing references it.
- **A GUID whose records carry different conditions** is not set as one rule: writing one record alone could leave another making a half-filled copy of the unit in the loads where the first is absent.
- **A template is never written**: a record's condition is its own word, outside what it copies from a template ([`../retail/formats/msn-mission-file.md`](../retail/formats/msn-mission-file.md#random-tables)).
- **Members conditioned apart from their group** leave the group with no members where they are absent, which the game does not handle: an objective, order or trigger asking about the group can crash or activate by mistake. The mission check finds it, and the view offers to move the condition to the group.
- **Outside the campaign** the launch clears every campaign variable but the three random numbers (4 to 6), so a rule on any other variable that does not hold at 0 is never present there, and the view warns.

Some records have no Present when:

- the player's squad, its orders and its HERCs, since the squad build reads the squad's members (the editor's choice);
- a route (the user's choice): every conditioned retail route record is a template, so every route is present in every version;
- a trigger area, a trigger and a countdown: no retail record of rows 9 to 11 carries a condition.

## Mission check

The mission check runs over every version of the mission at once: each thing as its base, then with each variation set over it, then with each option of its random tables copied in, so a problem only some loads have is still found, and marked so. Rosters and text are counted as the budgets count them. The squad is exempt from the first-order and group-kind rules, since DBSIM builds it from `player.mec`. Each finding's DBSIM reason is `ScriptDatLint`'s; the Problems list words it in the editor's terms (`ProblemText`), and the HercWorks tools print the check's own `.MSN` terms.

## Writing the editor's UI

What the user has corrected in the editor's panels, which every new view follows:

- **Vocabulary.** A trigger *activates*. Win and lose conditions, triggers and countdowns are named so in every label, link and refusal reason ([Triggers and countdowns](#triggers-and-countdowns), [Objectives](#objectives)). The player's group is the squad, its other members squadmates. The player *pilots* a HERC; "fly" is for flyers. The subject of a sentence is the player, the mission or the HERC, never "the game". A group's side is its **Team**. A structure or ground vehicle is titled by its kind and the name the MFD's F5 screen prints, its type number under Advanced. The editor says "references", never "names it". Never "version" for the mission as one load plays it: it reads as another file of the mission, so phrase the sentence to need no term.
- **Names.** Every record is named by what it is and does, with no editor-only names (the user's choice). Its number, counted from 1 by its place in its row, follows the text; `MissionText.Number` is the one place that computes it. A name inside another name refers by number alone, since full names would loop (an area named by its trigger, the trigger by its area). A name is drawn on one line, cut to fit and whole on hover.
- **Plain words, short.** A note says what the user sees or can do in one short sentence, not how the records produce it. Labels are the user's words. A field or group should explain itself; it does not need a paragraph above it.
- **No note for what a disabled control already shows.** A field the user cannot change is drawn disabled; a note is added only when the reason is not obvious.
- **Notes go through `EditorText`**, which wraps them in a gray lighter than ImGui's disabled text; unwrapped, the Properties panel cuts them off. `EditorText.Emphasis` draws the one note per view the user must not miss.
- **Latin-1 only.** The ImGui font atlas holds Basic Latin and Latin-1, so an em dash or a curly quote draws as `?`.
- **Identity first.** An object's view opens with its name and then its group.
- **Errors point at the fix.** A refused input shows its reason in strong red and tints the field to change.

## Open

- **Open:** what the editor does not edit yet. Row 1: a random table's requirement, the order of conditions, route and squad tables and tables under a shared key. Row 2: the objective type (header offset 6), the training mission number, and the zone after New mission. Rows 3 and 5: the movies (the debrief's shows as a number), the debrief's outcomes and their clauses, the loss line. Row 4: the briefing movie (`0x8E`), the text package's records after the first, a line's trailing newline, and a line's replacements under other conditions (status alert texts likewise). A HERC's or flyer's own heading; a HERC's cruise speed; a group's own route field and its map-shown flag; an order's formation and point; a trigger's leftover text (`0x44`); the campaign variables a unit or group sets when put out of action (shown only); the order of objectives; a random table option's fields other than type and weapons.
- **Open:** a variation is named in its record's Present when but not edited there; the user's choice is to list each variation in the view with its fields editable, an edit inside it writing that record.
- **Open:** features not built yet: moving a unit into a new group of its own; clearing a reference that refuses a delete; a check of a line's length against the mission screen's wrap; a heading turn handle; the mission lists showing each mission's zone and theater; ghosts of empty formation places; an order's point, which only the briefing map reads, as a marker; tinting every area of a selected trigger, and structures' ground planes inside a tinted area; waiting groups as translucent ghosts rather than outlines; an orthographic top-down view (V is the perspective camera at its pitch limit); a trigger graph showing the wiring at once.
- **Open:** an objective about a later order whose route differs from every earlier order's takes several steps to make: it starts with no route, and the route list offers the group's orders' routes only once its test is the finished order.
- **Open:** a trigger area's shape or radius edit writes its supplying record alone; another record of its GUID under a condition keeps its own shape.
- **Open:** the mission check counts a reference as missing only when no record of the row carries the GUID; one to a record present only under other conditions than the referrer's is not checked.
- **Open:** a unit's view names only the group that places it; a later group of another condition that references the same record is not listed, though the user's choice is that it is.
- **Open:** where DBSIM stands a group with neither a position of its own nor a first-order route; the editor stands it at the map's corner (0, 0).
- **Open:** a HERC's or flyer's own position has not been seen in retail play: no retail record names one, so DBSIM honoring it is read from the code ([`../retail/formats/script-dat.md`](../retail/formats/script-dat.md#placement--the-actual-rule), rule 2). Confirming it means running retail DBSIM over such a mission.
- **Open:** whether VSHELL's `script.dat` reader for the briefing map (`ShellMap_LoadScriptDat`, VSHELL `004243d7`) has fixed arrays a long mission overruns.
- **Open:** whether every retail random table has an option whose range holds a roll of 0. A table with none picks no option at the editor's all-zero open, and its users keep their own references, which point nowhere, in that generation, while the layout shows the table's first option.

