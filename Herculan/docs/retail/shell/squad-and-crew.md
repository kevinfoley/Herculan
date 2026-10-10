# The squad panel and the crew screen

The squad panel the WEAPONS, REPAIR, BUILD and CREW tabs share down the left of the canvas, and tab 6, the crew screen, which assigns pilots to the bays. The frame they sit in is in [`screen-layout.md`](screen-layout.md).

## The squad panel

`wsquadi.cpp`'s per-slot panel, built once by `Squad_BuildRosterList` (0043c999) and put up by `Squad_ShowPanel` (0043cfe7) on the WEAPONS, REPAIR, BUILD and CREW tabs. Every rect is a canvas literal.

| Widget | Class | Rect | Content |
|---|---|---|---|
| 8 pictures | `Grid` | `{5, 0x2b, 0xeb, 0x130}` | one per bay; filled and moved by the tab ([below](#the-bay-picture)) |
| empty picture | `Grid` | `{5, 0x2b, 0xeb, 0x130}` | `DAT_0048d4dc`, grid lines off |
| pilot label | `Text` | `{5, 0x134, 0x25, 0x141}` | `0x65` `Pilot:`, left, `0x1a` |
| pilot | `Text` | `{0x28, 0x134, 0x82, 0x141}` | left, `0x17`, opaque |
| skill label | `Text` | `{5, 0x141, 0x25, 0x14e}` | `0x66` `Skill:`, left, `0x1a` |
| skill | `Text` | `{0x28, 0x141, 0x82, 0x14e}` | left, `0x17`, opaque |
| condition label | `Text` | `{0x8d, 0x134, 0xea, 0x141}` | `0x67` `Condition:`, right, `0x1a` |
| condition | `Text` | `{0x8d, 0x141, 0xea, 0x14e}` | right, opaque |
| roster | `TitledPanel` | `{6, 0x154, 0xec, 0x1da}` | `0x64` `Squad Inventory`, header 19 tall, `+0x65 = 0` |
| 8 rows | `Panel` | `{9, i*0xe + 0x16, 0xe2, i*0xe + 0x23}` in the roster | border `0x10`, `0x29` on the selected bay |

The rows are 14 tall on a 14-pixel pitch, so unlike the repair lists they do not overlap. Each is a [four-column row](weapons-and-repair.md#a-row-is-four-text-columns) cut at `0x21`, `0x70` and `0x7b`: `"%d."` of the bay number centred, the machine's name left, `-` centred, and the crew column left. `Squad_RefreshRowNames` (`0043da47`) writes the name, `estext.bin` `0x6e + type`, in the band colour of the machine's overall condition (`HercStatus_OverallCondition`, 00411bd4), finished or not. `Squad_RefreshRowCrew` (`0043dad7`) writes the crew column in `0x27`: the assigned pilot's name, or `"%d%s"` of the build percentage and `0x6c` `% Complete` for a machine still being built.

`Squad_RefreshReadout(bay)` (`0043d38a`) fills the readout:

| Field | Bay with a pilot | Machine, no pilot | Empty bay |
|---|---|---|---|
| pilot | the pilot's name | `0x6d` `Unassigned` | blank |
| skill | `0x35 + skill` | blank | blank |
| condition | the band word `0x68 + level` of the overall condition, in the band colour; while the machine is being built, `"%d%s"` of the build percentage and `% Complete` in `0x20` | the same | blank |

A bay's pilot is `Squad_PilotForBay(00482a78, bay)` (`00410220`), which looks at exactly four records: the player's own, embedded at `+0x04`, and the three squad members the player structure points at from `+0x3f`. `Player_Read` (`004101b8`) sets those pointers on load to record `DAT_00483b48[k]` of squad `k`, so a pilot elsewhere in the squad block is never shown against a bay.

**A roster click is `Squad_SelectBay(bay)` (`0043d64d`)**, through eight thunks from `Squad_OnRosterRow0` (`0043dde7`), each of which sets `DAT_004765be` for the length of the call. It returns at once for the bay already selected, and each tab takes the click its own way, picked by `DAT_0047581c`. The repair tab's arm refuses a bay that is empty or still being built; otherwise it hides whichever of the old bay's two pictures was up and shows the new bay's external one, relights the two rows, stores `DAT_00482ae5`, resets the selection with `Repair_SelectHotspot(0, 0)`, and refills the names, the rows and the panels. The weapons tab's arm is in [Entering the weapons screen](weapons-and-repair.md#entering-the-weapons-screen), the build tab's in [Scrapping and building are gated on the bay](build-and-armory.md#scrapping-and-building-are-gated-on-the-bay), and the crew tab's in [Entering the crew screen](#entering-the-crew-screen).

### The bay picture

`Squad_ShowPanel(tab)` starts with `Squad_BuildTabPictures(tab)` (`0043c915`), which fills the eight pictures for the tab: `Repair_BuildDiagrams` on the repair tab ([The damage diagram](weapons-and-repair.md#the-damage-diagram)), and `Squad_BuildBayPictures` (`00414e5b`) on WEAPONS, BUILD and CREW. It then shows the selected bay's picture, or the empty one (`DAT_0048d4dc`) when no bay is selected.

`Squad_BuildBayPictures` moves each bay's picture to `{5, 0x2b}` at `0xe7` by `0x105` — `Window_SetWidth` (`0041ec33`) and `Window_SetHeight` (`0041ece6`) take a size and write the far corner as `origin + size - 1`, so the pictures end on row `0x12f`, one short of the rect they were built at — and switches their grid lines off. It fills them from `gam\arm_<chassis>.dat` ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamarm_dat--armory-layout)) and three banks per chassis, whose stems are not the layout files': `out`, `rap`, `tom`, `sam`, `col`, `apoc`, `ogr`, `mav` and `fly`.

| Part | Slot | Frame | Position |
|---|---|---|---|
| top half | the first layout record's id | `dba\<stem>_bod.dba`, the record's own frame once the machine is built; `dba\mt_3qtr.dba` frame 0 at 0% built; body frame 2 below 51% built and 4 from it | the record's |
| bottom half | the second record's id | as above with frames 1, 1, 3 and 5 | the record's |
| each fitted weapon | the group record's id | `dba\<stem>_wep.dba`, the record's frame | the record's, one pixel in from the load |

A weapon's record is the one in its weapon-id group whose id is `slot + 2`, found by `RepairLayout_FindWeaponPart` (`00413ccc`), and a record whose frame is `-1` draws nothing. Every part takes its record's `+0x16` as its blit flags and no colour remap, so nothing on this picture is recoloured by damage. An empty bay, and the empty picture, get `mt_3qtr`'s two frames at `(1, 1)` and `(1, 0x8c)`. Retail's layouts put the body in slots 0 and 1 and the weapons from 2.

The body banks do not all carry the construction frames: `out_bod` and `mav_bod` have two frames, `rap_bod` and `tom_bod` four, and the other five six ([Open](#open)).

## The crew screen

Tab 6, `CREW`. Built once by `Crew_BuildScreen` (00440eb8, `wcrewi.cpp`), which also loads `dba\c_pilots.dba` and `dba\star.dba`; entered by `Crew_Enter` (00441a01) and hidden by `Crew_Leave` (00441afa), the teardown dispatcher's crew arm. Rects are parent-relative. The left of the canvas is [the squad panel](#the-squad-panel).

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| content panel | `TitledPanel` | `{0xf1, 0x2b, 0x278, 0x1d9}` | `0xa8` `PILOT ASSIGNMENT`, header 19 tall, plate `0x7e`-`0x108`, face `0x25` |
| 3 squad portraits | image panel | `{0xbc, 0x19, 0xf7, 0x50}`, `{0xfc, …, 0x137, …}`, `{0x13c, …, 0x177, …}` | the squad member's portrait at `(1, 1)`, border `0x21` |
| `CLEAR` | `Button` | `{0x99, 0x188, 0xdf, 0x197}` | `0xa9`, border `0x22` |
| label | `Text` | `{10, 0x1d, 0xb4, 0x29}` | `0xab` `Available Pilots:`, right, `0x29` |
| 4 rows | `HatchedDivider` | `{0xd, i*0x4a + 0x58, 0x178, i*0x4a + 0x9e}` | `+0x55 = 0`, `+0x5d = 1`, border `0x21` |
| row portrait | image panel | `{0xb, 9, 0x43, 0x3d}` in the row | row 0 `star.dba` frame 0, rows 1-3 the pilot's portrait |
| 3 labels | `Text` | `{100, 0xb, 0xec, 0x18}`, then 13 lower twice | `0xac` `Name:`, `0xad` `Skill:`, `0xae` `Herc:`, right |
| 3 values | `Text` | `{0xef, 0xb, 0x164, 0x18}`, then 13 lower twice | left, opaque |

A pilot's portrait is the `c_pilots` frame its roster id (`+0x00`) names; the bank has twelve, one per roster id. The three squad portraits are the three pilots the player structure points at from `+0x3f` — the squad members of [the squad panel](#the-squad-panel).

**The image panel** is the class `ESBitmap_Ctor` (0040b698) builds. Its paint, `ESBitmap_Paint` (`0040b772`), blits the bitmap at `+0x5d` at the offset `(+0x55, +0x59)` and then draws the border over it with no fill, so a 56x52 portrait at `(0, 0)` in a row's 57x53 panel loses its top row and left column to the border. The constructor clears `+0x51`, which stops the paint drawing the border and leaves the bitmap; the builder sets it on all seven.

**`ESArm_Paint` (0040c513)** fills and borders the panel, draws horizontal lines in `+0x59` from row `+0x55` down to the bottom border, redraws row `+0x55` and the border in `+0x4d`, and with `+0x5d` set draws a second border one pixel inside the first. The constructor sets `+0x55` to the widget's height, which draws no lines at all; the crew builder writes 0, which makes the lines a solid body.

### The rows

Row 0 is the player; rows 1-3 are the three squad positions. `Crew_MatchRowPilots` (`00441b08`) fills the pointers at `004776e0` with the squad member whose position, pilot record `+0x27`, is the row — `Squad_MemberAtPosition` (`004102d6`) tests the three in pointer order — or null. `Crew_FillRows` (`00441c4f`) fills the values: the player's name, `0x35 + skill` and the chassis name `0x6e + type` of the player's bay (`00482a9e`), or `0x7e` `None` when that bay is empty; the same three from a squad member's record and bay; and for a row with no pilot, no portrait and `estext.bin` entry 0 in all three.

`Crew_ColourRows` (`00441857`) colours each row by whether it is below `DAT_00482a78`, the count of squad positions in play ([`../formats/save-games.md`](../formats/save-games.md#savgame_sav--block-order)):

| | Row in play | Row out of play |
|---|---|---|
| body, `+0x59` | `0x25` | `0x10` |
| labels | `0x19` | `0x1a` |
| value backing, `+0xc5` | `0x25` | `0x10` |

It writes the values' own colour too, `0x16` and `0x17`, but `Crew_FillRows` runs after it and its `ESMessage_SetString` calls overwrite it with `0x29`, so every value is drawn in `0x29`.

**`Crew_SelectRow(row)` (`00441b85`) selects a row**, and a row's panel and its portrait both carry it as their click handler, through four thunks from `Crew_OnRow0` (`004423b0`). The row's six texts are built with no handler and take no mouse events, so a click on one reaches the row ([Which widget a click reaches](widgets.md#which-widget-a-click-reaches)). It relights the border of the row and its portrait — `0x21` on the row being left, `0x29` on the new one — calls `Squad_SelectBay` (`0043d64d`) with the row's pilot's bay, the player's for row 0 and `-1` for a row with no pilot, and only then stores the row in `DAT_004776dc`. It has no early return, so clicking the selected row runs it again.

### Entering the crew screen

`Crew_Enter` runs `Crew_ColourRows` (`00441857`) and `Crew_MatchRowPilots` (`00441b08`), sets the three squad portraits, selects rows 0, 1, 2, 3 and then 0 again, runs `Crew_FillRows` (`00441c4f`), and then calls `Squad_SelectBay(bay)` (`0043d64d`) for every bay that holds a machine, in order.

The tab handler has already stored 6 in `DAT_0047581c` ([What a tab click does](screen-layout.md#what-a-tab-click-does)), so all of those calls take `Squad_SelectBay`'s crew arm. It accepts `-1`, or a bay holding a finished machine that is not the Razor unless `DAT_004776dc` is 0 — a squad member cannot be given the Razor, and on a row click the row it tests is the one being left. It swaps the bay's picture and relights the roster rows as the repair arm does, stores `DAT_00482ae5` and refills the readout; while `DAT_004765be` is set, which a roster click and `CLEAR` do, it first gives the bay to the selected row's pilot ([below](#assigning-pilots)).

**So the screen opens on the last bay that holds a finished machine**, whatever bay was selected before and whichever is the player's: the row loop ends on row 0, which lets the closing loop accept every finished machine, the Razor included. The readout under the picture is that bay's until a row is clicked.

### Assigning pilots

Three clicks change the crew, all against the selected row.

**A squad portrait** — handlers `Crew_OnSquadPortrait0` (`00442151`), `Crew_OnSquadPortrait1` (`004421fc`) and `Crew_OnSquadPortrait2` (`004422a7`) — lights its own border `0x29` and the other two `0x21`, then calls `Crew_AssignSquadMember(k)` (`00441eb8`). On the player's row that returns at once, so the portrait lights and nothing moves. On a squad row, whoever holds the row's position gives it up (`Squad_SetMemberPosition(k, -1)`, `004102be`), squad member `k` takes it, and the row pointers at `004776e0` follow; the member is then given the selected bay by `Crew_AssignSelectedBay`, exactly as a roster click gives it. The lit portrait is a widget colour that no entry resets, so it stays lit across visits.

**A roster click** reaches `Crew_AssignSelectedBay` (`00442055`) from inside `Squad_SelectBay`'s crew arm ([above](#entering-the-crew-screen)), so the bay already selected, and a bay the arm refuses, assign nothing. It runs only when the selected row is the player's or has a pilot. Whoever holds the bay loses it first: the player through `Player_SetBay(-1)` (`0040e6c8`, which writes `00482a9e`), and each squad member through `Squad_SetMemberBay(k, -1)` (`0040e6d7`), which also takes them off strength. The row's pilot then takes the bay, and for a squad member `Squad_UpdateOnStrength` (`00410366`) recomputes the on-strength byte for the row's position.

**Taking a bay leaves its old pilot with none.** Nothing hands the new pilot's previous bay on, so giving the player's bay to a squad member leaves the player's `Herc:` reading `None` until a bay is clicked on row 0. With no bay selected — where selecting an empty row leaves it — the bay handed out is `-1`, and every squad member with no bay counts as its holder.

**`CLEAR`** (handler `Crew_OnClear` (`00442352`)) calls `Crew_ClearRow` (`00441f94`), which on a squad row holding a pilot calls `Squad_SetMemberBay(k, -1)`, frees the position, nulls the row pointer, and then calls `Squad_SelectBay` with the member's bay — `-1` by then — with `DAT_004765be` set. The row is empty by that point, so the assignment inside finds no pilot, and what remains is the picture going to the empty bay. On the player's row, or an empty one, it does nothing.

Both the unassign and the recompute write the on-strength byte through `Squad_SetOnStrength` (`00410327`), which moves `00482a7a`, the count of machines on strength ([`../formats/save-games.md`](../formats/save-games.md#savgame_sav--block-order)), by one whenever the byte changes.

## Open

- **Open:** what retail draws for a machine under construction whose body bank lacks the construction frames ([The bay picture](#the-bay-picture)). `Squad_BuildBayPictures` (`00414e5b`) indexes past them unchecked.
- **Deferred:** whether anything calls `Squad_ColorSelectedBayPicture` (`0043d1c0`), which given tab 3 colours the selected bay's picture by condition band — external groups 0-5 into parts 0-5 and each hardpoint into part 6 + slot, part i to group i with no chassis part table, unlike `Repair_ColorDiagram` (`0041469a`) — and repaints it. `es2_xref.py` finds no caller or stored pointer.
