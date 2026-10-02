# AI combat states

The eleven behaviour thinks that are not navigation: the six a machine fights in, the two it runs or detours in (`fleeing` and `skirting`), the two it stands still in (`sleeping`, and the shared think of the out-of-action states), and the one it kills itself in. What state a machine is in and how the think is reached is [`ai-dispatch.md`](ai-dispatch.md); which object it fights is [`ai-targeting.md`](ai-targeting.md); how it shoots once it is pointed is [`ai-weapons.md`](ai-weapons.md); the walking states are [`ai-navigation.md`](ai-navigation.md).

**A combat state decides one thing: where to stand.** Every one of them ends in the same two calls — `Ai_CombatMoveStep` to walk and `Ai_AimAndFire` to shoot — and differs only in the steering and standoff it hands the first of those. The target is already chosen when the think runs, and the weapon is chosen inside the second.

## The shape they share

```
if (Ai_BeginSkirtIfBlocked(mech)) return 0                    // 0041de9c, below
Ai_BuildCombatGeometry(mech, &geom, null)                     // 0041e758
<the state's own steering and standoff, written into geom>
Ai_CombatMoveStep(mech, &geom)                                // 0041e828
Ai_AimAndFire(mech, geom.aspect, null)                        // 0041ea7c
if (Ai_ShouldAbandonTarget(mech) || mech is out of action) return Ai_ClearSquadEngageOrder(mech)
return 0
```

`attacking flyer` omits the skirt gate and `fleeing` omits it and the abandonment test; everything else is verbatim. A nonzero return zeroes the state's own dwell countdown, which is a state saying it is finished — see [`ai-dispatch.md`](ai-dispatch.md#what-the-dwell-time-buys).

**"Out of action" is the same three bytes throughout the AI**: `mech+0xa5` no weapons left, `mech+0xa4`, `mech+0x99` destroyed. `Ai_ClearSquadEngageOrder` (`0041c478`) is the tail: it returns 1, and on the way clears a standing squad engage order (`mech+0x23e == 4`) whose target is the one being let go.

## The geometry block — `Ai_BuildCombatGeometry` (`0041e758`)

`0x18` bytes on the think's own stack, built once per tick and then amended by the state before the move step reads it.

| Offset | Type | Field |
|---|---|---|
| `+0x00` | `int16` | Bearing to the target |
| `+0x02` | `int16` | **Bearing error** — that bearing less the machine's heading. The one field a state rewrites to steer |
| `+0x04` | `int16` | Its magnitude, saturating `-0x8000` at `0x7fff` |
| `+0x06` | `int16` | The target's **aspect**: the bearing back to this machine in the target's turret frame |
| `+0x08` | `int16` | Its magnitude |
| `+0x0a` | `int32` | **3D** range, `Math_DistanceBetweenPoints`. Every range a navigation steer is computed from is the ground-plane one; this is not |
| `+0x0e` | `int16` | **Approach flag**: `1` forward, `-1` reverse, `0` stand. Starts 0 |
| `+0x10` | `int32` | Near standoff. Starts 15000 |
| `+0x14` | `int32` | Far standoff. Starts 30000 |

