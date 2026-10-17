# DBSIM.EXE — shields and the behaviour state tables

**ES1's shields are ES2's one pool split front and rear by a balance the player moves ([the shield system](../../retail/simulation/damage-system.md#the-shield-system)), but they recharge 4 units a tick where ES2's recharge 5; the cockpit's rings measure the charge against the current capacity, as ES1 keeps no separate base capacity; and the front charge always slews toward the balance at its slow rate.** The AI's behaviour states are built at startup into two tables as ES2's are ([three parallel tables](../../retail/simulation/ai-dispatch.md#three-parallel-tables)): twenty for a HERC, ES2's twenty-two less `player fly` and `following`, with a `fleeing` that holds 30000 counts where ES2's holds 15000, and seven for an aircraft, ES2's seven with the same dwells and flags. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object files start and end

Three object files, each ending in zero fill after its last return ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

| Code | Opens with | Holds | Ends |
|---|---|---|---|
| `0000fa20`–`0000febb` | a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)): `HeapPtr_DtorShieldsCopy` (`0000fa20`), `ConfigPart_DtorShieldsCopy` (`0000fa48`, slot `+0x1c` of `00091766`), `ConfigPart_GetClassTagShieldsCopy` (`0000fa87`) and `TSPartBase_DtorShieldsCopy` (`0000fa90`, slot `+0x1c` of `0009179a`) | the shields, then `Behaviour_SetState` and `Behaviour_CountTick`: ES2's `Shield_Init` (`00413a90`) to `Behaviour_CountTick` (`00413eb0`) less `Shield_SetMax` and `Shield_RefillToBalance` | `RET 4` at `0000feb6`, three zero bytes |
| `0000febc`–`000105af` | `HeapPtr_DtorStateTableCopy` (`0000febc`), a lone copy of the run's first destructor | `Behaviour_BuildStateTable` (`0000fee4`), the static initializer whose pointer is `000adbf4`, and `Behaviour_ExpandFlagBits` (`00010578`) | `RET 8` at `000105ab`, two zero bytes |
| `000105b0`–`000107ef` | `Flyer_BuildStateTable` (`000105b0`), the static initializer whose pointer is `000adbf8` | it and `Behaviour_ExpandFlagBitsFlyerStatesCopy` (`000107b8`), the code of `Behaviour_ExpandFlagBits` | `RET 8` at `000107eb`, two zero bytes |

