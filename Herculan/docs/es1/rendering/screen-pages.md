# GO.EXE and DBSIM.EXE — screen pages

**The shell draws each frame on one page of video memory while it shows another, and keeps the screen's clean background on a third, so that what it drew can be wiped by copying the background back over it.** The VGA display holds four 320 × 200 pages. Each frame the shell shows the page it has just drawn, makes the other its page to draw on, and copies the background over the parts of that page drawn two frames earlier. A script's animation is drawn again on every tick ([running a script](../formats/ttm-scripts.md#running-a-script)), so this copy is what erases its last frame; a rect a script fixes is copied into the background, and stays. The `.TTM` drawing commands ([the commands](../formats/ttm-scripts.md#the-commands)) draw through [the 2D primitives](2d-drawing.md) and record their boxes as [dirty boxes](#dirty-boxes) give. Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the image](../formats/x32-executable.md#the-image)).

ES2's simulator has the same three pages, on paged display targets only ([ES2's `-b` paged path](../../retail/simulation/cockpit-views.md#the--b-paged-path)).

## The pages

The shell's display, the object at `000ab616`, is made by `Shell_CreateDisplay` (`00045b25`), which `Shell_InitDevices` (`00045d24`) calls: `0x8c` bytes, constructed by `0005a388` for the driver named `VGA_8`. Its fields from `+0x24` on sit four bytes lower than the simulator's and ES2's, whose `GLDisplay_GetAttribute` has an attribute at `+0x24` the shell's lacks:

| Offset | Holds |
|---|---|
| `+8`, `+0xc` | the width and height, `GLDisplay_GetAttribute` (`0005a6e4`) items 0 and 1 |
| `+0x30` | the visible page, the one on the screen |
| `+0x34` | the source page, which a copy between pages reads |
| `+0x38` | the draw page, which drawing writes |
| `+0x40` | the clip block `Ttm_RunScript` sets ([choosing a page](../formats/ttm-scripts.md#choosing-a-page)) |
| `+0x44` | the four pages' origins, `int32` {*x*, *y*} pairs: (0, 0), (0, 200), (0, 400), (0, 600), each built by `ShellDisplay_PagePointCtor` (`00045d09`) |
| `+0x64` | four more such points, built the same way and then zeroed |
| `+0x84`, `+0x88` | the two pages [presenting a frame](#presenting-a-frame) swaps, 0 and 1 |

`Shell_CreateDisplay` sets the draw page to 1, the source page to `+0x88`, 1, and the visible page to `+0x84`, 0, then calls `0005a5d0`(display) and `GLDisplay_SetAttribute` (`0005a77c`)(display, 2, 9), the value 2 for attribute 9, which for the current display is `VGA8_ShowPage`'s retrace wait `00084472` (the simulator's [`GLDisplay_SetAttribute`](#the-video-memory) with the shell's display fields, attributes 2, 9, 10, 15 and 16, the last two being the simulator's 16 and 17), and makes the render context `00082754` with `GLViewport_Ctor` (`000512a0`)(0), its `+0x208` set to 1 and `+0x20c` to 0, and installs it. A render context holds its clip block at `+4`, so its `+0x208` and `+0x20c` are the block's clip mode `+0x204` and translation flag `+0x208` ([the display drivers](polygon-shading.md#the-display-drivers)).

**The installed render context.** The shell's drawing reads the installed render context from `000ab612`, among them `HudFont_DrawString` (`000520d8`), whose alignment `+0x231` and field width `+0x235` place a string ([labels](../simulation/cockpit-widgets.md#labels)). A raw scan of the image for stores to `000ab612` finds one, in `Raster_InstallRenderContext` (`000513fc`), and `es2_xref.py` finds 19 calls of that. `Shell_CreateDisplay`'s installs `00082754`. Each of the others is paired, in one function and with no return between, with an install of the context found on entry: `DUIWindow_Draw` and `DUIWindow_Update` install the window's own context ([drawing a window](../shell/menu-controls.md#drawing-a-window--duiwindow_draw-0001bfa3)); `Ttm_RunAll` and `Ttm_RunScript` put back the one `TtmScript_Run` replaced with a script's ([running a script](../formats/ttm-scripts.md#running-a-script)); `Map_PushContext` with `Map_PopContext`, `AlertPanel_Enter` with `AlertPanel_Leave`, `DirtyList_Flush`, `Screen_BlindsCopy` and `Screenshot_Save` bracket their own. So whatever the shell draws outside those brackets it draws in `00082754`.

`GLViewport_Ctor` clears a new context's `+0x231` and `+0x235`. The other 16 writes of `+0x231` that `es2_fieldscan.py` and a raw scan of the image for its displacement find are in a window's draw (`MTWindow_Draw`), its controls' (`MTButton_Draw`, `MTBmpButton_Draw`, `MTStrListBox_DrawLines`, `MTEditBox_Draw`) and its hotspots' (the career, debriefing, hangar, fabrication and arming panels), each on the context installed when it runs. A sweep of the image's indirect calls finds the window's slot `+8` and its controls' called from `DUIWindow_Draw` and `DUIWindow_Update`, and its hotspots' handlers from `DUIWindow_Draw` and `DUIWindow_UpdateHotspots`, which `DUIWindow_Update` calls. The menus' handlers that reach a hotspot set its redraw byte. So each write lands on a window's context, and `00082754`'s alignment stays 0.

**Setting a page.** While the display is the current one, `000ab616`, a store to its visible, source or draw page is copied to `00084486`, `0008448a` or `0008448e` and passed to the raster's routine at `000865f8`, `000865f0` or `000865f4`: slots 18, 16 and 17 of the shell's routine table at `000865b0`, which `Raster_InstallDriverTable` fills from the driver's. A function that changes the source or draw page for a copy puts back the low byte of the value it found. `VGA_8`'s table (`0008b2f0`) holds:

| Slot | Routine | Does |
|---|---|---|
| 15 | `VGA8_SetPageOrigin` (`00070313`) | given a page and a point {*x*, *y*}: stores the page's offset, the row table `00088852`'s entry *y* + *x* >> 2, at `00088e92` + 4 · page, and its pan, (*x* & 3) · 2 while `00084476` is set and 0 otherwise, at the byte `00088eba` + page; then sets the page again through slot 17, 16 or 18 when it is the current draw (`0008448e`), source (`0008448a`) or visible (`00084486`) page |
| 16 | `VGA8_SetSourcePage` (`000701d4`) | the page's offset to `00088ec4` and its pan >> 1 to `00088ed0` |
| 17 | `VGA8_SetDrawPage` (`000701f5`) | the same to `00088ec8` and `00088ed4`, by which the drawing routines address the draw page, as in [the simulator's `VGA_8`](polygon-shading.md#the-display-drivers) |
| 18 | `VGA8_ShowPage` (`00070216`) | shows the page: by `00084472`, 0 waits for nothing, 1 waits out a vertical retrace in progress, and any other value waits for the next retrace to start and then to end (port `3DA` bit 3); writes the page's offset to the CRTC start address (port `3D4`, registers `0xc` and `0xd`); when `00084472` is not 0 waits for the next retrace to start; writes the pan to the attribute controller's register `0x13` (port `3C0`, index `0x33`); and stores the offset and pan >> 1 at `00088ecc` and `00088ed8`, calling the routine at `0008662c` before and the one at `00086628` after while `00084424` is set, and copying the 8 bytes at `00084428` to `00084452` before and back after. `0008444e` is 0 while it runs and 1 after |

### Copying between pages

`Region_CopyBetweenPages` (`00051c40`), given a region and an offset point, C convention, is ES2's (DBSIM `00487d54`): for a region of type 0, a rect {*x0*, *y0*, *x1*, *y1*} at `+4`, `Region_CopyRectBetweenPages` (`000519bc`); for type 2, a pointer at `+4` to a [span list](../../retail/rendering/polygon-fill.md#span-lists), `Region_CopySpansBetweenPages` (`00051aed`); for any other type nothing. A region is laid out as `Region_Restore`'s.

- **`Region_CopyRectBetweenPages`** adds the offset to the rect, and the block's translation (`+0x20c` − `+0x21c`, `+0x210` − `+0x220`) too when `+0x208` is set, and passes the rect and the offset, as it was given, to slot 20 (`00086600`). In clip mode 0 it passes the rect straight through; in clip mode 1 it first intersects the rect with the block's rect at `+0x20c` (`Rect_Intersect`, `0006659c`), copying nothing when they do not meet; in any other clip mode it walks the block's region list, when its count `+0` is above 0, from its last entry to its first, copying the rect's intersection with each rect region through slot 20 and its cut to each span region (`SpanList_ClipToRect`, `0004fd65`) through slot 19 (`000865fc`), and passing over a region of any other type.
- **`Region_CopySpansBetweenPages`** copies the span list moved by the same amount into the scratch list `00084495` points to and passes it with the offset to slot 19: whole in clip mode 0; cut to the block's rect (`SpanList_ClipToRect`, `0004fd65`, into the list `00084499` points to) in mode 1; in any other mode cut to each rect region (`SpanList_ClipToRect`) and intersected with each span region (`SpanRegion_Intersect`, `0004fc50`, [lines](2d-drawing.md#lines-and-clipped-bitmaps)) from the list's last entry to its first.

The two are ES2's `Region_CopyRectBetweenPages` (DBSIM `00487ad0`) and `Region_CopySpansBetweenPages` (DBSIM `00487c01`), statement for statement.

`VGA8_CopyRectBetweenPages` (`000733bc`), `VGA_8`'s slot 20, given an inclusive rect and an offset, copies the rect of the source page, moved back by the offset, onto the rect of the draw page. When the two pages' pans differ in their low two bits, (*offset.x* + `00088ed4` − `00088ed0`) & 3 not 0, it copies row by row: for each row *y* the routine at slot 21 (`00086604`, `VGA_8`'s `VGA8_ReadRow`) reads the source row *y* − *offset.y*, columns *x0* − *offset.x* to *x1* − *offset.x*, into a stack buffer, and slot 22 (`00086608`, `VGA8_WriteRow`) writes it to row *y*, columns *x0* to *x1*. Otherwise it copies through the VGA latches, in write mode 1 (graphics controller register 5 set to `0x41`, put back to `0x40` after), a byte per four columns with the planes the edges cover enabled: a rect whose two ends fall in one group of four columns with the plane mask from the 4 × 4 table `00088ee0`, any other in up to three passes, each over every row from the top: its partial left group (`00088ef0`), then its whole groups with all four planes, a row at a time, then its partial right group (`00088ef4`), the row stride being `0008884a`. So where a copy's target overlaps its source, a later pass reads what an earlier one wrote ([command 3's column 320](../simulation/cockpit-views.md#cockpitview_stepviewtransition-00022904)). In the one-group case it reads source row *y* + *offset.y*, where the other cases read *y* − *offset.y*; every copy in this doc passes the offset (0, 0).

## Dirty boxes

**Every draw on a shell screen records the box it covered, and the frames that follow copy the background back over that box.** The list's head is at `000a09c0`, and each record is `0x13` bytes:

| Offset | Holds |
|---|---|
| `+0` | `uint16` *x* |
| `+2` | `uint16` *y* |
| `+4` | `uint16` width |
| `+6` | `uint16` height |
| `+8` | `uint16` area, width × height when the box is added |
| `+0xa` | `int8` the delay, the number of frames before the box is copied |
| `+0xb` | 0 for the first record of a block of ten, 1 for the others |
| `+0xf` | the next record |

Records come from a free list at `000a09bc`. `DirtyBox_Alloc` (`00040e58`) takes its head, first allocating a block of ten (`0xbe` bytes) linked in order in front of the list when it is empty, and returns 0 when that allocation fails; `DirtyBox_Free` (`00040ee4`) pushes a record back.

`DirtyBox_Add` (`00040f00`), given a delay, a height, a width, *y* and *x* in stack order, callee-cleaned:

1. returns when the box misses the display: *x* > *W* − 1, *x* + width < 0, *y* > *H* − 1 or *y* + height < 0, *W* and *H* being the display's width and height (`GLDisplay_GetAttribute` of `000ab616`, items 0 and 1);
2. adds a negative *x* to the width and sets *x* to 0, and *y* and the height likewise;
3. widens the box to whole groups of four columns: the width plus *x* mod 4, rounded up to a multiple of 4, from *x* less *x* mod 4;
4. when *x* + width > *W* − 1 sets the width to *W* − 1 − *x*, and when *y* + height > *H* − 1 the height to *H* − 1 − *y*;
5. when the width and the height are both non-zero, takes a record with `DirtyBox_Alloc`, which it does not test for 0, fills it, the next pointer 0, and passes it to `DirtyList_Insert`.

`DirtyBox_AddRect` (`00041062`), given a delay and an inclusive rect {*x0*, *y0*, *x1*, *y1*} by value, callee-cleaned, adds the box (*x0*, *y0*) of width *x1* − *x0* + 1 and height *y1* − *y0* + 1.

`DirtyList_Insert` (`000411a5`), given a record, looks through the list for the record of the same delay that saves the most when merged: one whose box touches or overlaps the new one, each box's *x* at most the other's *x* + width and its *y* at most the other's *y* + height, and for which, with *U* the area of the box around both and *S* the sum of the two areas (`+8`), both cut to 16 bits, *U* ≤ *S* and *S* − *U* is the largest above 0. It sets that record's box to the box around both, leaving its area as it was, and frees the new record; with none it puts the new record at the head of the list. A merged box is not compared with the others again.

`DirtyList_Flush` (`000413db`), given a draw page and a source page in stack order, callee-cleaned, installs the render context `00082248`, sets the display's source and draw pages to those, and for each record whose delay is 0 copies its box from the source page to the draw page with `Region_CopyBetweenPages`, offset (0, 0), as the type-0 region {*x*, *y*, *x* + width, *y* + height}: the rect is inclusive, so the copy covers one column and one row past the box. It lowers every record's delay by 1, puts the source and draw pages back, installs the render context it found, and with `DirtyList_Prune` (`0004109c`) frees every record whose delay is now below 0. A box added with delay 0 is therefore copied at the next flush, and one added with delay 1 at the flush after that.

`DirtyList_CreateContext` (`00040d54`) makes the flush's render context with `GLViewport_Ctor` (`000512a0`)(0) and stores it at `00082248`, setting its clip block's clip mode to 1, its translation flag to 1, its rect to (0, 0) to (319, 199) and its origin to (0, 0). `Shell_OpenFirstScreen` (`0003ba47`), which sets up the scripts ([the shell](../formats/ttm-scripts.md#the-shell)), calls it first.

The `.TTM` commands add every box with delay 1, `0xa601`'s rect through `DirtyBox_AddRect`; `es2_xref.py` finds further calls of `DirtyBox_Add` from `ShellBackdrop_Draw` (`0002141c`), `Sprite_Draw` (`000217a9`) and `Sprite_Erase` (`00021a6e`), with delay 0, and from `Menu_Close` (`0003b138`) and `Shell_DrawScreen` (`0003c1b7`), and of `DirtyBox_AddRect` from the text box's `DialogBox_MarkDirty` (`0001907e`) and `DialogBox_Dtor` (`00019b54`) ([drawing a page](../shell/message-box.md#drawing-a-page)), `DUIWindow_Update` (`0001c067`) and `DUIWindow_UpdateHotspots` (`0001c270`).

## Presenting a frame

**The shell shows a new frame each time its frame timer ticks: it shows the page it drew, switches drawing to the other, and restores that page's dirty boxes from the background.** `Shell_PresentFrame` (`00012980`), which `es2_xref.py` finds called twice from `Shell_Main` and nine times from `Shell_PlayTtmInPlace` (`00037dce`), `MessageBox_Run` (`000382f6`), `MessageBox_Answer` (`0003837f`), `Shell_PlayTtm` (`0003b8df`) and `Shell_HandleMessage` (`0003c7d8`):

1. waits until the byte `Shell_FrameDue` (`0007fd06`) is set, and clears it. `Shell_FrameTick` (`00012974`) sets it: `Shell_InitDevices` (`00045d24`) registers it with `Timer_AddCallback` at a count of 2, the scripts' count ([the timer](../formats/ttm-scripts.md#the-timer));
2. when there is a palette at `0007fcd0` and the byte `0007fcd4` is set, uploads it with `Palette_UploadWhole` and clears the byte. The scripts' palette commands leave their palette there;
3. when the byte `0007fcd5` is set, calls `ShellMusic_Play` (`00020a70`) with the dword at `00081938` and clears it;
4. makes `+0x88` the visible page and `+0x84` the draw page, and swaps `+0x84` and `+0x88`;
5. when the byte `0007fcd7` is 0, `0007fcd6` is set and the word `Shell_BackdropId` (`000817c4`) is 1000, calls `Shell_WaitTicks` (`0004bbe9`)(6), which zeroes `Shell_WaitCount` (`000a0a90`) and loops until the timer callback `Shell_WaitTick` (`0004bbdd`), run every second interrupt, has raised it to 6, 12 interrupts or 0.1 s ([the shell's devices](../shell/startup.md#the-devices)), and clears `0007fcd6`;
6. flushes the dirty list with `DirtyList_Flush`(the new `+0x88`, the word `0007fccc`): the draw page it has just set, and page 2, the word's value in the image, which `Shell_OpenFirstScreen` (`0003ba47`) also passes as `Ttm_Configure`'s *a*;
7. when the byte `Shell_SyncPointer` (`0007fd07`) is 1, calls `Cursor_SyncPosition` (`00050ed4`) with the address `Shell_PointerX` (`0009aff0`), `Input_GetMouseDevice_Thunk` (`00027bc9`), whose answer it drops, and `Mouse_SetPosition` (`00051037`) with the dwords at `0009aff0` and `Shell_PointerY` (`0009aff4`) ([opening a menu](../shell/menus.md#opening-a-menu--menu_open-0003969e)).

So pages 0 and 1 take turns, one drawn while the other shows, and page 2 holds the background. A page is drawn every other frame, and a box drawn on it with delay 1 is restored at the second flush after, when that page is next the one drawn.

### Fixing a rect

`Display_CopyRectToPage` (`0004c17c`), given a page, a source page and a rect in stack order, callee-cleaned, sets the display's source page to the source and its draw page to the page, copies the rect from one to the other with `Region_CopyBetweenPages`, offset (0, 0), adds the rect with `DirtyBox_AddRect` at delay 0, and puts the two pages back. `TtmScript_FixRect` passes `Ttm_Configure`'s *a*, page 2, and the draw page, so a fixed rect is copied into the background now and onto the other page at the next frame. `es2_xref.py` finds one other call, from `Shell_HandleMessage` (`0003c7d8`).

## The simulator

**`ES1DBSIM` has the same pages, region copy and dirty boxes, at its own addresses, with ES2's display layout.** Its display is ES2's, the visible page at `+0x34`, the source page `+0x38`, the draw page `+0x3c`, the clip block `Ttm_RunAll` sets at `+0x44`, and the page origins at `+0x48` ([`Sim_CreateDisplay`](polygon-shading.md#the-display-drivers) sets the draw page to 1 and the visible page to 0); `GLDisplay_GetAttribute` (`ES1DBSIM 0006a780`) is ES2's (DBSIM `00481244`). While the display is the current one, `000bdd28`, a store to the source or draw page is copied to `00098dd6` or `00098dda` and passed to the routine at `0009df14` or `0009df18`, slots 16 and 17. `VGA_8`'s are `VGA8_SetSourcePage` (`ES1DBSIM 00052644`) and `VGA8_SetDrawPage` (`ES1DBSIM 0005266a`), the shell's with the page tables at `0009e5ce` and `0009e5f6`, the offsets at `0009e600` and `0009e604` and the pans at `0009e60c` and `0009e610`, each also storing the page itself at `00098dd6` or `00098dda`.

| Shell | `ES1DBSIM` | Difference |
|---|---|---|
| `Region_CopyBetweenPages`, `Region_CopyRectBetweenPages`, `Region_CopySpansBetweenPages` | `00066914`, `00066690`, `000667c1` | none |
| `DirtyBox_Alloc`, `DirtyBox_Free`, `DirtyBox_Add` | `000838e4`, `00083970`, `0008398c` | the list's head at `000bbc84`, the free list at `000bbc80` |
| `DirtyList_Insert`, `DirtyList_Prune` | `00083bb4` (entered through its padding at `00083bb1`), `00083ae4` | none |
| `DirtyList_Flush` | `00083e80` | installs no render context; `es2_xref.py` finds no reference to it ([Open](#open)) |
| `GLBitmapArray_Dtor`, `GLPalette_Dtor` | `000673c8`, `00069258` | none |

The cockpit's script draws add their boxes with delay 1. `TtmScript_FixRect` (`ES1DBSIM 00079c38`) passes `DirtyBox_Add` the rect's *x0* and *y0* as the box's corner and its *x1* and *y1* as its width and height, with delay 0.

### The video memory

**A mission lays video memory out as a screen 640 pixels wide and 409 rows high, two 320 × 200 pages side by side at the top and two more below them, and the monitor shows a 320 × 200 window of it.** `Sim_CreateDisplay` opens the display for `VGA_8`'s mode 0 ([the display drivers](polygon-shading.md#the-display-drivers)), and `GLDisplay_Open`'s pass through the driver (`RasterDriver_OpenDisplay`) runs three of `VGA_8`'s routines:

- **Slot 8, `VGA8_IsModeUnsupported` (`0008b4d8`)**, frameless, (mode), C convention: 0 for modes 0, 1 and 2, and 1 for any other, which fails the open.
- **Slot 11, `VGA8_OpenMode` (`0008b4f8`)**, (mode, *info*), C convention, *info* being the display's `+4`: sets the ten page offsets `0009e5ce` to *i* × the mode's page size, the dword `000ad8e8`[mode] (16,000 for mode 0), or 0 where that reaches `0x10000`; zeroes the ten pan bytes `0009e5f6` and the dwords `0009e600`–`0009e614`; calls `0008b408`(mode) ([Open](#open)); and copies the mode's eleven dwords, `000ad788` + `0x2c` · mode, to *info*. Mode 0's are {0, 320, 200, 4, 8, 256, 1, 1, 1, 320, 200}: the display's width `+8` and height `+0xc` 320 and 200, its `+0x1c`, `+0x20` and `+0x24` 1, and its `+0x28` and `+0x2c` 320 and 200.
- **Slot 7, `VGA8_SetVirtualWidth` (`0008b39c`)**, (width, *info*), C convention, given the display's `+0x28`: stores the width rounded down to a multiple of 4 at the display's `+0x28` and 0x40000 divided by it at `+0x2c`, the rows the 256 KB of video memory hold at that width; stores width / 4, the bytes a row takes in each plane, at `0009df86`, and that less the display's width / 4 at `0009df8a`; fills the row table `0009df8e`, 800 `int16`, entry *y* being *y* × width / 4; and writes width / 8 to the CRTC's offset register (port `3D4`, index `0x13`), the distance between the monitor's rows.

`Raster_SelectRenderTarget` then copies the display's `+4`–`+0x2c` to `00098da2`–`00098dca`, which makes `00098dba`, the retrace wait of `VGA8_ShowPage`, and `00098dbe`, the pan of `VGA8_SetPageOrigin`, both 1 ([the page routines](#the-simulators-own-page-routines)).

`Sim_StartMission` follows `Sim_CreateDisplay` with `GLDisplay_SetAttribute`(display, 640, 2) ([the mission start](../simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc), step 11), which runs slot 7 again with 640: rows 160 bytes apart, 0x40000 / 640 = 409 of them, and the row table's step 160. It then moves pages 1 to 3 through slot 15 and sets the display's page pair `+0x88`/`+0x8c` to {0, 2}, or {0, 0} with `-S` ([the command line](../simulation/main-loop.md#the-command-line)):

| Page | Origin | Covers |
|---|---|---|
| 0 | (0, 0) | columns 0–319, rows 0–199 |
| 1 | (0, 200) | columns 0–319, rows 200–399 |
| 2 | (320, 0) | columns 320–639, rows 0–199 |
| 3 | (320, 200) | columns 320–639, rows 200–399 |

`Sim_EndFrame` shows one page of the pair while the frame is drawn on the other and swaps them, so a mission draws its frames alternately into the left and right halves ([the frame's end](../simulation/cockpit-views.md#the-pages-a-view-change-moves)). Page 3 is the draw page of the mission's start without `-S`. The canvas origins of the views are the same in all eight `.VUE` files, {0, 0}, {0, 197}, {320, 0} and {−320, 0} for views 0 to 3: the heads-down display lies 197 rows below the top of a half, rows 197 to 396, which the 409 rows hold, and a glance's 320 is the distance between the halves ([switching views](../simulation/cockpit-views.md#the-pages-a-view-change-moves)).

`GLDisplay_SetAttribute` (`0006a824`), (display, value, attribute) in stack order, callee-cleaned, is ES2's (DBSIM `004812ec`, whose arguments are (display, attribute, value)): attribute 2 runs slot 7 with (value, display `+4`) and, for the current display, copies `+0x28` and `+0x2c` to `00098dc6` and `00098dca`; 9, 10 and 11 store the value at `+0x1c`, `+0x20` and `+0x24` and, for the current display, at `00098dba`, `00098dbe` and `00098dc2`; 16 stores it at `00098dde`; 17 calls slot 32 (`0009df54`) with it. It returns 1, or 0 for any other attribute. `es2_xref.py` finds two calls of it: `Sim_StartMission`'s, and one with attribute 16 and the value 1 from the display's assert hook (`0006a34c`), which `GLDisplay_CtorAndOpen` installs.

### The simulator's own page routines

**Eight of the simulator's `VGA_8` routines are not in its library but among its game objects, and four of them address video memory as 640 pixels a row.** `VGA_8`'s table (`ES1DBSIM 000a7b70`) holds them in slots 15, 16, 17, 18 and 20 to 23; `VGA_4`'s (`000a7ae0`) holds the same slots 15 to 18. Each is the shell's routine with the simulator's globals unless a difference is given:

| Slot | `ES1DBSIM` | The shell's (`ES1GO`) | Difference |
|---|---|---|---|
| 15 | `VGA8_SetPageOrigin` (`0005279d`) | `00070313` | none; the row table is `0009df8e` |
| 16, 17 | `VGA8_SetSourcePage`, `VGA8_SetDrawPage` (`00052644`, `0005266a`) | `000701d4`, `000701f5` | each also stores the page ([above](#the-simulator)) |
| 18 | `VGA8_ShowPage` (`00052690`) | `00070216` | while the word `000bda96` is 1 it neither waits for the next retrace nor writes the pan; it also stores the page at `00098dd2`, the offset at `0009e608` and the pan >> 1 at `0009e614` |
| 20 | `VGA8_CopyRectBetweenPages` (`00051c3c`) | `000733bc` | a row's offset is the row × `0xa0` |
| 21 | `VGA8_ReadRow` (`00052254`) | `000739e8` | the same |
| 22 | `VGA8_WriteRow` (`00052138`) | `000738ca` | the same |
| 23 | `VGA8_WriteRowTransparent` (`00051f1c`) | `000736ac` | the same |

**Slots 21 to 23** take (pixels, *y*, *x0*, *x1*), C convention, and work a plane at a time on every fourth pixel. `VGA8_ReadRow` reads row *y*, columns *x0* to *x1*, of the source page into the pixels through the graphics controller's read map select (port `3CE` index 4). `VGA8_WriteRow` writes the pixels to that run of the draw page through the sequencer's map mask. `VGA8_WriteRowTransparent` writes them leaving a pixel whose byte is 0 as it was: in clip mode 2 (`+0x204` of the clip block `00098d64` points at) once for each region of the block's list that holds row *y*, cut to it, a rect region (type 0) to its columns and a span region (type 2) to its span on the row, passing over any other type; in any other mode once, uncut.

Where the shell's routines take a row's offset from the row table, which the driver's slot 7 fills in steps of its width argument / 4, the simulator's four multiply the row by `0xa0`, 160 bytes, the row table's step for a mission's 640-pixel screen ([the video memory](#the-video-memory)).

**Moving the visible page's origin scrolls the screen.** `VGA8_SetPageOrigin` sets the page again through `VGA8_ShowPage` when it is the visible page (`00098dd2`), which writes the page's new offset to the CRTC's start address; with `00098dbe` 1 a column that is not a multiple of 4 is reached by the attribute controller's pan, (*x* & 3) · 2. The view slides and the view shake move the visible page's origin this way ([switching views](../simulation/cockpit-views.md#the-pages-a-view-change-moves), [the view shake](../simulation/cockpit-views.md#the-view-shake)).

**The word `000bda96` decides whether `VGA8_ShowPage` waits for the next retrace and pans.** With `00098dba` 1, `VGA8_ShowPage` first waits out a vertical retrace in progress and then writes the start address; while `000bda96` is 1 it returns there, and otherwise it waits for the next retrace to start and writes the pan. `Sim_EndFrame` sets the word at its start, to 1 while `Sim_Frozen` (`000b5b92`) is 0 and to 0 while it is set; `CockpitView_StepViewTransition` sets it to 0 when it runs a command, and `CockpitView_ProcessViewCommand` after its retrace wait ([the view command](../simulation/cockpit-views.md#cockpitview_processviewcommand-000226f0)). So a running frame's page flip does not wait for the retrace to start, and each step of a view's slide, like every flip while the simulation is frozen, waits for it: a slide moves one step a vertical retrace. `es2_xref.py` finds one more writer, `PreferencesPanel_Raise` (`00057820`, `F12` and `Alt+P`), which sets it to 0 while the preferences panel is up, so each of the panel's page flips waits for a retrace ([raising the panel](../simulation/preferences.md#raising-the-panel--preferencespanel_raise-00057820)), and one reader, `VGA8_ShowPage`.

**The dword `00098d96` is 0 while `VGA8_ShowPage` runs**, which clears it on entry and sets it on return; `CockpitView_StepViewTransition` also clears it before a slide and sets it after. While it is 0, `Cursor_SyncPosition` (`000669dc`) leaves the drawn cursor alone and stores the pointer's position at `Cursor_PendingPosition` (`00098d9a`). `VGA8_ShowPage` moves [the cursor](#the-cursor) to the new page: when the cursor is shown (`00098d6c`), after writing the start address and the pan it hides it with slot 31 (`0009df50`), which puts the old page's pixels back, and copies the position `00098d70` to `00098d9a`; it stores the new page's offset and pan; and when the cursor was shown on entry it copies `00098d9a` back to `00098d70` and shows it with slot 30 (`0009df4c`) on the new page. So a pointer move during the change lands when it ends.

The code runs from `00051c3c` to `00052813` in two pieces. `VGA8_CopyRectBetweenPages` closes the code from `00050e20` ([profile files](../formats/profile-files.md#where-the-object-file-starts-and-ends)); the other three row routines, `00051f1c`–`000523a3`, stand between two zero fills. From `000523a4` to `00052813`, between zero fills at `000523a1` and `00052811`, is ES2's `DBSIM 0045c61c`–`0045c860` in order: `Vga_WaitVerticalRetrace` (`000523a4`), `Vga_WaitRetraceEnd` (`000523ae`), the monochrome component panel (`000523b8`–`00052643`, [the component panel](../simulation/debug-monitor.md#the-component-panel)), and then the four page routines.

### The cursor

**The simulator draws the mouse pointer itself on the page on the screen, saving the pixels under it first and putting them back to hide it; the shown page's change, a view's slide and a new image each hide it and show it again.** `VGA_8`'s slots 30 and 31 (`0009df4c`, `0009df50`), which `VGA_4`'s table (`000a7ae0`) also holds, are its show and hide, no arguments, each with interrupts off (`CLI` to `STI`) and doing nothing without a bitmap. They draw on the visible page: for the call each sets the draw page's offset `0009e604` and pan `0009e610` to the visible page's, `0009e608` and `0009e614`, and the show sets the source page's, `0009e600` and `0009e60c`, the same, and puts all of them back after. The state:

| Address | Holds |
|---|---|
| `Cursor_Bitmap`, `00098d68` | the cursor's bitmap header, 0 for none |
| `Cursor_Shown`, `00098d6c` | 1 while the cursor is drawn, 0 in the image |
| `Cursor_Position`, `00098d70` | the pointer's position, two `int32`, which `Cursor_SyncPosition` writes |
| `Cursor_Hotspot`, `00098d78` | the hotspot, two `int32` |
| `Cursor_SaveUnder`, `00098d80` | the save-under, a bitmap header: rows `+0` and columns `+2` the cursor's, 8 at `+4` and the flags `0x10` at `+5` from the image, the size `+6` and the pixel buffer `+0xa` (`00098d8a`) |
| `Cursor_DrawCorner`, `00098d8e` | the corner the cursor was drawn at, two `int32` |
| `00098d34` | the clip rect both pass, the rect `+0x20c` of the clip block `00098b28`, the one `00098d64` points at in the image ([Open](#open)) |

- **`VGA8_ShowCursor` (`00088728`)**, slot 30: with a bitmap and the cursor not shown, sets the shown flag, puts the corner at the position less the hotspot, reads the visible page's pixels under it into the save-under with slot 27 (`0009df40`, `VGA8_ReadBitmap`), and draws the cursor's bitmap there with slot 26 (`0009df3c`, `VGA8_BlitBitmap`), both with the clip rect and flags 0.
- **`VGA8_HideCursor` (`000887ff`)**, slot 31, no Ghidra function: with a bitmap and the cursor shown, clears the shown flag and, while the dword `00098dc2` is set, draws the save-under back at the corner with slot 26, with the clip rect and flags 0. `00098dc2` is the display's `+0x24`, which `Raster_SelectRenderTarget` copies and attribute 11 sets ([the video memory](#the-video-memory)); `VGA_8`'s mode 0 makes it 1.
- **`VGA8_ReadBitmap` (`0008be88`)**, slot 27, (header, point, clip, flags), C convention: the reverse of `VGA8_BlitBitmap`, on the same clipping and mirroring: it reads the rect of the source page at the point, a plane at a time through the graphics controller's read map select (port `3CE` index 4, put back after), into the bitmap's pixels (`+0xa`). With a clip rect and flags 0, a read cut by *T* rows at the top and *L* columns at the left starts writing at byte *T* × columns + *L* and steps a whole row of the bitmap per row, so each pixel lands at its own row and column and the bytes the clip cuts off are not written.

**Index 0.** `VGA8_BlitBitmap` skips a byte of 0, leaving the pixel, unless bit 4 of the bitmap's flags byte `+5` is set, which writes every byte ([ES2's flags](../../retail/formats/dfn-hfn-dci.md#the-bitmap-record)). The cursor files in `SIMVOL0.VOL` are ES2's bytes, flags 0 ([cursors](../formats/dfn-hfn-dci.md#cursors)), so index 0 is see-through, and the save-under's `0x10` puts back every pixel.

`Cursor_SetImage` (`00066948`), (bitmap, hotspot): hides the cursor while it is shown, stores the bitmap and the hotspot, frees the save-under's buffer and, for a bitmap, gives the save-under the bitmap's rows and columns and a new buffer of their product, and shows the cursor again when it was shown and the allocation succeeded. The mission start loads `dci\cursor` and shows it ([the mission start](../simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc), steps 5, 13 and 15); a view's slide hides it around its first-half copy and the external view hides it until its return shows it ([the view's step](../simulation/cockpit-views.md#cockpitview_stepviewtransition-00022904)).

The shell's are the same code with its own globals, the shell's slots 30 and 31 (`00086628`, `0008662c`), `VGA8_ShowCursor` (`ES1GO 00070388`) and `VGA8_HideCursor` (`ES1GO 0007045f`): the bitmap `00084420`, the shown flag `00084424`, the position `00084428`, the hotspot `00084430`, the save-under `00084438`, the corner `00084446`, the clip rect `000843ec`, and the page offsets and pans `00088ec4`–`00088ed8`; its hide puts the save-under back without a test of a display attribute. So the 8 bytes the shell's `VGA8_ShowPage` copies from `00084428` to `00084452` and back are the pointer's position.

The two routines are one object file, `00088728`–`0008886f`, between zero fills after the `RET`s at `00088724` and `0008886d`.

## Functions

| ES1GO | Address | Notes |
|---|---|---|
| `Shell_PresentFrame`, `Shell_FrameTick` | `00012980`, `00012974` | |
| `DirtyList_CreateContext`, `DirtyBox_Alloc`, `DirtyBox_Free` | `00040d54`, `00040e58`, `00040ee4` | |
| `DirtyBox_Add`, `DirtyBox_AddRect`, `DirtyList_Prune` | `00040f00`, `00041062`, `0004109c` | |
| `DirtyList_Insert`, `DirtyList_Flush` | `000411a5`, `000413db` | |
| `Shell_CreateDisplay`, `ShellDisplay_PagePointCtor` | `00045b25`, `00045d09` | `ShellDisplay_PagePointCtor` is the element constructor `__vec_ctor` builds the page origins with, returning its argument or 8 new bytes |
| `Display_CopyRectToPage` | `0004c17c` | |
| `Region_CopyRectBetweenPages`, `Region_CopySpansBetweenPages`, `Region_CopyBetweenPages` | `000519bc`, `00051aed`, `00051c40` | |
| `GLDisplay_GetAttribute`, `GLDisplay_SetAttribute` | `0005a6e4`, `0005a77c` | |
| `VGA8_SetVirtualWidth`, `VGA8_IsModeUnsupported`, `VGA8_OpenMode` | `00072ff4`, `00073130`, `00073150` | slots 7, 8 and 11, the simulator's ([the video memory](#the-video-memory)) with the shell's display fields, `VGA8_OpenMode` copying ten dwords a mode |
| `VGA8_SetSourcePage`, `VGA8_SetDrawPage`, `VGA8_ShowPage`, `VGA8_SetPageOrigin` | `000701d4`, `000701f5`, `00070216`, `00070313` | |
| `VGA8_ShowCursor`, `VGA8_HideCursor` | `00070388`, `0007045f` | slots 30 and 31 ([the cursor](#the-cursor)); `VGA8_HideCursor` has no Ghidra function |
| `VGA8_CopyRectBetweenPages` | `000733bc` | |
| `VGA8_WriteRowTransparent`, `VGA8_WriteRow`, `VGA8_ReadRow` | `000736ac`, `000738ca`, `000739e8` | slots 23, 22 and 21 ([the simulator's](#the-simulators-own-page-routines), but for the row table) |
| `SpanList_ClipToRect`, `SpanList_FromRect`, `Rect_Intersect` | `0004fd65`, `000665fc`, `0006659c` | the simulator's code (`ES1DBSIM` `00073c91`, `00073d8c`, `00073dc4`) |

## Open

- **Open:** whether a mission's dirty boxes are ever copied, and what bounds the list if they are not: `es2_xref.py` finds no reference to the simulator's `DirtyList_Flush` (`ES1DBSIM 00083e80`). The boxes of a script redrawing the same frames merge.
- **Open:** whether `Shell_CreateDisplay`'s display is the current one when it sets attribute 9, and so whether `00084472` is 2, and what else sets `00084472` and `00084476`.
- **Open:** what sets the cursor's clip rect, the rect `+0x20c` of the clip block `00098b28` (`ES1DBSIM`; the shell's `000843ec`): `es2_xref.py` finds only the cursor routines' pushes of its address, and a store through the block's base is not found by that search.
- **Open:** whether a mission calls the simulator's slots 20 to 23. A retail check: a breakpoint on `ES1DBSIM 00051c3c` during a glance.
- **Open:** what `0008b408`, which `VGA8_OpenMode` (`ES1DBSIM 0008b4f8`) calls with the mode, does to the VGA, and what `VGA_8`'s slots 9 and 10 (`000885dc`, `0008862c`) do in the open.
- **Open:** `Shell_CreateDisplay`'s `0005a388` and `0005a5d0`, and the `0x20` bytes at `+0x64`.
- **Open:** which screens `Sprite_Draw` (`000217a9`) and `Sprite_Erase` (`00021a6e`) draw, which add dirty boxes or fix rects; the menus' own boxes are [the menus'](../shell/menus.md#drawing-a-screen).
