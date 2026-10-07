# AI goals — the mission-group order layer

What tells a machine *what it is for*. Every AI machine belongs to a mission group, and the group carries an ordered list of orders it works through; the order in force is what [`ai-dispatch.md`](ai-dispatch.md#choosing-a-state--mech_aiselectbehaviour-0041eb34) turns into a behaviour state, and it is also what the target filter, the combat reassess and the abandonment test read when they ask what this machine was sent to do — see [`ai-targeting.md`](ai-targeting.md).

This layer is structurally **upstream of the rest of the AI**: `Mech_AiTick`'s only caller is `Group_OrderTick`, so a machine that is not a live member of a group never thinks at all — see [`ai-dispatch.md`](ai-dispatch.md#the-ai-tick--mech_aitick-00411cec).

The file half of the subject — how block 10 and block 11 are written and read — belongs to [`script-dat.md`](../formats/script-dat.md).

## The order record

The 22-byte record, its offsets and the block-10 fields each is built from are [`script-dat.md`](../formats/script-dat.md#block-10-in-memory--22-bytes-0x16)'s. What the simulation makes of each field:

| Offset | Meaning to the simulation |
|---|---|
| `+0x00` | **The verb.** 0-6; the state and completion tables below |
| `+0x02` | A formation index. No DBSIM reader found — [Open](#open) |
| `+0x04` | A block-1 point. No DBSIM reader found — [Open](#open) |
| `+0x08` | **The route** — a waypoint group. Read by `DBSim_BuildGroupRecord` and `DBSim_SpawnMissionObjects`, from slot 0 only, and by `Mission_GroupOrderCompleteOnRoute` (`0041324c`), which matches it across all ten slots against an objective's waypoint group ([`mission-objectives.md`](mission-objectives.md#the-record)) |
| `+0x0c` | **What kind of thing the order names** |
| `+0x0e` | **The subject** — a group record for kind 0, an object for 1-3 |
| `+0x12` | **A mission action.** When it fires, the group moves on |

`+0x02` and `+0x04` are authored: `+0x04` is a real block-1 point in 47 of the 637 retail order records, and `+0x02` holds 0, 1 or 3. The briefing map reads both off the player's squad's first order, `+0x02` as the formation it spreads the squad in and `+0x04` as a fallback anchor ([`../shell/mission-map.md`](../shell/mission-map.md#the-squads-positions)). A group in the simulation spreads by the formation id in its own block-11 record instead ([`script-dat.md`](../formats/script-dat.md#row-mapping)). The orders live in one pool (`004a9eb4`), which `DBSim_SpawnMissionObjects` fills and indexes into each group's order array; [What else reads an order](#what-else-reads-an-order) lists what the loads of a slot go on to read.

The verb's range is the first confirmation the field is what it looks like: in all 637 order records of the 62 retail `.MSN` files, the verb holds 0-6, which is exactly the span of the switch in `Mech_AiSelectBehaviour`.

## The group record's order fields

A mission group's own record is `0x7a` bytes, built by `DBSim_BuildGroupRecord` (`00423b34`). The fields this layer owns:

| Offset | Type | Meaning |
|---|---|---|
| `+0x04` | short | **Route cursor** — the index of the waypoint last reached |
| `+0x06` | ptr | **Route** — the waypoint group the cursor runs over |
| `+0x0c` / `+0x10` | ptr / short | Member array and count |
| `+0x12` | byte | Side, 0 human and 1 Cybrid — [`script-dat.md`](../formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps) |
| `+0x14` | ptr | Deployment action; non-null means the group is not in the mission yet — [`mission-deployment.md`](mission-deployment.md#the-deployment-gate--group0x14) |
| `+0x1c` / `+0x30` | short[10] x2 | The group's own ten mission-counter slots, refs and operations — [`mission-deployment.md`](mission-deployment.md#the-out-of-action-report) |
| `+0x44` | ptr[10] | **The order array**, a null in every slot the block-11 record left unset |
| `+0x6c` | int | **The current order's index** |
| `+0x70` | byte[10] | One flag per order, set when that order reaches completion. Read by the mission-objective layer's `Mission_GroupOrderCompleteOnRoute` — [`mission-objectives.md`](mission-objectives.md); no AI reader found — [Open](#open) |

`Sim_MainTick` runs `Group_OrderTick` (`00423a74`) only for a group whose `+0x14` is clear, so **a group waiting to arrive runs no orders and no AI** — [`mission-deployment.md`](mission-deployment.md#the-deployment-gate--group0x14).

### The route cursor is loaded once

When **order slot 0** is set, `DBSim_BuildGroupRecord` sets `+0x06` from its `+0x08` and zeroes `+0x04`. The group array's element constructor (`004240e4`) also zeroes `+0x04`, at allocation, and `Route_AdvanceCursor` (`0042313c`) steps it; no writer that re-points `+0x06` has been found ([Open](#open)). So a group has **one route for the whole mission**, whatever its later orders name, and an advance to the next order leaves the cursor where it was — see the rejected reading below. Retail play agrees: in `C1_06` the player's own group carries a patrol on route 40 and then a patrol on route 29, and once route 40's last waypoint is reached the waypoint indicator shows nothing further ([`player-waypoints.md`](player-waypoints.md)).

`Route_WaypointAt(cursor, index)` (`00423b0c`) reads a waypoint out of it: null when there is no route or the index is past the end, otherwise the block-1 point at that index. It has 16 call sites, `Group_IsOrderComplete`'s "is there a waypoint after the cursor" among them. `Route_AdvanceCursor`, `Route_NextWaypointClosesRoute` (`00423170`), `Group_OrderTargetPosition` and `DBSim_SpawnMissionObjects`'s heading fallback read the waypoint group directly instead.

`Route_AdvanceCursor(cursor)` advances the index, and **wraps it to zero when the route closes on itself**: with more than one waypoint, an index that has just landed on the last one whose point is the same object as the first restarts at zero. A closed route is a patrol that never ends; an open one runs out, and running out is what finishes a movement order.

## The tick — `Group_OrderTick` (`00423a74`)

```
order = group.orders[group.orderIndex]
advance = false
if (order != null) {
    if (Group_IsOrderComplete(group, order)) {
        advance = true
        group.orderDone[group.orderIndex] = 1
    } else if (order.action != null && order.action.fired != 0) {
        advance = true
    }
}
if (advance && group.orderIndex < 9 && group.orders[group.orderIndex + 1] != null) {
    group.orderIndex++
    for each member: member.dwellCountdown = 0        // the block field at mech+0x52
}
for each member: Mech_AiTick(member)
```

Four things the shape settles:

- **Two ways forward, and only one of them flags the order.** Completing it sets the `+0x70` byte; a mission action firing under it does not. The flag is read outside this layer, by objective condition 0 — so an objective written against a route is satisfied by the group finishing that order and not by its action firing.
- **The advance is capped by the next slot, not by the count.** A group that finishes its last order stays on it, re-testing (and re-flagging) it every frame for the rest of the mission.
- **The dwell reset is what makes an advance visible immediately.** Zeroing every member's countdown forces each one's reassess on the very next tick, so the new order takes effect at once rather than after the old state's dwell — see [`ai-dispatch.md`](ai-dispatch.md#what-the-dwell-time-buys).
- **Every member is ticked, live or not.** There is no filter here; `Mech_AiTick` handles a machine with no descriptor by doing nothing, which is how the structure groups, whose members have no behaviour block, pass through harmlessly. Most have no orders either, but 12 retail structure-group records carry them — ten a single `travelling` order, one a single `following`, and `C2_08.MSN`'s travel, guard, travel. None of the twelve names a point or a heading of its own, so slot 0's route is where `Base_AttachToGroup` stands its members and which way `DBSim_SpawnMissionObjects` faces the group.

## When an order is finished — `Group_IsOrderComplete` (`004239fc`)

Switched on the verb. Two verbs have no completion test at all and can only be ended by their action firing.

| Verb | State it installs | Completes when |
|---|---|---|
| 0 | `search/destroy` | The subject's condition is 4 — destroyed |
| 1 | `ramming` | Never |
| 2 | `guarding` | The order names a subject, **and** either its condition is 4 or no rival group is still working to it |
| 3 | `patrolling` | The route has no waypoint after the cursor |
| 4 | `sleeping` | Never |
| 5 | `travelling` / `bulldog travel` | The route has no waypoint after the cursor |
| 6 | `following` | The route has no waypoint after the cursor |

The state column is [`ai-dispatch.md`](ai-dispatch.md#choosing-a-state--mech_aiselectbehaviour-0041eb34)'s; it is repeated here only as a key. A flyer group's members map the same verbs onto their own states — see [`ai-flyers.md`](ai-flyers.md#orders--flyer_aiselectbehaviour-00422d00).

### The subject's condition — `Group_OrderSubjectCondition` (`00412dc4`)

A 0-4 scale, where 4 means gone. Which of two readings applies is the order's `+0x0c`:

- **An object** (kinds 1-3). Destroyed (`+0x99`) or collapsed (`+0xb4`) is 4. Otherwise its own vtable `+0x40` overall-damage figure, banded: below `0x32` → 0, below `0x80` → 1, below `0xc0` → 2, else 3.
- **A group** (kind 0) is `Group_ConditionTier` (`00412c8c`), which weighs the same scale across the members. Let *lost* be the members that are immobilised (`+0xa4`) or destroyed (`+0x99`), and *average* the mean of every member's overall damage — **including the dead ones**, each contributing whatever its damage figure last read. Then: all lost → 4; else *lost* at or above 650/1024 of the group **or** *average* at or above `0xc1` → 3; else 250/1024 or `0x81` → 2; else *average* at or above `0x33` → 1; else 0.

So a group is written off either by losing enough machines or by having enough damage spread across the ones it keeps.

### No rival group is still working to it — `Group_NoRivalOrderOnSubject` (`00412e74`)

Walks every mission group and answers false the moment it finds one that is **on the other side**, whose own current order names **the same subject pointer**, and that still has a member in the fight. A guard order therefore ends when the thing being guarded is gone *or* when nothing hostile is assigned against it any more: the post is finished, not just survived.

**"Still has a member in the fight" is `Group_IsWipedOut` (`00412be4`), which tests all three out-of-the-fight bytes, the disarmed one included** ([the bytes](component-damage.md#the-three-out-of-the-fight-bytes--0x99-0xa4-0xa5)). Its name says destroyed; what it tests is "out of the fight", so a group whose members are alive but disarmed answers yes. The disarmed term is the point rather than an oversight: this order asks whether anything can still contest the post, and a machine with no working hardpoint cannot. It does mean a rival group of ordinary structures, born disarmed ([`structure-behaviour.md`](structure-behaviour.md#five-classes-one-switch)), is wiped out from the moment it is built and never blocks a guard order at all. Anything that reused the function for a destroy-the-group objective would be reading its name, not its test.

The comparison is on the subject pointer, not on the guarded position, so two groups only count as rivals when the mission gave them literally the same subject.

## What else reads an order

The loads of an order slot that the searches in [Open](#open) find read five of the record's fields. Four are read through the current index:

| Field | Read by |
|---|---|
| `+0x00` verb | `Mech_AiSelectBehaviour`, `Mech_AiCombatReassess`, `Ai_SelectTarget` (twice), the `patrolling` think, `Mech_ReceiveSquadOrder` (`00420ad4`) and `Flyer_AiSelectBehaviour` (`00422d00`), all with the same "or `0x0b` if the slot is null" idiom; and `Group_IsOrderComplete`, which `Group_OrderTick` hands only a non-null order |
| `+0x0c` kind | `Group_OrderTargetObject` (`004238a0`), `Group_IsOrderTarget` (`00423918`), `Group_OrderTargetPosition` (`004238d4`), `Group_OrderSubjectCondition`, and the objective-side `Group_OrderSubjectEngaged` (`00412d4c`), `Group_OrderSubjectRouteExhausted` (`004139a0`) and `Group_OrderSubjectArrivedAndClear` (`00413a08`) |
| `+0x0e` subject | The three `Group_OrderTarget…`/`Group_IsOrderTarget` functions; `Group_OrderSubjectObject` (`00412bbc`) and `Group_OrderSubjectGroup` (`00412bd0`), the two accessors the condition and objective-side functions read it through; `Group_NoRivalOrderOnSubject`, comparing it across groups; `Group_IsOrderComplete` and `Ai_SelectDefenceTarget` (`0041e0e0`), each only to ask whether it is null; and `Group_OrderSubjectPtr` (`00423960`), the same read again, which `es2_xref.py` finds no reference to |
| `+0x12` action | `Group_OrderTick` |

See [`ai-targeting.md`](ai-targeting.md) for what the `Group_OrderTarget…` functions decide.

The fifth, `+0x08`, is read outside the current index. `DBSim_BuildGroupRecord` takes slot 0's for the route cursor. `DBSim_SpawnMissionObjects` takes slot 0's for the heading of a group whose record names none: the bearing of the route's first leg, when it has two waypoints. `Mission_GroupOrderCompleteOnRoute` scans all ten slots for the one whose route is the objective's waypoint group, then answers that slot's `+0x70` flag.

`Group_OrderTargetPosition` is the one that falls back: with no subject it answers the **first waypoint of the group's route**, which is why a group ordered to hold a place with nothing named still has somewhere to stand.

## A group with no order at all

`Mech_AiSelectBehaviour` substitutes verb `0x0b` for a null order slot, and `0x0b` matches no case in its jump table. The function is `__cdecl(mech)`. Every case of the jump table loads its descriptor into `EDX`, and the default label (`0041ed82`) runs `MOV EAX,EDX; PUSH EAX` ahead of `Behaviour_SetState`. A non-player machine whose group order is null gets there from `0041ebb6` through `0041ecf6`, which write only `EAX` and `ECX`, so the descriptor handed to `Behaviour_SetState` is the **caller's `EDX`**.

Through `Behaviour_DispatchReassess` (`00415b74`) that is the reassess triple's third word, the last thing the dispatcher writes to `EDX`, and the word is zero in all 22 source blocks. **That default path installs a null descriptor, and `Behaviour_SetState` reads `descriptor+0x04` from it a few instructions on.** The seven direct calls from other functions (`Mech_AiFleeCheck`, `Mech_AiCombatReassess` twice, `Mech_AiOnTakingFire`, `Mech_ReceiveSquadOrder` three times) hand over whatever `EDX` they hold; [Open](#open). The eighth is the function's own recursive call at `0041ecd6`, which re-enters after clearing a squad engage order (squad verb 4) whose target is destroyed, and it hands over the 4 that `0041ebd8` loaded for the squad-verb switch, so `Behaviour_SetState`'s read of `descriptor+0x04` is a read of address 8.

A group with neither a slot-0 order nor a heading of its own faults earlier, at load: `DBSim_SpawnMissionObjects` reads slot 0's `+0x08` without testing the slot (`004260f0`). Every retail group, merged by GUID with conditions not applied, names a heading or a slot-0 order.

Nothing in the retail missions reaches the null descriptor. Across the 62 `.MSN` files, every mech group and every flyer group that names a member carries an order in slot 0 as authored (503 groups, counted by GUID, the player's record 0 left out, campaign conditions not applied); the groups that carry none are all structures, and a structure has no behaviour block for `Mech_AiTick` to find.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| Each order carries the route the group follows while that order is in force | The order record does hold one at `+0x08`, and in 14 retail groups across six missions a later order names a different route from slot 0's — but only slot 0's is installed, at group construction. A group that advances to its second order keeps walking the first one's route from where the cursor stands, with nothing left of it if the first order was a movement order that ran it out |
| A group with no orders simply keeps whatever state its machines already had | That is what the *absence* of a matching case looks like in the decompiler, where the descriptor argument reads as an unwritten parameter. In the disassembly it is `EDX`, which is zero when the reassess dispatcher is the caller |
| `Group_OrderTick` skips members that are dead or removed | It ticks every member unconditionally. The filtering is `Mech_AiTick`'s, and it is by whether the machine has a behaviour descriptor at all |

## Open

- **Open:** (Deferred) whether a group ever switches to a later order's route. As found, a group walks slot 0's route for the whole mission ([The route cursor is loaded once](#the-route-cursor-is-loaded-once)), so in the 14 retail groups whose later orders name a different route (`C1_06`, `C2_05`, `C2_08`, `C4_01`, `C4_06` and `C5_10`; `HercWorks.Query orders --route-switch`), that route is ignored. The `C1_06` retail observation rules out a writer that installs the new route with its cursor reset, as `DBSim_BuildGroupRecord` installs the first; what remains is a writer none of the searches below resolves that also leaves the cursor in place. The other missions make poor retail tests: `C4_06`'s switching groups reach their travel orders only after destroying a whole allied base, and `C2_08`'s two supply transports never drive their first route. Searched: `es2_fieldscan.py 6 --writes-only` over the whole image finds 97 writes, and `DBSim_BuildGroupRecord`'s at `00423be4` is the only one on a group record, found through its `EAX = EBX + 0x4` rebase; of the 15 dword stores at displacement `+0x02` in the disassembly, the one that writes through a route cursor (`group+0x04`) is that same store, and `Route_AdvanceCursor`, which callers hand the cursor by pointer, writes the index only; none of the image's 138 `memset`/`memcpy`/`memmove` calls has a built group record as its destination. An action is not a route to one either: `Action_Activate` writes only its own fired flag, its counters and its message, and of the two readers of that flag, `Group_OrderTick` stores the completion flag, the order index and the members' dwell countdowns, and `Group_DeploymentCheck` stores the group's `+0x13` and `+0x14` and its members' positions.- **Open:** whether DBSIM reads an order's formation (`+0x02`) or point (`+0x04`). As found, they matter only to the briefing map, so a group keeps the formation its own record names and never goes to a point an order names. A reader would mean a group re-forms or heads for a point when an order starts, but little retail data could show it. The 47 orders with a point (`HercWorks.Query orders --with-point`) are 25 guard, 10 sleep, 9 follow, 2 travel and 1 patrol; every follow order and 23 of the guard orders also name a subject, which is what they aim at, and a sleeping group does not move. The orders where the point could be the only destination are the guard orders of `C2_08` group 180 and of `C3_01`'s player squad, `TRAIN4` group 40's patrol, and the travel orders of `C1_06` group 229 and `C2_08` group 180. Searched: the order pool pointer `004a9eb4` has no reference outside `DBSim_SpawnMissionObjects`; every order-slot load the disassembly shows as `[reg + reg*0x4 + 0x44]` or `+ 0x48]` (29 sites on a group record), every `group+0x44` access `es2_fieldscan.py 44 48` resolves through a rebase, and every `+0x6c`-indexed slot read in the decompile goes on to read only the five fields in [What else reads an order](#what-else-reads-an-order), and the same searches find a reader of each of those five.
- **Open:** (Deferred) whether the AI reads the per-order completion flags at `group+0x70`. The mission-objective layer reads them; the AI tracks its progress through the current order's index, so a reader there would add little. `es2_fieldscan.py 70`-`79` over `00411000`-`00424200` and a disassembly grep for an indexed `+ 0x70]` find only `Group_OrderTick`'s write and `Mission_GroupOrderCompleteOnRoute`'s read.
- **Open:** (Deferred) which garbage descriptor a group with no order gets when `Mech_AiSelectBehaviour` is reached from another function rather than from the reassess dispatcher. It depends on what `EDX` holds at the seven direct call sites (`0041cc24`, `0041d068`, `0041d134`, `0041fab5`, `00420d82`, `004210af`, `00421109`). Only a mech group with no order reaches this, and no retail mech group outside the player's squad has one ([A group with no order at all](#a-group-with-no-order-at-all)); the reassess route already faults on such a group, so the answer only picks between faults.
