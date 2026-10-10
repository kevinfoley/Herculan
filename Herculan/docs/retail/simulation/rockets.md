# DBSIM.EXE launcher rounds (`PROJ.DAT` type `Rocket`)

Addresses are DBSIM virtual addresses.

The third and last fire branch. A `Beam` record resolves inside the call that fired it ([`beam-visuals.md`](beam-visuals.md)); a `Bullet` record becomes a travelling shot ([`projectiles.md`](projectiles.md)); a `Rocket` record becomes one of these. Every missile launcher — `MSL6`, `MSL8`, `MSL10`, `FLYMSL`, `BMSL` — fires one.

Like a bullet it lives in the effect pool (`g_ProjectilePool`, `004a9746`) that `Sim_MainTick` walks **before** the machine list, cannot be shot at, and does not move on the tick that spawned it ([`projectiles.md`](projectiles.md)).

The round's type table, indexed by the firing `PROJ.DAT` record's subtype id, and its shapes are in [`../formats/rockets-dat.md`](../formats/rockets-dat.md). The `record[+0x..]` offsets below are that table's.

## Spawning — `Rocket_Fire` (`0040a9c4`)

`Rocket_Fire(projIndex, muzzleWorldPoint, aimEulerTriple, ownerMech, ownerTravelSpeed)`, called from `WeaponMount_FireDispatch_Missile` (`0040e964`) for a mount whose `PROJ.DAT` record has `Type == 0`. **The magazine is spent before that type test**, so a launcher pays for its round on the same line an autocannon does.

