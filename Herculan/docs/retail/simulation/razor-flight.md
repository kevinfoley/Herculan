# Razor flight — flight model, contact probes, and the flight ceiling

The RAZOR is a **HERC-class object with a flyer flag**, not an instance of the `Flyer` class the SKIMMER uses. It is built by `Mech_Constructor`, carries a mech's 29-component damage array and a mech's weapon mounts, and appears on the target list as target class 0, a HERC (`obj+0x1a8`). What the flag (`typeRec+0x50`, file offset 78) changes is which code paths it takes, and it changes nearly all of them.

[`mech-locomotion.md`](mech-locomotion.md) covers the walker paths; nothing in it applies to a RAZOR. The flight model's parameters come from `fm\<NAME>.FM`, laid out in [`../formats/flight-model-fm.md`](../formats/flight-model-fm.md); `fm[N]` below is that file's field at byte offset N.

## Call graph

| Address | Name | Role |
| --- | --- | --- |
| `0041bb9c` | `Razor_ApplyFlightInput` | Input hand-off. Replaces `Mech_ApplyThrottleInput` **and both turret ticks** |
| `00466a54` | `FlightModel_Step` | The flight model. Settles throttle, airspeed, drag, angular rates, attitude, velocity |
| `004198f4` | `Razor_MovementTick` | The per-tick move. Replaces `Mech_MovementTick` |
| `0041b130` | `Player_PerFrameCockpitUpdate` | Its throttle exchange has a flyer branch — see [Throttle](#throttle) |
| `0041bb3c` | `Mech_GetDisplaySpeedKph` | Flyer branch maps airspeed through `Math_MapRange` (`0047de3c`) |
| `00415498` | `Mech_GetSpeed` | Returns `mech+0x2bd` for a flyer, a scaled `mech+0x28e` for a walker |
| `00467a24` | `Math_RateLimitedMoveTowardInt` | 32-bit twin of `Math_RateLimitedMoveToward`; airspeed is an int |
| `004669dc` / `00466a1c` | `Math_IntegrateVec3IntOverTick` / `...ShortOverTick` | `Math_IntegrateRateOverTick` over a vec3 |
| `00466984` | `Math_MeanVec3Short` | Component-wise mean of two vec3s, into the static at `004d3bdc` |

### How the flyer paths are reached

`Mech_Constructor` (`00415bb0`) picks one of three **behaviour class** instances by (is this the local player `mech+0xa3`, does the type record set the flyer flag):

| Condition | Behaviour state |
| --- | --- |
| Not the player | `004993a4` — `deciding` |
| Player, walker | `004993e2` — `player` |
| Player, flyer | `00499420` — `player fly` |

These are states 0, 1 and 2 of the 22-entry AI behaviour table, and the names are the game's own; the full roster and the dispatch mechanism are in [`ai-dispatch.md`](ai-dispatch.md). Each descriptor holds three pointer-to-member-function triples `{func, thisDelta, vtableIndex}` filled in at startup from a 0x24-stride source block. Block 1 (`0049991c`) is the walker set and its `+0x0c` slot is `Mech_MovementTick`; `FlyerBehaviourSlots` (`00499940`) is block 2 and its `+0x0c` slot is `Razor_MovementTick`. Because these are member pointers reached through the vtable dispatchers rather than vtable entries directly, Ghidra reports no xrefs on either move function.

**Only the player's RAZOR flies.** An AI-controlled one takes the not-the-player branch and the walker move, which would walk it ([Open](#open)). The Cybrid aircraft are a different class entirely and fly under their own AI — see [`ai-flyers.md`](ai-flyers.md).

The input side is gated separately, in `Sim_PollPlayerInput` (`00460764`), on the flyer flag alone.

## Flight state — `mech+0x2b9`

`Mech_Constructor` zeroes 0x4e bytes from here, seeds the airspeed at 1000 and copies the machine's throttle into `+0x2d7`. Every flyer path addresses the block through a single pointer.

The `Flyer` class ([`ai-flyers.md`](ai-flyers.md)) carries the same block as its last 0x4e bytes, at `flyer+0x243`: `Flyer_ApplyFlightCommand` (`004221a8`) hands `FlightModel_Step` that address in the argument where `Razor_ApplyFlightInput` hands it `mech+0x2b9`, and `0x243 + 0x4e` is the flyer's whole length. Subtract `0x76` from the offsets below for a flyer's — `flyer+0x287` is the bank heading rate.

| Offset | Type | Meaning |
| --- | --- | --- |
| `+0x2b9` | i32 | Body velocity X — sideslip. **-X is port**, see [Contact probes](#contact-probes) |
| `+0x2bd` | i32 | Body velocity Y — **airspeed**. What `Mech_GetSpeed` returns for a flyer |
| `+0x2c1` | i32 | Body velocity Z — vertical |
| `+0x2c5` | i32 x3 | World velocity. What `Razor_MovementTick` integrates into the position |
| `+0x2d1` | i16 | Pitch rate |
| `+0x2d3` | i16 | Roll rate |
| `+0x2d5` | i16 | Yaw rate |
| `+0x2d7` | i16 | Throttle setting, ±0x400 — **not** the same field as the walker's `mech+0x290` |
| `+0x2d9` | ptr | Back-pointer to the object's own transform block at `mech+0x0c` |
| `+0x2dd` | i16 x10 | Last tick's rotation matrix, transposed |
| `+0x2f1` | i32 x3 | Last tick's position, negated and rotated — the inverse translation |
| `+0x2fd` | i32 | The heading rate the current bank is producing |

`mech+0x28e`, the walker speed scalar, is **never written** on a flight path. That is deliberate — `Mech_GetSpeed` branches specifically to avoid it — and it is why the cockpit throttle gauge's speed bar sits dead on a RAZOR (see [`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md)).

## Axis remapping

The device layer hands the same four axes to both control paths. A flyer reads them as an aircraft's:

| Device axis | Walker | Flyer |
| --- | --- | --- |
| `+0x0e` stick X | Steering | **Aileron** |
| `+0x10` stick Y | Throttle | **Elevator** |
| `+0x12` | Turret twist | **Rudder** |
| `+0x14` | Turret pitch | **Throttle** |

Neither turret tick is on this path, so **a RAZOR's turret never moves** and its guns point where its nose points, toed in on the range to its target ([Gun convergence](#gun-convergence)). The throttle has to move off stick Y because on an aircraft the primary stick axes are pitch and roll, and it lands on the axis a walker has no other use for.

The keyboard reaches these axes through the same source table as the stick — see [The keyboard](../formats/joystick-input.md#the-keyboard).

## Control law (`FlightModel_Step`)

Nothing in it moves the aircraft; it produces the world velocity `Razor_MovementTick` integrates. The order below is the function's own.

### Throttle

An analogue throttle axis is read as a position, `axis << 3` clamped to ±0x400. Everything else is a rate: `IntegrateRateOverTick(Q8(100, axis))` accumulated into `+0x2d7` and clamped the same way. Unlike the walker's throttle lever there is no inverted sense and no clamp to one side of zero.

`Razor_ApplyFlightInput` then copies `+0x2d7` onto `mech+0x290` and sets the `mech+0x93` dirty flag, but **only on a tick the throttle axis moved**. The reverse direction — gauge to flight model — is in `Player_PerFrameCockpitUpdate`, which with the dirty flag clear writes the gauge's value to `mech+0x2d7` as well as `mech+0x290`, gated on the flyer flag. That single line is the only path by which the cockpit slider reaches the flight model.

### Airspeed

```
demand = fm[46] + Q10(fm[42] - fm[46], (throttle + 0x400) >> 1)   // idle and top airspeed
demand -= Q10(pitch < 0 ? 250 : 62, pitch)
RateLimitedMoveTowardInt(airspeed, demand, IntegrateRateOverTick(fm[10]))   // thrust response
```

**Airspeed is not thrust and pitch is not momentum.** Attitude biases the speed the throttle *asks for*, four times as strongly nose-down as nose-up, and the aircraft slews toward it at a fixed rate. A dive is fast and a climb is slow, but level out and the speed returns to whatever the throttle wants. There is no energy to trade.

### Sideslip drag

The sideways and vertical components of body velocity — forward excluded, which is what makes this drag rather than braking — are rotated into world space, scaled by the lateral drag (`fm[50]`), rotated back through **last tick's** frame, and subtracted. This is what keeps the aircraft flying where it is pointing instead of drifting round its own turns.

Only the two ground-plane components are scaled; the world-vertical one is subtracted at an effective coefficient of 1 (`00466c26`-`00466c67` scales two of the three). The asymmetry is load-bearing: a RAZOR sheds vertical speed far harder than sideslip, which is why it settles onto its flight path rather than floating.

### Angular rates

| Axis | Command with input | Command without | Damping |
| --- | --- | --- | --- |
| Pitch | `Q8(fm[0], elevator)` | `-pitch >> fm[22]` | `-Q10(fm[18], pitchRate)` |
| Roll | `Q8(fm[2], aileron)` | `-roll >> fm[26]` | as above, but only when the stick fights the roll already under way |
| Yaw | `Q8(fm[4], -rudder)` | — | always `-Q10(fm[18], yawRate)` |

**Pitch self-levelling is switched off on retail data.** Both files state a shift of 16, and a 16-bit angle shifted 16 is nothing. An aircraft holds the attitude it is trimmed to and bleeds only its pitch *rate* away — which is why a RAZOR left nose-up climbs until the ceiling stops it. Roll self-levels for real, and its branch stops the wings exactly at level rather than letting the term overshoot into a wallow.

Each command is clamped to its acceleration limit, the damping is added *outside* that clamp, the sum is integrated, and the resulting rate is clamped to its rate limit. Yaw borrows the roll axis' acceleration limit; the file has only two.

### Lost wings and nacelles

`Razor_ApplyFlightInput` hands the model four flags, one per destroyed component: the wings (7 left, 8 right) and the nacelles (4 left, 5 right). A lost component takes the controls away:

| Lost | Effect on the command |
| --- | --- |
| Either nacelle | The elevator is replaced by `-cos(roll) >> 6`, a fixed nose-down demand resolved through the bank |
| Right nacelle | The aileron is pinned at full deflection, `+0x100` |
| Left nacelle | The aileron is pinned at `-0x100`. Both nacelle tests come before the wing tests, so a nacelle overrides any wing |
| Right wing | While the roll is under `0x1000` (22.5 degrees), the aileron gains `Q14(0x14, 0x1000 - roll)` |
| Left wing | While the roll is over `-0x1000`, the aileron loses `Q14(0x14, roll + 0x1000)` |

With a nacelle gone, pitch and roll are out of the pilot's hands. A lost wing leaves the pilot control: the bias fades to nothing at 22.5 degrees of bank, so the aircraft settles into a permanent lean that the pilot can hold off but has to keep holding off.

### Turning is banking

```
bankTurnRate = |roll| < 0x4000 ? -roll >> fm[30]
                               : (short)(roll - 0x8000) >> fm[30]
```

The rudder yaws the airframe about its own axis, but what swings the nose round the sky is the bank. The rate is read straight off the bank angle and applied to the heading **on top of** the integrated attitude, so a banked RAZOR turns about the world's vertical axis and not its own. Past a quarter turn of bank the sense inverts, measured from the half turn, so an inverted aircraft turns the way its wings say.

### Attitude

The rotation is integrated as a **matrix**, not as three angles: a delta matrix is built from the mean of this tick's rates and last tick's (`Math_MeanVec3Short`), composed onto the current rotation, and the euler triple read back out of the result. That is what keeps a RAZOR flyable through a vertical climb where integrating the angles directly would gimbal, and it is the only place in the simulation that composes a rotation this way. The matrix the function writes is then invalidated immediately by the heading change, so the euler round-trip is what actually survives.

Finally the world velocity is re-expressed in the new body frame. That costs forward speed whenever the airframe rotates; `Q10(900, loss)` of it is handed straight back, so a hard turn scrubs about 12% and no more.

### The flight ceiling

```
ceiling = fm[38] + Q16(airspeed - fm[46], fm[14])   // fm[14]: the slope the loader derives
```

**Altitude is bought with speed.** The RAZOR's ceiling runs from 6000 world units (36 m) at its 250 idle airspeed to 60000 (360 m) at its 1500 maximum. A pilot who wants height has to go and get it at full throttle; one who throttles back is pushed back down.

Nothing clamps to it. Past the ceiling the model builds a push proportional to the overshoot (Q10 gain 10) and resolves it through the current bank — cosine onto pitch, the quarter-turn shift onto yaw — so the push is toward the *ground* however the aircraft is lying. The yaw command is replaced by its share of the push outright; the pitch command is replaced only when the push is the lower of the two, so it overrides a climb or a shallower dive and never a steeper one.

## Contact probes

`Razor_MovementTick` has **no swept body test and no terrain clamp on the airframe as a whole**. Six points are checked instead: four against the ground, and all but the fuselage swept forward as a ray one tick's travel long through `Sim_RaycastObjectList`, so a wing catches a building as readily as a hillside. Two of the ground tests sample somewhere other than their own point: [The probes share one transform](#the-probes-share-one-transform).

The components are the game's own, from `STRINGS0` group 14, the flyer damage-readout list the Heads-Down Display takes in place of the walker's group 13 (see [`heads-down-display.md`](../formats/heads-down-display.md)):

| Component | Name | Probe point | Clearance | Ground test | Reaction |
| --- | --- | --- | --- | --- | --- |
| 7 | `L WING ARMOR` | `(-1000, -700, -100)` | 300 | yes | Roll away, `Q10(4000, depth)` or a flat 4000 |
| 8 | `R WING ARMOR` | `(1000, -700, -100)` | 300 | yes | as above, opposite sign |
| 4 | `L NACELLE ARMOR` | `(-450, -500, 0)` | 150 | no | Flat roll kick of 8000 |
| 5 | `R NACELLE ARMOR` | `(450, -500, 0)` | 150 | no | as above, opposite sign |
| 0 | `COCKPIT ARMOR` | `(0, 1000, 0)` | 200 | yes | Pitch **up**, `Q10(2000, depth)` or a flat 2000 |
| 6 | `FUSELAGE ARMOR` | the machine's origin | — | yes | Position snapped back to the ground |

Component 4 being the *left* nacelle settles the frame's handedness: its probe sits at negative X, so **-X is port and +X starboard**.

**Both nacelle contacts are reported at the left nacelle's point.** The right nacelle's branch tests its own probe point but hands `Mech_ApplyDirectFireDamage` the address of the left one, so a right-nacelle strike draws its impact effect on the wrong side. See [`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

### The probes share one transform

`Razor_MovementTick` copies the airframe's transform into a local **before** it integrates this tick's move, and every probe works through that copy: the five probe points are computed from it up front, it is never refreshed after a contact kicks the attitude, and the ray each probe sweeps is that copy with its translation overwritten by the probe's point. So every probe but the fuselage tests the airframe where it stood before this tick's move.

The translation is never put back. The wings and the cockpit each compute their ground-test point afresh through the copy *before* writing their own ray origin into it, so each is displaced by the previous ray probe's point:

| Ground test | Translation it is computed with | Point tested, every component intact |
| --- | --- | --- |
| Right wing | the airframe's position | `(1000, -700, -100)`, as listed |
| Left wing | the right wing's point while that wing is intact | `(0, -1400, -200)` — on the centreline, 8.4 m aft and 1.2 m below |
| Cockpit | the last intact ray probe's point: right nacelle, else left nacelle, else left wing, else right wing | `(450, 500, 0)` — 2.7 m right of the nose |
| Look-ahead | the cockpit's point, which it always has, since it runs only with the cockpit intact | `(0, 16000, -1500)` |

The displaced point is also the one a contact reports, ground or object, so a left-wing or cockpit strike draws its impact effect, and a fatal cockpit strike throws its wreckage, there. Only the rays themselves start at their own listed points.

Damage scales with speed on a ground contact (`Q10(airspeed, 500)` for a wing, 1000 for the cockpit, 5000 for the fuselage) and is a flat figure on an object contact. The shield figure is always 8000. A contact kicks the rate *and* applies it to the attitude in the same tick, leaving the rate standing for the flight model to damp out afterwards.

Destroying the cockpit or the fuselage latches `mech+0xa4` — the same byte a walker loses its legs to — and with it set the aircraft stops integrating position altogether. It is down where it fell.

### The look-ahead

A seventh point at `(0, 15000, -1500)` from the cockpit's point ([above](#the-probes-share-one-transform)) — far ahead and well below — pulls the nose up when the ground rises into it, at a hundredth of the cockpit probe's gain. **It only runs on an intact airframe**: both nacelles and the cockpit have to be alive, so a RAZOR that has lost any of the three flies straight into the hill.

### The shot record

Contacts go through `Mech_ApplyDirectFireDamage` with `AirframeContactShot` (`0049a170`), a shot record assembled in the executable's statics and refilled per probe rather than taken from a fired weapon. Two of its fields matter beyond the damage figures:

- `+0x0e`, the attacker, is **NULL** — flying into a hillside is nobody's kill.
- `+0x14` is the aircraft itself. This is `Sim_RaycastObjectList`'s *second* exclusion, distinct from the attacker; the beam path writes only the first, so the airframe probe is what makes the second reachable at all.

Its impact effects come from `AirframeContactImpactFx` (`0049a158`), a `PROJ.DAT`-shaped 12-entry table held in the image rather than in a file: shield `{11,11,11,11}`, ground and armour both `{0,1,4,5}`.

A fatal contact sheds wreckage — group 3 at the contact point, and only from the cockpit and fuselage probes, the two that can end the flight. See [`destruction-effects.md`](destruction-effects.md#spawn-sites).

## HUD speed

`Mech_GetDisplaySpeedKph` (`0041bb3c`) branches on the flyer flag. A walker divides its speed scalar by the type's top speed; a flyer maps airspeed from `[0, fm[42]]` onto `[0, typeRec+0xc2]` (the loader computes that field for walkers only, so a RAZOR carries its type file's own value) through `Math_MapRange` (`0047de3c`), where the walker branch divides by the type's top speed. `RAZOR.DAT` states 265 there (record offset 192), so level flight at full throttle, airspeed 1500, reads 265 km/h, and the 250 idle airspeed reads 45.

## The engine hum

`Razor_MovementTick` closes by pitching the looping engine hum (catalog id `0x2d`, `herceng1.wav`) at `FastMagnitude3D(bodyVelocity) * 16 + 28000` in 16.16, clamped to 16 bits, and re-placing it at the machine. It runs for the player's machine alone and is silenced on death. The hum is started by `Cockpit_PowerUpSound` and is the flyer's, not the walker's, despite the sample's name — see [`../formats/audio.md`](../formats/audio.md).

## Gun convergence

`Razor_MovementTick`'s last act is `Mech_ConvergeGunsOnRange` (`0041a74c`), unconditionally — an immobilised or destroyed RAZOR still runs it. The range is the distance from the machine to its selected target (`mech+0x1a4`) through `Math_DistanceBetweenPoints` (`00492780`), or 0 with none, the same figure `Sim_PollPlayerInput` computes for a walker. A walker reaches the convergence from `Mech_TorsoPitchTick`, which no flight path runs, so this is the flyer's only route to it; what the convergence does is in [`weapon-firing.md`](weapon-firing.md#gun-convergence--mech_convergegunsonrange-0041a74c).

## Rejected readings

| Reading | Why it is wrong |
| --- | --- |
| `004198f4` is a flyer terrain-avoidance autopilot | It is the flyer's whole per-tick move, the counterpart of `Mech_MovementTick`. The terrain probes are its collision model, not an assist; the pull-up look-ahead is one of seven points |
| The ceiling at top airspeed (`fm[34]`) is a flat maximum altitude | Nothing clamps to it. It is the far end of a ramp the loader derives at offset 14, reached only at the top airspeed (`fm[42]`) — see [the flight ceiling](#the-flight-ceiling) |
| The cockpit throttle slider does nothing on a RAZOR | It works. `Player_PerFrameCockpitUpdate` has a flyer-gated line writing the gauge value to `mech+0x2d7`. What is dead is the gauge's *speed* bar, which reads the walker scalar |
| The RAZOR is an instance of the `Flyer` class | That class is the SKIMMER's. The RAZOR is a `Mech` with `typeRec+0x50` set |

## Open

- **Open:** whether any retail mission places an AI-controlled RAZOR, which the constructor would give the walker move. None has been found.
