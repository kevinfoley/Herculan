# Terrain texturing (DBSIM.EXE)

Terrain *lighting* is a separate subject with its own file: [`terrain-lighting.md`](terrain-lighting.md).

Which cells a frame draws, in what order, and how the objects on them are painted in between: [`terrain-drawing.md`](terrain-drawing.md).

See `dts-texture-binding.md` for the mech-side texturing chain — terrain and mechs share a data structure.

## The answer, end to end

```
world<N> descriptor file  ──(a string field in the data)──▶  dba\<name>.DBA
                                                                    │
                            Terrain_BindTextureBank (0046bc98)      │
                                                                    ▼
                                         _DAT_006b4fc4 = 20-byte-per-frame descriptor table
                                                                    ▲
   cell[0xf] >> 2  =  material index ──▶ mat0[i] (8 bytes) ─ field 0 = FRAME INDEX
                                                           └ field 1 = BlockShift (tiling)
                                              Terrain_ResolveCellTexture (0046bcf4)
```

**Terrain bank selection:** `Terrain_BindTextureBank` loads a DBA by name read from the `world<N>` descriptor file. Bank names (`ice`, `bsnow`, `volcan`, `moon`, `urban`) are data-driven, not hardcoded.

**Descriptor table:** 20-byte stride (frame descriptors); first 16 bytes are the `int32` UV-rect corners `F0..F3` (documented in `dts-texture-binding.md`). Terrain and mechs share one texturing substrate.

### The `world<N>` descriptor — layout

`wld\WORLD0.WLD` … `WORLD9.WLD`, 310–313 bytes each. `maybe_World_LoadTheater` reads them field-by-field in this order:

