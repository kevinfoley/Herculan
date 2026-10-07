# .DGS shape library

The `.DGS` container and the structure shapes it holds. Companion: [`weapons-dat-sim.md`](weapons-dat-sim.md). The `.HD0`-`.HD3` / `.ED0`-`.ED3` clip-region files this file's name also mentions are documented in [`cockpit-views.md`](cockpit-views.md#hd0-hd3--ed0-ed3--3d-viewport-clip-regions), which owns that format, its loader and its real-file verification.

## `.DGS` container format

`BASES.DGS`/`BHULKS.DGS`: a flat sequential list of `ClassItem`-tagged records — **not** the same container as `.DTS`, despite `BASES_AN.DTS` and `BASES.DGS` both starting with a 4-byte value that resembles `recordSize<<16|version`.

**Container.** Each record: `[classId:int32 LE][payloadSize:int32 LE]` + payload. `classId` for this library is `0x02BC0001` (= the record's own leading 4 on-disk bytes). Read via the generic polymorphic `ClassItem_LoadResource` (`0047a038`) registry dispatch — same mechanism as `.DFN`/`.DCI` ([`dfn-hfn-dci.md`](dfn-hfn-dci.md)), different registered class: `GridShape` by its RTTI name (vtable `0049aaa0`, `0x438` bytes, derived from `TSShape`), built by `GridShape_Construct` (`00427568`). `BaseType_LoadShape` (`00405ebc`) → `BaseType_ResolveShape` (`00474cd8`) walks this list sequentially by index (not random-access) to resolve `dat\BASES.DAT`'s shape index (`+0x02`, [`bases-dat.md`](bases-dat.md#the-type-record)).

**Record layout** (traced through the class's chain of base-class reads — `GridShape_ReadFromStream` (`0042762c`) → `TSShape_ReadFromStream` (`00490d5c`) → `TSPartList_ReadFromStream` (`0048fd94`) → `TSPartBase_ReadFromStream` (`0048f894`)):
1. 3×`int16` head fields + 6 raw bytes (base header). The **third is the shape's bounding radius** — [below](#the-bounding-radius--shape8).
2. `int16` child count, then that many nested `ClassItem` records
3. the rest of the `TSShape`, in `TSShape_ReadFromStream`'s order: `int16` node-transform count (`+0x16`), `int16` sequence count (`+0x24`), that many `int16` per-sequence frame counts (`+0x20`), then the node transforms, 32 bytes each (`+0x18`). The same tail a `.DTS` root carries ([`dts-node-posing.md`](dts-node-posing.md#the-shapes-own-node-transforms)); the per-sequence cell array at `+0x1c` is allocated zeroed, not read. No retail record states a node transform. 22 of the 45 `BASES.DGS` records and 2 of the 16 `BHULKS.DGS` records state sequences: the frame counts a structure's flipbook wraps on, and the size of its shape instance's cell array ([`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#a-structure-coming-down))
4. the shape's **collision volume**: 5×`int16` scalars, a 1024-byte height table, then one row of height codes per grid row. Layout [below](#the-collision-volume); the queries that walk it are [`../simulation/hit-detection.md`](../simulation/hit-detection.md#the-collision-volume--the-dgs-records-height-field)'s.

Every record's on-disk footprint (header+payload) pads to an even total.

**Every retail record's one child (step 2) is an ordinary DTS chunk** — observed tag `0x0014000c` = `TSDetailPart`, byte-identical format to a plain `.DTS` file's own chunks. The `.DGS` is a new envelope around the existing mesh format, not a new mesh format.

**Verified against retail data:** an independent whole-file scan for the `0x02BC0001` tag pattern finds the same record boundaries the sequential reader does (45/45 `BASES.DGS`, matching `BASES.DAT`'s 57 static types many-to-45 through shared shape indices). Every embedded child parses as a DTS chunk with zero exceptions and produces real geometry: `BASES.DGS` 45/45 records, 1536 groups, 8978 polys; `BHULKS.DGS` 16/16 records, 113 groups, 786 polys.

### Shape origin

**A shape's origin is its ground contact point, not a rig pivot.** Measured across the libraries: 44 of the 45 `BASES.DGS` shapes and all eight `BASES_AN.DTS` roots have their lowest vertex at exactly y=0. The exception is shape 28 (base type 38, an elevated span), whose geometry starts 10.8 render units up because the structure is meant to stand clear of the terrain.

The HERC roster is the same rule: every root 0 sits at y=0 except COLOSSUS, which dips 2.4 render units (400 world units) and is also the one HERC with a 400-unit ride height — the same correction (see [`dts-node-posing.md`](dts-node-posing.md)).

So a placed structure is drawn at terrain height with no vertical correction of any kind. Raising an object by its mesh's lowest point is a no-op on every shape but 28, which it drags down onto the ground — visible against retail in `Reference/Building_comparison.png`.

## The collision volume

Step 4 of the record, read by `GridShape_ReadFromStream` (`0042762c`):

| Offset | Type | Meaning |
|---|---|---|
| `+0x2a` | `int16` | columns (grid X extent) |
| `+0x2c` | `int16` | rows (grid Y extent) |
| `+0x2e` | `int16` | origin column |
| `+0x30` | `int16` | origin row |
| `+0x32` | `int16` | log2 of the cell size in world units |
| `+0x34` | 256 × `int32` | height table, indexed by a cell's byte code |
| `+0x430` | — | the table's last entry, addressed directly as the grid's ceiling |
| `+0x434` | rows × columns bytes | height codes, row-major with Y outermost |

**Verified against retail data:** all 45 `BASES.DGS` records have a cell shift of 9 (512 world units, ~3 m), the origin at the grid centre, and a height table that is **ascending** — which is what makes `+0x430` the true ceiling rather than just the last entry. Footprints run 2560×4096 to 19456×19456 world units (15 m to 117 m), and ceilings 511 to 29537.

How a shot and a walking machine sample the grid is [`../simulation/hit-detection.md`](../simulation/hit-detection.md#the-collision-volume--the-dgs-records-height-field).

## The bounding radius — `shape+8`

The third of the three `int16` head fields every part record carries (`TSPartBase_ReadFromStream`, `0048f894`). Two unrelated consumers identify it: the LOD selector (`Shape_DrawAtDetailLevel`, `004033e4`) divides it by viewing distance to estimate on-screen size, and vtable `+0x10` (`SimObject_GetShapeRadius`, `0046b80c`) hands it to every coarse hit reject ([`../simulation/hit-detection.md`](../simulation/hit-detection.md#the-three-radius-slots)). It tracks `BASES.DAT`'s own `+0x2a` radius ([`bases-dat.md`](bases-dat.md#the-type-record)) within about a fifth across all 45 records (6334/5600, 10325/9600, 3577/3600).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The tail of the record is a sub-record size, a sub-record count, three scalars, an opaque block, then count × size raw bytes | It consumes exactly the same bytes, so a reader can parse every retail record correctly while naming all of it wrongly. It is the one structure in [the collision volume](#the-collision-volume): five scalars and a fixed 1024-byte table, then the height codes |
| The third head field is a shape id | It is the bounding radius ([above](#the-bounding-radius--shape8)); its value tracks `BASES.DAT +0x2a`, not any index |
| Step 3 is a count and 32-byte BSP records, then a count and an `int16` array, since `TSBSPPart_RenderNode` (`00476a1c`) walks points at a 32-byte stride | Both counts come first, then the `int16` frame counts, then the 32-byte node transforms. With every retail transform count 0 the two readings consume the same bytes, but only this one puts each count with its own array |
