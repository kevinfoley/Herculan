# DBSIM.EXE and GO.EXE — animation scripts (`.TTM`)

**The shell's talking heads, its introduction and endings and its other animated screens, and a script per HERC in the simulator's cockpit, are played by scripts.** Each `.TTM` file holds a script of 16-bit drawing and timing commands, packed by run-length coding and divided into pages, with a version, the page count and a table of named tags around it. It is Dynamix's TTM format, which their adventure games of the time also use. `SHELL0.VOL` holds 113, 106 in `RES32\` and 7 in `INTRO\`; `SIMVOL0.VOL` holds 10 in `ttm\`. Each program has its own copy of the loader and the interpreter, the simulator's differing from the shell's in the points [below](#the-simulator). Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)).

## The chunks

A file is a run of chunks, each a 4-byte tag, three letters and a colon, then a `uint32` size and that many bytes of content. Bit 31 of the size marks a container, whose content is chunks in turn; the size is the content's length with the bit cleared. All 123 files hold the same five chunks:

| Chunk | Content |
|---|---|
| `VER:` | 5 bytes, `4.14` and a NUL |
| `PAG:` | 2 bytes, the page count |
| `TT3:` | the packed script: a method byte, 1 in every file; the `uint32` unpacked length; the packed bytes, the chunk's size less 5 |
| `TTI:` | a container, its size word `0x80000000` + the `TAG:` chunk's 8 + content bytes |
| `TAG:` | a word count, then that many tags, each a word id and a NUL-terminated name |

