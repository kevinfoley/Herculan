# DBSIM.EXE — the sound catalog and voice lines

**The simulator names its sounds in a catalog, `str\SOUNDS.STR`, and plays them by row; the voice lines take the rows after it.** Addresses are `ES1DBSIM` image addresses. The objects and the manager that play them are [`sound-manager.md`](../sound/sound-manager.md), and the files [`sound-samples.md`](../formats/sound-samples.md).

## The simulator's catalog

`SoundCatalog_Load` (`0004dc70`), the last step of `Sound_Init`, reads `str\SOUNDS.STR` row by row with `StringTable_ReadNext` into a name array (`SoundCatalog_Names`, `000b18a4`) and an attribute pointer array (`SoundCatalog_Attributes`, `000b1994`), and passes the names to `SoundManager_SetSampleNames`, so a row's number is its name index. For each row it makes `SoundCatalog_Objects` (`000bd63c`)[row] with `SoundManager_CreateObject`(manager, 2, attribute byte 3, row), calls the object's slot `+0x28`(-1, -1, attribute byte 0) and `+0x2c`(attribute byte 1), and loads it at once when attribute byte 2 is set; the row count goes to `SoundCatalog_Count` (`000bd72c`). `Sound_Play` and its siblings take a row number ([playing a row](#playing-a-row)).

The name array holds every row `StringTable_ReadNext` reads, empty ones included, since its last call, at the group's end, stores nothing: 57 names, of which `SoundManager_SetSampleNames` makes `+0x502` 56. The array's 58th dword, which the load never writes, ends the list as 0 from the start-up's zero fill ([loading the image](../formats/x32-executable.md#loading-the-image)). The creation loop stops at the first empty name, testing the names rather than a count: a table whose names were all non-empty would read past the 57 stored, and with no table it reads a name through the null first entry. It uses `SoundManager_CreateObject`'s answer unchecked, calling the object's slot `+0x28` on it.

`SOUNDS.STR` is a `.STR` table ([the layout](../../retail/formats/str-strings.md#layout)) of one group, 47 named rows with four attribute bytes each and ten empty rows with none; the loader stops at the first empty name.

| Byte | Meaning |
|---|---|
| 0 | the repeat count, zero-extended into the slot `+0x28` count ([the vtables](../sound/sound-manager.md#the-vtables)); a song's is counted down by `MidiSound_LoopCallback`, a digital sound's is the start block's `+0x0c`. `0xff` is 255, not the callback's endless -1 |
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

Each voice-line slot then gets `SoundManager_CreateObject`(manager, 0, 0, index) for the indices that follow the catalog's rows, kept at the slot's `+0x36` with the index at `+0x34`. Those indices, 47–56 (47 alone in the low-memory mode's one slot), are the table's ten empty rows, so each passes the index test and makes a `DigiSound` with no name and flags 0, which `SpeechLine_Load` names ([voice lines](#voice-lines)).

## Playing a row

`Sound_Play` (`0004de84`), `Sound_Stop` (`0004df70`), `Sound_IsPlaying` (`0004e05c`) and `Sound_Preload` (`0004dde0`) each take a row, their argument removed by the function, and do nothing without a sound manager, answering 0:

- **`Sound_Play`** plays `SoundCatalog_Objects` (`000bd63c`)[row] through its slot `+0x10` when its kind's switch is on (`SoundObject_IsCategoryEnabled` (`0004de50`), [turning songs and sounds off](#turning-songs-and-sounds-off)).
- **`Sound_Stop`** stops it through its slot `+0x14`.
- **`Sound_IsPlaying`** answers its slot `+0x30`.
- **`Sound_Preload`** answers `SoundObject_LoadSample` on it.

None tests the row: the catalog fills rows 0–46, the start-up's zero fill leaves the rest 0, and `Sound_Play` on such a row passes the null object to `SoundObject_IsCategoryEnabled`, which reads its `+0x17`. `Sound_PlayAt` places the row first ([placed sounds](../sound/sound-manager.md#placed-sounds)).

## Turning songs and sounds off

**Preferences' MUSIC and SOUNDS switch the songs and the digital sounds, and switching one off stops what of it plays.** `Sound_Play` plays an object only while its kind's switch is on (`SoundObject_IsCategoryEnabled`): `Sound_MusicEnabled` (`000959b8`) for a song, `Sound_EffectsEnabled` (`000959bc`) for a digital sound, both 0 in the image. `es2_xref --decode` finds each dword's absolute references there and in its setter, which is the one that writes it.

- **`Sound_SetMusicEnabled` (`0004e294`)**, (on): when *on* is 0 and the switch was on, with a manager, stops the songs with `Sound_StopSongs`; then stores *on*, a byte zero-extended, so the switch holds the option's value rather than a boolean. `es2_xref` finds calls from `Prefs_ApplyMusicOption` (`0004ad0c`, preferences option 0's handler, [the option array](preferences.md#the-option-array)) and from `Sim_StartMission`'s end, with 1 ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)).
- **`Sound_SetEffectsEnabled` (`0004e2c8`)**, (on): the same over `Sound_EffectsEnabled` with `Sound_StopDigitalSounds` (`0004e34c`), called through the two `NOP`s before it at `0004e34a`. `es2_xref` finds one call, from `Prefs_ApplySoundsOption` (`0004acfc`, option 1's).
- **`Sound_StopDigitalSounds` (`0004e34c`)**: with a manager, each object in the manager's slots below the catalog's row count (`000bd72c`), not the catalog's array, whose kind `+0x17` is 0 and whose slot `+0x30` says it plays is stopped through its slot `+0x14`; a song plays on. `es2_xref` finds one call, from [the death flash](main-loop.md#the-death-flash--sim_deathflash-00058414).
- **`Sound_StopSongs` (`0004e2f9`)**: the same for the objects whose `+0x17` is set.

The voice lines are digital sounds that SOUNDS does not switch: RADIO does ([playing a line](../formats/snc-lip-sync.md#playing-a-line)). Turning a switch on starts nothing; option 0's handler then runs `Music_Tick` ([the frame's sounds](main-loop.md#the-frames-sounds--sim_framesoundtick-00058574)).

## Voice lines

`SpeechLine_Load` (`0004e0f0`) takes a script name and a voice name in stack order. It finds the first voice-line slot (`000bd734`, `0x3a` bytes each, count `000bd73c`, [the speech slot](../formats/snc-lip-sync.md#the-speech-slot)) whose `+0x2b` holds the voice name's key, and returns it as it is, loading neither the sample nor the script again. With none, it takes one from `Voice_EvictLru` (`0004e08c`), returning 0 when that gives none, and stores the key at `+0x2b` and 1 at `+0x33`. On that new slot, when the manager `Sound_Manager` (`000959ac`) exists and the byte `Sound_SpeechEnabled` (`000959e4`), which preferences option 2 sets ([the option array](preferences.md#the-option-array)), is set, it builds `simvoice\` + voice name + `.sfx` (`000959cb`, `000959d5`) in the slot's `+0x0e`, names it as the slot's name index `+0x34` with `SoundManager_SetSampleName`, and loads the slot's sound `+0x36` with `SoundObject_LoadSample`, returning 0 when that fails, before the store below, so the slot keeps the last use `Voice_EvictLru` chose it by; then it loads the script with `Snc_Load`(slot, the sound `+0x36`, script name) ([`snc-lip-sync.md`](../formats/snc-lip-sync.md#the-simulators-scripts)). It stores `000981c4`, as read on entry, at `+0x2f` and returns the slot.

**With no manager** a line loads its script and plays no voice. `SpeechLine_Load` still passes the slot's `+0x36` to `Snc_Load`, and the slot's constructors do not write it ([the speech slot](../formats/snc-lip-sync.md#the-speech-slot)), so it is what the block `__vec_new` gave held there ([Open](#open)). `Snc_Start` plays it only with `Sound_SpeechEnabled` set, and the byte's writes leave it 0. `Sim_StartMission`'s step 3 runs `Prefs_SetOption`(1, 0, *i*) for options 0 to 2 when there is no manager ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc)), which runs option 2's handler, `Prefs_ApplyRadioOption` (`0004ad30`), with 0; `es2_xref --decode` finds one write of `000959e4`, in that handler; and the preferences panel puts RADIO in state 2, which the mouse and the keys pass over ([the loop](preferences.md#the-loop--preferencespanel_run-00047a88)). The controls panel's DONE reruns the handler of each option that differs from its load-time copy, option 2 among them, with the option's value, 0. What a value other than 0 does is `Snc_Load`'s clearing of its byte `+0x16`.

**`Voice_KeyOfName` (`0004e208`)**, (name), the key: a copy of the name with its zero in a 12-byte stack buffer, its length unchecked, upper-cased by `Str_ToUpperInPlace` (`0006d4b4`), and from it (character 1 − `'0'`) × `0x1000000` + characters 3 to 7 read as five decimal digits, each character less `'0'`, signed, times 10000, 1000, 100, 10 and 1. For `P1_01000` that is `0x1000000` + 1000. Characters 0 and 2 and any after 7 do not enter. **`Str_ToUpperInPlace` (`0006d4b4`)**, (string), C convention: each byte up to the zero through `toupper`, in place; it returns the string.

So the key is the voice bank and the five digits of the id and attribute byte ([starting a line](hdd-comm-boxes.md#starting-a-line)), and two lines with one voice name share a slot: two portraits of one voice bank saying one line get the script of whichever loaded first, though their script names differ in the portrait's letter. With the manager and speech on, a sample that fails to load leaves the key and `+0x33` stored and no script loaded, and the next line with that key finds the slot and returns it with the buffer as `Voice_EvictLru` left it, holding the script it last held. The key holds the whole name only while the voice bank is one digit and the id two: an id of 100 or more, or a bank of 10 or more, moves the attribute byte's last digit past character 7, and names that differ only there share a key.

## Open

- **Open:** what a voice-line slot's sound object `+0x36` holds when `Sound_Init` makes no manager ([voice lines](#voice-lines)): whether the block `__vec_new` gives comes back zeroed, and so whether `Snc_Load` clears a byte `+0x16` of a stray address.
