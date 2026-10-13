# DBSIM.EXE and GO.EXE — animation scripts (`.TTM`)

**The shell's talking heads, its introduction and endings and its other animated screens, and a script per HERC in the simulator's cockpit, are played by scripts.** Each `.TTM` file holds a script of 16-bit drawing and timing commands, packed by run-length coding and divided into pages, with a version, the page count and a table of named tags around it. It is Dynamix's TTM format, which their adventure games of the time also use. `SHELL0.VOL` holds 113, 106 in `RES32\` and 7 in `INTRO\`; `SIMVOL0.VOL` holds 10 in `ttm\`. Each program has its own copy of the loader, the simulator's differing from the shell's in the points [below](#the-simulator). Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)).

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

The shell keeps its scripts in a table of handles. `Ttm_Configure` (`000464b0`), given *a*, *b* and a count in stack order, stores the count in the byte `000829dc`, *b* at `00082a98`, *a* at `00082a9c` and the display object `000ab616` at `00082a94`, and clears the path prefix with `Ttm_SetPathPrefix` (`0004692d`), which stores a string pointer at `00082aa0` and returns the one before. `Ttm_AllocHandles` (`0004670e`), given a timer count, allocates the table at `000829e0`, a dword per handle, and zeroes it; takes `000ab784` into `000829d8`, or a new object `0005d6b4`(0, `0x4da6`) when that is 0, freeing the table and returning 0 when there is still none; and registers `Ttm_TimerTick` (`00046681`) with `Timer_AddCallback` at that count into the word `000829e4`. It compares the word, zero-extended, with −1, which it never equals, so it always registers. It returns 1. `Ttm_Shutdown` (`000464e8`) passes the slot to `000565b7` under the same comparison, calls `Ttm_CloseAll` (`00046a3f`), and deletes the table.

