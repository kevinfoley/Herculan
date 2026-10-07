# AI weapons — aiming, weapon choice and the fire decision

What an AI machine does once it has something to shoot at: bring the turret onto it, decide which hardpoint to use, and pull the trigger. Which object it is shooting at is [`ai-targeting.md`](ai-targeting.md); the state it is in while it does is [`ai-dispatch.md`](ai-dispatch.md); the mounts themselves and what firing one costs are [`weapon-mounts.md`](weapon-mounts.md) and [`weapon-firing.md`](weapon-firing.md).

**The player and the AI share the whole of the mechanism below the trigger.** An AI machine turns the turret with the same aim primitive the player's tracker uses ([`torso-aim.md`](torso-aim.md#aiming-at-a-point)) and then calls the mount's own fire dispatch (vtable `+0x28`) directly, skipping the trigger poll and the arbitration in front of it ([`weapon-firing.md`](weapon-firing.md#weaponmounts_firetrigger-in-order)). What is AI-only is the four functions in this doc, which stand where the player's hands are.

## The tail every combat state shares — `Ai_AimAndFire` (`0041ea7c`)

`Ai_AimAndFire(mech, aspect, target)` is the last thing every fighting state does ([`ai-combat-states.md`](ai-combat-states.md#the-shape-they-share)), and `travelling` and `following` call it too — **a walking machine shoots at whatever it is watching**, without ever making it a selected target. A null `target` means `mech+0x1a4`.

Its whole body is a switch on the target's target class (`obj+0x1a8`, [`target-selection.md`](target-selection.md)), choosing where on the target to put the shot:

| Class | Aim point |
|---|---|
| 0, a machine | `Ai_AimAndFireAtMech`, below — the only branch that uses the shooter's aim component |
| 1, a structure | The base's **first surviving component**: vtable `+0x54` (`Base_FirstLiveComponent`, `00406868`) with a null second argument returns the first index whose damage word is non-zero, and vtable `+0x58` places it |
| anything else | The object's own aim offset, vtable `+0x30`'s second triple, added to its position. A GroundVehicle (class 3) is built as a structure and answers with its `BASES.DAT +0x2c` height (`Base_GetAimPoint`, [`structure-behaviour.md`](structure-behaviour.md#what-a-structure-is-aimed-at)); a flyer (class 2) answers zero (`SimObject_GetAimPointZero`), so it is shot at its origin |

The last two go straight to `Ai_FireAtPoint`. Only the first has a turret gate.

### Aiming at a machine — `Ai_AimAndFireAtMech` (`0041e984`)

```
bearingError = Math_HeadingToward(target, mech) - mech.heading
lim = typeRec+0x22
if ((uint16)(bearingError + lim) >= (uint16)(2 * lim)) { Mech_CenterTorsoTick(mech, 0); return }   // |error| >= lim
range = Math_DistanceBetweenPoints(mech, target)                    // 3D, unlike navigation
if (aimComponent >= 0 && 5000 <= range < 20000)  point = target.ComponentPosition(aimComponent)
else                                             point = target.AimNodeTransform origin, in world
Ai_FireAtPoint(mech, point, aspect, target)
```

- **The turret has to be able to reach it.** `typeRec+0x22` is the shooter's own torso-twist limit, 14000 on twenty of the 21 chassis ([`mech-locomotion.md`](mech-locomotion.md#mech-type-record)), so a target more than ~77° off the hull's nose is not shot at — the turret is centred instead, which also zeroes the [gun convergence](weapon-firing.md#gun-convergence--mech_convergegunsonrange-0041a74c). The machine keeps walking; the steering that brings the target back round is [`ai-navigation.md`](ai-navigation.md)'s. The PITBULL states 32767, the sentinel for a turret with no stop, and the unsigned test then passes every bearing error but `0x7fff` and `-0x8000`: it shoots at anything short of dead astern.
- **The aim component is only used at conversational range**, 5000 to 20000 units — 30 m to 120 m. Outside that band the AI shoots at the target's aim node and takes whatever component the hit test gives it. `Mech_AiSelectAimComponent` picks the component; see [`ai-targeting.md`](ai-targeting.md).
- Range here is `Math_DistanceBetweenPoints`, the 3D form, and it decides nothing but the component band above. Every range a navigation steer is computed from is the ground-plane one ([`ai-navigation.md`](ai-navigation.md#drive-to-a-point--ai_drivetopoint-0041fac4)), and so is the range `Ai_FireAtPoint` weighs weapons against, measured to the chosen point.

## The fire decision — `Ai_FireAtPoint` (`0041f5a0`)

```
range = Math_GroundDistanceBetweenPoints(mech, point)
weapon = mech+0x2ac                                                  // the latched weapon
if (weapon == 0 || !weapon.CanFire() || !WeaponMount_RangeAllows(weapon, range)) {
    if (mech+0xb5) { weapon = 0; mech+0xb5 = 0 }                     // suppressed for this tick
    else           { weapon = Ai_ChooseWeapon(mech, aspect, range, target); mech+0x2ac = 0 }
}
if (weapon == 0) { Cockpit_TargetAnglesFromCameraBone(mech, point); return }   // aim, do not fire

lead = target.Speed()                                                // vtable +0x38
if (weapon.Projectile.Speed > 0 && lead != 0)
    Math_OffsetPointByBearing(point, target.heading, range * lead / weapon.Projectile.Speed)
if (group.Side != 0 && (spread = AiAimScatter[difficulty]) != 0)
    point += (rand(spread), rand(spread), rand(spread))

residual = Cockpit_TargetAnglesFromCameraBone(mech, point)           // slews the turret; torso-aim.md
if (weapon.AmmoType != 5 || (|residual.yaw| < 1000 && |residual.pitch| < 1000))
    weapon.Fire(mech)
    if (weapon.template+0x56 is 6 or 22) mech+0x2ac = weapon         // ELF, ELF2
```

Six things it settles.

**Aiming happens whether or not anything fires.** The turret is slewed on the tick a weapon is chosen and on the tick none is, so the AI tracks continuously and shoots intermittently.

**Only a gun waits for the turret.** Ammunition type 5 is `WeaponMount_GetAmmoType`'s "not a launcher" ([`weapon-mounts.md`](weapon-mounts.md)); those fire only once the residual aim error is inside 1000 BAM (5.5°) in *both* axes. A launcher fires the moment it is chosen — the round steers itself, so pointing it is enough.

**Only a travelling shot gets a lead.** `range × targetSpeed ÷ projectileSpeed` along the target's own heading, from the `PROJ.DAT` record's `Speed` at `+0x0a`. A `Beam` record carries speed 0 and so takes no lead, which is right; a structure returns speed 0 from vtable `+0x38` and takes none either.

**The scatter is Cybrid-only and one-sided.** `group+0x12` is the group's side, and the scatter is added whenever it is not 0, the human side — so no human machine, the player's squad or an allied group, has its aim perturbed at all. The table at `0049a30c` is indexed by the mission difficulty (`004a9ee0`, the same global `Damage_ScaleByDifficulty` reads) — the enemy shoots straighter the harder the game is set. Its entries and where the difficulty comes from are [`difficulty.md`](difficulty.md#what-the-difficulty-changes)'s. `Math_RandomBelow` draws in `[0, bound)`, so all three components are displaced in the **positive** direction only; see [`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

**`mech+0x2ac` is the ELF latch.** Only weapon ids 6 and 22 are kept, and only while the mount stays ready and in range — `WeaponMount_RangeAllows` ([`weapon-mounts.md`](weapon-mounts.md#readiness--weaponmounts_mountisready-00410970)) asked with the ground-plane range to the point, not the 3D range `Ai_AimAndFireAtMech` measured a moment earlier. That is what lets an AI machine sustain an ELF burst across ticks instead of re-rolling its choice each one — `ElfMount_CanFire`'s sustain clause needs the mount fired on the previous tick.

**`mech+0xb5` costs exactly one selection.** `Rocket_HomingSteer` sets it on the launching machine for a subtype-3 (EO) missile, once per tick of flight — see [`rockets.md`](rockets.md#guidance--rocket_homingsteer-0040a254) — and the branch above clears it (other writers: [Open](#open)). So an AI machine that has an EO missile in the air fires nothing else while it steers, which is the machine's equivalent of the pilot flying it. It does not stop a *latched* weapon: the flag is only tested on the path that re-chooses.

## Choosing a weapon — `Ai_ChooseWeapon` (`0041f358`)

Walks every mount and scores it. Highest score above a floor wins; nothing above the floor means nothing fires this tick. **A spent mount is passed over first**, by the mount's own vtable `+0x5c`: destroyed for an energy or ELF mount (`WeaponMount_EnergyIsSpent`, `0040ed34`), destroyed or out of rounds for an ammunition mount (`WeaponMount_AmmoIsSpent`, `0040ed48`), and always for a pod (`WeaponMount_IsSpent_Always`, `0040f8a4`).

```
best   = Math_MapRange(mech+0x2aa, 0, 0x400, 150, -100)          // the floor: fear lowers it
frontal = -0x4000 <= aspect < 0x4000                              // a boolean, 0 or 1
shield = target.ShieldByHeading(frontal)                          // vtable +0x34, which wants a heading
jitter = target.TargetClass == 0 ? 35 : 30
for each mount:
    if (mount == 0 || mount.IsSpent()) continue                       // vtable +0x5c
    kind = mount.AmmoType
    inRange = WeaponMount_RangeAllows(mount, range)
    if (kind == 0 && inRange && !mech.Scanner && mech+0x26b == 0) mech.Scanner = true     // radar ACTIVE
    if (!inRange) continue
    if (kind != 5 && kind != 3 && manager+0x0a[kind] == 0) continue         // no missile lock
    if (!mount.CanFire()) continue
    if (shield == 0)             score = proj.DamageArmor
    else if (shield < proj.DamageShield) score = proj.DamageArmor + 10000
    else                         score = proj.DamageShield
    score = Q10(100, score) - Q10(1000, template+0x34)
    score += rand(jitter) * rand(jitter)
    if (score > best) { best = score; chosen = mount }
```

**The floor is the machine's fear.** `mech+0x2aa` is 0 from `Mech_Constructor`, then 1000, 600 or 300 from `Mech_AiFleeCheck` as it climbs ([`ai-targeting.md`](ai-targeting.md#the-flee-check--mech_aifleecheck-0041cb94); other writers: [Open](#open)). Mapped through `[0, 1024] → [150, −100]`, a calm machine needs a score over 150 and a frightened one will fire almost anything. **A tick where nothing clears the floor is a tick where the machine aims and does not shoot**, so the floor is the AI's rate of fire as much as its taste.

**The +10000 is what pays for a missile.** Retail costs at `template+0x34` are 500 for the `MSL` launchers and 600 for `BMSL`; 150 for the EMP cannons, the particle beams and `PLAS`/`MFAC`; 10, 20 and 30 for the autocannons and lasers by size; 5 for the ELFs; and 1 for `BEMP` ([`proj-dat.md`](../formats/proj-dat.md#the-retail-records) has the damage figures). The damage term is scaled by 100/1024 while the cost is scaled by 1000/1024, so an `MSL` launcher's 156 of armour credit against its 488 of cost leaves it at −332 before the jitter, and −449 while the target's shield holds and the credit is the 400 shield figure. On the shot that breaks the shield the bonus lifts it to +644. **Off that shot a launcher fires only when the jitter rescues it**: a calm machine shooting at a HERC clears its floor of 150 with an `MSL` alone about one tick in five against bare armour and one in seven against a holding shield. `BMSL` is the exception, its 7200 armour damage keeping it at +118 on armour alone. With the shield down every autocannon, laser and ELF stays positive on armour damage, as do `PLAS`/`MFAC`, `BPBW` and `BEMP`; the EMP cannons (−107), `PBW` (−49) and `PBW2` (−10) go negative and fire on the jitter.

**The score reads the mount's own `PROJ.DAT` record, which is not always the record a shot applies.** ATC75, ATC100, L400 and L500 are each shadowed by an earlier record with the same `(Type, id)`, so the AI scores them on their own larger figures while the shot applies the earlier weapon's ([`proj-dat.md`](../formats/proj-dat.md#lookup)).

**The jitter is as large as the signal.** Two independent draws below 35 multiplied together average 289 and reach 1156; against anything but a HERC the draws are below 30, for 210 and 841. The deterministic terms outside the bonus run from −449 (`MSL` against a holding shield) to +781 (`BEMP` against one), and for the autocannons, the ELFs and the lighter lasers they are in the tens. The choice is therefore mostly noise, biased by the damage-versus-cost term and all but decided by the shield-break bonus: across the retail arsenal, a weapon whose shot would break the shield outscores every weapon whose shot would not by at least 629 before the jitter.

**Missile lock is a hard gate on scoring.** `manager+0x0a` is the per-subtype lock array ([`missile-lock.md`](missile-lock.md)); every launcher needs its own subtype's flag up before it can even be scored, except subtype 3 (EO) and the non-launcher class 5, which skip the test. The lock block keeps no countdown for an EO missile and raises only the other four subtypes' flags ([`missile-lock.md`](missile-lock.md#the-mechanism)) — the pilot flies it — so the exemption is what lets an AI machine score one. This is `Ai_ChooseWeapon`'s own test. `Rocket_Fire` applies a separate gate at launch, and its subtype 3 exemption is for any machine the player is not flying ([`rockets.md`](rockets.md#spawning--rocket_fire-0040a9c4)); the cockpit's readiness predicate has the same two exemptions as the scoring ([`weapon-mounts.md`](weapon-mounts.md#readiness--weaponmounts_mountisready-00410970)).

**A SARH launcher in range lights the radar.** Any unspent subtype-0 mount whose window covers the range switches the machine's radar to ACTIVE, ready or not, unless the radar-silence timer is running — that class of missile needs its own illumination. The rest of the AI's radar policy is in [`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set).

**The shield lookup is fed the wrong argument.** `Ai_ChooseWeapon` works out whether it is shooting the target's front or rear itself, then hands that boolean to `Mech_GetShieldByHeading` (`004154d0`), which expects a heading. Both 0 and 1 fall inside that function's front quadrant, so **the front shield is what comes back however the target is facing**. The obvious reading — that the AI weighs a weapon against the facing it is actually shooting at — is wrong. See [`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md). The aspect that feeds the boolean is built by the combat states' geometry block ([`ai-combat-states.md`](ai-combat-states.md#the-geometry-block--ai_buildcombatgeometry-0041e758)).

### Running dry — `mech+0xa5`

When every mount is absent or spent — destroyed, out of rounds, or a pod — and the flag is not already up:

```
mech+0xa5 = 1
if (mech+0x1b6) Action_Activate(mech+0x1b6)      // the object's own mission action
if (MissionPollTimer_Count < 1000) MissionPollTimer_Count = 1000
```

**The last line defers the mission's objective poll.** `004a9ee7` is the counter of the poll-interval timer record at `004a9ee6`, which `Mission_PollStatus` steps each tick through `Math_CountdownTimerTick` and evaluates the objectives when it reaches 0 ([`mission-objectives.md`](mission-objectives.md#the-poll--mission_pollstatus-004131ac)). Raising it to 1000 counts, about half a second, means the next evaluation — including any "disarmed" objective (condition 7) — runs no sooner than that after the machine runs dry; a poll already further off is left alone.

`mech+0xa5` is "this machine has no weapons left", not a third damage latch beside `+0xa4` and `+0x99`; what the three mean and which tests read them is [`component-damage.md`](component-damage.md#the-three-out-of-the-fight-bytes--0x99-0xa4-0xa5).

## Open

- **Open:** the units of `template+0x34`. It is plainly a per-shot cost the AI weighs against damage, and the retail values order the arsenal sensibly, but `Ai_ChooseWeapon` is the only reader `es2_fieldscan.py` finds among the weapon functions, so nothing else pins what it is measured in ([`../formats/weapons-dat-sim.md`](../formats/weapons-dat-sim.md)).
- **Open:** whether anything else writes `mech+0x2aa`. `es2_fieldscan.py` over the whole image and a grep of the decompile find four stores, `Mech_Constructor`'s 0 and `Mech_AiFleeCheck`'s three; no `memset`/`memcpy` sweep has been run for it.
- **Open:** whether anything else sets `mech+0xb5`. On a machine, `es2_fieldscan.py` over the whole image and a grep of the decompile find `Rocket_HomingSteer`'s store of 1 and `Ai_FireAtPoint`'s clear; the offset's other hits are cockpit widgets and the `004d2540` block.
