# The Multi-Function Display

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project. Addresses are DBSIM. Symbols are in `tools/ghidra_scripts/known_symbols_dbsim.json`; apply with `ES2ApplySymbolNames.java`.

The console screen the F1-F6 keys switch between six screens. Surrounding cockpit: [`cockpit-views.md`](cockpit-views.md), [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md). Caption text: [`../formats/str-strings.md`](../formats/str-strings.md).

How a click on one of the buttons below reaches `MfdButton_OnClick`: [`cockpit-input.md`](cockpit-input.md).

## Object model

| Symbol | Address | Role |
|---|---|---|
| `Gau_MfdPanelWidget` | `004324c8` | Builds the display from `.GAU` offset 728, then `MfdDisplay_SetMode(obj, 3)`. |
| `MfdGau_ApplyCoordShift` | `00447650` | Pre-shifts the `.GAU` block by `VideoMode_X/YCoordShift`. |
| `MfdDisplay_Ctor` | `00445218` | Builds screen, 13 buttons, the title and message labels, the `MFDListGadget` over the inset, 6 screen objects, and once per process a third label held in `0049cbd0` ([Open](#open)). |
| `MfdDisplay_SetMode` | `00446e38` | Switches screen; relights the mode column; applies the button visibility table. |
| `MfdDisplay_Repaint` | `00446138` | Full repaint: buttons, background, buttons, screen, title. |
| `MfdDisplay_Update` | `00446328` | Per-frame update; dispatches the current screen's update slot. |
| `MfdButton_OnClick` | `0044681c` | Button dispatch — indices 0-5 call `SetMode(i)`. |
| `MfdButton_SetCaption` | `00447358` | Repaint for the **momentary** button class (indices 7-10): frame from the shared press byte `+0x1b`, caption `"F%d"` for indices < 6 and the caption table otherwise, never re-fonted. |
| `MfdButton_Repaint` | `004474e4` | Repaint for the **latching** button class (indices 0-5, 11-12): frame and `DARK`/`WHITE` caption both from the button's own `+0x40` selection flag, never from the press byte. |

Object fields, base `MfdDisplay_Ctor`'s `param_1`:

| Offset | Contents |
|---|---|
| `+0x18` | 13 button pointers, `+0x18 + i*4` |
| `+0xb1` | Screen-shared state block, 0x10 bytes |
| `+0xbd` | Scanner range, from `_DAT_004d1cf4[+0xc5]` |
| `+0xc1` | Current mode; -1 before the first switch |
| `+0xc5` | Scanner zoom index, 0-2; init 2 |
| `+0xc9` | Title label |
| `+0xcd` | 6 screen objects, `+0xcd + mode*4` |
| `+0xeb` | Inset screen rect `x0, y0, x1, y1` |
| `+0xfb` | Base panel widget, covering the inset rect |
| `+0x100` | Coarse tick past which modes 0 and 4 next refresh |
| `+0x308` | The status screen's squad roster, four machine pointers — [The subject](#the-subject) |
| `+0x318` | Roster cursor |
| `+0x31c` | Roster count |
| `+0x329` | Message label |
| `+0x331`-`+0x341` | The power-up's sprite sequencer, its sequence set, sequence and frame table, and the `radar` bank — see [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#scanner-dish-grows) |
| `+0x345` | Coarse tick past which the power-up is done on a screen other than the scanner |

### Two button classes

`MfdDisplay_Ctor` switches on the button index and builds its 13 buttons through **two different classes**, which is why some MFD buttons show a pressed state and others do not:

| Class | Ctor | Indices | Repaint | Frame comes from |
|---|---|---|---|---|
| `MFDStateGadget` (latching) | `MFDStateGadget_Ctor` (`0044741c`) | 0-5, 11, 12, and 6 (below) | `MfdButton_Repaint` (`004474e4`) | its own selection flag `+0x40` |
| `MFDSelectGadget` (momentary) | `MFDSelectGadget_Ctor` (`004472e4`) | 7, 8, 9, 10 | `MfdButton_SetCaption` (`00447358`) | the shared press byte `+0x1b` |

The two names are the classes' own, from their descriptor records — [`cockpit-input.md`](cockpit-input.md#the-cockpits-own-gadget-classes) places them in the cockpit's widget hierarchy.

So the F-key column and the two scanner toggles (PASS, ACTIVE) **have no pressed state at all** — blue when unselected, green when selected — while SELECT, RANGE, TARGET and XMIT light *only* while held and have no selected state.

`MfdButton_Repaint`'s caption re-font test, `index < 6 || index - 0xb < 2`, covers the latching class's indices bar the degenerate 6: it is a class invariant restated, not a rule of its own. Only latching buttons ever re-font, which is why holding SELECT does not darken its caption.

Index 6 has **no case in the switch** (its jump-table entry lands past the assignments) and so keeps what index 5 left on the stack: the latching class, frames 3/4 and the `DARK` font. It is the degenerate zero rect no mode shows.

Per-button fields, base a button pointer from `+0x18`:

| Offset | Contents |
|---|---|
| `+0x28` | Button index 0-12 — what both the ctor switch and the caption re-font test key on |
| `+0x2c` | Caption label |
| `+0x30` | Two sprite pointers, unlit then lit |
| `+0x40` | Selection flag, **latching class only**. Set by the button's click handler (`MFDStateGadget_OnClick`, `004474a8`), and set and cleared by the display itself: `MfdDisplay_SetMode` for 0-5, `MfdButton_OnClick` for the PASS/ACTIVE pair 11-12, and the constructor. The press path sets the shared widget state byte `+0x1b` instead (see [`cockpit-input.md`](cockpit-input.md) §7) |

## Modes

Mode index = F-key - 1. `Gau_MfdPanelWidget` boots the display at mode 3.

| Mode | Key | Title | Screen ctor | Object size | Class |
|---|---|---|---|---|---|
| 0 | F1 | `STATUS` | `MfdStatusScreen_Ctor` `0043a2e0` | 0x42 | `MFDStatus` |
| 1 | F2 | `FLASH COMM` | `MfdFlashCommScreen_Ctor` `0043f5d8` | 0x49 | `MFDFlashComm` |
| 2 | F3 | `NAV MAP` | `MfdMapScreen_Ctor` `00440494` | 0x28 | `MFDMap` |
| 3 | F4 | `SCANNER` | `MfdRadarScreen_Ctor` `0043e70c` | 0x524 | `MFDRadar` |
| 4 | F5 | `TARGET` | `MfdStatusScreen_Ctor` `0043a2e0` | 0x42 | `MFDStatus` |
| 5 | F6 | `MISSILE CAM` | `MfdMissileViewScreen_Ctor` `0043facc` | 0x4a | `MFDMissileView` |

Modes 0 and 4 share one constructor and one class — the target screen is the status screen pointed at another object.

**The display switches itself to the missile cam while an electro-optical launcher is armed.** Every update, `MfdDisplay_SyncMissileCamMode` (`00447164`) tests whether the armed mount's `PROJ.DAT` record (`mount+0x20`) is a `Rocket` of subtype 3: `CockpitView_FindArmedMountGauge` (`00434310`) finds the cockpit weapon gauge whose mount is the armed one, and `WeaponGauge_MountProjectileType` (`00440a14`) and `WeaponGauge_MountProjectileSubtype` (`00440a3c`) read the record's type and subtype through that gauge. When the condition starts the update saves the current mode in `g_MfdMissileCamSavedMode` (`0049cbc4`) and selects mode 5, and when it ends it restores the saved mode; `g_MfdMissileCamMode` (`0049cbc6`) is 5 in between. Choosing a mode by hand meanwhile (`MfdDisplay_SetMode` resets `g_MfdMissileCamMode` and clears `g_MfdMissileCamArmed`, `0049cbc8`) keeps that choice until the condition ends, when the saved mode is restored over it. A condition that starts with the display already on mode 5 saves 5, and restoring it at the end calls `MfdDisplay_SetMode` with the current mode, which returns before it touches either global, so the switch goes on holding until a mode is chosen by hand.

Scanner ranges are `_DAT_004d1cf4` = 50000 / 100000 / 200000 world units = 300 / 600 / 1200 m at 1000 units = 6 m. Index 2 is the default, matching the retail screenshot's `RNG: 1200`.

## Geometry

One rect comes from the herc's `.GAU`, the panel rect in its MFD block at 728 ([layout and per-herc values](../formats/gau-cockpit-layout.md#gau-block-at-728)); everything inside is hardcoded in DBSIM.

**Screen inset.** The constructor applies `x0 += 0x12 << XCoordShift` and leaves `y0`, `x1`, `y1`, then works relative to that origin. The strip left of the inset holds the F-key column, which is why its table x values are negative. The inset region is 98x61 GAU inclusive = **196x122 device** = exactly the size of `MFD` bank frames 0-2.

Coordinates below are GAU (320-wide) units relative to the inset origin, inclusive on all edges. Device pixels are 2x in the 640-wide modes.

### Buttons

13 buttons from four parallel `int16` tables: `0049cacc` x0, `0049cae6` y0, `0049cb00` x1, `0049cb1a` y1.

| i | Rect | Size | `MFD` frames | Caption |
|---|---|---|---|---|
| 0-5 | `-16, 1+10i – -2, 8+10i` | 15x8 | 3 / 4 | `F1`-`F6` |
| 6 | `0,0 – 0,0` | — | 3 / 4 | `MODE` |
| 7 | `50,2 – 95,11` | 46x10 | 5 / 6 | `SELECT` |
| 8 | `4,36 – 30,45` | 27x10 | 7 / 8 | `RANGE` |
| 9 | `4,47 – 30,56` | 27x10 | 7 / 8 | `TARGET` |
| 10 | `50,2 – 95,11` | 46x10 | 5 / 6 | `XMIT` |
| 11 | `4,14 – 30,23` | 27x10 | 9 / 10 | `PASS` |
| 12 | `4,25 – 30,34` | 27x10 | 9 / 10 | `ACTIVE` |

The lower frame of each pair is unlit, the upper lit. Every rect size matches a `dba\MFD.DBA` frame size exactly, which is the layout's primary confirmation. Index 6 is a degenerate rect no visibility row selects. Indices 7 and 10 share a rect: one top-right button under two names.

### Button visibility

6 rows x 13 bytes at `0049cbd8`. `MfdDisplay_SetMode` and `MfdDisplay_Repaint` index it as `table[mode][button]`; the decompiler folds the `+6` entry offset into the symbol, so it reads as `DAT_0049cbde + mode * 13`. Indices 0-5 are 1 in every row.

| Mode | Aux buttons shown |
|---|---|
| 0 Status | 7 `SELECT` |
| 1 FlashComm | 10 `XMIT` |
| 2 NavMap | none |
| 3 Scanner | 8 `RANGE`, 9 `TARGET`, 11 `PASS`, 12 `ACTIVE` |
| 4 TargetStatus | 7 `SELECT` |
| 5 MissileCam | none |

`MfdButton_OnClick` gives **7 `SELECT` and 9 `TARGET` one shared case**, which branches on the current mode: mode 0 steps the status screen's squad roster (`MfdDisplay_StepStatusSubject`, `00446f9c` — [The subject](#the-subject)), every other mode calls `TargetSelect_Cycle`. So F5's SELECT and F4's TARGET are the same action, and both do what [Enter] does. On a status screen the press also scrambles the readouts for a refresh.

10 `XMIT` opens a transmission on FLASH COMM.

### Screen background

`MFD` frames 0-2 are three pieces of screen chrome, all 196x122: **0** two boxes split by a central divider, **1** one box spanning the content area, **2** one small box in the top-left corner with the rest open. `MfdDisplay_Repaint` selects by mode from the bank's frame-pointer array:

| Modes | Frame |
|---|---|
| 0, 1, 4 | 1 |
| 3 | 2 |
| 2, 5 | none — the blit is skipped |

**Frame 0 is never used as a background.** The nav map and missile cam fill the whole screen with their own image; the map's paint floods its rect first (below).

Verified against the retail reference crops: status, flash-comm and target show vertical borders only at the content area's outer edges, while the scanner shows an extra border at panel x 100-101 — frame 2's box edge at frame-local x 64-65 plus the inset.

### Fixed labels

| Label | Rect | Font | Align |
|---|---|---|---|
| Title | `4,0 – 40,9` | `WHITE` (`ColorSchemePanels[10]`) | left |
| Message | `22,46 – 74,52` | `DARK` (`[12]`) | centre |

The message label carries the speaking pilot's name during a transmission ([below](#transmissions)), and is blank otherwise. Its font is the text colour only; the plate behind it is filled with the speaker's slot colour ([`heads-down-display.md`](heads-down-display.md#the-gauge)), which the comm box publishes alongside the name.

### Label placement

`Label_SetRect` (`00438884`) anchors, `Label_SetText` (`00438920`) places. Together, given a rect, an alignment flag and a `short[4]` margin:

```
anchorX = flags & 2 ? ((x1 - x0) >> 1) + x0 + margin[0]  -- centre
        : flags & 4 ? x1 - margin[0]                     -- right
        :             x0 + margin[0]                     -- left
anchorY = ((y1 - y0) >> 1) + y0 + (inkHeight >> 1) + margin[2] + 1

width   = measure(text) - (1 << XCoordShift)
textX   = flags & 2 ? anchorX - (width >> 1) : flags & 4 ? anchorX - width : anchorX
textY   = anchorY - inkHeight
```

There is no vertical alignment flag: every label is vertically centred in its rect. Retail uses alignment 1 (left) for the title, the status labels, the flash-comm rows and the scanner's two captions, 2 (centre) for button captions, the message label, the `0049cbd0` label and the missile-cam labels, and 4 (right) for the scanner's two readings ([`mfd-scanner.md`](mfd-scanner.md#readouts)). All margins are zero except the flash-comm rows'.

`inkHeight` is the font's own `0x1a` header field (11 for `.HFN`), **not** its cell height (13) — see [`../formats/dfn-hfn-dci.md`](../formats/dfn-hfn-dci.md), "`inkHeight` and label placement". All the arithmetic is integer, both shifts included; doing it in floating point shifts a label up to a pixel on either axis.

**The label object** is every HUD caption in the simulator, the MFD's and the other displays' alike. `Label_Ctor` (`004387ac`), (label, font, panelRelative), zeroes `0x21` bytes and stores:

| Offset | Holds |
|---|---|
| `+0x00` | the font, which is the text's color ([HUD fonts](cockpit-hud-widgets.md#hud-fonts)) |
| `+0x04`–`+0x13` | the rect `{x0, y0, x1, y1}` |
| `+0x14`, `+0x16` | the anchor's x and y, `int16` |
| `+0x18` | panel-relative: `Label_SetText` subtracts the global blit offset (`004d25da`, `004d25de`) from the fill rect and the pen |
| `+0x19` | the alignment flags, 2 center and 4 right |
| `+0x1d` | the fill color; 0 draws no background |
| `+0x21` | the hotkey font, on a label built by `Label_CtorWithAltFont` |

`Label_SetText` fills the rect in the fill color, with the brush `{0, color}` installed at the current render context's `+0x22c` for the fill, before it draws the text. The other entry points:

- `Label_CtorWithAltFont` (`00438a70`), (label, font, panelRelative): `Label_Ctor`, then the same font again at `+0x21`, a `0x25`-byte label. `es2_xref.py` finds its calls in `MfdFlashCommScreen_Ctor` and `HddCommandScreen_Ctor`, and those of `Label_SetTextWithHotkey` in the two screens' paints and updates; the hotkey font the rows draw in is [below](#mfdflashcomm--mode-1).
- `Label_SetTextWithHotkey` (`00438aac`), (label, text, index): `Label_SetText` through `Label_SetText_Thunk` (`00438a94`), then, when index is at most the text's length, moves the pen x (`004d1838`) right by `HudFont_MeasureString` of the text's first index characters and draws `text[index]` again with `HudFont_DrawGlyph` in the font at `+0x21`. That is the order list's red hotkey letter ([the order rows](#mfdflashcomm--mode-1)).
- `Label_SetRectAroundPoint` (`004387d0`), (label, point, text, flags): sizes the rect to the text around a point instead of taking one. With the text's width w, flags 2 makes `x0 = x − w/2`, `x1 = x0 + w` and the anchor x their midpoint; 4 makes `x1 = x`, `x0 = x − w + 1` and the anchor x x; otherwise `x0 = x`, `x1 = x + w` and the anchor x x. `y0 = y − cellHeight/2` and `y1 = y`, and the anchor y is `inkHeight + y1 + y`, which counts y twice ([Open](#open)).
- `Label_SetNumber` (`00438a40`), (label, n): the decimal text of n through `_itoa`, then `Label_SetText` ([Open](#open)).

`Label_StaticInitNoOp` (`00438b1c`), a lone `RET`, is the label file's static initializer.

## Screens

### `MFDStatus` — modes 0 and 4

`MfdStatusScreen_Ctor` (`0043a2e0`) loads the `bases`, `vehicles` and `flyers` sprite banks — target silhouettes — and builds a wireframe viewport plus five labels.

- Wireframe viewport `45,13 – 95,58` (102x92 device).
- Five labels at x0 6, right edge 45, height 6. y0 from `0049bd8e` = 16, 23, 32, 40, 49. Font selector `0049bd98` = 0,1,0,1,1 into `{ColorSchemePanels[10] WHITE, [14] RED}`. Background `COLORS.DAT` id 0x11. Label text sources, all confirmed by xref:

| Label | Text | Source |
|---|---|---|
| 0 | `ID:` / `TARGET:` / `DIST:  ` | `DAT_004d1570`-`78` = group 20 |
| 1 | Subject name | group 17 `YOU`; a squadmate's pilot name (`HddGauge_Name`); a HERC's or flyer's own type-record name (`typeRec+0x58`, flyer `typeRec+0x12`); a structure's or vehicle's name from group 23 or 24; group 26 `NONE`, group 27 `UNKNOWN` |
| 2 | `STATUS:` | `DAT_004d157c` = group 21 |
| 3 | Condition | `DAT_004d1698[state]` = group 28 |
| 4 | Integrity or range | composed, below |

`MfdStatusScreen_SetCondition` (`0043b260`) writes labels 3 and 4. Label 4 is the literal `"[ "`, then `itoa((0x100 - damage) * 100 >> 8)`, then `"% ]"` — `[ 100% ]` undamaged. While a press is scrambling the screen ([below](#the-subject)) it writes `XXXXXX` and `XXX` instead.

**Group 10 is not the condition table.** It holds a near-identical five-string set (`OK`, `INT DMG`, `SHLD DWN`, `CRITICAL`, `WASTED`); the condition label reads group 28's array, and no reader of group 10's has been found ([Open](#open)).

#### The subject

`MfdDisplay_Update` (`00446328`) and `MfdDisplay_SetMode` both park the screen's subject in the display's shared state block at `+0xb9`, refreshed every 30 coarse ticks: for mode 0 the entry `+0x308[+0x318]` the SELECT button cycles, for mode 4 `CockpitView+0x210`, the current selection. Both screens read the same field, so **the two modes differ only in their subject**. Each paint latches it at the screen's `+0x3e`. When the parked subject is gone and the latched one is not, the paint drops a latched subject that is still in the fight at once, and holds one that is destroyed or immobilised (`+0x99` or `+0xa4`) until a paint more than 300 coarse ticks after the first that found it so, which arms the flag `+0x39` with that deadline at `+0x3a`. Only the hold running out clears `+0x39`: latching a new subject leaves it armed, so after a hold cut short by a new subject, the next dead subject to lose its parked entry is dropped at its first paint. The constructor and the paint are the only writers of `+0x39` (`es2_fieldscan.py 39` over the class).

The roster is filled once, at mission start: `Cockpit_LoadSquadmatePilots` (`00431530`) seats up to three squadmates in the heads-down display's comm boxes ([`heads-down-display.md`](heads-down-display.md#squad-comm-boxes)) and hands their count to `MfdDisplay_SetStatusRoster` (`00447294`), which puts the cockpit's own machine (`CockpitView+0x203`) in entry 0, those squadmates after it in box order, and the total in `+0x31c`. SELECT on mode 0 moves the cursor `+0x318` one entry on, back to 0 past the last. Nothing else writes the cursor and the display is allocated zeroed, so F1 opens on the player's own machine; nothing rebuilds the roster, so a destroyed squadmate keeps its entry.

**A SELECT press scrambles the status screen for one refresh.** Besides its action, the shared case sets the current mode's dirty flag and writes 100 to the current screen's `+0xc` — the only store of 100 to that field in the image. `MfdStatusScreen_Update` (`0043b210`) turns it into the flag `+0x34` with a deadline 30 coarse ticks ahead at `+0x35`, and clears the flag at its first run past the deadline. While the flag is set the paint skips a HERC's paper-doll blit and the pod highlight, label 1 reads `XXXXXXXXX`, label 3 `XXXXXX`, and label 4 `XXX` for a friendly or `DIST:  XXXXX` for a hostile. The update runs only on the 30-tick refresh, so the scramble goes up at the first refresh after the press — the same one that parks a stepped roster entry — and comes down at the first refresh past its deadline.

Each paint (`MfdStatusScreen_Paint`, `0043a5a0`) starts by flooding the viewport in palette index `0x11`, an immediate ([ids and immediates](../formats/colors-dat.md#ids-and-immediates)) installed as the brush {0, `0x11`} at the render context's `+0x22c`, over the viewport's rect with its top edge moved down and its right and bottom edges moved in by `1 << XCoordShift`. Everything it then chooses is a property of the subject, not of the mode:

| Test | Effect |
|---|---|
| Subject is the viewing object (`CockpitView+0x203`) or one of the three squadmates (`Squad_IndexOf`, `00433134`) | Label 0 is `ID:` and label 1 the pilot's name — `YOU` for the machine being flown; otherwise `TARGET:` and the type name |
| Group record's side byte (`obj+0x45` → `+0x12`) | Label 1's font, for a HERC only: `ColorSchemePanels[1]` `CPGREEN` for a friendly, `[2]` `CPRED` for a Cybrid. The flyer and structure branches set no font, so label 1 keeps the last one written: the constructor's `[14]` `RED` until a HERC or empty subject has been painted on that screen, and that paint's font after |
| Same byte | A friendly gets the integrity readout in label 4; a hostile gets group 20 entry 2 `DIST:  ` with the range appended (`Math_DistanceBetweenPoints` (`00492780`) between the two origins), in raw world units; v1.10 converts it to metres ([`../retail-builds.md`](../retail-builds.md#how-v110s-programs-differ)) |
| Target class `obj+0x1a8` | Which branch below draws the viewport, and how the condition is worked out |

With no subject at all the paint writes `TARGET:` and group 26 `NONE` in `ColorSchemePanels[0]` `CPBLUE`, and blanks labels 2-4.

The switch's default arm (`0043b117`), which writes `TARGET:` and group 27 `UNKNOWN`, is unreachable: every subject has a class from 0 to 3. A subject is a roster entry (the player's machine or a squadmate, all HERCs) or the F5 selection, which every writer of `CockpitView+0x210` takes from the live object list or clears. The three constructors that call `ObjectList_Add` — `Mech_Constructor`, `Flyer_Constructor` and each of `Base_Construct`'s five branches — write the `0xffff` placeholder to `+0x1a8` before the call and the class after it, with no branch in between. `es2_fieldscan.py 1a8` finds no other writer of the field.

#### Viewport and condition, per class

| Class | Viewport | Condition |
|---|---|---|
| 0 HERC | The type's paper doll, `pdgView.origin + viewportTopLeft + (0x11, 2)` device, then per-region damage tints at the same origin (below). The paint reaches one view record through the mech type without computing an index; only view 2 fits: in all 21 retail `.PDG` files views 0 and 1 overrun the 51x46 GAU (102x92 device) viewport and view 2 does not | Scanned: `DESTROYED` if `obj+0x99`; else `CRITICAL` when all twelve dependent readings from `Component_FillDamageReadouts` are `>= 0x81`, `INT DAMAGE` when any is non-zero; else `SHIELDS DN` if `mech+0xb0`, else `OK`. `mech+0xb0` is the shields-down alert latch, which `Mech_DirectFireHitTest` sets on the machine the player is piloting (guard `mech+0xa3`) and `Mech_PerTickSystemsUpdate` clears ([`component-damage.md`](component-damage.md#what-the-endpoint-announces)), and `TargetSelect_CanTarget` admits only the other side to the F5 selection, so a HERC on F5 does not read `SHIELDS DN` from that writer ([Open](#open), [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md)) |
| 2 flyer | `flyers` bank frame 0, centred in the viewport by its own frame size | `Damage_ToConditionState(damage)`: intact ≥ 90% `OK`, ≥ 74% `SHIELDS DN`, ≥ 51% `INT DAMAGE`, ≥ 1% `CRITICAL`, else `DESTROYED` |
| 1, 3 structure | `bases` or `vehicles` bank, frame = the type record's `+0x28`, centred the same way | as above |

Damage is the object's vtable `+0x40` as a Q8 fraction — `Component_ReadOverallDamage` (`0040db2c`) over every component and dependent for a machine, the component sum for a structure.

`BASES.DAT +0x28` is both the silhouette frame and the type-name index: into group 23's 31 structure names when `+0x32` is 0, and group 24's four vehicle names when it is not. Confirmed by construction — all 45 structure types in the retail `BASES.DAT` state a value in 0-30 (30 distinct) and all 20 vehicle types one in 0-3 (all four used), against `BASES.DBA`'s 31 frames and `VEHICLES.DBA`'s 4.

The constructor loads all three banks from `hba\` when `VideoMode_UseHiResBanks` is set and from the 320-wide `dba\` otherwise; `bases` and `vehicles` ship both in `SIMVOL0.VOL`, and `flyers`' `.HBA` ships only in `SIMPATCH.VOL`. With `VideoMode_PanelMode == 3 && VideoMode_UseHiResBanks == 0` ([`cockpit-views.md`](cockpit-views.md#video-modes)) the flyer and structure branches both centre by the frame size doubled, matching the doubled blit.

#### Region tints

Mechanism, region record and colour ladder: [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#tinting). What differs here is the reading each region takes, because view 2 is a compact doll whose regions cover more than one component. Indices are into `Component_FillDamageReadouts`' buffer, whose entries 1-19 are the armour components:

| Region id | Walker | Flyer chassis (`typeRec+0x50`) |
|---|---|---|
| 0 | mean of 1, 2 — the two cockpit halves | 1 |
| 4 | 5 | 5 |
| 5 | 6 | 6 |
| 6 | 7 | 7 |
| 7 | mean of 8, 10, 12 | 8 |
| 8 | mean of 9, 11, 13 | 9 |
| 13 | mean of 14, 16, 18 | — |
| 14 | mean of 15, 17, 19 | — |

The switch has no default arm and divides by its own count, so a region id it does not name would divide by zero. None does: every retail view 2 states a subset of these eight, six of them on a flyer chassis. The condition line takes no part in this — it is scanned from the internals, so **armour damage moves the doll and nothing else**.

Last of all, over the tints: with a hostile subject and the Targeting Pod's present flag at `CockpitView+0x27c` set, the paint outlines the `.PDG` region holding the component id beside it at `+0x27e`: `Raster_FillRect` with the brush in mode 4 ([outlines](../rendering/polygon-fill.md#rectangles-and-ellipses)) and `COLORS.DAT` id 16, over the region's own rect grown one device pixel on each side, so a one-pixel frame just outside the region, drawn over the damage tints. It stops at the first region that matches. The id only ever comes from a Targeting Pod (`mech+0x30b`) — [`target-selection.md`](target-selection.md#component-targeting--the-targeting-pod).

A region that does not state the component id itself is reached through a merge mapping, because the compact view folds each three-deep limb stack into one region: 1 → 0, 9 and 11 → 7, 10 and 12 → 8, 15 and 17 → 13, and **16 and 18 → 13 as well**, where the tint pass reads region 14 as the mean of 14, 16 and 18. That last pair is a transcription slip in the original and cannot be observed: the pod's rotation only ever produces 0, 4, 5, 7, 8, 9 and 10.

### `MFDFlashComm` — mode 1

`MfdFlashCommScreen_Ctor` (`0043f5d8`) builds six order rows.

Row block, device pixels relative to the inset origin: rect `2,0xd – 0x60,0x3a` GAU, both corners nudged in by `1 << XCoordShift`, giving x 6-190 and y0 28. Rows step `7 << YCoordShift` = 14 device. Both nudges use `XCoordShift` on the y axis — no effect in any retail video mode.

Each row's rect is `top` to `top + 14` **inclusive**, and the step is the same 14, so **every row shares its bottom line with the row below**. `MfdFlashComm_HandleListClick` walks the six in index order and stops at the first hit, so the shared line belongs to the upper row — the general rule in [`cockpit-input.md`](cockpit-input.md#registration-order-is-precedence).

Text margin `2 << XCoordShift` = 4 device — the only nonzero label margin on the display. Four fonts:

| Font | When |
|---|---|
| `ColorSchemePanels[1]` `CPGREEN` | an ordinary row |
| `[3]` `CPYLW` | the selected row, which the paint re-fonts as it fills it |
| `[6]` `CPOFF` | an unavailable row, one whose state byte has bit 0 set. `MfdFlashComm_DisableRow` (`0043fa14`) sets that bit and turns the row's hotkey font `+0x21` to `CPOFF` as well; `MfdFlashComm_ResetRow` (`0043f9f4`) puts the hotkey font back to `CPRED` and clears bit 1, keeping bit 0. Neither has a caller found ([Open](#open)) |
| `[2]` `CPRED` | the row's alternate at `+0x21`, which is **not** an unavailable state — `Label_SetTextWithHotkey` (`00438aac`) redraws exactly one character of the row in it, at the index the order's own attribute byte names, which is how the hotkey letter is picked out. The [F7] order list uses the same mechanism |

The selected row also carries a plate: `MFD` frames 11-13, 91x8 GAU, blitted by `MfdFlashCommScreen_DrawRowPlate` (`0043fa34`) **after** the text so the hollow rounded rect frames it rather than covering it. Frame 11 unpressed, 12 while XMIT is held — the index is `0xb +` that button's own press byte — and 13 the plain plate that erases a row which has just stopped being selected. `MfdFlashCommScreen_Update` (`0043f878`) repaints exactly those two rows when the cursor moves, rather than the whole block.

The screen is flooded with **palette index `0x11`** before any of it goes down — a constructor immediate, so an index and not a logical id ([`../formats/colors-dat.md`](../formats/colors-dat.md#ids-and-immediates)).

The six rows are six *positions*, not six of the eighteen orders: `MfdFlashComm_SelectedVerb` (`0043f998`) reads the selected row at `screen+0x32` and adds 3 when that row's own state byte at `screen+0x2c + row` has bit 1 set. `MfdFlashComm_ToggleRowVariant` (`0043f9d0`) is what flips that bit, after a transmission that at least one squadmate took, and it **returns immediately unless the row is 4 or 5**. So rows 0-3 always name `STRINGS0` group 0 verbs 0-3 (`ATTACK MY TARGET`, `IGNORE MY TARGET`, `HELP ME OUT!`, `JOIN ON ME`), row 4 alternates `SCAN FOR HOSTILES` and `EMCON` (verbs 4 and 7), row 5 `FIRE AT WILL` and `HOLD YOUR FIRE` (5 and 8), and the page sends verbs 0-5, 7 and 8. XMIT (`MfdFlashComm_Transmit`, `00447220`) writes the resolved verb into the shared order record and broadcasts it to the whole of the player's group — [`ai-squadmates.md`](ai-squadmates.md).

Screen fields, based at `MfdDisplay+0xd1`:

| Offset | Contents |
|---|---|
| `+0x08` | Pointer to the display's shared state block (`display+0xb1`), whose first int is the selected row |
| `+0x10` | The display |
| `+0x14` | Six row label pointers |
| `+0x2c` | Six row state bytes — bit 0 unavailable, bit 1 showing the second verb |
| `+0x32` | The screen's own row, which is what XMIT resolves |
| `+0x36` | Dirty flag |
| `+0x37` | The previously-selected row, `-1` for none |
| `+0x39` | The row block's rect |

#### Keyboard

Two dispatches, not one. `CockpitWidgets_HandleCommand` (`00432bc8`) offers every code to `MfdFlashComm_HandleAltKey` (`00446c10`) first and then to the MFD widget's own command slot `MfdDisplay_KeyDispatch` (`004469c0`). Codes are PC set-1 scancodes, `+0x200` for [Alt].

| Code | Handler | Effect |
|---|---|---|
| `0x1e` `0x22` `0x23` `0x18` `0x2e` `0x12` `0x21` (A G H O C E F) | `MfdDisplay_KeyDispatch` (`004469c0`) | Select rows 0, 1, 2, 3, 4, 4, 5. Gated on the display being on mode 1 |
| the same seven `+0x200` | `MfdFlashComm_HandleAltKey` (`00446c10`) | Select **and transmit**, from any screen: `MfdFlashComm_SetScreenRow` (`0043f9b8`) makes the row the screen's own (`+0x32`), keeping the old one at `+0x37` for the update to repaint, `MfdFlashComm_Transmit` sends its verb, and `MfdFlashComm_SelectRow` selects it. `0x22e` only transmits when the resolved verb is 4 and `0x212` only when it is 7 |
| `0x2d` (X) | `MfdDisplay_KeyDispatch` (`004469c0`) | Press button 10 if the current mode shows it |
| `0x33` `0x34` (`,` `.`) | `MfdDisplay_KeyDispatch` (`004469c0`) | Previous / next available row, wrapping |
| `0x20` (D) | `MfdDisplay_KeyDispatch` (`004469c0`) | Press button 7 SELECT if visible |
| `0x213` `0x214` ([Alt]+R, [Alt]+T) | `MfdDisplay_KeyDispatch` (`004469c0`) | Press button 8 RANGE or 9 TARGET if the current mode shows it. `0x213` on a mode that does not show RANGE cycles the scanner range instead (`MfdDisplay_CycleScannerRange`) |

`MfdFlashComm_SelectRow(display, widget, row)` (`00447130`) writes the display's shared row **only when the mode is 1**, which is what lets an [Alt] hotkey pressed from another screen transmit a row the cursor never moved to. `MfdFlashComm_HandleListClick` (`00447098`) is the mouse path: it hit-tests the six label rects itself, inclusive on all four edges. A [double-click](cockpit-input.md#4-once-per-frame-the-real-clickpressdrag-logic) on the selected row presses XMIT through `Widget_PressChild`, which clicks and flashes it, and then transmits with `MfdFlashComm_Transmit` itself; any other click, a single one on the selected row included, selects the row it hits. There is no widget per row — the rows sit under the display's own `MFDListGadget`, which is the widget the shared hit test actually finds, and whose `OnClick` (`MFDListGadget_OnClick`, `00447630`) makes no sound ([`audio.md`](audio.md#sounds-a-cockpit-control-makes)).

**XMIT ignores a double-click.** `MfdButton_OnClick` (`0044681c`)'s case 10 returns before transmitting while the double-click flag is set, which is what stops the list's press from transmitting a second time. The same test makes a double-click on XMIT itself transmit once, on its first click, and a key press of XMIT made while the flag is still standing transmit nothing ([`cockpit-input.md`](cockpit-input.md#4-once-per-frame-the-real-clickpressdrag-logic)).

### Transmissions

A squadmate answering takes the whole inset, whichever screen is up. `MfdDisplay_Update` reads a block published at `CockpitViewInstance+0x1f9` — **the heads-down display's pilot roster**, not the MFD — and draws from it *before* it ever reaches the current screen's update slot, jumping past both that and the title refresh. So a transmission replaces the screen rather than sitting on it, and the title is absent for as long as it lasts.

| Offset | Contents |
|---|---|
| `+0x766` | The transmitting pilot's name pointer, and the claim on the block — set only while it is 0 |
| `+0x76a` | The frame to blit |
| `+0x772`, `+0x776` | Its `.OFS` offset pair |
| `+0x782` | Valid-this-frame flag, which the update also uses as its blit count |
| `+0x786` | The caption plate's `COLORS.DAT` id — the speaker's own comm-box colour |
| `+0x788` | The box's state; the caption is written only when it is 2 |

Not while the missile-cam switch holds: the update tests `g_MfdMissileCamMode` first, and a reply that comes in then leaves the screen to the camera.

Both of the comm box's video paints write it, *before* their own visibility test, so it works with the heads-down display panned up — [`heads-down-display.md`](heads-down-display.md#squad-comm-boxes) owns the box and its state machine.

The update floods the inset with palette index `0x11` and blits at `inset + (0x14 << XCoordShift, 0)` **plus the `.OFS` pair added raw** — the frame is drawn doubled, the offset is not.

**The picture is clipped to the inset.** After the buttons' updates and before the transmission, `MfdDisplay_Update` saves the live render context's `0x204`-byte clip block, copies the display's own (`+0x104`) over it, and restores the saved one on the way out. `MfdDisplay_Ctor` gives every display that block in the one-rectangle form — count 1 at `+0x104`, and at `+0x108` a pointer to `MfdDisplay_StaticRegion` (`004d1d34`), a function-local static `GLRectangleRegion` whose rect it overwrites with the inset rect (`+0xeb`..`+0xf7`). So a portrait whose `.OFS` offset lifts it above the screen loses its top rows at the bezel, and the same clip holds for the current screen's update, which runs under the same swap.

### `MFDMap` — mode 2

**The NAV MAP is the command display's terrain map at a fixed scale, centered on the player's machine and turned so that its heading points up, with a small cross at the center.** It has no labels, grid, border, markers or aux buttons, and no clamp, zoom or pan.

`MfdMapScreen_Ctor` (`00440494`), (screen, inset rect), allocates nothing itself: it runs `MFDisplay_Ctor`, stores `MFDNavMapVtable` (`0049c4be`) and copies the rect to `+0x18`–`+0x24`. It builds at `+0x10` a `0x239`-byte `GLViewport`, its origin `+0x220`/`+0x224` at −((*x1* − *x0*) >> 1), −((*y1* − *y0*) >> 1), so the map's center is 97, 60 device pixels into the inset on every retail herc, and its clip mode `+0x208` 1; and at `+0x14` a `0x22`-byte view, `View_Ctor`(view, the viewport, perspective shift 8, near plane `0x100`, position (0, 0, 0), angles `MfdMapScreen_ViewAngles` (`0049c454`)), which stores the viewport at `+0x16`, the shift at `+0x1a` and the near plane at `+0x1e`. `MfdMapScreen_StaticInit` (`004407f0`) sets those angles to {`0xc000`, 0, 0}, a pitch straight down. The constructor puts back the render context it found. The object is `0x28` bytes.

The table's paint slot `+0` is `MfdMapScreen_PaintNoOp` (`004405dc`), an empty body; the map is drawn by its update slot `+4`, `MfdMapScreen_Paint` (`004405e4`), (screen), every frame the screen is up ([paint order](#paint-order)):

1. While the view manager's view-change flag is set (`CockpitViewManagerPublished` `+0x1c`, [the view manager](cockpit-views.md#object-model)), it returns at once.
2. It floods the screen's rect (`+4`) with `COLORS.DAT` id 19, the brush {0, color} at the installed render context's `+0x22c`, which is why no screen chrome is blitted for this mode.
3. It sets the viewport's rect `+0x210`–`+0x21c` to the screen's rect less that context's origin `+0x220`/`+0x224`, copies the context's clip block (`0x81` dwords from `+4`) and its clip mode `+0x208` over the viewport's, and installs the viewport with `Raster_InstallRenderContext`.
4. It puts the view over the cockpit's machine (`CockpitViewInstance` `+0x203`): position `+4`–`+0xc` = the machine's `+0x26`, `+0x2a` and 200000; angles `+0x10`–`+0x14` = the first `int16` of `MfdMapScreen_ViewAngles`, 0 and 0. It clears the viewport's translation flag `+0x20c` and installs the view's projection with `Raster_InstallViewProjection`. A camera straight overhead at a height of 200000 with a focal length of 2⁸ = 256 maps about 781 world units, 4.7 m, to a device pixel, which is the command display's projection ([the map's frame of reference](heads-down-display.md#the-maps-frame-of-reference)). It also calls `View_GetActiveObject` and does not use the result.
5. `HddMap_DrawTerrain`(the machine's `int16` heading `+0x10`) blits the command display's terrain raster turned by that angle ([terrain raster](heads-down-display.md#terrain-raster)); the command display passes 0.
6. It sets the viewport's translation flag `+0x20c`, installs the pen {0, `COLORS.DAT` id 16} at its `+0x228`, and draws two lines with `Raster_DrawLine` (`004838f8`), (−(1 ≪ `VideoMode_XCoordShift`), 0)–(1 ≪ `VideoMode_XCoordShift`, 0) and (0, −(1 ≪ `VideoMode_YCoordShift`))–(0, 1 ≪ `VideoMode_YCoordShift`): a cross through the viewport's origin, each line five device pixels long in the 640-wide modes.
7. It reinstalls the render context it found and copies the screen's rect to the display with `Widget_DrawToCockpit`(1, rect, 0).

**Heading up.** The turn is a positive binary angle in screen space, clockwise on a screen whose y runs down. A heading counts the other way — a machine moves along `(-sin h, cos h)` — so turning the picture by the heading puts the nose at the top.

### `MFDRadar` — mode 3

The plan view, its turret wedge and its contact list: [`mfd-scanner.md`](mfd-scanner.md). Frames 14-18 are the whole of that screen's art. Frame 14 matching the `radar` bank's frame size is not a coincidence — that bank is the dish growing into frame 14's shape at power-up ([`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#scanner-dish-grows)).

### `MFDMissileView` — mode 5

`MfdMissileViewScreen_Ctor` (`0043facc`) builds an offscreen `GLViewport` over the inset rect with its origin at the rect's centre, as the nav map's is (97, 60 device pixels in), and a camera on it: `View_Ctor` with perspective shift 7 — a focal length of 128 device pixels, where the cockpit view's is 512 — and near plane `0x80`. Then four labels, centred, their x, y, width and height from four `int16` tables at `0049c374`, `0049c37c`, `0049c384` and `0049c38c`, built in `ColorSchemePanels[2]` `CPRED`; the constructor then re-fonts label 2 to `[3]` `CPYLW` on a `COLORS.DAT` id 5 plate:

| Label | Rect | Text |
|---|---|---|
| 0 | `15,21 – 83,31` | `READY TO` |
| 1 | `15,28 – 83,38` | `LAUNCH`, or `NONE` |
| 2 | `42,30 – 56,40` | none found ([Open](#open)) |
| 3 | `37,42 – 61,52` | `LOCK` |

The words are `STRINGS0.STR` group 35: `MISS`, `READY TO`, `LAUNCH`, `NONE`, `LOCK`.

**The screen paints only with something to show.** Its update slot, `MfdMissileViewScreen_Update` (`004402ec`), which the display calls every frame the screen is up, paints when `DAT_0049c394` holds a round, when the machine has lock — `mech+0x9b`, which `Player_PerFrameCockpitUpdate` copies to the roving gunsight's `+0xd6` every frame — or when the screen's own `+0x1c` is set ([Open](#open)). Otherwise the last paint stays up until the display's next full repaint.

`MfdMissileViewScreen_Paint` (`0043fe1c`) takes the first of three arms that applies:

1. **The strike flash.** While `DAT_0049c398` is raised — the tracked round ended short of its lifetime ([`rockets.md`](rockets.md#flight--rocket_tickupdate-0040a538)) — the first paint to see it stamps a deadline `0x1e` coarse ticks ahead in `+0x46`, and until then each paint floods the inset with `COLORS.DAT` id 19 and id 16 in turn, starting on 19, and returns. At the deadline it clears `DAT_0049c398` and goes on to the arms below.
2. **The view.** With `DAT_0049c394` set the paint walks the effect pool for the round. Gone, the round is forgotten — `DAT_0049c394` cleared — and the labels go up instead. Found, the camera stands at the round's position plus `Q14(500, cos h)` in x and `Q14(500, cos(h - 0x4000))` in y, `h` being the heading at `+0x10`, and takes the round's whole euler triple. That push is 500 units along the round's own X axis, to its right and level whatever its pitch, not along its heading, which carries an object along `(-sin h, cos h)`. `Raster_InstallViewProjection` installs the screen's view before anything is drawn, so the pass projects at its focal length of 128, and every machine's root and every detail part's level ([`../rendering/mech-shape-drawing.md`](../rendering/mech-shape-drawing.md#the-lod-root-is-chosen-per-frame-per-object)) is picked by its size on this screen, measured from this camera. The paint floods the inset with id 16, one column short of its right edge, and draws the world into it from that camera: `Scene_SubmitFrameObjects` with the camera as `ViewObjectPtr`, so the ground shapes submitted are those within range of the camera ([`ground-shapes.md`](ground-shapes.md#the-draw-pass)), then `Terrain_SetupVisibleRegion` and `Scene_DrawTerrain`, with `TerrainTexturingEnabled` forced off and the shade mode `004aab30` forced to 1 for the pass. The paint calls `Scene_DrawTerrain` itself rather than `Scene_DrawTerrainPass`, which is where the frame draw paints the sky backdrop ([`../rendering/distance-fog-and-sky.md`](../rendering/distance-fog-and-sky.md#the-sky--hzline)), so no horizon is painted and the id 16 flood is the sky. Over the world go two `Raster_DrawLine` lines in id 19 through the centre, the context's full width and full height, and a one-pixel `Raster_DrawEllipse` ring in id 19 of radius 30 device pixels, half the centre's own y. The cockpit's damage-shake offset is taken out of the context's rect for the pass, so the picture stays still while the cockpit shakes.
3. **The labels**, with no round: the inset floods with palette index `0x11`. When `CockpitView_SumLauncherCounts` (`00440348`) finds no rounds left in any launcher row, label 1 reads `NONE`; otherwise label 0 reads `READY TO` and label 1 `LAUNCH`. Label 3 reads `LOCK`, in `ColorSchemePanels[0]` `CPBLUE` on an id 4 plate without lock, and with it in `[3]` `CPYLW` on a plate of id 19 while coarse-tick bit `0x20` is clear and id 9 while it is set.

The display's update redraws the title over whichever arm painted.

## Paint order

`MfdDisplay_Repaint`: mode buttons 0-5, background, all visible buttons 0-12, the current screen's paint (`radar` frame 0 instead, while the display is [powering up](cockpit-hud-widgets.md#scanner-dish-grows) on the scanner), then the title. The background covers only the inset rect and the mode column sits left of it, so the first pass is not overdrawn.

`MfdDisplay_Update` calls the current screen's paint again whenever its dirty flag at `display+0xe5+mode` is set. Mode 1 clears it after one paint. The others leave it set: modes 0 and 4 repaint when a 30-tick timer expires, and modes 2, 3 and 5 every frame — for 2 and 5 the update then redraws the title, which their paint covers.

The `mfd_dmg` bank is the screen's sensor-dropout wipe, played over the inset at each change: [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#the-wipes).

## Open

- **Deferred:** what sets the MISSILE CAM screen's `+0x1c`, which its update slot tests beside the round and the lock. `es2_fieldscan.py 1c` over `0043f000`-`00440500` finds that read and no write.
- **Deferred:** `MfdMissileViewScreen_BlinkLockLabel` (`004403bc`), which toggles the MISSILE CAM screen's `+0x44` with coarse-tick bit `0x20` and repaints label 3 in `CPYLW` on id 9 or id 19 to match — the paint's lock blink, latched in a byte. `es2_xref.py` finds no branch to it and no stored pointer.
- **Deferred:** what the label `MfdDisplay_Ctor` builds once per process into `0049cbd0` is for — centred over the inset rect, zero margins, plate `COLORS.DAT` id 19 (`004d3c26`). `es2_xref.py 0049cbd0` finds four references, all in the constructor.
- **Deferred:** a reader of `STRINGS0.STR` group 10's array (`004d1440`). `es2_xref.py 004d1440` finds only `SimStrings_LoadAll` (`0043766a`), and the disassembly holds no operand in `004d1440`-`004d1453`; an access through a neighbouring group's base would show in neither.
- **Deferred:** a caller of `MfdFlashComm_DisableRow` (`0043fa14`) or `MfdFlashComm_ResetRow` (`0043f9f4`), the FLASH COMM row-state helpers. `es2_xref.py` reports both UNREFERENCED with no late function start nearby, and the only writes to the row state bytes found over `0043f000`-`00447fff` (`es2_fieldscan.py 2c` plus a disassembly grep for indexed byte writes) are the constructor's clear, `MfdFlashComm_ToggleRowVariant`'s `XOR` and these two. Without a caller no row draws in `CPOFF`.
- **Deferred:** what label 2 of the MISSILE CAM screen (`+0x3b`) shows. `es2_fieldscan.py 3b` over `0043facc`-`00440500` finds only the constructor's re-font, against two paint reads of label 1 (`+0x37`) as control.
- **Deferred:** a writer of `mech+0xb0` other than `Mech_DirectFireHitTest` (`00418dc7`, set) and `Mech_PerTickSystemsUpdate` (`0041ab25`, clear). `es2_fieldscan.py b0 --writes-only` over `00400000`-`004a0000` finds no other on a mech (the rest are widget dirty flags and the `004d2540` video block), and no `memset`, `memcpy` or `REP MOVS` onto a mech was found. A writer that reaches a machine the player is not piloting would let a HERC on F5 read `SHIELDS DN`.
- **Deferred:** what the 100 a TARGET press writes to the SCANNER screen's `+0xc` does. `es2_fieldscan.py c` over `0043a2e0`-`00440a00` finds the status screen's read of its own `+0xc` in `MfdStatusScreen_Update` and no read of the SCANNER screen's: both `+0xc` reads in its update and paint are of the shared state block, through `+0x8`.
- **Open:** a caller of `Label_SetRectAroundPoint` (`004387d0`) or `Label_SetNumber` (`00438a40`): `es2_xref.py` finds no branch to either and no stored pointer.