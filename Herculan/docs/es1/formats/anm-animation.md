# GO.EXE — the shell's animations (`.ANM`)

**The shell plays two full-screen animations, each with a sound: `anm\esiege2.anm` and `anm\esiege3.anm` in `SHELL0.VOL`.** They are Electronic Arts' Deluxe Paint Animation large-page files, which name themselves `LPF ` and `ANIM`: a header, a table of pages, and the frames' records in pages of 64 KB, each record a run-length list of the pixels it changes. The player reads the whole file into memory and draws each frame straight to the display, a 320 × 200 background showing through colour 0. ES2 has no such file. Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## The layout

Fields in bold are ones the player reads.

| Offset | Bytes | Field | `ESIEGE2` | `ESIEGE3` |
|---|---|---|---|---|
| `0x00` | 4 | **id**, `LPF ` (`0x2046504c`) | | |
| `0x04` | 2 | | 256 | 256 |
| `0x06` | 2 | **the page count** | 2 | 2 |
| `0x08` | 4 | | 8 | 9 |
| `0x0c` | 2 | | 256 | 256 |
| `0x0e` | 2 | | `0x500` | `0x500` |
| `0x10` | 4 | **content type**, `ANIM` (`0x4d494e41`) | | |
| `0x14` | 2 | | 320 | 320 |
| `0x16` | 2 | | 200 | 200 |
| `0x18` | 2 | | 0, 0 | 0, 0 |
| `0x1a` | 1 | **a byte subtracted from the frame count** | 1 | 1 |
| `0x1b` | 37 | | 1, 0, 1, 0, 1, then 0 | the same |
| `0x40` | 4 | **the frame count** | 8 | 9 |
| `0x44` | 2 | **frames a second** | 15 | 15 |
| `0x46` | 58 | | 0 | 0 |
| `0x80` | 128 | | | |
| `0x100` | 1,024 | | | |
| `0x500` | 6 × 256 | the page table | | |
| `0xb00` | | the pages, page *i* at `0xb00` + `0x10000` × *i* | | |

A page table entry is three words: **the page's first record**, **its record count**, and the bytes its records take. A page starts with four words: **its first record**, **its record count**, the bytes its records take and a word that is 0 in both files; then a word per record, **its size**, and the records back to back. Every page but the last runs the full `0x10000` bytes to the next, and both files end at their last page's last record.

| File | Page 0 | Page 1 |
|---|---|---|
| `ESIEGE2.ANM` | records 0–6 | record 7 |
| `ESIEGE3.ANM` | record 8 | records 0–7 |

A record starts with a byte, **a byte that says whether extra bytes follow**, and **their length**, a word; when the second byte is not 0 the extra bytes, the length rounded up to even, come next. The rest of the record is the draw operations. The second byte is 0 in every record of both files.

### The draw operations

The operations change pixels from offset 0 on; an operation's first byte *b* decides it:

| *b* | Operation |
|---|---|
| 1–127 | copy the next *b* bytes to the next *b* pixels |
| 0 | fill: the next byte is a count *c* and the byte after it a colour; *c* pixels of that colour |
| `0x81`–`0xff` | skip *b* & `0x7f` pixels |
| `0x80` | an `int16` *w* follows. *w* > 0 skips *w* pixels; *w* = 0 ends the frame; for *w* < 0, with *c* = *w* & `0x3fff`, bit 14 clear copies the next *c* bytes and bit 14 set fills *c* pixels with the byte after *w* |

A skipped pixel keeps what the display holds. With a background set, a fill of colour 0 writes the background's pixels at those offsets in its place, and a copied 0 byte writes the background's pixel at its offset; without one, both write 0.

