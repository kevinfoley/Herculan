# `gau\<HERC>.GAU` — cockpit layout

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses. Verified against retail data in `ES2/VOL/simvol0/gau/`.

One file per pilotable chassis, nine in retail — APOCA, COLOSSUS, MAVERICK, OGRE, OUTLAW, RAPTOR2, RAZOR, SAMSON and TOMAHAWK — holding the per-herc layout of the cockpit's instruments. `Gau_Load` (`00431778`) reads it and `Gau_BuildCockpitWidgets` (`00431bf8`) builds the cockpit's widgets from fixed offsets into it. What each widget does with its block is in the behaviour docs: [`cockpit-hud-widgets.md`](../simulation/cockpit-hud-widgets.md) for the console gauges, [`cockpit-gunsight-hud.md`](../simulation/cockpit-gunsight-hud.md) and [`hud-target-indicator.md`](../simulation/hud-target-indicator.md) for the front-window HUD, [`mfd.md`](../simulation/mfd.md) and [`mfd-scanner.md`](../simulation/mfd-scanner.md) for the Multi-Function Display, [`heads-down-display.md`](../simulation/heads-down-display.md) for the Heads-Down Display, and [`cockpit-messages.md`](../simulation/cockpit-messages.md) for the two message boxes.

## `.GAU` widget tree

`Gau_Load` (`00431778`, `PANEL.CPP:0x1d6`) reads a `0x6a4`-byte struct and vector-constructs six arrays of 16-byte rects inside it, of 10, 3, 4, 13, 15 and 3. Every offset in this doc is into that struct, which is the file's content after the 9-byte VOL prefix ([`vol-archive.md`](vol-archive.md#the-per-entry-prefix--fixed-9-bytes)): the last block, the 16-byte ticker rect at 1684, ends at 1700, `0x6a4`. The file's first two `int32`s are an origin offset added to every widget rect. `Gau_BuildCockpitWidgets` (`00431bf8`) then builds seven top-level widgets from fixed offsets and shifts every rect by `VideoMode_X/YCoordShift`. The order it builds them in is also the cockpit's click precedence — [`cockpit-input.md`](../simulation/cockpit-input.md#registration-order-is-precedence) has the full sequence.

GAU coordinates are authored in the 320-wide space, half the 640-wide art's. See [`cockpit-views.md`](../simulation/cockpit-views.md#cockpit-canvas) for the y-range question.

### Block map

