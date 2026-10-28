# DBSIM.EXE — `dat\BASES.DAT`, the structure type table

ES1's nine structure types are fixed 54-byte records: each names its shape, its wreck and which of two shape sets it belongs to, and the rest of its numbers sit after them. ES2's ([ES2's `BASES.DAT`](../../retail/formats/bases-dat.md)) are variable-length, each carrying its destructible parts inline. A search of `GO.EXE`'s image for `bases` finds nothing. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The layout

```
int16 count
byte[0x36][count]
```

The retail file is 488 bytes: 9 records, nothing left over.

### The type record

The loader reads each record into the first 54 bytes of a 62-byte runtime record, so the file's offsets are the record's. Offsets in bold are ones the loader reads.

| Offset | Holds | Retail values |
|---|---|---|
| `+0x00` | the type's index: [the status screen](../simulation/mfd-status-screen.md#the-paint) draws a structure as that frame of `bases` and names it by that entry of `STRINGS0.STR` group 20, through the record a structure's `+0x188` holds (stored at `00004245`) | the record's own index, 0 to 8 |
| **`+0x02`** | the shape's index in its set | 0 to 5 |
| **`+0x04`** | the wreck type in `dgs\bhulks` | 0 to 8; type 5 has 6, type 6 has 7, type 7 has 5 |
| **`+0x06`** | the animation thread count, as ES2's ([the type record](../../retail/formats/bases-dat.md#the-type-record)); non-zero also picks the second shape set and installs the type's `BASECOL.DAT` model | 1 on type 5, 2 on types 6 and 8 |
| `+0x08`–`+0x35` | 23 `int16` | below |

| Type | `+0x02` | `+0x04` | `+0x06` | `+0x08`, `+0x0a` | `+0x1c` | `+0x1e`–`+0x24` | `+0x26`, `+0x28` | `+0x2a`, `+0x2c` | `+0x30` | `+0x34` |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 0 | 0 | 0 | 15000, 5000 | 200 | 0, 0, 2440, 900 | 0, 0 | −1, 0 | 0 | 0 |
| 1 | 1 | 1 | 0 | 15000, 5000 | 200 | 0, −5300, 4700, 600 | 0, 0 | −1, 0 | 0 | 0 |
| 2 | 2 | 2 | 0 | 2000, 5000 | 400 | −680, −3580, 870, 1000 | 0, 0 | 0, 256 | 0 | 0 |
| 3 | 3 | 3 | 0 | 15000, 5000 | 200 | 0, −20, 270, 300 | 0, 0 | −1, 0 | 0 | 0 |
| 4 | 4 | 4 | 0 | 15000, 5000 | 200 | 0, 750, 1320, 1200 | 0, 0 | −1, 0 | 0 | 0 |
| 5 | 0 | 6 | 1 | 5000, 5000 | 200 | 0, −490, 2280, 300 | 100, 0 | −1, 0 | 4000 | 0 |
| 6 | 1 | 7 | 2 | 5000, 5000 | 200 | 0, 0, 2700, 250 | 30, 100 | −1, 0 | 2000 | 0 |
| 7 | 5 | 5 | 0 | 15000, 5000 | 200 | 4000, 1300, 3590, 600 | 0, 0 | −1, 0 | 0 | 0 |
| 8 | 2 | 8 | 2 | 5000, 2000 | 200 | 0, 0, 3640, 700 | 0, 0 | 0, 256 | 2000 | 1 |

In every record `+0x0c`, `+0x0e`, `+0x12`, `+0x14` and `+0x2e` are 0, `+0x10` is 1000, `+0x32` is 1500, and `+0x16`–`+0x1a` are 1000, 1000, 1000 except type 2's 2800, 2700, 6000.

What the structure code ([the structures](../simulation/structures.md)) reads them as:

| Offset | Read by | Meaning |
|---|---|---|
| `+0x08` | `Base_Construct`, `Base_ApplyDamage`, `Base_DamageFraction` | the hit points |
| `+0x0c`–`+0x10` | `Base_ThinkTick` | the point, in the structure's frame, of the collapse's large explosion and its smoke |
| `+0x12`–`+0x16`, `+0x18`–`+0x1c` | `Base_ThinkTick` | the center and half-extents of the box the collapse's small explosions are scattered in |
| `+0x1e`–`+0x22`, `+0x24` | `Base_DirectFireHitTest` | the weak spot's center and radius: a hit inside it does three times its damage |
| `+0x26`, `+0x28` | `Base_Construct` | the two threads' playback rates |
| `+0x2a`, `+0x2c` | `Base_ThinkTick`, `Base_ArmedThinkTick`, `Base_Construct` | the cell sequence a standing structure steps, −1 for none, and its frame interval: an idle flipbook on the MINING FACILITY, the muzzle flash on the GUN TOWER |
| `+0x30` | `Base_GetBodyRadius` | the body radius of a type with a collision model |
| `+0x32` | `Base_GetAimPoint`, `Base_GetAimNodeTransform` | how far up the structure anything aiming at it aims |
| `+0x34` | the HERCs' AI | 1 on the GUN TOWER, the armed type ([the AI](../simulation/structures.md#what-the-hercs-ai-does-with-a-structure)) |

## The load

`Base_LoadResources` (`000046ac`) is a phase-2 subsystem loader, registered by the static initialiser at `00004b80` with `RegisterSubsystemLoader` (`00000ac4`). In order:

1. It reads `dat\bforms` (asserting at severity 5, `base.cpp` line `0x270`, when the stream status is set): an `int16` count to `000bc18e` and that many `0x38`-byte [formations](formations-dat.md) into an array `__vec_new` builds at `000bc190`.
2. It opens `dat\bases` (line `0x27b`), reads the count into `000bc188`, builds that many `0x3e`-byte records at `g_BaseTypeTable` (`000bc18a`) with `__vec_new`, and allocates count × 6 bytes at `000bc194`. For each type it reads `0x36` bytes into its record and sets the record's `+0x3a` to the type's 6-byte slot at `000bc194` when `+0x06` is non-zero, and to 0 when not.
3. It opens `dat\basecol` (line `0x296`) and reads one [collision model](#the-collision-models) into each type's slot with `Collision_LoadRecordArray` (`000094f0`), in type order, for every type.
4. Over the types it keeps the greatest `+0x02` plus one of the first set at `000bc174` and of the second set at `000bc16a`, and the greatest `+0x04` plus one at `000bc17e`: 6, 3 and 9 in retail.
5. It loads every root of `dgs\bhulks`, the wrecks, with `Shape_LoadAllRoots` (`000635a8`)(0, `0x2bc0001`, path, `BaseHulkShapes` (`000bc180`)), the 0 a count of roots to skip, and puts no bank in any wreck's `+0x26`, where `BaseType_LoadShape` puts step 6's bank in each standing shape's. It allocates two zeroed arrays of `000bc174` pointers (`BaseShapes_Static` (`000bc176`), `BaseShapes_StaticEnemy` (`000bc17a`)) and two of `000bc16a` pointers (`BaseShapes_Animated` (`000bc16c`), `BaseShapes_AnimatedEnemy` (`000bc170`)), the shape tables of `dgs\bases`, `dgs\bases_e`, `dts\bases_an` and `dts\bases_ae`, which a mission's structures fill by type and side ([loading the shapes](../simulation/structures.md#loading-the-shapes--basetype_loadshape-000045a4)).
6. When `g_LowMemory` ([low memory](chassis-dat.md#low-memory)) is 0 it loads `dba\basetex` into `BaseTexture` (`000bc184`).

ES2's `Base_LoadResources` (`00405fac`) also reads `dat\lc_wpns` between the formations and the types, and reads each type field by field with its component array. ES1's type takes its collision model when `+0x06` is non-zero, where ES2's takes it when [`+0x30`](../../retail/formats/bases-dat.md#the-type-record) is.

## The collision models

**`BASECOL.DAT` holds every structure type's hit spheres, one model after another in type order, ES2's format.** Each model is ES2's walk of nodes, clusters and spheres, every field an `int16` ([ES2's collision models](../../retail/formats/collision-spheres.md#layout)); the file has no count of its own, the load reading as many models as `BASES.DAT` has types.

`Collision_LoadRecordArray` (`000094f0`, `collide.cpp`), given the type's 6-byte slot and the stream, asserts at severity 5 (line `0xee`, `collision data load`) when the stream status is set, reads the node count into the slot's `+0`, and for a count not 0 allocates count × 10 bytes at `+2` and reads each node with `Collision_ReadNode` (`000093c8`) and then `Collision_ComputeAllBoundingSpheres` (`000094a0`). `Collision_ReadNode` reads the node's index into `+0` and its cluster count into `+4`, and for a count not 0 builds that many 16-byte clusters with `__vec_new` and `CollisionCluster_ElementCtor` (`00009458`) at `+6` and reads each with `Collision_ReadCluster` (`0000938c`): the component index to `+6`, then the spheres with `Collision_ReadSphereArray` (`00008e00`), which reads a cluster's spheres when its count × 8, as an `int16`, is not 0, the same test as ES2's count & `0x1fff`. `Collision_ComputeAllBoundingSpheres` stores `Collision_ComputeBoundingSphere` (`00008b8c`)'s radius for each cluster at its `+0xe`. These are ES2's functions under the same names ([readers](../../retail/formats/collision-spheres.md#layout)); ES2's `Collision_ReadNode` builds its clusters with `Rtl_VectorNew`.

A type whose `+0x06` is 0 has its model read all the same and not installed. An installed model is what a standing structure's hit test tests a shot against, sphere by sphere, in the clusters whose component the hit test's flags let it strike, component 0 of the first three, the one every retail cluster names ([hits](../simulation/structures.md#hits--base_directfirehittest-00003a58)); a type without one is tested against its shape's collision volume ([the structures' tests](../simulation/terrain.md#the-structures-tests)). `SIMVOL0.VOL`'s `BASECOL.DAT` is 618 bytes, nine models with nothing left over: types 5, 6 and 8, the three with `+0x06` set, have 2, 4 and 2 nodes, and the other six none.

## Open

- **Open:** what reads `+0x0a` and `+0x2e`: none of the structure code's functions does ([its Open](../simulation/structures.md#open)).
