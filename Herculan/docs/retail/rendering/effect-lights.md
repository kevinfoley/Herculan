# Effect light sources (DBSIM.EXE)

Addresses are DBSIM virtual addresses.

An impact effect can carry a dynamic light. It is not a light in the renderer's own list: it is a slot in a separate *effect light manager*, and that manager synthesises a throwaway renderer light per drawn object, per frame, from whichever slots are close enough to matter. The manager is the only producer of dynamic lights in the binary: `Light_Register`'s four call sites are the manager's two light getters, `Light_CreateMissionSun` (`00461240`), and `Light_ReregisterPersistent` (`00407878`), whose list holds only the sun. A muzzle flash or a beam lights nothing.

The table field that starts one is `EXPLOS.DAT`'s light mode, in [`../formats/explos-dat.md`](../formats/explos-dat.md#type-row-0x28-bytes); the effect that owns the light is [`../simulation/impact-effects.md`](../simulation/impact-effects.md). The shade byte a light finally moves is `Light_ComputeShadeForFace`, in [`dts-texture-binding.md`](dts-texture-binding.md). This doc owns everything between the two.

## The manager — `DAT_004a968c`

One singleton, 0x404 bytes, built by `LightManager_InitSubsystem` (`004076e4`) at startup. Twenty slots of stride `0x23` from `+0x6c`, a `Rtl_VectorNew` array:

| Offset in slot | Field |
|---|---|
| `+0x00` | free flag — 1 free, 0 in use |
| `+0x01`, `+0x05`, `+0x09` | world position, three int32 |
| `+0x0d` | intensity as int16, 0-255; what the cull radius is computed from |
| `+0x0f` | `A`, copied from manager `+0x10` |
| `+0x13` | `B`, copied from manager `+0x14` |
| `+0x17` | cull radius, derived — see below |
| `+0x1b` | intensity again, as int32; **the field the per-object selection reads** |
| `+0x1f` | the renderer light object currently standing in for this slot, or 0 |

Manager fields: `+0x00`..`+0x08` the camera position (`LightManager_SetCameraPosition` (`0040707c`), written once a frame from `Sim_RenderFrame`), `+0x0c` a literal 10000, `+0x10`/`+0x14` the `A`/`B` above, `+0x18` the count of live slots and `+0x1c` their pointer array (twenty entries, ending at `+0x6b`), `+0x328` an embedded 0x34-byte light object of type 0 (ambient), which `LightManager_SelectLightsForObject` unregisters before every object and none of `Light_Register`'s call sites passes ([Open](#open)), `+0x35c` and `+0x3b0` the free lists the two synthesised light types are recycled through.

### `A` and `B` are 0 and 62

`LightManager_Construct` (`00406e44`), the constructor, calls `LightManager_SetFalloffConstants(mgr, 2000, 3000)` (`00406ee4`); `LightManager_InitSubsystem` (`004076e4`) then immediately calls it again with `(10, 2000)`. The setter stores both **shifted right by 5**, so the values that survive into every calculation below are

```
A = 10   >> 5 = 0
B = 2000 >> 5 = 62
```

`A` is zero, and it is zero in the three places it is used — the cull radius's additive term and the denominator offset of both falloffs. The startup call is what counts; the constructor's pair never reaches a frame.

`LightManager_RecomputeCullRadius` (`0040735c`) recomputes the cull radius on a claim and on every intensity change:

```
slot.cullRadius = (slot.intensity * B * 0x20) / 10 + A * 0x20     // = intensity * 198.4
```

At full intensity that is 50,592 world units, about 300 m.

## Claiming a slot

`Explosion_Construct` (`00407f1c`) branches on the type row's light mode at `+0x06` and tests it **only against zero**. Values 1 and 2 both take the same branch. The only other reader is the unreferenced proximity test `Explosion_ProximityTest` ([`../formats/explos-dat.md`](../formats/explos-dat.md#type-row-0x28-bytes)), so the two are indistinguishable at runtime; the split is authoring intent that the code never honoured. Fourteen of the twenty-two rows are nonzero, in `EXPLOS.DAT` and `EXPLOS2.DAT` alike.

