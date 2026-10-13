# DBSIM.EXE and GO.EXE — digital sounds

**ES1's sound effects, voice lines and spoken lines are bare sample data: each file is the sound and nothing else.** ES2's are RIFF WAVs ([ES2's sample format](../../retail/formats/sound-samples.md#sample-format)); ES1's `.SFX` and `.RAW` files carry no header, and both programs load a file whole and hand all of it to the sound driver as one sample. The two programs' sound managers are the same code: addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)), and each `ES1GO` copy is given beside its `ES1DBSIM` original. The music, `.HMP`, goes through the same manager and is [`hmp-music.md`](hmp-music.md).

## Which files

| Archive | Folder | Files | What names them |
|---|---|---|---|
| `SIMSOUND` | `hmi\` | 52 `.SFX` | the simulator's catalog, `str\SOUNDS.STR`, as `hmi\<name>.sfx` ([The simulator's catalog](#the-simulators-catalog)) |
| `SIMVOICE` | `simvoice\` | 147 `.SFX`, `P1_01000.SFX` … | `SpeechLine_Load`, as `simvoice\<name>.sfx` ([Voice lines](#voice-lines)) |
| `SHELL0` | `res32\` | 3 `.SFX`, 14 `.RAW` | the shell's name table, which holds `BUTTON.SFX`, `XXXXXXXX.SFX`, `MENUCLSA.RAW`, `MENUOPN2.RAW` and `XXXXXXXX.RAW` ([The shell's names](#the-shells-names)) |
| `SPEECH` | `snc\` | 161 `.RAW` | the talking heads' lip-sync lists, each line played with its `.SNC` script ([`snc-lip-sync.md`](snc-lip-sync.md#loading-a-line)) |

`ROSEY1.SFX` and eleven `res32\` `.RAW` files are not in the shell's table; what plays them is [Open](#open).

## Layout

There is none: the file's content is the sample, and its size is the sample's length. The manager records the size the volume stream reports for the file, `SoundObject_LoadSample` reads that many bytes, and `DigiSound_Play` passes that length to the driver. No byte of the file is read as a field.

## The sound manager

One object, made at start-up, owns every sound: 60 names, 60 sound objects and the loaded data. The simulator's is `000959ac`, made by `Sound_Init` (`0004db14`) with `SoundManager_Ctor` (`0004eb40`); the shell's is `ES1GO 00082768`, made by `ShellSound_Init` (`ES1GO 00046235`) with `ES1GO 00041f64`. Its fields, from the functions below:

| Offset | Contents |
|---|---|
| `+0x000` | the digital driver block: device id, `0x78` at `+0x04`, 8 at `+0x0c`, 1 at `+0x10`, far pointers to the init block at `+0x14` and the hardware block at `+0x1c` |
| `+0x024` | the hardware block: port, IRQ and DMA channel at `+0x24`, `+0x28` and `+0x2c` |
| `+0x0b8` | a far pointer to the manager, the `+0x18` of the block at `+0x0a0` that the second MIDI driver is opened with ([The driver](#the-driver)) |
| `+0x0d8` | the init block: `0x1000` at `+0xd8`, 1 at `+0xe4`, the rate, 11,025, at `+0xe8`, and `0x13` at `+0xec` |
| `+0x134` | `int16` timer slot |
| `+0x13a`, `+0x140`, `+0x146` | far pointers to `sos\melodic.bnk`, `sos\drum32.dig` and `sos\drum.bnk`, read whole |
| `+0x14c` | 60 sound object pointers |
| `+0x241` | byte, the volume digital sounds are scaled by; 90 from the constructor |
| `+0x262` | per name, `int32` size |
| `+0x352` | per name, byte reference count |
| `+0x38e` | per name, 6-byte far pointer to the loaded data |
| `+0x4f6`, `+0x4fa`, `+0x4fe` | bytes loaded, their high-water mark and the limit, 300,000 from the constructor |
| `+0x502` | byte, the highest name index |
| `+0x503` | byte, set when loading may unload other sounds to make room |
| `+0x504` | the volume stream every sound is read through, `VolRStream_CtorFromPath` on `sos\sound.vol` |
| `+0x508` | the name table: a pointer to a null-terminated array of name pointers |

### Sound objects

A sound object plays one name. `SoundManager_CreateObject` (`0004f4e8`, `ES1GO 000429db`) makes one in the first free object slot (`SoundManager_FindFreeSlot`, `0004f4b0`, `ES1GO 00042985`; `SoundManager_GetObject`, `0004f660`, `ES1GO 00042b81`, returns a slot's object). It takes a kind, a priority and a name index, and returns 0 when the index is above `+0x502` or no slot is free. The index's name decides the kind: a name holding `.hmp` is a MIDI song (1), and one holding `.sfx` or `.raw` a digital sound (0), the test running in that order on what `0006f948` returns for the name. With no name, kind 2 returns 0. Kind 1 builds a `MidiSound`, any other kind a `DigiSound`, each told its slot, index, name and the manager; `CreateObject` then calls the object's slot `+0x2c` with its byte `+0x31` and stores the priority at `+0x34`.

| Offset | Contents |
|---|---|
| `+0x00` | the name |
| `+0x08` | byte, the object slot |
| `+0x09` | the name index |
| `+0x0d` | flags: bit 0 loaded, bit 1 has a name to load |
| `+0x11` | the manager |
| `+0x15` | byte, set for a sound placed in the world |
| `+0x17` | byte, the kind: 1 MIDI, 0 digital |
| `+0x18` | byte, plays even at volume 0 |
| `+0x19` | the voice or song handle, -1 when none |
| `+0x1d` | `int16`, the handle's copy, `0xffff` when none |
| `+0x1f` | `int16`, passed for a placed sound |
| `+0x21` | `int16`, passed to the driver and copied to `+0x23` on each play; when non-zero, a loop range may be passed with it |
| `+0x31` | byte volume |
| `+0x32` | `int16`, the placed sound's volume |
| `+0x34` | byte priority |
| `+0x3b` | the vtable |

The vtables' slots `+0x08`, `+0x10` and `+0x14` destroy, play and stop the object; `+0x28`, `+0x2c` and `+0x30` are [Open](#open).

| Class | Size | Vtable | Built by | Its own fields |
|---|---|---|---|---|
| `DigiSound` | `0x50` | `00095cd6` (`ES1GO 0008261c`) | `DigiSound_Ctor` (`000507c8`, `ES1GO 0004446c`) | `+0x3f` 2; `+0x43` and its switch `+0x47`, 0; `+0x48` and `+0x4c`, a loop range, -1 each |
| `MidiSound` | `0xcf` | `00095c82` (`ES1GO 000825bc`) | `MidiSound_Ctor` (`000504dc`, `ES1GO 0004412a`) | `+0x3f` 32 dwords, the track map, `0xff` each; `+0xbf` and `+0xc7`, the song block ([`hmp-music.md`](hmp-music.md#loading-and-playing)) |

Both constructors run `00050020` (`ES1GO 00043b28`) on the object first, and set the flags `+0x0d` to 2 when the object has a name, else 0.

### Naming and loading

`SoundManager_SetSampleNames` (`0004f888`, `ES1GO 00042e16`) takes a null-terminated array of name pointers and keeps it at `+0x508`; a name's index is its position. For each name it records the size the manager's volume stream gives (`VolRStream_Open`, `VolRStream_GetSize`) at `+0x262`, 0 for an empty name, and clears the name's data pointer and reference count. It sets `+0x502` to the count less one, but returns at once, leaving `+0x502` as it was, when a name fails to open.

`SoundManager_SetSampleName` (`0004f99c`, `ES1GO 00042f39`) puts one name at an index. It returns 0 for no name, an index above `+0x502`, or a name holding none of `.sfx`, `.raw` and `.hmp`. Every object already on that index must be of the name's kind, or it returns 0; each is stopped through its slot `+0x14`, unloaded by `00050164`, marked to load and not loaded, and given the new name. It then stores the name in the table, records its size and clears its data pointer, returning 1, or 0 when the file fails to open.

`SoundObject_LoadSample` (`000501ec`, `ES1GO 00043d8a`) returns 0 unless the object has a name to load, and 1 at once when it is loaded. When the bytes loaded plus the name's size would pass the limit, it returns 0 unless `+0x503` is set; otherwise it unloads one sound at a time until the sample fits, each time the loaded object whose non-zero priority is highest and at least this object's, ties going to the larger size, and whose slot `+0x30` returns 0, returning 0 when none qualifies. It then allocates the name's size, reads the whole file into it through the manager's stream, stores the data's pointer at `+0x38e`, counts a reference at `+0x352` (adding the size to the bytes loaded on the first, and raising the high-water mark), marks the object loaded and returns 1.

### Playing

`DigiSound_Play` (`000509c4`, `ES1GO 000446ab`), the `DigiSound` vtable's `+0x10`:

1. When `0006e055` reports more than 30 for the driver, it stops one digital sound to make room: of the digital objects whose slot `+0x30` returns non-zero, the one with the lowest `0005049c` value below 100, through its slot `+0x14`. With none, it returns 0.
2. It loads the sample, returning 0 when that fails.
3. The volume is the byte `+0x31` × `0x7fff` × the manager's `+0x241` / 10,000, or the word `+0x32` for a placed sound. At volume 0 with `+0x18` clear it returns 1 without playing.
4. It builds a start block and passes it to `HmiDigi_StartSample` (`0006dad9`, `ES1GO 0005e291`) with the driver handle `000bd8c8`, which is 0, and keeps the voice it returns at `+0x19` and the slot at `+0x1d`. When no voice is free it sets `+0x04` and `+0x19` to -1 and `+0x1d` to `0xffff` and returns 0.

| Block | Contents |
|---|---|
| `+0x00` | far pointer to the loaded file |
| `+0x08` | the length: the file's size |
| `+0x0c` | the object's `+0x21` |
| `+0x10` | the object's `+0x3f`, 2 |
| `+0x14` | the volume |
| `+0x18` | `0x1000` + the object slot |
| `+0x1c` | far callback `0004e7fc` |
| `+0x28` | flags: `0x4100`, `0x4300` for a placed sound, and bit `0x40` set with a loop range |
| `+0x30`, `+0x34` | when `+0x21` is non-zero and neither of `+0x48` and `+0x4c` is -1, those two |
| `+0x38` | `+0x43`, when `+0x47` is set |
| `+0x40` | for a placed sound, `+0x1f` |

`HmiDigi_StartSample` returns 0 when the dword `0009d0d8`[driver] is above `0xe105`, and -1 when all 32 of its voices are busy (bit `0x8000` of the voice's word `+0x30`). Otherwise it copies the block into the first free voice and returns the voice's number. The block carries no rate: the data plays at the driver's ([The driver](#the-driver)).

`DigiSound_Stop` (`00050c88`, `ES1GO 000449d8`), slot `+0x14`, passes a live voice to `0006dd7f` and sets `+0x19` to -1 and `+0x1d` to `0xffff`. `DigiSound_Dtor` (`00050838`, `ES1GO 000444f2`), slot `+0x08`, stops the sound, calls `000500d8`(object, 0), and frees the object when bit 0 of its argument is set.

## The driver

**The sound settings come from the install's configuration, except the rate, which the image fixes at 11,025 Hz.** `Sound_Init` (`0004db14`; `es2_xref` finds its one call, from `Sim_StartMission`) takes six values in stack order — MIDI port, port, DMA channel, IRQ, MIDI device and digital device — and returns without a manager when the digital device is 0. It stores them over the defaults block at `000959ec`, whose image values are a buffer of `0x1000`, the rate 11,025, `0x78`, DMA 1, IRQ 7, port `0x220`, digital device `0xe000`, MIDI port `0x388` and MIDI device `0xa002`. The buffer and the rate are not among the values it stores, and `es2_xref` finds one reference to the rate's dword `000959f0`, the read in `SoundManager_LoadDrivers`. The shell's block, `ES1GO 00082310`, holds the same values; `ShellSound_Init` fills its six from `resource.cfg`'s `[SoundFX]` `Driver#`, `IRQ`, `DMA` and `PortAddress` and `[Music]` `Driver#` and `PortAddress`, the driver numbers and ports read as hex, and asserts `ERROR: Run soundset first.` when the file does not open. With no `[SoundFX]` `Driver#` it makes no manager.

`Sound_Init` then makes the manager, runs `SoundManager_LoadDrivers` (`0004ef8c`), sets the memory limit through `00050008` (`0xffffffff`, or 200,000 with `+0x503` set in the low-memory mode), calls `0004ffe0`(manager, 100,000) and loads the catalog. It also sets the number of voice-line slots, 10, or 1 in the low-memory mode.

`SoundManager_LoadDrivers` (`0004ef8c`):

1. reads the three bank files whole with `SoundManager_LoadBanks` (`0004f6d4`), and fails when one does not load;
2. registers `0004e710` with `Timer_AddCallback` and keeps the slot at `+0x134`;
3. opens the MIDI device with `HmiMidi_InitDriver` (`00071076`), handle `000bd8c0`, and loads the melodic and drum banks into it through `00072275`;
4. fills the digital driver, hardware and init blocks above from the defaults block, sets `000bd8c8` to 0, and opens MIDI device `0xa005` with `HmiMidi_InitDriver` on the block at `+0xa0`, handle `000bd8c4`, loading `sos\drum32.dig` into it;
5. calls `0007225a`(0), sets `000bd8cc` and returns 1.

A failure formats `SoundManager::loadDrivers: %s` with `0006f23d`'s text for the error and, from the bank loads on, passes one or both MIDI handles to `0007144b`; it then calls `0006fe28`, `00071064` and `0006d5c2`(0) and returns 0. `HmiMidi_InitDriver` hands device `0xa005`'s driver the block's `+0x18` far pointer, the manager's address, and through it the digital driver, hardware and init blocks; how that driver opens the digital driver from them is [Open](#open).

ES2's loader plays a file that is not RIFF as raw 8-bit unsigned mono at 11,025 Hz ([ES2's sample format](../../retail/formats/sound-samples.md#sample-format)).

## The simulator's catalog

`SoundCatalog_Load` (`0004dc70`), the last step of `Sound_Init`, reads `str\SOUNDS.STR` row by row with `StringTable_ReadNext` into a name array (`000b18a4`) and an attribute pointer array (`000b1994`), and passes the names to `SoundManager_SetSampleNames`, so a row's number is its name index. For each row it makes `000bd63c`[row] with `SoundManager_CreateObject`(manager, 2, attribute byte 3, row), calls the object's slot `+0x28`(-1, -1, attribute byte 0) and `+0x2c`(attribute byte 1), and loads it at once when attribute byte 2 is set; the row count goes to `000bd72c`. `Sound_Play` and its siblings take a row number. The first ten rows name songs, `hmi\battle1a.hmp` five times and the five other `hmi\battle*.hmp` once each, and the eleventh `hmi\shipexpl.sfx`.

Each voice-line slot then gets `SoundManager_CreateObject`(manager, 0, 0, index) for the indices that follow the catalog's rows, kept at the slot's `+0x36` with the index at `+0x34`.

### Voice lines

`SpeechLine_Load` (`0004e0f0`) takes a script name and a voice name in stack order. It finds the voice-line slot (`000bd734`, `0x3a` bytes each) whose `+0x2b` holds `0004e208`'s key for the voice name, or takes one from `0004e08c` and stores the key. On a new slot, when the manager exists and the byte `000959e4` is set, it builds `simvoice\<voice name>.sfx` in the slot's `+0x0e`, names it as the slot's index with `SoundManager_SetSampleName`, and loads the slot's sound with `SoundObject_LoadSample`, returning 0 when that fails; then it loads the script with `Snc_Load` ([`snc-lip-sync.md`](snc-lip-sync.md#the-simulators-scripts)). It stores `000981c4` at `+0x2f` and returns the slot.

## The shell's names

`ShellSound_Init` (`ES1GO 00046235`; `es2_xref` finds its one call, from `ES1GO 00045d24`) gives the manager the 20 names at `ES1GO 000828c0`, `res32\xxxxxxxx.sfx` first, then the songs and sounds of the front end. Index 9, `res32\button.sfx`, is made into the sound `ES1GO 0007fd00` and loaded at once. Index 0 is a placeholder: `ShellSnc_OpenSound` names each talking-head line, `snc\<name>.RAW`, as index 0 and makes a sound on it ([`snc-lip-sync.md`](snc-lip-sync.md#loading-a-line)).

`ES1GO 000087ce` returns 1 when `vol\speech.vol`, the `.RAW` lines' archive, opens; `es2_xref` finds its one call, from `ES1GO 00008920`.

## Open

- **Open:** the sample format the driver mixes the data as. The bytes centre on `0x80`, as unsigned 8-bit samples would, and many files open on a run of `0x7f`–`0x81`.
- **Open:** how device `0xa005`'s driver, reached through the in-image table `HmiMidi_InitDriver` calls, opens the digital driver from the manager's blocks, and so whether 11,025 Hz is the rate the samples play at; and what the block's `0x78`, 8, 1, `0x1000`, 1 and `0x13` are.
- **Open:** what plays `ROSEY1.SFX` and the eleven `res32\` `.RAW` files the shell's table does not name. `ES1GO`'s resource name builder, the function holding the reference at `ES1GO 00047170`, rewrites a `.RAW` extension, and is a lead.
- **Open:** the sound object vtables' slots `+0x28`, `+0x2c` and `+0x30`; what the object's `+0x1f` and `+0x21` mean to the driver; the start block's `+0x10` (2 for every digital sound) and flags; and `+0x38` with the object's `+0x43` and `+0x47`.
- **Open:** what `0006e055` and `0005049c` measure in `DigiSound_Play`'s choice of a sound to stop.
- **Open:** what sets the byte `000959e4` that gates loading a voice line's sample, and what the caller of `ES1GO 000087ce` does with its answer.
- **Open:** `ES1GO 00041f64` and `ES1GO 0004240a`, which `ShellSound_Init` calls where `Sound_Init` calls `SoundManager_Ctor` and `SoundManager_LoadDrivers`.
- **Open:** what `SOUNDS.STR`'s attribute bytes 0 and 1 set, through the slots `+0x28` and `+0x2c`.
