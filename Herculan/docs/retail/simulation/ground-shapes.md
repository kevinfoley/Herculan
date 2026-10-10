# Ground shapes and HERC shadows — `FlatObj` (DBSIM.EXE)

Addresses are DBSIM virtual addresses.

A ground shape is a flat shape lying on the terrain. Most of them are HERC shadows: a dark octagon under each foot and one under the body, which follow the machine as it walks. The rest are the square a drop pod leaves where it lands, and a growing ring an impact effect can lay, though no retail effect asks for it. All of them are one class, `FlatObj` (Borland RTTI at `0041bcf6`, `0x41` bytes, type tag 9 at `obj+0x04`), out of one pool, `g_FlatObjPool` (`004a9711`, Borland `ObjPool<FlatObj>`, 150 entries), each drawing one root of the theater's flat shape set.

## The shape set — `FlatObj_LoadResources` (`004097a8`)

`World_LoadTheater` (`0042e010`) ends with it, passing `World_FlatSetSelector` (`0049aeea`), a field of the theater's descriptor ([`../formats/wld-world.md`](../formats/wld-world.md#the-worldn-descriptor--layout)). Zero selects the set `flat`, anything else `flat2`. It loads `dts\<set>` through `Shape_LoadAllRoots` into `g_FlatShapes` (`004a971c`, count `004a9720`), packs `dba\<set>` with `BitmapArray_PackToAtlas` into `004a9726` and points every root's bound-bank field `+0x26` at `004a9722` ([`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#dba-binding)). Then it builds the pool (`Pool_Init`, 150 × `0x41`) and `g_FlatObjDeleteQueue` (`004a9715`): an 8-byte header over a 100-byte pointer array, capacity `0x19` at `+6`.

All ten retail descriptors select `flat2`, and only `FLAT2.DTS` and `FLAT2.DBA` ship. `FLAT2.DTS` has four roots, each a single group; sizes are in world units, and every root lies in its model XY plane:

| Root | Geometry | Extent | Radius | Laid by |
|---|---|---|---|---|
| 0 | one `TSSolidPoly` octagon, palette 224 | 300 across, 968 along model Y, 100 ahead of its origin at the centre | 490 | a HERC, the shadow under each foot |
| 1 | a five-cell `TSCellAnimPart` of `TSSolidPoly` octagons: half-widths 1024 (96), 1536 (96), 2048 (96) round 1024 (97), 2048 (97), 1536 (97) | up to 4096 | 2290 | an impact effect |
| 2 | one `TSSolidPoly` octagon, palette 224 | 1364 across, 768 along | 709 | a HERC, the shadow under its body |
| 3 | one `TSTexture4Poly` square, frame 0 of `FLAT2.DBA` | 4000 | 2829 | a landed drop pod |

The radius is the root's own `shape+8`, which `SimObject_GetShapeRadius` (`0046b80c`) reports and the draw's terrain conform probes at ([The draw pass](#the-draw-pass)).

## The class

| Vtable slot | `FlatObj_Vtable` (`0049a262`) |
|---|---|
| `+0x00` | `FlatObj_Draw` (`0040991c`) |
| `+0x04` | `SimObject_GetPosition` (`0046b801`), `obj+0x26` |
| `+0x08` | `FlatObj_Destruct` (`0041bd46`) |
| `+0x0c` | `Stub_ReturnZero` (`004785bf`) |
| `+0x10` | `SimObject_GetShapeRadius` (`0046b80c`) |

**It has no tick.** Of the nine references `es2_xref.py` finds to `g_FlatObjPool`, seven are code — the loader, `FlatObj_RegisterSubsystem` (`00409940`, which nulls it at static init), `Sim_FlushDeleteQueue`, the three spawners and the draw pass — and the draw pass is the only one that walks the pool. A shape moves only when its HERC moves it.

Every spawner inlines the same construction after `Pool_Alloc` (`00471a24`): `SimObjectBase_Constructor(obj, g_FlatShapes[root])`, which zeroes the position and the euler angles and builds a fresh shape instance, then the vtable and tag 9. **None of the three checks the allocation.** Each skips the construction on a null, then writes the position through the pointer anyway (`Meteor_Tick` from `00409e03`, `Explosion_Construct` at `0040802f`, `Mech_Constructor` at `00415efb`), so a spawn into a full pool faults.

## A HERC's shadows

`Mech_Constructor` (`00415bb0`, `00415e25`-`00415f11`) counts the type record's byte list at `+0x72` up to its first negative byte into `mech+0x23c`, allocates that many pointers at `mech+0x238`, and builds one shadow per entry, the byte naming the root. Each is built at z = −100000 (`00415efb`), out of the draw's range from anywhere until it is first placed.

The same entries drive the footfalls: the byte at `+0x72` is also the entry's kind, and `typeRec+0x77` names the shape part it follows.

| Chassis | `+0x72` (root and kind) | `+0x77` (part) |
|---|---|---|
| every HERC but the two below | 0, 0, 2 | 14, 15, 12 |
| PITBULL | 0, 0, 0, 0 | 14, 15, 22, 23 |
| SPIDER | none | — |

So a biped casts three shadows: one under each foot and one under part 12, its body. The count is the list's, not the leg count (`typeRec+0x4a`, 2 on a biped).

**Placement** is the first half of each pass of `Mech_PlaceLegsOnGround`'s loop (`004195c8`), run from `Mech_MovementTick` and `Mech_BehaviourRamTick` every movement tick. For each entry still holding a shadow it resolves the part's node through the shape instance, puts the node's translation through the machine's frame (`Transform_ApplyToPoint`) into the shadow's position, copies the machine's heading (euler `+0x10`) and clears the shadow's matrix-valid flag. That runs for every entry whatever the machine is doing; the footfall tests after it are the loop's other half ([`mech-locomotion.md`](mech-locomotion.md)). The draw then presses each shadow onto the ground under its part, so the shadows follow the feet and the body through a walk, a fall and the wreck.

**Loss.** The leg branch of `Mech_ComponentDamageWrite` (`00418144`-`00418190`) queues a leg's shadow for deletion (`ObjectPool_QueueForDelete`) and nulls its slot when that leg's servo reading is `0x100`, so that foot casts no shadow, and `Mech_PlaceLegsOnGround` skips the entry from then on, footfall included ([`component-damage.md`](component-damage.md#going-out-of-the-fight)). The no-wreck branch queues every entry, and the SPIDER, the one chassis it runs for, has none. `es2_fieldscan.py` finds `mech+0x238` read by those two branches, the constructor and `Mech_PlaceLegsOnGround`, and written only by the constructor.

## An impact effect's shape

`Explosion_Construct` (`00407f1c`, `00407fc5`-`0040804c`) lays root 1 at the effect's point when the `EXPLOS.DAT` row's `+0x04` is nonzero, keeping the pointer at `effect+0x4f` ([`../formats/explos-dat.md`](../formats/explos-dat.md#type-row-0x28-bytes)). `Explosion_TickUpdate` (`0040813c`) steps the shape's sequence-0 cell each time it steps its own frame to a nonzero one, modulo the shape's own cell count, so the ring grows through its five cells beside the flipbook. The effect's destructor, `Explosion_Destruct` (`00407e48`), queues the shape for deletion with the effect.

**No retail row sets `+0x04`**: it is 0 on all 22 rows of both `EXPLOS.DAT` and `EXPLOS2.DAT`, so root 1 is never drawn in a retail mission.

## A drop pod's shape

`Meteor_Tick` (`00409d2c`) lays root 3 at the pod's landing point once the pod has finished opening, whenever the pod carried a group, delivered or not ([`mission-deployment.md`](mission-deployment.md#the-drop-pod--meteor)). The heading is 0 and the pod keeps no pointer, so nothing frees it: the square stays for the rest of the mission.

## The draw pass

`Scene_SubmitFrameObjects` (`0042841c`) walks the pool first every frame, from the tail with `Pool_Prev` (`00471b64`), which is oldest first: `Pool_Alloc` puts each new node at the head of the in-use list. A shape whose position is under 30000 from the view object's by `Math_DistanceBetweenPoints` goes through `Scene_SubmitObject` (`004282d8`) to `Scene_SubmitObjectWithRadius` (`0042837c`), which files it under the cell `HeightGrid_PickDrawCell` (`0046e528`) picks from its position and radius ([`../rendering/terrain-drawing.md`](../rendering/terrain-drawing.md#heightgrid_pickdrawcell-0046e528)). The pick reads the position before the draw conforms it, so a HERC's shadows are measured at their part's height.

**Tag 9 is drawn on the spot**, ahead of the rest of its cell's objects, with the ramp's row count zeroed around the draw ([`../rendering/terrain-drawing.md`](../rendering/terrain-drawing.md#objects-in-the-walk)). So a ground shape is painted over its own cell's ground, and over whatever else is already on the screen where it lands; every cell the walk paints later paints over it. The solid roots are unlit and fog with the fade their cell's quad installed ([`../rendering/distance-fog-and-sky.md`](../rendering/distance-fog-and-sky.md#what-gets-faded)); the zeroed row count makes root 3's textured face a plain palette copy, with no light term and no fog ([`../rendering/dts-texture-binding.md`](../rendering/dts-texture-binding.md#tstexture4poly--frame-index-ramp-row-by-light-fullbright-on-demand)). A shape the pick puts off the grid is drawn after all the ground, from the no-cell bucket ([`../rendering/terrain-drawing.md`](../rendering/terrain-drawing.md#after-the-walk--objlist_drawafterterrain-0042883c)).

`FlatObj_Draw` (`0040991c`) conforms the shape to the terrain with `SimObject_ConformToTerrain` (`004029d8`, [`ground-vehicles.md`](ground-vehicles.md#terrain-conform--simobject_conformtoterrain-004029d8)), then installs its model transform and draws its shape instance. The conform sets the shape's pitch, roll and Z from four ground samples at its radius, so the shape lies on a plane through them, not on the terrain's own surface. The result stays on the object, and the next conform rotates its probes through the pitch and roll this one left.

## Lifetimes

Deletion goes through `g_FlatObjDeleteQueue`. `Sim_FlushDeleteQueue` (`00409904`) runs `ObjectPool_FlushDeleteQueue` over it, which calls `FlatObj_Destruct` and `Pool_Free` on each entry. `FlatObj_RegisterSubsystem` registers the flush at subsystem phase 5, which `Sim_RenderFrame` runs at the start of every frame's draw (`0045fba5`), and `Mech_ComponentDamageWrite` calls it at its end. A queued shape is therefore never drawn again.

`ObjectPool_QueueForDelete` (`0047851c`) appends without testing the capacity at `+6`, so a 26th deletion between two flushes would write past the array. A damage write queues at most four and flushes at its end, and only an impact effect's end queues from the tick, which no retail row can reach.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `EXPLOS.DAT`'s `+0x04` attaches a trail object at `effect+0x4f` | It lays a `FlatObj` on the ground: root 1 of the flat set, stepped with the effect and deleted with it |
| `mech+0x238` holds a HERC's legs as child objects | They are its shadows, `FlatObj`s drawn from the flat set; the legs are nodes of the machine's own shape |
| The HERC part list runs for the leg count's (`typeRec+0x4a`) entries | `Mech_Constructor` counts to the list's first negative byte: three on a biped, the third the body's |
| `SimObject_ConformToTerrain` is the ground vehicle's alone | `FlatObj_Draw` calls it on every ground shape it draws |
| A shadow lies on the terrain's surface, so it only has to beat the ground under it by a little depth | It is a flat plane through four ground samples, and on uneven ground it passes under the terrain and over it. What keeps it visible is the paint order: it is drawn after its own cell's ground and before every nearer cell's |

## Open

- **Deferred:** `bnd\FLAT.BND`, which ships beside the `FLAT2` pair, has not been examined.
- **Open:** whether a RAZOR's shadows are ever placed. It carries the biped list, but its flight states' move is `Razor_MovementTick` ([`razor-flight.md`](razor-flight.md)), which does not call `Mech_PlaceLegsOnGround`; while it flies its shadows stay at their build depth.