Nonzero allocates a 0xe-byte handle from `EffectLightPool` (`004a9682`), a pool of **three** — `LightManager_InitSubsystem` builds it with `Pool_Init(pool, 3, 0xe)` — and runs `EffectLight_Construct` (`00407604`), which is the whole of the attachment. `Explosion_Construct` tests the allocation: with the pool empty it stores 0 at `effect+0x53` and the effect runs its whole life without a light. When the effect ends, `Explosion_Destruct` (`00407e48`) only queues the handle (`ObjectPool_QueueForDelete`). It returns to the pool, and its destructor releases the slot, when `EffectLightPool_FlushDeletes` (`004077e8`) runs: a phase-5 subsystem hook, so at the top of the `Sim_RenderFrame` the main loop runs after each `Sim_MainTick` (`Subsystem_RunPhase`, `00401d94`). `Explosion_Destruct` itself runs from another phase-5 hook, `ExplosionPool_FlushDeletes` (`00407b3c`), which is registered after this one: a phase runs its hooks in registration order, and registration order is the order of the static initialisers in Borland's `_INIT_` table, all at priority `0x20` here, which the runtime's walker (`CW3220.DLL` `0041a634`) runs first-entry-first among equal priorities. `LightManager_RegisterSubsystem` (`004078c8`, entry `004a7a74`) comes before `Explosion_RegisterSubsystem` (`00408284`, entry `004a7a7a`). So the handle of an effect that ends on a tick is queued after that frame's light flush has run and returns at the next one: the slot keeps its last intensity and lights that frame's draw, and the handle counts against the three through the whole of the following tick. At most three effect lights exist at once.

```
slotIndex   = LightManager_ClaimSlot(mgr, worldPoint)                // 00406f38
handle+0x0c = slotIndex
LightManager_SetSlotIntensity(mgr, slotIndex, row[+0x08])           // 00407048, ramp entry 0
```