- **The aim triple goes in verbatim.** No `ROCKETS.DAT` field is a scatter and the spawn draws no random numbers — a launcher does not disperse.
- **Launch speed is a literal 500** plus the machine's own travel speed (mech vtable `+0x38`). The record's `Speed` is not read here; it is the ceiling the burn climbs toward.
- **The target is captured once, at launch**, into `+0x56` — the launcher's selected target at `+0x1a4`, and only when the launcher's vtable `+0x6c` returns nonzero — for a machine `Mech_MissileLockState` (`004155ac`); every other class, flyer and structure alike, installs `SimObject_MissileLockState_Always` (`00411b04`), so a missile tower's round always takes its target. The mech's reads the per-subtype *lock* flag, not an ammunition count ([`missile-lock.md`](missile-lock.md#manager0x0a-is-the-lock-state-not-an-ammunition-count)). The one bypass: a machine other than the locally piloted one (`mech+0xa3` clear) firing subtype 3 skips the gate outright, so an AI's electro-optical missile always locks. That bypass is `Rocket_Fire`'s own and is not the AI's weapon-scoring exemption from the lock test ([`ai-weapons.md`](ai-weapons.md#choosing-a-weapon--ai_chooseweapon-0041f358)). A lock also asks the target, through its vtable `+0x54` handed the round's own pose, which component to steer at, into `+0x5a`. A HERC's `Mech_ComponentNearestAim` (`0041b534`) and a structure's `Base_FirstLiveComponent` (`00406868`) both walk the live components (a HERC's 29 slots, a structure's type-record count), place each through vtable `+0x58`, and keep the one whose bearing from the muzzle differs least from the round's launch attitude, by `|Δpitch| + |Δheading|` (a difference of `-0x8000` counts as `0x7fff`; the first of equals wins) — the part the launcher was pointing at. Either answers `-1` with nothing live; a flyer's `SimObject_ComponentNearestAim_None` (`00411a44`) always does. The choice is made once: the round never re-picks, and goes on steering at where a shot-off component stands.
- **A locally piloted owner's round is remembered** in `DAT_0049c394` whatever its subtype, for [the missile camera](#the-missile-camera).
- Plays `record[+0x0c] + 10` at the muzzle point.

`Rocket_Fire` builds only the `Type == 0` class. No caller of the `Type == 3` class's constructor, `Grenade_Construct` (`0040ac3c`), is found — [`weapon-damage-types.md`](weapon-damage-types.md#type--a-firing-mechanism-selector) and [`../cut-content.md`](../cut-content.md#projectiles).

## Flight — `Rocket_TickUpdate` (`0040a538`)

Vtable `+0x14` of `RocketVtable` (`00498448`); draw is `Bullet_Draw`, shared with the bullet class.

1. **Animation.** When `record[+0x08]` is nonzero, a countdown at `+0x5c` steps the shape instance's cell-frame entry for sequence `record[+0x0a]`, modulo the shape's own frame count for that sequence. This is the exhaust flame — see below.
2. **Age.** `+0x54 += 1`; expire at `record[+0x02] < age` (the lifetime), with no impact of any kind. A rocket burns out, it does not detonate on a timer.
3. **Burn**, damped: `speed += IntegrateRateOverTick(record[+0x04])`, then averaged with the speed the tick opened at, then capped at the `PROJ.DAT` record's `Speed` (`proj+0x0a`).
4. **Guidance** — `Rocket_PlayerSteer` when the owner is locally piloted (`mech+0xa3` set) *and* the subtype is 3, which also raises `DAT_004d25aa`; `Rocket_HomingSteer` otherwise.
5. `step = IntegrateRateOverTick(speed)` along the frame's Y axis, then a `Sim_RaycastObjectList` over that step alone with `record[+0x06]` as the shot record's slack — the same sweep-the-segment arrangement a bullet uses. Struck anything and the round ends.

**Damage is never power-scaled**: a rocket comes off a rack, not a capacitor, so the `PROJ.DAT` figures apply at face value. The shot record's `+0x12` carries the subtype id where a bullet hardcodes 5 ([`weapon-firing.md`](weapon-firing.md#the-shot-record)).

**When the round ends**, by burning out or by striking something, a subtype 3 round of a locally piloted owner — one still being flown — clears the trigger byte `004d2357` and calls `Input_LatchButton(1, 1)`, latching the first button row, the trigger's, until it is let go ([`joystick-input.md`](joystick-input.md#the-buttons)); and if the round is the one in `DAT_0049c394` and ended before its lifetime, `DAT_0049c398` is raised.

**The proximity beep.** A round fired by a machine that is **not** locally piloted (`mech+0xa3` clear) plays sound `0x32` when it comes within 40000 units of the camera (`ViewObjectPtr`, the camera position); a latch at `+0x06` holds it to once per approach and re-arms when the round leaves that range. The player's own rounds never beep.

**On retail data the speed cap is unreachable.** Every record's rate of 250 becomes 79 at the simulation's timestep and 39 after the damping, so 80 ticks carry a round from ~540 to ~3600 against a ceiling of 6000 — it is still accelerating when it burns out, and `PROJ.DAT`'s `Speed` sets nothing. Because the life is a tick count while the step scales with the timestep, a rocket is the one shot whose **range** was frame-rate dependent in the original.

## Guidance — `Rocket_HomingSteer` (`0040a254`)

A steer of the euler angles, not of a velocity, as the plasma round's is — but with a real lead and gates the plasma round has none of.

- **The selection gate.** A round with no target does not steer. A locally piloted owner's round also steers only while its target is still the owner's selected target (`mech+0x1a4`), so selecting another target stops every missile in the air steering until the first is selected again; an AI's rounds are not asked.
- **Lead.** A round holding a component (`+0x5a >= 0`) steers at that component's world position (target vtable `+0x58`, [`damage-system.md`](damage-system.md#where-a-component-stands--the-0x58-slot)); otherwise at the target's aim point ([`target-selection.md`](target-selection.md#aim-point--vtable-0x24)). `Math_EulerToward` (`00492884`) turns that into a bearing triple; the two aiming components are moved toward it through `Math_RateLimitedMoveToward` at **`0x500` per 125 ms**, twice the plasma round's cap.
- **The emission gate.** Subtype 2 (`ARM`, anti-radiation) steers only while the target has `+0x96` (the scanner the pilot toggles, `Mech_ToggleRadarMode` (`0041b468`)) or `+0xa1` (its jammer) set.
- **The spoofing wobble.** For subtypes 0, 1 and 4, when the *launching* machine's `+0x9c` is set, each aim error (heading, then pitch) in `[-0xc00, 0xbff]` — the test is `(ushort)(error + 0xc00) < 0x1800` — moves `0xc00` toward the opposite sign before the rate limit, so a round nearly on its target steers away from it and weaves instead of converging. `Mech_PerTickSystemsUpdate` (`0041aa5c`) rolls that flag while the machine's selected target is jamming (`target+0xa1`) — the odds, interval and Targeting Pod discount are in [`missile-lock.md`](missile-lock.md#ecm). **This is the mechanical form of the manual's ECM.**
- Subtype 3 never wobbles: it instead sets the owner's `+0xb5` once per tick it steers; what that does to the AI is in [`ai-weapons.md`](ai-weapons.md#the-fire-decision--ai_fireatpoint-0041f5a0).

## `Rocket_PlayerSteer` (`0040a488`) — the player flying the missile

Not a "non-homing variant": the pilot flies the electro-optical missile from [the missile camera](#the-missile-camera). It reads two axis values through the camera-axis pointers `+0x22` and `+0x26` of the **player input block** at `0x4d234a` ([`../formats/tap-input-tape.md`](../formats/tap-input-tape.md)), which address the steering and throttle axes while the round has the controls ([`joystick-input.md`](joystick-input.md#while-the-camera-has-the-controls)). Each turns the round by `Math_IntegrateRateOverTick(Q8Multiply(0x500, axis))` a tick — `0x500` per 125 ms at full deflection, with no rate limit and no deadband: the `+0x22` axis is subtracted from the heading (`+0x10`), so steering right turns the round right, and the `+0x26` axis is added to the pitch (`+0x0c`). It marks the frame for rebuild (`+0x32 = 0`) and zeroes both axes and the trigger byte. `Sim_MainTick` rebuilds the byte with `Input_BuildPlayerDevice` before it walks the effect pool and reads it in `Sim_PollPlayerInput` after, so while the player flies a round the trigger never reaches the fire path and the machine fires nothing.

The gate is the block's `+0x0d`, `0x4d2357`: **the fire trigger** ([`weapon-firing.md`](weapon-firing.md#the-trigger-is-polled-not-dispatched)), so the round is flown only while the trigger is held. With the trigger released the function drops the round's target and rewrites its subtype id to 0, and the round flies straight on as an unguided subtype 0 round (`Rocket_TickUpdate` then sends it to `Rocket_HomingSteer`, which has no target to steer at).

## The missile camera

`DAT_004d25aa`, "an electro-optical missile is being flown", is zeroed by `Sim_MainTick` just before it walks the effect pool and raised again by every tick that sends a round to `Rocket_PlayerSteer`, the tick that releases it included; `WeaponMounts_FireTrigger` also raises it on the firing tick ([`weapon-firing.md`](weapon-firing.md#weaponmounts_firetrigger-in-order)). The next input build hands the controls to the round, as `InputDrivesCamera` hands them to a camera ([`joystick-input.md`](joystick-input.md#while-the-camera-has-the-controls)), and the weapon chain holds still until the flag drops.

The pilot sees the flight on the MFD. `DAT_0049c394` is the round its MISSILE CAM screen rides and `DAT_0049c398` the strike that screen flashes for ([`mfd.md`](mfd.md#mfdmissileview--mode-5)); the display switches itself to that screen while an electro-optical launcher is armed ([`mfd.md`](mfd.md#modes)).

## The exhaust flame

The shape is a static body plus a two-cell animation of flame cones at the tail ([`../formats/rockets-dat.md`](../formats/rockets-dat.md#dtsrocketsdts)). The animation step is the first step of `Rocket_TickUpdate`. The record's interval of 256 drives the cells at one cell every four ticks, because the record names sequence 0 and every cell-animation part in both roots carries sequence 0. `BMSL`'s record carries an interval of zero, so its flame is frozen on cell 0, and at its shape's finest level it has none: that level's BSP tree never reaches the flame.
