# PLAYFLIC.EXE — the flics

**The intro and end movies, `FLIC\INTRO3.DFL` and `FLIC\END.DFL`, are Autodesk Animator Pro flics (`.FLC`) with chunk types of Dynamix's own, and each has its sound beside it in a headerless `.RAW` file.** The launcher plays them with `PLAYFLIC.EXE` ([the flics](../shell/launcher.md#the-flics)). The picture is 256-colour, 320 × 200, held as palette indices, with no block transform: the dithering seen on screen is in the frames' pixels.

## The player

`PLAYFLIC.EXE` (168,170 bytes, a DOS-extender program) is built on Jim Kent's `READFLIC`, the example flic decoder published with its C source in the March 1993 Dr. Dobb's Journal; its usage text says so and gives its switches. `Flic_Main` (`ES1PLAYFLIC 00000310`) takes the flic's name from the first argument and looks up each later argument that starts with `-` or `/` by its second character, either case, in the table at `ES1PLAYFLIC 000158e0`; an argument it does not find is passed over.

| Switch | Usage text | Sets |
|---|---|---|
| `-S`*n* | *n* milliseconds a frame | `atoi` of the rest; when not 0, it replaces the header's milliseconds a frame |
| `-Z`*n* | — | `000155f8`, milliseconds added to the sync point ([the sync chunk](#the-sync-chunk)) |
| `-B`*n* | the preload buffer's size | `000155fc`, 2,000,000 by default |
| `-C`*n* | the chunk load a frame | `00015600`, 10,000 by default |
| `-Q` | quiet mode, no sound | `000155f4` to 0 from its 1: the sound chunks do nothing ([the sound chunk](#the-sound-chunk)) and a sync on a sound returns at once |
| `-D` | debug mode | `000155f0` to 1, and calls `ES1PLAYFLIC 00004d48` |

So the launcher's `-B1000000 -S83` asks for a 1,000,000-byte preload buffer and 83 ms a frame, over the header's 71. Its strings also name HMI's digital and MIDI drivers (`HMIDIGIP`, `HMIMIDIP`), `Digi Sound %s`, `Midi Sound %s` and three sync messages, `SYNC to %s : Zero Adjust %ld ms`, `SYNC: wait for CLOCK tick %ld` and `SYNC: wait for SOUND byte %ld`. Its addresses are `ES1PLAYFLIC` image addresses ([the X-32 executable](x32-executable.md#the-image)).

### Playing a flic

**The player puts the screen into VGA mode `0x13` and decodes each frame straight into video memory, so a frame shows as it is decoded; it then waits for the frame's time on the PC's timer or, once a sync chunk moves it there, on the flic's sound, and any key ends the flic.** `Flic_Main` runs, in order:

1. `Machine_Open` (`ES1PLAYFLIC 00001e20`): the clock (`Clock_Open`, `ES1PLAYFLIC 00001c1c`), the screen (`Screen_OpenMode13`, `ES1PLAYFLIC 00001844`), and the sound drivers named in `RESOURCE.CFG` ([the drivers](#the-drivers)). The screen saves the BIOS video mode (`INT 10h` function `0x0f`), sets mode `0x13` (function 0, `AL` = `0x13`, which clears video memory), and fails unless the mode then reads back as `0x13`; its buffer is video memory at `0xa0000`, 320 × 200 with a stride of 320. A screen that fails ends the program with step 5's message. A sound card that is not found, or whose drivers do not open, still answers 0, and the flic plays.
2. `Flic_Open` (`ES1PLAYFLIC 000030f0`): reads the 128-byte header and checks its magic. `0xaf12`, a `.FLC`, keeps the header's frame 1 offset and milliseconds a frame; `0xaf11`, a `.FLI`, takes frame 1 at `0x80` and the header's speed as 70ths of a second, times 1000 / 70. Another magic fails.
3. A non-zero `-S` replaces the milliseconds a frame, and `Flic_CenterOnScreen` (`ES1PLAYFLIC 00000510`) sets the origin to ((screen width − flic width) / 2, (screen height − flic height) / 2), (0, 0) for both flics.
4. `Flic_Play` (`ES1PLAYFLIC 00003530`), then `Flic_Close` (`ES1PLAYFLIC 000031a4`) and `Machine_Close` (`ES1PLAYFLIC 00002104`), which sets the saved video mode back (`Screen_Close`, `ES1PLAYFLIC 000018b8`) and closes the sound drivers (`SoundSys_Close`, `ES1PLAYFLIC 00006110`), ending any sound still playing.
5. An error other than a key's −10 prints `READFLIC had troubles with %s: %s` with the flic's name and `Flic_ErrorText` (`ES1PLAYFLIC 000036a8`).

`Flic_Play` opens the preload buffer (`Preload_Open`, `ES1PLAYFLIC 00000554`), reads the clock as the start, and sets the sync to the clock (`0002566c` = `0xd`) with the frame count `00025670` at 1. Then, once for each of the header's frames:

1. With a palette fade running (`00025662` not 0), one step of it ([the palette fades](#the-palette-fades)).
2. `Flic_NextFrame` (`00003348`) decodes the next frame; an error ends the flic.
3. `Flic_FrameDue` (`ES1PLAYFLIC 000033a8`) gives the frame's due time, and the frame count goes up by one. On the clock (sync `0xd`) it is ⌊speed × 1,193,180 / 1000⌋ × *count* + *start*, in the 8253 timer's ticks, 1,193,180 a second (`Clock_Read`, `ES1PLAYFLIC 00001c40`); with the sync on a sound (any other value) it is ⌊speed × 11,025 / 1000⌋ × *count* + *start*, in bytes of that sound ([the sound's position](#the-sounds-position)), each product and sum unsigned 32-bit. Its first call on a sound also stores ⌊speed × 1,193,180 / 1000⌋ at `00015c34`, that times the `double` 0.05 at `000248f8` (through the C library's `__LNGDBL@`, `__DMUL@` and `__DBLLNG@`, `00008075`, `00007ea8`, `00008110`) and the clock at `000248fc` ([Open](#open)).
4. `Flic_WaitFrame` (`ES1PLAYFLIC 00003440`) waits for that time. On the clock it loops: a key waiting (`Key_Poll`, `ES1PLAYFLIC 00001ca4`, `INT 16h` function 1) is read (`Key_Read`, `ES1PLAYFLIC 00001ce8`, function 0) and ends the flic with −10; otherwise, once the clock has reached the due time, the next frame. With the sync on a sound it loops the same way on the position of the first sound in the list whose number is the sync's; with no such sound it neither waits nor reads a key.

So on the clock frame *k*, counting from 0, is decoded when the clock reaches *start* + *k* periods, and a frame that is late is decoded at once; both flics move to their sound in frame 0 ([the sync chunk](#the-sync-chunk)). At 83 ms a period is 99,033 ticks. A key the BIOS buffers ends the flic at the next wait; a shift key alone puts nothing in the buffer. Once every frame has played, the last frame's wait ends the loop: the flic plays once, with no ring frame, and the screen goes back to the saved mode.

## The header

The 128-byte header is Animator Pro's:

| Offset | Field | `INTRO3.DFL` | `END.DFL` |
|---|---|---|---|
| `+0x00` | `uint32` file size | 5,391,630 | 726,233 |
| `+0x04` | `uint16` magic | `0xaf12` | `0xaf12` |
| `+0x06` | `uint16` frames | 1,074 | 397 |
| `+0x08`, `+0x0a` | `uint16` width, height | 320, 200 | 320, 200 |
| `+0x0c` | `uint16` bits a pixel | 8 | 8 |
| `+0x0e` | `uint16` flags | 3 | 3 |
| `+0x10` | `uint32` milliseconds a frame | 71 | 71 |
| `+0x26`, `+0x28` | `uint16` aspect | 6, 5 | 6, 5 |
| `+0x50` | `uint32` offset of frame 1 | `0x80` | `0x80` |
| `+0x54` | `uint32` offset of frame 2 | `0x167a` | `0x7a8e` |

## The frames

Every top-level chunk is a frame (`0xf1fa`), 16 bytes of header (a `uint32` size, the `uint16` type, a `uint16` count of subchunks and 8 more bytes) and its subchunks; each subchunk is a `uint32` size, a `uint16` type and its data. `Flic_NextFrame` (`ES1PLAYFLIC 00003348`) reads a chunk and passes a frame whose size is not 4 to `Flic_DecodeFrame` (`ES1PLAYFLIC 000031d0`), which walks its subchunks and dispatches each by type: Animator Pro's 4, 7, `0xb`, `0xc`, `0xd`, `0xf` and `0x10`, and Dynamix's `0x14` to `0x18`. Any other type, `PSTAMP` among them, is passed over. A top-level chunk of another type ends the flic with error −4. The two files hold these subchunks:

| Type | Animator Pro's | `INTRO3.DFL` (count, bytes) | `END.DFL` (count, bytes) |
|---|---|---|---|
| `0x04` | `COLOR_256`, palette packets of 8-bit channels ([below](#the-palette-chunks)) | 45, 25,450 | 1, 778 |
| `0x0d` | `BLACK` | 1, 6 | — |
| `0x12` | `PSTAMP`, the thumbnail | 1, 690 | 1, 2,712 |
| `0x14` | not Animator Pro's: the picture ([below](#the-picture-chunk)) | 578, 5,347,920 | 307, 716,187 |
| `0x15` | not Animator Pro's: a sound ([below](#the-sound-chunk)) | 2, 54 | 2, 54 |
| `0x16` | not Animator Pro's: a palette fade ([below](#the-palette-fades)) | 8, 176 | — |
| `0x18` | not Animator Pro's: the sync ([below](#the-sync-chunk)) | 1, 22 | 1, 22 |

`0x14` carries the picture: 99% of `INTRO3.DFL`'s bytes, in 578 of its 1,074 frames. The other frames hold no `0x14`, so their picture does not change. Animator Pro's own picture chunks (`DELTA_FLC` 7, `DELTA_FLI` 12, `BYTE_RUN` 15, `FLI_COPY` 16) are in neither file, and neither holds a `0x17`.

`BLACK` (`Flic_DecodeBlack`, `ES1PLAYFLIC 00002ff8`) writes index 0 over the flic's rect at its origin, a row at a time.

### The palette chunks

**A `COLOR_256` chunk always updates the player's copy of the palette, and sends it to the VGA's DAC only while no palette fade runs.** `Flic_DecodeColor256` (`ES1PLAYFLIC 00002d00`) and `Flic_DecodeColor64` (`0xb`, `ES1PLAYFLIC 00002d1c`) both run `Flic_DecodeColorPackets` (`ES1PLAYFLIC 00002c54`): a `uint16` count of packets, each a byte of entries to skip, a byte count of entries (0 for 256) and three bytes for each, red, green and blue. Each packet's entries are copied into the palette copy at `00025056`; with `00025662` at 0 they are also sent to the DAC from the packet's first entry. `Vga_SetDac8` (`ES1PLAYFLIC 00001af0`) sends `COLOR_256`'s 8-bit channels shifted right by 2, `Vga_SetDac6` (`ES1PLAYFLIC 00001b68`) `COLOR_64`'s as they are.

### The palette fades

**A `0x16` chunk fades the screen between the palette and one color: at once in a loop that holds the player, or a step a frame.** `Flic_DecodeFade` (`ES1PLAYFLIC 0000286c`) reads four `uint32`: the direction, the kind, the steps *n* (0 taken as 1) and the color, 12-bit `0x0RGB`, each nibble shifted up by 4 into an 8-bit channel. For a step *k* of *n*, every entry *i* of the 256 is *P*ᵢ + (*C* − *P*ᵢ) × *k* / *n* in each channel, *P* the palette copy and *C* the color, in signed integer arithmetic truncated toward zero, and the whole palette goes to the DAC (`Vga_SetDac8`).

| Direction | Kind 8: at once | Kind 9: a step a frame |
|---|---|---|
| 6, from the color to the palette | steps *k* = *n* down to 1, then the palette itself | `00025662` = *n*; a step now, and one before each later frame, with *k* the remaining count after it goes down by one: *n* − 1 twice, then *n* − 2 down to 0, the palette |
| 7, from the palette to the color | steps *k* = 1 up to *n*, the last the color | the same count, with *k* = *n* − the remaining count: 1 twice, then 2 up to *n*, the color |

A kind-8 fade writes its *n* palettes back to back, with no wait, and ends any running fade (`00025662` and `0002565e` to 0); it takes as long as the DAC takes *n* × 768 port writes, and the frame clock runs on meanwhile, so the frames after it are decoded at once until they catch up. A kind-9 fade's count reaches 0 at its last step, which lets `COLOR_256` reach the DAC again. Another direction or kind does nothing. `Flic_FadeStep` (`ES1PLAYFLIC 000027a8`) is the kind-9 step.

`INTRO3.DFL`'s eight, all to or from black:

| Frame | Direction, kind, steps |
|---|---|
| 0, 109 | 6, 9, 32,000 |
| 1, 110 | 6, 8, 300 |
| 100 | 7, 8, 150 |
| 360 | 7, 8, 300 |
| 389 | 6, 9, 6 |
| 1061 | 7, 9, 9 |

So frame 0's fade, before its `COLOR_256`, holds the screen black: at step 31,999 of 32,000 from black every channel comes out at most 1, which the shift by 2 makes 0. Frame 1's fades the picture in at once; frames 109 and 110 do the same after frame 100's fade out.

### The picture chunk

**A `0x14` chunk changes the screen buffer in place: it skips the pixels that stay, writes new ones, and copies runs from elsewhere on the screen.** `Flic_DecodeChunk14` (`ES1PLAYFLIC 00002c1c`) starts a cursor at the frame's origin on the screen buffer, the flic's y offset (`+0x90`) times the buffer's stride plus its x offset (`+0x8c`), and `Flic_DecodeCopyDelta` (`ES1PLAYFLIC 00003e19`) runs the chunk's commands from there. The data is a `uint32` count of commands, of which the player counts the low 16 bits, then the commands. A command's opcode is the low 3 bits of its first byte, taken through the jump table at `ES1PLAYFLIC 00003c7c`; its fields are read from the same bytes, *w0* and *w1* the command's first and second `uint16` and *b0* and *b1* its first and second byte. Every shift of a signed field keeps its sign.

| Opcode | Bytes | Offset | Count *n* | Does |
|---|---|---|---|---|
| 0 | 2 | | *w0* >> 3 | moves the cursor on *n* pixels |
| 1 | 2 + *n* | | *w0* >> 3 | copies the *n* bytes after the command |
| 2 | 2 | signed *b0* >> 3 | *b1* | copies *n* pixels from the cursor plus the offset |
| 3 | 2 | signed *b1* | *b0* >> 3 | the same |
| 4 | 2 | signed *b0* >> 3, less the stride | *b1* | the same, from the line above |
| 5 | 2 | signed *b0* >> 3, plus the stride | *b1* | the same, from the line below |
| 6 | 4 | signed *w0* >> 3 | *w1* | the same |
| 7 | 4 | signed *w1* | *w0* >> 3 | the same |

Every command but 0 writes its *n* pixels at the cursor and moves it on by *n*. Pixels a skip passes over keep what the buffer holds. Commands 2 to 7 copy from the screen buffer itself, as it stands: *n* / 2 steps of two bytes, each read and then written (`REP MOVSW`), then one byte more when *n* is odd. So where the source overlaps the run being written, a step reads bytes that earlier steps of the same command wrote. The offsets reach −16 to 15 pixels (2, 4, 5), −128 to 127 (3), −4,096 to 4,095 (6) and −32,768 to 32,767 (7), and the counts 255, 31, 65,535 and 8,191.

### The sound chunk

**A `0x15` chunk loads, starts or stops a sound by its number.** `Flic_DecodeSound` (`ES1PLAYFLIC 000023ec`) does nothing under `-Q` (`000155f4` at 0). Otherwise it reads a `uint32` operation, a `uint32` number and a NUL-ended name, and acts on the list at `00015c2c`, whose nodes hold the number at `+0`, a sample object at `+4` and the next node at `+0x80`; a lookup takes the first node of the number.

| Operation | Does |
|---|---|
| 0 | loads `pathFlic` + the name ([the sound](#the-sound)) into a new node, put at the head of the list when the file holds a byte |
| 1 | starts the sound of the number (`DigiSample_Start`, `ES1PLAYFLIC 000088cc`) |
| 2 | stops it (`DigiSample_Stop`, `ES1PLAYFLIC 00008918`, `HmiDigi_StopSample`) |
| 3 | loads a song, `pathFlic` read from `[FlicPlay]`, into a node of the list at `00015c30` |
| 4, 5 | act on the song of the number, through `00008aac` and `00008afc` |

Each file's frame 0 holds two, before its sync chunk, palette and picture: operation 0 on sound 1000, `introb3.raw` or `end.raw`, then operation 1 on sound 1000, whose name field holds `s:\utils\pc\`, which it does not read. Neither file holds operations 2 to 5.

### The sync chunk

**The `0x18` chunk moves the frames' timing from the clock to a sound, unless the player is quiet.** `Flic_DecodeSync` (`ES1PLAYFLIC 0000213c`) reads four `uint32`: the sync (`0xd` the clock, `0xe` a sound), a flag, the sound's number and a zero adjust in milliseconds. Both files' is in frame 0, after the sound starts: `0xe`, 1, 1000 and 150 (`INTRO3.DFL`) or 1450 (`END.DFL`).

- For a sound with `-Q` given, it returns at once and the frames stay on the clock.
- Otherwise the new start is the clock now (`0xd`) or that sound's position (`0xe`, 0 with no such sound loaded); another sync value leaves the start as it was. With the flag set the zero adjust is added to it, ⌊adjust × rate / 1000⌋ unsigned; `-Z`'s milliseconds are added whatever the flag, the same way on the clock and with a signed division on a sound. The rate is the clock's for `0xd` and 11,025 bytes a second for any other value. The sync becomes the chunk's, with the frame count at 1.
- With the flag set it then waits for the new start, on the clock or on the sound, as `Flic_WaitFrame` waits, except that a key is read and ends only this wait; with the sync on a sound of no number loaded, it does not wait.

So each flic's screen stays as it was, black, until its sound has played the zero adjust past the position at the sync, 1,653 bytes (`INTRO3.DFL`) or 15,986 (`END.DFL`), the wait coming before frame 0's palette and picture. At `-S83` frame *k* is then decoded once the sound has played *start* + 915 × *k* bytes, and the flic ends once it has played *start* + 915 × the frames: *start* + 982,710 against `INTROB3.RAW`'s 984,686 bytes, and *start* + 363,255 against `END.RAW`'s 400,700 ([Open](#open)).

## The sound

`FLIC\INTROB3.RAW` (984,686 bytes) and `FLIC\END.RAW` (400,700 bytes), the names the `0x15` chunks hold, have no header: they open on bytes around `0x7e`–`0x80`, unsigned 8-bit samples about silence.

**Operation 0 reads the whole file into memory; a start plays it once through HMI's digital driver, and its position is how many of its bytes the card has played.** The path is `RESOURCE.CFG`'s `[FlicPlayer]` `pathFlic`, `.\` when missing, then the name. `Profile_GetString` (`ES1PLAYFLIC 00000e58`) is `ES1DBSIM`'s `Profile_GetString` ([profile files](profile-files.md)) with two differences: the value passes through `000017d4` ([Open](#open)), and an empty one is passed over for a later line of the key, then the default. The CD's own `RESOURCE.CFG` has `pathFlic=D:\flic\`.

1. The node is 0x84 bytes from `malloc`, its number set and its sample object made by `DigiSample_Init` (`ES1PLAYFLIC 0000860c`)(object, number, driver): `HmiDigi_StartSample`'s start block ([playing](sound-samples.md#playing)) with no data, loop count 0, `+0x10` 2, volume `0x7fff`, its id the number, flags `0x100`, `+0x40` `0x8000`, the driver at `+0x78` and the loaded state `+0x7c` 0. The driver is the sound system's `+0xb9` (`SoundSys_DigiDriver`, `ES1PLAYFLIC 000061a8`), the `[Music]` device's handle, which `HmiMidi_InitDriver` gives the first free of its five slots ([the drivers](#the-drivers)).
2. `DigiSample_Load` (`ES1PLAYFLIC 000086c8`)(object, path) frees data a previous load read (`DigiSample_Free`, `ES1PLAYFLIC 0000868c`), sets the loop count to 0 and reads the file with `File_LoadWhole` (`ES1PLAYFLIC 0000c3f8`): its size from `filesize`, that many bytes from `malloc`, read whole; a file that does not open prints `FAIL: '%s' no exist` through `printf` and gives size 0. With a size not 0 the data and size go to `+0` and `+8`, the state to 1, and the load succeeds.
3. On success `DigiSample_SetVolume` (`ES1PLAYFLIC 000087d0`) sets `0x7fff` again with flag `0x100`, and the node goes on the list. The node's next pointer at `+0x80` is the object's `+0x7c`, so linking it overwrites the loaded state with the old head, 0 for the first sound. `DigiSample_Free`, which reads the state, runs before a load, and `Flic_DecodeSound` loads each object once.
4. `DigiSample_Start` calls `HmiDigi_StartSample`(driver, object), the simulator's own ([playing](sound-samples.md#playing)), and keeps the voice it answers at `+0x74`, 0 for −1. `DigiSample_Stop` calls `HmiDigi_StopSample`(driver, voice).

### The sound's position

**`DigiSample_Position` (`ES1PLAYFLIC 00008814`)**, (object): `HmiDigi_GetBytesProcessed` (`ES1PLAYFLIC 00009543`)(driver, voice), the voice record's `+0x4c` while bit `0x8000` of its word `+0x30` is set and 0 otherwise, plus how far the DMA transfer is past the driver's place in its `0x1000`-byte buffer. That place is the dword the driver's far pointer at `0001698c` names (`HmiDigi_GetDmaPosition`, `ES1PLAYFLIC 000093fd`). The transfer's is `0x1000` less *n*, where *n* is one more than the count the DMA controller gives, doubled for a count port above 7, a 16-bit channel's: port `000c` is cleared, then the channel's count port, from the table at `00016c50` by the driver's channel byte at `00016c70`, read twice. The distance forward from the driver's place to the transfer's, 1 to `0x1000`, is halved when the dword at `000169f0` + driver × `0x6c` is not 0, and added to the bytes processed.

### The drivers

`Machine_Open` reads `[SoundFX]` `Driver#` (hexadecimal), `DMA`, `IRQ` and `PortAddress`, `[Music]` `Driver#` and `PortAddress`, and `[SoundSet]` `pathDrivers`. With a `[SoundFX]` `Driver#` of 0 or none it detects the card in the drivers' path (`SoundSys_SetDriverPath`, `ES1PLAYFLIC 000061b8`; `SoundSys_Detect`, `000062a8`; `SoundSys_EndDetect`, `0000620c`); with another it takes the device and the three values as the card (`SoundSys_ForceCard`, `ES1PLAYFLIC 0000639c`). With a card, `SoundSys_Open` (`ES1PLAYFLIC 00005f08`) opens the drivers as the simulator's `SoundManager_LoadDrivers` does ([the driver](sound-samples.md#the-driver)), its init block (`00005c48`) a `0x1000`-byte buffer the driver allocates and the rate 11,025: the `[Music]` device, `0xa002` and port `0x388` when 0, with `HmiMidi_InitDriver` into `+0xb9`, then device `0xa005` into `+0xbd`, then `00005db4`, its state `+0xb8` set to 1 when all three succeed. A failure closes the handles that opened and answers 0; `+0xb9` is −1 when the first open failed, and keeps its handle otherwise. `SoundSys_Close` undoes an open whose state is 1 and does nothing otherwise. With no card found, the zeroed machine leaves `+0xb9` and `+0xb8` at 0.

## Open

- **Open:** the fields of `0x17`, which `Flic_DecodeFrame` passes to `ES1PLAYFLIC`'s `FUN_00002b64`; neither file holds one.
- **Open:** what the player does when the sound drivers do not open, by a card not found or a forced one that fails: the `0x15` load still reads the file, and the start and the position run on driver number `+0xb9`, 0 or −1, through driver tables the open did not fill. `HmiDigi_StartSample` loads the far pointers of driver 0's voice table at `000165ac`, which are zero in the image, and reads through them; which of the 26 references `es2_xref` finds to the table fill it is not read. A retail run with the card's open failing would show what the flics do.
- **Open:** what the position answers once a sound has played to its end, which turns on whether the driver's mixer clears bit `0x8000` of the voice's word at the end, as `HmiDigi_StopSample` does at a stop ([sound-samples.md](sound-samples.md#open)). `INTRO3.DFL`'s last wait is for 984,363 bytes plus the position at its sync: 323 bytes, 29 ms, short of `INTROB3.RAW`'s 984,686 when that position is 0. The DMA term alone can make the position larger than 323, which puts the wait past the sound's end, so whether the intro ends by itself or holds its last frame until a key turns on this.
- **Open:** the `.RAW` files' sample rate and channels. The driver's init block gives 11,025, as the simulator's does, with the same Open about the rate the driver plays at ([sound-samples.md's Open](sound-samples.md#open)). At 11,025 samples a second, mono, `INTROB3.RAW` lasts 89.3 s, and 1,074 frames at 83 ms last 89.1 s; `Flic_FrameDue` and `Flic_DecodeSync` count a sound's position at 11,025 bytes a second.
- **Open:** what `000017d4`, which `Profile_GetString` passes a value through, does.
- **Open:** a reader of `00015c34`, `000248f8` and `000248fc`, which `Flic_FrameDue`'s first call on a sound writes: `es2_xref` finds only that write of each, and `000248f8`'s address besides as the `.bss` clear's start and the bounds of two loops over the function table that ends there.
- **Open:** how long a kind-8 palette fade takes on period hardware and under DOSBox-X, which only the speed of the DAC's port writes sets.
