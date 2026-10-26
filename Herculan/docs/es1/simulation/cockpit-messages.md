# DBSIM.EXE — the cockpit's message boxes

**ES1's cockpit has ES2's two message boxes, the computer's ticker and the pilot channel, built on ES2's port class: each keeps a queue of up to ten messages in priority order and puts them up and takes them down by ES2's timings.** ES1's ticker centers its line in a plain box instead of scrolling it in a framed one, swallows no repeats and plays a tone for ids up to `0x21` only; neither box's show plays speech itself; both are posted through one function rather than a virtual slot; a squadmate who leads its group can speak from `commandr.str`, and a line from no squadmate is signed `XXXX`. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's, described in ES2's [message port](../../retail/simulation/cockpit-message-port.md) and [cockpit messages](../../retail/simulation/cockpit-messages.md). Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `0002e444`–`0002fe5b` and the initialized data `0009306e`–`0009326b`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `0002e444`, `0002e46c`, `0002e4ab` (frameless) and `0002e4b4`, each the code of `panel.cpp`'s ([where `panel.cpp` ends](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `0009306e` and `000930a2`. No assert in the file names a source path, and the data holds none.

The code ends with the module's static initializer and its exit routine, both frameless ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

- **`MessagePort_StaticInit` (`0002fe00`)**, entry `000adc30` of the static constructor table: builds the three pilot string tables at `000b0e14`, `0x10` bytes each, with `__vec_ctor` and `StringTable_Ctor` (`00044f14`), then the command table `000b0e44` and the system table `000b0e54` with `StringTable_Ctor`. ES2's `MessagePort_StaticInit` (`00437270`).
- **`MessagePort_StaticDtor` (`0002fe28`)**, entry `000adcb4` of the exit table: `OwnedArray_Dtor` (`00044f48`)(table, 2) on `000b0e54` and `000b0e44`, then `__vec_dtor` of the three pilot tables with it. ES2's (`004372c4`).

Its `RET` at `0002fe5b` is followed by the next object file at `0002fe5c`, already on a 4-byte boundary: [the string table's](../formats/str-strings.md#where-the-object-file-starts-and-ends).

The data holds, after the run's tables, the byte `MessagePort_ShowEnabled` (`000930d2`, [the lifecycle](#the-lifecycle)); the empty string `000930d3`; the module's copies of the region classes' vtables, `ClassItem` (`000930d6`), `GLBase` (`000930fa`), `GLRegion` (`00093122`) and `GLRectangleRegion` (`0009314a`); `MsgPort`'s table (`00093172`); the strings `system`, `commandr`, `pilotX` and `str`; the pilot channel's table (`000931b6`); `XXXX` and `": "`; and the ticker show's 34 jump targets (`000931e4`). The ticker's own table `00092f0a` is `panel.cpp`'s ([the message boxes' vtable](cockpit-panel.md#the-message-boxes-vtable)).

## The port object

**The object is ES2's ([the object](../../retail/simulation/cockpit-message-port.md#the-object)) with the vtable after the fields instead of before them, a record 5 bytes shorter and no pause or suppression fields.**

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x000` | `+0x004` | the message queue ([below](#the-queue)) |
| `+0x1ec` | `+0x222` | the message log ([below](#the-message-log)) |
| `+0x432` | `+0x49a` | the current message |
| `+0x436` | `+0x49e` | a line is up |
| `+0x437` | `+0x49f` | the box, a type-0 rectangle region: its type at `+0x43b`, its rect at `+0x43f`–`+0x44b` |
| `+0x44f`, `+0x450` | `+0x4b7`, `+0x4b8` | the begin and end callback counts |
| `+0x451`, `+0x459` | `+0x4b9`, `+0x4c1` | two begin callbacks, two end callbacks |
| `+0x461`, `+0x462`, `+0x463` | `+0x4c9`, `+0x4ca`, `+0x4cb` | the due, ready and cancelled latches |
| `+0x464` | `+0x000` | the vtable |

ES1's has nothing in the place of ES2's `+0x4cc`, `+0x4d0` and `+0x4d2`. The ticker is `0x469` bytes, its blink latch the byte `+0x468` (ES2's `+0x4d3`), and keeps no copy of its rect; the pilot channel is `0x474` bytes, its two buffers at `+0x468` and `+0x46c` (ES2's `+0x4d3`, `+0x4d7`).

### The queue

ES2's queue ([the queue](../../retail/simulation/cockpit-message-port.md#the-queue)) with ten `0x2c`-byte records, the order at `+0x1b8`, the in-use bytes at `+0x1e0` and the `int16` count at `+0x1ea`. A record holds the id at `+0`, the poster's record's bytes `+2`–`+0x15` at `+2`, attribute byte 1 at `+0x16`, the text pointer at `+0x17`, attribute byte 2, the priority, at `+0x1b`, and the four timings at `+0x1c`–`+0x28` as ES2's; ES1 reads attribute bytes 0 to 6, and the record ends at `+0x2c`, where ES2's holds bytes 7 to 11. Now is the dword `000981c4` throughout the file, where ES2's calls `Time_GetCoarseTicks`.

| ES1 | Address | ES2 |
|---|---|---|
| `MessagePort_QueueInit`, (queue) | `0002e4e0` | `00434e50`; allocates `0x1ec` bytes when given none, and builds the records with `__vec_ctor` and `QueuedMessage_ElementCtor` (`0002e538`), which returns the record, allocating `0x2c` bytes when given none |
| `MessagePort_Enqueue`, (queue, entry, record) | `0002e59c` | `00434e8c` |
| `MessagePort_QueueFront`, `MessagePort_QueueAt` | `0002e768`, `0002e790` | `00435074`, `004350d4` |
| `MessagePort_RemoveAt`, `MessagePort_QueueDropLast`, `MessagePort_QueueExpire` | `0002e7c8`, `0002e85c`, `0002e898` | `00435140`, `004351c0`, `004351ec` |
| `MessagePort_QueueRemoveMatch`, (queue, id, subject) | `0002e908` | `00435240` |

## Building the two ports

- **`MessagePort_CtorBase` (`0002eb80`)**, (port, rect): ES2's on ES1's offsets, the region's vtables the module's copies and `MsgPort`'s table `00093172` at `+0x464`; it clears the current message, `+0x436`, the callback counts and the latches. Then, when `StrTable_FirstString` (`00045114`)(`000b0e54`) returns 0, it loads `str\system.str` with `SystemMessages_Index`(port, 0, −1) ([the string files](#the-string-files)). ES2's loads nothing.
- **The ticker** is built by `Cockpit_LoadPanel` ([building the cockpit](cockpit-panel.md#building-it)), which inlines ES2's `MessagePort_Ctor`: `MessagePort_CtorBase`, the table `00092f0a`, and 0 at `+0x468`.
- **`DialogPort_Ctor` (`0002f3d8`)**, (port, rect), the pilot channel: allocates `0x474` bytes when given none, returning 0 when that fails; `MessagePort_CtorBase`, the table `000931b6`, and two buffers of `Region_PixelCount`(`+0x43b`) bytes from `operator new` at `+0x468` and `+0x46c`. ES2's (`00435bf0`). The rect is the `.GAU` record's at 1668, (0, 100)–(320, 110) in all eight retail layouts (`HercWorks.Query gau --block 1668`), so each buffer holds 321 × 11 = 3,531 bytes; a line's box is the rect's 11 rows by *w* + 21 columns ([the pilot channel](#the-pilot-channel)), so a line wider than 300 pixels saves past them ([Open](#open)).

### The slots

ES1's tables start their slots at `+4` ([vtables](../formats/compiler.md#vtables)), so ES2's slot `+0x00`, the post, has no place: ES1 posts through one function ([posting](#posting)). The other slots are at ES2's offsets:

| Slot | `MsgPort` (`00093172`) | the ticker (`00092f0a`) | the pilot channel (`000931b6`) |
|---|---|---|---|
| `+0x04`, show | 0 | `MessagePort_Show` (`0002f96c`) | `PilotMessagePort_Show` (`0002f4f4`) |
| `+0x08`, the line's text | 0 | `MessagePort_SetText` (`0002f934`) | `PilotMessagePort_ComposeLine` (`0002f454`) |
| `+0x0c`, take down | 0 | `MessagePort_Erase` (`0002fb18`) | `PilotMessagePort_Erase` (`0002f7ec`) |
| `+0x10`, per frame | `MessagePort_Tick` (`0002ef58`, by its padding `0002ef56`) | `MessageTicker_Update` (`0002fab8`) | `PilotMessagePort_Update` (`0002f8c4`) |
| `+0x14`, put the background back | `MessagePort_UnpaintNoOp` (`0002ed7e`) | `MessagePort_UnpaintNoOpPanelCopy` (`0002cb41`) | `PilotMessagePort_RestoreBackground` (`0002fb74`) |
| `+0x18`, paint | `MessagePort_PaintNoOp` (`0002ed83`) | `MessageTicker_Paint` (`0002fa00`) | `PilotMessagePort_RedrawFromSaveUnder` (`0002fbd0`) |
| `+0x1c`, the box's rect | `MessagePort_GetRect` (`0002ed87`) | `MessagePort_GetRectPanelCopy` (`0002cb47`) | `MessagePort_GetRect`, entered past its `NOP` at `0002ed88` |

`MsgPort`'s three zero slots are where ES2's `MsgPort` holds `Rtl_PureVirtualCalled`. The stubs are frameless: `0002ed7e` is two `NOP`s and `RET 4`, `0002ed83` a `NOP` and `RET 4`, and `0002ed87` a `NOP` and then the port plus `0x43f` returned with `RET 4`.

## Posting

**One function posts to either box: a line about no machine comes from `system.str`, and a line about a machine from its squadmate slot's pilot file or, for ids from `0x32` on spoken by the first member of a group, from `commandr.str`.**

**`MessagePort_Post` (`0002ed94`)**, (port, record), in place of ES2's two slot-0 posts ([posting](../../retail/simulation/cockpit-message-port.md#posting)), with the byte id at the record's `+0` and the subject at `+2`:

- With no subject, the slot `000bd0ec` + id × 9 against the system table `000b0e54`.
- Otherwise the squadmate slot is `Squad_IndexOf`(subject) as a byte; above 3, which −1 is, the record's subject becomes the first squadmate, `g_SquadmateMachines` (`000bcc16`), and the slot 0. For an id below `0x32`, or a subject that is not the first member of its group (entry 0 of the member array `+0xe` of the group at the machine's `+0x3f`), the slot `000bcc22` + slot × `0x183` + id × 9 against the pilot table `000b0e14` + slot × `0x10`; otherwise the slot `000bceea` + id × 9, that is `000bd0ac` + (id − `0x32`) × 9, against the command table `000b0e44`.

It then rolls and queues as ES2's posts do: with a count byte (`+4`) of 2 or more the variant is the low word of `Math_RandomNext`(`000b5c48`), unsigned, modulo the count, else 0; a nonzero variant goes through **`MessagePort_StepToVariant` (`0002f31c`)**, (port, *n*, slot, table), ES2's (`00435a74`), which builds its slot at `MessagePort_VariantSlot` (`000bd21e`) from the string after the slot's own text whatever *n* is ([posting](../../retail/simulation/cockpit-message-port.md#posting)); and the slot goes to `MessagePort_Enqueue` when its text and its attributes are both set. Where ES2's channel goes on with slot 255 for a subject in no slot ([rejected readings](../../retail/simulation/cockpit-message-port.md#rejected-readings)), ES1's gives the line to the first squadmate.

`es2_xref.py` finds six calls: `Computer_PostMessage` (`0001a154`) posts {id, 0} to the cockpit's `+0x207`, the ticker; **`Ai_PostSquadMessage` (`0001a188`)**, (machine, force, id), posts {id, machine} to the port `CockpitView_GetSquadMessagePort` returns, the pilot channel, unless the machine's byte `+0x93` is set and *force* is 0, ES2's (`00420a98`) with `+0x99` and a call through slot 0; and `HddDisplay_KeyDispatch` (`0003d12c`) three times, for its `Ctrl`+`Alt` keys, and `HddDisplay_SelectPilot` (`0003da08`) once, {`0x22`, the squadmate} as a pilot is selected, both to the pilot channel ([the Heads-Down Display's keys](heads-down-display.md#keys), [presses](heads-down-display.md#presses)).

### The string files

**`SystemMessages_Index` (`0002f1ec`)**, (port, digit, which), loads one file from `str\` (`ResourcePath_BuildFolderName`(`str`, name)) with `StringTable_LoadFromFile` and indexes it as ES2's does ([posting](../../retail/simulation/cockpit-message-port.md#posting)):

| *which* | File | Table | Index |
|---|---|---|---|
| −1 | `system` | `000b0e54` | `000bd0ec` |
| −2 | `commandr` | `000b0e44` | `000bd0ac` |
| a slot, 0–2 | `pilot<digit>` | `000b0e14` + slot × `0x10` | `000bcc22` + slot × `0x183` |

For a slot whose squadmate is the first member of its group it first loads `commandr` with *which* −2. Each string goes to the 9-byte slot at the index plus its attribute byte 0 × 9, which takes {text, attributes at `+5`} while its count byte `+4` is 0, and the count goes up by 1. It indexes the file's first group alone: its loop ends the first time `StringTable_ReadNext` (`00045004`) answers 0, which that function does at a group's end as well as at the buffer's ([the string table](../formats/str-strings.md)), where ES2's loop stops only on two such answers in a row and so indexes every group. ES2's takes a mode, 0 to 2, names its command file `commandX` with the training mission's number, and finds its files by language.

**`str\system.str`** holds one group of 34 strings, each with 7 attribute bytes; their ids, attribute byte 0, are 0 to `0x21`, one string each, so every slot's count is 1 and no post of a computer line rolls a variant; and every priority, byte 2, is 0, so a post always goes to the queue's end. Id 0 is `WAYPOINT REACHED`, 5 `MISSION TARGET DETECTED`, and `0x1d` to `0x21` the data link's lines ([the player's think](mech-ai.md#the-players-think--mech_behaviourplayerthink-00015f80)). The system index `SystemMessageIndex` holds those 34 slots, `000bd0ec` to `000bd21d`, and `MessagePort_VariantSlot` follows it at `000bd21e`. `MessagePort_CtorBase` loads `system`; `es2_xref.py` finds the third call in `HddGauge_LoadPilotFrames` (`0003dab4`), which `Cockpit_LoadSquadmatePilots` calls for each squadmate ([the squadmates](cockpit-panel.md#the-squadmates)), with the squadmate's slot and the digit `Pilot_VoiceBankOf` of its portrait number ([who is in it](heads-down-display.md#who-is-in-it--hddgauge_loadpilotframes-0003dab4)).

**The command index.** `SystemMessages_Index` puts each `commandr` string at `CommandMessageIndex` (`000bd0ac`, the base it sets at `0002f23e`) + attribute byte 0 × 9, and `MessagePort_Post` reads a group leader's line of id *n*, `0x32` or more, at `000bceea` + *n* × 9 (`0002edf8`–`0002ee03`), which is `000bd0ac` + (*n* − `0x32`) × 9. So the post finds the string whose byte 0 is *n* − `0x32`: `commandr`'s line for id `0x32` is the one whose byte 0 is 0. The 64 bytes from `000bd0ac` to the system index `000bd0ec` hold seven slots, byte 0 from 0 to 6; a higher byte 0, and a post of id `0x39` or more, reach into the system index. What `commandr.str`'s byte 0 holds is [Open](#open). The three pilot indexes, `0x183` bytes each, hold 43 slots, ids 0 to `0x2a`, and end at `000bd0ab`, just before the command index.

## The lifecycle

**`MessagePort_Tick` (`0002ef58`)** is ES2's ([the port](../../retail/simulation/cockpit-messages.md#the-port)) with three differences: it has no pause field to wait on (ES2's `+0x4cc`), takes no line down for a dead player, and calls the show only while `MessagePort_ShowEnabled` (`000930d2`) is set. With that byte clear a due and ready message is counted as shown without being shown: its two display times become deadlines and the due latch clears, and no line goes up. The byte is 1 in the image; the frameless `0004ad30`, (value), sets it to *value* ≠ 1 and the byte `000959e4` to *value* ≠ 0, the shape of ES2's two message preferences ([the port](../../retail/simulation/cockpit-messages.md#the-port)), and the ticker's update sets it to 1 around its own tick, so only the pilot channel obeys it ([Open](#open)).

Each tick, with *now* the clock, every comparison unsigned and "past" strictly after:

1. `MessagePort_QueueExpire` (ES2's); with no current message `+0x432` the queue's front becomes it, and with none the tick ends.
2. **Ready `+0x462` or cancelled `+0x463` set, with due `+0x461` set and cancelled clear: the show.** While `MessagePort_ShowEnabled` is set it calls the show, slot `+4`, and the tick ends when that returns 0; then, or at once with the byte clear, it adds *now* to the current message's `+0x1c` and `+0x20`, clears due, and the tick ends.
3. **Ready or cancelled set otherwise: the take-down.** The tick ends unless cancelled is set, *now* is past `+0x20`, or *now* is past `+0x1c` with 2 or more queued (`+0x1ea`). It calls the take-down, slot `+0xc`, and when that returns non-zero removes the front with `MessagePort_RemoveAt`(queue, 0) and clears the current message, cancelled and ready.
4. **Neither set.** With *now* past `+0x28` it removes the front unshown and clears the current message, `+0x436`, cancelled and ready; otherwise, with *now* past `+0x24` and due clear, it sets due, clears ready and calls each begin callback (`+0x451`, count `+0x44f`) with the current message.

- **`MessagePort_Withdraw` (`0002f370`)**, (port, id, subject): ES2's (`00435ac8`). `es2_xref.py` finds one call, from `Squad_SendOrderToSlot` ([the squadmates](cockpit-panel.md#the-squadmates)).
- **`MessagePort_AddBeginCallback` (`0002eed8`)** and **`MessagePort_AddEndCallback` (`0002ef18`)**, (port, callback): ES2's (`004355a8`, `004355dc`). `es2_xref.py` finds one call of each, both in `HddDisplay_Ctor` (`0003c258`), the Heads-Down Display's constructor, which adds `CommBox_OnMessageBegin` (`0003e488`) and `CommBox_OnMessageEnd` (`0003e53c`) to the pilot channel ([starting a line](heads-down-display.md#starting-a-line)).
- **`MessagePort_LineOverlapsRect` (`0002f10c`)**, (port, rect): ES2's (`00435920`) less the suppression test. `es2_xref.py` finds no reference to it ([Open](#open)).

## The ticker

**The computer's lines appear centered in a plain box, blink when the line asks for it, and come with a tone; nothing scrolls and nothing is spoken.**

- **`MessagePort_Show` (`0002f96c`)**, (port): with none of ES2's tests and no repeat window ([`MessagePort_Show`](../../retail/simulation/cockpit-messages.md#the-port)), sets the text (slot `+8`), sets `+0x436`, paints (slot `+0x18`), runs `MessagePort_CopyBoxBetweenPages`(port, 1) and plays the tone for the id through the table `000931e4`, which covers ids 0 to `0x21`: `Sound_Play`(`0x17`) for `0x10`, `0x12`, `0x1b` and `0x1c`; `0x18` for 8, `0xb`–`0xd`, `0xf`, `0x11`, `0x13` and `0x14`; `0x19` for 6 and `0xe`; and `0x1a` for every other id up to `0x21`, `0x1f` also setting the blink latch `+0x468` and `0x20` and `0x21` clearing it. A higher id plays nothing. It returns 1.
- **`MessagePort_SetText` (`0002f934`)**, (port, record): ES2's (`00436a0c`) into `000b0eb4`.
- **`MessageTicker_Paint` (`0002fa00`)**, (port): between `Cockpit_PushCanvasContext` and `Cockpit_PopRenderContext`, fills the box's rect with the brush {0, 63}; then, while a line is up and the blink latch is clear or bit `0x20` of `000981c4` is set, draws the text in `ColorSchemePanels`[2], `cpred` (`00092d48`), at *x* = *x0* + (*x1* − *x0*) / 2 − width / 2 and *y* = *y1* − 1, each halving an arithmetic shift. ES2's [paint](../../retail/simulation/cockpit-messages.md#the-ticker) frames the box, narrows the clip, scrolls the text and centers the cell vertically.
- **`MessageTicker_Update` (`0002fab8`)**, (port), slot `+0x10`: sets `MessagePort_ShowEnabled` to 1; while a line is up and the view manager's `+0x18` is clear, paints; runs `MessagePort_Tick`; sets the ready latch when the due latch is set; and puts `MessagePort_ShowEnabled` back. ES2's `MessageTicker_ScrollText` (`00436f70`) sets the ready latch before its tick, which comes to the same one frame between due and shown.
- **`MessagePort_Erase` (`0002fb18`)**, (port): fills the box with {0, 63}, runs `MessagePort_CopyBoxBetweenPages`(port, 1), clears `+0x436` and returns 1, whether or not a line is up; ES2's (`00436fd0`) acts only on a line that is up and not suppressed and also clears the blink latch.

## The pilot channel

**A squadmate's line appears in a box of the squadmate's color, sized to the line and centered on the 320-wide screen, with the speaker's name before it.** The show plays no sound itself, where ES2's speaks: the speaker's comm box on the Heads-Down Display loads the line's voice and lip-sync script as the line becomes due and starts them as its face begins to talk, which is also when it makes the line ready ([starting a line](heads-down-display.md#starting-a-line), [the state machine](heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0003e574)).

- **`PilotMessagePort_Show` (`0002f4f4`)**, (port), ES2's `PilotMessagePort_Speak` (`00435d9c`): returns 0 while the view manager's `+0x18` is set. Otherwise it composes the line (slot `+8`), sets `+0x436` and makes the display's (`000bdd28`) `+0x8c` its source page `+0x38`, with `00098dd6` and the routine `0009df14` when that display is current. Between the push and the pop, with *w* the line's width in `cpred` (`HudFont_MeasureString`) and (*ox*, *oy*) the canvas origin (the context's `+0x220`, `+0x224`):
  1. it writes the box into the region's own rect: *x0* `+0x43f` = *ox* + 150 − *w* / 2 and *x1* `+0x447` = *x0* + *w* + 20, the rect's *y0* `+0x443` and *y1* `+0x44b` kept, so the box spans *w* + 21 columns centered on column *ox* + 160;
  2. saves the screen under the box into `+0x468` with `Region_Save`, from the `+0x8c` page;
  3. fills the box with the brush {0, fill} and frames it with {4, frame}, where a squadmate slot from `Squad_IndexOf` of the current message's subject gives the fill `HudColorTable_Get`(slot) and the frame one below it, and no slot gives 63 and 53;
  4. draws the line at (*ox* + 160 − *w* / 2, *y1* − 1);
  5. runs `MessagePort_CopyBoxBetweenPages`(port, 1), the box from `+0x8c` onto `+0x88`, which leaves `+0x8c` the source page;
  6. saves the box as painted, from that page, into `+0x46c`;
  7. shows the cursor (`g_RasterRoutines` slot 30) unless the view is 4, makes the `+0x8c` page the display's draw page `+0x3c`, and returns 1.

  The box stays in the rect: the take-down, the two save-under slots and `MessagePort_CopyBoxBetweenPages` all work on it, `MessagePort_ShiftRect` moves it with the view changes ([`CockpitView_ApplyViewState`](cockpit-views.md#cockpitview_applyviewstate-000222c0)), and the next show writes its *x0* and *x1* again. The two saves add (*ox*, *oy*) to the box for the `Region_Save` call and take it off after; the restores pass the box as it stands. That is retail's arithmetic, and it lands each save on the pixels a draw at the box covers: the canvas context translates by its rect's corner, (0, 0), less its origin, `Raster_FillRect` and `Raster_BlitDispatch` move a rect or a point by that once, and `Region_SaveRect` and `Bitmap_GrabClipDispatch` move it twice ([lines and clipped bitmaps](../rendering/screen-pages.md#lines-and-clipped-bitmaps)), so the add cancels the second move. ES2's speaks, tests the preferences and the player's machine, centers on the screen's width with the coordinate shift, centers the ink vertically and falls back to `COLORS.DAT` ids 19 and 9 ([its box](../../retail/simulation/cockpit-messages.md#its-box)).
- **`PilotMessagePort_ComposeLine` (`0002f454`)**, (port, record): ES2's (`00435d0c`) into `000b0e64`, signing a record with no squadmate slot, a null subject included, `XXXX` (`000931da`) where ES2's signs `HQ`.
- **`PilotMessagePort_Erase` (`0002f7ec`)**, (port), the take-down: returns 1 at once while no line is up (`+0x436`), before its view-change test, and 0 while the view manager's `+0x18` is set; otherwise calls each end callback (`+0x459`, count `+0x450`) with the current message, then puts the screen back from `+0x468` with `Region_Restore` at the box between the push and the pop, runs `MessagePort_CopyBoxBetweenPages`(port, 1), clears `+0x436` and returns 1. So `CommBox_OnMessageEnd` runs before the line leaves the screen. ES2's (`00436130`) also tests the suppression byte ([taking a line down](../../retail/simulation/cockpit-message-port.md#taking-a-line-down)).
- **`PilotMessagePort_Update` (`0002f8c4`)**, (port), slot `+0x10`, which `Sim_RenderFrame` calls through the channel's vtable once a frame, after the gunsight's slot ([the frame](main-loop.md#the-frame)): while a line is up and the view manager's `+0x18` is clear, makes the `+0x8c` page the source page and runs slot `+0x18`; then `MessagePort_Tick`. It sets no ready latch, where ES2's (`004361cc`) sets it for a line whose record `+0x2c` is set; ES1's latch `+0x462` is set, besides this file's writes, by `CommBox_OnMessageBegin` and `HddDisplay_ServiceCommBoxes` ([the state machine](heads-down-display.md#the-state-machine--hdddisplay_servicecommboxes-0003e574)), as `es2_fieldscan.py --writes-only 462` finds.
- **`PilotMessagePort_RestoreBackground` (`0002fb74`)**, slot `+0x14`, (port): while a line is up, puts the screen under the box back from `+0x468` with `Region_Restore` at the box, between the push and the pop. **`PilotMessagePort_RedrawFromSaveUnder` (`0002fbd0`)**, slot `+0x18`, (port): while a line is up, between the push and the pop, saves the screen under the box into `+0x468` from the source page, with the canvas origin added as the show's saves add it, and puts the painted box back from `+0x46c`. So `+0x14` takes the line off the draw page and `+0x18` puts it back, first keeping what is under it. ES2's (`00437044`, `00437094`, [the save-unders](../../retail/simulation/cockpit-message-port.md#the-pilot-channels-save-unders)) also test the suppression byte and the view-change flag. Through the channel's vtable, `Cockpit_DrawFrame` runs `+0x14` each frame no view change is staged ([drawing it](cockpit-panel.md#drawing-it)), `PilotMessagePort_Update` runs `+0x18`, and the view changes ([the view command](cockpit-views.md#cockpitview_processviewcommand-000226f0), [the slide](cockpit-views.md#cockpitview_stepviewtransition-00022904)) and the Heads-Down Display's copy ([the paint stages](heads-down-display.md#drawing-it)) run them around their own drawing.

**`MessagePort_CopyBoxBetweenPages` (`0002fcd4`)**, (port, direction), is ES2's ([taking a line down](../../retail/simulation/cockpit-message-port.md#taking-a-line-down)) on the region at `+0x437`, the offset (0, 0), while a line is up: direction 0 copies the box from the display's `+0x88` page to `+0x8c`, and 1 from `+0x8c` to `+0x88`. It shows the cursor again unless the view manager's view `+0x10` is 4 and `AlertPanel_OpenCount` (`00094d78`) is 0, so with a panel up it shows it in view 4 too ([putting a panel up](alert-panels.md#putting-a-panel-up-and-taking-it-down)), and it puts back the draw page alone, leaving the source page as it set it. **`MessagePort_ShiftRect` (`0002eea0`)**, (port, delta), subtracts the delta's *x* from *x0* and *x1* and its *y* from *y0* and *y1* of the rect at `+0x43f`.

## The message log

ES2's log ([the message log](../../retail/simulation/cockpit-message-port.md#the-message-log)) on the queue at `+0x1ec`, its strings at the queue's `+0x1ec` + index × 9: `MessagePort_StampedQueueAppend` (`0002e994`), (queue, entry, record), whose time is `CockpitClock_FormatTime` (`00043734`) of the cockpit clock ([the cockpit clock](../formats/ttm-scripts.md#the-cockpit-clock)); `MessagePort_StampedQueueRemoveAt` (`0002eaac`, also by its padding `0002eaaa`); and `MessagePort_StampedQueueTimeAt` (`0002eb40`), returning `000930d3`, empty, past the count. ES1 adds two readers: `MessagePort_StampedQueueComposeAt` (`0002f170`), (port, index), returns slot `+8`'s line for the logged message at *index*, and `MessagePort_StampedQueueShownTimeAt` (`0002f19c`), (port, index), its time when its `+0x1c` is not after now, else `00093196`, empty. `es2_xref.py` finds no reference to `MessagePort_StampedQueueAppend` or to these two ([Open](#open)).

## Callees of other modules

Read for this file and named with it:

- **`StringTable_Ctor` (`00044f14`)**, (table): allocates `0x10` bytes when given none, clears the buffer, cursor, length and the two `int16` counters, and returns the table; ES2's (`004537e0`) allocates nothing.
- **`OwnedArray_Dtor` (`00044f48`)**, (holder, flags): frees the block the holder points at, then the holder when bit 0 of *flags* is set, with `operator delete`; ES2's (`00453804`).
- **`StrTable_FirstString` (`00045114`)**, **`StrTable_NextString` (`0004513c`)** and **`StrTable_GetAttributes` (`00045194`)**: ES2's (`004539b0`, `004539cc`, `00453a08`), the first two identical after normalization.
- **`CockpitClock_FormatTime` (`00043734`)**, (clock), frameless and not reading its argument: the hours `000bd504`, `:` and the minutes `000bd508` into `000b16b4`, ES2's `PanelAmbience_FormatClock` (`00452218`).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMsgPortCopy`, `ConfigPart_DtorMsgPortCopy`, `ConfigPart_GetClassTagMsgPortCopy`, `TSPartBase_DtorMsgPortCopy` | `0002e444`, `0002e46c`, `0002e4ab`, `0002e4b4` | the run; slots `+0x1c` and `+4` of `0009306e` and `+0x1c` of `000930a2` (`0002e4b4` by its padding `0002e4b1`) |
| `MessagePort_QueueInit`, `QueuedMessage_ElementCtor`, `MessagePort_Enqueue` | `0002e4e0`, `0002e538`, `0002e59c` | `00434e50`, `00437558`, `00434e8c` |
| `MessagePort_QueueFront`, `MessagePort_QueueAt`, `MessagePort_RemoveAt`, `MessagePort_QueueDropLast`, `MessagePort_QueueExpire`, `MessagePort_QueueRemoveMatch` | `0002e768`, `0002e790`, `0002e7c8`, `0002e85c`, `0002e898`, `0002e908` | `00435074`, `004350d4`, `00435140`, `004351c0`, `004351ec`, `00435240` |
| `MessagePort_StampedQueueAppend`, `MessagePort_StampedQueueRemoveAt`, `MessagePort_StampedQueueTimeAt` | `0002e994`, `0002eaac`, `0002eb40` | `004352ec`, `00435408`, `00435458` |
| `MessagePort_CtorBase` | `0002eb80` | `00435490` |
| `GLBase_DtorMsgPortCopy`, `GLRegion_DtorMsgPortCopy`, `GLRectangleRegion_DtorMsgPortCopy` | `0002ed04`, `0002ed28`, `0002ed50` | slot `+0x1c` of `000930fa`, `00093122` and `0009314a` (the first two by their padding `0002ed02`, `0002ed26`); the code of `panel.cpp`'s copies |
| `MessagePort_UnpaintNoOp`, `MessagePort_PaintNoOp`, `MessagePort_GetRect` | `0002ed7e`, `0002ed83`, `0002ed87` | `0043757e`, `00437590`, `00437583` |
| `MessagePort_Post`, `MessagePort_AddBeginCallback`, `MessagePort_AddEndCallback`, `MessagePort_Tick`, `MessagePort_LineOverlapsRect` | `0002ed94`, `0002eed8`, `0002ef18`, `0002ef58`, `0002f10c` | `MessagePort_PickVariant` (`00436a3c`) and `PilotMessagePort_Post` (`00435c48`), `004355a8`, `004355dc`, `00435610`, `00435920` |
| `MessagePort_StampedQueueComposeAt`, `MessagePort_StampedQueueShownTimeAt` | `0002f170`, `0002f19c` | none |
| `SystemMessages_Index`, `MessagePort_StepToVariant`, `MessagePort_Withdraw`, `DialogPort_Ctor` | `0002f1ec`, `0002f31c`, `0002f370`, `0002f3d8` | `00435970`, `00435a74`, `00435ac8`, `00435bf0` |
| `PilotMessagePort_ComposeLine`, `PilotMessagePort_Show`, `PilotMessagePort_Erase`, `PilotMessagePort_Update` | `0002f454`, `0002f4f4`, `0002f7ec`, `0002f8c4` | `00435d0c`, `PilotMessagePort_Speak` (`00435d9c`), `00436130`, `004361cc` |
| `MessagePort_SetText`, `MessagePort_Show`, `MessageTicker_Paint`, `MessageTicker_Update`, `MessagePort_Erase` | `0002f934`, `0002f96c`, `0002fa00`, `0002fab8`, `0002fb18` | `00436a0c`, `00436abc`, `00436cec`, `MessageTicker_ScrollText` (`00436f70`), `00436fd0` |
| `PilotMessagePort_RestoreBackground`, `PilotMessagePort_RedrawFromSaveUnder`, `MessagePort_CopyBoxBetweenPages` | `0002fb74`, `0002fbd0`, `0002fcd4` | `00437044`, `00437094`, `00437164` |
| `MessagePort_StaticInit`, `MessagePort_StaticDtor` | `0002fe00`, `0002fe28` | `00437270`, `004372c4` |

## Globals

| | Address |
|---|---|
| `MessagePort_ShowEnabled` (byte, 1) | `000930d2` |
| `MsgPortVtable`, `DialogPortVtable` | `00093172`, `000931b6` |
| `MessagePort_ToneTable`, 34 jump targets | `000931e4` |
| `PilotStringTables` (three), `CommandStringTable`, `SystemStringTable` | `000b0e14`, `000b0e44`, `000b0e54` |
| `PilotMessagePort_LineBuffer`, `MessagePort_TextBuffer` | `000b0e64`, `000b0eb4` |
| the pilot, command and system indexes | `000bcc22`, `000bd0ac`, `000bd0ec` |
| `MessagePort_VariantSlot`, the 9-byte slot `MessagePort_StepToVariant` builds | `000bd21e` |

## Open

- **Open:** what `commandr.str` and the pilot files hold in ES1, and whether any poster hands the ticker an id above `0x21`: `system.str` has no line for one, and its slot would lie past the system index, from `MessagePort_VariantSlot` on ([the string files](#the-string-files)). For `commandr.str` the question is its strings' attribute byte 0, which `SystemMessages_Index` indexes at `000bd0ac` (`0002f23e`) and `MessagePort_Post` reads as id − `0x32` (`000bceea`, `0002edf8`–`0002ee03`): byte 0 from 0 makes a group leader's id `0x32` its first line, and byte 0 equal to the id puts every line 450 bytes past the slot the post reads ([the command index](#the-string-files)). `HercWorks.Query strings` lists `STRINGS0.STR` alone; Core's `.STR` reader reads the attribute bytes.
- **Open:** whether any line is wider than 300 pixels in `cpred`, whose box would be larger than the save-under buffers, 3,531 bytes each ([building the two ports](#building-the-two-ports)): a line is the speaker's name, `": "` and up to `0x4a` characters of text.
- **Open:** what reads the message log and `MessagePort_LineOverlapsRect`: `es2_xref.py` finds no reference to `MessagePort_StampedQueueAppend`, `MessagePort_StampedQueueComposeAt`, `MessagePort_StampedQueueShownTimeAt` or `MessagePort_LineOverlapsRect`, nor to `HeapPtr_DtorMsgPortCopy`.
- **Open:** whether ES1 pauses the ports, as ES2's modal panels do ([the port](../../retail/simulation/cockpit-messages.md#the-port)); this file has no pause or resume function.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: the computer's lines show centered, unscrolled and unframed in the ticker's box; and in DOSBox-X's debugger, `BP <CS>:0002ed94` fires on the first damage message with `[ESP+8]` pointing at a record whose `int16` `+2` and `+4` are 0.
