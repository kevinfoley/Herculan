# DBSIM.EXE — the ground and the sky

**Before it draws any shape, ES1's simulator paints the whole backdrop: a banded sky above the horizon, a flat ground below it, and a skyline of bitmaps standing on the horizon.** The ground is a 256 × 256 pattern tiled across the plane in perspective, which fades band by band toward one palette colour as it recedes; with the preferences set lower it is plain colour bands instead. The skyline scrolls sideways as the view turns, a full turn showing the whole bank once. The sky is ES2's ([ES2's sky](../../retail/rendering/distance-fog-and-sky.md#the-sky--hzline)) with the differences below; ES2 has none of the ground or the skyline in use. Addresses are `ES1DBSIM` image addresses ([`../formats/x32-executable.md`](../formats/x32-executable.md#the-image)). The fields come from the theater's world file ([`../formats/wld-world.md`](../formats/wld-world.md)).

The view is a 320-pixel-wide frame: several routines below use 160 as the projection centre's column and 320 as the view's width, as constants.

## The frame

`Sim_RenderFrame` (`00059c08`), unless `CockpitViewManager_Published`'s record has 1 at `+0x10` and a clear byte at `+0x18`, installs the camera's projection (`Raster_InstallViewProjection`), clears the model transform, and calls `Scene_DrawTerrainPass` (`00025b68`) with the camera and then `ObjList_DrawFrameObjects` (`00021008`), which draws the shapes ([`polygon-shading.md`](polygon-shading.md#which-objects-are-drawn)). `Scene_DrawTerrainPass`, given the camera:

1. calls `Hzline_Draw` (`00026264`) on `g_TheaterHzline` (`000928d4`) with the canvas origin pair at `000afa9c` and the camera, when the `hzline` exists;
2. calls slot `+0x34` of `World_Clouds` (`000928d8`) with the camera, when it exists ([Open](#open));
3. calls `PointScatter_Update` with `World_PointScatter` (`000928dc`) and the camera, when it exists ([the scattered points](../formats/wld-world.md#the-scattered-points), [Open](#open));
4. calls `Palette_BlendRangeByPitch` (`00025fe4`) with the camera ([below](#the-pitch-gradient-palette-entries-6479)).

ES2's (`Scene_DrawTerrainPass`, `0042e700`) sets the `hzline`'s one-colour flag and projects the terrain's far edge first, and ends by drawing the terrain; ES1's does none of the three. A second view, drawn by `000364f8`, calls `Hzline_Draw` with no canvas origin and then `ObjList_DrawFrameObjects`.

The camera is a context with its position at `+0`, `+4` and `+8` (x, y, z) and its angles at `+0xc` (pitch), `+0xe` (roll) and `+0x10` (heading) ([ES1's contexts](view-transform-and-projection.md#differences-from-es2)). The render context installed at `000bdbda` gives the view rect, left `+0x210`, top `+0x214`, right `+0x218`, bottom `+0x21c`, and its origin `+0x220`/`+0x224`; the projection centre is (left − origin x, top − origin y), written (*cx*, *cy*) below.

## The horizon line and the sky

`Hzline_Draw` (`00026264`), given the `hzline`, the canvas origin and the camera, calls `Hzline_DrawWithOffset` (`00026280`) with the `hzline`'s line offset `+0x30`. That runs ES2's steps ([the horizon line](../../retail/rendering/distance-fog-and-sky.md#the-horizon-line)) on the camera's pitch and roll: it stores the negated pitch at `+0x34` and the roll at `+0x36`, builds the line, moves both ends down by half the line offset, fills the sky, moves both ends down one row and fills the ground. ES1 differs in four places:

- **`Hzline_BuildHorizon` (`00026348`) makes the line 512 pixels long at every resolution**, its ends ±256 × (cos roll, −sin roll) from the midpoint, where ES2 scales them by the coordinate shift. A pitch whose cosine is 0 is replaced by the cosine of the pitch less one unit, where ES2 uses 1. A non-null canvas origin *o* moves both ends by ((cos roll · *o*.y) >> 14, −(sin roll · *o*.x) >> 14), cos and sin in Q14, where ES2 lengthens the line; `Hzline_DrawWithOffset` then adds *o* to both ends as well. The test that would negate the band step past a vertical pitch is ES2's and is never true.
- **`Hzline_FillSky` (`0002650c`) paints nothing while both ends of the line lie above the view's top row.** Otherwise its level branch is ES2's ([above the line](../../retail/rendering/distance-fog-and-sky.md#above-the-line)). Its rolled branch starts the first quad at the line itself, where ES2's starts it 2*h* below, and steps each next quad by (−sin roll · *h*, −*t*) for the first band and (−sin roll · *h*, −cos roll · *h*) after it, *t* being `+0x6c` >> 1 and *h* the band height `+0x54`. Beyond the last band it fills one polygon from the last band's far edge to the view-rect corners on the sky's side, two to four of them, in the zenith colour, where ES2's draws a quad 5000 pixels deep.
- **The ground below the line is [textured or banded](#the-ground)**, never ES2's one-colour fill: ES1's `hzline` has no one-colour flag.
- **When the line is in view, the [skyline](#the-horizon-skyline) is drawn last**, over the sky and the ground.

The sky's colours are ES2's: bands of `+0x54` rows from the horizon colour `+0x28` (zenith colour + band count − 1) down one palette entry a band, then the zenith colour. Every fill installs the solid brush `{0, colour}` at the render context's `+0x22c` and puts the previous brush back afterwards.

## The ground

`Hzline_FillGround` (`000267d8`), given the `hzline`, the canvas origin's x (0 when there is none) and the camera, paints nothing while both ends of the line lie below the view's bottom row. Otherwise:

- when the `hzline`'s byte `+0x70` is set and the preferences byte `000bd567` is greater than `000956be`, it draws [the textured ground](#the-textured-ground) with `GndTex_Draw` (`00029120`) and returns. `World_LoadTheater` sets `+0x70` to 1 ([the load](../formats/wld-world.md#the-load)), and `0004afe8` stores 240 in `000956be`. `000bd567` is the value of the preferences panel's option 5 (`00047f84`, case 5);
- otherwise it paints [colour bands](#the-banded-ground).

### The banded ground

The banded ground is the sky's banding turned downward, from the world file's shorts 4–6 ([the header shorts](../formats/wld-world.md#the-header-shorts)); it is the branch ES2's `Hzline_FillGround` leaves behind its one-colour flag ([below the line](../../retail/rendering/distance-fog-and-sky.md#below-the-line)). There are `+0x60` − 1 bands (short 6 − 1), the first in colour *top* = `+0x2c` + `+0x60` − 1 (short 4 + short 6 − 1) and each next one palette entry lower. Level, each band is a `Raster_FillRect` rect from the previous band's bottom row, the line for the first, down *t* rows for the first band (*t* = `+0x6c` >> 1) and `+0x5c` rows (short 5) for each later one, so each band's bottom row is painted over by the next; a last rect from the last band's bottom row to the view's bottom row takes colour `+0x2c` (short 4). Rolled, each band is a `Raster_DrawPolygonDispatch` quad stepped away from the sky as the sky's quads step toward it, and the last fill a polygon from the last band's far edge to the view-rect corners on the ground's side.

So the ground's colour steps from short 4 + short 6 − 1 at the horizon to short 4 at the bottom of the view: in retail data, 5 to 11 bands of 1 to 3 rows above the last fill.

### Loading the ground texture

`GndTex_Load` (`00028de8`, `gndtex.cpp`), given the world file's fifth string, its fourth, and shorts 0, 5, 6 and 4, in stack order:

1. sets `00092c4c` to 1, builds `dba\<fourth>.64k`, calls `GndTex_Alloc` (`00029714`) with 320 and 380, and passes the name to `GndTex_LoadTexture` (`00029830`);
2. opens `dat\<fifth>`, asserting at severity 5 (`gndtex.cpp` line `0x3f`) when it cannot, and reads an `int32` count to `000b0d74` and that many `int32` to `000b0d78`: in retail, 15 ascending distances, from 20000 to 112400 in `TEX.DAT`;
3. stores short 5 at `000b0d7c`, half of short 0 at `000b0d84`, 15 at `000b0d80` and 320 at `00092c80`;
4. builds 15 fade tables of 16 bytes at `000b0d70` (allocating count × 4 bytes there first, then replacing them with the 15 pointers): with *g* short 4, *b* = *g* & `0xf0` and *top* = *g* + short 6 − 1, entry *j* of table *i* is

```
s = top − (b + j);   if s < 0: s = s − 1
entry = b + j + (s · i) / 14              integer division, truncating
```

so table 0 is the 16-entry palette row that holds the ground colour, unchanged, and each later table moves every entry of that row further toward *top*; table 14 is *top* throughout, or *top* − 1 for an entry above it.

`GndTex_Alloc` (`00029714`), given 320 and 380, does nothing when `00092c70` is already set. Otherwise it allocates the 64 KB texture at `00092c74`, gets a selector for it with `GndTex_AllocSelector` (`000298b8`), and when that succeeds fills the bitmap header at `000b0d8c` (the bitmap object is at `000b0d88`): rows 320 at `+0`, columns 380 at `+2`, 8 bits at `+4`, bit 4 of `+5` set, the size 121,600 at `+6` and a buffer of that size at `+0xa`. It zeroes the texture and the buffer and sets `00092c70`; when anything fails it frees the texture and leaves `00092c70` clear. `GndTex_AllocSelector` asks the extender for DS's base (`INT 21h` `AX=3504h`, `BX` = DS), adds the texture's address, and calls `AX=3501h`, `3503h` with that sum in `ECX` and `3505h` with `ECX` = `0x10000`, keeping the `BX` of the `3501h` call at `00092c72`; on a carry it returns 0. `GndTex_LoadTexture` (`00029830`) reads the file's first 65,536 bytes into the texture when `00092c70` is set and the file opens.

`TRENCHES.64K` holds only palette indices 244 to 253 and `CRACKS.64K` 242 to 250, inside the 240–255 the draw expects ([below](#the-textured-ground)).

`GndTex_Free` (`000297f0`), when `00092c70` is set, frees the selector with `GndTex_FreeSelector` (`000298fe`), the texture and the buffer, and clears `00092c70`, `00092c72` and both pointers. `0005a7b8` and `0005b568` call it. `GndTex_FreeSelector` zeroes ES, FS and GS and calls `AX=3502h` with the selector in `BX`; the `JZ` that would skip that tests the flags its caller left, which `GndTex_Free`'s test of `00092c70` leaves clear, so it always calls it.

### The textured ground

`GndTex_Draw` (`00029120`), given the canvas origin's x, `Raster_FocalShift`, 8, the line's two end rows (right end first), the camera's angles and the camera, renders the ground row by row into the bitmap at `000b0d88` and then puts the bitmap on screen. *f* is 2^`Raster_FocalShift`, (*x*, *y*, *z*) the camera's position, *p*, *r* and *h* its pitch, roll and heading.

**The rows.** *start* is the mean of the line's two end rows (their sum shifted right by one), raised to the view's top row when above it. It returns at once when *cy* is negative. Level (roll exactly 0), *d* = *start* − *cy* and *rows* = bottom − *start* + 1. A *rows* of 320 or more becomes 319. The first table row is *first* = *d* − origin y, which is *start* − top when level.

**The row table.** `GndTex_BuildRowTable` (`00028fbc`), given the canvas origin's x, *rows*, *first*, the focal shift, the pitch and *z*, fills 12-byte entries `{λL, D, λR}` from `000afe68`, one per row *k* from *first* to *first* + *rows* − 1:

```
(q0, q1) = rotate((−origin.y − k − 1,  f),  −p)       the ray through the row, levelled
D        = q0 ≠ 0 ? (−z · q1) / q0 : 10,000,000      forward ground distance to where it meets z = 0
(s0, s1) = rotate((−z, D),  p)                       s1: that point's depth in the view
λL       = ((originX − 160) · s1) >> shift           lateral offsets of the view's two edge columns
λR       = ((originX + 159) · s1) >> shift
```

with `rotate(v, a)` = `Math_Rotate2DPoint` through `Math_BuildRotation2D(a)`: `x' = (x·cos a − y·sin a + 0x2000) >> 14`, `y' = (x·sin a + y·cos a + 0x2000) >> 14`, cos and sin from the 1024-entry quarter-wave table in Q14, the angle rounded to it (the matrix's sin is cos(*a* − `0x4000`)). The divisions truncate. The table is rebuilt only when *z*, the pitch or the origin's x differs from the last build's (`00092c44`, `00092c48`, `000b0d6c`) or *rows* exceeds its row count (`000b0d68`); *first* does not enter the test.

**Drawing the rows.** The texture transform is `Math_BuildRotation2D(h)` with the translation (*x* & `0xffff`, *y* & `0xffff`), the mask 2^(8 + 8) − 1 from the third argument. Row *i* of the bitmap, *i* from 0, takes table entry *i*:

1. its fade table: number 14 when *i* is less than half of short 0 (`000b0d84`), so the rows nearest the horizon are fully faded; otherwise the number of the first 14 `TEX.DAT` distances that *D* reaches or passes, counting from the first and stopping at the first it does not reach;
2. the two ends' ground points, `Transform2D_ApplyToPoint` of (λL, *D*) and (λR, *D*): `x' = (λ·cos h − D·sin h + 0x2000) >> 14 + tx`, `y' = (λ·sin h + D·cos h + 0x2000) >> 14 + ty`, in 64 bits;
3. the texture coordinates in 8.16 fixed point, each kept to 24 bits: *u* = (left *x'* << 8) & `0xffffff`, *v* = (left *y'* << 8) & `0xffffff`, and the steps *du* = ((right *x'* − left *x'*) · 204 >> 8) & `0xffffff`, *dv* likewise, 204/256 standing in for 256/320;
4. `GndTex_DrawSpan` (`0002991d`) writes `00092c80` pixels from the row's start: for each, the texel at byte (*v* >> 16 & 255) · 256 + (*u* >> 16 & 255) of the texture, through the fade table, entry texel − 240; then *u* += *du* and *v* += *dv*, the integer parts wrapping at 256. The routine is the last `00092c80` copies of an unrolled 13-byte step repeated 380 times (`00029989`–`0002acd4`), entered by a jump to `0002acd5` − 13 × `00092c80`; it reads the texels through the selector at `00092c72` in GS.

The next row starts `00092c80` bytes further on. One texel covers 256 world units, so the 256 × 256 pattern repeats every 65,536 units in both directions, its rows along world *y*, and turns with the heading. The first pixel of a row is the view's left edge, and the step covers the view's 320 columns.

**Putting it on screen.** The bitmap's row count becomes *rows*, its column count `00092c80`. Level, `Bitmap_Blit` draws it with its top-left at (0, *start*).

Rolled, `GndTex_Draw` draws the rows in the roll-free frame and turns the bitmap. With *c* = cos *r* (1 when 0) and *s* = sin *r*, Q14:

- *d* = ((*start* − *cy*) · 2^14) / *c*; with *M* = `Math_BuildRotation2D(r)`, *P* = `rotate((±160, bottom + origin.y + 1), M)`, 160 taking the roll's sign, and *rows* = *P*.y − *d*;
- the width `00092c80`, with *a* = |*d*| as an `int16` (`0x7fff` for −`0x8000`): for *r* above `0x16a8`, `0x320000 / cos(0x4000 − r) + (a·c) / cos(r + 0xc000)`; for *r* below −`0x16a8`, `0x320000 / cos(0xc000 − r) + (a·c) / cos(r + 0xc000)`; otherwise `0x500000 / c`, plus `cos(r + 0xc000) / (a·c)` when *a*·*c* is not 0. The width persists at `00092c80` into later frames, level ones included;
- *first* also loses ((*cx* − 160) · *s*) >> 14;
- after the rows, *Q* = `rotate((width >> 1, d), M)`, and `Bitmap_BlitRotatedUnpacked` (`00067214`) draws the bitmap turned by −*r*, sized (width, *rows*), its top-left corner at (160 − *Q*.x, *Q*.y + *cy*).

`Bitmap_BlitRotatedUnpacked` is ES2's (`Bitmap_BlitRotatedUnpacked`, `00481750`): it unpacks a packed bitmap into the scratch buffer and calls `Bitmap_BlitRotatedScaled` (`00076630`) directly where ES2 goes through a wrapper. That is ES2's `Bitmap_BlitRotatedScaled` (`00488a8c`): the bitmap's whole frame, (0, 0) to (columns − 1, rows − 1), mirrored by flag bits 2 and 1, is mapped onto the rectangle (0, 0)–(width, height) turned by the angle about its top-left corner and moved to the point, and filled through brush mode 5 `{5, 4, the source corners, the bitmap}` ([Open](#open)).

`GndTex_Draw` bounds neither the width nor the bitmap's size: the span routine holds 380 steps and the buffer 380 × 320 bytes.

## The horizon skyline

`Hzline_DrawWithOffset` draws the skyline last, with `Hzline_BlitHorizonStrip` (`00026ac8`), when either end row of the line plus the line offset is greater than 0, the `hzline`'s bank `+0x50` is loaded and the preferences byte `000bd565` (option 3, `00047f84` case 3) is non-zero. The bank is `dba\world<N>`, which only the world files with *d* 0 name ([the layout](../formats/wld-world.md#the-layout)). It is ES2's function (`Hzline_BlitHorizonStrip`, `0042f398`) without ES2's coordinate shifts.

Given the `hzline`, the canvas origin, the line offset, the camera and the bank, with *n* the bank's frame count (`+4`), its frames at `+8`, and *w* frame 0's column count, every frame taken as *w* wide:

1. *X* = (heading · 2^16) / ((2^16 / *n*) · 2^16 / *w*), unsigned, so the bank's *n* frames span one turn of heading; *X* steps back by *n* · *w* until it is at or left of the view's left edge plus the canvas origin's x, *L*.
2. Level: the step along the line is (*w*, 0) and the strip's row is the line's left end row `+0x3c`. Rolled: the step is ((cos roll · *w*) >> 14, −(sin roll · *w*) >> 14), and the start is the line's midpoint (plus the canvas origin) moved along the line by *X* − 160.
3. Frames before the view are skipped, the frame number advancing modulo *n*, while the next step's *x* is still left of *L*.
4. From there, while *x* is left of *L* + 320 and fewer than *n* frames have been drawn, each frame goes up with its bottom on the line, at the step's point less the canvas origin: level, `Bitmap_Blit` at (*x*, row + line offset − the frame's row count); rolled, `Bitmap_BlitRotatedUnpacked` turned by −roll, sized (*w*, the frame's rows), its top-left moved by (line offset − rows) along (sin roll, cos roll) in Q14. The frame number wraps at *n*.

So the skyline is a 360° panorama of *n* frames standing on the horizon, which slides by one frame width for every *n*-th of a turn.

## The pitch gradient (palette entries 64–79)

`Palette_BlendRangeByPitch` (`00025fe4`), given the camera, rewrites palette entries 64 to 79 each frame while the byte `000928ec` is set. The first time, it reads from the active palette the colours of entry `000bc5ce` (64, as the load stores it), of entries short 10 (`000bc5d0`) and short 11 (`000bc5d2`), and of entry `000bc5d4` (98), and builds 16 colours from short 10's colour to short 11's with `Palette_InterpolateColours` (`000283b0`) at `000afde4`. Then every frame:

1. the step: when the `int16` at `000928ee` is negative, ((*p* + `0x2000`) & `0xffff`) >> 10, at most 15; otherwise `000928ee` itself, after which `000928ee` becomes −1;
2. the end colour: entry 98's colour while `000928eb` is set, otherwise colour number *step* of the 16. `Palette_ActivateImpact` (`00025ed0`) sets `000928eb`, `Palette_ToggleImpact` (`00025ef8`) flips it and `Palette_RestoreFromImpact` (`00025f1c`) clears it, each only while `ImpactPaletteObject` is loaded and each switching palettes through `00027e7c`, as ES2's do;
3. 16 colours from entry 64's original colour to the end colour, through `Palette_InterpolateColours`, installed at entries 64–79 with `Palette_InstallRange`.

`Palette_InterpolateColours` is ES2's (`004307b0`), its arguments in the reverse order: for each pair, *levels* − 1 colours stepping by (to − from) · 256 / (*levels* − 1) per channel in 8.8 fixed point, then the *to* colour, each written with `0x01` in its top byte. `Palette_GetEntry` (`00027f54`) returns the active palette's dword for an index.

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `Scene_DrawTerrainPass` | `00025b68` | |
| `Palette_ActivateImpact`, `Palette_ToggleImpact`, `Palette_RestoreFromImpact` | `00025ed0`, `00025ef8`, `00025f1c` | |
| `Palette_BlendRangeByPitch` | `00025fe4` | where ES2's `Scene_DrawTerrainPass` ends with a call to an empty function |
| `Hzline_Draw`, `Hzline_DrawWithOffset`, `Hzline_BuildHorizon` | `00026264`, `00026280`, `00026348` | |
| `Hzline_FillSky`, `Hzline_FillGround`, `Hzline_BlitHorizonStrip` | `0002650c`, `000267d8`, `00026ac8` | |
| `Palette_GetEntry`, `Palette_InterpolateColours` | `00027f54`, `000283b0` | |
| `GndTex_Load`, `GndTex_BuildRowTable`, `GndTex_Draw` | `00028de8`, `00028fbc`, `00029120` | |
| `GndTex_Alloc`, `GndTex_Free`, `GndTex_LoadTexture` | `00029714`, `000297f0`, `00029830` | |
| `GndTex_AllocSelector`, `GndTex_FreeSelector`, `GndTex_DrawSpan` | `000298b8`, `000298fe`, `0002991d` | the span's unrolled steps have no Ghidra function |
| `Math_TransposeRotation2D`, `Math_BuildRotation2D`, `Transform2D_ApplyToPoint` | `00063df0`, `000694b8`, `00069448` | the three that [`fixed-point-math.md`](../simulation/fixed-point-math.md#the-assembly-library) lists as ES2's code |
| `Bitmap_BlitRotatedUnpacked`, `Bitmap_BlitRotatedScaled` | `00067214`, `00076630` | |

## Globals

| | Address |
|---|---|
| the ground bitmap object, its header (rows, columns, depth, flags, size, pixels) | `000b0d88`, `000b0d8c`–`000b0d96` |
| the texture, its selector, the allocated flag | `00092c74`, `00092c72`, `00092c70` |
| the row table, 12 bytes a row | `000afe68` |
| the row table's key: *z*, pitch, rows, origin x | `00092c44`, `00092c48`, `000b0d68`, `000b0d6c` |
| the span's width, its entry, destination, fade table − 240 | `00092c80`, `00092ca8`, `00092c78`, `00092c7c` |
| the span's *u*, *v*, *du*, *dv* | `00092c8c`, `00092c94`, `00092c9c`, `00092ca4` |
| the fade tables, the distances, their count, the table count | `000b0d70`, `000b0d78`, `000b0d74`, `000b0d80` |
| short 5, half of short 0 | `000b0d7c`, `000b0d84` |
| the pitch gradient's 16 colours, its built flag, its forced step | `000afde4`, `00092a3e`, `000928ee` |

## Open

- **Open:** what draws the clouds: `World_Clouds`' slot `+0x34`, which `Scene_DrawTerrainPass` calls each frame with the camera, and the clouds' class ([`../formats/wld-world.md`](../formats/wld-world.md#open)).
- **Open:** what `00026fe0` draws for the scattered points, which `PointScatter_Update` calls each frame with the camera; it shows count × `000bd567` / 240 of them while the preferences value is below 240.
- **Open:** what the preferences panel's options 3 and 5 are called and which values they take; `0005b48a` also sets `000b5b96` to whether option 5 exceeds 240.
- **Open:** what draws with palette entries 64–79, which the pitch gradient rewrites, and what sets `000928ec` (`00057f89`, `00058407`) and `000928ee` (`00057e15`, `0005b3a3`).
- **Open:** what the canvas origin pair at `000afa9c` holds, and what writes it (`00022384`, `0002252c`, `0002253c`, `000243f9`, `0002e131` hold its address).
- **Open:** what `00092c4c`, which `GndTex_Load` sets to 1, is read for; `es2_xref.py` finds no reference to it besides that store.
- **Open:** what draws the second view that `000364f8` renders.
- **Open:** what brush mode 5, which `Bitmap_BlitRotatedScaled` fills the turned ground bitmap and the rolled skyline frames through, does with palette index 0: `Raster_DrawPolygonDispatch` hands modes 5 and 7 to the routine at `0009df58`, which the image holds as 0 and the driver fills.
