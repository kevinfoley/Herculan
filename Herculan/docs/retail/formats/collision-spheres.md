# Collision sphere models — `BASECOL.DAT` and `.COL`

The hit geometry a shot is tested against for a structure, a HERC or a flyer. DBSIM never tests a shot against a unit's polygons. Reverse-engineered from `DBSIM.EXE` (Ghidra project `ES2Recon`); addresses are DBSIM virtual addresses. The test that walks this model, and what it does with the result, is [`../simulation/hit-detection.md`](../simulation/hit-detection.md#the-test--mech_selectstruckcomponent-0040c9d4).

## Layout

One format, two sources. Every field is `int16`, with no header and no padding:

```
nodeCount
  per node: nodeIndex, clusterCount
    per cluster: componentIndex, sphereCount, sphereCount * { x, y, z, radius }
```

- **Structures** read 65 of these back to back out of `dat\BASECOL.DAT`, in `BASES.DAT` type order, as one continuous stream partway through `Base_LoadResources` (`00405fac`). `componentIndex` indexes the type's `BASES.DAT` component array ([`bases-dat.md`](bases-dat.md#the-component-record-30-bytes)).
- **Mechs and flyers** each read one whole file, `col\<NAME>.COL`, through `Collision_RegisterObject` (`0040cd88`), which files each model in a fixed table (`CollisionTable_Array`, `004a98a8`, 6 bytes an entry, count at `004987de`) — the mech from `Mech_Constructor` (`00415bb0`, into `mech+0x1f6`), the flyer from its type loader (`FlyerType_LoadResources` (`00422ed0`), into `flyerTypeRec+0x32`). `componentIndex` indexes the `.DMG` file's 29-slot component array instead ([`../simulation/component-damage.md`](../simulation/component-damage.md#the-component-damage-system)).

`nodeIndex` is `-1` for the object's own frame; anything else is a shape part id, resolved at test time ([`../simulation/hit-detection.md`](../simulation/hit-detection.md#the-test--mech_selectstruckcomponent-0040c9d4)).

Readers: `Collision_LoadRecordArray` (`0040ccf8`) → `Collision_ReadNode` (`0040cc50`) → `Collision_ReadCluster` (`0040cc14`) → `Collision_ReadSphereArray` (`0040c7c4`). The field order inside a cluster is not the struct order: `componentIndex` lands at the record's `+6` and is read first, `sphereCount` lands at `+0` and is read second, because the two live in different functions. `sphereCount` is tested as `value & 0x1fff` but allocated and read unmasked — the mask is only a zero-test, and no retail record sets the top bits.

**They really are spheres.** `Collision_ReadSphereArray` allocates elements of 8 bytes, `Collision_ClusterSphereTest` (`0040c524`) strides four `int16` per element and passes index 3 to `Collision_RaySphereTest` (`0040c428`) as a single scalar radius applied radially about the ray, and `Collision_ComputeBoundingSphere` (`0040c5d0`) inflates each child by that one radius on all three axes. Nothing compares per-axis extents.

## Not on disk

Each cluster's bounding sphere is built at load by `Collision_ComputeBoundingSphere` (`0040c5d0`) as the AABB of the children each inflated by its own radius, centred on that box's midpoint, radius = `Math_FastMagnitude3D` of the half-extents. That approximation is direction-dependent, from about 8% under to about 9% over a true Euclidean radius depending on the box's proportions ([`../simulation/dbsim-physics-notes.md`](../simulation/dbsim-physics-notes.md#fixed-point-math-toolkit)), so the bound is not exactly circumscribing; it is behaviour, not an artefact. The hit test rejects on it before touching any child sphere, and the mech component-position lookup reads its centre ([`../simulation/damage-system.md`](../simulation/damage-system.md#where-a-component-stands--the-0x58-slot)).

## A fitted weapon brings its own spheres

A mech `.COL` names its weapon-mount components with no spheres in them, and the weapon fitted to each hardpoint fills them at load. Every sim `WEAPONS.DAT` template carries a cluster of its own at `+0x12`, read by the same `Collision_ReadCluster` ([`weapons-dat-sim.md`](weapons-dat-sim.md#weaponmounttemplate-record-variable-length)): the gun's hit spheres strung along its barrel about its mount point, radius 70–450.

`Mech_ConfigureLoadout` (`004175dc`) walks the fitted mounts. For each it sets the template cluster's component index to the mount's component (`.GL +0x17` plus 19) and calls `Collision_ReplaceClusters` (`0040cec0`) with that cluster and `WeaponMount_MuzzleOffset` (`0040f540`), the hardpoint's mount point plus the side offset. That finds the first cluster in the machine's model, node by node, with the same component index, gives it copies of the template's spheres with each centre moved by the offset in 16 bits and the radius kept (`Collision_CopyClusterSpheres`, `0040c834`), and reruns `Collision_ComputeAllBoundingSpheres` (`0040ccc8`) on that node. The model is the machine's own, read by `Mech_Constructor`, so each machine's spheres follow its own fit.

The weapon's damage record replaces the mount's `.DMG` piece in the same loop ([`dmg-damage-file.md`](dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece)). A hardpoint with nothing fitted keeps its empty cluster, and its component loses its occupancy flag straight afterwards ([`../simulation/component-damage.md`](../simulation/component-damage.md#the-component-damage-system)).

## Verified against retail data

**`BASECOL.DAT`** is 4,938 content bytes; the walk lands exactly on the end after 65 types. Every cluster's `componentIndex` is inside its type's component array. The geometry reads as deliberate hitboxes — a three-section bunker with a cluster per section, a gun tower with a cluster per barrel. One type (3) carries a full model that its `BASES.DAT +0x30` flag leaves switched off.

**The 22 `.COL` files** likewise walk exactly to their own end and round-trip byte-exact. Every `componentIndex` is inside the 29-slot array (max 28). Every cluster naming a mount component, 19 and up, has no spheres: twenty files carry them (nine or ten on most, MONGOOSE 3, RAMSES 4, PITBULL 1), and SPIDER and SKIMMER none; every node id resolves to a real shape part except RAZOR's single node 5, which the hit test gives an identity transform. Node counts run 1 (RAZOR, SKIMMER) to 13 (SPIDER); sphere radii 40–600 world units. `SKIMMER` is the only file with an object-frame cluster.

ACHILLES' first cluster cross-checks against its `.DMG`: it places spheres for components 7, 9 and 11 on nodes 3 and 1, and 7→9→11 is exactly the parent chain that file states for the left leg ([`dmg-damage-file.md`](dmg-damage-file.md#the-piece-record), `+0x04`). Component 7's two spheres are `(-20, 0, -100) r=200` and `(-20, 0, -400) r=180` — a thigh as two stacked balls.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A `.COL` file is a 10-byte header followed by unparsed data | There is no header. The five shorts read as one are the walk's first five fields: "always 6" is ACHILLES' node count (MONGOOSE has 8, PITBULL 10, SPIDER 13, RAZOR and SKIMMER 1), "always 3 for hercs, `FFFF` for skimmer" is the first node's index (SPIDER's is 12, and `FFFF` is the object frame), a "collider type" that crashes above 1 is the first node's cluster count reading past the end of the file, "hercs have 7" is the first cluster's component index (component 7 is `LEG/LEFT/UPPER`), and the last is that cluster's sphere count |