`LightManager_ClaimSlot` (`00406f38`) seeds the slot's intensity to `0xff` and copies `A`/`B` in; the `LightManager_SetSlotIntensity` (`00407048`) call right behind it overwrites the intensity with the row's first ramp entry and writes `+0x1b`, which the claim itself leaves alone ([Open](#open)). `Explosion_TickUpdate` then calls `EffectLight_SetIntensity(handle, ...)` (`004076a0`) — the same setter through the handle — with the ramp entry for the frame, `& 0xff`, as each frame is stepped, and `EffectLight_Destruct` (`0040765c`) releases the slot when the queued handle is flushed.

The intensity is read from the ramp at the **new** frame index, and the tick reaches that line only when the stepped frame is nonzero, so ramp entry 0 is used exactly once, by the constructor.

### The allocator has no full-table guard

`LightManager_ClaimSlot` (`00406f38`) has no full-table guard. With all twenty slots busy it falls out of its scan with the destination register still holding **the caller's `worldPoint` argument** and the returned index at `0x14`. It then writes the slot record through that pointer — 0x1b bytes with the cull radius, over the 12-byte position vector `Explosion_Construct`'s caller passed and the 15 bytes after it — and appends the pointer to the live array, whose twentieth entry ends at `+0x6b`, so the twenty-first lands on slot 0's free flag and the first three bytes of its position. Index `0x14` resolves to `mgr + 0x6c + 20 * 0x23` = `mgr + 0x328`, the embedded light object: `LightManager_SetSlotIntensity` (`00407048`) and the cull-radius recompute write into it, and `LightManager_ReleaseSlot` (`00406fbc`), when the effect ends, sets its vtable pointer's low byte as the "free" flag and then fails to find it in the live array, so the stray entry stays.

None of this runs in retail. `EffectLight_Construct` is the allocator's one call site, and the three-entry handle pool ([above](#claiming-a-slot)) keeps at most three slots claimed at once.

## Per-object selection — `LightManager_SelectLightsForObject` (`00407098`)

Called from `ObjList_DrawEntryRender` (`0042876c`), **once per depth-sorted render entry, just before that object is drawn**, with the entry's cached position and its bounding radius — the entry's `+0x10` short, filled by `ObjList_DrawCellObjects` from `SimObject_GetShapeRadius` (vtable `+0x10`).

For each live slot, with `dist` the distance from the object to the slot:

```
if (dist >= slot.cullRadius)  { release the slot's light; slot.light = 0; continue; }

d = dist >> 5
angle = Math_Atan2Guarded(d, radius >> 5)          // (x, y) order: atan(radius / dist)
if (d != 0 && angle < 8000)  -> DIRECTIONAL
else                         -> POINT
```

`8000` in the sim's binary-angle unit is 43.9 degrees, so the test is `radius / dist < 0.964`: the object subtends less than that from the light, i.e. the light is more than about one bounding radius away. **Far is directional, near is point** — the light is only made a real point light once it is close enough that the object's own extent matters, which is the standard approximation and not the inversion the argument order invites. `Math_Atan2Guarded` takes `(x, y)`, and reading it as `(y, x)` mirrors the test about the 45-degree line and swaps the two branches.

Both branches recycle through the manager's free lists (`LightManager_RecycleLight` (`00407500`), `Light_GetOrCreateDirectional`, `Light_GetOrCreatePoint`) and reuse the slot's existing light object untouched whenever its type already matches, so consecutive objects mutate one light rather than allocating.

**Directional.** Intensity is attenuated by distance at selection time, and the direction is rebuilt to point from the light at the object:

```
atten     = min(0xff, (B << 8) / (d + A))          // = min(255, 15872 / d)
intensity = slot.intensity * atten >> 8
direction = (objectPos - slotPos) * 0x800 / dist   // length 0x800
```

**Point.** Intensity passes through unattenuated; the falloff is deferred to the shade calculation, which is handed the same two constants:

```
intensity  = slot.intensity
light+0x34 = A << 5   = 0        // denominator offset
light+0x38 = B * 0x20 = 1984     // numerator
light+0x3c = intensity * 1984    // unread by the shade path
position   = slotPos
```

The synthesised light is registered into the ordinary ten-slot active list (`DAT_006c6130`) beside the mission sun. `Light_Register` refuses an eleventh entry, but effect lights never reach it: the sun holds one entry and the handle pool allows three effect lights. `Raster_SetModelTransform` (`0048c338`) re-transforms every registered light into model space (`light+0x22` position, `light+0x2e` direction) for each node it composes, gated on `DAT_006cbc88`, which `Light_ResetSystem` (`0048dbfc`), the per-mission light reset, sets to 1 through `Light_EnableModelSpaceTransform` (`0048dd64`) ([Open](#open)). The flag is the byte after the polygon pipeline's scratch block, `006c6158`–`006cbc87`, which the reset's memset clears up to it. The block ends with `Poly_ScreenPoints` (`006cbb86`), room for 32 screen points of 8 bytes (`Raster_StaticInit`, `0048dc7c`), and its count word at `006cbc86`. `Poly_ProjectShapeVertices` (`0048c848`) and `Poly_ProjectViewVertices` (`0048cbd8`) write a face's points by its vertex number with no bound, so a 33rd point would land on the count and the flag. No retail face has more than eight corners, in every `dts\*.DTS` and `dgs\*.DGS` the VOLs ship, and a near-plane clip of an eight-corner face is far short of 32 points, so with retail data the writes stop at the count.

### A part drawn through a saved state shades the effect lights in another node's frame

**An effect light can light part of a machine or an animated structure as though that part stood where another part of it does.** The sun is unaffected. `ShapeNode_InstallTransform` (`00476030`) composes a node's transform, which re-transforms every registered light into it, the first time one of the object's parts names that node, and saves the renderer's state in that node's slot (`Raster_SaveState`, `0048d60c`). A later part on the same node, unless it directly follows another part on that node, gets the slot back through `Raster_RestoreState` (`0048d6e0`) instead; a part with no node (`-1`) after a node's part gets back slot 0, the object's own state, which `ShapeInst_BindNodeTransformArray` (`00475fd8`) saved before the first part. Every part installs its own transform as it draws — `TSPartList_Render` (`004766fc`), `TSBSPPart_Render` (`00476b0c`), `TSCellAnimPart_Render` (`004767e4`) and `TSGroup_RenderPolys` (`004758c8`), each through `TSGroup_BindNodeTransform` (`00476014`) — and none puts the previous state back afterwards.

Both functions step through the slot's saved light copies while reading the light from the fixed address `[006c6130]`, so the restore puts back list entry 0 alone, and the other lights keep the model-space copies of the node composed last. Entry 0 is the sun: `Light_CreateMissionSun` (`00461240`) registers it at mission start, `Light_Register` (`0048bd44`) appends, and `Light_Unregister` (`0048bd8c`) fills a hole with the last entry, so the effect lights `LightManager_SelectLightsForObject` (`00407098`) creates and recycles per object come and go behind it, and the manager's `LightManager_RecycleLight` (`00407500`) unregisters only its own lights. A part drawn through a restored state, or straight after one on the same node, or on no node once any node has been composed, therefore takes the sun correctly and every effect light in the frame of the last node composed before it: the light's copy in that node's space, read as though it were in the part's own.

The shapes this reaches are the 21 machine shapes — `ACHILLES`, `APOCA`, `CERBERUS`, `COLOSSUS`, `DIABLO`, `HEADHUNT`, `HYPERION`, `MAVERICK`, `MIRIMAC`, `MONGOOSE`, `OGRE`, `OUTLAW`, `PITBULL`, `RAMSES`, `RAPTOR2`, `RAZOR`, `SAMSON`, `SCARAB`, `SPIDER`, `STINGRAY` and `TOMAHAWK` — and the animated structures in `BASES_AN.DTS`, and each draws through at most one BSP part: a machine's main part, and the part of the detail level a structure is drawn at, where that level has one. Across every `.DTS` root the VOLs ship, each child of a BSP part installs at most one node, the same one in every cell, and `TSBSPPart_RenderNode` (`00476a1c`) draws the children back to front from the camera, so which of them take their effect lights in another node's frame changes with the view. Two layouts let a child follow another node's first child:

- **A node owns more than one child.** Every root of every machine but the `RAZOR`, except `SAMSON.DTS`'s crudest. On `ACHILLES.DTS`'s finest level nodes 4 and 11 own 3 and 11 of the 23 children; on `DIABLO.DTS`'s, nodes 1, 2, 3 and 11 each own more than one. Whenever another node's child falls between two of one node's, the later of those, and any of that node's children straight after it, take the effect lights in the other node's frame.
- **Children on no node sit beside a node's.** Every root of `RAZOR.DTS`, where one child is on a node and the rest on none, `SAMSON.DTS`'s crudest root, `RAPTOR2.DTS` root 4, and `BASES_AN.DTS`'s animated structures, whose parts mix node-less groups with one to three nodes' groups. A node-less child drawn after a node's takes the effect lights in that node's frame.

A fitted hardpoint draws in its slot's turn: the mount's render, `ConfigPart_Render` (`0040316c`), installs the slot's node, composing it when no earlier child did, and every part of the gun carries that node or none (`Shape_StampTransformId`, `00417530`), so the gun is lit as a part on the slot's node. An empty hardpoint's blank record installs nothing. No `.DGS` part carries a node.

## What a light contributes

The directional term is the sun's own curve, in [`dts-texture-binding.md`](dts-texture-binding.md); the only difference is that the direction vector is built at length `0x800` rather than the sun's `0x1000`, which halves the raw dot and makes the term peak at `intensity` instead of `2 * intensity`.

The point term is `Light_ComputeShadeForFace`'s type-2 branch, and is a different shape:

```
disp = lightPosModel - faceCentre
dot  = disp . normalAsInt                       // normal length 0x800
if (dot > 0) shade += intensity * light+0x38 * (dot / (|disp| + 1)) / (light+0x34 + |disp|) >> 11
```

With `A = 0` and `B * 0x20 = 1984` that reduces to

```
shade += intensity * 1984 * cos / dist
```

**The two branches carry the same falloff.** Substituting `d = dist >> 5` and `A = 0` into the directional attenuation gives `slot.intensity * 1984 / dist` as its peak too, so the branch boundary is smooth in magnitude and differs only in the angular term — half-lambert `(1 - cos) / 2` for the directional approximation against `cos` for the point light. That agreement is what confirms the `A`/`B` reading; getting either constant wrong makes the two branches disagree by a factor at the crossover.

So a full-intensity frame adds 255 to a face turned squarely at it within about 12 m, and roughly 100 at 30 m.

## Why retail reads unlit

The intensities are real and large, and the effect is still hard to see. Five structural reasons, none of them a brightness of zero:

- **Terrain cannot respond.** `Terrain_BuildCellSurfaceAndShade` bakes a cell triangle's shade byte once at zone load ([`terrain-lighting.md`](terrain-lighting.md)). The ground — the largest surface near any impact — never flashes, whatever is registered when it draws.
- **Only depth-sorted entries are lit at all.** `LightManager_SelectLightsForObject` (`00407098`) runs from `ObjList_DrawEntryRender`. `ObjList_DrawCellObjects` draws class-tag-9 objects immediately and fullbright, bypassing the light pass entirely.
- **Three at a time.** The handle pool holds three ([Claiming a slot](#claiming-a-slot)), so in a heavy exchange most light-bearing effects carry no light at all.
- **About half a second.** Every retail row holds a frame for one sim tick, and a light lasts as long as its effect's flipbook — 7 to 15 frames on the fourteen light-bearing rows, 10 or 11 on eleven of them — and one frame more ([Claiming a slot](#claiming-a-slot)): about 0.3 to 0.6 s when the sim keeps up with its 40 ms frame cap ([`../simulation/dbsim-physics-notes.md`](../simulation/dbsim-physics-notes.md)).
- **The sun has already saturated most of what is visible.** The shape curve is `128 + 256 * facing`, so every face within 60 degrees of the sun is pinned at 255 before an effect light adds anything. Only surfaces turned away from the sun have headroom, and the billboard flipbook is drawn over the part of the object nearest the light.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| Light mode 1 and 2 select directional versus point | `Explosion_Construct` tests the field against zero, and the only other reader, `Explosion_ProximityTest` (`00408100`), has no reference anywhere in the image. The type is chosen per drawn object by `LightManager_SelectLightsForObject` (`00407098`)'s angular test, and both values reach the same code. |
| `A` and `B` are 2000 and 3000 | Those are `LightManager_Construct` (`00406e44`)'s constructor defaults, overwritten by `LightManager_InitSubsystem` (`004076e4`) before any frame runs. Both calls also shift right by 5, which the raw literals do not show. |
| `Math_Atan2Guarded(d, radius)` makes near lights directional | The helper takes `(x, y)`, so this is `atan(radius / dist)` — the object's angular size. Small angle means far, and far is the directional branch. |
| `Light_ComputeShadeForFace` reads the light's world position | It reads `+0x22`/`+0x2e`, the model-space copies `Raster_SetModelTransform` rebuilds per node. `+0x04`/`+0x10` are the world-space fields `LightManager_SelectLightsForObject` writes. |
| The mission sun is the only entry in the active light list | It is the only *persistent* one, and the only one a mission starts with. Types 1 and 2 are both created dynamically here, into the same ten-slot list. Type 0, ambient, exists once, as the manager's embedded light at `+0x328`, which none of `Light_Register`'s four call sites passes ([Open](#open)). |

## Open

- **Deferred:** no writer of slot `+0x1b` other than `LightManager_SetSlotIntensity` (`00407048`) found by reading the slot-address computations in `00406e44`–`00407388`. `es2_fieldscan.py` does not settle it: it misses the known store at `0040706c`, made through an `ADD EAX,0x6c` rebase.
- **Deferred:** no caller of `LightManager_SetSlotFalloff` (`00406f00`) found by `es2_xref`. It gives one slot its own `A` and `B`, shifted right by 5 as the manager's pair is, and recomputes that slot's cull radius; while it has no caller, every slot carries the manager's `0` and `62`.
- **Deferred:** what registers the embedded type-0 light at manager `+0x328`, if anything: none of `Light_Register`'s four call sites passes it, yet `LightManager_SelectLightsForObject` unregisters it before every object.
- **Deferred:** no store of 0 to `DAT_006cbc88` found. The disassembly dump's one absolute store is `Light_EnableModelSpaceTransform`'s 1. The scratch block it follows is also written through its base `006c6158`, which the near-plane clip routines and `Poly_ProjectIndexedVertices` hold in `ESI`/`EDI`; `es2_fieldscan.py` finds no access at `+0x5b30`, the flag, against about fifty at `+0x5b2e`, the point count.