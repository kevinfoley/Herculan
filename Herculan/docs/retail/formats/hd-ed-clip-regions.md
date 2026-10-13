# `.HD0`-`.HD3` / `.ED0`-`.ED3` — 3D-viewport clip regions

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses. Verified against retail data in `ES2/VOL/simvol0/{hd0-3,ed0-3}/`.

One file per herc and cockpit view (`APOCA.HD0`), in the `hd<n>` and `ed<n>` folders: the region of the view the 3D scene may be drawn into, as rects and per-scanline spans. `CockpitClipRegions_Load` (`0042dcf0`) reads one for each view during cockpit bring-up ([`../simulation/cockpit-views.md`](../simulation/cockpit-views.md#cockpitviewmanager_loadviews-sequence)); how the game turns the regions into the canopy cutout is [The viewport cutout](../simulation/cockpit-views.md#the-viewport-cutout).

## Layout

After the 9-byte VOL prefix, all fields little-endian `int16`:

```
int16 rectCount
rectCount x { int16 y0, int16 y1, int16 x0, int16 x1 }      -- inclusive on all four edges
int16 blockCount
blockCount x {
    int16 firstRow, int16 rowCount,
    rowCount x { int16 xStart, int16 xEnd }                 -- one entry per scanline, inclusive
}
int16 0                                                     -- not read by the loader
```

The loader stops after the last block. Every retail file (all 74 under `edg`, `hd0`-`hd3` and `ed0`-`ed3`) ends with the one int16 `0` above; the extracted copies add the VOL entry's framing byte after it.

Coordinates are shifted by the caller's `(xShift, yShift)`: `(0,0)` for the `.HD*` set (already 640-wide), `VideoMode_X/YCoordShift` for `.ED*` and `HDDCLIP.EDG`. The two region kinds widen differently:

- **Rects:** every coordinate is shifted left by its axis's shift, and when that shift is nonzero the inclusive ends become `end = (end << shift) + (1 << shift) - 1`.
- **Span blocks:** `firstRow` and `rowCount` are shifted left by `yShift`, and with `yShift` nonzero each pair in the file fills its row and is copied into the next. With `xShift` nonzero each pair becomes `{xStart << xShift, (xEnd << xShift) + 1}`.

Output is a `0x204`-byte block: `int count` plus up to 128 region pointers, rects tagged type 0 and span blocks type 2.

Every rect in every retail file has `x0 == 0`. This matters because DBSIM's flattening step ([The viewport cutout](../simulation/cockpit-views.md#the-viewport-cutout)) feeds a rect's fourth field to the rasterizer as a span *length* (`piVar4[1] = piVar1[3]`, against `end - start + 1` for span blocks) while the loader's own shift arithmetic treats it as an inclusive end. With `x0 == 0` the two readings differ by one column at the right edge and nothing else.

## Retail files

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

RAZOR is the only herc with a non-stub view-1 file, matching the file sizes on disk (`APOCA.HD1` is 16 bytes, both counts zero; `RAZOR.HD1` is 2368). `APOCA.HD0` resolves to rows 0-371, matching the independently measured index-0 bounding box on `APOCA.HB0` (`y:[0..371]`).

The `edg` folder holds two files in the same layout, each with no rects and a single span block from row 0 in 320-wide coordinates, rounded at the corners:

| File | Rows | Widest span |
|---|---|---|
| `HDDCLIP.EDG` | 101 (0-100) | 0-229 |
| `MFDCLIP.EDG` | 61 (0-60) | 0-97 |

`HDDCLIP.EDG` is the heads-down display's clip shape, which `HddDisplay_Ctor` (`00448cc8`) loads ([`../simulation/heads-down-display.md`](../simulation/heads-down-display.md#hddclip)). What reads `MFDCLIP.EDG` is [Open](#open).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| An `.EDG` file is a flat array of `{int16 left, int16 right}` rows, one per scanline, plus a trailing int16 | Both `.EDG` files walk to the byte that way, because the two counts and the block header each read as a two-word row (`{0, 1}`, then `{0, 101}` or `{0, 61}`). `HddDisplay_Ctor` reads `HDDCLIP.EDG` through `CockpitClipRegions_Load`, so those four words are `rectCount 0`, `blockCount 1`, `firstRow 0` and `rowCount`, and the scanline rows start at the fifth word. |

## Open

- **Open:** what reads `edg\MFDCLIP.EDG`. A search for the name `mfdclip` in `DBSIM.EXE`, `VSHELL.EXE` and the VOLs finds only the archive's own file table; the same search finds `hddclip` in `DBSIM.EXE`.
