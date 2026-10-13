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

`TtmScript_IndexPages` (`000480d1`), given a loaded script, zeroes the page count's entries of the page table `+0x30` and walks the script from its start: for each page it stores the position as the page's start and steps over commands until the step past a `0x0ff0`. Before each opcode it fails when the position has reached `+0x38`, the script's last byte. It returns 0 with every page found and 2 when it fails, on that test or on low bits 8–14. `TtmScript_SkipString` (`00047571`) steps over a string. All 123 files divide into their page count's pages. What the commands do is [Open](#open).

## The shell

The shell keeps its scripts in a table of handles. `Ttm_Configure` (`000464b0`), given *a*, *b* and a count in stack order, stores the count in the byte `000829dc`, *b* at `00082a98`, *a* at `00082a9c` and the display object `000ab616` at `00082a94`, and clears the path prefix with `Ttm_SetPathPrefix` (`0004692d`), which stores a string pointer at `00082aa0` and returns the one before. `Ttm_AllocHandles` (`0004670e`), given a timer count, allocates the table at `000829e0`, a dword per handle, and zeroes it; takes `000ab784` into `000829d8`, or a new object `0005d6b4`(0, `0x4da6`) when that is 0, freeing the table and returning 0 when there is still none; and registers `00046681` with `Timer_AddCallback` at that count into the word `000829e4`. It compares the word, zero-extended, with −1, which it never equals, so it always registers. It returns 1. `Ttm_Shutdown` (`000464e8`) passes the slot to `000565b7` under the same comparison, calls `00046a3f`, and deletes the table.

`es2_xref.py` finds one call of each: `0003ba47` calls `Ttm_Configure` with a count of 50, `Ttm_AllocHandles` with 2, sets the prefix `res32\` and calls `TtmLists_Load` ([the animation lists](shell-file-lists.md#the-animation-lists)); `0003c081` calls `Ttm_Shutdown`. Of the twelve calls of `Ttm_SetPathPrefix` it finds, `Ttm_Configure`'s passes none and the other eleven pass `res32\` or `intro\`, the two `SHELL0` folders that hold scripts.

### Opening and closing

`Ttm_Open` (`0004680a`), given *a*, *b*, *c* and a name in stack order, returns `0xff` when the handle count is 0 or every handle holds a script. Otherwise it makes a script with `TtmScript_Ctor` (`00046bb8`) in the first empty handle and loads it with `TtmScript_Load`(script, *a*, *b*, *c*, name); on a nonzero return it destroys it with `TtmScript_Dtor` (`00046e2f`) and flags 3, empties the handle and returns `0xff`, and otherwise returns the handle's index. `es2_xref.py` finds calls from `00011f19`, `00037996` (two), `00037dce`, `0003b8df` and `maybe_SncList_Load`.

`Ttm_Close` (`000469da`), given a handle, destroys its script with flags 3, empties the handle and returns 1; for an empty handle it formats `Invalid handle (%d) passed to unload.` into a stack buffer it does not use and returns 0.

A script is `0x1eb` bytes. `TtmScript_Ctor` allocates one when given none, constructs twelve `0x18`-byte objects at `+0x88` with `__vec_ctor` and `00046c6c`, and sets the dwords `+0x1d3` and `+0x1db` to 0 and `+0x1e3` to 4. `TtmScript_Dtor` returns at once for none; otherwise it calls `000474bd`, destroys the twelve objects with `__vec_dtor` and `00046e73`, and deletes the script when bit 0 of the flags is set.

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

| Offset | Set to |
|---|---|
| `+0x02` | 0, a word: the current page |
| `+0x2c` | *c* |
| `+0x3c` | *a* |
| `+0x40`, `+0x41`, `+0x55`, `+0x1c0` | 0, bytes |
| `+0x43` | *b* |
| `+0x47`, `+0x51` | six dwords of 0 each |
| `+0x58` | twelve dwords of 0 |
| `+0x1a8` | 0, a dword |
| `+0x1ac`, `+0x1ae` | −1, words |
| `+0x1b0`–`+0x1be` | 0, eight words |
| `+0x1c5`, `+0x1cb`, `+0x1d1` | 0, words |
| `+0x1d7`, `+0x1df`, `+0x1e7` | 10, dwords |

What the fields hold is [Open](#open). `maybe_SncList_Load` opens a talking head's script on its player's `+0x15` object as *c* ([the lip-sync lists](shell-file-lists.md#the-lip-sync-lists)).

### Choosing a page

`Ttm_SetPage` (`000468fc`), given a page and a handle, calls `TtmScript_SetPage` (`000470e7`) on the handle's script and returns its result, or 0 for an empty handle; it does not test the handle against the count. `TtmScript_SetPage`, when the page is below the page count, stores it at `+2`, copies the word `+0x1ba` to `+0x1b8`, clears the bytes `+0x40`, `+0x41` and `+0x42` and the dword `+0x3c`, and returns 1; otherwise it returns 0. A talking head's [lip-sync frame](snc-lip-sync.md#playing-it) is the page its script is set to.

`00046948` and `00046991`, given a value and a handle, store the value at the handle's script's `+0x3c` and `+0x43` and return 1, or 0 for an empty handle. `es2_xref.py` finds 24 calls of `00046948` and none of `00046991`.

`Ttm_RunScript` (`000465da`), given a handle, returns 0 for −1. Otherwise it clears the byte `000829d5`, calls `00046f2c` with the handle's script, when there is one, and the word `000829d6`; stores the render context `000ab612`'s `+4`, or 0 with none, at `+0x40` of the display object `00082a94`, passing it to `Raster_SetClipBlock` when `000ab7a0` is that object, installs `000ab612` with `Raster_InstallRenderContext`, sets `000829d6` to its value less the value it had on entry, and returns 1. `00046f2c` runs the script ([Open](#open)).

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

## Open

- **Open:** what the commands do, and the interpreter that runs them: `00046f2c`, the timer callback `00046681`, `000474bd`, the twelve objects at `+0x88` (`00046c6c`, `00046e73`), and what the loaded fields hold, among them *a*, *b* and *c* (`+0x3c`, `+0x43`, `+0x2c`); `ES1DBSIM`'s counterparts.
- **Open:** what reads `VER:`, `TTI:` and `TAG:`, and what the tags name. A byte search of both images for `VER:`, `TTI:`, `TAG:` and `4.14` found none, where the same search found `PAG:` and `TT3:` in each.
- **Open:** which shell screen opens which script, through `00011f19`, `00037996`, `00037dce` and `0003b8df`; what `0003ba47` and `0003c081` are; what *a* and *b* of `Ttm_Configure`, and `0005d6b4`'s object at `000829d8`, are.
- **Open:** in the simulator, which chassis record `000433cc` reads and what sets `000925f8`; what opens `AMBIENC1.TTM` and `CPWINCRK.TTM` (`000433cc` copies `cpwincrk` into a stack buffer after its open); where the handle table at the object's `+5` is allocated; what `0007cd4a` does with the clock.
