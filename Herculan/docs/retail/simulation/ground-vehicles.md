# Ground vehicles

The mobile ground units: supply transports, mobile missile launchers and the other `BASES.DAT` ([`bases-dat.md`](../formats/bases-dat.md)) types `0x2d`-`0x34` and `0x37`-`0x3d`. They are built as structures. `Base_Construct` gives these types the GroundVehicle class and target class 3 ([Five classes, one switch](structure-behaviour.md#five-classes-one-switch)), and everything they share with a building is [`structure-behaviour.md`](structure-behaviour.md): the armed tick they fight with, the turret seek, how they take damage and where they are aimed. This doc owns the GroundVehicle class's `+0x18` tick and the movement it adds, which no other structure class has.

## Timing constants

Countdowns are in the simulation's timer unit, about 0.49 ms a count ([Timer units](dbsim-physics-notes.md#timer-units)).

| Constant | Units | Real time |
|---|---|---|
| Back-off `+0x223` | 3000 | 1.5 s |

## The ground vehicle tick — `0046a5d0`

Gated on the group's first member answering `targetClass == 3`, so a ground-vehicle type dropped into a group led by anything else is an ordinary building.

```
if (typeRec+0x2e == 0) Base_ThinkTick(this)
else { 00404100(this); if (no target) TurretSeek(this, -turretAz, turretEl) }
if (!destroyed) {
    save position, pitch, heading
    GroundVehicle_Advance(this)              // 0046a70c
    SimObject_ConformToTerrain(this)       // 004029d8
    if (GroundVehicle_CollisionTest(this)) { // 0046a510
        restore the save; speed = 0
        back-off timer +0x223 = 3000, reverse flag +0x227 = (speed > 0)
    }
}
```

So a ground vehicle fights with [the armed tick](structure-behaviour.md#the-armed-tick--00404100) and moves with its own, and its block handling is `Mech_MovementTick`'s: restore the step and arm a back-off rather than detonate. **This is [the turret seek](structure-behaviour.md#the-turret-seek--base_aimturret-00403eec-and-00403d5c)'s second caller**: with nothing acquired, the two turret angles are fed straight back in negated, which walks the turret to centre.

- **`GroundVehicle_CollisionTest` (`0046a510`)** is `Mech_CollisionTest`'s two object sweeps standing alone — the same group `+0x14` action gate, the same asymmetric vtable `+0x5c` against `+0x7c` radius pair, and the same `Structure_GatherWalkCandidates` volume sweep behind them, whose result is the return value. The vehicle is itself in the structure pool, so it passes itself as the gather's one excluded structure (`0046a5ad`); `Mech_CollisionTest` and `Deployment_PickPointNearPlayer` pass none. It drops the machine's other two arms: there is no terrain test, so a ground vehicle drives up anything, and a block does no damage to what was hit. It does still call the blocker's vtable `+0x68`, which makes it the **second writer** of the `obj+0xb1` latch a ramming machine detonates on — see [`ai-combat-states.md`](ai-combat-states.md#the-charge--mech_behaviourramtick-0041e488).
- **`GroundVehicle_Advance` (`0046a70c`)** picks the group leader (`0046a4b8`: the first of up to four group members that is neither immobilised nor destroyed — so unlike a HERC group, a convoy promotes when the vehicle in front goes down), steers as leader or follower, then steps the position forward by `+0x220` along model Y, through the vehicle's whole frame rather than its heading alone.
- **Leader (`0046a8e4`)** drives the group's route: no waypoint after the cursor means steer 0 and speed 0; otherwise drive at it on the bearing between the two waypoints, and advance the cursor on arrival.
- **Follower (`0046a95c`)** keeps formation on the leader through its own vtable `+0x78` slot. Inside 90° of the leader's heading it matches speed — `leaderSpeed - alongTrackError >> 5`, clamped to `+0x100`/`-0x96` — and drives at a point 20000 ahead of its own post along the leader's heading, with no lateral steering term at all; outside it, it abandons the leader's heading and turns at the post itself at `distance >> 5`. The leader's speed it matches is that object's own `+0x220`, not its vtable `+0x38`, which answers zero for every structure.
- **The post is anchored on one object and rotated by another.** `Base_ApplyFormationOffset` (`00405c04`) is handed the *able* leader's position, but `Formation_RotateAndAddOffset` (`00411d64`) reaches past its caller for the group's member array slot 0 and rotates the `BFORMS.DAT` offset by that object's heading. The array is never compacted, so once the vehicle in the lead slot is destroyed a convoy is anchored on its new leader while still dressed on the wreck's last heading.
- **`SimObject_ConformToTerrain` (`004029d8`)** sits the vehicle on the ground — [below](#terrain-conform--simobject_conformtoterrain-004029d8).

### Terrain conform — `SimObject_ConformToTerrain` (`004029d8`)

It samples the ground at ±r forward and ±r right (`r` from vtable `+0x10`, the shape's own radius), takes pitch from `Math_Atan2Bam(2r, forward - back)` and roll from the left/right pair, and sets Z to the mean of the four samples. This is how a vehicle sits on a slope, and it is the only thing in the simulation that writes a structure's pitch and roll. It is not the vehicle's alone: `FlatObj_Draw` calls it for every ground shape ([`ground-shapes.md`](ground-shapes.md#the-draw-pass)).

### The control law — `0046a798` and `0046a854`

Everything above decides a steer and a speed and hands the pair to `0046a798`, the ground vehicle's `Mech_LocomotionTick`:

```
if (Math_CountdownTimerTick(&this+0x222) != 0)          // a back-off is running,
    speed = this+0x227 ? -200 : 200                     // and overrides the speed, not the steer
steer = clamp(steer, +/-0x100)
Math_RateLimitedMoveToward(&this+0x220, speed, 0x1e)    // the speed slews
heading += Q8(200, steer)                               // the steer *is* the heading change
```

**There is no turn rate and no inertia in the heading**: the clamped steer becomes heading within the same tick, at a little over 200 binary-angle units at full lock. Only the speed is rate-limited. Nothing here is scaled by the tick length — the steer gain, the speed slew and the forward step are all per-call constants, as the turret seek's are.

`0046a854` is the drive-to-point both steering halves call, and the counterpart of `Ai_DriveToPoint`. Same 10000-unit arrival range, and two differences:

- **It does not steer at the point it is given.** It steers at a point offset from that one along the caller's stated bearing by `9000 - range`, so from 9000 out to 41767 the aim point sits *short* of the destination, back down the incoming line, and inside 9000 it swings past. Against the leader's route bearing that pulls a convoy onto the leg between two waypoints instead of letting each vehicle cut its own corner.
- **The offset wraps at 16 bits.** `Math_OffsetPointByBearing` (`004928f0`) hands the distance to `Math_ScaleByCos` and `Math_ScaleBySin`, which pass its low word to `Math_Q14Multiply_Thunk` (`MOVSX EDX,word ptr`) and sign-extend a 16-bit product (`MOVSX EAX,AX`). So the aim point is always within 32768 of the destination along the leg, past it or short of it as `9000 - range` falls in its 65536-unit cycle. On a long leg the vehicle therefore heads for the far waypoint, not for a point back along the line: `C1_06`'s group 33 starts 245,934 units out by `Math_FastMagnitude2D`, its offset `-236934` wraps to `+25210`, and it drives up the leg toward the rendezvous. Carried at full width, the same offset puts the aim point behind the vehicle and turns it round.
- **Its steering gain is four times a HERC's** — the bearing error over 16, not over 64.

A speed of zero means "none stated" and takes `0xaa`, the same default `Ai_DriveToPoint` uses.

**`C1_06` exercises both arms.** Its groups 4 and 33 are four-vehicle convoys led by a supply transport (type `0x2d`): group 4 travels out and back along a three-waypoint route, and group 33 drives a two-waypoint route to the rendezvous with the Maverick. In retail play group 33 is met head-on beyond the player's first waypoint, four vehicles in formation driving toward the player.
