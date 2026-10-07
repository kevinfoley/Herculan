# DBSIM.EXE component damage — the damage arrays, cascade, and going out of the fight

Reverse-engineered from `DBSIM.EXE` disassembly (Ghidra project `ES2Recon`). All addresses are DBSIM.EXE virtual addresses. This is the shared endpoint both damage pathways in [`damage-system.md`](damage-system.md) write into — see that doc for how a shot or a blast decides how much damage arrives and at which component. The separate weapon-mount destruction roll is in [`weapon-mounts.md`](weapon-mounts.md#the-chance-path--the-destruction-roll), and `PROJ.DAT`'s per-weapon damage figures are in [`../formats/proj-dat.md`](../formats/proj-dat.md). The `.DMG` file that supplies every component's armour, parent and internals is [`../formats/dmg-damage-file.md`](../formats/dmg-damage-file.md).

## The component damage system

**Flyers have one too.** `Flyer_Constructor` (`004215f4`) allocates the same header at `flyer+0x200` with literal counts of **1 and 1** — one main component, one dependent — which is exactly what `SKIMMER.DMG` ships. The counts are hard-coded at each constructor, not read from the file.

**`this+0x206` is a header of pointers, not inline arrays.** Allocator `Component_AllocDamageArrays` (`0040d2cc`), called as `Component_AllocDamageArrays(this+0x206, 0x1d /*29*/, 0x16 /*22*/)`:

| Offset (abs) | Field |
|---|---|
| `+0x206` | **pointer** to a 22-`short` dependent-subpiece **damage** array, zeroed = undamaged |
| `+0x20a` | **pointer** to a 29-`short` main-component **damage** array, zeroed = undamaged |
| `+0x20e` | **pointer** to a 29-`short` active/occupancy-flag array, all bytes `0x01` at init, then cleared by `Mech_ConfigureLoadout` for each component with no internal |
| `+0x212` | **pointer** to the loaded `.DMG` table: the piece array at `[+0]`, the internals' maxima at `[+4]` ([layout](../formats/dmg-damage-file.md#layout)) |
| `+0x216` | **pointer** to the object's collision registration record from `Collision_RegisterObject` (`0040cd88`), tying this system to the bounding-sphere tree in [`../formats/collision-spheres.md`](../formats/collision-spheres.md) |
| `+0x21a` | the owning object |
| `+0x21e` | `short` count = 29 |
| `+0x220` | `short` count = 22 |

`Component_LinkMaxRefData(this+0x206, mechThis, damageDataPtr, collisionRegistration)` (`0040d354`), a second constructor call, fills in the last three pointers. Every accessor (`Component_ReadDamagePercent`/`Component_ApplyDamageAndCascade`/`Mech_ComponentDamageWrite`/`Mech_ComputeShieldCapacity`/…) treats `this+0x206` as `(int*)` and does an extra pointer dereference before indexing.

The 22-entry array holds damage on the internals (the aggregation is below) and the 29-entry array damage on the components, the indexing space both damage pathways' component selection uses; both index spaces are [the file's](../formats/dmg-damage-file.md#the-two-index-spaces). The flag array is an **occupancy flag per component**, not a second depleting health pool: `Component_DestroyAndCascade` clears a slot's when the component goes, and every write into a slot whose flag is clear is refused. A machine also starts with some flags clear. `Mech_ConfigureLoadout` (`004175dc`), after fitting the weapons, clears the flag of each of the first 29 components whose `.DMG` piece lists no internal (`piece+0x06 == 0`), which takes out every empty hardpoint and the slots each chassis does not use — [which those are](../formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece). The hit test skips a cleared slot's clusters, the component pickers in [`target-selection.md`](target-selection.md) and [`ai-targeting.md`](ai-targeting.md) step over it, and a write into it is refused, as for a destroyed one.

**Read: `Component_ReadDamagePercent` (`0040dbc0`) — accumulated damage as Q8 (0–256), 0 = pristine, 256 = destroyed.** Note the sense: it returns damage, not health, so every caller's curve runs the opposite way to how a `…HealthPercent` name would suggest. Looks up the component's max-reference record (18 bytes, via `this+0x212`), starts with its own damage (main 29-entry array) and max values, then **aggregates in every dependent sub-component** listed in that record (walking a list, adding each dependent's damage from the 22-entry array and max from a parallel max-side array) before computing `(totalDamage << 8) / totalMax`. An entry holding `-1` (destroyed) substitutes its max, so it reads as fully damaged. A leg piece therefore reads as its own armour plus its side's leg servos, and a cockpit as its own armour plus the systems behind it — [which internals each component holds](../formats/dmg-damage-file.md#which-internals-each-component-holds).

**Write and cascade: `Component_ApplyDamageAndCascade` (`0040da38`)**, called from `Mech_ComponentDamageWrite` (`00417de4`, mech vtable `+0x74`) and `Flyer_ComponentDamageWrite` (`00421bb4`, the flyer's) — the shared endpoint both damage pathways call into.

`Mech_ComponentDamageWrite` returns before doing any of this in two cases. The first is its own first line, `if (obj+0xa3 && Sim_DamageToPlayerDisabled()) return` — the mission's invulnerability setting ([`difficulty.md`](difficulty.md#the-two-sibling-cheats)). The second is a component whose active flag is already clear: a destroyed component is written to no more, and the mount snapshot, the death gate and every warning below are skipped with it. Shields are outside both, since they are spent in the pathways above.

```
destroyed = Component_AddDamage(&mainDamage[i], piece.Armor, &damage)   // 0040d3ec
if (destroyed) {
    drained = Component_SpillIntoDependents(piece, subDamage, damage, subMax)   // 0040cf44
    if (drained && (piece.DestructionFlags & 1)) {
        Component_DestroyAndCascade(i)                     // 0040d434
        drain the pending parent-index queue through the same call
    }
}
```

- `Component_AddDamage` **adds** damage, stores `-1` rather than the max once the entry is finished, and **writes the excess back into `damage`**. An entry already at `-1` absorbs nothing, so a lost part cannot be shot again.
- `Component_SpillIntoDependents` pours that excess into the component's dependents, **one at a time, weighted and random**: each live dependent contributes its [spill weight](../formats/dmg-damage-file.md#the-piece-record) to a total, a draw under that total picks the one that takes the hit, and if that spill destroys it the remainder goes round again. It returns true only once no live dependents are left — which is why a component with internals still intact does not cascade even after its own armour is gone.
- `Component_DestroyAndCascade` writes `-1`, clears the active flag, finishes off everything under it with a flat 32000, and queues every live piece whose parent index (`.DMG` `+0x04`) names this component. The original drains that queue iteratively rather than recursing.
- `Component_IsFullyDestroyed` (`0040d9f8`) ("is component *i* destroyed **and** all of its dependents too", via `Component_AllDependentsDestroyed` (`0040cf10`)) is the stricter test the mech's death gate asks of its two cockpit slots.

### Slots the write path reads by index

The names of the slots are [the file's](../formats/dmg-damage-file.md#the-two-index-spaces); the retail values behind them are in [which internals each component holds](../formats/dmg-damage-file.md#which-internals-each-component-holds).

- **Components 0–1, the cockpits** — individually checked (`Component_IsFullyDestroyed`) as the mech's death-trigger gate.
- **Components 19–28, the weapon mounts** — `Mech_ComponentDamageWrite` snapshots each mount's reading, `Component_ReadDamagePercent(.GL +0x17 + 19)`, before and after the write, and `Mech_SalvageValue` reads the same ([the destruction roll](weapon-mounts.md#the-chance-path--the-destruction-roll)). The brackets, 4–5, are structure the mounts hang off through the parent index; a mount's runtime ammo and heat are not in this array.
- **Internals (22-entry) read by literal index in `Mech_ComponentDamageWrite`**, not by a loop. 0 and 1 are the front leg servos, joined by 10 and 11 (the rear pair) when `typeRecord+0x4a` is 4; the pair(s) are averaged before being compared against `0x8d` (crippled) and `0x50` (the milder grade), and half of them destroyed immobilises the machine. 4 is the shield generator, which `Mech_ComputeShieldCapacity` reads — so shooting it shrinks the array the machine can hold, and that recompute happens **here as well as at spawn**. 5 is the reactor, latching the two output-damage flags. 8 and 9 are life support and the pilot: either destroyed, or either cockpit slot fully gone, and the machine dies.

### What the endpoint announces

`Mech_ComponentDamageWrite` is also where the cockpit computer's damage warnings are posted, and **every one of them is gated on `obj+0xa3`** — the machine being the one the player is flying — so an AI machine losing a leg says nothing. The ids are `SYSTEM.STR`'s and the port they go to is [`../formats/cockpit-messages.md`](../formats/cockpit-messages.md#the-port)'s.

| id | line | guard |
|---|---|---|
| `0x0c` | `SHIELD GENERATOR DESTROYED` | dependent 4's reading was under `0x100` before the write and is `0x100` after |
| `0x03` | `INTERNAL DAMAGE: SHIELD GENERATOR` | the reading was 0 before the write and is not after, **and the test above did not fire** — it is that test's `else` arm, so a hit that takes an untouched generator out posts `0x0c` alone |
| `0x10` | `WEAPON DESTROYED` | a mount's own component was under `0x100` before the write and is `0x100` after. **Once per write, not once per mount** — the walk over the mounts raises a flag and the post comes after it, so a cascade that strips several hardpoints says it once |
| `0x08` | `INTERNAL DAMAGE: LEG SERVOS` | fewer than half the servos gone, both graded sides under `0x8d`, one of them over `0x50`, and `mech+0xa8` clear |
| `0x13` | `STRUCTURAL FAILURE IMMINENT` | the same with a side at or past `0x8d`, on `mech+0xa9` |
| `0x04` | `INTERNAL DAMAGE: ENGINE` | the reactor grade crossing either band. Two call sites, one per latch — `mech+0xaa` for `0x81`-`0xc0`, `mech+0xab` past `0xc0` — posting the same line; but the grade is only read while **both** latches are clear, so a machine announces its reactor once however far it goes on degrading |
| `0x2e` | `ENEMY TARGET DESTROYED` | the death gate, on the shared predicate below |
| `0x2f` | `ENEMY TARGET DISABLED` | the leg branch's immobilise, on that same predicate |

`0x15` `SHIELDS CRITICAL` belongs to the same family from one function further out: `Mech_DirectFireHitTest` posts it where it sets `mech+0xb0` (`00418dc7`), on the first shot to land on the player's own machine with under 500 points of charge left across both facings.

**Four of the five latch bytes are one-shots that are never cleared** — `+0xa8`/`+0xa9`/`+0xaa`/`+0xab`, each written `1` exactly once, in `Mech_ComponentDamageWrite`. They are why a machine that keeps taking hits in the same band does not repeat itself, and they are separate from `MessagePort_Show`'s own 4.8 s swallow of a repeated id ([`../formats/cockpit-messages.md`](../formats/cockpit-messages.md)), which would not be enough on its own. All four are load-bearing elsewhere as well: `+0xa8`/`+0xa9` are the two speed penalties ([`mech-locomotion.md`](mech-locomotion.md)) and `+0xaa`/`+0xab` the reactor's grades, which cut speed too; the reactor output they also grade is fixed before either can be set ([`reactor-energy-pool.md`](reactor-energy-pool.md#reactor-output-rate--mech_computereactorrate-00417d08)).

**`+0xb0` is the exception: it re-arms.** `Mech_PerTickSystemsUpdate` clears it (`0041ab25`) on the player's own machine every tick that `front + rear` exceeds `0x5dc` (1500), so `SHIELDS CRITICAL` is hysteretic — it fires under 500 and can fire again once the array has rebuilt past 1500, with the band between the two thresholds leaving the latch as it was. The same byte is what the MFD status screen reads for its `SHIELDS DN` condition, so that indicator clears itself on the same threshold.

**`0x2e` and `0x2f` share a predicate, and it does not test sides**: the attacker is the machine the player is flying, and the victim is that machine's own selected target (`mech+0x1a4`). Nothing is asked about whose side the victim was on. `0x2e` has **three** call sites — this endpoint, the flyer's `+0x74` (`Flyer_ComponentDamageWrite`) and `Base_ApplyDamage` (`00404d70`) — so it covers a HERC, an aircraft and a building alike. What the missing side test costs is in [`../../../KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

### Going out of the fight

Two independent branches, and they are **not** two readings of one condition. Losing legs disables; losing the cockpit, the pilot or life support kills.

**Disabled** — the leg branch, non-flyers only. A leg whose servos read fully destroyed has its shadow deleted ([`ground-shapes.md`](ground-shapes.md#a-hercs-shadows)), so `Mech_PlaceLegsOnGround` skips it and it plants no more footfalls; that runs whatever else is true of the machine. Then, if it is not already immobilised and half or more of its legs are gone:

1. the attacker is told, through *its own* vtable `+0x60` ([below](#what-the-attacker-is-told--mech_creditneutralisedtarget-00415710)), with "was already immobilised" clear;
2. the machine's [out-of-action report](mission-deployment.md#the-out-of-action-report) runs, then its own defeat action fires;
3. `disabled` (21) is installed;
4. `mech+0xa4` immobilised is latched and the target released.

Steps 1–3 run only while `+0x99` is clear; step 4 runs regardless.

**Dead** — the cockpit/pilot/life-support gate. `mech+0x99` is set, then, in this order:

1. **the reading of `+0xa4` is sampled**, because the next step invalidates it;
2. the recursive finish-off, a flat 30000 on component 0 with no attacker, which is why a kill leaves a machine comprehensively wrecked rather than merely stopped — and which re-enters this whole function, where `+0x99` being set keeps the death gate and the leg branch's report from running a second time;
3. the attacker is told, carrying that sampled reading;
4. **the [out-of-action report](mission-deployment.md#the-out-of-action-report) and the defeat action run only if the machine was not already immobilised**, so they go off once per machine rather than once per way of stopping it;
5. target released, scanner forced passive;
6. the state: `in limbo` (19) when the chassis' `typeRecord+0x4c` is set, otherwise `dead` (20) for a non-flyer. **A flyer takes neither**, and keeps whatever state it was in.

`typeRecord+0x4c` means *this chassis leaves no wreck*, and the SPIDER is the only one that sets it: that branch also sinks the object to z = -100000, raises `obj+0x38` (a structure's no-wreck branch does the same at `004039a1`, a machine's at `004185ec` and a flyer's at `00421c36`, into the 8 bytes `SimObjectBase_Constructor` zeroes at `00402250`; [Open](#open)), and hands every ground shape in `mech+0x238` to `ObjectPool_QueueForDelete` (`00418634`), nulling each slot and zeroing the count at `mech+0x23c` — none, on the SPIDER, whose part list is empty. The machine itself is not queued — the branch takes it off the screen by sinking it, not by removing it from the object list.

The three state indices and their think are [`ai-combat-states.md`](ai-combat-states.md)'s. What a machine does *after* the state is installed — the fall, and the collapse that ends it — is [`mech-locomotion.md`](mech-locomotion.md#going-down)'s.

### The three out-of-the-fight bytes — `+0x99`, `+0xa4`, `+0xa5`

Which tests read which of them is the last paragraph below. They are **three different conditions, not three damage latches**, and each has its own writers:

| Byte | Condition | Written by |
|---|---|---|
| `+0x99` | **Destroyed** | The three classes' damage-write paths, and nothing else: `Mech_ComponentDamageWrite` (`00417de4`) when a core component reaches full damage, `Flyer_ComponentDamageWrite`, `Base_ApplyDamage`. `Base_Construct` also sets it for a structure spawned already destroyed |
| `+0xa4` | **Immobilised** — cannot move under its own power | `Mech_ComponentDamageWrite` when half or more legs reach full damage; `Razor_MovementTick` (`004198f4`) when the airframe loses its nose or belly |
| `+0xa5` | **Disarmed** — has nothing left to fight with | `Ai_ChooseWeapon` (`0041f358`) the first time it walks a machine's whole mount list and finds every mount absent or spent — destroyed, out of rounds, or a pod ([`ai-weapons.md`](ai-weapons.md#running-dry--mech0xa5)); `Base_Construct` at spawn, for a structure type that has no weapons ([`structure-behaviour.md`](structure-behaviour.md#five-classes-one-switch)); `Base_TransportThinkTick` (`004045c8`) once every component but the first is at full damage ([`structure-behaviour.md`](structure-behaviour.md#the-transport--004045c8)); `Flyer_AiSelectBehaviour` for a flight ordered to sleep or travel ([`ai-flyers.md`](ai-flyers.md#orders--flyer_aiselectbehaviour-00422d00)) |

None of the three means "removed from the simulation". Which subset a test reads is the behaviour: the detection sweep, the player's target selection and `Group_ConditionTier` read `+0x99` and `+0xa4` only; the AI's own tests add `+0xa5`, which is why a disarmed machine flees and is abandoned as a target while remaining a legal player target, and why an unarmed building is the AI's last choice of target. `Sim_RaycastObjectList`'s gate on the struck object's `+0x50` slot, `Mission_IsClearOfThreats`, `Mech_ApplyOutnumberedDamage` and `Group_IsWipedOut` read all three as well; the last one's name overstates what it asks — [`ai-goals.md`](ai-goals.md#no-rival-group-is-still-working-to-it--group_norivalorderonsubject-00412e74).

### What the attacker is told — `Mech_CreditNeutralisedTarget` (`00415710`)

Mech vtable `+0x60`, called on the machine that put the victim out of the fight, from both branches above and only when the write named an attacker. It is `void __cdecl(SimObject *attacker, SimObject *victim, short victimAlreadyImmobilised)` — plain `__cdecl` on three stack arguments, whatever the decompiler's `__thiscall` rendering of the vtable slot says; all four call sites push three and clean 12 bytes. The base class' slot is an empty stub, so only a HERC credits anything.

`victimAlreadyImmobilised` suppresses the kill half only. Unless it is set, and the victim is on the other side from the attacker:

- the attacker's tally at `mech+0x2a4` for the victim's target class goes up one, which is [what the debrief reports](mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c), and `mech+0xa6` is latched — a byte nothing live reads ([`mission-objectives.md`](mission-objectives.md#the-group-report-and-why-nothing-shows-it));
- when the attacker is a squadmate of the player's, it also calls out `0x02`, the kill line.

Either way, when the victim is a squadmate of the player's, it cries out — `0x25` if destroyed, `0x04` if only stopped, the image's one squad post that is forced past a destroyed machine ([`../formats/cockpit-messages.md`](../formats/cockpit-messages.md)) — and, if the player was the attacker, [mission counter](mission-deployment.md#the-mission-counters--dat_004a9ef4) 10 goes up one.

### Spread impact damage — `Mech_SpreadImpactDamage` (`00417a04`)

One impact spread over the whole machine, rather than a shot aimed at a component. Every live component draws its own roll out of 256 against `odds`; one that is caught takes `Q8(rand(maxDamage) + maxDamage/2, totalArmor)` — so the share scales with what that component had to lose. `totalArmor` is `Component_TotalArmor` (`0040dc58`), a component's own armour plus the maximum of every internal mapped onto it, which is also the denominator the damage percentage uses. A `maxDamage` of zero returns immediately.

The damage goes in through this same `+0x74` endpoint, cascade and death gate included. Two callers: the collapse landing ([`mech-locomotion.md`](mech-locomotion.md#going-down)) and the starting condition below.

### Starting condition — `Mech_ApplyStartingCondition` (`004178e8`)

Called once from `DBSim_SpawnMissionObjects`, immediately after the machine's two mission actions are resolved, with the percentage at [`../formats/script-dat.md`](../formats/script-dat.md)'s block 7 `0x84`. The odds are always the damage figure plus 25.

| Condition | Effect |
|---|---|
| ≥ 80, or negative | untouched |
| 60–79 | 50 damage at 75 |
| 40–59 | 80 at 105 |
| 20–39 | 120 at 145, the **reactor dependent is set to its own maximum** — written off outright rather than damaged toward it — and `+0xb3` is raised |
| < 20 | a **wreck**: one of components 7/8 destroyed with 32000 (and one of 13/14 on a four-legged chassis), `+0xa4` immobilised, `+0xb3` raised and `+0xb4` collapsed, then 150 at 175 over the rest |

The order matters: writing a leg off can fire the death gate, so the defeat action has to be attached first. The wreck grade places a derelict as scenery — already down, so it never falls, and never targetable.

**`+0xb3` is *worth no salvage*** — below.

### A squad machine's condition — `Mech_ApplySquadCondition` (`00415068`)

A machine of the player's squad takes no starting condition. `DBSim_SpawnMissionObjects` hands its damage arrays the three condition spans of its `player.mec` entry instead ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4)), right after `Mech_ConfigureLoadout`, so it starts the mission in the condition the career holds for it. Each entry, 0–100, becomes `(100 - condition) * maximum / 100` damage:

| Slots written | From |
|---|---|
| components 0–12 | the 13 external conditions, against each piece's armour |
| components 13–18 | zeroed |
| internals 0–8 | the first nine internal conditions, against each internal's maximum |
| internal 9, the pilot, and 10–11 | zeroed. The internal span's tenth entry is the machine's overall condition ([`../formats/save-games.md`](../formats/save-games.md#the-66-byte-status-block)) and is not read |
| components 19–28, the mounts | the ten hardpoint conditions, against the mount's armour |

A mount's share is taken against its armour plus the maximum of internal 12 + *n* behind it, with anything past the armour going to that internal; every one of those internals has a maximum of 0 in retail ([`../formats/dmg-damage-file.md`](../formats/dmg-damage-file.md#layout)), so the mount simply takes its share of its own armour. Components 13–18 and internals 10–11 are the rear legs, which none of the nine chassis a squad can field has; zeroing internal 9 starts the pilot unhurt.

The arrays are written directly, not through `Mech_ComponentDamageWrite`, so nothing reacts to the condition at spawn. The shield array `Mech_ConfigureLoadout` has just sized for a pristine machine stands until the first write recomputes it ([`damage-system.md`](damage-system.md#the-shield-system)), the reactor keeps its pristine rate for the mission ([`reactor-energy-pool.md`](reactor-energy-pool.md#reactor-output-rate--mech_computereactorrate-00417d08)), and the speed latches stay clear until a write grades the legs and the reactor, so a squad machine with worn servos walks at full speed until it is next hit.

### What a wreck is worth — `Mech_SalvageValue` (`00418e60`)

What the player's side takes home. `Mission_TotalSalvage` (`00423e88`) sums this function at the end of the run over every machine that is on the other side from the player's and is destroyed (`+0x99`) or immobilised (`+0xa4`); what the total becomes is [`mission-objectives.md`](mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)'s.

Per machine, in order:

1. **`mech+0xb3` short-circuits it to zero.** A machine the mission placed already broken — the two worst starting-condition grades above — is worth nothing, so a mission cannot be farmed by authoring derelicts into it.
2. **Each surviving hardpoint is queued.** For every mount whose component is under `0x80` damage, `Salvage_QueueWeapon` (`00426ac8`) takes `{template+0x56, (0x100 - damage) * 100 >> 8}` — the weapon's catalog id and its condition as a percentage. This is the same list the mount-destruction path appends to ([the destruction roll](weapon-mounts.md#the-chance-path--the-destruction-roll)), and the results carry it to the shell as salvage pairs. The list is `Mem_NewArray(200)` (`004773e4`, at `0042530d`), room for 50 four-byte pairs, and the append checks nothing. `Mem_NewArray` allocates 8 bytes more than it is asked for, so pairs 51 and 52 land in that slack; pair 53 is the first that can write past the block ([Open](#open)). Campaign missions field far more mounts than that on the side opposite the player's: 18 of the 50 can load with more than 52, and `C4_07.MSN` with up to 99, so a player who puts most of such a mission's opposition out of the fight without stripping its guns runs the list past its end.
3. **The chassis itself** is `Q10(Mech_WeightedArmorRemaining(mech), typeRec+0x54)`, and `typeRec+0x54` is **halved when component 0 is at full damage** — a chassis blown apart is worth half one merely stopped. `Mech_WeightedArmorRemaining` (`0041537c`) sums `(maxArmor - damage) * weight / maxArmor` over the live components, against the weight table at `00499fe0`; `maxArmor` is the component's own `.DMG` record and a component at or past 150 damage contributes nothing.

So a machine pays for what survived, not for what was wrecked, and its guns pay separately by how intact each one is.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `+0xa4` is "removed" and `+0xa5` is "destroyed" | `+0xa4` is written where a machine loses its legs and a RAZOR loses its fuselage, and the flyer's position integration refuses to run while it is set — it is *immobilised*. `+0xa5` is written by the weapon chooser and by `Base_Construct` for unarmed structure types — it is *disarmed*. `+0x99` is the one the damage paths write. |

## Open

- **Open:** what reads `obj+0x38`, the byte the no-wreck sink raises. `es2_fieldscan.py`, which resolves `LEA reg,[base + k]`, `ADD reg,k` rebasing and Borland's spill-and-reload of a rebased pointer, finds no read of it on a sim object, nor a word or dword read at `+0x35`-`+0x37` on one that would cover it, against a control on `obj+0x39` (the shape layer's flag beside it) that finds three — `AnimObj_Dtor` (`00402400`), `SimObject_ApplyRootMotionIfEnabled` and `Sim_PollPlayerInput`. The byte carries a meaningful value, so the absence of a reader rests on the scan being exhaustive, which it cannot be shown to be.
- **Open:** what a mission that salvages more than 52 weapons overwrites, and so what reaches the file; `Mission_WriteResults` reads back as many pairs as the count says. Which memory follows the list depends on which path `Mem_NewArray` took at run time. From the mymem pool, the first pair past the block's end — pair 53, or later when `Memory_Alloc` handed over a whole free block rather than split one — lands on the next block's 8-byte header ([`../runtime-library.md`](../runtime-library.md)): an allocated block's `KLBA` tag and size, or a free block's next pointer and size. Nothing checks writes past a pool block's end. From `Mem_HeapAlloc`, the list borders the C heap instead.