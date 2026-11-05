# GO.EXE — the menu window and its controls

**A menu's window is a box of the menu's picture with its title on a framed plate and its labels; on it sit its controls, each a rect, an id, a state and a vtable: text and icon buttons, list boxes, picture tiles and [ARMING's animation](arming.md#the-animation). The window draws itself and its controls, and redraws only the controls and hotspots marked changed.** How a menu is built, opened, drawn and closed is [the menus](menus.md), and how the window takes the mouse and the keys is [the menus' input](menu-input.md).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object files start and end

**The window file** runs from `0001a4f4`, after `HercStatus_Set`'s `RET 0x10` at `0001a4f0` and one zero byte, to `DUIWindow_PointAtControl` (`0001c3ec`), whose `RET 8` at `0001c45c` is followed by one zero byte. It holds `DUIControl` and `DUIWindow`, with its own copies of the region classes' vtables (`0007f00c`, `0007f036`, `0007f068`, `0007f0a0`) and their destructors (`GLBase_DtorWindowCopy`, `0001aec1`, and the two after it).

**The button file** starts at `MTButton_Ctor` (`0001cde8`). It holds a second copy of `DUIControl`'s vtable (`0007f1ea`), `DUIButtonControl` (`0007f130`), `MTButton` (`0007f18c`), `MTBmpButton` (`0007f24a`), and the slider and list classes from `DUISliderControl` (`0007f2f8`) on ([Open](#open)).

