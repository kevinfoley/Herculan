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
+0x024  12         (not read by the loader)
+0x030  int32      track count
+0x034  3 x int32  (not read by the loader)
+0x040  16 x int32 (not read by the loader)
+0x080  32 x 5 x int32   per track, the device ids it can play on, a 0 ending the list
+0x300  8          zero; the loader writes the caller's callback here
+0x308  the tracks, back to back:
          +0x00  int32  chunk number
          +0x04  int32  length, these 12 bytes included
          +0x08  int32  (not read by the loader)
          +0x0c         the events, length - 12 bytes
the branch table, at +0x020's offset:
          track count x byte   how many entries each track has
          24-byte entries, each track's in turn:
            +0x00  8      (not read by the loader)
            +0x08  int32  the slot the loader writes the entry's data pointer into
            +0x0c  int32  offset from the file's start of the entry's data
            +0x10  8      (not read by the loader)
          the entries' data, to the end of the file
```

In all 19 files the 24 bytes after the signature and the 8 at `+0x300` are zero, the tracks are numbered from 0 in order and end where the branch table starts, each entry's `+0x08` holds the same offset as its `+0x0c`, and every offset points into the data after the entries, except one entry of `SHELLC.HMP`'s, which points at the file's end.

### Events

The loader reads only each track's first delta time, with `HmiMidi_ReadDelta` (`0006ed60`, `ES1GO 0005f518`): seven bits a byte, the first byte the lowest seven, ending at the byte whose top bit is set. That is the reverse, in byte order and in its end marker, of a standard MIDI file's variable-length number. In `INTRO1` and `BATTLE3B` every track ends `80 ff 2f 00`: a zero delta and MIDI's end-of-track event.

## Loading and playing

`MidiSound_Play` (`000505c8`, `ES1GO 0004423b`), the `MidiSound` vtable's `+0x10`, returns 0 when the object's slot `+0x30` returns non-zero or the song fails to load (`SoundObject_LoadSample`, which reads the file whole). It builds a block at `+0xbf`, the far pointer to the loaded file followed at `+0xc7` by the far callback `MidiSound_EndCallback` (`0004e7b4`), and calls `HmiMidi_InitSong` (`0006e4c1`, `ES1GO 0005ec79`) with it, the object's track map `+0x3f` and `+0x19` for the song's handle. An error goes to `+0x04` and is formatted into `SoundObject::playMIDI: %s` with `0006f23d`'s text for it; the function then sets `+0x19` to -1 and `+0x1d` to `0xffff`, and returns 0. Otherwise it calls `0006eeae`(handle, `+0x35`, `+0x39`) when the far pointer `+0x35` is set, then `0006ea6b`(handle), copies the handle to `+0x1d` and `+0x21` to `+0x23`, calls `0006ee87`(handle, far `MidiSound_LoopCallback` (`0004e718`)), and returns 1. Of the two callbacks ([the driver's calls back](sound-samples.md#the-vtables)), `MidiSound_EndCallback` stops the song, and `MidiSound_LoopCallback` counts down its repeats, 255 for each of the catalog's battle songs.

`HmiMidi_InitSong` works on the loaded copy:

1. It returns `0xe` when the file does not open `HMIMIDIP`, and `0xb` when all 8 of its song slots are taken.
2. It keeps the file's far pointer and the caller's track map in the slot, and the track count from `+0x30` twice, one copy then counting the tracks still playable.
3. It writes the block's callback into the file's `+0x300`, the offset there and the selector at `+0x304`.
4. It walks the tracks from `+0x308`, each the previous one's length on: for each it keeps a pointer to the chunk and one to its events, moved past the first delta, which it keeps per track.
5. From the branch table it keeps, for each track with entries, a pointer to its first, and turns every entry's `+0x0c` offset into a pointer at its `+0x08`.
6. It routes each track whose map entry is `0xff` to a driver. Of the track's device ids, in order up to a 0, the first that one of the open drivers (up to five, their ids at `000a130c`) answers wins, and the map entry becomes that driver's slot: `0xa000` is answered by a driver of id `0xa000`, `0xa001` or `0xa008`, `0xa002` by `0xa002` or `0xa009`, and any other id by itself. A track whose first id is 0 goes to slot 0. A track no open driver answers loses its events pointer, keeps `0xff`, and leaves one fewer track playable.
7. It returns 0, with the slot through the handle pointer.

`MidiSound_Ctor` sets all 32 map entries to `0xff`, so every track is routed this way. Each program opens two MIDI drivers, the shell through its copy of `SoundManager_LoadDrivers` (`ES1GO 0004240a`, [the copies](sound-samples.md#the-copies)): the MIDI device `resource.cfg`'s `[Music]` `Driver#` names, `0xa002` in the images' defaults, with the melodic and drum banks, and `0xa005` with `sos\drum32.dig` ([sound-samples.md](sound-samples.md#the-driver)).

