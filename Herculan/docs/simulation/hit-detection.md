# Hit detection

How a shot decides what it struck, for all three shootable classes. Reverse-engineered from `DBSIM.EXE` (Ghidra project `ES2Recon`); addresses are DBSIM virtual addresses. Companion to [`damage-system.md`](damage-system.md) (what the hit then does to a mech), [`impact-effects.md`](impact-effects.md) (what a hit spawns) and [`../formats/collision-spheres.md`](../formats/collision-spheres.md) (the hit-sphere files).

Each class has a vtable `+0x20` hit test: `Base_DirectFireHitTest` (`00405038`), `Mech_DirectFireHitTest` (`00418ba8`, in [`damage-system.md`](damage-system.md)) and `Flyer_DirectFireHitTest` (`00421c8c`). All three reach the same sphere test; only the model source and what surrounds it differ.

## The sweep — `Sim_RaycastObjectList` (`00426528`)

A generic ray-vs-live-object-list query, not weapon-specific. Nine call sites in four functions: the launcher round's per-tick step (`Rocket_TickUpdate`, `0040a538`, once), the travelling bullet's (`Bullet_TickUpdate`, `0040b124`, twice — [`projectiles.md`](projectiles.md#flight--bullet_tickupdate-0040b124)), the beam dispatch (`Bullet_FireBurst`, `0040bf74`, once — [`beam-visuals.md`](beam-visuals.md#chain)) and a flyer's airframe contact probes (`Razor_MovementTick`, five, see [`razor-flight.md`](razor-flight.md#contact-probes)). One raycast primitive reused for weapon hit-scan **and** obstacle sensing. It shares the global live-object list (`DAT_004a9b7c`/`DAT_004a9b82`) with the confirmed-`objlist.cpp` functions at `004281b0`/`004282f8`, which makes `objlist.cpp` its likely source file ([Open](#open)); it is not `fire.cpp`.

It walks the live-object list; for each candidate that passes the filter below, it calls that object's vtable method at `+0x20` — for a mech `Mech_DirectFireHitTest` (`00418ba8`, [`damage-system.md`](damage-system.md#direct-fire-damage-armor-then-part-deterministic-shield-gated)), for a structure `Base_DirectFireHitTest` and for a flyer `Flyer_DirectFireHitTest`, both below. **The hit test and the damage application are the same call** — there is no separate "apply damage" step visible from the caller's side. It also makes a second, unrelated vtable call on each candidate the ray struck (`+0x50`), passing the shooter and the shot's damage: "this object just took fire", not damage. The call is skipped when the shot has no owner, when the struck object is destroyed (`+0x99`), and when it is on the other side from the shooter and out of action (`+0x99`, `+0xa4` or `+0xa5`); a struck object on the shooter's own side gets it whatever its other two bytes say. A HERC's slot is `Mech_AiOnTakingFire` (`0041f7b8`, [`ai-targeting.md`](ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50)); every other class's is `Mech_ShareContact` (`00411aec`, [`ai-targeting.md`](ai-targeting.md#passing-a-contact-on)).

Four properties of the sweep:

- **The candidate filter** is three tests, all before the vtable call: not the shot's owner (`shotData+0x0e`), not the object at `shotData+0x14`, and not an object whose mission group still carries an action (below). The middle one is a second exclusion, and the only caller that fills it is `Razor_MovementTick`, which puts the flyer itself there (`00419bef`) so its own airframe probes cannot strike it. The three weapon callers leave that slot alone: it is **uninitialised stack**, not an empty field, so the comparison runs against whatever the previous frame left and, for a weapon shot, excludes nothing. The team byte (`obj[+0x45][+0x12]`) is read only *after* a hit, for the AI notification and the two friendly-fire warnings ([`ai-targeting.md`](ai-targeting.md#radio-callouts)); it does not gate the hit itself.
- Before the sweep it **caches the world-to-muzzle transform** in the ray record at `+0x0a` (copy, transpose, negate-and-rotate the translation), which is the frame every hit test works in.
- It **shortens the ray to each hit** (`rayRecord+0x04`) rather than stopping at the first, so a candidate found later but nearer wins — every subsequent candidate is tested against the shortened length. It breaks early only for a hit inside 500 units. Because damage is applied inside the hit test, a candidate that is later superseded has still taken its damage.
- It opens with a **ray-versus-terrain query**, `Sim_RaycastTerrain` (`00428048`) → `Terrain_RayWalk` (`0046e87c`) against `ActiveHeightGrid`. A ground hit clips the ray before any object is tested, so a beam cannot shoot through a hillside. The ray record's own 200 is passed down as a walk radius and has no effect on the result: `Terrain_RayWalk` forwards it to `Terrain_CellSurfaceIntersect` (`0047068c`) — its only destination — which never reads that parameter. See [`../formats/terrain-heightmap.md`](../formats/terrain-heightmap.md#ray-versus-terrain--terrain_raywalk-0046e87c). Returns `hitDistance + 1`, or 0 for a clean miss. A ground hit is reported as a stand-in object, `g_TerrainHitPseudoObject` (`004aab58`), with the hit point stored at its `+0x1a`/`+0x1e`/`+0x22`. Nothing reads those fields: the stand-in's address is only the non-null "something was struck" value that reaches the blocked-line-of-fire report below, whose callee ignores its argument. The terrain impact effect does not come from it ([`impact-effects.md`](impact-effects.md#the-terrain-impact-is-the-raycasts-own-job)).

The tail of the sweep carries two effects on the shooter's side, after the terrain impact effect it spawns where the shot ended. Reaching the shooter's own selected target engages it — [`mission-deployment.md`](mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6). Anything else the ray stops on is tested for the **blocked-line-of-fire report**, made to the shooter's vtable `+0x64` (`Mech_AiOnLineOfFireBlocked`, `0041dd2c`):

```
if (something was struck && shooter has a target
    && groundRange(shooter, hitPoint) < range(shooter, target)
    && |bearing(hitPoint) - bearing(target)| < 0x2000)
        shooter->vtable+0x64(hitObject)
```

So a shot that stops on terrain or on a third object, nearer than the intended target and within 45° of the same line, tells the shooter its line of fire is blocked; what a machine does about it is [`ai-combat-states.md`](ai-combat-states.md#how-it-is-reached). Hitting the shooter's own target is what the report excludes: the branch that recognises it passes null in place of the struck object, so a machine does not report its target as blocking the shot at it.

**The group test is the filter that is easy to miss, and it matters: an object whose mission group still carries an action is skipped before any geometry is touched** (`*(int*)(obj[+0x45] + 0x14) != 0` — the group record's action slot, the same test [`mission-deployment.md`](mission-deployment.md) covers). Retail missions place undeployed groups by the ordinary rules, so several routinely sit stacked on a shared waypoint; the first stock mission parks seven objects in three overlapping pairs. Without the skip they are invisible and unticked but perfectly solid, and shots stop on nothing.

## `Base_DirectFireHitTest` — `00405038`

Vtable `+0x20` for every structure class: all five type-switched vtables `Base_Construct` (`00405314`) installs (`00497784`, `00497818`, `004978ac`, `00497940`, `004979d4`) point slot `+0x20` at this one function. Like the mech's, it is the hit test and the damage application in one call.

```
wreck = typeRec[+4] != -1 && componentState[0][+5] <= 1 && obj[+0x99] != 0
if (wreck || typeRec[+0x38] == 0)  -> collision-volume path, component 0
else                               -> sphere-model path, names the component
if (hit) {
    if (!destroyed) vtable+0x74(componentIndex, shot.DamageArmor, shot.owner)
    spawn ImpactFXArmor effect at rayTransform.TransformPoint(0, hitDistance, 0)
    if ((rand & 0xfff) < 0x401) throw debris group 1 from the same point     // 25%
}
```

Group 1 is one of `DEF_DEB`'s shared groups; every debris spawn site is tabulated in [`destruction-effects.md`](destruction-effects.md#spawn-sites).

`typeRec[+0x38]` is non-null exactly when `BASES.DAT`'s `+0x30` is non-zero ([`bases-dat.md`](../formats/bases-dat.md#the-type-record)), so the branch is a per-type flag. Retail split: **25 of 65 types use the sphere model, 40 use the volume**. A *destroyed* type that leaves a wreck (`typeRec[+4] != -1`) switches to the volume whichever it used standing — the wreck is a different shape out of `BHULKS.DGS`.

Both paths open with the same coarse reject as every other hit test in the simulation: `shapeRadius + shot.clearance + rayLength < |muzzle - object|`, where `shapeRadius` is vtable `+0x10` (`SimObject_GetShapeRadius`, `0046b80c`), i.e. `shape+8`.

**Damage recovery for plasma.** `Bullet_TickUpdate`'s subtype-9 branch stashes the round's two damage figures in globals (`Bullet_StashDirectFireDamage`, `0040501c` → `DAT_004a9676`/`DAT_004a9678`) and then **zeroes them in the shot record** before the raycast. This branch is the only reader: a *volume-path* hit whose shot carries zero on both counts puts the armour figure back. The sphere path does not, so a plasma round does direct-fire damage to a volume-path building and none to a sphere-model one — both then stand in its blast. See [`projectiles.md`](projectiles.md#the-plasma-branch).

## The sphere model

Structures, HERCs and flyers each carry a model of hit spheres grouped into clusters, one cluster per component, read from `dat\BASECOL.DAT` or `col\<NAME>.COL`. The file layout, the readers and the per-cluster bounding sphere built at load are [`../formats/collision-spheres.md`](../formats/collision-spheres.md).

### The test — `Mech_SelectStruckComponent` (`0040c9d4`)

Despite the name it is shared: structures reach it from `00405038`, flyers from `Flyer_DirectFireHitTest` (`00421c8c`), mechs from `Mech_DirectFireHitTest`. Only the model source differs.

Per cluster (`Mech_ComponentGeometryTest_Candidate`, `0040c8fc`):

- A cluster whose component is already destroyed (`obj+0x201` alive flags) is **skipped entirely**, so a building stops blocking shots through the sections it has lost.
- `nodeIndex < 0` means the object's own frame. A non-negative one is a shape **part id**, resolved through the shape (`shape->vtable+0x20`) to that part's transform slot and then through the instance's node-transform array (`inst+0x16 + part[+4] * 0x20`), so a moving part carries its hit volume. A part the shape does not have falls back to an identity transform rather than being skipped. **This is the whole of a HERC's hit geometry** — every mech `.COL` cluster is node-placed, so the volume walks with the legs and swings with the torso. Structures are the opposite: only the eight animated types have any, and each keeps its body cluster in the object frame.
- Bound first (`Collision_ClusterBoundTest`, `0040c4c4`), spheres only if it passes (`Collision_ClusterSphereTest` (`0040c524`) → `Collision_RaySphereTest` (`0040c428`)).
- **The ray shortens as the test runs** (global `DAT_004a9894`): each struck sphere clips the working distance to its own entry point, `alongAxis - (radius + clearance - offAxis)` floored at zero, and later spheres are tested against the clipped ray. The result kept is the *last* cluster that hit, which is therefore the nearest. Returned distance is that clipped distance `+ 1`; `00405038` and `00421c8c` each add another `+ 1`, `00418ba8` does not.

`Collision_RaySphereTest`'s off-axis test is written in 16-bit arithmetic — `(ushort)(offAxis + reach) < (ushort)(reach * 2)` — because the doubled radius can overflow a signed short.

The two transform helpers under it differ only in input width: `Transform_ApplyToShortPoint` (`004800c8`) takes a `short` point (structure/sphere centres), `Transform_ApplyToPoint` (`00480330`) an `int` one. Both branch on the transform's rank byte at `+0x12` — translation only, Z-rotation only, or full 3×3 — which is an optimisation, not a behavioural difference.

## The collision volume — the `.DGS` record's height field

The shape's collision volume is the tail of its `.DGS` record: a grid of columns, rows and a cell size, a 256-entry height table and one byte code per cell, laid out in [`../formats/dgs-hd0-notes.md`](../formats/dgs-hd0-notes.md#the-collision-volume).

**A building is a height field, not a mesh.** Nothing tests a shot against structure polygons; the ray is stepped in shape space and each step asks the grid how tall the column under it is.

- `GridShape_HeightAround` (`00427238`) — the height under a point. When `1 << (shift-1) < radius` it takes the tallest of every cell within `radius`; below that it samples the single cell. Off-grid returns 0, which is what makes the volume end at its own edges.
- `GridShape_Raycast` (`004273c8`) — the march. Rejects an empty grid, and a ray with *both* ends above the ceiling. A point is shifted by `origin << shift` (world units, not cells) before it is divided down, so the footprint straddles the model. Step length is `(1 << shift) + clearance`; the direction is rescaled to it by a Q16 divide whose result is **truncated to 16 bits** before it multiplies. Hits when the sampled height is non-zero and above the ray's current Z.
- `Sim_RaycastShapeVolume` (`00427da8`) — the object test around it. After the coarse reject, brings the object's centre into muzzle space and tests a box on **X and Y only** (`|x| < reach`, `-reach < y < rayLength + reach`) — Z is not tested. Then transforms the ray into shape space and marches. Also called by `Sim_RaycastShapeList` (`00404bc0`), a bulk line-of-sight query over the structure list.
- `Structure_WalkCollisionTest` (`00427c68`) — the **walking-collision** test, called from `Structure_GatherWalkCandidates`'s (`00404ae4`) gather (see [`mech-locomotion.md`](mech-locomotion.md#collision)). 2D distance against `shape+8`, then the point rotated into shape space and sampled at radius 0. It is the only query of the three that **does not apply the `origin << shift`** the march applies, so the footprint a machine walks into is displaced from the one a shot is tested against by the grid's origin — its centre, for all 45 records ([Open](#open)).

### Verified against retail data

Firing at all 65 types from 16 bearings × 15 muzzle heights (200 to 6000 world units) strikes every type on its correct path, at distances matching where the surface is. Short structures stop being hit above their own roof line: type 51's shape has a 899-unit ceiling and it is struck only from the two sample heights below that.

## The three radius slots

An object has three unrelated radii, on three vtable slots, and no two of them are read by the same consumer. Confusing them is easy: `+0x5c` and `+0x7c` are the *same function body* on a mech, and `+0x10` matches neither on anything.

| Slot | What reads it | Mech | Structure | Flyer |
|---|---|---|---|---|
| `+0x10` shape radius (`SimObject_GetShapeRadius`, `0046b80c`) | every hit test's coarse reject; the LOD selector (`Shape_DrawAtDetailLevel`, `004033e4`); the HUD target box | `shape+8` — the shape's bounding radius, [`../formats/dgs-hd0-notes.md`](../formats/dgs-hd0-notes.md#the-bounding-radius--shape8) | `shape+8` | `shape+8` |
| `+0x5c` body radius | the blast sweep's surface-to-centre range; the *mover's* half of the collision gap | `typeRec+0x70`, **750 for every HERC** (`Mech_GetBodyRadius`, `00415518`) | `BASES.DAT +0x2a` (`Base_GetBodyRadius`, `004035a4`) | `SimObject_GetBodyRadiusZero` (`00411aa4`), **0** |
| `+0x7c` collision radius | the *other* object's half of the collision gap, and nothing else | the same `typeRec+0x70` (`Mech_GetCollisionRadius`, `0041552c`) | `BASES.DAT +0x2a`, but **only for an animated type** (`Base_GetCollisionRadius`, `004035b8`) | `SimObject_GetCollisionRadiusZero` (`00411aac`), **0** |

**Zero on `+0x7c` means walk through me**, and `Mech_CollisionTest` skips the object outright. A flyer never blocks anything. A structure's zero is narrower than it looks: the slot tests `BASES.DAT +0x06`, the animation-thread count ([`bases-dat.md`](../formats/bases-dat.md#the-type-record)), which is non-zero on exactly the eight animated types, so only one of those still standing blocks by radius — every static type and every animated wreck is stopped by its **collision volume** in a second sweep instead. The two sets are exact complements. See [`mech-locomotion.md`](mech-locomotion.md#collision).

A mech's `+0x5c` is a third of the `+0x1a` shot radius, and its model bound is larger than either; nothing in the simulation reads that bound.

## `Flyer_DirectFireHitTest` — `00421c8c`

The shortest of the three, and the only one with no second piece of geometry behind it: the coarse reject on `SimObject_GetShapeRadius` (`shape+8`), then straight to the sphere test against `flyerTypeRec+0x32` with the alive flags at `flyer+0x208`. No shields, no collision volume.

```
if (shapeRadius + shot.clearance + rayLength < |muzzle - obj|) -> miss
hit = Mech_SelectStruckComponent(typeRec[+0x32], obj, ray, objToMuzzle, obj[+0x208])
if (hit) {
    fx = shot.ImpactFXArmor[rand & 3]
    vtable+0x74(hit.component, shot.DamageArmor, shot.owner)
    if (obj[+0x99]) fx = 10                       // already a wreck: a fixed effect id, not the shot's
    spawn fx at rayTransform.TransformPoint(0, hit.distance + 1, 0)
    throw debris at the same point: group 1, or 3 if obj[+0x99] is now set   // every hit, where a structure rolls 25%
}
```

A flyer's health record is one component with one dependent ([`component-damage.md`](component-damage.md#the-component-damage-system)); what its vtable `+0x74` (`Flyer_ComponentDamageWrite`, `00421bb4`) does on losing it is [`ai-flyers.md`](ai-flyers.md#death). The debris groups are in [`destruction-effects.md`](destruction-effects.md#spawn-sites).

Retail ships a `.COL` and a `.DMG` for `SKIMMER` only, so `HOVTANK` and `DROPSHIP` cannot be shot at all.

## Measured: hit geometry versus the drawn mesh

Across every static structure type, the `.DGS` grid's world footprint is **larger** than the mesh it is drawn with, by 200–900 units a side, because it rounds out to whole 512-unit cells — so the volume never runs small horizontally and a shot stops slightly early if anything. The march step at the retail cell shift is 712 units (`(1 << 9) + clearance`).

Several types' collision **ceiling** sits well below their roof line — type 3 stops at 2225 against a 6756 mesh, type 22 at 9400 against 18300 — so shots at the upper part of those buildings pass clean through. That is the retail data's own authoring.

## Open

- **Open:** confirm `Sim_RaycastObjectList` (`00426528`)'s source translation unit with a direct assert string. The `objlist.cpp` attribution rests on the shared object list, not on a string, unlike `rocket.cpp` and `collide.cpp`.
- **Unported:** testing a structure's node-placed clusters in the node's frame. Only the eight animated types carry any.
- **Open:** whether the walking footprint being displaced from the shot footprint by the grid's origin shows in retail play; the displacement is read from the two functions and measured on the 45 records, not observed.
- **Unported:** `Sim_RaycastShapeList` (`00404bc0`), the bulk line-of-sight query over the structure list.