The first file follows [the mission status's](mission-status.md#where-the-object-file-starts-and-ends), and [the squad condition's file](damage-readouts.md#where-the-object-file-starts-and-ends) follows the third. No assert in the three names a source path. ES2 builds its two tables in one file, `Flyer_BuildStateTable` (`00414c68`) right after `Behaviour_BuildStateTable` (`00413ed4`). `es2_xref.py` finds no branch or stored pointer to `HeapPtr_DtorShieldsCopy` or `HeapPtr_DtorStateTableCopy` ([Open](#open)).

## The shields

The machine's shield block is four `int16` at `+0x1b8`: front charge `+0`, rear charge `+2`, balance `+4` (Q10 over 0–`0x400`, `0x200` even) and capacity `+6`, which caps front + rear. ES2's block, at `+0x222`, has a fifth word, the base capacity.

- **`Shield_Init` (`0000fabc`)**, (block, value): capacity = value, balance `0x200`, front and rear value >> 1. `Mech_Constructor` (`00011330`) passes the type record's `+0xc0`, the capacity field of ES2's record ([the shield system](../../retail/simulation/damage-system.md#the-shield-system)). ES2's `Shield_Init` also stores the base capacity.
- **`Shield_BalanceAdjust` (`0000fae4`)**, (block, direction): ES2's (`00413af8`): balance ± `0x66`, + for a non-zero direction, clamped to [0, `0x400`]. `es2_xref.py` finds three calls: `Shield_BalanceInputRead` (twice) and `00011050`.
- **`Shield_RechargeTick` (`0000fb2c`)**, (block, request): ES2's ([recharge tick](../../retail/simulation/damage-system.md#recharge-tick--shield_rechargetick-00413b38)) granting at most 4 where ES2's grants 5, and moving the front toward Q10(front + rear + granted, balance) by `Math_RateLimitedMoveToward` at `0x41` whatever the deficit, where ES2's snaps with a step of 10000 when the capacity has fallen below the charge held. The rear takes the rest; it returns the request less what was granted. `es2_xref.py` finds one call, from `00014ee0`.
- **`Shield_BalanceInputRead` (`0000fbb4`)**, (block): ES2's ([balance-adjustment input](../../retail/simulation/damage-system.md#balance-adjustment-input--players-own-mech-only)) on the shield gauge at `CockpitViewInstance` `+0x1e5`, where ES2's is at `+0x1e9`: it copies the gauge's 15-byte state block, calls `Shield_BalanceAdjust` with 1 when its byte `0xd` is set or with 0 when byte `0xe` is, clearing that byte, and writes back (front << 10) / capacity, (rear << 10) / capacity and the balance. ES2's divides by the base capacity. `es2_xref.py` finds one call, from `000155a8`.
- **`ShieldsGauge_GetStateBlock` (`00039334`)**, (gauge), returns the gauge's `+0x7e`; **`ShieldsGauge_SetStateBlock` (`000392d0`)**, (gauge, block), copies the 15 bytes back over `+0x7e` and sets `+0x75` to 2 when the three dwords changed. ES2's (`004438e0`, `00443858`) keep the block at `+0xb5`, and until the gauge's power-up is done (`+0x8d`, [the power-up](../../retail/simulation/cockpit-hud-widgets.md#power-up-sequence)) the setter moves the two readings to `+0xcc` and `+0xd0` and zeroes them; ES1's has no such step.
- **`Mech_ShieldAbsorb_DirectFire` (`0000fca8`)**, (pair, damage, length, frame, machine): ES2's (`00413cc4`, [direct-fire damage](../../retail/simulation/damage-system.md#direct-fire-damage-armor-then-part-deterministic-shield-gated)) on the type record at `+0x188`: the hit cylinder's center height `+0x18` and radius `+0x1a`, the facing chosen by where the muzzle sits in the machine's frame, min(damage, charge) taken off both, and the entry distance returned, 0 for a miss. `es2_xref.py` finds one call, from the mech's hit test `00013b44`, with the block at `+0x1b8`.
- **`Mech_ShieldAbsorb_Explosive` (`0000fc6c`)**, (pair, damage, front): takes `Math_Q10Multiply`(1000, damage) off the front, or the rear when *front* is 0, and clamps it at 0. It returns nothing: ES2's (`00413c68`) returns the overflow scaled back by `0x400` / 1000. `es2_xref.py` finds no branch or stored pointer to it ([Open](#open)).

## The behaviour block

A machine's behaviour block is at `+0x47`, ES2's `+0x4d` ([the mech's behaviour block](../../retail/simulation/ai-dispatch.md#the-mechs-behaviour-block--mech0x4d)), with the same layout.

- **`Behaviour_SetState` (`0000fdfc`)**, (block, descriptor): ES2's (`00413e50`). The jitter word is `Behaviour_DwellJitter` (`000bc426`). `es2_xref.py` finds 25 calls, where ES2's has 30.
- **`Behaviour_CountTick` (`0000fe84`)**, (block): ES2's (`00413eb0`): when `+0x36` is set it clears it; otherwise it sets `+0x35` and adds 1 to the `int32` `+9`. `es2_xref.py` finds one call, the last of [`Mech_AiTick`](mission-groups.md#the-tick).

## The HERC's state table

`Behaviour_BuildStateTable` (`0000fee4`) fills the twenty `0x28`-byte descriptors of `BehaviourStateTable` (`000af26c`): name, dwell, the 16 flag bytes `Behaviour_ExpandFlagBits` (`00010578`) unpacks from a 16-bit mask, the think, move and reassess functions at `+0x18`, `+0x1c` and `+0x20`, each one function pointer where ES2's is a three-dword member-function triple, and a closing dword at `+0x24`. The states, in order: `deciding`, `player`, `attacking`, `flanking`, `facing off`, `attacking base`, `attacking flyer`, `patrolling`, `travelling`, `bulldog travel`, `search destroy`, `sleeping`, `skirting`, `guarding`, `driving off en`, `ramming`, `fleeing`, `in limbo`, `dead`, `disabled`. The dwell is 50000 for the five combat states and `driving off en`, 30000 for `fleeing`, 0 for `deciding`, `dead` and `disabled`, and 10 for the rest. ES2's table ([the 22 states](../../retail/simulation/ai-dispatch.md#the-22-states)) also has `player fly` and `following`, and holds `fleeing` for 15000.

## The aircraft's state table

`Flyer_BuildStateTable` (`000105b0`) fills seven `0x24`-byte descriptors at `FlyerBehaviourStateTable` (`000af58c`) the same way, with no closing dword, unpacking each mask with the file's own copy of the flag expander. Names, dwells and masks are ES2's ([the seven states](../../retail/simulation/ai-flyers.md#the-seven-states)):

| # | Name | Think | Move | Reassess | Dwell | Mask |
|---|---|---|---|---|---|---|
| 0 | `deciding` | — | — | `0001c46c` | 0 | 0 |
| 1 | `attacking` | `0001c3f8` | `0001b084` | `0001c46c` | 50000 | 1 |
| 2 | `patrolling` | `0001c21c` | `0001b084` | `0001c46c` | 5000 | 0 |
| 3 | `search and destroy` | `0001c14c` | `0001b084` | `0001c46c` | 5000 | 0 |
| 4 | `sleeping` | — | — | — | 500 | 1 |
| 5 | `scouting` | `0001c2d8` | `0001b084` | `0001c46c` | 5000 | 0 |
| 6 | `dead` | — | — | — | 500 | 1 |

The names are the strings at `000918a0`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorShieldsCopy`, `ConfigPart_DtorShieldsCopy`, `ConfigPart_GetClassTagShieldsCopy`, `TSPartBase_DtorShieldsCopy` | `0000fa20`, `0000fa48`, `0000fa87`, `0000fa90` | |
| `Shield_Init`, `Shield_BalanceAdjust`, `Shield_RechargeTick`, `Shield_BalanceInputRead` | `0000fabc`, `0000fae4`, `0000fb2c`, `0000fbb4` | `00413a90`, `00413af8`, `00413b38`, `00413bc8` |
| `Mech_ShieldAbsorb_Explosive`, `Mech_ShieldAbsorb_DirectFire`, `Behaviour_SetState`, `Behaviour_CountTick` | `0000fc6c`, `0000fca8`, `0000fdfc`, `0000fe84` | `00413c68`, `00413cc4`, `00413e50`, `00413eb0` |
| `HeapPtr_DtorStateTableCopy`, `Behaviour_BuildStateTable`, `Behaviour_ExpandFlagBits` | `0000febc`, `0000fee4`, `00010578` | —, `00413ed4`, `00415028` |
| `Flyer_BuildStateTable`, `Behaviour_ExpandFlagBitsFlyerStatesCopy` | `000105b0`, `000107b8` | `00414c68`, — |

Callees of another file read for them: `ShieldsGauge_GetStateBlock` (`00039334`) and `ShieldsGauge_SetStateBlock` (`000392d0`), ES2's `004438e0` and `00443858`.

## Globals

| | Address |
|---|---|
| `BehaviourStateTable`, 20 descriptors | `000af26c` |
| `FlyerBehaviourStateTable`, 7 descriptors | `000af58c` |
| `Behaviour_DwellJitter`, `int16` | `000bc426` |

## Open

- **Open:** what reaches `Mech_ShieldAbsorb_Explosive`, to which `es2_xref.py` finds no branch or stored pointer, and so whether a blast is taken by ES1's shields at all; ES2's blast path calls its counterpart. A retail check: stand a HERC with full shields beside a destroyed enemy's blast and compare the damage readout with one taken with shields down.
- **Open:** what calls `HeapPtr_DtorShieldsCopy` and `HeapPtr_DtorStateTableCopy`, to which `es2_xref.py` finds no branch or stored pointer.
- **Open:** what sets the shield capacity after `Shield_Init`: ES1 has no `Shield_SetMax` or `Shield_RefillToBalance` in this file, where ES2's `Mech_ComputeShieldCapacity` resizes the array as the generator is shot.
- **Open:** the callers `es2_xref.py` finds and this batch did not read: `00011050` (the balance), `00014ee0` (the recharge), `000155a8` (the gauge read), and the thinks, moves and reassesses of the aircraft's table.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in a mission, `BP <CS>:0000fbb4` fires again and again during a mission, and each press of the shield balance control changes the word at the player's machine `+0x1bc`, the balance, by `0x66`.