The aspect is `targetTurretTwist + (bearingToTarget - 0x8000) - targetHeading`. The twist is added, the same direction the player's forward-cone test and the sensor arc fold it in ([`target-selection.md`](target-selection.md#can-this-be-targeted--targetselect_cantarget-00433174)). The states below steer on it. The copy `Ai_AimAndFire` is handed has one reader, `Ai_ChooseWeapon`'s shield-facing test ([`ai-weapons.md`](ai-weapons.md#choosing-a-weapon--ai_chooseweapon-0041f358)). The two travel thinks build the same quantity by hand rather than through this function, for that hand-off alone.

## The move step — `Ai_CombatMoveStep` (`0041e828`)

```
if      (range < nearStandoff) approach = |bearingError| > 0x3fff ? 1 : -1     // open the range
else if (range > farStandoff)  approach = |bearingError| < 0x4001 ? 1 : -1     // close it
geom+0x04 = |bearingError|                                                    // after the tests
if (approach != 0) { mech+0x5a = approach; speed = approach > 0 ? 0x100 : -0x100 }
else                                                                speed = 0
Mech_LocomotionTick(mech, bearingError >> 6, speed, 0)
```

Four things it fixes.

- **The standoff pair is a ring, and the approach flag is how the machine holds it.** Too near and it moves away from the target, too far and it moves toward it, and the sign is chosen so that either happens whichever way the machine is facing: a machine with its back to something it wants to leave drives forward, a machine facing it reverses. Between the two ranges the caller's own flag stands, and `0` means stand and shoot.
- **A state that wants to steer somewhere other than at the target rewrites `+0x02`.** Steering is the same `error >> 6` the navigation layer uses, clamped by the control law at the axis stop.
- **The two range tests read the magnitude the geometry left**, before the recompute on the next line — so they judge by where the *target* is, not by where the state has pointed the machine.
- **Speed is `±0x100` and nothing else.** A combat state never cruises; it is at the stop or stopped.

`mech+0x5a` is the behaviour block's scratch, and the approach flag written there is read nowhere. It is a `short` laid over the same bytes as the `int` timer `fleeing` steps at `mech+0x5b`, so a fleeing machine's side-switch clock has its low byte rewritten every tick by whichever sign the flag took: `1` writes `01 00` and lands `00` on the clock's low byte, `-1` writes `ff ff` and lands `ff`. The clock is 4000 (`0x0fa0`); the aliasing pins it to 3840 or 4095 immediately after every move step. `fleeing` never leaves the flag at zero, so this happens on every one of its ticks.

## `attacking` (3) — `Mech_BehaviourAttackThink` (`0041c594`)

The only state that reads the *target's* facing to decide where to stand, and it does it in two halves split on `|aspect| < 8000`.

**The target is looking at me** (`|aspect| < 8000`, about 44°). Take a point 18000 units off the target's own beam:

```
approach = (0x8000 - |bearingError|) >= 4000 ? 1 : -1
side     = (bearingError < 0) == (|bearingError| > 0x4000) ? +0x4000 : -0x4000
point    = Math_OffsetPointByBearing(target.position, target.heading + side, 18000)
if (approach == -1) point = 2*mech.position - point          // reflect it through myself
bearingError = Math_HeadingToward(point, mech.position) - mech.heading
```

The side works out to the target's right when it lies ahead-right or behind-left of the machine and to its left otherwise, so the machine always cuts toward the nearer beam rather than crossing the target's nose. `approach` is `-1` only for a target within about 21° of dead astern, and that branch **reflects the aim point through the machine's own position**: the steer reverses, and with the reverse speed the move step gives it the machine backs onto the same point instead of turning around to walk at it.

**The target is not looking at me.** No point is built at all: `approach = 0` when the target is within 45° of the nose and `-1` otherwise, and the bearing error is left pointing straight at it. So the machine squares up and shoots, and gives ground only while it is still turning.

The standoff pair is left at 15000/30000. Of the states that write one at all it is the only one that keeps both, but `flanking` keeps the far figure on every tick and the near one whenever `Ai_CircleStep` takes its square-up arm — that arm writes neither, and nothing in the circling step ever writes the far standoff.

## `flanking` (4) — `Mech_BehaviourFlankThink` (`0041d4e4`)

The shared shape with `Ai_CircleStep` (below) between the geometry and the move. Nothing else.

**It is what a fast machine does when outgunned.** The combat reassess gates `flanking` on `typeRec+0xc8`, which `MechType_InitOne` loads with a copy of the chassis' forward speed; 13 of the 21 chassis clear the bar of 185 — see [`ai-targeting.md`](ai-targeting.md#the-combat-reassess--mech_aicombatreassess-0041cf18). The circling step it is built out of is reachable by the other eight too, because `attacking base` shares it.

## `facing off` (5) — `Mech_BehaviourFaceOffThink` (`0041d41c`)

The simplest of them.

```
approach  = |bearingError| > 0x1fff ? -1 : 1
standoffs = 16000 / 35000
```

Walk in when pointed at the target, back off when more than 45° off it, and hold a slightly wider ring than `attacking`'s. It never steers anywhere but at the target, which is what "facing off" is: the machine that is outgunned keeps its front armour toward the machine that outguns it.

## `driving off en` (16) — `Mech_BehaviourDriveOffThink` (`0041def0`)

The skirt gate, then `facing off`'s think verbatim. What separates the two states is entirely in their descriptors: `driving off en` carries the "holding a place" flag bit and reassesses through `Mech_AiSelectBehaviour` rather than the combat reassess, so after its 50000-count dwell (about 24 s) it goes back to its order instead of looking for another fight. It is what a guard and a machine taking fire at its post are put into — see [`ai-navigation.md`](ai-navigation.md) and [`ai-targeting.md`](ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50).

## `attacking base` (6) — `Mech_BehaviourAttackBaseThink` (`0041c86c`)

```
if (target.typeRec+0x2e == 0) { aspect = |aspect| = 32000 }
Ai_CircleStep(mech, &geom)
standoffs = 25000 / -20536
```

The negative far standoff is the point: a range can never be below it, so the move step's "close it" arm runs on every tick the machine is outside 25000 and its "open it" arm on every tick inside. **The machine holds a 25000-unit ring and is never allowed to stand still.**

Forcing the aspect to 32000 makes `Ai_CircleStep` see a target that is not facing it, whatever the geometry says, so the machine stands off rather than circling. Which structures escape that is `BASES.DAT +0x2e`, below.

The completion test is the one place a state does not simply ask whether its target is finished:

```
if (squad order 4 with a target, or Group_IsOrderTarget(group, target))  done = target+0x99
else                                                                     done = target is out of action
```

A structure the mission actually named has to be **destroyed**; any other structure only has to be out of action. So a machine sent to level a specific building stays on it to the end, and one that picked a building up on its own lets go as soon as it stops mattering.

### `BASES.DAT +0x2e`

The AI reads the field as a flag in exactly two places — here and the flee check's structure exception ([`ai-targeting.md`](ai-targeting.md#the-flee-check--mech_aifleecheck-0041cb94)) — and in both a nonzero value means *this target is dangerous*: a crippled machine flees from one instead of pressing the attack, and an attacking machine circles one instead of standing off. The field is the type's armament class, so the flag the AI wants and the armament the tick wants are the same field, and a generator that never fires reads as dangerous as a gun tower. Which types state it, and what each value fires, is [`structure-behaviour.md`](structure-behaviour.md#the-armed-tick--00404100)'s.

## `attacking flyer` (7) — `Mech_BehaviourAttackFlyerThink` (`0041c9cc`)

No skirt gate — there is nothing to walk around under a flyer — and the standoffs are 0 and 1000000, which puts both of the move step's arms out of reach and leaves the approach flag entirely to the state:

```
approach = |bearingError| >= 2000            ? -1
         : |aspect| > 2000 && range > 35000  ?  1
         : |aspect| > 2000 || range > 35000  ?  0
         :                                     -1
```

A machine only advances on a flyer it is pointed within 11° of *and* that is both far off and not pointed back at it; anything else is a stand or a retreat. Turning under a flyer is what the machine mostly does, since the bearing gate is tight enough that it is rarely satisfied while the flyer is manoeuvring.

It gives up on range: past 150000 units the think returns finished, before it even looks at whether the flyer is alive.

## `fleeing` (18) — `Mech_BehaviourFleeThink` (`0041d2c4`)

It runs *from* something, which is not the same as having it as a target. The first thing it does is move the selected target into the block scratch at `mech+0x61` and release the selection — decrementing `target+0x1a2` and setting `mech+0x9d`, the ordinary release — so a fleeing machine holds no target while it runs. The geometry, the aim and the completion test are all built against the stashed pointer instead.

```
if (|bearingError| < 0x3000) { approach = -1; bearingError -= 0x8000 }
else {
    if (Timer_CountDown(mech+0x5a) == 0) { mech+0x5b = 4000; mech+0x5f = rand & 1 }
    approach = 1; bearingError += mech+0x5f ? -0x6000 : +0x6000
}
standoffs = 0 / 10000000
TurboPod_Engage(mech+0x317)
```

Two legs. While the threat is still within 67.5° of the nose the machine reverses with its steering pointed a half turn away, which turns it; once it has turned, the other leg runs it off at 135° to the threat, flipping sides every 4000 counts — about two seconds, see [`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units) — so it does not run in a straight line. The Turbo Pod is engaged on every tick — this is the one place in the AI that uses one on mission orders, since [`ai-navigation.md`](ai-navigation.md)'s sprint needs a standing squad order.

**It still shoots at what it is running from.** `Ai_AimAndFire` is called against the stash, and `Mech_AiFleeCheck` has already set `mech+0x2aa` to 300, 600 or 1000, which drops `Ai_ChooseWeapon`'s score floor to near or below zero — so a fleeing machine fires almost anything it still has.

The state ends when the thing it is running from is out of action: that is the think's only nonzero return, and it zeroes the state's own dwell so the combat reassess runs on the next tick. Otherwise the descriptor's 15000-count dwell (about 7.3 s) ends it, or one of the two external routes any state can be cut short by — damage, or a group order advancing ([`ai-dispatch.md`](ai-dispatch.md#what-the-dwell-time-buys)).

## `skirting` (14) — `Mech_BehaviourSkirtThink` (`0041dd64`)

The state a machine enters when its own shots are hitting something that is not what it aimed at.

### How it is reached

`Mech_AiOnLineOfFireBlocked` (`0041dd2c`, mech vtable `+0x64`) copies the machine's current target position to `mech+0x31e` and sets `mech+0xad`. Its one caller is the tail of `Sim_RaycastObjectList` (`00426528`), which makes the call when a shot stops on terrain or on a third object, nearer than the intended target and within 45° of the same line — the test is [`hit-detection.md`](hit-detection.md#the-sweep--sim_raycastobjectlist-00426528)'s. The call is unconditional, so no vtable can leave the slot empty: every `SimObject`-shaped table but `MechVtable` fills it with `SimObject_AiOnLineOfFireBlockedNoOp` (`00411b34`), a shared stub that is a frame set-up and a `RET`. Only a machine reacts.

`Ai_BeginSkirtIfBlocked` (`0041de9c`) is the gate at the top of every combat think and turns that flag into the state:

```
if (mech+0xad) {
    stashed = mech+0x4d.descriptor
    Behaviour_SetState(mech, skirting)                        // zeroes the block scratch
    mech+0x5a = stashed
    mech+0x67 = Math_DistanceBetweenPoints(mech, mech+0x31e)
    return true                                               // the think does nothing else
}
```

The state it interrupted is stashed in the scratch it just cleared, which is what makes `skirting` an excursion rather than a decision: the machine goes back to exactly the state it left.

### The state

```
if (mech+0x5e == 0) {                                                  // first tick
    mech+0x60 = (bearingTo(stash) - heading) < 0                       // which way to go round
    mech+0x5e = 1; mech+0x63 = 5000; mech+0x61 = 1
}
if (Timer_CountDown(mech+0x62) == 0) {
    mech+0x63 = 5000
    mech+0x61 = Ai_LineOfSightBlocked(mech)
    if (mech+0x61 == 0) { Behaviour_SetState(mech, mech+0x5a); mech+0xad = 0 }
    else                  mech+0x67 = groundRange(mech, stash)
}
point = mech+0x31e
if (mech+0x61 == 1) point = OffsetPointByBearing(point, bearingFrom(stash, mech) ± 0x4000, mech+0x67)
Mech_LocomotionTick(mech, (bearingTo(point) - heading) >> 6, 0x100, 0)
Mech_CenterTorsoTick(mech, 0)
```

- **It walks at a point 90° off its own line to the stash**, at the range it currently stands at — an arc around the obstruction, always to the same side, chosen once from where the machine stood when the state began.
- **The line of sight is re-tested every 5000 counts, about 2.4 seconds, not every tick**, and only a *clear* reading ends the state. `Ai_LineOfSightBlocked` ([below](#line-of-sight--ai_lineofsightblocked-0041dc24)) answers 1 for anything the machine cannot get past and 2 for ground it could simply walk over, and **only the 1 gets the arc**: on a 2 the machine drives straight at the stash and crests the rise that is in the way.
- **It does not shoot and it does not steer around anything else.** The torso is centred and the throttle is at the stop. The think has one exit and always returns 0, so the only way out from inside the state is the clear reading on the 5000-count re-test; what can still take a machine out of it is external — `Mech_ComponentDamageWrite` installing an out-of-action state, or a group order advancing, which zeroes the dwell countdown and lets the reassess resolve the machine into something else ([`ai-dispatch.md`](ai-dispatch.md#what-the-dwell-time-buys)).
- The stash is a *position*, taken once. The state never looks at the target again, so a machine skirting after a moving target walks to where that target was.

### Line of sight — `Ai_LineOfSightBlocked` (`0041dc24`)

`Mech_BehaviourSkirtThink` is its only caller. It is built out of the same two probe primitives as [obstacle avoidance](ai-navigation.md#the-two-probes), `Sim_RaycastShapes` and `Terrain_RayWalk`. Both endpoints are lifted to their objects' aim-node origins, or by 500 units when there is no node, and then:

```
steep    = Terrain_RayWalk(from, to, mode 1)          // is a face in the way too steep to walk
if (Sim_RaycastShapes(from, to) hit something that is not the target) return 1
if (!Terrain_RayWalk(from, to, mode 0))               return 0      // the ground is clear
return steep ? 1 : 2
```

**The two nonzero answers are not "shape" and "terrain".** `1` is anything the machine cannot get past — a shape, or ground whose slope `Terrain_FaceBlocksMovement` says it could not walk. `2` is ground it *could* walk: the thin ray grazes a rise the machine can simply crest. That is what makes the reading matter to the state above.

## `sleeping` (13) — `Mech_BehaviourSleepThink` (`0041c418`)

Release the target, `Mech_LocomotionTick(mech, 0, 0, 0)`, `Ai_UpdateWeaponsFree`. A sleeping machine stands with its throttle at zero and its radar on whatever the mission file set, and holds no target — but it is still ticked, still detectable, and still answers fire through its vtable `+0x50` like any other.

## `dead` (20), `disabled` (21) and `in limbo` (19) — `Mech_BehaviourInertThink` (`0041e554`)

`Mech_LocomotionTick(mech, 0, 0, 0)`. Nothing else. `dead` and `disabled` share the think and differ only in their descriptors' `+0x3c`; `in limbo` has none at all.

All three are installed by `Mech_ComponentDamageWrite` and by nothing else — `disabled` when half a machine's legs are gone, `dead` when its cockpit, pilot or life support is, and `in limbo` in place of `dead` for a chassis that leaves no wreck. The conditions and their order are [`component-damage.md`](component-damage.md#going-out-of-the-fight)'s.

**A stopped machine is not an idle one.** The think's zero throttle is a *deceleration request*, so a machine killed at speed walks its momentum off over the next few ticks; and an immobilised one takes `Mech_LocomotionTick`'s own separate branch and goes down in its death animation — [`mech-locomotion.md`](mech-locomotion.md#going-down).

## `ramming` (17) — `Mech_BehaviourRamThink` (`0041e570`)

The odd one out twice over. It is the only AI state whose **move slot is not the walk**, `Mech_BehaviourRamTick` (`0041e488`) rather than `Mech_MovementTick` — `player fly` is the roster's other exception and belongs to the player path — and the only behaviour in the simulation whose success kills the machine running it. Nothing in it is shared with the section above: no geometry block, no move step, no `Ai_AimAndFire` — a rammer never shoots.

```
if (Timer_CountDown(&mech+0x5a) == 0) {            // retarget, every 10000 counts
    <select Ai_SelectTarget(mech, 6, 0) into mech+0x1a4, maintaining +0x1a2 and +0x9d>
    mech+0x5b = 10000
}
if (no target) { Mech_LocomotionTick(mech, 0, 0, 1); return 0 }

steer()                                            // below
if (mech+0x5f == 0) {                              // approaching
    if (Math_CountdownTimerTick(&mech+0x61) == 0) {
        mech+0x5f = 1;  mech+0x62 = 3000 + rand(1500)
    }
    return 0
}
Mech_BehaviourRamTick(mech);  if (no target) return 0     // charging
steer();  Mech_BehaviourRamTick(mech);  if (no target) return 0
steer()
if (Math_CountdownTimerTick(&mech+0x61) == 0) {
    mech+0x5f = 0;  mech+0x62 = 8000 + rand(4000)
}
return 0
```

`steer()` is `Mech_LocomotionTick(mech, (bearing - heading) >> 8, 0x100, 1)`: **full throttle, and the bearing error's top byte**. Every other state steers at `>> 6`, so a rammer turns a quarter as hard — it commits to a line rather than tracking a target that sidesteps.

The target is acquired with mask `6`, which drops the "it is shooting at me" weight and the crowding divisor both — [`ai-targeting.md`](ai-targeting.md). Nothing else in the state releases it, and its dwell flag keeps the reassess from running, so a rammer holds one target for a 10000-count interval — about 4.9 seconds — at a time whatever happens to it.

**The timer arms the phase it is entering, not the one it is leaving.** Both flag and countdown start at zero out of `Behaviour_SetState`, so the first think flips straight to charging: a machine takes the state and begins its run at once, then alternates 3000–4500 counts charging (1.5–2.2 s) with 8000–12000 counts approaching (3.9–5.9 s).

### The charge — `Mech_BehaviourRamTick` (`0041e488`)

The state's move slot, so it runs once a tick from the dispatch like any other move; charging, the think runs it **twice more**, which is why a charging machine covers three ticks of ground in one and arrives at three times its walking speed.

```
Mech_IntegrateMotion(mech)
mech.z = Terrain_HeightQuery(grid, mech.pos) + typeRec+0x16
Mech_PlaceLegsOnGround(mech)
if (Mech_CollisionTest(mech) || mech+0xb1) {
    Damage_ExplosiveBlastSweep(mech.pos, 3000, 2000, 0, mech)
    for (i = 0; i < 29; i++) if (mech+0x20e[i]) mech->vtbl+0x74(mech, i, 32000, mech)
}
return 1
```

It is `Mech_MovementTick` with the undo removed. The walking move restores the step and backs away from a block ([`mech-locomotion.md`](mech-locomotion.md)); this detonates instead — a blast the machine excludes *itself* from, and then a flat 32000 on every component it still has, through the same endpoint a shot reaches. There is no roll, no falloff and no survival; the blast is only what it does to everyone else on the way out. Damage and radius are [`damage-system.md`](damage-system.md#the-sweep--damage_explosiveblastsweep-00426a20)'s third call site.

**What sets it off is any block at all** — a rock, a building, a wingman — not contact with the target, and not this tick's contact either. `mech+0xb1` is the "something ran into me" latch, and this is its one reader in the image. It is written by `SimObject_SetRunInto` (`0042200c`), vtable `+0x68` in all eight `SimObject`-shaped tables with no class overriding it, on whatever object blocked a move and whatever class that object is. Two sweeps call it: `Mech_CollisionTest`, and `GroundVehicle_CollisionTest` inside the ground vehicle tick ([`structure-behaviour.md`](structure-behaviour.md#the-ground-vehicle-tick--0046a5d0)). **Nothing ever lowers the byte.** A machine bumped once at any earlier point in the mission blows up on its first tick in the state, before it has gone anywhere.

That "nothing lowers it" is a negative claim, so here is what it rests on, by three methods that fail differently:

- **A field scan of the disassembly** resolving the `LEA`/`ADD` rebases and the spill-and-reload idiom finds one write on a simulation object, `SimObject_SetRunInto`'s `1`, and one read. This matters because **both instructions rebase** — each does `ADD reg, 0x92` and then addresses `[reg + 0x1f]` — so a scalar search for the displacement finds neither.
- **A whole-program decompile** of all 3051 functions grepped for `0xb1` agrees: that read, that write, no third site.
- **A sweep of every `memset` and `memcpy` call site in the image**, which the other two cannot see, finds no bulk write over a mech that reaches the byte. The only ones on a machine are `SimObjectBase_Constructor`'s over `+0x38`, `Mech_Constructor`'s `0x4e` bytes from `+0x2b9`, and `Behaviour_SetState`'s two over the behaviour block, which stop at `+0x91`. `Base_Construct` has none.

The neighbours a wider store could straddle it from — `+0xae`, `+0xaf`, `+0xb0` — are byte fields with byte-wide accesses on a mech base.

The three cover each other's blind spots, which is the point of using them together. The disassembly leaves stretches of AI code as raw bytes rather than resolved instructions — 444 of them inside `Mech_BehaviourDriveOffThink` — and both the field scan and the `memset` sweep read the disassembly, so neither sees into those. The decompile does cover them, and it folds the rebase back into the true offset (`*(char *)((int)this + 0xb1)` is how it renders the read above), so a text grep of it for the offset is sound rather than defeated by the `ADD`.

What none of them closes is a write that reaches the byte without naming it: through a base register neither alias pass follows, or through a pointer the decompiler renders as an index rather than a constant. That is the residual, and it is why this is a well-supported claim rather than a proof.

**A machine's slot is never reissued, so the latch cannot be inherited either.** A recycled slot would carry its predecessor's bytes, but machines are never returned to their pool ([`sim-object-layout.md`](sim-object-layout.md#only-the-short-lived-classes-are-recycled)). The short-lived classes that are recycled do inherit a stale latch, but nothing reads `+0xb1` on them, since the only reader tests a *machine's own*.

Because the move slot runs whether or not the think is charging, the detonation is live in the approach phase too. The phases change how fast the machine closes, not whether contact kills it.

## The circling step — `Ai_CircleStep` (`0041c72c`)

Shared by `flanking` and `attacking base`. It reads a hysteresis byte at `mech+0x66` and two countdowns in the block scratch: `mech+0x60`, the break-off timer, and `mech+0x63`, the interval between break-offs. Each is stepped by `Math_CountdownTimerTick` through the byte below it — `mech+0x5f` and `mech+0x62` — which the timer takes as a handle and never reads.

```
if (mech+0x60 still running) {                      // breaking off
    nearStandoff = 0; approach = -1; bearingError = 0; mech+0x66 = 0; mech+0x63 = 4000
    return
}
if (mech+0x63 expired && mech+0x288 > 100) mech+0x60 = 1000
threshold = mech+0x66 ? 0x4000 : 0x6000
if (|aspect| < threshold) {                         // the target is facing me: circle
    side  = bearingError >= 0 ? +6000 : -6000
    point = OffsetPointByBearing(target.position, bearingToTarget - 0x8000 + side, 15000)
    nearStandoff = 0; approach = 1
    bearingError = Math_HeadingToward(point, mech) - mech.heading
    mech+0x66 = 0
} else {                                            // it is not: square up
    mech+0x66 = 1
    approach = |mech.turretTwist| < 2000 ? 0 : -1
}
```

- **The circle is a point 15000 units out from the target on the machine's own approach line, rotated 33° to one side** — near enough a tangent, so the machine walks a wide arc around a target that is pointing at it and closes on one that is not.
- **Which side it circles to is the side it is already turning toward**, re-chosen every tick, so a machine that overshoots reverses its arc rather than committing.
- **The break-off costs half a second and is bought with damage.** `mech+0x288` is the damage taken within the current ~4000-count window (about two seconds), zeroed at each window's expiry ([`sim-object-layout.md`](sim-object-layout.md#countdowns-keep-their-counter-one-byte-past-the-record)); once it passes 100 the machine spends 1000 counts in every 4000 — about half a second in every two — reversing in a straight line with no steering at all. An undamaged machine never breaks off, and one that has stopped taking fire stops breaking off within about two seconds.
- **The hysteresis is one-sided.** The threshold is 0x6000 (135°) while the machine is circling and 0x4000 (90°) once it has squared up, so a target has to turn further to start the circle than to stop it.
- The square-up arm gates on the machine's *own* turret twist rather than on any range: it stands still while the turret is within 2000 BAM of centre and reverses while it is not, so the machine walks backwards until its hull has caught up with where its guns are already pointing.

## Fields this layer owns

The behaviour block's scratch (`mech+0x5a` to `mech+0x81`, zeroed by every `Behaviour_SetState`) is a union: each state lays its own fields over it, and the same bytes mean different things in two states. Below are this layer's uses. The walking states lay their own fields over the same bytes — `+0x5b` as a 10000-count countdown in all four travel-shaped thinks, `+0x5f` as the object being watched in two of them — and are [`ai-navigation.md`](ai-navigation.md)'s.

`Timer_CountDown` and `Math_CountdownTimerTick` both take a pointer and step only what follows it, so a countdown here is always named by the byte *below* the field the timer steps: `+0x5a` is the handle for `fleeing`'s and `ramming`'s clocks, `+0x5f` and `+0x62` for the circling step's, `+0x62` for `skirting`'s. A handle byte is never read.

| Offset | State | Meaning |
|---|---|---|
| `+0x5a` | every combat state | The approach flag the move step wrote. No reader: the only other code to touch the bytes is `skirting`'s stash and the two timers, which take the address and step what follows it |
| `+0x5a` | `skirting` | The descriptor to go back to |
| `+0x5b` | `fleeing` | Side-switch countdown, 4000 counts |
| `+0x5b` | `ramming` | Retarget countdown, 10000 counts |
| `+0x5d` | `Ai_CircleStep` | Written zero on the square-up arm, as a `word` that also covers `+0x5e`. The write at `0041c858` is the only access `es2_fieldscan.py` finds to the field over the AI's code range, and no reader was found |
| `+0x5e` | `skirting` | The state has started |
| `+0x5f` | `fleeing` | Which side to run to |
| `+0x5f` | `ramming` | Charging rather than approaching |
| `+0x60` | `Ai_CircleStep` | Break-off countdown, 1000 counts |
| `+0x60` | `skirting` | Which way round to go |
| `+0x61` | `skirting` | The last line-of-sight reading |
| `+0x61` | `fleeing` | The object being run from |
| `+0x62` | `ramming` | Phase countdown |
| `+0x63` | `Ai_CircleStep` | Interval between break-offs, 4000 counts |
| `+0x63` | `skirting` | Line-of-sight re-test countdown, 5000 counts |
| `+0x66` | `Ai_CircleStep` | Circling or squared up — the aspect threshold's hysteresis |
| `+0x67` | `skirting` | Range to the stashed point |

Fields outside the block:

| Offset | Type | Meaning |
|---|---|---|
| `+0xad` | byte | The line of fire is blocked. Written by `Mech_AiOnLineOfFireBlocked`, cleared when `skirting` ends |
| `+0xb1` | byte | Something ran into this object. Written by `SimObject_SetRunInto` through vtable `+0x68`, called from `Mech_CollisionTest` and `GroundVehicle_CollisionTest`; read by `Mech_BehaviourRamTick`; never cleared |
| `+0x288` | int | Damage taken within the current ~4000-count window, zeroed at its expiry — [`sim-object-layout.md`](sim-object-layout.md#countdowns-keep-their-counter-one-byte-past-the-record), [`damage-system.md`](damage-system.md). Read here as the gate on the circling break-off |
| `+0x31e` | `int32`×3 | Where the target was when the line of fire was found blocked |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `skirting` is unreachable, because nothing calls mech vtable `+0x64` | The decompiler renders the slot in decimal (`*(code **)(*p + 100)`), so a search for `+ 0x64` finds nothing. Four call sites exist; the one on the mech vtable is `Sim_RaycastObjectList`'s blocked-line-of-fire test |
| A combat think chooses its own speed | Every one of them takes `±0x100` or 0 from `Ai_CombatMoveStep`. The cruise speed at `mech+0x252` is the navigation layer's and is never read in a fight |
| `Ai_CombatMoveStep`'s range tests read the bearing the state just steered to | The magnitude at `geom+0x04` is recomputed *after* both tests, so they see the bearing to the target and the steer sees the state's own point |
| `fleeing` keeps the machine it is running from as its target | It moves the pointer into the block scratch and releases the selection on its first tick. Nothing holds a target while it flees, which is why the machine is not counted among that target's holders |
| `attacking` walks a circle like `flanking` does | It builds one aim point per tick off the target's beam and steers at it; there is no timer and no alternation. The circling step is `flanking`'s and `attacking base`'s alone |
