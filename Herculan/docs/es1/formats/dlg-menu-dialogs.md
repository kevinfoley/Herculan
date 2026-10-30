# GO.EXE — the menu dialogs

**Each of the shell's menus is laid out by a file: its window, its title and labels, its buttons with their keys, and the other controls on it, one record each.** `SHELL0.VOL`'s `dlg\` holds 39, `MENU<n>.DLG`, `n` the menu's number, and `eng\` the text of each, `MENU<n>.BIN` ([the `.BIN` string tables](bin-string-tables.md#the-table)). The shell builds a menu from the two when it opens it ([the menus](../shell/menus.md#opening-a-menu--menu_open-0003969e)). Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

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

The first record is the menu's window, kind 0. Its id picks the bitmap array the window loads, `res32\menu<id>.dba` ([the window](../shell/menus.md#the-window)): the menu's own number in 25 files, 999 in eight (menus 1270, 3000, 3333, 5555, 6666 and 7770 to 7772), 1240 in three (888, 1250 and 1260), and 1501, 1700 and 1800 in menus 1500, 1710 and 1820. `SHELL0.VOL`'s `RES32\` holds no `MENU1501.DBA` or `MENU1700.DBA`. A control's rect is relative to the window's, and a control's id is the screen its press names ([a message's path](../shell/menu-input.md#a-messages-path)). In `MENU1000.DLG`, the main menu, the window is (49, 50)–(281, 165), and six style-0 buttons with no key follow, ids 1499, 1100, 1700, 1200, 1300 and 1003, then a style-1 kind-15 control, id 1002.

## The text

A record's text is the line of `eng\menu<n>.bin` whose number is the record's position in the file, counting records that read no text. `Text_GetString` takes it, with no bounds test, for kinds 0, 1, 4 and 5 before the kind's code runs, and for kind 3 within it. For kinds 0, 1, 4 and 5 the five ids at `0008196c` take another text: 1211 skips the record, and 5556, 5557, 5558 and 1223 take theirs from the current career record, from the menu's own lines or `eng\watext.bin` ([the build](../shell/menus.md#opening-a-menu--menu_open-0003969e)).

## The kinds

| Kind | Builds |
|---|---|
| 0 | the window, kept at the menu's `+6`: style 0 a [menu window](../shell/menus.md#the-window) (`MTWindow_Ctor`, `00036874`) with the text as its title, style 1 a plain one (`DUIWindow_Ctor`, `0001abcf`), any other style none. Each style copies the rect to the menu's `+0x12` |
| 1, 4, 5 | a label: the window's vtable `+0x4e9`, slot `+4`, is called with a mode, 0, 1 or 2 for kinds 1, 4 and 5, the width (right − left), the point (left, top), the text's length plus 1, and the text. The menu window draws mode 0 from the point, 1 ending at the point plus the width and 2 centered in the width ([its drawing](../shell/menus.md#the-menu-windows-drawing--mtwindow_draw-00036b4c)). In menu 4444 a kind-1 text holding `#` is first passed to `0002a1aa` |
| 2 | `0001f442`(0, id, rect), added to the window |
| 3 | a [button](../shell/menus.md#buttons): style 1 an icon button, any other style a text button, added to the window |
| 14 | for the 17 ids at `00081b08`, a clickable area: `DUIWindow_AddHotspot` (`0001b2de`)(window, 0, id, handler, rect), each id with its handler ([the build](../shell/menus.md#opening-a-menu--menu_open-0003969e)); any other id builds nothing |
| 15 | a [list box](../shell/menus.md#the-list-box), `MTStrListBox_Ctor` (`0001e11a`)(0, `0x100`, 0, 0, a word, style, id, `Menu_ListLines` (`0003750d`)(id), rect), added to the window. The word is 0, or for the 23 ids at `00081a00` set by their own code, two of them from the bytes `0007fcb8` and `0007fcb9` |
| 16 | `0001eaef`(0, `0x100`, 0, a word, id, `00037827`(id), rect), added to the window. The word is −1, or for the ten ids at `00081ab8` set by their own code, 0 and the word `0007fcba` for two of them |
| 17 | `0001eff6`(0, `0x13`, 0, `0xffff`, id, `Menu_OpenControlTtms` (`00037996`)(id), rect), added to the window. `00037996` returns the 18 bytes at `000a056c`: for id 1803 the handles of fourteen `w1800w<i>.ttm` and four `w1800r0<i>.ttm` scripts it loads, for any other id `0xff` each |
| 18 | with style 1, `0004c83a`(rect), which stores the rect in the 24-byte entry of `000a0a9c` that the word at `+4` of `00083bc4`'s object numbers, when that is under 9 |
| 6–13, above 18 | nothing |

What the build does with each kind beyond this table, the keys a control adds and the lines a list box takes, is [the build's](../shell/menus.md#opening-a-menu--menu_open-0003969e).

## Open

- **Open:** what the classes `0001f442` (kind 2, vtable `0007f590`), `0001eaef` (kind 16) and `0001eff6` (kind 17) are, and what `00037827` returns for them and `Menu_ListLines` (`0003750d`) for the 27 kind-15 ids other than 1002.
- **Open:** the code kind 15's 23 special ids and kind 16's ten jump to.
- **Open:** what `Resource_LoadBitmapArray` returns for a name with no file, which windows 1501 and 1700 load.
- **Open:** what `0002a1aa` does with menu 4444's `#` text.
- **Open:** `eng\` holds `MENU1499.BIN`, `MENU1600.BIN` and `MENU1810.BIN`, for which `dlg\` holds no `.DLG`; what builds those menus.
