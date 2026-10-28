# DBSIM.EXE — squad orders and taking fire

**A squadmate told an order takes it or refuses it, says why on the radio when it is its turn to speak, and answers whether it took it; a HERC hit by the other side shares the contact, counts the damage when it is in the player's group, and turns on what it finds unless its state holds it.** These are ES2's ([the standing squad order](../../retail/simulation/ai-squadmates.md), [taking fire](../../retail/simulation/ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50)) on ES1's fields, with the differences below. What a player would see differently:

- a squadmate refuses to engage the player's selection from more than 130000 units (780 m) away;
- a HERC taking fire while it holds a place does not answer the shooter on the spot: the state choice runs again on the next tick and puts it back to its order.

Addresses are `ES1DBSIM` image addresses and argument lists are in stack order, as in [the HERC's AI](mech-ai.md); the fields are [the HERC's](mech.md#the-fields) and [the AI's](mech-ai.md#the-fields).

## Where the object file starts and ends

`Mech_ReceiveSquadOrder` has an object file of its own, `0001a12c`–`0001ab53`, between [`mechsys.cpp`'s](mechsys.md#where-the-object-file-starts-and-ends) and the file that opens at `0001ab54` with a run of `ConfigPart`'s copies ([shared modules](shared-modules.md)). It opens with `HeapPtr_DtorSquadOrdersCopy` (`0001a12c`), a lone copy of the 40-byte destructor ([module copies](../formats/compiler.md#vtables)), to which `es2_xref.py` finds no branch or stored pointer; then `Computer_PostMessage` (`0001a154`) and `Ai_PostSquadMessage` (`0001a188`) ([posting](cockpit-messages.md#posting)); and `Mech_ReceiveSquadOrder` (`0001a1cc`), whose `RET 0xc` at `0001ab51` is the file's last instruction. ES2's three are `00420a68`, `00420a98` and `00420ad4`, in the same order. `es1_boilerplate.py bounds` finds no static constructor and no assert source path in it.

`Mech_AiOnTakingFire` and `Mech_AiFriendlyFireComplaint` are in [the HERC AI's file](mech-ai.md#where-the-object-file-starts-and-ends).

## Receiving a squad order — `Mech_ReceiveSquadOrder` (`0001a1cc`)

`Mech_ReceiveSquadOrder` is the HERC's slot `+0x2c` ([the HERC's table](mech.md#the-hercs-table)), ES2's (`00420ad4`, [receiving one](../../retail/simulation/ai-squadmates.md#receiving-one--mech_receivesquadorder-00420ad4-mech-vtable-0x28)). It takes (machine, reply so far, order), the order record's verb at `+0`, its point at `+6` and its subject at `+0x12` ([the order record](../../retail/simulation/ai-squadmates.md#the-order-record--22-bytes)). It keeps a reply id, starting at `0xffff`, and a taken flag, starting at 0, as the low and high words of one dword at `[EBP-0x58]`, and switches on the verb, read as an unsigned `int16`, through the jump table `00091ebc` over 0–`0x11`. "Out of action" is `+0x9f` (disarmed), `+0x9e` (immobilized) or `+0x93` (destroyed) set; "committed" is the behaviour descriptor's (`+0x47`) byte `+9`; the player's selection is `CockpitViewInstance`'s `+0x20c` ([the target selection](cockpit-panel.md#the-target-selection)).

| Verb | Refused when (reply) | Otherwise, taken (reply) |
|---|---|---|
| 0 | `+0x94` is cleared first; then out of action (`0x1b`); the descriptor's byte `+0xc` set (`0xd`); no selection, or the selection immobilized or destroyed (`0x12`); the selection farther than 130000 from the machine, `Math_DistanceBetweenPoints` in 3D, a range of exactly 130000 passing (`0x10`); squad verb `+0x1d2` already 4 with `+0x1dc` the selection (`0xf`) | `+0x1d2` = 4, `+0x1dc` = the selection, `+0x1e4` = `Ai_TargetStateTier` (`0000d880`) of it, `Mech_AiEngageOrderedTarget`(machine, selection), `+0x217` = 0 (`0x11`) |
| 1 | out of action (`0x1e`); no selection (`0x12`); `+0x94` already set (`0xf`) | `+0x94` = 1 (`0x16`); then a committed machine whose target `+0x15e` is the selection zeroes its dwell count `+0x4c` when the descriptor's byte `+0xa` is set, and otherwise takes `Ai_SelectTarget` (`0000ddf4`)(machine, 1): `Mech_AiEngageOrderedTarget` on what it finds, `Mech_AiSelectBehaviour` on nothing |
| 2 | out of action (`0x1b`); no machine of `g_MechPool` (`000af68c`, walked with `Pool_Prev`) that is not out of action, whose target `+0x15e` is `PlayerMech` (`000bc42a`) and that is within 60000 of it (`0x1d`); the nearest such already the machine's target with the machine committed (`0x1a`) | as verb 0 against the nearest such machine (`0xb`) |
| 3, `0xa`, `0xf` | immobilized (`0x1b`) | `Behaviour_SetState` (`0000fdfc`)(`+0x47`, `patrolling` at `000af384`), `+0x1d2` = 0, `+0x217` = 0; `0x1e` when `Math_DistanceBetweenPoints`, in 3D, from the group's first member (array `+0xe` of the group `+0x3f`) to the machine is under 25000, else `0x14`, 25000 itself included (`JGE`) |
| 4, `0x10` | — | the radar `+0x90` = 1 (`0x26`), or `0xf` when it was already set; `+0xac` = 1 either way |
| 5 | `+0x94` is cleared first; then out of action (`0x1b`); not committed and `Ai_SelectTarget`(machine, 1) finding nothing (`0xe`) | committed: nothing more (`0x20`). Otherwise, with the target found: in a group whose word `+4` is 6, `guarding`, `+0x1d2` = 4 with `+0x1dc` = 0 and `Mech_AiSelectBehaviour`; in any other, `+0x1d2` = 0 and `Mech_AiEngageOrderedTarget` on it (`0x1c`) |
| 7, `0x11` | — | the radar `+0x90` = 0 and `+0xac` = 0 (`0x28`) |
| 8 | not committed and `Ai_SelectTarget`(machine, 0) finding nothing (`0xc`) | committed: `Mech_AiSelectBehaviour`, then `+0x1d2` = 0. Not committed, with a target found: nothing more, the target unused (`0xc`). `+0x217` = 0, taken or not |
| `0xb` | `+0x94` is cleared first; then out of action (`0x1b`); `+0x1d2` already 4 with `+0x1dc` the order's subject (`0xf`); the subject immobilized or destroyed, read with no null test (`0x1d`) | as verb 0 against the subject (`0x11`) |
| `0xc` | out of action (`0x1b`); `+0x1d2` already 6 and the order's point within 1000 of `+0x1d4` by `Math_DistanceBetweenPoints2D` (`0x20`) | `Behaviour_SetState`(`+0x47`, `guarding` at `000af474`), `+0x1d2` = 6 (`0x17`) |
| `0xd` | immobilized (`0x1b`); `+0x1d2` already 2 and the machine's `+0x20` within 2000 of the order's point by `Math_DistanceBetweenPoints`, in 3D (`0x20`) | `patrolling`, `+0x1d2` = 2 (`0x2a`) |
| `0xe` | as `0xd`, with `+0x1d2` already 1 | `patrolling`, `+0x1d2` = 1 (`0x2a`) |

**The point and the target.** Verbs `0xc`, `0xd` and `0xe` copy the order's dwords `+6` and `+0xa`, the point's X and Y, to `+0x1d4` and `+0x1d8`, taken or not; the point's Z, `+0xe`, is not kept, though `0xd` and `0xe` measure their 2000 in 3D with it. `+0x1dc` is the order's target: the selection (verb 0), the nearest machine (verb 2), the subject (`0xb`), or 0 (verb 5). The receiver never writes `+0x1e0`, which `Mech_AiGoalPosition` reads for squad verb 3 ([navigation](mech-ai.md#navigation)); nor does it store a verb 3 or 5 in `+0x1d2` ([Open](#open)).

Verbs 6, 9 and above `0x11` have no case: the reply stays `0xffff` and the order is not taken.

**The reply.** Every arm ends at `0001ab25`. When the reply so far is 0, or is 1 and this machine took the order, it posts `Ai_PostSquadMessage` (`0001a188`)(machine, 0, the dword at `[EBP-0x58]`), whose id argument `Ai_PostSquadMessage` reads as a word (`0001a1a5`), so the reply id; then, at the one exit `0001ab47`, `MOV AX,[EBP-0x56]` returns the taken flag in `AX`, the rest of `EAX` left as the last call or arm left it. [The broadcast](mission-groups.md#the-squad-order-broadcast) passes a reply so far of 0, then 1 after a refusal and 2 after a take, so the first machine told answers whatever it does, a later one only when it takes the order, and none after a take. `Squad_SendOrderToSlot` (`0002b3e8`, [the squadmates](cockpit-panel.md#the-squadmates)) passes 0.

ES2's differs in five arms. Verb 0 has no range test. Verb 1 first clears a standing engage order on the selection. Verb 5 sets ES2's FIRE AT WILL latch `+0xb6`, which ES1 has no counterpart of, is taken with `0x1c` even when nothing is found, and re-enters `Mech_AiSelectBehaviour` only when the group's current order is to guard and the machine is within 100000 of the group's target, clearing the verb where ES1's sets verb 4 with no target. Verb 8 clears `+0xb6` and is always taken. Verb `0xc` compares and stores the order's subject as well as its point; ES1's stores no subject. The latches are ES1's `+0x94` for ES2's `+0x9a` and `+0xac` for `+0xb2`, and `+0x217` is ES2's `+0x281`.

`es2_xref.py` finds the function's address once, as slot `+0x2c` of `MechVtable` (`00091b12`). A grep of the disassembly for calls through `+0x2c` finds two through an object's table, `Squad_BroadcastOrder`'s and `Squad_SendOrderToSlot`'s; the others are a weapon mount's fire slot, sound and animation objects' and a textured poly's. Both skip the cockpit's machine, the player's, so through them the order, and verb 4's write of 1 to the radar `+0x90` (`0001a617`), reaches only squadmates. The base tables' slot `+0x2c` is `SimObject_ReceiveSquadOrder_Refuse` (`0000d6d8`, [the base class's own functions](sim-object-base.md#the-base-classs-own-functions)), which answers 0.

## Taking fire — `Mech_AiOnTakingFire` (`00019338`)

Slot `+0x54`, (machine, damage, attacker), ES2's `+0x50` ([taking fire](../../retail/simulation/ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50)). [The shot sweep](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0) calls it (`0001f561`) with the shot record's dword `+4`, and the function reads it as an `int16` (`MOVSX` at `000193b7`): the shot's armor figure as the round carries it, before the difficulty scale, which the struck object's hit test applies to its own copies ([the difficulty scale](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c)). In order:

1. **The complaint.** When the attacker is `PlayerMech` (`000bc42a`) and the machine's group `+0x3f` is `PlayerMech`'s, `Mech_AiFriendlyFireComplaint`.
2. **The side.** It returns when the attacker's group's side byte `+0x14` is the machine's group's, both sign-extended. ES2's also returns for an attacker out of action.
3. `Mech_ShareContact` (`0000d760`)(machine, 0, attacker), and the under-fire window `+0x213` = 30000.
4. **The damage taken from the other side.** For a machine in `PlayerMech`'s group it adds the damage to the dword `+0x217` and returns while the sum is under 8000. Step 2 lets only an attacker of the other side reach this, so `+0x217` is the damage a machine in the player's group has taken from the enemy; squad verbs 0, 2, 3, 8 and `0xb` zero it ([receiving](#receiving-a-squad-order--mech_receivesquadorder-0001a1cc)).
5. It returns for the player's machine (`+0x9d`), while the retarget countdown `+0x209` (handle `+0x208`) is not 0, and when the attacker is the machine's own target `+0x15e` and the descriptor's (`+0x47`) byte `+9`, committed, is set.
6. **The countdown** `+0x209` is set to 10000 (`0001943a`), before any test below. With *l* the group's first member (array `+0xe`), the tests on the descriptor's bytes:
   - **Byte `+9` clear and *l*'s `+0x9d` set**, an uncommitted machine in a group the player leads: `Ai_PostSquadMessage`(machine, 0, 3) for an attacker of class 0 (`+0x162`), a HERC, and `+0x94` cleared when *l*'s target `+0x15e` is the attacker, as ES2's. The tests go on.
   - **Byte `+0xa` set**, holding a place: with *l*'s `+0x9d` set, squad verb `+0x1d2` = 4 and its target `+0x1dc` = 0; in any group the dwell countdown, the dword `+0x4c`, = 0, so the reassess runs on the next tick. It returns. ES2's picks the defender with `Ai_SelectDefenceTarget` and installs `driving off en`.
   - **Byte `+0xb` set**: it returns.
   - **Otherwise** `Ai_SelectTarget` (`0000ddf4`)(machine, 0), mask 0 where ES2's passes `0x24`, with no squad-order substitution. A target other than the one held is installed as the thinks install one: the old target's holder count `+0x15c` down 1, the new one's up 1, and `+0x97` set. With a target, held or new, it sets `+0xa6` and runs `Mech_AiEnterCombat` (`000174cc`); with none, when it held one before the call, it runs `Mech_AiSelectBehaviour` (`0001892c`).

When the descriptor is the machine's own behaviour block, as after `Mech_AiSelectBehaviour`'s no-case path, bytes `+9`, `+0xa` and `+0xb` are the tick count's low three bytes ([what `EDI` holds](mech-ai.md#choosing-a-state--mech_aiselectbehaviour-0001892c)).

**`Mech_AiFriendlyFireComplaint` (`00019300`)**, (machine): ES2's: with the countdown `+0x20e` at 0, sets it to 40000 and posts `Ai_PostSquadMessage` (`0001a188`)(machine, 0, 8). `es2_xref.py` finds two calls, this function's and [the shot sweep's](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorSquadOrdersCopy` | `0001a12c` | the lone copy |
| `Computer_PostMessage`, `Ai_PostSquadMessage`, `Mech_ReceiveSquadOrder` | `0001a154`, `0001a188`, `0001a1cc` | `00420a68`, `00420a98`, `00420ad4` |
| `Mech_AiFriendlyFireComplaint`, `Mech_AiOnTakingFire`, in [the HERC AI's file](mech-ai.md#functions) | `00019300`, `00019338` | `0041f790`, `0041f7b8` |

## Open

- **Open:** what writes `+0x1e0`, and what stores squad verb 3 or 5, which `Mech_AiGoalPosition` turns into the position of `+0x1e0` and of `+0x1dc`. `es2_fieldscan.py 1e0` finds one direct access, that read (`000179cb`); its other hits are string scans, block copies in the cockpit view's, the gauges' and the viewports' code, the message port's own `+0x1e0`, and a 16384-dword fill in the unread `0008b408`. The same scan of `+0x1dc`, the control, finds its 17 writes, the receiver's, `driving off en`'s and this file's among them. `es2_fieldscan.py --writes-only 1d2` finds stores of 0, 1, 2, 4 and 6, one of `AX` in the unread `Flyer_MovementTick` (`0001b084`), and none of 3 or 5. A retail check: `BP <CS>:000179a0` while squadmates follow orders, and read the word `+0x1d2` of the machine passed.
- **Open:** a positive control in the running game. Proposed: order a squadmate to engage a selection more than 130000 (780 m) away; it answers `0x10`'s line and keeps its state, where within 130000 it turns on the selection.
