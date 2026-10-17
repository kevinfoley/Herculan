# GO.EXE and DBSIM.EXE — data\script.dat and data\mission.var

ES1's shell hands a mission to the simulator in two files, as ES2's does ([ES2's `script.dat`](../../retail/formats/script-dat.md)): `data\script.dat`, the mission's surviving records with the fields the simulator needs, and `data\mission.var`, the campaign flags. The shell writes both when it loads a mission ([loading a mission](msn-mission-file.md#loading-a-mission)) and the simulator reads both when it loads the world. ES1's `script.dat` has nine blocks where ES2's has thirteen, and there is no `data\mission.str`. Addresses are `ES1GO`'s for the writer and `ES1DBSIM`'s for the reader.

## Writing

`WriteScriptDatFile` (`ES1GO 0003445b`) writes to the `FileRWStream` the mission load opened before reading the rows. That stream opens without truncating ([streams](streams.md#the-file-classes)), so a mission shorter than the one written before it leaves that one's tail in the file, as ES2's does.

The header is ten `int16`, the [header words](msn-mission-file.md#header-patches) at `000ab21a`–`000ab22c`, except that offset 4 holds the load's mode (`000aad56`) in place of word 2.

Nine blocks follow, each an `int16` count of the row's surviving records whose GUID is not `-1` and then those records' fields, refs resolved ([the layout](msn-mission-file.md#the-layout)):

| Block | `.MSN` row | Fields, by `.MSN` offset | Bytes |
|---|---|---|---|
| 1 | points | X, Y, Z (`0x0a`–`0x15`) | 12 |
| 2 | headings | `0x08` | 2 |
| 3 | waypoint groups | the waypoint count, then each waypoint | 2 + 2 per waypoint |
| 4 | trigger areas | `0x06`, `0x08`, `0x0a` | 6 |
| 5 | actions | `0x06`, `0x08`, `0x0a`–`0x19`, `0x1a`, `0x1c` | 24 |
| 6 | mechs | `0x08`–`0x69` | 98 |
| 7 | flyers | `0x08`–`0x3f` | 56 |
| 8 | bases | `0x08`–`0x17` | 16 |
| 9 | groups | `0x06`–`0x57` | 82 |

A block record is the listed fields in `.MSN` order, so an offset into it is the `.MSN` offset less the first listed field's: less `0x08` for mechs, flyers and bases, `0x06` for groups, actions and trigger areas, and `0x0a` for points.

`data\mission.var` is the 100 `int16` flags at `ES1GO 000ab244`, 200 bytes, written after the rows are read through a `FileWStream`, which truncates.

## Reading

`DBSim_LoadScriptDat` (`ES1DBSIM 0001d850`) is the world load's first pass:

1. It clears the roster slot arrays: for mechs `000af7b4`, `000af818` and `000af87c`, 100 bytes each; for flyers `000af8e0` and `000af914`, and for bases `000af948`, `000af97c` and `000af9b0`, 50 each.
2. It reads `data\mission.var`'s 200 bytes into `000bc4b8` and sets flag 7 (`000bc4c6`) to 0. The file must open (severity-5 assert, `world.cpp` line `0x8d`).
3. It reads `data\player.mec`, the player's HERCs and pilots ([the simulator's read](career-log-and-player-mec.md#the-simulators-read)).
4. It opens `data\script.dat` (assert at line `0xb2`) and reads the header into `000bc49c` and the nine blocks, with the strides above. It keeps:

| Block | Kept |
|---|---|
| 1 | every point, at `000bc480`, and the least and greatest X and Y at `000af7a4`–`000af7b0` |
| 2 | every heading, at `000bc484`, as the file's value shifted left 14 into 16 bits |
| 3 | every waypoint group, at `000bc48a`, as its count and a pointer to an array of pointers to its points |
| 4 | every trigger area, resolved by `TriggerArea_Resolve` (`0001cb48`) into a 10-byte record at `000bc490` ([trigger areas](../simulation/mission-groups.md#trigger-areas)) |
| 5 | every action as a 16-byte record at `000bc496`: the type and verb, the number of area refs before the first negative one with a pointer to those areas, and the two words from `.MSN` `0x1a` and `0x1c` at `+0xc` and `+0xe`, the flag and operation the action [writes](../simulation/mission-groups.md#actions) |
| 6, 7, 8 | the type of each record, from block offsets `0x28`, `0x2c` and `0` (`.MSN` `0x30`, `0x34` and `0x08`) |
| 9 | from the second record on, each member ref (block `0x34`, `.MSN` `0x3a`) that is not negative marks its slot live in the roster block `0x28` names; a base member also stores block `0x44` (`.MSN` `0x4a`) for the slot, which is passed with the base's type to `000045a4` |

5. It marks each `data\player.mec` entry live as a mech slot after the block's count, as ES2's pass marks the squad ([placement](../../retail/formats/script-dat.md#placement--the-actual-rule)).
6. Among the calls that follow, `Terrain_LoadZoneShapes` (`00020238`) loads the shapes of the zone in header word 1, from the shape library of the theater in word 0 ([`zone-terrain.md`](zone-terrain.md#the-simulators-load)), and `World_LoadTheater` (`000251c4`) the theater's palette and world file, which words 0, 8 and 9 name ([`wld-world.md`](wld-world.md#which-file)).

The headings in the two `.MSN` files walked by hand (`SM_SCT50`, `B0_C2_60`) are multiples of 45 from 0 to 315. Shifted left 14 into 16 bits, a value keeps only its two low bits, so 45 and 225 both become `0x4000` ([Open](#open)).

### The second pass

`DBSim_SpawnMissionObjects` (`0001e638`) builds the mission's objects, as ES2's [second pass](../../retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps) does; `es2_xref.py` finds one call, from `Sim_StartMission` (`0005a9dc`). It sets `Mission_StatusRaised` (`000bc49a`, [the poll](../simulation/mission-status.md#the-poll--mission_pollstatus-0000f454)), the word before the header, to `0xffff`, opens `data\script.dat` again and reads past the header and blocks 1 to 5 into a 2,000-byte stack buffer, and then reads blocks 6 to 9 in full.

An object takes its position and rotation from its record's refs. A point ref that is not negative gives the block-1 point's X, Y and Z; a negative one gives the position sentinel (0, 0, −1) at `000afa00`. A heading ref that is not negative gives the rotation (0, 0, the heading); a negative one gives the rotation sentinel (−32768, −32768, 0) at `000afa10`. The position goes to the object's `+0x20` (three `int32`), the rotation to `+6` (three `int16`, the heading at `+0xa`), and the `int16` at `+0x2c` is zeroed. ES2's position sentinel is (0, 0, −32768) and its rotation sentinel the same as ES1's.

**Block 6, then the squad.** For each slot from 0 to the block's count plus the `data\player.mec` entry count (`000af9e2`), it reads a 98-byte record while the slot is inside the block, and builds a mech when the slot is live (`000af7b4`): `Mech_Constructor` (`00011330`) with no object, so from the mech pool, and the type, the record's `0x28` or, past the block, the entry's `+4`. It calls `WeaponModels_LoadShapeChunks` (`0000bc60`) before the loop and `WeaponModels_FreeShapeChunks` (`0000bd40`) after it, so the raw weapon models exist while the HERCs are built ([the weapon models](../simulation/weapon-mounts.md#the-weapon-models)).

| Record offset (`.MSN`) | What the second pass does with it |
|---|---|
| `0x00` (`0x08`) | when not 0, the mech's `+0x91` is set to 1, where ES2's sets its radar setting `+0x97` |
| `0x02` (`0x0a`) | when not negative, stored at `+0x1e6`, where ES2's stores its cruise speed at `+0x252` |
| `0x28` (`0x30`) | the type |
| `0x2a` (`0x32`), `0x46` (`0x4e`) | the weapon fit and the ammunition types, ten `int16` each, passed with the mech to `Mech_ConfigureLoadout` (`00012c00`, [construction](../simulation/mech.md#construction)) as (mech, `0x46`, `0x2a`) in stack order, as ES2's passes its fit and ammunition |
| `0x3e` (`0x46`) | the point ref; the position's Z is the point's Z plus the type record's (`+0x188`) `int16` at `+0x16` |
| `0x40` (`0x48`) | the heading ref |
| `0x42`, `0x44` (`0x4a`, `0x4c`) | stored at `+0x174` and `+0x176` |
| `0x5c`, `0x5e` (`0x64`, `0x66`) | action refs: a pointer to the 16-byte action at `+0x16c` and `+0x170`, or 0 for a negative ref |
| `0x60` (`0x68`) | the starting condition, passed with the mech to `Mech_ApplyStartingCondition` (`00012f20`, [construction](../simulation/mech.md#construction)) |

The second pass does not read record offsets `0x04`–`0x27` and `0x5a` (`.MSN` `0x0c`–`0x2f` and `0x62`).

A `data\player.mec` entry *k* builds the squad's machine at slot block count + *k*, with both sentinels; its `+8` and `+0x20` go to `Mech_ConfigureLoadout` as the record's `0x2a` and `0x46` do, its three condition spans `+0x3c`, `+0x56` and `+0x6a` go to `Mech_ApplySquadCondition` (`000107f0`, [the squad's condition](../simulation/damage-readouts.md)) with the mech's `+0x19c` as (`+0x19c`, `+0x6a`, `+0x56`, `+0x3c`) in stack order, as ES2's pass does, and its `+0` to the mech's `+0x232`, where ES2's stores the pilot index at `+0x29c`. The entry whose index is the word `data\player.mec` opens with (`000af9fa`) is the player's: the mech goes to `000bc42a` and its `+0x9d` is set to 1. Unlike ES2's, the position takes no ground height and no lift for a type that flies ([placement](../../retail/formats/script-dat.md#placement--the-actual-rule)).

**Block 7.** For each live flyer slot (`000af8e0`), `0001ac58` with no object and the 56-byte record's type at `0x2c` (`.MSN` `0x34`); the point ref `0x28` with no height added, the heading ref `0x2a`, the action refs `0x32` and `0x34` (`.MSN` `0x3a`, `0x3c`) to `+0x16c` and `+0x170`, and `0x2e` and `0x30` (`.MSN` `0x36`, `0x38`) to `+0x174` and `+0x176`. `0x36` (`.MSN` `0x3e`) is not read.

**Block 8.** For each live base slot (`000af948`), `00003cbc` with the byte the first pass kept for the slot (`000af9b0`) and the 16-byte record, as (byte, record) in stack order; the point ref `0x02` with no height added, the heading ref `0x04`, the action refs `0x0a` and `0x0c` to `+0x16c` and `+0x170`, and `0x06` and `0x08` to `+0x174` and `+0x176`. ES2's marks each structure's footprint, takes the ground height and, once every object is placed, levels the ground and settles every structure again; ES1's second pass does none of these.

**Block 9.** One 82-byte record for each element of the group array the first pass allocated (`MissionGroups`, `000bc476`, its count at `000bc47a`), each built by `DBSim_BuildGroupRecord` (`0001cff4`) as (group, point, `MissionGroups`, heading, action, route, record) in stack order ([the group record](../simulation/mission-groups.md#the-group-record)):

- the point: the block-1 point the ref `0x2e` (`.MSN` `0x34`) names, or 0 for `-1`;
- the route: the waypoint group the ref `0x32` (`.MSN` `0x38`) names, or 0 for `-1`;
- the heading: the heading the ref `0x30` (`.MSN` `0x36`) names; for a negative ref, `Math_HeadingToward` from the route's first waypoint to its second when the route has two or more, otherwise 0. ES2's takes the route of the group's first order;
- the action: the action the ref `0x4a` (`.MSN` `0x50`) names, or 0 for `-1`;
- the target ref `0x48` (`.MSN` `0x4e`) is replaced by its roster's slot-to-pool map entry, `000af818`, `000af914` or `000af97c`, for target kinds 1, 2 and 3; a group target is left as it is;
- the eight member refs `0x34` (`.MSN` `0x3a`) are replaced in the same way through the map their kind `0x28` names. The first record's are instead the mech map's entries at block-6 count + *k* for each `data\player.mec` entry *k*, and `-1` past the last, so the squad's machines are its members in entry order, as ES2's are ([placement](../../retail/formats/script-dat.md#placement--the-actual-rule)); and the first record's `0x2c` (`.MSN` `0x32`), copied to the group's `+4`, becomes 8 when it is 11.

ES2's pass goes on to the orders, the objectives, the ground and the actions' targets; ES1's closes the file after the groups.

### The header

Besides the theater and world words above, the simulator reads header word 7 as the difficulty and the mode at offset 4 as an override of it ([the difficulty scale](../simulation/world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c)), and word 8 for the contact scan range ([small functions](../simulation/world.md#small-functions)), which are ES2's header word 7 and word 8 ([ES2's header](../../retail/formats/script-dat.md#header-format)).

## Open

- **Open:** the second pass's callees, not read: `0001ac58` (a flyer from its type) and `00003cbc` (a base from its record), where ES2's pass calls `Flyer_Constructor` and `Base_Construct`.
- **Open:** what reads an object's `+0x174`/`+0x176` pair and its `+0x16c`/`+0x170` actions, besides `Sim_RaycastObjectList` (`0001f1d0`), which fires the struck object's `+0x16c` ([shots](../simulation/world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0)); what the mech's `+0x91` and `+0x1e6` are to ES1; and who fills the slot-to-pool maps `000af818`, `000af914` and `000af97c` the second pass reads, which `DBSim_LoadScriptDat`'s unread calls after the blocks may do.
- **Open:** what the group's `+4` is, and why the squad's 11 there becomes 8. `Mission_Status` tests the player's group's for 6 ([the status](../simulation/mission-status.md#the-status--mission_status-0000f4e8)).
- **Open:** whether ES1's headings are degrees; the second pass stores the shifted value as an object's rotation `+0xa` and as a group's heading.
- **Open:** what header words 4 to 6 mean to the simulator, and what the other five readers of the mode at offset 4 do with it. Word 3 selects the question the mission status asks and the verb of the squad's report that `Mission_WriteResults` writes as the outcome ([the mission status](../simulation/mission-status.md)). What reads `000bc4b8`'s flags during a mission besides that writer's flag 7: the actions and the groups' out-of-action reports write them ([mission groups](../simulation/mission-groups.md)), and `Mission_WriteResults` writes them back to `data\mission.var`.
