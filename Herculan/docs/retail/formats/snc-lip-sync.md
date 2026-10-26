# `snc\<NAME>.SNC` — portrait lip-sync scripts

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

**`.SNC` is not an audio format.** It is the frame timeline that animates the talking pilot portrait in a comm box while the matching `.wav` plays. `Snc_Load` (`00463270`) reads one into a speech slot, and [playing a script](#playing-a-script) steps through it; the comm box that shows it, and the portrait bank its frames index, are [`heads-down-display.md`](../simulation/heads-down-display.md#squad-comm-boxes).

556 files in `snc\` (in both `SIMVOL0.VOL` and `SIMSOUND.VOL`): twelve speakers `PA`-`PL`, 47 message names, `PA`-`PH` carrying 46 of them and `PI`-`PL` all 47 (`_03001` is theirs alone). **Each speaker has its own scripts:** the twelve copies of a message hold between 6 and 12 distinct scripts, and the four of `_03001` hold 3.

## Layout

After the 9-byte [`.VOL` entry prefix](vol-archive.md#the-per-entry-prefix--fixed-9-bytes):

```
int32  length            -- bytes that follow
length/2 x {
    int8  frame          -- index into the pilot<n>.DBA portrait bank
    int8  delta          -- coarse ticks until the NEXT event
}
```

The `0xff` terminator is **not in the file** — `Snc_Load` (`00463270`) reads the declared length into the slot's 100-byte buffer and appends `0xff` itself. With no script at all the buffer is just `0xff`, and the voice plays with the portrait held.

**Verified across all 556 files**: length always even, always `fileLength - 14`, never containing a `0xff` byte, 2-28 pairs (so at most 57 bytes, terminator included, in the 100-byte buffer). Frame values are 0-23 — matching the 24 talking-head frames at the head of a `pilot<n>.DBA` bank, described in [`heads-down-display.md`](../simulation/heads-down-display.md#the-gauge) — and deltas 2-74 ticks.

## Playing a script

**The portrait steps through the script once, showing each frame for its delta in 16 ms ticks; the line is over when the script runs out, however long the recording lasts.** The comm box starts the script as its face begins to talk and reads the frame on show every frame ([the state machine](../simulation/heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0044b5f8)). The clock is `Time_GetCoarseTicks` (`00467724`), 16 ms a tick. The speech slot's fields the player uses:

| Offset | Contents |
|---|---|
| `+0x00` | the voice handle, −1 when none |
| `+0x04` | the tick the frame on show ends at |
| `+0x08` | `int16`, the frame on show; −1 once the script has ended |
| `+0x0a` | the cursor into the script |
| `+0x2f` | the slot's 100-byte buffer, which `Snc_Load` fills |

- **`Snc_Start` (`004632fc`)**, (slot): points the cursor at the buffer. A buffer that opens with the terminator gets the frame 1 and nothing queued; otherwise the end tick is set to now and `Snc_Advance` runs. Then it returns `Sfx_Play`(`SfxManager`, the handle) when the sound manager exists, `Sound_SpeechEnabled` (`0049f97e`) is set, the frame is not 0 and the handle is not −1, and otherwise 1. So a script that opens on frame 0 plays no recording.
- **`Snc_Advance` (`004633ac`)**, (slot): reads a frame byte and a delta byte at the cursor, both signed, and adds the delta to the end tick, repeating while the end tick is before now, so frames whose time has passed are skipped. A frame byte of `0xff` sets the frame −1 and returns with nothing queued. Otherwise the last frame read goes on show and the slot is queued at its end tick.
- **The queue** holds up to 10 entries {end tick, slot} at `SncEventQueue` (`004d2efa`), the `int16` count at `0049f97c`, and pointers to the entries at `004d2f4c`. Queuing a slot: when the count is above 0 and the delta just read, compared unsigned, is below the end tick of the first pointer's entry, 1 comes off the count and the pointers after the first move down one place. The entry is then stored at the count's index, the first pointer is pointed at it, and 1 goes on the count.
- **`Snc_ServiceQueue` (`004631c0`)**, first in `HddDisplay_ServiceCommBoxes` each frame: scans the pointers from the last to the first for one whose entry's end tick has come; with none it returns. Otherwise it moves that pointer and the ones after it to the front, takes its index + 1 off the count, and runs `Snc_Advance` on the slot the first pointer now names.
- **`Snc_GetFrame` (`00463468`)**, (slot), returns the frame on show.

A comm box starts a script for the pilot channel's current message, of which the port keeps one ([the object](../simulation/cockpit-message-port.md#the-object)), and a script's queue entry is gone once it has ended. With one entry queued the steps come to this: once a frame, when now reaches the end tick, the next frame goes on show and the slot is queued again at its new end tick, until the terminator. Whether two are ever queued at once is [Open](#open).

## Open

- **Open:** whether two scripts are ever queued at once, which takes a second comm box starting while the first still talks: the port taking a line down at its maximum time ([each frame](../simulation/cockpit-message-port.md#each-frame)) before its script ends would let the next message's box start, and `es2_xref` finds a second caller of `Snc_Start`, `Voice_PlayNamed`. With two entries, queuing replaces the first pointer and the service advances one slot of those due, so a script can stop advancing and its box wait in state 2.
