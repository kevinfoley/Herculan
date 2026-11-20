# DBSIM.EXE — the cockpit's animation scripts (`.TTM`)

**In a mission, the cockpit plays one script, an animation drawn over the HERC's panel, through a copy of the shell's interpreter that plays no sounds, makes no random jumps and fades to black.** `ES1DBSIM`'s code has the shell's shape with the differences below; a function given a shell function's name is that function's code at the simulator's offsets, which are listed where they matter. The ten `SIMVOL0` scripts use ten opcodes, `0x0ff0`, `0x0110`, `0x1021`, `0x1051`, `0x1111`, `0x1201`, `0x2022`, `0xa504`, `0xa524` and `0xf02f`, all of them ones the simulator's interpreter has a case for. The format, and the shell's loader and interpreter that the sections below compare it with, are [`ttm-scripts.md`](../formats/ttm-scripts.md). Addresses are `ES1DBSIM` image addresses; an address given as the shell's is `ES1GO`'s ([the X-32 executable](../formats/x32-executable.md#the-image)).

## Handles

The handles live in a `0xb`-byte object: `Ttm_ManagerCtor` (`ES1DBSIM 00069540`), given the object, *a*, *b* and a count in stack order, allocates it when given none, stores the count at its byte `+4`, *b* at `0009f248`, the default render context, `000bdd28` at `0009f24c`, the display object, and *a* at `0009f250`, clears the prefix with `Ttm_SetPathPrefix` (`ES1DBSIM 00069940`, the prefix at `0009f254`), and seeds `rand` (`ES1DBSIM 0007cd28`) with `srand` (`ES1DBSIM 0007cd4a`)(`clock()`). `Ttm_SetDefaultContext` (`ES1DBSIM 0006982c`) stores a render context at `0009f248` and returns the one before.

