# The weapons and repair screens

Tabs 2 and 3, which work on the machine in the bay [the squad panel](squad-and-crew.md#the-squad-panel) has selected: fitting weapons to its hardpoints and repairing it, and the hotspots both lay over its picture. What the repairs cost is in [`armory.md`](armory.md).

## The weapons screen

Tab 2, `WEAPONS`, the arming screen. Built once by `Arming_BuildScreen` (`0043e52c`, `warmingi.cpp`), entered by `Arming_Enter` (`0043f548`) and hidden by `Arming_Leave` (`0043f5d2`), the teardown dispatcher's arming arm. Rects are parent-relative. The left of the canvas is [the squad panel](squad-and-crew.md#the-squad-panel), filled with the three-quarter [bay picture](squad-and-crew.md#the-bay-picture).

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| content panel | `TitledPanel` | `{0xf1, 0x2b, 0x278, 0x1d9}` | `0x9f` `Weapons`, header 19 tall, plate `0x66`-`0x116`, face `0x25`, filled body |
| inventory | `TitledPanel` | `{6, 0xc5, 0x183, 400}` | `0xa0` `Weapons Inventory`, header 19 tall, `+0x65 = 0` |
| picture box | `HatchedDivider` | `{6, 0x1a, 0x183, 0xc0}` | border `0x15`, `+0x55 = 0x76`, `+0x59 = 0x25`, `+0x49 = 0` |
| 26 weapon pictures | image panel | each `gam\arm_weap.dat` record's corner in the box, sized to its frame | that frame of `dba\arm_weap.dba` |
| 4 guidance pictures | image panel | the same, from the file's second list | |
| blank picture | `Panel` | `{0x14, 0x1a, 0x14a, 0x5f}` in the box | `+0x51 = 0` |
| `ARM`, `ARH`, `SARH`, `EO` | `Button` | `{0x152, 5, 0x179, 0x16}`, then `0x17` lower three times, in the box | `0xa1`-`0xa4`, border `0x22`, hidden |
| rack button | `Button` | `{0x101, 0x61, 0x179, 0x72}` in the box | built blank, border `0x22`, hidden |
| 3 description lines | `Text` | `{1, 0x7a, W, 0x88}`, `{1, 0x88, W, 0x94}`, `{1, 0x94, W, 0xa0}` in the box | centred, `0x29`, opaque in `0x25` |
| 13 rows | `Panel` | `{9, i*0xd + 0x14, 0xaf, i*0xd + 0x20}` in the inventory | border `0x10` |
| 14 rows | `Panel` | `{0xbf, i*0xd + 0x14, 0x165, i*0xd + 0x20}` in the inventory | border `0x10` |
| label | `Text` | `{0xe, 0x195, 0x76, 0x1a1}` | `0xa5` `Hard Points`, right, `0x29` |
| `<` | `Button` | `{0x7d, 0x194, 0x8b, 0x1a3}` | border `0x22` |
| `>` | `Button` | `{0x91, 0x194, 0x9f, 0x1a3}` | border `0x22` |

`W` is the box's own width, `+0x2d - +0x25`. The `ARM` to `EO` buttons step down by `0x17`, so the four are 18 tall with five rows between them.

**The picture box is black over a grey band.** Its line fill starts on row `0x76` in `0x25`, which makes everything below that row a solid band and leaves the interior above it at the paint's own `0x10` ([The crew screen](squad-and-crew.md#the-crew-screen) for the class). The pictures sit in the black, the description lines in the band. The box's enable flag is cleared, so a click anywhere on it that no button takes is swallowed.

**A picture is a bare bitmap.** Each is placed at its `arm_weap.dat` record's corner and sized `{x, y, x + width, y + height}` to its frame, and the builder writes `+0x51 = 0` on it, which `ESBitmap_Ctor` has already done: that stops [the image panel's paint](squad-and-crew.md#the-crew-screen) drawing a border, and the bitmap is blitted regardless. The weapon pictures are indexed by row through `Arming_RowOfWeapon` (`0043f6f7`) into `0048d5a0`, the guidance pictures by kind into `0048d608`. `None` has no record and shows the blank picture, a `Panel` whose cleared `+0x51` leaves it drawing nothing.

### The inventory rows

The rows list the 27 weapon ids of the table at `004769b0` — `arm_weap.dat`'s 26 in the file's own order, then `None` (0) — thirteen down the left and fourteen down the right, 13 tall on a 13-pixel pitch. Each is a [four-column row](#a-row-is-four-text-columns) cut at `0x8b`, `0x8c` and `0x8d`: the name, `estext.bin` `0x7e + id`, left from `2`; two one-pixel columns holding a space; and the count right-aligned from `0x8d` to one inside the row.

`Arming_RefreshRows` (`0043fbc6`) regates all 27 on every row selection. The count is `"%d"` of the weapon's owned count, `weapons.dat` record `+0x17`, or two spaces for `None` ([`../formats/weapons-dat.md`](../formats/weapons-dat.md#weaponsdat-catalog-record-29-bytes)). A weapon whose unlock flag `+0x16` is clear has its row disabled and its four columns set to `0x10`, the background, so the list shows a gap and keeps the builder's `"0"` where the count would be. An unlocked weapon's row takes `0x27` and is live while `Arming_RowLive` (`004149fb`) holds: always with no hardpoint selected, and with one only when the chassis's armory layout has a part for that weapon at `slot + 2`, the socket [the bay picture](squad-and-crew.md#the-bay-picture) draws it in. A row that fails is disabled in `0x25`.

### Selecting a row

`Arming_SelectRow(row)` (`0043f71c`) is each row's handler, through 27 thunks from `Arming_OnRow00` (`00440300`), each of which sets `DAT_00476d58` for the length of the call. It does nothing for the row already lit (`DAT_00476d5a`) unless a guidance picture has been put up since (`DAT_00476d5c` not `-1`) or a hardpoint is selected. Otherwise it:

1. With a hardpoint selected, refuses a weapon [it will not fit](#fitting-a-weapon), and `Arming_FitSelected` (`0043dc44`) fits the one it accepts.
2. Runs `Arming_RefreshRows`, then puts the old row back to `0x27` or `0x25` by `Arming_RowLive` with its border `0x10`, and hides its picture and any guidance picture that is up.
3. Lights the new row: border and all four columns `0x29`, enabled whatever its unlock flag says. Shows its picture, or the blank one for `None`, and fills the three description lines from `wpn_desc.bin` entries `id * 3` to `id * 3 + 2`.
4. For ids `0xd` to `0x10` — the three missile racks and the Razor's launcher — captions the rack button with the weapon's name and shows the five buttons (`Arming_ShowGuidanceButtons`, `0043f5f7`, once, on `DAT_004769e6`); with a hardpoint selected it then runs `Arming_ShowGuidance` with that mount's kind and its second argument clear, which lights the kind's button and leaves the pictures alone. Any other id runs `Arming_HideGuidanceButtons` (`0043f644`), which puts the four guidance borders back to `0x22` and hides all five, and sets `DAT_00476d5c` to `-1`.
5. Stores the row in `DAT_00476d5a`.

A missile rack's row therefore leaves `DAT_00476d5c` where it was, with its picture hidden, so clicking the rack's row again runs the selection through.

### Guidance kinds

The four buttons call `Arming_ShowGuidance(kind, 1)` (`0043fd69`) with `ARM` 2, `ARH` 1, `SARH` 0 and `EO` 3 — the ids a fitted mount's record carries at `+0x08`, where the arming code reads 5 for an empty mount. It relights the borders, `0x22` on the kind in `DAT_00476d60` and `0x20` on the new one; with its second argument set it hides the lit row's picture, shows the kind's, fills the description lines from `wpn_desc.bin` `99 + kind * 3`, and stores the kind in `DAT_00476d5c`. It always stores the kind in `DAT_00476d60`, and `Arming_SetMountGuidance` (`0043dcb6`) writes it into the selected hardpoint's mount at `+0x08` when there is a hardpoint and a mount there.

The rack button's handler (`Arming_OnRackButton`, `0044012a`) runs `Arming_SelectRow` on the lit row, which a guidance picture being up lets through: it puts the rack's own picture and description back.

`wpn_desc.bin` is three lines per weapon id: the 33 ids fill entries 0 to 98, and the four kinds 99 to 110 in id order.

### Entering the weapons screen

`Arming_Enter` shows the content panel, the inventory and the picture box. When the selected bay is `-1`, empty or holds a machine still being built, it calls `Squad_SelectBay` (`0043d64d`) with `Herc_FirstBuiltBay` (`00410c2a`). It then clears the hardpoint (`ArmingSelectedHardpoint`, `0xffff`) and runs `Arming_SelectRow(0)`. **So the screen opens on the first row, `AutoCannon 20mm`, with no hardpoint selected**, and a row click from there shows the weapon and fits nothing.

The tab handler stores 2 in `DAT_0047581c` only after the entry ([What a tab click does](screen-layout.md#what-a-tab-click-does)), so that `Squad_SelectBay` takes the arm of the tab being left. Every arm takes a finished machine, except that the crew arm refuses the Razor while the crew screen's selected row is not the player's, which leaves the screen with no bay. `Repair_Enter` makes the same call in the same place ([Which names a chassis shows](#which-names-a-chassis-shows)). Recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

**This tab's arm of `Squad_SelectBay`** refuses an empty bay and an unfinished machine and accepts `-1`. It clears the hardpoint, clears part slot 12 of the old bay's picture — the slot `Arming_MarkHardpoint` (`004155db`) puts the selected socket's outline in — runs `Arming_SelectRow(0)`, and then swaps the bay pictures, relights the two roster rows, stores `DAT_00482ae5` and refreshes the readout.

### Fitting a weapon

A hardpoint is selected by `Arming_SelectHardpoint` ([below](#the-arming-and-repair-hotspots)), which the ten arming hotspots and the two steppers reach: `<` (`Arming_OnPreviousHardpoint`, `00440244`) calls `Arming_PreviousHardpoint` (`0043dd49`), which wraps from 0, or from none, to the last mount, and `>` (`Arming_OnNextHardpoint`, `004402a2`) calls `Arming_NextHardpoint` (`0043dd09`), which steps modulo the mount capacity `+0x4c`, so from none to the first. It selects the row of the mount's fitted weapon, `None`'s for an empty mount, and runs `Arming_MarkHardpoint`. With no bay selected the steppers, like everything else here, read the machine through `00482abf`, the dword before the bay array, and `>` divides by the capacity it finds there ([Open](#open)).

**`Arming_MarkHardpoint` outlines the socket.** It looks up the socket's `slot + 2` record in the group of the weapon the mount carries, `None`'s group for an empty one — every retail `arm_*.dat` has a `None` record for every socket — and puts the record's frame of the chassis's `dba\<stem>_out.dba` (the table at `0046ffc0`) in part slot 12, at the record's second position, with `0xba` remapped to `99`. It also redraws the socket's weapon part, which is what the bay picture already holds. A socket with no record, or one whose frame is `-1`, leaves slot 12 as it was, outline included. A bay change clears slot 12 ([above](#entering-the-weapons-screen)) and the tab's teardown clears all thirty parts of every bay picture (`Squad_HidePanel` through `Squad_FreeTabPictures`, `0043c95a`, and `Squad_FreeChassisPictures`, `004153f1`), so an outline lasts until the bay changes or the tab is left.

With a hardpoint selected, `Arming_SelectRow` refuses a weapon the armory holds none of unless the mount already carries it; `None` is never refused. The test reads the mount's guidance kind at `+0x08` where the weapon id at `+0x00` belongs before it reads the fitted id, so a mount whose kind number equals the weapon's id skips the refusal. `Arming_FitSelected` then calls `Herc_FitMount(herc, hardpoint, weapon)` only while `DAT_00476d58` is set, which is only inside a row's own thunk — the entry, a hardpoint, the steppers and the rack button select a row without fitting it — and in every case runs `Arming_MarkHardpoint` again.

**`Herc_FitMount` (`004114ec`) moves units, not counts.** The mount's unit goes back onto its weapon's stock list through `Armory_AddUnit` and the machine's occupied-mount count at `+0x4e` drops by one. `None` then leaves the slot empty at condition 100. Any other weapon takes the head of its list through `Armory_PopUnit` — the unit most recently returned or delivered, so refitting the fitted weapon puts the same unit back. The unit's `+0x04` becomes the hardpoint's condition through `HercStatus_Set`, `+0x4e` goes up by one, and the unit's guidance kind is written: ARH (1) for the three missile racks `0xd` to `0xf`, and 5 for every other id, the Razor's launcher `0x10` included. With the list empty the pop returns nothing, and the slot is left empty with its condition untouched. That is what the refusal's misread lets through: a weapon with none in stock whose id equals the mount's guidance kind. `AutoCannon 20mm`, `35mm` and `50mm` (ids 1-3) pass on a rack or the Razor's launcher set to ARH, ARM or EO, and `AutoCannon 100mm` (id 5) on a mount of kind 5, which `Herc_FitMount` gives every weapon but the three racks. The fitted unit returns to stock and nothing is fitted. Recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

**A unit keeps the condition it was fitted at.** `Herc_FitMount` returns the outgoing unit without copying the hardpoint's condition into its `+0x04` ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#the-weapon-unit-record)). `Herc_ReadStatusBlock` (`00411720`), `Repair_Apply` (`004113af`) and `Herc_StripMounts` (`00411795`) change a hardpoint's condition or move its unit, and none of them write `+0x04` either. The hardpoint's condition is held only in the machine's status block, and the next fit overwrites it with the incoming unit's `+0x04`. So a mount's damage, and any repair to it, is dropped whenever the unit comes off. The unit goes back to stock at the `+0x04` it arrived with: 100 for a delivered, granted or starting unit, or the condition its `results.dat` salvage pair carried for a salvaged one ([`campaign-loop.md`](campaign-loop.md#the-debrief--game_processmissionresults-0040eae7)). Clicking the fitted weapon's own row with its hardpoint selected passes `Arming_SelectRow`'s refusal, which lets through a weapon the mount already carries. `Herc_FitMount` then pushes the unit onto its list and pops the same unit back, setting the hardpoint to that `+0x04`, which repairs it for nothing. Recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

**The rows are regated only by a selection that goes through.** `Arming_RefreshRows` runs inside `Arming_SelectRow` after the fit, so the gating is whatever the last selection that passed the early return left. The entry and a bay change clear the hardpoint and then select row 0, which returns at once when row 0 is already lit and no guidance picture is up — leaving the rows gated for the hardpoint just cleared, the socket's dead rows disabled, until another row is selected. Recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

## The repair screen

Tab 3, `REPAIR`. Its widgets are built once by `Repair_BuildScreen` (00432037), the screen is brought up by `Repair_Enter` and taken down by `Repair_Leave`, its rows are filled by `Repair_FillRow` (004339b2), its component names set by `Repair_SetComponentNames` (00433cdf), its selection moved by `Repair_SelectHotspot` (00433eb9) and its three readout panels refilled by `Repair_RefreshDetail` (00433445). Every rect is four immediates on the builder's stack and they are parent-relative, as everywhere else.

The content panel takes the right two thirds of the canvas. The left is the [damage diagram](#the-damage-diagram) and the [squad panel](squad-and-crew.md#the-squad-panel); of those this builder owns only the eight internals pictures.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| 8 internals pictures | `Grid` | `{0x10, 0x2f, 0xe0, 0x12f}` in the canvas | one per bay, [below](#the-damage-diagram) |
| content panel | `TitledPanel` | `{0xf1, 0x2b, 0x278, 0x1d9}` | `0x3d` `REPAIR`, header 19 tall, plate `0x7f`-`0x10a`, face `0x25` |
| external list | `TitledPanel` | `{4, 0x1a, 0xe5, 0x105}` | `0x3e` `External`; `+0x65 = 0`, so no hatch and no plate |
| 6 group rows | `Panel` | `{0xc, i*0xc + 0x1c, 0xd5, i*0xc + 0x28}` | `0x4e`-`0x53` |
| 10 hardpoint rows | `Panel` | `{0xc, i*0xc + 0x72, 0xd5, i*0xc + 0x7e}` | built blank |
| internal list | `TitledPanel` | `{4, 0x10b, 0xe5, 0x1a3}` | `0x3f` `Internal`, same header |
| 9 component rows | `Panel` | `{0xc, i*0xc + 0x1c, 0xd5, i*0xc + 0x28}` | `0x54`-`0x5c` |
| mode label | `Text` | `{0xef, 0x1a, 0x179, 0x26}` | `0x40` `Mode:` |
| mode readout | `Button` | `{0xff, 0x2b, 0x168, 0x3e}` | `0x41` `Manual Repair` or `0x42` `Auto Repair` |
| salvage label | `Text` | `{0xef, 0x48, 0x179, 0x54}` | `0x43` `Salvage Available:` |
| salvage readout | `Button` | `{0xff, 0x59, 0x168, 0x6b}` | the pool, net of the build queue |
| item panel | `FramedPanel` | `{0xef, 0x77, 0x179, 0xf4}` | `0x4b` `Selected Item` |
| item cost | `Button` | `{0xf, 0x24, 0x78, 0x36}` in it | under `0x49` `Salvage Required:` |
| item condition | `Button` | `{0xf, 0x4b, 0x78, 0x5d}` in it | under `0x4c` `Condition:` |
| `REPAIR` | `Button` | `{0xf, 0x68, 0x78, 0x77}` in it | `0x4d` |
| total panel | `FramedPanel` | `{0xef, 0xfb, 0x179, 0x14f}` | `0x48` `Total` |
| total cost | `Button` | `{0xf, 0x24, 0x78, 0x36}` in it | under `0x49` again |
| `REPAIR ALL` | `Button` | `{0xf, 0x3f, 0x78, 0x4e}` in it | `0x4a` |
| scrap panel | `FramedPanel` | `{0xef, 0x156, 0x179, 0x181}` | `0x47` `Scrap Herc` |
| `SCRAP` | `Button` | `{0xf, 0x17, 0x78, 0x26}` in it | `0x46` |
| `CANCEL` | `Button` | `{0x112, 0x193, 0x156, 0x1a2}` | `0x44` |

Each row carries a click handler from the 25-thunk table `RepairHotspotHandlers` (`0048d1f8`), one per `(column, row)` pair, and what that pair means and which clicks are refused are [below](#the-arming-and-repair-hotspots).

The `Internal` panel has a click handler of its own, `Repair_OnInternalPanel` (`00434705`). While tab 3 is up and the selection is in column 0, it hides the bay's exploded external picture and shows its internals picture, the swap `Repair_SwapDiagram` makes when the selection crosses into column 1, and leaves the selection where it is ([Open](#open)).

**Five of the "buttons" are readouts.** A `Button` is constructed with a border colour and an enable flag, and the mode, salvage, item cost, condition and total cost boxes are all built disabled with border `0x13` and caption `0x17` where a live button takes `0x22` and `0x29`. They are boxes with a figure in them, and a click on one stops there and does nothing ([Which widget a click reaches](widgets.md#which-widget-a-click-reaches)). Four of the five also set the caption's `+0xc1`, so each clears its own rect before drawing and a refresh overwrites the last figure cleanly; the mode box, which changes only with the mode flag, does not.

The three `FramedPanel`s keep the constructor's `+0x55` of `0x25`, so their bodies carry a visible checkerboard — where the save screen flattens its own to `0x10`. The content panel keeps `+0x59` at the constructor's 1, so its body is filled rather than dithered and no backdrop shows through it.

**The condition readout's damage colour never reaches the screen.** `Repair_RefreshDetail` looks the band colour up with `Repair_DamageLevelColor` (0043da0f) and writes it into that widget's `+0xb5`, and then calls `ESMessage_SetString(widget, word, 2, 0x17, 1)` — which sets `+0xb5` from its fourth argument before painting, so the box is drawn in the same grey as every other readout. The write is dead. The *list rows* are colour-coded, because `Repair_FillRow` passes the band colour to `ESMessage_SetString` rather than writing it beside the call. Read from the decompile only; `ESMessage_SetString`'s argument order is corroborated by `ESMessage_Ctor` and by `Repair_Enter`, both of which pass a widget's own `+0x45` and `+0xb5` back in to mean "keep what is there".

### A row is four text columns

`ESQuad_AddColumns(panel, font, t0, …)` (0040a310) gives a row `Text` children at `+0x55`, `+0x59`, `+0x5d` and `+0x61`, each spanning from the previous one's right edge to its own, and the repair screen passes the same four edges for all 25 rows: `2`-`0x94` left-aligned, a zero-width second column that is never written, `0x94`-`0xab` built centred, and `0xab` to two inside the row's right edge, right-aligned. So a row reads as a name, a number and a percentage.

`Repair_FillRow(column, row)` fills one. The name column is the component's, the last column is `HercStatus_Get` (00411d06)'s reading of it formatted `"%d%%"`, and the colour that last column is drawn in is the damage band's ([below](#the-condition-readout)). A hardpoint row additionally puts the mount number, one-based, in the third column, left-aligned — the fill passes alignment 0 over the builder's centring — and takes its name from the fitted weapon — `estext.bin` `0x7e + id`, or `0x7d` `--Empty--` for an empty mount, whose percentage is replaced by the string at `0047465c` — two bytes, `20 00`, a single space, so the column reads blank rather than showing the 100 an empty slot's condition entry actually holds. **A row past the machine's mount capacity is blanked**, all three columns set to the string table's single space, rather than left showing the last machine's fitting.

### Which names a chassis shows

`Repair_SetComponentNames` holds two fifteen-entry tables of `estext.bin` indices — six group names then nine internal names — and picks the second **when the machine's chassis type is 8**, the Razor. It is a per-chassis substitution of all fifteen names at once, not a per-component list.

```
00474000   4e 4f 50 51 52 53  54 55 56 57 58 59 5a 5b 5c   walker
0047401e   4e 5d 5e 5f 60 61  62 63 56 57 58 59 5a 5b 5c   Razor
```

The Razor spends each of `0x5d`-`0x63` exactly once: the two torsos become nacelles, the chassis a fuselage, the legs wings, and the leg servos wing servos. Its cockpit and its last seven internals are the walker's. The condition arrays behind them are unchanged — a Razor's thirteen external facets group the same six ways.

It runs from one place, `Squad_SelectBay` (`0043d64d`)'s repair arm (`es2_xref.py` finds the one call), which a roster click on this tab and [the scrap dialog](build-and-armory.md#the-scrap-dialog)'s reselection reach. `Repair_Enter` calls `Squad_SelectBay` too, when the bay it opens on holds nothing it can work on, but tab 3's handler stores 3 in `DAT_0047581c` only after the entry, as tab 2's does ([Entering the weapons screen](#entering-the-weapons-screen)), so that call takes the arm of the tab being left. **Entering the tab does not set the names.** The lists keep the set the last bay change on this tab chose — until there is one, the walker set the builder constructs the rows with, `0x4e + row` and `0x54 + row` — so a machine selected on WEAPONS, BUILD or CREW is listed under the names of the chassis this tab last selected, a Razor under the walker's or a walker under the Razor's, until a roster click here selects another bay.

### The damage diagram

Both pictures are `Grid` widgets (`ESGrid_Ctor`, 0040b7e0): a filled panel with a `0x22` border, grid lines every 16 pixels in `+0x6e6` = `0x22` while `+0x6e5` is set, and thirty 56-byte part slots from `+0x55`. `ESGrid_SetPart` (0040b8cf) writes a slot: a position, a frame, blit flags at `+0x89`, and ten colour remap pairs — a source index at `+0x61` and a target at `+0x75`, the target defaulting to `0x10`. `ESGrid_Paint` (0040b97c) draws the panel and the lines, then blits the parts in slot order, and after each one fills the part's rect, `{x, y, x + width, y + height}`, through a lookup table that is the identity except for each pair whose target is not `0x10`. **A part's colour is chosen at paint time, and by rect rather than by mask**, so a recoloured part also recolours the matching pixels of any earlier part it overlaps.

`Repair_BuildDiagrams` (004140a9) fills both pictures for all eight bays:

- **`SquadSlotPictureWidgets[bay]` (`0048d4bc`), the exploded external picture.** These are the squad panel's eight pictures, built by `Squad_BuildRosterList` at `{5, 0x2b, 0xeb, 0x130}` and moved here to `{0x10, 0x2f}` and sized `0xd0` by `0x100`, with their grid lines on. The sizing writes the far corner as `origin + size - 1` ([The bay picture](squad-and-crew.md#the-bay-picture)), so they end at `{0xdf, 0x12e}`, a pixel short of the internals pictures' literal `{0xe0, 0x12f}`. Each `gam\rpr_*.dat` body record becomes the part in the slot its id names, from frame `+0x12` of `dba\rpr_<chassis>.dba` with the record's flags, remapping index `0xe`. Each fitted mount then adds the record `RepairLayout_FindWeaponPart` (`00413ccc`) finds in the weapon's group with id `slot + 6`, from `dba\rpr_wpns.dba`, remapping `0xf`; a weapon with no record for that socket draws nothing.
- **`RepairInternalsPictures[bay]` (`0048d118`), the internals picture.** One part in slot 0 from the chassis's single internals record, drawing its frame of `dba\<chassis>_int.dba` with flags 0. The Razor gets a second part in slot 1: frame 1 of the same bank at `(0x1d, 0xe)`. The decompiler shows that handle as a global of its own, `0046fe0c`; it is entry 8 of the bank cache at `0046fdec`, the one the Razor's first part was just loaded into.

The blit flags are 0 or 2 in every retail record, and 2 is the mirror: each left/right pair is one frame placed twice.

**The two sides disagree.** On all seven chassis with torsos — the Tomahawk's and Maverick's files have none — group 1 (`Left Torso`) sits on the viewer's left and group 4 (`Left Leg`) on the viewer's right, with 2 and 5 opposite them; the Razor's nacelles and wings split the same way. The `rpr_hots.dat` areas follow the layout records, so the colours and the clicks are consistent with the picture and with each other, and only the pairing of sides is wrong. Recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

`Repair_ColorDiagram` (`0041469a`) colours whichever picture the selection's column has up, and `Repair_RefreshDetail` calls it on every refresh:

| Picture | Slot | Remap | Target |
|---|---|---|---|
| external | each body record's id | `0xe` | the band colour ([below](#the-condition-readout)) of group `id`, or `id - 0x10` for an id past 15 |
| external | `6 + slot`, each mount below the capacity | `0xf` | the band colour of that hardpoint |
| internals | 0 | the nine indices at `0046fe80`, `1d 1c 18 19 17 1e 16 1f 1b` | the band colour of internal 0-8, in list order |

Ids past 15 are the Razor's: its twelve body records are two parts per group, 0-5 and 16-21.

**Which of the two is up follows the selection.** `Repair_SwapDiagram` shows the internals diagram when the selection moves into the internals list and the exploded picture when it moves back, so the picture always matches the list being worked in. With no bay selected the squad panel shows its empty picture, `DAT_0048d4dc`, instead: the same widget at the builder's rect with its grid lines off.

`Hotspots_BuildOverlay` (0043c1a0) lays the clickable areas over each bay's exploded picture: the chassis's six `gam\rpr_hots.dat` areas as handlers 0-5, and for each fitted mount a panel over the weapon part's own rect (`Repair_WeaponPartRect`, `00414418`) as handler `6 + slot`. Every one is a `Panel` with `+0x51` cleared, so none of them draws. They are children of the external picture, so while the internals picture is up there is nothing on the diagram to click. Where two overlap — a weapon part over a body area, on several chassis — the one built later answers ([Which widget a click reaches](widgets.md#which-widget-a-click-reaches)).

### Repairing and cancelling

**`REPAIR` (`Repair_OnRepair`, `00434b2d`) lifts the selection one level.** It takes `Repair_SelectionCost` off `CareerSalvage` — the whole pool, where the button is gated on the pool net of the build queue — and writes `Repair_TargetForLevel(Repair_DamageLevel(condition))` (`00413838`) into the selection through `HercStatus_Set(block, category, index, value)` (`00411cbd`), `HercStatus_Get`'s setter. The target is 100 from level 0 and the floor of the band above from any other, the same one the cost was quoted to ([`armory.md`](armory.md#what-one-repair-level-costs)). An external group is written by `HercStatus_SetGroup` (`00411c78`), which puts the value in every facet the group covers, so a group whose facets differ comes out uniform. The handler refills the row and the panels.

**`REPAIR ALL` (`Repair_OnRepairAll`, `00434c59`) rebuilds the machine.** It takes `Repair_HercCost(herc, 100)` off the pool and runs `Repair_Apply(herc, 100)` (`004113af`), which writes 100 into all thirteen facets, all nine internals and every mount slot below the capacity. A fitted mount at 0 is among them, so a destroyed weapon comes back at 100 for nothing: `Repair_HercCost` bills no mount at 0 ([`armory.md`](armory.md#repair-levels)). It then refills every row and the panels (`Repair_FillAllRows`, `00433caf`).

**`CANCEL` (`Repair_OnCancel`, `00434d73`) undoes, and does not leave the screen.** `Repair_Snapshot` (`004338f6`) copies `CareerSalvage` into `RepairSnapshotSalvage` (`0048d25c`) and the selected machine's 66-byte status block into `RepairSnapshotStatus` (`0048d260`), taken from the machine by `HercList_CopySelectedStatus` (`00434eb7`). It runs at the end of `Repair_Enter` and of the repair arm of `Squad_SelectBay`, so the snapshot is the pool and the machine as they stood when the tab was entered or the bay last changed. `CANCEL` writes the pool back outright and copies the status block over the selected machine, then refills every row and the panels. Each `REPAIR` and `REPAIR ALL` on the bay since then is undone. With no bay selected the snapshot copies the pool alone, and `CANCEL` still copies the old block — over the machine read through `00482abf` ([Open](#open)).

**The mode readout is the preferences' repair option**, `ShellOption_RepairMode` (`004824e4`, `prefs.cfg` option 44, [`../simulation/preferences.md`](../simulation/preferences.md)). `Repair_Enter` writes `0x42` `Auto Repair` for 0 and `0x41` `Manual Repair` for 1, and nothing for 2, so mode 2 keeps what the builder wrote, `Manual Repair`, or what an earlier entry did. None of the three buttons reads it; the debrief's repair pass, `Game_AutoRepairSquad` (`0040e804`), branches on it ([`armory.md`](armory.md#what-one-repair-level-costs)).

### What the buttons are gated on

`Repair_RefreshDetail` writes the same greying trio ([below](#the-condition-readout)) at three of the four:

| Button | Live when |
|---|---|
| `REPAIR` | the salvage pool, net of the build queue, covers lifting the selected component one level ([`armory.md`](armory.md#what-one-repair-level-costs)) |
| `REPAIR ALL` | it covers `Repair_HercCost(herc, 100)` — the whole machine to full, a different figure |
| `SCRAP` | there is a machine, the eight bays do not hold exactly one deployable machine (`Herc_HasSingleDeployable`, 00410add) — whichever bay that one is in, so a machine that cannot deploy is not scrappable beside a single one that can, and with none deployable every machine is — and its chassis is available — `(&DAT_00483b62)[type * 8]`, the `herc_inf.dat` `+0x0e` flag that `Herc_GrantUnlocks` (004118c5) sets and save block 7 carries ([`../formats/save-games.md`](../formats/save-games.md)) |
| `CANCEL` | always — no trio is written for it; what it does is [above](#repairing-and-cancelling) |

"Deployable" is `Herc_IsDeployable` (`00410a9d`): the bay is occupied, `+0x4a` is 100 so the machine is built, and `Herc_IsFlightworthy` (00411681) holds — both leg servos, the engine and life support all above 50. <!-- doc-lint: ok -->

`SCRAP`'s handler (`Repair_OnScrap`, `00434d15`) puts up [the scrap dialog](build-and-armory.md#the-scrap-dialog), the build screen's.

## The arming and repair hotspots

Both screens lay clickable rects over a picture of the selected machine. The geometry comes from `gam\arm_hots.dat` and `gam\rpr_hots.dat` ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md#gamarm_hotsdat-and-gamrpr_hotsdat--the-clickable-regions)), which carry position and nothing else: **an area's index within its chassis group is its identity**, because the builder passes `handlerTable[areaIndex]` as the panel's click handler. Each handler is a one-line thunk that calls a common function with its own index baked in.

`DAT_00482ae5` is the selected bay slot, 0-7 and `-1` for none, and both screens read the machine out of the eight-pointer array at `Hangar_BayRecords` (`00482ac3`).

**Arming** — `Arming_SelectHardpoint(hardpoint)` (0043dbb2), ten thunks, `Arming_OnHardpoint0`-`Arming_OnHardpoint9` (`0043e15f`-`0043e4c8`). `Hotspots_BuildOverlay(2)` lays one chromeless `Panel` per mount below the capacity over each bay's picture, the chassis's `arm_hots.dat` area of that index, so a higher mount answers over a lower one. The handler returns immediately when the click is on the hardpoint already selected, then reads the mount pointer at `herc + 0x50 + hardpoint*4` — null for an empty slot, otherwise its first `int16` is the fitted weapon id — and repaints. What it goes on to do is [above](#fitting-a-weapon).

**Repair** — `Repair_SelectHotspot(column, row)`, twenty-five thunks in one table, `RepairHotspotHandlers` (`0048d1f8`). Sixteen are column 0, the hotspots on the picture, `Repair_OnHotspot00`-`Repair_OnHotspot15` (`004340b5`-`004346a0`); nine are column 1, a list beside it, `Repair_OnInternalRow0`-`Repair_OnInternalRow8` (`004347a0`-`00434ac8`). The pair is resolved into a category and an index within it:

| | `Repair_HotspotCategory` (00433410) category | `Repair_HotspotIndex` (00433431) index | Count | What it selects |
|---|---|---|---|---|
| column 0, rows 0-5 | 0 | `row` | 6 | the external component **groups** |
| column 0, rows 6-15 | 2 | `row - 6` | 10 | the per-hardpoint conditions |
| column 1, rows 0-8 | 1 | `row` | 9 | the internal components |

**That category is the status block's own accessor mode.** `HercStatus_Get(block, mode, index)` takes exactly these three: mode 0 averages an external group, mode 1 addresses the nine internals and mode 2 the ten hardpoints ([`../formats/save-games.md`](../formats/save-games.md#the-66-byte-status-block)). Three independent things agree — the counts, the mode semantics, and `rpr_hots.dat` carrying exactly six areas per chassis — so the six hotspots on the picture are the six named groups `Cockpit`, `Left Torso`, `Right Torso`, `Chassis`, `Left Leg`, `Right Leg`, in that order.

The ten weapon rows are not in `rpr_hots.dat`: the same builder loop places them from the per-chassis `rpr_*.dat` geometry, starting at handler index 6. A row is refused — the selection does not move and nothing repaints — when its slot is past the machine's mount capacity at `+0x4c`, or when the slot is empty. So an unfitted hardpoint cannot be selected on the repair screen at all.

Selection repaints the outgoing entry with `(0x10, 0x27)` and the incoming with `(0x29, 0x29)`; `0x29` is the highlight colour throughout the shell, and `0x10` the resting one.

### The condition readout

`Repair_RefreshDetail` refreshes the detail panel from the selection, writing four text fields: the selected component's repair cost, its condition, the whole machine's rebuild cost (`Repair_HercCost(herc, 100)`), and the salvage available — which is `CareerSalvage` **minus** `Armory_QueuedTotal()`, so the figure the repair screen quotes is already net of what the build queue has committed. The three figures suffix `estext.bin` `0xc8` `kg`; the condition is a word.

The condition itself goes through two functions over two in-image tables. `Repair_DamageLevel(condition)` (0043d9cf) walks `004765c0` = `{89, 79, 59, 29, 0}` and returns the first index whose entry the condition is **strictly greater** than, or 5 if it is greater than none:

| Level | Condition | `estext.bin` word | Colour (`004765ca`) |
|---|---|---|---|
| 0 | 90-100 | `0x68` `Nominal` | 14 |
| 1 | 80-89 | `0x69` `Light` | 13 |
| 2 | 60-79 | `0x6a` `Moderate` | 12 |
| 3 | 30-59 | `0x6b` `Heavy` | 11 |
| 4 | 1-29 | `0x6c` `% Complete` | 10 |
| 5 | 0 | `0x6d` `Unassigned` | 39 |

**The bands are the repair ladder's.** `Repair_LevelForCondition`'s own table is `{90, 80, 60, 30, 1, 0}` tested with `<=` where this one is `{89, 79, 59, 29, 0}` tested with `<`, which partitions identically ([`armory.md`](armory.md#repair-levels)) — the word the screen prints *is* the repair level the machine would be worked to.

**The last two rows are a retail bug.** The word run in `estext.bin` is only four long: `0x67` is the label `Condition:`, `0x68`-`0x6b` are the four words, and `0x6c`/`0x6d` are `% Complete` and `Unassigned`, which belong to the build screen and the crew screen. Nothing in the table is a fifth or sixth damage word — there is no `Critical` or `Destroyed` anywhere in the 342 entries. The index really is the level plus a fixed base, read off the instruction stream rather than the decompiler: `Repair_DamageLevel` returns its loop counter in `EAX` (`xor eax,eax` … `inc eax`, with the comparison kept in `CX`/`DX`), and the caller does `mov esi,eax` / `add si,0x68` before the lookup. Recorded in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

**Greying a button is three writes, not one.** `+0xb5` and `+0x4d` go to `0x26` when it is disabled and to `0x29`/`0x22` when it is not, and `+0x49` follows. That pairing is what makes `+0x49` the enable flag rather than a style bit: it moves with the colours, on a test of whether the player can act. `Repair_RefreshDetail` writes the trio at three of the repair screen's four buttons, and what each is tested on is [above](#what-the-buttons-are-gated-on).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `warmingi.cpp` is a warning dialog | It is `w` + `arming` + `i`, the weapon-fitting screen, in the same naming pattern as `wsrvbayi.cpp`, `wcrewi.cpp` and `warmoryi.cpp` |
| The repair screen's detail figure and its `REPAIR ALL` figure are the same cost scaled | Both say `Salvage Required:` in kg and both come from the same unit-value tables, so a per-item share of the whole is the obvious reading. They use different functions with different targets: `Repair_HercCost` prices the machine to 100, and `Repair_LevelStepCost` (00413871) prices the selected component up to the floor of the next band only ([`armory.md`](armory.md#what-one-repair-level-costs)) |

## Open

- **Deferred:** what the build screen's `SCRAP` gate, `ScrapDialog_Show` and `Hangar_ScrapSelected` do with no bay selected, where each reads the third squad-member pointer at `00482abf` as [the bay's machine](build-and-armory.md#scrapping-and-building-are-gated-on-the-bay). A roster click selects one of the eight bays, but the crew tab can leave `-1` selected for the build tab to open on. The repair and weapons tabs reach `-1` when no bay holds a finished machine, or from the crew tab through [their entries' `Squad_SelectBay`](#entering-the-weapons-screen). There `Repair_RefreshDetail`'s gates and all three of [its handlers](#repairing-and-cancelling) read the same pointer, `CANCEL` copying a stale status block through it, and so do the weapons screen's [steppers](#fitting-a-weapon), its refusal test and its fit; `>` would divide by zero through a null pointer.
- **Open:** whether a retail click on the repair tab's `Internal` panel outside its nine rows reaches `Repair_OnInternalPanel` and swaps the pictures while an external row is selected ([The repair screen](#the-repair-screen)). The builder registers it as the panel's handler; no retail observation confirms the swap.