The other files of the menus' code are [the menus'](menus.md#where-the-object-files-start-and-end).

## The window

**The window is the `.DLG`'s first record: a box of the menu's picture with its title on a framed plate, its labels, and its controls.** `DUIWindow_Ctor` (`0001abcf`, class `DUIWindow`) builds a `0x4ed`-byte window over the rect:

| Offset | Holds |
|---|---|
| `+0` | the rect, `int32` left, top, right, bottom, on the screen |
| `+0x10` | a byte: 0 hit-tests the rect; otherwise the pointer must lie within the distance `+0x15` of the point (`+0x11`, `+0x13`), measured as \|dx\| + \|dy\|. The constructor zeroes it and leaves `+0x11`–`+0x16` unwritten |
| `+0x17` | the render context, `GLViewport_Ctor` (`000512a0`)(0), with the rect copied to its `+0x210`, a zero point to its `+0x220` and 1 to its `+0x208` |
| `+0x1b` | `int16` the control count |
| `+0x1d` | `int16` `0xffff` |
| `+0x1f` | the controls, up to 64 pointers |
| `+0x11f` | the keys, a zero-ended 64-byte list of scan codes and their release codes |
| `+0x19f` | 15 hotspots of `0x38` bytes (`__vec_ctor` of `WindowHotspot_Ctor`, `0001ad4e`) |
| `+0x4e7` | `int16` the hotspot count, which `DUIWindow_Ctor` does not write: `MTWindow_Ctor` zeroes it, and a plain window keeps what the allocation held |
| `+0x4e9` | the vtable: `DUIWindow`'s `0007eff0`, slot `+4` `DUIWindow_AddLabel` (`0001ae9b`), which does nothing, and `+8` `DUIWindow_DrawBackground` (`0001aeb7`), which does nothing |

`MTWindow_Ctor` (`00036874`, class `MTWindow`) takes the window record's id, the title and the rect. It builds the window in `0x5a7` bytes with `DUIWindow_Ctor`, six label entries of `0x12` bytes at `+0x537`, and the vtable `00081140`, whose slot `+4` is `MTWindow_AddLabel` and `+8` `MTWindow_Draw` (`00036b4c`). It loads `res32\menu<id>.dba` with `Resource_LoadBitmapArray` into `+0x5a3`; id 9999 loads `res32\menu<9999 − r>.dba`, `r` from `Math_RandomBelow(5)`. It sets `+0x4ed` to the `title` font (`Shell_Fonts` (`000aacc6`), [the shell's fonts](../formats/styles-dat.md#the-loads)) and the label font `+0x4f1` by the id: the six ids at `00081174` jump through the targets beside them, 999, 1240 and 4444 to `nm_green` (`000aacda`), 1610 and 1800 to `cstats_2` (`000aacea`) and 1920 to `active` (`000aacce`), and any other id takes `000aacca`, `cpylw`, which `Widget_LoadStylesAndFonts` stores over `Shell_LoadData`'s `gray` ([the loads](../formats/styles-dat.md#the-loads)); every id then goes on alike (`00036987`). It copies the title to `+0x4f5`, or a 0 byte when there is none, and zeroes the label count `+0x535` and the hotspot count `+0x4e7`.

`MTWindow_AddLabel` (`00036a93`), the slot kinds 1, 4 and 5 call, keeps up to six labels and drops the rest: for each, a copy of the text in a new buffer of the length given, at `+0`, the point at `+4` and `+8`, the `int16` width at `+0xc` and the mode as a dword at `+0xe`, the entry's last four bytes.

`DUIWindow_AddHotspot` (`0001b2de`)(window, *a*, id, handler, rect) keeps up to 15 areas: for each, 1 at `+1`, the rect at `+2` and again at `+0x24`, *a* as a dword at `+0x12`, the id at `+0x16`, the handler at `+0x18` and 0 at `+0x34`. The byte `+0` is set when the area is pressed. `DUIWindow_HitHotspot` (`0001beca`) takes *a* as a pointer to a rect: a point inside the hotspot's rect but also inside that rect, when *a* is not 0, does not hit it.

`DUIWindow_AddControl` (`0001b207`) appends a control to `+0x1f`, counted at `+0x1b`, with no test against the 64 slots. When the control's key byte `+0x1d` is non-zero it passes the key, and the key with bit 7 set, to `DUIWindow_AddKey` (`0001b296`), which appends a byte to the key list unless the list holds it already; its walk has no bound either, so a 65th distinct code would be written past the 64 bytes the constructor zeroes.

### The menu window's drawing — `MTWindow_Draw` (`00036b4c`)

`MTWindow_Draw`(window) draws, in the window's render context, whose origin is the window's corner:

1. the first bitmap of `+0x5a3`, `res32\menu<id>.dba`, with `Bitmap_Blit` at the window's own first two dwords, its corner on the screen: `DUIWindow_Draw` has cleared the context's translation `+0x20c`, so the corner is not added again. It then sets `+0x20c`, and the rest is drawn relative to the corner;
2. the plate, for every menu window: the test at `00036b86` (`ADD EBX,0x4f5`) is of the title buffer's address, never 0, so a window with an empty title gets the plate too. *y0* 4 on backdrops 4000 and 2000 and 5 on the others, *y1* = *y0* + 17; a margin *m* of 20 on backdrop 4000 and 40 on the others; *x0* = (*w* − title width − *m*) / 2, truncating, with *w* the window's `int16` right − left, and *x1* = title width + *x0* + *m*, the widths by `HudFont_MeasureString` in the `title` font. Four lines through `Raster_DrawLineColour` (`0004ba84`), each under the pen {0, color} at the render context's `+0x228`: the top (*x0*, *y0*)–(*x1*, *y0*) and the left (*x0*, *y0*)–(*x0*, *y1*) in `0x10`, the right (*x1*, *y1*)–(*x1*, *y0*) in `0x1b` and the bottom (*x1*, *y1*)–(*x0*, *y1*) in `0x1c`. Then frames 0 to 3 of `res32\corners.dba` (`000ab4ee`) with `Bitmap_Blit`, flags 0, at (*x0*, *y0*), (*x1* − 4, *y0*), (*x0*, *y1* − 4) and (*x1* − 4, *y1* − 4), so only the right and bottom ones are set in. Last the title in the `title` font, centered between *x0* and *x1* (the render context's `+0x231` 2, `+0x235` *x1* − *x0*; `HudFont_DrawString`), its pen at (*x0*, (*y1* − *y0*) / 2 + *y0* − 1), the division truncating;
3. each label, in the font `+0x4f1`: `HudFont_DrawString` at (*x*, *y* + the font's cell height − 3), the label's mode as `+0x231` and its width as `+0x235`. So kind 1's mode 0 draws the text from *x*, kind 4's mode 1 ends it at *x* + width, and kind 5's mode 2 centers it in the width ([the kinds](../formats/dlg-menu-dialogs.md#the-kinds)).

### Drawing a window — `DUIWindow_Draw` (`0001bfa3`)

`DUIWindow_Draw`(window) installs the window's render context, clears its `+0x20c`, calls the window's slot `+8`, sets `+0x20c` again, calls each control's slot `+8`, calls each hotspot's handler with (the hotspot's `+2`, its id), and installs the render context it found.

### Updating a window — `DUIWindow_Update` (`0001c067`)

`DUIWindow_Update`(window) installs the window's render context with `+0x20c` set and clears `DUIWindow_InputReady`. For each control whose byte `+0x1f`, *changed*, is 1 or whose `+0x1b` is 2, it draws the control through its slot `+8` on page 2 and again on the draw page, and adds its rect (slot `+0x48`, moved by the window's corner) with `DirtyBox_AddRect`, delay 0. Then `DUIWindow_UpdateHotspots` (`0001c270`) does the same for each hotspot whose byte `+0` is set, calling its handler on page 2 and on the draw page, and clears the byte. Last it sets `DUIWindow_InputReady` and installs the render context it found.

## The controls

**A control is a rect, an id, a state and a vtable; the id is the screen a press of it names.** `DUIControl_CtorWithKey` (`0001a7e5`)(control, *focusable*, key, rect) and `DUIControl_Ctor` (`0001a716`)(control, *focusable*, id, rect) build the `DUIControl` base, `0x24` bytes, zeroing the byte `+0x10` and storing the vtable `0007ef92` and the rect with `DUIControl_SetRect` (`0001a8b3`), which also clears `+0x19` and `+0x1f`; neither writes `+0x1b`, which the derived constructors set. `DUIControl_SetHitMode` (`0001a4f4`)(control, mode) stores the mode at `+0x10` and, for mode 1, sets the distance `+0x15` to (right − left) >> 1 and the point `+0x11`, `+0x13` to (left + that, top + that), so the control is hit inside the diamond inscribed in the top square of its rect:

| Offset | Holds |
|---|---|
| `+0` | the rect, `int32`, relative to the window |
| `+0x10` | a byte: 0 hit-tests the rect; otherwise the distance test of the window's `+0x10`, with the point at `+0x11`, `+0x13` and the distance `+0x15` |
| `+0x17` | `int16` the id |
| `+0x19` | `int16` the state: 0 up, 1 pushed, 2 inactive |
| `+0x1b` | `int16` how a press is tracked: 0 a button's, 1 the control follows the pointer while pressed, 2 the control holds the input ([the mouse](menu-input.md#the-mouse)) |
| `+0x1d` | the key, a scan code, or 0 |
| `+0x1e` | *focusable*: 1 for buttons, which `Tab` stops at |
| `+0x1f` | *changed*: set when the control needs drawing |
| `+0x20` | the vtable |

The slots the window code calls:

| Slot | Called by | `DUIControl` (`0007ef92`) | `MTButton` (`0007f18c`) |
|---|---|---|---|
| `+4` | `DUIWindow_Dtor`, (control, flags) | `DUIControl_Dtor` (`0001a61a`) | `MTButton_Dtor` (`0001d10d`) |
| `+8` | `DUIWindow_Draw`, `DUIWindow_Update`, (control) | `DUIControl_Draw` (`0001a647`), nothing | `MTButton_Draw` (`0001d200`) |
| `+0xc` | after every state change, (control) | `DUIControl_Invalidate` (`0001a651`), nothing | `MTButton_Invalidate` (`0001d17b`): sets *changed* |
| `+0x2c` | a release, (control): the message's value | `DUIControl_GetValue` (`0001a6ac`): 1 | `DUIButtonControl_GetValue` (`0001d088`): 1 |
| `+0x38` | a press or a move of a tracking control, (control, point relative to the window) | | |
| `+0x3c` | a typed character to the holding control, (control, character) | | `MTButton_SetTextFromTable` (`0001d393`) |
| `+0x40` | a key code to the holding control, (control, code & `0x7f`) | | |
| `+0x48` | `DUIWindow_Update`, (control): the rect | `BarGraph_GetRectWindowCopy` (`0001a70a`) | `BarGraph_GetRectButtonCopy` (`0001d0e6`) |

`MTButton`'s slots `+0x10` and `+0x18`, `MTButton_Highlight` (`0001d1ce`) and `MTButton_Unhighlight` (`0001d1e7`), set and clear the byte `+0x94` and set *changed* ([Open](#open)).

### Buttons

Kind 3's style 1 builds an icon button, `MTBmpButton_Ctor` (`0001d47d`, class `MTBmpButton`), with no text when the key is non-zero; any other style a text button, `MTButton_Ctor` (`0001cde8`, class `MTButton`). The main menu's six are text buttons with no key. Menu 1300's button 100 takes its text by `Shell_ReturnScreen` (`0007fce8`) among the four values at `000819e0` ([the return screen](main-menu.md#the-return-screen)); when it is none of them, the text pointer keeps the value an earlier record left in it.

`MTButton_Ctor` builds a `0x95`-byte button with `DUIControl_CtorWithKey`(button, 1, key, rect), sets `+0x1b` to 0, zeroes the bytes `+0x94` and `+0x1f`, and stores the id at `+0x17`, its vtable `0007f18c` at `+0x20`, the `STYLES.DAT` records 0, 1 and 2 (the pointer `00082654`) at `+0x30`, `+0x34` and `+0x38`, and the `active`, `pushed` and `inactive` fonts (`000aacce`, `000aacd2`, `000aacd6`) at `+0x24`, `+0x28` and `+0x2c`, one per state. It copies the text to `+0x3c` and places it three times at `+0x7c`, once per font: x the rect's left, y the bottom less ((`int16` height) − the font's cell height) / 2, truncating, less 2: the up point with the font `+0x24`, the pushed one a pixel lower, and the inactive one with the font `+0x2c`. With no text, `+0x3c` and the points are 0. `MTButton_SetTextFromTable` (`0001d393`)(button, line) copies line *line* of `eng\watext.bin` (`0009afa8`) to `+0x3c`, places it again and sets *changed*; `Menu_Open` calls it on the buttons of several menus.

