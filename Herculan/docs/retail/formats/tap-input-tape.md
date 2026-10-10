# `.TAP` input tapes

DBSIM can record a whole mission's input to a file and play it back frame for frame. A tape carries both halves of what that needs: the starting state, as a bundle of the seven files the mission runs from, and then one record per frame of everything the player did. Three finished tapes ship in `ES2/TAPES/`.

The main menu's `VIEW DEMO` plays one — see [Reaching it](#reaching-it). The format itself is settled: a parser written from the code below consumes all three retail tapes to their final byte.

## The switches

`Sim_ParseCommandLine` (`0045e73c`) is DBSIM's own argument parser. Four of its cases drive the tape:

| Switch | Effect |
|---|---|
| `-r<name>` | Record to `<name>.tap`. Writes the bundle, closes the file, sets the recording flag `004d255c` |
| `-p<name>` | Play `<name>.tap`. Unpacks the bundle over the live data files, sets the playback flag `004d255a` |
| `-D` | Play a tape chosen at random by `DemoTape_PickRandom`, and additionally set `004d25b4` — demo mode |
| `-d` | Open the checkpoint file `<name>.dmp` beside the tape — see [The checkpoint file](#the-checkpoint-file) |

The extension is forced to `tap` in the first three cases, so `<name>` is a bare stem: a tape in `tapes\` is named as `tapes\demo1`. `-d` takes no name of its own: it reads the stem and the recording flag the others left, so it only works after `-r`, `-p` or `-D` on the line.

`DemoTape_PickRandom` (`0045ce9c`) loads group `0x14` of `tapes\demolist.str` and returns entry `time() % count`, or entry 0 when the table holds one name. The retail table holds `DEMO1`, `DEMO2`, `DEMO3`.

Demo mode is playback plus an abort: with `004d25b4` set, any key the player presses that makes a command code, the first axis key (`Input_KeyjoyAxisKey`'s opening test) or closing the window (`WM_CLOSE` in the window procedure) raises `004d25b6`, as does the tape running out ([below](#where-it-runs)); a `WM_QUIT` reaching the input build raises it in any mission, demo or not. The flag is what the mission loop and `StatusAlertPanel_RunModal` read to leave the mission. That is attract-mode behaviour — run until somebody touches something. The command-code test reads the live keyboard's command word, which `Input_PollDeviceBlock` (`0045ba8c`) builds at `004d247a` each frame, not the tape's — see [Rejected readings](#rejected-readings).

## File layout

### The bundle

Seven length-prefixed blocks, written once when `-r` opens the tape and unpacked by `-p` before the mission starts. `Tape_PackFile` (`0045cc88`) writes a `uint32` size then the bytes in 500-byte chunks; a source file it cannot open writes size 0, which is why `object.str` is empty in every retail tape. `Tape_UnpackFile` (`0045cd40`) is the inverse and tolerates a null destination.

| # | Recorded from | Unpacked to | `DEMO1` | `DEMO2` | `DEMO3` |
|---|---|---|---|---|---|
| 0 | `data\script.dat` | the same | 13520 | 13520 | 13520 |
| 1 | `data\player.mec` | the same | 151 | 495 | 143 |
| 2 | `data\mission.var` | the same | 2000 | 2000 | 2000 |
| 3 | `data\prefs.cfg` | `tapes\prefs.cfg` | 54 | 54 | 54 |
| 4 | `data\restore.dat` | the same | 42 | 42 | 42 |
| 5 | `data\object.str` | the same | 0 | 0 | 0 |
| 6 | `data\keyjoy.cfg` | `tapes\keyjoy.cfg` | 518 | 518 | 518 |

The two configuration files change folder because `Config_BuildPath` (`0045eea4`), which builds both of their paths, prefixes `data\` normally and `tapes\` once `004d255a` is set. The other five go over the live `data\` copies.

The three retail tapes are three different missions — `script.dat` byte 0, the theater, reads 1, 2 and 4 — and all three were recorded at pilot skill 3, the value at `script.dat +0x0e` ([`../simulation/difficulty.md`](../simulation/difficulty.md)).

**A replay is not fully determined by the tape.** Three things come from the playing machine's install:

- **Seven preference bytes.** `-p` reads `data\prefs.cfg` and `tapes\prefs.cfg`, copies bytes 0-3 and 8-10 of the former over the latter — sound, music, the two message modes, TERRAIN TEXTURE, HERC DETAIL and STRUCTURE DETAIL ([`../simulation/preferences.md`](../simulation/preferences.md#what-each-byte-is)) — and writes `tapes\prefs.cfg` back. EFFECTS DETAIL, byte 11, is not among them, so the collapse smoke and debris bursts it decides ([`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#effects-detail)) replay at the recording's setting. It also stores that path in the preferences path `Prefs_Path` (`0049e844`), which the loader otherwise fills with `data\prefs.cfg`, so the simulator runs from the reconciled copy.
- **The keyjoy switches.** `Keyjoy_LoadConfig` (`0045b78c`) always reads `data\keyjoy.cfg`, so the tape's own copy in `tapes\` is never read. Its `Backturn` applies to the replayed axes: it is applied after the point where a frame is recorded, and playback runs that code too.
- **Everything the bundle does not carry**, which is whatever `data\` already holds — the mission's text in `mission.str` among it.

When a playback mission ends with `004d255a` still set — `-D` aborted, or the mission left before the tape ran out — the teardown in `Sim_Run` (`0045f144`) deletes `tapes\prefs.cfg` and `tapes\keyjoy.cfg` (`OpenFile` with `OF_DELETE`). A tape played to its end leaves them.

### The stream

Immediately after the bundle, once, comes the eight-byte joystick capability block `Input_QueryCapabilities` returns ([`../simulation/joystick-input.md`](../simulation/joystick-input.md#the-capability-block--input_querycapabilities-004777f8)) — so a replay knows what device the recording was made on. All three retail tapes carry `01 00 04 00 00 01 10 00`: four buttons, a rudder, a hat and no throttle. Playback reads the block into `Input_QueryCapabilities`' own buffer, which that function rebuilds from the live device on its next call.

Then one 24-byte header per frame, plus its variable tail:

| Offset | Type | Contents |
|---|---|---|
| `+0x00` | int16 | The frame's command word — the head of the player input block at `PlayerInputBlock` (`004d234a`) |
| `+0x02` | int16 x4 | The four axis values: `004d2358`, `004d235a`, `004d235c`, `004d235e` |
| `+0x0a` | int16 | `SimTickDelta`. Playback puts it in the input block's `+0x2e` (`004d2378`), where `Sim_MainTick` picks it up |
| `+0x0c` | byte | Bits 0-3 buttons 1-4 (`004d2360`-`004d2363`); bits 4-7 the four hat bytes (`004d2368`-`004d236b`), north, south, west, east |
| `+0x0d` | byte | The stick's own eight buttons, before the bindings: the live device block's `+0x1d`..`+0x24` (`004d2497`-`004d249e`) |
| `+0x0e` | byte | Bit 0 the trigger, `004d2357` ([`../simulation/weapon-firing.md`](../simulation/weapon-firing.md)) |
| `+0x0f` | byte | Zero: the writer builds the four bytes as one `uint32` with nothing above bit 16 |
| `+0x10` | uint32 | Mouse-event count |
| `+0x14` | uint32 | Command count |
| `+0x18` | 14 x n | The mouse events, in `CockpitMouseQueue_Push`'s own record layout ([`../simulation/cockpit-input.md`](../simulation/cockpit-input.md#3-the-cockpits-one-listener-queues-it-doesnt-act)) |
| — | int16 x n | The command codes, in the layout of the queue at `004d2148` ([`../simulation/cockpit-input.md`](../simulation/cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes)) |

**Buttons 5-8 are not recorded.** Only `004d2360`-`004d2363` reach the header, and playback's `memset` of the input block leaves the other four zero. The four bits are written after the press-once latch has masked the build ([`../simulation/joystick-input.md`](../simulation/joystick-input.md#the-buttons)), so each is set on the one frame its action fires, and the trigger's own button is zero because the trigger is extracted from it. The hat bytes are zero under HAT = 2, which writes the hat onto the turret axes and clears them before they are recorded.

**Nor is the block's pointer.** Its `+0x02`..`+0x0c` — the pointer position and the mouse buttons the live build copies in — are on no record, and playback leaves them zero. The [gunsight drag](../simulation/joystick-input.md#the-gunsight-drag) reads them, so a replay never arms it.

The raw bank at `+0x0d` is read back into locals that only the recording branch reads. In the retail tapes its bit 0, the stick's trigger button, is set on 180, 116 and 185 frames, every one of them a frame whose trigger bit is set; the trigger bit is set on 180, 200 and 204.

The command word is the one key event the build took off the keyboard ring, releases included: a press and its `0x80` release sit in two different frames, and a held key's auto-repeat arrives as a run of presses with one release at the end. The command queue carries presses of its seven keys only, their releases reaching the ring and so the command word. The three retail tapes run 1499, 1901 and 3660 frames. The only codes in their queues are `0x0c`, `0x0d` and `0x1b` — `-`, `=` and `]`, three of the seven scancodes on `SimCommandWantedCodes`.

## Where it runs

Both directions live in `Input_BuildPlayerDevice` (`0045a7f4`), the per-frame input build, around the point where the live device would otherwise be read.

**Recording** reopens the tape in append mode every frame, writes the capability block if it has not yet, writes the header and the two arrays, and closes the file again. A failed open reports through the error logger as `APPINPUT.CPP:834` rather than stopping the mission. The mouse events it writes are the front buffer `CockpitMouse_ProcessQueue` returns — which is the only reason that function returns anything.

**Playback** reads the capability block once, then the header, then pushes the frame's mouse events into the cockpit queue through `CockpitMouseQueue_Push` and its command codes into `004d2148`, and unpacks the button bits back into the same globals the live path would have written. The rest of the frame is then ordinary: the command queue drains through `Sim_DispatchCommand`, the mouse queue through `CockpitMouse_ProcessQueue`.

**Live mouse input is shut off while a tape plays.** `CockpitMouse_OnEvent` queues nothing unless `004d1e5a` is set, and that byte is clear for the duration, so the tape's recorded events are the only ones the cockpit sees. The end of the tape sets it. A mouse event's position is in the game's screen space: `Mouse_DispatchEvent` (`0048083c`) scales a client position by `(backBufferWidth << 15) / clientWidth` through a Q16 multiply, which is half the back buffer and so the viewport — 640x480, or 320x240 in the low-resolution mode.

**A modal panel reads the tape too.** `Sim_MainTick` is not the only caller of `Input_BuildPlayerDevice`: all four alert-panel modal loops call it once per pass — `StatusAlertPanel_RunModal` (`00455fe4`, which the pause panel runs too), `ObjectivesPanel_RunModal` (`00457ae4`), `PreferencesPanel_Run` (`00456d4c`) and `ControlsPanel_Run` (`00458650`) — and so does `AlertPanel_SetFocus` (`00454c7c`) when it adopts the widget under the pointer. So a recording goes on writing frames while a panel is up, and a playback goes on reading them: the keystrokes and clicks that answered the panel are on the tape and answer it again. A panel's loop never calls `Time_BeginSimTick`, so its frames come at the loop's own rate and carry the stale `SimTickDelta` of the frame before the panel; the retail tapes' mouse timestamps put that rate at 5-7 ms a frame.

A panel raised by a frame's own command runs from inside that frame's tick. `Sim_MainTick` builds the input, ticks the effect pools and the groups, and then calls `Sim_PollPlayerInput`, which dispatches the command word first — so the panel comes up there, and the rest of the tick, the machines included, waits for it. `AlertPanel_Enter` (`00454630`) saves the input block's `0x30` bytes as the panel opens and `AlertPanel_Leave` (`004548ac`) writes them back as it closes, so after it the control laws read the frame that raised the panel. The button latches the panel set lie outside those bytes and outlast it ([`../simulation/joystick-input.md`](../simulation/joystick-input.md#a-latched-first-row-holds-the-axes)). The mission's own status alert comes up at the very end of a tick, after `Mission_PollStatus`.

All three retail tapes end in a panel, each a run of one repeated delta:

| Tape | Frames | Delta | Raised by | Answered by |
|---|---|---|---|---|
| `DEMO1` | 1015-1498 | 225 | no command on the tape — see [Open](#open) | a left click at (324, 298), pressed on 1427 and released on 1498 |
| `DEMO2` | 1587-1900 | 221 | `Q`, command `0x10` | `Enter` on 1899 |
| `DEMO3` | 3349-3659 | 249 | `Q` | left clicks at (375, 310) and (376, 306) |

Playback ends on a short read — any of the three header reads returning 0 — or when the player presses `[Ctrl]+[E]` (command `0x412`, tested on the live command word). Either way the file is closed, `004d255a` clears, live mouse input is restored and the live command word is zeroed; under `-D` the abort flag is raised as well. The live command word also still reaches `Sim_HandleWindowKey` (`0045fd60`), so `-B`'s `Ctrl+B` works during playback, and so does `Alt+Enter` unless `-B` is on the line ([`../command-line.md`](../command-line.md#dbsim)).

### Timing

**Playback is not paced.** The mission loop in `Sim_Run` (`0045f144`) calls `Time_BeginSimTick` (`004677bc`) — the spin until 40 ms have passed, and the only place `SimTickDelta` is measured — only while `004d255a` is clear. During playback `Sim_MainTick` (`0045f464`) instead copies the frame record's `SimTickDelta` into the global. The simulation therefore advances by exactly the time that passed when the tape was recorded, and frames come as fast as the machine can draw them: on a modern computer a demo finishes in a fraction of its recorded length. The low-resolution mode's present does wait: below 640 wide, `Screen_PresentFrame` (`00465524`) ends with `Flip(NULL, DDFLIP_WAIT)`, which DirectDraw synchronises with the display's vertical retrace, so frames come at most once a refresh. The 640-wide modes copy into the locked primary surface, and the windowed path `StretchBlt`s, without a wait.

**A replay does not reproduce its recording.** Played in retail, a tape drifts from the mission it was recorded in, further as it goes, until the recorded player's actions no longer fit the scene. The outcome also depends on how fast playback runs: pacing it to real time, by patching `DBSIM.EXE` to wait out each frame's recorded delta (`tools/scripts/patch_dbsim_tape_pacing.py`), makes the drift worse. Playback's own path departs from the recording's in these ways:

- **A latched trigger freezes the axes for the rest of the tape.** Playback skips the loop that releases a button's press-once latch, so after the first modal panel or flown round the pair of axes the stick drives reads zero on every frame — see [`../simulation/joystick-input.md`](../simulation/joystick-input.md#a-latched-first-row-holds-the-axes).
- **The playing machine's install.** Its `Backturn` ([The bundle](#the-bundle)), and its stick: the capability block is rebuilt from the live device, and the camera branch's pitch and `CHANGE DIRECTION` are gated on it ([`../simulation/joystick-input.md`](../simulation/joystick-input.md#while-the-camera-has-the-controls)).
- **Buttons 5-8 and the pointer bytes** are not recorded — [The stream](#the-stream).

Random draws follow the simulation's state rather than lead it: the simulation's draw sites sit in the tick's object updates, the spawn and the zone load, so its stream parts from the recording's only once the state has, and the presentation draws on a generator of its own ([`../simulation/random-generator.md`](../simulation/random-generator.md#the-simulations-draws)). Wall time reaches DBSIM through `GetTickCount`, called from `Time_GetCoarseTicks` (`00467724`), `Time_InitSimClock` (`0046773c`) and `Time_BeginSimTick`, and through `_time` in `DemoTape_PickRandom`. Of those, playback runs only `Time_GetCoarseTicks` once the mission is under way, from 64 call sites. What makes the outcome depend on playback speed is [Open](#open).

The recorded deltas are far from the 25 Hz cap's 81. Counting only the frames that ticked the simulation — each tape up to and including the frame that raised its closing [panel](#where-it-runs), 1015, 1587 and 3349 — the three retail tapes were made at 7.6, 6.5 and 8.7 frames per second of recorded length:

| Tape | Frames | Simulation frames | Their `SimTickDelta` min / median / max | Frames at the `0x1c2` clamp | Recorded length |
|---|---|---|---|---|---|
| `DEMO1` | 1499 | 1016 | 155 / 235 / 450 | 89 | 134 s |
| `DEMO2` | 1901 | 1588 | 90 / 301 / 450 | 267 | 245 s |
| `DEMO3` | 3660 | 3350 | 92 / 227 / 450 | 51 | 384 s |

The recorded length is the sum of the simulation frames' deltas at 125/256 ms per count. The panel frames after them carry the raising frame's delta, which only `Sim_MainTick` copies into `SimTickDelta`, so it never reaches the simulation. A frame that took longer than the clamp's 220 ms is recorded as 220 ms, so the sum is the time the simulation saw, which is shorter than the wall time of the recording session.

## The checkpoint file

`-d` opens `<stem>.dmp` into `004d2562`: mode `wb` when recording, which it then closes at once while leaving the handle non-null, and `rb` when playing back. The routine built around it is `FUN_00401dc0 (ptr, size, label)`:

- **Recording:** copies `size` bytes from `ptr` onto the end of a buffer at `004a8580`, advancing the cursor `00497078` and the total `0049707c`.
- **Playback:** reads `size` bytes from the `.dmp` into that same buffer, `memcmp`s them against `ptr`, and on a mismatch sets `00497080` and formats `label` into a stack buffer that is then discarded.

The buffer goes out once a frame. The static initialiser at `00401ef8` registers `00401e5c` with `RegisterSubsystemLoader` (`00401d64`) as a phase-6 subsystem, which `Sim_Run` runs after every frame. While recording with the `.dmp` handle set, it opens `<stem>.dmp` in mode `ab` (a failed open reports as `vcr.cpp:47`), writes the buffer's total bytes, closes the file, and resets the cursor to `004a8580` and the total to 0; in playback it does nothing.

The design is a state snapshot per checkpoint, compared on replay to detect drift. What calls the checkpoint routine is [Open](#open). Unless something calls it, the total stays 0 and each frame's flush appends nothing, so what `-d` is known to do in retail is create an empty `<stem>.dmp` when recording and hold one open when playing back.

## Reaching it

The main menu's `VIEW DEMO` button (`MainMenu_OnViewDemo`, `0043156f`, VSHELL) exits the shell with code 5, and `ES.EXE` answers that code by starting `dbsim` with `-D`; the tape's end returns exit code 6 and the shell comes back. `ES.EXE -r<name>` passes `-r<name>` through to every mission it launches. `ES.EXE` has no `-p` case, and neither it nor `VSHELL.EXE` contains a `-p` string. See [`../command-line.md`](../command-line.md#the-loop), which also covers the unreferenced argument list in VSHELL that ends in `-D`.

`dbsim -eggplant -ptapes\demo1` replays a retail tape directly.

## Symbol reference

| Symbol | Address | Role |
|---|---|---|
| `Sim_ParseCommandLine` | `0045e73c` | DBSIM's argument parser; owns `-r`, `-p` and `-D` |
| `DemoTape_PickRandom` | `0045ce9c` | Picks a tape name from `tapes\demolist.str` |
| `Tape_PackFile` / `Tape_UnpackFile` | `0045cc88` / `0045cd40` | One bundle block out and in |
| `Input_BuildPlayerDevice` | `0045a7f4` | Per-frame input build; both the record and the playback point |
| `PlayerInputBlock` | `004d234a` | The input block a frame record is a snapshot of |
| `TapeRecording` / `TapePlayback` | `004d255c` / `004d255a` | The two mode flags |
| `TapeFile` | `004d2566` | The open tape's `FILE*` |
| `TapeStem` | `004d255e` | The name the `tap` extension is forced onto |
| `DemoMode` / `DemoAbort` | `004d25b4` / `004d25b6` | `-D`'s extra flag, and the abort it raises |
| `CockpitMouseLive` | `004d1e5a` | Gates `CockpitMouse_OnEvent`; clear while a tape plays |
| `Input_PollDeviceBlock` | `0045ba8c` | Builds the live device block at `004d247a`, whose head is the live command word the stop and abort tests read |
| `Time_BeginSimTick` | `004677bc` | The 40 ms frame cap and `SimTickDelta` measurement; skipped during playback |
| `FUN_00401dc0` | `00401dc0` | The checkpoint: buffers a snapshot when recording, compares one when playing back |
| — | `00401e5c` | The checkpoint flush: appends the recording buffer to `<stem>.dmp` once a frame, as subsystem phase 6 |
| — | `00401ef8` | Static initialiser registering `00401e5c` for phase 6 |
| — | `004d2562` | The open `.dmp` checkpoint file's `FILE*` |
| `Config_BuildPath` | `0045eea4` | Prefixes `data\`, or `tapes\` during playback, onto `prefs.cfg` and `keyjoy.cfg` |
| `Keyjoy_LoadConfig` | `0045b78c` | Reads `data\keyjoy.cfg` whatever is playing |
| — | `Prefs_Path` (`0049e844`) | The preferences path; `tapes\prefs.cfg` during playback |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The `-D` abort and the `Ctrl+E` stop test the tape's own command word, so a recorded `Ctrl+E` ends playback and a retail demo aborts itself at its first command. | `Input_BuildPlayerDevice` tests `*DAT_004d2414`, which `Input_PollDeviceBlock` points at the live device block `004d247a`, and it tests it before the frame record is copied into `PlayerInputBlock`. The tape's command word never reaches either test. |

## Open

- **Deferred:** what calls the checkpoint routine `FUN_00401dc0`. `es2_xref.py` finds no branch or stored pointer reaching `00401dc0`, whose prologue is its first byte (its neighbour `Subsystem_RunPhase` returns seven callers under the same sweep, and the flush `00401e5c` its one registration), and an absolute-operand search finds no reader of the mismatch flag `00497080`. Until a caller is found, the only `-d` behaviour to reproduce is the empty `<stem>.dmp`.
- **Open:** the flush `00401e5c` and its registration `00401ef8` sit in the undefined bytes after `FUN_00401dc0` and have no function entries in the Ghidra project, so the decompile dumps lack them.
- **Open:** whether the retail tapes' missions are the `DEMO`, `DEMO_01` and `DEMO_02` entries in the campaign's own mission table ([`../shell/campaign-loop.md`](../shell/campaign-loop.md)), or the `DEMO*.MSN` files — a tape carries `script.dat`, a save formatted from a `.MSN` rather than the mission file itself, so the two are not matched up.
- **Deferred:** whether the latch freeze ([Timing](#timing)) accounts for the drift seen in the retail tapes. It needs a frame before a tape's closing panel that opens a modal panel or ends a flown electro-optical round.
- **Deferred:** what makes a replay's outcome depend on playback speed. The playback-only paths in [Timing](#timing) are per-frame and speed has no part in them. `Time_GetCoarseTicks`' 64 call sites are in the cockpit and HUD widgets, messages and lip-sync, palette effects, the alert panels, sound, `LiftStart_Rise` and `Sim_DeathFlash`; whether any of them moves simulation state is not established. Whether two plays of one tape at one speed match each other is not established either.
- **Open:** which panel `DEMO1`'s closing run of frames (1015-1498) was recorded under. No command on the tape raises one, which leaves the status alert `Sim_MainTick` raises when the mission is decided as the candidate.
