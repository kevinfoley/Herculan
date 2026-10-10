# DBSIM.EXE beam visuals: the tracer object and how it is drawn

What a fired beam looks like. The firing side — trigger, dispatch, shot record, hit resolution — is in [`weapon-firing.md`](weapon-firing.md); the effect a shot spawns where it lands is in [`impact-effects.md`](impact-effects.md).

## Chain

`Bullet_FireBurst` (`0040bf74`) resolves the hit, then builds the visual from the already-shortened distance:

1. `Sound_PlayAt(0x0b, muzzlePoint)` — catalog id `0x0b` is `laser1.wav`, placed by distance and bearing. See [`audio.md`](audio.md).
2. The far end is rebuilt from the shot's own frame as `transform(0, travelled, 0)`, where `travelled` is the raycast's distance or the weapon's full range when it struck nothing.
3. One tracer object per **5000-unit** span, allocated from the pool at `g_ProjectilePool` (`004a9746`), plus a final one for the remainder. The loop advances the shot transform's translation by a 5000-unit step each iteration and writes it back, so each tracer spans start→start+step.
4. Subtype ids **1 and 7** (ELF, ELF2) skip the span loop entirely and spawn one object of a different shape — see [ELF](#elf-and-elf2--the-jagged-branch).

The `local_20` values written before each allocation (`0x14`, `0x2c`, `0x20`, `0x44`) are Watcom exception-frame state, not data.

## The tracer object — `BeamTracer_Ctor` (`0040b804`)

Constructed as `(obj, subtypeId, startPoint, endPoint, owner)`; vtable `BeamTracerVtable` (`004987c4`), type 3.

| Field | Meaning |
|---|---|
| `+0x41`, `+0x52` | subtype id (byte and short copies) |
| `+0x4a` | owner |
| `+0x54` | quad count — 1 for a straight beam |
| `+0x55` | jagged flag — 0 straight, 1 ELF |
| `+0x56` | point count — 2 for a straight beam |
| `+0x58` | point array, 12 bytes per point |
| `+0x5d` | life timer |

A straight beam stores exactly two points: the muzzle and the hit.

**Lifetime is one tick.** The timer arms at `0x38` = 56, in the same Q8-of-125 ms unit as every other simulation timer, so 27 ms. Vtable `+0x14` (`BeamTracer_LifeTick`, `0040c2a0`) is one `Math_CountdownTimerTick` and nothing else, and `Sim_MainTick` frees the object the tick it returns zero. Since 56 is less than one `SimTickDelta` (81 at the 40 ms frame cap), a tracer never survives a second tick however fast the machine runs — which is what makes a held trigger read as separate flashes rather than a continuous beam.

`Sim_MainTick` walks this pool **before** the machine list, so a tracer spawned during a machine's update is not counted down until the tick after.

## Appearance data

`Beam_LoadResourceTables` (`0040b6e0`) loads `dat\BEAM.DAT` and `dba\BEAMTEX.DBA` once at startup; their layout and the retail records are in [`../formats/beam-dat.md`](../formats/beam-dat.md). The draw reads the tracer's subtype id (`+0x52`) into `BEAM.DAT` for a half-width, a palette index and a `BEAMTEX` frame. Retail's one frame is a pure cross-section ribbon, so the half-width is all that tells straight beams apart.

## Drawing — `BeamTracer_Draw` (`0040bc14`, vtable slot 0)

Per quad:

1. Both points to view space (`Raster_ModelToView`, `0048c470`), then the pair clipped against the near plane (`Beam_ClipSegmentToNearPlane`, `0040bb4c`); a pair wholly behind it is dropped.
2. Both projected to screen (`Raster_PerspectiveDivide`, `Raster_ProjectToScreen` (`0048c5c4`)).
3. Half-width in pixels at each end: `Raster_PerspectiveScale(width, viewZ)` (`0048c4c0`) = `(width << shift) / z`, then `if (< 2) = 2`. This floors the **half**-width, so a beam is never narrower than four pixels.
4. Four vertices: each screen point stepped ±(half-width) along the segment's 2D perpendicular, normalised in Q11.
5. UVs from the frame descriptor: u runs along the beam's **length**, v across its width.
6. `Raster_DrawPolygon(4, verts, 0, page, NULL, 0)` (`00468310`).

No z is written — the vertex struct's `+8` is left untouched.

### The fill is a plain texture copy

A beam uses `Raster_DrawPolygon`'s **mode 0 with the transparency argument zero**, which is `Raster_SpanTextured`'s opaque half: the palette byte at `atlasPage[v][u]` goes to the framebuffer unchanged. The non-zero form is a colour-key skip of index 0, not blending. **There is no alpha, no shade level and no colour lookup anywhere in this path.** The mode table is in [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#the-frame-descriptor-table-and-the-span-routines-dbsim).

### `BEAM.DAT`'s colour index is the fill brush, and only the jagged path uses it

Before either branch runs, the draw installs `{0, colourIndex}` at the graphics context's `+0x22c`. That field is **the rasterizer's fill brush**: `Raster_InstallRenderContext` (`00480c38`) sets the clip block to `ctx + 4`, so `ctx+0x22c` is the `clipBlock+0x228` that `Raster_DrawPolygonDispatch` reads and dispatches on. A brush is `{mode, colour}`; mode 0 with a colour whose top byte is zero is a flat fill of that palette index.

The straight path installs it and then never uses it — it submits through `Raster_DrawPolygon` (`00468310`), whose mode-0 span routine has no colour lookup. The [jagged path](#elf-and-elf2--the-jagged-branch) goes through the polygon dispatch and does.

So every retail straight beam draws the identical orange-to-white ribbon and is told apart only by its width, while ELF and ELF2 are the flat colour their record names. Corroborated by retail screenshots: laser and particle-beam shots are orange-white regardless of weapon, while `ELF` is yellow, matching its index 104.

## ELF and ELF2 — the jagged branch

`BeamTracer_Ctor`'s branch for subtype ids 1 and 7 builds a chain instead of a segment:

- `nodeCount = (char)(distance >> 10) + 1` at `+0x54`, i.e. one node per 1024 units; `pointCount = nodeCount * 2 + 2`, so the loop writes `nodeCount + 1` node pairs.
- The step is the start→end delta rescaled to length `0x400` (`Math_NormalizeVec3ToLength`, `004926e4`).
- Node `k` is the running point; the **last** node restarts from the exact endpoint instead. Every node but the first — the last one included — is then jittered on each axis by `Math_RandomNext() & 0x7f`. The mask leaves that one-sided, 0 to 127, so the chain bows off the straight line rather than wandering either side of it, and the far end does not sit on the impact point.
- Each node writes a **pair**: `points[2k]` with `BEAM.DAT`'s width added to its z, `points[2k+1]` without. So the chain is a ribbon standing vertically **in the world**, not a camera-facing one: seen from directly above an ELF is edge-on.

### The paint uses the polygon renderers' project, clip and fill chain

The paint loop is not beam code past its set-up. It calls `Poly_ProjectIndexedVertices`, then `Poly_ClipRingToNearPlane` if a vertex fell behind the near plane, then `PolyFill_Fill` — the flat-poly chain described in [`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#the-projection-clip-and-fill-chain-dbsim). The beam draw feeds it through the globals that chain reads, published up front: `DAT_006c6970` = point array, `DAT_006c6974` = point count, `DAT_006c6976` = the vertex-index list, and per quad `DAT_006c6968` = 4 vertices with `DAT_006c696a` = `k << (3 - jaggedFlag)` as the offset into that list. `jaggedFlag` is 1 on every object that reaches here, so the shift is always 2 and the other value is unreachable.

The fill is winding-agnostic — `Raster_DrawPolygonEitherWinding` (`004841af`) measures the signed area and hands the other winding to `Raster_DrawPolygonReversed` (`00484116`), [`../rendering/polygon-fill.md`](../rendering/polygon-fill.md#filling-a-polygon) — so the ribbon draws from either side. The index list is the 120-entry table at `DAT_004a9796`, built by `Beam_LoadResourceTables` as `(i >> 1) + {1, 0, 1, 2}[i & 3]` over the **`int16`** table at `00498640`. Read four entries from `4k`, that is `points[2k+1]`, `points[2k]`, `points[2k+2]`, `points[2k+3]` — a wound quad spanning nodes `k` and `k+1`.

120 entries is 30 quads. Retail never approaches it: the longer-ranged of the two is `ELF` at 20000 units (see [`weapons-dat-sim.md`](../formats/weapons-dat-sim.md)), which is 20.

`PolyFill_Fill`'s outline pass, gated on `DAT_006c60d4 != DAT_006c60dc`, draws nothing new here. Those globals are the *default* brush; the beam installed its own on the context, so the redraw is an identical flat fill.

### The muzzle stub is a retail fall-through

The jagged branch does not return. Control drops into the straight-beam code below it, which draws `points[0]`→`points[1]` — for a chain, node zero with and without the width, a stub one half-width long standing at the muzzle — and the enclosing loop runs once per quad, so the identical stub is redrawn `nodeCount` times. It takes the straight path's `BEAMTEX` frame, not the chain's flat colour, and the half-width pixel floor makes it wider than it is long at any real range: a ~4 px orange-white dash at the muzzle.

Nothing in the fire path spawns a muzzle visual for this to be part of — `Bullet_FireBurst` does one thing at the muzzle point, the sound. Logged in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

Retail reference: `Reference/Simulator3.jpg` shows an ELF as a thin bright yellow zigzag.

## Rejected readings

Readings a fresh pass could land on. Each is disproven; do not reintroduce.

| Reading | Why it is wrong |
|---|---|
| `BEAM.DAT`'s colour index is unused — the `+0x22c` pair is a HUD colour pair mode 0 never reads | `ctx+0x22c` is `clipBlock+0x228`, the fill brush, because the clip block is `ctx + 4`. The jagged path dispatches on it |
| `Poly_ProjectIndexedVertices` (`0048c964`) is the projection the `.DTS` renderers use | Theirs is `Poly_ProjectShapeVertices` (`0048c848`), over 6-byte `int16` point triples. The indexed one reads 12-byte `int32` points and has two callers, this draw and `TexPoly_Render` (`0042ff2d`) |
| One of `0048c964`/`0048ce14`/`0048d4b4` redirects the geometry, since the tail reads `points[0]` and `points[1]` with no loop index | That tail is the straight-beam code the jagged branch falls through into — a separate draw, not part of the chain's |
| The chain's last node is the exact endpoint | It is the endpoint **plus** the same jitter every other node gets |
| The index table at `00498640` is bytes | `word ptr [ECX*2 + 0x498640]`; as bytes the quads collapse |