`MTBmpButton_Ctor` builds a `0xb1`-byte button over a text button, stores its vtable `0007f24a` at `+0x20` and the rect's left and top at `+0x95`, and takes its pictures by key from `res32\corners.dba`, the array `Shell_Main` loads into `000ab4ee`: frame *a* at `+0xa5` and `+0xad`, frame *b* at `+0xa9`: the frames of states 0, 1 and 2, so an inactive icon button looks as it does up. Eight ids take other frames, and four of them also give themselves the diamond hit area with `DUIControl_SetHitMode`(button, 1).

| Key | Frames *a*, *b* | Other ids |
|---|---|---|
| `0x48` Up, `0x49` PgUp | 20, 22 | 1503: 36, 37 and `0001a4f4` |
| `0x4a` keypad − | 29, 31 | 109: 34, 35 |
| `0x4b` Left | 25, 27 | 1505: 40, 41 and `0001a4f4`; 2003: 44, 46 |
| `0x4d` Right | 24, 26 | 1506: 42, 43 and `0001a4f4`; 2004: 45, 47 |
| `0x4e` keypad + | 28, 30 | 108: 32, 33 |
| `0x50` Down, `0x51` PgDn | 21, 23 | 1504: 38, 39 and `0001a4f4` |

With any other key the constructor looks the id up among the four at `0007f2c4`, 1806, 2003, 1807 and 2004, ARMING's < POINTS, < HERCS, POINTS > and HERCS > ([the window](arming.md#the-window)), and jumps to `0001d97a` for the first two and `0001d9f9` for the others, code Ghidra left undefined ([Open](#open)); any other id sets no frames.

