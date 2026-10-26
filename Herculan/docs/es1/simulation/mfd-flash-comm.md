# DBSIM.EXE — the MFD's FLASH COMM screen

**ES1's FLASH COMM screen is ES2's ([`MFDFlashComm`](../../retail/simulation/mfd.md#mfdflashcomm--mode-1)): six squad orders, one to a row, the selected one in yellow with its hotkey letter in red, rows 4 and 5 switching to their second order once squadmates take the first.** What a player would see differs: the six rows are spread evenly down the whole screen, each a text line tall, on a background of palette index `0x3f`, and the selected row has no frame around it. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00035bdc`–`00036023` and the initialized data `0009387a`–`0009388d`, between [the scanner's](mfd-scanner.md#where-the-object-file-starts-and-ends) and [the missile camera's](mfd-missile-camera.md#where-the-object-file-starts-and-ends). The code opens with `HeapPtr_DtorFlashCommCopy` (`00035bdc`), a lone copy of the 40-byte destructor that opens each run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)); `es2_xref.py` finds no reference to it ([Open](#open)). `MfdFlashComm_DisableRow`'s `RET 8` at `0003601f` is followed by two zero bytes ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer. The data is `MFDFlashCommVtable` (`0009387a`): a zero dword, then the paint and the update.

## The screen object

**`MfdFlashCommScreen_Ctor` (`00035c04`)**, (screen, display, rect), allocates `0x35` bytes when given none, runs `MFDisplay_Ctor` and stores `MFDFlashCommVtable` at `+8` and the display at `+0xc`. `MfdDisplay_Ctor` (`00039c64`) builds it as the screen of mode 1, at its `+0x9a`, with the rect `+0xb4` and the block `+0x7a` ([building the display](mfd-display.md#building-it)), whose first dword is the selected row ([picking a row](#picking-a-row)).

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the rect | `+0x04` |
| `+0x04` | the display's block; its `+0` is the selected row | `+0x08` |
| `+0x08` | the vptr | `+0x00` |
| `+0x0c` | the display | `+0x10` |
| `+0x10`–`+0x24` | the six row labels | `+0x14` |
| `+0x28`–`+0x2d` | the six row state bytes: bit 0 unavailable, bit 1 showing the second order | `+0x2c` |
| `+0x2e` | the screen's own row, the one `MfdFlashComm_SelectedVerb` resolves | `+0x32` |
| `+0x32` | the force byte | `+0x36` |
| `+0x33` | the previous row to repaint, `int16`, −1 for none | `+0x37` |

Each row is a `Label_CtorWithAltFont` in `ColorSchemePanels`' `cpgreen` whose hotkey font `+0x20` is then set to `cpred`, with the fill color `0x3f`, placed by `Label_SetRect` left-aligned with a margin of 1 pixel. Row *i* spans the rect's width and, with *s* = (*y1* − *y0*) / 6 truncated, runs from *y0* + *i s* to that plus the cell height of `cpgreen` (`HudFont_CellHeight` for `'0'`). ES2's rows run from an inset rect of their own, `7 << YCoordShift` apart with each sharing its bottom line with the next, in fill `0x11`, with a margin of `2 << XCoordShift` ([`MFDFlashComm`](../../retail/simulation/mfd.md#mfdflashcomm--mode-1)). The constructor clears the state bytes and the force byte and sets `+0x33` to −1.

## Painting

**`MfdFlashCommScreen_Paint` (`00035d20`)**, (screen), slot `+4`, fills the rect with the brush {0, `0x3f`} at the render context's (`000bdbda`) `+0x22c`, copies the selected row into `+0x2e`, and for each row sets the font, `cpoff` for bit 0 of its state byte, else `cpylw` for the selected row and `cpgreen` for the others, and writes its order with `Label_SetTextWithHotkey`(label, the order's attribute byte, the order's text), the order being the row, plus 3 when bit 1 of its state byte is set ([`STRINGS0.STR` group 0](../formats/str-strings.md#the-groups)). Only rows 4 and 5 take bit 1 (`MfdFlashComm_ToggleRowVariant`), so the orders it writes are entries 0 to 5, 7 and 8, and in the retail `STRINGS0.STR` each one's attribute byte, its hotkey's index, lies inside its text: `ATTACK MY TARGET` 0, `IGNORE MY TARGET` 1, `HELP ME OUT!` 0, `JOIN ON ME` 5, `SCAN FOR HOSTILES` 1, `FIRE AT WILL` 0, `EMCON` 0 and `HOLD YOUR FIRE` 10 (`HercWorks.Query strings --group 0`). Entries 6 and 9 are empty, with attribute 0. ES2's paint is the same, then blits the selected row's plate, which ES1's has no counterpart of.

**`MfdFlashCommScreen_Update` (`00035e14`)**, (screen), slot `+8`, is ES2's without the plates: when the selected row differs from `+0x2e`, the force byte is set or `+0x33` is not −1, it rewrites the old row (`+0x33` when set, else `+0x2e`) in `cpgreen`, whatever its state, then stores the selected row in `+0x2e` and rewrites it in `cpylw`. Each row's order is the row, plus 3 when bit 1 of that row's own state byte is set; each is written with `Label_SetTextWithHotkey`(label, the order's attribute byte, the order's text) and copied to the screen with `Widget_DrawToCockpit`(0, the label's rect `+4`, 1), as ES2's are. It then clears the force byte; it sets `+0x33` to −1 in either case.

## The order helpers

Each is ES2's statement for statement on ES1's offsets:

- **`MfdFlashComm_SelectedVerb` (`00035f4c`)**, (screen): the row `+0x2e`, plus 3 when bit 1 of its state byte is set.
- **`MfdFlashComm_SetScreenRow` (`00035f80`)**, (screen, row): copies `+0x2e` into `+0x33` and stores the row at `+0x2e`.
- **`MfdFlashComm_ToggleRowVariant` (`00035f9c`)**, (screen): for row 4 or 5 alone, flips bit 1 of its state byte and sets the force byte.
- **`MfdFlashComm_ResetRow` (`00035fcc`)** and **`MfdFlashComm_DisableRow` (`00035ff8`)**, (screen, row): clear the state byte to its bit 0 and put the hotkey font back to `cpred`, or set bit 0 and the hotkey font to `cpoff`. `es2_xref.py` finds no reference to either, as for ES2's ([Open](#open)).

**`MfdFlashComm_Transmit` (`0003adc8`)**, (display), in the display's own code: stores `MfdFlashComm_SelectedVerb` of the screen at `+0x9a` as a word (`MOV [000b0ddc],AX`), the verb at `+0` of the order record `HddOrderRecord` (`000b0ddc`), the record the Heads-Down Display's `XMIT` also fills ([presses](heads-down-display.md#presses)), passes the display's `+0x10` and the record to `Squad_BroadcastOrderFromCockpit`, which then stores the issuer at `+2` ([the squad order broadcast](mission-groups.md#the-squad-order-broadcast)), and toggles the row variant when that returns non-zero, as ES2's does ([`ai-squadmates.md`](../../retail/simulation/ai-squadmates.md)). The verb comes from the screen's own row `+0x2e`, which the paint and the update set to the selection, not from the selection itself. `es2_xref.py` finds it called from `MfdButton_OnClick` (`0003a46c`, [the display's clicks](mfd-display.md#clicks-and-keys)) and seven times from `MfdFlashComm_HandleAltKey` (`0003a890`), and its address also as a dword at `000258cc`.

Each of the handler's seven cases, found by `REPNE SCASD` over the seven codes at `000940bc` with their targets after them, runs `MfdFlashComm_SetScreenRow` on the screen with its row and returns 1 in `AL`. [Alt]+A, G, H, O and F, rows 0, 1, 2, 3 and 5, then transmit and pass (display, row, the display's `+0xc4`) to `MfdFlashComm_SelectRow`, ES2's order of calls ([keyboard](../../retail/simulation/mfd.md#keyboard)). [Alt]+C and E, row 4, transmit and call `MfdFlashComm_SelectRow` only when `MfdFlashComm_SelectedVerb` then answers 4 for C and 7 for E, as ES2's do; otherwise they jump straight to the return of 1. So [Alt]+C with row 4 showing its second order (verb 7), or [Alt]+E with it showing its first (verb 4), sends nothing but leaves `+0x2e` at 4 and the old row in `+0x33`, without setting the mode's byte that runs the update ([drawing the display](mfd-display.md#drawing-it)): until the update next runs, `MfdFlashComm_Transmit` from `XMIT` or `X` sends row 4's order rather than the selected row's ([Open](#open)). The five cases from `0003a8fc` are undefined bytes in `ES1Recon`, read here as the image's bytes. The display's buttons, `XMIT` (10) and the two row arrows (11 and 12) among them, and its keys, `X`, `,`, `.` and the unshifted order letters among them, are in [the display's doc](mfd-display.md#clicks-and-keys).

## Picking a row

**The selected row moves with a click on a row, with the order letters on FLASH COMM, and with the two arrow buttons or `,` and `.`, which skip unavailable rows; a double-click on the selected row presses `XMIT`.** The selection is the first dword of the display's shared block, its `+0x7a` ([the display object](mfd-display.md#the-display-object)), and each of these sets the display's byte `+0xaf`, mode 1's, so the screen's update runs on the next frame ([drawing the display](mfd-display.md#drawing-it)).

- **`MfdDisplay_SetSharedRow` (`0003abe8`)**, (display, row): stores the row at `+0x7a` when it differs, ES2's (`0044707c`).
- **`MfdFlashComm_NextAvailableRow` (`0003ab70`)** and **`MfdFlashComm_PrevAvailableRow` (`0003abac`)**, (display): step from `+0x7a` forward, 5 wrapping to 0, or back, 0 wrapping to 5, past each row whose state byte (the screen `+0x9a`'s `+0x28` + row) has bit 0 set, and store the row with `MfdDisplay_SetSharedRow`. ES2's (`00447014`, `00447048`).
- **`MfdFlashComm_SelectRow` (`0003acd4`)**, (display, row, widget): only when the widget is the display's click surface `+0xc4` and the mode is 1, `MfdDisplay_SetSharedRow`(display, row) and `+0xaf` = 1. ES2's (`00447130`) takes (display, widget, row). So an order letter, with or without [Alt], moves the selection only on FLASH COMM; [Alt] with a letter on another screen transmits the row without moving it.
- **`MfdFlashComm_HandleListClick` (`0003ac04`)**, (display, point, gadget), which `MFDListGadget_OnClick` passes the click's canvas point ([the click surface](mfd-display.md#the-click-surface)): in mode 1 alone, it tests the point against the six row labels' rects in row order, inclusive on all four edges, stopping at the first hit. A hit on another row than the selected one, or any hit without a double-click (`CockpitMouse_DoubleClick` clear), selects that row with `MfdDisplay_SetSharedRow` and sets `+0xaf`. A double-click on the selected row presses `XMIT`, the display's button 10 at its `+0x3c`, unless its `int16` state `+0x17` is 2 or 3, by its click slot `+0xc` with (button, 1). ES2's (`00447098`) presses `XMIT` through `Widget_PressChild`, transmits with `MfdFlashComm_Transmit` itself and sets the dirty byte ([keyboard](../../retail/simulation/mfd.md#keyboard)). `MfdButton_OnClick`'s case 10 transmits only while `CockpitMouse_DoubleClick` is clear ([clicks and keys](mfd-display.md#clicks-and-keys)), so this press reaches it with the flag set and sends nothing ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorFlashCommCopy` | `00035bdc` | the code of `HeapPtr_DtorPanelCopy` (`0002acdc`) |
| `MfdFlashCommScreen_Ctor`, `MfdFlashCommScreen_Paint`, `MfdFlashCommScreen_Update` | `00035c04`, `00035d20`, `00035e14` | `0043f5d8`, `0043f7a4`, `0043f878` |
| `MfdFlashComm_SelectedVerb`, `MfdFlashComm_SetScreenRow`, `MfdFlashComm_ToggleRowVariant` | `00035f4c`, `00035f80`, `00035f9c` | `0043f998`, `0043f9b8`, `0043f9d0` |
| `MfdFlashComm_ResetRow`, `MfdFlashComm_DisableRow` | `00035fcc`, `00035ff8` | `0043f9f4`, `0043fa14` |
| `MfdFlashComm_Transmit` | `0003adc8` | `00447220` |
| `MfdFlashComm_HandleAltKey` | `0003a890` | `00446c10` |
| `MfdDisplay_SetSharedRow`, `MfdFlashComm_NextAvailableRow`, `MfdFlashComm_PrevAvailableRow` | `0003abe8`, `0003ab70`, `0003abac` | `0044707c`, `00447014`, `00447048` |
| `MfdFlashComm_SelectRow`, `MfdFlashComm_HandleListClick` | `0003acd4`, `0003ac04` | `00447130`, `00447098` |

The display's own functions, `MfdButton_OnClick` and `MfdDisplay_KeyDispatch` among them, are in [the display's doc](mfd-display.md#functions).

## Globals

| | Address |
|---|---|
| `MFDFlashCommVtable` | `0009387a` |
| `HddOrderRecord`, the order record `MfdFlashComm_Transmit` broadcasts and the Heads-Down Display's `XMIT` sends | `000b0ddc` |

## Open

- **Open:** what calls `HeapPtr_DtorFlashCommCopy`, `MfdFlashComm_ResetRow` or `MfdFlashComm_DisableRow`: `es2_xref.py` finds no branch or stored pointer to any of them, so no row is known to draw in `cpoff`.
- **Open:** the undefined bytes in `ES1Recon` of `MfdFlashComm_HandleAltKey`'s cases from `0003a8fc` and of `MfdDisplay_KeyDispatch`'s from `0003a6c5` and `0003a839`; a `REPNE SCASD` repair would give them Ghidra code.
- **Open:** whether a double-click on the selected row transmits: `MfdFlashComm_HandleListClick` presses `XMIT`, whose case in `MfdButton_OnClick` sends nothing while `CockpitMouse_DoubleClick` is set, and the flag is set for the release whose click this is ([the frame's mouse events](cockpit-mouse.md#the-frames-mouse-events--cockpitmouse_processqueue-000444d0)). A retail check: on FLASH COMM, double-click the selected row and see whether a squadmate answers; then click `XMIT` once and see that one does.
- **Open:** whether a refused [Alt]+C or [Alt]+E shows in play. A retail check: on FLASH COMM, send row 4's first order once so that the row shows its second, select row 1, press [Alt]+C, then click `XMIT`: a squadmate's radio answer to row 4's second order rather than to row 1's, and row 4 showing its first order again, show the stale row.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: with FLASH COMM up, `BP <CS>:00035e14` fires on the frame after `.` moves the selection, and in it `BP <CS>:00031294` fires twice, once for the old row and once for the new.
