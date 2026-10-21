# DBSIM.EXE — the sensor model

**ES1 decides which enemies each side knows about as ES2 does ([the sensor model](../../retail/simulation/target-selection.md#the-sensor-model--sim_detectiontick-004123ac)): once a tick every human-side object looks around for Cybrid objects, an active radar shows up the objects it reaches, and what one machine sees is passed to the rest of its side.** What a player would see differs in the distances and in what blocks the view. A radar reaches 100000 units where ES2's reaches 200000, and when either of two objects has its radar on, both show up. A machine looks only as far as the contact scan range, 30000 or 60000, where ES2's looks to 80000. A sighting is passed to the side within 120000 and dropped beyond 120000, where ES2's uses 100000 for both. Only the zone's placed shapes block the line of sight, since ES1's ground is flat ([the zone's shapes](terrain.md#a-line-between-two-points--sim_raycastshapes-00020b70)). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The objects' fields stand 6 bytes below ES2's up to `+0xad` ([the object fields](mission-groups.md#the-object-fields)), and a slot 4 bytes above.

## Where the object file starts and ends

The code is `0000e218`–`0000eb67`. It opens with `HeapPtr_DtorDetectionCopy` (`0000e218`), a lone copy of the 40-byte destructor that opens each run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)), after the zero fill that ends [the object base's file](sim-object-base.md#where-the-object-file-starts-and-ends); `es2_xref.py` finds no reference to it ([Open](#open)). `Detection_Sweep`'s `RET 4` at `0000eb63` is followed by two zero bytes and, at `0000eb68`, a run of `ConfigPart`'s copies, which opens [the mission status's file](mission-status.md#where-the-object-file-starts-and-ends) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer, and no assert in it names a source path. Its functions are ES2's from `004123ac` to `004128f8`, in the same order.

## The fields

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x20` | `+0x26` | the position |
| `+0x3f` | `+0x45` | the group: its side byte `+0x14` (ES2's `+0x12`) and its action `+0x16` (where ES2's has the deployment gate `+0x14`; [the group record](mission-groups.md#the-group-record)) |
| `+0x45` | `+0x4b` | the slot in `LiveObjectList` ([the list](sim-object-base.md#the-live-object-list)) |
| `+0x8f` | `+0x95` | radar-visible |
| `+0x90` | `+0x96` | the radar: the object emits while it is set |
| `+0x93` | `+0x99` | destroyed |
| `+0x98` | `+0x9e` | engaged |
| `+0x9c` | `+0xa2` | the per-tick gate on the engagement action |
| `+0x9d` | `+0xa3` | the player's machine |
| `+0x9e` | `+0xa4` | immobilised |
| `+0xbc` | `+0xc2` | the contacts, a byte per slot, 80 rows |
| `+0x10c` | `+0x132` | the line-of-sight cache, a byte per slot, 80 rows |
| `+0x16c` | `+0x1b2` | the engagement action |
| `+0x178`, `+0x17b` | `+0x1e2`, `+0x1e5` | the line-of-sight and decay countdowns, their counters a byte later |

## The tick — `Sim_DetectionTick` (`0000e240`)

No arguments; ES2's (`004123ac`, [passes](../../retail/simulation/target-selection.md#passes)) in its three passes over `LiveObjectList`:

1. For each object whose group's action `+0x16` is clear, it ticks the countdowns `+0x178` and `+0x17b` with `Math_CountdownTimerTick`; when the second expires, it reloads its counter `+0x17c` with 10000 + `Math_RandomBelow`(`000b5bd4`, 1000) and runs `Detection_DecayContacts` on the object.
2. For each object whose group's action is clear, neither immobilised nor destroyed, on side 0, it runs `Detection_Sweep`, except an object with `+0x9d` set, the player's machine, which it holds back; after the walk it sweeps the last object it held back, so with more than one such object only the last that passed the tests is swept.
3. It clears every object's `+0x9c`.

`es2_xref.py` finds one call, from `Sim_MainTick` (`00059700`, [the tick](main-loop.md#the-tick--sim_maintick-00059700)), unless the simulation is frozen.

## Looking around — `Detection_Sweep` (`0000e85c`)

(machine), over every object of `LiveObjectList` that is neither immobilised nor destroyed, whose group's action is clear and whose side byte is 1:

```
d = Math_DistanceBetweenPoints(other+0x20, machine+0x20)
if (!(machine+0x8f && other+0x8f) && (machine+0x90 || other+0x90) && d < 100000
        && Detection_LineOfSight(machine, other)):
    machine.slot+0x4c(machine, other)              // the enemy-sighted callout
    machine+0x8f = 1; other+0x8f = 1
if d < Detection_ContactScanRange():               // 30000 or 60000
    if (!machine.contacts[other] || !other.contacts[machine]):
        h = Math_HeadingToward(to other+0x20, from machine+0x20)
        if machine.slot+0x48(machine, machine.slot+0x40(machine) + h)
                && Detection_LineOfSight(machine, other):
            machine.slot+0x4c(machine, other)
            if (!machine+0x9d) Detection_ShareContact(machine, other)
            else machine.contacts[other] = 1
        if other.slot+0x48(other, other.slot+0x40(other) + h - 0x8000)
                && Detection_LineOfSight(machine, other):
            Detection_ShareContact(other, machine)
    else if d < 50000:
        other+0x98 = 1;   Action_Activate(machine+0x16c) unless it is null or machine+0x9c
        machine+0x98 = 1; Action_Activate(other+0x16c) unless it is null or other+0x9c
```

Slot `+0x40` is the turret twist and `+0x48` [the sensor arc](sim-object-base.md#the-sensor-arc), ES2's `+0x3c` and `+0x44`. The arc's argument is added in 16 bits, so twist + *h* and twist + *h* − `0x8000` wrap as angles do. ES2's (`004128f8`, [ranges](../../retail/simulation/target-selection.md#detection_sweep-004128f8-ranges)) paints within 200000 and lights each object on its own terms: the other when it emits or lies within 140000, after the callout, and the machine when it emits or the other lies within 140000. It looks within 80000, a fixed range, where ES1's looks within `Detection_ContactScanRange` (`0001f814`), the range the player's own target selection allows a contact ([`Detection_ContactScanRange`](world.md#small-functions)).

## Line of sight — `Detection_LineOfSight` (`0000e55c`)

(machine, other): when the counter of the *other's* countdown, `+0x179`, is 0, it reloads the *machine's* counter `+0x179` with 5000 + `Math_RandomBelow`(`000b5bd4`, 1000) and stores in the machine's cache byte `+0x10c` + the other's slot whether `Sim_RaycastShapes`(0, other `+0x20`, machine `+0x20`, result) answers 0, a line from the machine's position to the other's that strikes no zone shape. It returns the cached byte, sign-extended. While the other's counter is not 0 it returns the machine's byte as it stands, so a test is answered by whatever the last test with the same machine and slot stored, or, before any, by what the byte held from the machine's construction ([Open](#open)). `Detection_Sweep`'s three tests pass the sweeping machine, a human-side object, as the machine, and `Detection_DecayContacts` passes the decaying object, so a Cybrid's decay reads its own row of the cache, which no sweep's test writes, behind the gate of the human's counter, which the sweeps' tests reload. ES2's ([line of sight](../../retail/simulation/target-selection.md#line-of-sight--detection_lineofsight-00412608)) has the same asymmetric gate but walks the terrain between the two aim nodes, each raised by its aim node's height or 500; ES1's runs between the two objects' positions with no height added.

## Passing a contact on — `Detection_ShareContact` (`0000e618`)

(spotter, contact): ES2's (`00412704`, [passing a contact on](../../retail/simulation/ai-targeting.md#passing-a-contact-on)) with a reach of 120000 where ES2's has 100000. When the two are on different sides, for each object whose group's action is clear and whose side is the spotter's, the spotter included: within 120000 of the contact (`Math_DistanceBetweenPoints`), it sets that object's contact byte for the contact; within 50000, it also sets the contact's `+0x98` and runs `Action_Activate` on that object's `+0x16c` unless it is null or the object's `+0x9c` is set. `Mech_ShareContact` (`0000d760`), the base class's slot `+0x54`, calls it for a hit object with the shooter ([the base class](sim-object-base.md#the-base-classs-own-functions)).

## Decay — `Detection_DecayContacts` (`0000e41c`)

(object): clears its `+0x8f`, then, for each object whose group's action is clear, on the other side, that this object holds as a contact: unless `Math_DistanceBetweenPoints2D`(that object `+0x20`, this one's `+0x20`) is below 120001 and `Detection_LineOfSight`(this, that) answers 1, it clears both objects' contact bytes for each other. ES2's (`0041251c`) drops beyond 100000 on the ground plane.

## The enemy-sighted callout — `Mech_AiEnemySighted` (`0000e760`)

(machine, object), slot `+0x4c` of `00091ae6`, a table `Mech_Constructor` stores (the dword at `00091b32`), is ES2's (`00412800`, [radio callouts](../../retail/simulation/ai-targeting.md#radio-callouts)) on ES1's fields. It sets the byte `000af264` to 1 when it is 0. For the player's machine it sets the object's latch, `EnemySighted_CalledInLatch` (`000af200`) + the object's slot. For any other machine it ticks `EnemySighted_CalloutRateLimit` (`000af268`); then, when the object's class `int16` `+0x162` is not 1, the machine's group is `PlayerMech`'s (`000bc42a`), the latch is clear, the object is not radar-visible, and neither the machine nor the player holds it as a contact, it sets the latch and, when the limit's counter `000af269` is 0, posts message 1 with `Ai_PostSquadMessage`(machine, 0, 1) and sets the counter to 10000. The base class's slot is **`SimObject_AiEnemySightedNoOp` (`0000e75c`)**, `RET 8`, ES2's `004127f8`.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorDetectionCopy` | `0000e218` | the code of `HeapPtr_DtorGroupsCopy` (`0001c824`) |
| `Sim_DetectionTick`, `Detection_DecayContacts`, `Detection_LineOfSight`, `Detection_ShareContact` | `0000e240`, `0000e41c`, `0000e55c`, `0000e618` | `004123ac`, `0041251c`, `00412608`, `00412704` |
| `SimObject_AiEnemySightedNoOp`, `Mech_AiEnemySighted`, `Detection_Sweep` | `0000e75c`, `0000e760`, `0000e85c` | `004127f8`, `00412800`, `004128f8` |

## Globals

| | Address |
|---|---|
| `EnemySighted_CalledInLatch`, a byte per slot | `000af200` |
| `EnemySighted_CalloutRateLimit`, a countdown, its counter at `000af269` | `000af268` |
| `PlayerMech` | `000bc42a` |

## Open

- **Open:** what calls `HeapPtr_DtorDetectionCopy`, to which `es2_xref.py` finds no branch or stored pointer.
- **Open:** what reads `000af264`, which `Mech_AiEnemySighted` sets as ES2's sets `00499358`.
- **Open:** what else ticks or reloads the two countdowns `+0x178` and `+0x17b`, and what sets them and the line-of-sight cache `+0x10c` at a machine's construction.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in a mission, `BP <CS>:0000e240` fires once per simulation tick, and `BP <CS>:0000e760` fires as a squadmate first sees an enemy, just before its enemy-sighted message comes up.
