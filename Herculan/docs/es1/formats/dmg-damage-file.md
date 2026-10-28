# DBSIM.EXE — `dmg\<NAME>.DMG`

Each ES1 machine type's damage file gives its components and internals their armour, as ES2's does: ES1's simulator reads it with ES2's code and ES2's layout ([ES2's `.DMG`](../../retail/formats/dmg-damage-file.md)). Of the 18 files in `SIMVOL0.VOL`'s `dmg\`, 17 walk that layout to their last byte; `HOVTANK.DMG` does not, and the simulator's path to it ends the program before it opens the file. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The load

`Damage_LoadMechDmgFile` (`00009a2c`) follows ES2's: it opens the path with `VolRStream_CtorFromPath`, asserting at severity 5 (`damage.cpp` line `0x85`) when the stream status is set, takes the next 12-byte slot of the table at `000bc2b0` by the count word `00090dfe`, which it increments, reads the file into it with `HercPiece_LoadTable` (`00009930`), destroys the stream and returns the slot. `HercPiece_LoadTable` reads ES2's layout into ES2's slot: the `int16` internal count into `+0x0a` and that many `int16` maxima into a new array at `+0x04`, the `int16` piece count into `+0x08`, and that many 18-byte pieces into a new array at `+0x00`, each with `HercPiece_ReadRecord` (`00009890`).

Two functions call it, the two that push the `dmg` folder name (`000980e0`, at `000117e7` and `0001c73e`):

