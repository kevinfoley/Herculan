# DBSIM.EXE — `gau\<chassis>.GAU`, the cockpit layout

**ES1's cockpit layout files are ES2's 1,700-byte layout with room for nine weapon rows instead of ten and five console buttons instead of four; the console instruments' blocks from 548 to 1000, the Heads-Down Display's and the two message boxes sit where ES2's do.** `Gau_Load` (`0002b5c4`) reads the file and the cockpit's builders take fixed offsets into it ([the cockpit object](../simulation/cockpit-panel.md#the-cockpit-object)). Offsets are into the content, as in [ES2's layout](../../retail/formats/gau-cockpit-layout.md); addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## The load

`Gau_Load` (`0002b5c4`), (path): opens the path with `VolRStream_CtorFromPath`, asserting at severity 5 (`panel.cpp` line `0x192`) when the stream does not open; allocates `0x6a4` bytes and runs the inlined constructors of the blocks' rects and points, with `__vec_ctor` for six rect arrays: 9 at 20 with `GauWeaponRect_ElementCtor` (`0002c93c`, by its padding entry `0002c939`), and 3 at 372, 5 at 468, 13 at 744, 15 at 1292 and 3 at 1532 with `GauRect_ElementCtor` (`0002c994`, by its padding entry `0002c993`). Each element constructor allocates its 16 bytes when given none and writes nothing. It reads `0x6a4` bytes over the struct and, when the origin pair at 0 is not (0, 0), adds it to both corners of the first *n* weapon-row rects, *n* being the `int32` at 16, and to the first 16 bytes of the blocks at 452, 548, 616, 728, 1000, 1104 and 1212. It returns the struct. ES2's `Gau_Load` (`00431778`) builds ten weapon-row rects and four at 484, offsets the blocks at 468 and 1088 where ES1's offsets 452 and 1104, and asserts at line `0x1d6`.

Nothing shifts the rects into device pixels: ES1's builders pass each block to its constructor as read.

**The eight retail files**, `gau\ROADRUNN` to `gau\COLOSSUS` in `SIMVOL0.VOL` as HercWorks.Core reads them, are 1,700 bytes each with the origin pair (0, 0), so `Gau_Load` moves nothing. The rows in use, the `int32` at 16, are 3, 3, 4, 5, 6, 7, 8 and 9 in chassis order (`ROADRUNN`, `GUNSLING`, `RAPTOR`, `TOMAHAWK`, `PATRIOT`, `PANTHER`, `HERCULES`, `COLOSSUS`). The in-use row rects lie between screen rows 1 and 189, each 7 or 8 rows high, and the parts' rects at 180, 196, 260 and 276 span rows 0 to 7 from a row's top, inside every in-use row. The console block's 16-byte header at 452 is not zero in `PATRIOT`, `HERCULES` and `COLOSSUS`, and in `HERCULES` and `COLOSSUS` the five rects from 468 lie where they would relative to it ([Open](#open)).

## Differences from ES2

| Offset | ES1 | ES2's offset |
|---|---|---|
| 0, 16 | the origin pair, the number of weapon rows in use | the same |
| 20 | nine weapon-row rects, indexed by the row the mount's factory is given | 20, ten rects |
| 164, 244, 340 | the blocks of the ammunition, energy and pod rows, each opening with the scratch rect its factory copies a row's rect to before building the row's gauge ([the weapon rows](../simulation/cockpit-panel.md#the-weapon-rows)). The ammunition and energy blocks hold at 180 and 260 the select button's rect and at 196 and 276 the round count's or charge bar's, each relative to the row's top-left, and the energy block at 304 an `int32` its gauge hands the charge bar's constructor ([the weapon rows' gauges](../simulation/cockpit-weapon-gauges.md#the-energy-row)) | 180, 260, 356; ES2's gauges place their parts at fixed offsets from the row's rect, and read a mirror flag from a byte of the block |
| 372 | three rects | 388 |
| 452 | the console buttons' block: a 16-byte header, then five rects from 468 | 468, four rects from 484 |
| 548, 616, 728, 1000 | the blocks of the instruments at the cockpit's `+0x1e1` to `+0x1ed` | the same |
| 1104 | the gunsight's block, [below](#the-gunsight-block-at-1104) | 1088 |
| 1212 | the block of the instrument at `+0x1f5`, with 15 rects at 1292 and 3 at 1532 | the same, the Heads-Down Display |
| 1604 | the cockpit clock's block ([the cockpit clock](ttm-scripts.md#the-cockpit-clock)) | the same, `PanelAmbience`'s |
| 1668, 1684 | the pilot channel's box and the ticker's | the same |

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

- **Open:** what the console block's header at 452 is to its five rects from 468: whether the console's constructors (`00037bd0`, `00037ce4`, unread) add it to them, which `HERCULES`' and `COLOSSUS`' rects suggest, and whether the five are the console buttons.
- **Open:** what the instrument at 1212 is; its constructor (`0003c258`) is unread, as are those of the other blocks but 1104's. What the gunsight block's `int32` 2 and 3 (1112, 1116) and its bytes from 1180 to 1211 hold: the builder reads neither, and `Gau_Load` adds the origin to `int32` 2 and 3.
