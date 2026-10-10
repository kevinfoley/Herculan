# Mission actions, deployment and drop pods (DBSIM.EXE)

Addresses are DBSIM virtual addresses.

A **mission action** is a `script.dat` block-5 record: a one-shot latch with consequences hanging off it. Something activates it, and everything waiting on it acts. It is the only scripting the simulation has — mission progression, reinforcement waves and the drop pods are all this one mechanism.

See [`../formats/script-dat.md`](../formats/script-dat.md) for the record layouts and for how groups are placed in the first place.

## The four ways an action activates

`Action_Activate` (`00423430`) is one-shot: it sets the action's runtime activation flag (in-memory `+0x0a`, zeroed at load), walks the ten (counter ref, operation) pairs at `+0x0c`/`+0x20` bumping (op 6) or clearing (op 5) the mission-counter array `DAT_004a9ef4`, and queues the message at `+0x34`. **The message queue is inside the counter loop**, so an action naming five counters posts its line five times and one naming none posts it not at all.

The message goes to the **pilot and squad** port (`view+0x207`, through `CockpitView_GetSquadMessagePort`, `00433158`), not the computer's ticker, as `{id, null}` — no subject. That port looks a speakerless id up in `str\COMMAND0.STR` — `COMMAND<n>.STR` in training mission `n` — not in `data\mission.str`; see [`cockpit-messages.md`](cockpit-messages.md#its-speakerless-set).

| activated by | site | condition |
|---|---|---|
| its own trigger areas | `Actions_EvaluateTriggers` (`00426b70`) | a subject stands in one of them |
| an action timer | `ActionTimer_Tick` (`004230a4`) | the timer's delay runs out |
| an object being engaged | three sites below | that object's `+0x1b2` |
| an object being defeated | four sites below | that object's `+0x1b6` |

**None of these is the primary and the others fallbacks.** One action commonly carries two routes — in `TRAIN8.MSN`, action 0 has both a trigger area and a machine whose death activates it, and whichever happens first wins.

Both per-frame evaluators run from `Sim_MainTick` (`0045f464`), back to back and **after** the group pass, not before it:

```
per group: group+0x14 ? Group_DeploymentCheck : Group_OrderTick
ActionTimers_Tick (00426b48)       // every action timer
Actions_EvaluateTriggers(PlayerMech)
```

So a group waiting on an action arrives on the tick *after* it activates.

### Trigger areas — `Actions_EvaluateTriggers` (`00426b70`)

Walks the whole action array and, per action, picks whose position `Action_TestTrigger` (`004234b8`) is offered. The action's **type** (in-memory `+0x00`) selects the subject:

| type | subject |
|---|---|
| 0 | the player's mech |
| 1 | every member of the player's group |
| 2 / 3 | every member of every deployed group of side 0 (human) / side 1 (Cybrid) |
| 4 / 5 / 6 | every member of every deployed mech / flyer / base group |
| 7 / 8 / 9 | the action's own resolved target object (`+0x36`) |
| 10 | every member of the action's own resolved target group |

**The deployment gate is part of the test**: types 2-6 skip a group whose `+0x14` is still set, so an undeployed group cannot trip an action, including the one it is itself waiting on. Each sweep stops at the first subject that activates the action.

`Action_TestTrigger` returns "in the area" for an action that has already activated without testing anything, so the caller stops offering subjects. Otherwise it offers the position to each resolved block-4 area in turn (count `+0x04`, pointer array `+0x06`) and activates it on the first hit.

`DBSim_SpawnMissionObjects` (`004253d8`) resolves `+0x36` in a final pass over the array: types 7/8/9 resolve it as a mech/flyer/base roster slot, type 10 as a group, and types 0-6 have it zeroed.

#### The areas — block 4, resolved by `TriggerArea_Resolve` (`00423358`)