`es2_xref.py` finds one call of `Ttm_ManagerCtor`, from `Cockpit_LoadPanel` (`ES1DBSIM 0002c9d8`, [building the cockpit](cockpit-panel.md#building-it)), which makes the manager with *a* 0, *b* the installed render context `000bdbda` and a count of 10, stores it at `CockpitTtmManager` (`00092d70`), sets the prefix `ttm\` and calls `Ttm_AllocHandles` with a timer count of 2; it then builds the panel's instruments and the [clock](cockpit-clock.md), which opens the script, and ends with a call of `Ttm_RunAll`.

`Ttm_AllocHandles` (`ES1DBSIM 00069780`), given the manager and a timer count, allocates the handle table at `+5`, a dword per handle, and zeroes it; takes `000bdd0c` into the manager's `+0`, or a new timer `DVTimer_Ctor` (`0006c6f8`)(0, `0x4da6`), which in the simulator finds the slot already taken by `Sim_StartMission`'s ([the PC timer](timer.md#the-rate)) when that is 0, freeing the table and returning 0 when there is still none; registers `Ttm_TimerTick` (`ES1DBSIM 000696f4`) with `Timer_AddCallback` at the count, storing the slot at the word `+9`, and returns 1. `Cockpit_LoadPanel`'s count of 2 at the simulator's 120 interrupts a second makes the scripts' tick 60 times a second ([the rate](timer.md#the-rate), [callbacks](timer.md#callbacks)). `Ttm_ManagerDtor` (`ES1DBSIM 00069598`) passes the slot to `Timer_RemoveCallback` (`0006d337`), closes every handle with `Ttm_CloseAll` (`ES1DBSIM 00069a04`), deletes the table, and deletes the manager when bit 0 of its flags is set.

`Ttm_Open` (`ES1DBSIM 00069848`), given the object, *a*, *b* and a name in stack order, takes the first empty handle of the table at `+5`, makes a `0x1c1`-byte script with `TtmScript_Ctor` (`ES1DBSIM 000797ac`) and loads it with `TtmScript_Load`(script, *a*, *b*, name), destroying it with `TtmScript_Dtor` (`ES1DBSIM 000799cc`) and returning `0xff` on a nonzero return; otherwise it returns the handle. `TtmScript_Ctor` constructs the twelve `0x18`-byte regions at `+0x58` with `GLRectangleRegion_Ctor` (`ES1DBSIM 00079880`, the shell's code with the vtables `0009f25a`, `0009f27e`, `0009f2a6` and `0009f2ce` for its `00082ab0`, `00082ada`, `00082b0c` and `00082b44`), sets the dwords `+0x19d` and `+0x1a5` to 0 and `+0x1ad` to 4, and zeroes `+0x1b5`, `+0x1b9` and `+0x1bd`. `GLRectangleRegion_Dtor` (`ES1DBSIM 0007999c`) is the shell's code with these vtables. `TtmScript_Dtor` frees the script with `TtmScript_Free` (`ES1DBSIM 00079f68`), which resets it and frees the script `+6`, the page table `+2` and the slot tables `+0x21` and `+0x17`; there is no sound object.

`TtmScript_Load` (`ES1DBSIM 0007a54c`):

- keeps no copy of the name, and its `TtmScript_BuildPath` (`ES1DBSIM 00079b54`) neither upper-cases the name nor rewrites `.RAW`, building in `000bbbf0`;
- returns 3 when the file stream's status after the open is not 0;
- finds the tags with `TtmScript_FindTag` (`ES1DBSIM 0007cba4`), which takes the script first and searches as `Stream_FindTag` does;
- reads one byte of the page count, its low byte;
- returns 2 when the method byte is not 1, and 1 when the script's allocation fails;
- keeps the page count at `+0` (a byte), the page table at `+2`, the script at `+6`, its last byte at `+0xa`, the two blocks at `+0x21` and `+0x17`, *a*, the mode, at `+0xe` and *b*, the loop flag, at `+0x13`;
- zeroes the two blocks' six slots each, the twelve saved-pixel slots `+0x28` and the three cycling directions, sets the once flag `+0x12`, the page `+1` and the palette slot `+0x25` to 0, the next page `+0x178` to −1, the words from the call depth `+0x17a` to the cycle's reload `+0x188` and the byte `+0x18a` to 0, and the three brush colours to 10. It sets neither the bitmap-array slot `+0x26`, the region slot `+0x27` nor the twelve regions' rects, which `TtmScript_Ctor` leaves as allocated: zero, while the heap is on, whose allocations are zero-filled ([the heap](memory.md#the-heap)), until `0x1051`, `0x1121` and `0x4214` set them;
- indexes the pages with `TtmScript_IndexPages` (`ES1DBSIM 0007a940`, through its padding entry), the shell's walk, testing the position at each page's start and after each command, and `TtmScript_SkipString` (`ES1DBSIM 00079fb0`); `RLERStream_DtorCopy` is `ES1DBSIM 0007a914`.

`Ttm_GetPage` (`ES1DBSIM 00069704`), `Ttm_GetMode` (`00069740`), `Ttm_GetLoop` (`00069760`), `Ttm_SetMode` (`0006995c`), `Ttm_SetLoop` (`0006998c`), `Ttm_Close` (`000699bc`) and `Ttm_CloseAll` are the shell's, given the manager first, with the page a byte at the script's `+1`, the mode at `+0xe` and the loop flag at `+0x13`, and `Ttm_Close` formatting no message. `Ttm_SetPage` (`ES1DBSIM 00069904`), given the manager, a page and a handle, stores the page at `+1` when it is below the page count and returns 1; it leaves the wait, the mode and the flags as they are, where the shell's resets them.

## Each frame

**The simulator runs its scripts while it draws the cockpit, once in each frame in which the timer has ticked, after the panel's instruments, so a script draws over them.** `Ttm_TimerTick` (`ES1DBSIM 000696f4`, a frameless stub) sets the byte `0009b404` to 1 and adds 1 to the word `0009b406`; unlike the shell's it has no byte that stops it, as the simulator's fades set none.

`Ttm_RunAll` (`ES1DBSIM 000695e0`) returns 0 at once while `0009b404` is 0: unlike the shell's it does not wait for a tick. Otherwise it notes `0009b406` and the installed render context `000bdbda`; stores the default context's `+4`, or 0 with none, at `+0x44` of the display object, passing it to `Raster_SetClipBlock` when the display is `000bdd28`, and installs the default context with `Raster_InstallRenderContext`; clears `0009b404`; runs each handle's script with `TtmScript_Run`(script, the noted count); does the same with the noted context; subtracts the noted count from `0009b406` and returns 1.

`es2_xref.py` finds two calls of `Ttm_RunAll`: the end of `Cockpit_LoadPanel`, and `Cockpit_RunTtmScripts` (`ES1DBSIM 0002e0ec`), which sets the installed render context's dwords `+0x20c`, `+0x220` and `+0x224` to 1, `000afa9c` and `000afaa0`, makes it the default context with `Ttm_SetDefaultContext`, calls `Ttm_RunAll`, and puts the three back. It finds two calls of `Cockpit_RunTtmScripts`: from `Cockpit_DrawFrame` (`ES1DBSIM 0002cf28`), once a frame after the panel's instruments unless the widgets are off ([drawing the cockpit](cockpit-panel.md#drawing-it)), and from `CockpitView_InvalidateWidgets` (`ES1DBSIM 0002b4a0`), last, when a view change repaints the panel.

## The simulator's run

`TtmScript_Run` (`ES1DBSIM 00079a14`), given a script and a count of ticks, installs the script's render context `+0x1bd`, when it is not 0, as the default context `0009f248` and with `Raster_InstallRenderContext`, and puts the one before back when it ends. It subtracts the ticks from the wait `+0x182`. Then:

- **While the wait is above 0:** unless the mode `+0xe` is 2, it runs the page with `TtmScript_RunPage`. There is no flag for a page that has run.
- **Once the wait is 0 or less:** it cycles colours as the shell's does (the byte `+0x18a`, the count `+0x186` and its reload `+0x188`, and `TtmScript_CyclePalette`, `ES1DBSIM 00079d90`, the shell's code with the ranges at `+0x18b`, their directions at `+0x18f`, `+0x195` and `+0x19b`, and the palettes at `+0x21` selected by `+0x25`). It reloads the wait from `+0x184`, then by the mode, a dword taken through the table `0009f2f4` after an unsigned test against 3: 0 runs the page and moves to the next; 1 runs the page; 3, with the loop flag `+0x13` 1, resets the script and sets the mode to 0, and otherwise sets the mode to 2, leaving the script as it is; 2 and any mode above 3 do nothing.

Moving to the next page is done in place, there being no `TtmScript_NextPage`: it takes the page `+0x178` when that is not −1, setting it back to −1, and otherwise the page `+1` plus 1, clearing the byte `+0x12`; when the page has reached the page count `+0`, compared as bytes, it does as mode 3 does. There is no random choice.

**A `0x0110` ends a script that does not loop.** Its page sets the mode to 3 while it runs, and the next run whose wait has run out finds mode 3 and, with the loop flag 0, sets mode 2, after which the script's pages never run again.

A page therefore runs on each tick while its wait lasts, and once more, with the wait reloaded, when it runs out. Most of the commands the shell runs on a page's first run act in the simulator only while the wait is 0 or less ("no wait" in [the table](#the-simulators-commands)), which, the wait being reloaded before the page runs, holds while the reload `+0x184` is 0 or less: until a wait command has run. The wait commands act once per page through the byte `+0x12`, which a step to the next page clears and a jump leaves set: after a page that sets a wait and jumps, the target page's wait command does not act, and the jumping page's wait stays in force until a page sets another. In `ROADRUNN.TTM` the first page loads `ROADTTM.BMP`; page 1, labelled 2, draws frame 0 at (287, 166) and, with flags 2, at (306, 166), and sets a wait of 3; pages 2 to 8 draw frames 1 to 7; page 9 draws frame 8, sets a wait of 60 and jumps to label 2. After the first pass every page of the loop therefore lasts 60 ticks ([Open](#open)).

`COLOSSUS.TTM`, read through HercWorks.Core, has the same shape, with a `0x0110` before its first page's end: page 0 is label 1, `0x1051` 0, `0xf02f` `COLOTTM.BMP`, `0x0110`; pages 1 to 9 draw frames 0 to 8 at (210, 4), page 1 labelled 2 and setting a wait of 3, and page 9 jumps to label 2. The clock opens it with the loop flag 0 ([the cockpit clock](cockpit-clock.md)), so on its first run page 0 loads the bitmap and sets mode 3, and the next run sets mode 2: no frame is drawn. It is the only one of the ten scripts with a `0x0110` ([Open](#open)). In `GUNSLING.TTM` page 17 jumps to label 2, page 1, so pages 18 and 19, labelled 3, are reached only by page 19's own jump to label 3, and a run from page 0 never draws their frames 17 and 18.

`TtmScript_Reset` (`ES1DBSIM 0007ca44`) is the shell's at the simulator's offsets, destroying the bitmap arrays with `GLBitmapArray_Dtor` (`ES1DBSIM 000673c8`, the shell's code)(array, 3), with one difference: it leaves the byte `+0x12`, where the shell's clears its once flag `+0x40`. The mode is a dword, at `+0xe` to `+0x11`. `TtmScript_FindLabel` (`ES1DBSIM 00079ef4`) is the shell's, returning a byte page and formatting no message.

## The simulator's commands

`TtmScript_RunPage` (`ES1DBSIM 0007aa58`, entered through its padding at `0007aa57`) is the shell's with these differences:

- its table at `0009ffcc` (targets at `000a0040`, the `JMP` at `0007adea`) holds the shell's opcodes in the shell's order less `0x5005`, which it steps over; its call offsets' table is the shell's four at `0009ffac` (targets at `0009ffbc`, the `JMP` at `0007ad1d`);
- it steps over every opcode `0xc000`–`0xcfff`, `0x1301` and `0x1311` among them once renumbered, so the simulator plays no script sound;
- it copies a point list with `TtmScript_CopyArgBytes` (`ES1DBSIM 00079fdc`, the shell's code with the buffer `000bbc18`);
- it hands an opcode `0xa000`–`0xafff` to `TtmScript_RunDrawCommand` (`ES1DBSIM 0007c01c`, entered through its padding at `0007c01b`), whose table at `000a00b4` (targets at `000a010c`, the `JMP` at `0007c040`) holds the shell's 22 opcodes in the shell's order, and which returns 1 for every opcode.

`Ttm_TokenName` (`ES1DBSIM 0007a050`, the `JMP` at `0007a06d`) holds the shell's 128 opcodes at `0009fb14` (targets at `0009fd14`) and the same name strings, at `0009f322`.

The script's fields are the shell's less `0x30` up to the region slot `+0x27`, the saved pixels at `+0x28` and the regions at `+0x58`, and less `0x36` from the next page `+0x178` to the brushes: the call depth `+0x17a`, its x `+0x17c` and y `+0x17e`, the base page `+0x180`, the wait `+0x182` and its reload `+0x184`, and the brushes `+0x19d`, `+0x1a5` and `+0x1ad` with their colours `+0x1a1`, `+0x1a9` and `+0x1b1`. The table gives every case; one "as the shell's" is the shell's code at these offsets.

| Opcode | Case | How it differs from the shell's |
|---|---|---|
| `0x0020` | `0007bced` | No wait, where the shell's acts on the first run; fixes (0, 0) to `GLDisplay_GetAttribute` (`0006a780`) of the display's items 0 and 1, each less 1. |
| `0x0070`, `0x00c0` | `0007b2e1`, `0007b189` | No wait, where the shell's act on the first run. |
| `0x0080` | `0007aff1` | No wait, where the shell's acts on every run; destroys the array with `GLBitmapArray_Dtor`. |
| `0x0110` | `0007beb9` | As the shell's: sets the mode `+0xe` to 3, on every run. |
| `0x0400` | `0007bc21` | As the shell's, no wait: zeroes the three directions, `+0x18a`, `+0x186` and `+0x188`. |
| `0x1021` | `0007bcbd` | Once through `+0x12`. |
| `0x1051`, `0x1121` | `0007af4e`, `0007b037` | As the shell's, on every run: the low byte of *slot* to `+0x26` or `+0x27`. |
| `0x1061` | `0007b1de` | No wait; uploads the slot's palette with `Palette_UploadWhole` where the shell's stores it at `0007fcd0` and sets `0007fcd4`. |
| `0x1101`, `0x1111` | `0007abc2` | As the shell's: nothing, the loop's next command. |
| `0x1201` | `0007bfb5` | As the shell's, on every run: `+0x178` to `TtmScript_FindLabel`(*label*). |
| `0x2002` | `0007aded` | As the shell's, on every run: `+0x1a1` and `+0x1b1` to *fg* and `+0x1a9` to *bg*, each sign-extended. |
| `0x2022` | `0007bc6c` | Once through `+0x12`; the wait is *min* + \|`rand`() mod (*max* − *min*)\|, below *max* when *max* is above *min*, where the shell's reaches *max*. The two are signed words, and with *max* = *min* the modulo is an `IDIV` by 0. |
| `0x2302`, `0x2312`, `0x2322` | `0007bb21` | As the shell's, on every run: the range record at `+0x18b` + 6 · ((opcode − `0x2302`) / 16) gets *first* and *last* − *first* + 1. |
| `0x2402` | `0007bb63` | As the shell's, no wait: `+0x18a` to 1, `+0x188` and `+0x186` to \|*speed*\|, and *speed* / \|*speed*\|, an `IDIV`, to the directions `+0x18f`, `+0x195` and `+0x19b` for bits 0, 1 and 2 of *mask*, else 0. With *speed* 0 the division is by 0. |
| `0x3003` | `0007beca` | As the shell's, on every run, with the depth `+0x17a`, the x `+0x17c` and the y `+0x17e`. |
| `0x3201` | `0007bf12` | As the shell's, on every run: the base page `+0x180`. |
| `0x3303` | `0007bf2e` | As the shell's: no wait, the depth 0, and the base page `+0x180` + *n*, signed words, not the page `+1`; it runs the page whose number is the low byte of the sum. |
| `0x4204` | `0007bdd7` | No wait, where the shell's acts on the first run; fixes the rect from (*x*, *y*) to (*x* + *w*, *y* + *h*) with its corners ordered. |
| `0x4114` | `0007b348` | On every run of the page; fades to black ([below](#the-simulators-fades)). |
| `0x4124` | `0007b73a` | No wait; fades up from black ([below](#the-simulators-fades)). |
| `0x4214` | `0007b04a` | No wait; leaves the display's and the render context's fields as they are. |
| `0x5005` | — | Stepped over. |
| `0xf01f` | `0007ae0f` | No wait; loads the bitmap with `VolRStream_CtorFromPath` and `ClassItem_LoadResource` in place, as `Resource_LoadBitmap` does, and `TtmScript_FixRect`s (0, 0) to its `+6` and `+4`. |
| `0xf02f` | `0007af5e` | No wait; destroys the array with `GLBitmapArray_Dtor` and loads in place. |
| `0xf05f` | `0007b221` | No wait; loads in place and uploads the palette with `Palette_UploadWhole`, where the shell's stores it at `0007fcd0` and sets `0007fcd4`. |
| the drawing commands | `0007c043`–`0007c9ac` | Each but `0xa601` records its box with `DirtyBox_Add` (`0008398c`) at a delay of 1, as the shell's do; the ellipse is `Raster_DrawEllipse` (`000831f4`), the sized draw `Bitmap_BlitScaled` (`00067178`) ([the simulator's drawing](../rendering/screen-pages.md#the-simulator)). The cases are `0xa0a4` `0007c043`, `0xa104` `0007c10d`, `0xa114` `0007c1d3`, `0xa404` `0007c2b9`, `0xa424` `0007c37f`, `0xaf1f` and `0xaf2f` `0007c465`, `0xa002` `0007c624`, the bitmaps `0007c674` and `0xa601` `0007c9ac`. The rect and ellipse cases build the rect from (*x*, *y*) to (*x* + *w*, *y* + *h*) with its corners ordered, so a negative *w* or *h* draws on the other side of (*x*, *y*), and record the box at (*x*, *y*), *w* + 1 by *h* + 1, as given; the line records the box of its two points with their corners ordered. |
| `0xa504` to `0xa5a7` | `0007c674` | One case for the twelve, testing the opcode's low four bits as the shell's does; adds the script's origin, the dwords `+0x1b5` to *x* and `+0x1b9` to *y*, to where a bitmap is drawn. It takes the slot from *a4* and the frame from *a3* as signed words and draws when the slot's array has more bitmaps (its `+4`) than the frame, compared signed. |
| `0xa601` | `0007c9ac` | Puts the region's saved pixels back with `Region_Restore` at (0, 0) where the shell's puts them at the region's corner, and `TtmScript_FixRect`s the region's rect without the shell's call of `00041062`. |

`TtmScript_FixRect` (`ES1DBSIM 00079c38`), given a script and a rect, sets the display's source page `+0x38` to the low byte of its draw page `+0x3c` and the draw page to `Ttm_ManagerCtor`'s *a*, which `Cockpit_LoadPanel` passes as 0, so page 0, copies the rect between them with `Region_CopyBetweenPages` (`00066914`), offset (0, 0), adds a dirty box with `DirtyBox_Add` at a delay of 0 whose width and height are the rect's *x1* and *y1*, and puts back the low bytes of the two pages ([the simulator's drawing](../rendering/screen-pages.md#the-simulator)).

### The simulator's fades

The fades work on a palette on the stack (vtable `0009ff86`) given the selected palette's fields with `GLPalette_Assign` (`ES1DBSIM 0007bfdc`, the shell's code) and its own 1 KB of colours, copied from the selected palette's. `Palette_ScaleRange` (`ES1DBSIM 0007cc24`), given the script, a percent, a last and a first colour and the two palettes, sets each colour from the first to the last inclusive of the copy to the selected palette's with each channel multiplied by `Math_Q10Multiply` by percent · 1024 / 100, and its fourth byte 1. The fades pass *first* as the first colour and *count* as the last, and read neither *colour* nor the timer; the step is 6 − *speed*, as in the shell, a word written over *speed*, and the percent is a signed word. With *speed* 6 the step is 0 and the loop never ends; with *speed* above 6 the step is negative and the percent runs away from its end until it passes the word's range and changes sign, which ends the loop.

- **Fade out:** for *percent* from 100 down to 0 by the step, scales and uploads. Then copies the full-screen region (0, 0) to (319, 199) with `Region_CopyBetweenPages` twice, with the display's source page `+0x38` and draw page `+0x3c` set as the shell's fade sets its `+0x34` and `+0x38`, and puts back their low bytes; uploads the selected palette; and destroys the copy with `GLPalette_Dtor` (`ES1DBSIM 00069258`, the shell's code)(copy, 2).
- **Fade in:** sets the copy's 256 colours to `0x01000000` and uploads it; copies the selected palette's colours in; copies the full-screen region once, with `+0x38` set to `Ttm_ManagerCtor`'s *a* and `+0x3c` to the display's `+0x34`, and puts back their low bytes; then for *percent* from 0 up to 100 by the step, scales and uploads; uploads the selected palette and destroys the copy.

None of the ten `SIMVOL0` scripts holds `0x2402` or a fade, and the one `0x2022`, on `GUNSLING.TTM`'s page 18 ([the simulator's run](#the-simulators-run)), has *min* 3 and *max* 90, so none reaches the divisions by 0 or the endless fade.

## Open

- **Open:** a retail look at `ROADRUNN.TTM`'s animation in a Roadrunner's cockpit: what it shows, and whether after its first pass each page lasts 60 ticks, a second, as the jump's kept once flag makes it ([the simulator's run](#the-simulators-run)).
- **Open:** whether a Colossus cockpit shows any animation in its panel, which by [the simulator's run](#the-simulators-run) `COLOSSUS.TTM` never draws, where the other HERCs' scripts loop a run of frames. A retail check: SINGLE MISSION's list 6, `SM_ASS01`, whose player's squad is two COLOSSUS HERCs; watch the panel around (210, 4), at the top of the screen, for a minute, and compare another chassis' cockpit, whose script draws from its first second.
- **Open:** what writes the simulator's script origin `+0x1b5` and `+0x1b9` but `TtmScript_Ctor`'s zeroes. `es2_fieldscan.py` over `ES1DBSIM 00069540`–`0007cd28` finds that store and no other, where the same scan of the clock's code finds its store of `+0x1bd`.
- **Open:** in the simulator, `es2_xref.py` finds no call of `Ttm_ManagerDtor`, `Ttm_GetPage`, `Ttm_GetMode`, `Ttm_GetLoop`, `Ttm_SetPage`, `Ttm_SetMode`, `Ttm_SetLoop` or `Ttm_Close`, and no reference to `Ttm_TokenName`.
