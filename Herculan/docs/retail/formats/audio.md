# Audio

DBSIM's sound is three stacked layers:

| Layer | What it is |
|---|---|
| Backend | HMI **Sound Operating System** (SOS) 9503, bound at runtime out of `sos9503.dll`, plus Win32 `mciSendCommand` for CD audio |
| `SFX` | A general resource/voice manager: named samples, handles, a memory budget, priority eviction |
| `Sound_*` | The game's own layer: a 57-entry catalog keyed by integer id, 3D placement, and a separate five-slot speech channel |

The message channels themselves — the computer's ticker and the pilot/squad comm boxes that ride on this layer's speech slots — are [`cockpit-messages.md`](cockpit-messages.md)'s.

## Backend

### HMI SOS

`Sos_BindLibrary` (`004957f1`) picks the DLL by `GetVersion()` — Win32s (high bit set, major <= 3) gets `sos32s03.dll`, everything else `sos9503.dll` — then walks a self-describing binding table at `004a6ab4`: `0x24`-byte records of `{ void **destination, char name[0x20] }`, terminated by a NULL destination. **100 entry points** are bound this way: 44 `sosDIGI*`, 45 `sosMIDI*`, 8 `sosTIMER*`, plus `sosGetErrorString`, `sosPrepare32Memory`, `sosUnPrepare32Memory`. It refcounts (`004a6ab0`), so repeated calls bind once.

The v1.0 install ships `sos9503.dll` and not `sos32s03.dll`. v1.10 ([`../retail-builds.md`](../retail-builds.md)) carries both, `sos32s03.dll` in its Windows 3.1 build as `VER31\SOS32S03.DLL`.

**SOS's MIDI half is never brought up.** The `sosMIDI*` entry points are bound and the `SFX` layer carries a `.hmp` song path beside its sample path, but both executables' `Sos_InitBackend` (`004735fc`; VSHELL `0042d7f0`) pass MIDI driver id `0xffff` to both of their `Sos_OpenDrivers` calls, and `Sos_OpenDrivers` (`00473da0`; VSHELL `0042df68`) runs its MIDI arm — `sosMIDIInitDriver`, then `MELODIC.BNK` and `DRUM.BNK` — only for an id other than `0xffff`. `es2_xref` finds one caller of `Sos_MidiInitDriver` in each binary, inside that arm. A song voice therefore has no MIDI driver to play on. No `.hmp` ships in either build; VSHELL carries a hardcoded `.\sos\song.hmp`, and the `SOS` directory `SHELL0.VOL` lists is empty.

### CD audio

Music is Red Book, driven straight through MCI on device `cdaudio` — not through SOS at all. The `Sound_*` layer reaches it through four `SFX`-level thunks (`Sfx_PlayMusicTrack` `00464754`, `Sfx_StopMusic` `00464770`, `Sfx_GetMusicPosition` `00464884`, `Sfx_ResumeMusicAt` `00464898`) rather than through the digital backend, so no part of music touches a voice record.

| Function | MCI |
|---|---|
| `Music_PlayTrack` (`00473b3c`) | `MCI_OPEN` type `cdaudio`; `MCI_SET` time format TMSF; `MCI_PLAY` `MCI_FROM｜MCI_TO｜MCI_NOTIFY`, from track *n* to that track's own length |
| `Music_Stop` (`00473af4`) | `MCI_STOP` then `MCI_CLOSE`, and `Music_MciDeviceId` (`004a0e44`) back to -1 |
| `Music_GetPosition` (`00473c38`) | `MCI_STATUS` item `MCI_STATUS_POSITION`, `MCI_WAIT` |
| `Music_GetTrackLength` (`00473c78`) | `MCI_STATUS` item `MCI_STATUS_LENGTH` with `MCI_TRACK`; the result is kept in `Music_TrackLength` (`006b5610`) |
| `Music_ResumeAt` (`00473cc0`) | Same open/set/play, but `MCI_FROM` is a saved TMSF position rather than a track start, and `MCI_TO` is whatever `Music_TrackLength` already holds — it never re-queries |
| `Music_IsIdle` (`00473b2c`) | No command; the device id against -1 |

The device is held open only while a track is sounding: `Music_PlayTrack` and `Music_ResumeAt` open it, the other entry points use the id already held, and each failure after an open calls `Music_Stop` to close it again.

The track is meant to loop: on `MM_MCINOTIFY` (`0x3b9`) with `MCI_NOTIFY_SUCCESSFUL`, and only while `Music_CdEnabled` is set, `sfxWndProc` calls `Sfx_StopMusic` and then `Sfx_PlayMusicTrack` with `Music_CdTrack`. The notify is addressed to `Music_NotifyWindow` (`006b560c`), the handle `Sos_InitBackend` was given.

**On Windows 11 it plays once.** The `mcicda` driver never reports the end of a play: once the play head reaches `MCI_TO` the device goes on answering `MCI_MODE_PLAY` with the position frozen there, and no `MM_MCINOTIFY` is ever posted. So the restart never happens, and retail's mission music falls silent after one pass of its track.

#### The disc

The v1.0 disc's table of contents, as `IOCTL_CDROM_READ_TOC` reports it:

| Track | Kind | Start (LBA) | Length |
|---|---|---|---|
| 1 | data | 0 | — |
| 2 | audio | 172283 | 2:24.87 |
| 3 | audio | 183148 | 2:45.27 |
| 4 | audio | 195543 | 2:45.24 |
| 5 | audio | 207936 | 2:54.20 |
| 6 | audio | 221001 | 2:53.40 |
| 7 | audio | 234006 | 2:26.92 |
| lead-out | | 245025 | |

