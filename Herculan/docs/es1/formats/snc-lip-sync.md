# DBSIM.EXE and GO.EXE — lip-sync scripts

**The simulator's talking heads read ES2's lip-sync scripts; the shell's read their own.** `SIMVOL0.VOL`'s 556 `snc\` files are in [ES2's layout](../../retail/formats/snc-lip-sync.md#layout), 548 of them ES2's bytes. `SHELL0.VOL`'s 162 hold 16-bit frames and times behind a 16-bit length, which the shell turns into running totals as it loads them. Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The simulator's scripts

Every one of the 556 files' `int32` length is its size less 4, as in ES2's.

`Snc_Load` (`ES1DBSIM 0004e4a8`) takes a slot, an object and a name. It builds `snc\<name>` with `ResourcePath_BuildFolderName`, opens it with `VolRStream_CtorFromPath`, reads the `int32` length and that many bytes into the buffer the slot's `+0x27` points to, and appends `0xff`, as ES2's `Snc_Load` (`00463270`) does through `+0x2f`. It stores the object at the slot's `+0`, clearing the object's byte `+0x16` when there is one. ES2's takes a path rather than a name, and with none leaves the buffer the terminator alone; ES1's always opens a file. `es2_xref` finds calls from `SpeechLine_Load` (`ES1DBSIM 0004e0f0`), which loads the line's voice first ([`sound-samples.md`](sound-samples.md#voice-lines)), and `ES1DBSIM 0004e46c`, through the padding before the prologue.

## The shell's scripts

`SHELL0.VOL`'s `snc\` holds 16 scripts each for `C0` to `C3` (`C0_01000.SNC` …), 56 for `G0` and 42 for `T1`, the names the [lip-sync lists](shell-file-lists.md#the-lip-sync-lists) give each line of a talking head's text.

```
int16  length            -- bytes that follow, a multiple of 4
length/4 x {
    int16 frame          -- what the line's .TTM script is handed
    int16 delta          -- timer counts until the next record
}
```

In all 162 the length is the file's size less 2 and a multiple of 4. ES2's reader refuses them: their first four bytes, read as ES2's `int32` length, run past the end.

### The player

The player that `maybe_SncList_Load` fills holds one script and its sound:

| Offset | Contents |
|---|---|
| `+0x00` | `int16` timer slot, from `Timer_AddCallback` |
| `+0x02` | byte, the `.TTM` script's handle, or -1 |
| `+0x03` | the line's sound object |
| `+0x07` | the script's records |
| `+0x0b` | `int16` record count |
| `+0x0d` | `int16` index of the current record |
| `+0x0f` | `int16` line count |
| `+0x11` | the lines' name lists, 6 bytes each |
| `+0x15` | the object the `.TTM` script is opened on |
| `+0x19` | byte, set while a line plays |
| `+0x1a` | byte, a request to stop |

`maybe_SncList_Init` (`00045337`), called from `Shell_Main`, makes the `+0x15` object with `000512a0`(0) and sets its dwords `+0x210`, `+0x214`, `+0x218` and `+0x21c` to 0, 0, 319 and 199, `+0x208` to 1 and `+0x20c` to 0, then registers `maybe_SncList_TimerTick` (`00045320`) with `Timer_AddCallback` at count 2 and keeps the slot. The tick adds 1 to the word `00082658`, the line's clock, while the byte `0009afed` is set. The clock's rate: [Open](#open).

### Loading a line

`maybe_SncList_PlayEntry` passes a line's first name to `ShellSnc_Load` (`0004589c`), with the script at `+0x03`. It opens `SNC\<name>`, reads the `int16` length, makes `length / 4` records for `+0x0b`, reads `length` bytes into them and closes the stream. It then adds each record's delta to the one after it, from the second record to the last, so each holds the clock reading at which it ends, and sets the index to 0. It appends no terminator.

`maybe_SncList_StartSound` (`0004544a`) starts a line's sound once: when the handle is not -1 and neither `+0x19` nor `+0x1a` is set, it sets `+0x19`, opens the line's second name, its `.RAW`, with `ShellSnc_OpenSound` (`00045801`), starts it through the sound's slot `+0x10`, `DigiSound_Play` ([`sound-samples.md`](sound-samples.md#playing)), when the dword `0007fcbc` is above 0, and sets the clock to 0. `ShellSnc_OpenSound` does nothing while `0007fcbc` is 0; otherwise it builds `snc\<name>` and, when the sound manager `00082768` exists, calls the slot `+0x30` of the manager's object slot 0 (`SoundManager_GetObject`, `00042b81`) until it returns 0 and names the file as the manager's name index 0 with `SoundManager_SetSampleName` (`00042f39`), which takes a `.RAW` as a digital sound. When that succeeds it stores `SoundManager_CreateObject` (`000429db`)(manager, 2, 0, 0), a sound on that index, and loads it with `SoundObject_LoadSample` (`00043d8a`); when it fails it stores 0 ([the shell's names](sound-samples.md#the-shells-names)).

### Playing it

`maybe_SncList_Tick` (`000454c4`) stops the line with `maybe_SncList_Stop` when `+0x1a` is set, and while `+0x19` is set advances the script and shows its frame. `ShellSnc_Advance` (`0004597b`) does nothing with no records or while the clock is below the current record's total; otherwise it steps the index past every record whose total the clock has reached, and on reaching the count sets the byte `0009afee` to 1. A record therefore shows for its own delta, as in ES2's scripts. The tick takes the current record's frame, the last record's once the index reaches the count, or -1 with no records, passes it with the handle to `000468fc`, and calls `00046948`(0, handle); what the script does with it is [Open](shell-file-lists.md#open). The last frame stays on after the script ends.

`ShellSnc_Release` (`00045a04`), from `maybe_SncList_Stop` and `maybe_SncList_Dtor` (`00045758`), frees the records and zeroes the count, then stops and destroys the sound through its slots `+0x14` and `+0x08`(3), `DigiSound_Stop` and `DigiSound_Dtor`, and clears it. `maybe_SncList_Dtor` also deletes the lines' name lists, passes the timer slot to `000565b7`, destroys the `+0x15` object through its slot `+0x1c`(3), and frees the player when its flags' bit 0 is set.

## Open

- **Open:** how often the timer calls a slot registered at count 2, and so the clock's rate ([the timer's Open items](../simulation/debug-monitor.md#open)).
- **Open:** what sets and clears `0009afed`, which gates the clock, and what reads `0009afee`.
- **Open:** how `SpeechLine_Load`'s caller, `ES1DBSIM 0003e060`, and `ES1DBSIM 0004e46c` choose the simulator's script name.
