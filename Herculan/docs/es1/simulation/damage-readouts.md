# DBSIM.EXE — the squad's condition and the damage readouts

**A squad HERC in ES1 starts a mission worn as the career left it, and its damage is read out for the screens, the results and the salvage, exactly as in ES2: the four functions are ES2's on ES1's damage header ([a squad machine's condition](../../retail/simulation/component-damage.md#a-squad-machines-condition--mech_applysquadcondition-00415068), [the damage readouts](../../retail/simulation/component-damage.md#the-damage-readouts--component_filldamagereadouts-004151a4), [what a wreck is worth](../../retail/simulation/component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60)).** The header is the machine's `+0x19c`, ES2's `+0x206`, with ES2's layout: the internals' damage array at `+0`, the components' at `+4`, their occupancy flags at `+8` and the `.DMG` table at `+0xc`. The salvage weights are ES2's values. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)): the header first and the other arguments in the reverse of ES2's order, the shape a C++ member function's arguments take there.

## Where the object file starts and ends

The code is `000107f0`–`00010d5f`. It opens with `Mech_ApplySquadCondition`, after the two zero bytes that end [the aircraft state table's file](shields-and-behaviour-states.md#where-the-object-files-start-and-end), with no copy of `ConfigPart`'s destructors before it. `Mech_WeightedArmorRemaining`'s `RET 4` at `00010d5c` is followed by one zero byte and the run of `ConfigPart`'s copies at `00010d60` that opens the next file ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer, and no assert in it names a source path. Its functions are ES2's from `00415068` to `0041537c`, in the same order.

## The functions

- **`Mech_ApplySquadCondition` (`000107f0`)**, (header, hardpoint conditions, internal conditions, external conditions): ES2's. `es2_xref.py` finds one call, from [`DBSim_SpawnMissionObjects`](../formats/script-dat.md#the-second-pass), which passes a `data\player.mec` entry's `+0x6a`, `+0x56` and `+0x3c`.
- **`Component_FillDamageReadouts` (`000109bc`)**, (header, buffer): ES2's. `es2_xref.py` finds one call, from `Mech_ReadDamageReadouts` (`0001583c`, [what the results read](mission-groups.md#what-the-results-read)).
- **`Component_ReadEntryDamage` (`00010b7c`)**, (header, index, kind): ES2's. `es2_xref.py` finds one call, from `Mech_ReadEntryDamage` (`00015868`, [the panel's readings](cockpit-panel.md)), which passes index *n* and kind 0, a dependent's reading.
- **`Mech_WeightedArmorRemaining` (`00010c38`)**, (header): ES2's, with the components' weights at `SalvageComponentWeights` (`000918e8`) and the internals' at `SalvageInternalWeights` (`00091910`). `es2_xref.py` finds one call, from [`Mech_SalvageValue`](mission-groups.md#what-the-results-read) (`00014034`).

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

- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in a career mission with a squad HERC the shell lists as damaged, `BP <CS>:000107f0` fires once per squad machine as the mission loads, and that HERC's damage screen shows the damage on its first frame.