Track 7 is music in its own right, distinct from the other five, and **DBSIM never plays it**: the track formula below reaches 2 to 6 only, or below 2 for a negative `-R`. VSHELL has its own MCI play routine; see [Open](#open).

The v1.10 disc image keeps no table of contents; what its audio holds is in [`../retail-builds.md`](../retail-builds.md#the-v110-disc-image).

#### Which track, and whether there is one

`Sim_InitMissionSession` (`004614fc`) writes `Music_CdTrack` (`0049f914`) and `Music_CdEnabled` (`0049f918`) and then calls `Sound_StartMissionMusic` (`00463038`), which plays only if `Sound_MusicEnabled` is also up.

```
Music_CdTrack = Music_TrackSelect % 5 + 2
```

`Music_TrackSelect` (`004d25f7`) is the `-R` command-line switch, parsed with `atol` at `0045e824`; it is byte `+0xb7` of the `0xc3`-byte block at `004d2540` that `Main_StaticInit` clears, so it defaults to 0 ([Open](#open)). `ES.EXE` passes `-R<n>` with `n` counting the simulator launches of its own run from 0, so retail's missions play tracks 2, 3, 4, 5, 6, 2… in the order they are flown ([`../command-line.md`](../command-line.md#the-loop)). The remainder is a signed `IDIV`, so a negative `-R` would ask MCI for a track below 2.

v1.10 chooses and plays the track the same way. Both of its `DBSIM.EXE`s (`VER95\`, `VER31\`) carry this formula, the same nine accesses to `Music_CdTrack`, and the same `Sfx_PlayMusicTrack` and `Music_PlayTrack`, instruction for instruction once absolute addresses are masked; `VER95\ES.EXE` numbers `-R` as v1.0's does. Since the tracks are only rotated through, the order of the music on a disc decides which song a launch plays and nothing else.

The whole arm is skipped when `TrainingMissionNumber` (`004aa7ac`) is nonzero, so **a training mission runs without music**. That value is the copy of `script.dat` header offset 8 taken at the end of `DBSim_LoadScriptDat` (`00425321`); it also selects the larger pilot and squad message port and supplies the digit of the `TM<n>_` instructor voice template — see [`script-dat.md`](script-dat.md#header-format).

`Music_CdEnabled` is set at `00461cb0` and read by `sfxWndProc` before its loop restart and by `Sound_SuspendAll` before it saves the position. A mute tests `Music_CdTrack` alone.

#### The mission session overrides the MUSIC preference

`Sim_InitMissionSession` ends with an unconditional `Sound_SetMusicEnabled(1)`, long after the arm above has already tested the flag. With MUSIC off in `prefs.cfg` no track starts — `Sound_StartMissionMusic` sees the flag down — but the flag is then raised behind it, so the next `Sound_ResumeAll` starts the music the player turned off. Alt-tabbing away and back is enough.

#### No drive is named

`Music_PlayTrack` opens the device type and nothing else: `MCI_OPEN_TYPE` with the string `cdaudio`, no `MCI_OPEN_ELEMENT`, so MCI answers with whichever CD drive it picks. Neither executable imports or names `GetDriveType` or `GetLogicalDrives`, and `SOUND.CFG` has no key for a drive. Both read `data\drive.cfg`, a directory path with a drive letter, but only to find archives ([`vol-archive.md`](vol-archive.md#which-archives-are-mounted)) and to prefix the paths of the movies, the on-line manual and the training voice clips ([`cockpit-messages.md`](cockpit-messages.md#the-training-port)); it never reaches MCI. Nor is the disc checked: any audio CD in the drive plays.

### `sfxWndProc` (`00462294`)

Registered through `WndProcHook_Register` — this is one of the four `MainWndProc` filters mentioned in [`cockpit-input.md`](cockpit-input.md). It handles exactly two messages: `WM_TIMER` (`0x113`), pumped into the SOS/MME service routine, and the `MM_MCINOTIFY` loop above.

### `DATA\SOUND.CFG`

Plain INI, section `[Sound]`, read with `GetPrivateProfileString` by `Sfx_ReadConfig` (`00463698`) into the manager's config block at `+0x24`. VSHELL's `Sfx_Construct` (`0042bf3d`) reads it the same way, except that it reads `Driver` and then stores 1 whatever the file says.

| Key | Values | Stored |
|---|---|---|
| `Driver` | `DirectSound`, compared case-insensitively, gives 2; anything else, the shipped `MME` included, 1 | `+0x2a` |
| `Buffers` | `atol`; 1-64, else 5 | `+0x30` |
| `Rate` | `atol` of 11 gives `0x10`, anything else `0x20` | `+0x24` |
| `Width` | `Mono`, compared case-insensitively, gives 4; anything else 8 | `+0x28` |

`+0x26` is fixed at 1 and `+0x32` at `0x200`. A key the file lacks reads as the empty string, which gives the second value in each row, and so does a missing file. `atol` stops at the first non-digit, so `Rate = 11 kHz` is 11 and `Rate = 11025` is not.

`Sos_InitBackend` opens the digital driver through `Sos_OpenDrivers` with `Driver` as its id, and tries again with id 1 if that fails. The format word of the `sosDIGIInitDriver` block (`006b5662`) is `Rate | 1 | Width` when the detected driver's capability word (`006b5660`) has all three bits. Otherwise it is `0x15`, or the open fails if the capabilities have none of `0x15`'s bits. The file's own comments name `Rate`'s values 11 and 22 and `Width`'s Mono and Stereo, so the format sets the rate SOS mixes at, 11,025 or 22,050 Hz, and whether its output is mono or stereo; `0x15` is 11 kHz mono plus bit 1 ([Open](#open)). `Buffers` goes to `006b5680` and the fixed `0x200` to `006b5682`; `Buffers` only applies to the MME driver, per the file's comments. VSHELL's copies of both functions do the same.

## The `SFX` manager

One instance, a `0x4c`-byte heap object (`Mem_New(0x4c)`) whose pointer is `SfxManager` (`0049f904`). Its method names survive as `SFX::` assertion strings, all sixteen in both executables: `open`, `close`, `cache`, `play`, `stop`, `stopAll`, `isDone`, `setVolume`, `setPan`, `setPitch`, `setPriority`, `setLooping`, `setCallback`, `getAttributes`, `setAttributes`, `getSampleData`.

`Sfx_Init` (`00463590`) is called as `(memoryCap, 60, 90)` — **60 resource slots, 90 voice slots**.

| Field | Meaning |
|---|---|
| `+0x10` | resource slot count (60) |
| `+0x14` | voice slot count (90) |
| `+0x18` | memory cap in bytes |
| `+0x1c` | bytes currently cached |
| `+0x20` | handle generation counter |
| `+0x34` | resource table, stride `0x21c` |
| `+0x38` | voice table, stride `0x28` |
| `+0x3c` / `+0x40` | count of playing samples / playing songs |

**Handles are `generation << 16 | slotIndex`.** Every accessor re-reads the slot's own copy of the handle and rejects a mismatch, which is what the `Sample handle is old and no longer valid` assertions report. `0xffffffff` is the null handle.

A *resource* is one file; a *voice* is one playable instance bound to a resource. Two voices opened on the same filename share the resource and bump its refcount, which is how the ten music ids and their single file coexist.

### Resource record (`0x21c`)

```
+0x000  int32   handle
+0x004  char    name[0x200]        -- path as passed to open
+0x204  int32   user value (open's 4th argument)
+0x208  int32   refcount
+0x20c  int32   cached flag
+0x210  int32   byte size on disk
+0x214  void*   backend object (SOS sample, or MIDI song)
```

### Voice record (`0x28`)

```
+0x00  int32   handle
+0x04  int32   resource handle
+0x08  uint32  flags
+0x0c  int32   priority        default 5
+0x10  int32   callback        default 0
+0x14  int32   volume          default 100
+0x18  int32   loop count      default 1;  0 = forever, n = play n times
+0x1c  int32   pan             default 0x8000 (centre)
+0x20  int32   pitch           default 0x10000 (1.0 in 16.16)
+0x24  uint16  backend handle
```

Flag bits. `Sfx_Open` sets `0x0001` and `0x1000` from its open type, `Sfx_Play` sets `0x0100` and `Sfx_Stop`/`Sfx_StopAll` clear it, and the setters raise the other three as a side effect of a non-default value:

| Bit | Meaning |
|---|---|
| `0x0001` | resource is a `.hmp` MIDI song, not a sample |
| `0x0080` | looping (loop count is not 1) |
| `0x0100` | currently playing |
| `0x0400` | pitch is not 1.0 |
| `0x0800` | pan is not centre |
| `0x1000` | open type 2 — a third playback path, a file streamed by name through the window handle. See [Open](#open). |

### A repeated play layers; it does not restart

`Sfx_Play` (`00463f34`) never tests flag `0x100` before starting. It caches the resource if it has to and goes straight to `Sos_StartVoice` (`0047378c`), whose sample path zeroes the voice record's backend handle (`+0x24`) and hands that field to `sosDIGIStartSample` as an out-parameter. Every call is therefore given a **new** SOS handle, and the one it replaces is neither stopped nor reused: playing a catalog id that is already sounding starts a second concurrent copy on another driver channel.

The record's bookkeeping is one deep and does not follow. It keeps only the newest handle, so `Sfx_Stop` and `Sfx_StopAll` (`004647dc`) — both through `sosDIGIStopSample` — can no longer reach the older copies, and `Sfx_IsDone` asks `sosDIGISampleDone` (through `Sos_VoiceIsDone`) about the newest copy alone. The manager's playing-sample count (`+0x3c`) goes up once per start, in `Sfx_Play`, and down once when `Sfx_Stop` or `Sfx_StopAll` clears the record's playing flag, so every layered copy leaves it one higher. Those and its initialisation in `Sfx_ReadConfig` are the accesses to it that are known, and none of them reads it ([Open](#open)).

Because the settings belong to the record and not to the copy, **placing a new copy retunes the one already sounding**. `Sound_Place` sets volume and pan for the sound it is about to start, and `Sfx_SetVolume` (`00464514`) writes the record and then applies it through `Sos_ApplyVolume` (`004739e0`) to the handle at `+0x24` whenever the record is marked playing — which, until the new start overwrites it, is the previous copy. A near footstep therefore takes on the placement of the distant one that follows it.

[The play-request gate](#the-play-request-gate) is what would have thinned this, and neither play entry point goes through it.

### Binding the SOS DLL

`Sos_BindLibrary` (`004957f1`) loads the DLL its `GetVersion` test picks ([HMI SOS](#hmi-sos)), then walks `SosBindingTable` (`004a6ab4`) calling `GetProcAddress` for each entry. The table is 100 records of `0x24` bytes, terminated by a null destination:

```
+0x00  void**  destination slot   -- one of the pointers at 006cbde4-006cbf70
+0x04  char    exportName[0x20]   -- inline, not a pointer
```

Each slot has a one-line thunk in `00495xxx`-`00496xxx` (77 and 23 of them) that does nothing but call through it, so a thunk's meaning is recovered by reading its slot address out of the disassembly and finding that address in the table. The pointers live in BSS and this loop writes them through the destination field, which is why no instruction names a slot as a store target.

`Sfx_Open` (`00463910`) chooses the path from its third argument: 0 = `.hmp` song, 2 = the streamed type, and any other value a sample. What `SoundCatalog_Load` passes is in [Opening a catalog row](#opening-a-catalog-row).

### Memory budget and eviction

`Sfx_Cache` (`00463c48`) is the load/unload call. Loading first stats the file, and if `cached + size > cap` it calls `Sfx_EvictUntilFree` (`0046428c`) before committing. The victim picker (`0046417c`) scores every live voice and takes the **lowest**:

```
score = (resource cached ? 100 : 0)
      + priority
      + (playing            ? 1000  : 0)
      + (playing && looping  ? 10000 : 0)
```

so an idle, uncached, low-priority voice goes first and a looping playing one goes last.

### Loading a sample

`Sfx_Cache` loads a resource through `Sos_LoadOrFreeSample` (`0047371c`), which takes the song loader when the voice's flag `0x0001` is set and `Sos_LoadWaveSample` (`00474254`) otherwise; an empty file, or one that will not open, fails the cache. `Sos_LoadWaveSample` reads the whole file. One starting `RIFF` is taken as a canonical WAV at fixed offsets — rate at `+0x18`, channels `+0x16`, bits `+0x22`, data from `+0x2c` — with the dword at `+0x28` less `0x2c` as its length, so a canonical file's last `0x2c` bytes of sound are not played. **Anything else plays as raw 8-bit unsigned mono at 11,025 Hz.**

`Sound_Init` (`0046230c`) sets the cap to **2,000,000 bytes**, or **1,000,000** in the low-memory mode (`CockpitArt_LoadOnDemand` — `-l`, or under 12 MB physical).

### Backend volume and panning

`Sos_ApplyVolume` (`004739e0`) converts the voice's 0-100 volume to SOS's range as `volume * masterVolume * 0x7fff / 10000`, duplicated into both 16-bit halves for left and right, and for a MIDI song as `volume * 0x7f / 100`. `masterVolume` (`004a0e48`) is 100 in the image, and its one known writer is its setter `Sos_SetMasterVolume` (`004739a8`), which has no known caller ([Open](#open)).

## The sound catalog — `str\SOUNDS.STR`

The game addresses sounds by a small integer, 0-56. The mapping lives in `SOUNDS.STR`, a `.STR` string table (layout in [`str-strings.md`](str-strings.md)) whose single group of 57 entries pairs a filename with a 7-byte attribute blob.

`SoundCatalog_Load` (`00462448`) walks the group into three parallel arrays — names (`004d2b0c`), attribute pointers (`004d2bfc`), voice handles (`004d2cfc`) — and for each entry opens a voice, sets priority 5, applies the attributes, then fixes up defaults.

The code treats the blob as **ten** bytes. The file supplies seven; the last three are runtime scratch written in place, byte 8 by the loader and bytes 7 and 9 by the suspend and the request counter.

| Byte | Meaning |
|---|---|
| 0 | loop count — `Sfx_SetLooping`. `0` = loop forever, `1` = once, `n` = n times |
| 1 | volume, 0-100, applied as `Math_Q16Multiply(v, 65000)` |
| 2 | preload — nonzero caches the sample at startup instead of on first play |
| 3 | play requests per play (see below) |
| 4 | rolloff start distance, in units of 1024 world units. `0xff` becomes 5 |
| 5 | cutoff distance, same units. `0xff` becomes 100 |
| 6 | variation count — playing id *i* actually plays `i + rand(count)` when count > 1 |
| 7 | *runtime*: "was playing" flag, for suspend/resume |
| 8 | *runtime*: category volume percentage, initialised to 100 |
| 9 | *runtime*: play requests counted |

Because `.STR` attribute blobs point directly into the loaded file buffer, bytes 7-9 of one entry overlap the next entry's length field and first name byte. That is inert — every pointer is collected before the first write — and the four empty entries the file carries after the last real sound give the last one its slack.

`0xff` in bytes 4 and 5 means "use the default", not "not positional".

### Opening a catalog row

**The name does not decide a row's open type.** `SoundCatalog_Load` searches for `.hmp` (type 0, a song) and then `.wav` (type 1, a sample), but both `strstr` calls run over a 20-byte stack buffer the function reserves and never writes (`LEA ECX,[ESP+4]` with the needle just pushed), not over the row's name. Nothing in the loop writes that buffer, so every row gets the same type: 0 if the bytes an earlier call left there hold `.hmp`, 1 if they hold `.wav`, and otherwise whatever `EDI` already held — on the first row, the speech slot array `Sound_Init` (`0046230c`) has just allocated, a heap pointer, which `Sfx_Open` takes as a sample. Retail's effects play, which song voices could not ([HMI SOS](#hmi-sos)), so every row is opened as a sample. A `.hmp` named in `SOUNDS.STR` would therefore [load](#loading-a-sample) as raw 8-bit data and play as noise.

### Ids 0-9 are music

`Sound_IsCategoryEnabled` (`00462680`) splits the catalog at 10: ids below 10 answer to the music enable flag (`0049f90c`), ids 10 and up to the effects flag (`0049f910`). `Sound_MuteMusic`/`Sound_MuteEffects` and their unmute pair respect the same boundary. A second pair, `00462d20` and `00462e70`, does not: each mutes or unmutes the single id it is given and then clears or raises *both* flags. Neither has a known caller ([Open](#open)).

All ten music entries name `battle1.wav`, and **no v1.0 archive carries a `battle1.wav`**, so the digital-music path is dead in retail — music is the CD. v1.10's `SIMPATCH.VOL` adds one under both banks, 708 samples of near-silence, so the entries open and play nothing. `Sound_ShiftMusicSet` (`00462fbc`) offsets one character of each of the ten filenames by a delta and re-opens them, which is how a different set would have been selected.

### Sample banks

`Sound_ResolveSamplePath` (`00462238`) prefixes the catalog's filename with `HMI\` normally and `HMX\` in the low-memory mode. `SIMSOUND.VOL` carries both: 43 files under `hmi\` and 42 under `hmx\`. Every `hmx\` file is 8-bit mono 11,025 Hz. Of the 42 `hmi\` twins, 38 are 8-bit 22,050 Hz (twice the `hmx\` size); `TRGLOC`, `XPLMLT2` and `XPLMLT4` are 16-bit 22,050 Hz (four times); and `BACANN4` is 8-bit 11,025 Hz in both banks, the same size with different bytes.

**In v1.0, `EXPLO5.WAV` exists only in `hmi\`.** Catalog id `0x22` names it, so in low-memory mode that one sound fails to open. v1.10's `SIMPATCH.VOL` adds a copy under `hmx\`.

### The catalog

`vol`, `pre`, `thr`, `min`, `max`, `var` are attribute bytes 1, 2, 3, 4, 5, 6; `loop` is byte 0.

| id | File | loop | vol | pre | thr | min | max | var |
|---|---|---|---|---|---|---|---|---|
| 0 | `battle1.wav` | forever | 100 | 1 | 0 | - | - | 1 |
| 1-9 | `battle1.wav` | forever | 100 | 0 | 0 | - | - | 1 |
| 0x0a | `laser3h.wav` | 1 | 70 | 0 | 5 | 5 | 40 | 1 |
| 0x0b | `laser1.wav` | 1 | 50 | 0 | 2 | 5 | 40 | 1 |
| 0x0c | `impacts2.wav` | 1 | 70 | 0 | 2 | 0 | 15 | **3** |
| 0x0d | `impacts3.wav` | 1 | 70 | 0 | 2 | 0 | 15 | 1 |
| 0x0e | `impacts5.wav` | 1 | 70 | 0 | 2 | 0 | 15 | 1 |
| 0x0f | `missle.wav` | 1 | 98 | 0 | 2 | - | - | 1 |
| 0x10 | `xplmlt2.wav` | 1 | 50 | 1 | 3 | - | - | 1 |
| 0x11 | `gm_69.wav` | 1 | 90 | 1 | 1 | - | - | 1 |
| 0x12 | `bacann4.wav` | 1 | 90 | 0 | 3 | 5 | 15 | 1 |
| 0x13 | `start3.wav` | 1 | 90 | 0 | 0 | - | - | 1 |
| 0x14 | `bptslct.wav` | 1 | 90 | 0 | 1 | - | - | 1 |
| 0x15 | `trgloc.wav` | 1 | 90 | 0 | 2 | - | - | 1 |
| 0x16 | `trgunloc.wav` | 1 | 90 | 0 | 2 | - | - | 1 |
| 0x17 | `warn1.wav` | 5 | 30 | 0 | 0 | - | - | 1 |
| 0x18 | `wrnwoop2.wav` | 5 | 30 | 0 | 0 | - | - | 1 |
| 0x19 | `strcfail.wav` | 5 | 30 | 0 | 0 | - | - | 1 |
| 0x1a | `gnract.wav` | 1 | 80 | 0 | 1 | - | - | 1 |
| 0x1b | `gnrdact.wav` | 1 | 80 | 0 | 1 | - | - | 1 |
| 0x1c | `whitenz.wav` | 1 | 80 | 1 | 0 | - | - | 1 |
| 0x1d | `foot2.wav` | 1 | 98 | 1 | 4 | 1 | 20 | 1 |
| 0x1e | `callpsa.wav` | 1 | 68 | 0 | 2 | - | - | 1 |
| 0x1f | `callpsb.wav` | 1 | 68 | 0 | 2 | - | - | 1 |
| 0x20 | `plasma.wav` | 1 | 60 | 0 | 4 | 5 | 75 | 1 |
| 0x21 | `explo4.wav` | 1 | 60 | 0 | 3 | - | - | 1 |
| 0x22 | `explo5.wav` | 1 | 60 | 0 | 3 | - | - | 1 |
| 0x23 | `plsmahit.wav` | 1 | 99 | 0 | 4 | 1 | 50 | 1 |
| 0x24 | `explo7.wav` | 1 | 60 | 0 | 3 | - | - | 1 |
| 0x25 | `explos1.wav` | 1 | 80 | 1 | 3 | - | - | 1 |
| 0x26 | `explos2.wav` | 1 | 80 | 0 | 3 | - | - | 1 |
| 0x27 | `xplmlt4.wav` | 1 | 50 | 1 | 3 | - | - | 1 |
| 0x28 | `explo1d.wav` | 1 | 70 | 0 | 3 | - | - | 1 |
| 0x29 | `explo2.wav` | 1 | 70 | 0 | 3 | - | - | 1 |
| 0x2a | `explo3.wav` | 1 | 70 | 0 | 3 | - | - | 1 |
| 0x2b | `lsrhit2.wav` | 1 | 8 | 0 | 4 | - | - | 1 |
| 0x2c | `throtl.wav` | 1 | 100 | 0 | 0 | - | - | 1 |
| 0x2d | `herceng1.wav` | forever | 50 | 1 | 5 | 5 | 50 | 1 |
| 0x2e | `shield1.wav` | 1 | 80 | 1 | 4 | 0 | 25 | 1 |
| 0x2f | `podin2.wav` | 1 | 70 | 0 | 1 | - | - | 1 |
| 0x30 | `podland.wav` | 1 | 70 | 0 | 1 | - | - | 1 |
| 0x31 | `flyby1.wav` | 1 | 90 | 0 | 4 | - | - | 1 |
| 0x32 | `missin.wav` | 1 | 28 | 0 | 4 | 5 | 75 | 1 |
| 0x33 | `fire1a.wav` | forever | 40 | 0 | 4 | 0 | 25 | 1 |
| 0x34 | `ricup.wav` | 1 | 40 | 0 | 2 | 0 | 15 | 1 |
| 0x35-0x38 | *(empty)* | | | | | | | |

`-` is the authored `0xff`, i.e. the 5/100 defaults. The four empty entries have no attribute bytes at all and are never opened.

`0x33` is not the flamer: it is the burning-object loop, started by the first live [`FireEffect`](../simulation/destruction-effects.md#fire) and stopped by the last, and kept positioned on whichever fire is nearest the camera — see [`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#where-the-shared-sound-is-heard) for how that one is picked.

This resolves the sound ids scattered through the other docs: `0x0b` is `laser1.wav`, the beam muzzle sound of [`../simulation/weapon-firing.md`](../simulation/weapon-firing.md); `0x16` the target-lost tone of [`../simulation/missile-lock.md`](../simulation/missile-lock.md); `0x21` the drop-in lift's rumble ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-ride--liftstart_rise-0045d840)), which the turret's servo helpers test but never start ([`../simulation/torso-aim.md`](../simulation/torso-aim.md#the-servo-sound-helpers)); `0x2f` and `0x30` the drop pod's fall and landing in [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md). The `+ 10` seen at the three data-driven call sites — `record.SoundId + 10` from `BULLETS.DAT` (`Bullet_Fire`, record `+0x8`), `ROCKETS.DAT` (`Rocket_Fire`, `+0xc`) and `EXPLOS.DAT` (`Explosion_Construct`, `+0x24`) — is exactly the music/effects split: those tables index the effects half of the catalog from zero.

## Playing a sound

Two entry points, both taking a catalog id.

**`Sound_Play` (`0046272c`)** — non-positional. Applies the variation roll, sets volume to `Q16Multiply(vol, 65000) * byte8 / 100` (or 0 if the category is muted), and plays.

**`Sound_PlayAt` (`004627dc`)** — positional, `(id, worldPoint)`. Applies the variation roll, then `Sound_Place` (`00462898`) computes volume and pan; it plays only if the result is audible.

`Sound_Place` resets the model transform and pushes the world point through the current camera transform, so the listener is the camera. Then, with `d = Math_FastMagnitude3D(view)`:

```
minRange = attr[4] * 1024
maxRange = attr[5] * 1024
if d > maxRange:            volume = 0        -- not played at all
else:
    volume = Q16Multiply(attr[1], 65000)
    if d >= minRange:       volume = (maxRange - d) * volume / maxRange
volume = volume * attr[8] / 100
```

The rolloff divides by `maxRange`, not by `maxRange - minRange`, so a sound at exactly `minRange` is already attenuated rather than at full volume.

Pan comes from the horizontal bearing, `a = Math_Atan2Bam(viewX, viewY)`, as `(-2a) & 0xffff` for `a < 0x8000` and `2a & 0xffff` otherwise — a full sweep of the pan range over half a turn, mirrored front to back. At `a = 0` — a source dead abeam on the right, since `Math_Atan2Bam`'s 0 is view `+x` — the front-half formula gives 0, the hard-left end, while bearings on either side give values near `0xffff`, the hard-right end. Dead abeam on the left (`a = 0x8000`) is continuous.

`Math_Atan2Bam` (`0047d220`) also answers 0 when both of its arguments are 0, so **a source with no horizontal offset from the view is panned hard left**: one directly above or below the camera, or at it.

#### A sound played at the camera

The drop pod plays both its sounds at the camera's own position, `ViewObjectPtr + 4` ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-drop-pod--meteor)), and in ordinary play that point comes out of `Raster_ModelToView` as exactly `(0, 0, 0)`:

- `Sim_Run` (`0045f144`) calls `Sim_MainTick`, then `Cam_Update(ViewObjectPtr)`, then `Sim_RenderFrame`, so the camera is placed after the tick and before the view is built from it.
- `Sim_RenderFrame` (`0045fb9c`) installs the main view's projection from `ViewObjectPtr` through `Raster_InstallViewProjection` (`0048c1d8`), whose `View_BuildTransform` takes the camera's `+0x04` as the transform's translation `t` and which then inverts it.
- `Transform_InvertRigid` makes that `R^T` with translation `round(R^T · -t)`, each component a Q14 sum rounded by `+0x2000 >> 14`, and `Transform_ApplyToPoint` then gives `round(R^T · t) + round(R^T · -t)` for the camera's own position. That is 0 in each component unless the sum lands exactly on a half, one value in 16384, where it is 1.
- `Sound_Place` resets the model transform to identity first, so nothing else enters.

So the pod's whistle and landing are both heard **hard left**, whatever the pod's position. The MFD's map and missile view and the Heads-Down Display's command map install projections of their own ([Open](#open)).

At 166.667 world units per metre ([`../../herculan/planning.md`](../../herculan/planning.md)), a `max` of 40 is about 245 m, the largest authored one — `herceng1`'s 50 — about 307 m, and the default 100 that every `-` row takes about 614 m.

### The play-request gate

`Sound_ConsumeRequest` (`004626c4`) exists so that a sound fired by many objects at once does not play once per object. `Sound_Play` and `Sound_PlayAt` do not call it, so the authored divisors in attribute byte 3 do not thin a play made through either, and no other caller of it is known ([Open](#open)). What it computes:

```
interval = (2 - detailSetting) * attr[3]
if interval == 0:  play
else:              attr[9]++;  play only when attr[9] % interval == 0
```

`attr[9]` wraps at `0x0f`. `detailSetting` (`004d1fc7`) is an options-screen 0-2 value, so the highest setting zeroes the interval and lets everything through, while the lowest doubles the authored divisor.

### Mute, suspend and resume

`Sound_MuteMusic` / `Sound_MuteEffects` (`00462c74` / `00462cd8`) zero the volume of their half of the catalog and clear the enable flag; the unmute pair restores each id's own `Q16Multiply(vol, 65000) * byte8 / 100`. Music mutes by stopping the CD instead when a track is set.

`Sound_SuspendAll` (`00463078`) sets attribute byte 7 for each catalog voice that both loops forever (loop count `+0x18` is 0) and is marked playing (flag `0x100`), and clears it for every other; saves the CD position; and stops everything through `Sfx_StopAll`. A one-shot or a finite repeat cut off by the suspend is therefore not restarted, and neither is speech. That sweep is bounded by the manager's resource slot count (`+0x10`, 60) rather than its voice count (`+0x14`, 90), but `Sfx_Open` fills voice slots lowest first, and the voices opened are the 53 catalog voices and the 5 speech slots, 58 in all, so every voice is reached. `Sound_ShiftMusicSet` would open ten more without closing any, and it has no known caller ([Open](#open)). `Sound_ResumeAll` (`00463134`) replays the marked voices through `Sfx_Play`, at the volume, pan and pitch their records still hold, and resumes the CD from the saved TMSF position.

`Sound_SetCategoryVolume` (`00462f5c`) writes attribute byte 8, the per-sound category scale every volume computation multiplies through.

### The cockpit power-up

`Cockpit_PowerUpSound` (`004328cc`) is what the player hears on taking a machine. Two sounds, and the second is conditional:

```
if (cockpit+0x245 == 0):                 -- once per session
    cockpit+0x241 = Time_GetCoarseTicks()
    Sound_Play(0x13)                     -- start3, the start-up sequence
if (mech+0x1f2 -> +0x50 != 0):
    Sound_Play(0x2d)                     -- herceng1, looping forever
    Sound_SetPitch(0x2d, 42000)          -- 42000/65536, about 0.64
```

The engine hum is not started at its recorded rate: it is dropped to roughly two thirds of it immediately, which is what turns the sample into a hum rather than a whine. It loops for the rest of the mission — attribute byte 0 is 0 — and follows its machine through `Sound_UpdatePosition`. <!-- doc-lint: ok -->

**The hum belongs to the flyer, not to a HERC.** The gate is type record `+0x50`, which is file offset 78, the flyer flag, set on the RAZOR alone (see [`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md)'s type-record table). A walking HERC powers up with `start3` and nothing else; its running noise is its footsteps. The RAZOR powers up with the hum and nothing else: `Gau_BuildCockpitWidgets` sets `+0x245` for a flyer before this runs ([`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#power-up-sequence)), so `start3` never plays for it, its start is never stamped and the power-up announcement never comes.

### Sounds a cockpit control makes

These play directly rather than through any data table:

| Trigger | Sound |
|---|---|
| `Mech_ToggleRadarMode` (`0041b468`) | `0x1a` `gnract` going ACTIVE, `0x1b` `gnrdact` going PASSIVE. Not positional — the cockpit makes it, not the world. |
| [Tab] on the command display, `HddCommandScreen_KeyDispatch` (`0044cc40`) | The same pair, on what the screen's unit slot `+0x15c` holds after the cycle (`HddCommandScreen_CycleHostileUnit` for ATTACK ENEMY, `_CycleFriendlyUnit` for DEFEND POSITION, none for another order): `0x1a` as a held unit becomes the pick, `0x1b` when it is empty. |
| `HddCommandScreen_PickTarget` (`0044d6b8`) | `0x14` `bptslct` when a click or [Enter] on the command display's map picks a unit or a gridpoint for the armed order. |
| `Widget_ClickSound` (`00438e2c`) | `0x11` `gm_69`, the console click. |

**No tone of XMIT's own is found.** `HddDisplay_HandleWidgetPress` (`0044a178`)'s case 13 calls no play function, and neither does any function it calls that was read for it: `HddCommandScreen_CommitPick`, `_FillOrderRecord`, `_SetMessageRow`, `_CancelTransmission`, `Squad_SendOrderToSlot` and `HddDisplay_SelectPilot`. `es2_xref.py` finds 31 callers of `Sound_Play`, 9 of `Sound_PlayAt` and 4 of `Sfx_Play`, and none of them is one of those functions ([Open](#open)). Every `Sound_Play` call pushes an immediate id, and the `0x1a`/`0x1b` pushes are the radar toggle's, [Tab]'s and `MessagePort_Show`'s alert tones ([`cockpit-messages.md`](cockpit-messages.md#the-port)). What a transmit is known to sound is XMIT's click, which its class plays whatever the press goes on to do (below), and then the squadmate's reply through its comm box ([`heads-down-display.md`](heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0044b5f8)).

The mode-change tone is the [R] path only. The scanner screen's PASS/ACTIVE buttons write `mech+0x96` directly and play no mode tone; the only sound they make is the console click below, which their class `MFDStateGadget` carries. The radar toggle also announces the new mode in the computer's voice — see [`cockpit-messages.md`](cockpit-messages.md#posters).

`Widget_ClickSound` is the whole of the click: `push 0x11; call Sound_Play; ret`, and it is the image's only reference to that id. Nothing calls it directly — it sits in **fifteen widget vtables**, `PanelGadget`'s own and the fourteen button classes that inherit it, and a class's own `OnClick` calls it through that table, so whether a widget clicks is decided by its class and not by anything its handler goes on to do. That is why a button wired to nothing still clicks, and why the two system buttons, whose `OnClick` does not call it, are silent ([`cockpit-input.md`](cockpit-input.md#the-two-system-buttons)). The two list classes are silent the same way. The command display's order column and map viewport are `HDDListGadget`s, whose `OnClick` (`HDDListGadget_OnClick`, `0044f6ac`) only queues the click ([`heads-down-display.md`](heads-down-display.md#the-two-click-regions)); the MFD's screen area, which holds the FLASH COMM rows, is an `MFDListGadget`, whose `OnClick` (`MFDListGadget_OnClick`, `00447630`) only calls `MfdFlashComm_HandleListClick` ([`mfd.md`](mfd.md#mfdflashcomm--mode-1)). A double-click on the order already armed or the row already selected presses XMIT through `Widget_PressChild`, so that click is XMIT's. Both XMITs sound it whether or not their press transmits: `HDDSelectGadget`'s `OnClick` (`HddButton_OnClick`, `0044be50`) and `MFDSelectGadget`'s (`Widget_ForwardClickToOwner`) call the sound slot after the owner's handler, whatever it did.

Which kind matters: the slot belongs to `PanelGadget`, the mixin base a cockpit widget carries alongside its button or slider class, and `PanelSliderGadget` overrides it with an empty stub (`00439014`). **Dragging the throttle makes no sound at all**, and neither does an alert panel's slider row. A control that takes neither mixin has no such slot to begin with and is silent for that reason: the click surface over the 3D view, the F7 map's surface, `HDDisplayGadget` and `ScrollTrigger` — see [`cockpit-input.md`](cockpit-input.md#the-second-vtable).

## Speech and the comm portraits

Squadmate and commander speech does not go through the catalog. It has its own five-slot channel pool allocated by `Sound_Init`: five records of `0x42` bytes plus a 5 x 100-byte script buffer.

```
+0x00  int32   SFX voice handle (-1 = free)
+0x04  uint32  next script event time
+0x08  int16   current portrait frame  (-1 = finished)
+0x0a  char*   script cursor
+0x0e  char    wav name[0x21]
+0x2f  char*   this slot's 100-byte script buffer
+0x33  char*   name pointer, for the by-name lookup
+0x37  uint32  last-use tick, for LRU
+0x3b  byte    in-use
+0x3c  uint16  catalog id owning the slot
+0x3e  uint32  SFX voice handle
```

`Voice_Acquire` (`00462a98`) looks the requested `.wav` name up across the five slots; a miss evicts the least recently used one (`00462a2c`), copies the name in, opens an `SFX` voice at **priority `0xff`**, caches it, and loads the matching `.SNC` script. Speech is gated on its own enable flag (`0049f97e`). What the `.SNC` script drives is the comm portrait, not the audio — see [`heads-down-display.md`](heads-down-display.md#snc--portrait-lip-sync-scripts).

### File naming

`CommBox_BeginMessage` (`0044afc8`) builds two names from the comm box's portrait number (gauge `+0x135`, see [`heads-down-display.md`](heads-down-display.md)) and the message id:

```
suffix = "_" + 2-digit message id + 3-digit variant     e.g. "_01000"
wav    = "P" + voiceBank + suffix          in simvoice/simvoicf/simvoicg
snc    = "P" + ('A' + portrait) + suffix   in snc/
```

`voiceBank` is `Pilot_VoiceBankOf` (`00434260`): `(portrait >> 2) + 1`, with 3 remapped to 4 — so twelve portraits share three recorded voices, `P1_`, `P2_`, `P4_`. That is the same 1/2/4 grouping as the channel's own message sets ([`cockpit-messages.md`](cockpit-messages.md#its-message-sets)). `SIMVOICE.VOL` holds 147 `P*_*.WAV` and 66 `CVM_*.WAV`, the cockpit computer's own lines. `SIMSOUND.VOL`'s `snc\` holds 556 scripts across the twelve portraits `PA` to `PL`, 46 or 47 each.

The three name templates live together in DATA as literals the loader patches digits into: `BC_00000`, `TMx_0000`, `CVM_0000`. `TMx_` is the training instructor's, and its clips are loose files rather than archive entries — see [`cockpit-messages.md`](cockpit-messages.md#the-training-port).

The language picks a folder, not a file. `Voice_ArchiveName` (`0045ef68`) patches the last character of the literal `simvoice` with the language byte — `SIMVOICE` / `SIMVOICF` / `SIMVOICG` — and `Voice_FilePath` (`0045ef80`) puts that name in front of the clip as its folder, `simvoicf\P1_01000.wav`. Every archive in `vol\` is mounted ([`vol-archive.md`](vol-archive.md#which-archives-are-mounted)), so the clip comes from whichever archive carries that folder label. In the v1.0 install the three files are byte-identical (7,042,407 bytes each), all labelled `SIMVOICE\`, and the v1.0 installer lists the other two as 4-byte files. v1.10's are recordings in their own language: `SIMVOICF.VOL` (7,261,567 bytes) and `SIMVOICG.VOL` (6,610,094 bytes), labelled `SIMVOICF\` and `SIMVOICG\`, with the same 213 entry names ([`../retail-builds.md`](../retail-builds.md)). Only the cockpit computer is translated: 57 of the 66 `CVM_*.WAV` in French and 53 in German differ from the English, and all 147 squadmate clips are the English recordings.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `.SNC` is an audio format | It carries no samples. It is a two-byte-per-event portrait animation script, and the audio beside it is an ordinary RIFF WAV — see [`heads-down-display.md`](heads-down-display.md#snc--portrait-lip-sync-scripts). |
| Attribute byte 0 selects a mixer channel or category | Its three retail values (0, 1, 5) look like a small enum, but it is passed straight to `Sfx_SetLooping` as a repeat count — 0 means forever, which is why the music entries and `herceng1`/`fire1a` carry it. |
| Attribute byte 2 is "looping" | It is the preload flag; `Sfx_Cache` is a load call, not a play call. Looping is byte 0. |
| The `battle1.wav` entries are the real music | v1.0 ships no such file, and v1.10's is near-silence. The ten slots are a stub; music is Red Book CD audio through MCI. |
| `SoundCatalog_Load` opens a `.hmp` row as a song and a `.wav` row as a sample | The decompile shows the two `strstr` tests for those extensions, but their haystack is an unwritten stack buffer, not the name — see [Opening a catalog row](#opening-a-catalog-row). Every row opens as a sample. |
| A `.wav` name resolves under one directory | It resolves under `HMI\` or `HMX\` depending on the low-memory flag, and the two banks are not identical — v1.0's `HMX\` has no `EXPLO5.WAV`. |
| `herceng1` is the HERC engine hum | The name says so and the sample is one, but the only thing that starts it gates on type record `+0x50` — the flyer flag, the RAZOR. A walking HERC never plays it. |
| A speech voice's priority `0xff` protects it from eviction | Priority is one term of the [victim score](#memory-budget-and-eviction). A cached idle speech voice scores 355 and goes before any playing catalog voice (at least 1005); `0xff` wins only against catalog voices in the same cached and playing state. |
| One voice per catalog id means one copy of that sound at a time | The voice record is bookkeeping, not a hardware channel. `Sfx_Play` starts a fresh `sosDIGIStartSample` every call without testing the `0x100` playing flag, so the copies overlap — see [A repeated play layers; it does not restart](#a-repeated-play-layers-it-does-not-restart). |

## Open

- **Open:** whether a sound played at the camera ([A sound played at the camera](#a-sound-played-at-the-camera)) is ever measured against another view's projection. `MfdMapScreen_Paint`, `MfdMissileViewScreen_Paint`, `HddCommandScreen_DrawMap`, `HddCommandScreen_IsOnMap`, `HddCommandScreen_SynthesizeListClick` and `LiftStart_Rise` also call `Raster_InstallViewProjection` (`es2_xref.py`); when they run in the frame relative to the main view's install, and whether anything in `Sim_MainTick` writes the camera's `+0x04` before the pod's tick, were not read.
- **Open:** a sound reached from XMIT's press beyond the functions it calls directly. `Squad_SendOrderToSlot` calls the squadmate's vtable `+0x28`, `Mech_ReceiveSquadOrder`, and neither that function's callees nor any call made through a table were checked against the play functions' callers.
- **Open:** which word of the `sosDIGIInitDriver` argument block at `006b5614` is retail's channel count.
- **Open:** what format bit 1 selects — `SOUND.CFG` block `+0x26`, fixed at 1 and part of the `0x15` fallback.
- **Open:** no `Sfx_Open` call passing open type 2, the streamed voice behind flag `0x1000`, is known. `es2_xref.py` finds three callers — `SoundCatalog_Load` ([0, 1 or a heap pointer](#opening-a-catalog-row)), `Voice_Acquire` (1) and `Sound_ShiftMusicSet` (0) — and no stored pointer. `SoundCatalog_Load` holds a `row == 0 → type 2` arm at `004624eb`, after its `.wav` test, but the instruction before it is an unconditional jump past it, and `es2_xref.py` finds no branch to it.
- **Open:** whether VSHELL plays CD music, and which tracks. It has its own MCI play routine (`0042dcef`), reached only through the thunk `0042d5d1`, and `es2_xref.py --binary VSHELL` finds no reference to that thunk.
- **Open:** no writer of `Music_TrackSelect` but the `-R` parse and the static clear, and no store to `Music_CdTrack` but `00461caa`, found by `es2_fieldscan.py` over the `004d2540` block (`+0xb7`) and `es2_xref.py`.
- **Open:** no reader of the playing-sample count `SfxManager+0x3c` found by `es2_fieldscan.py`, and no reference to `Sos_SamplesPlaying` (`00495d4f`), the `sosDIGISamplesPlaying` thunk, found by `es2_xref.py`.
- **Open:** no caller found by `es2_xref.py` (and no late function start near them by `es2_late_entries.py`) for `Sound_ConsumeRequest` (`004626c4`), `Sound_ShiftMusicSet` (`00462fbc`), `Sos_SetMasterVolume` (`004739a8`), or the single-id mute pair `00462d20`/`00462e70`.
