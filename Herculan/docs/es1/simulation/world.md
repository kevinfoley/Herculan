# DBSIM.EXE — world.cpp, the mission's handoff and its results

**At the end of a mission ES1's simulator writes what happened for the shell in two files, as ES2's does: `data\results.dat`, with the outcome, the salvage and the state of every machine in the player's squad, and `data\mission.var`, the campaign flags.** `world.cpp` holds that writer and the load that reads the shell's files when the mission starts ([`script-dat.md`](../formats/script-dat.md#reading)). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's, whose `world.cpp` asserts in the same two functions, `Mission_WriteResults` and `DBSim_LoadScriptDat`.

## Where the module starts

`world.cpp`'s code opens at `0001d5fc` and its initialised data at `0009225e`. Both open with the copy of `ConfigPart`'s destructors and vtables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): the four functions `0001d5fc`–`0001d697` and the tables `0009225e` and `00092292`. `Mission_WriteResults` (`0001d698`), the first function to assert `world.cpp`, follows the run; in the data the four `int16` 2800, 2000, 1400 and 1000 at `000922c4` follow the tables, and then the strings from `000922cc` (`data\results.dat`, the source path, `results.dat`, …).

The object file before it, `0001c824`–`0001d5fb` with its data at `00092142`–`0009225d`, opens with its own run and tables, and its data holds its switches' jump tables and no source path, so the asserts give it no name. It holds the mission groups, actions and trigger areas ([`mission-groups.md`](mission-groups.md)): none of its functions belongs to `flyersys.cpp` or to `world.cpp`. `flyersys.cpp` ends at its static initialiser, `Flyer_RegisterSubsystem` (`0001c814`), and its data with the string `flyers` at `00092139`; its functions in this stretch, `FlyerTypes_LoadAll` (`0001c770`) and `FlyerFormation_GetSlotOffset` (`0001c7f0`), are in [`nam-name-list.md`](../formats/nam-name-list.md) and [`formations-dat.md`](../formats/formations-dat.md).

The next run, at `0002019c`, opens `terrain.cpp`: `Terrain_LoadZoneShapes` (`00020238`) follows it ([the simulator's load](../formats/zone-terrain.md#the-simulators-load)). So `world.cpp` is `0001d5fc`–`0002019b`.

ES2's group functions stand in the same place, `Route_AdvanceCursor` (`0042313c`) to `Group_ReportIfAllOutOfAction` (`00423f30`) between `FlyerSys_RegisterSubsystem` (`00423068`) and `Mission_WriteResults` (`0042412c`); which of ES2's modules holds them is [Open](#open).

## Writing the results — `Mission_WriteResults` (`0001d698`)

ES2's writer ([what the mission leaves the shell](../../retail/simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)), given the player's machine. `es2_xref.py` finds one call, from `0005b568`, which also calls `GndTex_Free` ([the ground texture](../rendering/ground-and-sky.md#loading-the-ground-texture)). With *g* the player's group (`+0x3f`) and *w* header word 3 of `data\script.dat` (the dword at `000bc4a2`):

1. It asks `0000f0f0` (*g*, *w*) for the outcome.
2. It opens `data\results.dat` through `Path_ApplyAltDrive` and `FileWStream_Ctor`, asserting at severity 5 (`world.cpp` line `0x58`) when it cannot, and writes:

| Bytes | Field |
|---|---|
| 2 | the outcome, as `0000f0f0` returned it |
| 4 | `Math_Q10Multiply`(`Mission_TotalSalvage`(*g*, *w*), 2500) + flag 7 × 25000 |
| 2 | the number of salvage pairs, the `int16` at `000bc4b2` |
| 4 × that | each pair at `000bc4b4`, two `int16` |
| 72 per machine | `Group_WriteStatusBlocks` (*g*): for each member of the player's group in order, 33 conditions and 6 bytes of kill tallies ([the status blocks](mission-groups.md#what-the-results-read)) |

3. It closes the file, opens `data\mission.var` (assert at line `0x6d`) and writes the 100 flags at `000bc4b8`, 200 bytes, back over it.

A results file is therefore 8 + 4 × pairs + 72 × squad machines bytes.

Where ES2's differs:

| | ES1 | ES2 |
|---|---|---|
| the outcome | `0000f0f0`'s answer for the player's group and header word 3, written as it is | 1 when its objectives' evaluation for the player answers 9, otherwise 0 |
| the salvage bonus | 25,000 per unit of flag 7 (`000bc4c6`), which `DBSim_LoadScriptDat` zeroes at the load | per unit of counter 20 |
| `mission.var` | the 100 flags, 200 bytes | 1,000 counters, 2,000 bytes |

`Mission_TotalSalvage` (`0001d434`) ignores *w*, as ES2's ignores its second argument ([what the results read](mission-groups.md#what-the-results-read)).

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `HeapPtr_DtorWorldCopy`, `ConfigPart_DtorWorldCopy`, `ConfigPart_GetClassTagWorldCopy`, `TSPartBase_DtorWorldCopy` | `0001d5fc`, `0001d624`, `0001d663`, `0001d66c` | the run that opens the module ([module copies](../formats/compiler.md#vtables)); `es2_xref.py` finds no branch or stored pointer to `0001d5fc`, and the tables' slots `+0x1c` and `+4` hold the other three |
| `Mission_WriteResults` | `0001d698` | ES2's `Mission_WriteResults` (`0042412c`) |
| `DBSim_LoadScriptDat` | `0001d850` | [reading `data\script.dat`](../formats/script-dat.md#reading) |

## Globals

| | Address |
|---|---|
| `data\script.dat`'s 20-byte header, `ScriptDatHeader`; word 3 at `000bc4a2` | `000bc49c` |
| the salvage pairs' count and the pointer to them | `000bc4b2`, `000bc4b4` |
| `data\mission.var`'s 100 flags, `MissionVars`; flag 7 at `000bc4c6` | `000bc4b8` |

## Open

- **Open:** the rest of `world.cpp`, `0001d850`–`0002019b`, and the start of `terrain.cpp` after it: the next two batches read `0001d850`–`00020580` and `000205dc`–`00021fb0`. Among them are the second pass over `data\script.dat` (`0001e638`, which builds the groups), the callers of the action tests (`0001f87c`), `0001f7e8`, which `Mech_SalvageValue` queues each salvage pair through, and `0001f855`, which reads the four `int16` at `000922c4`.
- **Open:** what `0000f0f0` computes from the player's group and header word 3, and so what the outcome and word 3 mean. Its counterpart is the objectives' evaluation ES2's writer calls ([ES2's outcome](../../retail/simulation/mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)); the body is not read.
- **Open:** how `GO.EXE` reads `data\results.dat`.
- **Open:** which of ES2's modules holds its group functions (`0042313c`–`00423f30`): its `WORLD.CPP` asserts sit in `Mission_WriteResults` and `DBSim_LoadScriptDat`, after them.
- **Open:** the player object's field `+0x3f`, its group, has no `known_structs.json` layout: no ES1 object layout is recorded there ([the object fields](mission-groups.md#the-object-fields)).
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0001d698` set during a mission fires once as the mission ends, and after `F5` the install's `DATA\RESULTS.DAT` has just been rewritten with 8 + 4 × pairs + 72 × squad machines bytes: 80 for a one-HERC squad that salvaged nothing.
