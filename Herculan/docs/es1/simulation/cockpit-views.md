# DBSIM.EXE — mechview.cpp, the cockpit views

**ES1's cockpit has ES2's five views: the forward window, the heads-down display below it, a glance to either side and the external view with no cockpit. Moving between the forward window and the heads-down display moves the screen's picture up or down a few rows at a time, and a glance moves it sideways before the side view appears.** `mechview.cpp` builds the cockpit view manager, loads each view's canopy art and clip regions, switches between the views, holds the external view's rect and its border, and shakes the view. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's, described in ES2's [cockpit views](../../retail/simulation/cockpit-views.md).

## Where the module starts and ends

`mechview.cpp`'s code is `00021d38`–`00024d43` and its initialized data `00092576`–`000927d1`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): the four functions `00021d38`, `00021d60`, `00021d9f` and `00021da8`, each identical after normalization to `terrain.cpp`'s ([where `terrain.cpp` ends](terrain.md#where-the-module-starts-and-ends)), and the tables `00092576` and `000925aa`. `CockpitCanopy_LoadViewBitmap` (`00022184`) asserts `mechview.cpp` (line `0x103`). The data holds the scheme colors at `000925dc`, the chassis names and their table at `00092644`, the strings `cockpit`, `vue`, `edg` and `dba`, the source path at `00092678`, the switch tables of the view functions, the module's copies of `GLRectangleRegion`'s four vtables (`000926ee`, `00092712`, `0009273a`, `00092762`), and the switch tables of `CockpitView_StepViewTransition` and `CockpitView_SetShakeBand`.

The next object file, `00024d44`–`00025127` with its data at `000927d2`–`0009286d`, has no `ConfigPart` run and no source path. Its data is a second copy of `GLRectangleRegion`'s four vtables (`000927d2`, `000927f6`, `0009281e`, `00092846`), and each object file holds its own copy of a class's tables ([module copies](../formats/compiler.md#vtables)); `es1_naming.py vtables` finds them stored by `CockpitClipRegions_Load` ([clip regions](../formats/hd-ed-clip-regions.md)) and the four functions after it. The object file holds that loader, the three region destructors of those tables and `ClipRegions_AppendRectAboveScreen` (`00025014`). The run at `00025128`–`000251c3` opens the object file of `World_LoadTheater` (`000251c4`).

ES2's view functions are `CockpitViewManager_Ctor` (`00429660`) to `CockpitView_StepViewTransition` (`0042a9c0`), and its shake, external-view and static functions `CockpitView_SetShakeBand` (`0042d2f8`) to `CockpitVueRecord_ElementCtor` (`0042dc88`), with `CockpitClipRegions_Load` (`0042dcf0`) after them.

## The view manager

**The manager is ES2's ([object model](../../retail/simulation/cockpit-views.md#object-model)) without the per-view scanline span tables, so its fields from `+0x10` on sit 4 bytes lower; it is `0x33` bytes.**

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00`, `+0x04` | the same | the view count and the `.VUE` records, `CockpitVueRecords` (`000afaa4`) |
| `+0x08` | the same | the four canopy bitmap pointers |
| `+0x0c` | the same | the four `0x204`-byte clip blocks |
| | `+0x10` | the per-view scanline span tables |
| `+0x10` | `+0x14` | the current view |
| `+0x14` | `+0x18` | the pending view command, −1 for none |
| `+0x18` | `+0x1c` | the byte set while a view change is staged |
| `+0x19` | `+0x1d` | the `int16` cooldown |
| `+0x1b` | `+0x1f` | the cockpit, `CockpitViewInstance` |
| `+0x1f` | `+0x23` | the view to go back to from the external view |
| `+0x23` | `+0x27` | the `int16` color scheme, the chassis index |
| `+0x25` | `+0x29` | the player's machine |
| `+0x29` | `+0x2d` | the chassis name, 8 characters and a 0 at `+0x31` |
| `+0x32` | `+0x36` | the byte that asks for the external view's border |

`CockpitViewManager_Ctor` (`00021dd4`), (manager, machine) in stack order, callee-cleaned, allocates the manager when given none and returns it. `Sim_StartMission` calls it with none and the machine at `000b5b86`, stores the result at `000b5b60` and passes it to `CockpitViewManager_Publish` (`00021f8c`). In order:

1. The first time, while `CockpitPaletteSingleton` (`000925f0`) is 0, it loads `dpl\cockpit` (`Palette_LoadDpl` on `ResourcePath_BuildFolderName` of the folder `000980d0` names) there and sets `ExternalView_DefaultRect` (`000afb24`) to {0, 30, 319, 170, −160, −75}, the rect and the projection pair of a `.VUE` record, copying it to `ExternalView_Rect` (`000afb3c`). ES2's is {0, 0, 319, 194, −160, −90}, each value shifted by the video mode's coordinate shift.
2. The scheme word `+0x23` and the name are the chassis type's: the type record `+0x188` of the machine, its `+0x6c` and the 8 characters at its `+0x58`. When `CockpitOverride_Name` (`000925f8`) is set, the `-C` option's ([the cockpit clock](../formats/ttm-scripts.md#the-cockpit-clock)), `CockpitOverride_Index` (`000925f4`) and that name replace them.
3. `CockpitViewManager_LoadViews` (`00021fb0`) with the name, which is ES2's sequence ([`LoadViews`](../../retail/simulation/cockpit-views.md#cockpitviewmanager_loadviews-sequence)) without the span tables: the `.VUE` records, each view's clip regions ([which files](../formats/hd-ed-clip-regions.md#which-files)), `CockpitCanopy_LoadViewBitmap` for each view unless `g_LowMemory` (`000bda98`) is set, the cockpit (`Cockpit_LoadPanel`) and the scheme's colors ([below](#the-color-scheme)). It ends by making `ExternalView_ClipBlock` (`000afb94`) a block of one region, `ExternalView_ClipRegion` (`000afd98`), whose rect is `ExternalView_Rect`'s.
4. `+0x14` = −1, `+0x18` = 0, `+0x19` = 0, `+0x32` = 0.
5. `CockpitShakeStripRegion` (`000afb78`), a rect region, gets the rect {0, 200, 320, 210}, and `CockpitShakeStripPixels` (`000afb90`) gets `Region_PixelCount` bytes from `operator new` to save it in ([the view shake](#the-view-shake)).

It does not set the current view `+0x10`, which ES2's sets to −1. The manager is `operator_new`'s, which allocates from the heap with `Heap_Alloc` while the heap is on, as it is from `Sim_Main`'s start ([the heap](memory.md#the-heap)), and `Heap_Alloc` zero-fills the block, so the first `CockpitView_SetView` finds view 0 current; `CockpitView_ApplyViewState` takes the same branch for view 0 as for −1, with the old canvas origin (0, 0). `Sim_StartMission` then calls `CockpitView_SetView` with view 1 and then view 0, ES2's order ([both canopies resident](../../retail/simulation/cockpit-views.md#heads-down-pan--cockpitview_stepviewtransition-0042a9c0)).

**`CockpitViewManager_StaticInit` (`00024c28`)**, the module's static initializer (the dword at `000adc1c`), with no frame: `__vec_ctor` of the four 32-byte `CockpitVueRecords` with `CockpitVueRecord_ElementCtor` (`00024cc4`, by its padding entry `00024cc2`), which allocates `0x20` bytes when given none and returns the record; and the two static regions, `CockpitShakeStripRegion` and `ExternalView_ClipRegion`, each given the vtables `000926ee`, `00092712`, `0009273a` and `00092762` in turn and 0 at `+4`, the region type. **`CockpitViewManager_StaticDtor` (`00024c90`)**, its exit routine (the dword at `000adcac`), stores the vtables back in reverse order on both. ES2's (`0042db40`, `0042dc40`) count destructors where ES1's store vtables, and ES2's initializer also stores the rect {0, 210, 320, 240} at `0049ac54`, which ES2's constructor shifts.

### The color scheme

**Each of the eight HERCs the player can pilot paints its cockpit in colors of its own, taken from `dpl\cockpit` into the live palette when the cockpit is built.** The scheme *s* is the manager's `+0x23`, the chassis type number 0 to 7 (`ROADRUNN` to `COLOSSUS`, [which files](../formats/chassis-dat.md#which-files)), or the `-C` chassis's index. `CockpitViewManager_LoadViews`, after `Cockpit_LoadPanel`, with *e* the colors of `CockpitPaletteSingleton` (`000925f0`, its dword `+8`, four bytes an entry, [the layout](../../retail/formats/dpl-palette.md#layout)):

1. `Palette_InstallRange`(*e* + 32*s*, 16, `0x20`): live entries `0x20`–`0x2f` take `dpl\cockpit` entries 32*s* to 32*s* + 15;
2. `Palette_InstallRange`(*e* + 32*s* + 16, 16, `0x40`): live entries `0x40`–`0x4f` take entries 32*s* + 16 to 32*s* + 31;
3. `Palette_InstallRange`(&*c*, 1, `0x40`), *c* being `Palette_GetEntry`(the `int16` `CockpitScheme_ColorIndices` (`000925dc`)[*s*]) of the live palette after steps 1 and 2: live entry `0x40` is overwritten with the color of the live entry the table names;
4. `Palette_FlushDirtyRange`, which sends the changed entries to the display ([the upload](../rendering/palette.md#differences-from-es2));
5. slot `+0x1c` of the `dpl\cockpit` palette with 3, its destructor, which in `GLPalette`'s table (`0009b3b6`) is `GLPalette_Dtor` (`00069258`): the colors and the palette are freed. `CockpitPaletteSingleton` keeps the freed pointer.

`Palette_InstallRange` (`00027e98`), (source, count, base), copies *count* colors to the live palette from entry *base* and widens the dirty range over them; `Palette_GetEntry` (`00027f54`), (index), returns the live palette's color at the `int16` index ([the palette module](../rendering/palette.md)).

`CockpitScheme_ColorIndices` holds eight words, `0x2c`, `0x25`, `0x2d`, `0x27`, `0x2d`, `0x2d`, `0x2d`, `0x28`, each inside step 1's range, so entry `0x40` becomes `dpl\cockpit` entry 32*s* + the word − `0x20`:

| *s* | Chassis | `0x20`–`0x2f` | `0x40`–`0x4f` | `0x40` |
|---|---|---|---|---|
| 0 | `ROADRUNN` | 0–15 | 16–31 | 12 |
| 1 | `GUNSLING` | 32–47 | 48–63 | 37 |
| 2 | `RAPTOR` | 64–79 | 80–95 | 77 |
| 3 | `TOMAHAWK` | 96–111 | 112–127 | 103 |
| 4 | `PATRIOT` | 128–143 | 144–159 | 141 |
| 5 | `PANTHER` | 160–175 | 176–191 | 173 |
| 6 | `HERCULES` | 192–207 | 208–223 | 205 |
| 7 | `COLOSSUS` | 224–239 | 240–255 | 232 |

The eight schemes read `dpl\cockpit` entries 0 to 255 between them. For a type above 7 the word read is past the table's end, in `CockpitViewShakeArmed` and the globals after it. ES2's installs one window of 24 colors at entry 42 from entry 32 + 24*s*, its scheme being the type record's file offset 80 ([ES2's palette](../../retail/rendering/cockpit-canopy-palette.md#palette)); ES1's files hold 0 at offset 80 and its scheme is the type number.

## Loading and freeing the canopy art

`CockpitCanopy_LoadViewBitmap` (`00022184`), (manager, view) in stack order, callee-cleaned, is ES2's ([the canopy art](../../retail/formats/canopy-art-hb-db.md#canopy-art--hb0hb1hb2-and-db0db1db2)) with one folder: it writes the file number, the view less 1 for views above 2, with `ltoa` over the third character of the string `dba` (`00092674`), making `db0` to `db2`, and loads `db<n>\<name>` with `ClassItem_LoadResource` into slot *view* of `+0x08`, asserting at severity 5 when the stream does not open. It loads nothing for view 4 or a filled slot, and after file 2 copies the pointer to slot 3. It has no `hb` folder and no layout-mode branch.

`CockpitCanopy_FreeViewBitmap` (`0002226c`), (manager, view), for views 0, 1 and 2 destroys the slot's bitmap array with `GLBitmapArray_Dtor`(array, 3) and clears the slot, and slot 3 too for view 2; it does nothing for other views. ES2's has a layout-mode branch for view 0.

## Switching views

**Commands and their gates are ES2's** ([view switching](../../retail/simulation/cockpit-views.md#view-switching)): `CockpitViewManager_QueueViewCommand` (`00022634`) latches them at `+0x14`. ES1's views run on the display's pages alone, as ES2's do on its [`-b` paged path](../../retail/simulation/cockpit-views.md#the--b-paged-path), with no scroll window and no layout mode.

### The pages a view change moves

**A pan or a glance scrolls the monitor across video memory, where the next view has already been drawn: down to the heads-down display, sideways to a glance.** Video memory is a screen 640 pixels wide whose left and right halves hold pages 0 and 2, and a mission draws its frames alternately into the two ([the video memory](../rendering/screen-pages.md#the-video-memory)). Moving the visible page's origin moves the monitor's window ([scrolling](../rendering/screen-pages.md#the-simulators-own-page-routines)):

- **The heads-down display** (canvas origin (0, 197)) is drawn 197 rows below the top of a half. `CockpitView_ApplyViewState` moves the draw page's origin there, and the pan slides the visible page's origin down to it, 5 rows a step, so both pages end at (0, 197) from their own origins; the pan up slides it back.
- **A glance** keeps the pages' origins: the side view is drawn into the half the monitor is not showing, and the slide moves the visible page's origin 320 columns toward it, 10 a step, after which that half's page becomes the visible one, the slid page's origin goes back and that half is copied onto it. Command 4, the glance right (canvas origin (320, 0)), starts from page 0 on the screen and slides right; command 5, the glance left (−320, 0), starts from page 2 and slides left; the return from view 2 starts from page 2 and slides left, and from view 3 from page 0 and slides right.

That is the page gate of [the view command](#cockpitview_processviewcommand-000226f0): the pair `+0x88`/`+0x8c` is {0, 2}, `+0x88` being the page on the screen and `+0x8c` the page drawn during a frame, and `Sim_EndFrame` swaps it every frame, so a gated command waits at most one frame. ES2's `-b` path has the same gates with the pair {0, 1} ([the paged path](../../retail/simulation/cockpit-views.md#the--b-paged-path)). With `-S` the pair is {0, 0} ([the command line](main-loop.md#the-command-line)), and holds no 2.

### `CockpitView_ApplyViewState` (`000222c0`)

(manager, view) in stack order, callee-cleaned: does nothing when *view* is the current view or above 4. When the shake is armed (`CockpitViewShakeArmed`, `000925ec`) it clears it with `CockpitView_ClearShake` and sets `CockpitShakeReseedAfterSlide` (`000925ed`).

- **View 4**, the external view: clears `CockpitShakeReseedAfterSlide`, and unless already in view 4 saves the current view at `+0x1f`, copies `ExternalView_ClipBlock` into the installed render context's clip block (`000bdbda` + 4, `0x204` bytes), sets its clip mode `+0x208` to 2, copies `ExternalView_Rect`'s six values to the context's `+0x210`–`+0x224`, sets the current view to 4, turns the cockpit's widgets off (`CockpitView_DisableWidgets`), zeroes the view's canvas origin `CockpitViewCanvasOrigin` (`000afa9c`, `000afaa0`) and paints the border (`View_FillOutside3dRect`).
- **Any other view.** Leaving view 4 turns the widgets on (`CockpitView_EnableWidgets`). Leaving a glance, view 2 or 3, moves the cockpit's widgets (`CockpitWidgets_TranslateForView`) and the squad message port's box (`MessagePort_ShiftRect`) by that view's canvas origin. Leaving view 0 or 1, or with no current view, it sets the draw page's origin (the display `000bdd28`'s `+0x3c`) to that page's origin `+0x48` plus the new view's canvas origin, record `+0x18`/`+0x1c`, through the driver's routine at `0009df10`, slot 15, unless the new view is 2 or 3; and moves the widgets and the message port's box by the old view's origin less the new view's, the old origin (0, 0) when the current view is −1. Then it copies the view's clip block into the context's, sets clip mode 2, copies the record's six values to `+0x210`–`+0x224`, stores the canvas origin in `CockpitViewCanvasOrigin` and adds it to the context's `+0x220`/`+0x224`.

ES2's also points `ActiveScanlineClipSpans` at the view's span table, and moves the scroll window's origin.

### `CockpitView_SetView` (`00022558`)

(manager, view): for a view below 5, `CockpitView_ApplyViewState`; then for any view but 4, with `g_LowMemory` set loads the view's canopy first and frees it after; blits frame 0 of the view's bitmap array at (0, 0) with `Bitmap_Blit`, flags 2, mirrored, for view 3 and 0 otherwise, with the context's clip mode set to 0 for the blit; and for views 0 to 3 repaints the cockpit (`CockpitView_InvalidateWidgets`, `0002b4a0`). It stores the view as current. ES2's ([view switching](../../retail/simulation/cockpit-views.md#view-switching)) also scales the blit in layout mode 2 and blits a second panel in layout mode 1.

`CockpitView_InvalidateWidgets` (`0002b4a0`) is `panel.cpp`'s ([drawing the cockpit](cockpit-panel.md#drawing-it)).

### `CockpitView_ProcessViewCommand` (`000226f0`)

(manager), callee-cleaned, once a frame from `Sim_EndFrame` (`00059b40`) after the page flip:

1. When `+0x32` is set, `CockpitViewManager_FlushBorderFill` (`00024af8`): `View_FillOutside3dRect` in view 4, then `+0x32` = 0.
2. While the cooldown `+0x19` is not 0, takes 1 from it and returns.
3. **The page gate.** With *p* the display's `+0x88`: command 4 waits for *p* = 0, command 5 for *p* = 2, command 6 from view 3 for 0 and from view 2 for 2. A command that waits stays latched. *p* is the page on the screen, 0 and 2 in turn ([the pages a view change moves](#the-pages-a-view-change-moves)).
4. `Vga_WaitVerticalRetrace` (`000523a4`), which spins on port `0x3DA` until bit 3 is set, and `000bda96` = 0 ([the retrace word](../rendering/screen-pages.md#the-simulators-own-page-routines)). With no command pending the gate passes too, so every frame past the cooldown ends waiting for a vertical retrace.
5. While `+0x18` is clear, it stages the command and sets `+0x18`:
   - 0 and 1: when the squad message port has a line up (its byte `+0x436`), calls the port's vtable slot `+0x14` (the vtable at its `+0x464`), then `CockpitView_ApplyViewState` with the current view plus or minus 1;
   - 2: `CockpitView_ApplyViewState`(4);
   - 3: `CockpitView_SetView` with the saved view `+0x1f`, putting the current view back;
   - 4 and 5: `CockpitView_SetView` with view 2 or 3, putting the current view back; 6: the same with view 0, then `CockpitView_EnableWidgets`.

For 4, 5 and 6 it then adds (0, 0, 0) to the three `int16` angles at `+0x44` of the view object (`View_GetActiveObject`, `00059280`, which returns `000b5b8a`). ES2's adds ∓`0x3600` there in panel mode 1 alone ([rejected readings](../../retail/simulation/cockpit-views.md#rejected-readings)), so in both games a glance keeps the forward camera. ES2's also takes the pans through `CockpitView_SetView` in layout mode 2, and hides the system buttons.

### `CockpitView_StepViewTransition` (`00022904`)

(manager), callee-cleaned, once a frame from `Sim_EndFrame` before anything else, runs the staged command; ES1's is ES2's paged path ([the `-b` paged path](../../retail/simulation/cockpit-views.md#the--b-paged-path)). It does nothing while no command is pending or `+0x18` is clear. With *v* the display's `+0x88` and *d* its `+0x8c`, it sets `000bda96` to 0, and for the first half sets the clip mode and translation flag of the display's clip block (`+0x208` and `+0x20c` of its `+0x44` less 4) to 0, putting them back after.

1. **The first half.** Every command but 2 calls slot `+0x18` of the cockpit's object at `+0x207` (its vtable at `+0x464`). Commands 0, 1 and 3 then copy a rect from *d* to *v* with `Region_CopyBetweenPages`, the source page set to *d* and the draw page to *v* for the copy and the cursor hidden around it (slots 31 and 30, `0009df50` and `0009df4c`).
2. **The slide.** It clears `+0x18` and moves *v*'s origin, its page's origin `+0x48` plus an offset, through slot 15, step by step. While the squad message port has a line up (its byte `+0x436`), it first sets the source and draw pages to *v*, calls the port's slots `+0x14` and `+0x18` around each step and once more round the last, then sets the draw page back to *d* and calls `MessagePort_CopyBoxBetweenPages`(port, 0). `00098d96` is 0 during a slide and 1 after, which holds back the pointer's moves ([the page routines](../rendering/screen-pages.md#the-simulators-own-page-routines)).

With (*dx*, *dy*) the destination record's canvas origin less the current view's:

| Command | The first half | The slide | After |
|---|---|---|---|
| 0, pan down | returns from view 1; copies {−*dx*, 200 − *dy*, 319, 199} at the offset (*dx*, *dy*) | *y* from 0 to *dy*, 5 rows a step | `Cockpit_UseOffForwardCursor` (`0002dfd4`), current view + 1, `SimInput_SetEnabled`(0) |
| 1, pan up | returns from view 0; copies {0, 0, 319 − *dx*, −*dy*} at (*dx*, *dy*) | *y* from −*dy* to 0, 5 rows a step | `Cockpit_UseForwardCursor` (`0002e060`), current view − 1, `SimInput_SetEnabled`(1) |
| 2, external | hides the cursor; `00094c5b` = 0 | none | current view 4, `+0x32` = 1 |
| 3, from external | copies {0, 0, 320, 199} at (0, 0); `00094c5b` = 1 | none | when `Input_GetMouseDevice_Thunk` (`0004c634`), `Input_GetMouseDevice`'s result, is not 0, `Mouse_SetPosition` (`00055ab8`) to the cursor's last position, which `Cursor_GetPosition` (`00066a2e`) copies from `00098d70`; shows the cursor; the port's slot `+0x18`; current view = `+0x1f` |
| 4, glance | none | *x* by record 2's *x* less record 0's, 10 columns a step | `Cockpit_UseOffForwardCursor`; shows *d* (slot 18, `0009df1c`) and draws on *v*, swaps `+0x88` and `+0x8c`, puts *v*'s origin back on its page's and copies {0, 0, 319, 199} from *d* to *v*; current view 2 |
| 5, glance | none | *x* by record 0's *x* + record 3's, 10 columns a step | the same; current view 3 |
| 6, back from a glance | none | *x* by −(record 0's *x* + the current view's) | the same; current view 0 |

The byte `00094c5b` lets the pointer move the drawn cursor: the mouse callback at `00044464`, whose address `000441cc` stores, calls `Cursor_SyncPosition` with the pointer's position only while it is set. `es2_xref.py` finds it also read by `000444d0` and `00045f00` and written by `00045f00`, `000460b8` and `00057820` ([Open](#open)).

It then sets the command to −1 and the cooldown to 2, calls `CockpitView_SetShakeBand`(10) when `CockpitShakeReseedAfterSlide` is set, and returns the current view.

Where ES2's differ: ES2's steps 10 rows and `0x14` columns, both doubled by the coordinate shift; its copies span 240 rows; its external view sets sound `0x2d`'s volume to 100 and its return puts the volume back, where ES1's moves the mouse; and it ends by restoring the system buttons.

**The frame's end.** `Sim_EndFrame` (`00059b40`), with no arguments: `000bda96` = 1 when `Sim_Frozen` (`000b5b92`) is 0, and 0 otherwise; `CockpitView_StepViewTransition`; `Palette_CycleAnimatedRanges` (`00028260`), which rotates the palette's animated ranges whose delay has passed; `Palette_FlushDirtyRange` (`00027f74`), which sends the palette's changed entries to the display ([the palette](../rendering/palette.md)); shows `+0x8c` (slot 18), draws on `+0x88` (slot 17), swaps the two; `CockpitView_ProcessViewCommand`; `Mem_TrackPeakUse` ([the frame](main-loop.md#the-frame)). So during a frame `+0x88` is the page on the screen and `+0x8c` the page drawn, and a pan or a glance moves the shown page's origin, a scroll of the screen ([the pages a view change moves](#the-pages-a-view-change-moves)). `es2_xref.py` finds calls of `Sim_EndFrame` from `Sim_Main` (`00059619`) and eight from `00057820`, `00057ffc` and `00058414` ([Open](#open)).

## The external view

**The external view draws the world in a rect of the screen and paints the rest one color.** Its rect and projection pair are `ExternalView_Rect` (`000afb3c`), six `int32`, from `ExternalView_DefaultRect` (`000afb24`); `ExternalView_ClipRegion`'s rect (`000afda0`) is a copy of the first four.

- **`ExternalView_SetRect` (`00024a00`)**, (offset, rect) in stack order, callee-cleaned, is ES2's (`0042d89c`): stores the rect at `ExternalView_Rect` and `000afda0`, and the offset pair at `000afb4c`; in view 4 also writes the six to the render context's `+0x210`–`+0x224`.
- **`ExternalView_ResetRect` (`00024a7c`)** does the same from `ExternalView_DefaultRect`, ES2's `0042d944`.
- **`ExternalView_GetRect` (`00024c20`)**, frameless, returns `ExternalView_Rect`'s address, ES2's `0042db10`.
- **`View_FillOutside3dRect` (`00024b18`)**, (manager), callee-cleaned, its argument unread, is ES2's (`0042da08`) with color `0x3f` where ES2's takes `COLORS.DAT` id 19 and the screen 320 × 200: installs the brush {0, `0x3f`} at the context's `+0x22c`, sets clip mode and translation to 0, and fills, each when not empty, {0, 0, 319, *top* − 1}, {0, *bottom* + 1, 319, 199}, {0, *top*, *left* − 1, *bottom*} and {*right* + 1, *top*, 319, *bottom*}, then puts both back.

`es2_xref.py` finds the calls of `ExternalView_SetRect` and `ExternalView_ResetRect` from `00057820`, which it also finds calling `CockpitView_SetView` twice and `View_FillOutside3dRect` twice, and the call of `ExternalView_GetRect` from `ViewChain_DrawCaption` (`00058b68`, [the views](main-loop.md#the-views--viewchain_apply-0005894c)). It finds ES2's `ExternalView_ResetRect` and `View_FillOutside3dRect` called from `PreferencesPanel_Raise`.

## The view shake

**The view shakes by moving where the screen's picture sits, up and down within a band: ES2's moves its window over the back buffer, which carries the canopy ([the damage shake](../../retail/rendering/cockpit-canopy-palette.md#the-damage-shake)), and ES1's moves the origins of its display pages, which scrolls the screen ([the pages a view change moves](#the-pages-a-view-change-moves)).** The globals: `CockpitShakeRestOffset` (`000afb54`), the resting origin, two `int32`; `CockpitShakeOffset` (`000afb5c`), the moving one; `CockpitShakeBand` (`000afb64`), the two limits; `CockpitShakeTotal` (`000afb6c`), the `int16` sum of the steps, which `CockpitView_GetShakeOffset` (`000249d4`) returns as {0, total}.

- **`CockpitView_SetShakeBand` (`000243d4`)**, (amplitude), callee-cleaned: nothing in view 4 or while `+0x18` is set. Both offsets take `CockpitViewCanvasOrigin`. View 1's band is [*y* − amplitude, *y*], every other view's [*y*, *y* + amplitude]. In views 2 and 3 the offsets' *x* is 0, and with the context's clip mode and translation at 0 it saves `CockpitShakeStripRegion` into `CockpitShakeStripPixels` (`Region_Save`) and fills its rect with color `0x3f`: on the draw page as it is, then on `+0x88`, leaving `+0x8c` the draw page. It zeroes the total and sets `CockpitViewShakeArmed`. ES2's fills once, with `COLORS.DAT` id 19.
- **`CockpitView_StepShake` (`00024564`)**, (step), while armed and not in view 4: moves `CockpitShakeOffset`'s *y* by up to *step* toward the farther limit, as ES2's; sets the origins of both pages, `+0x8c` and `+0x88`, to their page origins plus the offset; moves every region of the current view's clip block by the step, a rect region's two rows (`+0xc`, `+0x14`) and a span region's first row; adds the step to the total; and moves the cockpit's widgets by (0, −step). ES2's moves its one window over the back buffer, a rect region's last row alone, and the span table and the context's rect too.
- **`CockpitView_ClearShake` (`00024750`)**, while armed and not in view 4: puts both pages' origins back on `CockpitShakeRestOffset`, moves the clip regions back by the total, in views 2 and 3 puts the strip back with `Region_Restore` on the draw page and on `+0x88`, leaving `+0x8c` the draw page, moves the widgets by (0, total), and clears the total and the armed byte.

`es2_xref.py` finds the shake's callers in `Cockpit_StartHitShake`, which passes the band 5, and `Cockpit_HitShakeTick`, which steps by 0–4 ([the hit shake](cockpit-panel.md#the-hit-shake)), in `00057ffc` and `00058414`, and in `CockpitView_ApplyViewState` and `CockpitView_StepViewTransition` ([Open](#open)).

## Callees of other modules

Read for this module and named with it, each ES2's counterpart; `CockpitView_DisableWidgets`, `CockpitView_EnableWidgets` and `CockpitWidgets_TranslateForView` are `panel.cpp`'s ([drawing the cockpit](cockpit-panel.md#drawing-it)):

| ES1 | Address | Differs from ES2's |
|---|---|---|
| `MessagePort_ShiftRect` | `0002eea0` | the rect at `+0x43f`, ES2's `+0x4a7` |
| `Cockpit_UseOffForwardCursor`, `Cockpit_UseForwardCursor` | `0002dfd4`, `0002e060` | the cursors at `+0x236` and `+0x232`, the slots at `+0x222`, `+0x226` and `+0x22e`, 4 below ES2's; passes the image's `+4` (0 for none) and `+8` to `Cursor_SetImage` |
| `SimInput_SetEnabled` | `0004d82c` | the flag at `00095818`; turning input on registers one listener, `0006c2bb`(`0004c6ec`, `0009586c`), unless `00095894` is set; turning it off clears the three 14-byte blocks at `000b1844`, `000b1854` and `000b1864` |
| `Vga_WaitVerticalRetrace` | `000523a4` | none |
| `View_GetActiveObject` | `00059280` | returns `000b5b8a` |
| `Mouse_SetPosition` | `00055ab8` | no counterpart: (*x*, *y*), C convention; when `000962e0` is set, `INT 33h` function 4 with *x* doubled while `000962dc` is set |

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMechviewCopy`, `ConfigPart_DtorMechviewCopy`, `ConfigPart_GetClassTagMechviewCopy`, `TSPartBase_DtorMechviewCopy` | `00021d38`, `00021d60`, `00021d9f`, `00021da8` | the run ([module copies](../formats/compiler.md#vtables)) |
| `CockpitViewManager_Ctor` | `00021dd4` | `00429660` |
| `CockpitViewManager_Publish`, `CockpitViewManager_Published`, `CockpitViewManager_GetCockpitView`, `CockpitViewManager_LoadViews` | `00021f8c`, `00021f98`, `00021fa0`, `00021fb0` | `00429810`, `00429820`, `00429828`, `00429834` |
| `CockpitCanopy_LoadViewBitmap`, `CockpitCanopy_FreeViewBitmap` | `00022184`, `0002226c` | `00429c2c`, `00429de4` |
| `CockpitView_ApplyViewState`, `CockpitView_SetView` | `000222c0`, `00022558` | `00429e60`, `0042a1f0` |
| `CockpitViewManager_QueueViewCommand`, `CockpitView_ProcessViewCommand`, `CockpitView_StepViewTransition` | `00022634`, `000226f0`, `00022904` | `0042a3f4`, `0042a4c4`, `0042a9c0` |
| `GLBase_DtorMechviewCopy`, `GLRegion_DtorMechviewCopy`, `GLRectangleRegion_DtorMechviewCopy` | `00024318`, `0002433c`, `00024364` | slot `+0x1c` of `00092712`, `0009273a`, `00092762`; the code of `00079950`, `00079974` and `GLRectangleRegion_Dtor` (`0007999c`) |
| `CockpitChassis_SelectByName` | `00024394` | [the cockpit clock](../formats/ttm-scripts.md#the-cockpit-clock) |
| `CockpitView_SetShakeBand`, `CockpitView_StepShake`, `CockpitView_ClearShake`, `CockpitView_GetShakeOffset` | `000243d4`, `00024564`, `00024750`, `000249d4` | `0042d2f8`, `0042d4a8`, `0042d624`, `0042d870` |
| `ExternalView_SetRect`, `ExternalView_ResetRect`, `CockpitViewManager_FlushBorderFill`, `View_FillOutside3dRect`, `ExternalView_GetRect` | `00024a00`, `00024a7c`, `00024af8`, `00024b18`, `00024c20` | `0042d89c`, `0042d944`, `0042d9e8`, `0042da08`, `0042db10` |
| `CockpitViewManager_StaticInit`, `CockpitViewManager_StaticDtor`, `CockpitVueRecord_ElementCtor` | `00024c28`, `00024c90`, `00024cc4` | `0042db40`, `0042dc40`, `0042dc88` |

The next object file's: `CockpitClipRegions_Load` (`00024d44`); `GLBase_DtorClipRegionsCopy`, `GLRegion_DtorClipRegionsCopy`, `GLRectangleRegion_DtorClipRegionsCopy` (`00024f98`, `00024fbc`, `00024fe4`), slot `+0x1c` of `000927f6`, `0009281e`, `00092846`, the same code as the mechview copies; and `ClipRegions_AppendRectAboveScreen` (`00025014`), (block): allocates a `0x18`-byte rect region (the vtables `000927d2` to `00092846` in turn, type 0), appends its `+4` to the block's list, raising the count `+0`, and gives it the rect {0, −100, 319, −1}. `es2_xref.py` finds no reference to it ([Open](#open)).

## Globals

| | Address |
|---|---|
| `CockpitPaletteSingleton`, the `dpl\cockpit` palette | `000925f0` |
| `CockpitScheme_ColorIndices`, eight `int16`, [the color scheme](#the-color-scheme) | `000925dc` |
| `CockpitOverride_Index` (`int16`) and `CockpitOverride_Name` | `000925f4`, `000925f8` |
| `CockpitViewShakeArmed`, `CockpitShakeReseedAfterSlide` (bytes) | `000925ec`, `000925ed` |
| `CockpitViewCanvasOrigin`, the current view's canvas origin, two `int32` | `000afa9c` |
| `CockpitVueRecords`, four 32-byte `.VUE` records | `000afaa4` |
| `ExternalView_DefaultRect`, `ExternalView_Rect`, six `int32` each | `000afb24`, `000afb3c` |
| `CockpitShakeRestOffset`, `CockpitShakeOffset`, `CockpitShakeBand`, `CockpitShakeTotal` | `000afb54`, `000afb5c`, `000afb64`, `000afb6c` |
| `CockpitShakeStripRegion` and `CockpitShakeStripPixels` | `000afb78`, `000afb90` |
| `ExternalView_ClipBlock`, `0x204` bytes, and `ExternalView_ClipRegion` | `000afb94`, `000afd98` |
| `CockpitViewManagerInstance`, the manager `Sim_StartMission` makes, and `SimDisplay`, the display `Sim_EndFrame` flips | `000b5b60`, `000b5b5c` |

## Open

- **Open:** the squad message port's slots `+0x14` and `+0x18`, which the view change calls.
- **Open:** what `000444d0`, `00045f00` and `000460b8` do with the cursor byte `00094c5b`.
- **Open:** the callers `es2_xref.py` finds and this batch did not read: `00057820` (the rect setters, `CockpitView_SetView`, `View_FillOutside3dRect`, and stores to `000bda96` and `00094c5b`; `F12`'s and `Alt+P`'s case, where ES2's raises the preferences panel: [commands](main-loop.md#commands--sim_dispatchcommand-00059df8)), and `00057ffc` and `00058414` (the shake), with the amplitudes they pass.
- **Open:** what calls `ClipRegions_AppendRectAboveScreen`, to which `es2_xref.py` finds no reference, and `HeapPtr_DtorMechviewCopy`, likewise.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:000222c0` set during a mission in the forward view fires when the view changes to the heads-down display, with the dword at `[ESP+8]` 1, and again on the way back, with 0. A check by eye: a glance to the right from the forward view slides the forward view out to the left as the side view comes in from the right, rather than cutting to it, and the return slides the other way.
