# Terrain heightmap — `HeightGrid`, zone loading, height query, byte-verified

Reverse-engineered from `DBSIM.EXE` disassembly (Ghidra project `ES2Recon`). Covers the heightmap/geometry side of terrain: struct layout, zone loading, height interpolation, the structure-footprint flattening pass, and the ray walk. See [`terrain-texturing.md`](terrain-texturing.md) for how cells get their texture, which is a separate pipeline over the same grid.

## The `HeightGrid` struct

0x129 (297) bytes, allocated by `HeightGrid_Constructor` (`0046bdf8`), installed as `ActiveHeightGrid` (`004a0bf8`) by `Terrain_LoadZone` (`0042789c`).

| Offset | Field | Meaning |
|---|---|---|
| `+0xec` | `int*` | Base pointer to the per-cell array (16 bytes/cell, row-major: `cellIndex = x + y*(1 << grid[0x100])`) |
| `+0xf0` | `byte*` | Parallel per-cell scratch byte array (`width*height` bytes). Zeroed at load, used by the [structure-footprint pass](#structure-footprints--the-flattening-pass) at spawn, then `memset` to 0 at the top of every frame and reused as `Terrain_DrawCellObjects`' per-cell pending-object count — see [that section](#structure-footprints--the-flattening-pass) for the handover |
| `+0x100` | `int` | Width shift — log2(grid width in cells) |
| `+0x104` | `int` | Height shift — log2(grid height in cells) |
| `+0x108` | `int` | Cell shift — log2(world-units per cell); also the shift used to convert world (x,y) → cell (x,y) |
| `+0x10c` | `int` | **View radius in cells** (10 at retail detail settings). Its derivation, writer and consumers are in [`terrain-texturing.md`](terrain-texturing.md#grid0x10c--the-lod--draw-radius-field); `Terrain_DrawCellQuad` installs `+0x10c << +0x108` as the visibility range distance fog is measured against — see [`distance-fog-and-sky.md`](distance-fog-and-sky.md) |
| `+0x110` | `int` | Height base — additive height offset (0 for real/binary zones; `MinHeight*8` for the ASCII debug format) |
| `+0x118` | `int` | Height scale — multiplicative height scale applied to each cell's raw byte |
| `+0x11d` | `int` | Material/detail-type record count (from `dat\mat0`) |
| `+0x121` | `int*` | Pointer to the material/detail-type table, `count` × 8-byte records (`ZONES_MaterialTable`, from `dat\mat0`, confirmed against real `ES2/VOL/simvol0/dat/MAT0.DAT`) |

## Per-cell record (16 bytes)

- `+0x0` (byte): raw height value 0–255. World height = `rawByte * grid[0x118] + grid[0x110]` (height scale, height base).
- `+0x1`..`+0x6` (3 shorts): the **near** face normal, scaled to length 0x800.
- `+0x7`..`+0xc` (3 shorts): the **far** face normal, same scale. Which of the two a point belongs to is the diagonal selector's decision, exactly as in `Terrain_HeightQuery`.
- `+0xd` (byte): the **near** triangle's baked shade byte; `+0xe` (byte): the **far** triangle's. Written by the surface build and read straight back by `Terrain_DrawCellQuad` as the ramp row — see [`terrain-lighting.md`](terrain-lighting.md).
- `+0xf` (byte, bitfield): bits `[0:1]` = diagonal-split selector consumed by `Terrain_HeightQuery`'s barycentric interpolation (values `0`/`1`/`2` are produced; `3` is handled by the query but never written); bits `[2:7]` = material/detail-type index into `ZONES_MaterialTable`, assigned via a weighted random roll (~30.6% chance per type, first match wins) at an LOD-driven block stride so neighboring cells within a block share one roll.

**The selector and both normals are written by the same function**, `Terrain_BuildCellSurface` (`0046bed8`), which `Terrain_BuildSurface` (`0046c1dc`) runs over the whole grid via `Terrain_BuildCellSurfaceAndShade` (`0046c2ec`) — at zone load, and again at the end of the [structure-footprint pass](#structure-footprints--the-flattening-pass). Choosing a cell's normals requires choosing its diagonal, so it derives the selector from the four corner heights and stores all three together:

| Corners | Selector | Split |
|---|---|---|
| `h00 + h11 == h01 + h10` | 1 | coplanar quad — both normals identical, no triangle test |
| `h00 + h11 - (h01 + h10) < 1` | 2 | along the `(0,0)`–`(1,1)` diagonal |
| otherwise | 0 | along the `(0,1)`–`(1,0)` anti-diagonal |

Normals are built in *raw height units*, not world units: the horizontal components are plain corner differences and the vertical one is `cellSize / heightScale`, which is a true cross product divided through by the height scale. All six components are doubled before `Math_NormalizeVec3ShortToLength` (`0046c138`) rescales them to 0x800, which changes nothing. The last row and column are skipped — no east/north neighbour to difference against — so they keep a flat `(0, 0, 0x800)` normal and selector 0.

## Loading pipeline

Confirmed against real files in `ES2/VOL/ZONES.VOL`.

1. `Terrain_LoadZone(zoneIndex)` builds the base name `zoneNNNN` and reads a **16-byte per-zone header** resource at `dat\zoneNNNN` (`ZONES.VOL\DAT\ZONE*.DAT`, always exactly 16 bytes): four LE `int32`s — `[0]` width shift and `[1]` height shift (redundant, re-derived from the bitmap itself later), `[2]` cell shift, `[3]` height scale. E.g. `ZONE504.DAT` = `07 00 00 00 07 00 00 00 0E 00 00 00 95 00 00 00` → width and height shift 7 (128×128 cells), cell shift 14, height scale 149.
2. `TerrainZone_LoadHeightmap` (`0046c650`) loads the shared material table from `dat\mat0`, then opens `dba\zoneNNNN.dba`. Every real zone resolves to `.dba` and goes through the generic `ClassItem_LoadResource` polymorphic loader — the same registry-dispatch architecture as `.DFN`/`.HFN`/`.DCI` — into `TerrainZone_PopulateFromBitmap` (`0046c3c0`). Any other extension falls back to a plain `fopen`/`fscanf` ASCII format (`"%d %d %d %d"` header = width shift, height shift, maximum raw height, minimum raw height, then one `%d` per cell) — a level-design/debug path; no loose files of this kind exist in retail data.
3. **`TerrainZone_PopulateFromBitmap`: a zone's heightmap is literally an ordinary Dynamix bitmap** — the same 8-bit-indexed container used for `.DBM`/`.DBA` textures elsewhere (see `dfn-hfn-dci.md`). Each pixel byte (minus a small bias) becomes one cell's raw height byte; the width and height shifts are re-derived from the bitmap's own dimensions rather than trusted from the zone header. **Verified byte-exact against every real file in `ES2/VOL/ZONES.VOL/DBA/`:** 128×128 zones are exactly 16418 bytes (`128*128 + 34`-byte bitmap header), 256×256 zones exactly 65570 bytes (`256*256 + 34`) — the zones that come out 256×256 are precisely the ones whose `.DAT` header declared width and height shift 8 (e.g. `ZONE123.DAT`).

`Terrain_HeightQuery(HeightGrid*, {x,y})` (`0046e07c`) converts a world `(x, y)` into a grid cell via the cell shift, fetches the enclosing cell's 4 corner texels from the 16-byte-per-cell array, and — using each cell's `+0xf` diagonal-selector bits — does barycentric/bilinear interpolation across whichever triangle the query point falls in. Each grid quad can independently choose which way its diagonal split runs, chosen at terrain-authoring/compile time.

Neither loader path writes the selector — every `+0xf` write in `TerrainZone_PopulateFromBitmap` and its ASCII counterpart masks with `& 2` and sets only the material index, via `Math_RandomNext() & 0xfff < 0x4ce` (~30%) for material 1 vs. 0. (The bitmap path hardcodes a ceiling of two materials, unlike the ASCII fallback which loops the whole `mat0` table. The roll is sparse: only cells on a block boundary roll, block size `(1 << (0x15 - mat0[0].field4 - cellShift)) - 1` — 2×2 cells at cell shift 14, 4×4 at 13.) `Terrain_BuildCellSurface` runs afterwards and is what fills it in.

## Structure footprints — the flattening pass

A zone heightmap marks a ground vehicle with a **single raised sample**: `ZONE555` puts one cell of 120 in a plain of 97 under each of its five turrets, and 120 is the only even value anywhere in that half of the file's histogram. Read as corner samples — which is what `Terrain_HeightQuery` and `Terrain_DrawCellQuad` both do — one raised sample is the apex of a four-quad pyramid standing at the *corner* of the marked cell, while the structure it was painted for stands near that cell's *centre* (all five of `ZONE555`'s turrets sit at a cell fraction of about 0.5, 0.5). DBSIM reconciles the two at spawn time rather than in the data.

The `+0xf0` scratch array gets marks from **two** sources before the pass runs.

Each structure registers its own footprint as it is placed — `Terrain_MarkStructureFootprint` (`00470dc8`), called from `Base_AttachToGroup` (`00405c3c`) and from `DBSim_SpawnMissionObjects`' own base branch, in both cases immediately before that structure's height query. It sets bit 0 for the cell the structure stands in unconditionally, then for every cell corner within the structure's `SimObject_GetShapeRadius` of it — measured with the sim's sqrt-free magnitude approximation at the structure's own Z, so the test is planar — plus the three cells west, south and south-west of each such corner, covering all four quads that meet it.

A base group additionally marks its whole pad: `Terrain_PaintFormationPad` (`00471260`) sets bit 0 for every cell its formation's layout map calls occupied, which is why a base levels as one connected region while a lone turret gets its own small mound — turret groups leave the paints-ground flag (block 11 `0x06`) at 0 and contribute nothing here. See [`terrain-texturing.md`](terrain-texturing.md#base-formation-pads), which owns that pass.

Once the whole roster is down, `Terrain_FlattenStructureFootprints` (`00471190`) walks the grid. At each cell still marked and not yet counted it flood-fills the connected region eight-way, summing raw heights and counting cells (`Terrain_AccumulateFootprint`, `00470edc`), writes `sum / count` back over that region (`Terrain_WriteFootprintHeight`, `0047101c`), and when the whole grid is done re-runs `Terrain_BuildSurface` so every normal, diagonal selector and baked shade matches the new ground.

**The write-back is what produces the flat top.** `0047101c` force-writes the average into the three neighbours east, north and north-east whether or not they are marked, without recursing into them — while the other five are visited without the force and change nothing unless marked in their own right. A lone marked cell therefore becomes a 2×2 block of equal samples: one flat quad, centred on the structure, with the four surrounding quads sloping down to the plain.

`DBSim_SpawnMissionObjects` (`004253d8`) calls the pass at `004263d9` and then walks its own structure list re-querying `Terrain_HeightQuery` at each position, because the ground moved under them. Machines are placed before the pass and are not revisited; a walking HERC re-queries the ground every tick anyway.

The scratch byte's three bits all belong to this pass — bit 0 marked, bit 1 counted, bit 2 written. `Terrain_SetCellScratch` (`00470cd0`) and `Terrain_GetCellScratch` (`00470cf4`) are its accessors; `HeightGrid_SetCellScratchByte` (`00470c68`) is the loader's, addressing the same array by cell pointer instead of coordinates.

**The bits are wiped before anything else reads the array.** `HeightGrid_ClearCellScratch` (`0046e840`) `memset`s the whole thing to 0 as the first act of `Scene_SubmitFrameObjects` (`0042841c`), which runs every frame — so the pass's marks never survive into the frame that follows mission spawn. From then on the same byte is a per-cell pending-object count: `Terrain_IncrementCellObjectCount` (`00470cac`) increments it as an object registers against a cell, and `Terrain_DrawCellObjects` (`0046e4a0`) reads it, dispatches that many, and clears that cell back to 0. Nothing bridges the two uses, and without the per-frame `memset` the first frame would read a flattened base's marks as object counts.

The marking bounds-checks nothing beyond refusing to step to a negative index, so a structure near a zone edge writes past the array. `00471190` and `00470edc` walk both axes to `1 << grid[0x100]`, the width, which cannot misbehave on retail data since every zone is square.

## Ray-versus-terrain — `Terrain_RayWalk` (`0046e87c`)

The terrain module's largest function (5129 bytes). Takes two world points and walks the segment between them across the grid. Its last argument selects the per-step test over a shared walk: **mode 0**, the thin ray, reports where the segment first passes into the ground; **mode 1**, the [slope walk](#mode-1--the-slope-walk), reports the first face along it too steep to walk. `es2_xref.py` finds five call sites and no stored pointer or vtable slot:

| Caller | Mode | Use |
|---|---|---|
| `Sim_RaycastTerrain` (`00428048`) | 0 | Weapon fire, clipping the ray to the ground |
| `Terrain_RayHitDistance` (`004280f4`) | 0 | Range to the ground hit, for `Detection_LineOfSight` and `Ai_LineOfSightBlocked` |
| `Mech_AiObstacleAvoidance` (`00416274`), twice | 1 | The AI's two ground-hugging obstacle probes — [`../simulation/ai-navigation.md`](../simulation/ai-navigation.md#the-two-probes) |
| `Ai_LineOfSightBlocked` (`0041dc24`) | 1 | Whether the ground in the way is too steep to walk over — [`../simulation/ai-combat-states.md`](../simulation/ai-combat-states.md#line-of-sight--ai_lineofsightblocked-0041dc24) |

Setup: halve the segment delta until every component fits ±32000, take four Q16 slopes — `dy/dx`, `dz/dx`, `dx/dy`, `dz/dy`, each falling back to 1.0 on a zero denominator — and classify the ground-plane delta into an **octant** 0–7, which encodes the major axis and both step signs in one value. Ties make X the major axis.

| Octant | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| Major axis | X | Y | Y | X | X | Y | Y | X |
| X step | + | + | − | − | − | − | + | + |
| Y step | + | + | + | + | − | − | − | − |

The walk is a cell DDA with a two-phase step: each iteration takes the segment's exit across the major axis, but if a minor-axis boundary falls before it, that exit is **stashed and replayed on the next iteration** while the minor crossing is handled first. Each iteration therefore yields one sub-segment and one exit edge code — 0 west, 1 east, 2 north, 3 south. The last step — the one ending at the segment's endpoint rather than a cell boundary — has no exit edge, and each mode tests its endpoint instead. Both modes return no-hit for a segment starting outside the grid or walking off its edge.

**Six of the walk's multiplies truncate.** In the octant-2 and octant-3 arms the compiler emitted 32-bit `imul`/`sar 16` (at `0046f077`, `0046f08d`, `0046f162`, `0046f178`, `0046f228`, `0046f23e`) instead of the 64-bit `Math_Q16Multiply` used everywhere else. Diverges only at grazing angles.

### Mode 0 — the thin ray

`Terrain_EdgeCrossingTest` (`0047035c`) is the per-step test, and takes only the exit edge's two corner heights: below both → clear, at or above both → hit, straddling → interpolate the edge height at the exit point. Only once that reports a crossing does `Terrain_CellSurfaceIntersect` (`0047068c`) solve the exact point, intersecting the sub-segment with the cell's triangle planes (built from the `+0x1`/`+0x7` normals, through a corner each triangle contains) via `Math_PlaneSegmentIntersect` (`0047e504`). The near triangle is tried first; a hit outside it falls through to the far one.

Two details of the plane solve that look like porting slips but are the original's:

- **Selector 2's far plane keeps the near triangle's plane constant.** The far normal is loaded but `d` is not recomputed against it, so that plane is skewed by the difference in the two normals' Z.
- **Selector 0's far triangle shares no corner with its own cell** (its corners are `01`/`10`/`11`), so the code shifts one cell east and takes corner `10` from that record. Deliberate, and why the two selectors are not symmetric.

The last step falls back to `Terrain_HeightQuery` at each end of it. The **first** iteration also queries the start point, so a ray beginning underground is a hit immediately.

When the plane solve finds nothing it still returns "hit" with its output buffer left unwritten.

`Sim_RaycastTerrain` (`00428048`) is the weapon-fire caller: it builds the ray's far end as the muzzle frame's own `(0, distance, 0)`, walks, and measures the ground hit back to the muzzle with the fast-magnitude approximation.

**The walk radius is dead in both modes.** The ray record's `+0x08` (a literal 200) arrives as `Terrain_RayWalk`'s fourth argument, and mode 0 does read it — at `0046fb35`, `0046fba7` and `0046fc1b`, the three sites that hand it to `Terrain_CellSurfaceIntersect` (`0047068c`). That is where it stops: the callee never reads that parameter, and those three are its only callers, so nothing downstream of the walk is a function of the radius. Every mode-1 caller passes 0. See [`../simulation/weapon-firing.md`](../simulation/weapon-firing.md).

### Mode 1 — the slope walk

The same segment and the same walk, with a different question at each step: is the face being crossed one a machine could walk up? Nothing is swept; the segment has no width. A segment sliding along rolling ground reports nothing, and only a face too steep to walk stops it, which is why the AI's ground-hugging probes use this mode — see [`../simulation/ai-navigation.md`](../simulation/ai-navigation.md#the-two-probes).

**The direction.** Setup packs the halved delta into three shorts and normalises it to length `0x800` with `Math_NormalizeVec3ShortToLength` (`0046c138`), the normaliser the face normals go through, so the two are in the same units.

**The face test** is `Terrain_FaceBlocksMovement` (`0046fe40`), on the steepness of the face's normal alone; its threshold, and how it sits against the move's own slope refusal, are in [`../simulation/ai-navigation.md`](../simulation/ai-navigation.md#the-two-probes). On the threshold value exactly it falls through to the dot product of the normal and the direction, blocking under `-8000000`.

**Each step**, in order:

1. On the first iteration only, `Terrain_FaceBlocksAt` (`0046fe84`) at the start point: the face under it, picked by the cell's diagonal selector as `Terrain_HeightQuery` picks it. A hit reports the start point.
2. On the last step, `Terrain_FaceBlocksAt` at the endpoint. A hit reports `Terrain_FindDiagonalCrossing` (below); a clear endpoint ends the walk with no hit.
3. Otherwise `Terrain_EdgeFaceBlocks` (`0046ff74`) for the exit edge. It tests this cell's triangle bordering that edge, then the neighbouring cell's triangle bordering it from the other side. Under either diagonal a cell's near triangle borders its west edge and its far triangle its east edge; which one borders north and south depends on the selector. It returns 2 for this cell's face, 1 for the neighbour's, 0 for neither. A 2 reports `Terrain_FindDiagonalCrossing`; a 1 reports the exit point.

**The hit point.** `Terrain_FindDiagonalCrossing` (`0046fcac`) answers where the step entered the triangle its far end lies in. It works in cell-local `x` and `y`, mirroring `x` across the cell for selector 0 so that one test, `y < x`, serves both diagonals. When both ends of the step are on the same side, the answer is the step's start, `z` included. Otherwise it bisects between them, at most eight times and stopping early when the midpoint lands on the diagonal, and the answer is the last midpoint with `z` 0. Of the callers, only the obstacle probes read the point, and only as a ground-plane range from the probe's start.

**Cells are addressed by flat index.** `Terrain_FaceBlocksAt` and `Terrain_EdgeFaceBlocks` both form `(y << widthShift) + x` and treat only an index outside the whole array as off the grid; such a cell blocks. So the neighbour across the grid's west or east edge, and a point just past it, are the far end of the adjacent row.

`Deployment_PickPointNearPlayer` (`0042354c`) also calls `Terrain_FaceBlocksAt` directly, with a zero direction, to ask whether a point is too steep to stand on.

## Consumers outside the terrain system

- **Drop-pod landing** (`Meteor_Tick`, `00409d2c`) checks altitude against `Terrain_HeightQuery` every tick and detonates the instant the pod dips below ground — see [`../simulation/mission-deployment.md`](../simulation/mission-deployment.md) and [`../simulation/damage-system.md`](../simulation/damage-system.md#the-sweep--damage_explosiveblastsweep-00426a20).
- **A flyer's airframe contact probes** (`Razor_MovementTick`, assumed `flyersys.cpp` ([Open](#open))). Six points on the airframe are each transformed into world space and tested against `Terrain_HeightQuery`, and all but one also raycast via `Sim_RaycastObjectList` (`00426528`, see [`../simulation/hit-detection.md`](../simulation/hit-detection.md#the-sweep--sim_raycastobjectlist-00426528)). A contact damages the component that touched and kicks the airframe away from it. This is the flyer's whole collision model, not an assist — see [`../simulation/razor-flight.md`](../simulation/razor-flight.md#contact-probes).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `Terrain_RayWalk`'s mode 1 is a machine's movement collision, sweeping its volume against the ground | The `FaceBlocks` names suggest it, but its callers are the AI's obstacle probes and its line-of-sight test ([table above](#ray-versus-terrain--terrain_raywalk-0046e87c)). A machine's move is refused by `Mech_CollisionTest`'s own slope test ([`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md#collision)), and mode 1 walks the same zero-width segment mode 0 does |

## Open

- **Open:** confirm `Razor_MovementTick`'s source file — assumed `flyersys.cpp` by naming convention, but no assert string in the binary names it.
