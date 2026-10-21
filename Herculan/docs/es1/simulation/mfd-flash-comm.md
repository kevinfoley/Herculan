# DBSIM.EXE — the MFD's FLASH COMM screen

**ES1's FLASH COMM screen is ES2's ([`MFDFlashComm`](../../retail/simulation/mfd.md#mfdflashcomm--mode-1)): six squad orders, one to a row, the selected one in yellow with its hotkey letter in red, rows 4 and 5 switching to their second order once squadmates take the first.** What a player would see differs: the six rows are spread evenly down the whole screen, each a text line tall, on a background of palette index `0x3f`, and the selected row has no frame around it. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00035bdc`–`00036023` and the initialized data `0009387a`–`0009388d`, between [the scanner's](mfd-scanner.md#where-the-object-file-starts-and-ends) and [the missile camera's](mfd-missile-camera.md#where-the-object-file-starts-and-ends). The code opens with `HeapPtr_DtorFlashCommCopy` (`00035bdc`), a lone copy of the 40-byte destructor that opens each run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)); `es2_xref.py` finds no reference to it ([Open](#open)). `MfdFlashComm_DisableRow`'s `RET 8` at `0003601f` is followed by two zero bytes ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer. The data is `MFDFlashCommVtable` (`0009387a`): a zero dword, then the paint and the update.

## The screen object

**`MfdFlashCommScreen_Ctor` (`00035c04`)**, (screen, display, rect), allocates `0x35` bytes when given none, runs `MFDisplay_Ctor` and stores `MFDFlashCommVtable` at `+8` and the display at `+0xc`. The display's constructor (`00039c64`) builds it as the screen of mode 1, at its `+0x9a`, with the rect `+0xb4` and the block `+0x7a` ([the status screen](mfd-status-screen.md#the-screen-object)), whose first dword is the selected row.

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

**`MfdFlashCommScreen_Paint` (`00035d20`)**, (screen), slot `+4`, fills the rect with the brush {0, `0x3f`} at the render context's (`000bdbda`) `+0x22c`, copies the selected row into `+0x2e`, and for each row sets the font, `cpoff` for bit 0 of its state byte, else `cpylw` for the selected row and `cpgreen` for the others, and writes its order with `Label_SetTextWithHotkey`(label, the order's attribute byte, the order's text), the order being the row, plus 3 when bit 1 of its state byte is set ([`STRINGS0.STR` group 0](../formats/str-strings.md#the-groups)). ES2's paint is the same, then blits the selected row's plate, which ES1's has no counterpart of.

**`MfdFlashCommScreen_Update` (`00035e14`)**, (screen), slot `+8`, is ES2's without the plates: when the selected row differs from `+0x2e`, the force byte is set or `+0x33` is not −1, it rewrites the old row (`+0x33` when set, else `+0x2e`) in `cpgreen`, whatever its state, and the new row in `cpylw`, each followed by `Widget_DrawToCockpit` over the label's rect as ES2's are, stores the new row in `+0x2e` and clears the force byte; it sets `+0x33` to −1 in either case.

## The order helpers

Each is ES2's statement for statement on ES1's offsets:

- **`MfdFlashComm_SelectedVerb` (`00035f4c`)**, (screen): the row `+0x2e`, plus 3 when bit 1 of its state byte is set.
- **`MfdFlashComm_SetScreenRow` (`00035f80`)**, (screen, row): copies `+0x2e` into `+0x33` and stores the row at `+0x2e`.
- **`MfdFlashComm_ToggleRowVariant` (`00035f9c`)**, (screen): for row 4 or 5 alone, flips bit 1 of its state byte and sets the force byte.
- **`MfdFlashComm_ResetRow` (`00035fcc`)** and **`MfdFlashComm_DisableRow` (`00035ff8`)**, (screen, row): clear the state byte to its bit 0 and put the hotkey font back to `cpred`, or set bit 0 and the hotkey font to `cpoff`. `es2_xref.py` finds no reference to either, as for ES2's ([Open](#open)).

**`MfdFlashComm_Transmit` (`0003adc8`)**, (display), in the display's own code: stores `MfdFlashComm_SelectedVerb` of the screen at `+0x9a` in the order record `000b0ddc`, passes the display's `+0x10` and the record to `Squad_BroadcastOrderFromCockpit`, and toggles the row variant when that returns non-zero, as ES2's does ([`ai-squadmates.md`](../../retail/simulation/ai-squadmates.md)). `es2_xref.py` finds it called from `MfdButton_OnClick` (`0003a46c`, [below](#the-displays-buttons-and-keys)) and seven times from `MfdFlashComm_HandleAltKey` (`0003a890`), and its address also as a dword at `000258cc`. Each of the handler's seven cases runs `MfdFlashComm_SetScreenRow` on the screen with its row, transmits, and passes (display, row, the display's `+0xc4`) to `0003acd4`, ES2's order of calls ([keyboard](../../retail/simulation/mfd.md#keyboard)): [Alt]+A, G, H, O and F rows 0, 1, 2, 3 and 5, and [Alt]+C and E row 4, these two transmitting and calling `0003acd4` only when `MfdFlashComm_SelectedVerb` then answers 4 for C and 7 for E, as ES2's do. The five cases from `0003a8fc` are undefined bytes in `ES1Recon`, read here as the image's bytes.

## The display's buttons and keys

**A click on the display's button 9, or `T`, steps the target selection as `Tab` does, whichever screen the display shows; ES2's TARGET button does the same, pressed by [Alt]+T, on the scanner screen alone.** These are the display's own code, after the screens' files; its constructor (`00039c64`) stores its table `00093fb6` at `+0x76`, which holds `MfdButton_OnClick` at `+4`, `0003a1f0` at `+8`, `0003a2a8` at `+0xc` and `MfdDisplay_KeyDispatch` at `+0x10`, each by its padding entry. The mode is the display's `+0x8a`, its buttons the dwords from `+0x14`, their count `+0x64`.

- **`MfdButton_OnClick` (`0003a46c`)**, (display, flags, child), the slot a button calls when it is clicked or pressed ([the gadget bases](cockpit-widgets.md#the-gadget-bases)), finds the child among the buttons and switches on its index: 0 to 5 call `0003a9ec`(display, index); 6 `0003a9c4`(display); 7, in mode 3, `Mech_ToggleRadarMode` on the cockpit's machine; 8 `0003ab1c`(display) and 9 `Cockpit_CycleTarget`, in any mode; 10, in mode 1 and while `CockpitMouse_DoubleClick` (`00094c5c`) is clear, `MfdFlashComm_Transmit`; and 11 and 12, in mode 1, `0003abac` and `0003ab70`. Cases 8 to 12 then set the byte `+0xae` + mode. ES2's (`0044681c`, [button visibility](../../retail/simulation/mfd.md#button-visibility)) takes (display, button), gives 7 and 9 one case that steps the status screen's subject in mode 0 and cycles the target in the others, stores 0 or 1 in the machine's `+0x96` on 11 and 12, PASS and ACTIVE, and tests the mode only in that shared case.
- **`Cockpit_CycleTarget` (`0003ab08`)**, (display): `TargetSelect_Cycle`(`CockpitViewInstance`) ([the target selection](cockpit-panel.md#the-target-selection)), removing its argument with `RET 4`. ES2's (`00446f88`) takes none.
- **`MfdDisplay_KeyDispatch` (`0003a5e8`)**, (display, code), the display's key slot, which `CockpitWidgets_HandleCommand` reaches last in its chain outside view 1 ([keys](cockpit-panel.md#keys-and-the-view-device)): it answers 0 while `CockpitView_RectCornerVisible` refuses the display's rect `+0xb4`. Six codes, the table `00094054`, press a button when the byte `00093f64` + 13 × mode + button is set, and return 1 either way: `0x20` (`D`) button 7, `0x213` ([Alt]+R) button 8, or `0003ab1c`(display) when its byte is 0, `0x14` (`T`) button 9, `0x2d` (`X`) button 10, `0x33` (`,`) button 11 and `0x34` (`.`) button 12. A press skips a button whose `int16` `+0x17` is 2 or 3 and otherwise calls its click, slot `+0xc` of its table at `+0x1c`, with (button, 1), as `CockpitWidgets_HandleCommand`'s does. In mode 1, `0x1e`, `0x22`, `0x23`, `0x18`, `0x2e`, `0x12` and `0x21` (`A`, `G`, `H`, `O`, `C`, `E`, `F`), the table `00094084`, pass (display, row, `+0xc4`) to `0003acd4` for rows 0, 1, 2, 3, 4, 4 and 5 and return 1. Any other code answers 0. The handlers of `0x14` to `0x34` and of rows 2 to 5 are undefined bytes in `ES1Recon`, read here as the image's bytes. ES2's (`004469c0`) presses button 9 on [Alt]+T (`0x214`) and steps to the previous or next available row on `,` and `.`.

The six 13-byte rows from `00093f64` hold, by mode: 0, 2, 4 and 5 buttons 0–6 and 9; 1 buttons 0–6 and 9–12; 3 buttons 0–9. So `T` presses button 9 in every mode, while the display's rect is on screen, and `D` and [Alt]+R press 7 and 8 in mode 3 alone and `X`, `,` and `.` press 10 to 12 in mode 1 alone. `0003a9ec` reads the same table; ES2's is the buttons' visibility table, `MfdButtonVisibility` ([button visibility](../../retail/simulation/mfd.md#button-visibility)), which shows TARGET in mode 3 alone.

`0003ab16`–`0003ab1a`, after `Cockpit_CycleTarget`, is two `0x90` bytes and a frameless `RET 8`, `MfdDisplay_StubNoOp` (`0003ab18`), before `0003ab1c`'s padding byte; `es2_xref.py` finds no branch or stored pointer to any of its three addresses ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorFlashCommCopy` | `00035bdc` | the code of `HeapPtr_DtorPanelCopy` (`0002acdc`) |
| `MfdFlashCommScreen_Ctor`, `MfdFlashCommScreen_Paint`, `MfdFlashCommScreen_Update` | `00035c04`, `00035d20`, `00035e14` | `0043f5d8`, `0043f7a4`, `0043f878` |
| `MfdFlashComm_SelectedVerb`, `MfdFlashComm_SetScreenRow`, `MfdFlashComm_ToggleRowVariant` | `00035f4c`, `00035f80`, `00035f9c` | `0043f998`, `0043f9b8`, `0043f9d0` |
| `MfdFlashComm_ResetRow`, `MfdFlashComm_DisableRow` | `00035fcc`, `00035ff8` | `0043f9f4`, `0043fa14` |
| `MfdFlashComm_Transmit` | `0003adc8` | `00447220` |
| `MfdButton_OnClick`, `MfdDisplay_KeyDispatch`, `Cockpit_CycleTarget`, `MfdDisplay_StubNoOp` | `0003a46c`, `0003a5e8`, `0003ab08`, `0003ab18` | `0044681c`, `004469c0`, `00446f88`; `0003ab18` none |

## Globals

| | Address |
|---|---|
| `MFDFlashCommVtable` | `0009387a` |
| the order record `MfdFlashComm_Transmit` broadcasts | `000b0ddc` |

## Open

- **Open:** what calls `HeapPtr_DtorFlashCommCopy`, `MfdFlashComm_ResetRow` or `MfdFlashComm_DisableRow`: `es2_xref.py` finds no branch or stored pointer to any of them, so no row is known to draw in `cpoff`.
- **Open:** the undefined bytes in `ES1Recon` of `MfdFlashComm_HandleAltKey`'s cases from `0003a8fc` and of `MfdDisplay_KeyDispatch`'s from `0003a6c5` and `0003a839`; a `REPNE SCASD` repair would give them Ghidra code. Also `0003acd4`, unread, which ES2's order makes `MfdFlashComm_SelectRow`, and the callees of `MfdButton_OnClick` and `MfdDisplay_KeyDispatch`, `0003a9ec`, `0003a9c4`, `0003ab1c`, `0003abac` and `0003ab70`, unread.
- **Open:** whether an instrument before the display in `CockpitWidgets_HandleCommand`'s chain takes `0x14` (`T`) first: the key slots of `+0x1d4`, `+0x1dd`, `+0x1e1`, `+0x1e5` and `+0x1ed` are unread; the gunsight's, `+0x1f1`, answers 0. A retail check: in the forward view with a hostile in range and ahead, `T` puts the target box on it, as `Tab` does, and a click on the display's button 9 does too.
- **Open:** what calls `MfdDisplay_StubNoOp`: `es2_xref.py` finds no branch or stored pointer to `0003ab16`, `0003ab17` or `0003ab18`.
- **Open:** which of the display's buttons is 9 on screen, its rect and its art.
- **Open:** whether any order's attribute byte in the retail `STRINGS0.STR` is past its text's length, which would draw a character from the stack ([`Label_SetTextWithHotkey`](cockpit-widgets.md#labels)).
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: with FLASH COMM up, `BP <CS>:00035e14` fires each frame and, after the selection moves, `BP <CS>:00031294` fires twice in it, once for the old row and once for the new.
