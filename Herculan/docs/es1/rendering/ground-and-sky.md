# DBSIM.EXE — the ground and the sky

**Before it draws any shape, ES1's simulator paints the whole backdrop: a banded sky above the horizon, a flat ground below it, and a skyline of bitmaps standing on the horizon.** The ground is a 256 × 256 pattern tiled across the plane in perspective, which fades band by band toward one palette colour as it recedes; with the preferences set lower it is plain colour bands instead. The skyline scrolls sideways as the view turns, a full turn showing the whole bank once. The sky is ES2's ([ES2's sky](../../retail/rendering/distance-fog-and-sky.md#the-sky--hzline)) with the differences below; ES2 has none of the ground or the skyline in use. Addresses are `ES1DBSIM` image addresses ([`../formats/x32-executable.md`](../formats/x32-executable.md#the-image)). The fields come from the theater's world file ([`../formats/wld-world.md`](../formats/wld-world.md)).

The view is a 320-pixel-wide frame: several routines below use 160 as the projection centre's column and 320 as the view's width, as constants.

## Where the object files start and end

The backdrop's code is `00025128`–`0002acdb`, after the clip regions' object file ([`mechview.cpp`'s end](../simulation/cockpit-views.md#where-the-module-starts-and-ends)). Two `ConfigPart` runs ([module copies](../formats/compiler.md#vtables)) open its first two parts, and zero fill and static initializers ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)) divide the rest:

| Code | What | Opens with |
|---|---|---|
| `00025128`–`000260e3` | `World_LoadTheater` ([the load](../formats/wld-world.md#the-load)), [the clouds' destructors](#the-object), `Scene_DrawTerrainPass`, the shades and the distance bands ([`polygon-shading.md`](polygon-shading.md#the-distance-bands)), the impact palette switch and the pitch gradient | the run `00025128`, `00025150`, `0002518f`, `00025198`, its tables `0009286e` and `000928a2` |
| `000260e4`–`00026deb` | the `hzline` | the run `000260e4`, `0002610c`, `0002614b`, `00026154`, its tables `00092a42` and `00092a76` |
| `00026dec`–`000271bf` | the scattered points | zero fill |
| `000271c0`–`00027bff` | `TexPoly` and its classes' destructors | zero fill |
| `00027c00`–`00027e0f` | the clouds' set-up and step | zero fill |
| `00027e10`–`00028de7` | the palette module ([`palette.md`](palette.md)), its static initializer last | zero fill |
| `00028de8`–`00029713` | `gndtex.cpp`: the ground texture's load and draw, its static initializer last | zero fill |
| `00029714`–`000298b7` | the texture's allocation, the ground bitmap's static initializer and exit routine last | — |
| `000298b8`–`0002acdb` | the selector calls and the span routine, frameless | zero fill |

Each run is the code of `mechview.cpp`'s, its class tag stubs at `0002518f` and `0002614b`. The first part's data, `0009286e`–`00092a41`, holds after its run's tables the backdrop's globals from `g_TheaterHzline` (`000928d4`), the clouds' vtable `000928f6`, the strings `world`, `world`, `wld`, `cyc` and `shades`, and its own copies of `TexPoly`'s five base tables, `00092952` to `00092a16`, which the clouds' destructor stores. The `hzline`'s data, from `00092a42`, holds its run's tables and the `hzline`'s vtable `00092aaa`; `TexPoly`'s seven tables, `00092ade` to `00092bde`, follow, then the palette's globals from the active palette (`00092c20`) to `00092c43`, `gndtex.cpp`'s from `00092c44` with its source path at `00092c5a`, and the texture's and the span routine's from `00092c70` to `00092cad`. `gndtex.cpp`'s is the only source path in it.

The span routine's 380 unrolled steps, `00029989`–`0002acd4` ([the textured ground](#the-textured-ground)), have no Ghidra code; its exit, `0002acd5`–`0002acda`, pops `EBX`, `EDI`, `ESI`, `EBP` and `DS` and returns. One zero byte follows, then `panel.cpp`'s run ([where it starts](../simulation/cockpit-panel.md#where-the-module-starts-and-ends)).

## The frame

`Sim_RenderFrame` (`00059c08`), unless `CockpitViewManager_Published`'s record has 1 at `+0x10` and a clear byte at `+0x18`, installs the camera's projection (`Raster_InstallViewProjection`), clears the model transform, and calls `Scene_DrawTerrainPass` (`00025b68`) with the camera and then `ObjList_DrawFrameObjects` (`00021008`), which draws the shapes ([`polygon-shading.md`](polygon-shading.md#which-objects-are-drawn)). `Scene_DrawTerrainPass`, given the camera:

1. calls `Hzline_Draw` (`00026264`) on `g_TheaterHzline` (`000928d4`) with the canvas origin pair at `000afa9c` and the camera, when the `hzline` exists;
2. calls slot `+0x34` of `World_Clouds` (`000928d8`) with the camera, when it exists, which drifts and draws [the clouds](#the-clouds);
3. calls `PointScatter_Update` with `World_PointScatter` (`000928dc`) and the camera, when it exists, which draws [the scattered points](#the-scattered-points);
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

`GndTex_Alloc` (`00029714`), given 320 and 380, does nothing when `00092c70` is already set. Otherwise it allocates the 64 KB texture at `00092c74`, gets a selector for it with `GndTex_AllocSelector` (`000298b8`), and when that succeeds fills the bitmap header at `000b0d8c` (the bitmap object is at `000b0d88`): rows 320 at `+0`, columns 380 at `+2`, 8 bits at `+4`, bit 4 of `+5` set, the size 121,600 at `+6` and a buffer of that size at `+0xa`. It zeroes the texture and the buffer and sets `00092c70`; when anything fails it frees the texture and leaves `00092c70` clear. `GndTex_AllocSelector` asks the extender for DS's base (`INT 21h` `AX=3504h`, `BX` = DS), adds the texture's address, and calls `AX=3501h`, `3503h` with that sum in `ECX` and `3505h` with `ECX` = `0x10000`, keeping the `BX` of the `3501h` call at `00092c72`; on a carry it returns 0. `GndTex_LoadTexture` (`00029830`) reads the file's first 65,536 bytes into the texture when `00092c70` is set and the file opens. At startup `GndTexBitmap_StaticInit` (`0002989c`) passes the bitmap object `000b0d88` to `00066e34`, and at exit `GndTexBitmap_StaticDtor` (`000298a8`) passes it and 2 to `00066f20` ([Open](#open)).

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

with `rotate(v, a)` = `Math_Rotate2DPoint` through `Math_BuildRotation2D(a)`: `x' = (x·cos a − y·sin a + 0x2000) >> 14`, `y' = (x·sin a + y·cos a + 0x2000) >> 14`, cos and sin from the 1024-entry quarter-wave table in Q14, the angle rounded to it (the matrix's sin is cos(*a* − `0x4000`)). The divisions truncate. The table is rebuilt only when *z*, the pitch or the origin's x differs from the last build's (`00092c44`, `00092c48`, `000b0d6c`) or *rows* exceeds its row count (`000b0d68`); *first* does not enter the test. The table holds 320 entries, which `gndtex.cpp`'s static initializer, `GndTex_StaticInit` (`000296cc`), builds at startup with `__vec_ctor` and `GndTexRow_ElementCtor` (`000296e4`, entered at its padding `000296e3`), which allocates 12 bytes when given none and writes nothing.

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

`Bitmap_BlitRotatedUnpacked` is ES2's (`Bitmap_BlitRotatedUnpacked`, `00481750`): it unpacks a packed bitmap into the scratch buffer and calls `Bitmap_BlitRotatedScaled` (`00076630`) directly where ES2 goes through a wrapper. That is ES2's `Bitmap_BlitRotatedScaled` (`00488a8c`): the bitmap's whole frame, (0, 0) to (columns − 1, rows − 1), mirrored by flag bits 2 and 1, is mapped onto the rectangle (0, 0)–(width, height) turned by the angle about its top-left corner and moved to the point, and filled through brush mode 5 `{5, 4, the source corners, the bitmap}`, which leaves the bitmap's palette index 0 unpainted ([bitmap fills](polygon-shading.md#bitmap-fills-brush-modes-5-and-7)).

`GndTex_Draw` bounds neither the width nor the bitmap's size: the span routine holds 380 steps and the buffer 380 × 320 bytes.

## The horizon skyline

`Hzline_DrawWithOffset` draws the skyline last, with `Hzline_BlitHorizonStrip` (`00026ac8`), when either end row of the line plus the line offset is greater than 0, the `hzline`'s bank `+0x50` is loaded and the preferences byte `000bd565` (option 3, `00047f84` case 3) is non-zero. The bank is `dba\world<N>`, which only the world files with *d* 0 name ([the layout](../formats/wld-world.md#the-layout)). It is ES2's function (`Hzline_BlitHorizonStrip`, `0042f398`) without ES2's coordinate shifts.

Given the `hzline`, the canvas origin, the line offset, the camera and the bank, with *n* the bank's frame count (`+4`), its frames at `+8`, and *w* frame 0's column count, every frame taken as *w* wide:

1. *X* = (heading · 2^16) / ((2^16 / *n*) · 2^16 / *w*), unsigned, so the bank's *n* frames span one turn of heading; *X* steps back by *n* · *w* until it is at or left of the view's left edge plus the canvas origin's x, *L*.
2. Level: the step along the line is (*w*, 0) and the strip's row is the line's left end row `+0x3c`. Rolled: the step is ((cos roll · *w*) >> 14, −(sin roll · *w*) >> 14), and the start is the line's midpoint (plus the canvas origin) moved along the line by *X* − 160.
3. Frames before the view are skipped, the frame number advancing modulo *n*, while the next step's *x* is still left of *L*.
4. From there, while *x* is left of *L* + 320 and fewer than *n* frames have been drawn, each frame goes up with its bottom on the line, at the step's point less the canvas origin: level, `Bitmap_Blit` at (*x*, row + line offset − the frame's row count); rolled, `Bitmap_BlitRotatedUnpacked` turned by −roll, sized (*w*, the frame's rows), its top-left moved by (line offset − rows) along (sin roll, cos roll) in Q14. The frame number wraps at *n*.

So the skyline is a 360° panorama of *n* frames standing on the horizon, which slides by one frame width for every *n*-th of a turn.

**A level frame's palette index 0 is transparent: the sky and the ground already painted show through it.** `Bitmap_Blit` (`00067048`), given flags 0, calls `Raster_BlitDispatch` (`0006a8e0`), which moves the point by the canvas's origin when the canvas (`00098d64`) has 1 at `+0x208`, hands a 1-bit bitmap (type 1 in the low four bits of header byte `+5`) to `0007d66c`, and otherwise calls the active [display driver's](polygon-shading.md#the-display-drivers) blit through `0009df3c` when the canvas's `+0x204` is 0, or its clipped blit through `0009df44`, with the canvas's clip rect `+0x20c`, when `+0x204` is 1, or 2 with a positive `+0`. Both 8-bit drivers' blits, `VGA8_BlitBitmap` and `BMP8_BlitBitmap`, copy every byte when header byte `+5` has bit 4 set ([the opaque bit](../../retail/formats/dfn-hfn-dci.md#the-bitmap-record)) and otherwise skip each 0 byte, leaving the destination pixel as it was (`OR AL,AL` / `JZ` at `0008bc49` in `VGA8_BlitBitmap`); `BMP8_BlitBitmap`'s clipped blit (`0008dc3c`) calls `BMP8_BlitBitmap`. `VGA_8`'s clipped blit, `VGA8_BlitBitmapClipped` (`0008c27c`), clips the bitmap to the rect, and in clip mode 2 each row to every region of the clip block's list that holds it as well, and copies or skips by the same bit (`0008c58b`). The simulator draws through `VGA_8` with its canvas in clip mode 1 ([the display](polygon-shading.md#the-display-drivers)), so a level frame goes through `VGA8_BlitBitmapClipped`, and a rolled one, through brush mode 5, leaves index 0 unpainted too ([bitmap fills](polygon-shading.md#bitmap-fills-brush-modes-5-and-7)). Every frame of the twelve `WORLD<M>.DBA` banks in `SIMVOL0.VOL`, four frames each, 320 wide and 3 to 48 rows, unpacked, has 0 in byte `+5`.

## The clouds

**A layer of flat textured clouds hangs high over the ground, drifting with one wind; a cloud that falls far behind the viewer reappears as far ahead.** There are as many as the world file's count, 20 in retail ([the clouds](../formats/wld-world.md#the-clouds)), each a square 25000 units on a side at height 20000, textured from the `dba\clouds2` bank, with no light and no distance colour. Their class is ES2's `TexPoly`, a `TSTexture4Poly` drawn once at each of a set of points round the viewer, which ES2 never builds ([ES2's `TexPoly`](../../retail/simulation/random-generator.md#open)); ES1 adds a subclass that sets it up and moves it.

### The object

`TexPoly_Ctor` (`000271c0`) builds the base ([the load](../formats/wld-world.md#the-clouds)); `World_LoadTheater` then stores the subclass's vptr `000928f6` over `TexPoly`'s `00092bde`.

The classes are ES2's chain, `ClassItem`, `TSBase`, `TSGroupItem`, `TSPoly`, `TSSolidPoly`, `TSTexture4Poly` and `TexPoly`, whose tables `TexPoly_Ctor` stores in that order, `00092ade` to `00092bde`; ES1's subclass replaces slots `+0x24` and `+0x34` ([the set-up](#the-set-up), [each frame](#each-frame)). Each destructor, slot `+0x1c` of its class's table, given the object and the flags, does nothing for a null object; otherwise it stores its own class's vptr and each base's in turn down to `TSGroupItem`'s, calls `TSBase_Dtor` with flags 0, and frees the object when bit 0 of the flags is set. None frees the positions, the angle triples, the carry-over bytes or the bitmap bank. The clouds' destructor, `Clouds_Dtor` (`00025a08`), stores `000928f6` and then the first part's copies of the base tables, `00092952` to `00092a16`; `TexPoly_Dtor` (`000274c8`) stores `00092bde` to `00092b2e`. ES2's `TexPoly_Dtor` (`004302fa`) likewise frees only the object.

| Offset | |
|---|---|
| `+0x04`, `+0x06`, `+0x08`, `+0x0a`, `+0x0c` | the poly's normal point, centre point, corner count, index offset and surface index, a `TSPoly`'s: 4, 0, 4, 0 and 0 when it draws |
| `+0x0e` | the clouds' positions, three `int32` each |
| `+0x12` | an angle triple per cloud |
| `+0x16` | a byte per cloud, bit 0 set while the cloud is [carried over](#each-frame) |
| `+0x1a` | the bitmap bank |
| `+0x1e` | set once the layer is set up |
| `+0x1f`, `+0xf9` | the number of clouds and the number drawn, `int16` |
| `+0x21` | five points of three `int32`: the four corners and the normal |
| `+0x5d`, `+0x61`, `+0x65` | the surface entries: the frame seen from below, an unused one, the frame seen from above |
| `+0x69`, `+0x6d`, `+0x71`, `+0x75`, `+0x79` | the field radius, the size in x and in y, the height, the carry-over margin |
| `+0x7d`, `+0x9b`, `+0xd7`, `+0xf5` | the ring tables and their count ([Open](#open)) |
| `+0xfb`, `+0xff`, `+0x103` | the viewer's position at the last frame |
| `+0x107`, `+0x10b`, `+0x10f` | the wind, three `int32`, the subclass's |

### The set-up

`TexPoly_DrawAtPoints` calls slot `+0x24` with the camera the first time it runs, while `+0x1e` is clear. The subclass's, `Clouds_Setup` (`00027c00`):

1. calls `TexPoly_SetQuadAndRadius` (`000275dc`) with 200000, 10000, 25000 and 25000 in stack order: the field radius `+0x69` = 200000, the height `+0x75` = 10000, the sizes `+0x71` and `+0x6d` = 25000, and the corners (0, 0, 10000), (25000, 0, 10000), (25000, 25000, 10000) and (0, 25000, 10000);
2. stores the margin 10000 at `+0x79`, and with `TexPoly_SetFifthVertex` (`0002761c`) the fifth point (0, 0, −1), the normal;
3. calls `TexPoly_SetupRings` (`00027638`), which fills the ring tables, stores the camera's position at `+0xfb` and sets `+0x1e`: *n* = `+0x69` / `+0x6d` rings, 8, and for ring *i* the `int32` `+0x6d` · (*i* + 1) + `+0x6d` / 2 at `+0x9b`, the `int16` `0xffff` / ((`+0x6d` · (*i* + 1) · 4) / `+0x71`) at `+0x7d` and a `Math_RandomNext` word at `+0xd7`;
4. places each cloud with slot `+0x28`, `TexPoly_RandomPointOnCircle` (`00027540`), at a distance *r* = (`Math_RandomNext` & `0xffff`) << 1 from the camera: with *a* = `Math_RandomNext`, it writes the angles (0, 0, *a*) and the position (camera *x* + Q14(*r* · cos *a*), camera *y* + Q14(*r* · sin *a*), `+0x75`), cos and sin by `Math_CosBam` and the products by `Math_Q14Multiply`. The shift 1 is the number of doublings of `0x10000` before four times it reaches the field radius. Every cloud's angles go to the first triple of `+0x12`, which the draw does not read;
5. sets the wind: with *w* = `Math_RandomNext` and *s* = 50 + `Math_RandomBelow(100)`, (Q14(*s* · cos *w*), Q14(*s* · sin *w*), 0) at `+0x107`, 50 to 149 units a frame in one direction for the whole layer;
6. stores `Math_RandomBelow(3)` at `+0x65`, the frame every cloud shows from above.

### Each frame

`Clouds_DriftAndDraw` (`00027d78`), slot `+0x34` of the subclass, given the camera:

1. sets the number drawn `+0xf9` to (count · (`000bd566` + 1)) >> 8, `000bd566` being a preferences byte;
2. adds the wind to the position of each cloud drawn;
3. calls `TexPoly_DrawAtPoints` (`00027724`), `TexPoly`'s slot `+0x34`, with the camera.

`TexPoly_DrawAtPoints` makes the bank the active bitmap bank (`000bdb5e`), points `Poly_IndexList` at the 32 words from `000bdb62` that `Light_ResetSystem` sets to 0–31 and the surface list `000b6732` at `+0x5d`, and sets the poly's fields as in the table. The camera has moved when its *x* or *y* differs from `+0xfb` or `+0xff`, and *mid* is the point halfway, ((last + now) >> 1) on each axis. For each cloud, *d* being `Math_FastMagnitude2D` of its *x* and *y* less the camera's:

- **Carrying over.** When the camera has moved, a cloud whose bit 0 is clear and whose *d* exceeds `+0x69` + `+0x79` (210000) is reflected through *mid*, *x* = 2 · *mid*.*x* − *x* and likewise *y*, and its bit set; a cloud whose bit is set and whose *d* is below `+0x69` − `+0x79` (190000) has it cleared. A cloud left behind thus jumps to the same distance ahead, and the bit keeps it from jumping back until it has come nearer.
- **Drawing.** A cloud below the number drawn gets slot `+0x2c` with *d*, which does nothing (`TexPoly_Slot28NoOp`, `0002750c`, entered at its padding `0002750a`, and the first part's copy `00025a50`, each `RET 8`), `Raster_SetModelTransform` with no rotation and its position as the translation, and slot `+0x30`, `TexPoly_Render`.

It then stores the camera's position at `+0xfb`. So the panel's corners sit at the cloud's position plus (0–25000, 0–25000, 10000), at height 20000, unturned.

`TexPoly_Render` (`000279a4`) draws the panel through brush mode 5, as `Bitmap_BlitRotatedScaled` does, so a frame's palette index 0 is left unpainted and the sky drawn before shows through it ([bitmap fills](polygon-shading.md#bitmap-fills-brush-modes-5-and-7)): `Raster_FrontFacing` of the normal and the centre, point 4 read as `int16` (0, 0, −1) and point 0, picks surface entry 0 when it is positive, the camera below the panel, and entry 2 with the back flag otherwise. The entry is a frame of the active bank, its frame array at `+8`; a null frame draws nothing. The brush `{5, 4, the corners' texture points, the frame's bitmap header}`, installed at the render context's `+0x22c` and replaced by `PolyFill_FillBrush` after, maps (0, 0), (*u*, 0), (*u*, rows − 1) and (0, rows − 1) onto the four corners, *u* being columns − 1 rounded down to a multiple of 8. The corners are projected with `Poly_ProjectIndexedVertices`, clipped with `Poly_ClipRingToNearPlaneWithUV` when it reports one behind the near plane, and filled with `PolyFill_FillThenOutline`.

ES2's `TexPoly_DrawAtPoints` (`0042fa18`) also sets the depth fade to 0 and holds `004a09ac` at 1 while it walks the points; its `TexPoly_Render` (`0042fc34`) takes the bitmap brush only while `g_TexturedPolyUseBitmapBrush` is set, and otherwise draws the frame's atlas page through `Raster_SetupTexturedSpan`, and clips through a slot of its own, `TexPoly_ClipPolygon`, where ES1's calls `Poly_ClipRingToNearPlaneWithUV`; its `TexPoly_RandomPointOnCircle` (`0042f860`) shifts the two offsets after the multiply where ES1's caller shifts the distance before.

## The scattered points

**While the preferences value that textures the ground is below 240, specks of one colour dot the ground round the viewer: a single pixel far off, two side by side nearer, and a small cluster close by.** They are the world file's 250 points in the colour of its short 8 ([the scattered points](../formats/wld-world.md#the-scattered-points)), on the ground plane within 65536 of where the viewer first stood, the distance 2*r* a signed 16-bit draw doubled (`CWDE` at `00026f35`) and so nearer more often than farther. `PointScatter_Update` draws count × `000bd567` / 240 of them with `PointScatter_Draw` (`00026fe0`) while `000bd567` is below `000956be` (240), and the ground is [textured](#the-ground) only while it is above, so at 240 there is neither.

`PointScatter_Draw`, given the object and the camera:

1. clears the model transform (`Raster_SetModelTransform(0)`) and installs a pixel brush `{0, colour}` from its own stack at the render context's `+0x228`, which it leaves there when it returns;
2. the camera has moved when any of its coordinates differs from the centre `+9`, `+0xd`, `+0x11`; *mid* is ((centre + camera) >> 1) in *x* and *y*;
3. for each of the first `+7` points, *d* being `Math_FastMagnitude2D` of its *x* and *y* less the camera's: when the camera has moved and *d* exceeds 70000, the point is reflected through *mid* in *x* and *y*. It is moved into the view with `Raster_ModelToView` and drawn when its depth (the view point's `+4`) exceeds the camera's near plane `+0x1e`: `Raster_PerspectiveDivide` and `Raster_ProjectToScreen` give the pixel (*x*, *y*), and `Gfx_PlotPixel` plots (*x*, *y*) in colour *c*, short 8 (`+0x15`); when *d* is below 30000 also (*x* + 1, *y*) in *c*; when it is below 20000 also (*x* + 2, *y*) and (*x* + 2, *y* − 1) in *c* − 2 and (*x* + 1, *y* − 1) in *c* − 4;
4. stores the camera's position as the centre.

`Gfx_PlotPixel` (`00069058`) is ES2's (`Gfx_PlotPixel`, `004894e0`): it reads the brush through the canvas's `+0x224` (the render context's `+0x228`, the canvas being the context's `+4`). With mode 0 it resolves the colour by its top byte as a fill's brush does ([the colour ramps](polygon-shading.md#the-colour-ramps)) and plots through the display driver's routine at `0009dee8`; with mode 2 it hands the brush's `+4` to `0009deec`; with any other mode, or a colour it cannot resolve, it plots nothing. It moves the point by the canvas's origin when `+0x208` is set, and clips it when the canvas's `+0x204` is 1 to the rect `+0x20c`–`+0x218`, and otherwise when it is not 0 to the canvas's list of rects and span lists.

## The pitch gradient (palette entries 64–79)

`Palette_BlendRangeByPitch` (`00025fe4`), given the camera, rewrites palette entries 64 to 79 each frame while the byte `000928ec` is set. The first time, it reads from the active palette the colours of entry `000bc5ce` (64, as the load stores it), of entries short 10 (`000bc5d0`) and short 11 (`000bc5d2`), and of entry `000bc5d4` (98), and builds 16 colours from short 10's colour to short 11's with `Palette_InterpolateColours` (`000283b0`) at `000afde4`. Then every frame:

1. the step: when the `int16` at `000928ee` is negative, ((*p* + `0x2000`) & `0xffff`) >> 10, at most 15; otherwise `000928ee` itself, after which `000928ee` becomes −1;
2. the end colour: entry 98's colour while `000928eb` is set, otherwise colour number *step* of the 16. `Palette_ActivateImpact` (`00025ed0`) sets `000928eb`, `Palette_ToggleImpact` (`00025ef8`) flips it and `Palette_RestoreFromImpact` (`00025f1c`) clears it, each only while `ImpactPaletteObject` is loaded and each switching palettes through `Palette_SetActive` (`00027e7c`), as ES2's do;
3. 16 colours from entry 64's original colour to the end colour, through `Palette_InterpolateColours`, installed at entries 64–79 with `Palette_InstallRange`.

`Palette_InterpolateColours`, `Palette_GetEntry` and `Palette_InstallRange` are the palette module's ([`palette.md`](palette.md#functions)).

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `HeapPtr_DtorTheaterCopy`, `ConfigPart_DtorTheaterCopy`, `ConfigPart_GetClassTagTheaterCopy`, `TSPartBase_DtorTheaterCopy` | `00025128`, `00025150`, `0002518f`, `00025198` | the run that opens the first part; `es2_xref.py` finds no branch or stored pointer to `00025128`, and the tables' slots `+4` and `+0x1c` hold the other three, `00025198` by its padding `00025195` |
| `Clouds_Dtor`, `TexPoly_Slot28NoOpTheaterCopy` | `00025a08`, `00025a50` | slots `+0x1c` and `+0x2c` of the clouds' vtable `000928f6`, `00025a08` by its padding `00025a05`; `00025a50` is also slot `+0x2c` of `00092952` |
| `TexPoly_DtorTheaterCopy`, `TSTexture4Poly_DtorTheaterCopy`, `TSSolidPoly_DtorTheaterCopy`, `TSPoly_DtorTheaterCopy`, `TSGroupItem_DtorTheaterCopy` | `00025a54`, `00025a98`, `00025ad4`, `00025b0c`, `00025b3c` | slot `+0x1c` of the first part's copies `00092952`, `00092992`, `000929be`, `000929ea` and `00092a16` |
| `Scene_DrawTerrainPass` | `00025b68` | |
| `Palette_ActivateImpact`, `Palette_ToggleImpact`, `Palette_RestoreFromImpact` | `00025ed0`, `00025ef8`, `00025f1c` | |
| `Palette_BlendRangeByPitch` | `00025fe4` | where ES2's `Scene_DrawTerrainPass` ends with a call to an empty function |
| `HeapPtr_DtorHzlineCopy`, `ConfigPart_DtorHzlineCopy`, `ConfigPart_GetClassTagHzlineCopy`, `TSPartBase_DtorHzlineCopy` | `000260e4`, `0002610c`, `0002614b`, `00026154` | the `hzline`'s run, as the first part's; its tables are `00092a42` and `00092a76` |
| `Hzline_Dtor` | `00026208` | slot `+0x1c` of `00092aaa`, by its padding `00026205`: stores that vptr, calls `TSGroup_Dtor` with flags 0 and frees the object on bit 0 of the flags, as ES2's `Hzline_Dtor` (`0042f6d2`); the bank at `+0x50` is not freed |
| `Hzline_Draw`, `Hzline_DrawWithOffset`, `Hzline_BuildHorizon` | `00026264`, `00026280`, `00026348` | |
| `Hzline_FillSky`, `Hzline_FillGround`, `Hzline_BlitHorizonStrip` | `0002650c`, `000267d8`, `00026ac8` | |
| `HzlineFill_VertexElementCtor` | `000267bc` | the element constructor of the five 8-byte points `Hzline_FillSky` (by its padding `000267ba`) and `Hzline_FillGround` build on their stacks with `__vec_ctor`: returns the element, allocating 8 bytes when given none, and writes nothing |
| `PointScatter_Draw` | `00026fe0` | |
| `TexPoly_Ctor`, `TexPoly_RandomPointOnCircle`, `TexPoly_SetQuadAndRadius`, `TexPoly_SetFifthVertex` | `000271c0`, `00027540`, `000275dc`, `0002761c` | |
| `TexPoly_SetupRings`, `TexPoly_DrawAtPoints`, `TexPoly_Render` | `00027638`, `00027724`, `000279a4` | slots `+0x24`, `+0x34` and `+0x30` of `TexPoly`'s vtable `00092bde` |
| `TSGroupItem_DtorTexPolyCopy`, `TSPoly_DtorTexPolyCopy`, `TSSolidPoly_DtorTexPolyCopy`, `TSTexture4Poly_DtorTexPolyCopy`, `TexPoly_Dtor` | `000273c8`, `000273f4`, `00027424`, `0002745c`, `000274c8` | slot `+0x1c` of `00092b2e`, `00092b5a`, `00092b86`, `00092bb2` and `00092bde` |
| `TexPoly_PointElementCtor`, `TexPoly_Slot28NoOp`, `TexPoly_AngleElementCtor` | `00027498`, `0002750c`, `00027510` | the element constructors of `TexPoly_Ctor`'s 12-byte points and 6-byte angle triples, each returning the element, allocating 12 or 6 bytes when given none, and writing nothing; `TexPoly_Slot28NoOp` is slot `+0x2c` of `00092bde`, ES2's `TexPoly_Slot28NoOp` (`00430346`) |
| `TexPoly_RenderPointElementCtor` | `00027be4` | the element constructor of the 32 8-byte points `TexPoly_Render` builds on its stack with `__vec_ctor`, the code of `HzlineFill_VertexElementCtor` |
| `Clouds_Setup`, `Clouds_DriftAndDraw` | `00027c00`, `00027d78` | slots `+0x24` and `+0x34` of the clouds' vtable `000928f6` |
| `GndTex_Load`, `GndTex_BuildRowTable`, `GndTex_Draw` | `00028de8`, `00028fbc`, `00029120` | |
| `GndTex_StaticInit`, `GndTexRow_ElementCtor` | `000296cc`, `000296e4` | `gndtex.cpp`'s static initializer, the dword at `000adc24`, and the row table's element constructor |
| `GndTex_Alloc`, `GndTex_Free`, `GndTex_LoadTexture` | `00029714`, `000297f0`, `00029830` | |
| `GndTexBitmap_StaticInit`, `GndTexBitmap_StaticDtor` | `0002989c`, `000298a8` | the dwords at `000adc28` and `000adcb0`, among the static constructors and the exit routines ([static construction](../formats/compiler.md#static-construction-and-exit)) |
| `GndTex_AllocSelector`, `GndTex_FreeSelector`, `GndTex_DrawSpan` | `000298b8`, `000298fe`, `0002991d` | the span's unrolled steps have no Ghidra function |
| `Math_TransposeRotation2D`, `Math_BuildRotation2D`, `Transform2D_ApplyToPoint` | `00063df0`, `000694b8`, `00069448` | the three that [`fixed-point-math.md`](../simulation/fixed-point-math.md#the-assembly-library) lists as ES2's code |
| `TSGroup_Ctor`, `TSGroup_Dtor` | `000547b4`, `00054888` | `Hzline`'s base, ES2's (`004757a8`, `004757f4`): the constructor allocates `0x28` bytes when given none, stores the vptrs `000961ca`, `000961ee`, `0009621a` and `0009624e` in turn and zeroes `+0x10` to `+0x1c`; the destructor stores `0009624e`, frees `+0x10`, `+0x14` (through `__vec_delete`) and `+0x18`, calls slot `+0x1c` with flags 3 on each non-null pointer of the `+0x26` (`uint16`) in the array `+0x1c` and frees the array, stores `0009621a`, calls `TSBase_Dtor` and frees the object on bit 0 of the flags |
| `Bitmap_BlitRotatedUnpacked`, `Gfx_PlotPixel`, `Bitmap_BlitRotatedScaled` | `00067214`, `00069058`, `00076630` | |

## Globals

| | Address |
|---|---|
| the ground bitmap object, its header (rows, columns, depth, flags, size, pixels) | `000b0d88`, `000b0d8c`–`000b0d96` |
| the texture, its selector, the allocated flag | `00092c74`, `00092c72`, `00092c70` |
| the row table, 320 entries of 12 bytes | `000afe68` |
| the row table's key: *z*, pitch, rows, origin x | `00092c44`, `00092c48`, `000b0d68`, `000b0d6c` |
| the span's width, its entry, destination, fade table − 240 | `00092c80`, `00092ca8`, `00092c78`, `00092c7c` |
| the span's *u*, *v*, *du*, *dv* | `00092c8c`, `00092c94`, `00092c9c`, `00092ca4` |
| the fade tables, the distances, their count, the table count | `000b0d70`, `000b0d78`, `000b0d74`, `000b0d80` |
| short 5, half of short 0 | `000b0d7c`, `000b0d84` |
| the pitch gradient's 16 colours, its built flag, its forced step | `000afde4`, `00092a3e`, `000928ee` |

## Open

- **Open:** what the preferences panel's options 3 and 5 are called and which values they take; `0005b48a` also sets `000b5b96` to whether option 5 exceeds 240. Which option sets `000bd566`, the byte the number of clouds drawn scales by.
- **Open:** what reads `TexPoly`'s ring tables (`+0x7d`, `+0x9b`, `+0xd7`, count `+0xf5`) and the angle triples at `+0x12`; of the functions this page describes, the set-up writes them and none of the others reads them. What `dba\clouds2`'s frames 0 to 2 show, and what writes the surface entry `+0x5d`, the frame seen from below, which the constructor zeroes.
- **Open:** what draws with palette entries 64–79, which the pitch gradient rewrites, and what sets `000928ec` (`00057f89`, `00058407`) and `000928ee` (`00057e15`, `0005b3a3`).
- **Open:** what the canvas origin pair at `000afa9c` holds, and what writes it (`00022384`, `0002252c`, `0002253c`, `000243f9`, `0002e131` hold its address).
- **Open:** what `00092c4c`, which `GndTex_Load` sets to 1, is read for; `es2_xref.py` finds no reference to it besides that store.
- **Open:** what draws the second view that `000364f8` renders.
- **Open:** what `00066e34` and `00066f20` do with the ground bitmap object `000b0d88`, which `GndTexBitmap_StaticInit` and `GndTexBitmap_StaticDtor` pass them; both lie in the library region, unread.
