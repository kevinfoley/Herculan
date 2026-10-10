# The build and armory screens

Tab 4, which buys chassis into the bays, the scrap dialogs it shares with the repair and armory screens, and tab 5, the weapon build queue. The economy behind them is in [`armory.md`](armory.md).

## The build screen

Tab 4, `BUILD`, the `Herc Construction` panel. Built once by `Build_BuildScreen` (`00445758`), entered by `Build_Enter` (`0044690d`) and hidden by `Build_Leave` (`0044694e`), the teardown dispatcher's build arm. Rects are parent-relative. The left of the canvas is [the squad panel](squad-and-crew.md#the-squad-panel), filled with the three-quarter [bay picture](squad-and-crew.md#the-bay-picture).

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| content panel | `TitledPanel` | `{0xf1, 0x2b, 0x27c, 0x162}` | `0xba` `Herc Construction`, header 19 tall, plate `0x7f`-`0x10a`, face `0x25`, dithered body in `0x25` |
| 9 blueprints | `Grid` | `{0xb2, 0x2d, 0x180, 0x12d}` | one per chassis type, border `0x27`, grid lines `0x18` ([below](#the-blueprints)) |
| chassis list | `FramedPanel` | `{0x16, 0x4b, 0x9a, 0xd1}` | face `0x10` |
| 9 rows | `Panel` | `{10, i*0xe + 4, 0x6e, i*0xe + 0x11}` in the list | `0x6e + i`, the chassis name, border `0x10` |
| stats box | `FramedPanel` | `{0x60, 0xe3, 0xab, 0x11f}` | face `0x10` |
| 2 headings | `Text` | `{0x16, 0x28, 0x9a, 0x34}`, `{0x16, 0x34, 0x9a, 0x40}` | `0xbc` `Select Herc`, `0xbd` `Type To Build`, centred, `0x1a` |
| 4 labels | `Text` | `{8, 0xe9, 0x5e, 0xf5}`, then 12 lower three times | `0xbb` `Mass`, `0xbe` `Speed`, `0xbf` `Hardpoints`, `0xc0` `Salvage Reqd`, left, `0x1a` |
| 4 figures | `Text` | `{5, 6, 0x45, 0x12}` in the stats box, then 12 lower three times | left, `0x17`, opaque |
| lower panel | `FramedPanel` | `{0xf1, 0x166, 0x27c, 0x1d9}` | face `0x10` |
| salvage label | `Text` | `{0x7d, 9, 0x10e, 0x15}` in the lower panel | `0xc1` `Salvage Available`, centred, `0x1a` |
| salvage box | `FramedPanel` | `{0x91, 0x1a, 0xfa, 0x2c}` in the lower panel | face `0x10`; its figure a `Text` at `{1, 3, 0x68, 0xf}`, centred, `0x17`, opaque |
| scrap box | `FramedPanel` | `{0x37, 0x38, 0xc1, 100}` in the lower panel | face `0x25`; `0xc2` `Scrap Herc` at `{8, 6, 0x82, 0x12}`, centred, `0x1a` |
| `SCRAP` | `Button` | `{0xf, 0x17, 0x78, 0x26}` in the scrap box | `0xc4`, border `0x22`, built disabled |
| build box | `FramedPanel` | `{0xcc, 0x38, 0x156, 100}` in the lower panel | face `0x25`; `0xc3` `Build Herc`, placed as the scrap box's title |
| `BUILD` | `Button` | `{0xf, 0x17, 0x78, 0x26}` in the build box | `0xc5`, border `0x22`, built disabled |

The builder writes `+0x4d = 0x27` over the constructor's border on every panel and blueprint. The content panel's `+0x59 = 0` and `+0x5d = 0x25` make its body a checkerboard over [the scope's black](screen-layout.md#the-palette), which is why the labels sit on a dithered ground while the boxes holding figures are flat. The lower panel is parented to the shell's top-level window rather than to the content panel, so the two are siblings and `Build_Enter` shows each.

**The rows are 14 tall on a 14-pixel pitch.** Each is a [four-column row](weapons-and-repair.md#a-row-is-four-text-columns) cut at `0x62`, `0x62` and `0x62`: the name centred from `2` to `0x62`, then three empty columns, two of them zero-wide. Nine rows means every chassis type has one, the Razor included.

The four figures are `herc_inf.dat`'s first four stats for the selected chassis, as `Herc_BuildScreenRefresh` (`00446cfa`) formats them ([`../formats/herc-catalogs.md`](../formats/herc-catalogs.md)). The salvage figure is `Build_RefreshSalvage` (`00446e71`)'s `"%ld %s"` of `(CareerSalvage - Armory_QueuedTotal()) / 1000` and `0xc6` `TONS` — the same net pool [the repair screen](weapons-and-repair.md#the-condition-readout) quotes in kilograms.

### Choosing a chassis

`Build_SelectChassis(chassis)` (`00446c3b`) is each row's handler, through nine thunks from `Build_OnChassisRow0` (`00446fbf`). It returns at once for the chassis already selected, `DAT_004786e4`; otherwise it sets the old row's name to `0x27` and its border to `0x10` and hides its blueprint, sets the new row's name and border to `0x29` and shows its blueprint, stores the chassis, and runs `Build_GateButtons` and `Herc_BuildScreenRefresh`. `DAT_004786e4` is `-1` in the image, and `es2_xref.py` finds no other store to it ([Open](#open)).

`Build_GateRows` (`00446835`) gates the rows on the chassis availability flag, `(&DAT_00483b62)[type * 8]` ([What the buttons are gated on](weapons-and-repair.md#what-the-buttons-are-gated-on)): a chassis without it has its row disabled and all four columns set to `0x10`, the background, so the list shows a gap where it is and a click on the gap does nothing. Every other row is enabled with all four columns at `0x27` — the selected row's name included.

`Build_Enter` runs `Build_GateRows`, `Herc_BuildScreenRefresh`, `Build_GateButtons` and `Build_FillBlueprints`, shows the content panel, the lower panel and chassis 0's blueprint, and calls `Build_SelectChassis(0)`. **So the screen always opens on chassis 0**, available or not. Coming back to the tab with chassis 0 still selected, the select returns at once, and the row keeps its lit border under the name `Build_GateRows` has just set back to `0x27`.

### Scrapping and building are gated on the bay

`Build_GateButtons` (`004469d4`) writes the [greying trio](weapons-and-repair.md#the-condition-readout) at both buttons from the bay the squad panel has selected, `DAT_00482ae5`:

| Bay | `SCRAP` | `BUILD` |
|---|---|---|
| empty | dead | live when the net pool is **more** than the selected chassis's price times 1000, compared unsigned |
| occupied | the repair screen's `SCRAP` test: the bays not holding exactly one deployable machine, and its chassis available | dead |

A machine is built into an empty bay, and only an occupied one can be scrapped. A pool exactly equal to the price leaves `BUILD` dead. With no bay selected the function reads the dword before the eight-pointer array at `Hangar_BayRecords` (`00482ac3`) as the bay's machine: `00482abf`, which is `+0x47` of the player structure at `00482a78` — the third of [the squad-member pointers](squad-and-crew.md#the-squad-panel) at `+0x3f` ([Open](weapons-and-repair.md#open)).

**This tab's arm of `Squad_SelectBay` takes any bay.** Unlike the repair and crew arms it refuses nothing, an empty bay and an unfinished machine included: it swaps the bay pictures, relights the two roster rows, stores `DAT_00482ae5`, refreshes the readout, and runs `Build_GateButtons` for any bay but `-1`.

**`BUILD` buys the selected chassis into the selected bay and pays for it at once.** Its handler (`Build_OnBuild`, `00446f3e`) calls `Hangar_BuySelected` (`0040e91c`) with `DAT_004786e4`. That calls `HercList_OrderIntoSelected` (`00410982`), which allocates a record, stores it in the slot `DAT_00482ae5` names without looking at what the slot holds, adds one to the hangar's count at `00482ae3` and runs `Herc_Order` on it; `0040e91c` then takes the price `Herc_Order` returns off `CareerSalvage`. Nothing is queued: the machine is in the bay at 0% built and the pool is down by its price ([`armory.md`](armory.md#buying-a-chassis--herc_order-00411019)). The handler refreshes the roster's names and crew column, the readout, the salvage figure and the gate, which leaves `BUILD` dead on the bay it has just filled.

`SCRAP`'s handler (`Build_OnScrap`, `00446ee0`) puts up [the scrap dialog](#the-scrap-dialog).

### The blueprints

`Build_FillBlueprints` (`0041579d`) fills the nine grids from the same records and banks as the repair screen's [exploded external picture](weapons-and-repair.md#the-damage-diagram): each `gam\rpr_*.dat` body record, from `dba\rpr_<chassis>.dba`, in the slot its id names with the record's flags. No weapon is drawn. Every part's remap pair is `0xe` to `0xe`, which `ESGrid_Paint` applies because the target is not `0x10` and which changes nothing, so the parts show in their own ink. `Build_Leave` frees the nine banks (`Build_FreeBitmapsAndGrids`, `00415928`) and `Build_Enter` loads them again.

## The scrap dialog

Both `SCRAP` buttons — the build screen's (`Build_OnScrap`, `00446ee0`) and the repair screen's (`Repair_OnScrap`, `00434d15`) — call `ScrapDialog_Show` (`00447711`), which quotes the machine in the selected bay, `DAT_00482ae5`, and puts up a dialog `ScrapDialog_Build` (`00447328`) built once at startup, after every tab screen. Rects are parent-relative.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| window | `Window` | the display root's own rect | |
| panel | `ESAlert` (`0040afe0`, `esalert.cpp`) | `{0xcb, 199, 0x1af, 0x149}` | `0xc9` `WARNING`, border `0x27`, header 20 tall, plate `0x3f`-`0xa6`, face `0x25`, dithered body in `0x10` |
| 2 frames | `FramedPanel` | `{5, 0x18, 0xdf, 0x60}`, `{10, 0x20, 0xd8, 0x58}` in the panel | border `0x15`, filled |
| caption | `Text` | `{0xc, 0x2e, 0xd6, 0x3b}` in the panel | `0xcc` `This herc will yield`, centred, `0x29` |
| figure | `Text` | `{0xc, 0x3c, 0xd6, 0x49}` in the panel | centred, `0x29` |
| `CANCEL` | `Button` | `{10, 0x6a, 0x6c, 0x79}` in the panel | `0xca`, border `0x22` |
| `ACCEPT` | `Button` | `{0x76, 0x6a, 0xd8, 0x79}` in the panel | `0xcb`, border `0x22` |

`ESAlert_Ctor` is `ESTitle_Ctor` with its own vtable, the mouse bits ORed into the event mask and a buffer the size of its rect; its paint is `ESTitle_Paint`. Both frames keep the class's `0x25` checkerboard.

`ScrapDialog_Show` writes `"%d %s"` of `Herc_ScrapValueTons` (`0041140f`) — `Herc_ScrapValue` over 1000 ([`armory.md`](armory.md#scrapping)) — and `0xcd` `tons of salvage.` into the figure, then calls `Alert_Show` (`0040b239`), which shows the panel's parent, the window, and then the panel. `ScrapDialog_Hide` (`00447795`) is `Alert_Hide` (`0040b258`), the same two in the other order.

**While the dialog is up, nothing beneath it takes a click.** The window is built by `ESWindow_Ctor` alone, so it is hidden from construction until `Alert_Show` shows it ([Showing and hiding a widget](widgets.md#showing-and-hiding-a-widget)). It is a child of the display root, as the top-level window every tab screen hangs from is, and built after it, so [the hit test](widgets.md#which-widget-a-click-reaches) tries it first, and it covers the display. A click outside the panel lands on the window, whose event mask has no mouse bit, and climbs to the display root, never reaching the screen beneath; one on the panel outside its two buttons is swallowed by the disabled panel.

**`CANCEL` (`ScrapDialog_OnCancel`, `00447c38`) only takes the dialog down**: its body is `ScrapDialog_Hide` and the handler's epilogue. The builder passes it as a bare address, so auto-analysis made no function of it; `ES2DefineFunctionAt` does.

**`ACCEPT` (`ScrapDialog_OnAccept`, `00447c96`) scraps.** It takes the dialog down and runs `Hangar_ScrapSelected` (`0040e757`) on the selected bay ([`armory.md`](armory.md#scrapping)). `Squad_RefreshAfterScrap` (`0043d17c`) then puts the empty-bay picture in the bay's slot (`Squad_SetEmptyBayPicture`, `00415506`), refreshes the readout and the roster's names and crew column, and the salvage figure of whichever tab is up. The build tab then regates its buttons; the repair tab calls `Squad_SelectBay(Herc_FirstBuiltBay())` and `Repair_RefreshDetail`, so it moves to the first bay holding a finished machine. Last it repaints the repair screen's external list and condition readout and the squad panel's condition text. **On the build tab the scrapped bay stays selected**, empty now, so `SCRAP` is dead and `BUILD` gated on the pool.

**The armory's `Scrap` has a twin**, built next by `WeaponScrapDialog_Build` (`004477ae`) with the same rects and colours and `0xce` `These weapons will yield` for its caption. `WeaponScrapDialog_Show` (`00447b97`) keeps the lit weapon's id in `DAT_0048d970` and writes `"%d %s"` of `Armory_ScrapValueTons(id)` ([`armory.md`](armory.md#scrapping)) and `0xcd` into the figure; its `CANCEL` (`00447d59`) only takes it down. Its `ACCEPT` (`WeaponScrapDialog_OnAccept`, `00447db7`) takes it down, sells the stock (`Armory_ScrapWeapons`, `0040e7b2`), runs `Armory_RefreshQueue` (`00412413`) over the build queue, and then `Armory_RefreshRows` and `Armory_RefreshReadout`. The row refresh puts every live row's four columns back to `0x27`, the lit row's included, and the readout then rewrites only the lit row's count held in `0x29` — so after a scrap the lit row reads as unlit but for its count, and stays the lit row, so a click on it queues.

## The armory screen

Tab 5, `ARMORY`, the weapon build queue ([`armory.md`](armory.md#the-weapon-build-queue--armory_-armorycpp-00411efd)). Built once by `Armory_BuildScreen` (`00447e34`, `warmoryi.cpp`), entered by `Armory_Enter` (`004494f7`) and hidden by `Armory_Leave` (`004495b6`), the teardown dispatcher's armory arm. Rects are parent-relative. The tab has no squad panel, and the content panel, parented to the shell's top-level window, spans the canvas.

| Widget | Class | Rect (in its parent) | Content |
|---|---|---|---|
| content panel | `TitledPanel` | `{0, 0x2d, 0x27f, 0x1d9}` | `0xcf` `ARMORY`, header 19 tall, plate `0xf1`-`0x18c`, face `0x25`, filled body |
| list | `TitledPanel` | `{0xd, 0x1a, 0x117, 0x1a3}` | `0xd0` `Armaments Inventory`, header 19 tall, face `0x24`, `+0x65 = 0` |
| 6 headings | `Text` | [below](#the-armory-rows), in the list | `0x29` |
| 26 rows | `Panel` | `{8, i*0xc + 0x3c, 0xf8, i*0xc + 0x48}` in the list | border `0x21`, `+0x51 = 0` |
| picture box | `HatchedDivider` | `{0x121, 0x1a, 0x272, 0xf3}` | border `0x15`, `+0x55 = 0x77`, `+0x59 = 0xf`, `+0x49 = 0` |
| 26 weapon pictures | image panel | each `gam\arm_weap.dat` record's corner in the box, sized to its frame | that frame of `dba\arm_weap.dba` |
| 5 info lines | `Text` | `{0, 0x8a, W, 0x96}`, `{0, 0x96, W, 0xa2}`, `{0, 0xa2, W, 0xae}`, `{0, 0xae, W, 0xbc}`, `{0, 0xbc, W, 200}` in the box | centred, `0x29`, opaque in `0xf` |
| readout panel | `FramedPanel` | `{0x121, 0xf8, 0x272, 0x14e}` | filled, border `0x15`, face `0xf` |
| 2 workspace labels | `Text` | `{0x4a, 9, 0xdc, 0x15}`, `{0x4a, 0x15, 0xdc, 0x21}` in it | `0xd3` `Workspace Available:`, `0xd4` `Workspace In Use:`, left, `0x29` |
| 2 workspace counts | `Text` | `{0xdc, 9, 0xeb, 0x15}`, `{0xdc, 0x15, 0xeb, 0x21}` in it | built as `1` and `4`, right, `0x29`, opaque |
| 2 salvage labels | `Text` | `{0x28, 0x2b, 0xa2, 0x37}`, `{0xa2, 0x2b, 0xfe, 0x37}` in it | `0xd5` `Salvage Available:` left, `0xd6` `Allocated:` right, `0x29` |
| salvage box | `Button` | `{0x31, 0x3d, 0x93, 0x4e}` in it | built blank, border `0x13`, disabled, caption opaque |
| allocated box | `Button` | `{0xaf, 0x3d, 0x111, 0x4e}` in it | the same |
| button panel | `FramedPanel` | `{0x121, 0x153, 0x272, 0x18c}` | filled, border `0x15`, face `0xf` |
| `Clear` | `Button` | `{0x31, 0x14, 0x93, 0x23}` in it | `0xd1`, border `0x22` |
| `Scrap` | `Button` | `{0xaf, 0x14, 0x111, 0x23}` in it | `0xd2`, border `0x22` |

`W` is the parent's own width, `+0x2d - +0x25`. The picture box is [the weapons screen's](weapons-and-repair.md#the-weapons-screen) with a different rect and band: black above row `0x77` and solid `0xf` below it, the pictures in the black and the info lines in the band. The builder reads `arm_weap.dat`'s weapon list into `0048d984`, indexed by `Arming_RowOfWeapon`, and reads past the file's guidance list without building a picture from it.

### The armory rows

The headings are `0xd7` `Num to` at `{10, 0x19, 0x4b, 0x25}` over `0xd8` `build` at `{10, 0x25, 0x4b, 0x31}`, `0xd9` `Type` at `{0x4c, 0x25, 0xaa, 0x31}` and `0xda` `avail.` at `{0xb4, 0x25, 0xdc, 0x31}`, all left-aligned, and `0xdb` `Salv.` at `{0xdc, 0x19, W - 9, 0x25}` over `0xdc` `req.` at `{0xdc, 0x25, W - 9, 0x31}`, right-aligned.

The rows list the first 26 weapon ids of the table at `004769b0` — [the weapons screen's](weapons-and-repair.md#the-inventory-rows) without `None` — 13 tall on a 12-pixel pitch, so, as in the repair lists, the lower row owns the shared line. Each is a [four-column row](weapons-and-repair.md#a-row-is-four-text-columns) cut at `0x32`, `0xb4` and `0xc9`: the queued count and the weapon's name, both left-aligned, then the count held and the price in tons, both right-aligned. The name is `weapons.bin`'s, through `weapons.dat` record `+0x10` rather than `estext.bin`, and the price is `"%d"` of `+0x14 / 1000`, written once by the builder ([`../formats/weapons-dat.md`](../formats/weapons-dat.md#weaponsdat-catalog-record-29-bytes)).

**A row draws no border.** The builder installs the list-row vtable at `00479bb0` on every row, as the other row builders do, and then clears `+0x51`. Its paint, `ESQuad_Paint` (`0040a600`), clears the interior to `0x10`, runs `ESRect_FillAndBorder` — which draws nothing with `+0x51` clear — and paints the four columns, so the lit row shows only in its text.

`Armory_RefreshRows` (`00449329`) runs on every entry. A weapon whose unlock flag `+0x16` is clear has its row disabled and its four columns set to `0x10`, the background, so the list shows a gap and keeps the builder's texts behind it. An unlocked weapon's row is enabled, and its first column set to `"[ %1d ]"` of `Armory_QueuedCount` (`00412642`) or to `"[   ]"` with none queued, its third to `"%d"` of the count held, and all four to `0x27`.

### Selecting and queueing

Row `i`'s handler is `Armory_OnRow`*i* (`Armory_OnRow00`, `0044a0b6`, through `Armory_OnRow25`, `0044ac0a`, `0x74` bytes apart), which the builder takes from `ArmoryRowHandlers` (`0048dab0`). Each calls `Armory_ClickRow` (`0044969f`) on the left release and `Armory_RightClickRow` (`004499de`) on the right. On any row but the lit one (`Armory_LitRow`, `00479174`), and on every row while weapons are built automatically, both select it: the old row goes back to `0x27` and its picture is hidden, and the new row is lit `0x29`, its picture shown and the five info lines filled from `wpn_info.bin` entries `Arming_RowOfWeapon(id) * 5` onward — `row * 5`, since the rows and that table share an order.

Whether weapons are built by hand is `prefs.cfg` option 45, `ShellOption_WeaponsBuildMode` ([`../simulation/preferences.md`](../simulation/preferences.md)). When they are, the lit row queues instead:

| Click on the lit row | Does |
|---|---|
| left | with a queue slot free and `Armory_QueuedTotal() + price` **less than** the pool, `Armory_Enqueue` of the weapon, the first column rewritten in `0x29`, and the readout refreshed |
| right | one unit fewer: `Armory_DequeueLit` (`0044963c`) takes every queued unit of the weapon off, and `Armory_ClickRow` then queues the count less one back |

A pool exactly covering the queue plus the price refuses the unit, as the build screen's `BUILD` refuses [a pool equal to the chassis's price](#scrapping-and-building-are-gated-on-the-bay).

`Armory_Enter` writes the [greying trio](weapons-and-repair.md#the-condition-readout) at `Clear` from the build mode, so `Clear` is live only while weapons are built by hand, then runs `Armory_RefreshRows` and `Armory_RefreshReadout`, shows the content panel and the list, and calls `Armory_ClickRow(0)`. `Armory_Leave` puts the lit row back to `-1`, so **the screen always opens on the first row, `Autocannon 20mm`**, and that call selects rather than queues.

`Clear` (`00449ef4`) is `Armory_DequeueLit`: every queued unit of the lit weapon comes off, the first column goes back to `"[   ]"` in `0x29`, and the readout is refreshed. `Scrap` (`Armory_OnScrap`, `00449e78`) puts up [the scrap dialog](#the-scrap-dialog)'s twin on the lit weapon, when a row is lit, and its `ACCEPT` sells the armory's whole stock of it.

### The armory readout

`Armory_RefreshReadout` (`00449cab`) writes all four figures centred in `0x29`: the salvage box `"%ld %s"` of `CareerSalvage - Armory_QueuedTotal()` and `0xc8` `kg`, the allocated box `"%d %s"` of `Armory_QueuedTotal()` and `kg`, and the two workspace counts, the queue's free slots and five less them. It then rewrites the lit row's count held in `0x29`. The two boxes are built as [the repair screen's readouts](weapons-and-repair.md#the-repair-screen) are — disabled, border `0x13` — but their captions are drawn in `0x29` rather than `0x17`.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `00445758` is the mission tab's builder | It is the largest function after `wmissini.cpp`'s assert-string anchor, so the file attribution points at the mission tab. Every widget it builds is one that `Build_Enter` (`0044690d`), tab 4's entry, shows and the teardown's build arm hides, and its captions are the `Herc Construction` run `0xba`-`0xc5`. `known_symbols_vshell.json` names it `Build_BuildScreen`; the mission tab's builder is `Mission_BuildScreen` (`00442534`) |

## Open

- **Deferred:** no other store to the build screen's chassis `DAT_004786e4` found: `es2_xref.py` finds `Build_SelectChassis`'s at `00446ce6` and twelve reads.
