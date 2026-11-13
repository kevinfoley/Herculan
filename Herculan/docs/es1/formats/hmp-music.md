# DBSIM.EXE and GO.EXE — music

**ES1's music is MIDI, in HMI's `.HMP` song format, played through the MIDI drivers the sound setup names.** ES2 plays its music from the CD and ships no song ([ES2's sound backend](../../retail/simulation/audio.md#hmi-sos)). A song is loaded whole by the sound manager that loads the [digital sounds](sound-samples.md), and the HMI library linked into both programs reads it in place. Addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)).

## Which files

| Archive | Folder | Files | What names them |
|---|---|---|---|
| `SIMSOUND` | `hmi\` | 6, `BATTLE1A`–`BATTLE3B` | the first ten rows of the simulator's catalog ([sound-samples.md](sound-samples.md#the-simulators-catalog)) |
| `SHELL0` | `res32\` | 13: `DEATH`, `DESERT`, `ICE`, `INTRO1`, `INTRO2`, `LOSING`, `MENU`, `RUBBLE`, `SHELLA`, `SHELLB`, `SHELLC`, `VOLCANO`, `WIN2` | the shell's name table, which names each ([sound-samples.md](sound-samples.md#the-shells-names)) |

## Layout

```
+0x000  8          "HMIMIDIP", the bytes the loader compares; 24 zero bytes follow
+0x020  int32      offset from the file's start of the branch table
+0x024  12         (not read)
+0x030  int32      track count
+0x034  int32      (not read)
+0x038  int32      the song's ticks a second
+0x03c  int32      (not read)
+0x040  16 x int32 per MIDI channel, a value the channel-stealing mode compares
+0x080  32 x 5 x int32   per track, the device ids it can play on, a 0 ending the list
+0x300  8          zero; the loader writes the caller's callback here
+0x308  the tracks, back to back:
          +0x00  int32  chunk number
          +0x04  int32  length, these 12 bytes included
          +0x08  int32  the track's MIDI channel
          +0x0c         the events, length - 12 bytes
the branch table, at +0x020's offset:
          track count x byte   how many entries each track has
          24-byte entries, each track's in turn:
            +0x00  int32  offset into the track's events of the event the branch lands on
            +0x04  byte   the branch's id
            +0x05  byte   the program a branch to it sets
            +0x06  byte   the loop count, written while the song plays
            +0x07  byte   the length of the entry's data
            +0x08  int32  the slot the loader writes the entry's data pointer into
            +0x0c  int32  offset from the file's start of the entry's data
            +0x10  8      (not read)
          the entries' data, to the end of the file: controller and value pairs
```

In all 19 files:

- the 24 bytes after the signature, the 12 at `+0x024`, the 8 at `+0x300` and each entry's last 8 are zero; `+0x034` and `+0x038` are 120, `+0x03c` runs from 17 to 90, and each dword from `+0x040` is 0 or 9;
- the tracks are numbered from 0 in order and end where the branch table starts; track 0 holds one event, an end of track at delta 0, lists no device and has no branch entry;
- every other track has one branch entry, of id `0x80` and loop count 0, whose data is one or two pairs of controller 7 (volume) and 10 (pan) or is empty; 203 of those 222 tracks hold a controller `0x6c` event near their start, and in `LOSING`'s tracks 9–12 the entry lands just past it;
- each entry's `+0x08` holds the same offset as its `+0x0c`, and every offset points into the data after the entries, except one entry of `SHELLC.HMP`'s, which points at the file's end.

### Events

A track is a run of events, each a delta time and then a MIDI message. The delta is read by `HmiMidi_ReadDelta` (`0006ed60`, `ES1GO 0005f518`): seven bits a byte, the first byte the lowest seven, ending at the byte whose top bit is set. That is the reverse, in byte order and in its end marker, of a standard MIDI file's variable-length number. The message always opens with its status byte (no running status), and its length comes from the status alone, from two tables in the program:

| Status | Length | |
|---|---|---|
| `0x8n`, `0x9n`, `0xAn`, `0xBn`, `0xEn` | 3 | |
| `0xCn`, `0xDn` | 2 | |
| `0xF1`, `0xF3` | 1 | |
| `0xF2`, `0xF8`–`0xFE` | 2 | |
| `0xFF 0x2F` | 3 | the end of the track |
| `0xFF 0x51` | 5 | stepped over; the player sets no tempo from it |
| any other `0xFF` | 2 | stepped over |
| `0xF0`, `0xF4`–`0xF7` | 0 | |

A data byte may have its top bit set: the branch ids and loop counts of the controllers below are `0x80` and `0xff`. The 19 files hold only notes (`0x9n`, 44,074), controllers (20,332), program changes (2,103), pitch bends (27,070) and the 241 ends of track; a note ends with a `0x9n` of velocity 0.

## Loading

`MidiSound_Play` (`000505c8`, `ES1GO 0004423b`), the `MidiSound` vtable's `+0x10`, returns 0 when the object's slot `+0x30` returns non-zero or the song fails to load (`SoundObject_LoadSample`, which reads the file whole). It builds a block at `+0xbf`, the far pointer to the loaded file followed at `+0xc7` by the far callback `MidiSound_EndCallback` (`0004e7b4`), and calls `HmiMidi_InitSong` (`0006e4c1`, `ES1GO 0005ec79`) with it, the object's track map `+0x3f` and `+0x19` for the song's handle. An error goes to `+0x04` and is formatted into `SoundObject::playMIDI: %s` with `HmiLib_ErrorText` (`0006f23d`)'s text for it; the function then sets `+0x19` to -1 and `+0x1d` to `0xffff`, and returns 0. Otherwise it gives the song the branch callback at the far pointer `+0x35` when that is set (`HmiMidi_SetBranchCallback`, `0006eeae`), starts it (`HmiMidi_StartSong`, `0006ea6b`), copies the handle to `+0x1d` and `+0x21` to `+0x23`, gives it the loop callback `MidiSound_LoopCallback` (`0004e718`) (`HmiMidi_SetLoopCallback`, `0006ee87`), and returns 1.

`HmiMidi_InitSong` works on the loaded copy:

1. It returns `0xe` when the file does not open `HMIMIDIP`, and `0xb` when all 8 of its song slots are taken.
2. It keeps the file's far pointer and the caller's track map in the slot, and the track count from `+0x30` twice, one copy then counting the tracks still playing.
3. It writes the block's callback into the file's `+0x300`, the offset there and the selector at `+0x304`.
4. It walks the tracks from `+0x308`, each the previous one's length on: for each it keeps a pointer to the chunk and one to its events, moved past the first delta, which it keeps per track.
5. From the branch table it keeps, for each track with entries, a pointer to its first, and turns every entry's `+0x0c` offset into a pointer at its `+0x08`.
6. It routes each track whose map entry is `0xff` to a driver. Of the track's device ids, in order up to a 0, the first that one of the open drivers (up to five, their ids at `000a130c`) answers wins, and the map entry becomes that driver's slot: `0xa000` is answered by a driver of id `0xa000`, `0xa001` or `0xa008`, `0xa002` by `0xa002` or `0xa009`, and any other id by itself. A track whose first id is 0 goes to slot 0. A track no open driver answers loses its events pointer, keeps `0xff`, and leaves one fewer track playing.
7. It returns 0, with the slot through the handle pointer.

`MidiSound_Ctor` sets all 32 map entries to `0xff`, so every track is routed this way. Each program opens two MIDI drivers, the shell through its copy of `SoundManager_LoadDrivers` (`ES1GO 0004240a`, [the copies](sound-samples.md#the-copies)): the MIDI device `resource.cfg`'s `[Music]` `Driver#` names, `0xa002` in the images' defaults, with the melodic and drum banks, and `0xa005` with `sos\drum32.dig` ([sound-samples.md](sound-samples.md#the-driver)).

## Playing

**A song advances one tick on each interrupt of the game's timer, 120 times a second, and a delta counts those ticks.** `HmiMidi_StartSong` registers the song's tick, `HmiMidi_SongTick` (`0007ef95`), with the library's timer (`HmiTimer_AddEvent`, `0006d5e6`) at the rate the file's `+0x038` gives, keeps the timer event's number per song and the song's slot per timer event (`0009d900`), and marks the song playing.

### The library's timer

The library keeps 16 timer events, each a far handler and a rate in calls a second, and one divisor, the PC timer divisor it takes the timer to run at (`0009d7dc`). An event's step is its rate over the rate the divisor stands for, `0x1234dc` / divisor, as a 16.16 fraction. `HmiTimer_Dispatch` (`0006da51`) adds each live event's step to its accumulator and, when that passes 1.0, drops the whole part and calls the handler, first storing the event's song slot in `0009d910` when it has one; that byte is how `HmiMidi_SongTick` knows its song. `HmiTimer_AddEvent` lowers the divisor to the new event's own when that is smaller, and so do `HmiTimer_SetEventRate` (`0006d78c`) and `HmiTimer_RemoveEvent` (`0006d8d5`), which sets it for the fastest event left; each then works out every step again. The divisor is only a number: `HmiTimer_SetDivisor` (`0006da37`) stores it and calls `HmiTimer_NoOp`, which writes no port.

`SoundManager_LoadDrivers` starts the timer with `HmiTimer_Init` (`0006d4f5`)(0, 1). Bit 0 of the second argument keeps the library from installing a handler of its own and leaves the divisor at `0xffff`; the dispatcher is called instead by the game's timer callback `0004e710`, which `SoundManager_LoadDrivers` registers at a count of the interrupts a second over 120, so 1: on every interrupt ([the rate](../simulation/timer.md#the-rate)). A song's event at 120 a second lowers the divisor to `0x1234dc` / 120, its step is then exactly 1.0, and the song ticks once a dispatch.

### The tick

`HmiMidi_SongTick` does nothing for a song that is not playing or is paused. Otherwise it first runs [the fade](#volume), then takes each track in order, up to the file's track count:

1. It adds 1 to the track's tick count.
2. While the track has an events pointer and the count has reached the track's delta, it handles the event at the pointer, zeroes the count, moves the pointer on by the event's length and past the next delta, which becomes the track's delta, and goes on with the next event while that delta is 0.

So an event of delta *d* is handled *d* ticks after the one before it, and the first event of a track at its first delta, from the first tick on. What handling an event does:

- **`0xFF 0x2F`** ends the track: its events pointer is cleared and the count of tracks playing drops by 1. When that reaches 0 [the song ends](#the-end).
- **Any other `0xFF`** is stepped over.
- **A controller `0x6c`–`0x79`** is the player's own ([below](#the-branch-controllers)), and no driver gets it.
- **Anything else** goes to the track's driver through `HmiMidi_SendEvent` (`000715c5`) with the event's length.

`HmiMidi_SendEvent` (song, far event pointer, driver slot, length) calls the first function of the slot's driver entry ([the drivers](#the-drivers)) with the event's far pointer, the length and the slot. With the channel-stealing mode off it passes every event as the file holds it, except a controller 7, whose value it keeps per driver and channel (`000a56fc`) and replaces with the [scaled volume](#volume), and the other controllers, which it copies into the library's own 4-byte buffer first. `SoundManager_LoadDrivers` ends by turning the mode off (`HmiMidi_SetChannelStealing`, `0007225a`, with 0), in both programs, and `es2_xref.py` finds no other call of it, so the mode's code is not described here: with the mode on, `HmiMidi_SendEvent` maps each song channel to a free channel of the device, or takes one over by comparing the values at the file's `+0x040`.

### The branch controllers

A branch moves a track's events pointer to the event a branch entry lands on: the entry of the track's list whose id `+0x04` matches, found by walking the list from its first entry with no count of its length. The player's controllers, by the event's second byte, *v* its third:

| Controller | Does |
|---|---|
| `0x6c`, `0x6e`, `0x71`, `0x74`, `0x78` | nothing: stepped over |
| `0x6d`, `0x73` | writes the loop count of this track's entry *v*: the byte 6 past this event's status, which is the value of the controller event that follows when its delta is one byte |
| `0x6f`, `0x70` | the song's loop end: takes 1 off entry *v*'s loop count unless it is 0 or `0xff`, calls the loop callback, and with a count left branches every track but track 0 that still has an events pointer to its own entry *v* |
| `0x75`, `0x76` | the track's loop end: the same count and callback, branching this track alone |
| `0x72` | the song's branch: calls the branch callback with *v* and its bit `0x80` set, and branches every track but track 0 to its entry of that id unless the callback answers 0 |
| `0x79` | the track's branch: the same, for this track alone |
| `0x77` | calls the song's trigger callback *v*, one of 127 a song |

The loop callback is called with (song, track, id, count left) and the branch callback with (song, track, id); an answer of 0 stops the branch, and with no callback set the count alone decides. A count of `0xff` is never counted down.

A branch of the whole song, for each track it moves, reads the delta at the landing point, zeroes the track's tick count, sends a program change with the entry's `+0x05` on the channel of the event it lands on, then each pair of the entry's data as a controller on that channel. A branch of one track sends the same on the channel the chunk's `+0x08` gives.

**Every retail song loops as a whole from track 1.** Its track 1 opens with `Bn 6d 80`, `Bn 6e ff` and ends with `Bn 6f 80` just before its end of track: the `0x6d` writes the loop count `0xff`, the value of the `0x6e` after it, into track 1's entry `0x80`, and the `0x6f` asks the loop callback and branches every later track to its entry `0x80`, near its start. No other track holds one of these, and no file holds a controller above `0x6f`. Tracks are handled in order within a tick, so the loop moves the later tracks before their own events of that tick are reached. Track 1 lists `0xa005` alone in three songs and `0xa002` first in the rest; a song whose track 1 [no driver answers](#the-devices) has no loop and ends when its tracks do.

`MidiSound_LoopCallback` answers 1 to an id below `0x80`. For `0x80` and above it finds the playing object by the song's handle and answers by its repeat count: -1 loops forever, 0 answers 0, and any other count is lowered by 1 through the object's slot `+0x28` and answers 1. The catalog gives each battle song 255 repeats.

Three functions that reach the same branches from outside the tick have no caller `es2_xref.py` finds in either program: `HmiMidi_BranchTrack` (`0006ef09`) and `HmiMidi_BranchSong` (`0006f04d`), which branch one track or the whole song to an id, and `HmiMidi_SetTriggerCallback` (`0006eed5`). Each of the first two clears the byte `000a77bc`, which the tick sets before a callback: a callback that branched this way has the tick leave the events pointer where the branch put it.

### The end

When the last track ends, the tick marks the song not playing, silences it (`HmiMidi_SilenceSong`, `00071f84`), removes its timer event, puts the song's tracks back at their starts with the loader's steps 2–4 and its dropping of the unrouted tracks (`HmiMidi_ResetSong`, `0006eb98`), and calls the callback the file's `+0x300` holds with the song's handle. That is `MidiSound_EndCallback`, which stops the playing object through its vtable slot `+0x14`: `MidiSound_Stop` (`000506f4`) calls `HmiMidi_StopSong` (`0006ead9`), which does the same silencing, removal and reset for a song still playing and calls no callback, then `HmiMidi_FreeSong` (`0006ea3d`), which clears the slot's file pointer.

`HmiMidi_SilenceSong` sends, for each routed track, on the chunk's `+0x08` channel and straight to the driver: controller `0x7b` 0 (all notes off), controller `0x79` 0, the pitch bend `En 40 40` and controller 7 with 0.

### Volume

**A channel's volume is the song's own controller 7 value, scaled by the song's volume and the master volume, each out of 127.** `HmiMidi_SendEvent` sends a controller 7 of value *v* as ((*v* × song volume) >> 7) × master volume >> 7, or as 0 while the song is muted.

- **The master volume** (`000a56d8`, 127 in the image) is set by `HmiMidi_SetMasterVolume` (`0006edb3`), which also sends a new controller 7 at once: for each open driver, on each channel its device's 16-byte channel table marks and on channel 9, the kept value × master volume >> 7, or 0 for a channel a pause silenced. `MidiSound_SetVolume` (`0005075c`) is its caller in both programs, with the object's volume (100 at most) × 127 × the manager's music volume / 10,000, so the F12 panel's MUSIC level and the shell's [fade](../shell/menus.md#the-backdrop) both act here.
- **The song volume** (`000a56dc` per song, 127 in the image) is set by `HmiMidi_SetSongVolume` (`0006f64e`), which sends each track's channel its kept value × song volume >> 7. Its callers are the library's own fade.
- **The fade.** `HmiMidi_FadeSong` (`0006f75e`)(song, flags, time, from, to) sets a song volume to move from one value to the other, and the tick steps it on every fourth tick: flag 1 adds the step, flags 2 and 4 take it off, and with flag 4 the song ends as [above](#the-end) when the steps run out. `es2_xref.py` finds no call of it in either program.
- **Pause and mute.** `HmiMidi_PauseSong` (`0006f24d`) stops the tick for the song and, asked to, sends controller 7 with 0 on each track's channel; `HmiMidi_ResumeSong` (`0006f362`) sends the kept values back. `es2_xref.py` finds one call of each, from `maybe_MidiSound_Pause` and `maybe_MidiSound_Resume`. `HmiMidi_MuteSong` (`0006f471`) and `HmiMidi_UnmuteSong` (`0006f55c`) do the same with the tick left running; `es2_xref.py` finds no call of either.

## The drivers

**The library plays through up to five open drivers, each a slot of six far functions.** `HmiMidi_InitDriver` (`00071076`) takes the first free slot (ids at `000a130c`) and fills its 0x24-byte entry at `000a21b8`:

| Entry | Function | Called by |
|---|---|---|
| `+0x00` | send an event: (far event pointer, length, slot) | `HmiMidi_SendEvent` and the volume, silence and reset functions |
| `+0x06` | open | `HmiMidi_InitDriver` |
| `+0x0c` | close | `HmiMidi_CloseDriver` (`0007144b`) |
| `+0x12` | reset | `HmiMidi_ResetDriver` (`00071515`), after controllers `0x79` and `0x7b` with 0 on all 16 channels |
| `+0x18` | load a bank | `00072275`, which `SoundManager_LoadDrivers` calls for the melodic and drum banks and `sos\drum32.dig` |
| `+0x1e` | | |

For `0xa005` and `0xa003` the six come from tables in the program ([the devices](#the-devices)). For any other device the first five come from the driver's image, which `HmiMidi_LoadDriverImage` (`00081365`) [loads from `hmimdrv.386`](hmi-driver-container.md#loading-an-image) unless the caller's block already holds one, and the open function is then called with the three dwords of the caller's port block. `HmiMidi_CloseDriver` calls the close function, frees a loaded image with `HmiMidi_FreeDriverImage` (`00081599`) when asked to, and frees the slot.

## The devices

**The songs are written for the FM driver: what a song plays depends on the MIDI device the setup chose.** `resource.cfg`'s `[Music]` `Driver#` ([sound-samples.md](sound-samples.md#the-driver)) holds one of the MIDI devices the setup program `SOUNDSET.EXE` lists in its `SOUNDSET.INI`:

| Id | `SOUNDSET.INI`'s name | Answers a track listing |
|---|---|---|
| `0xa000` | Sound Master II | `0xa000` |
| `0xa001` | MPU 401 | `0xa000` |
| `0xa002` | FM | `0xa002` |
| `0xa004` | MT-32 | `0xa004` |
| `0xa008` | AWE 32 | `0xa000` |

The drivers themselves are [`SOS\HMIMDRV.386`'s images](hmi-driver-container.md#layout), which also hold a driver for `0xa009` (OPL3, answering a track that lists `0xa002`), `0xa00a` (Gravis UltraSound) and `0xa006` (the PC speaker). `0xa005`, which `SoundManager_LoadDrivers` opens with `sos\drum32.dig` and the digital driver's blocks, is none of them, and neither is `0xa003`: `HmiMidi_InitDriver` takes those two devices' functions from tables in the program itself, `000a3028` and `000a2574`.

Track 0 of each of the 19 songs lists no device and goes to slot 0. Of the others, every melodic track lists `0xa002` first, `0xa000` after it or not at all, except `ICE`'s tracks 13–15, `INTRO1`'s 10–14 and `INTRO2`'s 14–17, which list `0xa000` first (`INTRO2`'s 17 then `0xa00a`, `0xa002` and `0xa007`); the rest list `0xa005` alone, and most songs have one track listing `0xa003` alone. `DESERT`'s eleven tracks and every melodic track of `RUBBLE`, `VOLCANO`, `WIN2` and `LOSING` list `0xa002` alone. No track lists `0xa004`. So by step 6 of `HmiMidi_InitSong`, with `0xa005` always open beside the setup's device:

- the FM setup plays every track but the `0xa003` ones and the ones listing `0xa000` alone;
- a setup whose device answers `0xa000` drops every track listing `0xa002` alone, so `DESERT` plays nothing and `RUBBLE`, `VOLCANO`, `WIN2` and `LOSING` play only their `0xa005` tracks;
- the MT-32 setup plays only the `0xa005` tracks.

## Open

- **Open:** the header's `+0x024`–`+0x02f`, `+0x034` and `+0x03c`, and a branch entry's last 8 bytes, which the code read does not read.
- **Open:** whether every branch entry lands just past its track's `0x6c` event, and where the entries of the 19 tracks with no such event land.
- **Open:** the channel-stealing mode of `HmiMidi_SendEvent` and `HmiMidi_SilenceSong`, which both programs turn off.
- **Open:** the driver entry's `+0x1e` function, the arguments of its close, reset and bank functions, and the Gravis UltraSound driver's timer function, which `HmiMidi_InitDriver` asks the image for and registers at 120 a second.
- **Open:** what stores a `MidiSound`'s far pointer `+0x35`, the branch callback `MidiSound_Play` passes on.
- **Open:** what `0xa003` and `0xa007` stand for, which neither `SOUNDSET.INI` nor `HMIMDRV.386` lists.
- **Open:** [the devices](#the-devices)' routing in the running game. A retail check: with the setup's MIDI device set to MPU 401 under DOSBox-X, `DESERT`'s song is silent and `RUBBLE`'s plays only its `0xa005` track, where FM plays both whole.
- **Open:** the song's pace in the running game. A retail check: `BATTLE1A`'s track 1 loops at tick 10,243, so at 120.05 ticks a second its opening returns 85.3 s after it starts.