| Offset | Contents | Used by |
|---|---|---|
| 0 | Origin offset, added to every widget rect | [above](#gau-widget-tree) |
| 8 | The screen size the header declares, (320,400). Widget origins across all nine retail files span `x:[3..298] y:[1..230]` | [`cockpit-views.md`](../simulation/cockpit-views.md#cockpit-canvas) |
| 16 | How many of the weapon-row rects are in use | [below](#weapon-row-rects) |
| 20 | Ten weapon-row rects, 16 bytes each, indexed by the `.GL` fire-chain byte | [below](#weapon-row-rects) |
| 180, 260, 356 | Zero in every retail file. The ammunition, energy and pod gauge factories copy a row's rect here before building its gauge | [below](#weapon-row-rects) |
| 484, 500, 516, 532 | Console button rects, read by `ConsoleButtons_Ctor` (`00441dd0`): the chain selector, LINK, TRACK and a fourth. Chain, link and auto-track are all 24x7 GAU in every retail file; the fourth's rect is zero in every retail file | [Console buttons](../simulation/cockpit-hud-widgets.md#console-buttons), [registration order](../simulation/cockpit-input.md#registration-order-is-precedence) |
| 564 | The Master Energy Pool meter's rect, read by `EnergyPoolGauge_Ctor` (`00444d5c`) | [LED gauges](../simulation/cockpit-hud-widgets.md#led-gauges) |
| 616 | Shields gauge | [below](#gau-block-at-616) |
| 728 | Multi-Function Display | [below](#gau-block-at-728) |
| 1000 | Throttle | [below](#gau-block-at-1000) |
| 1088 | Gunsight complex | [below](#gau-block-at-1088) |
| 1212 | Heads-Down Display | [below](#gau-block-at-1212) |
| 1604 | `PanelAmbience` clock, read by `Gau_PanelAmbienceWidget` (`004326a8`) | [`cockpit-hud-widgets.md`](../simulation/cockpit-hud-widgets.md#open) |
| 1664 | Training lift, an `int32`: APOCA 60, RAPTOR2 70, MAVERICK and OUTLAW 75, COLOSSUS, OGRE, SAMSON and TOMAHAWK 85, RAZOR 0 | [The training port](../simulation/cockpit-messages.md#the-training-port) |
| 1668 | Pilot channel box, `0,y - 320,y+10` in every retail file | [Its box](../simulation/cockpit-messages.md#its-box) |
| 1684 | Ticker box, `100,y - 220,y+9`: 120x9, centred horizontally, at `y = 34` in seven cockpits, 43 in APOCA's and 100 in RAZOR's | [The ticker](../simulation/cockpit-messages.md#the-ticker) |

## Weapon-row rects

The cockpit's weapon rows sit at offset 20: ten `x0,y0,x1,y1` rects, the array `Gau_Load` builds with `GauWeaponRect_ElementCtor` (`00434ab9`). The int at 16 is how many are in use. `Gau_Load` adds the file's origin offset to that many, and `Gau_BuildCockpitWidgets` shifts that many by `VideoMode_X/YCoordShift` after it has built the seven top-level widgets. The weapon rows are built last: `WeaponMounts_BuildGauges` (`00410644`) calls each mount's vtable `+0x64`, which hands its class's factory (`CockpitView_CreateAmmoWeaponGauge` (`00432124`), `CockpitView_CreateEnergyWeaponGauge` (`00432074`) or `CockpitView_CreatePodGauge` (`004321d4`)) a row — the mount's `.GL` fire-chain byte ([`weapon-mounts.md`](../simulation/weapon-mounts.md#the-hardpoint-list)). The factory copies rect `20 + row * 16` to its own scratch slot at 180, 260 or 356 and builds the gauge from there. What the gauge builds inside the rect is in [Weapon hardpoint rows](../simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows).

Every in-use rect is `x,y – x+55,y+6`, 56x7 inclusive: the 112x14 hole of the `PWEAPONS` row plate at 640 wide. Rows step 9 units down a column:

| Herc | In use | Rows 0 onward, by top-left |
|---|---|---|
| APOCA | 9 | 0-4 at `9,169` down; 5-8 at `255,169` down |
| COLOSSUS | 9 | 0-3 at `5,179` down; 4-7 at `259,179` down; 8 at `130,229` |
| MAVERICK | 4 | 0-1 at `103,153` down; 2-3 at `161,153` down |
| OGRE | 10 | 0-1 at `7,47` down; 2-4 at `7,188` down; 5-6 at `257,47` down; 7-9 at `257,188` down |
| OUTLAW | 3 | 0-2 at `11,190` down |
| RAPTOR2 | 6 | 0-2 at `15,168` down; 3-5 at `249,168` down |
| RAZOR | 7 | 0-2 at `75,209` down; 3-5 at `190,209` down; 6 at `133,209` |
| SAMSON | 8 | 0-3 at `3,179` down; 4-7 at `261,179` down |
| TOMAHAWK | 5 | 0-4 at `27,175` down |

The slots past the count hold `100,140 – 155,146` (`50,140 – 105,146` on RAZOR). Every fire-chain byte in the nine pilotable `.GL` files is below its `.GAU`'s count. Eight of the counts equal the chassis' hardpoint count. RAPTOR2 is the exception: it authors six rows for five hardpoints, and its `.GL` addresses rows 0-4.

## `.GAU` block at 616

The shields gauge's block, read by `ShieldsGauge_Ctor` (`004434fc`) through `Gau_ShieldDisplayWidget` (`00432454`) — [`ShieldsGauge`](../simulation/cockpit-hud-widgets.md#shieldsgauge).

A 16-byte header whose first two ints are an origin offset added to the rest (all-zero in every retail file), then four ordinary `x0,y0,x1,y1` rects, all shifted by `VideoMode_X/YCoordShift` in `ShieldsGauge_ApplyCoordShift` (`00444b9c`):

| Offset | Rect |
|---|---|
| 632 | front facing's meter body |
| 648 | rear facing's meter body |
| 664 | front readout |
| 680 | rear readout |

The block ends at 696. It starts at 616, not 628 — starting it one int later rotates every slot and leaves a spurious leftover int at 692. All nine retail `.GAU` files round-trip byte-exact under this reading.

## `.GAU` block at 728

The MFD block, read by `Gau_MfdPanelWidget` (`004324c8`) — [the MFD's geometry](../simulation/mfd.md#geometry). 728/732 are an origin offset added to the rest, zero in all nine retail files. 744-951 hold 13 rect-shaped slots that `MfdGau_ApplyCoordShift` coordinate-shifts but no constructor reads; zero in every retail file. 952 is the panel rect, read as `param_2[0x38..0x3b]`.

Panel rect is 115x60 exclusive / 116x61 inclusive in every herc — only its position varies:

| Herc | Panel rect | Herc | Panel rect |
|---|---|---|---|
| APOCA | `102,173 – 217,233` | RAPTOR2 | `102,176 – 217,236` |
| COLOSSUS | `102,163 – 217,223` | RAZOR | `102,1 – 217,61` |
| MAVERICK | `102,179 – 217,239` | SAMSON | `102,167 – 217,227` |
| OGRE | `100,167 – 215,227` | TOMAHAWK | `102,176 – 217,236` |
| OUTLAW | `161,150 – 276,210` | | |

## `.GAU` block at 1000

The throttle's block, read by `ThrottleGauge_Ctor` (`00447b84`) through `Gau_ThrottleWidget` (`0043254c`) — [Throttle gauge](../simulation/cockpit-hud-widgets.md#throttle-gauge).

The constructor is handed `.GAU` offset **1000**, not 1016, and treats the whole block from there as one widget record. `GauThrottle_ApplyCoordShift` (`004488cc`) shifts ints `[4..0xf]` left by the video mode's coordinate shift before it ever sees them, so the geometry below is in device pixels (`.GAU` units x2 at 640x480):

| int | content offset | Role |
|---|---|---|
| `[0]`,`[1]` | 1000, 1004 | Origin the rest is measured from. Zero in all 9 retail files, which is why it reads as an always-zero "null widget" slot until the constructor is traced |
| `[4..7]` | 1016-1028 | Slider **track** rect, `x0,y0,x1,y1` |
| `[8..11]` | 1032-1044 | **Forward fill bar** rect — `LedBarGraph_CtorV` (`00439344`) with range `+0x400` |
| `[12..15]` | 1048-1060 | **Reverse fill bar** rect — same, range `-0x400` |
| `[0x10]` | 1064 | `SLIDE_DIR`. 1 in every retail file, selecting `ThrottleSlider_CtorV` (`00447e24`); the 0 branch (`004483c0`, a fixed 12px knob spanning the track's full height) is never exercised |
| `[0x12]` | 1072 | x nudge for the centre tick, shifted by the ctor itself rather than at load |

Ints `[8..15]` are **two rects, not four points**. That explains both things the point reading found odd: "points" 1 and 2 always sit close together because they are the bottom of the upper bar and the top of the lower one, and the x alternates between two values because those are each bar's left and right edge. On OUTLAW they are two 4x20 strips inside the 14x49 track, one either side of centre.

## `.GAU` block at 1088

The gunsight complex's block, read by `Gau_RovingGunsightWidget` (`0043c7d8`) — [the gunsight complex](../simulation/cockpit-gunsight-hud.md#front-window-hud--the-gunsight-complex).

| int | content offset | Role |
|---|---|---|
| `[0]`,`[1]` | 1088, 1092 | Origin added to every child rect. Zero in all 9 retail files |
| `[2]`,`[3]` | 1096, 1100 | The complex's own bottom-right — `320, 117` or `320, 157` |
| `[4..7]` | 1104-1116 | **Heading tape** rect. `100,y - 220,y+17` in every file, so 120x17 centred on the 320-wide HUD. The [rotation indicator](../simulation/cockpit-gunsight-hud.md#rotation-indicator) is derived from it |
| `[8..0xb]` | 1120-1132 | Speed and time readout anchors: 1120/1124 the *right* edge of the time field, 1128/1132 the *left* edge of the `SPEED:` caption — [Speed and time readouts](../simulation/cockpit-gunsight-hud.md#speed-and-time-readouts) |
| `[0xc]`,`[0xd]` | 1136, 1140 | Reticle point — [Child 4](../simulation/hud-target-indicator.md#child-4--the-reticle) |
| `[0xe]` | 1144 | Half-extent of child 4's rect about the reticle point. Zero in all 9 retail files |
| `[0xf..0x12]` | 1148-1163 | Rect shared by children 0, 5 and 6 — the gunsight area, the [target arrow](../simulation/hud-target-indicator.md#the-arrow)'s safe area |
| `[0x13..0x16]` | 1164-1179 | The [**`ATT` legend's**](../simulation/cockpit-gunsight-hud.md#the-att-legend) rect |
| `[0x17..0x1a]` | 1180-1195 | A second label of the same kind, at the widget's `+0x107` |
| `[0x1b]`,`[0x1c]` | 1196, 1200 | Top-left of the [floating scanner repeater](../simulation/mfd-scanner.md#geometry), a bare point with no size |

### Per-herc values

The `ATT` legend's rect is 24x7 in every retail file, `68,0 - 92,7` on most hercs, `60,0` on OGRE, `30,0` on SAMSON and `60,67` on RAZOR.

The gunsight area, 1148:

| Herc | Area | Herc | Area |
|---|---|---|---|
| APOCA | `66,0 – 253,146` | RAPTOR2 | `106,0 – 228,146` |
| COLOSSUS | `80,0 – 239,155` | RAZOR | `55,68 – 264,186` |
| MAVERICK | `81,0 – 238,135` | SAMSON | `82,0 – 237,148` |
| OGRE | `84,0 – 235,150` | TOMAHAWK | `81,0 – 239,151` |
| OUTLAW | `86,0 – 233,143` | | |

The scanner repeater's top-left, 1196/1200:

| Herc | Point | Herc | Point |
|---|---|---|---|
| APOCA | `40,27` | RAPTOR2 | `54,29` |
| COLOSSUS | `50,11` | RAZOR | `15,20` |
| MAVERICK | `50,29` | SAMSON | `51,5` |
| OGRE | `67,80` | TOMAHAWK | `53,28` |
| OUTLAW | `44,28` | | |

## `.GAU` block at 1212

The Heads-Down Display's block, read by `Gau_PilotRosterWidget` (`00432634`) and pre-shifted by `HddGau_ApplyCoordShift` (`0044bed0`) — [`heads-down-display.md`](../simulation/heads-down-display.md).

**The whole display is authored per herc** — unlike the MFD, whose `.GAU` supplies [one panel rect](#gau-block-at-728) and nothing else. The block runs to 1588 and is read as `int32`s from its own start:

| Index | Bytes | Contents |
|---|---|---|
| 0-1 | 1212 | Origin offset, added to every rect below |
| 2-3 | 1220 | [Open](#open) |
| 4-7 | 1228 | Screen rect |
| 8-11 | 1244 | Order column rect |
| 12-15 | 1260 | Damage column rect |
| 16-19 | 1276 | Title indicator rect |
| `0x14`+4i | 1292 | 15 [widget](../simulation/heads-down-display.md#widgets) rects |
| `0x50`+4i | 1532 | 3 comm-box marker rects |
| `0x5c` | 1580 | Arrow-button frame set, 0 or 1 |
| `0x5d` | 1584 | [Open](#open) |
| `0x5e` | 1588 | [Comm-box](../simulation/heads-down-display.md#squad-comm-boxes) highlight mode. 1 in every retail file |

All values are authored in the 320-wide space. `HddGau_ApplyCoordShift` adds `0x28` to the origin's y **before** shifting, then shifts every rect by `VideoMode_X/YCoordShift`; the constructor adds the shifted origin to each rect.

**The `+0x28` bias is what puts the block on the art.** Every retail file authors the origin as `(0, 197)`, so the bias makes it `(0, 237)` — the canvas origin every retail `.VUE` gives view 1, i.e. the row `.HB1` is blitted at. Subtracting the `.VUE` origin back off yields art-local coordinates, and in retail data the two cancel exactly: art-local device = authored x 2.

Screen rect is 459x201 device (230x101 authored inclusive) in every herc; only its position varies.

| Herc | Screen rect (device, art-local) | Arrow set |
|---|---|---|
| APOCA | `92,12 – 550,212` | 0 |
| COLOSSUS | `92,12 – 550,212` | 0 |
| MAVERICK | `92,32 – 550,232` | 0 |
| OGRE | `92,38 – 550,238` | 0 |
| OUTLAW | `94,84 – 552,284` | 1 |
| RAPTOR2 | `90,56 – 548,256` | 0 |
| RAZOR | `90,84 – 548,284` | 1 |
| SAMSON | `90,86 – 548,286` | 0 |
| TOMAHAWK | `90,134 – 548,334` | 1 |

Positions differ structurally, not just by offset: TOMAHAWK puts its comm boxes above the map, OUTLAW and RAZOR stack the two page buttons vertically, APOCA/COLOSSUS/MAVERICK put the button strip below the map rather than above it.

## Open

- **Deferred:** the Heads-Down Display block's indices 2-3 (1220) and `0x5d` (1584). No constructor found reads them.