## The devices

**The songs are written for the FM driver: what a song plays depends on the MIDI device the setup chose.** `resource.cfg`'s `[Music]` `Driver#` ([sound-samples.md](sound-samples.md#the-driver)) holds one of the MIDI devices the setup program `SOUNDSET.EXE` lists in its `SOUNDSET.INI`:

| Id | `SOUNDSET.INI`'s name | Answers a track listing |
|---|---|---|
| `0xa000` | Sound Master II | `0xa000` |
| `0xa001` | MPU 401 | `0xa000` |
| `0xa002` | FM | `0xa002` |
| `0xa004` | MT-32 | `0xa004` |
| `0xa008` | AWE 32 | `0xa000` |

The drivers themselves are [`SOS\HMIMDRV.386`'s images](hmi-driver-container.md#layout), which also hold a driver for `0xa009` (OPL3, answering a track that lists `0xa002`), `0xa00a` (Gravis UltraSound) and `0xa006` (the PC speaker). `0xa005`, which `SoundManager_LoadDrivers` opens with `sos\drum32.dig` and the digital driver's blocks, is none of them, and neither is `0xa003`: `HmiMidi_InitDriver` (`00071076`) takes those two devices' functions from tables in the program itself, and loads any other device's image with `00081365` unless its caller's block already holds one.

Track 0 of each of the 19 songs lists no device and goes to slot 0. Of the others, every melodic track lists `0xa002` first, `0xa000` after it or not at all, except `ICE`'s tracks 13–15, `INTRO1`'s 10–14 and `INTRO2`'s 14–17, which list `0xa000` first (`INTRO2`'s 17 then `0xa00a`, `0xa002` and `0xa007`); the rest list `0xa005` alone, and most songs have one track listing `0xa003` alone. `DESERT`'s eleven tracks and every melodic track of `RUBBLE`, `VOLCANO`, `WIN2` and `LOSING` list `0xa002` alone. No track lists `0xa004`. So by step 6 of `HmiMidi_InitSong`, with `0xa005` always open beside the setup's device:

- the FM setup plays every track but the `0xa003` ones and the ones listing `0xa000` alone;
- a setup whose device answers `0xa000` drops every track listing `0xa002` alone, so `DESERT` plays nothing and `RUBBLE`, `VOLCANO`, `WIN2` and `LOSING` play only their `0xa005` tracks;
- the MT-32 setup plays only the `0xa005` tracks.

## Open

- **Open:** the header's `+0x024`–`+0x02f`, `+0x034`–`+0x07f` and each track chunk's `+0x08`. In the three songs walked, `+0x034` and `+0x038` are 120, and `+0x03c` is 17, 45 and 76; the dwords from `+0x040` are 0 or 9.
- **Open:** the branch entries' `+0x00`–`+0x07` and `+0x10`–`+0x17`, and what the data they point to does.
- **Open:** the event encoding past the first delta, and what reads the events.
- **Open:** `0006eeae`, `0006ea6b` and `0006ee87`, which `MidiSound_Play` calls after the song is ready, and when the song player calls the two callbacks.
- **Open:** what `0xa003` and `0xa007` stand for, which neither `SOUNDSET.INI` nor `HMIMDRV.386` lists.
- **Open:** `00081365`, which pushes the name `hmimdrv.386`: how it finds a device's image in the file and where it puts it.
- **Open:** [the devices](#the-devices)' routing in the running game. A retail check: with the setup's MIDI device set to MPU 401 under DOSBox-X, `DESERT`'s song is silent and `RUBBLE`'s plays only its `0xa005` track, where FM plays both whole.