`MTBmpButton`'s vtable (`0007f24a`) has its own slots `+4`, `+8`, `+0xc` and `+0x48`, and `MTButton`'s for `+0x10`, `+0x18`, `+0x2c` and `+0x3c`:

| Slot | Function | Does |
|---|---|---|
| `+4` | `MTBmpButton_Dtor` (`0001da8d`) | stores its vtable, then `MTButton`'s, `DUIButtonControl`'s and the button file's copy of `DUIControl`'s (`0007f1ea`), and frees the button when bit 0 of the flags is set |
| `+8` | `MTBmpButton_Draw` (`0001db87`) | `Bitmap_Blit` of the state's frame (`+0xa5` + 4 × state), flags 0, at the point `+0x95`; in state 1, the click as `MTButton_Draw`'s, after the blit; the text, centered (the render context's `+0x231` 2) in the frame's `int16` width `+6`, in the state's font at the state's point; *changed* cleared |
| `+0xc` | `MTBmpButton_Invalidate` (`0001dc9f`) | sets *changed* |
| `+0x48` | `MTBmpButton_GetRect` (`0001db1d`) | sets `+0x9d`, `+0xa1` to the point `+0x95` plus the state's frame's width `+6` and height `+4`, and answers the rect at `+0x95` |

### Drawing a control

`MTButton_Draw` (`0001d200`)(button), by the state `+0x19`:

- **1, pushed**: while the byte `Shell_SoundOption` (`0007fcb9`) and the sound object `ShellSound_ButtonSound` (`0007fd00`) are set, it stops the object when its slot `+0x30` answers non-zero (slot `+0x14`) and plays it (slot `+0x10`): the click. Then as state 0;
- **0, up**, and pushed: `00044b86` with the state's `STYLES.DAT` record over the button's rect, and, while the byte `+0x94` is set, `0004b867`(10, rect);
- **2, inactive**: `00044b86` with record 2;
- any other state draws no frame;

then the text in the state's font at the state's point, centered in the button's width, the `int16` right − left (the render context's `+0x231` 2, `+0x235` the width), and *changed* cleared. The text test is of the text buffer's address, never 0, so the text is always drawn, and an empty one draws nothing.

