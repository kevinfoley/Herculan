# `dat\BEAM.DAT` and `dba\BEAMTEX.DBA` (beam appearance)

The two resources `Beam_LoadResourceTables` (`0040b6e0`) loads once at startup. It is the beam module's init, named by the `BEAM.CPP` string at `00498781`. How the tracer object is built and drawn from them is in [`../simulation/beam-visuals.md`](../simulation/beam-visuals.md).

## `dat\BEAM.DAT`

```
int16 count
{ int16 halfWidth; int16 colourIndex; int16 frame; }[count]
```

Read straight into the table at `DAT_004a9888`. Six bytes a record; retail has 10. The table is **indexed by the firing `PROJ.DAT` record's subtype id**, not by weapon id, the same way `BULLETS.DAT` is ([`../simulation/projectiles.md`](../simulation/projectiles.md#datbulletsdat)).

| Field | Meaning |
|---|---|
| `halfWidth` | half-width in world units. The draw hands it to the perspective scale at `0048c4c0`, and the jagged path adds it to a node's z |
| `colourIndex` | palette index. Only the jagged (ELF) path uses it, as a flat fill; a straight beam ignores it — [`beam-visuals.md`](../simulation/beam-visuals.md#beamdats-colour-index-is-the-fill-brush-and-only-the-jagged-path-uses-it) |
| `frame` | `BEAMTEX.DBA` frame index |

Retail, frame 0 throughout:

| id | Weapon | Half-width | Colour |
|---|---|---|---|
| 0 | PBW | 60 | 10 |
| 1 | ELFW | 30 | 104 |
| 2 | BPBW | 120 | 10 |
| 3 | L100 | 20 | 88 |
| 4 | L200, L400 | 25 | 88 |
| 5 | L300, L500 | 30 | 88 |
| 6 | PBW2 | 75 | 1 |
| 7 | ELF2 | 45 | 99 |
| 8-9 | none: L400's and L500's own records, which their `PROJ.DAT` records do not name ([`proj-dat.md`](proj-dat.md#lookup)) | 35, 40 | 88 |

## `dba\BEAMTEX.DBA`

Loaded into the descriptor table at `DAT_004a988c` by `BitmapArray_PackToAtlas` (`00469f38`); the 20-byte descriptor layout, and the `+0x12` flag that marks a frame containing palette index 0, are in [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#the-frame-descriptor-table-and-the-span-routines-dbsim).

Retail ships **one** frame, 128x25, and every `BEAM.DAT` record points at it. Every row is a single repeated palette index: 11 at both edges, then the ramp 84..95 in to the middle and back out. The frame holds no index 0, and nothing in it varies along the beam's length, so it is a pure cross-section. In a `WORLD<n>.DPL` that ramp is the fire ramp, dark orange (184, 92, 20) climbing to near-white (252, 248, 228).
