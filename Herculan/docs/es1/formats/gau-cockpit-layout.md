# DBSIM.EXE — `gau\<chassis>.GAU`, the cockpit layout

**ES1's cockpit layout files are ES2's 1,700-byte layout with room for nine weapon rows instead of ten and five console buttons instead of four; the console instruments' blocks from 548 to 1000, the Heads-Down Display's and the two message boxes sit where ES2's do.** `Gau_Load` (`0002b5c4`) reads the file and the cockpit's builders take fixed offsets into it ([the cockpit object](../simulation/cockpit-panel.md#the-cockpit-object)). Offsets are into the content, as in [ES2's layout](../../retail/formats/gau-cockpit-layout.md); addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)).

## The load

`Gau_Load` (`0002b5c4`), (path): opens the path with `VolRStream_CtorFromPath`, asserting at severity 5 (`panel.cpp` line `0x192`) when the stream does not open; allocates `0x6a4` bytes and runs the inlined constructors of the blocks' rects and points, with `__vec_ctor` for six rect arrays: 9 at 20 with `GauWeaponRect_ElementCtor` (`0002c93c`, by its padding entry `0002c939`), and 3 at 372, 5 at 468, 13 at 744, 15 at 1292 and 3 at 1532 with `GauRect_ElementCtor` (`0002c994`, by its padding entry `0002c993`). Each element constructor allocates its 16 bytes when given none and writes nothing. It reads `0x6a4` bytes over the struct and, when the origin pair at 0 is not (0, 0), adds it to both corners of the first *n* weapon-row rects, *n* being the `int32` at 16, and to the first 16 bytes of the blocks at 452, 548, 616, 728, 1000, 1104 and 1212. It returns the struct. ES2's `Gau_Load` (`00431778`) builds ten weapon-row rects and four at 484, offsets the blocks at 468 and 1088 where ES1's offsets 452 and 1104, and asserts at line `0x1d6`.

Nothing shifts the rects into device pixels: ES1's builders pass each block to its constructor as read.

## Differences from ES2

| Offset | ES1 | ES2's offset |
|---|---|---|
| 0, 16 | the origin pair, the number of weapon rows in use | the same |
| 20 | nine weapon-row rects, indexed by the row the mount's factory is given | 20, ten rects |
| 164, 244, 340 | the scratch rects of the ammunition, energy and pod factories, which copy a row's rect there before building its gauge | 180, 260, 356 |
| 372 | three rects | 388 |
| 452 | the console buttons' block: a 16-byte header, then five rects from 468 | 468, four rects from 484 |
| 548, 616, 728, 1000 | the blocks of the instruments at the cockpit's `+0x1e1` to `+0x1ed` | the same |
| 1104 | the block of the instrument at `+0x1f1` | 1088, the gunsight complex |
| 1212 | the block of the instrument at `+0x1f5`, with 15 rects at 1292 and 3 at 1532 | the same, the Heads-Down Display |
| 1604 | the cockpit clock's block ([the cockpit clock](ttm-scripts.md#the-cockpit-clock)) | the same, `PanelAmbience`'s |
| 1668, 1684 | the pilot channel's box and the ticker's | the same |

## Open

- **Open:** a check of the reading against the eight retail files in `SIMVOL0.VOL`, through HercWorks.Core: the `int32` at 16 at most 9 in each, every in-use row rect inside 20–163, and the five rects at 468–547 holding the console buttons.
- **Open:** what the instruments at 1104 and 1212 are; their constructors (`00034aec`, `0003c258`) are unread, as are those of the other blocks.
