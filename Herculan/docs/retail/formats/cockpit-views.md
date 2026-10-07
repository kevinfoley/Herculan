# Cockpit views: the view manager, `.VUE` geometry, clip regions, video modes

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project. All addresses are DBSIM unless noted. Symbols are in `tools/ghidra_scripts/known_symbols_dbsim.json`; apply with `ES2ApplySymbolNames.java`.

Verified against retail data in `ES2/VOL/simvol0/{hb0,hb1,hb2,hba,hd0-3,ed0-3,vue,gau,dpl,dat}/`.

Canopy art itself and the cockpit palette: [`cockpit-canopy-palette.md`](cockpit-canopy-palette.md). The console and HUD widgets a view's canvas carries: [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md). The front-window gunsight complex: [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md). How a mouse click on a screen-edge widget reaches its handler: [`cockpit-input.md`](cockpit-input.md).

## Object model

| Symbol | Address | Role |
|---|---|---|
| `CockpitViewManagerInstance` | `004d2544` | Cockpit view state: current view, pending command, per-view assets. 0x37 bytes. Built in `Sim_InitMissionSession`. |
| `CockpitViewManager_Ctor` | `00429660` | Constructs the above; loads `dpl\cockpit` under a singleton guard. |
| `CockpitViewManager_LoadViews` | `00429834` | Whole cockpit bring-up (below). |
| `CockpitViewManagerPublished` | `004cfa20` | The same manager object again, stored at the tail of `CockpitViewManager_LoadViews` by `CockpitViewManager_Publish` (`00429810`) and read back by `CockpitViewManager_Published` (`00429820`). How a module that does not have the manager to hand reaches it — the message port and the joystick's `HDD VIEW` action both do. |
| `CockpitViewInstance` | `0049b088` | The GAU widget tree, owned by the manager. |
| `Gau_BuildCockpitWidgets` | `00431bf8` | Builds that tree from `gau\<HERC>.GAU` — see [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#gau-widget-tree). |

Translation units: `MECHVIEW.CPP` (view manager, `00429660`–`0042ab00`), `PANEL.CPP` (widget tree, `00431008`–`00434400`), palette module (`Palette_LoadResources` (`0043034c`)–`00430e40`).

`CockpitViewManagerInstance` fields:

| Offset | Contents |
|---|---|
| `+0x00` | View count, from `.VUE` (4 in every retail file) |
| `+0x04` | `.VUE` records: `viewCount x 32` bytes |
| `+0x08` | Canopy bitmap handles, 4 pointers |
| `+0x0c` | Clip-region blocks, `4 x 0x204` bytes |
| `+0x10` | Per-scanline span tables, 4 pointers |
| `+0x14` | Current view index (0-4, -1 before first switch) |
| `+0x18` | Pending view command (-1 = none) |
| `+0x1c` | View-change flag: set by `CockpitView_ProcessViewCommand`, cleared by `CockpitView_StepViewTransition`; the message ports' paints test it |
| `+0x1f` | `CockpitViewInstance` |
| `+0x29` | Owning mech object |
| `+0x2d` | Herc model name, 8 chars |

### `CockpitViewManager_LoadViews` sequence

1. Load `vue\<HERC>`: `int32 viewCount`, then `viewCount x 32`-byte records.
2. Allocate the four per-view slot arrays above.
3. Per view `i`: `CockpitClipRegions_Load` on `ed<i>`/`hd<i>`, then `ClipRegions_BuildScanlineSpans`; and unless `CockpitArt_LoadOnDemand`, `CockpitCanopy_LoadViewBitmap` for `db<i>`/`hb<i>` — see [`cockpit-canopy-palette.md`](cockpit-canopy-palette.md#canopy-art--hb0hb1hb2-and-db0db1db2).
4. Build `CockpitViewInstance` (`CockpitView_Ctor`, `00431008` → `ColorSchemePanels_LoadAll`) and `Gau_BuildCockpitWidgets`.
5. Install the per-herc cockpit colour scheme — see [`cockpit-canopy-palette.md`](cockpit-canopy-palette.md#palette).
6. Install `IMPACTCP.DPL`'s same-index scheme into the secondary palette `DAT_0049aef8` for the damage flash — see [`cockpit-canopy-palette.md`](cockpit-canopy-palette.md#the-damage-shake).

## Views

Four views, indexed 0-3, plus 4 = external/no-cockpit.

| View | Canopy bitmap | Blit flags | 3D viewport | Canvas origin | Clip file |
|---|---|---|---|---|---|
| 0 forward | `DB0`/`HB0` | 0 | full | `(0,0)` | `ed0`/`hd0` |
| 1 heads-down | `DB1`/`HB1` | 0 | **empty**, except RAZOR | `(0,237)` | `ed1`/`hd1` (stub, except RAZOR) |
| 2 glance | `DB2`/`HB2` | 0 | narrower | `(+320,0)` | `ed2`/`hd2` |
| 3 glance, opposite | `DB2`/`HB2` | **2 = mirror X** | full | `(-320,0)` | `ed3`/`hd3` |
| 4 external | none | — | full | — | default block at `DAT_004cfb1c` |

Views 2 and 3 share one bitmap handle: `CockpitCanopy_LoadViewBitmap` maps view to file index as `view > 2 ? view - 1 : view`, and after loading file 2 stores the same handle in slot 3. View 3 is drawn horizontally mirrored. There is no separate mirrored asset.

**A glance does not turn the camera.** It keeps the forward view's orientation and focal length, and shows the image plane continued sideways — see [The side glances are one image plane](#the-side-glances-are-one-image-plane). The RAZOR's heads-down view continues the same plane downward; see [The RAZOR's heads-down view](#the-razors-heads-down-view).

### View switching

- `CockpitView_QueueViewCommand` (`0042a3f4`) latches a command at `+0x18`, gated on the current view.
- `CockpitView_ProcessViewCommand` (`0042a4c4`) executes it.
- `CockpitView_SetView` (`0042a1f0`) does the work: `CockpitView_ApplyViewState`, then one `Bitmap_Blit` of the canopy at `(0,0)` (see [`cockpit-canopy-palette.md`](cockpit-canopy-palette.md#blitting)), then `CockpitView_InvalidateWidgets` (`004316c0`) invalidates every cockpit widget.
- `CockpitView_ApplyViewState` (`00429e60`) copies the view's `0x204`-byte clip block into the render context (`DAT_006c5ff4 + 4`), sets that context's clip mode (`+0x208`) to **2** — the region-list mode, which makes even sprite blits follow the cutout scanline by scanline — points `ActiveScanlineClipSpans` at its span table for the polygon rasterizers, and installs the `.VUE` rect into context slots `0x84`-`0x89`. Only the target box is drawn through this context; see [`hud-target-indicator.md`](hud-target-indicator.md).

**The canopy is blitted once per view change, not per frame.** The 3D scene is then rasterized over it every frame, span-clipped to `ActiveScanlineClipSpans`; HUD widgets repaint on top.

Command values latched at `+0x18`, and the current-view gate each requires:

| Command | Gate (current view) | Effect |
|---|---|---|
| 0 | 0 | pan down one view — forward → heads-down |
| 1 | 1 | pan up one view — heads-down → forward |
| 2 | not 4 | external view |
| 3 | 4 | return from external |
| 4 | 0 (or 3 → 6) | glance to view 2 |
| 5 | 0 (or 2 → 6) | glance to view 3 |
| 6 | 2 or 3 | return from a glance to view 0 |

Two device paths reach those commands. `CockpitView_HandleEdgeTrigger` (`00433a88`) answers a **mouse click on one of three screen-edge strips** — `+0x21a` the bottom one, `+0x21e`/`+0x222` the left and right — picking the command by current view; they are ordinary widgets, and [`cockpit-input.md`](cockpit-input.md#10-the-screen-edges-are-three-widgets) has their rects and the full mapping. `CockpitView_PollViewDevice` (`00432b14`) reads four device-state bytes at `+0x1e`-`+0x21` for commands 1/0/5/4, the joystick hat's up/down/left/right. The manual binds `[F7]`/`[F8]` to heads-down, `[F9]`/`[F10]` to the left and right windows and `[Esc]` to the way back.

### Heads-down pan — `CockpitView_StepViewTransition` (`0042a9c0`)

Called once per frame from `Sim_EndFrame` (`0045fa98`), immediately before `CockpitView_ProcessViewCommand`. A view change spans three frames:

1. A key calls `CockpitView_QueueViewCommand`, latching `+0x18`.
2. `CockpitView_ProcessViewCommand` installs the destination view's clip block and canvas origin on the back page (via `CockpitView_ApplyViewState`, no blit) and sets the transition flag `+0x1c`.
3. `CockpitView_StepViewTransition` runs the whole slide, then `+0x14 += 1` (or `-1`), `+0x18 = -1`, and `+0x1d = 2` — a two-frame cooldown that `CockpitView_ProcessViewCommand` decrements and returns on before doing anything else.

The slide itself, for commands 0/1:

```
travel = vue[dest].canvasOriginY - vue[src].canvasOriginY     -- 237, or 474 in the 640x480 modes
for (i = 0; i < travel; i += 10)
    displayOriginY += 10
    Display_SetOrigin(page, {x, displayOriginY})              -- g_RasterRoutines slot 15
    Display_Present()                                         -- scroll-window path only
displayOriginY += travel - i                                  -- final remainder step
```

The present after each step is what puts the intermediate positions on screen, and only the scroll-window path makes it; see [Presentation](#presentation).

Step is 10 canvas rows; `maybe_CockpitLayoutMode == 2` doubles it and forces travel to `0x1e0`. The side-glance commands (4/5/6) use step `0x14` and scroll on x instead.

**There is no timing in this loop**. Its real-time duration is whatever the host CPU makes it, and only a step *count* is recoverable: 24 steps in mode 0, 48 in modes 1/2, since the step is in device rows and the coord shift doubles the travel.

**Both views' canopies are resident in the canvas throughout.** `Sim_InitMissionSession` (`004614fc`) calls `CockpitView_SetView(mgr, 1)` and then `CockpitView_SetView(mgr, 0)` during bring-up, so the pan is a pure scroll and never a redraw. That order also settles the six-row overlap where the two blits meet — `.HB1` lands at canvas row 474 and `.HB0` runs to 479, so **`.HB0` wins**.

## `.VUE` — per-view geometry

After the 9-byte VOL prefix: `int32 viewCount`, then `viewCount x` 8 `int32`s. All coordinates are authored in the 320-wide space and shifted by `VideoMode_X/YCoordShift`.

| Field | Meaning |
|---|---|
| 0-3 | 3D viewport rect `x0, y0, x1, y1` |
| 4-5 | Projection centre, `cx, cy` — **stored negated**, see below |
| 6-7 | Canvas origin `originX, originY` |

The rect is the **outer bound** on where the 3D scene may reach, and the `.HD<n>` scanline spans below are the canopy-shaped hole inside it: two mechanisms over one view, both applied to the whole 3D pass. The two agree on retail data (`APOCA.HD0` resolves to rows 0-371 against a rect of `0,0 - 640,372` in the 640-wide modes), so the rect changes nothing that is visible on a herc whose canopy is opaque outside its rect — which is what makes the spans sufficient on their own and the rect easy to miss.

The two glances share a canopy bitmap but not a rect: view 3's runs the full width where view 2's stops short of it, on every retail herc.

Every retail `.VUE` gives view 1 the canvas origin `(0,237)` — no herc differs.

`APOCA.VUE` (`viewCount = 4`):

| View | Rect | Centre | Canvas origin |
|---|---|---|---|
| 0 | `0,0 – 320,186` | `-160,-95` | `0,0` |
| 1 | `0,0 – 0,0` | `-160,-95` | `0,237` |
| 2 | `0,0 – 287,231` | `-160,-95` | `320,0` |
| 3 | `0,0 – 320,231` | `-160,-95` | `-320,0` |

View 1's zero-size rect is why the heads-down view shows no 3D. **RAZOR is the sole exception** — `0,0 – 320,181`, matching its 2368-byte `.HD1` against every other herc's 16-byte stub; see [The RAZOR's heads-down view](#the-razors-heads-down-view).

`RAZOR.VUE` (`viewCount = 4`):

| View | Rect | Centre | Canvas origin |
|---|---|---|---|
| 0 | `0,0 – 320,239` | `-160,-146` | `0,0` |
| 1 | `0,0 – 320,181` | `-160,-146` | `0,237` |
| 2 | `0,0 – 320,239` | `-160,-146` | `320,0` |
| 3 | `0,0 – 320,239` | `-160,-146` | `-320,0` |

### The projection centre is not the middle of the view

Fields 4-5 are where the view axis lands on screen, and `Raster_ProjectToScreen` (`0048c5c4`) is the projection's last step: `screenX = x + centreX`, `screenY = centreY - y`. Anything running straight away from the eye — a beam leaves its muzzle parallel to the view axis — vanishes at that point, and it is where the gunsight reticle is drawn. **It is not the centre of the viewport rect, and not the centre of the view window.** APOCA's is 95 rows down a 240-row view, 45 above the window's middle.

The value reaches the projection in two steps:

1. `CockpitView_ApplyViewState` (`00429e60`) copies the record's first six ints into the render context at `+0x210..+0x224`, then adds the view's canvas origin into the last pair.
2. `Raster_InstallViewProjection` (`0048c1d8`) computes `centre = rectTopLeft - thatPair`, where the rect is the one at `+0x210` — the `.VUE` rect, in the view's own window coordinates.

Every retail rect starts at `(0,0)`, so the centre in a view's own window is `-(c + canvasOrigin)`. For the forward view the origin is `(0,0)` and this is `(-cx, -cy)` authored — `(160, 95)` for APOCA. Retail `cy` runs 95 (APOCA, RAPTOR2) to 146 (RAZOR); `cx` is 160 for every herc and every view, and all four views of a herc carry the same pair. In every other view the origin stays in the sum, and it moves the centre off the view's own window to the point on the canvas where the forward view's centre is.

### The side glances are one image plane

For the glances the canvas origin does not cancel. View 2, origin `+320`, gets its centre at x = `160 - 320 = -160` authored — 160 columns left of its own window, which is exactly where the forward view's centre sits when the forward window is placed immediately left of it on the canvas. View 3, origin `-320`, gets `160 + 320 = 480`, the same point seen from the other side. With the same focal length and no change of orientation (the yaw turn in `CockpitView_ProcessViewCommand` does not run; see [Rejected readings](#rejected-readings)), the forward view and both glances are three windows onto **one** perspective image 960 columns wide authored: the glances are the forward view's image plane continued sideways, not cameras turned to face sideways. That is why the retail side views stretch towards their outer edges the way a very wide lens does.

### The RAZOR's heads-down view

`Sim_RenderFrame` (`0045fb9c`) draws the world into the current view whenever `CockpitView_ShowsWorld` (`0042db18`) returns 1: when the view's `.VUE` rect bottom is at least 1, or a view transition is running. Only RAZOR's view 1 passes the first test, so the RAZOR is the one herc whose heads-down display has the world behind it. It shows through the `.HD1` spans, in the `hd` file's device pixels: the two top corners (rows 0-27 on the left, 0-29 on the right) and a window low on either side (rows 100-362, within columns 0-79 and 558-639).

Its centre is `0 - (-146 + 237) = -91` authored rows, 91 rows above the view's window and at canvas row 146, where the forward view's centre is. Commands 0 and 1 leave the view object's angles alone, so the camera keeps the forward orientation and focal length, and the heads-down view is the forward view's image plane continued downward: the rect's top row is `atan(91/256)` = 19.6° below the view axis and its bottom row `atan(272/256)` = 46.7°.

### The view's focal length

`Raster_InstallViewProjection` (`0048c1d8`) also installs, from the same view struct: `+0x1a` the perspective shift (`(width << shift) / z` is the whole of the divide), `+0x1e` the near plane, `+0x22` the orthographic divisor. `2^shift` is the focal length in pixels, which fixes the field of view against the view's row count. `Sim_InitMissionSession` (`004614fc`) picks the shift as 9 when the back buffer's width (`DAT_004d30c4`, a copy of `VideoMode_BackBufferWidth`; see [Video modes](#video-modes)) reaches 1201 and 8 otherwise, and passes it as the third argument of `View_Ctor` (`0048bc98`), which stores it at `+0x1a`. The constructor's other fields: render target `+0x16`, near plane `+0x1e`, and through `View_CtorBase` (`0048bb64`) the position `int[3]` at `+4` and three `short` angles at `+0x10`. Both work out to the same angle — 256 px across a 240-row view, 512 across a 480-row one, 50.2 degrees vertical.

### Cockpit canvas

`CockpitCanvasWidth`/`Height` (`004d25d2`/`004d25d6`) are 320x480 in mode 0 and 640x960 in modes 1/2 — taller and wider than the 3D viewport (`004d25c2`/`004d25c6` = 320x240 / 640x480). The canvas is a virtual space the views window into at their `.VUE` origins: rows 0-239 the forward cockpit, rows 237-476 the heads-down display, x ±320 the side views.

**No retail `.GAU` uses more than the forward quadrant.** Widget origins across all nine hercs span `x:[3..298] y:[1..230]`, so the screen size the `.GAU` header declares at offset 8, (320,400), overstates the used range and the side views have no widgets of their own.

## `.HD0`-`.HD3` / `.ED0`-`.ED3` — 3D-viewport clip regions

`CockpitClipRegions_Load` (`0042dcf0`). Layout after the 9-byte VOL prefix, all fields little-endian `int16`:

```
int16 rectCount
rectCount x { int16 y0, int16 y1, int16 x0, int16 x1 }      -- inclusive on all four edges
int16 blockCount
blockCount x {
    int16 firstRow, int16 rowCount,
    rowCount x { int16 xStart, int16 xEnd }                 -- one entry per scanline, inclusive
}
```

Coordinates are shifted by the caller's `(xShift, yShift)`: `(0,0)` for the `.HD*` set (already 640-wide), `VideoMode_X/YCoordShift` for `.ED*`. Inclusive ends are expanded as `end = (end << shift) + (1 << shift) - 1`. Output is a `0x204`-byte block: `int count` plus up to 128 region pointers, rects tagged type 0 and span blocks type 2.

`ClipRegions_BuildScanlineSpans` (`0048b9a8`) flattens that into a table of `0xf0 << VideoMode_YCoordShift` rows (240 or 480), each `{ int spanCount, ptr to spanCount x {int start, int length} }`, sorted by start, and stores it in `ActiveScanlineClipSpans` (`004a5b10`). The polygon rasterizer (`00468310`) indexes it by row and skips rows with zero spans.

**This is the viewport cutout mechanism.** DBSIM never colour-keys the canopy art, and palette index 0 has no special meaning in it.

Blocks may overlap and repeat — `APOCA.HD0` lists `row 204 +168` twice — which is harmless because flattening accumulates every region per row.

Parsed extents (`hd0`/`hd2`, all nine hercs; span counts after flattening):

| Herc | hd0 rows/spans | hd2 rows/spans | hd1 spans |
|---|---|---|---|
| APOCA | 372 / 666 | 462 / 538 | 0 |
| COLOSSUS | 350 / 694 | 407 / 424 | 0 |
| SAMSON | 352 / 762 | 436 / 446 | 0 |
| MAVERICK | 450 / 1050 | 442 / 495 | 0 |
| OGRE | 388 / 734 | 447 / 467 | 0 |
| OUTLAW | 392 / 768 | 480 / 480 | 0 |
| RAPTOR2 | 334 / 720 | 434 / 477 | 0 |
| RAZOR | 480 / 948 | 480 / 630 | **584** |
| TOMAHAWK | 380 / 958 | 430 / 430 | 0 |

Every file consumes its whole body under this layout with 3 constant trailing bytes unread. RAZOR is the only herc with a non-stub view-1 file, matching the file sizes on disk (`APOCA.HD1` is 16 bytes, both counts zero; `RAZOR.HD1` is 2368). `APOCA.HD0` resolves to rows 0-371, matching the independently measured index-0 bounding box on `APOCA.HB0` (`y:[0..371]`).

Every rect in every retail file has `x0 == 0`. This matters because DBSIM's flattening step feeds a rect's fourth field to the rasterizer as a span *length* (`piVar4[1] = piVar1[3]`, against `end - start + 1` for span blocks) while the loader's own shift arithmetic treats it as an inclusive end. With `x0 == 0` the two readings differ by one column at the right edge and nothing else; `CockpitClipRegions` takes the inclusive reading.

## Video modes

`VideoMode_Configure` (`0045e4f4`) sets the whole block from a mode argument.

| Mode | `VideoMode_PanelMode` (`004d25bb`) | `VideoMode_UseHiResBanks` (`004d25f0`) | Viewport | Canvas | Back buffer | Coord shifts |
|---|---|---|---|---|---|---|
| 0 | 0 | 0 | 320x240 | 320x480 | 640x480 | 0 |
| 1 | 3 | 0 | 640x480 | 640x960 | 1280x960 | 1 |
| 2 and up | 3 | 1 | 640x480 | 640x960 | 1280x960 | 1 |

The back buffer is `VideoMode_BackBufferWidth`/`Height` (`004d25ca`/`004d25ce`), twice the viewport both ways; see [Presentation](#presentation).

**The argument is the player's only on the command line.** The first call — `WinMain`'s, passing 0 — discards what it was given and reads `data\prefs.cfg` instead, taking option 4 and mapping it to **0 for a stored 1 and 3 for anything else**, so the file reaches mode 0 or the last row and never the middle one. That first call also latches a once-only gate, so the later `-v<n>` call keeps its own argument, and `-v1` is the only way to the low-res banks at 640x480. See [`../simulation/preferences.md`](../simulation/preferences.md#the-video-mode-and-full-screen-bytes).

Both mode flags are fields of one `0xc3`-byte global block at `004d2540`, which `MAIN.CPP`'s static initializer, `Main_StaticInit` (`0045cad8`), zeroes with `memset` and then fills through `EBX`. Borland's `_INIT_` table reaches it at `004a7b70`, a priority-`0x20` entry like every other source file's; it has no direct caller. Its first dword is the main render target. The block is one global object, of a class the Borland type descriptor at `0045cc07` names `MGlobs`: `0xdd` bytes, the `0xc3` bytes of plain fields the `memset` covers and a member object at `+0xc3` (`004d2603`) that `GLBitmap_Ctor` (`00481460`) constructs first. Its destructor `MGlobs_Dtor` (`0045cc53`) and the `_EXIT_` routine `Main_StaticDtor` (`0045cbf0`, reached from `004a7c50`) touch only that member. Eighteen functions hold the block's base, in a register or as a pushed argument, and reach its fields by displacement, so a field of this block is never settled by a search for its absolute address. Those eighteen are every holder in the image: of the 63 dwords in the PE equal to `004d2540`, 62 are their loads and pushes and the 63rd is the descriptor's pointer to the object.

`VideoMode_PanelMode` is a three-valued selector, not a flag. Retail stores only 0 and 3: `Main_StaticInit` writes 0, and `VideoMode_Configure`'s three branches write 0, 3 and 3 (modes 0, 1 and 2+). `PanelMode == 3` selects `.HFN` fonts, `hba\` sprite banks, `hb<n>` canopy art and `hd<n>` clip files. Value 1 is a display mode the shipped game cannot enter; see [Panel mode 1](#panel-mode-1). `UseHiResBanks` separately selects hi-res banks for `hudhtick`, `mfd`, `radar`, `hdd`, `pweapons`, `wpn_dmg`, `weapons`, `pdg`, `bases`, `vehicles`, `flyers` and the alert banks — which is why two different flag idioms appear at the bank load sites.

**`maybe_CockpitLayoutMode` (`004d25bc`) holds zero on every path found** ([Open](#open)). It is byte `+0x7c` of the block, and the one write found is `Main_StaticInit`'s store of 0. All 23 absolute occurrences of `004d25bc` are `MOVSX` reads. Over the eighteen functions that hold the block's base, `es2_fieldscan.py` finds one other access to `+0x7c`, a read in `Sim_InitMissionSession`, and finds `Main_StaticInit`'s store as its positive control. The two values it is tested against would select: value 1, the defective path described in [`cockpit-canopy-palette.md`](cockpit-canopy-palette.md#known-defect-in-the-retail-code), and value 2 would route the canopy blit through `Bitmap_BlitScaled` (`004816bc`) and put the view origin in `DAT_004d25da`/`de` rather than `DAT_004cfa24`/`28`. **So `DAT_004d25da`/`de` stay zero** with it: the stores found, in `CockpitView_SetView` at `0042a1d8`/`0042a1e1`, are on the value-2 path, and the offset `Widget_OnMouseDown` and `Widget_OnMouseUp` add from them ([`cockpit-input.md`](cockpit-input.md#10-the-screen-edges-are-three-widgets)) is zero.

### Presentation

DBSIM draws everything into a system-memory back buffer and copies a viewport-sized window of it to the screen. `Screen_CreateBackBuffer` (`00464c40`) creates the buffer as a DIB section of `VideoMode_BackBufferWidth` x `Height` (pixels at `BackBuffer_Pixels`, `004d30ac`) and wraps it in a render target named `BMP_8`, which is raster driver 3. `VGA_8` and `VGA_4` name drivers 1 and 0, and their entries in the driver list carry a null routine table, so driver 3's is the only one `RasterDriver_InstallRoutines` can install.

| Symbol | Address | Role |
|---|---|---|
| `Display_SetOrigin` | `004648d4` | Slot 15 of `g_RasterRoutines` (`004a5800`), through driver 3's stub `Driver3_SetOrigin` (`00489802`). Moves the window to `(x, y)`: rebases the render target's pixel pointer and row table on it and stores it in `Display_OriginX`/`Y`. Its page argument is ignored |
| `Display_OriginX` / `Display_OriginY` | `004d309c` / `004d30a0` | The window's top-left in the back buffer. Zeroed by driver 3's surface setup `Driver3_OpenDisplay` (`0048a0c8`); every other store is `Display_SetOrigin`'s |
| `Display_ScreenRect` | `004d307c` | `{0, 0, w-1, h-1}`, the viewport on screen, set by `Driver3_OpenDisplay` (`0048a0c8`) |
| `Display_Present` | `00464910` | `Screen_PresentFrame(&Display_OriginX, &Display_ScreenRect)` |
| `Display_PresentRect` | `00464924` | The same for one rect of the view, offset by the render context's origin when its clip mode `+0x20c` is set |
| `Screen_PresentFrame` | `00465524` | Copies a source point's rect of the back buffer to a screen rect. Fullscreen: locks the DirectDraw primary (or, below 640 wide, the back surface, then flips), copies row by row and draws the software cursor. Windowed: `StretchBlt` from the DIB's DC |

`Display_UseScrollWindow` (block `+0xaa`, `004d25ea`, a word) selects between two ways of getting frames on screen.

**Nonzero is the scroll-window path**, and every retail launch takes it: `Main_StaticInit` and each branch of `VideoMode_Configure` store 1. `Sim_EndFrame` calls `Display_Present` once a frame, and the view slides present after every step. A view change moves the window rather than redrawing: `CockpitView_ApplyViewState` shifts `Display_OriginX` by the difference between the two views' canvas x origins and puts `Display_OriginY` at the destination's canvas y. The back buffer is only two viewports wide, so when a glance's target would fall outside it, `CockpitView_ProcessViewCommand` first copies the viewport into the other half with `Region_CopyBetweenPages` (`00487d54`) and moves the window with it: for command 4 when `Display_OriginX` is nonzero, for command 5 and a return from view 2 when it is 0, and for a return from view 3 when it is nonzero. `CockpitView_SetShakeBand` rests the shake band on `Display_OriginX`/`Y`.

### The `-b` paged path

`Sim_ParseCommandLine` runs after `WinMain`'s `VideoMode_Configure` and `-v` pre-parse, and its `-b` case stores 0 in `Display_UseScrollWindow`. That selects a page-flipping scheme. The render target's `+0x88`/`+0x8c` hold a pair of page indices, and it keeps a source, a draw and a visible page at `+0x38`, `+0x3c` and `+0x34`, each pushed to the driver through `g_RasterRoutines` slots 16, 17 and 18 (`004a5804`-`004a580c`). Its page origins are four points at `+0x48`: `Screen_CreateBackBuffer` sets them to y = 0, 200, 400 and 600 for a `VGA_8` target and zeroes them for anything else. The pair starts at `{0, 1}`.

| Function | Under `-b` |
|---|---|
| `Sim_EndFrame` (`0045fa98`) | Makes `+0x8c` the visible page and `+0x88` the draw page, swaps the pair, and does not call `Display_Present` |
| `Sim_InitMissionSession` (`004614fc`) | After bring-up calls `Widget_DrawToCockpit(1, {0, 0, 320, 400})` in place of `Display_Present` |
| `CockpitView_ProcessViewCommand` (`0042a4c4`) | Gates glances on the draw page `+0x88` instead of the window: command 4 and a return from view 3 wait for page 0, command 5 and a return from view 2 for page 2 (1 under [panel mode 1](#panel-mode-1)). A gated command returns with `+0x18` still latched. Every pass that gets past the gate and the cooldown calls `Vga_WaitVerticalRetrace` (`0045c61c`), which spins on VGA port `0x3DA` until bit 3, vertical retrace, is set |
| `CockpitView_ApplyViewState` (`00429e60`) | Sets the origin to the draw page's origin plus the destination's canvas origin, and leaves it alone for views 2 and 3 |
| `CockpitView_StepViewTransition` (`0042a9c0`) | For commands 0, 1 and 3 first copies the outgoing image across the pages by the difference between the views' canvas origins; presents no step; ends a glance by swapping the page pair and putting the origin back on the page origin |
| `Widget_DrawToCockpit` (`0043122c`) | Acts only here. Copies a widget's rect from one page to the other — `+0x8c` to `+0x88` for a first argument of 1, the reverse for 0 — and does nothing for argument 1 while bring-up's `DAT_004d25ae` is set, for a rect `CockpitView_RectCornerVisible` (`00431410`) rejects, or while a view transition is armed. The offset is `Cockpit_ZeroOffset` (`0049b07e`), `{0, 0}` in the image, whenever the third argument is null, and all 34 calls pass null |
| `CockpitView_SetShakeBand` (`0042d2f8`) | Rests the band on the view's canvas origin (`DAT_004cfa24`/`28`) and, in views 2 and 3, zeroes the resting x and the saved resting y (`004cfae4`, `004cfae0`) |
| `AlertPanel_Present` (`00454ab0`), `AlertPanel_Leave` (`004548ac`) | Do not present |
| `PanelButton_Paint`, `ControlsPanel_RefreshRow`, `PreferencesPanel_Run` | Wrap their painting in `g_RasterRoutines` slots 31 and 30 (`004a5840`/`004a583c`), the pair `Cursor_SyncPosition` calls around a pointer move |
| `AlertPanel_Leave`, `AlertPanel_LoadCursor` (`00454b70`), `AlertPanel_RestoreCursorImage` (`00454c10`), `Cockpit_UpdateEdgeCursor` (`00433c54`), `Cockpit_SetForwardCursor` (`00433d90`), `Cockpit_SetOffForwardCursor` (`00433e3c`), `Cockpit_UseOffForwardCursor` (`00433ee8`), `Cockpit_UseForwardCursor` (`00433f7c`) | Call `Cursor_SyncPosition` or `maybe_Cursor_SetImage` (`00486d64`) in place of driver 3's `Driver3_SyncCursorPosition` (`0048982e`) or `maybe_Driver3_SetCursorImage` (`00489822`) |

The block-base scan finds the rest of the readers: over the eighteen holders, `es2_fieldscan.py` reports three reads of `+0xaa`, all in `Sim_InitMissionSession`, and `Main_StaticInit`'s store.

**Driver 3 implements none of the paging.** In its routine table slots 16, 17 and 18 are the empty stubs `Driver3_Slot16NoOp` (`004897f0`), `Driver3_Slot17NoOp` (`004897f6`) and `Driver3_Slot18NoOp` (`004897fc`), and slots 30 and 31 are `Driver3_Slot30NoOp` (`004897c8`) and `Driver3_Slot31NoOp` (`004897d3`), which save and restore registers and return. `maybe_Cursor_SetImage` and `maybe_Driver3_SetCursorImage` are empty; `Driver3_SyncCursorPosition` (`0048982e`) is `Cursor_SyncPosition` again, calling `Driver3_Slot30NoOp` directly instead of through slot 30. Slot 20, the rect copy `Region_CopyBetweenPages` (`00487d54`) ends in, `Driver3_CopyRect` (`0048a69b`), moves pixels within the one DIB by the offset it is given and returns at once when the offset is zero, so every `Widget_DrawToCockpit` copy moves nothing. The page indices reach only those stubs and the page-origin lookups, and the origins are zero.

What that leaves:

- **Nothing presents during a mission.** Of `Display_Present`'s 29 call sites, 25 are scroll-window only: 21 in `CockpitView_StepViewTransition` and one each in `AlertPanel_Present`, `AlertPanel_Leave`, `Sim_EndFrame` and `Sim_InitMissionSession`. The rest are the mission loading screen (`Sim_ShowLoadingScreen`, `00461344`), the preferences panel's per-pass `PreferencesPanel_Present` (`00457180`), and `MainWndProc`'s `WM_PAINT` (windowed only) and `0x812` display-change handlers. The main loop's only other per-frame hook, `Subsystem_RunPhase(6)`, runs the input-tape flush `00401e5c` and nothing else.
- **The pages are only ever 0 and 1**, so the gates waiting for page 2 never open: command 5 and a return from view 2 stay latched at `+0x18`, and `CockpitView_QueueViewCommand` takes no new command while one is. The only reset of `+0x18` that `es2_fieldscan.py` finds in the view module is `CockpitView_StepViewTransition`'s.
- **`Vga_WaitVerticalRetrace` executes `IN AL,DX` from user mode** on every pass of `CockpitView_ProcessViewCommand` that is not cooling down or gated. `es2_xref.py` finds its one caller at `0042a726`.

**`-S` has no effect.** Any `-S` argument other than exactly `-SPRUNKNOWN`, which toggles the developer keys instead ([`../command-line.md`](../command-line.md#-sprunknown-the-developer-keys)), sets `CmdLineSwitch_S` (`004d254b`, block `+0x0b`) to 1. Its only reader is `Sim_InitMissionSession`, which builds the pair `{0, count}` from it — count 1 or 0 under [panel mode 1](#panel-mode-1), 2 or 0 on the paged path, 0 otherwise — in the stack slots `[EBP-0xa4]`/`[EBP-0xa0]`, and no instruction in the function reads them back. The only address-taken local nearby is `View_Ctor`'s 6-byte angle triple at `[EBP-0xb8]`, which does not reach them. The instruction after the pair is `CMP word ptr [EDI+0xaa],0` at `004619f7`, a `Display_UseScrollWindow` test that nothing branches on. The counts are the page numbers `CockpitView_ProcessViewCommand`'s paged glance gate waits for; see [Open](#open).

### Panel mode 1

The code tests `VideoMode_PanelMode` against 1 at 17 sites, 10 of them `!= 1`. Nothing stores 1: besides the four stores above, the only accesses to `+0x7b` through the block's base are reads, in `Sim_InitMissionSession` and in the three blit helpers `VideoMode_BlitBitmap` (`0045c8a0`), `VideoMode_BlitBitmapFlags` (`0045c948`) and `VideoMode_BlitBitmapSized` (`0045c9f4`), which compare it with 3.

| Function | Sites | Under value 1 | Path |
|---|---|---|---|
| `CockpitView_ProcessViewCommand` (`0042a4c4`) | 4 | A glance adds ∓`0x3600` (~76°) to the view object's yaw on commands 4/5 and takes it back on 6 | Live; only the value keeps it off |
| `CockpitView_ProcessViewCommand` | 1 | The glance gate's second page is 1 instead of 2 | The [`-b` paged path](#the--b-paged-path) |
| `Sim_EndFrame` (`0045fa98`) | 2 | Selects the render target at block `+0xac` (`DAT_004d25ec`) around the view transition, then the main one at block `+0` again | Live. Only `Main_StaticInit`'s `memset` writes `+0xac`, so the target is null in this build |
| `Sim_InitMissionSession` (`004614fc`) | 1 | Picks the discarded page count from `CmdLineSwitch_S` as 1 or 0 | Live, with no effect |
| `CockpitView_StepViewTransition` (`0042a9c0`) | 9 | Each copy rect is `0x140 x 0x1e0` (320x480, the whole mode-0 canvas) instead of `0x140 x 0xf0` (one view) | Every site is on the paged path or under `maybe_CockpitLayoutMode == 2` |

Taken together, value 1 keeps the cockpit canvas in its own off-screen surface with one display page fewer, and turns the camera for a glance instead of scrolling the canvas sideways. The retail modes do the opposite on both counts: the canvas lives in the back buffer around the viewport window (see [Presentation](#presentation)), and a glance is the forward image plane continued (see [The side glances are one image plane](#the-side-glances-are-one-image-plane)).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A glance turns the pilot view ∓`0x3600` (~76°) to face sideways. | `CockpitView_ProcessViewCommand` (`0042a4c4`) does add ∓`0x3600` to the view object's yaw on commands 4/5 and undoes it on 6, and the decompile reads that way at a glance. Every one of those adds is gated on `VideoMode_PanelMode == 1`, which nothing stores; see [Panel mode 1](#panel-mode-1). A retail glance keeps the forward orientation. |
| `VideoMode_PanelMode` is a flag for the hi-res art set. | It holds three distinct values. 3 selects the art set, and 17 sites test for a 1 that belongs to a scrapped display mode. |
| Nothing can write a byte of the video-mode block through a pointer, because no address inside `004d2580`-`004d2602` appears as an immediate. | The block starts at `004d2540`, and that base does appear: `Main_StaticInit` loads it into `EBX` and writes `+0x7b` and `+0x7c` through it, and eighteen functions in all hold it. |
| `-b` selects a software scroll: the same game, with the view slides unanimated. | The flag's other value is the scroll-window path, which suggests two ways of scrolling. `-b` is a page-flipping scheme written against paged VGA targets, and in this image its pages, page flips and page copies all land on driver 3's empty stubs, nothing presents a mission frame, and its glance gate waits for a page that never comes up; see [The `-b` paged path](#the--b-paged-path). |
| The canvas origins of the glances and the heads-down view cancel out of the projection centre, so each view is centred in its own window. | The subtraction in `Raster_InstallViewProjection` is against the `.VUE` rect's top-left, which is view-local and `(0,0)` for every view, not against the view's canvas origin. The origin stays in, and it moves each of those views' centre off its own window to the forward view's reticle. |

## Open

- **Open:** what display [panel mode 1](#panel-mode-1) was for, and which viewport and canvas it ran at. `VideoMode_Configure` has no branch that sets it, so nothing records those. Values 0, 1 and 3 also fit a two-bit field where the high bit requires the low one, but no site tests a single bit.
- **Open:** whether the pair `{0, count}` that `CmdLineSwitch_S` selects is the render target's `+0x88`/`+0x8c` page pair, installed by a call the build dropped. The counts match the paged glance gate's page numbers and the pair's first value matches the page it starts on, and `004619f7` tests `Display_UseScrollWindow` with no branch on the result; nothing in the image stores the pair. On that reading `-S` would select a single page.
- **Open:** `-b` against retail. On Windows NT-family systems, including the Windows 11 setup, the expected result is a privileged-instruction fault (`0xC0000096`) at `0045c620`, `Vga_WaitVerticalRetrace`'s `IN AL,DX`, on the first mission frame, after the loading screen has been shown. On Windows 9x, which lets a Win32 program read port `0x3DA`, the expected result is the loading screen staying up for the whole mission while sound and simulation run; palette changes recolouring that frozen image; the F12 preferences panel drawing and updating (its per-pass `PreferencesPanel_Present` (`00457180`) presents) and staying on screen after it closes; the P, Q and F11 panels pausing the game invisibly; and a glance to view 3, or back from view 2, jamming every view key for the rest of the mission.
- **Open:** no write to `maybe_CockpitLayoutMode` (`004d25bc`, block `004d2540` `+0x7c`) other than `Main_StaticInit`'s 0 found by `es2_xref.py` and `es2_fieldscan.py 7c` over the eighteen holders of the block base, so its values 1 and 2, and the `DAT_004d25da`/`de` origin, have no known way in.
- **Open:** `Display_PresentRect` (`00464924`) has no caller that `es2_xref.py` finds. The same sweep finds `Display_Present`'s 29.
- **Open:** what the paged path's pre-slide copies in `CockpitView_StepViewTransition` (commands 0, 1 and 3) were meant to do on a paged driver. On driver 3 they copy within the one DIB, from the outgoing view's rows onto the incoming view's.
