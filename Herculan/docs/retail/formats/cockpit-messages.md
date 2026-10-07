# Cockpit messages

DBSIM's **cockpit message port** is two instances of two classes on one base, `MsgPort`: the computer's own ticker at `view+0x20b` (`EventPort`) and the pilot-and-squad channel at `view+0x207` (`DialogPort`, or in a training mission its subclass `CommandPort`). Both are ten-slot queues sharing one lifecycle (`MessagePort_Tick`); what differs is where each posts from, what it draws, and whether it speaks through the [computer's voice or a squadmate's](audio.md#speech-and-the-comm-portraits).

## The computer's messages

`str\SYSTEM.STR` is what the cockpit computer can say: 63 lines, and for each the recording that reads it. Two `.STR` groups of 40 and 23 — but **the grouping means nothing**. Every call site passes one number, counted straight through both groups, and that same number is in each entry's own attribute byte 0.

Eight attribute bytes, all read by `MessagePort_Enqueue` (`00434e8c`) into the queued record:

| Byte | Record | Meaning |
|---|---|---|
| 0 | `+0x00` | The message id, which is also the entry's flat position |
| 1 | `+0x16` | A digit the pilot channel patches into its `.WAV` filename. Zero throughout |
| 2 | `+0x17` | Queue priority: the insert stops at the first queued entry of strictly higher value, so low sorts first. Zero throughout |
| 3 | `+0x1c` | `minTime` — shortest time on screen |
| 4 | `+0x20` | `maxTime` — longest time on screen |
| 5 | `+0x24` | `minWait` — delay before it may be shown |
| 6 | `+0x28` | `maxWait` — after this it is dropped unshown |
| 7 | `+0x2c` | Which `CVM_nnnn.WAV` reads the line, one-based |

The four timings are stored as `byte * 0x3c` coarse ticks, so at 16 ms a tick their units read as seconds. Every line carries `3, 6, 0, 0x14` — up for 3 to 6 seconds, no delay, gone in 20 if it never got its turn — except `TRANSFERRING DATA`, which carries `0x0a, 0x14, 0, 0x14`. The names come from the port's own trace string, and the enqueue settles the order: bytes 3 and 4 stay durations until the message is shown and have the show tick added in then, while 5 and 6 have the post tick added immediately.

The record reads four bytes past the eight the file supplies. As with `SOUNDS.STR`, attribute blobs point into the loaded file buffer, so those four overlap the next entry. They land at record `+0x2d`-`+0x30`, which have no reader found ([Open](#open)).

Byte 7 is a field and not an offset from the id: the numbering runs 1 to 66 across the 63 messages, skipping 0x1c, 0x2d and 0x2f, and the archive holds exactly 66 clips — so three are recorded lines no message claims.

`SystemMessages_Index` (`00435970`) is what keys the set. It scatters each string into a table at `base + attr[0] * 9` — a `{ char *text; byte count; byte *attributes }` triple — and counts how many landed in each slot, so **two entries sharing an id would become variants of one message** and the post would roll between them (`MessagePort_PickVariant` (`00436a3c`), the same roll `SOUNDS.STR` byte 6 drives). All 63 retail ids are distinct, so every count is one and no variant exists.

### The port

Messages reach the cockpit's message port through a vtable call. The cockpit view holds two ports on the one `MsgPort` base: the computer's ticker at `view+0x20b` and the pilot and squad channel at `view+0x207`. Each is a queue of ten records plus one lifecycle, and the preferences screen's COMPUTER MESSAGE and PILOT MESSAGE settings are their two enable bytes — options 3 and 2 of the simulator's option array ([`../simulation/preferences.md`](../simulation/preferences.md#dataprefscfg--the-option-array)), offered as TEXT ONLY / VOICE ONLY / TEXT / VOICE.

The byte gates the two halves separately: the display runs when it is not 1 and the voice when it is not 0 — three behaviours for three settings, which is why that row offers no OFF. The voice has a second gate both ports share: PILOT MESSAGE's handler writes `Sound_SpeechEnabled`, which every clip goes through ([`../simulation/preferences.md`](../simulation/preferences.md#dataprefscfg--the-option-array)), so with PILOT MESSAGE on TEXT ONLY the computer is silent whatever COMPUTER MESSAGE says. With the display off the port still runs the whole lifecycle and only skips the drawing — `port+0x4d2`, the suppression flag every paint entry point tests alongside `port+0x49e`, "a line is up".

Two further gates sit on the display half, both fields of the cockpit view manager, which `CockpitViewManager_Published` (`00429820`) hands back ([`cockpit-views.md`](cockpit-views.md#object-model)). Its `+0x14` is the **current view index**: the show refuses to display while it reads 4, the external view, and suppresses the line exactly as TEXT OFF does, lifecycle and all, alert tone included. Its `+0x1c` is the **view-change flag**, which `CockpitView_ProcessViewCommand` sets as it stages a change and `CockpitView_StepViewTransition` clears as the slide begins ([`cockpit-views.md`](cockpit-views.md#heads-down-pan--cockpitview_stepviewtransition-0042a9c0)): both ports' paints, and the pilot and squad channel's show, erase and background restore, test it and return, so neither box is painted while a view change is pending.

Both boxes are the herc's own, the last two fields of its `.GAU`: the pilot channel's at content offset 1668, `0,y - 320,y+10`, of which only the height is ever drawn ([below](#its-box)), and the ticker's at 1684, `100,y - 220,y+9` — a 120x9 box centred horizontally, at `y = 34` in seven cockpits, 43 in APOCA's and 100 in RAZOR's. Both are coordinate-shifted into device pixels by the `.GAU` loader's caller (`Gau_BuildCockpitWidgets`, `00431bf8`) before the constructor sees them.

`MessagePort_Tick` (`00435610`) is the whole lifecycle, and it runs on four latches:

| Latch | Meaning |
|---|---|
| `+0x4c9` | Due — `minWait` has passed and the message is waiting to go up |
| `+0x4ca` | Ready — on the ticker set by its next scroll, which is what puts one frame between due and shown; on the pilot and squad channel set by the comm box or by attribute byte 7 ([below](#its-speakerless-set)) |
| `+0x4cb` | Cancelled |
| `+0x49e` | A line is up |

Each tick first drops every *queued* message past index 0 whose `maxWait` has passed, then takes the front of the queue as current if there is nothing current, then:

- **not ready and not cancelled** — if `maxWait` has passed, drop it unshown; otherwise once `minWait` has, mark it due and run the port's begin callbacks (`+0x4b9`, up to two, registered through `MessagePort_AddBeginCallback`, `004355a8`). Only the pilot channel registers any: the comm box installs `CommBox_OnMessageBegin` (`0044b4ec`), which is what starts that speaker's `.wav` and `.SNC` portrait and plays the `whitenz` static under it, and `CommBox_OnMessageEnd` (`0044b5c0`) on the matching end hook. The computer's port has none.
- **not due, or cancelled** — take the line down when it is cancelled, when `maxTime` has passed, when `minTime` has passed *and the queue holds more than one message*, or when the player's machine is dead (`LocalPlayerMech + 0x99`). That middle clause is the whole of the port's preemption: a message with the screen to itself keeps it for its maximum and gives it up at its minimum only when there is a successor.
- **due and ready** — show it, and on success add the current tick into `minTime` and `maxTime`, turning both from durations into deadlines.

`MessagePort_Show` (`00436abc`) is the show. It swallows a repeat of the same id inside 300 coarse ticks (about 4.8 s), and a swallowed repeat *refreshes* that window rather than leaving it, so a stream of them stays silent for as long as it keeps coming. Otherwise it latches the line (`strncpy`, 0x50 characters), restores the box to its authored rect, publishes the scroll origin, repaints, and plays an alert tone picked by a switch on the id:

| Ids | Tone |
|---|---|
| `0x00` `INTERNAL DAMAGE`, `0x13` `STRUCTURAL FAILURE IMMINENT` | `0x19` `strcfail` |
| `0x0c`, `0x0f`, `0x10` (shield generator / powerplant / weapon destroyed), `0x14` `SHIELDS LOW`, `0x15` `SHIELDS CRITICAL` | `0x18` `wrnwoop2` |
| everything else | `0x1a` `gnract` |

The switch names fourteen further ids explicitly — `0x17`, `0x19`, `0x1d`-`0x1f`, `0x2a`-`0x2f`, `0x34`, `0x37` and `0x38` — and gives every one of them the same `gnract` its default arm gives, so it is wider than its behaviour. `0x36` has an arm of its own: `gnract` too, plus the blink latch `port+0x4d3` ([below](#the-ticker)). Speech goes last, and only then: the voice is a consequence of the line going up, not a separate event. See [`audio.md`](audio.md) for the tones themselves.

`MessagePort_Withdraw` (`00435ac8`) is the withdraw. A match on the current message sets its cancel latch unless the message is due (`+0x4c9`), that is, past its delay and waiting to go up; that one is left alone and the call returns 0. The show clears the due latch, so a line already on screen is cancelled and comes down on the next tick. A match anywhere else is removed from the queue. It matches on the id **and** the record's `+0x02` subject pointer, so the same message about two machines is two entries.

`MessagePort_Pause` (`00435b58`) / `MessagePort_Resume` (`00435b80`) are the pause pair: the second shifts every deadline in the queue, the current message's two display deadlines and the scroll's publish time forward by however long the pause lasted. Two places call it, each on both ports: `AlertPanel_Enter` (`00454630`) and `AlertPanel_Leave` (`004548ac`) around every modal panel — the status alert, pause, objectives, preferences and controls panels — which leave the sound alone, and the lost-window pair `Sim_Suspend`/`Sim_Resume` (`0045f0b8`/`0045f0ec`), which also calls `Sound_SuspendAll`/`Sound_ResumeAll`.

### The ticker

`MessageTicker_Paint` (`00436cec`) paints it: the box flooded with `COLORS.DAT` id 19 (black), a one-pixel frame in id 9 (red) — the fill brush's style 4, which `Raster_FillRect` (`004865f8`) implements as four line draws round the rect — and the line in `ColorSchemePanels[2]`, `CPRED`. The clip rect is then narrowed by `3 << VideoMode_XCoordShift` on each side before the glyphs go down, which is what makes the text slide under the frame rather than past it.

The text **scrolls**. `MessageTicker_ScrollText` (`00436f70`) recomputes its x every frame as `port+0x4af - (0x23 << VideoMode_XCoordShift) * elapsed / 0x3c` — starting at the box's right edge and travelling left at `0x23` authored units every `0x3c` ticks — about 36 units, 73 device pixels, a second in the 640-wide mode. There is no wrap: a line that outlives its own width simply leaves, and against a 120-unit box the 3-to-6-second display time is matched so a long line crosses about once.

`TRANSFERRING DATA` (`0x36`) is the one exception, and the one id the port tests outside the tone switch: the paint compares the id with `0x36` and centres that line in the box instead of scrolling it, and draws it only on `Time_GetCoarseTicks() & 0x20` while the latch `port+0x4d3`, which the switch's `0x36` arm sets and the erase clears, is up — so it blinks. Its 10-and-20-second timings are what make that readable.

Vertically the line is centred by cell rather than by ink: the paint anchors at `((height - cellHeight) >> 1) + inkHeight + 1` and the glyph blitter (`HudFont_DrawGlyph`, `00482428`) subtracts `inkHeight` straight back off. That is **not** `Label_SetRect`'s rule (see [`mfd.md`](mfd.md#label-placement)), which centres `inkHeight`; the ticker is not a label.

### Posters

**These are the posters.** The port is constructed once, in `Gau_BuildCockpitWidgets`, and stored at `view+0x20b`, so anything that posts has to load that displacement; besides that store there are seventeen loads of it in the image, and six of the functions holding them post — the six rows below. The rest pause, resume, erase or paint it. `Computer_PostMessage` (`00420a68`) is the shared helper the first row stands for. A copy of the pointer kept anywhere else would escape this count ([Open](#open)).

| Poster | Messages |
|---|---|
| `Computer_PostMessage`'s eighteen callers | The damage set `0x03`, `0x04`, `0x08`, `0x0c`, `0x10`, `0x13`, `0x15`; `0x19` `MISSION TARGET DETECTED` and `0x1d` `WAYPOINT REACHED` from the player's think; `0x2a`/`0x2b` jamming; `0x2e` `ENEMY TARGET DESTROYED` and `0x2f` `ENEMY TARGET DISABLED`; and the data link's `0x34`-`0x37` and `0x38`. Plus `0x12`, below |
| `Mission_Status` (`004135e8`) | `0x16` `MISSION FAILED`, `0x17` `MISSION SUCCESSFUL`, `0x1e` `APPROACHING MISSION ZONE BOUNDARY`, `0x20` `RULES OF ENGAGEMENT VIOLATED` — see [`../simulation/mission-objectives.md`](../simulation/mission-objectives.md#what-the-computer-says) |
| `Cockpit_PowerUpTick` (`00432924`) | Once `200 <` coarse ticks have passed since the sequence began, it reads the piloted machine's internals 0-9 with `Mech_ReadEntryDamage` (`0041b514`) and posts `0x22` `POWERUP INITIATED. INTERNAL DAMAGE DETECTED.` if any reads nonzero, else `0x21` `... ALL SYSTEMS NOMINAL.`, then sets `cockpit+0x245` so it announces once. The reading is the internal's damage as Q8 of its maximum, so one counts once it holds 1/256 of it: a nonzero reading also goes through `Damage_ToConditionState` (`00438700`) and is tested against `0x5a`, but that function returns a condition index from 0 to 4, which is always under. A flyer never announces, because its start is never stamped — see the [power-up sound](audio.md#the-cockpit-power-up), a separate event the announcement rides alongside |
| `NavMarker_Tick` (`004349ac`) | `0x1d` `WAYPOINT REACHED` again, on returning to a dropped marker — [`../simulation/player-waypoints.md`](../simulation/player-waypoints.md#the-nav-marker) |
| `Mech_ToggleRadarMode` (`0041b468`) | Withdraws **both** `0x2c` `ACTIVE RADAR MODE` and `0x2d` `PASSIVE RADAR MODE`, then posts the one the mode just became — so flipping twice quickly announces where it ended up rather than reading out the sequence |
| `ConsoleButtons_ToggleAutoTrack` (`00441f7c`) | The same shape with `0x26` `AUTO TRACKING ENGAGED` and `0x27` `AUTO TRACKING DISABLED` |

**`0x12` `DAMAGE LEVEL CRITICAL` has a call site it cannot reach.** In `Mech_ApplyDirectFireDamage` (`004188c8`) the struck component's damage percent is read before the write and again after, and the post needs the **later** read under 100 and the earlier one over it — the reading would have to have fallen. It only falls if the write is negative, which needs the shot's diverted splash share to exceed its own armour damage; the largest splash factor among retail `PROJ.DAT`'s 27 records is 1000 against the Q10 unit of 1024, so it never is. The cockpit jolt (`Cockpit_StartHitShake`, `00434010`) shares the gate, sits above the test and does fire. A hand-edited `PROJ.DAT` would reach the line.

Those rows post 29 distinct ids, so **34 of the file's sixty-three lines have no poster** — among them `MISSION OBJECTIVES COMPLETE` (`0x1a`), `PRIMARY OBJECTIVE COMPLETE` (`0x1b`), `SECONDARY OBJECTIVE COMPLETE` (`0x1c`), `MISSION ABORTED` (`0x18`), `FRIENDLY TARGET DESTROYED` (`0x30`), `AUTO PILOT ENGAGED`/`DISABLED`, `FOLLOW MODE ENGAGED`/`DISABLED` and the `10...9...8...` countdown. Every one has a recorded `CVM` clip, and `MessagePort_Show`'s tone switch names four of them (`0x00`, `0x0f`, `0x14`, `0x1f`) — the switch is written wider than the game reaches.

**`0x2e` and `0x2f` do not test sides.** Their guard is only that the player fired the killing shot and that the victim is the player's own selected target (`mech+0x1a4`), so destroying a friendly you had boxed announces `ENEMY TARGET DESTROYED`, and the two `FRIENDLY` lines are among those with no poster.

The damage set's own guards — which reading of what, and which latch byte stops each line repeating — are [`../simulation/component-damage.md`](../simulation/component-damage.md#what-the-endpoint-announces)'s.

At 16 ms a coarse tick the power-up announcement lands 3.2 s in, inside `start3`'s five seconds rather than after them.

## The pilot and squad channel

The port's second instance, at `view+0x207`. Same queue, same lifecycle, same four timings; a different catalog, a different box, and a squadmate's face on the comm portrait beside it.

A squadmate's line is posted by `Ai_PostSquadMessage` (`00420a98`) as `{id, machine}` through vtable slot 0 of the port `CockpitView_GetSquadMessagePort` (`00433158`) returns. The machine is the record's `+0x02` subject, which picks the comm-box slot and so the set the id is looked up in. The post is suppressed for a destroyed machine unless forced. Which situation raises which id is the *raised by* column of [What each id says](#what-each-id-says).

### Its message sets

`str\PILOT0.STR`, `PILOT1.STR`, `PILOT2.STR` and `PILOT4.STR`, one per voice bank, keyed the same way `SYSTEM.STR` is but with **seven** attribute bytes read rather than eight: the clip number is absent because the filename is built from the id and the variant instead (see [`audio.md`](audio.md#file-naming)). `SystemMessages_Index(port, 2, slot, bank)` scatters a bank into a per-slot table at `DAT_004d04e8 + slot * 0x183`, 43 ids of 9 bytes each, so each comm box carries its own speaker's set.

Unlike the computer's, **the variant roll is live here**: ids `0x02`, `0x1e` and `0x1f` carry two or three recordings apiece, and `MessagePort_PickVariant` chooses between them.

**Only banks 1, 2 and 4 are ever loaded.** The bank a squadmate takes is `(portrait >> 2) + 1` with 3 remapped to 4 (`Pilot_VoiceBankOf`, `00434260`), its portrait being its pilot index over three, 0 to 11 across `PILOTS.STR`'s 36 pilots ([`heads-down-display.md`](heads-down-display.md#who-is-in-it)). That never yields 0, so `PILOT0.STR` ships and is never read — and it is the only one of the four that differs in shape rather than in wording: it stores seven attribute bytes per entry where the other three store an eighth that is zero throughout, it carries id `0x24` which no other bank has and lacks `0x2a` which every other bank has, and its two-recording ids are `0x05` and `0x06` rather than `0x02`. Read it as the early draft it is, not as a fourth voice.

### Its speakerless set

A post whose `+0x02` subject is null is not a squadmate's. The port's post (`PilotMessagePort_Post`, `00435c48`, vtable slot 0) resolves such an id in a table of its own at `004d0971` instead of a slot's, rolling variants against `CommandStringTable` (`004d04c8`) the same way. `Gau_BuildCockpitWidgets` fills that table right after building the port, from `str\COMMAND<n>.STR` — `SystemMessages_Index` mode 1, the literal `commandX` with the [training mission number](script-dat.md#the-training-mission-number) as the digit. The one poster is `Action_Activate` (`00423430`), a mission action's line ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-four-ways-an-action-activates)).

An ordinary mission speaks from `COMMAND0.STR`: three lines, one group, a pilot bank's shape with an eighth attribute byte.

| id | line | timings (min/max shown, min/max wait) |
|---|---|---|
| `0x00` | `CYBRID UNITS HAVE REACHED OUR PERIMETER! PROTECT OUR BASE!` | 3, 5, 0, 10 |
| `0x01` | `MAYDAY! MAYDAY! OUR BASE IS UNDER ATTACK!` | 3, 5, 0, 10 |
| `0x02` | `THIS IS BASE COMMAND. WE ARE UNDER ATTACK! ALL UNITS, PLEASE ASSIST!` | 3, 6, 0, 10 |

The composer signs it `HQ` — `STRINGS0.STR` group 8, the one string at `DAT_004d1430` that `PilotMessagePort_GetHqName` (`004342b8`) returns — and with no squadmate to colour it, [the box](#its-box) is the computer's black and red. `CommBox_OnMessageBegin` resolves the null subject to no slot and returns, so no comm box opens, no portrait runs and no static plays.

Byte 7 is 1 on all three, and it lands at the queued record's `+0x2c`, which gates two things. The port's per-frame update (`PilotMessagePort_Update`, `004361cc`, vtable `+0x10`) sets the ready latch for a due message only when it is set; a squadmate's line, whose byte 7 is 0, is instead made ready by the comm box as its portrait starts talking (`MessagePort_MarkReady`, `00435b14`, from `HddDisplay_ServiceCommBoxes` (`0044b5f8`)), and cancelled when the portrait's script runs out (`MessagePort_Cancel`, `00435b38`). So byte 7 is what lets a line up with no comm box behind it — without it a speakerless line would wait out its `maxWait` and drop unshown. The same byte gates `PilotMessagePort_Speak`'s voice arm, which patches `id + 1` and the variant digit into `BC_00000` and hands the name to `Voice_PlayNamed`. No `BC_*` clip ships in any archive, so an action's line is text only.

Across the 62 retail missions (`.MSN` row #10 `0x4E`, the message id plus one) exactly one action outside `TRAIN1`-`TRAIN4` posts a message: `C1_02.MSN`'s, with id 1, `MAYDAY!`. No retail mission posts ids 0 and 2.

### The training port

A training mission builds a different class for `view+0x207` (vtable `0049baa8`, constructor `TrainingMessagePort_Ctor`, `00436244`, a `0x4ef`-byte instance) and indexes `COMMAND<n>.STR` into it, `n` the training mission number. `COMMAND1.STR`-`COMMAND4.STR` are the four instructors' scripts. Only `TRAIN1.MSN`-`TRAIN4.MSN` set a training mission number, 1 to 4; `TRAIN5`-`TRAIN8` leave it 0 and run on the ordinary port with `COMMAND0.STR`, and none of their actions posts a message. The four post from their scripts: 15 of `TRAIN1`'s 16 actions, and all of `TRAIN2`'s 21, `TRAIN3`'s 19 and `TRAIN4`'s 10.

**Its post (`TrainingMessagePort_Post`, `004362e4`) ignores the subject.** Every id resolves in the `COMMAND<n>` table, and the first entry is enqueued without a variant roll. In these files several consecutive entries share an id: they are the sentences of one instruction, attribute byte 1 counting 0, 1, 2 through them, and only the first carries timings.

**Its paint (`PilotMessagePort_Paint`, `0043660c`) shows them all.** `PilotMessagePort_WrapText` (`00436318`) takes the count from the id's table slot and walks forward from the first sentence with `StrTable_NextString` (`004539cc`), which returns the string after the one it is given in the loaded file, so an instruction is its first entry and the ones after it. It joins them into lines of at most 80 characters (60 in the 320-wide mode):

- A sentence that fits on the current line (`length + column < limit`) is appended after a space, or starts the line if it is empty.
- One that does not is split at its last space that still fits. The head goes on the current line after a space — added even when that line is empty, so an instruction whose first sentence is too long opens with a blank (`COMMAND2.STR` id 2 does). The rest starts the next line **unwrapped**, however long.
- The widest line is picked by character count, and only that line is measured for the box's width.

The box is `(lines + 1) * (8 << YCoordShift)` tall, as wide as that measured line plus `10 << XCoordShift` each side, and centred on the screen. Its top is the `.GAU` rect's, raised by the herc's **training lift**, the `int32` at content offset 1664: on the training arm only, `Gau_BuildCockpitWidgets` shifts it by `YCoordShift` and subtracts it from both of the rect's y edges (`00431ce4`-`00431d05`) before the constructor sees the rect. It is not the rect's height — every retail rect is 10 units tall — but APOCA 60, RAPTOR2 70, MAVERICK and OUTLAW 75, COLOSSUS, OGRE, SAMSON and TOMAHAWK 85, RAZOR 0, so a walker's training box opens 45 to 55 units down the screen instead of 115 to 135. The constructor extends the rect by eleven lines to size the save-under buffers. The lines are left-aligned at the box's left edge plus the same margin, the first anchored at `top + 1.5 * lineHeight` and each next one line lower, in `ColorSchemePanels[10]` `WHITE` (`0049b0d4`, stored at `+0x4df`) on the computer's black, framed in its red.

**Its voice follows the paint**, on the same display pass and only when PILOT MESSAGE is not TEXT ONLY: `TMx_0000` with the training number and `id + 1` patched in, under the voice folder (`simvoice`, its last letter the language byte) and the directory `data\drive.cfg` names (`DriveCfg_PrefixPath`, `0045ee44`). So the clips are loose files beside the archives, one per instruction: `SIMVOICE\TM1_0001.WAV` reads all of `COMMAND1.STR` id 0. The 65 retail clips are exactly the four files' instruction ids plus one.

Its per-frame update (`TrainingMessagePort_Update`, `004365d0`) sets the ready latch for any due message, byte 7 or not.

### What each id says

`PILOT1` as the reference bank; the other two live banks reword every line and change none of the meanings. `/` separates the variants of one id.

| id | line | raised by |
|---|---|---|
| `0x01` | `I SPOTTED SOME BAD GUYS, SIR` | sighting a hostile — [`../simulation/ai-targeting.md`](../simulation/ai-targeting.md#radio-callouts) |
| `0x02` | `CHALK UP ANOTHER KILL FOR THE GOOD GUYS!!` / `ALL RIGHT!` | this machine put something out of the fight |
| `0x03` | `I'M GETTING MY BUTT KICKED OUT HERE! HOW 'BOUT A LITTLE HELP?!` | taking fire |
| `0x04` | `THEY NAILED ME! I THINK I'M DONE FOR...` | a squadmate immobilised |
| `0x05` | `NICE SHOOTING` | the player scored |
| `0x06` | `THAT'S ALL SHE WROTE, LETS GO HOME!` | nothing hostile left |
| `0x07` | `MY HERCS BEEN SHOT TO PIECES. I'M HEADING BACK TO BASE.` | withdrawing |
| `0x08` | `WATCH YOUR TARGET, SIR!` | friendly fire — [`../simulation/ai-targeting.md`](../simulation/ai-targeting.md) |
| `0x09` | `I'M BREAKIN' UP! EJECTING!` | ejecting |
| `0x0a` | `NO PROBLEM.` | — |
| `0x0b` | `ON MY WAY.` | `HELP ME OUT!` taken |
| `0x0c` | `ROGER. SITTING TIGHT.` | `HOLD YOUR FIRE` taken |
| `0x0d` | `NO CAN DO. THESE GUYS ARE ALL OVER ME!` | `ATTACK MY TARGET` refused: already broken off |
| `0x0e` | `I DON'T SEE ANYTHING!` | — |
| `0x0f` | `I HEARD YOU.` | the order is the one already in force, or `SCAN FOR HOSTILES` to a machine whose radar is already active |
| `0x10` | `I'M OUT OF RANGE.` | — |
| `0x11` | `ENGAGING TARGET!` | `ATTACK MY TARGET` / `ATTACK ENEMY` taken |
| `0x12` | `WHAT'S YOUR TARGET?` | refused: the player has nothing selected |
| `0x13` | `STAND BY.` | — |
| `0x14` | `ON MY WAY.` | `JOIN ON ME` taken from outside formation range |
| `0x16` | `THAT ONE'S ALL YOURS, SIR` | `IGNORE MY TARGET` taken |
| `0x17` | `ROGER, SWITCHING OVER TO DEFENSIVE MODE.` | `DEFEND POSITION` taken |
| `0x18` | `ROGER. SIGHTING CONFIRMED.` | — |
| `0x19` | `ROGER. MOVIN' OUT.` | — |
| `0x1a` | `CAN'T HELP YOU THERE. I HAVE MY OWN PROBLEMS RIGHT NOW.` | `HELP ME OUT!` refused: already committed to that threat |
| `0x1b` | `MY HERC'S TOO SHOT UP!` | any order but `IGNORE MY TARGET` refused: out of action (or, for the movement orders, immobilised) |
| `0x1c` | `ROGER THAT! PREPARING TO OPEN FIRE!` | `FIRE AT WILL` taken |
| `0x1d` | `WHAT?!` | `HELP ME OUT!` refused: nothing is shooting at the player; `ATTACK ENEMY` refused: the subject is already neutralised |
| `0x1e` | `AFFIRMATIVE!` / `YES SIR.` / `ROGER.` | the generic yes — `JOIN ON ME` from inside formation range, and `IGNORE MY TARGET` from a machine that is out of action |
| `0x1f` | `NEGATIVE.` / `SORRY SIR.` / `UNABLE TO COMPLY.` | the generic no; no poster found |
| `0x20` | `ALREADY GOTCHA COVERED.` | the order names a post this machine already holds; `FIRE AT WILL` to a machine already in a fight |
| `0x21` | `PLEASE STAND BY...` | — |
| `0x22` | `STANDING BY...` | the pilot selected on the command display, by comm box or map marker — `HddDisplay_SelectPilot` (`0044a720`), which posts it directly through the port's slot 0, not through `Ai_PostSquadMessage`; withdrawn when an order is sent to that slot ([`../simulation/ai-squadmates.md`](../simulation/ai-squadmates.md)) |
| `0x23` | `DAMN!` | — |
| `0x25` | `AAAAAAARRGHH!` | a squadmate destroyed — `Mech_CreditNeutralisedTarget`, [`../simulation/component-damage.md`](../simulation/component-damage.md#what-the-attacker-is-told--mech_creditneutralisedtarget-00415710) |
| `0x26` | `ROGER. RADAR ACTIVATED.` | `SCAN FOR HOSTILES` taken |
| `0x27` | `I ALREADY SHUT IT DOWN.` | — |
| `0x28` | `ROGER. SHUTTING DOWN.` | `EMCON` taken |
| `0x29` | `NEGATIVE. IT'S TRASHED.` | — |
| `0x2a` | `ON MY WAY.` | `PATROL GRIDPOINT` / `GOTO GRIDPOINT` taken |

`0x15` is in no bank at all. The em-dashed ids and `0x1f` are recorded but have no poster found ([Open](#open)); which arm of `Mech_ReceiveSquadOrder` raises each of the rest is [`../simulation/ai-squadmates.md`](../simulation/ai-squadmates.md#receiving-one--mech_receivesquadorder-00420ad4-mech-vtable-0x28)'s case table.

**`0x1e` is the yes and `0x1f` the no.** Mistaking them is easy because one refusal arm of `Mech_ReceiveSquadOrder` posts `0x1e`: a squadmate too shot up to comply with `IGNORE MY TARGET` answers affirmatively, which is correct and reads as a bug in a table of refusals.

### Its box

`PilotMessagePort_Speak` (`00435d9c`) paints it, and it looks nothing like the ticker. The box is **sized to its line and centred on the screen**: the paint measures the composed text, sets `x0 = (screen / 2) - (width / 2) - (10 << XCoordShift)` and `x1 = x0 + width + (0x14 << XCoordShift)`, and takes y from the `.GAU` rect unchanged. Every retail file authors that rect as `0,y - 320,y+10`, so the authored width is discarded and only the height reaches the screen. The line sits at `(screen / 2) - (width / 2)`, vertically at `bottom - ((height - inkHeight) >> 1)` — the **ink** centred in the box, where the ticker centres the cell.

**The colours are the speaker's.** The paint resolves the message record's `+0x02` through `Squad_IndexOf` and, for a squadmate, fills with that slot's colour, `HudColorTable_Get(slot)` (ids 12, 15, 26 — [`heads-down-display.md`](heads-down-display.md#the-gauge)), and frames it in the palette entry **one below** the fill:

```
slot   = record->speaker ? Squad_IndexOf(record->speaker) : -1
fill   = slot < 0 ? COLORS.DAT[19] : HudColorTable_Get(slot)
border = slot < 0 ? COLORS.DAT[9]  : fill - 1
```

That subtraction is arithmetic on the already-resolved palette index, not a second logical id — slot 0's id 12 lands on palette 14, green, and its frame on palette 13, yellow. Only a message with no squadmate behind it falls back to the computer's black and red. The text is `ColorSchemePanels[2]` `CPRED` either way, so red on green is what a squadmate's reply looks like.

`PilotMessagePort_ComposeLine` (`00435d0c`) builds the line: the speaker's name from their comm box (`Squad_PilotName` (`00434298`) into `HddGauge_Name` (`0044b900`), the gauge's own `+0x137`), or `HQ` from `PilotMessagePort_GetHqName` (`004342b8`) when the record names no object; then `": "`; then the message text, `strncat`ed at 0x4a characters.

A training mission draws a different picture altogether ([above](#the-training-port)). What is on screen in `Reference/MFD_Talking_head.png` is the speaker-coloured single line.

The speaker's own portrait, alongside this box, is driven separately — see [`heads-down-display.md`](heads-down-display.md#snc--portrait-lip-sync-scripts).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `PilotMessagePort_Speak` dispatches the squad's voice | It is named for the `BC_00000` template it patches, and that arm is not a squadmate's: it is gated on the queued record's `+0x2c`, attribute byte 7, which is 0 or absent in every `PILOT<n>.STR` entry and 1 in every `COMMAND0.STR` one — so it is `HQ`'s voice ([above](#its-speakerless-set)), and asks for a clip that does not ship. What the function does for a squadmate is paint the channel's box. A squadmate's voice comes solely from the comm box, through `CommBox_BeginMessage` ([`audio.md`](audio.md#speech-and-the-comm-portraits)). |

## Open

- **Unported:** the display's two further gates — the computer's line suppressed, tone and all, while the cockpit view manager's `+0x14` reads 4 (the external view), and both ports' paints skipped while its view-change flag `+0x1c` is set.
- **Open:** no poster of the computer's port outside the six in [Posters](#posters) found by `es2_fieldscan.py 20b` (seventeen loads of `view+0x20b`); a copy of the port pointer held elsewhere would escape that scan, and the 34 lines with no poster rest on it.
- **Open:** no reader of the queued record's `+0x2d`-`+0x30` (attribute bytes 8-11) found by `es2_fieldscan.py 2d 2e 2f 30` over the port's code (`00434e50`-`00437300`); the comm box also reads the current record.
- **Open:** whether anything posts the pilot ids the table marks with an em dash, or `0x1f`. No poster found among `Ai_PostSquadMessage`'s six callers (`Mech_ReceiveSquadOrder` included), `Action_Activate`, or the other callers of `CockpitView_GetSquadMessagePort`, of which only `HddDisplay_SelectPilot` posts, and only `0x22`; which does not settle it.
- **Open:** no retail capture of TRAIN1-TRAIN4 has been checked against the raised training box (top 45 to 55 units for a walker); the lift is read from code and `.GAU` data only.