`VgaPlanar_FillRun` (`0001320f`) and `VgaPlanar_CopyRun` (`00013270`), given an offset *o*, a count, the colour or the source bytes, and the background, write each pixel to display memory through `FS` at byte (*o* >> 2) + the dword `00088ec8`, in the plane the map mask selects. The mask starts at entry *o* & 3 of the table at `0007e9a0`, `0x11`, `0x22`, `0x44`, `0x88`, and rotates left a bit per pixel, the byte address advancing when the bit leaves the top, so pixel *o* goes to plane *o* & 3. Each write first sends the mask to port `0x3c5`, whose index `VgaPlanar_SelectMaskRegisters` (`000131f8`) sets to 2 at port `0x3c4`, setting index 4 at port `0x3ce` too. The background is read at the same offset *o*. Both test the count after each pixel, so a count of 0 does not stop them at once.

## The player

The player is a `0x18`-byte object, which `Anm_Ctor` (`00013a3c`) allocates when given none and zeroes:

| Offset | Field |
|---|---|
| `+0x00` | the file's bytes |
| `+0x04` | the same buffer, the one freed |
| `+0x08` | the background's pixels, or null |
| `+0x0c` | the current frame |
| `+0x10` | the frame count − 1 − the byte `0x1a` |
| `+0x14` | the frame count − 1 |

`Anm_Free` (`00013bb4`) deletes the buffer at `+0x04` and zeroes the six fields; `Anm_Dtor` (`00013a7a`) frees the player and deletes it when its flag's bit 0 is set. `Anm_GetFrame` (`00013be6`) returns `+0x0c`; `Anm_SetFrame` (`00013bf7`) stores a frame *f* at `+0x0c` and returns 1 when a file is loaded and 0 ≤ *f* < `+0x14`, and returns 0 otherwise; `Anm_SetBackground` (`00013c30`) stores `+0x08`.

### Loading

`Anm_Load` (`00013aa8`), given the player and a path in stack order, frees the player and opens the path with `VolRStream_CtorFromPath`. When it opens, it allocates `VolRStream_GetSize` bytes, stores the buffer at `+0x04` and `+0x00`, and reads the whole file into it through the stream's slot `+0x1c`. The file is good when that read succeeds and the dwords `0x00` and `0x10` hold `LPF ` and `ANIM`. It sets `+0x14` and `+0x10` from the header either way and closes the stream. A file that does not open or is not good is freed. It destroys the stream and returns 1 for a good file and 0 otherwise. It also formats `unable to allocate rawData, size %d` and the size into a stack buffer that it does not use.

### Finding a frame's record

`Anm_FindFrameRecord` (`00013d9e`), given the player, takes the last page *i* below the page count whose table entry's first record ≤ the current frame < its first record + its record count, and returns null when there is none. In page *i*, at the buffer + `0xb00` + `0x10000` × *i*, the records start after the four header words and the sizes; it adds the sizes of the first (current frame − the page header's first record) records to reach the frame's. When the record's second byte is 0 its operations start at `+4`; otherwise at `+4` + *w* + (*w* & 1), *w* the word at `+2`.

### Drawing a frame

`Anm_DrawFrame` (`00013c46`), given the player, finds the frame's record, calls `VgaPlanar_SelectMaskRegisters`, and runs the record's operations with `+0x08` as the background until the end operation; a null record draws nothing.

### Playing

`Anm_Play` (`000132d8`), given a mode byte, a sound index and a path in stack order:

1. saves the source page `+0x34` and the draw page `+0x38` of the display object the dword `000ab616` points to, then makes the draw page the source page and the visible page `+0x30` the draw page, each store passed on to the raster ([the pages](../rendering/screen-pages.md#the-pages));
2. in a mode other than 0, makes a 320 × 200 bitmap over a new 64,000-byte buffer and calls `Bitmap_GrabClipDispatch` (`0005afa8`) with the bitmap's `+4`, the point (0, 0) and flags 0, which reads the source page's screen, the draw page the caller left, into it;
3. sets up a player with `Anm_Ctor`;
4. when the sound index is not −1 and the sound manager `ShellSound_Manager` (`00082768`) exists, destroys the sound object `0007e9a4` (slot `+0x08` with 3), makes a new one with `SoundManager_CreateObject`(manager, 2, 0, index) into `0007e9a4`, and, when the byte `Shell_SoundOption` (`0007fcb9`) is set and the object was made, calls `ShellMusic_FadeOut` (`000209e0`)(`ShellMusic_Object`) when the backdrop's music `ShellMusic_Object` (`0007fcfc`, [the backdrop](../shell/menus.md#the-backdrop)) is not 0, which fades the song to volume 1 over as many ticks as its volume and leaves it playing there, and then plays `0007e9a4` (slot `+0x10`, [sound objects](sound-samples.md#sound-objects));
5. calls `NoOp_Void` (`00012bde`), which returns at once;
6. loads the path with `Anm_Load`. For a good file, in a mode other than 0 it sets the 64,000 bytes as the background, and then repeats: in mode 0, a deadline 60 / frames a second (integer division) ticks past the clock `ShellTime_Ticks` (`0007e6fc`); `Anm_DrawFrame`; `Anm_SetFrame` with the current frame + 1, stopping when it returns 0; in mode 0, a wait until the clock reaches the deadline; `NoOp_Void`;
7. deletes the 64,000 bytes, calls `NoOp_Void`, builds the rectangle (0, 0)–(319, 199), makes the visible page the source page and puts the saved draw page back, copies the rectangle from the one to the other with `Region_CopyBetweenPages` (`00051c40`), so both pages hold the last frame, puts the saved source page back, and destroys the player (`Anm_Dtor` with 2) and the bitmap (`00050198`).

So the player draws frames 0 to the frame count − 2: `ESIEGE2.ANM`'s records 0–6, not its second page's record 7, and `ESIEGE3.ANM`'s records 0–7, not its first page's record 8. In mode 1 it draws the frames one after another with no wait between them, and frames a second goes unread.

## Which animation

The path is formatted into the buffer `000aacfe` at ten sites, found by a search of the disassembly for the two strings' addresses, each also setting the byte `0007fcd7`: `anm\esiege3.anm` with 1 at `0002a755`, `0002a934`, `0002ac9e`, `0002af68` and `0002b3f2`, and `anm\esiege2.anm` with 2 at `0002b7e5`, `0002d36d`, `0003b3e2`, `0003be3f` and `0003cc82`. The first follows `SingleMission_Start`.

`es2_xref.py` finds both calls to `Anm_Play` in `Shell_DrawScreen` (`0003c1b7`), each passing mode 1 and clearing `0007fcd7` after it:

- when `0007fcd7` is 2, with sound 10, `res32\menuclsa.raw`;
- later, when it is 1, after uploading the palette `0007fcd0` (`Palette_UploadWhole`) and clearing the byte `0007fcd4`, with sound 11, `res32\menuopn2.raw`.

The sound names are entries 10 and 11 of the shell's table at `000828c0` ([the shell's names](sound-samples.md#the-shells-names)).

So `esiege2.anm` closes over the screen being left, in its palette, before the new backdrop is drawn, and `esiege3.anm` opens over the new screen, drawn and in its palette ([drawing a screen](../shell/menus.md#drawing-a-screen)). The new backdrop's palette is the one `ShellBackdrop_Show` loads when `Shell_HandleMessage` changes the backdrop, which a byte of 2 forces ([changing screens](../shell/menus.md#changing-screens--shell_handlemessage-0003c7d8), step 8); the next frame uploads it, and waits 0.1 s on backdrop 1000 only while the byte is 0 ([presenting a frame](../rendering/screen-pages.md#presenting-a-frame)).

## Open

- **Open:** what the clock `ShellTime_Ticks` (`0007e6fc`) counts, for mode 0's ticks a frame; `es2_xref.py` finds both calls to `Anm_Play` passing mode 1.
- **Open:** the header's fields the player does not read, the 128 bytes at `0x80` and the 1,024 at `0x100`, the page table's and page header's byte counts, a record's first byte and its extra bytes; and what reads the player's `+0x10`, which the functions on this page set and do not read.
