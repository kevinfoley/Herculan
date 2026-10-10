# AI targeting

How an AI machine acquires, shares, keeps and abandons a target.

[`ai-dispatch.md`](ai-dispatch.md) owns the 22 behaviour states, the descriptor layout, the `mech+0x4d` behaviour block and the three vtable dispatchers; state indices, descriptor addresses and descriptor flag bits are cited from there. [`target-selection.md`](target-selection.md) owns `mech+0x1a4` itself, the sensor model that decides what is *known*, and the player's own selection, which the cockpit makes.

`Ai_SelectTarget` is not mech-only. Of its 16 call sites, two are structures' — `Base_ArmedThinkTick` (`00404100`, which a ground vehicle's tick also runs) with mask `0x30`, and the transport's tick (`Base_TransportThinkTick`, `004045c8`, [`structure-behaviour.md`](structure-behaviour.md#the-transport--004045c8)) with mask `0x10` inside a `0x3000` cone — and two are the flyer's `search/destroy` and `patrolling` thinks ([`ai-flyers.md`](ai-flyers.md)), so HERCs, aircraft and armed structures all acquire through it. The guard states' defensive pick, `Ai_SelectDefenceTarget`, is a separate routine — see [Taking fire](#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50).

## The AI writers of `+0x1a4`

| Writer | When |
|---|---|
| `Mech_AiCombatReassess` (`0041cf18`) | The reassess slot of every combat state |
| `Mech_AiOnTakingFire` (`0041f7b8`) | Something hit this machine |
| `Mech_AiEngageOrderedTarget` (`0041c0f4`) | A squad order names a target. `Mech_ReceiveSquadOrder` (`00420ad4`) also hands it an `Ai_SelectTarget(this, 1, 0)` pick, at two sites |
| `Mech_BehaviourRamThink` (`0041e570`) | The ramming state, through `Ai_SelectTarget(this, 6, 0)` |
| `Mech_BehaviourSearchDestroyThink` (`0041d60c`), `Mech_BehaviourPatrolThink` (`0041d7d0`), `Flyer_BehaviourSearchDestroyThink` (`00422a80`), `Flyer_BehaviourPatrolThink` (`00422b34`) | Their own `Ai_SelectTarget` picks |
| `Mech_BehaviourGuardThink` (`0041e224`) | `Ai_SelectDefenceTarget`'s pick, or a verb-4 squad order's target |
| `Flyer_EngageWithFlight` (`00422bf0`) | A flight's leader hands its own target to every member |
| The `sleeping`, `fleeing`, `search/destroy`, `patrolling`, `travelling`, `following` and `guarding` thinks, and `Mech_AiSelectBehaviour` (`0041eb34`) | Clear it |

`travelling` and `following` call `Ai_SelectTarget` too, with mask 0, but keep the pick at `mech+0x5f` as a look-at rather than installing it ([`ai-navigation.md`](ai-navigation.md)). Outside the AI, the two structure ticks, `Player_PerFrameCockpitUpdate` (`0041b130`) and `Mech_ComponentDamageWrite` (`00417de4`) write `+0x1a4` as well.

Every one of these writes maintains the target's `+0x1a2` holder count and raises `mech+0x9d` the same way, so the rule in [`target-selection.md`](target-selection.md) holds for the AI paths as well.

## Is it a target at all — `Ai_IsTargetable` (`00411e80`)

`(this, candidate, mask)`. Rejects, in order:

- the same side (`group+0x12`);
- `+0x99` destroyed or `+0xb4` collapsed;
- not currently known — `Ai_KnowsObject` (`00411c58`): radar-visible (`+0x95`) within **999999**, or a contact this machine holds (`this+0xc2 + candidate[0x4b]`) at any range. **The AI's knowledge test is far looser than the player's**, which caps radar at 200000 and contacts at 30000/60000 ([`target-selection.md`](target-selection.md#can-this-be-targeted--targetselect_cantarget-00433174));
- `+0xb7` invulnerable (`BASES.DAT +0x1e`, latched by `Base_Construct`);
- with `mask & 0x10`, a candidate of this machine's own object class (`+0x1a8`);
- a flyer (class 2) that is out of action (`+0xa5`, `+0xa4` or `+0x99`);
- the group's own order target, while this machine's group is led by the local player and the mission's objective type is 3 -- the data-link shield, see [`mission-objectives.md`](mission-objectives.md#the-player-thinks-objective-arms);
- when `this+0x9a` is set, whatever the player currently has selected (`CockpitViewInstance+0x210`) — the courtesy that stops the squad piling onto the player's target. `Mech_AiOnTakingFire` clears `+0x9a` when the player's target is the thing shooting at this machine.

## Acquisition — `Ai_SelectTarget` (`00411fa0`)

`(this, mask, coneLimit)` walks the live object list and returns the best-scoring candidate, or 0.

**Mask bits**, and the callers that pass them:

| Bit | Effect | Passed by |
|---|---|---|
| `0x01` | Only the group order's designated target gets the tier bonus below | `Mech_AiCombatReassess` (verbs 0–2), the `search/destroy` and `patrolling` thinks, `Mech_ReceiveSquadOrder`, the flyer's `search/destroy` think (`0x11`) |
| `0x02` | Suppress the "it is shooting at me" weight | `Mech_BehaviourRamThink` (`6`) |
| `0x04` | Suppress the crowding divisor | `Mech_BehaviourRamThink`, `Mech_AiOnTakingFire` (`0x24`) |
| `0x10` | Reject candidates of this machine's own class, through `Ai_IsTargetable` | the two structure call sites, the flyer's two |
| `0x20` | Ignore bearing: score on range alone | `Mech_AiOnTakingFire`, `Base_ArmedThinkTick` (`00404100`) |

**The tier** is a coarse bar applied before scoring: `2 * designated + alive`, where *designated* means `Group_IsOrderTarget` (`00423918`) and a group order verb of 0, and *alive* is the usual `+0xa5`/`+0xa4`/`+0x99` triple all clear — so an unarmed building counts as not alive, since `Base_Construct` sets `+0xa5` at birth on the plain and radar structure classes. The bar starts at 1 and drops to 0 when the group order verb is 3 (patrolling). With `mask & 1` clear every candidate counts as designated, so the bar only bites for the callers that set the bit. The bar also ratchets: a candidate of a higher tier than the best so far replaces it whatever its score, and the bar rises to that tier for the rest of the walk. A candidate whose `+0xa4` is set is skipped outright **unless it is the designated target and this machine is Cybrid** — human-side machines leave a crippled target alone, Cybrids finish it.

Range is capped at **1000000** for the designated target and **100000** for anything else.

**The score** starts from bearing, `b = 0x1000 - (bearingError >> 3)`, which is 4096 dead ahead and 0 astern. `coneLimit`, when nonzero, rejects anything outside it.

```
score = Q10(70, b)                               // 0..280
if (range < 60000) score = max(score, b / max(range >> 12, 1))
score = Q10(score, W_shooting[rating])           // when candidate+0x1a4 == this, unless mask & 2
score = Q10(score, W_idle[rating])               // when its state's flag bit 1 is clear and it is not the player
score = Q10(score, W_taken[rating])              // when it holds some other target
score = Q10(score, W_class[objectClass])
if (holders != 0) score /= holders + 1           // holders = candidate+0x1a2 less this machine; unless mask & 4
```

Inside 360 m the proximity term takes over completely — at point-blank `b / 1` is an order of magnitude above the bearing term, so a close enemy outranks a better-aligned distant one.

| Table | Values (Q10) | Indexed by |
|---|---|---|
| `W_shooting` `0049933c` | 1500, 2000, 2500 | relative combat rating |
| `W_idle` `00499342` | 600, 400, 200 | relative combat rating |
| `W_taken` `00499348` | 700, 850, 1000 | relative combat rating |
| `W_class` `0049934e` | 1500, 700, 500, 500 | object class `+0x1a8` |

A machine strongly prefers what is already shooting at it, discounts anything not yet engaged — hardest when that thing outguns it — and mildly discounts what someone else already holds. A class-1 candidate that is shooting at this machine is re-indexed as class 0, giving it a HERC's weight. Only an armed tower can reach that branch, since it is the one class-1 object that holds a target: the transport writes `+0x1a4` only across its own `Rocket_Fire` call and clears it straight after, and a ground vehicle holds one through the armed tick but is class 3, and keeps class 3's weight.

The shooting-at-me, taken and re-index tests read the **candidate's** `+0x1a4` off the shared base, so they see an armed structure's selection exactly as they see a machine's — and the holder count the divisor works from is every object holding the candidate, structures and aircraft included. `W_idle` reads the candidate's behaviour descriptor instead, which a structure has none of, and the original's null check skips it.

## Relative combat rating

**`Mech_CompareCombatRating` (`0041cabc`, mech vtable `+0x4c`)** returns the index those three weight tables share: **0** this machine's rating is the higher, **1** the two are within 400, **2** the candidate's is higher. It returns 0 for every non-HERC *candidate*, so a machine scores a structure or a flyer against column 0.

The slot is dispatched on the **asker**, though, and only the HERC class fills it with that function: the structure and flyer tables both install `SimObject_CompareCombatRatingStub` (`00411ac0`), a bare `return 1`. So a base turret and a Cybrid aircraft score every candidate against **column 1** — the middle of each table — however the ratings actually compare.

Both ratings are jittered before the comparison, and the jitter is `rand & 1000` where `rand % 1000` was plainly meant: `AND AX,0x3e8` at `0041cadd` and `0041caf8`, on the full 16-bit result of `Math_RandomNext` (`00492dd4`). `0x3e8` has six bits set, so the mask can only produce the 64 sums of a subset of 8, 32, 64, 128, 256 and 512: the jitter spans 0–1000 but lands on 64 of its 1001 values, every one a multiple of 8.

**The rating itself is `mech+0x29e`, computed by `Mech_ComputeCombatRating` (`0041edd8`)**:

```
rating = (typeRec+0x44                                                    // a per-type base
        + sum over live mounts   Q8(template+0x4e, 256 - mountDamage)     // weapon value x condition
        + sum over 19 components Q8(componentMax,  256 - damage)          // structure value x condition
        - sum of typeRec+0x7e[i] for each of 10 systems over 70% damaged
        ) >> 4
```

The weapon term reads the mount's own `+0x1c`, which `WeaponMount_CtorBase` (`0040df30`) sets to the `WEAPONS.DAT` template, so `+0x4e` is a template field. A mount counts when its vtable `+0x54` says so. That slot is `return 1` (`WeaponMount_CountsInCombatRating_Always`, `004111e9`) on every class but the two that carry rounds, the ammunition and grenade classes; those count only while they hold at least an eighth of their magazine, `template+0x3a >> 3` (`WeaponMount_AmmoCountsInCombatRating`, `0040f520`). So a machine's rating drops as its launchers and guns run low, not only as they are shot up.

**Retail states the same two numbers for all 21 chassis** — a base of 1000 at `typeRec+0x44` and a penalty of 500 at each `typeRec+0x7e[i]` — so what separates two machines is entirely their guns, their armour and their damage.

`Mech_ReadDamageReadouts` fills the three parallel readout blocks it reads — 19 components, 12 systems of which the penalty loop takes the first 10, and 10 mounts — as Q8 damage. `Mech_PerTickSystemsUpdate` recomputes the rating whenever `mech+0x94` is clear, and `Mech_DirectFireHitTest` (`00418ba8`) clears it when a shot gets through the shield, so it is a lazily refreshed cache that tracks battle damage: **a machine's worth as a target, and its own willingness to fight, both fall as it is shot apart.** What else clears it is [Open](#open).

## Passing a contact on

`Mech_AiOnTakingFire` routes the attacker through `Detection_ShareContact` (`00412704`, via the thin `Mech_ShareContact`, `00411aec`), the same function the sensor sweep uses — see [`target-selection.md`](target-selection.md#the-sensor-model--sim_detectiontick-004123ac). **Being shot is a way of being spotted**: the whole side within 100000 of the attacker learns where it is, whether or not anyone had line of sight. Each of them within 50000 of the attacker also marks the attacker engaged and activates its own engagement action ([`mission-deployment.md`](mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6)). A structure or an aircraft that is hit does the same, past only the raycast's own gate ([`hit-detection.md`](hit-detection.md#the-sweep--sim_raycastobjectlist-00426528)), because `Mech_ShareContact` itself is what the structure, flyer and base object tables install at `+0x50`.

## Taking fire — `Mech_AiOnTakingFire` (`0041f7b8`, mech vtable `+0x50`)

`(this, attacker, damage)`. The gates, in order:

1. If the attacker is the player and this machine is in the player's group, `Mech_AiFriendlyFireComplaint` (`0041f790`) posts squad message 8 on a 40000-count cooldown (`this+0x278`), about 19.5 s ([`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units)).
2. Return if the attacker is out of action (`+0xa5`, `+0xa4` or `+0x99`), or on this machine's own side.
3. Share the contact; set the under-fire window `this+0x27d` to **30000**.
4. **A machine in the player's group accumulates the damage in `this+0x281` and ignores it until the total passes 8000.** The accumulator is cleared once the window has run out, about 14.6 s after the last hit, by the timer `Mech_PerTickSystemsUpdate` steps at `this+0x27c`. The player's squadmates are deliberately slow to break off.
5. Return for the local player, while the retarget cooldown `this+0x273` runs, or when the attacker is already this machine's target and its state carries flag bit 1.
6. Set the retarget cooldown to **10000**.

Then the response, chosen by the current state's descriptor flag bits:

- **Not yet engaged (bit 1 clear), in a group led by the player** — post squad message 3 if the attacker is a HERC, and clear `+0x9a` if the attacker is what the player has selected.
- **bit 2 — `guarding`, `driving off en`.** Defend the post instead of chasing the shooter: `Mech_AiGoalPosition` (`0041dbcc`) resolves the place being held — a squad order's target or point, otherwise `Group_OrderTargetPosition` (`004238d4`) — and `Ai_SelectDefenceTarget` (`0041e0e0`) picks the HERC scoring highest on `(limit - distanceToPost) / (holders + 1)`, `limit` being 90000, or 980000 when the group's current order names no object. Unless the pick is itself shooting at this machine, a squad order with verb 4 replaces it with the order's own target; with neither, the attacker is installed. Installing a HERC gives `driving off en` and runs `Mech_AiSelectAimComponent`; anything else goes to `Mech_AiEngageOrderedTarget`.
- **bit 3 — `sleeping`, `ramming`, `bulldog travel`.** Return: incoming fire is ignored entirely.
- **Otherwise** — `Ai_SelectTarget(this, 0x24, 0)`, bearing-blind and crowding-blind, because the machine is reacting rather than choosing. Under squad order verb 4 a target that is not shooting at this machine is replaced by the order's own, and the order is cleared if that is missing, crippled (`+0xa4`) or destroyed. Finding nothing while it held something re-enters `Mech_AiSelectBehaviour`; finding something sets `+0xac` and enters the combat reassess.

## The combat reassess — `Mech_AiCombatReassess` (`0041cf18`)

The reassess slot of states 3–7 and 18. `Mech_AiEnterCombat` (`0041d5ec`) is a thin wrapper other paths call to force it.

**Radar.** `mech+0x96` goes ACTIVE here unless the machine's group is led by the local player, gated on the countdown at `mech+0x26b`; a player squadmate is put back to PASSIVE unless `mech+0xb2` is set. This is what puts a distant enemy's radar on, and so what makes it targetable by the player at long range — see [`target-selection.md`](target-selection.md#radar-mode).

**Keep or acquire.** `mech+0xac`, set by whoever just handed this machine a target, suppresses the acquisition for one pass and clears itself. Otherwise the squad order verb splits it:

- **verbs 3–6** — the squad order's target (`mech+0x248`) is installed; if it is absent or destroyed, `Ai_SelectTarget(this, 0, 0)` replaces it.
- **verbs 0–2** — `Ai_SelectTarget(this, 1, 0)`, which prefers the group order's designated target. The one exception: a machine that is *already* committed (flag bit 1) and under a movement order — squad verb 1, or squad verb 0 with a group order verb of 5 or 6 — skips acquisition and falls into `Mech_AiSelectBehaviour`, which drops the target and returns it to the order. **Being told to move ends a fight.**

Coming out of either with no target also ends in `Mech_AiSelectBehaviour`.

**The leader drags the group in.** A machine that is its group's first member (`**(group+0xc)`) runs `Mech_AiEnterCombat` on every other live member whose state's flag bit 1 is clear. One member finding a fight commits the whole group to it. Each dragged member then takes the keep-or-acquire branch above for itself, through its own `Ai_SelectTarget`, and never reads the leader's `mech+0x1a4`, so the crowding divisor spreads the group across targets rather than piling it onto the leader's.

**Then the state.** `mech+0x2a2` is reset to −1, the flee check runs, and if it did not take the decision itself, the combat state follows from the target. For a HERC target the flee check runs a second time before the ratings are compared; it draws no random number, so the repeat gives the first call's answer.

| Target | State |
|---|---|
| Class 1 or 3 — structure | `attacking base` (6) |
| Class 2 — flyer | `attacking flyer` (7) |
| Class 0, rating index 0 — I outgun it | `facing off` (5) |
| Class 0, rating index 1 — evenly matched | `attacking` (3) |
| Class 0, rating index 2 — it outguns me | `flanking` (4), or `facing off` when either leg-damage latch (`+0xa8`, `+0xa9`) is set or its type's `typeRec+0xc8` is 0xb9 or under |

**The `flanking` gate is forward speed.** `typeRec+0xc8` is not a record field: the file's own word at that offset is zero on all 21 chassis, but `MechType_InitOne` (`004202c1`) overwrites it at load with a copy of `typeRec+0x06`, the chassis' forward speed. Retail speeds run 140 to 325 against a bar of 0xb9 (185), so **13 of the 21 chassis records clear it and 8 do not** — an outgunned machine goes round what outguns it if it is fast enough to, and stands and takes it if it is not. One of the 13 is RAZOR, which no retail mission gives an AI to fight in, so twelve ground chassis flank in practice. `+0xa8` and `+0xa9` are the two graded leg-damage latches — a side past `0x50` damage, and a side at `0x8d` or worse ([`component-damage.md`](component-damage.md#what-the-endpoint-announces)) — so a machine with either leg side past `0x50` (about a third, on the Q8 damage scale) stands and takes it rather than going round.

`Mech_AiSelectAimComponent` runs after each of the three class-0 branches, once the flee check has not taken the decision.

## The flee check — `Mech_AiFleeCheck` (`0041cb94`)

Returns nonzero when it has taken the decision itself, which is how the reassess above knows to stop.

A machine that is itself out of action (`+0xa5`, `+0xa4` or `+0x99`) takes `fleeing` (18), unless its target is a structure whose `BASES.DAT +0x2e` is zero, which sends it to `Mech_AiSelectBehaviour` instead — so a crippled machine breaks off from a structure that can hurt it and presses on against one that cannot. The field is [`ai-combat-states.md`](ai-combat-states.md#basesdat-0x2e)'s.

Otherwise it builds a **fear** value: `Mech_GetOverallDamage` (mech vtable `+0x40`) plus, for each of 6 internal components (`0049a328` = 0, 1, 4, 5, 6, 7) whose Q8 damage exceeds a band threshold, that band's penalty — `+5` over 60, `+25` over 120, `+50` over 180, cumulative, stopping at the first band no component reaches. Fear then decides against `+0x1a2`, the number of objects holding this one as their target — armed structures and aircraft included:

| Fear | `+0x2aa` | Flees when |
|---|---|---|
| ≥ 71 | 1000 | anything at all is targeting it |
| 51–70 | 600 | 3 or more are, or 1–2 are whose combined rating beats its own |
| 31–50 | 300 | 3 or more are, whose *average* rating beats its own |
| ≤ 30 | — | never |

`Ai_SumAttackerRatings` (`0041cb44`) is the sum of `+0x29e` over every HERC holding this one: it walks `GlobalMechList` (`004a9bfe`), the pool `DBSim_SpawnMissionObjects` allocates HERCs from — aircraft come from a pool of their own — so a tower or an aircraft holding this machine counts in `+0x1a2` but adds no rating, which at fear 31–50 lowers the average. `+0x2aa` is written on all three live bands, and the weapon chooser reads it as its score floor ([`ai-weapons.md`](ai-weapons.md)).

## Which component the shot is aimed at — `Mech_AiSelectAimComponent` (`0041ce08`)

Writes `mech+0x2a2`, the component slot the machine aims at, or −1. Its callers are the combat reassess, after its three class-0 branches; `Mech_AiOnTakingFire`, when a held post installs `driving off en`; and `Mech_BehaviourGuardThink` (`0041e224`). One roll of `rand & 0x7f` picks a band:

| Roll | Band | Slots |
|---|---|---|
| — | Target has collapsed (`+0xb4`) | 0 only |
| < 0x28, **or** a Targeting Pod at `mech+0x30b` whose cached damage `+0x7f` is under 0xaa | Systems | 7 … 18 |
| < 0x50 | Weapon mounts | 19 … 18 + mount count |
| otherwise | Chassis | 0 … 6 |

**Weapon-mount components start at slot 19**, the count coming from the mount manager's `+0x08` (see [`weapon-mounts.md`](weapon-mounts.md#the-manager--mech0x202)) — this machine's own manager, `this+0x202`, not the target's. Within the band it skips slots the target no longer has — its occupancy array at `target+0x20e` — and takes the highest `Component_ReadDamagePercent + (rand & 0x3f)` above zero, working at whatever is already most damaged with enough jitter to spread the fire.

**It reads its own damage, not the target's.** `0041cec9` passes `this+0x206` to `Component_ReadDamagePercent` while the loop walks the *target's* occupancy array, so the AI works on whichever component of **its own** hull is worst hurt, constrained to the slots the target still has. The target's occupancy array and its `+0xb4` are the only fields the function reads off the target; the mount band's width is the aiming machine's own mount count, from the same register.

## Abandoning a target — `Ai_ShouldAbandonTarget` (`0041c4a8`)

Called from the think functions of `attacking`, `flanking`, `facing off` and `driving off en`. Three answers, in order:

1. **Under squad order verb 4** with a target object: abandon when the target's *state tier* exceeds the order's threshold at `mech+0x250`. The tier is `Ai_TargetStateTier` (`00411cb4`), read from the target's own behaviour descriptor: **2** for flag bit 5 (`in limbo`, `dead`, `disabled`), **1** for bit 4 (`fleeing`), 0 otherwise. So an order can say *chase it until it runs* or *chase it until it drops*.
2. **The group order's designated target** (`Group_IsOrderTarget`): a human-side machine abandons it dead or crippled, a Cybrid only once `+0x99` is set.
3. **Anything else**: abandon when out of action (`+0xa5`, `+0xa4` or `+0x99`) — which includes a target that has only run out of weapons.

## Radio callouts

A squad message is posted with `Ai_PostSquadMessage` (`00420a98`), which [`cockpit-messages.md`](cockpit-messages.md#the-pilot-and-squad-channel) owns along with the id catalog. Three ids are raised from this slice:

| Id | Raised by |
|---|---|
| 1 | `Mech_AiEnemySighted` (`00412800`, mech vtable `+0x48`) |
| 3 | `Mech_AiOnTakingFire`, for a squadmate of the player hit by a HERC |
| 8 | `Mech_AiFriendlyFireComplaint`, when the player is the one shooting |

There is a **second friendly-fire site**, in `Sim_RaycastObjectList` itself rather than in `Mech_AiOnTakingFire`: when the player's shot hits anything on his own side outside his own group — another group's machine or a friendly structure — `Group_NearestLiveMember` (`00423974`) finds the player's nearest live squadmate within 100000 of him and, if it is inside 30000 of the player, that squadmate complains. The struck object's own `+0x50` slot runs afterwards as usual.

`Mech_AiEnemySighted` fires at most once per enemy for the whole player group. `Detection_Sweep` calls it as a contact or a radar paint is about to be made, and `DAT_004a9b84[obj+0x4b]` is a per-object latch. The local player's own machine sets the latch whenever it calls in, without calling out. A squadmate sets it only for an object that is not class 1, not yet radar-visible (`+0x95`), and held as a contact by neither the squadmate nor the player, and calls out at that moment unless the rate limit `DAT_004a9be9` is still running. The latch is set either way, so a callout the rate limit swallows is lost rather than deferred. The limit is re-armed to 10000 counts on each callout, and `Mech_AiEnemySighted` steps it itself — `Math_CountdownTimerTick` on the handle `004a9be8`, once per call from any machine but the player's, before any of the tests above — so it runs down per call-in rather than per tick, and 10000 is not a fixed 4.9 seconds ([`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units)). Whether anything else steps it is [Open](#open).

## Mech fields this slice owns

Fields settled elsewhere link out rather than being restated.

| Offset | Type | Meaning |
|---|---|---|
| `+0x94` | byte | Combat rating cached; clear means recompute |
| `+0x9a` | byte | Do not take the player's current selection as a target; written by `IGNORE MY TARGET` — [`ai-squadmates.md`](ai-squadmates.md) |
| `+0xa5` | byte | No weapons left; written by `Ai_ChooseWeapon` — [`ai-weapons.md`](ai-weapons.md) |
| `+0xac` | byte | A target was just handed to this machine; skip one acquisition |
| `+0xb2` | byte | Keeps a player squadmate's radar active; written by `SCAN FOR HOSTILES` and `EMCON` — [`ai-squadmates.md`](ai-squadmates.md) |
| `+0xb4` | byte | Collapsed — the machine has finished going down and is lying on the ground. Latched when the death animation plays out ([`mech-locomotion.md`](mech-locomotion.md#going-down)), or at spawn for a machine the mission places as a wreck ([`component-damage.md`](component-damage.md#starting-condition--mech_applystartingcondition-004178e8)) |
| `+0xb7` | byte | Invulnerable; `Base_Construct` sets it from `BASES.DAT +0x1e` |
| `+0x250` | short | Squad order's abandon threshold — [`ai-squadmates.md`](ai-squadmates.md) |
| `+0x26b` | short | Countdown that holds the radar off, 6000 after an ARM hit — [`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set) |
| `+0x273` | int | Retarget cooldown, 10000 on reacting to fire |
| `+0x278` | int | Friendly-fire complaint cooldown, 40000 |
| `+0x27d` | int | Under-fire window, 30000; its expiry clears `+0x281` |
| `+0x281` | int | Damage accumulated from the player's group, threshold 8000 |
| `+0x29e` | short | Combat rating |
| `+0x2a2` | short | Component slot being aimed at, −1 for none |
| `+0x2aa` | short | Fear: written by the flee check, 300/600/1000, and read as the AI's weapon-score floor — [`ai-weapons.md`](ai-weapons.md) |
| `+0x30b` | ptr | Targeting Pod — [`target-selection.md`](target-selection.md#component-targeting--the-targeting-pod) |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `Mech_AiOnTakingFire` is a damage function | It is called per raycast candidate from `Sim_RaycastObjectList` and takes a damage amount, which makes it look like one. It applies no damage: the amount only feeds the `+0x281` accumulator that decides whether a player's squadmate reacts at all |
| `Ai_TargetStateTier` reads offsets `+0x0c`/`+0x0d` of the target's behaviour *block* | It dereferences `target+0x4d` first, so those are offsets into the **descriptor** the block points at — expanded flag bits 4 and 5, not block fields |
| `Mech_AiSelectAimComponent` picks the target's weakest component | It walks the target's component *occupancy* array but reads `this+0x206` for the damage, which is its own |
| The second friendly-fire complaint comes from the struck object's group | `Sim_RaycastObjectList` passes `Group_NearestLiveMember` the shooter's group and the shooter (`ESI`, whose `+0xa3` it has just tested), and measures the 30000 from the shooter: the complaint comes from the player's own nearest squadmate |

## Open

- **Open:** what clears `mech+0x94` besides `Mech_DirectFireHitTest`. No other clear was found by `es2_fieldscan.py 94` over `00400000-00480000` or in the decompile. If there is none, the cached rating takes in magazines running low, and damage that does not come through `Mech_DirectFireHitTest`, only at the machine's next direct-fire hit.
- **Deferred:** what else steps the callout rate limit at `004a9be8`. `es2_xref.py` finds one reference, the `Math_CountdownTimerTick` push in `Mech_AiEnemySighted`; the address sits `0x64` past the `004a9b84` latch array, so an access through that array's base would not show as a reference to it.
- **Deferred:** what the byte `00499358` is for. `Mech_AiEnemySighted` raises it to 1 on its first call from any machine, the player's included, before any other test. `es2_xref.py` finds two references, the function's own compare and increment; the word tables just below it that `Ai_SelectTarget` (`00411fa0`) indexes by target class end at `00499355`.
