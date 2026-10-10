# AI navigation — routes, formation, and obstacle avoidance

How an AI machine gets from where it is to where its order wants it. The order layer that says *where* is [`ai-goals.md`](ai-goals.md); the state a machine is in while it walks is [`ai-dispatch.md`](ai-dispatch.md); the control law the whole of this doc ends in is `Mech_LocomotionTick` in [`mech-locomotion.md`](mech-locomotion.md).

**Movement is the think slot's job, not the move slot's.** Eighteen of the 22 states share `Mech_MovementTick` as their move, and that function only integrates the animation and resolves collisions — it makes no decisions. Every steering decision in the game is a think function calling `Mech_LocomotionTick` directly with a turn axis and a desired speed.

## The four movement primitives

| Address | Name | What it does |
|---|---|---|
| `0041fac4` | `Ai_DriveToPoint` | Walk at a fixed point. Returns "arrived" |
| `0041fb60` | `Ai_FollowRoute` | Walk the group's route, one waypoint at a time, advancing the cursor |
| `0041fbb8` | `Ai_KeepFormation` | Hold a formation slot on the group leader |
| `0041d598` | `Ai_NavigationStep` | Picks between the three, then updates the turret and the radar mode |

`Ai_NavigationStep` is the whole choice:

```
if (mech+0x23e != 0)                  Ai_DriveToPoint(mech, mech+0x240)   // a standing squad order
else if (mech == group.members[0])    Ai_FollowRoute(mech)                // the leader
else                                  Ai_KeepFormation(mech)              // everyone else
Ai_UpdateWeaponsFree(mech)                                                // 0041c3c8, the radar mode
Mech_CenterTorsoTick(mech, 0)
```

**Only the leader follows the route.** Everyone else steers off the leader, which is why a group stays together over a route none of its members but one is reading, and why a group whose leader dies stops navigating — the leader is whatever `group+0x0c`'s slot 0 names, and the member array is not compacted.

The squad-order branch belongs to [`ai-squadmates.md`](ai-squadmates.md); `mech+0x240` is that order's own destination.

### Drive to a point — `Ai_DriveToPoint` (`0041fac4`)

```
bearing = Math_HeadingToward(point, mech.position)
dist    = Math_GroundDistanceBetweenPoints(mech.position, point)     // 2D, ground plane only
speed   = mech+0x252 != 0 ? mech+0x252 : 0xaa
if (mech+0x23e != 0 && dist > 30000) { TurboPod_Engage(mech+0x317); speed = 0x100 }
Mech_LocomotionTick(mech, (bearing - mech.heading) >> 6, speed, 0)
return dist < 10000
```

Four things it fixes for everything downstream:

- **Steering is the bearing error divided by 64.** `Mech_LocomotionTick` clamps its turn axis at `±0x100`, so the stick is hard over at any error past 16384 BAM — a quarter turn — and proportional inside that. Every AI steering decision in the game is this expression.
- **Range is measured on the ground plane.** `Math_GroundDistanceBetweenPoints` (`004927c4`) subtracts and takes a 2D magnitude, so a waypoint on a hilltop is as near as one at its foot. Every range a *steering* decision is made on is this one; the 3D form, `Math_DistanceBetweenPoints` (`00492780`), reaches the navigation layer in exactly two places — the avoidance's machine sweep and `following`'s standoff, both below.
- **The cruise speed is per machine**, out of the mission file: `mech+0x252`, set at spawn from block 7's `+0x02` and zero in 91% of retail records. Zero means `0xaa`, about two thirds of the `0x100` that saturates a chassis' maximum.
- **Arrival is 10000 units** — 60 metres, and again on the ground plane.

`Ai_DriveToPoint`'s Turbo Pod sprint fires only under a standing squad order, so nothing a machine does on mission orders sprints it to a waypoint however far it has to walk. It is not the pod's only user: `Mech_BehaviourFleeThink` (`0041d2c4`) engages it with no gate at all, which is the one place in the AI a pod fires on mission orders. Both call sites reach `mech+0x317` and there is no third. How an AI machine's pod is engaged and charged is [`equipment-pods.md`](equipment-pods.md#what-the-two-ticks-do-with-the-button); what the sprint is worth is [`mech-locomotion.md`](mech-locomotion.md#damage-effects-on-movement).

### Follow the route — `Ai_FollowRoute` (`0041fb60`)

