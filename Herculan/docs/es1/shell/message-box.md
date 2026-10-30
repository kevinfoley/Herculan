# GO.EXE — the message box and its text

**The shell shows short texts in a box over the screen: a talking head's message in a framed strip across the top, the mission briefing on the war map in a box near the bottom, the debriefing in a box at the upper left. The text is cut into lines at spaces to fit the box's width and the lines into pages, and the box shows one page at a time.** A message's text is one line of an `eng\*_text.bin` file ([the dialog text files](../formats/bin-string-tables.md#the-dialog-text-files)); a briefing's or a debriefing's is a block of the mission's `.ENG` file ([reading a block](../formats/eng-mission-text.md#reading-a-block)). With the RADIO preference at `VOICE ONLY` a message's text is not drawn at all.

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

**`dialog.cpp`** runs from `Mission_WriteObjectives` (`00018824`), whose assert names `src\dialog.cpp` (`00018a4f`), to `0001a29b`. The file before, `TextTable_Ctor` (`0001865c`) to `Text_LoadDataFile`, ends with a `RET 8` at `00018820` and one zero byte. Its last code is its static initializer, `Dialog_StaticInit` (`00019c2a`), and the exit routine `Dialog_StaticExit` (`0001a296`), whose `RET` at `0001a29a` is followed by one zero byte and `HercStatus_Ctor` (`0001a29c`). It holds no `ConfigPart` run. Its own copies of two library functions: `Rect_CopyCtorDialogCopy` (`00019050`), byte for byte `Rect_CopyCtor` (`0004b398`), and `Point_DefaultCtorDialogCopy` (`00019a02`), byte for byte `Point_DefaultCtor` (`0004b37d`).

## The text box

### The dialog record

The shell keeps one text box at a time, in the dialog record `Shell_Dialog` (`0009afc8`): `+0` the stream it was loaded from, `+4` the file's version word, `+6` the box. Two loaders fill it, `Dialog_LoadFromFile` (`00018c36`) for a message ([the load](../formats/bin-string-tables.md#the-load)) and `Dialog_LoadConditional` (`00018cdb`) for an `.ENG` block ([reading a block](../formats/eng-mission-text.md#reading-a-block)); each first deletes the box it held with `Dialog_DeleteBox` (`00018d93`), which calls the box's destructor (slot `+4` of its vtable) with 3 and sets `+6` to 0. The version word picks the class: 0 a `DefaultDialogBox`, 2 a `DigitalDialogBox` (a message), 1 a `ConditionalDialogBox` (an `.ENG` block).

The record's other functions do nothing without a box:

- `Dialog_Draw` (`00018dcb`)(dialog) draws it with `DialogBox_Draw`(box, the version word) ([drawing a page](#drawing-a-page)). `es2_xref.py` finds it called from `Dialog_PrevPage`, `Dialog_NextPage`, `MessageBox_Run` (`000382f6`), `Shell_DrawScreen` (`0003c1b7`) and `Shell_Main`.
- `Dialog_PrevPage` (`00018df0`) and `Dialog_NextPage` (`00018e18`) step the page ([stepping pages](#stepping-pages)) and draw it with `Dialog_Draw`.
- `Dialog_MarkDirty` (`00018d76`)(dialog) passes the box to `DialogBox_MarkDirty` ([drawing a page](#drawing-a-page)); `es2_xref.py` finds it called from `MessageBox_Answer` (`0003837f`).

### The box

A `DialogBox` is `0x2d` bytes; its subclasses differ in their constructors and vtables: `DialogBox` (`0007ee88`), `DefaultDialogBox` (`0007eea8`), `DigitalDialogBox` (`0007ef04`), `ConditionalDialogBox` (`0007ef48`), each the class name the image stores before its vtable.

| Offset | Field |
|---|---|
| `+0x00` | a byte, 1 while the box stands in the framed strip at the top and its frame is still to be drawn ([drawing a page](#drawing-a-page)) |
| `+0x01` | the font: `000aacda`, the `nm_green` font ([the loads](../formats/styles-dat.md#the-loads)), for every class |
| `+0x05`–`+0x11` | the box: left, top, right, bottom, `int32` |
| `+0x15` | the text |
| `+0x19` | `int16`, the count of line slots |
| `+0x1b` | the line slots, one pointer each, zeroed |
| `+0x1f` | one 8-byte point per line of a page |
| `+0x23` | `int16`, the count of pages |
| `+0x25` | `int16`, the lines a page holds |
| `+0x27` | `int16`, the page shown |
| `+0x29` | the vtable |

`DialogBox_Init` (`00019b25`), which each constructor calls first, sets `+0` to 1 and zeroes the font, the text, the slots, the points and the four words; it leaves the box's rect as it was. `DefaultDialogBox_Ctor` (`00019149`) takes the top strip's rect and the text `This is a Default Dialog Box.`; `DigitalDialogBox_Ctor` (`00019279`) and `ConditionalDialogBox_Ctor` (`00019453`) take theirs from their files and their rect from their `SetRect` ([where the box stands](#where-the-box-stands)). Each sets the font and lays the text out with `DialogBox_Layout`. `DialogBox_SetFont` (`00018e40`)(box, *n*) would set the font to the *n*th of the shell's fonts, the dwords from `Shell_Fonts` (`000aacc6`) ([Open](#open)).

**Destroying a box.** Each class's destructor stores its vtable and calls `DialogBox_Dtor` (`00019b54`)(box, 0): `DefaultDialogBox_Dtor` (`00019244`), `ConditionalDialogBox_Dtor` (`00019871`), and `DigitalDialogBox_Dtor` (`000193a8`), which first stops the message's lip-sync with `maybe_SncList_Stop` (`00045556`)(`0009afd4`). `DialogBox_Dtor` stores `DialogBox`'s vtable, frees the text, the slots and the points, runs `DialogBox_Init` again, destroys the frame bitmap `DialogBox_FrameBitmaps` (`0007ee64`) when it is loaded and clears it, adds the top strip's frame rect `DialogBox_TopFrameRect` (`0009ae74`) as a dirty box at delay 0, and deletes the box when bit 0 of the flags is set. It picks that rect by `+0`, which `DialogBox_Init` has just set to 1, so it never adds the box's own rect.

### Where the box stands

**Five rects place the box: a strip across the top of the screen, inside a frame, and one box each for the war map, the debriefing and backdrop 7000.** `Dialog_StaticInit` (`00019c2a`), the static constructor table's entry `0009a24c`, builds each from two points, the smaller *x* and *y* first:

| Rect | Address | (*x0*, *y0*)–(*x1*, *y1*) |
|---|---|---|
| `DialogBox_TopRect`, the top strip's text | `0009ae64` | (8, 7)–(284, 29) |
| `DialogBox_TopFrameRect`, the top strip's frame | `0009ae74` | (3, 4)–(287, 32) |
| `DialogBox_Backdrop6000Rect` | `0009ae84` | (69, 150)–(267, 184) |
| `DialogBox_Backdrop5000Rect` | `0009ae94` | (17, 31)–(143, 85) |
| `DialogBox_Backdrop7000Rect` | `0009aea4` | (108, 160)–(299, 195) |

Its exit routine, `Dialog_StaticExit` (`0001a296`, exit table entry `0009a290`), returns at once.

`ConditionalDialogBox_SetRect` (`00018e5f`) and `DigitalDialogBox_SetRect` (`000193ee`) round the word `Shell_BackdropId` (`000817c4`) down to a thousand, the current backdrop group ([the backdrop](menus.md#the-backdrop)), and look it up in four ints, `0007ee44` and `0007ef10`, with a jump table beside them. Each copies a rect to `+5` and sets `+0`:

| Group | `ConditionalDialogBox` (`.ENG`) | `DigitalDialogBox` (a message) |
|---|---|---|
| 3000, 4000 | `DialogBox_TopRect`, `+0` 1 | `DialogBox_TopRect`, `+0` 1 |
| 5000 | `DialogBox_Backdrop5000Rect`, `+0` 0 | `DialogBox_TopRect`, `+0` 1 |
| 6000 | `DialogBox_Backdrop6000Rect`, `+0` 0 | nothing: the rect and `+0` stay as they were ([Open](#open)) |
| 7000 | `DialogBox_TopRect`, `+0` 1 | `DialogBox_Backdrop7000Rect`, `+0` 0 |
| any other | `DialogBox_TopRect`, `+0` 1 | nothing |

The `.ENG` boxes are loaded on the war map, group 6000, and on the debriefing, screen 1510, whose group is 5000 plus 0 to 3 by the campaign ([the backdrop](menus.md#the-backdrop)), so both stand in their own box with `+0` 0. A message over group 3000 or 5000 stands in the top strip with `+0` 1, and over 7000 in `DialogBox_Backdrop7000Rect` with `+0` 0. `DefaultDialogBox_Ctor` always takes `DialogBox_TopRect`, with `+0` 1 from `DialogBox_Init`.

### Laying out the text

**The text is cut into lines at spaces, each line as wide as fits the box, and the lines go down the box at a fixed spacing.** `DialogBox_Layout` (`000198a6`)(box):

1. the line height *h* is the font's cell height, `HudFont_CellHeight2` (`00051ff0`), less 2;
2. the slot count `+0x19` is the text's width in the font, `HudFont_MeasureString` (`00051fac`), over the box's width (right − left), plus 2;
3. the lines a page holds, `+0x25`, is the box's height (bottom − top) over *h*;
4. the page count `+0x23` is the slot count over the lines a page holds, plus 1 when the two differ; the page shown `+0x27` is 0;
5. it allocates the slots and zeroes them, and with `__vec_new` and `Point_DefaultCtorDialogCopy` (`00019a02`) one point per line of a page: line *i* at (left, top + (*i* + 1) × *h*);
6. it cuts the text into lines with `DialogBox_Wrap` (`00019a1d`)(box, the box's width, the text) and drops the count it returns.

So the page count comes from the slot count, an estimate from the text's width, not from the lines the wrap makes.

**The wrap** is ES2's `TextBox_Wrap` ([the wrap](../../retail/shell/mission-screen.md#the-summary-text-box)): it walks the text in place, ends a line at a newline, and at each space measures the line so far and, when it is wider than the box, cuts it at the last break, which then stays where it was until a space that fits moves it; after the last character the final line is measured and cut once more. Each line's start goes into the next slot. ES1's differs:

- it turns no character into a space: a character below a space other than a newline, and a byte above 127, stays as it is;
- it does not assert after a cut;
- it takes the width as an argument, `int16`, and returns the count of cuts and newlines, one less than the lines;
- it does not test the slot count: a text that makes more lines than `+0x19` writes their starts past the slots' allocation ([Open](#open)).

### Drawing a page

**A page is drawn over a cleared box: the frame bitmap for the top strip, a flat colour for any other box, and then the page's lines in the green font, each on its own line point.** `DialogBox_Draw` (`00018edd`)(box, *mode*), *mode* being the dialog's version word, 2 for a message and 1 for an `.ENG` block:

1. **The background.** With `+0` set: unless `Shell_RadioOption` (`0007fcbc`), the shell's copy of the RADIO preference ([the shared data](startup.md#the-shared-data)), is 1, `VOICE ONLY`, it loads `res32\textbox.dba` into `DialogBox_FrameBitmaps` with `Resource_LoadBitmapArray` on first use and draws the array's first bitmap at (3, 4) with `Bitmap_Blit`, flags 0. With `+0` clear it fills the box's rect with palette colour `0x1f`, `Raster_FillFrameRectColours`(`0x1f`, `0x1f`, the rect) ([drawing a control](menus.md#drawing-a-control)).
2. **The lines.** When the box has a font and slots, and unless *mode* is 2 and `Shell_RadioOption` is 1, it draws the lines from the page shown × the lines a page holds, for as many as a page holds: each one below the slot count whose slot is set, with `HudFont_DrawString`(font, the line, the next line point), the points taken in order from the first, so a skipped slot leaves no gap. `HudFont_DrawString` aligns the text by the installed render context's `+0x231` ([labels](../simulation/cockpit-widgets.md#labels)), which the box does not set ([Open](#open)).
3. **The voice.** When the box has a font and slots it starts the message's line with `maybe_SncList_StartSound` (`0004544a`)(`0009afd4`), which starts it once ([loading a line](../formats/snc-lip-sync.md#loading-a-line)).

It sets no page and adds no dirty box: it draws on the display's current draw page ([the pages](../rendering/screen-pages.md#the-pages)). With `+0` set and `VOICE ONLY` an `.ENG` box would draw its lines with no background; no `.ENG` box stands in the top strip ([where the box stands](#where-the-box-stands)).

`DialogBox_MarkDirty` (`0001907e`)(box) adds `DialogBox_TopFrameRect` when `+0` is set, and the box's rect otherwise, as a dirty box at delay 0 and again at delay 1, so the next two frames copy the background over it on both pages ([dirty boxes](../rendering/screen-pages.md#dirty-boxes)).

### Stepping pages

`DialogBox_NextPage` (`0001911f`) moves the page shown `+0x27` forward while it is below the page count less 1, and `DialogBox_PrevPage` (`000190fd`) back while it is above 0; each clears `+0` when it moves. So once a box in the top strip has turned a page it no longer draws its frame and fills its rect, `DialogBox_TopRect`, with colour `0x1f` instead, inside the frame drawn before. The war map's handler steps its briefing ([the briefing](../formats/eng-mission-text.md#the-briefing)).

## Open

- **Open:** a caller of `DialogBox_SetFont` (`00018e40`): `es2_xref.py` finds no branch to it and no stored pointer.
- **Open:** whether a message is ever shown over group 6000, 1000, 2000 or 8000, where `DigitalDialogBox_SetRect` leaves the rect and `+0` as the allocation held them; which screens send each message kind is the message box's.
- **Open:** whether any retail text wraps into more lines than its slot count, the text's width over the box's width plus 2, in the `nm_green` font: one pass over the `.ENG` blocks and the `_TEXT.BIN` lines against the box each is shown in.
- **Open:** the alignment `+0x231` of the render context installed when `DialogBox_Draw` runs, from `MessageBox_Run`, `Shell_DrawScreen`, `Shell_Main` and the page steps.
- **Open:** a retail check of the boxes: INSTANT ACTION's message shows its text in the framed strip at the top of the screen, and the war map's briefing in a box across the lower middle, (69, 150)–(267, 184); with RADIO at `VOICE ONLY` the message shows neither the frame nor the text.
