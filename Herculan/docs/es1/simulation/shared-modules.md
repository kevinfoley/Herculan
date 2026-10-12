# DBSIM.EXE — the modules ES2's simulator shares

ES1's and ES2's simulators both name `bullet`, `collide`, `damage`, `debris`, `flyersys` and `mechsys` among their source modules ([the module map](../../herculan/plan-es1-support.md#the-binaries)). Addresses are in `ES1DBSIM`, the program image of ES1's `DBSIM.EXE` ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2 names are DBSIM's. This page holds a sample of sixteen function pairs until each module has a page of its own.

## The sample

**The modules share a skeleton; what they carry, and some of what they do, differs.** Sixteen function pairs across the six modules, each read in both decompiles, have the same order of operations, and their asserts sit in the same functions:

| Module | ES2 | `ES1DBSIM` | The same | Different in ES1 |
|---|---|---|---|---|
| collide | `Collision_LoadRecordArray`, `Collision_RegisterObject` | `000094f0`, `00009580` | assert lines 238 and 257, 10-byte nodes, 6-byte table slots | — |
| collide | `Collision_ReadNode`, `Collision_ComputeAllBoundingSpheres`, `Collision_ComputeBoundingSphere` | `000093c8`, `000094a0`, `00008b8c` | statement for statement: 16-byte clusters, the ±10000 box seed, the radius from `Math_FastMagnitude3D` | — |
| damage | `Damage_LoadMechDmgFile`, `HercPiece_ReadRecord` | `00009a2c`, `00009890` | assert line 133, 12-byte table slots, the 18-byte record and its initialised fields | — |
| flyersys | `Flyer_LoadResources` | `0001c558` | assert line 38, 18-byte `fforms` records | a 2-byte type-slot table, against 0x22 |
| flyersys | `Flyer_ComponentDamageWrite` | `0001b290` | the cascade, kill credit through vtable slot 24, Z set to −100000, the out-of-action report, the defeat action, the behaviour state | computer message 3, against 0x2e; ES2 adds a null check on the attacker, the velocity global and the sound stop |
| mechsys | `Mech_LoadResources` | `000198f0` | 28-byte `mforms` records | assert line 48, against 52; a 0x10-byte slot table, against 0x55; 4 texture groups kept as loaded, against 7 packed into an atlas |
| mechsys | `maybe_MechTypeSlot_Allocate`, `Mechs_InitAllTypes` | `00019a40`, `00019ee0` | the slot allocation, the per-slot collision, damage and gun-layout reservations, the init loop over every type id | a 0x1c4-byte type record, against 0x21e; type ids 0–15, the 16 its `MECHS.NAM` names, where ES2 allows 0–0x54 and its `MECHS.NAM` names 21 |
| bullet | `Bullet_LoadResources` | `00007bb0` | the load steps and the highest-shape scan | assert line 41, against 50; 10-byte records, against 14; ES2 packs the bitmaps into an atlas |
| bullet | `Bullet_TickUpdate` | `00007f84` | the animation countdown, the forward step through the rate-over-tick helper and `Transform_ApplyToShortPoint`, the Q10 damage scale, the raycast, the position commit | a bullet ages one count per tick against its type's life, where ES2's ages by `Math_IntegrateRateOverTick(0x200)` against life × 0x200; ES2 adds homing, the type-9 blast and difficulty scaling; the instance fields sit 6 bytes lower |
| debris | `Debris_LoadResources` with `Debris_LoadDatabase`, `Debris_LoadPieceList` | `0000607c`, `00005ed0` | the group header, the piece-weight sum, the highest shape index | assert line 89, against 161; one database loaded into globals, where ES2 loads any number into a 12-byte struct with a texture bank; 4-byte pieces, against 14 with two angles |

The differences are of four kinds: none beyond the compiler (collide, damage), table and record sizes, features ES2 added to the same skeleton, and one change of meaning, the bullet's age. Beneath them the [fixed-point primitives](fixed-point-math.md#the-primitives) round where ES2's truncate.

## Open

- **Open:** the ES1 functions the sampled ones call, which the sample pairs with their ES2 counterparts by position and arguments; none was read.
- **Open:** the rest of each module beyond the sixteen pairs.