```
next = Route_WaypointAt(group.routeCursor, group.routeCursor.index + 1)
if (next == null)                      Mech_LocomotionTick(mech, 0, 0, 0)     // stand
else if (Ai_DriveToPoint(mech, next))  Route_AdvanceCursor(group.routeCursor)
```

The whole route mechanism for a walking AI machine. It always drives at the waypoint **after** the cursor, so the cursor names the last one reached and a fresh group walks at waypoint 1, not waypoint 0.

`Route_AdvanceCursor` (`0042313c`) has four callers, and all four hand it the same `group+0x04` — every class that walks a route steps the cursor of the group it belongs to, at its own arrival range:

| Caller (entry) | Call site | Arrival | |
|---|---|---|---|
| `Ai_FollowRoute` (`0041fb60`) | `0041fb99` | 10000 | this layer |
| `Mech_BehaviourPlayerThink` (`0041c194`) | `0041c208` | 10000 | announces it: [`player-waypoints.md`](player-waypoints.md) |
| `Flyer_LeadRouteStep` (`004224c4`) | `00422539` | 15000 | [`ai-flyers.md`](ai-flyers.md) |
| `GroundVehicle_LeaderSteer` (`0046a8e4`) | `0046a93d` | `GroundVehicle_DriveToPoint`'s own | [`ground-vehicles.md`](ground-vehicles.md#the-control-law--0046a798-and-0046a854) |

