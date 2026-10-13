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

`MidiSound_Play` (`000505c8`, `ES1GO 0004423b`), the `MidiSound` vtable's `+0x10`, returns 0 when the object's slot `+0x30` returns non-zero or the song fails to load (`SoundObject_LoadSample`, which reads the file whole). It builds a block at `+0xbf`, the far pointer to the loaded file followed at `+0xc7` by the far callback `0004e7b4`, and calls `HmiMidi_InitSong` (`0006e4c1`, `ES1GO 0005ec79`) with it, the object's track map `+0x3f` and `+0x19` for the song's handle. An error goes to `+0x04` and is formatted into `SoundObject::playMIDI: %s` with `0006f23d`'s text for it; the function then sets `+0x19` to -1 and `+0x1d` to `0xffff`, and returns 0. Otherwise it calls `0006eeae`(handle, `+0x35`, `+0x39`) when the far pointer `+0x35` is set, then `0006ea6b`(handle), copies the handle to `+0x1d` and `+0x21` to `+0x23`, calls `0006ee87`(handle, far `0004e718`), and returns 1.

`HmiMidi_InitSong` works on the loaded copy:

1. It returns `0xe` when the file does not open `HMIMIDIP`, and `0xb` when all 8 of its song slots are taken.
2. It keeps the file's far pointer and the caller's track map in the slot, and the track count from `+0x30` twice, one copy then counting the tracks still playable.
3. It writes the block's callback into the file's `+0x300`, the offset there and the selector at `+0x304`.
4. It walks the tracks from `+0x308`, each the previous one's length on: for each it keeps a pointer to the chunk and one to its events, moved past the first delta, which it keeps per track.
5. From the branch table it keeps, for each track with entries, a pointer to its first, and turns every entry's `+0x0c` offset into a pointer at its `+0x08`.
6. It routes each track whose map entry is `0xff` to a driver. Of the track's device ids, in order up to a 0, the first that one of the open drivers (up to five, their ids at `000a130c`) answers wins, and the map entry becomes that driver's slot: `0xa000` is answered by a driver of id `0xa000`, `0xa001` or `0xa008`, `0xa002` by `0xa002` or `0xa009`, and any other id by itself. A track whose first id is 0 goes to slot 0. A track no open driver answers loses its events pointer, keeps `0xff`, and leaves one fewer track playable.
7. It returns 0, with the slot through the handle pointer.

`MidiSound_Ctor` sets all 32 map entries to `0xff`, so every track is routed this way. The tracks of the three songs walked list `0xa000`, `0xa002`, `0xa003` and `0xa005`. The simulator opens two MIDI drivers: the MIDI device `Sound_Init` is given, `0xa002` in the image's defaults, with the melodic and drum banks, and `0xa005` with `sos\drum32.dig` ([sound-samples.md](sound-samples.md#the-driver)).

## Open

- **Open:** the header's `+0x024`–`+0x02f`, `+0x034`–`+0x07f` and each track chunk's `+0x08`. In the three songs walked, `+0x034` and `+0x038` are 120, and `+0x03c` is 17, 45 and 76; the dwords from `+0x040` are 0 or 9.
- **Open:** the branch entries' `+0x00`–`+0x07` and `+0x10`–`+0x17`, and what the data they point to does.
- **Open:** the event encoding past the first delta, and what reads the events.
- **Open:** `0006eeae`, `0006ea6b` and `0006ee87`, which `MidiSound_Play` calls after the song is ready, the callbacks `0004e7b4` and `0004e718`, and the `MidiSound` slot `+0x30`.
- **Open:** which hardware each device id stands for, and which drivers `ES1GO 0004240a` opens for the shell.
