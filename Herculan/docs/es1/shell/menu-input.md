# GO.EXE — the menus' input

**One menu window at a time takes the mouse and the keyboard. Pointing at a button gives it the focus, a press pushes it and a release over the same button acts; `Tab` and `Shift+Tab` move the focus and the pointer, `Enter` presses and lets go of the focused button, and a button's own key does both. A release writes a message that names the button's number, and the frame loop hands it to the screen.** How the window and its controls are built and drawn is [the menus](menus.md); the frame loop that reads the devices is [the start-up's](startup.md#the-input).

Addresses are `ES1GO` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)). The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## The active window

**One window at a time takes the input: `DUIWindow_Active` (`0007f0d0`).** `DUIWindow_SetActive` (`0001c536`)(window) stores it, runs `DUIWindow_Activate` (`0001b0ff`) and sets the byte `DUIWindow_KeysEnabled` (`0007f0dd`). `DUIWindow_Activate`:

1. stores `DUIWindow_FindEnabledControlById`(window, 103)'s answer in `DUIWindow_PressedIndex` (`0007f0d4`) before testing it, so a window with no enabled control 103 leaves −1 there; with one, makes it pushed and changed, the holding control `DUIWindow_HeldIndex` (`0007f0d6`) with the byte `DUIWindow_Holding` (`0007f0db`) set, and calls its slot `+0xc`;
2. unless `-X` was given, calls `Input_GetKeyboardDevice_Thunk` (`00027bbf`), whose answer it keeps in locals and does not use, and registers `DUIWindow_KeySubscriber` (`0001ba73`) with the keyboard for the window's key list `+0x11f` (`Keyboard_AddSubscriber`, C convention);
3. sets `DUIWindow_FocusIndex` to the control under the pointer, `DUIWindow_HitControl` (`0001bc11`)(window, the pointer relative to the window), −1 for none.

`DUIWindow_ClearActive` (`0001c57c`) runs `DUIWindow_Deactivate` (`0001b25b`), which sets `DUIWindow_HeldIndex` to −1, clears `DUIWindow_Holding`, calls `Input_GetKeyboardDevice_Thunk` and drops its answer, and removes `DUIWindow_KeySubscriber` with `Keyboard_RemoveSubscriber` (`0001ff32`); it reads neither its window argument nor `Shell_DebugMode`, so under `-X` it removes a subscriber that was never added, which `Keyboard_RemoveSubscriber` answers with 0. Then `DUIWindow_ClearActive` clears `DUIWindow_Active` and `DUIWindow_KeysEnabled`.

`Input_GetKeyboardDevice_Thunk` calls `Input_GetKeyboardDevice` (`000258df`), which is 0 when the dword `000ab1cc` is 0 and otherwise `Input_DeviceSlot` (`00025899`) of it less 4, or 0 when the slot is 0; `Input_GetMouseDevice_Thunk` (`00027bc9`) calls `Input_GetMouseDevice` (`00025912`), the same with `000ab1d0` and less `0x3a`. They are the simulator's ([the input devices](../simulation/input.md#the-input-devices)); `DUIWindow_Activate`, `DUIWindow_Deactivate`, `ShellMouse_AddCallbacks` and `ShellMouse_RemoveCallbacks` drop the answer.

`DUIWindow_HitControl`(window, x, y) answers the first control, in the order they were added, that is not inactive and whose rect holds the point (or, with its `+0x10` set, within its distance), and −1 for none.

## The mouse

