# DBSIM.EXE and GO.EXE — digital sounds

**ES1's sound effects, voice lines and spoken lines are bare sample data: each file is the sound and nothing else.** ES2's are RIFF WAVs ([ES2's sample format](../../retail/formats/sound-samples.md#sample-format)); ES1's `.SFX` and `.RAW` files carry no header, and both programs load a file whole and hand all of it to the sound driver as one sample. The two programs' sound managers are the same code: addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)), and each `ES1GO` copy is given beside its `ES1DBSIM` original. The music, `.HMP`, goes through the same manager and is [`hmp-music.md`](hmp-music.md).

## Where the object files start and end

The simulator's sound file, `0004db14`–`0004e70f`, follows `appinput.cpp`'s exit routine `AppInput_ExitNoOp` (`0004db10`) and three zero bytes, and opens with `Sound_Init` and no `ConfigPart` run. It ends as the files placed from `0001c800` do ([where object files meet](compiler.md#where-object-files-meet-in-the-code)), with its static initializer, its exit routine and the initializer's element constructor, whose `RET 4` at `0004e70d` ends on a 4-byte boundary:

| Function | Address | Table entry | Does |
|---|---|---|---|
| `Sound_StaticInit` | `0004e6b0` | `000adc64` | `__vec_ctor` over `SoundCatalog_Positions` (`000b1b74`), 60 elements of 12 bytes, the position a placed catalog sound is given ([the catalog](#the-simulators-catalog)), with `maybe_Point3I_CtorSoundCopy` by its padding `0004e6dd`; then `StringTable_Ctor`(`SoundCatalog_StringTable`, `000b1e44`) |
| `Sound_StaticDtor` | `0004e6d0` | `000adcc8` | `OwnedArray_Dtor`(`SoundCatalog_StringTable`, 2), which frees the table's buffer, leaving the positions |
| `maybe_Point3I_CtorSoundCopy` | `0004e6e0` | | the code of `maybe_Point3I_Ctor` (`000633fc`) |

The first two are frameless, with no Ghidra function. ES2's `Sound_StaticInit` (`0046349c`) builds the string table alone, and its `Sound_StaticDtor` (`004634cc`) is ES1's. The lip-sync player is the end of the file, `0004e39c`–`0004e6ae` ([`snc-lip-sync.md`](snc-lip-sync.md#playing-a-line)). `0004e710`, the far call through `000b1ed0` that `SoundManager_LoadDrivers` registers with the timer, follows; the files from there to the [HMI timer's](#the-digital-drivers-timer), and the sound file's source name, are [Open](#open).

## Which files

| Archive | Folder | Files | What names them |
|---|---|---|---|
| `SIMSOUND` | `hmi\` | 52 `.SFX` | the simulator's catalog, `str\SOUNDS.STR`, as `hmi\<name>.sfx` ([The simulator's catalog](#the-simulators-catalog)) |
| `SIMVOICE` | `simvoice\` | 147 `.SFX`, `P1_01000.SFX` … | `SpeechLine_Load`, as `simvoice\<name>.sfx` ([Voice lines](#voice-lines)) |
| `SHELL0` | `res32\` | 3 `.SFX`, 14 `.RAW` | the shell's name table, which holds `BUTTON.SFX`, `XXXXXXXX.SFX`, `MENUCLSA.RAW`, `MENUOPN2.RAW` and `XXXXXXXX.RAW` ([The shell's names](#the-shells-names)) |
| `SPEECH` | `snc\` | 161 `.RAW` | the talking heads' lip-sync lists, each line played with its `.SNC` script ([loading a line](../shell/message-box.md#loading-a-line)) |

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
| `+0x23c` | the distance a placed sound is heard to, 100,000 from the constructor and `Sound_Init` ([placed sounds](#placed-sounds)) |
| `+0x240` | byte, the shift placed sounds' distances are taken with, 10 from the constructor |
| `+0x241` | byte, the master volume, 0–100, which digital sounds are scaled by; 90 from the constructor |
| `+0x242` | the listener's frame, eight dwords: a rotation in the first five, the position in the last three (`+0x256`, `+0x25a`, `+0x25e`) |
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
| `+0x15` | byte, set for a sound placed in the world ([placed sounds](#placed-sounds)) |
| `+0x16` | byte, 0 from the constructor; `Snc_Load` clears it ([`snc-lip-sync.md`](snc-lip-sync.md#playing-a-line)) |
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
| `+0x35`, `+0x39` | a far pointer, 0 from the constructor, that `MidiSound_Play` passes to `0006eeae` ([`hmp-music.md`](hmp-music.md#loading-and-playing)) |
| `+0x3b` | the vtable |

**`SoundObject_Ctor` (`00050020`, `ES1GO 00043b28`)**, (memory or 0): allocates `0x3f` bytes when given none, returning 0 when that fails, stores the base vtable `SoundObjectVtable` (`00095c46`) and the defaults above, and returns the object. Both class constructors run it first and set the flags `+0x0d` to 2 when the object has a name, else 0. **`SoundObject_Dtor` (`000500d8`)**, (object, flags): stores the base vtable again, unloads the sample with `SoundObject_Unload`, clears the manager's object slot (`+0x14c`[`+0x08`]) and frees the object when bit 0 of the flags is set. **`SoundObject_Unload` (`00050164`)**: stops the sound through slot `+0x14` when slot `+0x30` says it plays; when it is loaded, takes one off the name's reference count `+0x352`, and at 0 frees the name's data `+0x38e`, clears that pointer and takes the name's size `+0x262` off the bytes loaded `+0x4f6`; then clears the loaded bit. **`maybe_SoundObject_SetBranchCallback` (`00050450`)**, (object, offset, selector): for a song (`+0x17` set) stores the far pointer at `+0x35`; `es2_xref` finds no caller in either image, and the manager's debug listing ([the debug listing](#the-debug-listing)) shows it set as `B` in its `Br` column.

| Class | Size | Vtable | Built by | Its own fields |
|---|---|---|---|---|
| `DigiSound` | `0x50` | `DigiSoundVtable` (`00095cd6`, `ES1GO 0008261c`) | `DigiSound_Ctor` (`000507c8`, `ES1GO 0004446c`) | `+0x3f` 2; `+0x43` and its switch `+0x47`, 0; `+0x48` and `+0x4c`, a loop range, -1 each |
| `MidiSound` | `0xcf` | `MidiSoundVtable` (`00095c82`, `ES1GO 000825bc`) | `MidiSound_Ctor` (`000504dc`, `ES1GO 0004412a`) | `+0x3f` 32 dwords, the track map, `0xff` each; `+0xbf` and `+0xc7`, the song block ([`hmp-music.md`](hmp-music.md#loading-and-playing)) |

#### The vtables

Each slot takes the object first. The base's are stubs a `NOP` opens; where `MidiSound`'s table points one byte past a base stub's start it runs the same stub. A scan of both images' disassembly for a load of `+0x3b` followed within five instructions by a call through it finds calls through slots `+0x04`–`+0x14` and `+0x28`–`+0x30`, and none through `+0x18`–`+0x24` ([Open](#open)).

| Slot | Does | Base, `SoundObjectVtable` | `MidiSound`, `MidiSoundVtable` | `DigiSound`, `DigiSoundVtable` |
|---|---|---|---|---|
| `+0x04` | (volume), a placed sound's volume | `SoundObject_SetPlacedVolume` (`000500a3`), nothing | `MidiSound_SetPlacedVolume` (`000507c4`), nothing | `DigiSound_SetPlacedVolume` (`00050954`): `+0x32` = volume × `0x7fff` × the manager's `+0x241` / 10,000, signed, passed with a live voice to `0006e0b3`(driver, voice, volume) |
| `+0x08` | (flags), destroy | `SoundObject_Dtor` | `MidiSound_Dtor` (`0005055c`): its own vtable, `MidiSound_Stop`, `SoundObject_Dtor`(object, 0), freed on bit 0 | `DigiSound_Dtor` |
| `+0x0c` | the voice's status | `SoundObject_GetVoiceStatus` (`000500a7`), 0 | the base's, 0 | `DigiSound_GetVoiceStatus` (`00050c48`): 0 with no voice, else the word `+0x30` of the voice's record, the far pointer at `0009d10c` + driver × `0xc0` + voice × 6 |
| `+0x10` | play | `SoundObject_Play` (`000500ad`), 0 | `MidiSound_Play` | `DigiSound_Play` |
| `+0x14` | stop | `SoundObject_Stop` (`000500b5`), nothing | `MidiSound_Stop` (`000506f4`): with a song, `0006ead9`(handle) and `0006ea3d`(handle), then `+0x19` -1 and `+0x1d` `0xffff` | `DigiSound_Stop` |
| `+0x18` | [Open](#open) | `maybe_SoundObject_Resume` (`000500bb`), nothing | `maybe_MidiSound_Resume` (`000505b0`): `0006f362`(handle) | `maybe_DigiSound_Resume` (`00050832`), nothing |
| `+0x1c` | [Open](#open) | `maybe_SoundObject_Pause` (`000500bf`), nothing | `maybe_MidiSound_Pause` (`00050594`): `0006f24d`(handle, 1) | `maybe_DigiSound_Pause` (`000509a0`): with a voice, `0006dd7f`(driver, voice), the call `DigiSound_Stop` makes, keeping the handle |
| `+0x20` | (value) | `maybe_SoundObject_SetPitch` (`000500c3`), nothing | the base's | `maybe_DigiSound_SetPitch` (`00050884`): `+0x43` = the value, at least `0x8000`, and above `0xf0000` its low word with `0xf0000`; sets `+0x47` |
| `+0x24` | (value) | `SoundObject_SetStartWord10` (`000500c7`), nothing | the base's | `DigiSound_SetStartWord10` (`00050870`): `+0x3f` = the value's low word, the start block's `+0x10` |
| `+0x28` | (start, end, count), the repeats | `SoundObject_SetRepeatCount` (`00050470`): count to `+0x23` while it plays, else to `+0x21` | the base's | `DigiSound_SetRepeats` (`00050cbc`): while it plays, count to `+0x23` and to the voice record's word `+0x38`; else count to `+0x21` and `+0x23`, end to `+0x48` and start to `+0x4c` |
| `+0x2c` | (volume) | `SoundObject_SetVolume` (`000500cb`), nothing | `MidiSound_SetVolume` | `DigiSound_SetVolume` (`000508c4`): below 0, `+0x31` = 0 and a live voice to `0006e0b3`(driver, voice, 0); else `+0x31` = the volume held to 100, and a live voice to `0006e0b3` with `+0x31` × `0x7fff` × the manager's `+0x241` / 10,000 |
| `+0x30` | plays | `SoundObject_IsPlaying` (`000500cf`), 0 | `MidiSound_IsPlaying` | `DigiSound_IsPlaying` |

**The driver's calls back.** `MidiSound_Play` hands the song two far callbacks and `DigiSound_Play` the start block one; each finds its object among the 60 slots of `SoundManager_Instance` (`000bd73e`), the manager `SoundManager_Ctor` stores:

- **`MidiSound_LoopCallback` (`0004e718`)**, (*a*, handle, *c*, flags), registered through `0006ee87`: answers 1 when bit `0x80` of the flags is clear. Otherwise it finds the song (`+0x17` set) whose word `+0x1d` is the handle, answering 0 with none, and reads its repeat count, `+0x23` while it plays, else `+0x21`: -1 answers 1; 0 answers 0; any other count is lowered by one through slot `+0x28`(-1, -1, count − 1) and answers 1.
- **`MidiSound_EndCallback` (`0004e7b4`)**, (*a*, handle), the song block's `+0xc7`: stops the song whose word `+0x1d` is the handle through its slot `+0x14`.
- **`DigiSound_DoneCallback` (`0004e7fc`)**, (*a*, *b*, event, voice), the start block's `+0x1c`: with event 2, sets `+0x19` to -1 and `+0x1d` to `0xffff` on the digital object whose `+0x19` is the voice.

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
| `+0x1c` | far callback `DigiSound_DoneCallback` (`0004e7fc`, [the driver's calls back](#the-vtables)) |
| `+0x28` | flags: `0x4100`, `0x4300` for a placed sound, and bit `0x40` set with a loop range |
| `+0x30`, `+0x34` | when `+0x21` is non-zero and neither of `+0x48` and `+0x4c` is -1, those two |
| `+0x38` | `+0x43`, when `+0x47` is set |
| `+0x40` | for a placed sound, `+0x1f` |

`HmiDigi_StartSample` returns 0 when the dword `0009d0d8`[driver] is above `0xe105`, and -1 when all 32 of its voices are busy (bit `0x8000` of the voice's word `+0x30`). Otherwise it copies the block into the first free voice and returns the voice's number. The block carries no rate: the data plays at the driver's ([The driver](#the-driver)).

`DigiSound_Stop` (`00050c88`, `ES1GO 000449d8`), slot `+0x14`, passes a live voice to `0006dd7f` and sets `+0x19` to -1 and `+0x1d` to `0xffff`. `DigiSound_Dtor` (`00050838`, `ES1GO 000444f2`), slot `+0x08`, stops the sound, calls `SoundObject_Dtor`(object, 0), and frees the object when bit 0 of its argument is set.

### Placed sounds

**A sound placed in the world fades with its distance from the camera, linearly to silence at the manager's hearing distance, and is panned by where it lies across the world's x axis.** A placed sound is one `SoundObject_SetPosition` (`00050120`) has been given a position ([the simulator's catalog](#the-simulators-catalog)); it sets `+0x15` and calls `SoundObject_UpdatePlacement` for the object. `Sound_UpdateListener` (`0004df98`) passes the camera's frame to `SoundManager_UpdateListener` on every tenth frame, a rotation of (0, 0, 0) with the camera's position.

**`SoundManager_UpdateListener` (`0004fb60`)**, (manager, frame): copies the frame's eight dwords to `+0x242`, then places each of the 60 objects with `+0x15` set as `SoundObject_UpdatePlacement` places one. **`SoundObject_UpdatePlacement` (`0004fdd8`)**, (manager, slot), places the slot's object against the frame at `+0x242`, when the object exists and has `+0x15` set:

1. The range *R* is `+0x23c`. When it is above `0xffff`, the shift *s* is the byte `+0x240` and *R* is shifted right by it; otherwise *s* is 0.
2. The offset is the object's position `+0x25` less the frame's, each axis shifted right by *s* (arithmetic), and *d* is `Math_FastMagnitude3D` of it, 1 when 0.
3. With *d* not below *R* (signed), the volume slot `+0x04` gets 0, and the step ends.
4. The volume slot `+0x04` gets `+0x31` − *d* × `+0x31` / *R*.
5. Each axis of the offset becomes axis × `0xfff` / *d*; (`0xfff`, 0, 0) is turned by the frame's rotation (`Transform_RotatePoint`), and *p* is the dot product of the two (`DotProductVec3Int`) / `0xfff`. The pan is `0xffff` − 8 × (*p* + `0xfff`), stored by `SoundObject_SetPan` (`00050150`) at `+0x1f`: `0x000f` for a sound straight along the turned axis, `0xffff` for one straight against it, about `0x8000` square to it.

With the defaults, *s* is 10 and *R* 97, so a placed sound is silent from about 99,000 units away. `Sound_UpdateListener`'s rotation of (0, 0, 0) leaves (`0xfff`, 0, 0) along the world's x axis, so the pan follows the sound's x offset from the camera and not the camera's heading: on the Sound Blaster 16's 8-bit stereo device (`e016`) a sound's side does not change as the HERC turns. On a mono device such as `e015` every sound plays in the center. The pan reaches the driver only in the start block `DigiSound_Play` builds; the volume reaches a voice already playing, through `DigiSound_SetPlacedVolume`.

**The manager's settings.** **`SoundManager_SetHearingRange` (`0004ffe0`)**, (manager, range): `+0x23c` = the range, held to `0x3fffc00`. **`SoundManager_SetDistanceShift` (`0004fb48`)**, (manager, shift): `+0x240`; `es2_xref` finds no caller in either image. **`SoundManager_SetMemoryLimit` (`00050008`)**, (manager, limit): `+0x4fe`. **`SoundManager_SetMasterVolume` (`0004fae8`)**, (manager, volume): `+0x241` = the volume held to 0–100, then each object's slot `+0x2c` with its own `+0x31`, so that live voices take the new scale; `es2_xref` finds no caller in `ES1DBSIM`, where the volume stays the constructor's 90. **`SoundManager_ClearSlot` (`0004f678`)**, (manager, slot): clears the object pointer; `es2_xref` finds no caller in either image.

### Turning songs and sounds off

**Preferences' MUSIC and SOUNDS switch the songs and the digital sounds, and switching one off stops what of it plays.** `Sound_Play` plays an object only while its kind's switch is on (`SoundObject_IsCategoryEnabled`): `Sound_MusicEnabled` (`000959b8`) for a song, `Sound_EffectsEnabled` (`000959bc`) for a digital sound, both 0 in the image. `es2_xref --decode` finds each dword's absolute references there and in its setter, which is the one that writes it.

- **`Sound_SetMusicEnabled` (`0004e294`)**, (on): when *on* is 0 and the switch was on, with a manager, stops the songs with `Sound_StopSongs`; then stores *on*. `es2_xref` finds calls from `Prefs_ApplyMusicOption` (`0004ad0c`, preferences option 0's handler, [the option array](../simulation/preferences.md#the-option-array)) and from `Sim_StartMission`'s end, with 1 ([the mission start](../simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc)).
- **`Sound_SetEffectsEnabled` (`0004e2c8`)**, (on): the same over `Sound_EffectsEnabled` with `Sound_StopDigitalSounds` (`0004e34c`), called through the two `NOP`s before it at `0004e34a`. `es2_xref` finds one call, from `Prefs_ApplySoundsOption` (`0004acfc`, option 1's).
- **`Sound_StopSongs` (`0004e2f9`)**: `Sound_StopDigitalSounds` for songs: with a manager, each object in the slots below the catalog's row count (`000bd72c`) whose `+0x17` is set and whose slot `+0x30` says it plays is stopped through its slot `+0x14`.

Turning a switch on starts nothing; option 0's handler then runs `Music_Tick` ([the frame's sounds](../simulation/main-loop.md#the-frames-sounds--sim_framesoundtick-00058574)).

### The debug listing

**`SoundManager_PrintMono` (`0004e858`)**, (manager, count, first): clears the monochrome monitor the first time (`00095a10`), prints the bytes loaded `+0x4f6`, the limit `+0x4fe`, the master volume `+0x241` and the digital and MIDI device ids (`00095a04`, `00095a0c`), and one line for each of *count* slots from *first*, lettered from `A`: its kind (`MIDI` or `DIGI`), handle `+0x19`, repeat count (a playing digital sound's from `0006e221`(driver, voice), else `+0x23` while it plays or `+0x21`), `B` when `+0x35` is set, the flags `+0x0d` and slot `+0x0c`'s status, the name's size, `+` when loaded, `PLY` when it plays, its volume (`+0x31`, or a placed sound's `+0x32` mapped to 0–100) and its name; an empty slot prints `empty slot`. `es2_xref` finds no caller.

## The driver

**The sound settings come from the install's configuration, except the rate, which the image fixes at 11,025 Hz.** `Sound_Init` (`0004db14`; `es2_xref` finds its one call, from `Sim_StartMission`) takes six values in stack order — MIDI port, port, DMA channel, IRQ, MIDI device and digital device. It first sets the number of voice-line slots `000bd73c`, 10, or 1 in the low-memory mode, and makes the slots with `__vec_new` and `VoiceSlot_ElementCtor` and their 100-byte script buffers ([the speech slot](snc-lip-sync.md#the-speech-slot)); then, with the digital device 0, it makes no manager and goes to its last store, so the slots and buffers exist with no sound manager. A slot's sound object `+0x36`, which the catalog's load writes ([the simulator's catalog](#the-simulators-catalog)), is not written by the slot's constructors, and with no manager it is what the block held there when `SpeechLine_Load` passes it on to `Snc_Load` ([voice lines](#voice-lines), [Open](#open)). It stores them over the defaults block at `000959ec`, whose image values are a buffer of `0x1000`, the rate 11,025, `0x78`, DMA 1, IRQ 7, port `0x220`, digital device `0xe000`, MIDI port `0x388` and MIDI device `0xa002`. The buffer and the rate are not among the values it stores, and `es2_xref` finds one reference to the rate's dword `000959f0`, the read in `SoundManager_LoadDrivers`. The shell's block, `ES1GO 00082310`, holds the same values; `ShellSound_Init` fills its six from `resource.cfg`'s `[SoundFX]` `Driver#`, `IRQ`, `DMA` and `PortAddress` and `[Music]` `Driver#` and `PortAddress`, the driver numbers and ports read as hex, and asserts `ERROR: Run soundset first.` when the file does not open. With no `[SoundFX]` `Driver#` it makes no manager.

`Sound_Init` then makes the manager, runs `SoundManager_LoadDrivers` (`0004ef8c`), sets the memory limit through `00050008` (`0xffffffff`, or 200,000 with `+0x503` set in the low-memory mode), calls `0004ffe0`(manager, 100,000), zeroes `000bd72c` and loads the catalog; it sets the slot count again there, to the same value. Its last store, with or without a manager, is 10 into `000959b4`.

`SoundManager_LoadDrivers` (`0004ef8c`):

1. reads the three bank files whole with `SoundManager_LoadBanks` (`0004f6d4`), and fails when one does not load;
2. registers `0004e710` with `Timer_AddCallback` and keeps the slot at `+0x134`;
3. opens the MIDI device with `HmiMidi_InitDriver` (`00071076`), handle `000bd8c0`, and loads the melodic and drum banks into it through `00072275`;
4. fills the digital driver, hardware and init blocks above from the defaults block, sets `000bd8c8` to 0, and opens MIDI device `0xa005` with `HmiMidi_InitDriver` on the block at `+0xa0`, handle `000bd8c4`, loading `sos\drum32.dig` into it;
5. calls `0007225a`(0), sets `SoundManager_DriversLoaded` (`000bd8cc`) and returns 1.

A failure formats `SoundManager::loadDrivers: %s` with `0006f23d`'s text for the error and, from the bank loads on, passes one or both MIDI handles to `0007144b`; it then calls `0006fe28`, `00071064` and `0006d5c2`(0) and returns 0. `HmiMidi_InitDriver` hands device `0xa005`'s driver the block's `+0x18` far pointer, the manager's address, and through it the digital driver, hardware and init blocks; how that driver opens the digital driver from them is [Open](#open).

**`SoundManager_UnloadDrivers` (`0004f3d4`)**, (manager), the reverse, while `SoundManager_DriversLoaded` is set: stops every object through its slot `+0x14`, passes both MIDI handles (`000bd8c0`, `000bd8c4`) to `00071515` and then each with 1 to `0007144b`, removes the timer callback `+0x134` (its test against -1 compares the word zero-extended, so it always passes), calls `00071064`, `000705c5`(`000bd8c8`, 1, 1) and `0006fe28`, and clears it. `es2_xref` finds calls from `Sound_Shutdown` and `SoundManager_Dtor`. **`SoundManager_Dtor` (`0004eccc`)**, (manager, flags): formats `SoundManager::~SoundManager: max memory used = %d (limit %d)` with `+0x4fa` and `+0x4fe` into a stack buffer, unloads the drivers, destroys each object through its slot `+0x08` with 3, closes the volume stream `+0x504` and frees the manager on bit 0. `es2_xref` finds no caller in `ES1DBSIM`, whose `Sound_Shutdown` destroys the catalog's objects and unloads the drivers itself; the shell's copy (`ES1GO 000420fd`) is called by `Shell_AssertCleanupHook` and `Shell_ShutdownDevices`.

**Finding the card.** **`SoundManager_DetectDigitalCard` (`0004ed68`)**, (manager, device), answers 1 when the HMI library's detection finds a digital card and copies what it found over the defaults block; `es2_xref` finds no caller in either image (`ES1GO 000421a7`). It works on `SoundDetect_Buffer` (`000bd742`):

1. `SoundDetect_Init` (`0004ee30`)(manager, `.\`, buffer) copies the path into the buffer and clears its bytes `+0x100` and `+0x101`.
2. With *device* 0, `SoundDetect_FindFirst` (`0004eef4`)(manager, buffer): `0007235d`(buffer), then `00072901`(buffer + `0x102`, buffer + `0x16e`), then `00072ce9`(buffer + `0x16e`); when all three return 0 it sets `+0x100` and `+0x101` and answers 1, else calls `00072517` and answers 0. `SoundDetect_Close` (`0004ef68`) then calls `00072517` when `+0x100` is set and clears it.
3. Otherwise `SoundDetect_FindDevice` (`0004ee78`)(manager, device, buffer): `0007235d`(buffer), then `00072573`(device, buffer + `0x102`, buffer + `0x16e`), then `00072ce9`(buffer + `0x16e`); it calls `00072517` either way, clears `+0x100`, and sets `+0x101` when all three returned 0, answering `+0x101`.
4. On success it copies the buffer's `+0x176` to the DMA channel `000959f8`, `+0x172` to the IRQ `000959fc`, `+0x16e` to the port `00095a00` and `+0x166` to the digital device `00095a04`.

ES2's loader plays a file that is not RIFF as raw 8-bit unsigned mono at 11,025 Hz ([ES2's sample format](../../retail/formats/sound-samples.md#sample-format)).

`[SoundFX]` `Driver#` holds one of the digital devices the setup program `SOUNDSET.EXE` lists in its `SOUNDSET.INI`, up to four a card in the columns 8-bit mono, 8-bit stereo, 16-bit mono and 16-bit stereo: Sound Blaster `e000` and `e001`, Sound Blaster 16 `e015`–`e018`, and so on for 18 cards; the image's default `0xe000` is the Sound Blaster's 8-bit mono. The drivers are the 95 `.com` images in `SOS\HMIDRV.386`, a name both images hold; the code that loads one is [Open](#open). The MIDI devices are [`hmp-music.md`](hmp-music.md#the-devices)'s.

### The digital driver's timer

**The HMI code runs its timed work from the game's timer: it hands the game a far callback, which the game registers at a count of a sixtieth of the timer's rate.** What a count means to the timer is [Open](../simulation/debug-monitor.md#open). The functions at `00050d38`–`00050e1f`, after the sound files and before [the profile file](profile-files.md), are what the HMI code calls for it:

| Function | Address | Does |
|---|---|---|
| `HmiTimer_Install` | `00050d98` | (*a*, offset, selector), C convention: keeps the far pointer selector:offset in `HmiTimer_Callback` (`000b1ee0`) and registers `HmiTimer_Tick` with `Timer_AddCallback` (`0006d289`) at a count of ⌊⌊1000000 / `Math_Q14Multiply`(*w*, `0x35a1`)⌋ / 60⌋, *w* the PC timer's divisor, so a count of 2: every second interrupt, 60 times a second ([the rate](../simulation/timer.md#the-rate)), keeping the slot in `HmiTimer_Slot` (`000b1edc`) |
| `HmiTimer_Remove` | `00050e00` | passes the slot to `Timer_RemoveCallback` (`0006d337`, [callbacks](../simulation/timer.md#callbacks)), which `Ttm_ManagerDtor` gives its own slot |
| `HmiTimer_Enable`, `HmiTimer_Disable` | `00050d80`, `00050d8c` | set `HmiTimer_Enabled` (`000b1ed8`) to 1 and 0 |
| `HmiTimer_Tick` | `00050d38` | while enabled and not already inside the callback (the count `000b1ee8`), calls the callback through the far pointer |
| `HmiTimer_NoOp`, `HmiHook_NoOp` | `00050e10`, `00050e14` | return at once |
| `HmiLock_RegionStart`, `HmiLock_RegionEnd` | `00050e18`, `00050e1c` | a `RET` each: the HMI code passes the four bytes between them as a region to lock and unlock (`X32_UnlockRegion`, `00060b91`) |

`es2_xref.py` finds the calls of all but the tick and the markers in the HMI code at `0006d4f5`–`0006da46`, and none to `HmiTimer_TickUnguarded` (`00050d60`) or its copy `HmiTimer_TickUnguarded2` (`00050d70`), which call the callback while enabled with no count. The file ends with the second marker and three zero bytes.

## The banks

`SHELL0.VOL`'s `sos\` holds five files in the HMI library's own formats, which the programs hand to its drivers without reading a field: `MELODIC.BNK`, `DRUM.BNK` and `DONS.BNK`, 5,404 bytes each with `ADLIB-` from byte 2, and `DRUM32.DIG` and `DRUM.DIG`, which open with `HMIDIGIP`. `DONS.BNK` is `MELODIC.BNK` up to byte 67.

`SoundManager_LoadBanks` (`0004f6d4`, `ES1GO 00042bf9`) reads `sos\melodic.bnk`, `sos\drum.bnk` and `sos\drum32.dig`, in that order, through the manager's volume stream `+0x504`. For each it opens the file with `VolRStream_Open`, allocates the size `VolRStream_GetSize` gives, reads that many bytes with the stream's read slot `+0x1c`, closes the file and keeps a far pointer to the data at `+0x13a`, `+0x146` and `+0x140`. It returns 0 at the first that fails to open, allocate or read, and 1 after the third. [The driver](#the-driver) says where each goes. A search of both images finds no name for `DONS.BNK` or `DRUM.DIG` ([Open](#open)).

## The simulator's catalog

`SoundCatalog_Load` (`0004dc70`), the last step of `Sound_Init`, reads `str\SOUNDS.STR` row by row with `StringTable_ReadNext` into a name array (`000b18a4`) and an attribute pointer array (`000b1994`), and passes the names to `SoundManager_SetSampleNames`, so a row's number is its name index. For each row it makes `000bd63c`[row] with `SoundManager_CreateObject`(manager, 2, attribute byte 3, row), calls the object's slot `+0x28`(-1, -1, attribute byte 0) and `+0x2c`(attribute byte 1), and loads it at once when attribute byte 2 is set; the row count goes to `000bd72c`. `Sound_Play` and its siblings take a row number.

`SOUNDS.STR` is a `.STR` table ([the layout](../../retail/formats/str-strings.md#layout)) of one group, 47 named rows with four attribute bytes each and ten empty rows with none; the loader stops at the first empty name.

| Byte | Meaning |
|---|---|
| 0 | the repeat count, zero-extended into the slot `+0x28` count ([the vtables](#the-vtables)); a song's is counted down by `MidiSound_LoopCallback`, a digital sound's is the start block's `+0x0c`. `0xff` is 255, not the callback's endless -1 |
| 1 | the volume, 0–100, through slot `+0x2c` |
| 2 | load the sample at the catalog's load rather than at its first play |
| 3 | the priority `SoundObject_LoadSample` evicts by: 0 is never evicted |

| Rows | Names | Bytes 0–3 |
|---|---|---|
| 0–9 | `hmi\battle1a.hmp`, `battle2a`, `battle3a`, `battle1b`, `battle2b`, `battle3b`, then `battle1a` four times | 255, 100, 0, 0 |
| 10 | `shipexpl` | 0, 98, 1, 1 |
| 11–15 | `laser1h`, `laser3h` (volume 97), `tomlow`, `timproll`, `missle1` | 0, 98, 0, 1 |
| 16 | `enemfly4` | 255, 90, 1, 1 |
| 17 | `gm_69` | 0, 70, 1, 1 |
| 18–27 | `gun1a3`, `start3`, `trgslct`, `trglock`, `trgunloc`, `wrnwoop1`, `wrnwoop2`, `strcfail`, `gnract`, `gnrdact`; the three warnings 23–25 repeat 5 | 0, 70, 0, 1 |
| 28 | `whitenz` | 0, 70, 1, 1 |
| 29 | `footf2` | 0, 30, 1, 1 |
| 30–31 | `callpsa`, `callpsb` | 0, 98, 0, 1 |
| 32–37 | `tur1a`, `tur1b`, `tur1c`, twice; each `tur1b` repeats 255 | 0, 20, 1, 1 |
| 38–44 | `1explo`, `1explo2`, `explo1d`, `explo2`, `explo3`, `lsrhit2`, `ricup` | 0, 98, 0, 1 |
| 45 | `herceng1` | 255, 20, 1, 0 |
| 46 | `shield1` | 0, 98, 1, 1 |

Every row from 10 names `hmi\<name>.sfx`.

Each voice-line slot then gets `SoundManager_CreateObject`(manager, 0, 0, index) for the indices that follow the catalog's rows, kept at the slot's `+0x36` with the index at `+0x34`.

### Voice lines

`SpeechLine_Load` (`0004e0f0`) takes a script name and a voice name in stack order. It finds the first voice-line slot (`000bd734`, `0x3a` bytes each, count `000bd73c`, [the speech slot](snc-lip-sync.md#the-speech-slot)) whose `+0x2b` holds the voice name's key, and returns it as it is, loading neither the sample nor the script again. With none, it takes one from `Voice_EvictLru` (`0004e08c`), returning 0 when that gives none, and stores the key at `+0x2b` and 1 at `+0x33`. On that new slot, when the manager `000959ac` exists and the byte `Sound_SpeechEnabled` (`000959e4`), which preferences option 2 sets ([the option array](../simulation/preferences.md#the-option-array)), is set, it builds `simvoice\` + voice name + `.sfx` (`000959cb`, `000959d5`) in the slot's `+0x0e`, names it as the slot's name index `+0x34` with `SoundManager_SetSampleName`, and loads the slot's sound `+0x36` with `SoundObject_LoadSample`, returning 0 when that fails; then it loads the script with `Snc_Load`(slot, the sound `+0x36`, script name) ([`snc-lip-sync.md`](snc-lip-sync.md#the-simulators-scripts)). It stores `000981c4`, as read on entry, at `+0x2f` and returns the slot.

**`Voice_KeyOfName` (`0004e208`)**, (name), the key: a copy of the name with its zero in a 12-byte stack buffer, its length unchecked, upper-cased by `Str_ToUpperInPlace` (`0006d4b4`), and from it (character 1 − `'0'`) × `0x1000000` + characters 3 to 7 read as five decimal digits, each character less `'0'`, signed, times 10000, 1000, 100, 10 and 1. For `P1_01000` that is `0x1000000` + 1000. Characters 0 and 2 and any after 7 do not enter. **`Str_ToUpperInPlace` (`0006d4b4`)**, (string), C convention: each byte up to the zero through `toupper`, in place; it returns the string.

So the key is the voice bank and the five digits of the id and attribute byte ([starting a line](../simulation/heads-down-display.md#starting-a-line)), and two lines with one voice name share a slot: two portraits of one voice bank saying one line get the script of whichever loaded first, though their script names differ in the portrait's letter. With the manager and speech on, a sample that fails to load leaves the key and `+0x33` stored and no script loaded, and the next line with that key finds the slot and returns it with the buffer as `Voice_EvictLru` left it, holding the script it last held. The key holds the whole name only while the voice bank is one digit and the id two: an id of 100 or more, or a bank of 10 or more, moves the attribute byte's last digit past character 7, and names that differ only there share a key.

## The shell's names

`ShellSound_Init` (`ES1GO 00046235`; `es2_xref` finds its one call, from `ES1GO 00045d24`) gives the manager the 20 names at `ES1GO 000828c0`, `res32\xxxxxxxx.sfx` first, then the songs and sounds of the front end. Index 9, `res32\button.sfx`, is made into the sound `ES1GO 0007fd00` and loaded at once. After loading the drivers it sets the master volume to 95 (`ES1GO 000430cd`, the copy of `SoundManager_SetMasterVolume`) and the memory limit to 200,000 (`ES1GO 00043b0f`). `ES1GO 00043a90`, among the manager's copies where `SoundObject_UpdatePlacement`'s and `SoundManager_SetHearingRange`'s meet, has no `ES1DBSIM` copy: (manager and three more, its arguments removed by this function) it walks the 60 slots testing each object's `+0x15` and does nothing else; `es2_xref` finds no caller. Index 0 is a placeholder: `ShellSnc_OpenSound` names each talking-head line, `snc\<name>.RAW`, as index 0 and makes a sound on it ([loading a line](../shell/message-box.md#loading-a-line)).

`ES1GO 000087ce` returns 1 when `vol\speech.vol`, the `.RAW` lines' archive, opens; `es2_xref` finds its one call, from `ES1GO 00008920`.

## Open

- **Open:** the code that loads a digital driver out of `SOS\HMIDRV.386`.
- **Open:** the sample format the driver mixes the data as. The bytes centre on `0x80`, as unsigned 8-bit samples would, and many files open on a run of `0x7f`–`0x81`.
- **Open:** how device `0xa005`'s driver, reached through the in-image table `HmiMidi_InitDriver` calls, opens the digital driver from the manager's blocks, and so whether 11,025 Hz is the rate the samples play at; and what the block's `0x78`, 8, 1, `0x1000`, 1 and `0x13` are.
- **Open:** what plays `ROSEY1.SFX` and the eleven `res32\` `.RAW` files the shell's table does not name. `ES1GO`'s resource name builder, the function holding the reference at `ES1GO 00047170`, rewrites a `.RAW` extension, and is a lead.
- **Open:** what the start block's fields mean to the digital driver: `+0x0c`, the repeat count (does 0 play once, and 255 loop 255 times?); `+0x10`, 2 for every digital sound; `+0x38`, the `+0x43` slot `+0x20` sets; `+0x40`, the pan, and which of `0x000f` and `0xffff` is left; and the flags. And what `MidiSound_LoopCallback`'s answer and its flags' bit `0x80` mean to the song player.
- **Open:** the vtables' slots `+0x18`–`+0x24`, through which the scan of both images finds no call ([the vtables](#the-vtables)): whether `+0x18` and `+0x1c` resume and pause, as the `maybe_` names read them, and `+0x20` sets a pitch.
- **Open:** the HMI calls `SoundManager_DetectDigitalCard`'s helpers make (`0007235d`, `00072573`, `00072901`, `00072ce9`, `00072517`) and the buffer's fields they fill, for module E.
- **Open:** what `0006e055` and `0005049c` measure in `DigiSound_Play`'s choice of a sound to stop.
- **Open:** how the byte `000959e4`, which `Prefs_ApplyRadioOption` (`0004ad30`) sets for the preferences panel's `VOICE ONLY` and `TEXT / VOICE` ([the option array](../simulation/preferences.md#the-option-array)), gates loading a voice line's sample, and what the caller of `ES1GO 000087ce` does with its answer.
- **Open:** what a voice-line slot's sound object `+0x36` holds when `Sound_Init` makes no manager ([the driver](#the-driver)): whether the block `__vec_new` gives comes back zeroed. A value other than 0 is stored at the slot's `+0`, has its byte `+0x16` cleared by `Snc_Load` and, with speech on, is played through its slot `+0x10` by `Snc_Start` ([playing a line](snc-lip-sync.md#playing-a-line)). Proposed retail check: a mission with the sound card set to none in the setup, and a squadmate's line in its comm box.
- **Open:** `ES1GO 00041f64` and `ES1GO 0004240a`, which `ShellSound_Init` calls where `Sound_Init` calls `SoundManager_Ctor` and `SoundManager_LoadDrivers`.
- **Open:** where the object files from `0004e710` to the HMI timer's (`00050d38`) divide, and the source names of these files: a scan of the sound file's code for a pointer to one of the image's 41 `.cpp` paths finds none, where the same scan of the Heads-Down Display's file finds its two `phdd.cpp` asserts.
- **Open:** what reads `sos\DONS.BNK` and `sos\DRUM.DIG`. A case-insensitive search of both images for `dons` and the bank names finds `sos\melodic.bnk`, `sos\drum.bnk` and `sos\drum32.dig` in each, and no `dons` or `drum.dig`.