`C0_BRD.TTM`'s 204 bytes are `VER:`, `PAG:` with 4 pages, `TT3:` of `0x81` bytes holding a script of 128 bytes in 124 packed, and `TTI:` holding a `TAG:` of `0x1c` bytes: three tags, 1 `pos 1`, 2 `pos 2` and 3 `nothing`. In every file the tags' table fills its chunk exactly. The loaders read `PAG:` and `TT3:` only ([Open](#open)).

### The packing

The script unpacks through `RLERStream`, ES2's run-length filter ([the decompression filters](../../retail/runtime-library.md#the-decompression-filters)), whose `RLERStream_Read` (`000646c4`, `ES1DBSIM 00075200`) reads a control byte: with bit 7 set, a run of `control & 0x7f` copies of the byte after it; with bit 7 clear, that many literal bytes. A control byte of `0x80` still takes the byte after it, as a run of none. The filter reads a new control byte only while bytes are still wanted, so it stops reading at the unpacked length. In all 123 files that point is the end of the `TT3:` chunk.

### Pages

The unpacked script is a run of 16-bit commands, each an opcode word and its arguments, and a page is the commands up to and including the opcode `0x0ff0`. The opcode's low four bits give its arguments:

| Low bits | Arguments |
|---|---|
| 0–7 | that many words |
| 8–14 | none the walk accepts: the walk fails |
| 15, opcode `0xaf1f` or `0xaf2f` | a word *n*, then 2*n* words |
| 15, any other opcode | a NUL-terminated string, followed by one more byte when its length is even, so the next opcode starts on an even offset |

`TtmScript_IndexPages` (`000480d1`), given a loaded script, zeroes the page count's entries of the page table `+0x30` and walks the script from its start: for each page it stores the position as the page's start and steps over commands until the step past a `0x0ff0`. Before each opcode it fails when the position has reached `+0x38`, the script's last byte. It returns 0 with every page found and 2 when it fails, on that test or on low bits 8–14. `TtmScript_SkipString` (`00047571`) steps over a string. All 123 files divide into their page count's pages. What the commands do is under [The commands](#the-commands).

## The shell

The shell keeps its scripts in a table of handles. `Ttm_Configure` (`000464b0`), given *a*, *b* and a count in stack order, stores the count in the byte `000829dc`, *b* at `00082a98`, *a* at `00082a9c` and the display object `000ab616` at `00082a94`, and clears the path prefix with `Ttm_SetPathPrefix` (`0004692d`), which stores a string pointer at `00082aa0` and returns the one before. `Ttm_AllocHandles` (`0004670e`), given a timer count, allocates the table at `000829e0`, a dword per handle, and zeroes it; takes `000ab784` into `000829d8`, or a new timer `DVTimer_Ctor` (`0005d6b4`)(0, `0x4da6`) ([the PC timer](../simulation/timer.md#the-rate)) when that is 0, freeing the table and returning 0 when there is still none; and registers `Ttm_TimerTick` (`00046681`) with `Timer_AddCallback` at that count into the word `000829e4`. It compares the word, zero-extended, with −1, which it never equals, so it always registers. It returns 1. `Ttm_Shutdown` (`000464e8`) passes the slot to `Timer_RemoveCallback` (`000565b7`) under the same comparison, calls `Ttm_CloseAll` (`00046a3f`), and deletes the table.

`es2_xref.py` finds one call of each: `Shell_OpenFirstScreen` (`0003ba47`) calls `Ttm_Configure` with *a* the word `0007fccc`, page 2, the [background page](../rendering/screen-pages.md#presenting-a-frame), *b* the dword `0007fcd8` and a count of 50, `Ttm_AllocHandles` with 2, sets the prefix `res32\` and calls `TtmLists_Load` ([the animation lists](shell-file-lists.md#the-animation-lists)); `Shell_CloseScreens` (`0003c081`) calls `Ttm_Shutdown`. Of the twelve calls of `Ttm_SetPathPrefix` it finds, `Ttm_Configure`'s passes none and the other eleven pass `res32\` or `intro\`, the two `SHELL0` folders that hold scripts.

### Opening and closing

`Ttm_Open` (`0004680a`), given *a*, *b*, *c* and a name in stack order, returns `0xff` when the handle count is 0 or every handle holds a script. Otherwise it makes a script with `TtmScript_Ctor` (`00046bb8`) in the first empty handle and loads it with `TtmScript_Load`(script, *a*, *b*, *c*, name); on a nonzero return it destroys it with `TtmScript_Dtor` (`00046e2f`) and flags 3, empties the handle and returns `0xff`, and otherwise returns the handle's index. `es2_xref.py` finds calls from `ShellBackdrop_OpenGroupTtms` (`00011f19`), `Menu_OpenControlTtms` (`00037996`) (two), `Shell_PlayTtmInPlace` (`00037dce`), `Shell_PlayTtm` (`0003b8df`) and `maybe_SncList_Load`: the backdrop group's scripts ([the group's scripts](../shell/menus.md#the-backdrop)), a kind-17 control's scripts ([the message box's file](../shell/message-box.md#where-the-object-files-start-and-end)), the career's `camni` scripts ([the answer](../shell/message-box.md#the-answer)), the intro's and the endings' ([the first screen](../shell/menus.md#the-first-screen--shell_openfirstscreen-0003ba47)) and a talking head's ([loading a line](../shell/message-box.md#loading-a-line)).

`Ttm_Close` (`000469da`), given a handle, destroys its script with flags 3, empties the handle and returns 1; for an empty handle it formats `Invalid handle (%d) passed to unload.` into a stack buffer it does not use and returns 0.

A script is `0x1eb` bytes. `TtmScript_Ctor` allocates one when given none, constructs twelve `0x18`-byte regions at `+0x88` with `__vec_ctor` and `GLRectangleRegion_Ctor` (`00046c6c`), and sets the dwords `+0x1d3` and `+0x1db` to 0 and `+0x1e3` to 4, the modes of [the three brushes](#the-commands). A region is a `GLRectangleRegion` (vtable `00082b44`, over `GLRegion` `00082b0c` and `GLBase` `00082ada`): its type 0 at `+4` and its rect `{x0, y0, x1, y1}` at `+8`, which the constructor leaves as it was; `GLRectangleRegion_Dtor` (`00046dc4`), `GLRegion_Dtor` (`00046d7b`) and `GLBase_Dtor` (`00046d4f`) are the three vtables' `+0x1c` slots. `TtmScript_Dtor` returns at once for none; otherwise it calls `TtmScript_Free` (`000474bd`), destroys the twelve regions with `__vec_dtor` and `00046e73`, three instructions that jump to an object's `+0x1c` slot, and deletes the script when bit 0 of the flags is set. `TtmScript_Free` resets the script with [`TtmScript_Reset`](#running-a-script), frees the unpacked script `+0x34`, the page table `+0x30` and the slot tables `+0x51` and `+0x47`, and destroys the sound object `+0x1a8` (its `+0x3b` table's `+8` slot with 3).

### Loading

`TtmScript_Load` (`00047c28`), given a script, *a*, *b*, *c* and a name in stack order:

1. copies the name to `+4`;
2. builds the path with `TtmScript_BuildPath` (`00047138`), which upper-cases the name in place, rewrites the first `.SCR` to `.DBM`, `.BMP` to `.DBA`, `.PAL` to `.DPL` and `.RAW` to `.RAW`, and returns the prefix, when there is one, followed by the name, in the buffer `000a0a00`;
3. opens the path with `VolRStream_CtorFromPath`, and formats `Error opening ttm script file <<%s>>.` with it into a stack buffer it does not use;
4. finds `PAG:` with `Stream_FindTag`, reads the size and the page count word; finds `TT3:`, reads the size, the method byte and the unpacked length; it uses neither size nor the method byte;
5. allocates the unpacked length at `+0x34` and reads that many bytes into it through an `RLERStream` on the file stream (`RLERStream_CtorOnSource`);
6. when the file stream's status is not 0, frees `+0x34`, destroys the two streams and returns 1. This is the only test of the open;
7. closes the file stream, stores the page count at `+0`, allocates the page table of page count + 1 dwords at `+0x30`, stores the script's last byte, `+0x34` + the length − 1, at `+0x38`, and allocates two `0x18`-byte blocks at `+0x51` and `+0x47`. When an allocation fails it frees the four and returns 1;
8. indexes the pages with `TtmScript_IndexPages`, sets the fields below, destroys the streams through `RLERStream_DtorCopy` (`0004809c`, `RLERStream_Dtor`'s code with the module's copy of the vtable, `000837ee`) and `VolRStream_Dtor`, and returns the index's result, 0 or 2.

| Offset | Set to | Holds |
|---|---|---|
| `+0x02` | 0, a word | the current page |
| `+0x2c` | *c* | the render context the script draws through; with 0, the default `00082a98` |
| `+0x3c` | *a* | the mode: 0 playing, 1 paused, 2 hidden, 3 finished, named by the strings of `Ttm_DebugStatus` (`00046a9d`) |
| `+0x40` | 0, a byte | set once a wait command has set this page's wait |
| `+0x41` | 0, a byte | set once `0xc220` has played this page's sound |
| `+0x43` | *b* | the loop flag: with 1 the script starts again when it ends |
| `+0x47` | six dwords of 0 | the six bitmap-array slots |
| `+0x51` | six dwords of 0 | the six palette slots |
| `+0x55` | 0, a byte | the selected palette slot |
| `+0x58` | twelve dwords of 0 | the saved pixels of the twelve regions at `+0x88` |
| `+0x1a8` | 0, a dword | the sound object `0xc21f` makes |
| `+0x1ac` | −1, a word | the page a jump (`0x1201`) goes to next; −1 for none |
| `+0x1ae` | −1, a word | the page a random choice (`0x5005`) goes to next; −1 for none |
| `+0x1b0` | 0, a word | the call depth of `0x3003` and `0x3303` |
| `+0x1b2`, `+0x1b4` | 0, words | the x and y a call adds to drawing commands |
| `+0x1b6` | 0, a word | the base page of `0x3303` |
| `+0x1b8`, `+0x1ba` | 0, words | the wait's count of timer ticks, and the count it reloads from |
| `+0x1bc`, `+0x1be` | 0, words | the colour cycle's count of ticks, and the count it reloads from |
| `+0x1c0` | 0, a byte | 1 while colours cycle |
| `+0x1c5`, `+0x1cb`, `+0x1d1` | 0, words | the directions of the three colour ranges, six-byte records at `+0x1c1` of `{int16 first, int16 count, int16 direction}` |
| `+0x1d7`, `+0x1df`, `+0x1e7` | 10, dwords | the colours of the three brushes at `+0x1d3`, `+0x1db` and `+0x1e3` |

The byte `+0x42`, set once the page has run, the bitmap-array slot selected `+0x56` and the region slot selected `+0x57` are not set by the load, and `TtmScript_Ctor` takes the script from `Mem_New` uncleared. `maybe_SncList_Load` opens a talking head's script on its player's `+0x15` render context as *c* ([loading a line](../shell/message-box.md#loading-a-line)).

### Choosing a page

`Ttm_SetPage` (`000468fc`), given a page and a handle, calls `TtmScript_SetPage` (`000470e7`) on the handle's script and returns its result, or 0 for an empty handle; it does not test the handle against the count. `TtmScript_SetPage`, when the page is below the page count, stores it at `+2`, copies the word `+0x1ba` to `+0x1b8`, clears the bytes `+0x40`, `+0x41` and `+0x42` and the dword `+0x3c`, and returns 1; otherwise it returns 0. A talking head's [lip-sync frame](../shell/message-box.md#playing-it) is the page its script is set to.

`Ttm_SetMode` (`00046948`) and `Ttm_SetLoop` (`00046991`), given a value and a handle, store the value at the handle's script's mode `+0x3c` and loop flag `+0x43` and return 1, or 0 for an empty handle. `es2_xref.py` finds 24 calls of `Ttm_SetMode` and none of `Ttm_SetLoop`. `Ttm_GetPage` (`0004669f`) returns a handle's page, or `0xffff` for an empty handle; `Ttm_GetMode` (`000466cc`) and `Ttm_GetLoop` (`000466ed`) return its mode and loop flag without testing the handle for a script. `es2_xref.py` finds four calls of `Ttm_GetMode`, from `Shell_PlayTtmInPlace` (`00037dce`) and `Shell_PlayTtm` (`0003b8df`), and none of the other two.

`Ttm_RunScript` (`000465da`), given a handle, returns 0 for −1. Otherwise it clears the byte `000829d5`, calls [`TtmScript_Run`](#running-a-script) with the handle's script, when there is one, and the word `000829d6`; stores the render context `000ab612`'s `+4`, or 0 with none, at `+0x40` of the display object `00082a94`, passing it to `Raster_SetClipBlock` when `000ab7a0` is that object, installs `000ab612` with `Raster_InstallRenderContext`, sets `000829d6` to its value less the value it had on entry, and returns 1. `Ttm_SetDefaultContext` (`000467ef`) stores the default render context `00082a98` and returns the one it replaces; `es2_xref.py` finds calls from `DigitalDialog_ShowMessage`, `Shell_PlayTtm` (`0003b8df`) and `Shell_DrawScreen` (`0003c1b7`). `Ttm_CloseAll` destroys every handle's script with flags 3 and empties the handle.

## Running a script

**The shell redraws its animations on a timer.** Each time the timer fires, every open script runs its current page again from the top, and when the page's wait has run out it moves to the next page, or to the page a jump picked. A script that runs past its last page stops running, or starts again from its first page when its loop flag is set.

### The timer

`Ttm_AllocHandles` registers `Ttm_TimerTick` (`00046681`) with `Timer_AddCallback` at a count of 2, so the tick comes on every second interrupt ([callbacks](../simulation/timer.md#callbacks)) of the PC timer `Shell_InitDevices` built first, at 120.05 interrupts a second: 60 times a second ([the devices](../shell/startup.md#the-devices)). `Ttm_AllocHandles` takes that timer, GL slot 0's, and builds one of its own at the divisor `0x4da6` only when there is none ([the rate](../simulation/timer.md#the-rate)). The tick returns at once while the byte `000829d4` is set, which the [fade commands](#the-commands) do while they run; otherwise it sets the byte `000829d5` and adds 1 to the word `000829d6`, the ticks to be run.

`Ttm_RunAll` (`0004652e`) waits in a loop until `000829d5` is set, then notes `000829d6` (`00046540`), clears `000829d5` and runs every handle's script with `TtmScript_Run`(script, the noted value). It restores the render context as `Ttm_RunScript` does, subtracts the noted value from `000829d6` (`000465cc`), and returns 1, so ticks that come during the runs stay pending for the next call. `es2_xref.py` finds calls from `Shell_Main`, `MessageBox_Run` (`000382f6`) and `Shell_DrawScreen` (`0003c1b7`). `Ttm_RunScript` runs one handle's script without waiting.

### A run

`TtmScript_Run` (`00046f2c`), given a script and a count of ticks, installs the script's render context `+0x2c`, or the default `00082a98` when that is 0, as `Ttm_RunScript` installs `000ab612`, and subtracts the ticks from the wait `+0x1b8`. Then:

- **While the wait has not run out, or the page has not run yet** (`+0x42` is 0): unless the mode is 2 it runs the page with [`TtmScript_RunPage`](#the-commands) and sets `+0x42`. When the wait is then 0 or less, it reloads the wait from `+0x1ba` and moves to the next page.
- **Once the wait has run out on a page that has run:** while colours cycle (`+0x1c0` is 1) it subtracts the ticks from `+0x1bc`, and when that falls below 0 reloads it from `+0x1be` and steps the cycle. It reloads the wait from `+0x1ba`, then by the mode: 0 runs the page and moves to the next; 1 runs the page; 3, with the loop flag 1, resets the script and sets the mode to 0, and otherwise sets the mode to 2, leaving the script as it is; 2 and any other mode do nothing.

It returns 1. A page therefore runs once each tick while its wait lasts: the commands that load, save or set the wait test `+0x42` or `+0x40` and act on the first run only, and the drawing commands draw on every run.

`TtmScript_NextPage` (`00046e7c`) takes the page `+0x1ac` when it is not −1, setting it back to −1; else `+0x1ae` likewise; else the current page plus 1, unless the mode is 3, when the page stays. It clears `+0x40`, `+0x41` and `+0x42`. When the page has reached the page count, the two compared as signed words, as `TtmScript_FindLabel` also compares them, it does as mode 3 does: with the loop flag 1 it resets the script and sets the mode to 0, and otherwise it only sets the mode to 2.

`TtmScript_Reset` (`0004b3c6`) destroys the six palettes (their `+0x1c` slot with 3) and the six bitmap arrays (`GLBitmapArray_Dtor`, `0005343c`) and frees the twelve saved regions, zeroing every slot; zeroes the three cycling directions; sets the mode to 2, `+0x40`, `+0x41`, the palette slot `+0x55` and `+0x1c0` to 0, the page to 0, `+0x1ac` to −1 and the words `+0x1b0` to `+0x1be` to 0; and the three brush colours to 10. It leaves `+0x1ae` and `+0x42`.

### Labels

`TtmScript_FindLabel` (`0004741a`), given a script and a label, returns the first page, in page order, whose first command is `0x1101` or `0x1111` with that label; with none it formats `File: %s Can't find label %d` into a stack buffer it does not use and returns 0. The jump, call and random-choice commands name their pages by label. In `C0_BRD.TTM` the `TAG:` chunk's three ids are the labels of pages 1 to 3.

### Cycling colours

`TtmScript_CyclePalette` (`000472d0`) rotates each of the three colour ranges whose direction *d* is not 0 by one place in the selected palette, then uploads it with `Palette_UploadWhole`. For a range of *count* colours from *first*, the base is *first*, or *first* + *count* − 1 when *d* is −1; it keeps colour *count*·*d* + base − *d*, then for *c* from *count* down to 0 copies colour *c*·*d* + base − 2*d* to *c*·*d* + base − *d*, and stores the kept colour at the base. With *d* = 1 each colour moves up one place and the last wraps to the first; with *d* = −1 the other way. The step for *c* = 0 also copies the colour two places outside the range, on the side the colours move from, to the one just outside it.

## The commands

**A page loads pictures, palettes and sounds, draws, waits, and picks the page to show next.** The 123 files use 33 opcodes. The shell's interpreter has a case for 30 of them and steps over the other three, which appear once each: `IINTRO.TTM`'s `0x1071` and its `0xf04f` naming `IFONT_01.FNT`, and `CREDIT_A.TTM`'s `0xf10f` naming `producer`. The opcode's low four bits give its arguments as in [Pages](#pages).

### The interpreter

`TtmScript_RunPage` (`00048218`), given a script and a page, runs the page's commands from its start in the page table `+0x30`. For each command it:

1. renumbers `0x1301` to `0xc051` and `0x1311` to `0xc061`, and at `0x0ff0` returns 1;
2. reads the seven words after the opcode as arguments *a1* to *a7*, whatever the opcode's count, and steps past the arguments by the count: for a point list (`0xaf1f`, `0xaf2f`) past the word *n* and 2*n* words, copying the 4*n* bytes, the count truncated to a byte and at most 100, to the buffer `000a0a28` with `TtmScript_CopyArgBytes` (`0004759d`); for any other string past it with `TtmScript_SkipString`;
3. while a call is running (the depth `+0x1b0` is not 0), adds the call's x `+0x1b2` and y `+0x1b4` to *a1* and *a2* of an opcode `0xa000`–`0xafff` other than `0xa601`, `0xaf1f` and `0xaf2f`, and to *a3* and *a4* as well for `0xa0a4`;
4. hands an opcode `0xc000`–`0xcfff` to `TtmScript_RunSoundCommand` (`0004a0e0`) and one `0xa000`–`0xafff` to `TtmScript_RunDrawCommand` (`0004a213`), with the script, the string or point list, *a1* to *a7* and the opcode, returning 0 if they do, which they never do; finds any other in its table of 30 at `00083880` (targets at `000838f8`), and steps over an opcode it does not find.

The opcode switches are Symantec's `REPNE SCASD` form, a table of opcode dwords followed by a table of targets, with the indirect `JMP` at `000483db` (the call offsets' table, `00083860`), `000484ff` (`00083880`) and `0004a232` (`000839b8`); the `0x5005` case adds an indexed table at `00083970`. `TtmScript_RunSoundCommand` tests three opcodes with `CMP`. `Ttm_TokenName` (`00047618`) holds Dynamix's names for 128 opcodes, its `REPNE SCASD` table at `000833b0`; `es2_xref.py` finds no reference to it. The names below are its strings.

Some commands act only on a page's first run, while `+0x42` is 0 ("first run"), as loads and saves; the wait commands act once per page through `+0x40` ("once"); some act only while the wait `+0x1b8` is 0 or less ("no wait"). The others act on every run.

| Opcode | Name | Arguments | In the files | Case | What it does |
|---|---|---|---|---|---|
| `0x0ff0` | `TOKEN_ENDFRAME` | — | yes | `00049fe5` | Ends the page. |
| `0x0020` | `TOKEN_FIXBACK` | — | yes | `000499d3` | First run: `TtmScript_FixRect` of the whole display, (0, 0) to its width and height less 1, `GLDisplay_GetAttribute` (`0005a6e4`) items 0 and 1. |
| `0x0070` | `TOKEN_FREEPAL` | — | no | `00048bd1` | First run, when the slot holds a palette: destroys it (its `+0x1c` slot with 3) and empties the slot; `0007fcd0` keeps pointing at it. |
| `0x0080` | `TOKEN_FREESHAPE` | — | yes | `00048832` | Destroys the selected bitmap array with `GLBitmapArray_Dtor` and empties the slot. |
| `0x00c0` | `TOKEN_FREEPUT` | — | no | `00048a9c` | First run: frees the selected region's saved pixels and empties the slot. |
| `0x0110` | `TOKEN_ENDLABEL` | — | yes | `00049d3d` | Sets the mode to 3, finished. |
| `0x0400` | `TOKEN_STOP_CYCLE` | — | no | `00049913` | No wait: zeroes the three cycling directions, `+0x1c0`, `+0x1bc` and `+0x1be`. |
| `0x1021` | `TOKEN_WAIT` | ticks | yes | `000499a4` | Once: sets the wait and its reload to *ticks*. |
| `0x1051` | `TOKEN_SHAPESELECT` | slot | yes | `000487b2` | Selects bitmap-array slot *slot* (`+0x56`). |
| `0x1061` | `TOKEN_PALSELECT` | slot | yes | `00048af9` | First run: selects palette slot *slot* (`+0x55`); when it holds a palette, stores it at `0007fcd0` and sets the byte `0007fcd4`. |
| `0x1101`, `0x1111` | `TOKEN_LLABEL`, `TOKEN_GLABEL` | label | yes | `0004824e` | Nothing: they mark a page's [label](#labels). |
| `0x1121` | `TOKEN_PUTSELECT` | slot | yes | `00048881` | Selects region slot *slot* (`+0x57`). |
| `0x1201` | `TOKEN_JUMP_LABEL` | label | yes | `00049e3c` | Sets the next page `+0x1ac` to the page labelled *label*. |
| `0x1301`, `0x1311` | none; `0xc051` and `0xc061` are `TOKEN_START_SX` and `TOKEN_STOP_SX` | one word | no | — | Nothing: renumbered to `0xc051` and `0xc061`, which `TtmScript_RunSoundCommand` passes over. |
| `0x2002` | `TOKEN_PALETTE` | fg, bg | yes | `00048502` | Sets the brush colours: `+0x1d7` and `+0x1e7` to *fg*, `+0x1df` to *bg*. |
| `0x2022` | `TOKEN_RANDOM_WAIT` | min, max | yes | `0004995a` | Once: sets the wait and its reload to *min* + `Math_RandomBelow`(`0009aec0`, *max* − *min* + 1), the bound a 16-bit word. |
| `0x2302`, `0x2312`, `0x2322` | `TOKEN_SET_RANGE0` to `2` | first, last | no | `000497f2` | Sets colour range (opcode − `0x2302`) / 16 to *first* and *last* − *first* + 1 colours. |
| `0x2402` | `TOKEN_CYCLE` | mask, speed | no | `0004983d` | No wait: sets `+0x1c0`, the cycle count and its reload to \|*speed*\|, and each range's direction to *speed* / \|*speed*\| when bit *i* of *mask* is set, else 0: `labs` of the sign-extended *speed* and an `IDIV`, so *speed* 0 divides by 0, as the simulator's does. |
| `0x3003` | `TOKEN_CALL_LABEL` | x, y, label | yes | `00049d4c` | Raises the call depth, sets the call's x and y, runs the page labelled *label* with `TtmScript_RunPage`, and lowers the depth. |
| `0x3201` | `TOKEN_CALL_START` | label | no | `00049d92` | Sets the base page `+0x1b6` to the page labelled *label*. |
| `0x3303` | `TOKEN_CALL_OFFSET` | x, y, n | no | `00049daf` | No wait, no call running, and the base page + *n* not the current page: calls that page as `0x3003` does. |
| `0x4114` | `TOKEN_FADE_OUT` | first, count, colour, speed | yes | `00048c35` | First run: fades colours *first* to *first* + *count* toward the palette's colour *colour* ([below](#the-fades)). |
| `0x4124` | `TOKEN_FADE_IN` | first, count, colour, speed | yes | `0004933b` | First run: fades the same colours up from colour *colour* ([below](#the-fades)). |
| `0x4204` | `TOKEN_FIX_RECT` | x, y, w, h | yes | `00049b8e` | First run: `TtmScript_FixRect` of (*x*, *y*) to (*x* + *w*, *y* + *h*), the rect's corners ordered, as the simulator's. |
| `0x4214` | `TOKEN_GET` | x, y, w, h | yes | `00048892` | First run: frees the selected region's pixels, sets its rect to (*x*, *y*) to (*x* + *w*, *y* + *h*), allocates `Region_PixelCount` bytes and saves the pixels with `Region_Save`, with the display's source page `+0x34` set to `Ttm_Configure`'s *a*, the background, and the installed render context's clip mode `+0x208` to 0 while it does; then puts both back, the source page's whole old dword. |
| `0x5005` | `TOKEN_HOTSPOT` | five words | yes | `00049e59` | The [random choice](#the-random-choice). |
| `0xf01f` | `TOKEN_SCRNLOAD` | name | yes | `0004857b` | First run: loads the name with `TtmScript_BuildPath` (`.SCR` to `.DBM`) and `Resource_LoadBitmap` (`0004b79b`), draws it at (0, 0) with `Bitmap_Blit`(bitmap, 0, point), `TtmScript_FixRect`s (0, 0) to (its `+6`, its `+4`), the corners ordered, and destroys it; it does not test that the load gave a bitmap. |
| `0xf02f` | `TOKEN_SHAPELOAD` | name | yes | `000487c3` | First run: destroys the selected bitmap array and loads the name (`.BMP` to `.DBA`) into the slot with `Resource_LoadBitmapArray` (`0004b7df`). |
| `0xf05f` | `TOKEN_PALLOAD` | name | yes | `00048b4d` | First run: destroys the selected palette, loads the name (`.PAL` to `.DPL`) into the slot with `Resource_LoadPalette` (`0004b713`), stores its answer at `0007fcd0` and sets `0007fcd4`, also when the load gives none. |

`TtmScript_RunDrawCommand` (`0004a213`) finds its 22 opcodes in the table at `000839b8` (targets at `00083a10`) and returns 1 for every opcode. The pen is the render context's `+0x228`, the fill brush its `+0x22c`, each pointed at one of the script's three brushes: `+0x1d3` (mode 0, the colour *fg*), `+0x1db` (mode 0, *bg*) and `+0x1e3` (mode 4, *fg*), which [outlines](../rendering/screen-pages.md#ellipses-outlines-and-scaled-bitmaps) what the others fill. Each draw but `0xa601`'s records the box it drew with `DirtyBox_Add` (`00040f00`) at a delay of 1, so that the background is copied back over it when its page is next drawn on ([dirty boxes](../rendering/screen-pages.md#dirty-boxes)): the line's from its two points with the corners ordered, (*x0*, *y0*, *x1* − *x0* + 1, *y1* − *y0* + 1); the rect's and ellipse's (*x*, *y*, *w* + 1, *h* + 1) as given, while the drawn rect has its corners ordered; the polygons' from the points' least and greatest *x* and *y*, the least starting at `0x7fff` and the greatest at 0, (*minx*, *miny*, *maxx* − *minx* + 1, *maxy* − *miny* + 1); the pixel's (*x*, *y*, 1, 1); a bitmap's (*x*, *y*, its `+6`, its `+4`); a sized bitmap's (*x*, *y*, *w*, *h*).

| Opcode | Name | Arguments | In the files | Case | What it does |
|---|---|---|---|---|---|
| `0xa002` | `TOKEN_SET_PIXEL` | x, y | no | `0004accc` | Pen `+0x1d3`; `Gfx_PlotPixel` at (*x*, *y*). |
| `0xa0a4` | `TOKEN_LINE` | x0, y0, x1, y1 | yes | `0004a235` | Pen `+0x1d3`; `Raster_DrawLine` from (*x0*, *y0*) to (*x1*, *y1*). |
| `0xa104` | `TOKEN_RECTFILL` | x, y, w, h | yes | `0004a3b3` | Fill `+0x1db`; `Raster_FillRect` of (*x*, *y*) to (*x* + *w*, *y* + *h*); then `0xa114`'s case. |
| `0xa114` | `TOKEN_RECT` | x, y, w, h | no | `0004a562` | Fill `+0x1e3`; `Raster_FillRect` of the same rect. |
| `0xa404` | `TOKEN_CIRCFILL` | x, y, w, h | no | `0004a731` | Fill `+0x1db`; `Raster_DrawEllipse` (`00055b9c`) with the rect; then `0xa424`'s case. |
| `0xa424` | `TOKEN_CIRCLE` | x, y, w, h | no | `0004a8e0` | Fill `+0x1e3`; `Raster_DrawEllipse` with the rect. |
| `0xa504`, `0xa514`, `0xa524`, `0xa534` | `TOKEN_DRAWBRUSH0` to `3` | x, y, frame, slot | `0xa504`, `0xa524` | `0004ad68` | When bitmap-array slot *slot*, a signed word indexing `+0x47` with no range test, holds an array of more than *frame* bitmaps, compared signed with its `+4`: `Bitmap_Blit` of bitmap *frame* at (*x*, *y*) with flags 0 to 2 from bits 4 to 7 of the opcode, 3 for any other value. |
| `0xa505`, `0xa515`, `0xa525`, `0xa535` | `TOKEN_RLEDRAWBRUSH0` to `3` | five words | no | `0004ad68` | Nothing past the slot test. |
| `0xa506`, `0xa516`, `0xa526`, `0xa536` | `TOKEN_SIZEDRAWBRUSH0` to `3` | x, y, frame, slot, w, h | no | `0004ad66`, `0004ad68` | After the same test: `Bitmap_BlitScaled` (`000503f0`)(bitmap, (*w*, *h*), flags, (*x*, *y*)), the bitmap [stretched](../rendering/screen-pages.md#ellipses-outlines-and-scaled-bitmaps) to *w* by *h*. |
| `0xa5a7` | `TOKEN_DRAW_ROT_BRUSH` | x, y, frame, slot, w, h, angle | no | `0004ad64` | After the same test: `Bitmap_BlitRotatedUnpacked` (`0005048c`) of the bitmap with *angle*, the size (*w*, *h*) and flags 0, its corner at (*x*, *y*) plus *p* turned, *p* = (−(*w* >> 1), −(*h* >> 1)): `Transform2D_ApplyToPoint` (`000643f0`) turns *p* through `Math_BuildRotation2D`(*angle*) and adds the transform's translation, set to *p*, and the case adds that to (*x*, *y*) less *p*. So the turned bitmap is centred on (*x*, *y*), filled through brush mode 5 ([the map's 2D branch](../shell/map.md#the-shapes)). Its box is the square 2*m* on a side from the corner less *m*, *m* the larger of *w* and *h*, in words. |
| `0xa601` | `TOKEN_NEW_PUT` | one word, unread | yes | `0004b298` | No wait: puts the selected region's saved pixels back at its rect with `Region_Restore`, `TtmScript_FixRect`s the rect, and adds it with `DirtyBox_AddRect` (`00041062`) at a delay of 1. |
| `0xaf1f` | `TOKEN_POLY` | n, *n* x, *n* y | no | `0004aaaf` | Builds *n* points, the x values then the y values; fill `+0x1e3`; `Raster_DrawPolygonEitherWinding`. |
| `0xaf2f` | `TOKEN_POLYFILL` | as `0xaf1f` | no | `0004aaaf` | As `0xaf1f`, drawing first with fill `+0x1db`. |

`TtmScript_RunSoundCommand` (`0004a0e0`) returns 1 for every opcode, and acts only while the byte `0007fcb9` is set and the page has not played its sound (`+0x41` is 0):

| Opcode | Name | Arguments | In the files | What it does |
|---|---|---|---|---|
| `0xc102` | `TOKEN_VOLUME_SX` | two words | yes | Nothing. |
| `0xc21f` | `TOKEN_HDS_LOAD` | name | yes | Destroys the sound object `+0x1a8`; puts the name through `TtmScript_BuildPath` and, when the sound manager `00082768` exists and `SoundManager_SetSampleName`(manager, `0xe`, path) succeeds, stores `SoundManager_CreateObject`(manager, 2, 0, `0xe`) at `+0x1a8`. |
| `0xc220` | `TOKEN_HDS_PLAY` | — | yes | With a sound object: calls its `+0x2c` slot with `Math_RandomBelow`(`0009aec0`, 25) + 10, as a byte, and its `+0x10` slot, and sets `+0x41`. |

`TtmScript_FixRect` (`000472a2`), given a script and a rect, calls `Display_CopyRectToPage` (`0004c17c`)(`Ttm_Configure`'s *a*, the display's draw page `+0x38`, rect), which copies the rect into the background page and queues it for the other page ([fixing a rect](../rendering/screen-pages.md#fixing-a-rect)).

### The random choice

`TtmScript_RunPage` keeps five words and a flag on its stack, each call starting them as the data at `00083854`–`0008385d` with the flag cleared. A `0x5005` with the flag clear, or with `+0x1ae` not −1, stores the running sums of its five arguments, 16-bit, and sets the flag. A later `0x5005` in the same call, with `+0x1ae` still −1, draws *r* = `Math_RandomBelow`(`0009aec0`, the fifth sum), takes the first of the five sums above *r*, compared signed, and sets the next page `+0x1ae` to the page labelled by its own argument in that place; with none above *r* it takes its first argument. `C0_BRD.TTM`'s first page loads `c0_brd.BMP`, then gives a first `0x5005` the weights 20, 20, 20, 0, 0 and a second the labels 1, 2, 3, −1, −1: the script goes on to page 1, 2 or 3, a third of the time each, and each of those draws two frames, fixes a rect and ends with `0x0110`.

### The fades

Both fades set the byte `000829d4`, which stops the timer counting ticks, and clear it when they end. They work on a copy of the selected palette with its own 1 KB of colours, made with `GLPalette_CopyCtor` (`00049fee`) for the fade out and `GLPalette_Assign` (`0004a08c`) for the fade in, and blend with `Palette_BlendRange` (`0004b532`): colours *first* to *first* + *count* inclusive of the selected palette, each channel set to colour *colour*'s plus *percent* / 100 of its difference from it, and the fourth byte 1, written to the copy, which `Palette_UploadWhole` then uploads. The target colour is read from the selected palette, not the copy, on every call; the percent is a signed byte; the loop index is a word from *first*'s byte to *first* + *count*, both bytes. The step is 6 − *speed*. `Palette_UploadWhole` goes through the shell's driver slot 13 (`000865e4`), `VGA8_WritePaletteRange` (`00070130`), which waits for a vertical retrace before it writes the DAC, so each step's upload waits for one.

- **Fade out:** for *percent* from 100 down to 0 by the step, blends and uploads. Then, with the display's source page `+0x34` set to the low byte of its draw page `+0x38` and the draw page to its visible page `+0x30`, copies a full-screen region (0, 0) to (319, 199) with `Region_CopyBetweenPages` (`00051c40`); with the draw page set to `Ttm_Configure`'s *a*, the background, copies it again; puts the two pages back, each as the low byte of its old value, and adds no dirty box for either copy; uploads the selected palette; and destroys the copy with `GLPalette_Dtor` (`0005a824`). The picture drawn is thus put on the screen and into the background ([the pages](../rendering/screen-pages.md#the-pages)).
- **Fade in:** blends and uploads at 0 percent (the 256 it passes, taken as a byte); with the source page set to *a* and the draw page to the visible page, copies the full-screen region, putting the background on the screen, and puts the two back; then for *percent* from the step up to 100 by the step, blends and uploads; and uploads the selected palette.

`Palette_BlendRange` shifts each channel's product as unsigned, so a channel below the target's makes a negative product whose shift sets bits above that channel.

## The simulator

**In a mission, the cockpit plays one script, an animation drawn over the HERC's panel, through a copy of the shell's interpreter that plays no sounds, makes no random jumps and fades to black.** `ES1DBSIM`'s code has the shell's shape with the differences below; a function given a shell function's name is that function's code at the simulator's offsets, which are listed where they matter. The ten `SIMVOL0` scripts use ten opcodes, `0x0ff0`, `0x0110`, `0x1021`, `0x1051`, `0x1111`, `0x1201`, `0x2022`, `0xa504`, `0xa524` and `0xf02f`, all of them ones the simulator's interpreter has a case for.

### Handles

The handles live in a `0xb`-byte object: `Ttm_ManagerCtor` (`ES1DBSIM 00069540`), given the object, *a*, *b* and a count in stack order, allocates it when given none, stores the count at its byte `+4`, *b* at `0009f248`, the default render context, `000bdd28` at `0009f24c`, the display object, and *a* at `0009f250`, clears the prefix with `Ttm_SetPathPrefix` (`ES1DBSIM 00069940`, the prefix at `0009f254`), and seeds `rand` (`ES1DBSIM 0007cd28`) with `srand` (`ES1DBSIM 0007cd4a`)(`clock()`). `Ttm_SetDefaultContext` (`ES1DBSIM 0006982c`) stores a render context at `0009f248` and returns the one before.

`es2_xref.py` finds one call of `Ttm_ManagerCtor`, from `Cockpit_LoadPanel` (`ES1DBSIM 0002c9d8`), which `CockpitViewManager_LoadViews` calls with the HERC's name. It passes `ResourcePath_BuildFolderName`(`00098110`'s folder, name) to `0002b5c4`, keeping the result at `00092d3c`, from which it builds the panel's objects; makes the manager with *a* 0, *b* the installed render context `000bdbda` and a count of 10, stores it at `00092d70`, sets the prefix `ttm\`, calls `Ttm_AllocHandles` with a timer count of 2, builds the objects (among them the [clock](#the-cockpit-clock), stored at its `+0x1f9`), and ends with a call of `Ttm_RunAll`.

`Ttm_AllocHandles` (`ES1DBSIM 00069780`), given the manager and a timer count, allocates the handle table at `+5`, a dword per handle, and zeroes it; takes `000bdd0c` into the manager's `+0`, or a new timer `DVTimer_Ctor` (`0006c6f8`)(0, `0x4da6`), which in the simulator finds the slot already taken by `Sim_StartMission`'s ([the PC timer](../simulation/timer.md#the-rate)) when that is 0, freeing the table and returning 0 when there is still none; registers `Ttm_TimerTick` (`ES1DBSIM 000696f4`) with `Timer_AddCallback` at the count, storing the slot at the word `+9`, and returns 1. `Cockpit_LoadPanel`'s count of 2 at the simulator's 120 interrupts a second makes the scripts' tick 60 times a second ([the rate](../simulation/timer.md#the-rate), [callbacks](../simulation/timer.md#callbacks)). `Ttm_ManagerDtor` (`ES1DBSIM 00069598`) passes the slot to `Timer_RemoveCallback` (`0006d337`), closes every handle with `Ttm_CloseAll` (`ES1DBSIM 00069a04`), deletes the table, and deletes the manager when bit 0 of its flags is set.

`Ttm_Open` (`ES1DBSIM 00069848`), given the object, *a*, *b* and a name in stack order, takes the first empty handle of the table at `+5`, makes a `0x1c1`-byte script with `TtmScript_Ctor` (`ES1DBSIM 000797ac`) and loads it with `TtmScript_Load`(script, *a*, *b*, name), destroying it with `TtmScript_Dtor` (`ES1DBSIM 000799cc`) and returning `0xff` on a nonzero return; otherwise it returns the handle. `TtmScript_Ctor` constructs the twelve `0x18`-byte regions at `+0x58` with `GLRectangleRegion_Ctor` (`ES1DBSIM 00079880`, the shell's code with the vtables `0009f25a`, `0009f27e`, `0009f2a6` and `0009f2ce` for its `00082ab0`, `00082ada`, `00082b0c` and `00082b44`), sets the dwords `+0x19d` and `+0x1a5` to 0 and `+0x1ad` to 4, and zeroes `+0x1b5`, `+0x1b9` and `+0x1bd`. `GLRectangleRegion_Dtor` (`ES1DBSIM 0007999c`) is the shell's code with these vtables. `TtmScript_Dtor` frees the script with `TtmScript_Free` (`ES1DBSIM 00079f68`), which resets it and frees the script `+6`, the page table `+2` and the slot tables `+0x21` and `+0x17`; there is no sound object.

`TtmScript_Load` (`ES1DBSIM 0007a54c`):

- keeps no copy of the name, and its `TtmScript_BuildPath` (`ES1DBSIM 00079b54`) neither upper-cases the name nor rewrites `.RAW`, building in `000bbbf0`;
- returns 3 when the file stream's status after the open is not 0;
- finds the tags with `TtmScript_FindTag` (`ES1DBSIM 0007cba4`), which takes the script first and searches as `Stream_FindTag` does;
- reads one byte of the page count, its low byte;
- returns 2 when the method byte is not 1, and 1 when the script's allocation fails;
- keeps the page count at `+0` (a byte), the page table at `+2`, the script at `+6`, its last byte at `+0xa`, the two blocks at `+0x21` and `+0x17`, *a*, the mode, at `+0xe` and *b*, the loop flag, at `+0x13`;
- zeroes the two blocks' six slots each, the twelve saved-pixel slots `+0x28` and the three cycling directions, sets the once flag `+0x12`, the page `+1` and the palette slot `+0x25` to 0, the next page `+0x178` to −1, the words from the call depth `+0x17a` to the cycle's reload `+0x188` and the byte `+0x18a` to 0, and the three brush colours to 10. It sets neither the bitmap-array slot `+0x26`, the region slot `+0x27` nor the twelve regions' rects, which `TtmScript_Ctor` leaves as allocated: zero, while the heap is on, whose allocations are zero-filled ([the heap](../simulation/memory.md#the-heap)), until `0x1051`, `0x1121` and `0x4214` set them;
- indexes the pages with `TtmScript_IndexPages` (`ES1DBSIM 0007a940`, through its padding entry), the shell's walk, testing the position at each page's start and after each command, and `TtmScript_SkipString` (`ES1DBSIM 00079fb0`); `RLERStream_DtorCopy` is `ES1DBSIM 0007a914`.

`Ttm_GetPage` (`ES1DBSIM 00069704`), `Ttm_GetMode` (`00069740`), `Ttm_GetLoop` (`00069760`), `Ttm_SetMode` (`0006995c`), `Ttm_SetLoop` (`0006998c`), `Ttm_Close` (`000699bc`) and `Ttm_CloseAll` are the shell's, given the manager first, with the page a byte at the script's `+1`, the mode at `+0xe` and the loop flag at `+0x13`, and `Ttm_Close` formatting no message. `Ttm_SetPage` (`ES1DBSIM 00069904`), given the manager, a page and a handle, stores the page at `+1` when it is below the page count and returns 1; it leaves the wait, the mode and the flags as they are, where the shell's resets them.

### Each frame

**The simulator runs its scripts while it draws the cockpit, once in each frame in which the timer has ticked, after the panel's instruments, so a script draws over them.** `Ttm_TimerTick` (`ES1DBSIM 000696f4`, a frameless stub) sets the byte `0009b404` to 1 and adds 1 to the word `0009b406`; unlike the shell's it has no byte that stops it, as the simulator's fades set none.

`Ttm_RunAll` (`ES1DBSIM 000695e0`) returns 0 at once while `0009b404` is 0: unlike the shell's it does not wait for a tick. Otherwise it notes `0009b406` and the installed render context `000bdbda`; stores the default context's `+4`, or 0 with none, at `+0x44` of the display object, passing it to `Raster_SetClipBlock` when the display is `000bdd28`, and installs the default context with `Raster_InstallRenderContext`; clears `0009b404`; runs each handle's script with `TtmScript_Run`(script, the noted count); does the same with the noted context; subtracts the noted count from `0009b406` and returns 1.

`es2_xref.py` finds two calls of `Ttm_RunAll`: the end of `Cockpit_LoadPanel`, and `Cockpit_RunTtmScripts` (`ES1DBSIM 0002e0ec`), which sets the installed render context's dwords `+0x20c`, `+0x220` and `+0x224` to 1, `000afa9c` and `000afaa0`, makes it the default context with `Ttm_SetDefaultContext`, calls `Ttm_RunAll`, and puts the three back. It finds two calls of `Cockpit_RunTtmScripts`, from `0002b4a0` and from `Cockpit_DrawFrame` (`ES1DBSIM 0002cf28`), which `Sim_RenderFrame` calls. `Cockpit_DrawFrame` does nothing while the byte `00092d7c` is set. Otherwise it calls the `+0xc` slot, through the vtable each object holds at its `+0x76`, of the clock at `+0x1f9`; while its byte `+0x20b` is set it then calls that of the object `+0x1f5` and returns, and otherwise, among other calls, calls that of the panel's other objects and then `Cockpit_RunTtmScripts`.

### The simulator's run

`TtmScript_Run` (`ES1DBSIM 00079a14`), given a script and a count of ticks, installs the script's render context `+0x1bd`, when it is not 0, as the default context `0009f248` and with `Raster_InstallRenderContext`, and puts the one before back when it ends. It subtracts the ticks from the wait `+0x182`. Then:

- **While the wait is above 0:** unless the mode `+0xe` is 2, it runs the page with `TtmScript_RunPage`. There is no flag for a page that has run.
- **Once the wait is 0 or less:** it cycles colours as the shell's does (the byte `+0x18a`, the count `+0x186` and its reload `+0x188`, and `TtmScript_CyclePalette`, `ES1DBSIM 00079d90`, the shell's code with the ranges at `+0x18b`, their directions at `+0x18f`, `+0x195` and `+0x19b`, and the palettes at `+0x21` selected by `+0x25`). It reloads the wait from `+0x184`, then by the mode, a dword taken through the table `0009f2f4` after an unsigned test against 3: 0 runs the page and moves to the next; 1 runs the page; 3, with the loop flag `+0x13` 1, resets the script and sets the mode to 0, and otherwise sets the mode to 2, leaving the script as it is; 2 and any mode above 3 do nothing.

Moving to the next page is done in place, there being no `TtmScript_NextPage`: it takes the page `+0x178` when that is not −1, setting it back to −1, and otherwise the page `+1` plus 1, clearing the byte `+0x12`; when the page has reached the page count `+0`, compared as bytes, it does as mode 3 does. There is no random choice.

**A `0x0110` ends a script that does not loop.** Its page sets the mode to 3 while it runs, and the next run whose wait has run out finds mode 3 and, with the loop flag 0, sets mode 2, after which the script's pages never run again.

A page therefore runs on each tick while its wait lasts, and once more, with the wait reloaded, when it runs out. Most of the commands the shell runs on a page's first run act in the simulator only while the wait is 0 or less ("no wait" in [the table](#the-simulators-commands)), which, the wait being reloaded before the page runs, holds while the reload `+0x184` is 0 or less: until a wait command has run. The wait commands act once per page through the byte `+0x12`, which a step to the next page clears and a jump leaves set: after a page that sets a wait and jumps, the target page's wait command does not act, and the jumping page's wait stays in force until a page sets another. In `ROADRUNN.TTM` the first page loads `ROADTTM.BMP`; page 1, labelled 2, draws frame 0 at (287, 166) and, with flags 2, at (306, 166), and sets a wait of 3; pages 2 to 8 draw frames 1 to 7; page 9 draws frame 8, sets a wait of 60 and jumps to label 2. After the first pass every page of the loop therefore lasts 60 ticks ([Open](#open)).

`COLOSSUS.TTM`, read through HercWorks.Core, has the same shape, with a `0x0110` before its first page's end: page 0 is label 1, `0x1051` 0, `0xf02f` `COLOTTM.BMP`, `0x0110`; pages 1 to 9 draw frames 0 to 8 at (210, 4), page 1 labelled 2 and setting a wait of 3, and page 9 jumps to label 2. The clock opens it with the loop flag 0 ([the cockpit clock](#the-cockpit-clock)), so on its first run page 0 loads the bitmap and sets mode 3, and the next run sets mode 2: no frame is drawn. It is the only one of the ten scripts with a `0x0110` ([Open](#open)). In `GUNSLING.TTM` page 17 jumps to label 2, page 1, so pages 18 and 19, labelled 3, are reached only by page 19's own jump to label 3, and a run from page 0 never draws their frames 17 and 18.

`TtmScript_Reset` (`ES1DBSIM 0007ca44`) is the shell's at the simulator's offsets, destroying the bitmap arrays with `GLBitmapArray_Dtor` (`ES1DBSIM 000673c8`, the shell's code)(array, 3), with one difference: it leaves the byte `+0x12`, where the shell's clears its once flag `+0x40`. The mode is a dword, at `+0xe` to `+0x11`. `TtmScript_FindLabel` (`ES1DBSIM 00079ef4`) is the shell's, returning a byte page and formatting no message.

### The simulator's commands

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

#### The simulator's fades

The fades work on a palette on the stack (vtable `0009ff86`) given the selected palette's fields with `GLPalette_Assign` (`ES1DBSIM 0007bfdc`, the shell's code) and its own 1 KB of colours, copied from the selected palette's. `Palette_ScaleRange` (`ES1DBSIM 0007cc24`), given the script, a percent, a last and a first colour and the two palettes, sets each colour from the first to the last inclusive of the copy to the selected palette's with each channel multiplied by `Math_Q10Multiply` by percent · 1024 / 100, and its fourth byte 1. The fades pass *first* as the first colour and *count* as the last, and read neither *colour* nor the timer; the step is 6 − *speed*, as in the shell, a word written over *speed*, and the percent is a signed word. With *speed* 6 the step is 0 and the loop never ends; with *speed* above 6 the step is negative and the percent runs away from its end until it passes the word's range and changes sign, which ends the loop.

- **Fade out:** for *percent* from 100 down to 0 by the step, scales and uploads. Then copies the full-screen region (0, 0) to (319, 199) with `Region_CopyBetweenPages` twice, with the display's source page `+0x38` and draw page `+0x3c` set as the shell's fade sets its `+0x34` and `+0x38`, and puts back their low bytes; uploads the selected palette; and destroys the copy with `GLPalette_Dtor` (`ES1DBSIM 00069258`, the shell's code)(copy, 2).
- **Fade in:** sets the copy's 256 colours to `0x01000000` and uploads it; copies the selected palette's colours in; copies the full-screen region once, with `+0x38` set to `Ttm_ManagerCtor`'s *a* and `+0x3c` to the display's `+0x34`, and puts back their low bytes; then for *percent* from 0 up to 100 by the step, scales and uploads; uploads the selected palette and destroys the copy.

None of the ten `SIMVOL0` scripts holds `0x2402` or a fade, and the one `0x2022`, on `GUNSLING.TTM`'s page 18 ([the simulator's run](#the-simulators-run)), has *min* 3 and *max* 90, so none reaches the divisions by 0 or the endless fade.

### The cockpit clock

**The cockpit's clock, a 24-hour display in its panel, opens the HERC's script when it is made.** `es2_xref.py` finds one call of `Ttm_Open`, from `CockpitClock_Ctor` (`ES1DBSIM 000433cc`), which `CockpitClock_Create` (`ES1DBSIM 0002ce98`) calls with the panel and its description's `+0x644`, adding the clock to the panel's list at `+8` (the count at `+0x6c`). The clock is a `0x8a`-byte object, built on `00031318`, whose vtable `00094b5a` is held at `+0x76`.

`CockpitClock_Ctor` opens `ttm\` and eight characters `.ttm` (`Path_WithExtension`) with *a* and *b* 0, playing once without looping: the eight characters at `000925f8` when that is set, and otherwise the chassis name, file offset 86 of the [chassis file](chassis-dat.md), at `+0x58` of the type record `+0x188` of `CockpitViewInstance`'s (`00092d1c`) `+0x1ff`. It stores `CockpitViewInstance`'s `+4` at the script's `+0x1bd`, so the script draws through that render context, and keeps no handle. It takes the handle as a signed byte: when the open fails, the `0xff` reads the dword before the handle table as the script, and stores `+0x1bd` through it when it is not 0. It then copies `cpwincrk` into its name buffer, which it does not read again, sets the strings `00`, `00` and `:` at `000bd504`, `000bd508` and `000bd50c`, makes three labels with `Label_Ctor` on the first of `ColorSchemePanels`' (`00092d40`) ten fonts, `cpblue`, at `+0x7a`, `+0x7e` and `+0x82`, and sets `+0x86` to the count `000981c4` + 30.

**The clock's three labels share one rect, so the hours' fill wipes the colon, which makes it blink.** The clock's rect, passed to `PanelGauge_Ctor`, is the description's `+0x654`, the `.GAU` file's offset 1620, 16 bytes into the clock's block at 1604 ([the layout](gau-cockpit-layout.md#differences-from-es2)). Each label gets that rect from `Label_SetRect` ([labels](../simulation/cockpit-widgets.md#labels)): the hours with flags 1, left-aligned, and the margin (1, 0); the minutes with flags 4, right-aligned, and (1, 0); the colon with flags 2, centered, and (0, 0). Their fill colours `+0x1c` are `0x3f`, 0 and 0, so `Label_SetText` fills the rect with colour `0x3f` before it draws the hours and draws the minutes and the colon over what is there. `CockpitClock_Advance` sets the hours, then the minutes, and the colon only on every second call, so the colon shows for half a second and is wiped for the next half.

`CockpitClock_Tick` (`ES1DBSIM 000435c0`, the vtable's `+0xc`) sets the mark `+0x75` to 2 while the view manager's `+0x18` is set, a view change staged; while the mark is not 0 it copies the rect from the hour label's first corner to the minute label's second with `Widget_DrawToCockpit`(0, rect, 0), which copies from the display's page `+0x88` to `+0x8c` without the test of `000b5bca` its copy the other way makes and returns without copying while the view manager's `+0x18` is set ([drawing it](../simulation/cockpit-panel.md#drawing-it)), and clears the mark. When `000981c4` is above `+0x86`, compared unsigned, it calls `CockpitClock_Advance` (`ES1DBSIM 00043658`), sets `+0x86` to the count as it read it before the call plus 30, half a second of the clock's 60 counts a second ([the tick length](../simulation/fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)), and sets the mark to 1. `CockpitClock_Advance` toggles the byte `00094b56`, and on every second call adds a minute: the minutes run from 00 to 59 and the hours from 00 to 24, the hour after 24:59 being 00:00. When `CockpitView_RectCornerVisible`(the hour label's rect) is not 0 it sets the hour and minute labels' text with `Label_SetText`, and the colon's while `00094b56` is 1, between `Cockpit_PushCanvasContext` and `Cockpit_PopRenderContext`. `CockpitClock_Update` (`000435b0`, the `+8` slot) calls `CockpitClock_Advance`; the `+4` slot (`000435a1`) returns and the `+0x10` (`000435a7`) returns 0. None of the four touches the script.

The name at `000925f8` comes from the command line: the option `-C` passes the text after it to `CockpitChassis_SelectByName` (`ES1DBSIM 00024394`), which compares it, ignoring case, with the eight names `ROADRUNNER`, `GUNSLINGER`, `RAPTOR`, `TOMAHAWK`, `PATRIOT`, `PANTHER`, `HERCULES` and `COLOSSUS` at `00092644`, and for the first that matches stores its place at the word `CockpitOverride_Index` (`000925f4`) and the text's address at `CockpitOverride_Name` (`000925f8`). `CockpitViewManager_Ctor` (`ES1DBSIM 00021dd4`) takes the same pair in place of the HERC's chassis ([the view manager](../simulation/cockpit-views.md#the-view-manager)). Eight characters of the eight names are the names of `ROADRUNN.TTM` to `COLOSSUS.TTM`, chassis types 0 to 7 ([the chassis files](chassis-dat.md)).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The `TT3:` method byte chooses the unpacking: 0 stored, 1 run-length, 2 LZW, as Dynamix's later games and ScummVM's `dgds` read it | The shell reads the byte and unpacks run-length whatever it holds; the simulator refuses any value but 1. Every retail file holds 1. |
| A run-length control byte of `0x80` takes no further byte | `RLERStream_Read` takes the byte after any control byte with bit 7 set, `0x80` among them, as a run's value. |
| An opcode's low four bits give up to eight argument words | Both loaders' page walk fails on 8 to 14; only 0 to 7 count words. |
| `0x5005` sets a rect of the scene from five arguments, as ScummVM's `dgds` reads it | The shell's `0x5005`, Dynamix's `TOKEN_HOTSPOT`, is a [weighted random jump](#the-random-choice): one gives five weights, the next five labels. |
| `0x1301` and `0x1311` play and stop a sound effect | The shell renumbers them to `0xc051` and `0xc061`, which its sound commands pass over. |
| `0x0110` purges the script | The shell's `0x0110`, `TOKEN_ENDLABEL`, sets the mode to 3, finished. |
| `0xa601` puts back the saved region its argument names | It puts back the region `0x1121` selected; its argument is unread. |
| `0xa5a7` draws a bitmap at *x*, *y* and ignores its other arguments | It draws bitmap *frame* of slot *slot* turned by *angle* and sized *w* by *h*. |
| `0xa5a7` centres the turned bitmap on (*x* + *w*/2, *y* + *h*/2), (−*w*/2, −*h*/2) turned being added there | `Transform2D_ApplyToPoint` also adds its translation, which the case sets to (−*w*/2, −*h*/2), so the bitmap is centred on (*x*, *y*). |

## Open

- **Open:** a retail look at `ROADRUNN.TTM`'s animation in a Roadrunner's cockpit: what it shows, and whether after its first pass each page lasts 60 ticks, a second, as the jump's kept once flag makes it ([the simulator's run](#the-simulators-run)).
- **Open:** whether a Colossus cockpit shows any animation in its panel, which by [the simulator's run](#the-simulators-run) `COLOSSUS.TTM` never draws, where the other HERCs' scripts loop a run of frames. A retail check: SINGLE MISSION's list 6, `SM_ASS01`, whose player's squad is two COLOSSUS HERCs; watch the panel around (210, 4), at the top of the screen, for a minute, and compare another chassis' cockpit, whose script draws from its first second.
- **Open:** what writes the simulator's script origin `+0x1b5` and `+0x1b9` but `TtmScript_Ctor`'s zeroes. `es2_fieldscan.py` over `ES1DBSIM 00069540`–`0007cd28` finds that store and no other, where the same scan of the clock's code finds its store of `+0x1bd`.
- **Open:** whether a cockpit of a chassis without a script, types 8 to 15, is reached, where `CockpitClock_Ctor`'s failed open reads the dword before the handle table.
- **Open:** in the simulator, `es2_xref.py` finds no call of `Ttm_ManagerDtor`, `Ttm_GetPage`, `Ttm_GetMode`, `Ttm_GetLoop`, `Ttm_SetPage`, `Ttm_SetMode`, `Ttm_SetLoop` or `Ttm_Close`, and no reference to `Ttm_TokenName`; what `0002b4a0`, the other caller of `Cockpit_RunTtmScripts`, is.
- **Open:** what the byte `0007fcb9` is, which gates the scripts' sounds as it gates [the animations'](anm-animation.md#playing); a menu dialog control's word is taken from it ([the kinds](dlg-menu-dialogs.md#the-kinds)).
- **Open:** what reads `VER:`, `TTI:` and `TAG:`, and what the tags name. A byte search of both images for `VER:`, `TTI:`, `TAG:` and `4.14` found none, where the same search found `PAG:` and `TT3:` in each.
- **Open:** what opens `AMBIENC1.TTM` and `CPWINCRK.TTM`. A byte search of both images for `ambienc` found none, where the same search for `cpwincrk` found the one `CockpitClock_Ctor` copies after its open.