| | |
|---|---|
| 8 x `int16` | the sky backdrop's `hzline` — see [`distance-fog-and-sky.md`](distance-fog-and-sky.md#the-object) |
| 6 x `int16` | ditto; two land in `DAT_004cfd76`/`DAT_004cfd78`. The second of the six, the file's tenth `int16` (byte 18), is `World_FlatSetSelector` (`0049aeea`), which picks the theater's ground-shape set ([`../simulation/ground-shapes.md`](../simulation/ground-shapes.md#the-shape-set--flatobj_loadresources-004097a8)); 1 in all ten retail files |
| `int32` count + count x `int32` | `WorldShades_BandsTagged` (`004cfd84`), the distance thresholds of the [colour bands](distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8) for an object with a type tag: 16 entries, 4400 apart from 60000 — from 30000 in `WORLD4` |
| `int32` count + count x `int32` | `WorldShades_BandsTag0` (`004cfd88`), the same for tag 0: 16 entries, 4400 apart from 60000, in every retail file |
| `int16` rows, `int16` cols | sizes the pair of ramp tables that follow; 16 and 11 in every retail file |
| cols x `int32`, `int16`, cols x `int32` | expanded by `Palette_InterpolateIndexRanges` (`00430d08`) into `WorldShades_LevelRanges` (`004cfd7c`), one index range per column and band |
| 4 bytes, 4 bytes | a second, 1-wide ramp through the same expander, into `WorldShades_BlendRanges` (`004cfd80`) |
| `int16`, `int16`, `int32`, `int32` | the two `int32`s are `WorldShades_DistanceOffsets` (`004cfd6c`), the offsets a tag-5 object's distance takes |
| 5 NUL-terminated strings | `world24`, `clouds2`, `impact<N>`, **terrain bank**, `tex` |

| descriptor | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|---|
| bank | urban | urban | bsnow | bsnow | volcan | volcan | ice | ice | moon | moon |

Five theaters, two variants each. The variant is **time of day**: the practice missions screen's `Day` / `Night` row writes it straight into the header field, and the ten retail files all carry `Day`. See [`../shell/screen-layout.md`](../shell/screen-layout.md#the-parameters). Which theater, variant and zone a mission runs is the `script.dat` header's — see [`script-dat.md`](script-dat.md#header-format).

Alongside the terrain bank, `maybe_World_LoadTheater` loads the theater palette `dpl\world<N>.dpl`, one per theater, which mech and structure shading resolves through too.

### `mat0`'s two fields

Field 0 is **a DBA frame index** — `_DAT_006b4fc4[index * 0x14]`.

Field 1, the block shift, selects between two placement modes:

- **`blockShift == 0`** — the frame's own `F0..F3` corners are used verbatim: the whole frame is stretched across the quad.
- **`blockShift != 0`** — tiled:

  ```
  shift = cellShift + blockShift - 13
  u0    = (cellX << shift) & 0xff        u1 = u0 + (1 << shift)
  v0    = (cellY << shift) & 0xff        v1 = v0 + (1 << shift)      (V is negated)
  ```

**The `& 0xff` is the tiling wrap** — UV space is 256 texels, which is why every terrain bank ships as 256x256 frames and why they must be edge-tileable. Each cell covers `2^shift` texels.

`Terrain_ResolveCellTexture` returns descriptor field 4 (bytes 16–19) as a short — the bitmap handle passed to the polygon draw.

### Which quad corner takes which UV

The rect leaves `Terrain_ResolveCellTexture` as four `(u, v)` pairs, and `Terrain_DrawCellQuad` hands pair *i* to vertex *i* of the quad it fetched, in that function's own corner order — `(cellX, cellY)`, `(cellX, cellY+1)`, `(cellX+1, cellY+1)`, `(cellX+1, cellY)`. All three of its split branches reuse those pairs by index, so the mapping is unambiguous:

| corner | u | v |
|---|---|---|
| `(cellX, cellY)` | `u0` | `-v0` |
| `(cellX, cellY+1)` | `u0` | `-(v0 + span)` |
| `(cellX+1, cellY+1)` | `u0 + span` | `-(v0 + span)` |
| `(cellX+1, cellY)` | `u0 + span` | `-v0` |

So `u` rises with `cellX` and, because of the negation, `v` **falls** with `cellY`. The wrap itself is not a seam: the frames are edge-tileable, so `v` stepping from 0 back to 256 joins cleanly.

### Retail numbers

`MAT0.DAT` holds 13 records: `{0,6}`, `{1,6}`, `{2,5}`, … — field 0 ascending (frame index), field 1 per-material tiling shift. Materials 0 and 1 give `shift = cellShift - 7`: **128 texels per cell at `cellShift` 14, repeating every 2 cells; 64 at 13, repeating every 4.** Either way a texel spans 128 world units, the cell size cancelling out.

Materials **0 and 1 are the only ones a zone rolls**: `TerrainZone_PopulateFromBitmap`'s roll bound is the hard literal 2 (`CMP EBX,0x2` at `0046c5ca`), not the `mat0` count, and every shipped zone is a `.dba` that comes through it. Frame 0 is the plain tiling ground, frame 1 its variant.

Materials **2–12 are the eleven base-formation pads** — see [Base formation pads](#base-formation-pads) — and reach terrain only through `Terrain_PaintFormationPad`. Their block shift of 5 or 4 makes one frame span a whole 8- or 16-cell tile rather than tiling, which is why each is a single legible site plan rather than a repeating texture. Only `TerrainZone_LoadHeightmap`'s ASCII fallback bounds the roll by the `mat0` count and could roll one at random; no loose ASCII zone ships.

**World scale:** `Hud_WorldUnitsToMetres` (`00434228`) defines 166.667 world units = 1 metre (recovered from the HUD's distance conversion in `docs/herculan/planning.md`), so 128 world units per texel is ~0.77 m/texel.

## `grid+0x10c` — the LOD / draw-radius field

This section is the canonical account of `+0x10c`; [`terrain-heightmap.md`](terrain-heightmap.md) and [`distance-fog-and-sky.md`](distance-fog-and-sky.md) reference it rather than re-deriving it.

One function **writes** the field; four read it:

- `Terrain_SetupVisibleRegion` (`0046ca98`) sets `grid[+0x10c] = (short)DAT_004a0bcc[DAT_004d1fc3]` — the terrain-detail setting's own table, below — then `>>= (cellShift - 14)` when `cellShift > 14`. The `>>` only ever fires on the two shift-15 zones; nothing compensates in the other direction, so a zone with small cells is simply seen less far across. The original re-reads the setting every frame.
- `Terrain_BuildDrawRegionQuad` (`0046d220`) builds the draw region as a square of radius `grid[+0x10c] << cellShift` world units around the viewer, clamped to the grid extent. So the LOD field is literally **a terrain draw radius in cells**.
- `ObjList_SetDrawDistances` (`00428bc0`) scales that same distance by five Q10 factors into the per-class object draw distances ([`terrain-drawing.md`](terrain-drawing.md#objects-in-the-walk)). **Not** the distance fog, which is 12-slice and computed per drawn thing.
- `Terrain_DrawCellQuad` (`0046d344`) installs `grid[+0x10c] << grid[+0x108]` per cell as the visibility range the distance fade is measured against — see [`distance-fog-and-sky.md`](distance-fog-and-sky.md), which tabulates the resulting range per cell shift.

`Terrain_ProjectFarEdgeAhead` (`00470910`) reads the same field once a frame, from `Scene_DrawTerrainPass`: it projects to the screen the point that radius straight ahead of the viewer along its heading, at the grid's lowest height (`+0x110`), and writes the screen point to `+0x72`/`+0x76` of the theater's `hzline`. When the viewer's cell is within the radius of a grid edge the distance is a literal 1000 world units instead. The point never reaches the frame: [`distance-fog-and-sky.md`](distance-fog-and-sky.md#below-the-line).

### The terrain-detail setting

`DAT_004a0bcc` is three `int16`s — **6, 10, 14** cells, and its three entries are the whole of why the setting has three values: nothing else bounds it, the preferences panel's own label map being five entries wide with its last two zero.

The setting is **option 7** of the simulator's option array, and it has no writer of its own — the preferences panel's TERRAIN DISTANCE row is the only thing that moves it. The array and the file it is read from are in [`../simulation/preferences.md`](../simulation/preferences.md#dataprefscfg--the-option-array). So the draw distance is a player setting, not a property of the zone.

### The terrain-texture switch

**Option 8**, the panel's TERRAIN TEXTURE row, reaches the draw path as `TerrainTexturingEnabled` (`004aab2c`), which `Terrain_DrawCellQuad` tests per triangle: 1 picks the textured span writers and 0 the flat ones. Two things write it — the option's own handler (`00459d4c`) and `Terrain_LoadZone`, from the same byte — so the setting lands on the next zone whether or not the handler ever runs. `MfdMissileViewScreen_Paint` (`0043fe1c`) saves it, forces it to 0 and restores it around the MISSILE CAM screen's terrain pass, which is why that view's terrain is never textured however the setting reads.

## Who writes `cell[+0xf]`

The byte holds two fields: the low two bits are the diagonal-split selector, bits `[2:7]` the material index this document's texture lookup uses. Four functions write it, and **the render path is not among them** — every reference there is a read, `>> 2` for the material and `& 3` for the selector, the latter tested in four places in `Terrain_EdgeFaceBlocks` (`0046ff74`), all against `== 0`.

| Writer | Writes | When |
|---|---|---|
| `Terrain_BuildCellSurface` (`0046bed8`) | selector only | at zone load and after each flattening, alongside the two face normals it needs a diagonal to build — see [`terrain-heightmap.md`](terrain-heightmap.md) for the four-corner rule |
| `TerrainZone_PopulateFromBitmap` (`0046c3c0`) | material | the `.dba` roll, capped at material 1 |
| `TerrainZone_LoadHeightmap` (`0046c650`) | material | the ASCII fallback's roll, bounded by the `mat0` count; no retail zone takes this path |
| `Terrain_PaintFormationPad` (`00471260`) | material | per base group at mission spawn — see below |

## Base formation pads

A base group whose `script.dat` block-11 record sets its paints-ground flag (`0x06`) repaints the ground it stands on with its formation's own material, which is what puts a retail base on a marked concrete pad instead of open terrain. `DBSim_SpawnMissionObjects` (`004253d8`) calls `Base_ApplyFormationTerrain` (`00405db0`) for each such group, passing the group's first-attached member; that reads the group's `BFORMS.DAT` record and calls `Terrain_PaintFormationPad`.

The record supplies the material index and a square `dim`×`dim` map of `0`/`1` bytes — see [`script-dat.md`](script-dat.md#the-per-formation-trailer), which owns the file layout, how many formations carry one, and the anchor placement that goes with it. A formation whose material index is `-1` paints nothing.

```
tile      = 1 << (0x15 - mat0[material].blockShift)     world units square, cellShift-independent
map entry = tile / dim                                  so dim spans the tile exactly
per cell  = 1 << (cellShift - 13)                       map entries along each axis
```

Two things fall out of that. The tile is the same 65,536 or 131,072 world units whatever the zone's cell size, the map simply resolving finer or coarser against it; and `dim` is not free data — it is `2 ^ (8 - blockShift)` at cell shift 13, which holds for all eleven retail formations with no exceptions.

**The map is a levelling mask, not the pad's shape.** Every cell of the tile takes the material unconditionally; only cells whose map byte is nonzero also get `Terrain_SetCellScratch(1)`, feeding the flattening pass in [`terrain-heightmap.md`](terrain-heightmap.md#structure-footprints--the-flattening-pass) as its second input. The pad's outline is drawn into the frame art itself — overlay a formation's map on its frame and the marked entries land on that frame's concrete and nowhere else. **Map row 0 indexes the tile's high-y edge and counts down**, the same inversion the anchor placement uses.

The material write, but not the levelling mark, is skipped when `CockpitArt_LoadOnDemand` is set — the low-memory mode (`-l`, or under 12 MB physical). Such a machine gets flat ground with no pad painted on it.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A cell's UV rect attaches to the quad with both axes monotone — `u` rising with `cellX`, `v` rising with `cellY` | `v` falls with `cellY`. Monotone `v` mirrors every cell vertically against the row below it, so each cell boundary becomes a mirror seam |
| Materials 2–12 are addressable but nothing assigns them, so they are dead data | Only the two zone loaders' rolls are capped at material 1. `Terrain_PaintFormationPad` assigns 2–12, one per base formation, and reading the loaders alone makes the gap look unexplained |
| A formation's layout map is the pad's shape, so painting follows the map | The map is a levelling mask. The material is written to the whole tile regardless, and the pad outline is in the frame art. The maps read as legible site plans, which is what makes this the obvious reading |

## Open

- **Open:** whether DBSIM has already drawn from the shared RNG instance before terrain populates on a given zone load, which would offset the draw sequence and land the frame-1 roll ([Retail numbers](#retail-numbers)) on different cells even with a matching seed and algorithm; a retail screenshot comparison would settle it.