A block-4 record resolves to a 10-byte area ([`script-dat.md`](../formats/script-dat.md#the-13-block-structure), block 4). `TriggerArea_ContainsPoint` (`004233a4`) tests a position against it:

- **type 0** — axis-aligned XY box strictly between the two coordinates. Z is ignored, so a box catches anything standing over its footprint however high.
- **type != 0** — ground-plane distance from the coordinate is less than the stored radius.

A populated slot behind the action's first negative ref is never tested ([`script-dat.md`](../formats/script-dat.md#block-5-in-memory--58-bytes-0x3a)).

### Action timers — `ActionTimer_Tick` (`004230a4`)

The mission's timer, and the reason an action carrying no trigger area of its own is ordinary rather than dead. One block-6 record names a primary action, a delay and up to ten actions to activate:

```
if (timer.primary == null || timer.primary.activated) {
    if (Timer_CountDown(&timer.countdown) == 0) {
        for each of the ten sequence refs: if set, Action_Activate(it)
        re-arm the timer with 30000
    }
}
```

The file's delay is in seconds ([`script-dat.md`](../formats/script-dat.md#block-6-in-memory--49-bytes-0x31) has the shift). A timer with no primary runs from mission start; one with a primary runs from the moment that action activates. The re-arm goes through the same shift — 30000 seconds, about eight hours — and by then every action the timer names has activated, so the later expiry does nothing.

Chaining two of them staggers a sequence: `script6.dat` has action 1 arm a 92-second timer that activates action 2, which arms a 123-second timer that activates action 3.

### An object's own two actions — `+0x1b2` and `+0x1b6`

Every mech, flyer and structure carries two action pointers, resolved by `DBSim_SpawnMissionObjects` from its roster record's own refs ([`script-dat.md`](../formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps) has the offsets in blocks 7-9).

**`+0x1b2` — engaged.** Three sites activate it, and each also latches an `+0x9e`, the flag mission-objective condition 6 reads ([`mission-objectives.md`](mission-objectives.md)). Which object's flag is latched depends on the site:

| site | when | whose action activates | whose `+0x9e` is latched |
|---|---|---|---|
| `Detection_Sweep` (`004128f8`) | a pair that already have contact on each other are within 50000 units | both objects' | both |
| `Detection_ShareContact` (`00412704`) | a contact is passed to the spotter's side | each object of that side within 50000 of the contact | the contact's |
| `Sim_RaycastObjectList` (`00426528`) | a shot reaches the shooter's own selected target | the struck object's | the shooter's (`0042671f`) |

The raycast's route is the tail of the sweep in [`hit-detection.md`](hit-detection.md#the-sweep--sim_raycastobjectlist-00426528). Shooting at what you have boxed engages it with nothing in detection range of it.

`Detection_ShareContact` takes a spotter and a contact and does nothing if they are on the same side (`group+0x12`). Otherwise it walks the live-object list. Each object on the spotter's side whose group has arrived (`group+0x14` clear), the spotter included, is measured to the contact with `Math_DistanceBetweenPoints`. Within 100000 it gets the contact ([`target-selection.md`](target-selection.md#the-sensor-model--sim_detectiontick-004123ac)). Within 50000 the function also raises the **contact's** `+0x9e` (`004127a7`, through `EDI`, the second argument) and activates the **list object's own** `+0x1b2` (`004127ab`-`004127c2`, through `EBX`). The `+0x9e` write does not depend on the list object having an action. Two things call it:

- **A new contact in the sweep.** One that a human-side object other than the player's machine makes, and the reciprocal one that a Cybrid object makes. A contact the player's own machine makes is not shared ([`target-selection.md`](target-selection.md#passes)).
- **A shot striking an object**, with the struck object as spotter and the shooter as contact. The struck object's vtable `+0x50` makes the call: `Mech_ShareContact` (`00411aec`) for a structure, flyer or base object, `Mech_AiOnTakingFire` for a HERC past its own gates ([`ai-targeting.md`](ai-targeting.md#passing-a-contact-on)).

So an engagement action does not wait for a mutual contact. It activates as soon as an object within 50000 of a hostile first spots it, or is shot by it from within 50000.

Each site activates an object's action only while that object's own `obj+0xa2` is clear. `Mech_PerTickSystemsUpdate` raises the byte (`0041abd8`) on the machine's own selected target while its ECM pod's switch is on, which only the player's ever is ([`equipment-pods.md`](equipment-pods.md#what-each-class-actually-overrides)). `Sim_DetectionTick` clears it on everything at the end of its pass ([`target-selection.md`](target-selection.md#passes)). Those are the only two writers `es2_fieldscan.py` finds. `Sim_MainTick` runs the per-mech systems pass straight after the detection tick (`0045f775`, then `0045f7a0`), so the byte is raised again before the next tick's object updates and sweep read it. **Jamming a target holds back its engagement action** for as long as the jammer stays on it. No site gates `+0x9e`, so the target still counts as engaged.

**`+0x1b6` — defeated.** Four sites, and they are the four ways an object stops being a threat. The first three run the object's [out-of-action report](#the-out-of-action-report) just before it; the fourth does not:

| site | when |
|---|---|
| `Mech_ComponentDamageWrite` (`00417de4`) | the machine dies (both of that function's death branches) |
| `Flyer_ComponentDamageWrite` (`00421bb4`) | component 0 goes |
| `Base_ApplyDamage` (`00404d70`) | the last component goes — [`structure-behaviour.md`](structure-behaviour.md#taking-damage--base_applydamage-00404d70) |
| `Ai_ChooseWeapon` (`0041f358`) | the machine runs out of working weapons — [`ai-weapons.md`](ai-weapons.md) |

**This is how a retail mission chains its reinforcement waves.** `TRAIN8.MSN` names one on five of its ten mech records; see the worked example below.

## The deployment gate — `group+0x14`

`DBSim_BuildGroupRecord` (`00423b34`) resolves the block-11 record's action ref (record `0x70`) into the group record's `+0x14` pointer. Non-null means "not deployed yet". Among the places that test it:

| site | effect when non-null |
|---|---|
| `Scene_SubmitFrameObjects` (`0042841c`) | the mech, flyer or base is **not submitted for drawing** |
| `Sim_MainTick` (`0045f464`) | the group runs `Group_DeploymentCheck` (`004236c4`) **instead of** `Group_OrderTick` (`00423a74`); a base's own `+0x18` tick is skipped outright |
| `Mech_CollisionTest` (`00418f74`) | the object is skipped before any distance is measured |

`Deployment_PickPointNearPlayer` (`0042354c`) and `Actions_EvaluateTriggers` apply the same test, so an arriving group never picks a landing point on top of one that has not arrived, and an undeployed group cannot trip a trigger.

**An undeployed group's placed position is therefore meaningless.** It is placed by the ordinary rules — usually on its route's first waypoint, which mission authors routinely share with the player's own squad — so several such groups commonly sit stacked on one point. Harmless in the original, because nothing above can see or touch them — except a held-back structure, below.

### Held-back structures stand from the start

A structure group waiting on an action is not absent: it is built at mission load and stands where it was placed. `DBSim_LoadScriptDat` (`00424308`) marks every structure a block-11 group lists as live, and `DBSim_SpawnMissionObjects` (`004253d8`) constructs each live record with `Base_Construct`, marks its footprint for the flattening pass and, for a paints-ground group, paints its pad with `Base_ApplyFormationTerrain` (`00405db0`). None of the three tests the group's action, so the pad and the levelled ground are there from the first frame.

The gate keeps such a structure from being drawn or ticked, and out of shots, blasts, radar, the HDD map, detection, target cycling and the live-object sweeps. The two structure-pool gathers do not test it: `Structure_GatherWalkCandidates` (`00404ae4`), which feeds `Mech_CollisionTest`, `GroundVehicle_CollisionTest` (`0046a510`) and `Deployment_PickPointNearPlayer`, and `Sim_RaycastShapeList` (`00404bc0`, [`hit-detection.md`](hit-detection.md#the-shape-probe--sim_raycastshapes-00404ca0)), the AI's shape probe. So a held-back static structure is **solid but invisible**: it stops walkers and ground vehicles, refuses arrival points and blocks the AI's probe while nothing is drawn there. An animated type, a gun or missile tower, is gathered by the walk test only as a wreck, so it does not block. When the action fires, verb 1 only clears `group+0x14` ([Arrival](#arrival--group_deploymentcheck-004236c4)), and the structures appear where they already stood. Retail play shows it with `C2_08`'s two supply transports (group 180, held back on action 139): approached before the player has entered action 139's circle they are not there, and after it they stand where their records place them, [one inside the other](#two-vehicles-that-hold-each-other--c2_08s-transports).

Five retail missions hold structures back, 54 records in all, every one of them in a group whose action has verb 1: `C5_04` holds four Cybrid bases of 44 structures, and `C2_08`, `C4_02`, `C4_08` and `TRAIN7` a few supply transports each. `C5_04` fires its bases' actions, 36 to 39, when a member of the player's group (type 1) enters a trigger circle of radius 100,000 (stored as 10,000) centred on the base group's point as the mission records it. The paints-ground anchor move ([`script-dat.md`](../formats/script-dat.md#the-anchor)) then shifts each base within its terrain tile, up to a tile's width from that point, so part of a base can stand outside the circle that reveals it. Base group 134 is anchored at (724480, 906752), about 108,000 from its circle's centre at (740632, 799600) and so some 8,000 outside the circle: a player approaching from the north reaches its northernmost buildings, unseen, before reaching the circle ([`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md), [Open](#open)).

### Two vehicles that hold each other — `C2_08`'s transports

`C2_08`'s group 180 is two SUPPLY TRANSPORTs (type `0x2d`) whose first order is to drive route 61 from point 44 to the guard point, point 45, some 191,000 units east. In retail they never move. Both records name their own position and heading, so `Base_AttachToGroup` (`00405c3c`) leaves each where `DBSim_SpawnMissionObjects` put it: points 44 and 46, 2,069 units apart along Y, both at heading 0, which lines their long axes up along that offset. Type `0x2d`'s `BASES.DGS` shape (13) carries a 6 × 14 height field of 512-unit cells, 3,072 units across and 7,168 along, from 4,096 behind the origin to 3,072 ahead, solid but for one corner cell. Each transport's centre therefore lies over a solid column of the other's, more than 1,000 units from its edge: the front half of one stands inside the rear half of the other.

That holds both for the whole mission. A ground-vehicle type has no animation threads, so `Base_GetCollisionRadius` (`004035b8`) gives it no collision radius, and `GroundVehicle_CollisionTest` (`0046a510`) meets the other vehicle only through `Structure_WalkCollisionTest` (`00427c68`), which refuses any step whose end point lies over a non-zero column of a static structure's height field, whatever the mover's radius. `GroundVehicle_ThinkTick` (`0046a5d0`) answers a refusal by restoring position and heading and zeroing the speed, which `GroundVehicle_LocomotionTick` slews back at 30 units a tick, so the next step, the back-off's included, ends at most 30 units away and still over the other's footprint. Neither vehicle moves or turns again.

## Arrival — `Group_DeploymentCheck` (`004236c4`)

Runs every frame for every waiting group; does nothing until that group's action has activated. Once it has, the action's **verb** (in-memory `+0x02`) picks how the group turns up. Every arrival point is relative to the player and comes from `Deployment_PickPointNearPlayer`.

| verb | arrival | distance | bearing, relative to the player's heading |
|---|---|---|---|
| 2 | drop pod | 150,000 | `0x4000 - (rand & 0x7fff)` — ±90° |
| 3 | drop pod | 150,000 | `0x1000 - (rand & 0x1fff)` — ±22.5° |
| 4 | on foot | 90,000 | `-0x7000 - (rand & 0x1fff)` — behind, ±22.5° |
| 5 | on foot | 150,000 | `0x2000 - (rand & 0x3fff)` — ahead, ±45° |
| other | in place | — | — |

No retail `.MSN` authors verb 4 or 5: the verb runs 0-3 across the corpus ([`msn-mission-file.md`](../formats/msn-mission-file.md#row-10-field-decode--the-action-record-dat_00470660-82-bytesrecord)). The two on-foot arrivals are read from the code alone.

**On foot** places the group's leader at that point facing `bearing - 0x8000` (the bearing itself, not the player's heading plus it), runs each other member through its own vtable `+0x78` formation offset, and clears `group+0x14` immediately. Only the leader is turned.

**In place** (any other verb, e.g. verb 1) just clears `group+0x14`, so the group goes live where it already stands — the one arrival for which the placed position is not a placeholder.

**Drop pod** spawns a `METEOR` and leaves `group+0x14` set; the pod clears it on landing. A byte at `group+0x13` latches the launch, so a group only ever gets one pod.

### Picking the point — `Deployment_PickPointNearPlayer` (`0042354c`)

Offsets from the player's position by the caller's distance at (player heading + the caller's bearing), then steps outward in 2,000-unit increments along the same ray until the point clears three tests, in order. The point keeps the player's own Z, not the ground's.

1. **Deployed objects** — within their own `+0x7c` collision radius plus 5,000. **Applied only for the two walk-on verbs**: the caller's fourth argument is 1 there and 0 for a drop pod, so a pod is allowed to come down on top of a machine. That is what its landing-blast latch exists for.
2. **Structures** — `Structure_GatherWalkCandidates` (`00404ae4`) at radius 5,000, the same volume sweep a walking machine is stopped by ([`hit-detection.md`](hit-detection.md)).
3. **The ground** — `Terrain_FaceBlocksAt` (`0046fe84`) with a zero direction, which reduces it to the steepness of the face under the point. It finds the cell by flat index ([`terrain-heightmap.md`](terrain-heightmap.md#mode-1--the-slope-walk)), so a point past the north or south end of the grid blocks, while one past the west or east edge reads a cell of the neighbouring row and clears whenever that face is walkable.

**Nothing keeps the point on the heightmap, and the loop is unbounded.** The player stands on the grid, so every retry lies further out along the ray: a search that runs off the north or south end never returns, and one that runs off the west or east edge ends at the first walkable wrapped cell, off the heightmap. The heightmap is all the terrain there is, since the drawn region is clamped to the grid ([`terrain-drawing.md`](../rendering/terrain-drawing.md#the-visible-region--terrain_setupvisibleregion-0046ca98)). The point is on it whenever the player is further than the caller's distance from every edge; [A pod aimed off the heightmap](#a-pod-aimed-off-the-heightmap) is what follows when the player is not.

## The drop pod — `METEOR`

Its own class, pool (`g_MeteorPool`, `004a972e`) and resources: `Meteor_LoadResources` (`00409a34`) loads `dts\meteor` and the `dba\impact` texture bank and binds the bank into every shape.

`Meteor_Construct` (`00409b44`) stores the group pointer at `+0x55` and puts the pod on a **slanted** approach rather than dropping it straight down. It draws a heading and a **horizontal run-in** of 70,000 plus up to 25,000 units, places itself that far short of the target along that heading, and flies in at a flat 2,000 units per tick:

```
n      = runIn / 2000                  // ticks of flight
height = (n * n >> 1) * 50             // 30,600 to 55,200 units up
vz     = -height / n
```

**The 70,000-95,000 figure is the run-in, not the altitude.** The launch height is derived from it — what a constant 50 units/tick² would need over that many ticks — and both axes are then flown at constant velocity, so the descent is a straight line. Reading the drawn figure as a height puts the pod at twice the altitude it belongs at.

`Meteor_Tick` (`00409d2c`), walked from `Sim_MainTick` over the pool *before* the group pass, has two phases:

1. **Falling** (`+0x4b == 0`). Integrates position by the velocity at `+0x45`, pitches the shape to `atan2(vz, 2000)` so it faces its fall line, plays sound `0x2f` (`podin2`) once below absolute height 50,000, and on ground contact (`Terrain_HeightQuery`) sets the landed flag, snaps to ground height, plays sound `0x30` (`podland`) and detonates `Damage_ExplosiveBlastSweep(pos, 3000, 10000, 0, null)`. **If anything was in range it sets `+0x4c`** — the sweep answers on range alone, so a machine whose shields swallow the blast still trips the latch. It is the terrain query and not the flight-time count that ends the fall, so a pod aimed at ground well below the player keeps flying past its target.
2. **Landed.** Advances `+0x4d` at rate `0x5dc` per tick and drives the shape's frame counter from `+0x4d >> 10` — the pod opening. When that reaches the shape's frame count: if the pod carries a group and **`+0x4c` is clear**, it copies its own landed position onto the group's **leader** and clears `group+0x14`, which is the moment the group becomes real. A pod that landed on something delivers nothing, and the group it carried stays out of the mission for the rest of the run. Whenever the pod carries a group it then leaves a square mark on the ground at its landing point for the rest of the mission ([`ground-shapes.md`](ground-shapes.md#a-drop-pods-shape)), releases its shape instance and returns 1, and `Sim_MainTick` frees it.

**Both sounds are played at the camera, not at the pod.** Each `Sound_PlayAt` is passed `ViewObjectPtr + 4`, the camera's own position (`00409ed9`: `MOV ECX,[004d256e]; ADD ECX,4; PUSH ECX; PUSH 0x2f`), so where the pod is plays no part in what the player hears, and a point with no offset from the view is panned hard left ([`audio.md`](audio.md#a-sound-played-at-the-camera)).

`Meteor_Render` (`00409cd0`) draws the plain shape (root 0) while falling and the opening animation's own shape instance at `+0x41` (root 1) once landed.

Only the leader is repositioned; the rest of the group follows under its orders. Every retail drop-pod group has exactly one member.

### A pod aimed off the heightmap

A pod aimed past the west or east edge of the grid ([Picking the point](#picking-the-point--deployment_pickpointnearplayer-0042354c)) does not stop at the edge. `Terrain_HeightQuery` (`0046e07c`) answers 0 off the grid, and the target's Z is the player's, so the pod flies on past its target, still descending, until it drops below zero, and lands further out along its own heading. The machine it delivers stands at height 0 where no terrain is drawn, far below the ground at the edge, and where `Terrain_FaceNormalAt` (`0046e394`) finds no cell, and `Mech_CollisionTest` counts that as too steep for a computer-piloted machine ([`mech-locomotion.md`](mech-locomotion.md#collision)). Every step it takes is refused and undone, and so is the reversed retry, so it never moves or turns; it still aims and fires.

The campaign sets this up. In 25 retail missions action 0 is a drop-pod action (verb 3) that the player trips (type 0) on three or four boxes. In `C2_01` the four boxes are strips along the borders of the map, a penalty for wandering out of the mission area, and the action drops three DIABLOs, one per group (47, 48 and 49). On that side the mission's edges come in this order: the mission box ([`mission-objectives.md`](mission-objectives.md#the-status--mission_status-004135e8)) at x = 114,788, where the computer warns `APPROACHING MISSION ZONE BOUNDARY`; the end of the Heads-Down Display map's terrain, the box grown by 60,000 ([`heads-down-display.md`](heads-down-display.md#the-maps-frame-of-reference)), at x = 54,788; the mission abort, the box grown by 110,000, at x = 4,788; and the heightmap's own edge at x = 0. The west strip runs from x = 118,064 to 213,068, inside the box. A player who walks into it from the east, heading west, aims the pods at least 63,000 units inside the grid, and the DIABLOs walk in along route 0, the squad's own. One who trips it within about 150,000 units of the heightmap's edge and facing it aims them past that edge, beyond the abort line, and they never move ([Open](#open)).

## The lift start

A mission start left over from Metaltech: Earthsiege, in some of whose missions the player's HERC starts underground and rides an elevator lift up to the surface: the cockpit brightens through a palette shift as it nears the top, and the ride ends in a camera shake. DBSIM keeps the whole sequence, but it draws `dba\intro`, which no retail VOL contains, and nothing found sets its gate ([Open](#open)).

`Sim_InitMissionSession` (`004614fc`) gates it on the `0xc3`-byte global block's `+0x54` (`004d2594`) and tests the flag twice:

- **`00461cc6`** — nonzero calls `LiftStart_DarkenPalette` (`0045d52c`). Zero instead sets `0049aef6` to 8, calls the empty `Palette_NoOp` (`0042eb40`), flushes the palette and renders one frame; the lift branch makes the first two of those itself and skips the others.
- **`00461e20`**, near the end of bring-up — nonzero calls `LiftStart_Rise` (`0045d840`).

### The darkening — `LiftStart_DarkenPalette` (`0045d52c`)

Copies the live palette into two new palette objects, rewrites entries 32-47 and 64-79 of the first as (G/2, R/2, B/2) — **red and green swapped as well as halved** — and installs those two spans live. It then starts a `0x78`-coarse-tick (1.92 s) cross-fade from the darkened object to the untouched copy ([`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md#palette-module)), and clears `0049aef4` ([Open](#open)). In ES2's palette the two spans overlap both ends of the cockpit scheme's window, slots 42-65 ([`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md#palette)).

### The ride — `LiftStart_Rise` (`0045d840`)

Loads `dba\intro`, the lift's art, and uses its first frame without checking the load. It then **raises** the player's machine from 4000 units below its placed height to that height, 35 units a frame, with sound `0x21` (`explo4.wav`) running. The loop only renders — `Sim_MainTick` does not run, so nothing else in the mission moves. Each frame it:

1. Puts the machine at the current height and rebuilds the view from it.
2. Projects the view-space point (0, 1000, −camera z) to a screen row. That point is at world height 0, 1000 units ahead, so the surface the lift rises to is the world's zero plane, not the terrain under the machine.
3. Renders the scene with the viewport's bottom cut to that row, or to its top while the row is at or above 0.
4. Draws the lift: `intro` from the row down, then colour id 19 from the art's bottom edge to the viewport's bottom — the whole viewport while the art is still above it.
5. Paints the cockpit overlay at `CockpitViewInstance+0x1f5`, steps the cross-fade while the row is below −200, and ends the frame with `Sim_EndFrame`.

**The brightening can stop part-way.** The fade runs on the clock from its first step, and only this loop steps it. A ride that reaches the top less than 1.92 s after the row passed −200 leaves the 32 entries where its last step put them, and even a completed fade never writes its final colours ([`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md#palette-module)).

At the top it stops `0x21`, plays `0x29` (`explo2.wav`) and shakes the view for `0x1e` coarse ticks (0.48 s): a band of 5 — a literal, half the damage shake's `5 << VideoMode_YCoordShift` — and one `(next & 0xffff) % 5` step a frame on the [presentation generator](random-generator.md#the-presentation-generator). **None of the shake reaches the screen.** The shake loop calls `Sim_RenderFrame` without `Sim_EndFrame`, the only per-frame present ([`cockpit-views.md`](cockpit-views.md#presentation)), so the last frame of the ride stays up while `0x29` plays. It then clears the shake, frees the two palette objects and their entry buffers, and sets `0049aef4` back to 1.

## The mission counters — `DAT_004a9ef4`

1,000 shorts, and the **campaign's** flag array for the length of a mission. It makes a round trip through `data\mission.var`: the shell writes the file from its own flag array before launch and reads it back at debrief ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#the-files-crossing-between-the-two-binaries)). The simulator's two ends of it:

1. `DBSim_LoadScriptDat` (`00424308`) reads 2,000 bytes of it into `DAT_004a9ef4`, before it opens `player.mec`, and then zeroes slot 20, slot 10 and slots 21 to 42. Every other slot carries the campaign's value into the mission. Slots 21 to 42 are the 22 entries of the debrief's weapon-unit table ([`../formats/weapons-dat.md`](../formats/weapons-dat.md#campaign-grants--armory_grantcampaignweapons-004126be)), and the loop (`00424450`) stops at the table's last, so a mission starts with no unit grant owed. The debrief's grant loop runs on to slot 49, past the table's end; those seven slots name no weapon, and the load leaves them as the campaign left them.
2. `Mission_WriteResults` (`0042412c`) writes, as the mission ends, `results.dat` and then the same 2,000 bytes back to `mission.var` ([`mission-objectives.md`](mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)).

Four things write the counters during a mission: `Action_Activate`, an objective ([`mission-objectives.md`](mission-objectives.md)), an object or a whole group going out of the fight ([below](#the-out-of-action-report)), and `Mech_CreditNeutralisedTarget` (`00415710`), which adds one to slot 10 when the player puts a machine of its own group out of the fight ([`component-damage.md`](component-damage.md#what-the-attacker-is-told--mech_creditneutralisedtarget-00415710)).

The simulator reads slot 20 itself: `Mission_WriteResults` adds 25,000 kg of salvage per unit of slot 20 to the award it writes to `results.dat`.

### The out-of-action report

`Mech_ReportOutOfAction` (`00411bc8`) writes the counters an object is set to write when it goes out of the fight. `es2_xref.py` finds four calls to it, each immediately before the object's defeat action ([`+0x1b6`](#an-objects-own-two-actions--0x1b2-and-0x1b6)): both of `Mech_ComponentDamageWrite`'s branches ([disabled and dead](component-damage.md#going-out-of-the-fight)), `Flyer_ComponentDamageWrite` when component 0 goes, and `Base_ApplyDamage` when a structure's last component goes. The fourth defeat-action site is not among them: `Ai_ChooseWeapon`'s weapons-out block (`0041f554`-`0041f596`) latches `+0xa5`, activates `+0x1b6` and defers the [objective poll](mission-objectives.md#the-poll--mission_pollstatus-004131ac) by raising its counter (`004a9ee7`) to at least 1000, and `Action_Activate` is its only call. A machine that runs out of weapons writes none of its counters.

It does two things:

1. **The group's report.** `Group_ReportIfAllOutOfAction` (`00423f30`) walks the object's group, skipping the object itself, and returns at the first member that is neither destroyed (`+0x99`) nor immobilised (`+0xa4`). It does not test `+0xa5`, so a disarmed member counts as standing and holds back its group's report. If none is left standing it runs the group's own ten slots at `group+0x1c`/`+0x30`. The reporting object is skipped rather than tested because the leg branch reports before it latches `+0xa4`. Nothing latches the group's writes; they run once because each member reports once and only the last one standing finds the rest down.
2. **The object's own.** Ten slots at `obj+0x1ba` (counter refs) and `obj+0x1ce` (operations).

Each slot with a non-negative ref writes that counter by its operation. These are neither the action layer's codes nor the objective layer's ([`mission-objectives.md`](mission-objectives.md#the-record)):

| op | write |
|---|---|
| 1 | zero it |
| 2 | add one |
| `0x0d`-`0x10` | store op − `0x0c`, 1 to 4 |
| anything else | nothing |

The slots come from each object's `script.dat` record ([`../formats/script-dat.md`](../formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps)), copied in by `SimObject_SetOutOfActionCounters` (`00411b90`), and the group's from its block-11 record, record 0 included. `es2_xref.py` finds three calls to the copy, all in `DBSim_SpawnMissionObjects` and all on the roster side of its `index < rosterCount` test, so a machine of the player's squad does not get its slots from a record. Its slots start at zero instead: `DBSim_LoadScriptDat` builds the mech pool (`004a9bfe`) afresh for each mission with `Pool_Init` (`004719cc`, at `00425196`), which zeroes the pool's storage, and `Mech_Constructor` then sets refs 0-2 to −1 (`00415c3a`-`00415c51`). Every operation reads 0, so a squad machine's report writes nothing of its own ([Open](#open)).

Across the 62 `.MSN` missions, 32 use the mech slots, 31 the structure slots, 22 the group slots and one the flyer slots. Two uses account for all but six of the 1,258 filled slots:

- **Op `0x0d` on a machine**, 420 slots. 412 of them set a flag in 22-41, the weapon grants [`../formats/weapons-dat.md`](../formats/weapons-dat.md#campaign-grants--armory_grantcampaignweapons-004126be) reads at debrief: putting the HERC out of the fight earns one unit of that weapon. The weapon is one the machine carries in 400 of the 412; the other twelve name a weapon outside its fit, eight of them `L300` on a machine carrying weapon 8. The write is a store, so several machines setting one flag still grant one unit. The last eight slots set counter 20.
- **Op 2 on a structure**, 756 slots: counter 20, the 25,000 kg salvage bonus above, in 64 of them, and in the other 692 a counter of the structure's own from 100 up, which goes back to the campaign's flag array with the others.

The six others carry op 6 or `0x17`, which write nothing here.

## `TRAIN8.MSN` end to end

A worked example, because it is the only place the four mechanisms are visible together. `TRAIN8.MSN` (Scramble) fields 3 actions, 1 trigger area (two row-9 records sharing a GUID), 0 action timers, and 8 Cybrid HERCs in six groups; group numbers are `script.dat` block-11 record indices:

| stage | what activates it | who arrives |
|---|---|---|
| start | — | group 2, one HERC, north of the player; group 8, two flyers |
| wave 1 | group 2's machine dies (`+0x1b6` → action 0), **or** it walks into action 0's circle | groups 3 (two HERCs) and 4 (one), **in place** |
| wave 2 | either of group 3's machines dies (`+0x1b6` → action 1) | group 5, two HERCs, in place |
| wave 3 | either of group 5's machines dies (`+0x1b6` → action 2) | groups 6 and 7, one HERC each, **by drop pod** |

Action 0's circle is centred at (1005988, 1058404) with radius 150,000 and its subject is type 3 — deployed **Cybrid** groups, not the player. The player spawns inside it at 64,132; group 2's machine spawns north at 252,252 and walks south, crossing in at tick 1222 (~49 s at 25 Hz). So the first wave arrives on its own if the player does nothing, and sooner if the player kills the machine. Actions 1 and 2 carry no area, so their only route is the kill.

The Cybrid HERCs' chassis are not fixed. Each of the eight roster records names a type-3 variant key, so every HERC draws its own chassis (DIABLO, ACHILLES, HYPERION or HEADHUNTER) when the mission loads ([`msn-mission-file.md`](../formats/msn-mission-file.md#variants)). The counts, groups, actions and routes above are the same on every load.

## Rejected readings

| reading | why it is wrong |
|---|---|
| An action with no trigger area is unreachable | Three other things activate it; a mission's later actions routinely carry no area at all |
| `Meteor_Construct`'s 70,000-95,000 is the spawn altitude | It is the horizontal run-in. The altitude is derived from it and is 30,600-55,200 |
| `Deployment_PickPointNearPlayer` avoids deployed objects | Only for the walk-on verbs; a drop pod's point is picked without that test |
| `Actions_EvaluateTriggers` runs before the group pass | `Sim_MainTick` runs it after, so a group arrives a tick after its trigger |
| `obj+0x1b6` is a death action | It is also activated when a machine runs out of weapons |
| The load's zeroing of slots 21-42 misses the last seven weapon grants | The debrief's grant loop reads to slot 49, but its unit table ends at 42. The seven slots past it name no weapon |
| A disarmed machine counts as out of the fight for the out-of-action report | `+0xa5` sits beside the two damage latches, but the weapons-out branch runs no report and `Group_ReportIfAllOutOfAction` tests only `+0x99` and `+0xa4` |
| `Detection_ShareContact` engages the object whose action it activates, as `Detection_Sweep` does | The `+0x9e` write goes to the contact (`EDI`) and the activation to the list object (`EBX`), so the two land on opposite sides |
| `obj+0xa2` can only suppress a duplicate activation within one tick, since `Action_Activate` is one-shot and the detection tick clears the byte | The per-mech systems pass raises it again straight after the clear, so it is up at every gate for as long as a jammer holds the target |
| An action's message is a `data\mission.str` line | That file holds the objective text, and the id looks like a ref into it. The port it is posted to resolves a speakerless id in `COMMAND<n>.STR` |

## Open

- **Open:** a retail play check of [a pod aimed off the heightmap](#a-pod-aimed-off-the-heightmap): in `C2_01`, tripping the west strip within about 150,000 units of the heightmap's west edge while facing west should leave three DIABLOs standing past that edge, firing but never moving. In retail play they have been seen walking in, on a run that tripped the strip further from the edge.
- **Open:** a retail play check of [held-back structures](#held-back-structures-stand-from-the-start): walking into one of `C5_04`'s bases from the north before its circle is reached should be stopped by buildings that are not drawn.
- **Open:** what sets block `+0x54`, the lift start's gate, which both tests read as a word. No absolute operand in the image addresses `004d2592`-`004d2595`, and `Main_StaticInit` (`0045cad8`) clears it with the rest of the block. Of the 62 code references to the block's base, 45 push it to the blit helpers, which only read it, two read its first dword, and fifteen load it into a register. The nine writes at `+0x51`-`+0x54` that `es2_fieldscan.py` finds inside those fifteen functions go through their own first argument, none of their eight call sites passes the block, and none of the writes at `+0x55` is in them. No constant below the block plus a displacement reaches the word either; the one unbounded array beneath it, `SimCommandQueue_Push`'s queue at `004d2148`, would need 550 commands in one frame to get there.
- **Deferred:** what reads `0049aef4`, the byte the lift start clears for its duration (1 in the image). `es2_xref.py` finds only the lift's two stores (the same sweep finds both references to its neighbour `0049aeea`). Every other access in `0049aee0`-`0049aef8` is by absolute address and none is wide enough to overlap the byte, and the nearest immediate below the block, `0049aeb4`, is `CockpitClipRegions_Load`'s exception table, so nothing found reaches the byte through a base either.
- **Open:** whether anything writes a player-squad machine's slots (`+0x1ba`-`+0x1e1`) after construction. No writer found beyond `Mech_Constructor`, `Base_Construct` and `SimObject_SetOutOfActionCounters`, by `es2_fieldscan.py` over the whole span. After `Mech_Constructor`, the squad side of `DBSim_SpawnMissionObjects` itself writes only the machine's position, heading, `+0x29c` and, on the player's own, the locally-piloted flag, and runs `Mech_ConfigureLoadout` and `Mech_ApplySquadCondition` (`00415068`), which writes through the damage header's array pointers alone ([`component-damage.md`](component-damage.md#a-squad-machines-condition--mech_applysquadcondition-00415068)). The scan sees a loop that steps a pointer through the slots at its first access, as it sees the copy's loop and `Mech_ReportOutOfAction`'s two. Every `REP MOVSD` large enough to reach the slots (`0x81` or `0x101` dwords) is in render or palette code, and every fixed-size `_memcpy` in the simulation copies at most `0x14` bytes. A writer that derives its pointer from an offset outside the span would still be missed.