# Cockpit message port

**The cockpit's two message boxes, the computer's ticker and the pilot and squad channel, are one class family: each box is a port object holding a ten-message queue, the box it draws in and two pairs of callbacks, and the two classes fill the same table of virtual functions differently.** What the boxes say, when a line goes up and how each looks is [`cockpit-messages.md`](cockpit-messages.md); this page is the object itself: its fields, its queue, how it is built, and which function fills each slot. Addresses are DBSIM's; functions take their arguments on the stack and leave them to the caller.

## The object

`MsgPort` is the base class, `0x4d3` bytes:

| Offset | Holds |
|---|---|
| `+0x000` | the vtable |
| `+0x004` | the message queue ([below](#the-queue)) |
| `+0x222` | a second queue, the message log ([below](#the-message-log)) |
| `+0x49a` | the current message, a queued record |
| `+0x49e` | byte, a line is up |
| `+0x49f` | the box, a `GLRectangleRegion` of type 0: the type at `+0x4a3` and the rect {*x0*, *y0*, *x1*, *y1*} at `+0x4a7`–`+0x4b3` |
| `+0x4b7`, `+0x4b8` | bytes, how many begin and end callbacks are registered |
| `+0x4b9`, `+0x4c1` | two begin callbacks, two end callbacks |
| `+0x4c9`, `+0x4ca`, `+0x4cb` | bytes, the due, ready and cancelled latches ([the port](cockpit-messages.md#the-port)) |
| `+0x4cc` | dword; `MessagePort_Tick` does nothing while it is nonzero |
| `+0x4d0` | `int16` |
| `+0x4d2` | byte, the display suppressed ([the port](cockpit-messages.md#the-port)) |

`EventPort`, the ticker, is `0x4ea` bytes: the blink latch at `+0x4d3` ([the ticker](cockpit-messages.md#the-ticker)), a copy of the box's rect as built at `+0x4d4`–`+0x4e0`, a dword at `+0x4e4` and an `int16` at `+0x4e8`. `DialogPort`, the pilot and squad channel, is `0x4df` bytes: two buffers, `+0x4d3` for the screen under the box and `+0x4d7` for the box as painted ([the save-unders](#the-pilot-channels-save-unders)). A training mission's channel is a third class built on `DialogPort` ([the training port](cockpit-messages.md#the-training-port)).

## The queue

**A port keeps up to ten messages in priority order, but a post that has to go ahead of a queued message loses the one at the end, and a post into a full queue frees the wrong record.** The queue is `0x21e` bytes:

| Offset | Holds |
|---|---|
| `+0x000` | ten records of `0x31` bytes |
| `+0x1ea` | the order: ten dwords, each a record's index, front first |
| `+0x212` | ten bytes, 1 while that record is in use |
| `+0x21c` | `int16`, how many are queued |

A record holds the message id (`int16` `+0`), the twenty bytes at `+2`–`+0x15` of the record the poster passed, whose dword `+2` is the subject, the text pointer at `+0x18`, and the attribute bytes and the four timings ([the record](cockpit-messages.md#the-computers-messages)).

- **`MessagePort_QueueInit` (`00434e50`)**, (queue): builds the ten records with `QueuedMessage_ElementCtor` (`00437558`) and sets the count and the ten in-use bytes to 0.
- **`MessagePort_Enqueue` (`00434e8c`)**, (queue, record, entry), *entry* being an index slot {text, count, attributes} ([posting](#posting)): with ten queued it first runs `MessagePort_QueueDropLast` (`004351c0`), which clears the in-use byte of `order[order[9]]`, the record named by the order entry whose position is the tenth entry's value, not of the tenth entry's own record (`MOV EDX,[EAX+0x20e]`; `MOV ECX,[EAX+EDX*4+0x1ea]`; `MOV byte [EAX+ECX+0x212],0`), and takes 1 from the count. It takes the first record not in use, queuing nothing when every one is, marks it and fills it. Its index goes into the order at *k*, the first queued message whose priority (`+0x17`) is strictly greater, or at the end, and the count goes up by 1. To make room it moves entries down one from position count − 2 to *k*, so the entry at count − 1, the last queued, is overwritten, and the entry at the new count − 1 keeps the index it held before.

  So a post that goes ahead of a queued message drops the last queued one from the order, its record left in use, and leaves a stale index at the end. In a full queue the record `QueueDropLast` frees is still in the order unless `order[9]` is 9, so the post overwrites a queued message's record and the order names that record twice, while the tenth entry's record stays in use with no entry within the count naming it.

  The retail files never reach the first: attribute byte 2 is 0 on every entry of `SYSTEM.STR`, `PILOT1.STR`, `PILOT2.STR`, `PILOT4.STR` and `COMMAND0.STR`–`COMMAND4.STR`, in v1.0 and in v1.10's English, French and German, so *k* is always the count and nothing moves. The second needs only ten messages queued on one port.
- **`MessagePort_QueueFront` (`00435074`)** and **`MessagePort_QueueAt` (`004350d4`)**, (queue[, index]): the record at the order's index, or 0 past the count.
- **`MessagePort_RemoveAt` (`00435140`)**, (queue, index): with the queue not empty, frees the record and moves the later order entries down one.
- **`MessagePort_QueueExpire` (`004351ec`)**, (queue): from the last order entry down to entry 1, removes each message whose `maxWait` deadline (`+0x28`) is earlier than now; the front is never dropped here.
- **`MessagePort_QueueRemoveMatch` (`00435240`)**, (queue, subject, id): removes the first message whose id and subject both match and returns 1, else 0.

## Building a port

1. **`MessagePort_CtorBase` (`00435490`)**, (port, rect): stores `MsgPort`'s vtable, builds both queues, builds the box as a type-0 rectangle region holding *rect*, and clears the current message, `+0x49e`, the latches, the callback counts, `+0x4cc`, `+0x4d0` and `+0x4d2`.
2. **`MessagePort_Ctor` (`004369a4`)**, (port, rect), the ticker: `MessagePort_CtorBase`, `EventPort`'s vtable, the blink latch cleared, the rect copied to `+0x4d4`, 0 at `+0x4e4` and −1 at `+0x4e8`.
3. **`DialogPort_Ctor` (`00435bf0`)**, (port, rect), the pilot and squad channel: `MessagePort_CtorBase`, `DialogPort`'s vtable, and two buffers the size of the box's pixel count at `+0x4d3` and `+0x4d7`.

`Gau_BuildCockpitWidgets` builds the two from the `.GAU`'s rects ([the port](cockpit-messages.md#the-port)).

## The slots

| Slot | `MsgPort` (`0049bb00`) | `EventPort` (`0049ba7c`) | `DialogPort` (`0049bad4`) |
|---|---|---|---|
| `+0x00`, post | `MessagePort_PostNoOp` (`0043556c`) | `MessagePort_PickVariant` (`00436a3c`) | `PilotMessagePort_Post` (`00435c48`) |
| `+0x04`, show | pure virtual | `MessagePort_Show` (`00436abc`) | `PilotMessagePort_Speak` (`00435d9c`) |
| `+0x08`, the line's text | pure virtual | `MessagePort_SetText` (`00436a0c`) | `PilotMessagePort_ComposeLine` (`00435d0c`) |
| `+0x0c`, take down | pure virtual | `MessagePort_Erase` (`00436fd0`) | `PilotMessagePort_Erase` (`00436130`) |
| `+0x10`, per frame | `MessagePort_Tick` (`00435610`) | `MessageTicker_ScrollText` (`00436f70`) | `PilotMessagePort_Update` (`004361cc`) |
| `+0x14`, put the background back | `MessagePort_UnpaintNoOp` (`0043757e`) | the same | `PilotMessagePort_RestoreBackground` (`00437044`) |
| `+0x18`, paint | `MessagePort_PaintNoOp` (`00437590`) | `MessageTicker_Paint` (`00436cec`) | `PilotMessagePort_RedrawFromSaveUnder` (`00437094`) |
| `+0x1c`, the box's rect | `MessagePort_GetRect` (`00437583`) | the same | the same |

The two no-ops are empty bodies. `MessagePort_Tick` reaches the show through `+0x04` and the take-down through `+0x0c` ([the port](cockpit-messages.md#the-port)).

## Posting

**A post looks the message id up in the box's own string table, picks one of the id's recordings at random when it has several, and queues it.** A poster passes a record whose `int16` `+0` is the id and whose dword `+2` is the subject, the machine the message is about or 0.

- **`MessagePort_PickVariant` (`00436a3c`)**, (port, record), the ticker's post: the id's slot in the system index (`004d0b33` + id × 9), against `SystemStringTable` (`004d04d8`).
- **`PilotMessagePort_Post` (`00435c48`)**, (port, record), the channel's: with no subject, the slot in the command index `004d0971`, against `CommandStringTable` (`004d04c8`); otherwise `Squad_IndexOf` of the subject picks the squadmate's slot, and its index `004d04e8` + slot × `0x183` against `PilotStringTables` (`004d0498`) + slot × `0x10`. A subject in no slot is not caught: the post compares the low byte of `Squad_IndexOf`'s −1, zero-extended to 255, with −1, so it goes on with slot 255 and reads its slot from `004d04e8` + 255 × `0x183` + id × 9, past the three slots' index.

Both then roll: a slot is {dword text, byte count at `+4`, dword attributes at `+5`}; with a count of 2 or more the variant is the random generator `0x4d268f`'s next low word modulo the count, else 0. A nonzero variant goes through **`MessagePort_StepToVariant` (`00435a74`)**, (port, table, slot, *n*): it calls `StrTable_NextString` (`004539cc`), which returns the string after the one given in the loaded file, *n* times, each time with the slot's own text, so every variant from 1 on gives the string right after the slot's; and returns a slot it builds at `004d0d6a`: that string, a count of 0, and `StrTable_GetAttributes` (`00453a08`) of it. The post queues the slot with `MessagePort_Enqueue` on the port's `+4` when its text and attributes are both set.

**`SystemMessages_Index` (`00435970`)**, (port, mode, *n*, bank), loads a string file into its table and fills an index from it: mode 0 `system` into `SystemStringTable` and `004d0b33`; mode 1 `commandX`, the digit *n*, into `CommandStringTable` and `004d0971`; mode 2 the pilot file of *bank* into `PilotStringTables` + *n* × `0x10` and `004d04e8` + *n* × `0x183`. For each string it reads, the slot at the index + attribute byte 0 × 9 takes the text and attributes when its count is 0, and its count goes up by 1 ([variants](cockpit-messages.md#the-computers-messages)). `MessagePort_StaticInit` (`00437270`) builds the five tables empty.

## Withdrawing and the callbacks

- **`MessagePort_Withdraw` (`00435ac8`)**, (port, subject, id): when the current message has the id and the subject, it sets the cancelled latch and returns 1 unless the message is due, which it leaves and returns 0 for; otherwise `MessagePort_QueueRemoveMatch` on the queue ([withdraw](cockpit-messages.md#the-port)).
- **`MessagePort_AddBeginCallback` (`004355a8`)** and **`MessagePort_AddEndCallback` (`004355dc`)**, (port, callback): store the callback in the next of two slots and return 1, or 0 when both are taken. `MessagePort_Tick` calls each begin callback with the current message as it comes due; `PilotMessagePort_Erase` calls each end callback with it as the line comes down.

## Each frame

- **`MessageTicker_ScrollText` (`00436f70`)**, the ticker: recomputes the line's x ([the ticker](cockpit-messages.md#the-ticker)), paints (`+0x18`), sets the ready latch when the due latch is set, and runs `MessagePort_Tick`.
- **`PilotMessagePort_Update` (`004361cc`)**, the channel: sets the ready latch for a due message whose record `+0x2c` is set ([its speakerless set](cockpit-messages.md#its-speakerless-set)); while a line is up and not suppressed, makes the display's `+0x8c` its source page `+0x38` and paints (`+0x18`); then `MessagePort_Tick`.

## Taking a line down

- **`MessagePort_Erase` (`00436fd0`)**, the ticker: while a line is up and not suppressed, fills the box's rect with the paint's background color between `Cockpit_PushCanvasContext` and `Cockpit_PopRenderContext`, copies the box between the pages with `MessagePort_CopyBoxBetweenPages`(port, 1), clears `+0x49e`, `+0x4d2` and the blink latch, and returns 1; it also returns 1 with no line up.
- **`PilotMessagePort_Erase` (`00436130`)**, the channel: returns 1 at once with no line up or the display suppressed, 0 while the view manager's view-change flag `+0x1c` is set; otherwise runs the end callbacks, puts the screen under the box back from `+0x4d3`, copies the box between the pages, clears `+0x49e` and `+0x4d2` and returns 1.

**`MessagePort_CopyBoxBetweenPages` (`00437164`)**, (port, direction): while a line is up and not suppressed, copies the box region between the display's two pages, `+0x88` to `+0x8c` for direction 0 and `+0x8c` to `+0x88` for 1, with the cursor hidden; it shows the cursor again unless the view manager's view is 4 and `0049dd8c` is 0, and puts the draw page back.

### The pilot channel's save-unders

Each takes effect only while a line is up, the display is not suppressed and the view-change flag is clear:

- **`PilotMessagePort_RestoreBackground` (`00437044`)** puts the screen under the box back from `+0x4d3`.
- **`PilotMessagePort_RedrawFromSaveUnder` (`00437094`)** moves the box's rect by the canvas origin (the render context's `+0x220`, `+0x224`), saves the screen under it into `+0x4d3`, moves it back, and puts the box's own pixels back from `+0x4d7`.

## The message log

The second queue is a five-message log: `MessagePort_StampedQueueAppend` (`004352ec`) keeps each message's record with the clock's time as a 9-byte string at the queue's `+0x21e` + index × 9, dropping the oldest when five are kept, and `MessagePort_StampedQueueTimeAt` (`00435458`) returns an entry's time. Who reads the log is [Open](#open).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `PilotMessagePort_Post` drops a post whose subject is in no squadmate slot | The decompile shows a return on −1, but the code compares `EDX`, holding the zero-extended low byte of `Squad_IndexOf`'s result (255 for −1), with −1, which never holds ([posting](#posting)). |
| `MessagePort_QueueDropLast` frees the last queued message's record | It reads the tenth order entry and then the order entry at that position, freeing `order[order[9]]` ([the queue](#the-queue)). |
| `MessagePort_StepToVariant` walks *n* strings on from the slot | Each of its calls passes the slot's own text, not the string the last call returned ([posting](#posting)). |

## Open

- **Open:** what reads the message log: `es2_xref.py` finds no caller of `MessagePort_StampedQueueAppend` or `MessagePort_StampedQueueTimeAt`.
- **Open:** whether any poster hands the pilot and squad channel a subject in no squadmate slot, which `PilotMessagePort_Post` would look up as slot 255.
- **Open:** what `EventPort`'s dword `+0x4e4` and `int16` `+0x4e8` and the base's `int16` `+0x4d0` hold, and what writes `+0x4cc`, which `MessagePort_Tick` tests.