A route that runs out leaves the machine standing on the spot with its throttle at zero — and, through `Group_IsOrderComplete`, ends the order. The cursor wraps to zero on a closed route, so a patrol never runs out and a patrol order never completes; see [`ai-goals.md`](ai-goals.md#the-route-cursor-is-loaded-once).

### Keep formation — `Ai_KeepFormation` (`0041fbb8`)

Steers off the group leader rather than off the route. The post is the leader's position with **this machine's own formation-slot offset** applied through `Mech_ApplyFormationOffset` (mech vtable `+0x78`) — the same offset and the same rotation-by-the-leader's-heading that placed it at spawn, so the formation turns with the leader instead of being a fixed set of world points. `dist` throughout is the ground-plane range to that post.

| Condition | Turn | Speed |
|---|---|---|
| `dist < 2000` | `-(mech.heading - leader.heading) >> 6` | 0 |
| `dist ≤ 25000`, leader moving, heading error under `0x2000` | `-(((mech.heading - leader.heading) >> 6) + (-lateral >> 5))`, the lateral offset taken in the leader's frame | `leader.speed + (-longitudinal >> 5)` |
| `dist ≤ 25000`, leader moving, heading error `0x2000` or more | `±0x100` | `-0x100` |
| otherwise | bearing error `>> 6` | `dist >> 7`, or `0x100` past 25000 |

- **On station it matches the leader's heading, not its bearing to the post** — that is what keeps a stopped formation dressed rather than pointing inward.
- **In motion it flies formation properly.** The delta from the post is rotated into the leader's frame by transposing the leader's Q14 rotation matrix (`Math_TransposeRotation2D`, `0047ddf8`), giving a lateral and a longitudinal error; lateral steers, longitudinal trims the speed off the leader's own. This is the only place in the AI that works in anything but bearings and ranges.
- **A member facing more than 45° away from the leader backs out of it**, turning hard while reversing at full throttle, rather than driving a wide arc.
- "The leader is moving" is `leader.speed` outside `[-25, 24]`, so a leader that has stopped puts every member back on the plain close-the-gap arm.

## Obstacle avoidance — `Mech_AiObstacleAvoidance` (`00416274`)

Not a think function: `Mech_LocomotionTick` calls it on every machine that is not the player, with pointers to the turn axis and the desired speed it was handed, so it amends a decision already made rather than making one. It runs on the player's squadmates as much as on the enemy.

It is skipped entirely when the machine is stopped *and* being asked to stay stopped, and when the desired speed is negative unless the unstick timer is running.

**Bit 0 of `Mech_LocomotionTick`'s fourth argument, handed through, switches off the first two sources** — the probes and the machine sweep — and leaves only the player's line of fire. Of the 17 call sites in the whole-program decompile, the four in [`ramming`](ai-combat-states.md#ramming-17--mech_behaviourramthink-0041e570)'s think pass 1 and the rest 0. Without it a rammer would count its own target as an obstruction, and the avoidance's steer, many times the ram's own, would turn it away at close range.

The whole function reduces to two numbers, `nearLeft` and `nearRight` — the range to the closest obstruction on each side, both starting at 22000 for "nothing there". Three sources feed them.

### The two probes

Two 20000-unit segments in body space, splaying outward:

```
left :  (-1500, 0, 0)  ->  (-10000, 20000, 0)
right:  ( 1500, 0, 0)  ->  ( 10000, 20000, 0)
```

Each is transformed to world space, flattened onto the machine's own terrain height, and tested twice: against shapes by `Sim_RaycastShapes` (`00404ca0`) and against the ground by `Terrain_RayWalk`. Whichever hit is nearer becomes that side's number.

`Sim_RaycastShapes` tests the structure pool alone, gathering its candidates first (`Sim_RaycastShapeList`, `00404bc0`; the query itself is [`hit-detection.md`](hit-detection.md#the-shape-probe--sim_raycastshapes-00404ca0)). Its filter is the **same test `Base_GetCollisionRadius` (`004035b8`) uses**, read the other way round: `typeRec+0x06` zero means a static structure, which always counts, and an animated structure counts **only once it is a wreck**.

So the two sources are exact complements rather than overlapping. What has a collision radius is what the machine sweep below sees, and it is precisely what this probe skips: a standing animated structure, and every HERC, which is not in the structure pool at all.

**The ground half is `Terrain_RayWalk`'s mode 1, and it has to be.** The probes lie flat on the surface, so mode 0 — the thin ray, which reports the ground wherever the segment is at or below it — would graze on every tick of rolling terrain and pin the steer hard over. Mode 1 asks a different question at each cell the segment crosses: is the face it is crossing one movement can pass? `Terrain_FaceBlocksMovement` (`0046fe40`) answers it from the face normal's upward component alone — under `0x60e` at `0x800` scale, about 41° of slope, is a wall and stops the segment; anything shallower does not. So the probes see cliffs and nothing else. The threshold sits just *shallower* than `Mech_CollisionTest`'s own `0x5aa`, which is what gives a machine a band of slope it will steer away from before the move is refused outright.

### Other machines

A linear sweep of the live-object list. A candidate is weighed when it is not this machine, its group has entered the mission, and its collision radius (vtable `+0x7c`) is non-zero — which for a machine is `typeRec+0x70`, 750 on all 21 chassis, so the test only ever rejects the structures the probes already cover.

**This range is taken in three dimensions** — `Math_DistanceBetweenPoints` (`0041662f`), not the ground form every steering decision uses; `following`'s standoff is the layer's only other. It is then scaled by `Q10(2000, d)` — very nearly twice the true distance, so a machine reads as an obstruction from twice as far as its actual range — and if that lands inside 45° of dead ahead it claims whichever side it lies on. A machine on a rise therefore reads as further off than the steer would otherwise make it.

### The player's line of fire

The third source runs only for a machine whose group is **led by the player**, and what it reads is `DAT_004a9c0c`: a trail of up to 40 points that `Mech_PlayerFireTick` (`00415608`) stamps along the player's turret bearing every time the trigger produces a shot, spaced `0x1000` apart and cut to the range of the player's selected target. The array has exactly three references in the image: `Mech_RegisterSubsystem` (`0041bcac`) builds it — 40 elements of 8 bytes, which is where the cap comes from — `Mech_PlayerFireTick` writes it, and this function reads it. Nothing draws it.

**So the squad gets out of the player's line of fire.** These points are scaled by `Q10(1000, d)` — near enough the true range — and claim a side inside a wider 67.5° arc than a machine does. Not firing zeroes the count, so the line exists only while the player is actually shooting.

### Resolving it

```
if (unstickActive && nothing found)        take a quarter off the committed side's range
if (unstickActive && both sides found)     nearLeft += mech+0x254
steerAway = nearLeft <= nearRight
closest   = min(nearLeft, nearRight)
gain      = (source was the firing line) ? 1000 : 700
turn     += ±((22000 - closest) * gain / 22000)
```

The steer is added to the think function's own, and it grows linearly from nothing at 22000 to the full gain on contact. `0x100` is the axis' stop, so 700 is already well past hard-over: the avoidance overwhelms the navigation whenever an obstruction is inside about a third of the probe length.

**The speed is only ever cut for the player's firing line.** The threshold that gates the speed override is 3000 for a firing-line point and 0 for everything else, so terrain and machines are steered around at unchanged throttle, and only walking into the player's shots makes a squadmate stop — at full reverse.

### The unstick manoeuvre

Avoidance is prediction; this is what happens when it fails. `Mech_MovementTick` (`0041a360`), on a refused move and only for a machine that is not the player:

```
mech+0x26e = 10000                       // a ~4.9 s timer at mech+0x26d
mech+0x254 = rand & 1 ? -5000 : 5000     // a side, picked by coin flip
mech+0xae  = (mech.speed > 0)            // back out if it was going forward
```

For those five seconds `Mech_LocomotionTick` ignores the desired speed it was handed and runs the machine at its chassis maximum — reverse if `+0xae` is set, forward if not — while `mech+0x254` biases the avoidance to the side the coin chose. The bias is applied two ways: it breaks the tie when both sides are blocked, and when *neither* side reports anything it invents a 25% closer reading on the chosen side so the machine still turns.

## The navigation states

Six of the 22 states are navigation rather than combat — 8, 9, 10, 11, 12 and 15 — across five think functions, since `travelling` and `bulldog travel` share one. Every one of them returns zero unconditionally, so none ever ends itself: a movement order ends through `Group_IsOrderComplete`, never through its think.

### `patrolling` (8) — `Mech_BehaviourPatrolThink` (`0041d7d0`)

`Ai_NavigationStep`, then the target drops, and then a gate: **a machine that is not the group leader, has no standing squad order and has not been told to fire at will returns here and does nothing else.** It does not even hold a target — the drop is above the gate — so by default the leader is the group's only scout. The squad verb is sampled *before* the movement, which can clear it. `mech+0xb6` is `FIRE AT WILL` and is what buys a follower the right to scout; see [`ai-squadmates.md`](ai-squadmates.md).

That is not the same as a follower never fighting. Two things reach one: `Mech_AiOnTakingFire`, and the combat reassess's leader sweep, which the leader's own think triggers the moment it finds something. So a follower cannot *notice* a fight, only join one, and it joins by acquiring its own target rather than taking the leader's. See [`ai-targeting.md`](ai-targeting.md#the-combat-reassess--mech_aicombatreassess-0041cf18).

Past the gate, on a 10000-count timer — about 4.9 seconds, see [`dbsim-physics-notes.md`](dbsim-physics-notes.md#timer-units) — in the behaviour block's scratch (`mech+0x5a`):

- A machine that is out of action (`+0xa5`, `+0xa4` or `+0x99`) acquires a target and takes `fleeing`.
- Otherwise it acquires only when the group order verb is 3 or the squad order verb is 2 — the two that actually mean patrol — with the mission-target-only filter, and hands what it finds to `Mech_AiEnterCombat`.

### `search/destroy` (12) — `Mech_BehaviourSearchDestroyThink` (`0041d60c`)

The same shape and the same gate less its squad-verb term — only leadership and `mech+0xb6` open this one — with one difference that is the whole state: what it acquires must pass `Group_IsOrderTarget`. **A search-and-destroy group fights only what its order names** and walks past everything else. The flee arm is as in `patrolling`.

### `travelling` (9) and `bulldog travel` (11) — `Mech_BehaviourTravelThink` (`0041d9cc`)

`Ai_FollowRoute` **directly**, not `Ai_NavigationStep` — so in this one state every member reads the route itself and every member can advance the shared cursor. Formation is not held while travelling; the members' spawn offsets are all that keeps them apart.

That also means the cursor can be stepped several times in a tick, once by each member that is within 10000 of the waypoint the previous step just made current. A tightly-packed group crossing a dense stretch of route skips through it faster than one machine would.

It then drops its target, and on the same timer acquires one into `mech+0x5f` — a *look-at*, not a target: it is never written to `mech+0x1a4`. It is nonetheless **shot at**: the state closes with `Ai_AimAndFire`, the same tail the combat states use, so a machine walking a route engages what it watches without ever selecting it. See [`ai-weapons.md`](ai-weapons.md). On each acquisition the radar (`mech+0x96`) goes ACTIVE if there is something to watch or the mission's standing setting (`mech+0x97`) is on, and PASSIVE otherwise.

### `following` (10) — `Mech_BehaviourFollowThink` (`0041daac`)

Identical but for its first two lines: instead of a route it drives at `Group_OrderTargetObject`'s position, and it stops at 25000 rather than closing to 10000. **That 25000 is the layer's other three-dimensional range** — `Math_DistanceBetweenPoints` at `0041dad3` — so a machine holds further back from something above or below it than from something level with it.

Nothing here touches the route cursor, and verb 6's completion test is a route test — so **a `following` order can only ever complete on a group whose route was already empty**. The group can still leave the order without completing it, through the action path in `Group_OrderTick`; see [`ai-goals.md`](ai-goals.md).

### `guarding` (15) — `Mech_BehaviourGuardThink` (`0041e224`)

The post is `Mech_AiGoalPosition` (`0041dbcc`). A player squadmate with no standing order skips the whole state and holds formation instead. Everything else works a standoff ring, on ground range to the post — and the defence acquisition also runs for a machine told to fire at will, taking the standing engage order's own target (`mech+0x248`) when there is one in place of `Ai_SelectDefenceTarget`'s pick:

| Range | Behaviour |
|---|---|
| over 30000 | `Ai_DriveToPoint` at the post |
| 10000–30000 | stand still, turning to face **away** from the post |
| under 10000 | walk away from the post at `0x100` |

The half-turn in the steering term (`bearing - heading - 0x8000`) is what makes a guard face outward. The ring is the state's entire movement; there is no patrol of the perimeter.

Its target handling is the exception among the five: it runs `Ai_SelectDefenceTarget` (`0041e0e0`, scored in [`ai-targeting.md`](ai-targeting.md#taking-fire--mech_aiontakingfire-0041f7b8-mech-vtable-0x50)) against the post rather than a plain acquisition, and installs `driving off en` (16) on what it finds, or `fleeing` (18) if it is itself out of action.

## Mech and mission fields this layer owns

| Offset | Type | Meaning |
|---|---|---|
| `+0xae` | byte | Unstick direction: reverse out rather than push forward |
| `+0x254` | short | Unstick side, `±5000` |
| `+0x26d` | timer | The unstick window, 10000 counts or about 4.9 s; its counter is the int at `+0x26e` |
| `+0x252` | short | AI cruise speed, from block 7 `+0x02` ([`script-dat.md`](../formats/script-dat.md), [`msn-mission-file.md`](../formats/msn-mission-file.md)). Zero means `0xaa` |
| `+0x5a` | timer | The navigation states' own decision clock, 10000 counts or about 4.9 s, in the behaviour block's scratch |
| `+0x5f` | ptr | What `travelling` and `following` point the turret at. Not a selected target |
| `+0x97` | byte | The mission file's standing radar setting for this machine, from block 7 `+0x00` — [`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set) |
| `+0x96` | byte | Radar mode, written here from `+0x97`, or from `+0xb2` in the player's squad — [`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set) |
| `+0xb6` | byte | `FIRE AT WILL` — lets a non-leader run the patrol, search-and-destroy and guard thinks. Written by [`ai-squadmates.md`](ai-squadmates.md) |

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| `Mech_MovementTick`, the move slot 18 states share, is where AI movement happens | It integrates and collides; it steers nothing. Every AI steering decision is a think function calling `Mech_LocomotionTick` |
| `DAT_004a9c0c` is the HUD's lead-indicator trail | Nothing draws it. Its three references are the static initialiser that builds it, `Mech_PlayerFireTick`'s write and `Mech_AiObstacleAvoidance`'s read — it is a friendly-fire keep-out line, not a display |
| Obstacle avoidance has a mirrored mode for a machine walking backwards | It has the code for one — a flag that flips the probe length negative and rotates every bearing test by a half turn — and the flag is written zero at the top of the function and never anywhere else. The half-speed arm of the speed override is unreachable for the same reason |
| The whole group follows the route | Only the group leader does, through `Ai_NavigationStep`. The exception is `travelling`, which bypasses that chooser entirely and has every member reading the route at once |
| A `following` order ends when the group reaches what it is following | Its completion test is the route test the other two movement verbs use, and nothing in `following` advances the route cursor |
| Every navigation range is the ground one | Every range a *steer* is computed from is, which is the bulk of them and the reason a hilltop waypoint is as near as its foot. Two ranges that only gate a decision are 3D: the avoidance's machine sweep and `following`'s standoff |
| `Sim_RaycastShapes`' filter turns on whether the candidate moves | `typeRec+0x06` is a machine's top speed and a structure's animated flag, and the probe walks only the structure pool, so it reads the second. A standing *animated* structure is skipped here and picked up by the collision-radius sweep instead |

## Open

- **Deferred:** descriptor `+0x3c` groups `skirting` and `ramming` with the combat states against the rest of the navigation roster, a stronger distinction than its one reader needs — see [`ai-dispatch.md`](ai-dispatch.md).
- **Deferred:** why the firing line is the only source that cuts speed — the distance thresholds for the other two are zero, which reads more like an unfinished tuning pass than a decision.