- `Mech_Constructor` (`00011330`), ES2's, builds a mech of a chassis type and loads `dmg\<name>` into `+0x190` and `col\<name>` into `+0x18c`, the name being `g_MechTypeNames` (`000bc440`) at the type record's `+0x6c`, the chassis' type number ([the chassis load](chassis-dat.md#the-load)). So it reads the sixteen `MECHS.NAM` names only. Its damage arrays are 21 internals by 28 components where ES2's are 22 by 29 ([the damage header](#the-damage-header)).
- `FlyerType_LoadResources` (`0001c6c8`) loads `dmg\<name>` for a flyer type ([the flyer load](bullets-skimmer-dat.md#the-load)), after `dat\<name>`.

`DmgTable_Alloc` (`00009ab4`), which `DBSim_LoadScriptDat` calls, allocates `DmgTable_Count` (`00090e00`) 12-byte slots to `DmgTable_Array` (`000bc2b0`) when the count is not 0, as ES2's (`0040d218`); `Damage_LoadMechDmgFile`'s cursor is `DmgTable_Cursor` (`00090dfe`).

## The damage header

The machine's `+0x19c` (ES2's `+0x206`) is ES2's header of pointers ([the component damage system](../../retail/simulation/component-damage.md#the-component-damage-system)), at the same offsets from its start: the internals' damage at `+0`, the components' damage at `+4`, their occupancy flags at `+8`, the `.DMG` table at `+0xc`, the `.COL` model at `+0x10`, the machine at `+0x14`, the component count at `+0x18` and the internal count at `+0x1a`. `es2_xref.py` finds the two functions that fill it called from `Mech_Constructor` and from `Flyer_Constructor` (`0001ac58`).

- **`Component_AllocDamageArrays` (`00009b8c`)**, (header, internals, components): ES2's (`0040d2cc`), allocating `0x1c` bytes itself when the header is null: the two damage arrays zeroed, the flag array of one `int16` a component with every byte 1, and the two counts, each array from `operator_new`. `Mech_Constructor` passes 21 and 28. The squad condition and the readouts walk ES2's 22 internals and 29 components, one entry past each ([the tenth mount pair](../simulation/damage-readouts.md#the-tenth-mount-pair)).
- **`Component_LinkMaxRefData` (`00009c28`)**, (header, model, table, machine): ES2's (`0040d354`): the table at `+0xc`, the model at `+0x10`, the machine at `+0x14`; and on its first call it makes the six-byte list `maybe_ComponentDependencyList` (`00090e04`), a pointer to `0x14` bytes, the capacity byte 10 at `+4` and a zero count byte at `+5`, where ES2's has room for 20.

Three of the functions a damage write runs ([write and cascade](../../retail/simulation/component-damage.md#the-component-damage-system)) are in this code:

- **`Component_AddDamage` (`00009c88`)**, (damage, maximum, entry): for an entry not already negative, adds the damage to it as an `int16`, and when the sum reaches the maximum writes the excess back through the damage pointer and stores −1; it answers whether the entry is negative. ES2's (`0040d3ec`) sums in 32 bits and stores only a sum below the maximum, so in ES1 an entry and a damage whose sum passes 32767 wrap to a negative entry that answers destroyed with neither −1 nor the excess written ([Open](#open)).
- **`Component_SpillIntoDependents` (`0000979c`)**, (piece, internal maxima, damage, internal damage): ES2's (`0040cf44`): the weights of the piece's live dependents summed, `Math_RandomBelow` (`00063b3c`) of the generator `000b5bd4` under the sum, the one it falls on given the damage through `Component_AddDamage`, and the draw repeated while that destroys it; it answers 1 once no dependent is live. `es2_xref.py` finds its calls in `Component_DestroyAndCascade` (`00009cd4`) and `Component_ApplyDamageAndCascade` (`00009f7c`).
- **`Component_AllDependentsDestroyed` (`00009734`)**, (piece, internal damage): 1 when every dependent's entry is negative, ES2's (`0040cf10`), called from `Component_IsFullyDestroyed` (`00009f30`).

## The loadout's join

`Mech_ConfigureLoadout` ([construction](../simulation/mech.md#construction)) puts each fitted weapon's template into the machine's own model and table with ES2's five calls ([a fitted weapon replaces its mount's piece](../../retail/formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece), [a fitted weapon brings its own spheres](../../retail/formats/collision-spheres.md#a-fitted-weapon-brings-its-own-spheres)), as ES2's does. For each mount of the manager that is not null, with its template `+0x1c` and the fit slot, the signed byte `+0x17` of its `.GL` record `+0x18`, it first writes three fields of the shared template, so each fitting leaves them in it:

1. the component, the slot + `0x13`, into the template's cluster (`+0x12`) at its `+6`;
2. the internal, the slot + `0xc`, into the `int16` `+2` of the first entry of the template piece's (`+0`) dependent list `+8`;
3. the parent byte `+4` of the machine's own piece for that component into the template piece's `+4`.

Then, each with a count of 1: `Collision_ReplaceClusters`(the model `+0x18c`, 1, `WeaponMount_MuzzleOffset` (`0000b918`) of the mount, the template's cluster), `HercPiece_ReplacePieces`(the table `+0x190`, 1, the template piece, the component) and `HercPiece_SetInternalMaxima`(the table, 1, the template's `int16` `+0x2a`, the internal). A mount with a model (`+0x10`) has its hardpoint's node stamped first ([construction](../simulation/mech.md#construction)). After the mounts, `Collision_CollectComponentAnchors` fills three local arrays from the model and `HercPiece_BindComponentAnchors` binds the count it returns. The calls, each ES2's:

| ES1 | Arguments, in stack order | Does | ES2 |
|---|---|---|---|
| `Collision_ReplaceClusters` (`000096e8`) | model, count, offsets, template clusters | for each 10-byte node of the model (`+2`, count `+0`), `Collision_ReplaceNodeCluster` (`00009680`); for the first node that replaced a cluster, `Collision_ComputeAllBoundingSpheres` (`000094a0`), and it stops | `0040cec0` |
| `Collision_ReplaceNodeCluster` (`00009680`) | node, count, offsets, template clusters | for each 16-byte cluster of the node (`+6`, count `+4`), `Collision_ReplaceClusterIfMatch` (`000095f4`); 1 at the first that matched | `0040ce78` |
| `Collision_ReplaceClusterIfMatch` (`000095f4`) | cluster, count, offsets, template clusters | at the first template whose component (`+6`) is the cluster's, `Collision_CopyClusterSpheres` (`00008eb0`)(cluster, that offset, template): the template's 8-byte spheres copied into a new array (`__vec_new` with `CollisionSphere_ElementCtor` (`00008e68`)) with the offset added to each centre; 1 when one matched | `0040ce28`, `0040c834` |
| `HercPiece_ReplacePieces` (`00009ad4`) | table, count, template pieces, component indices | each component's 18-byte piece overwritten by `HercPiece_CopyRecord` (`000098e8`)(piece, template), which gives the copy a dependent list of its own (4 bytes a dependent, `operator_new`) | `0040d240`, `0040d050` |
| `HercPiece_SetInternalMaxima` (`000099e4`) | table, count, maxima, internal indices | the table's maxima array (`+4`) at each index takes its maximum | `0040d128` |
| `Collision_CollectComponentAnchors` (`000092e4`) | model, points, node ids, components | for each node, `Collision_CollectNodeAnchors` (`00009218`): per cluster its component (`+6`), the node's id (`+0`) and the address of its bound centre (`+8`); the total | `0040cb84`, `0040cafc` |
| `HercPiece_BindComponentAnchors` (`00009b28`) | table, points, node ids, components, count | the piece of each component takes the node id at `+0xc` and the point at `+0xe` | `0040d284` |

`Collision_CollectComponentIds` (`00009290`), (model, out), the component of every cluster through `Collision_CollectNodeComponents` (`000091bc`) and the total, is ES2's (`0040cb48`); `es2_xref.py` finds no reference to it ([Open](#open)). `Collision_RegisterObject` (`00009580`), (path), which `Mech_Constructor` and `FlyerType_LoadResources` call on `col\<name>`, is ES2's (`0040cd88`): it opens the stream, asserts at severity 5 (`collide.cpp` line `0x101`) when its status is set, loads the file with `Collision_LoadRecordArray` (`000094f0`) into the next 6-byte slot of `CollisionTable_Array` (`000bc2ac`) by `CollisionTable_Cursor` (`00090da2`), which it increments, and returns the slot.

## Where the code sits

The loaders and the join are in the object files of `collide.cpp` and `damage.cpp`, and the damage header's functions in the file after them ([where the object files start and end](../simulation/component-damage.md#where-the-object-files-start-and-end)).

## The files

| Files | Internals |
|---|---|
| `ACHILLES`, `APOLLO`, `BULLDOG`, `COLOSSUS`, `GUNSLING`, `HERCULES`, `MIRIMAC`, `MONGOOSE`, `PANTHER`, `PATRIOT`, `RAMSES`, `RAPTOR`, `ROADRUNN`, `SCORPION`, `TOMAHAWK` | 21 |
| `SPIDER` | 12 |
| `SKIMMER` | 1 |

These are the sixteen chassis `MECHS.NAM` names and the two flyer types `FLYERS.NAM` names, `SKIMMER` and `HOVTANK`.

## `HOVTANK.DMG`

28 bytes that do not fit the layout: read by it, they hold three internals (0, −300, 0) and a piece count of 150, where 16 bytes remain. The flyer load for type 1 reaches `dmg\HOVTANK` only after reading `dat\HOVTANK` with `Ovl_ReadFile`, whose failed open is a severity-5 assert, fatal unless the debugger flag `000b5bcc` is set ([severities](../simulation/asserts.md#severities)); no ES1 volume holds `dat\HOVTANK`, so a mission placing a `HOVTANK` ends the program there.

Read as words, the bytes are an `int16` 3, six Q16 values (−300, 150, 0, 150, 300, 150) and an `int16` 1500:

```
03 00 | 00 00 d4 fe | 00 00 96 00 | 00 00 00 00 | 00 00 96 00 | 00 00 2c 01 | 00 00 96 00 | dc 05
```

## Open

- **Open:** what reaches `Collision_CollectComponentIds`, to which `es2_xref.py` finds no branch or stored pointer.
- **Open:** what `HOVTANK.DMG`'s bytes are. `es2_xref` finds two calls of `Damage_LoadMechDmgFile` and one of `HercPiece_LoadTable`, both on the paths above, and no reference to `dmg` other than those two pushes; a reader of the file in another layout was not found.
