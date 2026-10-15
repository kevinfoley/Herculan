# DBSIM.EXE — panel.cpp, the cockpit's instruments

**ES1's cockpit is ES2's cockpit object: it loads the console fonts and cursors, reads the chassis' layout file, builds the console instruments and the two message boxes from it, keeps the squadmates' list for the orders the player sends, and repaints the instruments each frame.** ES1's has nine weapon rows and a separate pod row, a cockpit clock built from the block ES2's ambience panel would read, a `.TTM` script run over the panel, and no coordinate shifts; its frame runs none of ES2's power-up steps. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's, described in ES2's [cockpit object](../../retail/simulation/cockpit-hud-widgets.md#the-cockpit-object). Most functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the module starts and ends

`panel.cpp`'s code is `0002acdc`–`0002e443` and its initialized data `00092cae`–`0009306d`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): the four functions `0002acdc`, `0002ad04`, `0002ad43` and `0002ad4c`, each identical after normalization to `mechview.cpp`'s ([where `mechview.cpp` starts](cockpit-views.md#where-the-module-starts-and-ends)), and the tables `00092cae` and `00092ce2`. `Gau_Load` (`0002b5c4`) asserts `panel.cpp` (line `0x192`, the source path at `00092ef2`).

The code ends where the next run starts, at `0002e444`, with its tables `0009306e` and `000930a2`. Before it stands the module's static initializer, `0002e404`, the static constructor table's entry `000adc2c`: frameless and with no Ghidra function, it stores 4 at `000b0db0`, 500 at `000b0dc8`, −500 at `000b0dd0` and 0 at `000b0da8`, `000b0db8`, `000b0dc4`, `000b0dcc` and both dwords of `Cockpit_ZeroOffset` (`000b0dd4`), and its `RET` at `0002e442` is followed by one zero byte ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). A scan of `0002acdc`–`0002e470` for a return followed by zero bytes up to a 4-byte boundary finds that one alone, and `000adc2c` is the one entry of the static constructor and exit tables that lands in the range. This page covers `0002acdc`–`0002d097`, through `Cockpit_DrawFrame` ([Open](#open)).

The data holds, after the run's tables, the module's globals from `CockpitViewInstance` (`00092d1c`) to the byte `00092d7d`; the font and cursor names and their tables `00092dc4` and `00092e24`; the strings `strings0`, `str` and `pbuttons`; the module's copies of `GLRectangleRegion`'s three vtables (`00092e7e`, `00092ea6`, `00092ece`), which `Widget_DrawToCockpit` stores; the source path; the ticker's vtable `00092f0a`; the string `ttm\`; and the tables `0009301e` and `00093046`, which `0002d970` stores.

The functions after the run follow ES2's in order, from `Cockpit_CreateCanvasContext` (ES2's `00430ea0`) to `Cockpit_DrawFrame` (ES2's `CockpitView_PerFrameUpdate`, `004327ac`), with `Cockpit_LoadPanel` where ES2's `Gau_BuildCockpitWidgets` is and `CockpitClock_Create` where ES2's `Gau_PanelAmbienceWidget` is; ES1's adds the region destructors' copies and the two message-port slots.

## The cockpit object

**The object is ES2's ([the cockpit object](../../retail/simulation/cockpit-hud-widgets.md#the-cockpit-object)) with one weapon row fewer, a pod row, a clock and no power-up fields; it is `0x23d` bytes.**

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00` | the same | the root widget, `00043cac`(0, the `.GAU` record), which `00044310` is then given |
| `+0x04` | the same | the canvas context |
| `+0x08` | the same | the widget list, its count at `+0x6c` |
| `+0x70` | the same | nine weapon-row gauges, their count at `+0x94`; ES2's ten, count `+0x98` |
| `+0x1d0`, `+0x1d4`, `+0x1dc` | `+0x1d4`, `+0x1d8`, `+0x1e0` | the dwords and the byte `Gau_WeaponButtonsWidget` sets; `+0x1d4` also takes the first weapon-row gauge registered while it is 0 |
| `+0x1d8` | none | the pod row's gauge |
| `+0x1dd`–`+0x1f5` | `+0x1e1`–`+0x1f9` | the seven instruments ([below](#building-it)) |
| `+0x1f9` | none | the cockpit clock ([the cockpit clock](../formats/ttm-scripts.md#the-cockpit-clock)) |
| `+0x1fd`, `+0x1ff` | `+0x201`, `+0x203` | the chassis type's `int16` `+0x6c` (the type record is the machine's `+0x188`), the machine |
| `+0x203`, `+0x207` | `+0x207`, `+0x20b` | the pilot channel, from `.GAU` 1668, and the ticker, from 1684 |
| `+0x20b` | `+0x20f` | 1 while the widgets are off |
| `+0x20c` | `+0x210` | a dword `Cockpit_DrawFrame` hands to `0002d624` and clears when that returns 0, where ES2's clears the target selection when `TargetSelect_CanTarget` refuses it |
| `+0x210` | `+0x214` | 6 bytes: `LiveObjectList` (`000bc416`) and an `int16` 0 |
| `+0x222` | `+0x226` | the active cursor slot |
| `+0x232`, `+0x236` | `+0x236`, `+0x23a` | the forward view's cursor slot and the other views' ([ES2's slots](../../retail/simulation/cockpit-input.md#9-cursor-rendering)) |
| `+0x23a` | `+0x23e` | set by `Cockpit_DrawFrame` once `0002d970` has run |
| `+0x23b` | `+0x23f` | the `int16` row of the pod gauge, −1 for none |

### Building it

`CockpitViewManager_LoadViews` builds the cockpit with `CockpitView_Ctor`(0, the manager's machine `+0x25`), stores it at the manager's `+0x1b` and calls `Cockpit_LoadPanel` with it and the chassis name ([the view manager](cockpit-views.md#the-view-manager)).

1. **`CockpitView_Ctor` (`0002ae5c`)**, (cockpit, machine): allocates `0x23d` bytes when given none, returning 0 when that fails; stores `LiveObjectList` and an `int16` 0 at `+0x210`, the machine at `+0x1ff` and its type record's `int16` `+0x6c` at `+0x1fd`; runs `ColorSchemePanels_LoadAll` (by its padding entry `0002aecb`) and returns the cockpit. ES2's allocates nothing itself and ends by allocating the arrays at `004d0490` and `004d0494`, which ES1's has none of.
2. **`ColorSchemePanels_LoadAll` (`0002aecc`)**, (cockpit): passes `ResourcePath_BuildFolderName`(`str`, `strings0`) to `0002fe84`; the first time, loads ten fonts, `dfn\cpblue`, `cpgreen`, `cpred`, `cpylw`, `cpon`, `cppress`, `cpoff`, `cpgrey`, `cpblack` and `cporange`, into `ColorSchemePanels` (`00092d40`) through `Panel_LoadWrapper` (`0002adf4`); the first time, loads the seven cursors from `dci\` into `Cockpit_CursorImages` (`00092d20`) through `Cursor_LoadWrapper` (`0002ae28`), in ES2's order, and puts `CURSOR` in both slots `+0x232` and `+0x236`; zeroes the three `g_SquadmateMachines` (`000bcc16`); the first time, loads `dba\pbuttons` into `CockpitPButtonsBank` (`00092d68`) with `ClassItem_LoadFromFile`; calls `memset`(cockpit + `0x8f4`, `0x239`, 0), which, Symantec's `memset` taking (destination, value, count), fills nothing; stores the cockpit in `CockpitViewInstance` when that is 0; and clears `+0x20b`, points `+0x222` at the slot `+0x232` and sets `+0x23b` to −1. ES2's loads 18 fonts, from `hfn\` in panel mode 3, puts `PCURSOR` in the forward slot, leaves the active slot null, loads no `pbuttons` and clears its power-up fields. Each wrapper, (path), opens a `VolRStream` on the path and returns `ClassItem_LoadResource`'s item, as ES2's do; `es2_xref.py` also finds `Cursor_LoadWrapper` called from `00045f00`.
3. **`Cockpit_LoadPanel` (`0002c9d8`)**, (cockpit, name), in place of ES2's `Gau_BuildCockpitWidgets`: loads `gau\<name>` with `Gau_Load` into `CockpitGauRecord` (`00092d3c`) ([the layout](../formats/gau-cockpit-layout.md)); makes the canvas context over the record's first 16 bytes with `Cockpit_CreateCanvasContext` at `+4`; makes the root widget at `+0` with `00043cac`(0, the record) and passes it to `00044310`; makes the `.TTM` manager at `CockpitTtmManager` (`00092d70`) ([the simulator's scripts](../formats/ttm-scripts.md#the-simulator)); makes the pilot channel with `0002f3d8`(0, record + 1668) at `+0x203`, and the ticker, a `0x469`-byte object from `0002eb80`(object, record + 1684) given the vtable `00092f0a` at `+0x464` and 0 at `+0x468`, at `+0x207`; builds the seven instruments and the clock; and ends with `Ttm_RunAll`. Where ES2's calls `Widget_ClearPressed` and `SystemButtons_Ctor` on the root, ES1's calls `00044310` alone; it shifts nothing, and the weapon rows are built elsewhere ([below](#the-weapon-rows)); the [flyer branch](../../retail/simulation/cockpit-hud-widgets.md#power-up-sequence) has no counterpart.

**`Cockpit_CreateCanvasContext` (`0002ad78`)**, (rect): gets the context from `00069a84`(0) and stores it at `CockpitCanvasContext` (`000b0da4`), then sets ES2's fields: clip mode `+0x208` = 1, translation `+0x20c` = 1, the rect at `+0x210`–`+0x21c`, the byte `+0x230` and the dword `+0x231` 0 and `+0x235` = 200. It returns the context. ES2's allocates a `0x239`-byte `GLViewport` there ([Open](#open)).

Each instrument's wrapper, (cockpit, record), constructs the instrument with (0, cockpit, the record's block), appends it to the widget list and returns it, with no coordinate shift:

| Field | Wrapper | Block | Constructor | ES2's |
|---|---|---|---|---|
| `+0x1dd` | `Gau_WeaponButtonsWidget` (`0002ccd8`) | 452 | `00037bd0`, then `00037ce4`(it, 2); sets `+0x1dc` to 1, `+0x1d0` to 0 and `+0x1d4` to the dword at `+0x168` | `00432340`, block 468, `+0x1e1` |
| `+0x1e1` | `Gau_EnergyMeterWidget` (`0002cd3c`) | 548 | `00039818` ([its clip file](../formats/hd-ed-clip-regions.md#which-files)) | `004323e0`, `+0x1e5` |
| `+0x1e5` | `Gau_ShieldDisplayWidget` (`0002cd74`) | 616 | `00038f34` | `00432454`, `+0x1e9` |
| `+0x1e9` | `Gau_MfdPanelWidget` (`0002cdac`) | 728 | `00039c64`, then `0003a9ec`(it, 0) | `004324c8`, `+0x1ed`, mode 3 |
| `+0x1ed` | `Gau_ThrottleWidget` (`0002cdf0`) | 1000 | `0003b0d4` | `0043254c`, `+0x1f1` |
| `+0x1f1` | `Gau_RovingGunsightWrapper` (`0002ce28`) | 1104 | `00034aec` | `004325c0`, block 1088, `+0x1f5` |
| `+0x1f5` | `Gau_PilotRosterWidget` (`0002ce60`) | 1212 | `0003c258` | `00432634`, `+0x1f9` |
| `+0x1f9` | `CockpitClock_Create` (`0002ce98`) | 1604 | [the cockpit clock](../formats/ttm-scripts.md#the-cockpit-clock) | `Gau_PanelAmbienceWidget` (`004326a8`), to which `es2_xref.py` finds no call |

The names are ES2's, by the block, the order of the calls and the field; the constructors are unread ([Open](#open)). The instrument at `+0x1f1` is drawn outside the canvas context as ES2's gunsight complex is ([drawing it](#drawing-it)), and `Cockpit_LoadSquadmatePilots` hands the squadmates' pilots to the one at `+0x1f5` as ES2's does to its Heads-Down Display.

### The weapon rows

`es2_xref.py` finds one call of each factory, from `0000a97c` (energy), `0000ab20` (ammunition) and `0000abec` (pod). Each passes `CockpitViewInstance`, the mount and the row, the signed byte `+7` of the record at the mount's `+0x18`, the first two also `0000aa9c`(mount), and stores the gauge at the mount's `+0x77`; `0000a97c` then copies the block `000372d8`(gauge) returns, puts the mount's `int16` `+0x7b` in its second dword and hands the copy to slot `+0x14` of the gauge's vtable (at its `+0x76`), as ES2's energy mounts push the power level ([weapon hardpoint rows](../../retail/simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows)).

- **`CockpitView_CreateEnergyWeaponGauge` (`0002cbbc`)** and **`CockpitView_CreateAmmoWeaponGauge` (`0002cc18`)**, (cockpit, mount, value, row): copy the row's rect, record + 20 + 16 × row, to the scratch rect at 244 or 164, construct the gauge with `0003700c` or `000373a4`(0, mount, value, row, cockpit, scratch), register it with `CockpitView_RegisterWeaponGauge`(cockpit, row, gauge) and return it. ES2's (`00432074`, `00432124`) use 260 and 180.
- **`CockpitView_CreatePodGauge` (`0002cc74`)**, (cockpit, mount, row): copies the row's rect to 340, constructs the gauge with `00037810`(0, mount, row, cockpit, scratch), adds 1 to the widget list's count without storing the gauge in the list, and stores it at `+0x1d8` and the row at `+0x23b`; it returns the gauge. ES2's (`004321d4`) picks one of three pod classes and registers the gauge in its row as the others do.
- **`CockpitView_RegisterWeaponGauge` (`0002cb54`)**, (cockpit, row, gauge): ES2's (`00432018`) over nine slots: a row of −1 takes the first empty one; the gauge goes to `+0x1d4` while that is 0, onto the widget list and into its row's slot at `+0x70`, and the list count and `+0x94` each go up by 1. With −1 and all nine taken the row stays −1, and the store to `+0x70` − 4 lands on the list count `+0x6c`.

### The squadmates

- **`Cockpit_LoadSquadmatePilots` (`0002b308`)**, (cockpit), ES2's (`00431530`) with ES1's offsets: the player's machine is the first one `Pool_Prev` walks to in `g_MechPool` whose byte `+0x9d` is set (ES2's `+0xa3`); its group (`+0x3f`, ES2's `+0x45`) goes to `CockpitSquadGroup` (`00092d6c`), and up to three of the group's members (array `+0xe`, `int16` count `+0x12`, stopping at a null) other than `+0x1ff` into `g_SquadmateMachines`; each member's `int16` `+0x232` (ES2's `+0x29c`) goes to `0003dab4`(the instrument `+0x1f5`, pilot, slot). It calls nothing for the status roster, which ES2's hands its MFD. `es2_xref.py` finds one call, from `Sim_StartMission`.
- **`Squad_SendOrderToSlot` (`0002b3e8`)**, (cockpit, slot, order): ES2's (`00431610`): for a squadmate in the slot that is not the cockpit's machine and whose byte `+0x93` is clear (ES2's `+0x99`), stores the machine at the order's `+2` and calls slot `+0x2c` of the vtable at the squadmate's `+2` with (squadmate, 0, order); either way calls `0002f370`(the pilot channel `+0x203`, `0x22`, squadmate), ES2's `MessagePort_Withdraw`. `es2_xref.py` finds one call, from `0003d48c`.
- **`Squad_BroadcastOrderFromCockpit` (`0002b448`)**, (cockpit, order): ES2's (`0043166c`): stores the machine at the order's `+2` and returns `Squad_BroadcastOrder`(`CockpitSquadGroup`, order). `es2_xref.py` finds one call, from `0003adc8`.
- **`Squad_CallsignOf` (`0002b46c`)**, (machine): ES2's (`00431690`): entry *i* of `SquadCallsignStrings` (`000bd324`) for the squadmate in slot *i*, else 0. `es2_xref.py` finds no reference to it ([Open](#open)).

## Drawing it

**`Cockpit_PushCanvasContext` (`0002b00c`) and `Cockpit_PopRenderContext` (`0002b03c`)** are ES2's: the push adds 1 to `CockpitContextStackIndex` (`00092d7d`, −1 in the image), saves the installed render context (`000bdbda`) at that index of `CockpitContextStack` (`000b0df4`) and installs the canvas context `+4`; the pop reinstalls the saved one and takes 1 from the index.

**`Cockpit_DrawFrame` (`0002cf28`)**, (cockpit), once a frame from `Sim_RenderFrame` ([the cockpit's scripts](../formats/ttm-scripts.md#the-simulator)), does nothing while the byte `00092d7c` is set. Otherwise:

1. the first time (`+0x23a` clear) it calls `0002d970` and sets `+0x23a`;
2. it ticks the clock (slot `+0xc` of `+0x1f9`) and calls `0002e1d8`;
3. with the widgets off it calls slot `+0xc` of the instrument `+0x1f5` and returns;
4. while the view manager has no view change staged (its `+0x18`), slot `+0x14` of the pilot channel's vtable (at its `+0x464`);
5. between the push and the pop, slot `+0xc` of each weapon row that is set, of the pod row when set, and of `+0x1dd`, `+0x1e1`, `+0x1e5`, `+0x1ed`, `+0x1e9` and `+0x1f5`;
6. `Cockpit_RunTtmScripts`, slot `+0x10` of the ticker's vtable `00092f0a`, and `0002de48`;
7. with `+0x20c` set, `0002d624`(cockpit, `+0x20c`), clearing `+0x20c` when it returns 0.

ES1's widgets hold their vtable at their `+0x76`, where ES2's hold theirs at `+0`, and ES1's tables start their slots at `+4` ([vtables](../formats/compiler.md#vtables)); ES2's pass calls slot `+8`. ES2's `CockpitView_PerFrameUpdate` (`004327ac`) runs the power-up, the hit shake and the step kick where ES1's ticks the clock and calls `0002e1d8`, paints `+0x1f9`, its Heads-Down Display, with the widgets off, and ends with the edge cursor, the press flashes and the nav marker; it runs no script ([ES2's](../../retail/simulation/cockpit-hud-widgets.md#drawing-it)). Both leave the gunsight out: ES1's `+0x1f1`, ES2's `+0x1f5`.

**`CockpitView_InvalidateWidgets` (`0002b4a0`)**, (cockpit), which `CockpitView_SetView` runs ([switching views](cockpit-views.md#cockpitview_setview-00022558)), unless the widgets are off calls slot `+8` of each set widget: the nine rows, then the pod row `+0x1d8`, `+0x1dd`, `+0x1e1`, `+0x1e5`, `+0x1ed`, `+0x1e9` and `+0x1f5` between the push and the pop, then `+0x1f1` and the clock `+0x1f9`, and last runs the cockpit's `.TTM` scripts (`Cockpit_RunTtmScripts`). ES2's (`004316c0`) calls slot `+4` of its ten rows and six of its instruments between the push and the pop and of the gunsight `+0x1f5` after it, and runs no script.

**`CockpitView_DisableWidgets` (`0002b2d0`) and `CockpitView_EnableWidgets` (`0002b2ec`)** set and clear `+0x20b`, ES2's (`00431500`, `00431518`) on `+0x20f`.

**`CockpitWidgets_TranslateForView` (`0002ced0`)**, (cockpit, delta), which the view changes and the shake call ([the view shake](cockpit-views.md#the-view-shake)): calls `0004433c` with the root widget and the delta's two values, subtracts the delta from the canvas context's `+0x220` and `+0x224`, and calls `0002db1c`(cockpit, delta) when `+0x23a` is set. ES2's (`0043271c`) tests `+0x23e` and also offsets the widgets of its list at `+0x246`.

**`CockpitView_RectCornerVisible` (`0002b214`)**, (rect): 0 while the widgets are off; otherwise 1 when the corner (*x0*, *y0*) or (*x1*, *y1*) lies in the 320 × 200 box, bounds inclusive, whose origin is the canvas context's `+0x210` + `+0x220` and `+0x214` + `+0x224`. ES2's (`00431410`) tests `+0x20f`, takes the video-mode offsets `004d25da`/`004d25de` from the rect and sizes the box by the 3D viewport.

**`Widget_DrawToCockpit` (`0002b058`)**, (offset, rect, which), copies a widget's rect between the display's two pages, ES2's [paged path](../../retail/simulation/cockpit-views.md#the--b-paged-path) on ES1's only path: it returns 0 unless `CockpitView_RectCornerVisible` passes the rect and the view manager's `+0x18` is clear; *which* = 1 copies from the display's `+0x8c` to `+0x88` and returns 0 while `000b5bca` is set, any other value from `+0x88` to `+0x8c`. Inside the push and the pop it makes the source page the display's `+0x38` and the destination its `+0x3c`, each through `g_RasterRoutines` slots 16 and 17 as well, copies the rect as a type-0 region with `Region_CopyBetweenPages` (`00066914`) at the offset, `Cockpit_ZeroOffset` when that is null, between slots 31 and 30, puts the draw page back and returns 1. ES2's (`0043122c`) acts only on the paged path, tests the manager's `+0x1c`, widens the rect's far corner by a pixel in the scaled modes and takes the video-mode offsets `004d25da`/`004d25de` from it.

### The message boxes' vtable

The ticker's table `00092f0a`, which `Cockpit_LoadPanel` stores over the object `0002eb80` builds, holds two functions of this object file: slot `+0x14`, **`MessagePort_UnpaintNoOpPanelCopy` (`0002cb41`)**, three `NOP`s and `RET 4`, ES2's `MessagePort_UnpaintNoOp` (`0043757e`); and slot `+0x1c`, **`MessagePort_GetRectPanelCopy` (`0002cb47`)**, a `NOP` and then the port plus `0x43f` returned with `RET 4`, ES2's `MessagePort_GetRect` (`00437583`), which returns the port plus `0x4a7`. The pilot channel's table `000931b6` holds at its `+0x1c` `0002ed88`, the same code past `panel.cpp`'s end. The ticker's other slots, `0002f96c`, `0002f934`, `0002fb18`, `0002fab8` and `0002fa00`, lie past it too.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorPanelCopy`, `ConfigPart_DtorPanelCopy`, `ConfigPart_GetClassTagPanelCopy`, `TSPartBase_DtorPanelCopy` | `0002acdc`, `0002ad04`, `0002ad43`, `0002ad4c` | the run ([module copies](../formats/compiler.md#vtables)); slots `+0x1c` and `+4` of `00092cae` and `+0x1c` of `00092ce2` (`0002ad4c` by its padding `0002ad49`); `es2_xref.py` finds no reference to `0002acdc` |
| `Cockpit_CreateCanvasContext`, `Panel_LoadWrapper`, `Cursor_LoadWrapper` | `0002ad78`, `0002adf4`, `0002ae28` | `00430ea0`, `00430f58`, `00430fb0` |
| `CockpitView_Ctor`, `ColorSchemePanels_LoadAll` | `0002ae5c`, `0002aecc` | `00431008`, `00431098` |
| `Cockpit_PushCanvasContext`, `Cockpit_PopRenderContext`, `Widget_DrawToCockpit` | `0002b00c`, `0002b03c`, `0002b058` | `004311e0`, `00431210`, `0043122c` |
| `GLBase_DtorPanelCopy`, `GLRegion_DtorPanelCopy`, `GLRectangleRegion_DtorPanelCopy` | `0002b198`, `0002b1bc`, `0002b1e4` | slot `+0x1c` of `00092e7e`, `00092ea6` and `00092ece` (the first two by their padding `0002b196`, `0002b1ba`); the code of `mechview.cpp`'s copies ([its functions](cockpit-views.md#functions)) |
| `CockpitView_RectCornerVisible`, `CockpitView_DisableWidgets`, `CockpitView_EnableWidgets` | `0002b214`, `0002b2d0`, `0002b2ec` | `00431410`, `00431500`, `00431518` |
| `Cockpit_LoadSquadmatePilots`, `Squad_SendOrderToSlot`, `Squad_BroadcastOrderFromCockpit`, `Squad_CallsignOf` | `0002b308`, `0002b3e8`, `0002b448`, `0002b46c` | `00431530`, `00431610`, `0043166c`, `00431690` |
| `CockpitView_InvalidateWidgets`, `Gau_Load` | `0002b4a0`, `0002b5c4` | `004316c0`, `00431778` |
| `GauWeaponRect_ElementCtor`, `GauRect_ElementCtor` | `0002c93c`, `0002c994` | `GauWeaponRect_ElementCtor` (`00434ab9`) and `RectI_Ctor` (`0048dd23`) |
| `Cockpit_LoadPanel`, `MessagePort_UnpaintNoOpPanelCopy`, `MessagePort_GetRectPanelCopy` | `0002c9d8`, `0002cb41`, `0002cb47` | `Gau_BuildCockpitWidgets` (`00431bf8`), `0043757e`, `00437583` |
| `CockpitView_RegisterWeaponGauge`, `CockpitView_CreateEnergyWeaponGauge`, `CockpitView_CreateAmmoWeaponGauge`, `CockpitView_CreatePodGauge` | `0002cb54`, `0002cbbc`, `0002cc18`, `0002cc74` | `00432018`, `00432074`, `00432124`, `004321d4` |
| `Gau_WeaponButtonsWidget`, `Gau_EnergyMeterWidget`, `Gau_ShieldDisplayWidget`, `Gau_MfdPanelWidget` | `0002ccd8`, `0002cd3c`, `0002cd74`, `0002cdac` | `00432340`, `004323e0`, `00432454`, `004324c8` |
| `Gau_ThrottleWidget`, `Gau_RovingGunsightWrapper`, `Gau_PilotRosterWidget`, `CockpitClock_Create` | `0002cdf0`, `0002ce28`, `0002ce60`, `0002ce98` | `0043254c`, `004325c0`, `00432634`, `Gau_PanelAmbienceWidget` (`004326a8`) |
| `CockpitWidgets_TranslateForView`, `Cockpit_DrawFrame` | `0002ced0`, `0002cf28` | `0043271c`, `CockpitView_PerFrameUpdate` (`004327ac`) |

## Globals

| | Address |
|---|---|
| `CockpitViewInstance`, the first cockpit built | `00092d1c` |
| `Cockpit_CursorImages`, seven, and `ColorSchemePanels`, ten fonts | `00092d20`, `00092d40` |
| `CockpitGauRecord`, the `.GAU` struct | `00092d3c` |
| `CockpitPButtonsBank`, `dba\pbuttons` | `00092d68` |
| `CockpitSquadGroup`, the player's group | `00092d6c` |
| `CockpitTtmManager` | `00092d70` |
| `CockpitContextStackIndex` (byte) and `CockpitContextStack` | `00092d7d`, `000b0df4` |
| `CockpitCanvasContext` | `000b0da4` |
| `Cockpit_ZeroOffset`, {0, 0} | `000b0dd4` |
| `g_SquadmateMachines`, three, and `SquadCallsignStrings`, three | `000bcc16`, `000bd324` |

## Open

- **Open:** `0002d098`–`0002e443`, the rest of `panel.cpp`: `0002d624`, `0002d970`, `0002db1c`, `0002de48` and `0002e1d8` among them, and the readers of the globals the static initializer `0002e404` sets.
- **Open:** the instruments' constructors and the root widget's: `00037bd0` and `00037ce4`, `00039818`, `00038f34`, `00039c64` and `0003a9ec`, `0003b0d4`, `00034aec`, `0003c258` and `0003dab4`, the weapon gauges' `0003700c`, `000373a4` and `00037810`, `00043cac` and `00044310`, and `0004433c`, which `CockpitWidgets_TranslateForView` hands the root widget; and the message boxes' `0002f3d8` and `0002eb80`, and `0002f370`.
- **Open:** what `00069a84`(0) returns as the canvas context, in place of ES2's `GLViewport`; the naming backlog holds it until its class is read.
- **Open:** what `0002fe84` does with the path of `str\strings0`, in ES2's `SimStrings_LoadAll`'s place.
- **Open:** what reads `CockpitPButtonsBank` (`00092d68`), the byte `00092d7c` that stops `Cockpit_DrawFrame`, and the dwords `+0x1d0` and `+0x168` and the byte `+0x1dc` that `Gau_WeaponButtonsWidget` writes and reads.
- **Open:** what calls `Squad_CallsignOf`, to which `es2_xref.py` finds no reference, and `HeapPtr_DtorPanelCopy`, likewise.
- **Open:** whether the widget list's slot that `CockpitView_CreatePodGauge` counts without filling is ever read.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0002cb54` set before a mission starts fires once for each energy and ammunition hardpoint of the player's HERC as the cockpit is built, with the word at `[ESP+8]` its row, below 9.
