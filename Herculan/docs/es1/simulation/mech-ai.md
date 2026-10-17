# DBSIM.EXE — the HERC's AI

**ES1's HERC AI is ES2's: the same behavior states, thinks, target choice and fire decision, in the same order in one object file, on ES1's fields.** What a player would see differently:

- an AI HERC steers four times as hard for a given error, hard over from about 22° off its mark where ES2's needs a quarter turn, and a rammer steers as hard as any other;
- an outgunned HERC always circles what outguns it, however slow or leg-damaged it is;
- a HERC put to `driving off en` walks out to stand between the place it holds and the enemy, facing the enemy, where ES2's faces the enemy from wherever it stands;
- a guard walks its route or keeps formation between its looks round, and its ring is 20000/10000 where ES2's is 30000/10000;
- a HERC taking fire while it holds a place does not answer the shooter on the spot: the state choice runs again on the next tick and puts it back to its order;
- a HERC never advances on an aircraft, and keeps after one at any range;
- a structure is shot at its aim point, never at a component, and one the mission names is let go as soon as it is out of action;
- skirting walks round the object that stopped the shot, at the range of the target, and the line of sight it waits for is tested against shapes and structures alone;
- the Cybrid side's aim scatter is 1000, 666, 333 or 0 by difficulty, and this file's AI reads no Turbo Pod, FIRE AT WILL or Targeting Pod state, all of which ES2's AI tests.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order, the machine first and the other arguments in the reverse of ES2's order ([calling conventions](../formats/compiler.md#calling-conventions)). The machine's fields are [the HERC's](mech.md#the-fields), with the ones this file adds [below](#the-fields).

## Where the object file starts and ends

The code is `00015e40`–`00019853`. It opens, after the three zero bytes that end [the HERC's file](mech.md#where-the-object-file-starts-and-ends), with the run of `ConfigPart`'s copies that starts each object file carrying one ([module copies](../formats/compiler.md#vtables)): `HeapPtr_DtorMechAiCopy` (`00015e40`), to which `es2_xref.py` finds no branch or stored pointer; `ConfigPart_DtorMechAiCopy` (`00015e68`), slot `+0x1c` of `00091c7a`; `ConfigPart_GetClassTagMechAiCopy` (`00015ea7`); and `TSPartBase_DtorMechAiCopy` (`00015eb0`), slot `+0x1c` of `00091cae`. It closes with `MechAi_StaticInitNoOp` (`00019850`), a bare `RET`, the static constructor table's entry `000adc00`, then three zero bytes before [`mechsys.cpp`'s run](mechsys.md#where-the-object-file-starts-and-ends) at `00019854`. No assert in the file names a source path.

Its functions are ES2's from `Mech_AiEngageOrderedTarget` (`0041c0f4`) to `MechAi_StaticInitNoOp` (`0041fdac`), in ES2's order, less `Mech_BehaviourFollowThink` (ES1 has no `following` state), with `Mech_GetGroupSide` (`00016388`) added.

## The state functions

The HERC's twenty states and the think, move and reassess each holds are in [the HERC's state table](shields-and-behaviour-states.md#the-hercs-state-table). `Behaviour_DispatchThink`, `Behaviour_DispatchMove` and `Behaviour_DispatchReassess` ([small functions](mech.md#small-functions)) reach them; `Mech_AiTick` runs the three ([the tick](mission-groups.md#the-tick)).

**Steering.** Every steer this file hands `Mech_LocomotionTick` (`00012038`), (machine, avoidance, speed, steer), is a bearing error shifted right by 4, where ES2's shifts by 6 ([drive to a point](../../retail/simulation/ai-navigation.md#drive-to-a-point--ai_drivetopoint-0041fac4)). The instructions encode the shift as `SAR reg,0x64`, which the processor takes modulo 32: 4. With the steer clamped at ±`0x100`, an ES1 HERC is hard over at an error of `0x1000` (22.5°). The avoidance argument is 1 in `ramming` alone, as in ES2.

## Choosing a state — `Mech_AiSelectBehaviour` (`0001892c`)

(machine), the reassess of `deciding` and of the walking states, as ES2's is ([choosing a state](../../retail/simulation/ai-dispatch.md#choosing-a-state--mech_aiselectbehaviour-0041eb34)), with two paths where ES2's has three:

1. **The player's machine** (`+0x9d`) takes `player`.
2. **Any other** takes the state the word `+4` of its group names ([the group record](mission-groups.md#the-group-record), `.MSN` group `0x32`), through the jump table at `00091d14` over 0–11:

| Word | State |
|---|---|
| 1 | `search destroy` |
| 3 | `ramming` |
| 6 | `guarding` |
| 7, 8 | `patrolling` |
| 10 | `sleeping` |
| 11 | `travelling`, or `bulldog travel` when the type record's twist limit `+0x22` is above `0x7d00` |

Any other word, 0–11 or outside it, installs whatever descriptor `EDI` held when the function was called ([Open](#open)). It reads no standing squad order, where ES2's second path takes the squad order's verb. On every path it then releases the target `+0x15e`, decrementing the target's `+0x15c` and setting `+0x97`, where ES2's releases it on four paths. It returns 1.

`es2_xref.py` finds the ten descriptors that hold it in `Behaviour_BuildStateTable` and six calls: `Mech_AiFleeCheck`, `Mech_AiCombatReassess`, `Mech_AiOnTakingFire` and three in `0001a1cc`, the HERC's slot `+0x2c` ([the HERC's table](mech.md#the-hercs-table)).

## The combat reassess — `Mech_AiCombatReassess` (`00016e04`)

(machine), the reassess of the five combat states and `fleeing`. ES2's ([the combat reassess](../../retail/simulation/ai-targeting.md#the-combat-reassess--mech_aicombatreassess-0041cf18)), with these differences:

- **Keep or acquire.** With `+0xa6` clear and the squad verb `+0x1d2` 0, 1 or 2, a committed machine (descriptor byte `+9`) skips acquisition when the squad verb is 1 or the verb is 0 and its group's word `+4` is 11, `travelling`; otherwise `Ai_SelectTarget` (`0000ddf4`, [what an AI machine may target](sim-object-base.md#what-an-ai-machine-knows-and-may-target))(machine, 1). For verbs 3 and up it keeps the target it holds unless that target is destroyed (`+0x93`), reading the target with no null test, and then takes `Ai_SelectTarget`(machine, 0); ES2's installs the squad order's target first. No target ends in `Mech_AiSelectBehaviour`.
- **The state.** After the flee check: a target of class 1 takes `attacking base`, class 2 `attacking flyer`, and any other goes to the second flee check and `Mech_CompareCombatRating`: 0 `facing off`, 1 `attacking`, 2 `flanking` with no gate, where ES2's sends a slow or leg-damaged machine to `facing off`. ES2's also takes `attacking base` for class 3.

The radar, the leader's drag of the group's uncommitted members through `Mech_AiEnterCombat` and the aim slot `+0x238` reset to −1 are ES2's. **`Mech_AiEnterCombat` (`000174cc`)** is a call of this function; `es2_xref.py` finds five calls of it, from `Mech_AiEngageOrderedTarget`, this function, `Mech_BehaviourSearchDestroyThink`, `Mech_BehaviourPatrolThink` and `Mech_AiOnTakingFire`.

**`Mech_AiFleeCheck` (`00016a74`)**, (machine): ES2's ([the flee check](../../retail/simulation/ai-targeting.md#the-flee-check--mech_aifleecheck-0041cb94)) with the fear tables `AiFlee_InternalComponents` (`00091cf8`, 0, 1, 4, 5, 6, 7), `AiFlee_BandPenalties` (`00091d04`, 5, 25, 50) and `AiFlee_BandThresholds` (`00091d0c`, 60, 120, 180), ES2's values; the fear in `+0x240` and the bands, `fleeing` and the overall damage through slot `+0x44` are ES2's. A machine out of action goes to `Mech_AiSelectBehaviour` instead of `fleeing` only when its target is of class 1 and the target's type record `+0x34` is 0; ES2's tests classes 1 and 3 and `BASES.DAT +0x2e`.

**`Mech_CompareCombatRating` (`00016968`)**, slot `+0x50`, (machine, candidate), and **`Ai_SumAttackerRatings` (`00016a14`)**, (machine), are ES2's ([relative combat rating](../../retail/simulation/ai-targeting.md#relative-combat-rating), [the flee check](../../retail/simulation/ai-targeting.md#the-flee-check--mech_aifleecheck-0041cb94)) on the rating `+0x234`, the class `+0x162` and `g_MechPool` (`000af68c`), with ES2's `& 1000` jitter.

**`Mech_ComputeCombatRating` (`000189f0`)**, (machine): ES2's ([relative combat rating](../../retail/simulation/ai-targeting.md#relative-combat-rating)) from `Mech_ReadDamageReadouts` (`0001583c`), the type record's `+0x44` and `+0x7e`, the mounts at `+0x198` counted by their slot `+0x58`, and the component maxima of `+0x1a8`; it writes `+0x234`, sets `+0x8e` and returns the rating. `es2_xref.py` finds one call, from `Mech_PerTickSystemsUpdate` ([the systems tick](mech.md#the-systems-tick--mech_perticksystemsupdate-00014ee0)).

**`Mech_AiSelectAimComponent` (`00016cf4`)**, (machine): ES2's ([which component the shot is aimed at](../../retail/simulation/ai-targeting.md#which-component-the-shot-is-aimed-at--mech_aiselectaimcomponent-0041ce08)) without the Targeting Pod term: the target collapsed (`+0xae`) takes slot 0; otherwise `rand & 0x7f` under `0x28` takes systems 7–18, under `0x50` mounts from 19 for this machine's own mount count (`+0x198`'s `+4`), and the rest the chassis 0–6. Within the band it skips slots the target's occupancy array `+0x1a4` marks gone and keeps the highest `Component_ReadDamagePercent` (`0000a16c`) + `rand & 0x3f` above 0, reading this machine's own damage at `+0x19c` as ES2's does. It writes `+0x238`.

## The combat states

The shared shape is ES2's ([the shape they share](../../retail/simulation/ai-combat-states.md#the-shape-they-share)): `Ai_BeginSkirtIfBlocked`, `Ai_BuildCombatGeometry`, the state's steering, `Ai_CombatMoveStep`, `Ai_AimAndFire`, and when `Ai_ShouldAbandonTarget` answers or the machine is out of action (`+0x9f` disarmed, `+0x9e` immobilized, `+0x93` destroyed), `Ai_ClearSquadEngageOrder` and a return of 1.

- **`Ai_BuildCombatGeometry` (`0001851c`)**, (machine, target, block): ES2's ([the geometry block](../../retail/simulation/ai-combat-states.md#the-geometry-block--ai_buildcombatgeometry-0041e758)), the target 0 meaning `+0x15e`, the aspect from the target's slot `+0x40`, the standoffs 15000/30000.
- **`Ai_CombatMoveStep` (`00018618`)**, (machine, block): ES2's ([the move step](../../retail/simulation/ai-combat-states.md#the-move-step--ai_combatmovestep-0041e828)), the approach flag written to `+0x54`, a steer of the bearing error >> 4.
- **`Ai_ClearSquadEngageOrder` (`00016294`)**, (machine): clears a squad verb 4 whatever its target; ES2's clears it only when the order's target is the one let go. It returns nothing; its callers return 1.
- **`Mech_BehaviourAttackThink` (`000163a0`)**, `attacking`, and **`Mech_BehaviourFaceOffThink` (`000172f0`)**, `facing off`, are ES2's ([`attacking`](../../retail/simulation/ai-combat-states.md#attacking-3--mech_behaviourattackthink-0041c594), [`facing off`](../../retail/simulation/ai-combat-states.md#facing-off-5--mech_behaviourfaceoffthink-0041d41c)).
- **`Ai_CircleStep` (`00016574`)**, (machine, block): ES2's ([the circling step](../../retail/simulation/ai-combat-states.md#the-circling-step--ai_circlestep-0041c72c)) on the break-off countdown `+0x5a` (handle `+0x59`), the interval `+0x5d` (handle `+0x5c`), the damage window `+0x21e` and the hysteresis byte `+0x60`, and ending with `Ai_CombatMoveStep` on the block, which ES2's leaves to its caller. So **`Mech_BehaviourFlankThink` (`000173c8`)**, `flanking`, makes no move step of its own.
- **`Mech_BehaviourAttackBaseThink` (`00016718`)**, `attacking base`: the skirt gate; the aspect and its magnitude set to 32000 when the target's type record `+0x34` is 0; `Ai_CircleStep`, which runs the move step at the block's standoffs; then the standoffs 25000/−20536 and `Ai_CombatMoveStep` again, so `Mech_LocomotionTick` runs twice a tick and the speed and turn rate move toward their demands twice; `Ai_AimAndFire`. It is done when the target or the machine is out of action. ES2's ([`attacking base`](../../retail/simulation/ai-combat-states.md#attacking-base-6--mech_behaviourattackbasethink-0041c86c)) reads `BASES.DAT +0x2e`, steps once, and holds a target the mission names until it is destroyed.
- **`Mech_BehaviourAttackFlyerThink` (`0001684c`)**, `attacking flyer`: the approach flag is −1, or 0 when the bearing error is under 2000 and the aspect's magnitude is over 2000 or the range over 35000; ES2's ([`attacking flyer`](../../retail/simulation/ai-combat-states.md#attacking-flyer-7--mech_behaviourattackflyerthink-0041c9cc)) takes 1 when both hold, and gives up past 150000. It is done when the target or the machine is out of action; ES2's waits for the target's `+0x99`.
- **`Mech_BehaviourDriveOffThink` (`00017c84`)**, `driving off en`: with *g* `Mech_AiGoalPosition` and *t* the target, after the skirt gate it takes the point *p* that lies min(|*g t*| / 2, 50000) from *g* toward *t* on the ground (Z 0), all distances `Math_DistanceBetweenPoints2D`.
  1. While |*g t*| is at most that distance, which holds only for a target on *g*, and the bearing errors to *p* and to *t* differ by `0x1000` or more or |*g t*| is at most |*g* machine|, it returns `Mech_BehaviourFaceOffThink`'s answer.
  2. Otherwise, within 5000 of *p* it stands and turns toward *t*; else it walks at *p* at `0x100` while *p* is within `0x2000` of the nose, and backs at −`0x100` steering at *p* otherwise.
  3. It sets the fear `+0x240` to 750 and fires through `Ai_AimAndFire` with the aspect built by hand, the target's twist plus the bearing back from it less its heading; when `Ai_ShouldAbandonTarget` answers or the machine is out of action it clears the squad order's target `+0x1dc` and returns 1.

  ES2's think (`0041def0`, [`driving off en`](../../retail/simulation/ai-combat-states.md#driving-off-en-16--mech_behaviourdriveoffthink-0041def0)) returns `Mech_BehaviourFaceOffThink`'s answer and jumps over the 444 bytes that follow.
- **`Mech_BehaviourFleeThink` (`0001713c`)**, `fleeing`: ES2's ([`fleeing`](../../retail/simulation/ai-combat-states.md#fleeing-18--mech_behaviourfleethink-0041d2c4)) on the stash `+0x5b`, the side countdown `+0x55` (handle `+0x54`, 4000) and the side `+0x59`, with no Turbo Pod; releasing the target it also clears the squad verb `+0x1d2`. It returns 1 when the machine itself is out of action as well as when the stash is. The flee check sends a machine out of action to `fleeing`, so such a machine returns 1 on each think and the combat reassess installs the state again on the next tick, clearing the side countdown.
- **`Mech_BehaviourSleepThink` (`00016248`)**, `sleeping`: releases the target and `Mech_LocomotionTick`(machine, 0, 0, 0); returns 0. ES2's ([`sleeping`](../../retail/simulation/ai-combat-states.md#sleeping-13--mech_behavioursleepthink-0041c418)) also runs `Ai_UpdateWeaponsFree`.
- **`Mech_BehaviourInertThink` (`00018308`)**, `dead` and `disabled`: ES2's ([the out-of-action states](../../retail/simulation/ai-combat-states.md#dead-20-disabled-21-and-in-limbo-19--mech_behaviourinertthink-0041e554)).

### Ramming

**`Mech_BehaviourRamThink` (`00018320`)** is ES2's ([`ramming`](../../retail/simulation/ai-combat-states.md#ramming-17--mech_behaviourramthink-0041e570)) on the retarget countdown `+0x55` (handle `+0x54`, 10000, `Ai_SelectTarget` mask 6), the phase flag `+0x59` and the phase countdown `+0x5c` (handle `+0x5b`, 3000 + rand(1500) and 8000 + rand(4000)), with a steer of the bearing error >> 4 where ES2's shifts by 8, and no test for a lost target between the charge's steps.

**`Mech_BehaviourRamTick` (`00018278`)**, its move: `Mech_IntegrateMotion`, `Mech_PlaceLegsOnGround` and, when `Mech_CollisionTest` refuses or `+0xab` is set, `Damage_ExplosiveBlastSweep` (`0001f75c`, [blasts](world.md#blasts--damage_explosiveblastsweep-0001f75c))(2000, 3000, its position), then `Mech_ComponentDamageWrite`(machine, machine, 32000, *i*) for each of the 28 components whose occupancy word at `+0x1a4` is not 0; it returns 1. ES2's ([the charge](../../retail/simulation/ai-combat-states.md#the-charge--mech_behaviourramtick-0041e488)) first sets the height from the terrain and excludes the rammer from the blast; ES1's blast spares none in range.

### Skirting

**`Mech_AiOnLineOfFireBlocked` (`00017a80`)**, slot `+0x68`, (machine, object): stores the object at `+0x246` and sets `+0xa7`. [The shot sweep](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0) passes the blocker's `+6`, so `+0x246` + `0x1a` is the blocker's position. ES2's ([how it is reached](../../retail/simulation/ai-combat-states.md#how-it-is-reached)) copies the target's position.

**`Ai_BeginSkirtIfBlocked` (`00017bf8`)**, (machine): with `+0xa7` set, installs `skirting`, stashes the old descriptor at `+0x54` and the 3D distance to the target at `+0x61`, and returns 1. ES2's stores the distance to the blocked point.

**`Mech_BehaviourSkirtThink` (`00017aa4`)**, `skirting`: on its first tick (`+0x58` clear) it sets `+0x5a` to whether the bearing to the blocker less the heading is negative, `+0x58` and the countdown `+0x5d` to 5000 (handle `+0x5c`). When the countdown runs out it is set to 5000 again and, when `Ai_LineOfSightBlocked` answers 0, the stashed descriptor is installed and `+0xa7` cleared. Every tick it walks at `0x100` toward the point `+0x61` from the blocker at the bearing from the blocker to the machine ±`0x4000`, + for a clear `+0x5a`, and runs `Mech_CenterTorsoTick`(machine, 0); it returns 0. ES2's ([the state](../../retail/simulation/ai-combat-states.md#the-state)) circles the stashed point, at a range it renews on each blocked test, and walks straight on a reading of 2.

**`Ai_LineOfSightBlocked` (`00017a00`)**, (machine): 1 when `000038b8`(the machine's group's side byte, 0, the target's position, the machine's, result) or `Sim_RaycastShapes` (`00020b70`, [the zone shapes](terrain.md#the-tests-against-the-shapes))(0, the target's position, the machine's, result) answers non-zero, otherwise 0. ES2's ([line of sight](../../retail/simulation/ai-combat-states.md#line-of-sight--ai_lineofsightblocked-0041dc24)) lifts both ends to their aim nodes, ignores a hit on the target, and walks the terrain for its answer of 2.

## Targets and orders

- **`Mech_AiEngageOrderedTarget` (`00015edc`)**, (machine, target): makes the target the machine's own; a class-1 target installs `attacking base`, class 0 sets `+0xa6` and runs `Mech_AiEnterCombat`, class 2 installs `attacking flyer`, and any other class nothing. ES2's ([the AI writers](../../retail/simulation/ai-targeting.md#the-ai-writers-of-0x1a4)) sends class 3 to `attacking base`. `es2_xref.py` finds five calls, all in `0001a1cc`.
- **`Ai_ShouldAbandonTarget` (`000162b4`)**, (machine): ES2's ([abandoning a target](../../retail/simulation/ai-targeting.md#abandoning-a-target--ai_shouldabandontarget-0041c4a8)) on the squad verb 4, its target `+0x1dc` and threshold `+0x1e4`, with `Ai_TargetStateTier` (`0000d880`); a target the group's order names is abandoned when destroyed on either side, where ES2's human side also abandons one immobilized. `es2_xref.py` finds four calls: `attacking`, `facing off`, `flanking` and `driving off en`.
- **`Mech_GetGroupSide` (`00016388`)**, (machine): the side byte `+0x14` of the machine's group, sign-extended. `es2_xref.py` finds no branch or stored pointer to it ([Open](#open)).

## Taking fire — `Mech_AiOnTakingFire` (`00019338`)

Slot `+0x54`, (machine, damage, attacker): ES2's ([taking fire](../../retail/simulation/ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50)) gates on ES1's fields: the complaint when the attacker is `PlayerMech` (`000bc42a`) and in the machine's group; nothing for an attacker on the machine's own side (ES2's also returns for an attacker out of action); `Mech_ShareContact`, the under-fire window `+0x213` at 30000, the player's group accumulating `+0x217` up to 8000, the returns for the player's machine, the retarget countdown `+0x209` and a committed machine's own target, and the countdown set to 10000. The responses:

- **Not committed, in a group the player leads**: squad message 3 for a HERC attacker, and `+0x94` cleared when the attacker is the leader's target, as ES2's.
- **Flag bit 2**, holding a place: in a group the player leads it sets the squad verb `+0x1d2` to 4 with the target `+0x1dc` 0; in any group it zeroes the dwell countdown `+0x4c`, so the reassess runs on the next tick. ES2's picks the defender with `Ai_SelectDefenceTarget` and installs `driving off en`.
- **Flag bit 3**: nothing.
- **Otherwise**: `Ai_SelectTarget`(machine, 0), mask 0 where ES2's passes `0x24`, with no squad-order substitution; none, when it held one, re-enters `Mech_AiSelectBehaviour`; one sets `+0xa6` and runs `Mech_AiEnterCombat`.

**`Mech_AiFriendlyFireComplaint` (`00019300`)**, (machine): ES2's: with the countdown `+0x20e` at 0, sets it to 40000 and posts `Ai_PostSquadMessage` (`0001a188`)(machine, 0, 8). `es2_xref.py` finds two calls, this function's and [the shot sweep's](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0).

## Navigation

- **`Ai_NavigationStep` (`00017478`)**, (machine): ES2's ([the four movement primitives](../../retail/simulation/ai-navigation.md#the-four-movement-primitives)), driving to the squad order's point `+0x1d4`, following the route for the group's first member (`+0xe`), keeping formation otherwise, then `Ai_UpdateWeaponsFree` and `Mech_CenterTorsoTick`(machine, 0). **`Ai_NavigationStepThunk` (`000174dc`)** calls it; `es2_xref.py` finds no branch or stored pointer to the thunk ([Open](#open)).
- **`Ai_UpdateWeaponsFree` (`000161dc`)**, (machine): the radar `+0x90` from `+0xac` when the group's first member is the player's machine, from `+0x91` otherwise, as ES2's.
- **`Ai_DriveToPoint` (`00019580`)**, (machine, point): ES2's ([drive to a point](../../retail/simulation/ai-navigation.md#drive-to-a-point--ai_drivetopoint-0041fac4)) on the cruise speed `+0x1e6` or `0xaa`, with no Turbo Pod sprint; it answers whether the point is within 10000.
- **`Ai_FollowRoute` (`00019604`)** is ES2's ([follow the route](../../retail/simulation/ai-navigation.md#follow-the-route--ai_followroute-0041fb60)) on the group's cursor at `+6`.
- **`Ai_KeepFormation` (`00019660`)** is ES2's ([keep formation](../../retail/simulation/ai-navigation.md#keep-formation--ai_keepformation-0041fbb8)) with `Mech_ApplyFormationOffset` (`00012ed0`) called directly, the leader's speed at `+0x224`, and the heading terms >> 4.
- **`Mech_AiGoalPosition` (`000179a0`)**, (machine): the squad order's point `+0x1d4` for verb 6, the position of `+0x1e0` for verb 3 and of `+0x1dc` for verb 5, otherwise `Group_OrderTargetPosition` (`0001cde0`). ES2's ([taking fire](../../retail/simulation/ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50)) takes verb 6's object when there is one.
- **`Mech_BehaviourPatrolThink` (`000176bc`)** and **`Mech_BehaviourSearchDestroyThink` (`000174ec`)** are ES2's ([`patrolling`](../../retail/simulation/ai-navigation.md#patrolling-8--mech_behaviourpatrolthink-0041d7d0), [`search/destroy`](../../retail/simulation/ai-navigation.md#searchdestroy-12--mech_behavioursearchdestroythink-0041d60c)) on the countdown `+0x55` (handle `+0x54`, 10000), with no FIRE AT WILL in the gate; the patrol acquires when its group's word `+4` is 8 or the squad verb is 2.
- **`Mech_BehaviourTravelThink` (`000178a4`)**, `travelling` and `bulldog travel`: ES2's ([`travelling`](../../retail/simulation/ai-navigation.md#travelling-9-and-bulldog-travel-11--mech_behaviourtravelthink-0041d9cc)) on the look-at `+0x59` and the radar setting `+0x91`.
- **`Mech_BehaviourGuardThink` (`00017ed4`)**, `guarding`: releases the target, then, while the countdown `+0x55` (handle `+0x54`) runs or for a machine with no squad verb in a group led by `PlayerMech`, runs `Ai_NavigationStep` and returns. Otherwise it takes the squad order's target for verb 4 with one, or scores each machine of `g_MechPool` that `Ai_IsTargetable` (`0000dce8`) passes and that is not immobilized as ES2's `Ai_SelectDefenceTarget` does ([taking fire](../../retail/simulation/ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50)), on ground distance to `Mech_AiGoalPosition` from a limit of 980000, or 945000 when the group has a target (`+0x1c`), setting the countdown to 10000. With nothing found it centers the turret, runs `Ai_UpdateWeaponsFree`, and drives to the post beyond 20000, or turns to face away from it, walking at `0x100` within 10000 and standing beyond. With a target it installs `fleeing` for a machine out of action, or `driving off en`, `Mech_AiSelectAimComponent` and the radar as ES2's ([`guarding`](../../retail/simulation/ai-navigation.md#guarding-15--mech_behaviourguardthink-0041e224)), which works its ring on every tick the countdown runs.

## Weapons

- **`Ai_AimAndFire` (`00018894`)**, (machine, target, aspect), the target 0 meaning `+0x15e`: a class-0 target goes to `Ai_AimAndFireAtMech`; any other is shot at its position plus the offset its slot `+0x34` gives. ES2's ([the tail](../../retail/simulation/ai-weapons.md#the-tail-every-combat-state-shares--ai_aimandfire-0041ea7c)) aims at a structure's first surviving component. `es2_xref.py` finds eight calls, and four dword occurrences of its padding entry `00018893`, at `00010eed`, `00012adb`, `00021e7b` and `0003317d` ([Open](#open)).
- **`Ai_AimAndFireAtMech` (`0001879c`)**, (machine, target, aspect): ES2's ([aiming at a machine](../../retail/simulation/ai-weapons.md#aiming-at-a-machine--ai_aimandfireatmech-0041e984)) on the twist limit `+0x22`, the aim slot `+0x238`, the target's slot `+0x5c` for the component and its slot `+0x28`, `Mech_GetAimNodeTransform`, for the aim node.
- **`Ai_FireAtPoint` (`000190ec`)**, (machine, target, aspect, point): ES2's ([the fire decision](../../retail/simulation/ai-weapons.md#the-fire-decision--ai_fireatpoint-0041f5a0)) on the latch `+0x242`, the selection skip `+0xaf`, the mount's slots `+0x30` (ready), `+0x64` (subtype) and `+0x2c` (fire, (mount, 0, machine)), the target's speed through its slot `+0x3c`, and the projectile record at the mount's `+0x20`. The scatter needs the group's side byte `+0x14` not 0 and comes from `AiAimScatterByDifficulty` (`00091ce0`, 1000, 666, 333, 0), indexed by header word 7 (`000bc4aa`), or 3 when `MissionModeFlag` (`000bc4a0`) is not 0, the index [`Damage_ScaleByDifficulty`](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c) takes. The latch keeps weapon index 3, `ELF` ([the templates](../formats/weapons-dat-sim.md)), where ES2's keeps 6 and 22.
- **`Ai_ChooseWeapon` (`00018e50`)**, (machine, target, range, aspect): ES2's ([choosing a weapon](../../retail/simulation/ai-weapons.md#choosing-a-weapon--ai_chooseweapon-0041f358)), the shield through the target's slot `+0x38` with the same boolean, the mounts' slots `+0x60` (spent) and `+0x64`, the lock flags at the manager's `+6`, the radar `+0x90` and its silence `+0x201`; running dry sets `+0x9f`, fires the action `+0x170` and raises `MissionPollTimer_Count` (`000af79d`) to 1000, as ES2's.
- **`Cockpit_TargetAnglesFromCameraBone` (`00018b78`)**, (machine, point): ES2's aim primitive ([aiming at a point](../../retail/simulation/torso-aim.md#aiming-at-a-point)) without the damping beyond 50000 and without the snap target, which ES1's turret ticks lack ([the turret](mech.md#the-turret)): the point in the frame of the eye node the type record's `+0xc` names, less `+0x66` in Z, `Math_EulerToward` into `TargetAngles_Residual` (`000af714`, pitch at `+0`, yaw at `+4`), each axis −Q8(*c*, |*c*|) for yaw and Q8(*c*, |*c*|) for pitch with *c* = clamp(Q10(250, residual), ±`0x100`), `Mech_TorsoTwistTick`(machine, twist axis) and `Mech_TorsoPitchTick`(machine, `Math_FastMagnitude3D` of the point, pitch axis); it returns `000af714`. `es2_xref.py` finds three calls: `Ai_FireAtPoint` twice and `0005a0c0`.
- **`Mech_CenterTorsoTick` (`000186f8`)**, (machine, range): ES2's ([centring](../../retail/simulation/torso-aim.md#centring--backspace)) without the snap: −clamp(Q10(250, angle), ±`0x100`) on the twist `+0x22e` and the pitch `+0x230`. `es2_xref.py` finds five calls: `Ai_NavigationStep`, `Mech_BehaviourSkirtThink`, `Mech_BehaviourGuardThink`, `Ai_AimAndFireAtMech` and `0005a0c0`.

## The player's think — `Mech_BehaviourPlayerThink` (`00015f80`)

The think of `player`, ES2's ([the player's think](../../retail/simulation/player-waypoints.md#the-players-think--mech_behaviourplayerthink-0041c194), [its objective arms](../../retail/simulation/mission-objectives.md#the-player-thinks-objective-arms)) for the group's first member, by script.dat header word 3 (`000bc4a2`) with arms 0, 5 and 3 where ES2's also gives 7 the data link: the waypoint message is computer message 0 (ES2's `0x1d`); arm 0 posts 5 (ES2's `0x19`) once, latched by `MissionTarget_Announced` (`000af70c`), and sets `+0x99`; arm 5 sets `+0x99` within 40000 of `Mech_AiGoalPosition`; arm 3's link needs the subject's group to hold no action (`+0x16`), steps `DataLink_Step` (`000af700`) through `DataLink_StepDelays` (`00091ce8`, 5000, 5000, −25536, 0) on `DataLink_Timer` (`000af704`), posts `0x1d` + the step (ES2's `0x34` +), sets `+0x9a` at step 4, and posts `0x21` (ES2's `0x38`) on breaking off. It returns 0.

## The fields

The fields this file adds to [the HERC's](mech.md#the-fields). The behavior block's scratch from `+0x54` is ES2's from `+0x5a` ([fields this layer owns](../../retail/simulation/ai-combat-states.md#fields-this-layer-owns)), each field 6 bytes lower.

| ES1 | ES2 | Field |
|---|---|---|
| `+0x91` | `+0x97` | the mission file's radar setting |
| `+0x94` | `+0x9a` | do not take the player's selection |
| `+0x99`, `+0x9a` | `+0x9f`, `+0xa0` | the player's objective latches |
| `+0xa6` | `+0xac` | a target was just handed over |
| `+0xa7` | `+0xad` | the line of fire is blocked |
| `+0xac` | `+0xb2` | a squadmate's radar is kept on |
| `+0xaf` | `+0xb5` | skip one weapon choice |
| `+0x1d2`, `+0x1d4`, `+0x1dc`, `+0x1e0`, `+0x1e4` | `+0x23e`, `+0x240`, `+0x248`, `+0x24c`, `+0x250` | the squad order: verb, point, target, second object, abandon threshold |
| `+0x1e6` | `+0x252` | the cruise speed |
| `+0x209`, `+0x20e`, `+0x213`, `+0x217` | `+0x273`, `+0x278`, `+0x27d`, `+0x281` | the retarget and complaint countdowns, the under-fire window, the damage from the player's group |
| `+0x234`, `+0x238`, `+0x240`, `+0x242` | `+0x29e`, `+0x2a2`, `+0x2aa`, `+0x2ac` | the combat rating, the aim slot, the fear, the latched mount |
| `+0x246` | `+0x31e` | the blocker's `+6`, where ES2 keeps the target's position |

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMechAiCopy`, `ConfigPart_DtorMechAiCopy`, `ConfigPart_GetClassTagMechAiCopy`, `TSPartBase_DtorMechAiCopy` | `00015e40`, `00015e68`, `00015ea7`, `00015eb0` | the run |
| `Mech_AiEngageOrderedTarget`, `Mech_BehaviourPlayerThink`, `Ai_UpdateWeaponsFree`, `Mech_BehaviourSleepThink` | `00015edc`, `00015f80`, `000161dc`, `00016248` | `0041c0f4`, `0041c194`, `0041c3c8`, `0041c418` |
| `Ai_ClearSquadEngageOrder`, `Ai_ShouldAbandonTarget`, `Mech_GetGroupSide` | `00016294`, `000162b4`, `00016388` | `0041c478`, `0041c4a8`, — |
| `Mech_BehaviourAttackThink`, `Ai_CircleStep`, `Mech_BehaviourAttackBaseThink`, `Mech_BehaviourAttackFlyerThink` | `000163a0`, `00016574`, `00016718`, `0001684c` | `0041c594`, `0041c72c`, `0041c86c`, `0041c9cc` |
| `Mech_CompareCombatRating`, `Ai_SumAttackerRatings`, `Mech_AiFleeCheck`, `Mech_AiSelectAimComponent` | `00016968`, `00016a14`, `00016a74`, `00016cf4` | `0041cabc`, `0041cb44`, `0041cb94`, `0041ce08` |
| `Mech_AiCombatReassess`, `Mech_BehaviourFleeThink`, `Mech_BehaviourFaceOffThink`, `Mech_BehaviourFlankThink` | `00016e04`, `0001713c`, `000172f0`, `000173c8` | `0041cf18`, `0041d2c4`, `0041d41c`, `0041d4e4` |
| `Ai_NavigationStep`, `Mech_AiEnterCombat`, `Ai_NavigationStepThunk` | `00017478`, `000174cc`, `000174dc` | `0041d598`, `0041d5ec`, `0041d5fc` |
| `Mech_BehaviourSearchDestroyThink`, `Mech_BehaviourPatrolThink`, `Mech_BehaviourTravelThink`, `Mech_AiGoalPosition` | `000174ec`, `000176bc`, `000178a4`, `000179a0` | `0041d60c`, `0041d7d0`, `0041d9cc`, `0041dbcc` |
| `Ai_LineOfSightBlocked`, `Mech_AiOnLineOfFireBlocked`, `Mech_BehaviourSkirtThink`, `Ai_BeginSkirtIfBlocked` | `00017a00`, `00017a80`, `00017aa4`, `00017bf8` | `0041dc24`, `0041dd2c`, `0041dd64`, `0041de9c` |
| `Mech_BehaviourDriveOffThink`, `Mech_BehaviourGuardThink` | `00017c84`, `00017ed4` | `0041def0`, `0041e224` with `Ai_SelectDefenceTarget` (`0041e0e0`) |
| `Mech_BehaviourRamTick`, `Mech_BehaviourInertThink`, `Mech_BehaviourRamThink` | `00018278`, `00018308`, `00018320` | `0041e488`, `0041e554`, `0041e570` |
| `Ai_BuildCombatGeometry`, `Ai_CombatMoveStep`, `Mech_CenterTorsoTick`, `Ai_AimAndFireAtMech`, `Ai_AimAndFire` | `0001851c`, `00018618`, `000186f8`, `0001879c`, `00018894` | `0041e758`, `0041e828`, `0041e8d4`, `0041e984`, `0041ea7c` |
| `Mech_AiSelectBehaviour`, `Mech_ComputeCombatRating`, `Cockpit_TargetAnglesFromCameraBone` | `0001892c`, `000189f0`, `00018b78` | `0041eb34`, `0041edd8`, `0041ef14` |
| `Ai_ChooseWeapon`, `Ai_FireAtPoint`, `Mech_AiFriendlyFireComplaint`, `Mech_AiOnTakingFire` | `00018e50`, `000190ec`, `00019300`, `00019338` | `0041f358`, `0041f5a0`, `0041f790`, `0041f7b8` |
| `Ai_DriveToPoint`, `Ai_FollowRoute`, `Ai_KeepFormation`, `MechAi_StaticInitNoOp` | `00019580`, `00019604`, `00019660`, `00019850` | `0041fac4`, `0041fb60`, `0041fbb8`, `0041fdac` |

Callees of other files read for them: `Component_ReadDamagePercent` (`0000a16c`), `Math_OffsetPointByBearing` (`00063d8c`), `Timer_CountDown` (`00064178`) and `Math_MapRange` (`00065480`), ES2's `0040dbc0`, `004928f0`, `004679a4` and `0047de3c`; `Math_MapRange` keeps the C convention and ES2's argument order.

## Globals

| | Address |
|---|---|
| `Mech_AiSelectBehaviourVerbTable`, 12 targets | `00091d14` |
| `AiFlee_InternalComponents`, `AiFlee_BandPenalties`, `AiFlee_BandThresholds`, `int16` | `00091cf8`, `00091d04`, `00091d0c` |
| `AiAimScatterByDifficulty`, 4 `int16` | `00091ce0` |
| `TargetAngles_CalledOnce`, `TargetAngles_Residual` | `000af710`, `000af714` |
| `MissionTarget_Announced`, `DataLink_Step`, `DataLink_Timer`, `DataLink_StepDelays` | `000af70c`, `000af700`, `000af704`, `00091ce8` |
| `SimTickDelta`, `int16`, the tick's length that `Timer_CountDown` subtracts | `000bdbd0` |

## Open

- **Open:** what `EDI` holds when `Mech_AiSelectBehaviour` is reached with a group word it has no case for (0, 2, 4, 5, 9, or above 11), through `Behaviour_DispatchReassess` and its six direct callers; and which group words the retail missions' `.MSN` group `0x32` carries.
- **Open:** `000038b8`, the structure test `Ai_LineOfSightBlocked` calls first with the group's side byte, unread here ([the HERC's Open](mech.md#open) lists it).
- **Open:** what reaches `HeapPtr_DtorMechAiCopy`, `Mech_GetGroupSide` and `Ai_NavigationStepThunk`, to which `es2_xref.py` finds no branch or stored pointer; and the four dword occurrences of `Ai_AimAndFire`'s padding entry, which may be instruction bytes.
- **Open:** the callers `es2_xref.py` finds and this batch did not read: `0001a1cc` (the squad order, slot `+0x2c`), `0005a0c0` (the player's turret input).
- **Open:** whether the out-of-action machine's loop through `fleeing` and the combat reassess every tick shows in play. A retail check: disarm an enemy HERC (shoot off its weapons) and watch whether it runs off steadily or jitters from side to side.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: put a guard group's enemy in sight and watch it walk out toward the enemy rather than standing where it was (`driving off en`); or `BP <CS>:00017c84` fires once a tick for such a machine.
