# DBSIM.EXE — the squad's condition and the damage readouts

**A squad HERC in ES1 starts a mission worn as the career left it, and its damage is read out for the screens, the results and the salvage, exactly as in ES2: the four functions are ES2's on ES1's damage header ([a squad machine's condition](../../retail/simulation/component-damage.md#a-squad-machines-condition--mech_applysquadcondition-00415068), [the damage readouts](../../retail/simulation/component-damage.md#the-damage-readouts--component_filldamagereadouts-004151a4), [what a wreck is worth](../../retail/simulation/component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60)).** The header is the machine's `+0x19c`, ES2's `+0x206`, with ES2's layout: the internals' damage array at `+0`, the components' at `+4`, their occupancy flags at `+8` and the `.DMG` table at `+0xc`. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)): the header first and the other arguments in the reverse of ES2's order, the shape a C++ member function's arguments take there.

## Where the object file starts and ends

The code is `000107f0`–`00010d5f`. It opens with `Mech_ApplySquadCondition`, after the two zero bytes that end [the aircraft state table's file](shields-and-behaviour-states.md#where-the-object-files-start-and-end), with no copy of `ConfigPart`'s destructors before it. `Mech_WeightedArmorRemaining`'s `RET 4` at `00010d5c` is followed by one zero byte and the run of `ConfigPart`'s copies at `00010d60` that opens the next file ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer, and no assert in it names a source path. Its functions are ES2's from `00415068` to `0041537c`, in the same order.

## The functions

- **`Mech_ApplySquadCondition` (`000107f0`)**, (header, hardpoint conditions, internal conditions, external conditions): ES2's, with ES2's counts. `es2_xref.py` finds one call, from [`DBSim_SpawnMissionObjects`](../formats/script-dat.md#the-second-pass), which passes a `data\player.mec` entry's `+0x6a`, `+0x56` and `+0x3c` ([the simulator's read](../formats/career-log-and-player-mec.md#the-simulators-read)): it reads the 13 `int16` external conditions at `+0x3c` for components 0–12, the first nine of the ten internal conditions at `+0x56` for internals 0–8, and the ten hardpoint conditions at `+0x6a` for the mount pairs. Each damage is (100 − condition) × maximum / 100, a signed division, stored as an `int16`; a mount pair's maximum is its armor plus its internal's maximum, added as an `int16`, and a mount whose share passes its armor is written −1 with the rest going to its internal, −1 when that passes the internal's maximum, the internal left as it was otherwise.
- **`Component_FillDamageReadouts` (`000109bc`)**, (header, buffer): ES2's, with ES2's counts. `es2_xref.py` finds one call, from `Mech_ReadDamageReadouts` (`0001583c`, [what the results read](mission-groups.md#what-the-results-read)).
- **`Component_ReadEntryDamage` (`00010b7c`)**, (header, index, kind): ES2's. `es2_xref.py` finds one call, from `Mech_ReadEntryDamage` (`00015868`, [the panel's readings](cockpit-panel.md)), which passes index *n* and kind 0, a dependent's reading.
- **`Mech_WeightedArmorRemaining` (`00010c38`)**, (header): ES2's, with the components' weights at `SalvageComponentWeights` (`000918e8`) and the internals' at `SalvageInternalWeights` (`00091910`), the 19 and 12 `int16` of ES2's tables at `00499fe0` and `0049a006`, value for value ([what a wreck is worth](../../retail/simulation/component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60), step 3, lists them). `es2_xref.py` finds one call, from [`Mech_SalvageValue`](mission-groups.md#what-the-results-read) (`00014034`).

**Any negative entry counts as destroyed here**, not only −1, which `Component_AddDamage`'s 16-bit sum can also leave ([the readings](component-damage.md#the-readings)). `Component_FillDamageReadouts` reads `0x100` for a negative component or internal, and in a mount pair counts a negative entry at its maximum; `Component_ReadEntryDamage` reads `0x100`; `Mech_WeightedArmorRemaining` adds nothing for one.

## The tenth mount pair

**The squad condition and the readouts walk ES2's ten mount pairs, components 19–28 with internals 12–21, so the tenth, component 28 and internal 21, lies one entry past each of ES1's arrays** ([the damage header](../formats/dmg-damage-file.md#the-damage-header)): the components' damage array holds 28 entries and the internals' 21.

- `Mech_ApplySquadCondition` reads the `.DMG` table's piece 28 and internals' maximum 21, and writes component 28's entry, or internal 21's when that mount's share passes its armor. Its internal loop reads maxima 0–8.
- `Component_FillDamageReadouts` reads component 28's entry, internal 21's and their piece and maximum for readout 41, and maxima 0–11 for readouts 20–31.
- `Component_ReadOverallDamage` ([the readings](component-damage.md#the-readings)) walks the header's internal count, 21, through maxima 0–20.

So on `SPIDER`, whose `.DMG` gives 12 internals ([the files](../formats/dmg-damage-file.md#the-files)), the squad condition reads maxima 12–21, the readouts maxima 12–21 and the overall reading maxima 12–20 past the end of its maxima array.

`Component_AllocDamageArrays` takes the two damage arrays from `operator_new`, 42 and 56 bytes. Through a mission's load the heap rounds a block with its 8-byte header up to a multiple of 8 and zero-fills it ([the heap](memory.md#the-heap)), so internal 21 falls in the 6 bytes the internals' 42 are rounded up by, and component 28 in the 2 bytes after the components' block, which end on a multiple of 8 ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Mech_ApplySquadCondition`, `Component_FillDamageReadouts` | `000107f0`, `000109bc` | `00415068`, `004151a4` |
| `Component_ReadEntryDamage`, `Mech_WeightedArmorRemaining` | `00010b7c`, `00010c38` | `004152ec`, `0041537c` |

## Globals

| | Address |
|---|---|
| `SalvageComponentWeights`, 19 `int16` | `000918e8` |
| `SalvageInternalWeights`, 12 `int16` | `00091910` |

## Open

- **Open:** what the tenth mount pair reads and overwrites: the 2 bytes after the components' 56-byte block are the start of whatever follows it in the heap, the next block's 8-byte header when one follows, and which block that is depends on where `Heap_Alloc` placed the arrays; the `.DMG` table's piece 28 and `SPIDER`'s maxima 12–21 lie past arrays `HercPiece_LoadTable` sizes from the file. How many pieces each `dmg\` file holds, and each mount pair's armor plus internal maximum, which `Component_FillDamageReadouts` divides by, are data questions for `HercWorks.Core`: no `HercWorks.Query` command reads `.DMG`.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in a career mission with a squad HERC the shell lists as damaged, `BP <CS>:000107f0` fires once per squad machine as the mission loads, and that HERC's damage screen shows the damage on its first frame.
