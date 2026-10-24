# DBSIM.EXE — `gau\<chassis>.GAU`, the cockpit layout

**ES1's cockpit layout files are ES2's 1,700-byte layout with room for nine weapon rows instead of ten and five console buttons instead of four; the console instruments' blocks from 548 to 1000, the Heads-Down Display's and the two message boxes sit where ES2's do.** `Gau_Load` (`0002b5c4`) reads the file and the cockpit's builders take fixed offsets into it ([the cockpit object](../simulation/cockpit-panel.md#the-cockpit-object)). Offsets are into the content, as in [ES2's layout](../../retail/formats/gau-cockpit-layout.md); addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## The load

`Gau_Load` (`0002b5c4`), (path): opens the path with `VolRStream_CtorFromPath`, asserting at severity 5 (`panel.cpp` line `0x192`) when the stream does not open; allocates `0x6a4` bytes and runs the inlined constructors of the blocks' rects and points, with `__vec_ctor` for six rect arrays: 9 at 20 with `GauWeaponRect_ElementCtor` (`0002c93c`, by its padding entry `0002c939`), and 3 at 372, 5 at 468, 13 at 744, 15 at 1292 and 3 at 1532 with `GauRect_ElementCtor` (`0002c994`, by its padding entry `0002c993`). Each element constructor allocates its 16 bytes when given none and writes nothing. It reads `0x6a4` bytes over the struct and, when the origin pair at 0 is not (0, 0), adds it to both corners of the first *n* weapon-row rects, *n* being the `int32` at 16, and to the first 16 bytes of the blocks at 452, 548, 616, 728, 1000, 1104 and 1212. It returns the struct. ES2's `Gau_Load` (`00431778`) builds ten weapon-row rects and four at 484, offsets the blocks at 468 and 1088 where ES1's offsets 452 and 1104, and asserts at line `0x1d6`.

Nothing shifts the rects into device pixels: ES1's builders pass each block to its constructor as read.

**The eight retail files**, `gau\ROADRUNN` to `gau\COLOSSUS` in `SIMVOL0.VOL` as HercWorks.Core reads them, are 1,700 bytes each with the origin pair (0, 0), so `Gau_Load` moves nothing. The rows in use, the `int32` at 16, are 3, 3, 4, 5, 6, 7, 8 and 9 in chassis order (`ROADRUNN`, `GUNSLING`, `RAPTOR`, `TOMAHAWK`, `PATRIOT`, `PANTHER`, `HERCULES`, `COLOSSUS`). The in-use row rects lie between screen rows 1 and 189, each 7 or 8 rows high, and the parts' rects at 180, 196, 260 and 276 span rows 0 to 7 from a row's top, inside every in-use row. The console block's 16-byte header at 452 is zero in five files; its first pair, the origin of the console's five rects, is (2, 61) in `PATRIOT`, (1, 177) in `HERCULES` and (3, 177) in `COLOSSUS`.

## Differences from ES2

| Offset | ES1 | ES2's offset |
|---|---|---|
| 0, 16 | the origin pair, the number of weapon rows in use | the same |
| 20 | nine weapon-row rects, indexed by the row the mount's factory is given | 20, ten rects |
| 164, 244, 340 | the blocks of the ammunition, energy and pod rows, each opening with the scratch rect its factory copies a row's rect to before building the row's gauge ([the weapon rows](../simulation/cockpit-panel.md#the-weapon-rows)). The three hold at 180, 260 and 356 the number button's rect, {0, 0, 38, 7} in the energy and ammunition blocks of all eight files and {0, 0, 9, 7} in the pod's, whose right and bottom the button replaces ([the number button](../simulation/cockpit-weapon-gauges.md#the-plain-button)); at 196 and 276 the round count's or charge bar's rect; and at 372, which runs the pod block to 387, the pod's label rect, {11, 0, 59, 7}; each relative to the row's top-left. The energy block at 304 holds an `int32` its gauge hands the charge bar's constructor ([the charge bar](../simulation/cockpit-weapon-gauges.md#the-charge-bar--weaponslidergadget)), 0 in all eight | 180, 260, 356; ES2's gauges place their parts at fixed offsets from the row's rect, and read a mirror flag from a byte of the block |
| 388 | two rects, zero in the eight files | 388, three rects |
| 452 | the console buttons' block: its first 16 bytes the console's rect, of which the first pair is the origin `ConsoleButtons_Ctor` adds to each of the five rects from 468, the buttons `I`, `II`, `III`, `LINK` and `TRACK` in that order ([the console buttons](../simulation/cockpit-weapon-gauges.md#the-console-buttons)) | 468, four rects from 484 |
| 548 | the energy meter's block, [below](#the-energy-meter-block-at-548) | the same |
| 616 | the shield display's block, [below](#the-shield-display-block-at-616) | the same |
| 728 | the MFD's block, [below](#the-mfd-block-at-728) | the same, its panel rect alone |
| 1000 | the throttle's block, [below](#the-throttle-block-at-1000) | the same, with the centre tick's nudge |
| 1104 | the gunsight's block, [below](#the-gunsight-block-at-1104) | 1088 |
| 1212 | the block of the instrument at `+0x1f5`, with 15 rects at 1292 and 3 at 1532 | the same, the Heads-Down Display |
| 1604 | the cockpit clock's block ([the cockpit clock](ttm-scripts.md#the-cockpit-clock)) | the same, `PanelAmbience`'s |
| 1668, 1684 | the pilot channel's box and the ticker's | the same |

## The energy meter block at 548

`EnergyPoolGauge_Ctor` (`00039818`) reads the block's `int32` from 548 to 595 ([building the energy meter](../simulation/cockpit-energy-shields.md#building-it--energypoolgauge_ctor-00039818)), unshifted:

| int | Offset | ES1 | ES2's |
|---|---|---|---|
| `[0]`, `[1]` | 548, 552 | the origin, added to the bar's rect | the same |
| `[2]`, `[3]` | 556, 560 | unread: `PanelGauge_Ctor` copies `[0..3]` as the gauge's rect, which the constructor then replaces with the bar's | the same |
| `[4..7]` | 564–576 | the bar's rect | the same |
| `[8]` | 580 | 3 builds an `LEDBarGraphH` and 4 an `LEDBarGraphV`; any other value builds no bar | overwritten with 0, and an `LEDBarGraphH` built |
| `[9]` | 584 | 0 for no clip shape, else *n* of the clip file `edg\ENG_CLP`*n* | none |
| `[0xa]`, `[0xb]` | 588, 592 | the bar's two stripe shades, its `+0x2c` and `+0x30` | overwritten with the color ids 6 and 5 |
| `[0xc..0x10]` | 596–612 | unread; 0 in all eight files | |

The eight retail files hold:

| File | Origin | `[2]`, `[3]` | Bar | `[8]` | `[9]` | Shades |
|---|---|---|---|---|---|---|
| `ROADRUNN` | 0, 0 | 0, 0 | 22, 152, 31, 186 | 4 | 2 | 50, 48 |
| `GUNSLING` | 0, 0 | 0, 0 | 277, 153, 286, 187 | 4 | 2 | 50, 59 |
| `RAPTOR` | 0, 0 | 0, 0 | 253, 139, 281, 161 | 4 | 1 | 50, 48 |
| `TOMAHAWK` | 0, 0 | 0, 0 | 64, 125, 112, 134 | 3 | 4 | 50, 48 |
| `PATRIOT` | 75, 164 | 79, 185 | 0, 0, 4, 21 | 4 | 0 | 53, 51 |
| `PANTHER` | 0, 0 | 0, 0 | 257, 162, 275, 186 | 4 | 3 | 50, 48 |
| `HERCULES` | 108, 179 | 248, 196 | 0, 0, 37, 4 | 3 | 0 | 53, 56 |
| `COLOSSUS` | 197, 186 | 234, 190 | 0, 0, 37, 4 | 3 | 0 | 56, 54 |

So the bar runs up in five chassis and across in `TOMAHAWK`, `HERCULES` and `COLOSSUS`, and `PATRIOT`, `HERCULES` and `COLOSSUS` give it no clip shape.

## The shield display block at 616

`ShieldsGauge_Ctor` (`00038f34`) reads the block's `int32` from 616 to 695 ([building the shield display](../simulation/cockpit-energy-shields.md#building-it--shieldsgauge_ctor-00038f34)), on the plan of ES2's block ([ES2's](../../retail/formats/gau-cockpit-layout.md#gau-block-at-616)), unshifted:

| int | Offset | ES1 | ES2's |
|---|---|---|---|
| `[0..3]` | 616–628 | the gauge's rect, whose corners the display's per-frame update tests; its first pair the origin, added to the four rects below | the same origin |
| `[4..7]` | 632–644 | the front button's rect | the front facing's |
| `[8..0xb]` | 648–660 | the rear button's rect | the rear facing's |
| `[0xc..0xf]` | 664–676 | the front readout's rect | the same |
| `[0x10..0x13]` | 680–692 | the rear readout's rect | the same |
| `[0x14..0x1b]` | 696–724 | unread; 0 in all eight files | |

The eight retail files hold:

| File | `[0..3]` | Front button | Rear button | Front readout | Rear readout |
|---|---|---|---|---|---|
| `ROADRUNN` | 0, 0, 0, 0 | 287, 152, 299, 160 | 287, 181, 299, 189 | 301, 154, 314, 160 | 301, 182, 314, 188 |
| `GUNSLING` | 0, 0, 0, 0 | 240, 134, 252, 142 | 240, 164, 252, 172 | 254, 135, 267, 141 | 254, 166, 267, 172 |
| `RAPTOR` | 0, 0, 0, 0 | 253, 172, 265, 180 | 253, 186, 265, 194 | 275, 174, 288, 180 | 275, 188, 288, 194 |
| `TOMAHAWK` | 0, 0, 0, 0 | 287, 155, 299, 163 | 287, 187, 299, 195 | 287, 166, 300, 172 | 287, 179, 300, 185 |
| `PATRIOT` | 237, 161, 280, 191 | 5, 21, 17, 29 | 27, 21, 39, 29 | 28, 1, 40, 6 | 28, 12, 40, 17 |
| `PANTHER` | 0, 0, 0, 0 | 33, 184, 45, 192 | 46, 184, 58, 192 | 50, 161, 63, 167 | 50, 176, 63, 182 |
| `HERCULES` | 223, 180, 248, 196 | 14, 0, 26, 8 | 14, 9, 26, 17 | 0, 0, 13, 6 | 0, 9, 13, 15 |
| `COLOSSUS` | 237, 3, 263, 19 | 14, 0, 26, 8 | 14, 9, 26, 17 | 0, 0, 13, 6 | 0, 9, 13, 15 |

## The MFD block at 728

`MfdDisplay_Ctor` (`00039c64`) reads the block's `int32` from 728 to 967 ([building the display](../simulation/mfd-display.md#building-it)), where ES2's display reads only the panel rect and places everything else itself ([ES2's](../../retail/formats/gau-cockpit-layout.md#gau-block-at-728)), unshifted:

| int | Offset | ES1 |
|---|---|---|
| `[0]`, `[1]` | 728, 732 | the origin, added to every rect below |
| `[2]`, `[3]` | 736, 740 | unread by the constructor: `PanelGauge_Ctor` copies `[0..3]` as the gauge's rect `+0`, and the display's visibility tests and copies take the screen's rect instead |
| `[4..0x37]` | 744–951 | the 13 buttons' rects, button *i* at 744 + 16*i*: 0 to 5 the F-key buttons, 6 the title, 7 `MODE`, 8 `RANGE`, 9 `TARGET`, 10 `XMIT`, 11 and 12 the row arrows ([the buttons](../simulation/mfd-display.md#the-buttons)) |
| `[0x38..0x3b]` | 952–967 | the screen's rect, which the screens draw in and the click surface covers |
| `[0x3c..0x43]` | 968–999 | unread; 0 in all eight files |

The eight retail files hold, each rect {*x0*, *y0*, *x1*, *y1*} before the origin is added, and the F-key buttons 0 to 5 by their top-left corners, each 14 columns and 6 rows past its corner:

| File | `[0..3]` | 0–5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | Screen |
|---|---|---|---|---|---|---|---|---|---|---|
| `ROADRUNN` | 0, 0, 0, 0 | 96,161; 96,171; 96,181; 183,161; 183,171; 183,181 | 149, 127, 202, 134 | 65, 126, 92, 136 | 93, 126, 120, 136 | 65, 137, 92, 147 | 65, 148, 92, 158 | 121, 126, 134, 136 | 134, 126, 147, 136 | 113, 142, 181, 186 |
| `GUNSLING` | 0, 0, 0, 0 | 133,176; 151,176; 169,176; 187,176; 205,176; 223,176 | 192, 186, 237, 194 | 105, 174, 132, 184 | 105, 185, 132, 195 | 133, 185, 160, 195 | 240, 174, 267, 184 | 162, 185, 174, 195 | 175, 185, 187, 195 | 151, 113, 219, 158 |
| `RAPTOR` | 0, 0, 0, 0 | 48,140; 77,140; 48,149; 77,149; 48,158; 77,158 | 184, 131, 237, 138 | 211, 141, 238, 151 | 183, 152, 210, 162 | 211, 152, 238, 162 | 183, 163, 210, 173 | 183, 141, 196, 151 | 196, 141, 209, 151 | 109, 128, 177, 172 |
| `TOMAHAWK` | 0, 0, 0, 0 | 53,159; 72,159; 91,159; 214,159; 233,159; 252,159 | 214, 169, 267, 176 | 52, 168, 79, 178 | 80, 168, 107, 178 | 52, 180, 79, 190 | 241, 180, 268, 190 | 213, 180, 226, 190 | 226, 180, 239, 190 | 124, 148, 192, 192 |
| `PATRIOT` | 105, 126, 215, 196 | 2,21; 2,30; 2,39; 93,21; 93,30; 93,39 | 0, 50, 52, 57 | 56, 48, 83, 58 | 84, 48, 111, 58 | 56, 59, 83, 69 | 84, 59, 111, 69 | 29, 59, 41, 69 | 42, 59, 54, 69 | 20, 0, 88, 45 |
| `PANTHER` | 0, 0, 0, 0 | 87,144; 106,144; 125,144; 87,154; 106,154; 125,154 | 87, 166, 140, 173 | 217, 142, 244, 152 | 217, 154, 244, 164 | 217, 166, 244, 176 | 86, 176, 113, 186 | 114, 176, 127, 186 | 127, 176, 140, 186 | 143, 142, 211, 186 |
| `HERCULES` | 98, 128, 246, 180 | 72,1; 72,10; 72,19; 72,28; 72,37; 72,46 | 95, 7, 147, 14 | 95, 16, 122, 26 | 122, 16, 149, 26 | 95, 27, 122, 37 | 122, 27, 149, 37 | 94, 38, 106, 48 | 108, 38, 146, 48 | 0, 0, 68, 45 |
| `COLOSSUS` | 107, 122, 239, 181 | 8,0; 27,0; 46,0; 8,54; 27,54; 46,54 | 78, 18, 131, 25 | 78, 27, 105, 37 | 106, 27, 133, 37 | 78, 38, 105, 48 | 106, 38, 133, 48 | 78, 49, 91, 58 | 92, 49, 104, 58 | 0, 8, 68, 52 |

So the screen is 68 columns by 44 or 45 rows past its corner in every file and the four captioned buttons 27 by 10, while the title's and the arrows' sizes vary from file to file.

## The throttle block at 1000

`ThrottleGauge_Ctor` (`0003b0d4`) and the two slider constructors read the block's `int32` from 1000 to 1071 ([the throttle gauge](../simulation/cockpit-throttle-gauge.md#the-gauge--throttlegauge_ctor-0003b0d4)), on the plan of ES2's block at 1000 ([ES2's](../../retail/formats/gau-cockpit-layout.md#gau-block-at-1000)), unshifted:

| int | Offset | ES1 | ES2's |
|---|---|---|---|
| `[0]`, `[1]` | 1000, 1004 | the origin, added to every rect below | the same |
| `[2]`, `[3]` | 1008, 1012 | unread by the constructors; where the origin is set, the bottom-right corner of the rects' area | not read by the gauge's or the vertical slider's constructor |
| `[4..7]` | 1016–1028 | the track's rect, which is the slider's own | the same |
| `[8..0xb]` | 1032–1044 | the forward bar's rect | the same |
| `[0xc..0xf]` | 1048–1060 | the reverse bar's rect | the same |
| `[0x10]` | 1064 | 1 builds the vertical slider and any other value the horizontal one; also the index of the bars' picture among the light names at `0009328c` ([the light names](str-strings.md#where-the-object-file-starts-and-ends)) | 1 builds the vertical slider |
| `[0x11]` | 1068 | the knob's frame in `dba\pbuttons` | none; ES2's knob is frame 1 of the bank `throttle` |
| `[0x12]` | 1072 | unread | the centre tick's x nudge |

The eight retail files hold, with bytes 1072–1103 zero in all of them:

| File | Origin | `[2]`, `[3]` | Track | Forward bar | Reverse bar | `[0x10]` | `[0x11]` |
|---|---|---|---|---|---|---|---|
| `ROADRUNN` | 0, 0 | 0, 0 | 43, 133, 55, 168 | 57, 134, 58, 149 | 57, 150, 58, 166 | 1 | 27 |
| `GUNSLING` | 0, 0 | 0, 0 | 109, 134, 121, 172 | 122, 137, 123, 153 | 122, 154, 123, 169 | 1 | 27 |
| `RAPTOR` | 0, 0 | 0, 0 | 30, 183, 86, 191 | 59, 190, 83, 191 | 35, 190, 58, 191 | 2 | 27 |
| `TOMAHAWK` | 0, 0 | 0, 0 | 19, 155, 34, 191 | 18, 156, 19, 171 | 18, 173, 19, 188 | 1 | 27 |
| `PATRIOT` | 37, 162 | 62, 188 | 12, 0, 24, 26 | 0, 0, 1, 14 | 0, 16, 1, 26 | 1 | 27 |
| `PANTHER` | 0, 0 | 0, 0 | 62, 159, 78, 190 | 79, 159, 80, 175 | 79, 177, 80, 190 | 1 | 27 |
| `HERCULES` | 75, 136 | 103, 174 | 4, 0, 19, 39 | 0, 5, 1, 21 | 0, 23, 1, 36 | 1 | 27 |
| `COLOSSUS` | 82, 139 | 103, 173 | 3, 0, 16, 34 | 0, 2, 1, 18 | 0, 20, 1, 31 | 1 | 27 |

So the `RAPTOR`'s throttle runs across, its forward bar right of the track's middle and its reverse bar left of it, both along the track's bottom two rows, and the other seven run up, each with its bars in a two-column strip left or right of the track (in `TOMAHAWK` sharing the track's first column), the forward bar the strip's upper half and the reverse bar its lower.

## The gunsight block at 1104

`Gau_RovingGunsightWidget` (`00034aec`) reads the block's `int32` from 1104 to 1179 ([building the gunsight](../simulation/cockpit-gunsight-hud.md#building-the-gunsight)), on the plan of ES2's block at 1088 ([ES2's](../../retail/formats/gau-cockpit-layout.md#gau-block-at-1088)) moved 16 bytes on:

| int | Offset | ES1 | ES2's |
|---|---|---|---|
| `[0]`, `[1]` | 1104, 1108 | the origin, added to every child's rect but the reticle's centre | the same |
| `[2]`, `[3]` | 1112, 1116 | unread by the builder | the complex's bottom-right |
| `[4..7]` | 1120–1132 | the heading tape's rect, which the rotation indicator's and the waypoint indicator's derive from | the same |
| `[8..0xb]` | 1136–1148 | the vertical slide bar's rect | the speed and time anchors |
| `[0xc]`, `[0xd]` | 1152, 1156 | the reticle's centre, where the cursor is put | the same |
| `[0xe]` | 1160 | the half-size of the square about it that the reticle, the target box and child 6 take as their rect | the same |
| `[0xf..0x12]` | 1164–1179 | the click surface's rect | the gunsight area |

## Open

- **Open:** what the energy block's `int32` at 292 and 296 hold, 2 and 9 in all eight files: the energy row's constructor reads neither.
- **Open:** what the instrument at 1212 is; its constructor (`0003c258`) is unread. What the gunsight block's `int32` 2 and 3 (1112, 1116) and its bytes from 1180 to 1211 hold: the builder reads neither, and `Gau_Load` adds the origin to `int32` 2 and 3.
