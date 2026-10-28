# DBSIM.EXE and GO.EXE — `dat\MFORMS`, `FFORMS` and `BFORMS`, the formation tables

**A group's members stand in formation around its first member: per formation, each table gives where each later member stands from the first, turned with the first member's heading.** `MFORMS.DAT` places HERCs, `FFORMS.DAT` flyers and `BFORMS.DAT` structures. The first two hold ES2's bytes; `BFORMS.DAT` has a layout of its own, with no per-slot heading and no ground patch. ES2's tables and the placement rule are in [ES2's `script.dat` placement](../../retail/formats/script-dat.md#placement--the-actual-rule). Addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The layouts

Each file is an `int16` count and that many fixed records, little-endian, nothing after. Slot *k* of a record, *k* from 1, is where member *k* of the group stands; member 0 stands on the group's point.

| File | Record | Slots | A slot |
|---|---|---|---|
| `MFORMS.DAT` | `0x1c` bytes | 7 | `int16` X, `int16` Y |
| `FFORMS.DAT` | `0x12` bytes | 3 | `int16` X, Y and Z |
| `BFORMS.DAT` | `0x38` bytes | 7 | `int32` X, `int32` Y |

`SIMVOL0.VOL`'s `MFORMS.DAT` (142 bytes) and `FFORMS.DAT` (92 bytes) are byte-identical to ES2's, five formations each ([ES2's flyer formations](../../retail/formats/script-dat.md#the-flyer-formation-table)); `ZONES.VOL` holds the same `MFORMS.DAT` for the shell. `BFORMS.DAT` is 282 bytes, five formations, where ES2's is 3,186 bytes of 17 formations of 10-byte slots, each slot with a heading, and each formation with a ground patch ([ES2's trailer](../../retail/formats/script-dat.md#the-per-formation-trailer)). ES1's five, slots 1 to 7, (X, Y):

| Formation | Slots |
|---|---|
| 0 | (0, 30000), (22500, −15000), (−22500, −15000), (45000, 30000), (0, −60000), (−45000, 30000), (−45000, −60000) |
| 1 | (−25000, 20000), (25000, 20000), (−25000, −20000), (25000, −20000), (0, −40000), (−25000, −60000), (25000, −60000) |
| 2 | (30000, −30000), (0, −60000), (−30000, −30000), (22500, −7500), (22500, −52500), (−22500, −52500), (−22500, −7500) |
| 3 | (40000, 0), (−40000, 0), (80000, 0), (−80000, 0), (120000, 0), (−120000, 0), (160000, 0) |
| 4 | (0, −40000), (−50000, −40000), (−25000, −20000), (−50000, 0), (−25000, 20000), (−25000, −60000), (−25000, −20000) |

## The loads

None of the loaders checks the file's length.

- **`MFORMS.DAT`.** `Mech_LoadResources` (`000198f0`) reads the count into `000bc448` and the records into an array at `000bc44a`.
- **`FFORMS.DAT`.** `Flyer_LoadResources` (`0001c558`, `flyersys.cpp`), a phase-2 subsystem loader the static initialiser `FlyerSys_RegisterSubsystem` (`0001c814`) registers, sets the two-byte flyer type-slot table `000bc466` to `0xff` and the count `000bc464` to 0, opens `dat\fforms`, asserting at severity 5 (line `0x26`) when the stream status is set, reads the count into `000bc470` and count × `0x12` bytes into an array of `FlyerFormation_ElementCtor` (`0001c618`) elements `__vec_new` builds at `000bc472`. ES2's `Flyer_LoadResources` (`00422d90`) is the same with a `0x22`-byte slot table.
- **`BFORMS.DAT`.** `Base_LoadResources` (`000046ac`) reads the count into `000bc18e` and the records into an array at `000bc190` ([the load](bases-dat.md#the-load)).

## Where a member stands

`Mech_ApplyFormationOffset` (`00012ed0`), `Flyer_ApplyFormationOffset` (`0001b524`) and `Base_ApplyFormationOffset` (`00004474`), each given the object and a position in stack order, do nothing when the object's member slot, its `int16` at `+0x43`, is 0. Otherwise each takes the slot's offset for the formation whose number is at `+2` of the object's group (`+0x3f`) and adds it to the position with `Formation_RotateAndAddOffset` (`0000d9a0`). `es2_xref.py` finds them called directly, the mech's from `Mech_AttachToGroup` (`00013124`, [the HERC's construction](../simulation/mech.md#construction)), which writes the group and the slot before it places the machine, and `Ai_KeepFormation` (`00019660`, [navigation](../simulation/mech-ai.md#navigation)), the flyer's from `Flyer_AttachToGroup` (`0001b574`) and `Flyer_FormationStep` (`0001ba88`), the structure's from `000044a4`.

| Function | Arguments, stack order | Returns |
|---|---|---|
| `Formation_GetSlotOffset` (`0001a098`) | slot, formation | the static point `000af720`: the `int16` pair at `000bc44a` + formation × `0x1c` + slot × 4 − 4, and a Z set to 0 on the first call |
| `FlyerFormation_GetSlotOffset` (`0001c7f0`) | slot, formation | the address `000bc472` + formation × `0x12` + slot × 6 − 6, the slot itself |
| `BaseFormation_GetSlotOffset` (`0000441c`) | object, slot, formation | the static point `000aee08`: the `int32` pair at `000bc190` + formation × `0x38` + slot × 8 − 8, and a Z set to 0 on the first call |

Neither number is checked against the table. The mech's and the flyer's callers widen the three `int16` to `int32`. `Formation_RotateAndAddOffset`, given the object, the position and the offset, takes the group's first member (the first pointer of the array the group's `+0xe` points at), rotates the offset's X and Y by that member's transform (`Context_GetTransform` of its `+6`, `Math_Rotate2DPoint`) and adds the rotated X and Y and the unrotated Z to the position. ES2's (`00411d64`) reads the member array at the group's `+0xc`.

ES2's structure slot also carries a heading that turns the structure, and its formation a ground patch ([ES2's base formations](../../retail/formats/script-dat.md#placement--the-actual-rule)); ES1's record has room for neither, and `es2_xref.py` finds the array's address `000bc190` only in `Base_LoadResources` and `BaseFormation_GetSlotOffset`.

## The shell's squad

`ShellMap_PlaceSquad` (`ES1GO 0000cab1`, `src\shellmap.cpp`) places the player's squad on the briefing's map, the second half of ES2's [squad placement](../../retail/shell/mission-map.md#the-squads-positions) without its `player.mec` read. Given the map's object, it:

1. sets the squad size `+0xc6` from `Squad_MemberCount` (`ES1GO 0007ebc8`), the number of the first group's members ([loading a mission](msn-mission-file.md#loading-a-mission));
2. opens `dat\mforms`, asserting at severity 5 (line `0x59e`) when the stream status is set, reads the count into a local it does not use, and reads formation + 1 records of `0x1c` bytes one over another into one buffer, which ends holding the formation's, the formation being the map's `+0x9e`;
3. takes a heading: the map's `+0xa4`, when it is not negative, indexes the `int16` table at `+0x58`; otherwise, with a path at `+0x6a` of two points or more, `ES1GO 000553bc` of its first leg less `0x4000`; otherwise 0. `Math_BuildRotation2D` turns it into a rotation;
4. takes an anchor: point `+0xa2` of the 12-byte points at `+0x52`, or the path's first point when `+0xa2` is −1;
5. for each of the eight member refs at `+0xa8` that is not −1 and is below the squad size, sets the member's position at `+0xd4` + 12 × *k* to the anchor, and for *k* above 0 adds the record's slot *k*, Z 0, turned by the rotation through `ES1GO 000552e0`.

A formation past the file's count reads past its records.

## Open

- **Open:** the structure's other caller, `000044a4`, and so what fills a structure's `+0x43` and `+0x3f`; a HERC's are `Mech_AttachToGroup`'s, a flyer's `Flyer_AttachToGroup` (`0001b574`)'s ([joining its group](../simulation/flyers.md#joining-its-group--flyer_attachtogroup-0001b574)), and `Flyer_FormationStep` (`0001ba88`) is a flyer's station keeping ([the thinks](../simulation/flyers.md#the-thinks)).
- **Open:** `ES1GO 000553bc` and `000552e0`, the shell's heading from a path leg and its rotation, and how they round; and what fills the map's `+0x58`, `+0x9e`, `+0xa2` and `+0xa4`.
