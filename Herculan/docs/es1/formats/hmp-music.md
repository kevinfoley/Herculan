# DBSIM.EXE and GO.EXE — music

**ES1's music is MIDI, in HMI's `.HMP` song format, played through the MIDI drivers the sound setup names.** ES2 plays its music from the CD and ships no song ([ES2's sound backend](../../retail/simulation/audio.md#hmi-sos)). A song is loaded whole by the sound manager that loads the [digital sounds](sound-samples.md), and the HMI library linked into both programs reads it in place: [the song player](../sound/song-player.md). This page is the file's layout. Addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)).

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

A data byte may have its top bit set: the branch ids and loop counts of [the player's controllers](../sound/song-player.md#the-branch-controllers) are `0x80` and `0xff`. The 19 files hold only notes (`0x9n`, 44,074), controllers (20,332), program changes (2,103), pitch bends (27,070) and the 241 ends of track; a note ends with a `0x9n` of velocity 0.

## Open

- **Open:** the header's `+0x024`–`+0x02f`, `+0x034` and `+0x03c`, and a branch entry's last 8 bytes, which the code read does not read.
- **Open:** whether every branch entry lands just past its track's `0x6c` event, and where the entries of the 19 tracks with no such event land.
