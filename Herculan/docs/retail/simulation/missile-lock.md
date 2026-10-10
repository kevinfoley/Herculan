# Missile lock

How a machine's launchers achieve lock on its selected target, the ECM roll that spoils it, and the cockpit's lock lamp and tone. All of it is the second half of `Mech_PerTickSystemsUpdate` (`0041aa5c`). The consumers of the flags it builds are [`rockets.md`](rockets.md#spawning--rocket_fire-0040a9c4) (the `Rocket_Fire` gate), [`weapon-mounts.md`](weapon-mounts.md) (a launcher row's ready box) and [`ai-weapons.md`](ai-weapons.md) (the AI's weapon scoring).

## `manager+0x0a` is the lock state, not an ammunition count

Its reader is `Mech_MissileLockState` (mech vtable `+0x6c`, `004155ac`), and it counts nothing. The array is **five flags, one per `PROJ.DAT` missile subtype: has this class of launcher achieved lock on the machine's current target.**

`Mech_PerTickSystemsUpdate` clears all five, and the lamp flag `mech+0x9b` with them, ahead of its target block, then sets the ones whose countdown has expired. It does this for **every** mech each tick, the player's included, with or without a target selected — so losing the target drops every lock on the next tick. That is why a player's missiles lock in retail, and why `Rocket_Fire`'s gate on it is a real gate.

The genuine ammunition count is a separate local array built by `WeaponMounts_RoundsByMissileType` (`0040fbdc`), which walks the mounts calling each one's vtable `+0x60` (`WeaponMount_GetAmmoType`, `0040e644`) and accumulates its out-parameter (`mount+0x7b`, rounds remaining) into `rounds[subtype]`. A subtype with no rounds gets no lock timer.

## The mechanism

Each subtype the machine carries rounds for has its own countdown. Every tick the block either **reloads** it — holding it at full so no lock can form — or lets it run; reaching zero latches the flag.

- **Reload value** is `min(range >> 2, 0x7fff)`, so lock time is linear in range. The countdown is in the simulation's own timer unit ([`structure-behaviour.md`](structure-behaviour.md)): 256 to 125 ms, 81 a tick on hardware that keeps up. A target 20000 units away (120 m) takes 5000 units, about 2.4 s; one at 57205 (343 m) takes about 7 s. A turretless chassis (type record `+0x50`, file offset 78, the flyer flag — the RAZOR) uses 0 and locks instantly, and measures its bearing without a twist it does not have.
- **The whole block is held** when `mech+0x9d` is set (target just changed), the bearing error is outside ±`0x3000` of the turret centreline (±67.5°), or line of sight is broken. That branch resets the timers for subtypes 0, 1 and 2 — **not 4**, which keeps a partial lock across a broken moment — and raises the function's `local_1a`, which is what makes a target switch cost exactly one tick. `mech+0x9d` is cleared from that flag at the very **end** of the function, past the lock audio, so the audio still sees the switch on the tick it happened.
- Line of sight is asked from whichever end owns the cache row: human side asks about the target, Cybrid asks the target about itself ([`target-selection.md`](target-selection.md#line-of-sight--detection_lineofsight-00412608)).

| Subtype | Timer record | Manager slot | Hold condition |
|---|---|---|---|
| 0 | `+0x258` | `+0x0a` | own scanner off, or the ECM roll came up spoofed this tick |
| 1 | `+0x25b` | `+0x0c` | the ECM roll came up spoofed this tick — otherwise locks on sight |
| 2 (ARM) | `+0x25e` | `+0x0e` | **target** silent (`+0x96` and `+0xa1` both clear) |
| 3 (EO) | — | `+0x10` | never set. The player's round therefore leaves with no target: the pilot steers it while the trigger is held. `Rocket_Fire` skips the gate only for a machine that is not locally piloted ([`rockets.md`](rockets.md#spawning--rocket_fire-0040a9c4)) |
| 4 | `+0x264` | `+0x12` | as subtype 0 |

Each timer offset is the countdown record's base; the counter is the `short` one byte above it. The run leaves room for a subtype 3 timer at `+0x261`, which this block never touches.

Subtype 2's inverted condition is the anti-radiation missile: it locks *because* the target is emitting, on the same pair of flags its guidance homes on.

## ECM

A target that is a HERC with its jammer (`+0xa1`) on re-rolls `mech+0x9c` whenever the countdown record at `mech+0x267` expires: `(rand & 0xfff) < 0x14 * 0x29` — about 20% — holding for 5000 (about 2.4 s) on a spoof and re-rolling after `0x5dc` (about 0.7 s) otherwise. A target that is not a HERC, or whose jammer is off, clears the flag outright.

**The jammer flag is derived every tick.** `Mech_IsEcmSwitchedOn` (`0041aa10`) stores it into `mech+0xa1` ahead of the lock block, and an ECM pod at `mech+0x307` is its outer gate: without one the flag is clear. With one, who is flying decides which switch is read:

- **The player's follows the pod row's button**, through the copy `EcmPod_Tick` keeps at `pod+0x7d` ([`equipment-pods.md`](equipment-pods.md#what-the-two-ticks-do-with-the-button)). Their own radar mode does not enter into it.
- **Any other machine jams exactly while its radar mode (`mech+0x96`) is ACTIVE.** Its pod is never ticked, so it has no button to read. A squadmate's radar is set back to PASSIVE on entering a fight, so in practice this branch means Cybrids; `SCAN FOR HOSTILES` lights a squadmate up and its jammer with it, and `EMCON` clears both. Nothing orders a squadmate's pod on its own ([`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set), [`ai-squadmates.md`](ai-squadmates.md)).

An anti-radiation hit therefore silences the jamming it homed on: it clears the machine's scanner and holds it dark for 6000, so the machine stops spoofing the player's locks for the same window ([`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set)).

The flag has two effects on the lock. A roll that comes up spoofed reloads the timers of subtypes 0, 1 and 4 on that tick; and while the flag stands, no subtype but 2 can latch a completed countdown. It is the same flag that makes a missile already in the air weave ([`rockets.md`](rockets.md#guidance--rocket_homingsteer-0040a254)).

The roll's weight, `0x14`, drops to 5 — a quarter — when the machine holding the lock carries a Targeting Pod (`mech+0x30b`, catalog id 29) whose cached damage at `+0x7f` is under `0x33`, a fifth of the way to destroyed. **It is the observer's pod, not the jammer's.** What that cache is and the pod's other three thresholds are in [`target-selection.md`](target-selection.md#a-damaged-pod-degrades-in-four-steps) and [`equipment-pods.md`](equipment-pods.md#where-a-pod-reads-its-damage).

## The lock lamp

`mech+0x9b` is set from the armed mount's own class: a launcher lights its own subtype's flag, a mount that is not a launcher (class 5) lights if *any* subtype has lock. The HUD target indicator draws its locked frames from it ([`hud-target-indicator.md`](hud-target-indicator.md)). `Mech_LockTonePlay` (`0041b0bc`) turns it into the cockpit's lock audio: `Sound_Play(0x15)` (`trgloc.wav`) once per phase of a `0x40`-coarse-tick blink while set, `0x14` (`bptslct.wav`) when clear but the target changed this tick, `0x16` (`trgunloc.wav`) once on loss ([`audio.md`](audio.md)). Two latches carry it — `0049a1d1` remembers that a lock was held so its loss is announced once, `0049a1d0` that this phase's beep has sounded.

The loss branch **returns before** the target-changed test, so switching target while locked plays the loss tone and not the acquisition blip.

**Where it is called from is part of the behaviour.** The call — and the lamp calculation above it — sit inside the target block's locally-piloted arm (`mech+0xa3`), which is inside `if (mech+0x1a4 != 0)`. So the lamp is only ever computed for the locally-piloted machine (an AI machine's stays clear all mission), and neither lamp nor tone runs at all with nothing selected. That second nesting matters because `mech+0x9d` is *only* cleared with a target present: run the tone unconditionally and a selection dropped to null latches the flag and re-triggers the acquisition blip every tick.
