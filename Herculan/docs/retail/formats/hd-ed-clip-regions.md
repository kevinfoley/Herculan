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
```

Coordinates are shifted by the caller's `(xShift, yShift)`: `(0,0)` for the `.HD*` set (already 640-wide), `VideoMode_X/YCoordShift` for `.ED*`. Inclusive ends are expanded as `end = (end << shift) + (1 << shift) - 1`. Output is a `0x204`-byte block: `int count` plus up to 128 region pointers, rects tagged type 0 and span blocks type 2.

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

Every file consumes its whole body under this layout with 3 constant trailing bytes unread. RAZOR is the only herc with a non-stub view-1 file, matching the file sizes on disk (`APOCA.HD1` is 16 bytes, both counts zero; `RAZOR.HD1` is 2368). `APOCA.HD0` resolves to rows 0-371, matching the independently measured index-0 bounding box on `APOCA.HB0` (`y:[0..371]`).

`edg\HDDCLIP.EDG`, the heads-down display's clip file, uses the same layout ([`../simulation/heads-down-display.md`](../simulation/heads-down-display.md#hddclip)).
