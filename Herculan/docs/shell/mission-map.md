# The briefing's mission map

The picture inside the mission tab's `Mission Map` panel in the briefing view ([`screen-layout.md`](screen-layout.md#the-mission-screen)): a banded relief of the mission's ground, a grid, the bases, the squad and its nav path, a camera the six buttons beside it move, and an animated introduction the first time it comes up. It is one C++ object from `shellmap.cpp`, held in `DAT_0046f26c`. **Every address in this doc is in `VSHELL.EXE`.**

## The object

`ShellMap_Build` (`0040e1a7`) destroys the map there is and constructs a new one of `0x3c5` bytes over the canvas rect `{0x123, 0x43, 0x249, 0x124}`, then builds its relief. It runs from `Game_LoadSlot` and from the two paths that load a mission, `Career_LoadCurrentMission` (`0044d4cc`) and `Msn_BuildPath` (`0044d5bd`), so a map belongs to one loaded mission and lasts until the next.

`ShellMap_Constructor` (`00423f43`) is built on its base class `Map` (vtable `0047195e`, by its RTTI name), whose constructor `Map_Ctor` (`0041fdc8`) keeps the rect's width and height at `+0x46`/`+0x4a` (right minus left, bottom minus top: 294 and 225), its own drawing context at `+4` whose centre is the rect's left less `-width >> 1` and top less `-height >> 1`, half the size rounded up — canvas (438, 180) — and a 3Space camera at `+0xc` with focal shift 7 and no rotation. The camera's position is `+0x12`/`+0x16`, its altitude `+0x1a`.

The vtable at `004721b0`:

| Slot | Function | Does |
|---|---|---|
| 0 | `ShellMap_Paint` (`0042540a`) | [a paint](#a-paint) |
| `+4` | `Map_ZoomIn` (`004200e4`) | [zoom in](#the-six-buttons) |
| `+8` | `ShellMap_ZoomOut` (`00427946`) | zoom out |
| `+0xc` | `ShellMap_PanNorth` (`00427a3f`) | pan |
| `+0x10` | `ShellMap_PanSouth` (`00427a93`) | pan |
| `+0x14` | `ShellMap_PanWest` (`00427997`) | pan |
| `+0x18` | `ShellMap_PanEast` (`004279eb`) | pan |
| `+0x1c` | `Map_ResetPan` (`00420185`) | zeroes the pan |

## What it reads

| File | Reader | Kept |
|---|---|---|
| `data\mission.str` | the constructor | group 0's line count at `+0x217` and its lines at `+0x1c7`, which [the intro](#the-intro) paces itself by |
| `data\maplabel.str` | the constructor | two groups, at `+0x229` and `+0x2f3` ([Open](#open)) |
| `data\script.dat` | `ShellMap_LoadScriptDat` (`004243d7`) | blocks 1-3, 7 and 9-11 ([`../formats/script-dat.md`](../formats/script-dat.md)) |
| `dat\zone%d.dat`, `dba\zone%d.dba` | `ShellMap_LoadZone` (`00424b48`) | the zone named by the script's header: the `.dat`'s cell shift, and the `.dba`'s first frame as heights |
| `data\player.mec` | `ShellMap_ReadSquadHeader` (`00424db0`) | the second `int16`, the squad size, at `+0x80` |
| `data\mforms.dat` | the same | the formation record the squad stands in |

**The bounds** are every block-1 point's, widened by 10000 on each side (`DAT_00471c1c`): `+0x19f`/`+0x1a3` the low corner, `+0x1ab`/`+0x1af` the high. Of the 2,661 points in the 62 retail missions one has a non-zero height, `C1_03.MSN`'s GUID 15 at 35400, the spawn point of a flyer group, which the map does not draw.

**The nav path** at `+0x66` is a waypoint group from block 3: block 11 record 0 is the player's squad, its first order (`+0x5a`, into block 10) names the group at `+6`, and the map has no path when either is `-1`.

**The bases** are block 9 kept whole, `0x34` bytes a record in the file's own layout, in an array at `+0x62`. The loader then walks block 11: every record whose discriminator `+0x28` is 2 writes its own `+0x9a` (the `.msn` group's map-shown field) into `+0x1a` of each base its twenty member slots (`+0x32`) name, and its side `+0x6e` into their `+0x1c`, and moves each to its own point `+0x2c` — or, when that is `-1` and its route `+0x30` is not, to the route's first point (`00424940`-`004249ad`). A member ref is not checked against the block's count, and a later group overwrites an earlier one.

In the file, `+0x1a` and `+0x1c` are the first two operations of the structure's out-of-action report ([`../formats/script-dat.md`](../formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps)). The writes reach only this copy, which nothing writes back, so DBSIM still reads the record's own operations; a base no type-2 group names would be shown by its first operation. No retail mission has one under any campaign flags or variant roll: each of the 1,937 GUID-carrying base records in the 62 missions is named by a type-2 group record with no condition, and the only conditional group records that share such a group's GUID, and so [overlay](../formats/msn-mission-file.md#repeated-guids) it, are one each in `C4_09.MSN` and `C4_10.MSN`, of type 2 with no members. The 537 type-2 group records carry `-1` (465), 0 (71) or 1 (1). `+0x1a` is what [shows a base](#bases); no reader of `+0x1c` in the map's copy has been found ([Open](#open)).

**The heights.** `ShellMap_LoadZone` reads the `.dat`'s first two `int32` and discards them, keeps the third as the cell shift (`+0x104`) and the fourth as the height scale, and loads the `.dba` through `HeightGrid_Load` (`00429010`) and `HeightGrid_FromBitmap` (`00428d5b`), the shell's copy of the simulator's zone loader ([`../formats/terrain-heightmap.md`](../formats/terrain-heightmap.md)): each pixel byte is one cell's height, the bitmap's rows running north to south.

### The squad's positions

`ShellMap_ReadSquadHeader` places the members of block 11 record 0. Its anchor is the record's own point `+0x2c`; failing that the path's first point; failing that its first order's point `+4`; failing that the first point of that order's route `+6`. Its heading is block 2's entry at `+0x2e`, or with none, `atan2` of the path's first leg less a quarter turn (`Math_Atan2Bam`, `004505f0`), or 0 with no path. The block-2 value is used as it is stored, where the simulator multiplies it by 182 ([`../formats/script-dat.md`](../formats/script-dat.md)).

Each of the twenty member slots that names a block-7 record — not `-1` and below block 7's count — stands at the anchor, and every slot after the first is moved by `mforms.dat`'s offset for it: the formation is the first order's `+2`, and slot `n` takes the record's `n`-th `(x, y)` pair, turned through the heading by `Math_RotateVec2Q14` (`00451f94`), each component rounded as `(… + 0x2000) >> 14`. The positions are at `+0x82`, twelve bytes a slot.

## The camera

**The projection is a plan view, north up.** A world point lands at

```
x = centreX + ((worldX - cameraX) << 7) / altitude
y = centreY - ((worldY - cameraY) << 7) / altitude
```

in integer arithmetic truncating toward zero — the 3Space camera's `(view << 7) / depth` with no rotation over points whose height is 0. It is verified pixel for pixel against [`Reference/Managment_Mission_Briefing.png`](../../../Reference/Managment_Mission_Briefing.png): every base and squad icon pixel, every grid line and the bounds outline land where retail's do.

Three camera positions are derived at construction:

| | Set by | Centre | Altitude |
|---|---|---|---|
| full view, `+0x187`/`+0x18b`/`+0x18f` | `ShellMap_FitBounds` (`0042524e`) | the bounds' centre | the larger of `(halfWidth << 7) / (294 >> 1)` and `(halfHeight << 7) / (225 >> 1)`, also stored as the altitude limit `+0x3bd` |
| squad view, `+0x193`/`+0x197`/`+0x19b` | `ShellMap_FitSquad` (`004252e1`) | the squad's positions widened by 250000 (`DAT_00471c34`) | the same fit |
| start | `Map_Ctor` | 0, 0 | 200000 (`DAT_00471870`) |

The squad view takes the first `+0x80` slots — the count `player.mec` gives — whose member is not `-1`.

**The clamp.** `ShellMap_ClampCamera` (`00427891`) caps the altitude at `+0x3bd` and keeps the centre at least `(147 * altitude) >> 7` inside the bounds widened by a further 100000 (`DAT_00471c20`) across, and `(113 * altitude) >> 7` down; where the two limits cross, the high one wins. Before the intro is over a paint clamps a copy; after it, [the paint](#a-paint) clamps the camera plus its pan and keeps the result.

### The six buttons

The six map buttons ([`screen-layout.md`](screen-layout.md#the-mission-screen)) are `Mission_OnMapUp` (`00444ee7`) to `Mission_OnMapZoomOut` (`004452f2`), one each; each calls its method and then the paint, one to four times by its auto-repeat count ([`screen-layout.md`](screen-layout.md#the-three-views)).

| Button | Art | Method | Effect |
|---|---|---|---|
| 1 | up | `+0xc` | the pan `+0x3e` grows by the step `+0x42`, when the clamp would leave the moved centre where it is |
| 2 | down | `+0x10` | `+0x3e` shrinks by the step, on the same test |
| 3 | left | `+0x14` | `+0x3a` shrinks |
| 4 | right | `+0x18` | `+0x3a` grows |
| 5 | four arrows pointing in | `+4` | altitude down 50000 (`DAT_00471864`) while it stays at or above 50000 (`DAT_00471868`); step re-derived |
| 6 | four arrows pointing out | `+8` | altitude up 50000 when the clamp would leave it there; step re-derived |

The two zooms derive the pan step by different formulas. `Map_PanStepFor` (`00420195`), zooming in and at construction:

```
step = ((altitude - 50000 >> 3) * (100000 - 5000 >> 3)) / (1000000 - 50000 >> 3) * 8 + 5000
```

`ShellMap_PanStepFor` (`00427ae7`), zooming out, scales over the altitude limit instead:

```
step = ((altitude - 50000 >> 8) * (100000 - 5000)) / (limit - 50000) * 256 + 5000
```

Nothing re-derives it when the intro sets the altitude, so until the first zoom a pan moves 20000, the step for 200000.

## The relief

`ShellMap_BuildRelief` (`00426fe0`) draws the ground under the bounds once, into an 8-bit bitmap the paint stretches. It takes the cells from `(low - 100000) >> cellShift` to `(high + 100000) >> cellShift` on each axis and gives each `min(640 / columns, 400 / rows)` pixels (`DAT_00471c24`, `DAT_00471c26`), so the bitmap is `columns * perCell` by `rows * perCell`, cleared to 0.

A height becomes a colour as `min(height, 0x7f) / 5 + 0xd1` — the divisor is `0x80 / 0x18`, so the 128 heights fall in 26 steps, palette indices `0xd1` to `0xea`, and each briefing palette `br_w1`-`br_w5` carries its own ramp there. The rows run from the highest cell row down, each drawn as the band between its row and the one above; each cell is two triangles, `(left, bottom)`-`(left, top)`-`(right, top)` and `(left, bottom)`-`(right, top)`-`(right, bottom)`, whose corners take the colours of the heights at `(x, y)`, `(x, y + 1)`, `(x + 1, y + 1)` and `(x + 1, y)`. The last row and column of cells are never drawn, so the bitmap's right and bottom `perCell` pixels stay 0. A cell off the grid has all four corners at 0. A cell on the grid's last row reads the row above it past the end of the height array.

**The triangles are banded, not shaded** — `Gfx_BandedTriangle` (`00457aa8`), the 8-bit renderer's "Gouraud" fill. With its corners' colours `high >= mid >= low`, the edge from the high corner to the low one is cut into `high - low` steps (`point = from + (to - from) * i / steps`, truncating), and so are the two edges through the middle corner, `high - mid` and `mid - low` steps. The band between steps `i` and `i + 1` is a quadrilateral filled flat with `high - i` through the polygon filler (`Gfx_FillPolygon`, `00455798`; its span rules are in [`../polygon-fill.md`](../polygon-fill.md#scan-conversion)), closed along the first edge through the middle corner for the upper bands and the second for the lower, which are coloured `mid - i`. A triangle whose corners share a colour is filled flat; one with two corners on one pixel draws nothing.

## A paint

`ShellMap_Paint` (`0042540a`) runs seven passes over the viewport, clipped to the rect:

| Pass | Function | Draws |
|---|---|---|
| 1 | `ShellMap_Clear` (`0042551f`) | the viewport in `0x10` |
| 2 | `ShellMap_PaintRelief` (`004255b7`) | the relief, stretched so its corners land on the bounds widened by 100000 — the texture-mapped quad of `GLBitmap_DrawStretched` (`0045330c`) |
| 3 | `ShellMap_PaintGrid` (`004258f6`) | the grid in `0x0f`, then — outside the intro's first paint — the bounds outlined in colour 10 |
| 4 | `ShellMap_PaintPath` (`0042670d`) | the nav path in `0x0e` |
| 5 | `ShellMap_PaintBases` (`0042698d`) | the bases |
| 6 | `ShellMap_PaintNavMarkers` (`00426d3f`) | the nav markers |
| 7 | `ShellMap_PaintSquad` (`00426e5c`) | the squad |

`ShellMap_CurrentLine` (`00426f30`) runs between the last pass and the end: it picks the `mission.str` line the intro has reached and keeps its pointer at `+0x17e`, and every branch of what follows its choice is empty, so it draws nothing.

**The grid** passes through the projected world origin with a spacing measured as the projected x distance between the origin and `(3200000, 3200000)` — sixteen of `DAT_00471c3c`'s 200000 — shifted down by 4 per line: rightwards from the origin while short of the right edge, then leftwards from one spacing left of it, then downwards and upwards the same way, each line spanning the whole viewport.

**The path.** Once the intro has revealed all of it, a line through its points in order. Before then, only the line from the last point reached to the camera's centre, and a filled circle of radius 3 (`DAT_00471c5c`, `Gfx_FillEllipse`, `0045852c`) at the centre — the pen the intro draws with.

### Bases

A base is shown when its `+0x1a` is non-zero if it is friendly, and only when it is 1 if it is hostile; friendly is a type below `0x18` or from `0x2d` to `0x36`. Its icon is a `dba\mis_icon.dba` frame picked by type, in a colour for when it is too small to draw:

| Types | Frame | Colour | World size | Drawn offset | Pixel below |
|---|---|---|---|---|---|
| 4-6, 8-11, 13, 14, 16, 18-20 | 11 | 10 | 50000 | 10 | 5 |
| `0x1a`, `0x1c`-`0x1e`, `0x20`-`0x23`, `0x25`, `0x27`, `0x28` | 9 | `0x21` | 50000 | 10 | 5 |
| `0x2d`-`0x36` | 24 | 10 | 50000 | 5 | 3 |
| `0x37`-`0x40` | 25 | `0x21` | 50000 | 5 | 3 |
| any other, hostile | 8 | `0x21` | 50000 | 16 | 8 |
| any other, friendly | 10 | 10 | 50000 | 16 | 8 |

`ShellMap_PaintIcon` (`00426c8c`) projects the world size, `(size << 7) / altitude`: below the last column's figure it plots one pixel of the colour; below the frame's height it draws the frame scaled to that size and centred; otherwise it draws the frame as it is, offset up and left by half the drawn offset. The constants are the shorts at `DAT_00471c5e`-`DAT_00471c82`.

**The nav markers** are frames 14 onward, one per path point after the first — as many as the intro has revealed, at most nine — each offset by half of 13.

**The squad** is walked over the twenty slots, counting those whose member is not `-1` against how many the intro has revealed: frame 3 for a slot whose member is block-7 record 0 and frame 5 otherwise, at world size 100000, drawn offset 10 and pixel threshold 5, in `0x21`. A slot whose member is past block 7's count keeps the position the constructor zeroed.

## The intro

`Mission_Show` (`004441e3`) sets `DAT_0046c075` every time it shows the briefing. The shell's main loop then calls `ShellMap_RunIntro` (`0040146a`), which installs the briefing palette and loops `ShellMap_IntroStep` (`00425c7b`) until it returns false, presenting the frame (`Display_PresentStoredRect` (`00405cd8`)) and dispatching Windows messages (`Shell_PumpMessages` (`00405d9c`)) on each pass. It empties the keyboard ring before the loop (`Keyboard_FlushKeys` (`004052e1`)) and takes one key off it per pass (`Keyboard_PopKey` (`00404781`)), so a key the loop reads is gone from the ring and never posted as a shell event. A key reading 1 or `0x39` (Esc, Space), or either mouse button going down while the loop runs (`DAT_0046c07c`, which the window procedure sets while the loop's own flag `DAT_0046c078` is up), calls `ShellMap_SkipIntro` (`004253ef`), which moves a state below `0x12` to `0x11`.

The timer is `GetTickCount() >> 4` (`Shell_TimerTicks`, `00465a1c`), a tick of 16 ms. The state is `+0x172`, a deadline `+0x176`, the state a wait returns to `+0x174`; the revealed counts are `+0x183` (squad), `+0x184` (path points) and `+0x185` (nav markers).

| State | Does |
|---|---|
| 0 | first pass: the camera to the full view, a deadline 60 ticks on, and a paint to the front buffer with the waits below, then an ordinary paint. After that it holds until the deadline |
| 1, 2 | a zoom to the squad view over 60 ticks from the previous deadline: the step to the target is divided by 60 once and multiplied by the ticks gone |
| 3, 4 | one more squad member every 15 ticks until `+0x80` are shown; then 15 ticks to state 5 |
| 5, 6, 7 | each path point in turn: the path and marker counts go up by one and the camera pans to the next point over `(distance / 50000 + 1) * 15` ticks, the distance `Math_FastMagnitude3D` (`00450882`) |
| 8-16 | the `mission.str` lines interleaved with the stops, two per stop, then any left over |
| `0x11`, `0x12` | everything revealed, and a zoom back to the full view over 60 ticks |
| `0x13` | two more paints, then `+0x17a` is set and the function returns false from then on |

**The lines only pace it, and not even that.** The line states wait `strlen(line) * DAT_00471c9c` and `DAT_00471c98` ticks, both 0 in the image, and read only in this function; the caption that would show the line is the empty branches of `ShellMap_CurrentLine`. So the intro shows no text, and each line costs a pass or two.

**The first paint is slow on purpose.** While `+0x182` is set — only for state 0's first pass — the paint draws straight to the front buffer and busy-waits 10 ticks after the clear (`DAT_00471c2c`), 10 after the relief (`DAT_00471c38`) and one after each grid line (`DAT_00471c40`), skips the bounds outline, and draws the rest at once. The map appears as a black box, then the relief, then the grid line by line.

The map object keeps its state, so the next time the briefing is shown the loop makes one pass: state `0x13`, one paint, done.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `ShellMap` is VSHELL's mission editor, reading `script.dat` to display and edit placed units | It reads the whole of several blocks, which an editor would need, and nothing named it otherwise. It is the briefing's map: its builder runs on loading a mission, and its vtable is a paint, the six camera moves the mission tab's buttons call and a pan reset |
| `dba\zone%d.dba` is a picture of the zone, drawn as the map | It is a `.dba` bitmap and the map draws something shaped like the ground. Its pixels are heights, loaded through the same zone loader the simulator has, and the map draws them as coloured bands from the palette's `0xd1` ramp |
| The intro types out the briefing's lines | It walks `mission.str`'s lines and waits by their length. Both waits are 0 in the image and the function that would draw the line has an empty body past its choice of line |

## Open

- **Open:** what the two `maplabel.str` groups the constructor reads are for. None of the paint passes read here draws them.
- **Open:** the texel stepping of the textured quad (`Bitmap_BlitRotatedScaled`, `00458f68`) and the scaled blit (`Bitmap_BlitScaledClipDispatch`, `00458e78`). Against the retail capture the relief's bands differ along their edges by a pixel.
- **Open:** whether a click that skips the intro also reaches the widget under it afterwards. The window procedure queues the button's events while the loop runs, and nothing in the loop runs the shell's event queue (`Shell_PumpEvents` (`0046814c`)), so they are still queued when it ends; what the main loop's next pump delivers them to is unread.
- **Open:** whether anything reads the side the loader writes at a base's `+0x1c` in the map's copy. Neither `es2_fieldscan.py` over `00423f43`-`00427b80` nor a grep for `map+0x62` finds one, and the fieldscan also misses the loader's own indexed write, so it does not settle indexed reads.
