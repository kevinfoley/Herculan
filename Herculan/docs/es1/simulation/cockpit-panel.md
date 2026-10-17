# DBSIM.EXE — panel.cpp, the cockpit's instruments

**ES1's cockpit is ES2's cockpit object: it loads the console fonts and cursors, reads the chassis' layout file, builds the console instruments and the two message boxes from it, keeps the squadmates' list for the orders the player sends, answers the cockpit's keys and the screen-edge clicks, keeps the target selection, shakes the view when the machine is hit, and repaints the instruments each frame.** ES1's has nine weapon rows and a separate pod row, a cockpit clock built from the block ES2's ambience panel would read, a `.TTM` script run over the panel, and no coordinate shifts; its frame runs none of ES2's power-up steps, `Tab` steps the target selection through the object list, and a second hit only lengthens a running shake. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's, described in ES2's [cockpit object](../../retail/simulation/cockpit-hud-widgets.md#the-cockpit-object). Most functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the module starts and ends

`panel.cpp`'s code is `0002acdc`–`0002e443` and its initialized data `00092cae`–`0009306d`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): the four functions `0002acdc`, `0002ad04`, `0002ad43` and `0002ad4c`, each identical after normalization to `mechview.cpp`'s ([where `mechview.cpp` starts](cockpit-views.md#where-the-module-starts-and-ends)), and the tables `00092cae` and `00092ce2`. `Gau_Load` (`0002b5c4`) asserts `panel.cpp` (line `0x192`, the source path at `00092ef2`).

The code ends where the next run starts, at `0002e444`, with its tables `0009306e` and `000930a2`. Before it stands the module's static initializer, **`Cockpit_StaticInit` (`0002e404`)**, the static constructor table's entry `000adc2c`: frameless and with no Ghidra function, it stores 4 at `000b0db0`, 500 at `000b0dc8`, −500 at `000b0dd0` and 0 at `000b0da8`, `000b0db8`, `000b0dc4`, `000b0dcc` and both dwords of `Cockpit_ZeroOffset` (`000b0dd4`), and its `RET` at `0002e442` is followed by one zero byte ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). A scan of `0002acdc`–`0002e470` for a return followed by zero bytes up to a 4-byte boundary finds that one alone, and `000adc2c` is the one entry of the static constructor and exit tables that lands in the range. `Widget_DrawToCockpit` reads `Cockpit_ZeroOffset`; what reads the other seven is [Open](#open).

The data holds, after the run's tables, the module's globals from `SquadSlotColors` (`00092d14`) and `CockpitViewInstance` (`00092d1c`) to the byte `00092d7d`; the font and cursor names and their tables `00092dc4` and `00092e24`; the strings `strings0`, `str` and `pbuttons`; the module's copies of `GLRectangleRegion`'s three vtables (`00092e7e`, `00092ea6`, `00092ece`), which `Widget_DrawToCockpit` stores; the source path; the ticker's vtable `00092f0a`; the string `ttm\`; the view device's button latch `00092f33`; the command codes `00092f34`, their handlers `00092f94` and the two view-command tables `00092ff4` and `00093008` ([keys](#keys-and-the-view-device)); and the screen-edge strips' two vtables `0009301e` and `00093046` ([the screen edges](#the-screen-edges-and-the-cursors)).

The functions after the run follow ES2's in order, from `Cockpit_CreateCanvasContext` (ES2's `00430ea0`) to `CockpitView_FindArmedMountGauge` (ES2's `00434310`), with `Cockpit_LoadPanel` where ES2's `Gau_BuildCockpitWidgets` is and `CockpitClock_Create` where ES2's `Gau_PanelAmbienceWidget` is. ES1's adds the region destructors' copies, the two message-port slots and the button base table's seven slots; it has nothing in the places of ES2's power-up pair (`004328cc`, `00432924`), nearest-target selection (`004333c8`), step kick (`00434144`, `00434194`) and `004342b8`.

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
| `+0x20c` | `+0x210` | the target selection ([below](#the-target-selection)) |
| `+0x210`, `+0x214` | `+0x214`, `+0x218` | the selection's walk through the object list: a pointer into it, `LiveObjectList` (`000bc416`) as built, and its `int16` index, 0 |
| `+0x216`, `+0x21a`, `+0x21e` | `+0x21a`, `+0x21e`, `+0x222` | the bottom, left and right screen-edge strips ([below](#the-screen-edges-and-the-cursors)) |
| `+0x222` | `+0x226` | the active cursor slot |
| `+0x226` | `+0x22a` | the slot the edge slot took over from |
| `+0x22a` | `+0x22e` | the edge arrow picked, an index into `Cockpit_CursorImages`, 0 for none |
| `+0x22e` | `+0x232` | the edge slot |
| `+0x232`, `+0x236` | `+0x236`, `+0x23a` | the forward view's cursor slot and the other views' ([ES2's slots](../../retail/simulation/cockpit-input.md#9-cursor-rendering)) |
| `+0x23a` | `+0x23e` | set by `Cockpit_DrawFrame` once `CockpitView_BuildScrollTriggers` has run |
| `+0x23b` | `+0x23f` | the `int16` row of the pod gauge, −1 for none |

### Building it

`CockpitViewManager_LoadViews` builds the cockpit with `CockpitView_Ctor`(0, the manager's machine `+0x25`), stores it at the manager's `+0x1b` and calls `Cockpit_LoadPanel` with it and the chassis name ([the view manager](cockpit-views.md#the-view-manager)).

1. **`CockpitView_Ctor` (`0002ae5c`)**, (cockpit, machine): allocates `0x23d` bytes when given none, returning 0 when that fails; stores `LiveObjectList` and an `int16` 0 at `+0x210`, the machine at `+0x1ff` and its type record's `int16` `+0x6c` at `+0x1fd`; runs `ColorSchemePanels_LoadAll` (by its padding entry `0002aecb`) and returns the cockpit. ES2's allocates nothing itself and ends by allocating the arrays at `004d0490` and `004d0494`, which ES1's has none of.
2. **`ColorSchemePanels_LoadAll` (`0002aecc`)**, (cockpit): passes `ResourcePath_BuildFolderName`(`str`, `strings0`) to `SimStrings_LoadAll` (`0002fe84`, [the string table](../formats/str-strings.md#the-load)); the first time, loads ten fonts, `dfn\cpblue`, `cpgreen`, `cpred`, `cpylw`, `cpon`, `cppress`, `cpoff`, `cpgrey`, `cpblack` and `cporange`, into `ColorSchemePanels` (`00092d40`) through `Panel_LoadWrapper` (`0002adf4`); the first time, loads the seven cursors from `dci\` into `Cockpit_CursorImages` (`00092d20`) through `Cursor_LoadWrapper` (`0002ae28`), in ES2's order, and puts `CURSOR` in both slots `+0x232` and `+0x236`; zeroes the three `g_SquadmateMachines` (`000bcc16`); the first time, loads `dba\pbuttons` into `CockpitPButtonsBank` (`00092d68`) with `ClassItem_LoadFromFile`; calls `memset`(cockpit + `0x8f4`, `0x239`, 0), which, Symantec's `memset` taking (destination, value, count), fills nothing; stores the cockpit in `CockpitViewInstance` when that is 0; and clears `+0x20b`, points `+0x222` at the slot `+0x232` and sets `+0x23b` to −1. ES2's loads 18 fonts, from `hfn\` in panel mode 3, puts `PCURSOR` in the forward slot, leaves the active slot null, loads no `pbuttons` and clears its power-up fields. Each wrapper, (path), opens a `VolRStream` on the path and returns `ClassItem_LoadResource`'s item, as ES2's do; `es2_xref.py` also finds `Cursor_LoadWrapper` called from `00045f00`.
3. **`Cockpit_LoadPanel` (`0002c9d8`)**, (cockpit, name), in place of ES2's `Gau_BuildCockpitWidgets`: loads `gau\<name>` with `Gau_Load` into `CockpitGauRecord` (`00092d3c`) ([the layout](../formats/gau-cockpit-layout.md)); makes the canvas context over the record's first 16 bytes with `Cockpit_CreateCanvasContext` at `+4`; makes the root widget at `+0` with `00043cac`(0, the record) and passes it to `00044310`; makes the `.TTM` manager at `CockpitTtmManager` (`00092d70`) ([the simulator's scripts](../formats/ttm-scripts.md#the-simulator)); makes the pilot channel with `DialogPort_Ctor` (`0002f3d8`)(0, record + 1668) at `+0x203`, and the ticker, a `0x469`-byte object from `MessagePort_CtorBase` (`0002eb80`)(object, record + 1684) given the vtable `00092f0a` at `+0x464` and 0 at `+0x468`, at `+0x207` ([the message boxes](cockpit-messages.md#building-the-two-ports)); builds the seven instruments and the clock; and ends with `Ttm_RunAll`. Where ES2's calls `Widget_ClearPressed` and `SystemButtons_Ctor` on the root, ES1's calls `00044310` alone; it shifts nothing, and the weapon rows are built elsewhere ([below](#the-weapon-rows)); the [flyer branch](../../retail/simulation/cockpit-hud-widgets.md#power-up-sequence) has no counterpart.

**`Cockpit_CreateCanvasContext` (`0002ad78`)**, (rect): gets the context from `00069a84`(0) and stores it at `CockpitCanvasContext` (`000b0da4`), then sets ES2's fields: clip mode `+0x208` = 1, translation `+0x20c` = 1, the rect at `+0x210`–`+0x21c`, the byte `+0x230` and the dword `+0x231` 0 and `+0x235` = 200. It returns the context. ES2's allocates a `0x239`-byte `GLViewport` there ([Open](#open)).

Each instrument's wrapper, (cockpit, record), constructs the instrument with (0, cockpit, the record's block), appends it to the widget list and returns it, with no coordinate shift:

| Field | Wrapper | Block | Constructor | ES2's |
|---|---|---|---|---|
| `+0x1dd` | `Gau_WeaponButtonsWidget` (`0002ccd8`) | 452 | `00037bd0`, then `00037ce4`(it, 2); sets `+0x1dc` to 1, `+0x1d0` to 0 and `+0x1d4` to the dword at `+0x168` | `00432340`, block 468, `+0x1e1` |
| `+0x1e1` | `Gau_EnergyMeterWidget` (`0002cd3c`) | 548 | `00039818` ([its clip file](../formats/hd-ed-clip-regions.md#which-files)) | `004323e0`, `+0x1e5` |
| `+0x1e5` | `Gau_ShieldDisplayWidget` (`0002cd74`) | 616 | `00038f34` | `00432454`, `+0x1e9` |
| `+0x1e9` | `Gau_MfdPanelWidget` (`0002cdac`) | 728 | `00039c64`, then `0003a9ec`(it, 0) | `004324c8`, `+0x1ed`, mode 3 |
| `+0x1ed` | `Gau_ThrottleWidget` (`0002cdf0`) | 1000 | `0003b0d4` | `0043254c`, `+0x1f1` |
| `+0x1f1` | `Gau_RovingGunsightWrapper` (`0002ce28`) | 1104 | `Gau_RovingGunsightWidget` (`00034aec`, [the gunsight HUD](cockpit-gunsight-hud.md#building-the-gunsight)) | `004325c0`, block 1088, `+0x1f5` |
| `+0x1f5` | `Gau_PilotRosterWidget` (`0002ce60`) | 1212 | `0003c258` | `00432634`, `+0x1f9` |
| `+0x1f9` | `CockpitClock_Create` (`0002ce98`) | 1604 | [the cockpit clock](../formats/ttm-scripts.md#the-cockpit-clock) | `Gau_PanelAmbienceWidget` (`004326a8`), to which `es2_xref.py` finds no call |

The names are ES2's, by the block, the order of the calls and the field; the constructors other than the gunsight's are unread ([Open](#open)). The instrument at `+0x1f1` is drawn outside the canvas context as ES2's gunsight complex is ([drawing it](#drawing-it)), and `Cockpit_LoadSquadmatePilots` hands the squadmates' pilots to the one at `+0x1f5` as ES2's does to its Heads-Down Display.

### The weapon rows

`es2_xref.py` finds one call of each factory, from `0000a97c` (energy), `0000ab20` (ammunition) and `0000abec` (pod). Each passes `CockpitViewInstance`, the mount and the row, the signed byte `+7` of the record at the mount's `+0x18`, the first two also `0000aa9c`(mount), the name the gauge copies, and stores the gauge at the mount's `+0x77`; `0000a97c` then copies the block `EnergyWeaponGauge_GetState` (`000372d8`) returns, puts the mount's `int16` `+0x7b` in its second dword and hands the copy to slot `+0x14` of the gauge's vtable (at its `+0x76`), `EnergyWeaponGauge_SetState` ([the energy row](cockpit-weapon-gauges.md#the-energy-row)), as ES2's energy mounts push the power level ([weapon hardpoint rows](../../retail/simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows)).

- **`CockpitView_CreateEnergyWeaponGauge` (`0002cbbc`)** and **`CockpitView_CreateAmmoWeaponGauge` (`0002cc18`)**, (cockpit, mount, name, row): copy the row's rect, record + 20 + 16 × row, to the scratch rect at 244 or 164, construct the gauge with `EnergyWeaponGauge_Ctor` (`0003700c`) or `AmmoWeaponGauge_Ctor` (`000373a4`)(0, mount, name, row, cockpit, the block at the scratch rect) ([the weapon rows' gauges](cockpit-weapon-gauges.md)), register it with `CockpitView_RegisterWeaponGauge`(cockpit, row, gauge) and return it. ES2's (`00432074`, `00432124`) use 260 and 180.
- **`CockpitView_CreatePodGauge` (`0002cc74`)**, (cockpit, mount, row): copies the row's rect to 340, constructs the gauge with `00037810`(0, mount, row, cockpit, scratch), adds 1 to the widget list's count without storing the gauge in the list, and stores it at `+0x1d8` and the row at `+0x23b`; it returns the gauge. ES2's (`004321d4`) picks one of three pod classes and registers the gauge in its row as the others do.
- **`CockpitView_RegisterWeaponGauge` (`0002cb54`)**, (cockpit, row, gauge): ES2's (`00432018`) over nine slots: a row of −1 takes the first empty one; the gauge goes to `+0x1d4` while that is 0, onto the widget list and into its row's slot at `+0x70`, and the list count and `+0x94` each go up by 1. With −1 and all nine taken the row stays −1, and the store to `+0x70` − 4 lands on the list count `+0x6c`.

### The squadmates

- **`Cockpit_LoadSquadmatePilots` (`0002b308`)**, (cockpit), ES2's (`00431530`) with ES1's offsets: the player's machine is the first one `Pool_Prev` walks to in `g_MechPool` whose byte `+0x9d` is set (ES2's `+0xa3`); its group (`+0x3f`, ES2's `+0x45`) goes to `CockpitSquadGroup` (`00092d6c`), and up to three of the group's members (array `+0xe`, `int16` count `+0x12`, stopping at a null) other than `+0x1ff` into `g_SquadmateMachines`; each member's `int16` `+0x232` (ES2's `+0x29c`) goes to `0003dab4`(the instrument `+0x1f5`, pilot, slot). It calls nothing for the status roster, which ES2's hands its MFD. `es2_xref.py` finds one call, from `Sim_StartMission`.
- **`Squad_SendOrderToSlot` (`0002b3e8`)**, (cockpit, slot, order): ES2's (`00431610`): for a squadmate in the slot that is not the cockpit's machine and whose byte `+0x93` is clear (ES2's `+0x99`), stores the machine at the order's `+2` and calls slot `+0x2c` of the vtable at the squadmate's `+2` with (squadmate, 0, order); either way calls `MessagePort_Withdraw` (`0002f370`)(the pilot channel `+0x203`, `0x22`, squadmate) ([withdrawing](cockpit-messages.md#the-lifecycle)). `es2_xref.py` finds one call, from `0003d48c`.
- **`Squad_BroadcastOrderFromCockpit` (`0002b448`)**, (cockpit, order): ES2's (`0043166c`): stores the machine at the order's `+2` and returns `Squad_BroadcastOrder`(`CockpitSquadGroup`, order). `es2_xref.py` finds one call, from `0003adc8`.
- **`Squad_CallsignOf` (`0002b46c`)**, (machine): ES2's (`00431690`): entry *i* of `SquadCallsignStrings` (`000bd324`) for the squadmate in slot *i*, else 0. `es2_xref.py` finds no reference to it ([Open](#open)).

## Drawing it

**`Cockpit_PushCanvasContext` (`0002b00c`) and `Cockpit_PopRenderContext` (`0002b03c`)** are ES2's: the push adds 1 to `CockpitContextStackIndex` (`00092d7d`, −1 in the image), saves the installed render context (`000bdbda`) at that index of `CockpitContextStack` (`000b0df4`) and installs the canvas context `+4`; the pop reinstalls the saved one and takes 1 from the index.

**`Cockpit_DrawFrame` (`0002cf28`)**, (cockpit), once a frame from `Sim_RenderFrame` ([the cockpit's scripts](../formats/ttm-scripts.md#the-simulator)), does nothing while the byte `Cockpit_SkipFrameDraw` (`00092d7c`) is set. `es2_xref.py` finds two writes of it, both in `00057820`, which sets it for one `Sim_RenderFrame` and `Sim_EndFrame` between two `CockpitView_SetView` calls and clears it before a second `Sim_RenderFrame`. Otherwise:

1. the first time (`+0x23a` clear) it calls `CockpitView_BuildScrollTriggers` and sets `+0x23a`;
2. it ticks the clock (slot `+0xc` of `+0x1f9`) and calls `Cockpit_HitShakeTick`;
3. with the widgets off it calls slot `+0xc` of the instrument `+0x1f5` and returns;
4. while the view manager has no view change staged (its `+0x18`), slot `+0x14` of the pilot channel's vtable (at its `+0x464`), `PilotMessagePort_RestoreBackground`;
5. between the push and the pop, slot `+0xc` of each weapon row that is set, of the pod row when set, and of `+0x1dd`, `+0x1e1`, `+0x1e5`, `+0x1ed`, `+0x1e9` and `+0x1f5`;
6. `Cockpit_RunTtmScripts`, slot `+0x10` of the ticker's vtable `00092f0a`, `MessageTicker_Update`, and `Cockpit_UpdateEdgeCursor`;
7. with `+0x20c` set, `TargetSelect_CanTarget`(cockpit, `+0x20c`), clearing `+0x20c` when it returns 0.

ES1's widgets hold their vtable at their `+0x76`, where ES2's hold theirs at `+0`, and ES1's tables start their slots at `+4` ([vtables](../formats/compiler.md#vtables)); ES2's pass calls slot `+8`. ES2's `CockpitView_PerFrameUpdate` (`004327ac`) runs the power-up, the hit shake and the step kick where ES1's ticks the clock and runs the hit shake alone, paints `+0x1f9`, its Heads-Down Display, with the widgets off, and ends with the edge cursor, the press flashes and the nav marker; it runs no script ([ES2's](../../retail/simulation/cockpit-hud-widgets.md#drawing-it)). Both leave the gunsight out: ES1's `+0x1f1`, ES2's `+0x1f5`.

**`CockpitView_InvalidateWidgets` (`0002b4a0`)**, (cockpit), which `CockpitView_SetView` runs ([switching views](cockpit-views.md#cockpitview_setview-00022558)), unless the widgets are off calls slot `+8` of each set widget: the nine rows, then the pod row `+0x1d8`, `+0x1dd`, `+0x1e1`, `+0x1e5`, `+0x1ed`, `+0x1e9` and `+0x1f5` between the push and the pop, then `+0x1f1` and the clock `+0x1f9`, and last runs the cockpit's `.TTM` scripts (`Cockpit_RunTtmScripts`). ES2's (`004316c0`) calls slot `+4` of its ten rows and six of its instruments between the push and the pop and of the gunsight `+0x1f5` after it, and runs no script.

**`CockpitView_DisableWidgets` (`0002b2d0`) and `CockpitView_EnableWidgets` (`0002b2ec`)** set and clear `+0x20b`, ES2's (`00431500`, `00431518`) on `+0x20f`.

**`CockpitWidgets_TranslateForView` (`0002ced0`)**, (cockpit, delta), which the view changes and the shake call ([the view shake](cockpit-views.md#the-view-shake)): calls `0004433c` with the root widget and the delta's two values, subtracts the delta from the canvas context's `+0x220` and `+0x224`, and calls `CockpitView_FlipScrollTriggers`(cockpit, delta) when `+0x23a` is set. ES2's (`0043271c`) tests `+0x23e` and also offsets the widgets of its list at `+0x246`.

**`CockpitView_RectCornerVisible` (`0002b214`)**, (rect): 0 while the widgets are off; otherwise 1 when the corner (*x0*, *y0*) or (*x1*, *y1*) lies in the 320 × 200 box, bounds inclusive, whose origin is the canvas context's `+0x210` + `+0x220` and `+0x214` + `+0x224`. ES2's (`00431410`) tests `+0x20f`, takes the video-mode offsets `004d25da`/`004d25de` from the rect and sizes the box by the 3D viewport.

**`Widget_DrawToCockpit` (`0002b058`)**, (offset, rect, which), copies a widget's rect between the display's two pages, ES2's [paged path](../../retail/simulation/cockpit-views.md#the--b-paged-path) on ES1's only path: it returns 0 unless `CockpitView_RectCornerVisible` passes the rect and the view manager's `+0x18` is clear; *which* = 1 copies from the display's `+0x8c` to `+0x88` and returns 0 while `000b5bca` is set, any other value from `+0x88` to `+0x8c`. Inside the push and the pop it makes the source page the display's `+0x38` and the destination its `+0x3c`, each through `g_RasterRoutines` slots 16 and 17 as well, copies the rect as a type-0 region with `Region_CopyBetweenPages` (`00066914`) at the offset, `Cockpit_ZeroOffset` when that is null, between slots 31 and 30, puts the draw page back and returns 1. ES2's (`0043122c`) acts only on the paged path, tests the manager's `+0x1c`, widens the rect's far corner by a pixel in the scaled modes and takes the video-mode offsets `004d25da`/`004d25de` from it.

### The message boxes' vtable

The ticker's table `00092f0a`, which `Cockpit_LoadPanel` stores over the object `MessagePort_CtorBase` builds, holds two functions of this object file: slot `+0x14`, **`MessagePort_UnpaintNoOpPanelCopy` (`0002cb41`)**, three `NOP`s and `RET 4`, ES2's `MessagePort_UnpaintNoOp` (`0043757e`); and slot `+0x1c`, **`MessagePort_GetRectPanelCopy` (`0002cb47`)**, a `NOP` and then the port plus `0x43f` returned with `RET 4`, ES2's `MessagePort_GetRect` (`00437583`), which returns the port plus `0x4a7`, the code of the message boxes' file's `MessagePort_GetRect` (`0002ed87`). Its other five slots are that file's ([the slots](cockpit-messages.md#the-slots)).

## Keys and the view device

**The cockpit answers the number keys, the function keys, `Esc`, `Tab`, `R` and `F11` itself and passes every other key to its instruments, as ES2's does ([keyboard commands are scancodes](../../retail/simulation/cockpit-input.md#keyboard-commands-are-scancodes)); ES1's has nine number keys, a number key on the pod row switches the pod, and `Tab` steps the target selection.** A code is a PC set-1 scancode, `0x200` added for `Alt`.

**`CockpitWidgets_HandleCommand` (`0002d24c`)**, (cockpit, code), returns 0 while the widgets are off (`+0x20b`). With *v* the view manager's current view (`+0x10`), it looks the code up among the 24 dwords of `CockpitWidgets_CommandCodes` (`00092f34`) with `REPNE SCASD` and jumps through the 24 dwords after them (`00092f94`):

| Code | Key | What it does |
|---|---|---|
| 0 | | returns 0 |
| 2–10 | `1`–`9` | in view 1, as an unlisted code; otherwise row *code* − 2. The pod row (`+0x23b`) flips bit 0 of the first word of the block `00037b50`(the pod gauge `+0x1d8`) returns and hands the copy to `00037b14`(gauge, copy); another row's gauge (`+0x70` + 4 × row), when set, goes to `CockpitView_ArmWeaponByGauge` |
| `0x3b`–`0x40` | `F1`–`F6` | presses the MFD's (`+0x1e9`) button *code* − `0x3b`, then queues view command 1 from view 1 and 6 from views 2 and 3 (table `00092ff4`) |
| `0x41`, `0x42` | `F7`, `F8` | presses `+0x1f5`'s button *code* − `0x41`, then queues view command 0 |
| `0x43` | `F9` | command 5 from view 0, 6 from view 2 |
| `0x44` | `F10` | command 4 from view 0, 6 from view 3 |
| 1 | `Esc` | command 0 from view 0, 1 from view 1, 6 from views 2 and 3 (table `00093008`) |
| `0x0f` | `Tab` | returns 0 in view 4; in view 1, as an unlisted code; otherwise `TargetSelect_Cycle` |
| `0x57` | `F11` | `0004a1b4`(0, 0, 1), slot `+0xc` of the table at that object's `+0x314`, then `00045d0c`(object, 3) |
| `0x13` | `R` | `000157f4`(the machine) |

A press takes button *i* from the instrument's `+0x14` + 4*i* and, unless its `int16` `+0x17` is 2 or 3, calls slot `+0xc` of the button's table at `+0x1c`, its click, with (button, 1), the left button's value. ES2's presses through `Widget_PressChild`, which skips state 2 alone and also starts the [press flash](../../retail/simulation/cockpit-input.md#the-press-flash); ES1's press starts none. An unlisted code goes to `MfdFlashComm_HandleAltKey` (`0003a890`)(the MFD `+0x1e9`, code); when that returns 0, in view 1 to the key slot (`+0x10` of the table at `+0x76`) of `+0x1f5` alone, and in any other view to the key slots of `+0x1d4` when set, `+0x1dd`, `+0x1e1`, `+0x1e5`, `+0x1ed`, `+0x1f1` and `+0x1e9`, stopping at the first that returns nonzero, ES2's chain in ES2's order. It returns 1 for a code a case took or a slot accepted, else 0. `es2_xref.py` finds one call, from `00059df8`.

ES2's (`00432bc8`) takes the number keys `0x02`–`0x0b`, arms the row and then presses its select gadget; answers `;` (clear the selection), `Enter` (cycle the selection or press a scanner button) and `Alt+D`; steps the Targeting Pod on `Tab`; builds and runs an objectives panel on `F11`; and, while `maybe_CockpitLayoutMode` is 1, skips view command 0 after `F7`/`F8` and ends the chain with `+0x1f9`. `MfdFlashComm_HandleAltKey` takes seven codes in ES1, `0x21e`, `0x222`, `0x223`, `0x218`, `0x22e`, `0x212` and `0x221` (`Alt` with `A`, `G`, `H`, `O`, `C`, `E`, `F`), where ES2's takes five of them, the handlers being [Open](#open).

**`CockpitView_ArmWeaponByGauge` (`0002d098`)**, (cockpit, gauge): `WeaponMounts_SelectByGauge` (`0000ca70`) on the machine's weapon manager `+0x198`; when that arms a mount it plays sound `0x1a` and returns 1, else 0. ES2's (`00432a50`) plays nothing. `es2_xref.py` finds calls from `CockpitWidgets_HandleCommand`, `EnergyWeaponGauge_OnChildClick` (`000372e4`) and `AmmoWeaponGauge_OnChildClick` (`00037784`) ([the weapon rows' gauges](cockpit-weapon-gauges.md#the-energy-row)).

**`CockpitView_PollViewDevice` (`0002d1a0`)**, (cockpit, device), ES2's (`00432b14`) on ES1's offsets: once the strips exist (`+0x23a`) it hands the point at the device block's `+2` to `Cockpit_PickEdgeCursor`; the bytes `+0x19`, `+0x1a`, `+0x1b` and `+0x1c` queue view commands 1, 0, 4 and 5, where ES2's `+0x1e`–`+0x21` queue 1, 0, 5 and 4; on the press of the button byte `+0xb`, latched in `CockpitView_DeviceButtonLatch` (`00092f33`), in view 1 it sends `0x0f` to the key slot of `+0x1f5`, as ES2's to its Heads-Down Display's. It returns 1. `es2_xref.py` finds one call, from `0005a0c0`.

**`CockpitView_SetConsoleButtonsLatch` (`0002d0dc`) and `CockpitView_UnlightConsoleButtons` (`0002d10c`)** are ES2's (`00432a80`, `00432aa4`) 4 lower: the latch sets `+0x1dc` and clears `+0x1d0`; the unlight clears `+0x1dc`, puts out the lamps (byte `+0x3c`, ES2's `+0x40`) of the console's buttons at its `+0x14`, `+0x18` and `+0x1c` (ES2's `+0x18`–`+0x20`), invalidating a button (slot `+8` of its table at `+0x1c`) only when its lamp was lit, where ES2's invalidates all three; both end with the console's per-frame slot `+0xc`. `es2_xref.py` finds no reference to either ([Open](#open)).

## The target selection

**ES1 keeps ES2's target selection, with the same rules for what can be targeted and the same forward cone, but `Tab` steps to the next eligible object in the object list where ES2's `Enter` picks the one nearest the crosshair** ([ES2's](../../retail/simulation/target-selection.md#where-the-selection-lives)). The selection is the cockpit's `+0x20c`, measured from its machine `+0x1ff`; ES1 has none of ES2's spectator branches, and the object's fields are 6 lower than ES2's.

- **`TargetSelect_CanTarget` (`0002d624`)**, (cockpit, object): ES2's test ([can this be targeted](../../retail/simulation/target-selection.md#can-this-be-targeted--targetselect_cantarget-00433174)): the object's bytes `+0x9e` and `+0x93` clear, its group's (`+0x3f`) side byte `+0x14` (ES2's `+0x12`) other than the machine's, and either its radar byte `+0x8f` set within the dword `000bd4fc` or the machine's contact byte `+0xbc` + the object's `int16` `+0x45` set within `Detection_ContactScanRange`, each distance strictly less. `Cockpit_DrawFrame` drops the selection when this refuses it, as ES2's does ([losing the selection](../../retail/simulation/target-selection.md#losing-the-selection--cockpitview_perframeupdate)).
- **`TargetSelect_InForwardCone` (`0002d71c`)**, (cockpit, object): `Math_HeadingToward` from the machine's point `+0x20` to the object's, less the machine's heading `+0xa`, plus what slot `+0x40` of the machine's vtable (at `+2`) returns, within ±8999, as ES2's ([cycle's shortlist](../../retail/simulation/target-selection.md#cycles-shortlist--targetselect_cycle-0043349c)).
- **`TargetSelect_SetObject` (`0002d78c`)**, (cockpit, object): ES2's walk of the selection through the object list to the object asked for, from the cursor `+0x210`/`+0x214`, after `TargetSelect_Cycle` when there is no selection; returns 1 when the selection ends on the object, having given it to the roving gunsight `+0x1f1` (the dword `+6` of `Gunsight_GetStateBlock`'s block and its byte `+0x24` set to 1, then `Gunsight_SetValues`), else 0. ES2's also clears the selection when given 0. `es2_xref.py` finds one call, from `Gunsight_UpdateAndPaint` (`00034f3c`), which hands it the object the [pixel pick](terrain.md#the-pixel-pick-and-the-occlusion-probe) found.
- **`TargetSelect_Cycle` (`0002d898`)**, (cockpit): with no selection, the list's first object when it passes both tests; otherwise the first object after the selection, or after that first object, that passes both, walking with `ObjectList_Next` (`0000dba4`) and stopping when the walk is back at its start. A new object becomes the selection and goes to the gunsight as above; it returns the selection. ES2's (`0043349c`), which ES2's `TargetSelect_SetObject` runs in the same place, builds a shortlist by bearing and range instead ([cycle's shortlist](../../retail/simulation/target-selection.md#cycles-shortlist--targetselect_cycle-0043349c)). `es2_xref.py` finds calls from `CockpitWidgets_HandleCommand` (`Tab`), `TargetSelect_SetObject`, `0003ab08` and `0005a0c0`.

## The screen edges and the cursors

**Clicking a screen edge changes the view as in ES2, through three strips built on the first cockpit frame, and the pointer turns into the arrow toward the view that edge leads to** ([ES2's strips](../../retail/simulation/cockpit-input.md#10-the-screen-edges-are-three-widgets), [ES2's cursors](../../retail/simulation/cockpit-input.md#9-cursor-rendering)).

**`CockpitView_BuildScrollTriggers` (`0002d970`)**, (cockpit), from `Cockpit_DrawFrame`'s first frame, builds each strip as a `0x24`-byte object: `Widget_CtorRect` with the rect, `CTLButtonControlVtablePanelCopy` (`0009301e`) at `+0x1c` and 0 in the `int16` `+0x19`, then `ScrollTriggerVtable` (`00093046`) at `+0x1c` and the cockpit at `+0x20`; it stores the strip, null when the allocation fails, and registers it in the root widget's list with `Widget_RegisterClickable`. ES1's rects are fixed, {*x0*, *y0*, *x1*, *y1*}:

| Field | Rect | ES2's |
|---|---|---|
| `+0x216` | {0, 197, 319, 200}, the bottom | {0, *H* − 1 − (3 ≪ *Y*), *W* − 1, *H* − 1} |
| `+0x21a` | {0, 0, 5, 200}, the left | {0, 0, 5 ≪ *X*, *H* − 1} |
| `+0x21e` | {314, 0, 319, 200}, the right | {*W* − 1 − (5 ≪ *X*), 0, *W* − 1, *H* − 1} |

The two tables have seven slots from `+4`, ES2's [leaf-widget table](../../retail/simulation/cockpit-input.md#the-leaf-widget-vtable) one slot further on ([vtables](../formats/compiler.md#vtables)). `0009301e` is the module's copy of the button base table, the library's `00094b8a`, whose slot functions `000439ae`–`000439cf` it copies byte for byte: paint `CTLControl_PaintNoOpPanelCopy` (`0002dade`), invalidate `CTLControl_InvalidateNoOpPanelCopy` (`0002dae3`), click `CTLControl_OnClickNoOpPanelCopy` (`0002dae7`), per-frame `CTLControl_FlushDeferredPaintNoOpPanelCopy` (`0002daeb`), value `Widget_GetButtonValuePanelCopy` (`0002daef`), set value `CTLControl_SetValueNoOpPanelCopy` (`0002daf9`) and drag `CTLControl_DragMoveNoOpPanelCopy` (`0002daff`). Each is frameless, `NOP`s and a `RET` of 4 or 8, the value slot returning the `int16` at `00094b84` in `AX`, where ES2's returns its mouse-button word. `00093046` is the same table with the click slot `ScrollTrigger_OnClick` (`0002db04`), entered through its padding `0002db03`: (strip, value), it calls `CockpitView_HandleEdgeTrigger`(strip `+0x20`, strip), as ES2's (`00434df0`).

- **`CockpitView_HandleEdgeTrigger` (`0002dc2c`)**, (cockpit, strip): ES2's mapping by strip and current view (`+0x10`): the bottom strip command 0 from view 0 and 1 from view 1, the left 5 from view 0 and 6 from view 3, the right 4 from view 0 and 6 from view 2.
- **`CockpitView_FlipScrollTriggers` (`0002db1c`)**, (cockpit, delta), from `CockpitWidgets_TranslateForView`: ES2's reflection and shift ([a strip changes edge](../../retail/simulation/cockpit-input.md#a-strip-changes-edge-with-the-view)) with the full-screen shift fixed at 320 across and 200 down, where ES2's reads the display's width and height.
- **`Cockpit_PickEdgeCursor` (`0002dcc8`)**, (cockpit, point), from `CockpitView_PollViewDevice`: ES2's, storing the arrow at `+0x22a`: 4 (`SCURSOR`) over the bottom strip in view 0 and 3 (`NCURSOR`) in view 1, 6 (`WCURSOR`) over the left in view 0 and 5 (`ECURSOR`) in view 3, 5 over the right in view 0 and 6 in view 2, else 0. The point is rebased by the canvas context's `+0x210`/`+0x214` and `+0x220`/`+0x224`, and taken from `Input_GetCursorPos` (`0004d910`) instead while the dword `000b5b76` is set ([Open](#open)).
- **`Cockpit_UpdateEdgeCursor` (`0002de48`)**, (cockpit), each frame: with an arrow picked, puts its image in the edge slot `+0x22e` and makes that the active slot `+0x222`, saving the slot it replaces at `+0x226`; with none and the edge slot active, makes the saved slot active again. Each change goes to `Cursor_SetImage` (`00066948`) with the bitmap (the cursor record's dword `+4`, plus 4) and the hotspot (the record plus 8), as `Cockpit_UseForwardCursor` passes it.
- **`Cockpit_SetForwardCursor` (`0002df04`)** and **`Cockpit_SetOffForwardCursor` (`0002df6c`)**, (index): put image *index* in the slot `+0x232` or `+0x236` of `CockpitViewInstance` and pass it to `Cursor_SetImage` when that slot is the active one. `es2_xref.py` finds the first called twice from `000346a4`, and the second seven times, from `0003d48c`, `0003fbb8`, `0003fc14` and `000405ac`.

ES2's picker, updater and setters do nothing while `Cockpit_CursorsLive` is clear, and its setters skip a repeat of the last index and pass the image whenever the edge slot is not the active one; ES1's have no such gate.

## The hit shake

**A hit shakes the view and flashes the palette for `0x3c` ticks, as in ES2, but a second hit while the shake runs only makes it last longer** ([ES2's damage shake](../../retail/rendering/cockpit-canopy-palette.md#the-damage-shake), [the shake's steps](cockpit-views.md#the-view-shake)). The clock is the dword `000981c4` ([Open](#open)); the random numbers are `Math_RandomNext`(`000b5c48`), low word.

- **`Cockpit_StartHitShake` (`0002e17c`)**, outside view 4: `HitShakeEndTick` (`00092d74`) = now + `0x3c`; when `HitShakeNextToggleTick` (`00092d78`) is 0, it becomes now + random % 10, with `Palette_ActivateImpact` and `CockpitView_SetShakeBand`(5). ES2's (`00434010`) first restores the palette and clears the shake when one is running, which stops a running shake, and passes 5 ≪ `VideoMode_YCoordShift`. `es2_xref.py` finds one call, from `00013b44`.
- **`Cockpit_HitShakeTick` (`0002e1d8`)**, once a frame from `Cockpit_DrawFrame`: ES2's (`0043408c`). View 4 clears the end tick. Before it, each time the toggle tick has passed, `Palette_ToggleImpact` and the toggle tick rearmed at now + random % 10, and every frame `CockpitView_StepShake`(random % 5); at the end tick it clears it. With the end tick clear and the toggle tick set, `Palette_RestoreFromImpact`, the toggle tick cleared and `CockpitView_ClearShake`. It returns 1 while either acts, else 0.

## Squadmate and pilot helpers

ES2's helpers ([the pilot and squad channel](../../retail/simulation/cockpit-messages.md#the-pilot-and-squad-channel)), on ES1's offsets:

- **`Squad_IndexOf` (`0002d5dc`)**, (object): the slot 0–2 of the object in `g_SquadmateMachines`, or −1. `es2_xref.py` finds eight calls.
- **`Hud_WorldUnitsToMetres` (`0002e2a0`)**, (*u*): (*u* / 1000) × 6; **`Pilot_PortraitOf` (`0002e2b4`)**, (*p*): *p* / 3; **`Pilot_VoiceBankOf` (`0002e2c4`)**, (*p*): *p* / 4 + 1, 3 made 4. Each division truncates.
- **`HudColorTable_Get` (`0002e2e4`)**, (*i*): the `int16` entry *i* of `SquadSlotColors` (`00092d14`: 56, 59, 49, then 0), where ES2's (`00434280`) maps an id from its table through `HudColorTable`.
- **`Squad_PilotName` (`0002e2f4`)** and **`Squad_SlotOfPilotName` (`0002e314`)**, (slot) and (name): `HddGauge_Name` (`0003e830`) and `HddGauge_FindByName` (`0003e854`) on `CockpitViewInstance`'s `+0x1f5`, which ES2's run on `+0x1f9`. `es2_xref.py` finds no reference to `Squad_SlotOfPilotName` ([Open](#open)).
- **`Player_DependentCondition` (`0002e334`)**, (*n*): `Damage_ToConditionState` (`00030eb8`, [the paper doll](paper-doll.md#tinting)) of the `int16` `Mech_ReadEntryDamage` (`00015868`)(the machine, *n*, 0) returns.
- **`CockpitView_FindArmedMountGauge` (`0002e360`)**, (cockpit): the first weapon-row gauge (`+0x70`, count `+0x94`) whose mount (its dword `+0x86`, ES2's `+0xbd`) is the armed one: entry `+0x1d` (a signed byte, none when negative) of the array at `+0` of the weapon manager `+0x198`, where ES2's manager `+0x202` holds its array at `+4`; or 0.
- **`CockpitView_GetSquadMessagePort` (`0002d614`)**: the pilot channel `+0x203` of `CockpitViewInstance`, with no test for a missing cockpit, where ES2's (`00433158`) returns 0 then.

## Callees of other modules

Read for this module and named with it, each ES2's counterpart:

- **`WeaponMounts_SelectByGauge` (`0000ca70`)**, (manager, gauge): the first mount of the array at `+0` (`int16` count `+4`) whose slot `+0x5c` accepts the gauge; 0 when its byte `+0x4c` is clear, otherwise the mount's index to `WeaponMounts_SetSelection` (`0000cb00`, [arming](weapon-mounts.md#arming-chaining-and-linking)), the byte `+0x18` set and 1. ES2's (`004106ac`) keeps the array at `+4`, the count at `+8` and the test at slot `+0x58`.
- **`ObjectList_Next` (`0000dba4`)**, (list, object): entry (the object's `int16` `+0x45` + 1) mod the list's `int16` `+6` of the list's array `+0`; ES2's (`00411e40`) reads the object's `+0x4b`.
- **`Gunsight_GetStateBlock` (`00035180`)** returns the gunsight plus `0x7a` (ES2's `0043db88`, `+0xb1`). **`Gunsight_SetValues` (`00035090`)**, (gunsight, block): for each of the block's first three `int16` that differs from the cached `+0x7a`, `+0x7c`, `+0x7e`, stores it and hands a delta to slot `+0x10` of the table at `+0x10` of a child: the first's (new − old) to `+0xa4` and `+0xbc`, the second's and third's (old − new) to `+0xa8` and `+0xac`; then copies the block's `0x26` bytes over `+0x7a`.
- **`Input_GetCursorPos` (`0004d910`)** returns `Input_CursorPos` (`000b17d8`).
- **`HddGauge_Name` (`0003e830`)**, (roster, slot): the dword `+0x137` of gauge *slot*, the gauges `0x14a` bytes apart from `0xf6` past the pointer `000944cc`, ignoring the roster; ES2's (`0044b900`) `0x14e` apart from `0x12d` past `0049d230`. **`HddGauge_FindByName` (`0003e854`)**, (roster, name): the first of the three whose name is set and is a case-insensitive prefix of *name* (`Str_CompareNoCaseN` over the name's length), or −1.
- **`Mech_ReadEntryDamage` (`00015868`)**, (machine, *a*, *b*): `Component_ReadEntryDamage` (`00010b7c`)(machine + `0x19c`, *a*, *b*), ES2's on `+0x206`, which takes the index before the kind ([the readouts](damage-readouts.md)). ES1's `Player_DependentCondition` passes (*n*, 0), dependent *n*, as ES2's passes kind 0 and index *n*.
- **`MfdFlashComm_HandleAltKey` (`0003a890`)**, (MFD, code): `REPNE SCASD` over its seven codes at `000940bc`, jumping through the dwords after them to `0003a8ba`–`0003a991`; 0 for any other code.

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
| `CockpitView_ArmWeaponByGauge`, `CockpitView_SetConsoleButtonsLatch`, `CockpitView_UnlightConsoleButtons`, `CockpitView_PollViewDevice`, `CockpitWidgets_HandleCommand` | `0002d098`, `0002d0dc`, `0002d10c`, `0002d1a0`, `0002d24c` | `00432a50`, `00432a80`, `00432aa4`, `00432b14`, `00432bc8` |
| `Squad_IndexOf`, `CockpitView_GetSquadMessagePort` | `0002d5dc`, `0002d614` | `00433134`, `00433158` |
| `TargetSelect_CanTarget`, `TargetSelect_InForwardCone`, `TargetSelect_SetObject`, `TargetSelect_Cycle` | `0002d624`, `0002d71c`, `0002d78c`, `0002d898` | `00433174`, `00433250`, `004332dc`, `0043349c` (the first, third and fourth also by their padding `0002d623`, `0002d896`) |
| `CockpitView_BuildScrollTriggers` | `0002d970` | `00433770` (also by its padding `0002d96e`) |
| `CTLControl_PaintNoOpPanelCopy`, `CTLControl_InvalidateNoOpPanelCopy`, `CTLControl_OnClickNoOpPanelCopy`, `CTLControl_FlushDeferredPaintNoOpPanelCopy`, `Widget_GetButtonValuePanelCopy`, `CTLControl_SetValueNoOpPanelCopy`, `CTLControl_DragMoveNoOpPanelCopy` | `0002dade`, `0002dae3`, `0002dae7`, `0002daeb`, `0002daef`, `0002daf9`, `0002daff` | slots `+4`–`+0x1c` of `0009301e`, the code of the library's `000439ae`–`000439cf`; ES2's `00452ab2`, `00452ab7`, `0045555e`, `0045554a`, `00455530`, `0045553b`, `00455540` |
| `ScrollTrigger_OnClick`, `CockpitView_FlipScrollTriggers`, `CockpitView_HandleEdgeTrigger` | `0002db04`, `0002db1c`, `0002dc2c` | `00434df0`, `00433968`, `00433a88` |
| `Cockpit_PickEdgeCursor`, `Cockpit_UpdateEdgeCursor`, `Cockpit_SetForwardCursor`, `Cockpit_SetOffForwardCursor`, `Cockpit_UseOffForwardCursor`, `Cockpit_UseForwardCursor` | `0002dcc8`, `0002de48`, `0002df04`, `0002df6c`, `0002dfd4`, `0002e060` | `00433b20`, `00433c54`, `00433d90`, `00433e3c`, `00433ee8`, `00433f7c` |
| `Cockpit_RunTtmScripts`, `Cockpit_StartHitShake`, `Cockpit_HitShakeTick` | `0002e0ec`, `0002e17c`, `0002e1d8` | none, `00434010`, `0043408c` |
| `Hud_WorldUnitsToMetres`, `Pilot_PortraitOf`, `Pilot_VoiceBankOf`, `HudColorTable_Get` | `0002e2a0`, `0002e2b4`, `0002e2c4`, `0002e2e4` | `00434228`, `00434240`, `00434260`, `00434280` |
| `Squad_PilotName`, `Squad_SlotOfPilotName`, `Player_DependentCondition`, `CockpitView_FindArmedMountGauge`, `Cockpit_StaticInit` | `0002e2f4`, `0002e314`, `0002e334`, `0002e360`, `0002e404` | `00434298`, `004342c0`, `004342e0`, `00434310`, none |

## Globals

| | Address |
|---|---|
| `SquadSlotColors`, four `int16` | `00092d14` |
| `CockpitViewInstance`, the first cockpit built | `00092d1c` |
| `Cockpit_CursorImages`, seven, and `ColorSchemePanels`, ten fonts | `00092d20`, `00092d40` |
| `CockpitGauRecord`, the `.GAU` struct | `00092d3c` |
| `CockpitPButtonsBank`, `dba\pbuttons` | `00092d68` |
| `CockpitSquadGroup`, the player's group | `00092d6c` |
| `CockpitTtmManager` | `00092d70` |
| `HitShakeEndTick` and `HitShakeNextToggleTick` | `00092d74`, `00092d78` |
| `Cockpit_SkipFrameDraw` (byte) | `00092d7c` |
| `CockpitContextStackIndex` (byte) and `CockpitContextStack` | `00092d7d`, `000b0df4` |
| `CockpitView_DeviceButtonLatch` (byte) | `00092f33` |
| `CockpitWidgets_CommandCodes`, 24 codes and then their 24 handlers | `00092f34` |
| `CTLButtonControlVtablePanelCopy` and `ScrollTriggerVtable` | `0009301e`, `00093046` |
| `CockpitCanvasContext` | `000b0da4` |
| `Cockpit_ZeroOffset`, {0, 0} | `000b0dd4` |
| `g_SquadmateMachines`, three, and `SquadCallsignStrings`, three | `000bcc16`, `000bd324` |
| `Input_CursorPos` | `000b17d8` |

## Open

- **Open:** what reads the seven globals `Cockpit_StaticInit` sets besides `Cockpit_ZeroOffset`: `es2_xref.py` finds no occurrence of `000b0da8`, `000b0db0`, `000b0db8`, `000b0dc4`, `000b0dc8`, `000b0dcc` or `000b0dd0` besides the initializer's own stores (its positive control, `CockpitCanvasContext` `000b0da4`, finds its seven).
- **Open:** the callees `CockpitWidgets_HandleCommand` reaches and this batch did not read: `00037b50` and `00037b14` (the pod row's state), `000157f4` (`R`), `0004a1b4` and `00045d0c` (`F11`), and `MfdFlashComm_HandleAltKey`'s seven handlers `0003a8ba`–`0003a991`.
- **Open:** the callers `es2_xref.py` finds and this batch did not read: `00059df8` (`CockpitWidgets_HandleCommand`), `0005a0c0` (`CockpitView_PollViewDevice`, `TargetSelect_Cycle`), `00013b44` (`Cockpit_StartHitShake`), `0003ab08` (`TargetSelect_Cycle`), `000585dc` (the two target tests), the four of `Cockpit_SetOffForwardCursor`, `0003ad08` (`CockpitView_FindArmedMountGauge`), and the callers of the squad and pilot helpers.
- **Open:** what the view device block's bytes `+0x19`–`+0x1c` are, since ES1's last two queue view commands 4 and 5 where ES2's queue 5 and 4, and what sets them.
- **Open:** the dword `000981c4` the hit shake runs on (its writer and its rate, against ES2's coarse ticks, which ES2's message port reads where ES1's reads `000981c4`: [the queue](cockpit-messages.md#the-queue)), the dword `000b5b76` under which `Cockpit_PickEdgeCursor` takes the stored cursor point (ES2's tape playback flag), the value of the radar range `000bd4fc` (ES2's 200000), and the writer of the button word `00094b84` the value slots return.
- **Open:** whether ES1 has a nearest-target command outside `panel.cpp`, where ES2's `004333c8` sits beside the selection.
- **Open:** a body repair of `CockpitWidgets_HandleCommand` in `ES1Recon`, whose case bodies from `0002d2fa` on are undefined bytes there, as `TtmScript_RunPage`'s were ([the simulator's scripts](../formats/ttm-scripts.md#the-simulator)).
- **Open:** the library's button base table `00094b8a` and its seven slot functions `000439ae`–`000439cf`, of which `panel.cpp`'s are copies, for the library sweep; and the ES1GO counterparts of the module's copies.
- **Open:** the instruments' constructors and the root widget's: `00037bd0` and `00037ce4`, `00039818`, `00038f34`, `00039c64` and `0003a9ec`, `0003b0d4`, `0003c258` and `0003dab4`, the pod gauge's `00037810` ([the weapon rows' gauges](cockpit-weapon-gauges.md#open)), `00043cac` and `00044310`, and `0004433c`, which `CockpitWidgets_TranslateForView` hands the root widget.
- **Open:** what `00069a84`(0) returns as the canvas context, in place of ES2's `GLViewport`; the naming backlog holds it until its class is read.
- **Open:** what reads `CockpitPButtonsBank` (`00092d68`), and the dwords `+0x1d0` and `+0x168` and the byte `+0x1dc` that `Gau_WeaponButtonsWidget`, `CockpitView_SetConsoleButtonsLatch` and `CockpitView_UnlightConsoleButtons` write and read.
- **Open:** what calls `Squad_CallsignOf`, `HeapPtr_DtorPanelCopy`, `CockpitView_SetConsoleButtonsLatch`, `CockpitView_UnlightConsoleButtons` and `Squad_SlotOfPilotName`, to none of which `es2_xref.py` finds a reference.
- **Open:** whether the widget list's slot that `CockpitView_CreatePodGauge` counts without filling is ever read.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0002cb54` set before a mission starts fires once for each energy and ammunition hardpoint of the player's HERC as the cockpit is built, with the word at `[ESP+8]` its row, below 9.
