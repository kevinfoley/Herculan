# DBSIM.EXE — clip regions, `.ED0`–`.ED3` and `.EDG`

**ES1's cockpit views and its `edg\` files load their clip regions in ES2's layout, except that a band across the whole width is stored as two rows, where ES2's carries its columns too.** The files are [ES2's layout](../../retail/formats/hd-ed-clip-regions.md#layout) but for the rects, and the loader moves no coordinate. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## Layout

```
int16 rectCount
rectCount x { int16 firstRow, int16 lastRow }       -- across columns 0 to 319
int16 blockCount
blockCount x {
    int16 firstRow, int16 rowCount,
    rowCount x { int16 xStart, int16 xEnd }          -- one per row
}
```

`CockpitClipRegions_Load` (`00024d44`) takes the output block and a path; the block is ES2's, an `int` count and the region pointers after it. It opens the path with `VolRStream_CtorFromPath`, reads the counts, the rects' rows and each block's first row and row count 2 bytes at a time, and each block's spans in one read. Each rect becomes a 0x18-byte rect region (vtable `00092846`) whose dwords `+0x08` to `+0x14` hold 0, the first row, 0x13f and the last row, the order of ES2's `x0`, `y0`, `x1`, `y1`; the block stores the region's `+4`, which holds its type, 0. Each span block becomes an 8-byte region tagged 2 whose data is the first row and the row count as `int16`, then the spans as read. ES2's `CockpitClipRegions_Load` (`0042dcf0`) reads four values per rect and shifts every coordinate by the caller's shifts.

## Which files

**The cockpit views.** `CockpitViewManager_LoadViews` (`00021fb0`) takes the manager and the HERC's name. It reads `vue\<name>`: an `int32` view count into `+0x00`, and that many 32-byte records into the array `000afaa4`, whose address it stores at `+0x04`. It allocates 0x10 bytes for `+0x08` and 0x810, four 0x204-byte blocks, for `+0x0c`. For each view `i` it writes `i` with `ltoa` over the `g` of the string `edg` (`00092670`), which makes the folder `ed0` to `ed3`, and loads `ed<i>\<name>` into block `i`. It then loads the view's canopy art with `CockpitCanopy_LoadViewBitmap` (`00022184`), or under `g_LowMemory` clears slot `i` of `+0x08` instead ([the view manager](../simulation/cockpit-views.md#the-view-manager), [ES2's sequence](../../retail/simulation/cockpit-views.md#cockpitviewmanager_loadviews-sequence)).

**The `edg\` files.** `00039818` builds `edg\ENG_CLP<n>` from the table `000932c8`, `n` from 1 to 4 being its third argument's dword 9, and loads it into `000b13f4`; it loads none when that dword is 0. It then moves each block's first row down by the top it computes, its third argument's dwords 5 and 1 summed, and each span's two columns right by the left, dwords 4 and 0.

## Retail files

`SIMVOL0.VOL` holds `ed0\` to `ed3\` for the eight HERCs and `edg\ENG_CLP1.EDG` to `ENG_CLP4.EDG`; `DETECT.VOL`'s `ed0\COLOSSUS.ED0` is the same bytes as `SIMVOL0.VOL`'s.

| Files | Rects | Blocks |
|---|---|---|
| `.ED0` | none | 3 to 5 |
| `.ED1` but `COLOSSUS.ED1` | none | none: 6 bytes |
| `COLOSSUS.ED1` | rows 194–199 | none |
| `ROADRUNN.ED2`, `.ED3` | rows 27–86 | rows 0–26 and 87–183 |
| `RAPTOR.ED2`, `.ED3` | rows 24–67 | rows 0–23 and 68–178 |
| `PATRIOT.ED3` | row 24 | rows 0–23 and 25–165 |
| the other `.ED2` and `.ED3` | none | 1 |
| `ENG_CLP1`–`4.EDG` | none | 1 to 4 |

Every file ends in 2 bytes past its last block, which the loader does not read: 0 in every file but `COLOSSUS.ED1`, whose 30 bytes are a count of 1 and a block of rows 194–199 at columns 0–319, its rect again. Read as a third region list, each file's trailing bytes are an empty one but that file's ([Open](#open)).

## Open

- **Open:** what the bytes past the last block are; the bytes read as a third list that the loader skips.
- **Open:** what instrument `00039818` builds around the `edg\` file. `Gau_EnergyMeterWidget` (`0002cd3c`) calls it with the `.GAU` block at 548 and keeps it at the cockpit's `+0x1e1`; ES2 builds its energy pool meter from that block ([the cockpit object](../simulation/cockpit-panel.md#the-cockpit-object)).
- **Open:** how the simulator draws through the regions, and whether a span's `xEnd` is inclusive as ES2's is.
