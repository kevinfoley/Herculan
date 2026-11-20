# DBSIM.EXE and GO.EXE — the sound manager

**One object, made at start-up, owns every sound: 60 names, 60 sound objects and the loaded data.** The simulator's is `Sound_Manager` (`000959ac`), made by `Sound_Init` (`0004db14`) with `SoundManager_Ctor` (`0004eb40`); the shell's is `ShellSound_Manager` (`ES1GO 00082768`), made by `ShellSound_Init` (`ES1GO 00046235`) with its copy of the constructor ([the shell's sound manager](../shell/sound.md)). The two programs' sound managers are the same code: addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](../formats/x32-executable.md#the-image)), and each `ES1GO` copy is given beside its `ES1DBSIM` original or in [the copies](#the-copies). The files it plays are [`sound-samples.md`](../formats/sound-samples.md), and the drivers it loads are [`sound-drivers.md`](sound-drivers.md).

## Where the object files start and end

The simulator's sound file, `0004db14`–`0004e70f`, follows `appinput.cpp`'s exit routine `AppInput_ExitNoOp` (`0004db10`) and three zero bytes, and opens with `Sound_Init` and no `ConfigPart` run. It ends as the files placed from `0001c800` do ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)), with its static initializer, its exit routine and the initializer's element constructor, whose `RET 4` at `0004e70d` ends on a 4-byte boundary:

| Function | Address | Table entry | Does |
|---|---|---|---|
| `Sound_StaticInit` | `0004e6b0` | `000adc64` | `__vec_ctor` over `SoundCatalog_Positions` (`000b1b74`), 60 elements of 12 bytes, the position a placed catalog sound is given ([the catalog](../simulation/sound-catalog.md#the-simulators-catalog)), with `maybe_Point3I_CtorSoundCopy` by its padding `0004e6dd`; then `StringTable_Ctor`(`SoundCatalog_StringTable`, `000b1e44`) |
| `Sound_StaticDtor` | `0004e6d0` | `000adcc8` | `OwnedArray_Dtor`(`SoundCatalog_StringTable`, 2), which frees the table's buffer, leaving the positions |
| `maybe_Point3I_CtorSoundCopy` | `0004e6e0` | | the code of `maybe_Point3I_Ctor` (`000633fc`) |

The first two are frameless, with no Ghidra function. ES2's `Sound_StaticInit` (`0046349c`) builds the string table alone, and its `Sound_StaticDtor` (`004634cc`) is ES1's. The lip-sync player is the end of the file, `0004e39c`–`0004e6ae` ([`snc-lip-sync.md`](../formats/snc-lip-sync.md#playing-a-line)). `SoundManager_TimerCallback` (`0004e710`), the far call through `000b1ed0` that `SoundManager_LoadDrivers` registers with the timer, follows; the files from there to the [HMI timer's](sound-drivers.md#the-digital-drivers-timer), and the sound file's source name, are [Open](#open).

## The manager's fields

From the functions below:

| Offset | Contents |
|---|---|
| `+0x000` | the digital driver block, which device `0xa005` opens the digital driver from ([opening](drum-device.md#opening)): device id, `0x78` at `+0x04`, 8 at `+0x0c`, 1 at `+0x10`, far pointers to the init block at `+0x14` and the hardware block at `+0x1c` |
| `+0x024` | the hardware block: port, IRQ and DMA channel at `+0x24`, `+0x28` and `+0x2c` |
| `+0x0b8` | a far pointer to the manager, the `+0x18` of the block at `+0x0a0` that the second MIDI driver is opened with ([The driver](sound-drivers.md#the-driver)) |
| `+0x0d8` | the init block: `0x1000` at `+0xd8`, 1 at `+0xe4`, the rate, 11,025, at `+0xe8`, and `0x13` at `+0xec` |
| `+0x134` | `int16` timer slot, `0xffff` from the constructor |
| `+0x13a`, `+0x140`, `+0x146` | far pointers to `sos\melodic.bnk`, `sos\drum32.dig` and `sos\drum.bnk`, read whole |
| `+0x14c` | 60 sound object pointers |
| `+0x23c` | the distance a placed sound is heard to, 100,000 from the constructor and `Sound_Init` ([placed sounds](#placed-sounds)) |
| `+0x240` | byte, the shift placed sounds' distances are taken with, 10 from the constructor |
| `+0x241` | byte, the master volume, 0–100, which digital sounds are scaled by; 90 from the constructor |
| `+0x242` | the listener's frame, a `0x20`-byte transform in the layout `Transform_RotatePoint` (`0005ef74`) takes: the Q14 rotation's nine words and its kind byte in the first five dwords, the position in the last three (`+0x256`, `+0x25a`, `+0x25e`); the constructor builds it inline |
| `+0x262` | per name, `int32` size |
| `+0x352` | per name, byte reference count |
| `+0x38e` | per name, 6-byte far pointer to the loaded data |
| `+0x4f6`, `+0x4fa`, `+0x4fe` | bytes loaded, their high-water mark and the limit, 300,000 from the constructor |
| `+0x502` | byte, the highest name index |
| `+0x503` | byte, set when loading may unload other sounds to make room; 1 from the constructor |
| `+0x504` | the volume stream every sound is read through, `VolRStream_CtorFromPath` on `sos\sound.vol` |
| `+0x508` | the name table: a pointer to a null-terminated array of name pointers |

The constructor leaves the per-name tables `+0x262`, `+0x352` and `+0x38e`, and `+0x502`, as the allocation gave them; `SoundManager_SetSampleNames` sets them ([naming and loading](#naming-and-loading)).

## Sound objects

A sound object plays one name. `SoundManager_CreateObject` (`0004f4e8`, `ES1GO 000429db`) makes one in the first free object slot (`SoundManager_FindFreeSlot`, `0004f4b0`, `ES1GO 00042985`; `SoundManager_GetObject`, `0004f660`, `ES1GO 00042b81`, returns a slot's object). It takes a kind, a priority and a name index, and returns 0 when the index is above `+0x502` or no slot is free; the index is compared as a signed `int16` with the byte `+0x502`, so a negative one passes and reads the name table before its start. The index's name decides the kind: a name holding `.hmp` makes it 1, a MIDI song, and then one holding `.sfx` or `.raw` makes it 0, a digital sound, so `.sfx` or `.raw` wins over `.hmp`; a name holding none of the three keeps the kind passed. The tests run on the name lower-cased in place by `Str_ToLowerInPlace` (`0006f948`), (string), C convention, which passes each byte up to the zero through `tolower` and returns the string, the sibling of `Str_ToUpperInPlace` ([voice lines](../simulation/sound-catalog.md#voice-lines)): the table's own string is lower-cased, and the object holds it. With no name, or an empty one, kind 2 returns 0 and any other kind goes on with no name. Kind 1 builds a `MidiSound`, any other kind a `DigiSound`, each told its slot, index, name and the manager; it then calls the object's slot `+0x2c` with its byte `+0x31` and stores the priority at `+0x34`.

| Offset | Contents |
|---|---|
| `+0x00` | the name |
| `+0x08` | byte, the object slot |
| `+0x09` | the name index, an `int16` argument sign-extended |
| `+0x0d` | flags: bit 0 loaded, bit 1 has a name to load |
| `+0x11` | the manager |
| `+0x15` | byte, set for a sound placed in the world ([placed sounds](#placed-sounds)) |
| `+0x16` | byte, 0 from the constructor; `Snc_Load` clears it ([`snc-lip-sync.md`](../formats/snc-lip-sync.md#playing-a-line)) |
| `+0x17` | byte, the kind: 1 MIDI, 0 digital |
| `+0x18` | byte, plays even at volume 0 |
| `+0x19` | the voice or song handle, -1 when none |
| `+0x1d` | `int16`, the handle's copy, `0xffff` when none |
| `+0x1f` | `int16`, the pan a placed sound plays with, `0x8000` from the constructor |
| `+0x21` | `int16`, the repeat count, 0 from the constructor; passed to the driver and copied to `+0x23` on each play |
| `+0x23` | `int16`, the playing sound's repeat count, which slot `+0x28` changes while the sound plays |
| `+0x25` | three `int32`, the placed sound's position |
| `+0x31` | byte, the volume, 0–100; 100 from the constructor |
| `+0x32` | `int16`, the placed sound's volume |
| `+0x34` | byte, the priority; `0xff` from the constructor, then `SoundManager_CreateObject`'s |
| `+0x35`, `+0x39` | a far pointer, 0 from the constructor, that `MidiSound_Play` passes to `0006eeae` ([`song-player.md`](song-player.md#loading)) |
| `+0x3b` | the vtable |

**`SoundObject_Ctor` (`00050020`, `ES1GO 00043b28`)**, (memory or 0): allocates `0x3f` bytes when given none, returning 0 when that fails, stores the base vtable `SoundObjectVtable` (`00095c46`) and the defaults above, and returns the object. Both class constructors run it first and set the flags `+0x0d` to 2 when the object has a name, else 0. **`SoundObject_Dtor` (`000500d8`)**, (object, flags): stores the base vtable again, unloads the sample with `SoundObject_Unload`, clears the manager's object slot (`+0x14c`[`+0x08`]) and frees the object when bit 0 of the flags is set. **`SoundObject_Unload` (`00050164`)**: stops the sound through slot `+0x14` when slot `+0x30` says it plays; when it is loaded, takes one off the name's reference count `+0x352`, and at 0 frees the name's data `+0x38e`, clears that pointer and takes the name's size `+0x262` off the bytes loaded `+0x4f6`; then clears the loaded bit. **`maybe_SoundObject_SetBranchCallback` (`00050450`)**, (object, offset, selector): for a song (`+0x17` set) stores the far pointer at `+0x35`; `es2_xref` finds no caller in either image, and the manager's debug listing ([the debug listing](#the-debug-listing)) shows it set as `B` in its `Br` column.

| Class | Size | Vtable | Built by | Its own fields |
|---|---|---|---|---|
| `DigiSound` | `0x50` | `DigiSoundVtable` (`00095cd6`, `ES1GO 0008261c`) | `DigiSound_Ctor` (`000507c8`, `ES1GO 0004446c`) | `+0x3f` 2; `+0x43` and its switch `+0x47`, 0; `+0x48` and `+0x4c`, a loop range, -1 each |
| `MidiSound` | `0xcf` | `MidiSoundVtable` (`00095c82`, `ES1GO 000825bc`) | `MidiSound_Ctor` (`000504dc`, `ES1GO 0004412a`) | `+0x3f` 32 dwords, the track map, `0xff` each; `+0xbf` and `+0xc7`, the song block ([`song-player.md`](song-player.md#loading)) |

### The vtables

Each slot takes the object first. The base's are stubs a `NOP` opens; where `MidiSound`'s table points one byte past a base stub's start it runs the same stub. A scan of both images' disassembly for a load of `+0x3b` followed within five instructions by a call through it finds calls through slots `+0x04`–`+0x14` and `+0x28`–`+0x30`, and none through `+0x18`–`+0x24` ([Open](#open)).

| Slot | Does | Base, `SoundObjectVtable` | `MidiSound`, `MidiSoundVtable` | `DigiSound`, `DigiSoundVtable` |
|---|---|---|---|---|
| `+0x04` | (volume), a placed sound's volume | `SoundObject_SetPlacedVolume` (`000500a3`), nothing | `MidiSound_SetPlacedVolume` (`000507c4`), nothing | `DigiSound_SetPlacedVolume` (`00050954`): `+0x32` = volume × `0x7fff` × the manager's `+0x241` / 10,000, signed, passed with a live voice to `HmiDigi_SetSampleVolume` (`0006e0b3`)(driver, voice, volume) |
| `+0x08` | (flags), destroy | `SoundObject_Dtor` | `MidiSound_Dtor` (`0005055c`): its own vtable, `MidiSound_Stop`, `SoundObject_Dtor`(object, 0), freed on bit 0 | `DigiSound_Dtor` |
| `+0x0c` | the voice's status | `SoundObject_GetVoiceStatus` (`000500a7`), 0 | the base's, 0 | `DigiSound_GetVoiceStatus` (`00050c48`): 0 with no voice, else the word `+0x30` of the voice's record, the far pointer at `0009d10c` + driver × `0xc0` + voice × 6 |
| `+0x10` | play | `SoundObject_Play` (`000500ad`), 0 | `MidiSound_Play` | `DigiSound_Play` |
| `+0x14` | stop | `SoundObject_Stop` (`000500b5`), nothing | `MidiSound_Stop` (`000506f4`): with a song, `0006ead9`(handle) and `0006ea3d`(handle), then `+0x19` -1 and `+0x1d` `0xffff` | `DigiSound_Stop` |
| `+0x18` | [Open](#open) | `maybe_SoundObject_Resume` (`000500bb`), nothing | `maybe_MidiSound_Resume` (`000505b0`): `0006f362`(handle) | `maybe_DigiSound_Resume` (`00050832`), nothing |
| `+0x1c` | [Open](#open) | `maybe_SoundObject_Pause` (`000500bf`), nothing | `maybe_MidiSound_Pause` (`00050594`): `0006f24d`(handle, 1) | `maybe_DigiSound_Pause` (`000509a0`): with a voice, `HmiDigi_StopSample` (`0006dd7f`)(driver, voice), the call `DigiSound_Stop` makes, keeping the handle |
| `+0x20` | (value) | `maybe_SoundObject_SetPitch` (`000500c3`), nothing | the base's | `maybe_DigiSound_SetPitch` (`00050884`): `+0x43` = the value, at least `0x8000`, and above `0xf0000` its low word with `0xf0000`; sets `+0x47` |
| `+0x24` | (value) | `SoundObject_SetStartWord10` (`000500c7`), nothing | the base's | `DigiSound_SetStartWord10` (`00050870`): `+0x3f` = the value's low word, the start block's `+0x10` |
| `+0x28` | (start, end, count), the repeats | `SoundObject_SetRepeatCount` (`00050470`): count to `+0x23` while it plays, else to `+0x21` | the base's | `DigiSound_SetRepeats` (`00050cbc`): while it plays, count to `+0x23` and to the voice record's word `+0x38`; else count to `+0x21` and `+0x23`, end to `+0x48` and start to `+0x4c` |
| `+0x2c` | (volume) | `SoundObject_SetVolume` (`000500cb`), nothing | `MidiSound_SetVolume` (`0005075c`): below 0, `+0x31` = 0 and `0006edb3`(0); else `+0x31` = the volume held to 100, and `0006edb3`(`+0x31` × 127 × the manager's `+0x241` / 10,000), the song player's master volume ([`song-player.md`](song-player.md#volume)) | `DigiSound_SetVolume` (`000508c4`): below 0, `+0x31` = 0 and a live voice to `HmiDigi_SetSampleVolume` (`0006e0b3`)(driver, voice, 0); else `+0x31` = the volume held to 100, and a live voice to `0006e0b3` with `+0x31` × `0x7fff` × the manager's `+0x241` / 10,000 |
| `+0x30` | plays | `SoundObject_IsPlaying` (`000500cf`), 0 | `MidiSound_IsPlaying` (`00050730`): 0 with no song (`+0x19` -1), else 1 when `0006f225`(handle) answers 0 | `DigiSound_IsPlaying` (`00050c18`): 0 with no voice, else 1 when `HmiDigi_IsVoiceDone` (`0006df9d`)(driver, voice) answers 0, the voice still busy |

**The driver's calls back.** `MidiSound_Play` hands the song two far callbacks and `DigiSound_Play` the start block one; each finds its object among the 60 slots of `SoundManager_Instance` (`000bd73e`), the manager `SoundManager_Ctor` stores:

- **`MidiSound_LoopCallback` (`0004e718`)**, (*a*, handle, *c*, flags), registered through `0006ee87`: answers 1 when bit `0x80` of the flags is clear. Otherwise it finds the song (`+0x17` set) whose word `+0x1d` is the handle, answering 0 with none, and reads its repeat count, `+0x23` while it plays, else `+0x21`: -1 answers 1; 0 answers 0; any other count is lowered by one through slot `+0x28`(-1, -1, count − 1) and answers 1.
- **`MidiSound_EndCallback` (`0004e7b4`)**, (*a*, handle), the song block's `+0xc7`: stops the song whose word `+0x1d` is the handle through its slot `+0x14`.
- **`DigiSound_DoneCallback` (`0004e7fc`)**, (*a*, *b*, event, voice), the start block's `+0x1c`: with event 2, sets `+0x19` to -1 and `+0x1d` to `0xffff` on the first digital object in slot order whose `+0x19` is the voice; any other event does nothing.

## Naming and loading

`SoundManager_SetSampleNames` (`0004f888`, `ES1GO 00042e16`) takes a null-terminated array of name pointers and keeps it at `+0x508`; a name's index is its position. For each name it records the size the manager's volume stream gives (`VolRStream_Open`, `VolRStream_GetSize`) at `+0x262`, 0 for an empty name, and clears the name's data pointer and reference count, without freeing the data or changing the bytes loaded `+0x4f6`. It sets `+0x502` to the count less one, a byte, so an empty array (its first pointer null) gives `0xff`; but it returns at once, leaving `+0x502` as it was, when a name fails to open.

`SoundManager_SetSampleName` (`0004f99c`, `ES1GO 00042f39`) puts one name at an index. It returns 0 for no name, an index above `+0x502`, or a name holding none of `.sfx`, `.raw` and `.hmp`. It lower-cases the caller's string in place, as `SoundManager_CreateObject` does, and tests the other way round: `.sfx` or `.raw` makes the kind 0, and then `.hmp` makes it 1, so `.hmp` wins. Every object already on that index must be of the name's kind, or it returns 0; each is stopped through its slot `+0x14`, unloaded by `00050164`, marked to load and not loaded, and given the new name. The test and the change are one walk of the slots, so an object of the other kind ends it with 0 after the objects in the slots before it were changed. It then stores the name in the table, records its size and clears its data pointer, returning 1, or 0 when the file fails to open, the table and the objects changed either way. It does not write the reference count `+0x352`.

`SoundObject_LoadSample` (`000501ec`, `ES1GO 00043d8a`) returns 0 unless the object has a name to load, and 1 at once when it is loaded. When the bytes loaded plus the name's size would pass the limit, it returns 0 unless `+0x503` is set; otherwise it unloads one sound at a time until the sample fits, returning 0 when none qualifies. Each time it walks the 60 slots for a loaded object whose priority is not 0 and whose slot `+0x30` returns 0, keeping a candidate whose priority is above the one kept, or equal to it with a larger size; the kept priority starts at this object's and the size at 0, so the first candidate needs at least this object's priority, and of a full tie the first wins. The size it compares is the manager's `+0x262` at the candidate's slot, not at its name index; the two agree for the catalog's objects, made in row order into slots 0–46 ([the catalog](../simulation/sound-catalog.md#the-simulators-catalog)). It then allocates the name's size, reads the whole file, the object's own name `+0x00`, into it through the manager's stream, stores the data's pointer at `+0x38e`, counts a reference at `+0x352` (adding the size to the bytes loaded on the first, and raising the high-water mark), marks the object loaded and returns 1. A failed allocation, open or read returns 0, the allocation not freed.

## Playing

`DigiSound_Play` (`000509c4`, `ES1GO 000446ab`), the `DigiSound` vtable's `+0x10`:

1. When more than 30 of the driver's voices are busy (`0006e055`), it stops one digital sound to make room: of the digital objects whose slot `+0x30` returns non-zero, the first in slot order with the lowest `SoundObject_GetVolume` value below 100, through its slot `+0x14`. With none, it returns 0. A placed sound qualifies only at a placed volume of 0 (below).
2. It loads the sample, returning 0 when that fails.
3. The volume is the byte `+0x31` × `0x7fff` × the manager's `+0x241` / 10,000, or the word `+0x32` for a placed sound. At volume 0 with `+0x18` clear it returns 1 without playing.
4. It builds a start block and passes it to `HmiDigi_StartSample` (`0006dad9`, `ES1GO 0005e291`) with the driver handle `000bd8c8`, which is 0, keeping the voice it returns at `+0x19`; the slot goes to `+0x1d` before the call. When no voice is free it sets the dword `+0x04` and `+0x19` to -1 and `+0x1d` to `0xffff` and returns 0.

**`SoundObject_GetVolume` (`0005049c`)**, (object), its argument removed by this function: the byte `+0x31`, or for a placed sound (`+0x15`) the word `+0x32` × 10,000 / the manager's `+0x241`, times `0x7fff` (`IMUL` at `000504c4`), where the inverse of `DigiSound_SetPlacedVolume`'s scaling would divide by it. The product wraps at 32 bits and step 1 compares it unsigned; for no placed volume from 1 to 100 at any master volume from 1 to 100 does it come below 100, so step 1 stops a placed sound only at `+0x32` = 0. A master volume of 0 divides by zero (`IDIV` at `000504c2`). `Music_Tick` fades a song by it ([the music](../simulation/main-loop.md#the-music--music_tick-000585dc)).

| Block | Contents |
|---|---|
| `+0x00` | far pointer to the loaded file |
| `+0x08` | the length: the file's size |
| `+0x0c` | the object's `+0x21` |
| `+0x10` | the object's `+0x3f`, 2 |
| `+0x14` | the volume |
| `+0x18` | `0x1000` + the object slot |
| `+0x1c` | far callback `DigiSound_DoneCallback` (`0004e7fc`, [the driver's calls back](#the-vtables)) |
| `+0x28` | flags: `0x4100`, `0x4300` for a placed sound, and bit `0x40` set with a loop range |
| `+0x30`, `+0x34` | when `+0x21` is non-zero and neither of `+0x48` and `+0x4c` is -1, those two |
| `+0x38` | `+0x43`, when `+0x47` is set |
| `+0x40`, `+0x44` | for a placed sound, `+0x1f` and 0 |

The fields the function does not write hold what its stack held: `+0x40` and `+0x44` for a sound not placed, `+0x38` with `+0x47` clear, `+0x30` and `+0x34` with no loop range, and `+0x06`, `+0x22`–`+0x27`, `+0x2c` and `+0x3c` always.

`HmiDigi_StartSample` returns 0 when the dword `0009d0d8`[driver] is above `0xe105`, and -1 when all 32 of its voices are busy (bit `0x8000` of the voice's word `+0x30`). Otherwise it copies the block into the first free voice and returns the voice's number. The block carries no rate: the data plays at the driver's ([The driver](sound-drivers.md#the-driver)). The mixer reads the data as unsigned 8-bit samples and scales them by a gain linear in the volume ([the gain](digital-mixer.md#the-gain)). The objects' other calls into the digital driver read and write the voice records, the far pointers at `0009d10c` + driver × `0xc0` + voice × 6:

- **`HmiDigi_CountBusyVoices` (`0006e055`)**, (driver): how many of the 32 voices have bit `0x8000` of their word `+0x30` set, the busy ones.
- **`HmiDigi_IsVoiceDone` (`0006df9d`)**, (driver, voice): 10 for a voice above 31; 1 when bit `0x8000` of the voice's `+0x30` is clear, 0 while it is set. `DigiSound_IsPlaying` answers whether it is 0.
- **`HmiDigi_StopSample` (`0006dd7f`)**, (driver, voice): 10 for a voice above 31; for a voice with bit `0x8000` set and bit `0x1000` clear, clears bit `0x8000` and the word `+0x34`; it answers 0 otherwise too, so a busy voice with bit `0x1000` set is not stopped ([Open](#open)).
- **`HmiDigi_SetSampleVolume` (`0006e0b3`)**, (driver, voice, volume): with bit `0x8000` set, stores the volume in the word `+0x32` and answers the old one; else 0. It does not test the voice's range.

`DigiSound_Stop` (`00050c88`, `ES1GO 000449d8`), slot `+0x14`, passes a live voice to `HmiDigi_StopSample` (`0006dd7f`) and sets `+0x19` to -1 and `+0x1d` to `0xffff`. `DigiSound_Dtor` (`00050838`, `ES1GO 000444f2`), slot `+0x08`, stops the sound, calls `SoundObject_Dtor`(object, 0), and frees the object when bit 0 of its argument is set.

## Placed sounds

**A sound placed in the world fades with its distance from the camera, linearly to silence at the manager's hearing distance, and is panned by where it lies across the world's x axis.** A placed sound is one `SoundObject_SetPosition` (`00050120`) has been given a position; it sets `+0x15`, copies the point to `+0x25` and calls `SoundObject_UpdatePlacement` for the object. The sound file places a row of [the catalog](../simulation/sound-catalog.md#the-simulators-catalog) in one of two ways:

- **`Sound_PlayAt` (`0004deb8`)**, (point, row), its arguments removed by this function: with a manager, `SoundObject_SetPosition`(`SoundCatalog_Objects` (`000bd63c`)[row], point), then `Sound_Play`(row) ([playing a row](../simulation/sound-catalog.md#playing-a-row)). The object is placed even when its kind's switch is off, since `Sound_Play` tests the switch.
- **`Sound_OfferPosition` (`0004dee4`)**, (point, row), its arguments removed by this function: with a manager, *d* is `Math_FastMagnitude3D` of `Sound_Listener` (`000bd730`) less the point, and when the dword `SoundCatalog_OfferedDistance` (`000b1a84`)[row] is 0, *d* goes there and the point to `SoundCatalog_Positions` (`000b1b74`) + 12·row. Its test of a set dword compares two loads of the dword with each other (`0004df4d`), not with *d*, so the first point offered for a row in a frame is the one used; a point at the listener's own position stores 0 and leaves the row open to the next offer. `es2_xref.py` finds one call, from [a flyer's move](../simulation/flyers.md#the-move--flyer_movementtick-0001b084).

**`Sound_UpdateListener` (`0004df98`)**, (camera), its argument removed by this function, runs once a frame ([the frame's sounds](../simulation/main-loop.md#the-frames-sounds--sim_framesoundtick-00058574)). It stores the camera in `Sound_Listener`, with or without a manager. With one, it places each row below the catalog's count (`000bd72c`) whose `SoundCatalog_OfferedDistance` (`000b1a84`)[row] is not 0 at its offered point with `SoundObject_SetPosition` and zeroes the dword; then, on every tenth call (the countdown `Sound_ListenerCountdown` (`000959b4`), which `Sound_Init` sets to 10), it builds the rotation of (0, 0, 0) with `BuildEulerRotationMatrixQ14`, puts the camera's first three dwords, its position ([the camera](../simulation/external-views.md#the-camera)), after it as the transform's position, and passes that frame to `SoundManager_UpdateListener`. The offered rows are placed before that update in the same call, so they, like every sound placed between two updates, are faded and panned against a listener frame up to ten frames old.

**`SoundManager_UpdateListener` (`0004fb60`)**, (manager, frame): copies the frame's eight dwords to `+0x242`, then places each of the 60 objects with `+0x15` set as `SoundObject_UpdatePlacement` places one. **`SoundObject_UpdatePlacement` (`0004fdd8`)**, (manager, slot), places the slot's object against the frame at `+0x242`, when the object exists and has `+0x15` set:

1. The range *R* is `+0x23c`. When it is above `0xffff`, the shift *s* is the byte `+0x240` and *R* is shifted right by it; otherwise *s* is 0.
2. The offset is the object's position `+0x25` less the frame's, each axis shifted right by *s* (arithmetic), and *d* is `Math_FastMagnitude3D` of it, 1 when 0.
3. With *d* not below *R* (signed), the volume slot `+0x04` gets 0, and the step ends.
4. The volume slot `+0x04` gets `+0x31` − *d* × `+0x31` / *R*.
5. Each axis of the offset becomes axis × `0xfff` / *d*; (`0xfff`, 0, 0) is turned by the frame's rotation (`Transform_RotatePoint`), and *p* is the dot product of the two (`DotProductVec3Int`) / `0xfff`. The pan is `0xffff` − 8 × (*p* + `0xfff`), stored by `SoundObject_SetPan` (`00050150`) at `+0x1f`: `0x000f` for a sound straight along the turned axis, `0xffff` for one straight against it, about `0x8000` square to it.

With the defaults, *s* is 10 and *R* 97, so a placed sound is silent from about 99,000 units away. `Sound_UpdateListener`'s rotation of (0, 0, 0) leaves (`0xfff`, 0, 0) along the world's x axis, so the pan follows the sound's x offset from the camera and not the camera's heading: on the Sound Blaster 16's 8-bit stereo device (`e016`) a sound's side does not change as the HERC turns. There `0x000f` plays in the left channel alone and `0xffff` in the right alone ([the stereo pan](digital-mixer.md#the-stereo-pan)). On a mono device such as `e015` every sound plays in the center. The pan reaches the driver only in the start block `DigiSound_Play` builds; the volume reaches a voice already playing, through `DigiSound_SetPlacedVolume`.

**The manager's settings.** **`SoundManager_SetHearingRange` (`0004ffe0`)**, (manager, range): `+0x23c` = the range, held to `0x3fffc00`. **`SoundManager_SetDistanceShift` (`0004fb48`)**, (manager, shift): `+0x240`; `es2_xref` finds no caller in either image. **`SoundManager_SetMemoryLimit` (`00050008`)**, (manager, limit): `+0x4fe`. **`SoundManager_SetMasterVolume` (`0004fae8`)**, (manager, volume): `+0x241` = the volume held to 0–100, then each object's slot `+0x2c` with its own `+0x31`, so that live voices take the new scale; `es2_xref` finds no caller in `ES1DBSIM`, where the volume stays the constructor's 90. **`SoundManager_ClearSlot` (`0004f678`)**, (manager, slot): clears the object pointer; `es2_xref` finds no caller in either image.

## The debug listing

**`SoundManager_PrintMono` (`0004e858`)**, (manager, count, first): clears the monochrome monitor the first time (`00095a10`), prints the bytes loaded `+0x4f6`, the limit `+0x4fe`, the master volume `+0x241` and the digital and MIDI device ids (`00095a04`, `00095a0c`), and one line for each of *count* slots from *first*, lettered from `A`: its kind (`MIDI` or `DIGI`), handle `+0x19`, repeat count (a playing digital sound's from `0006e221`(driver, voice), else `+0x23` while it plays or `+0x21`), `B` when `+0x35` is set, the flags `+0x0d` and slot `+0x0c`'s status, the name's size, `+` when loaded, `PLY` when it plays, its volume (`+0x31`, or a placed sound's `+0x32` mapped to 0–100) and its name; an empty slot prints `empty slot`. `es2_xref` finds no caller.

## The copies

`GO.EXE`'s manager, sound objects and profile readers are the simulator's code: each `ES1GO` function in the table below holds the same statements as its `ES1DBSIM` original, compiled differently, with the shell's own globals.

| `ES1DBSIM` | `ES1GO` |
|---|---|
| `SoundManager_Ctor`, `SoundManager_Dtor`, `SoundManager_LoadDrivers`, `SoundManager_UnloadDrivers` | `00041f64`, `000420fd`, `0004240a`, `00042882` |
| `SoundManager_DetectDigitalCard`, `SoundDetect_Init`, `SoundDetect_FindDevice`, `SoundDetect_FindFirst`, `SoundDetect_Close` | `000421a7`, `00042281`, `000422c9`, `0004235a`, `000423dc` |
| `SoundManager_CountObjects`, `SoundManager_SetMasterVolume`, `SoundManager_SetHearingRange`, `SoundManager_SetMemoryLimit` | `00042bc3`, `000430cd`, `00043ade`, `00043b0f` |
| `SoundObject_Ctor`, `SoundObject_Dtor`, `SoundObject_Unload`, `SoundObject_SetPan`, `maybe_SoundObject_SetBranchCallback`, `SoundObject_SetRepeatCount` | `00043b28`, `00043c34`, `00043cc5`, `00043cae`, `00044087`, `000440b7` |
| the base's stubs, `SoundObject_SetPlacedVolume` to `SoundObject_IsPlaying` | `00043bbd`–`00043c28` |
| `MidiSound_Dtor`, `maybe_MidiSound_Pause`, `maybe_MidiSound_Resume`, `MidiSound_Stop`, `MidiSound_SetPlacedVolume` | `000441c1`, `00044203`, `00044220`, `0004437c`, `0004445f` |
| `maybe_DigiSound_Resume`, `DigiSound_SetStartWord10`, `maybe_DigiSound_SetPitch`, `DigiSound_SetVolume`, `DigiSound_SetPlacedVolume`, `maybe_DigiSound_Pause`, `DigiSound_GetVoiceStatus`, `DigiSound_SetRepeats` | `000444e8`, `00044534`, `0004454c`, `0004459a`, `00044635`, `00044684`, `0004499a`, `00044a14` |
| `0004e710` | `SoundManager_TimerCallback` (`000419d8`), a far call through `000a09c8` |
| `SoundManager_Instance`, `SoundManager_DriversLoaded`, `SoundDriver_Defaults` | `000ab356`, `000ab4e4`, `00082310` |
| `SoundObjectVtable`, `MidiSoundVtable`, `DigiSoundVtable` | `00082576`, `000825bc`, `0008261c` |

The functions the sections above give an `ES1GO` address beside their own are copies too. `ES1GO 00043a90`, among the copies where `SoundObject_UpdatePlacement`'s and `SoundManager_SetHearingRange`'s meet, has no `ES1DBSIM` copy: (manager and three more, its arguments removed by this function) it walks the 60 slots testing each object's `+0x15` and does nothing else; `es2_xref` finds no caller. `ES1GO 0004315a` and `0004360e` lie where `SoundManager_UpdateListener`'s and `SoundObject_UpdatePlacement`'s copies would ([Open](#open)).

## Open

- **Open:** what the start block's `+0x0c`, the repeat count, and `+0x38`, the `+0x43` slot `+0x20` sets, mean to the mixer; the repeat count's play is [the mixer's](digital-mixer.md#open) item, as is flag `0x4000`. And what `MidiSound_LoopCallback`'s answer and its flags' bit `0x80` mean to the song player.
- **Open:** the vtables' slots `+0x18`–`+0x24`, through which the scan of both images finds no call ([the vtables](#the-vtables)): whether `+0x18` and `+0x1c` resume and pause, as the `maybe_` names read them, and `+0x20` sets a pitch.
- **Open:** what sets bit `0x1000` of a voice's word `+0x30`, with which `HmiDigi_StopSample` (`0006dd7f`) leaves a busy voice playing and the mixer leaves it out of the mix ([the mix](digital-mixer.md#the-mix)). A lead: the start block's `+0x18` is `0x1000` + the object slot.
- **Open:** `ES1GO 0004315a` and `0004360e`, which `es2_xref` finds calling `ES1GO`'s `SoundObject_SetPan` and which stand where `SoundManager_UpdateListener`'s and `SoundObject_UpdatePlacement`'s copies would; their bodies are unread, and no caller of either was looked for.
- **Open:** where the object files from `0004e710` to the HMI timer's (`00050d38`) divide, and the source names of these files: a scan of the sound file's code for a pointer to one of the image's 41 `.cpp` paths finds none, where the same scan of the Heads-Down Display's file finds its two `phdd.cpp` asserts.