`CornerFrame_Draw` is `ES1DBSIM`'s `CornerFrame_Draw` (`ES1DBSIM 00044954`) with the shell display's `+0x40` less 4 for the simulator's `+0x44` less 4: `Raster_InstallRenderContext` (`000513fc`) stores the installed context + 4 there, so it draws in the installed context, the one `000ab612` holds ([drawing a frame from a style](../simulation/alert-panels.md#drawing-a-frame-from-a-style)), with `CornerFrame_DrawEdgeRing` (`00044dc8`), `CornerFrame_DrawCorners` (`000451c1`), which takes its pictures from the shell's corner bank, and `Rect_CopyCtorFrameCopy` (`00044d9a`). `Raster_FillFrameRectColours` and `Raster_FrameRectColour` (`0004b867`) are `ES1DBSIM`'s (`ES1DBSIM 00044e50`, `00044e00`) on the render context `000ab612`: the second frames a rect's four edges in one color ([the color helpers](../simulation/alert-panels.md#the-resource-loaders-and-the-color-helpers)), the first fills it in one and then frames it in the other. A button draws with the same two: its state's `STYLES.DAT` record through `CornerFrame_Draw`, and, while highlighted, a frame in color 10 through `Raster_FrameRectColour`.

### The list box

Kind 15 builds `MTStrListBox_Ctor` (`0001e11a`, class `MTStrListBox`, vtable `0007f474`) over `DUIVSlider` (`0007f412`) and `DUISliderControl` (`0007f2f8`), a `0x5a`-byte control on `DUIControl_Ctor`(list, 0, id, rect), so `Tab` passes over it: `+0x1b` 1, the range `+0x24` 0 to `+0x26` the line count `0001eac5`(list, lines), the selected line at `+0x28`, `+0x2a` and `+0x54` from the record's word, the style at `+0x3e`, the lines at `+0x4c`, `STYLES.DAT` record 3 at `+0x50` (`00082654` + `0xc0`, where `MTButton_Ctor`'s records 0 to 2 are `+0`, `+0x40` and `+0x80`), the fonts `nm_green` (`000aacda`) and `nm_red1` (`000aacde`) at `+0x40` and `+0x44`, the line height `+0x2c`: `HudFont_CellHeight2` of `nm_green` (passed the first line, which it does not read) − 1, replaced by the rect's `int16` height − 1 when the height is below it, so a height equal to it stays. The lines shown `+0x58` are the height over the line height (`int16` division), 1 when that is 0, then at most the line count, so 0 for an empty list; the first line shown `+0x56` is 0 when the selected line is below the lines shown, and otherwise selected − shown + 1, so that the selected line is the last shown. The main menu's 1002 is one, at (112, 47)–(196, 59). Its lines are `Menu_ListLines`(id) ([the main menu's list](main-menu.md#the-list)).

`MTStrListBox`'s vtable (`0007f474`) holds the slots the window code calls ([the controls](#the-controls)):

| Slot | Function | Does |
|---|---|---|
| `+4` | `MTStrListBox_Dtor` (`0001e41e`) | frees the lines array `+0x4c`, and for id 1002 first its first line and the name buffer pointer `0007ebc0`, which it clears; stores `DUIVSlider`'s, `DUISliderControl`'s and `DUIControl`'s vtables in turn and frees the list when bit 0 of the flags is set |
| `+8` | `MTStrListBox_Draw` (`0001e584`) | `CornerFrame_Draw` (`00044b86`) with `STYLES.DAT` record 3 (`+0x50`) over the list's rect, then `MTStrListBox_DrawLines` (`0001e5b7`), and *changed* cleared |
| `+0xc` | `MTStrListBox_TrackSelect` (`0001e96a`) | with style 0 only: selects the line under the tracked point, below |
| `+0x1c`, `+0x20` | `MTStrListBox_SelectPrev` (`0001e8df`), `MTStrListBox_SelectNext` (`0001e919`) | moves the selected line `+0x54` up or down one within the lines, scrolling the first line shown `+0x56` to keep it shown, and sets *changed* |
| `+0x24` | `MTStrListBox_SelectLine` (`0001ea4f`)(list, line) | selects the line, clamped to the last when past it and unchanged when negative, shows from it or from the last page, and sets *changed* |
| `+0x2c` | `MTStrListBox_GetSelected` (`0001e3d3`) | the selected line `+0x54`: the message's value |
| `+0x30` | `MTStrListBox_SelectedText` (`0001e3e5`) | the selected line's text |
| `+0x38` | `MTStrListBox_SetTrackPoint` (`0001e404`)(list, point) | stores the point's y at `+0x28` |
| `+0x44` | `MTStrListBox_SetLines` (`0001e50a`)(list, lines) | frees the old array, takes the new one, counts it into `+0x26` with `MTStrListBox_CountLines` (`0001eac5`), shows from line 0 and sets the lines shown `+0x58` to the rect's height over the line height, at most the count |
| `+0x48` | `BarGraph_GetRectButtonCopy` (`0001d0e6`) | the rect |

**Drawing the lines.** `MTStrListBox_DrawLines` sets the render context `000ab612`'s alignment `+0x231` to the list's `+0x48`, the constructor's fourth argument, 0 or the 2 that [the build](menus.md#opening-a-menu--menu_open-0003969e) gives some ids, and its field width `+0x235` to the rect's width less 2. For each row *r* from 0 while *r* is below the lines shown and the line `+0x56` + *r* exists, with *h* the line height `+0x2c` and (*x0*, *y0*, *x1*) the rect: in style 0 the selected line is drawn on a box (*x0* + 1, *y0* + *r* × *h* + 1)–(*x1* − 1, *y0* + *r* × *h* + *h*) filled in color 4 and framed in color 5 (`Raster_FillFrameRectColours` (`0004b8d8`)(5, 4, box)), its text in the font `+0x44`, `nm_red1`; every other line, and every line in style 1, in the font `+0x40`, `nm_green`; the text with `HudFont_DrawString` at (*x0* + 3, *y0* + *r* × *h* + *h* − 1).

**A press and a move.** The list tracks the pointer (`+0x1b` 1), so a press and each move while it is held call slot `+0x38` with the point relative to the window and then slot `+0xc` ([the mouse](menu-input.md#the-mouse)). `MTStrListBox_TrackSelect` returns at once unless the style `+0x3e` is 0. With style 0: a y above the rect's top selects the previous line with slot `+0x1c` and then the first line shown; a y below its bottom selects the next with slot `+0x20` and then one more while that is below the last line; a y inside selects the line of the shown row it falls in and sets *changed*.

**The slider's own slots.** `DUIVSlider` (`0007f412`), the list box's base and the picture tile's and [the animation's](arming.md#the-animation), has two slots of its own that the last two keep: `+0x38`, `DUIVSlider_SetTrackPoint` (`0001aac3`)(control, point), copies the tracked point `+0x28` to `+0x2a` and stores the point's y in `+0x28`, held within the rect's top and its bottom less `+0x2c`; `+0x2c`, `DUIVSlider_GetValue` (`0001ab2c`), answers the minimum `+0x24` plus (the thumb rect `+0x2e`'s top `+0x32` − the rect's top) × (`+0x26` − `+0x24`) / (the rect's height − the thumb rect's), in floats, truncated.

### The picture tile

**A picture tile shows one frame of a bank of pictures in a frame of its own, such as CREW's insignia and its HERCs ([CREW](hangar.md#the-screen)).** Kind 16 builds `MTGfxListBox_Ctor` (`0001eaef`, class `MTGfxListBox`, vtable `0007f4d4`)(0, `0x100`, 0, word, id, bank, rect), a `0x4b`-byte control over `DUIVSlider` and `DUISliderControl`, built as the list box's base is, so `Tab` passes over it. It stores the bank at `+0x3e`, the bank's frame count (its dword `+4`, as an `int16`) at `+0x26`, the frame shown `+0x42` = word mod that count (−1 stays −1), `STYLES.DAT` record 3 at `+0x44`, the `int16` color `+0x49` 10, and clears the highlight byte `+0x48` and *changed*.

`Menu_LoadControlBitmaps` (`00037827`)(id) gives the bank, by the eleven ids at `0008144c`, targets `00081478`: each loads its file with `Resource_LoadBitmapArray` into its dword when that is 0 and returns the dword as it stands. 1611 loads `res32\insignia.dba`, 1321 `res32\controls.dba`, 1316 `res32\bluebar.dba`, 1803 `res32\allweap.dba` ([ARMING's low-memory pictures](arming.md#entering-it)) and 1914 to 1916 `res32\check.dba`, all into `000813d4`; 1615 to 1618 load `res32\mechs.dba` into `000813dc`. None writes `000813d8`. Any other id answers 0.

| Slot | Function | Does |
|---|---|---|
| `+4` | `MTGfxListBox_Dtor` (`0001ec76`) | `Shell_FreeBitmapArraySlot` (`00037924`)(`+0x3e`), which destroys the bank and clears whichever of `000813d4`, `000813d8` and `000813dc` holds it; clears `+0x3e`, stores the base classes' vtables, and frees the control when bit 0 of the flags is set |
| `+8` | `MTGfxListBox_Draw` (`0001ed21`) | for a frame `+0x42` from 0 to below `+0x26`, `Bitmap_Blit` of that frame at the rect's corner + (1, 1), flags 0; otherwise `CornerFrame_Draw` with the record `+0x44` over the rect. While `+0x48` is set, `Raster_FrameRectColour`(the color `+0x49`, the rect one pixel in). *Changed* cleared |
| `+0xc` | `MTGfxListBox_TrackSelect` (`0001ef5b`) | as [the animation's](arming.md#the-animation): slot `+0x1c` above the rect, `+0x20` below it, which `DUIVSlider_SetTrackPoint` keeps a press from reaching |
| `+0x10`, `+0x18` | `MTGfxListBox_Highlight` (`0001efa7`), `MTGfxListBox_Unhighlight` (`0001efe0`) | set or clear `+0x48`, and set *changed* |
| `+0x14` | `MTGfxListBox_HighlightIn` (`0001efbd`)(control, color) | stores the color at `+0x49`, then calls slot `+0x10` |
| `+0x1c` | `MTGfxListBox_PrevFrame` (`0001eef0`) | lowers `+0x42` while it is above 0; *changed* set |
| `+0x20` | `MTGfxListBox_NextFrame` (`0001ef12`) | `+0x42` becomes (`+0x42` + 1) mod `+0x26`; *changed* set; answers (`+0x42` + 1) / `+0x26`, the old frame's |
| `+0x24` | `MTGfxListBox_SetFrame` (`0001ef3f`)(control, frame) | stores the frame at `+0x42`; *changed* set |
| `+0x2c`, `+0x38` | `DUIVSlider_GetValue`, `DUIVSlider_SetTrackPoint` | the slider's, above |

## Functions

| ES1 | Address |
|---|---|
| `DUIControl_Dtor`, `DUIControl_Draw`, `DUIControl_Invalidate`, `DUIControl_GetValue`, `DUIControl_Ctor`, `DUIControl_CtorWithKey`, `DUIControl_SetRect` | `0001a61a`, `0001a647`, `0001a651`, `0001a6ac`, `0001a716`, `0001a7e5`, `0001a8b3` |
| `BarGraph_NoOpWindowCopy`, `GridShape_GetMemorySizeWindowCopy`, `BarGraph_GetRectWindowCopy` | `0001a665`, `0001a6ba`, `0001a70a` |
| `DUIWindow_Ctor`, `WindowHotspot_Ctor`, `DUIWindow_AddLabel`, `DUIWindow_DrawBackground`, `DUIWindow_Dtor`, `WindowHotspot_Dtor` | `0001abcf`, `0001ad4e`, `0001ae9b`, `0001aeb7`, `0001afa1`, `0001b06f` |
| `GLBase_DtorWindowCopy`, `GLRegion_DtorWindowCopy`, `GLRectangleRegion_DtorWindowCopy`, `Rect_CopyCtorWindowCopy` | `0001aec1`, `0001aeed`, `0001af36`, `0001c242` |
| `DUIWindow_Draw`, `DUIWindow_Update`, `DUIWindow_UpdateHotspots`, `DUIWindow_PointAtControl` | `0001bfa3`, `0001c067`, `0001c270`, `0001c3ec` |
| `MTButton_Ctor`, `DUIButtonControl_Dtor`, `MTButton_Dtor`, `MTButton_Invalidate`, `DUIControl_DtorButtonCopy` | `0001cde8`, `0001cfd8`, `0001d10d`, `0001d17b`, `0001d18d` |
| `MTButton_Highlight`, `MTButton_Unhighlight`, `MTButton_Draw`, `MTButton_SetTextFromTable`, `DUIButtonControl_GetValue` | `0001d1ce`, `0001d1e7`, `0001d200`, `0001d393`, `0001d088` |
| `BarGraph_NoOpButtonCopy`, `GridShape_GetMemorySizeButtonCopy`, `BarGraph_GetRectButtonCopy`, `Rect_CopyCtorButtonCopy` | `0001d041`, `0001d096`, `0001d0e6`, `0001d365` |
| `MTBmpButton_Ctor`, `MTStrListBox_Ctor` | `0001d47d`, `0001e11a` |
| `MTStrListBox_GetSelected`, `MTStrListBox_SelectedText`, `MTStrListBox_SetTrackPoint`, `MTStrListBox_Dtor`, `MTStrListBox_SetLines`, `MTStrListBox_Draw`, `MTStrListBox_DrawLines` | `0001e3d3`, `0001e3e5`, `0001e404`, `0001e41e`, `0001e50a`, `0001e584`, `0001e5b7` |
| `MTStrListBox_SelectPrev`, `MTStrListBox_SelectNext`, `MTStrListBox_TrackSelect`, `MTStrListBox_SelectLine`, `MTStrListBox_CountLines` | `0001e8df`, `0001e919`, `0001e96a`, `0001ea4f`, `0001eac5` |
| `CornerFrame_Draw`, `Rect_CopyCtorFrameCopy`, `CornerFrame_DrawEdgeRing`, `CornerFrame_DrawCorners` | `00044b86`, `00044d9a`, `00044dc8`, `000451c1` |
| `Raster_FrameRectColour`, `Raster_FillFrameRectColours` | `0004b867`, `0004b8d8` |
| `MTWindow_Ctor`, `MTWindow_Dtor`, `MTWindow_AddLabel`, `MTWindow_Draw` | `00036874`, `00036a09`, `00036a93`, `00036b4c` |
| `DUIControl_SetHitMode`, `MTBmpButton_Dtor`, `MTBmpButton_GetRect`, `MTBmpButton_Draw`, `MTBmpButton_Invalidate` | `0001a4f4`, `0001da8d`, `0001db1d`, `0001db87`, `0001dc9f` |
| `MTGfxListBox_Ctor`, `MTGfxListBox_Dtor`, `MTGfxListBox_Draw`, `MTGfxListBox_PrevFrame`, `MTGfxListBox_NextFrame`, `MTGfxListBox_SetFrame` | `0001eaef`, `0001ec76`, `0001ed21`, `0001eef0`, `0001ef12`, `0001ef3f` |
| `MTGfxListBox_TrackSelect`, `MTGfxListBox_Highlight`, `MTGfxListBox_HighlightIn`, `MTGfxListBox_Unhighlight`, `Menu_LoadControlBitmaps` | `0001ef5b`, `0001efa7`, `0001efbd`, `0001efe0`, `00037827` |
| `DUIVSlider_SetTrackPoint`, `DUIVSlider_GetValue` | `0001aac3`, `0001ab2c` |

## Open

- **Open:** where the button file ends, and whether the slider and list classes from `0001deaa` share it.
- **Open:** what calls `MTButton_Highlight` and `MTButton_Unhighlight`: no caller of `MTButton`'s slots `+0x10` and `+0x18` was found in the window, active window's or shell code read here.
- **Open:** the frames the icon buttons 1806, 2003, 1807 and 2004, ARMING's POINTS and HERCS arrows, take: the constructor jumps to `0001d97a` and `0001d9f9`, code Ghidra left undefined.
- **Open:** the list box's slots `+0x10`, `+0x14`, `+0x18`, `+0x28`, `+0x34`, `+0x3c` and `+0x40` (`0001d037`, `BarGraph_NoOpButtonCopy`, `0001d04f`, `0001d07b`, `0001d0a2`, `0001d0bd`, `0001d0cb`).
