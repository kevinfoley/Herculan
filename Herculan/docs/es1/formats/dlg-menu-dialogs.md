# GO.EXE — the menu dialogs

**Each of the shell's menus is laid out by a file: its window, its title and labels, its buttons with their keys, and the other controls on it, one record each.** `SHELL0.VOL`'s `dlg\` holds 39, `MENU<n>.DLG`, `n` the menu's number, and `eng\` the text of each, `MENU<n>.BIN` ([the `.BIN` string tables](bin-string-tables.md#the-table)). The shell builds a menu from the two when it opens it. Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## Layout

| Bytes | Field |
|---|---|
| 2 | `int16` *n*, the count of records |
| 13 × *n* | the records |

A record:

| Offset | Type | Field |
|---|---|---|
| 0 | 4 × `int16` | the rect: left, top, right, bottom |
| 8 | `uint8` | the kind |
| 9 | `uint8` | the style, a variant within the kind |
| 10 | `uint8` | the key, a keyboard scan code; read for kind 3 only |
| 11 | `int16` | the id |

The first record is the menu's window, kind 0. Its id picks the bitmap array the window loads, `res32\menu<id>.dba` ([the menu window](#the-menu-window)): the menu's own number in 25 files, 999 in eight (menus 1270, 3000, 3333, 5555, 6666 and 7770 to 7772), 1240 in three (888, 1250 and 1260), and 1501, 1700 and 1800 in menus 1500, 1710 and 1820. `SHELL0.VOL`'s `RES32\` holds no `MENU1501.DBA` or `MENU1700.DBA`. A control's rect is relative to the window's. In `MENU1000.DLG`, the main menu, the window is (49, 50)–(281, 165), and six buttons follow, ids 1499, 1100, 1700, 1200, 1300 and 1003, then a kind-15 control, id 1002.

## The build

`Menu_BuildFromDlg` (`0003a1f7`) takes a menu object and its number; `es2_xref` finds one call, from `0003969e`, which stores the number at the menu's `+0` and sets the byte `0007fd07` to 1 first. It opens `eng\menu<n>.bin` through `VolRStream_CtorFromPath` and, when that opens, reads it into a text table with `Text_LoadDataFile`; then it opens `dlg\menu<n>.dlg`, reads the count, and for each record reads the four rect words into the low halves of four zeroed dwords, the three bytes and the id. It handles the record by its kind, then counts it. At the end it closes and destroys both streams and the table.

### The text

A record's text is the line of `eng\menu<n>.bin` whose number is the record's position in the file, counting records that read no text. `Text_GetString` takes it, with no bounds test, for kinds 0, 1, 4 and 5 before the kind's code runs, and for kind 3 within it. For kinds 0, 1, 4 and 5 the ids at `0008196c` go elsewhere first: 1211 skips the record, and 5556 takes the line two after the word at `0009b2e3` + `0x1f7` × the word `0009b21f`; 5557, 5558 and 1223 jump to code Ghidra left undefined ([Open](#open)).

### The kinds

| Kind | Builds |
|---|---|
| 0 | the window, kept at the menu's `+6`: style 0 a [menu window](#the-menu-window) (`MenuWindow_Ctor`, `00036874`) with the text as its title, style 1 a plain one (`Window_Ctor`, `0001abcf`), any other style none. Each style copies the rect to the menu's `+0x12` |
| 1, 4, 5 | a label: the window's vtable `+0x4e9`, slot `+4`, is called with a mode, 0, 1 or 2 for kinds 1, 4 and 5, the width (right − left), the point (left, top), the text's length plus 1, and the text. In menu 4444 a kind-1 text holding `#` is first passed to `0002a1aa` |
| 2 | `0001f442`(0, id, rect), added to the window |
| 3 | a [button](#buttons), added to the window |
| 14 | for ids 1612 to 1614, a clickable area: `Window_AddHotspot` (`0001b2de`)(window, 0, id, `00017a19`, rect). Fourteen other ids at `00081b08` jump to code Ghidra left undefined; any other id builds nothing |
| 15 | `0001e11a`(0, `0x100`, 0, 0, a word, style, id, `0003750d`(id), rect), added to the window. The word is 0, or for the 23 ids at `00081a00` set by their own code, two of them from the bytes `0007fcb8` and `0007fcb9` |
| 16 | `0001eaef`(0, `0x100`, 0, a word, id, `00037827`(id), rect), added to the window. The word is −1, or for the ten ids at `00081ab8` set by their own code, 0 and the word `0007fcba` for two of them |
| 17 | `0001eff6`(0, `0x13`, 0, `0xffff`, id, `00037996`(id), rect), added to the window. `00037996` returns the 18 bytes at `000a056c`: for id 1803 the handles of fourteen `w1800w<i>.ttm` and four `w1800r0<i>.ttm` scripts it loads, for any other id `0xff` each |
| 18 | with style 1, `0004c83a`(rect), which stores the rect in the 24-byte entry of `000a0a9c` that the word at `+4` of `00083bc4`'s object numbers, when that is under 9 |
| 6–13, above 18 | nothing |

`Window_AddControl` (`0001b207`) appends a control to the window's list at `+0x1f`, counted at `+0x1b`. When the control's key byte `+0x1d` is non-zero it passes the key, and the key with bit 7 set, to `Window_AddKey` (`0001b296`), which appends a byte to the window's zero-ended 64-byte list at `+0x11f` unless the list holds it already: a scan code and its release code.

`0003750d` jumps elsewhere for the 28 ids at `000812f4` and otherwise returns `000373fa`(`000811a4`); `00037827` jumps elsewhere for the eleven ids at `0008144c` and otherwise returns 0. The classes `0001f442`, `0001e11a`, `0001eaef` and `0001eff6` build are [Open](#open).

### The menu window

`Window_Ctor` (`0001abcf`) builds a `0x4ed`-byte window over the rect: 15 hotspot entries of `0x38` bytes at `+0x19f`, the vtable `0007eff0` at `+0x4e9`, `000512a0`(0) at `+0x17` with the rect and a zero point copied into it at `+0x210` and `+0x220` and 1 at its `+0x208`, the rect at `+0`, the control count `+0x1b` 0, `+0x1d` `0xffff` and the key list cleared.

`MenuWindow_Ctor` (`00036874`) takes the window record's id, the title and the rect. It builds the window in `0x5a7` bytes with `Window_Ctor`, six label entries of `0x12` bytes at `+0x537`, and the vtable `00081140`. It loads `res32\menu<id>.dba` with `0004b7df` into `+0x5a3`; id 9999 loads `res32\menu<9999 − r>.dba`, `r` from `Math_RandomBelow(5)`. It sets `+0x4ed` to the `title` font (`000aacc6`, [the shell's fonts](styles-dat.md#the-loads)); the six ids at `00081174`, 999, 1240, 4444, 1610, 1800 and 1920, go elsewhere from here ([Open](#open)). For the rest it sets `+0x4f1` to the `cpylw` font (`000aacca`), copies the title to `+0x4f5`, or a 0 byte when there is none, and zeroes the label count `+0x535` and the hotspot count `+0x4e7`.

`MenuWindow_AddLabel` (`00036a93`), the menu window's slot `+4`, is what kinds 1, 4 and 5 call. It keeps up to six labels and drops the rest: for each, a copy of the text in a new buffer of the length given at `+0`, the point at `+4` and `+8`, the width at `+0xc` and the mode at `+0xe`.

`Window_AddHotspot` (`0001b2de`) keeps up to 15 areas: for each, 1 at `+1`, the rect at `+2` and again at `+0x24`, the second argument at `+0x12`, the id at `+0x16`, the handler at `+0x18` and 0 at `+0x34`.

### Buttons

Style 1 builds an icon button, `IconButton_Ctor` (`0001d47d`), with no text when the key is non-zero; any other style a text button, `TextButton_Ctor` (`0001cde8`). Menu 1300's button 100 takes its text from code chosen by the word `0007fce8` among the four values at `000819e0` ([Open](#open)); when it is none of them, the text pointer keeps the value an earlier record left in it.

`TextButton_Ctor` builds a `0x95`-byte button with `Control_CtorWithKey` (`0001a7e5`)(button, 1, key, rect), which stores the base vtable `0007ef92`, passes the rect to `0001a8b3`, and sets the key `+0x1d`, the byte `+0x1e` to 1 and the id `+0x17` to 0; the button then stores the id at `+0x17`, its vtable `0007f18c` at `+0x20`, the `STYLES.DAT` records 0, 1 and 2 (the pointer `00082654`) at `+0x30`, `+0x34` and `+0x38`, and the `active`, `pushed` and `inactive` fonts (`000aacce`, `000aacd2`, `000aacd6`) at `+0x24`, `+0x28` and `+0x2c`. It copies the text to `+0x3c` and places it three times at `+0x7c`, once per font: x the rect's left, y the bottom less half of (height − `00051ff0`(font, text)) less 2, the pushed one a pixel lower than the active one. With no text, `+0x3c` and the points are 0. `Control_Ctor` (`0001a716`)(control, byte, id, rect) is the same base with no key and the id stored.

`IconButton_Ctor` builds a `0xb1`-byte button over a text button, stores its vtable `0007f24a` at `+0x20` and the rect's left and top at `+0x95`, and takes its pictures by key from `res32\corners.dba`, the array `Shell_Main` loads into `000ab4ee`: frame *a* at `+0xa5` and `+0xad`, frame *b* at `+0xa9`. Eight ids take other frames, and four of them also call `0001a4f4`(button, 1).

| Key | Frames *a*, *b* | Other ids |
|---|---|---|
| `0x48` Up, `0x49` PgUp | 20, 22 | 1503: 36, 37 and `0001a4f4` |
| `0x4a` keypad − | 29, 31 | 109: 34, 35 |
| `0x4b` Left | 25, 27 | 1505: 40, 41 and `0001a4f4`; 2003: 44, 46 |
| `0x4d` Right | 24, 26 | 1506: 42, 43 and `0001a4f4`; 2004: 45, 47 |
| `0x4e` keypad + | 28, 30 | 108: 32, 33 |
| `0x50` Down, `0x51` PgDn | 21, 23 | 1504: 38, 39 and `0001a4f4` |

Any other key sets no frames, and four ids at `0007f2c4` jump to code Ghidra left undefined. Text buttons carry keys too: button 99 of menu 1230 `0x01` Esc, button 1624 of menu 1610 `0x39` Space. Some labels carry a non-zero key byte, which the build does not read.

## Open

- **Open:** what the classes `0001f442` (kind 2, vtable `0007f590`), `0001e11a` (kind 15), `0001eaef` (kind 16) and `0001eff6` (kind 17) are, and what `0003750d`, `00037827` and `000373fa` return for them.
- **Open:** the code the special ids jump to: kinds 0, 1, 4 and 5's 5557, 5558 and 1223, kind 14's fourteen ids at `00081b08`, kind 15's 23 and kind 16's ten, menu 1300's button 100, the icon button's four ids at `0007f2c4`, and `MenuWindow_Ctor`'s six ids at `00081174`.
- **Open:** what `0004b7df` returns for a name with no file, which windows 1501 and 1700 load.
- **Open:** what the label's mode changes in the drawing, and what a plain window's slot `+4` (vtable `0007eff0`) does with a label.
- **Open:** what `0002a1aa` does with menu 4444's `#` text, what `0001a4f4` sets on the four icon buttons, and what `000512a0` builds at the window's `+0x17`.
- **Open:** what `0003969e` does after the build, and what reads the byte `0007fd07`.
- **Open:** `eng\` holds `MENU1499.BIN`, `MENU1600.BIN` and `MENU1810.BIN`, for which `dlg\` holds no `.DLG`; what builds those menus.