`ShellMouse_AddCallbacks` (`0001c460`) calls `Input_GetMouseDevice_Thunk` and registers three callbacks with the mouse driver ([the mouse driver's events](../simulation/cockpit-mouse.md)), keeping the slots `Mouse_AddCallback` answers in the words `0007f0c8`, `0007f0ca` and `0007f0cc`: mask 1, the pointer moved; 2, the left button pressed; 4, the left button released. `ShellMouse_RemoveCallbacks` (`0001c4de`) removes the three in that order. Each callback is given the pointer's point, and the two button callbacks hit-test with that point, not `Shell_PointerX`, `Shell_PointerY`.

- **`ShellMouse_OnMove` (`0001c605`)** returns unless `DUIWindow_InputReady` is set, tested first, and there is an active window. It clears `DUIWindow_InputReady` for its run, stores the point in `Shell_PointerX` and `Shell_PointerY` before any test of the window, and sets `DUIWindow_InputReady` again on every way out. While the byte `DUIWindow_Tracking` (`0007f0da`) is set it calls the pressed control's slot `+0x38` with the point relative to the window and its slot `+0xc`, without testing `DUIWindow_PressedIndex`. Otherwise, with the point inside the window (its rect, or its distance test), it runs `DUIWindow_OnDrag` (`0001b8fc`)(window, point): `DUIWindow_FocusIndex` becomes the control under the pointer, and with no control pressed (`DUIWindow_PressedIndex` −1) it returns there. With one pressed, it is made pushed from up (state 0) when the pointer is over it again, or it holds the input, and up from pushed (state 1) when the pointer has left it, each change calling slot `+0xc`, so a pressed control that is inactive keeps its state; and a hotspot under the pointer has its byte `+0` set.
- **`ShellMouse_OnLeftPress` (`0001c7d8`)** does not test `DUIWindow_InputReady`. Unless the message box is up (`0009b013`), it clears `DUIWindow_InputReady` and sets it again on every way out after that: with the point inside the active window, `DUIWindow_OnPress` (`0001b436`)(window, point): it lets go of the holding control (state 0, changed, `DUIWindow_Holding` cleared, `DUIWindow_HeldIndex` −1) with no call of its slot `+0xc`; takes the control under the pointer as `DUIWindow_PressedIndex`; makes it pushed and changed; for a `+0x1b` of 1 calls its slot `+0x38` with the point and sets `DUIWindow_Tracking`, for 2 calls slot `+0x38` and makes it the holding control; calls its slot `+0xc`; and sets the byte `+0` of a hotspot under the pointer. With no active window, or the point outside it, `DUIWindow_PressedIndex` becomes −1, and while the dword `0007fce4` is set, which screen 2000's case sets to `0008003c` ([the screens](menus.md#the-screens)), the point is tested against the selected hangar slot's rects: with *t* the type of `Hangar_SelectedSlot`'s entry in `000808a0`, or −1 when the slot is −1 or empty, the word `0007ff92` counts through the `0x14`-byte rects of `0007fce4`[*t*], as many as the word at `000803c8` + 2 × *t*, to the first holding the point, and becomes −1 when none does; the message posted is (0, −1, that index, 1702), for the hangar's screens ([the menus' Open](menus.md#open)).
- **`ShellMouse_OnLeftRelease` (`0001caaa`)** does not test `DUIWindow_InputReady` either; unless the message box is up it clears it and sets it again on every way out. With no active window it sets `DUIWindow_PressedIndex` to −1, before the tracking test. While `DUIWindow_Tracking` is set, it puts the pressed control up and changed with no call of its slot `+0xc`, clears `DUIWindow_Tracking`, posts the message (0, the index, its slot `+0x2c`, its id) wherever the pointer is, and sets `DUIWindow_PressedIndex` to −1. Otherwise, with the point inside the window, `DUIWindow_OnRelease` (`0001b683`)(window, point) writes the control under the pointer, −1 for none, to `DUIWindow_PressedIndex` before it tests it: when that is the control pressed earlier it posts (0, the index, slot `+0x2c`, the id); a control under the pointer that does not hold the input is put up and changed (slot `+0xc`); `DUIWindow_PressedIndex` becomes −1; and a hotspot under the pointer has its byte `+0` set. The control pressed earlier is not put up by a release over another control or none: `DUIWindow_OnDrag` has put it up when the pointer left it. With the point outside, `DUIWindow_PressedIndex` becomes −1 and nothing is posted.

So a click of a button posts its id as the target, and the press shows the pushed state from the press to the release.

## The keys

**`DUIWindow_HandleNavKey` (`0001cc86`)**, the dialog's key handler, which `Shell_NavKeySubscriber` and `Shell_HandleInput` call ([the input](startup.md#the-input)), does nothing without an active window, and otherwise acts on seven codes, the `REPNE SCASD` table at `0007f0e0`:

| Code | Does |
|---|---|
| `Tab` (`0x0f`) | moves `DUIWindow_FocusIndex` to the next control, (focus + 1) % count, passing over controls whose *focusable* byte is 0; each pass of that walk first puts the pressed control, if any, up and changed, with no call of its slot `+0xc`, and clears `DUIWindow_PressedIndex`, so only the first pass finds one. Then it puts the pointer on the focus's center with `DUIWindow_PointAtControl` |
| `Shift+Tab` (`0x80f`), `Alt+Tab` (`0x20f`) | moves the focus to the previous focusable control, a focus below 1 becoming the count before 1 is taken off, and puts the pointer on it; the pressed control is left as it is |
| `Enter` (`0x1c`), `Shift+Enter` (`0x81c`) | presses the focused control, `DUIWindow_PressControl` (`0001b5bb`) |
| `Enter`'s release (`0x9c`), with `Shift` (`0x89c`) | releases it, `DUIWindow_ReleaseControl` (`0001b7d5`) |

`Shell_NavKeySubscriber` passes `Tab`, `Enter` and `Enter`'s release only ([the keyboard subscribers](startup.md#the-keyboard-subscribers)). Neither walk has a bound or a test of the count: on a window with no controls `Tab` divides by zero, and on one with no focusable control both walks loop for ever.

`DUIWindow_PointAtControl`(window, index) sets `Shell_PointerX` and `Shell_PointerY` to the control's center on the screen, the `int16` (right − left) / 2, truncating, + left + the window's left, and likewise for *y*, which the next frame gives the cursor and the mouse driver ([opening a menu](menus.md#opening-a-menu--menu_open-0003969e)).

`DUIWindow_PressControl`(window, index), for an index of 0 or more whose control is not inactive, lets go of the holding control, makes the control `DUIWindow_PressedIndex`, pushed and changed, and calls its slot `+0xc`. `DUIWindow_ReleaseControl`(window, index), for the same, keeps the `DUIWindow_PressedIndex` it finds, stores the index there, and posts the message (0, index, slot `+0x2c`, the id) when the index equals the value it found; then puts the control up and changed unless it holds the input, and sets `DUIWindow_PressedIndex` to −1.

**`DUIWindow_KeySubscriber` (`0001ba73`)**(code), registered for the window's key list ([the keyboard subscribers](startup.md#the-keyboard-subscribers)), answers 1 at once while `DUIWindow_KeysEnabled` is clear. Otherwise, with *L* the last code `DUIWindow_LastKey` (`0007f0c4`) (disassembly `0001ba8e`–`0001baf2`):

1. it answers 2 when (*L* = `0xff`) & code, tested in the low byte, is not 0: only when *L* is `0x00ff` and the code is odd;
2. when *L* is not 0, its bit 7 is clear, and (code = `0x7f`) & *L*, tested against `0x7f`, is not 0, the code becomes *L* | `0x80`: only for the code `0x7f` after an odd press;
3. it stores the code in `DUIWindow_LastKey`, and answers 2 for a code with `Alt`, `Ctrl` or the shift bit (`0xe00`);
4. it looks code & `0xff7f` up among the enabled controls' keys with `DUIWindow_FindControlByKey` (`0001bd41`), which compares its `char` argument, the low byte, the scan code without bit 7, and writes the answer straight into `DUIWindow_PressedIndex`, −1 included. Found, the control takes the pointer (`DUIWindow_PointAtControl`) and the focus, is pressed (`DUIWindow_PressControl`) on a press code and released (`DUIWindow_ReleaseControl`) on a release code, and the answer is 1; otherwise 2, with `DUIWindow_PressedIndex` left at −1.

Since the index is stored before the release, `DUIWindow_ReleaseControl` always finds it pressed and posts: a key's release posts its control's message even when the press was not seen. A key whose control is inactive clears `DUIWindow_PressedIndex`.

**Typed characters and other keys** go only to the holding control, the one whose `+0x1b` is 2: `DUIWindow_SendChar` (`0001bb85`)(window, word), for a word with bit `0x4000` and without `0x8000`, calls its slot `+0x3c` with a dword whose low byte is the word's and whose high half is the caller's `EDI`'s; `DUIWindow_SendKey` (`0001bbd2`)(window, word), from `Shell_ForwardKeyToDialog`, its slot `+0x40` with word & `0x7f` (`AND DI,0x7f` at `0001bbdf`), the same high half above it ([the input](startup.md#the-input)). `es2_xref.py` finds one call of `Shell_ForwardKeyToDialog`, from `Shell_ScanKeySubscriber`, and that subscriber's address loaded at four places: in the kind-2 control's `MTEditBox_Ctor` (`0001f442`), which registers it, in `MTEditBox_Dtor` (`0001f66f`), which removes it, and twice in screen 1320's case ([the keyboard subscribers](startup.md#the-keyboard-subscribers)).

## A message's path

**A control's release writes the four words of `Shell_PendingMessage`; the frame loop's next step 4 passes them to `Shell_HandleMessage`.** `es2_xref.py --decode` on `0007fcb0` and `0007fcb4` finds them written by `DUIWindow_OnRelease`, `DUIWindow_ReleaseControl`, `ShellMouse_OnLeftRelease` and `ShellMouse_OnLeftPress`'s hangar test, beside `Shell_HandleMessage`'s reset:

| Word | Holds |
|---|---|
| `0007fcb0` | the code: 0 from a control, −1 for no message |
| `0007fcb2` | the control's index in the window |
| `0007fcb4` | the value, the control's slot `+0x2c`: 1 for a button |
| `0007fcb6` | the target: the control's id |

So on the main menu a click of a button runs the main menu's handler, slot `+8` of `MainMenu`'s vtable, with the message, and then opens the screen of the button's number when it is one of `Shell_ScreenIds`. The main menu's 1003 is not among them, so its click changes no screen ([the ways out](startup.md#the-ways-out)). The handler's cases are [the main menu's](main-menu.md#the-buttons).

## Functions

| ES1 | Address |
|---|---|
| `DUIWindow_Activate`, `DUIWindow_AddControl`, `DUIWindow_Deactivate`, `DUIWindow_AddKey`, `DUIWindow_AddHotspot`, `DUIWindow_Offset` | `0001b0ff`, `0001b207`, `0001b25b`, `0001b296`, `0001b2de`, `0001b395` |
| `DUIWindow_OnPress`, `DUIWindow_PressControl`, `DUIWindow_OnRelease`, `DUIWindow_ReleaseControl`, `DUIWindow_OnDrag` | `0001b436`, `0001b5bb`, `0001b683`, `0001b7d5`, `0001b8fc` |
| `DUIWindow_KeySubscriber`, `DUIWindow_SendChar`, `DUIWindow_SendKey`, `DUIWindow_HitControl` | `0001ba73`, `0001bb85`, `0001bbd2`, `0001bc11` |
| `DUIWindow_FindControlByKey`, `DUIWindow_FindEnabledControlById`, `DUIWindow_FindControlById`, `DUIWindow_FindHotspotById`, `DUIWindow_HitHotspot` | `0001bd41`, `0001bda4`, `0001be16`, `0001be6f`, `0001beca` |
| `ShellMouse_OnMove`, `ShellMouse_OnLeftPress`, `ShellMouse_OnLeftRelease` | `0001c605`, `0001c7d8`, `0001caaa` |
| `DUIWindow_SetActive`, `DUIWindow_DrawActive`, `DUIWindow_OffsetActive`, `DUIWindow_ClearActive`, `DUIWindow_EnableControl`, `DUIWindow_DisableControl`, `DUIWindow_HandleNavKey` | `0001c536`, `0001c556`, `0001c566`, `0001c57c`, `0001c59d`, `0001c5d0`, `0001cc86` |
| `Input_DeviceSlot`, `Input_GetKeyboardDevice`, `Input_GetMouseDevice`, `Input_GetKeyboardDevice_Thunk`, `Input_GetMouseDevice_Thunk` | `00025899`, `000258df`, `00025912`, `00027bbf`, `00027bc9` |

## Globals

The values in the image are the ones the shell starts with.

| | Address | In the image |
|---|---|---|
| `Shell_PendingMessage`, four `int16` | `0007fcb0` | −1, −1, 0, 0 |
| `Shell_PointerX`, `Shell_PointerY` | `0009aff0`, `0009aff4` | |
| `DUIWindow_Active` | `0007f0d0` | 0 |
| `DUIWindow_PressedIndex`, `DUIWindow_HeldIndex`, `DUIWindow_FocusIndex`, `int16` | `0007f0d4`, `0007f0d6`, `0007f0d8` | −1 each |
| `DUIWindow_Tracking`, `DUIWindow_Holding`, `DUIWindow_InputReady`, `DUIWindow_KeysEnabled`, bytes | `0007f0da`, `0007f0db`, `0007f0dc`, `0007f0dd` | 0, 0, 1, 1 |
| `DUIWindow_LastKey`, `int16` | `0007f0c4` | 0 |
| the mouse callbacks' slots, `int16` | `0007f0c8`, `0007f0ca`, `0007f0cc` | |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `DUIWindow_KeySubscriber` answers 2 for a repeat of the last code, and takes a code whose low seven bits match a last press as that key's release | That is the evident intent of its two tests, with the mask applied to one side of the comparison. The code applies `==` before `&`: the repeat test is (*L* = `0xff`) & code and the release test (code = `0x7f`) & *L*, so they act only for the codes the steps in [the keys](#the-keys) give |

## Open

- **Open:** a retail check under DOSBox-X: on the main menu, `Tab` moves the pointer onto the next button and `Enter` presses it; pressing a button and dragging off it before the release opens nothing.
