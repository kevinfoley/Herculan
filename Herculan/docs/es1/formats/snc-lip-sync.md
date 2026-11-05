# DBSIM.EXE and GO.EXE — lip-sync scripts

**The simulator's talking heads read ES2's lip-sync scripts; the shell's read their own.** `SIMVOL0.VOL`'s 556 `snc\` files are in [ES2's layout](../../retail/formats/snc-lip-sync.md#layout), 548 of them ES2's bytes. `SHELL0.VOL`'s 162 hold 16-bit frames and times behind a 16-bit length, which the shell turns into running totals as it loads them. Addresses are `ES1GO` image addresses unless `ES1DBSIM` is named ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The simulator's scripts

Every one of the 556 files' `int32` length is its size less 4, as in ES2's. Each holds 2 to 28 whole pairs, at most 56 bytes, within the slot's 100; the frames run 0 to 23, the first frames 2 to 23, and the deltas 2 to 74, with no `0xff` among the pairs. ES2's 556 give the same figures.

`Snc_Load` (`ES1DBSIM 0004e4a8`) takes a slot, an object and a name. It builds `snc\<name>` with `ResourcePath_BuildFolderName`, opens it with `VolRStream_CtorFromPath`, reads the `int32` length and that many bytes into the buffer the slot's `+0x27` points to, and appends `0xff`, as ES2's `Snc_Load` (`00463270`) does through `+0x2f`. It stores the object at the slot's `+0`, clearing the object's byte `+0x16` when there is one. ES2's takes a path rather than a name, and with none leaves the buffer the terminator alone; ES1's always opens a file and tests nothing after the open. A file that does not open leaves the stream's status set, and `VolRStream_Read` answers 0 on a set status without reading, so the length is the stack dword as it was, never written; the read into the buffer is the same, and the terminator goes at that length. Nothing bounds the length by the slot's 100 bytes. `es2_xref` finds calls from `SpeechLine_Load` (`ES1DBSIM 0004e0f0`), which loads the line's voice first ([`sound-samples.md`](sound-samples.md#voice-lines)), and `Snc_CtorLoad` (`ES1DBSIM 0004e46c`, [below](#the-speech-slot)), through the padding before the prologue.

### The speech slot

**The simulator holds each voice line in a `0x3a`-byte record, the lip-sync player's `0x2b` bytes first.** `Sound_Init` makes 10 of them, 1 in the low-memory mode, at `000bd734`, the count at `000bd73c`, and 100 bytes for each slot's script at `000bd738` ([the driver](sound-samples.md#the-driver)); a comm box keeps its line's slot at `+0x12f` ([starting a line](../simulation/heads-down-display.md#starting-a-line)). ES2's `Sound_Init` builds five records of `0x42` bytes with `VoiceSlot_ElementCtor` (`00463520`), with the buffer pointer at `+0x2f` ([playing a script](../../retail/formats/snc-lip-sync.md#playing-a-script)).

| Offset | Contents | Written by |
|---|---|---|
| `+0x00` | the line's sound object, or 0 | `Snc_Load` |
| `+0x04` | the count of the lip-sync clock at which the frame on show ends | `Snc_Start`, `Snc_Step` |
| `+0x08` | `int16`, the frame on show; −1 once the script has ended | `Snc_Start`, `Snc_Step` |
| `+0x0a` | the cursor into the script | `Snc_Start`, `Snc_ReadFrame`, `Snc_ReadDueTime` |
| `+0x0e` | the voice's path, `simvoice\<name>.sfx`, which `SoundManager_SetSampleName` lower-cases in place ([naming and loading](sound-samples.md#naming-and-loading)) | `SpeechLine_Load` |
| `+0x27` | the script's buffer: the slot's 100 bytes of `000bd738` | `Voice_EvictLru` |
| `+0x2b` | the key `SpeechLine_Load` finds the voice's name by, `Voice_KeyOfName` (`ES1DBSIM 0004e208`, [voice lines](sound-samples.md#voice-lines)) | `SpeechLine_Load` |
| `+0x2f` | the clock `000981c4` at the slot's last use | `SpeechLine_Load` |
| `+0x33` | byte, 1 once the slot is taken for a voice | `SpeechLine_Load` |
| `+0x34`, `+0x36` | the sound manager's name index for the voice, and the sound object on it | `SoundCatalog_Load` |

- **`VoiceSlot_ElementCtor` (`ES1DBSIM 0004dc40`)**, (slot), the element constructor `Sound_Init` passes `__vec_new` by its padding `0004dc3e`: allocates `0x3a` bytes for a null slot, returning 0 when that fails, runs `Snc_Ctor`, and clears `+0x33` and `+0x2f`. ES2's (`00463520`) allocates nothing.
- **`Snc_Ctor` (`ES1DBSIM 0004e444`)**, (slot): allocates `0x2b` bytes for a null slot, returning 0 when that fails, and clears `+0x27` and `+0`. ES2's `Snc_ClearSlot` (`00463238`) allocates nothing.

Neither writes the key `+0x2b`, so until `SpeechLine_Load` takes a slot its key is what `__vec_new`'s block held there, and a voice name whose key matched it would find that slot with its script buffer `+0x27` still 0 ([Open](#open)).
- **`Snc_CtorLoad` (`ES1DBSIM 0004e46c`)**, (slot, object, name): allocates `0x2b` bytes for a null slot, returning 0 when that fails, and passes its three arguments to `Snc_Load` unchanged, so the name it loads is its caller's; it returns the slot. ES2's `maybe_Snc_Ctor` (`0046324c`) allocates nothing. `es2_xref` finds no reference to either ([Open](#open)).
- **`Voice_EvictLru` (`ES1DBSIM 0004e08c`)**, no arguments, from `SpeechLine_Load` when no slot holds the voice: the first slot with the lowest `+0x2f`, its `+0x27` pointed at its own 100 bytes, the slot's index × 100 into `000bd738`. ES2's (`00462a2c`) also closes the voice the slot held.

### Playing a line

**The face steps through its script on a clock of the player's own, which the PC timer advances 60 times a second, each frame staying up for its delta; the line ends when the script runs out, whatever the recording does.** ES2's player ([playing a script](../../retail/formats/snc-lip-sync.md#playing-a-script)) is polled once a frame on its 16 ms tick and skips frames whose time has passed. ES1's steps the script inside the timer interrupt, one pair each time its count comes up, so the same deltas last 4% longer (16.66 ms a count) and no frame is skipped.

- **`Snc_MissionInit` (`ES1DBSIM 0004e498`)**, from `Sim_StartMission` after `Sound_Init` ([the mission start](../simulation/main-loop.md#the-mission-start--sim_startmission-0005a9dc), step 3), where ES2's `Sim_InitMissionSessionNoOp` (`0046326c`) is empty: unless the byte `Snc_SkipTimerInstall` (`000959e0`) is set, runs `Snc_InstallTimer` (`0004e434`), which registers `Snc_TimerTick` with `Timer_AddCallback` at a count of 2, every second interrupt of the 120 Hz timer the mission start's step 2 built ([callbacks](../simulation/timer.md#callbacks)). The byte is 0 in the image ([Open](#open)).
- **`Snc_TimerTick` (`ES1DBSIM 0004e39c`)**, the callback: adds 1 to the dword `Snc_Clock` (`000959dc`), 0 in the image. Then it walks the queue's pointers from the first. At an entry due at the clock's count it takes 1 off the queue's count, steps the entry's slot with `Snc_Step` and returns; at an entry due at an earlier count it moves the pointers after it down one place, taking 1 off the count for each pointer it moves, and returns without stepping; an entry due later it passes over. ES2's `Snc_ServiceQueue` (`004631c0`) in its place.
- **`Snc_Start` (`ES1DBSIM 0004e518`)**, (slot), from `HddDisplay_ServiceCommBoxes` as the face begins to talk ([the state machine](../simulation/heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0003e574)): points the cursor `+0xa` at the buffer `+0x27`, sets the frame `+8` from `Snc_ReadFrame` and the due count `+4` from `Snc_ReadDueTime`, and queues the slot at that count with `Snc_Enqueue`. Then, when the slot has a sound object, `Sound_SpeechEnabled` (`000959e4`) is set and the frame is not 0, it returns what the object's play slot `+0x10` returns ([playing](sound-samples.md#playing)), and otherwise 1; the state machine ends the line at once on 0. It calls the play slot itself rather than `Sound_Play`, so SOUNDS, which `Sound_Play` tests ([turning songs and sounds off](sound-samples.md#turning-songs-and-sounds-off)), does not switch a voice: with SOUNDS off and RADIO on, the voice plays. No script of the 556 opens on frame 0 ([the simulator's scripts](#the-simulators-scripts)), so with speech on every line's voice plays. ES2's also requires a voice handle other than −1, and gives a script that opens with the terminator the frame 1 and nothing queued. ES1's would read such a script's terminator as the frame −1 and the byte after it as the first delta, and queue it; each of the 556 has at least two pairs.
- **`Snc_Step` (`ES1DBSIM 0004e5f8`)**, (slot), which the tick enters by its padding `0004e5f6`: the frame `+8` from `Snc_ReadFrame`; unless that is −1, the due count `+4` from `Snc_ReadDueTime` and `Snc_Enqueue`. The terminator, read in a frame's place, leaves the frame −1 with nothing queued, which is the line's end. ES2's `Snc_Advance` (`004633ac`).
- **`Snc_ReadFrame` (`ES1DBSIM 0004e59c`)**, (slot), which `Snc_Start` enters by its padding `0004e59a`: the byte at the cursor, unsigned, moving the cursor on by one; `0xff` gives −1, as `0xffff` in `AX`.
- **`Snc_ReadDueTime` (`ES1DBSIM 0004e5c4`)**, (slot): the byte at the cursor, unsigned, plus `Snc_Clock`, moving the cursor on by one; `0xff` gives −1.
- **`Snc_Enqueue` (`ES1DBSIM 0004e624`)**, (slot, due count), on the queue: 10 entries {due count, slot} at `SncQueue_Entries` (`000b1e54`), the `int16` count `SncQueue_Count` (`000959e2`), and pointers to the entries at `SncQueue_Order` (`000b1ea4`). When the count is above 0 and the first pointer's entry is due after the new one, compared unsigned, it takes 1 off the count and moves the pointers after the first down one place; then it stores the entry at the count's index, points the first pointer at it and adds 1 to the count. ES2's compares the pair's delta with the entry's due tick, where ES1's compares the due counts.
- **`Snc_IsVoicePlaying` (`ES1DBSIM 0004e578`)**, (slot): what the sound object's slot `+0x30` returns, which for the voice's `DigiSound` is 1 while its voice is busy ([the vtables](sound-samples.md#the-vtables)), or 0 with no object; ES2's `SfxHandle_IsPlaying` (`00463360`). `es2_xref` finds no reference to either ([Open](#open)).

None of these reads the recording once it has started: the script alone times the face. With one line at a time, the pilot channel's current message ([the lifecycle](../simulation/cockpit-messages.md#the-lifecycle)), the queue is empty when a step queues the next frame, since the tick took 1 off the count first: a pair of delta *d* keeps its frame up for *d* counts, *d* / 60.02 s, and the terminator ends the line. `Snc_Enqueue` writes only the first pointer, `SncQueue_Order`[0]; the pointers from [1] on are written by the two shift loops alone, each copying down the pointer above it, so they keep the 0 the start-up's fill of `_BSS` gives them ([startup](compiler.md#startup)).

**A delta of 0 stops that face's script, and the queue then runs one place off.** Such a pair queues an entry due at the count just reached, with the count 0, so the count becomes 1; every later tick finds the entry overdue with no pointer after it to move, and returns. That face keeps its frame and its box stays in state 2 ([the state machine](../simulation/heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0003e574)) until a later line of the same squadmate begins. The next `Snc_Enqueue`, finding the count 1 and the overdue entry not due after its own, stores its entry at index 1, points [0] at it and makes the count 2, with [1] still 0. In each tick while [0]'s entry is not due, the walk reaches [1] and reads the entry at image address 0: the code's first eight bytes, a due count of `0xbaec8b55`, past any count the clock reaches, so the tick passes it, and the line plays. At the line's end the count is 1 with [0] overdue, as before, so each later line plays with the count 2. A delta of 0 while the count is 2 makes the tick move [1], the 0, into [0], losing that line's entry too; the next `Snc_Enqueue` then finds [0]'s due count `0xbaec8b55` after its own, drops it, and the queue is empty again. None of the 556 scripts holds a delta of 0 ([the simulator's scripts](#the-simulators-scripts)), so retail's data never reaches this. Whether two lines ever play at once is [Open](../../retail/formats/snc-lip-sync.md#open) for both games.

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

The shell plays these with the message box's talking head: the player, how a line is loaded and how it plays are [the talking head](../shell/message-box.md#the-talking-head)'s.

## Open

- **Open:** what calls `Snc_CtorLoad` (`ES1DBSIM 0004e46c`): `es2_xref` finds no branch or stored pointer to it or its padding, where it finds `Snc_Load`'s two calls; ES2's `maybe_Snc_Ctor` has none either.
- **Open:** a writer of `Snc_SkipTimerInstall` (`ES1DBSIM 000959e0`): `es2_xref --decode` finds `Snc_MissionInit`'s read and no other access.
- **Open:** what a voice-line slot's key `+0x2b` holds before `SpeechLine_Load` first takes the slot, which neither constructor writes ([the speech slot](#the-speech-slot)): what the block `Sound_Init` (`ES1DBSIM 0004db14`) gets from `__vec_new` holds there. A key of 0 cannot match a line's voice name, whose bank digit is 1 or more ([voice lines](sound-samples.md#voice-lines)).
- **Open:** what calls `Snc_IsVoicePlaying` (`ES1DBSIM 0004e578`): `es2_xref` finds no branch or stored pointer to it or its padding.
- **Open:** a retail check of [playing a line](#playing-a-line), by ear: with RADIO on `TEXT / VOICE` and SOUNDS off, a squadmate's line in the comm box is heard while its face talks.
