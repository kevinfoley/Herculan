# DBSIM.EXE — the Heads-Down Display's comm boxes

**The [Heads-Down Display](heads-down-display.md)'s three squad comm boxes are ES2's ([squad comm boxes](../../retail/simulation/heads-down-display.md#squad-comm-boxes)): idle, each shows a squadmate's name, condition and orders; while the squadmate speaks it goes to static, then the face talks, then static again.** What a player would see differently:

- a talking squadmate is drawn as a head, one of three picked at random on each paint, with the mouth frame over it;
- the name in a comm box is the `str\pilots` entry at the pilot's portrait number, where ES2's is the entry at the pilot's own number.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The code is in the display's object file, `phdd.cpp` ([where it starts and ends](heads-down-display.md#where-the-object-file-starts-and-ends)): the display builds the three gauges at its `+0xf6` ([the display object](heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258)), paints them and runs their state machine each frame ([drawing it](heads-down-display.md#drawing-it)), and the boxes are its buttons 10–12 ([the buttons](heads-down-display.md#the-buttons)).

## The gauge

A gauge is `0x14a` bytes, ES2's `0x14e` less ES2's published-message field, with ES2's offsets up to `+0x149`:

| Gauge | Holds |
|---|---|
| `+0x00`–`+0x14` | the six labels |
| `+0x18` | the pilot's bank |
| `+0x1c` | a byte `CommBox_OnMessageEnd` clears ([Open](#open)) |
| `+0x1d`, `+0x2d` | the video rect, the marker rect |
| `+0x3d` | 30 offset pairs, 8 bytes each, from `ofs\pilot<n>` |
| `+0x12d`, `+0x12e`, `+0x12f` | the message id (byte), the static frame 0–4, the speech slot |
| `+0x133` | 1 for the full bank, 0 for the low-memory one |
| `+0x135`, `+0x137` | the portrait number (`int16`), the name |
| `+0x13b`, `+0x13f`, `+0x143` | the state, the previous state, the deadline |
| `+0x147`, `+0x148`, `+0x149` | the comms-out latch, the scream's phase and its previous value |

**`CommBox_ElementCtor` (`0003cab8`)**, (gauge), allocates `0x14a` bytes when given none, returning 0 when that fails, and runs `__vec_ctor` over the 30 pairs with `Point_DefaultCtorHddCopy` (`0003cb74`, by its padding `0003cb71`), which allocates 8 bytes when given none and returns its argument. ES2's (`0044c0e7`).

## Who is in it — `HddGauge_LoadPilotFrames` (`0003dab4`)

(display, pilot, slot), from `Cockpit_LoadSquadmatePilots` once its walk has filled the slots, with each squadmate's pilot index, its `int16` `+0x232`, read as an `int16` ([the squadmates](cockpit-panel.md#the-squadmates)):

1. for slot 0, the pilot is `CommBox0PilotOverride` (`000944d8`) when that is not negative; `Sim_SetCommBox0PilotOverride` (`0003ea50`), (*n*), frameless and with no Ghidra function, stores *n* there, and `Sim_ParseCommandLine` calls it with `atol` of a `-t<n>` argument ([the command line](main-loop.md#the-command-line));
2. the portrait *p* = `Pilot_PortraitOf`(pilot), pilot / 3, at `+0x135`, and the name at `+0x137`: the string of `HddPilotNameTable` reached by *p* steps of `StrTable_NextString` from `StrTable_FirstString`, ES2's walk ([the layout](../../retail/formats/str-strings.md#layout)) to the portrait's number where ES2's walks to the pilot's own. Each step goes over one record by its length word and attribute count and knows no groups, so while *p* is below the count of `str\pilots`'s first group the name is that group's entry *p*. The step from a group's last entry lands on the next group's first record's length word, two bytes into that group, and takes it for text, its length the group's count word: a length below 256 reads as a one-character name, that length's low byte. The steps after that one read the records out of step, and one that leaves the buffer gives 0, no name ([Open](#open));
3. `SystemMessages_Index`(the pilot channel, `Pilot_VoiceBankOf`(*p*), slot), which loads `str\pilot<v>`, *v* = *p* / 4 + 1 with 3 made 4, for the slot's lines ([the string files](cockpit-messages.md#the-string-files)); ES2's passes mode 2 and the voice bank of the same portrait;
4. makes the file name `pilot` (`00094638`) followed by `ltoa`(*p*, radix 10) and reads it from the folder `0009463e` names, `ofs`, through a `VolRStream`, asserting at severity 5 (`phdd.cpp` line `0x3c1`, `HDDGauge::Couldn't load anim offset`) when the stream's status is set, and reading on either way: 27 entries, each an `int32` index *k* through the stream's read slot and 8 bytes into pair *k* at `+0x3d` + 8*k*, *k* used unchecked against the 30 pairs;
5. loads the bank of that name from the folder `000980f8` names, `dba`, with `ClassItem_LoadFromFile`, at `+0x18` and sets `+0x133`; with `g_LowMemory` (`000bda98`, [low memory](../formats/chassis-dat.md#low-memory)) set, the name with `s` (`0009467a`) appended, `pilot<p>s`, instead and `+0x133` cleared; asserting at line `0x3d9` (`HDDGauge::Couldn't load bitmap array`) when the bank is null;
6. builds the six labels on button 10 + slot's rect {*x0*, *y0*, *x1*, *y1*}, each `Label_Ctor`(0, font) and then `Label_SetRect` with flags 2 and margin (0, 0), *h* the cell height of `cpred` (its `int16` `+10`); a fill is stored in the label's `+0x1c` after its `Label_SetRect`:

| Label | Rect | Font | Fill |
|---|---|---|---|
| `+0x00` | *x0* + 2 to *x1* − 2, *y0* + 4 to *y0* + 4 + *h* | `cpred` | `HudColorTable_Get`(slot), the slot's color |
| `+0x04` | *x0* to *x1*, *y0* + 16 to *y0* + 16 + *h* | `cpblue` | none |
| `+0x08` | the same, from *y0* + 24 | `cpred` | none |
| `+0x0c` | the same, from *y0* + 32 | `cpblue` | none |
| `+0x10` | the same, from *y0* + 40 | `cpred` | none |
| `+0x14` | *x0* to *x0* + 10, *y1* − 10 to *y1* | `cpred` | `0x3b` |

7. clears the previous state, the state, the static frame, the comms-out latch and the scream's two phase bytes; puts the squadmate's machine in subject slot 1 + slot and the name in name slot 1 + slot; and closes the stream.

The labels are ES2's ([the gauge](../../retail/simulation/heads-down-display.md#the-gauge)) at half ES2's offsets with no coordinate shift. ES2's also puts the box's button back to state 0, where ES1's visibility rows show the boxes on both pages. The sixth label's text is set by none of this file's code, as in ES2 ([the unfilled sixth label](../../retail/simulation/heads-down-display.md#the-unfilled-sixth-label)).

## Starting a line

**`CommBox_OnMessageBegin` (`0003e488`)**, (record), the pilot channel's begin callback, ES2's (`0044b4ec`): for a record whose subject `+2` is in a squadmate slot (`Squad_IndexOf`), `CommBox_BeginMessage`(the display, that gauge, record); when that fails, the channel's ready and cancelled latches `+0x462` and `+0x463`, so the line goes by unshown ([the lifecycle](cockpit-messages.md#the-lifecycle)); when it succeeds, the deadline `+0x143` = the clock + `0x14`, state 1, `Sound_Play`(`0x1c`) unless `Sound_IsPlaying`(`0x1c`), and `HddDisplay_ServiceCommBoxes` while the screen rect is visible. ES2's also hands the MFD the published block and keeps the record.

**`CommBox_BeginMessage` (`0003e060`)**, (display, gauge, record), ES2's (`0044afc8`): the id `+0`'s low byte into the gauge's `+0x12d`; the suffix `_` (`000946b9`) + `ltoa` of the id, a word read unsigned, with a `0` before it below 10 + `ltoa` of the record's attribute byte 1 (`+0x16`, [the queue](cockpit-messages.md#the-queue)), zero-extended (`MOVZX` at `0003e163`), with `00` before it below 10 and `0` below 100, so 0 to 255 in three digits with no sign; and `SpeechLine_Load`(script name, voice name) ([voice lines](sound-catalog.md#voice-lines)) with the script name `P` (`000946b7`) + the byte `A` + portrait + suffix and the voice name `P` (`000946b5`) + `ltoa`(`Pilot_VoiceBankOf`(portrait)) + suffix, so `P1_01000` for id 1, byte 0, in voice bank 1, the shape of the `simvoice\` files. An id of 100 or more gives three digits and a longer suffix. The slot goes to `+0x12f`; it returns whether the slot is set. ES2's names its voice through its language folders.

**`CommBox_OnMessageEnd` (`0003e53c`)**, (record), the end callback: clears the byte `+0x1c` of the subject's gauge. ES2's (`0044b5c0`).

## The state machine — `HddDisplay_ServiceCommBoxes` (`0003e574`)

(display), from the update each frame and from the begin callback; ES2's ([the state machine](../../retail/simulation/heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0044b5f8)) on the clock `000981c4`, which the timer interrupt advances (`Time_SimClockTimerTick`), read on entry: one read gives the count every test in the call compares and the scream's deadline, that count + 5, and a second read right after it the deadline count + `0x14`; a deadline is reached when the clock is at or past it (`<=`). For each gauge with a name:

| State | Does |
|---|---|
| 0 | while the previous state was not 0 or `CommBox_IdleRepaintPending` (`000946bb`) is set, `HddGauge_PaintIdle` and `Widget_DrawToCockpit`(0, the video rect, 1), `CommBox_IdleRepaintPending` set when that copied nothing |
| 1 | static (`HddGauge_PaintStatic`) until the clock reaches the deadline with the latch clear, then state 2 at once |
| 2 | on entry the channel's ready latch `+0x462` and `Snc_Start`(slot), the frame −1 when that returns 0 and otherwise the slot's `int16` `+8`, with the scream's first deadline the clock + 5; after entry the slot's `+8`. At −1, the channel's cancelled latch `+0x463`; then for the scream (id `0x25`, `%`) the latch and state 1, and otherwise state 3, the deadline the clock + `0x14`, sound `0x1c` unless playing, and the latch when the squadmate's machine has `+0x93`; and static. Otherwise `HddGauge_PaintScream` for the scream and `HddGauge_PaintPilotFrame`(display, frame, gauge) for any other |
| 3 | static until the clock reaches the deadline, then state 0 at once |

and the previous state `+0x13f` = the state. ES2's drains the `.SNC` event queue first, marks and cancels the line through `MessagePort_MarkReady` and `MessagePort_Cancel`, and drops the MFD's published block.

**`Snc_Start` (`0004e518`)**, (slot), shows the script's first frame at `+8`, queues the next step and starts the recording, answering 0 when the recording fails to start; the PC timer steps the frame from then on, every second interrupt ([playing a line](../formats/snc-lip-sync.md#playing-a-line)). The recording plays while preferences option 2 has speech on ([the option array](preferences.md#the-option-array)), and the face talks either way.

## The three paints

**`HddGauge_PaintIdle` (`0003df60`)**, (display, slot), while the screen rect is visible, reads the squadmate's machine, `g_SquadmateMachines`[slot], with no null test; its callers run it for a gauge with a name, which `HddGauge_LoadPilotFrames` gives a filled slot alone. With the machine's `+0x93` clear, inside the canvas push and pop, fills the video rect `0x3f` and sets the five labels' text: the name, `STATUS:` (`000bd4a0`, group 29), the condition `000bd46c`[`HddGauge_ConditionIndex`(display, machine)] (group 24), `OBJECTIVE:` (`000bd4a4`) and `000bd4d4`[`Mech_SquadOrderLineIndex`(machine)] (group 34) ([the order line](mech.md#small-functions)); with it set, `HddGauge_PaintStatic` instead. ES2's (`0044ae78`).

**`HddGauge_ConditionIndex` (`0003ded8`)**, (display, machine), is ES2's (`0044adf4`): `Mech_ReadDamageReadouts` into a stack buffer of 42 `int16`, 84 bytes, which it fills whole, entry 0 with the type record's `+0x6c` and 1–41 through `Component_FillDamageReadouts` ([the readouts](damage-readouts.md#the-functions)); then the mean of entries 1–19 as (`0x100` − mean) × 100 ≫ 8, bucketed at 90, 74, 51 and 1 into 0–4.

**`HddGauge_PaintPilotFrame` (`0003e1cc`)**, (display, frame, gauge), while the screen rect is visible: fills the video rect `0x3f` inside the canvas push and pop; installs the box's clip (`CommBox_PushVideoClip`); then

- with `+0x133` set, the head: the bank's frame 24 + *r* mod 3 at pair 24 + *r* mod 3, *r* the `int16` `Math_RandomNext` draws on `000b5c48`, sign-extended to 32 bits and divided as unsigned (`CWDE` and `DIV` at `0003e23f`–`0003e247`), so a negative draw gives (2³² + *r*) mod 3; then the frame at its own pair;
- with it clear, the low-memory bank's frame 6 at pair 24, then frame `HddLowMemFrameMap` (`00094494`)[frame] at the frame's own pair, not the mapped frame's;

each frame the dword at the bank's `+8` array's index, with no test against the bank's count, blitted with `Bitmap_Blit` and flags 0; then `CommBox_PopVideoClip` and the name label's text again inside a push and pop. The map holds 28 `int16`, for frames 0 to 27: 1, 0, 1, 4, 2, 4, 5, 4, 2, 4, 4, 3, 3, 3, 4, 5, 5, 2, 4, 4, 4, 1, 2, 4, 6, 6, 6, 7; the same values as ES2's `DAT_0049d1f6`, which ES2's paint reads only behind a flag its loader always sets. Nothing here bounds the frame: in the low-memory path one of 28 or more reads past the map, and in either path one above 29 reads a pair past the gauge's 30. ES2's (`0044b120`) blits the frame alone, rolling the head's number and drawing nothing with it ([the gauge](../../retail/simulation/heads-down-display.md#the-gauge)), and publishes it for the MFD.

**`HddGauge_PaintScream` (`0003e348`)**, (display, gauge), while the screen rect is visible, is ES2's (`0044b31c`, [the death scream](../../retail/simulation/heads-down-display.md#the-death-scream)): with the clock read once, after the visibility test, at phase `+0x148` 0 frame `0x1b` through `HddGauge_PaintPilotFrame`, and at phase 1 static (`HddGauge_PaintStatic`); after the paint the phase flips once the clock is past the deadline `+0x143`, strictly (`<`, where the state machine's tests are `<=`); when the phase differs from `+0x149`, the deadline = the clock + max(5, `Math_RandomBelow`(`000b5c48`, `0x14`)), compared unsigned; and `+0x149` = the phase.

**`HddGauge_PaintStatic` (`0003e400`)**, (display, gauge), while the screen rect is visible: inside the box's clip, `Bitmap_Blit` with flags 0 of frame `+0x12e` of `HddStaticBank`, the dword at that index of the bank's `+8` array with no test against the bank's count, at `HddStaticFramePos`, which the static initializer zeroes; the frame stepped on and wrapped at 5; then the name label again, inside a canvas push and pop. ES2's (`0044b3b4`) blits through `VideoMode_BlitRotated` with the coordinate offsets added and publishes the frame for the MFD.

**`CommBox_PushVideoClip` (`0003e790`)**, (gauge): `Cockpit_PushCanvasContext`; keeps the installed render context (`000bdbda`) in `CommBox_SavedContext` (`000bd500`); sets the clip context's rect to the gauge's video rect less that context's origin `+0x220`/`+0x224`; and installs it with `Raster_InstallRenderContext`. **`CommBox_PopVideoClip` (`0003e828`)**: `Cockpit_PopRenderContext`. ES2's (`0044b83c`, `0044b8f8`), whose push also takes off the coordinate offsets.

`HddGauge_Name` (`0003e830`), (display, slot), and `HddGauge_FindByName` (`0003e854`) read the names through `HddDisplayInstance` and ignore the display they are given ([callees of other modules](cockpit-panel.md#callees-of-other-modules)); the constructor stores itself there with no test, the one write `es2_xref.py --decode` finds, so they read the last display built. `HddGauge_FindByName`, (display, name), answers the slot 0–2 of the first gauge whose name is set and is a case-insensitive prefix of *name* (`Str_CompareNoCaseN` over the gauge name's length), or −1; `es2_xref.py` finds one call, from `Squad_SlotOfPilotName` (`0002e314`, [squadmate and pilot helpers](cockpit-panel.md#squadmate-and-pilot-helpers)), and no branch, stored pointer or vtable slot reaching that one or its padding, where it finds `Squad_PilotName`'s call of `HddGauge_Name` ([Open](cockpit-panel.md#open)). `Squad_PilotName`, which reads `HddGauge_Name`, gives the outside view's caption its squadmate's name ([the views](external-views.md#the-views--viewchain_apply-0005894c)), so the caption shows the same `str\pilots` entry the box does.

## Callees of other modules

Read for this file and named with it:

- **`Snc_Start` (`0004e518`)**, after `Snc_Load` (`0004e4a8`), [above](#the-state-machine--hdddisplay_servicecommboxes-0003e574).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `CommBox_ElementCtor`, `Point_DefaultCtorHddCopy` | `0003cab8`, `0003cb74` | `0044c0e7`, `Rtl_ReturnArg` |
| `HddGauge_LoadPilotFrames`, `HddGauge_ConditionIndex`, `HddGauge_PaintIdle`, `CommBox_BeginMessage` | `0003dab4`, `0003ded8`, `0003df60`, `0003e060` | `0044a7c0`, `0044adf4`, `0044ae78`, `0044afc8` |
| `HddGauge_PaintPilotFrame`, `HddGauge_PaintScream`, `HddGauge_PaintStatic` | `0003e1cc`, `0003e348`, `0003e400` | `0044b120`, `0044b31c`, `0044b3b4` |
| `CommBox_OnMessageBegin`, `CommBox_OnMessageEnd`, `HddDisplay_ServiceCommBoxes`, `CommBox_PushVideoClip`, `CommBox_PopVideoClip` | `0003e488`, `0003e53c`, `0003e574`, `0003e790`, `0003e828` | `0044b4ec`, `0044b5c0`, `0044b5f8`, `0044b83c`, `0044b8f8` |
| `HddGauge_Name`, `HddGauge_FindByName` | `0003e830`, `0003e854` | `0044b900`, `0044b928` |
| `Sim_SetCommBox0PilotOverride` | `0003ea50` | `0044ba9c` |

## Globals

| | Address |
|---|---|
| `HddLowMemFrameMap`, 28 `int16` | `00094494` |
| `HddStaticBank`, `dba\static` | `000944d0` |
| `HddPilotNameTable`, `str\pilots` | `000944d4` |
| `CommBox0PilotOverride`, −1 in the image | `000944d8` |
| `CommBox_IdleRepaintPending` (byte) | `000946bb` |
| `CommBox_ClipContext`, `HddStaticFramePos` | `000b15f8`, `000b15fc` |
| `CommBox_SavedContext` | `000bd500` |

`HddDisplay_ServiceCommBoxes`'s jump table is at `000946bc`.

## Open

- **Open:** what reads the gauge's byte `+0x1c` that `CommBox_OnMessageEnd` clears: `es2_fieldscan.py --binary ES1DBSIM` over this file finds that write and no access through a gauge's base.
- **Open:** what `str\pilots` and the `ofs\pilot<n>` files hold, through HercWorks.Core, whose query tool's `strings` command reads `STRINGS0.STR` alone: how many names `str\pilots`'s first group holds against the largest portrait number, pilot / 3, a mission's pilots reach, and so whether a comm box's name ever comes from past that group ([who is in it](#who-is-in-it--hddgauge_loadpilotframes-0003dab4), step 2), and which of the 27 entries places frame 27, the scream's.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0003dab4` fires once for each squadmate as the mission starts, with the pilot index at `[ESP+8]`; and by eye, a squadmate's message shows static in its comm box, then its face talking, then static again.
