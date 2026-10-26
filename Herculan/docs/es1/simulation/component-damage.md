# DBSIM.EXE — the hit spheres and the component damage arrays

**A shot that gets through an ES1 HERC's shields finds the component it struck and damages it with ES2's code: the same hit spheres, the same pick of the nearest cluster, and the same cascade from a component's armor into the systems behind it and on to the parts hanging off it ([the sphere test](../../retail/simulation/hit-detection.md#the-test--mech_selectstruckcomponent-0040c9d4), [write and cascade](../../retail/simulation/component-damage.md#the-component-damage-system)).** What a player would see differently:

- a component whose armor is shot away while systems behind it still stand switches to its second shape cell, which on ES2's machines is the damage-shaded one, where ES2's cascade leaves it as it was until the component goes ([Open](#open) on ES1's cells);
- a destroyed component always throws the same debris group, and catches fire only for one of ES2's two fire flags, without putting out a fire the machine already carries;
- a cluster whose bounding sphere's center lies beyond what is left of the shot's reach is passed over, even when the sphere's near side is within it.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order, the reverse of ES2's ([calling conventions](../formats/compiler.md#calling-conventions)). The model is the `.COL` sphere model ([layout](../../retail/formats/collision-spheres.md#layout); ES1's readers in [`bases-dat.md`](../formats/bases-dat.md)): a model is a node count at `+0` and its 10-byte nodes at `+2`; a node its part id at `+0`, its cluster count at `+4` and its 16-byte clusters at `+6`; a cluster its sphere count at `+0`, its 8-byte spheres at `+2`, its component at `+6`, its bounding sphere's center at `+8` and radius at `+0xe`. The damage header is the machine's `+0x19c` ([the damage header](../formats/dmg-damage-file.md#the-damage-header)).

## Where the object files start and end

The code is three object files ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). `es1_boilerplate.py` finds no static constructor or exit table entry and no copy of `ConfigPart`'s destructors in any of them, and a source path pushed only in the second:

- **`000089d4`–`00008f53`, the sphere tests.** `Beam_RegisterSubsystem`'s `RET` at `000089d0` and three zero bytes end the file before it; `Collision_CopyClusterSpheres`' `RET 0xc` at `00008f4f` and two zero bytes end this one. Its functions are ES2's from `Collision_RaySphereTest` (`0040c428`) to `CollisionSphere_ElementCtor` (`0040c8be`), in ES2's order but for the element constructor, which comes before `Collision_CopyClusterSpheres` where ES2's comes after.
- **`00008f54`–`00009b8b`, `collide.cpp` and `damage.cpp`.** The asserts of `Collision_LoadRecordArray` and `Collision_RegisterObject` name `collide.cpp` and `Damage_LoadMechDmgFile`'s assert names `damage.cpp`, so two object files meet inside it, at a place no zero fill marks ([Open](#open)). `HercPiece_BindComponentAnchors`' `RET 0x14` at `00009b87` and two zero bytes end it. Its functions are ES2's from `Collision_ClusterTest` (`0040c8c8`) to `HercPiece_BindComponentAnchors` (`0040d284`), in ES2's order. Its data: `CollisionTable_Cursor` (`00090da2`), `collide.cpp`'s two source paths (`00090da4`, `00090dcf`) and the assert's text between them, two zero bytes, then `damage.cpp`'s path (`00090de8`), `DmgTable_Cursor` (`00090dfe`) and `DmgTable_Count` (`00090e00`). The loaders and the loadout's join are in [`dmg-damage-file.md`](../formats/dmg-damage-file.md#the-loadouts-join).
- **`00009b8c`–`0000a2df`, the damage arrays.** `Component_TotalArmor`'s `RET 8` at `0000a2da` and three zero bytes end it, before [the mount classes' file](weapon-mount-classes.md#where-the-object-file-starts-and-ends) at `0000a2e0`. Its functions are ES2's from `Component_AllocDamageArrays` (`0040d2cc`) to `Component_TotalArmor` (`0040dc58`), in ES2's order. Its data is `maybe_ComponentDependencyList` (`00090e04`), after `DmgTable_Count`. `Component_AllocDamageArrays`, `Component_LinkMaxRefData` and `Component_AddDamage` are in [the damage header](../formats/dmg-damage-file.md#the-damage-header).

## The struck component — `Mech_SelectStruckComponent` (`00009064`)

(model, occupancy flags, object-to-muzzle frame, ray record, object): ES2's ([the test](../../retail/simulation/hit-detection.md#the-test--mech_selectstruckcomponent-0040c9d4)). It sets the hit component `Collision_HitComponent` (`000af134`) to `0xffff` and the hit distance `Collision_HitDistance` (`000af130`) to 0, takes the ray's remaining length `Collision_RayRemaining` (`000bc2a6`) from the ray record's `+4` and the clearance `Collision_RayClearance` (`000bc2aa`) from its `+8`, and runs `Mech_ComponentGeometryTest_Candidate` on each node. It copies the distance and the component to `Collision_HitResult` (`000af138`, a dword and an `int16`) and returns its address, or null when the distance is 0. `es2_xref.py` finds three calls: `Mech_DirectFireHitTest` ([the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)), and the hit tests at `00003a58` and `0001b370`, slot `+0x24` of the base's and the flyer's tables ([the difficulty scale](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c)).

**`Mech_ComponentGeometryTest_Candidate` (`00008f84`)**, (node, occupancy flags, object-to-muzzle frame, object): ES2's, but for a part the shape lacks.

1. **The node's frame.** For a node whose part id is negative it copies the 32-byte frame. Otherwise it asks the shape at `+4` of the shape instance `+0x2e` for the part through its slot `+0x24`, takes the 32-byte entry of the instance's node array (`+0x16`) at the part's node index (`+4`), and composes it with the frame through `Transform_Concat` (`0005ea08`). For a part the shape lacks it passes a null node transform to `Transform_Concat`, which then reads its operand from address 0 ([Open](#open)); ES2's uses the identity there, and for a part whose node index is negative as well, which ES1's does not test. The result is `Collision_NodeToMuzzleTransform` (`000bc2a2`).
2. **Each cluster** whose component's occupancy flag is set goes to `Collision_ClusterTest` (`00008f54`); a hit stores its distance and component as the hit, so the last cluster to hit, the nearest, is kept. It returns 0.

**`Collision_ClusterTest` (`00008f54`)**, (cluster): `Collision_ClusterBoundTest`(the bound's radius, its center) and, when that passes, `Collision_ClusterSphereTest`(cluster, null), whose answer it returns; ES2's.

**`Collision_ClusterBoundTest` (`00008a88`)**, (radius, center): the center through `Collision_NodeToMuzzleTransform` with `Transform_ApplyToShortPoint` (`0005e858`), then `Collision_RaySphereTest`(0, radius, that point), the sphere test without the clip. So a bound passes only when its center is ahead of the muzzle and nearer than the remaining length, where ES2's lets it reach up to its radius past that ([the test](../../retail/simulation/hit-detection.md#the-test--mech_selectstruckcomponent-0040c9d4)); and its off-axis test is the sphere test's 16-bit one.

**`Collision_ClusterSphereTest` (`00008ab8`)**, (cluster, offset): ES2's. Each sphere's center, plus the offset's three `int16` when there is one, through `Collision_NodeToMuzzleTransform`, goes to `Collision_RaySphereTest`(1, the sphere's radius, that point); it returns `Collision_RayRemaining` + 1 when any sphere hit, otherwise 0.

**`Collision_RaySphereTest` (`000089d4`)**, (clip, radius, point), the point three `int32` in muzzle space where the ray is +Y: ES2's. With *y* the point's Y, *m* = `Math_FastMagnitude2D`(X, Z) and the `int16` reach *r* = clearance + radius, it passes when *y*, compared unsigned, is below `Collision_RayRemaining`, *m* is below `0x7fff`, and the low 16 bits of *m* + *r* are below those of 2*r*, both unsigned. With *clip* set it then cuts `Collision_RayRemaining` to *y* − (*r* − *m*), or to 0 when that is not positive. It answers 1 in `AX` for a pass, otherwise 0.

## The bounding spheres

- **`Collision_ComputeBoundingSphere` (`00008b8c`)**, (cluster, out center): ES2's ([not on disk](../../retail/formats/collision-spheres.md#not-on-disk)): a box seeded at 10000 and −10000, widened by each sphere's center ± its radius, its middle as the center (each sum halved by an arithmetic shift), and `Math_FastMagnitude3D` (`0006325a`) of the box's top corner less the center as the radius it returns. `Collision_ComputeAllBoundingSpheres` (`000094a0`) stores that at each cluster's `+0xe`.
- **`Collision_ClusterReach` (`00008d28`)**, (cluster, offset): ES2's: the greatest `Math_FastMagnitude3D` of a sphere's center, plus the offset when there is one, plus its radius. **`Collision_RayNodeOriginTest` (`00008a6c`)**, (radius): ES2's, `Collision_RaySphereTest`(0, radius, the translation at `+0x14` of `Collision_NodeToMuzzleTransform`). `es2_xref.py` finds no branch or stored pointer to either ([Open](#open)).
- **`CollisionSphere_ElementCtor` (`00008e68`)**, (sphere): returns its argument, allocating 8 bytes when it is null. `es2_xref.py` finds two stored pointers to it, one the element constructor `Collision_CopyClusterSpheres` passes to `__vec_new` ([the loadout's join](../formats/dmg-damage-file.md#the-loadouts-join)).

## Finding a component's cluster

**`Collision_FindClusterForComponent` (`0000916c`)**, (model, out part id, component): ES2's: the first node for which **`Collision_FindClusterInNode` (`000090fc`)**, (node, out part id, component), finds a cluster whose component (`+6`) is the one asked for; that writes the node's part id through the pointer and returns the cluster. It returns null when no node has one. `es2_xref.py` finds one call, from `Component_DestroyAndCascade`.

## Write and cascade — `Component_ApplyDamageAndCascade` (`00009f7c`)

(header, damage, component): ES2's ([write and cascade](../../retail/simulation/component-damage.md#the-component-damage-system)) with one step added.

1. `Component_AddDamage` (`00009c88`)(the damage, the piece's armor, the component's entry). When that leaves the entry standing it returns 0.
2. Otherwise `Component_SpillIntoDependents` (`0000979c`)(piece, the internals' maxima, the excess, the internals' damage).
   - **Added:** when that leaves a dependent standing and the piece's sequence byte `+3` is not −1, the entry of the shape instance's cell array (the `int16` array at `+8` of the instance `+0x2e`) at that sequence is set to 1, the second cell. On ES2's machines that is the damage-shaded cell ([a destroyed component hides its own geometry](../../retail/rendering/mech-shape-drawing.md#a-destroyed-component-hides-its-own-geometry)), and ES2's function has no such step.
   - When it leaves none and the piece's flags (`+5`) have bit 0 set, it clears `ComponentCascade_EffectLatch` (`000af140`), runs `Component_DestroyAndCascade` on the component, and then on the last entry of the list at `maybe_ComponentDependencyList` while its count (byte `+5`) is not 0, taking 1 from the count first; it returns 1.

It returns 0 on every other path. `es2_xref.py` finds three calls: `Mech_ComponentDamageWrite` ([the damage write](mech.md#the-damage-write--mech_componentdamagewrite-00013250)), `Mech_DirectFireHitTest`'s 10000 for a knocked-out mount, and `0001b290`.

## Losing a component — `Component_DestroyAndCascade` (`00009cd4`)

(header, component): ES2's ([a machine's component](../../retail/simulation/destruction-effects.md#a-machines-component)), the fire and the debris aside.

1. `Collision_FindClusterForComponent` on the model (`+0x10`), for the cluster's bound center (cluster `+8`) and the node's part id. A null cluster is used unchecked, its center read at address 8, as ES2's does.
2. With the component's occupancy flag clear it returns 0. Otherwise its entry becomes −1, its flag 0, and `Component_SpillIntoDependents` gives its dependents 32000.
3. **Fire and the shape**, for a sequence byte `+3` that is not −1: with the latch clear and the flags' bit 1 set, `maybe_FireEffect_Construct` (`000069ec`)(null, 0, the bound center, the part id, the machine); then the sequence's cell is set to 2. ES2's ([who catches fire](../../retail/simulation/destruction-effects.md#who-catches-fire)) puts out the machine's fires before lighting shape 0 for bit 1, and lights shape 2 for bit 3; ES1's reads no bit 3.
4. **The anchor**: the bound center as three `int32` for a negative part id; otherwise through the part's node transform, from the shape's slot `+0x24` and the instance's node array as in the sphere test, with `Transform_ApplyToShortPoint`, a null transform for a part the shape lacks. Then the machine's frame, `Context_GetTransform` (`000016e8`)(machine + 6), with `Transform_ApplyToPoint` (`0005f58c`).
5. **The explosion**, with the latch clear: `Explosion_Construct` (`00005af0`)(null, the anchor, 10), or `0x11` setting the latch when the flags' bit 2 is set. ES2's.
6. **Debris**: `Debris_ThrowGroup` (`00005fb8`)(group 3 of the groups at `000bc1c0`, the anchor, the roots `000bc1c6`) ([throwing a group](../formats/debris-dat.md#throwing-a-group)), every time. ES2's throws the piece's `+2` group, or 2 when that is −1.
7. **The queue**: each of the header's components (count `+0x18`) whose flag is set and whose piece's parent byte `+4` is this component is appended to the list: stored at the list's pointer + 2 × its count, the count then raised by 1, with no test against the capacity byte `+4`, 10 ([Open](#open)). ES2's.

It returns 1.

## Fully destroyed — `Component_IsFullyDestroyed` (`00009f30`)

(header, component): ES2's. When the component's entry is negative it returns `Component_AllDependentsDestroyed` (`00009734`)(piece, the internals' damage), otherwise 0. `es2_xref.py` finds two calls, both in `Mech_ComponentDamageWrite`'s death test, on components 0 and 1.

## The readings

- **`Component_ReadOverallDamage` (`0000a07c`)**, (header): ES2's. Over the header's components (count `+0x18`), whatever their flags, it sums each entry, or the piece's armor for a negative one, and each piece's armor; over the internals (count `+0x1a`) each entry, or the maximum for a negative one, and each maximum; and returns the damage sum shifted left 8 and divided by the maximum sum, signed. With every entry that is not negative at most its maximum, it answers 0 to `0x100`. `es2_xref.py` finds two calls, from `Mech_GetOverallDamage` (`00010eb0`, [small functions](mech.md#small-functions)) and `0001b50c`.
- **`Component_TotalArmor` (`0000a24c`)**, (header, component): ES2's, the piece's armor plus the maximum of each of its dependents (the `int16` at `+2` of each 4-byte entry of the list at `+8`, count `+6`), returned as an `int16` in `AX` where ES2's returns an `int`. `es2_xref.py` finds one call, from `Mech_SpreadImpactDamage` (`00013078`, [construction](mech.md#construction)).
- **`Component_ReadDamagePercent` (`0000a16c`)**, (header, component): ES2's ([read](../../retail/simulation/component-damage.md#the-component-damage-system)).

**A negative entry other than −1**, which `Component_AddDamage`'s `int16` sum leaves when an entry and a damage together pass 32767 ([the damage header](../formats/dmg-damage-file.md#the-damage-header)), counts as destroyed to `Component_IsFullyDestroyed`, `Component_AllDependentsDestroyed`, `Component_SpillIntoDependents`, `Component_AddDamage` and `Component_ReadOverallDamage`, which test for a negative entry. `Component_ReadDamagePercent` substitutes the maximum only for −1 itself, so it adds such an entry as it stands, and the reading falls. `Component_DestroyAndCascade`'s 32000 wraps on a dependent that already holds more than 767 ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Collision_RaySphereTest`, `Collision_RayNodeOriginTest`, `Collision_ClusterBoundTest`, `Collision_ClusterSphereTest` | `000089d4`, `00008a6c`, `00008a88`, `00008ab8` | `0040c428`, `0040c4a4`, `0040c4c4`, `0040c524` |
| `Collision_ComputeBoundingSphere`, `Collision_ClusterReach`, `Collision_ReadSphereArray`, `CollisionSphere_ElementCtor`, `Collision_CopyClusterSpheres` | `00008b8c`, `00008d28`, `00008e00`, `00008e68`, `00008eb0` | `0040c5d0`, `0040c71c`, `0040c7c4`, `0040c8be`, `0040c834` |
| `Collision_ClusterTest`, `Mech_ComponentGeometryTest_Candidate`, `Mech_SelectStruckComponent` | `00008f54`, `00008f84`, `00009064` | `0040c8c8`, `0040c8fc`, `0040c9d4` |
| `Collision_FindClusterInNode`, `Collision_FindClusterForComponent` | `000090fc`, `0000916c` | `0040ca58`, `0040ca8c` |
| the loaders and the loadout's join ([`dmg-damage-file.md`](../formats/dmg-damage-file.md)) | `000091bc`–`00009b8b` | `0040cacc`–`0040d284` |
| `Component_AllocDamageArrays`, `Component_LinkMaxRefData`, `Component_AddDamage` | `00009b8c`, `00009c28`, `00009c88` | `0040d2cc`, `0040d354`, `0040d3ec` |
| `Component_DestroyAndCascade`, `Component_IsFullyDestroyed`, `Component_ApplyDamageAndCascade` | `00009cd4`, `00009f30`, `00009f7c` | `0040d434`, `0040d9f8`, `0040da38` |
| `Component_ReadOverallDamage`, `Component_ReadDamagePercent`, `Component_TotalArmor` | `0000a07c`, `0000a16c`, `0000a24c` | `0040db2c`, `0040dbc0`, `0040dc58` |

Callees of other files read for them: `maybe_FireEffect_Construct` (`000069ec`), (effect, kind, point, part id, owner): with a null effect it takes one from the pool `000aefd4`, returning 0 when that gives none; it stores the table `000909fa`, the kind at `+0x22`, the `int16` the kind indexes at `00090964` at `+0x1e`, 4 × kind + `Math_RandomNext`(`000b5c48`) & 3 at `+0x20`, the owner at `+0xf` and the part id at `+0x13`, and the point's six bytes at `+0x15`, or with a null point sets `+6`. And `Rotation_Concat` (`00064fe4`), (a, b, out) in the C convention: ES2's (`0047ef4c`), identical after normalisation ([the assembly library](fixed-point-math.md#the-assembly-library)), the rotation-only product `Mech_ComponentDamageWrite` turns the player's disabled HERC with ([the damage write](mech.md#the-damage-write--mech_componentdamagewrite-00013250), step 7).

## Globals

| | Address |
|---|---|
| `Collision_NodeToMuzzleTransform`, the frame the sphere tests apply | `000bc2a2` |
| `Collision_RayRemaining`, the ray's remaining length, and `Collision_RayClearance`, the shot's clearance | `000bc2a6`, `000bc2aa` |
| `Collision_HitDistance` and `Collision_HitComponent`, the nearest hit so far | `000af130`, `000af134` |
| `Collision_HitResult`, the six bytes `Mech_SelectStruckComponent` returns | `000af138` |
| `ComponentCascade_EffectLatch`, one fire and explosion a cascade | `000af140` |
| `maybe_ComponentDependencyList`, the cascade's queue | `00090e04` |

## Open

- **Open:** where `collide.cpp` ends and `damage.cpp` begins within `00008f54`–`00009b8b`; a function that ends on a 4-byte boundary leaves no zero fill, as `Collision_ReplaceClusters` (to `00009733`) and `HercPiece_SetInternalMaxima` (to `00009a2b`) do. `Component_AllDependentsDestroyed`, at `00009734`, is the first function there about damage rather than spheres.
- **Open:** whether any ES1 `.COL` names a part its chassis' shape lacks, which sends a null node transform into the sphere test and the destroyed component's anchor: a walk of the `col\` files' part ids against the shapes' parts through `HercWorks.Core`. ES2's RAZOR is the one such case in ES2's data ([verified against retail data](../../retail/formats/collision-spheres.md#verified-against-retail-data)).
- **Open:** whether ES1's machine shapes' sequences have ES2's three cells, the second damage-shaded, which decides what the added cell-1 step shows: a count of the cells of each `TSCellAnimPart` in the ES1 machine shapes through `HercWorks.Core`. A retail check: shoot one leg of an enemy HERC until its armor goes but it still walks, and see whether the leg darkens.
- **Open:** how many entries the cascade's queue holds at once on retail data, against its 10: the largest number of live pieces sharing one parent in each `.DMG`, through `HercWorks.Core`.
- **Open:** which retail internals have a maximum above 767, where the 32000 a destroyed component gives its dependents wraps an entry holding more than 767 to a negative one other than −1: the internals' maxima of the 18 `dmg\` files through `HercWorks.Core`.
- **Open:** what reaches `Collision_ClusterReach` and `Collision_RayNodeOriginTest`, to which `es2_xref.py` finds no branch or stored pointer, as it finds none to ES2's.
- **Open:** what `maybe_FireEffect_Construct`'s object is, how it draws and when it ends (plan step 7e).
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:00009cd4` set before shooting an enemy HERC fires as one of its parts is shot away, and `BP <CS>:00009064` fires for each shot that gets through its shields.
