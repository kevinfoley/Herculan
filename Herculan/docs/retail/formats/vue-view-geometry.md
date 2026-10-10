# `.VUE` — per-view geometry

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses. Verified against retail data in `ES2/VOL/simvol0/vue/`.

One file per herc, `vue\<HERC>.VUE`: for each cockpit view, the 3D viewport rect, the projection centre and the view's origin on the cockpit canvas. `CockpitViewManager_LoadViews` (`00429834`) reads it as the first step of cockpit bring-up ([`../simulation/cockpit-views.md`](../simulation/cockpit-views.md#cockpitviewmanager_loadviews-sequence)); what the game does with each field is [View geometry](../simulation/cockpit-views.md#view-geometry).

## Layout

After the 9-byte VOL prefix: `int32 viewCount`, then `viewCount x` 8 `int32`s — one 32-byte record per view. `viewCount` is 4 in every retail file. All coordinates are authored in the 320-wide space and shifted by `VideoMode_X/YCoordShift`.

| Field | Meaning |
|---|---|
| 0-3 | 3D viewport rect `x0, y0, x1, y1` |
| 4-5 | Projection centre, `cx, cy` — **stored negated**, see [The projection centre is not the middle of the view](../simulation/cockpit-views.md#the-projection-centre-is-not-the-middle-of-the-view) |
| 6-7 | Canvas origin `originX, originY` |

## Retail files

The two glances share a canopy bitmap but not a rect: on every retail herc but RAZOR, view 3's runs the full width where view 2's stops short of it. RAZOR's two are both full width.

Every retail `.VUE` gives view 1 the canvas origin `(0,237)` — no herc differs.

Retail `cy` runs -95 (APOCA, RAPTOR2) to -146 (RAZOR) as stored; `cx` is -160 for every herc and every view, and all four views of a herc carry the same pair.

`APOCA.VUE` (`viewCount = 4`):

| View | Rect | Centre | Canvas origin |
|---|---|---|---|
| 0 | `0,0 – 320,186` | `-160,-95` | `0,0` |
| 1 | `0,0 – 0,0` | `-160,-95` | `0,237` |
| 2 | `0,0 – 287,231` | `-160,-95` | `320,0` |
| 3 | `0,0 – 320,231` | `-160,-95` | `-320,0` |

View 1's rect is zero-size. **RAZOR is the sole exception** — `0,0 – 320,181`, matching its 2368-byte `.HD1` against every other herc's 16-byte stub ([`hd-ed-clip-regions.md`](hd-ed-clip-regions.md#retail-files)); see [The RAZOR's heads-down view](../simulation/cockpit-views.md#the-razors-heads-down-view).

`RAZOR.VUE` (`viewCount = 4`):

| View | Rect | Centre | Canvas origin |
|---|---|---|---|
| 0 | `0,0 – 320,239` | `-160,-146` | `0,0` |
| 1 | `0,0 – 320,181` | `-160,-146` | `0,237` |
| 2 | `0,0 – 320,239` | `-160,-146` | `320,0` |
| 3 | `0,0 – 320,239` | `-160,-146` | `-320,0` |