`es2_xref.py` finds one call of each: `0003ba47` calls `Ttm_Configure` with a count of 50, `Ttm_AllocHandles` with 2, sets the prefix `res32\` and calls `TtmLists_Load` ([the animation lists](shell-file-lists.md#the-animation-lists)); `0003c081` calls `Ttm_Shutdown`. Of the twelve calls of `Ttm_SetPathPrefix` it finds, `Ttm_Configure`'s passes none and the other eleven pass `res32\` or `intro\`, the two `SHELL0` folders that hold scripts.

### Opening and closing

`Ttm_Open` (`0004680a`), given *a*, *b*, *c* and a name in stack order, returns `0xff` when the handle count is 0 or every handle holds a script. Otherwise it makes a script with `TtmScript_Ctor` (`00046bb8`) in the first empty handle and loads it with `TtmScript_Load`(script, *a*, *b*, *c*, name); on a nonzero return it destroys it with `TtmScript_Dtor` (`00046e2f`) and flags 3, empties the handle and returns `0xff`, and otherwise returns the handle's index. `es2_xref.py` finds calls from `00011f19`, `00037996` (two), `00037dce`, `0003b8df` and `maybe_SncList_Load`.

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

The byte `+0x42`, set once the page has run, the bitmap-array slot selected `+0x56` and the region slot selected `+0x57` are not set by the load. `maybe_SncList_Load` opens a talking head's script on its player's `+0x15` object as *c* ([the lip-sync lists](shell-file-lists.md#the-lip-sync-lists)).

### Choosing a page

`Ttm_SetPage` (`000468fc`), given a page and a handle, calls `TtmScript_SetPage` (`000470e7`) on the handle's script and returns its result, or 0 for an empty handle; it does not test the handle against the count. `TtmScript_SetPage`, when the page is below the page count, stores it at `+2`, copies the word `+0x1ba` to `+0x1b8`, clears the bytes `+0x40`, `+0x41` and `+0x42` and the dword `+0x3c`, and returns 1; otherwise it returns 0. A talking head's [lip-sync frame](snc-lip-sync.md#playing-it) is the page its script is set to.

`Ttm_SetMode` (`00046948`) and `Ttm_SetLoop` (`00046991`), given a value and a handle, store the value at the handle's script's mode `+0x3c` and loop flag `+0x43` and return 1, or 0 for an empty handle. `es2_xref.py` finds 24 calls of `Ttm_SetMode` and none of `Ttm_SetLoop`. `Ttm_GetPage` (`0004669f`) returns a handle's page, or `0xffff` for an empty handle; `Ttm_GetMode` (`000466cc`) and `Ttm_GetLoop` (`000466ed`) return its mode and loop flag without testing the handle for a script. `es2_xref.py` finds four calls of `Ttm_GetMode`, from `00037dce` and `0003b8df`, and none of the other two.

`Ttm_RunScript` (`000465da`), given a handle, returns 0 for −1. Otherwise it clears the byte `000829d5`, calls [`TtmScript_Run`](#running-a-script) with the handle's script, when there is one, and the word `000829d6`; stores the render context `000ab612`'s `+4`, or 0 with none, at `+0x40` of the display object `00082a94`, passing it to `Raster_SetClipBlock` when `000ab7a0` is that object, installs `000ab612` with `Raster_InstallRenderContext`, sets `000829d6` to its value less the value it had on entry, and returns 1. `Ttm_SetDefaultContext` (`000467ef`) stores the default render context `00082a98` and returns the one it replaces; `es2_xref.py` finds calls from `DigitalDialog_ShowMessage`, `0003b8df` and `0003c1b7`. `Ttm_CloseAll` destroys every handle's script with flags 3 and empties the handle.

## Running a script

**The shell redraws its animations on a timer.** Each time the timer fires, every open script runs its current page again from the top, and when the page's wait has run out it moves to the next page, or to the page a jump picked. A script that runs past its last page stops running, or starts again from its first page when its loop flag is set.

### The timer

`Ttm_AllocHandles` registers `Ttm_TimerTick` (`00046681`) with `Timer_AddCallback` at a count of 2 (the timer's rate is [Open](#open)). The tick returns at once while the byte `000829d4` is set, which the [fade commands](#the-commands) do while they run; otherwise it sets the byte `000829d5` and adds 1 to the word `000829d6`, the ticks to be run.

`Ttm_RunAll` (`0004652e`) notes `000829d6`, waits in a loop until `000829d5` is set and clears it, then runs every handle's script with `TtmScript_Run`(script, `000829d6` as it is then). It restores the render context as `Ttm_RunScript` does, subtracts the noted value from `000829d6`, and returns 1. `es2_xref.py` finds calls from `Shell_Main`, `000382f6` and `0003c1b7`. `Ttm_RunScript` runs one handle's script without waiting.

### A run

`TtmScript_Run` (`00046f2c`), given a script and a count of ticks, installs the script's render context `+0x2c`, or the default `00082a98` when that is 0, as `Ttm_RunScript` installs `000ab612`, and subtracts the ticks from the wait `+0x1b8`. Then:

- **While the wait has not run out, or the page has not run yet** (`+0x42` is 0): unless the mode is 2 it runs the page with [`TtmScript_RunPage`](#the-commands) and sets `+0x42`. When the wait is then 0 or less, it reloads the wait from `+0x1ba` and moves to the next page.
- **Once the wait has run out on a page that has run:** while colours cycle (`+0x1c0` is 1) it subtracts the ticks from `+0x1bc`, and when that falls below 0 reloads it from `+0x1be` and steps the cycle. It reloads the wait from `+0x1ba`, then by the mode: 0 runs the page and moves to the next; 1 runs the page; 3 resets the script and sets the mode to 0 when the loop flag is 1, and to 2 otherwise; 2 does nothing.

It returns 1. A page therefore runs once each tick while its wait lasts: the commands that load, save or set the wait test `+0x42` or `+0x40` and act on the first run only, and the drawing commands draw on every run.

`TtmScript_NextPage` (`00046e7c`) takes the page `+0x1ac` when it is not −1, setting it back to −1; else `+0x1ae` likewise; else the current page plus 1, unless the mode is 3, when the page stays. It clears `+0x40`, `+0x41` and `+0x42`. When the page has reached the page count it resets the script and sets the mode to 0 when the loop flag is 1, and to 2 otherwise.

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
| `0x0020` | `TOKEN_FIXBACK` | — | yes | `000499d3` | First run: `TtmScript_FixRect` of the whole display, (0, 0) to its `+8` and `+0xc` less 1, read through `0005a6e4`. |
| `0x0070` | `TOKEN_FREEPAL` | — | no | `00048bd1` | First run: destroys the selected palette (its `+0x1c` slot with 3) and empties the slot. |
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
| `0x2022` | `TOKEN_RANDOM_WAIT` | min, max | yes | `0004995a` | Once: sets the wait and its reload to *min* + `Math_RandomBelow`(`0009aec0`, *max* − *min* + 1). |
| `0x2302`, `0x2312`, `0x2322` | `TOKEN_SET_RANGE0` to `2` | first, last | no | `000497f2` | Sets colour range (opcode − `0x2302`) / 16 to *first* and *last* − *first* + 1 colours. |
| `0x2402` | `TOKEN_CYCLE` | mask, speed | no | `0004983d` | No wait: sets `+0x1c0`, the cycle count and its reload to \|*speed*\|, and each range's direction to *speed* / \|*speed*\| when bit *i* of *mask* is set, else 0. |
| `0x3003` | `TOKEN_CALL_LABEL` | x, y, label | yes | `00049d4c` | Raises the call depth, sets the call's x and y, runs the page labelled *label* with `TtmScript_RunPage`, and lowers the depth. |
| `0x3201` | `TOKEN_CALL_START` | label | no | `00049d92` | Sets the base page `+0x1b6` to the page labelled *label*. |
| `0x3303` | `TOKEN_CALL_OFFSET` | x, y, n | no | `00049daf` | No wait, no call running, and the base page + *n* not the current page: calls that page as `0x3003` does. |
| `0x4114` | `TOKEN_FADE_OUT` | first, count, colour, speed | yes | `00048c35` | First run: fades colours *first* to *first* + *count* toward the palette's colour *colour* ([below](#the-fades)). |
| `0x4124` | `TOKEN_FADE_IN` | first, count, colour, speed | yes | `0004933b` | First run: fades the same colours up from colour *colour* ([below](#the-fades)). |
| `0x4204` | `TOKEN_FIX_RECT` | x, y, w, h | yes | `00049b8e` | First run: `TtmScript_FixRect` of (*x*, *y*) to (*x* + *w*, *y* + *h*). |
| `0x4214` | `TOKEN_GET` | x, y, w, h | yes | `00048892` | First run: frees the selected region's pixels, sets its rect to (*x*, *y*) to (*x* + *w*, *y* + *h*), allocates `Region_PixelCount` bytes and saves the pixels with `Region_Save`, with the display's `+0x34` set to `Ttm_Configure`'s *a* and the render context's `+0x208` to 0 while it does. |
| `0x5005` | `TOKEN_HOTSPOT` | five words | yes | `00049e59` | The [random choice](#the-random-choice). |
| `0xf01f` | `TOKEN_SCRNLOAD` | name | yes | `0004857b` | First run: loads the name with `TtmScript_BuildPath` (`.SCR` to `.DBM`) and `Resource_LoadBitmap` (`0004b79b`), draws it at (0, 0) with `Bitmap_Blit`, `TtmScript_FixRect`s its rect and destroys it. |
| `0xf02f` | `TOKEN_SHAPELOAD` | name | yes | `000487c3` | First run: destroys the selected bitmap array and loads the name (`.BMP` to `.DBA`) into the slot with `Resource_LoadBitmapArray` (`0004b7df`). |
| `0xf05f` | `TOKEN_PALLOAD` | name | yes | `00048b4d` | First run: destroys the selected palette, loads the name (`.PAL` to `.DPL`) into the slot with `Resource_LoadPalette` (`0004b713`), stores it at `0007fcd0` and sets `0007fcd4`. |

`TtmScript_RunDrawCommand` (`0004a213`) finds its 22 opcodes in the table at `000839b8` (targets at `00083a10`) and returns 1 for every opcode. The pen is the render context's `+0x228`, the fill brush its `+0x22c`, each pointed at one of the script's three brushes: `+0x1d3` (mode 0, the colour *fg*), `+0x1db` (mode 0, *bg*) and `+0x1e3` (mode 4, *fg*). Each draw but `0xa601`'s passes the box it drew to `00040f00` ([Open](#open)).

| Opcode | Name | Arguments | In the files | Case | What it does |
|---|---|---|---|---|---|
| `0xa002` | `TOKEN_SET_PIXEL` | x, y | no | `0004accc` | Pen `+0x1d3`; `Gfx_PlotPixel` at (*x*, *y*). |
| `0xa0a4` | `TOKEN_LINE` | x0, y0, x1, y1 | yes | `0004a235` | Pen `+0x1d3`; `Raster_DrawLine` from (*x0*, *y0*) to (*x1*, *y1*). |
| `0xa104` | `TOKEN_RECTFILL` | x, y, w, h | yes | `0004a3b3` | Fill `+0x1db`; `Raster_FillRect` of (*x*, *y*) to (*x* + *w*, *y* + *h*); then `0xa114`'s case. |
| `0xa114` | `TOKEN_RECT` | x, y, w, h | no | `0004a562` | Fill `+0x1e3`; `Raster_FillRect` of the same rect. |
| `0xa404` | `TOKEN_CIRCFILL` | x, y, w, h | no | `0004a731` | Fill `+0x1db`; `00055b9c` with the rect; then `0xa424`'s case. |
| `0xa424` | `TOKEN_CIRCLE` | x, y, w, h | no | `0004a8e0` | Fill `+0x1e3`; `00055b9c` with the rect. |
| `0xa504`, `0xa514`, `0xa524`, `0xa534` | `TOKEN_DRAWBRUSH0` to `3` | x, y, frame, slot | `0xa504`, `0xa524` | `0004ad68` | When bitmap-array slot *slot* holds an array of more than *frame* bitmaps: `Bitmap_Blit` of bitmap *frame* at (*x*, *y*) with flags 0 to 2 from bits 4 to 7 of the opcode, 3 for any other value. |
| `0xa505`, `0xa515`, `0xa525`, `0xa535` | `TOKEN_RLEDRAWBRUSH0` to `3` | five words | no | `0004ad68` | Nothing past the slot test. |
| `0xa506`, `0xa516`, `0xa526`, `0xa536` | `TOKEN_SIZEDRAWBRUSH0` to `3` | x, y, frame, slot, w, h | no | `0004ad66`, `0004ad68` | After the same test: `000503f0`(bitmap, (*w*, *h*), flags, (*x*, *y*)). |
| `0xa5a7` | `TOKEN_DRAW_ROT_BRUSH` | x, y, frame, slot, w, h, angle | no | `0004ad64` | After the same test: `Bitmap_BlitRotatedUnpacked` of the bitmap with *angle*, the size (*w*, *h*) and flags 0, placed at (*x* + *w*/2, *y* + *h*/2) plus (−*w*/2, −*h*/2) turned through `Math_BuildRotation2D`(*angle*) and `Transform2D_ApplyToPoint`. |
| `0xa601` | `TOKEN_NEW_PUT` | one word, unread | yes | `0004b298` | No wait: puts the selected region's saved pixels back at its rect with `Region_Restore`, `TtmScript_FixRect`s the rect, and calls `00041062`(1). |
| `0xaf1f` | `TOKEN_POLY` | n, *n* x, *n* y | no | `0004aaaf` | Builds *n* points, the x values then the y values; fill `+0x1e3`; `Raster_DrawPolygonEitherWinding`. |
| `0xaf2f` | `TOKEN_POLYFILL` | as `0xaf1f` | no | `0004aaaf` | As `0xaf1f`, drawing first with fill `+0x1db`. |

`TtmScript_RunSoundCommand` (`0004a0e0`) returns 1 for every opcode, and acts only while the byte `0007fcb9` is set and the page has not played its sound (`+0x41` is 0):

| Opcode | Name | Arguments | In the files | What it does |
|---|---|---|---|---|
| `0xc102` | `TOKEN_VOLUME_SX` | two words | yes | Nothing. |
| `0xc21f` | `TOKEN_HDS_LOAD` | name | yes | Destroys the sound object `+0x1a8`; puts the name through `TtmScript_BuildPath` and, when the sound manager `00082768` exists and `SoundManager_SetSampleName`(manager, `0xe`, path) succeeds, stores `SoundManager_CreateObject`(manager, 2, 0, `0xe`) at `+0x1a8`. |
| `0xc220` | `TOKEN_HDS_PLAY` | — | yes | With a sound object: calls its `+0x2c` slot with `Math_RandomBelow`(`0009aec0`, 25) + 10 and its `+0x10` slot, and sets `+0x41`. |

`TtmScript_FixRect` (`000472a2`), given a script and a rect, calls `0004c17c`(`Ttm_Configure`'s *a*, the display's `+0x38`, rect) ([Open](#open)).

### The random choice

`TtmScript_RunPage` keeps five words and a flag on its stack, the flag cleared on each call. A `0x5005` with the flag clear, or with `+0x1ae` not −1, stores the running sums of its five arguments and sets the flag. A later `0x5005` in the same call, with `+0x1ae` still −1, draws *r* = `Math_RandomBelow`(`0009aec0`, the fifth sum), takes the first of the five sums above *r*, and sets the next page `+0x1ae` to the page labelled by its own argument in that place; with none above *r* it takes its first argument. `C0_BRD.TTM`'s first page loads `c0_brd.BMP`, then gives a first `0x5005` the weights 20, 20, 20, 0, 0 and a second the labels 1, 2, 3, −1, −1: the script goes on to page 1, 2 or 3, a third of the time each, and each of those draws two frames, fixes a rect and ends with `0x0110`.

### The fades

Both fades set the byte `000829d4`, which stops the timer counting ticks, and clear it when they end. They work on a copy of the selected palette with its own 1 KB of colours, made with `GLPalette_CopyCtor` (`00049fee`) for the fade out and `GLPalette_Assign` (`0004a08c`) for the fade in, and blend with `Palette_BlendRange` (`0004b532`): colours *first* to *first* + *count* inclusive of the selected palette, each channel set to colour *colour*'s plus *percent* / 100 of its difference from it, written to the copy, which `Palette_UploadWhole` then uploads. The step is 6 − *speed*.

- **Fade out:** for *percent* from 100 down to 0 by the step, blends and uploads. Then, with the display's `+0x34` set to the low byte of its `+0x38` and its `+0x38` to its `+0x30`, copies a full-screen region (0, 0) to (319, 199) with `00051c40`; with `+0x38` set to `Ttm_Configure`'s *a*, copies it again; puts `+0x34` and `+0x38` back; uploads the selected palette; and destroys the copy with `GLPalette_Dtor` (`0005a824`).
- **Fade in:** blends and uploads at 0 percent (the 256 it passes, taken as a byte); with the display's `+0x34` set to *a* and its `+0x38` to its `+0x30`, copies the full-screen region and puts the two back; then for *percent* from the step up to 100 by the step, blends and uploads; and uploads the selected palette.

`Palette_BlendRange` shifts each channel's product as unsigned, so a channel below the target's makes a negative product whose shift sets bits above that channel.

## The simulator

`ES1DBSIM`'s code has the shell's shape with these differences.

The handles live in a `0xb`-byte object: `Ttm_ManagerCtor` (`ES1DBSIM 00069540`), given the object, *a*, *b* and a count in stack order, allocates it when given none, stores the count at its byte `+4`, *b* at `0009f248`, `000bdd28` at `0009f24c` and *a* at `0009f250`, clears the prefix with `Ttm_SetPathPrefix` (`ES1DBSIM 00069940`, the prefix at `0009f254`), and passes `clock()` to `0007cd4a`. `0006982c` stores a value at `0009f248` and returns the one before. `es2_xref.py` finds one call of `Ttm_ManagerCtor`, from `0002c9d8`, with a count of 10; it stores the object at `00092d70` and sets the prefix `ttm\`.

`Ttm_Open` (`ES1DBSIM 00069848`), given the object, *a*, *b* and a name in stack order, takes the first empty handle of the table at `+5`, makes a `0x1c1`-byte script with `TtmScript_Ctor` (`ES1DBSIM 000797ac`) and loads it with `TtmScript_Load`(script, *a*, *b*, name), destroying it with `TtmScript_Dtor` (`ES1DBSIM 000799cc`) and returning `0xff` on a nonzero return; otherwise it returns the handle. `TtmScript_Ctor` constructs the twelve `0x18`-byte objects at `+0x58`, sets the dwords `+0x19d` and `+0x1a5` to 0 and `+0x1ad` to 4, and zeroes `+0x1b5`, `+0x1b9` and `+0x1bd`.

`TtmScript_Load` (`ES1DBSIM 0007a54c`):

- keeps no copy of the name, and its `TtmScript_BuildPath` (`ES1DBSIM 00079b54`) neither upper-cases the name nor rewrites `.RAW`, building in `000bbbf0`;
- returns 3 when the file stream's status after the open is not 0;
- finds the tags with `TtmScript_FindTag` (`ES1DBSIM 0007cba4`), which takes the script first and searches as `Stream_FindTag` does;
- reads one byte of the page count, its low byte;
- returns 2 when the method byte is not 1, and 1 when the script's allocation fails;
- keeps the page count at `+0` (a byte), the page table at `+2`, the script at `+6`, its last byte at `+0xa`, the two blocks at `+0x21` and `+0x17`, *a* at `+0xe` and *b* at `+0x13`;
- indexes the pages with `TtmScript_IndexPages` (`ES1DBSIM 0007a940`, through its padding entry), the shell's walk, testing the position at each page's start and after each command, and `TtmScript_SkipString` (`ES1DBSIM 00079fb0`); `RLERStream_DtorCopy` is `ES1DBSIM 0007a914`.

`es2_xref.py` finds one call of `Ttm_Open`, from `000433cc`, the constructor of a `0x8a`-byte cockpit object whose vtable is `00094b5a`. It opens `ttm\` and eight characters `.ttm` (`Path_WithExtension`): the string the pointer `000925f8` holds, or with none the eight bytes at `+0x58` of the record that `+0x188` of the cockpit view's `+0x1ff` points to; and stores the cockpit view's `+4` at the script's `+0x1bd`. Eight of the ten `ttm\` files, `ROADRUNN.TTM` to `COLOSSUS.TTM`, carry the names of chassis types 0 to 7 ([the chassis files](chassis-dat.md)).

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

## Open

- **Open:** the drawing commands in depth: what `00040f00` does with the box each draw passes it, and `00041062`; the copies of `TtmScript_FixRect` (`0004c17c`) and the fades (`00051c40`); the ellipse `00055b9c`; the sized draw `000503f0`; brush mode 4; the display object's `+0x30`, `+0x34`, `+0x38` and `+0x40`, the routines at `000865f0` and `000865f4` the commands call when the display is `000ab616`, and `0005a6e4`'s items 0 and 1.
- **Open:** the simulator's interpreter, `ES1DBSIM`'s counterparts of `TtmScript_Run` and `TtmScript_RunPage`, and how it differs from the shell's.
- **Open:** the timer's rate: what `Timer_AddCallback`'s count of 2 is in ticks a second ([the timer's divisor](../simulation/debug-monitor.md#open)).
- **Open:** what the byte `0007fcb9` is, which gates the scripts' sounds as it gates [the animations'](anm-animation.md#playing); a menu dialog control's word is taken from it ([the kinds](dlg-menu-dialogs.md#the-kinds)).
- **Open:** what reads `VER:`, `TTI:` and `TAG:`, and what the tags name. A byte search of both images for `VER:`, `TTI:`, `TAG:` and `4.14` found none, where the same search found `PAG:` and `TT3:` in each.
- **Open:** which shell screen opens which script, through `00011f19`, `00037996`, `00037dce` and `0003b8df`; what `0003ba47` and `0003c081` are; what *a* and *b* of `Ttm_Configure`, and `0005d6b4`'s object at `000829d8`, are.
- **Open:** in the simulator, which chassis record `000433cc` reads and what sets `000925f8`; what opens `AMBIENC1.TTM` and `CPWINCRK.TTM` (`000433cc` copies `cpwincrk` into a stack buffer after its open); where the handle table at the object's `+5` is allocated; what `0007cd4a` does with the clock.
