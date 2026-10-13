# DBSIM.EXE weapon firing: the trigger, the shot, beams

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

Covers how a trigger pull becomes a shot and what a beam does. The mounts it fires are in [`weapon-mounts.md`](weapon-mounts.md); what a hit does to the target is in [`damage-system.md`](damage-system.md); the template fields read here are in [`../formats/weapons-dat-sim.md`](../formats/weapons-dat-sim.md).

## The trigger is polled, not dispatched

**There is no scancode case for `[Space]` anywhere.** The manual binds it to Fire Active Weapon, but it never reaches `Sim_DispatchCommand` or `WeaponMounts_HandleCommand`. Instead:

| | |
|---|---|
| `Sim_PollPlayerInput` (`00460764`) | runs every frame, calls the next line for `LocalPlayerMech` |
| `Mech_PlayerFireTick` (`00415608`) | calls the fire entry, then stamps the player's line of fire into `DAT_004a9c0c` on a successful shot — a keep-out line for the squad's obstacle avoidance, not a display; see [`ai-navigation.md`](ai-navigation.md#the-players-line-of-fire) |
| `WeaponMounts_FireTrigger` (`00410dbc`) | the arbitration below |
| `WeaponMount_TriggerHeld` (`0040f8ad`) | mount vtable `+0x30` — returns the input device struct's byte at `+0x0d`, the fire button, and nothing else |

So the trigger is a **held state re-read every frame**: holding it fires again the instant the refire delay expires and the capacitor is back over its threshold. Nothing along the path looks at edges. Only the player's machine reaches it — AI machines fire from their own think function.

The device byte is `DAT_004d2357`, taken from whichever button the input configuration assigns the fire action, or the default keyboard binding at `DAT_004d23e4`/`DAT_004d23f4`.

### `WeaponMounts_FireTrigger`, in order

1. The armed mount (`manager+0x1d`); its link partner if `+0x4b` is set, via the hardpoint's own `+0x16` offset.
2. **Both** mounts pass `+0x2c` (ready) before either fires — a pair whose second half is still charging does not fire its first half alone.
3. **Both** pass `+0x30` (trigger held), asked separately even though both answer from the same byte.
4. Armed fires through `+0x28`, then the partner.
5. Single-fire (`manager+0x18`) is cleared once the armed mount is no longer ready. That is the whole of the manual's "once you fire, the current firing chain will resume" — the chain advance in `WeaponMounts_AdvanceToReady` takes the selection back on the next frame.

It also passes each fire dispatch the "this shot is free" flag of the unlimited-ammunition setting ([`difficulty.md`](difficulty.md#the-two-sibling-cheats)), which only the ammunition class reads ([below](#the-ammunition-dispatch)).

After the armed mount fires, a `+0x60` subtype of 3 — the electro-optical missile, which the pilot flies — sets `DAT_004d25ac` and `DAT_004d25aa`, "an electro-optical missile is being flown" ([`rockets.md`](rockets.md#the-missile-camera)). An energy mount always reports 5 and never sets them. `WeaponMounts_ChainReady` (`00410a04`) answers ready while `DAT_004d25aa` is up, so the chain advance does not step while the missile is in flight. `DAT_004d25ac` holds until the selection moves — `WeaponMounts_SetSelection` (`00410708`) clears it on any change — and `WeaponMounts_PerFrameUpdate` opens with it: set with `DAT_004d25aa` down, the flight is over, so it steps the selection on with `WeaponMounts_StepSelection(1)` and clears single fire. The launcher that fired the round therefore hands the selection to the next mount in the chain once the round stops being flown.

## The fire dispatch — vtable `+0x28`

Two implementations serve the energy and ammunition classes, both opening with `WeaponMount_PrepareShot`. The ELF's own dispatch is in [`weapon-mounts.md`](weapon-mounts.md#elf-and-elf2).

| Class | Function | Branch |
|---|---|---|
| Energy / gun | `WeaponMount_FireDispatch_GunBeam` (`0040ea58`) | `Beam` → `Bullet_FireBurst`; else a travelling `Bullet` |
| Ammunition | `WeaponMount_FireDispatch_Missile` (`0040e964`) | `Rocket` → `Rocket_Fire` ([`rockets.md`](rockets.md)); else the same `Bullet` fallback |

Both also raise `mount+0x44`, the muzzle flash, on a visible hardpoint; the conditions are in [the muzzle flash](weapon-mounts.md#the-muzzle-flash).

### The beam branch

```
power = min(template[0x38], capacitor +0x7d)      // the cost, capped at what is held
capacitor -= power
shotTransform.translation = muzzleWorldPoint      // overwrite the gun frame's origin
Bullet_FireBurst(proj.subtypeId, shotTransform, template[0x30], ownerMech, power)
```

`template[0x38]` is also the upper half of the readiness threshold pair, which [the energy mount's readiness test](weapon-mounts.md#energy) combines with the charge target. The two shapes the pair takes are two kinds of weapon:

- **Fixed cost.** `0x36 == 0x38` (`LAS100` 80/80 … `LAS500` 120/120): the mount fires at that charge and the cost is that number, so every shot is identical.
- **Charge-up.** `0x36 < 0x38` with `0x38` at 10000 (`PBEAM` 300/10000; `EMP`, `PLAS` and `MAGN` 350/10000): the mount fires at the charge target and the cost is the whole capacitor, so the shot is worth as much as the pilot let it accumulate. That charge target is the mount's [power level](#power-level--weaponmount_adjustpowerlevel-0040f48c).

### The gun branches

Everything that is not a `Beam` builds a travelling `Bullet` (see [`projectiles.md`](projectiles.md)), through one of two branches:

- **Charge-up gun**, taken when the capacitor holds *less* than the cost. It fires shots worth the whole charge (`Bullet_FirePowered` (`0040b5a0`), which stores the charge on the bullet) and then either arms a burst or empties the capacitor. **Every retail energy gun takes this branch always**, because they all read a 10000 cost against a capacitor scaled to 1200.
- **Fixed-cost gun**, taken otherwise: subtract the cost, fire one unpowered shot through `Bullet_Fire` (`0040b43c`). Unreachable in retail for the reason above.

Two multi-shot rules sit on the charge-up branch, and each identifies exactly one weapon:

| Test | Weapon | Effect |
|---|---|---|
| `template[0x3c] == 3` | catalog id 19, `BEMP`, the Bull's EMP (the simulator also names it `EMP`) | fires **three** shots, from barrels at `-x`, `0` and `+x` of the template's own muzzle offset |
| `template[0x3e] == 0x13` | catalog id 23, `EMP2` | arms `mount+0x4d`, so the mount fires again a quarter of a refire delay later and *then* empties — two volleys per trigger pull |

`0x3e` is the template's `PROJ.DAT` index, and `0x13` is `EMP2`'s own `PROJ.DAT` row, so that second test is a weapon check spelled as a data comparison. The follow-up shot is dispatched from the energy arbitration (`WeaponMounts_ArbitrateEnergy`, via `WeaponMount_AutoFireDue`), not from the trigger.

### The ammunition dispatch

**It spends `+0x7b`, not `+0x7d`** — it subtracts `template[0x38]` from the round count (5 on every autocannon, against magazines of 500 to 2000) and clears `+0x4c` (selectable) at zero, dropping an empty weapon out of the selection cycle. It spends **before** it looks at the projectile type, so a launcher pays a round on the `Rocket_Fire` path too. The one thing that can skip the spend is the "this shot is free" flag above, which skips the clear of `+0x4c` with it, so a cheating player's launcher never drops out of the selection cycle. The energy and ELF dispatches take the same flag and pass it on to `WeaponMount_PrepareShot`, whose body never reads it; only this dispatch does.

The gauge's rolling round counter, `+0x7d`, is in [the ammunition mount](weapon-mounts.md#ammunition).

## The shot record

`Bullet_FireBurst` builds it on its own stack and `Sim_RaycastObjectList` writes back into it.

| Offset | Field |
|---|---|
| `+0x00` | pointer to the ray record below |
| `+0x04` | `Q10Multiply(power, armourDamage)`, then scaled by the mission difficulty |
| `+0x06` | `Q10Multiply(power, shieldDamage)`, the same. The shot record puts armour first where [the `PROJ.DAT` record](../formats/proj-dat.md#layout) puts shield first |
| `+0x08` | the splash factor, the Q10 secondary-explosion fraction |
| `+0x0a` | pointer to the record's three impact-effect arrays, indexed as one 12-entry array — see [`impact-effects.md`](impact-effects.md#which-effect-a-shot-spawns) |
| `+0x0e` | the owner machine, which the sweep skips |
| `+0x12` | the firing rocket's subtype id, a literal 5 from a beam or a bullet. `Mech_DirectFireHitTest` reads it to set the target's scanner ([`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set)). It is not `PROJ.DAT`'s `Type`, which is decided at the mount's fire dispatch and never reaches the shot record |

The ray record:

| Offset | Field |
|---|---|
| `+0x00` | pointer to the shot transform (rotation, muzzle world position in the translation) |
| `+0x04` | the ray's length — starts at the weapon's range and **is overwritten with each hit distance** as the sweep shortens it |
| `+0x08` | a literal 200, slack the range check adds before rejecting a candidate |
| `+0x0a` | the world-to-muzzle transform, cached by the sweep for every hit test to work in |

What the power scale does to the two figures is in [`../formats/proj-dat.md`](../formats/proj-dat.md#layout); the difficulty scale, applied at the top of `Sim_RaycastObjectList` itself, is in [`difficulty.md`](difficulty.md#the-damage-scale-reaches-all-direct-fire-not-just-plasma).

## Where the shot comes from — `WeaponMount_PrepareShot` (`0040e788`)

The frame is the **firing hardpoint's own model bone**, posed as it stands this tick and composed with the machine's world transform. A beam follows the torso because the gun bone does: nothing adds the twist or pitch angle, and nothing needs to. The spectator flag swaps the machine for the watched object in that composition: [`external-views.md`](external-views.md#the-spectator-flag--dat_0049ef5c).

The prologue also composes a per-hardpoint aim rotation under the bone's pose: the [gun convergence](#gun-convergence--mech_convergegunsonrange-0041a74c), which toes each hardpoint in on the range the turret is aiming at.

The muzzle point is three offsets summed in bone space:

```
template[0x40..0x44]                       // the weapon's own muzzle triple
+ hardpoint[0x10..0x14]                    // the .GL mount-point offset
+ WeaponMountTemplate_SideMuzzleOffset     // 0040f904, below
```

**Only the last two are `WeaponMount_MuzzleOffset` (`0040f540`).** The template's own triple is added here, by the prologue, and nowhere else. The distinction matters because the pair without it is *where the weapon sits* — it is the offset the base constructor bakes into the mount's copy of the weapon model (`Shape_TranslatePointLists`, `0040dd4c`), and the triple is the length of the barrel from there. Retail triples run 630 (`LAS100`) to 2725 (`MISSL`) units of forward Y, 3.8 m to 16 m, so standing the model at the muzzle instead puts it a barrel clear of the chassis. The multi-barrel branch of `WeaponMount_FireDispatch_GunBeam` shows the split plainly: it loads `template[0x40..0x44]` into a local, calls `WeaponMount_MuzzleOffset`, and adds the two.

`WeaponMountTemplate_SideMuzzleOffset` is what makes a mirrored hardpoint pair fire from mirrored points off one template. The template carries a lateral figure at `0x46` and a vertical one at `0x4a`; the hardpoint's mounting code (`.GL +6`) picks one and its sign, and **only one axis is ever nonzero**:

| `.GL +6` | Meaning | Offset |
|---|---|---|
| 0 | on top | `(0, 0, +0x4a)` |
| 1 | underneath | `(0, 0, -0x4a)` |
| 2 | left side | `(-0x46, 0, 0)` |
| 3 | right side | `(+0x46, 0, 0)` |
| 4 | invisible | `(0, 0, 0)` |

The prologue then arms the refire timer as `Q10Multiply(mount+0x63, template[0x4c])`. `mount+0x63` is `0x400` from the base constructor (`WeaponMount_CtorBase`, `0040df30`) and only a damaged `Bullet` gun lowers it ([a damaged gun fires faster](weapon-mounts.md#the-certain-path--the-condition-notification)), so the delay is otherwise the template's own figure. `WeaponMount_RefireTick` (`0040ef94`) counts it down by `SimTickDelta` — about 15 ticks for the 1200 most weapons carry. **`ELF` and `ELF2` carry zero**, and their mount class does not test the timer either: what limits those two is the capacitor, not a cooldown — see [`weapon-mounts.md`](weapon-mounts.md#elf-and-elf2).

Its last two writes set the mount's `+0x33` and `+0x3b` flag blocks, which is what makes an ELF's sustained fire possible; see [`weapon-mounts.md`](weapon-mounts.md#elf-and-elf2).

### Gun convergence — `Mech_ConvergeGunsOnRange` (`0041a74c`)

Every hardpoint is toed in so that its shots cross the sight line at the range the turret is currently aiming at. It runs at the tail of `Mech_TorsoPitchTick`, for the player and the AI alike, and its argument is that tick's third parameter — the 3D distance to the aim point, which `Cockpit_TargetAnglesFromCameraBone` supplies ([`torso-aim.md`](torso-aim.md#aiming-at-a-point)). Its only other caller is `Razor_MovementTick`, since a flyer runs no turret tick: [`razor-flight.md`](razor-flight.md#gun-convergence).

```
for each mount:
    if (range == 0) { mount+0x24 = mount+0x28 = 0; continue }
    muzzle    = WeaponMount_MuzzleOffset(mount)
    muzzle.z -= typeRec+0x66                                  // the sight line's own height
    euler      = Math_EulerToward((0, range, 0), muzzle)
    mount+0x24 = euler[0]; mount+0x28 = euler[2]              // pitch and yaw
```

`WeaponMount_PrepareShot` is the consumer, and it applies each half only when the corresponding gate is clear:

```
if (mount+0x5f == 0 || mount+0x5b == 0) {
    aim = (mount+0x5b == 0 ? mount+0x24 : 0, 0, mount+0x5f == 0 ? mount+0x28 : 0)
    boneFrame = BuildEulerRotationMatrixQ14(aim) * boneFrame
}
```

`mount+0x5b` and `+0x5f` are shape-thread lookups from the hardpoint's `.GL` `+0x02` and `+0x04` ([`../formats/gun-layout-gl.md`](../formats/gun-layout-gl.md)), and `WeaponMount_CtorBase` writes **zero** for a negative record value. Both fields read −1 on every retail chassis, so both gates are clear and **the convergence is live on all of them** — a machine's guns really do toe in and out as its turret walks a target through depth. A centring command passes range 0 and squares them up again; every AI caller of `Mech_CenterTorsoTick` does.

## Power level — `WeaponMount_AdjustPowerLevel` (`0040f48c`)

Energy mount vtable `+0x38`, reached by `WeaponMounts_HandleCommand` codes `0x0c`/`0x0d`/`0x4a`/`0x4e` (`[-]`, `[=]`, keypad `[-]`, keypad `[+]`). Moves the charge target `+0x7b` by ±`0x50` (80), clamped to 0..1200, and sets `+0x34` and `+0x3c` ([below](#the-charge-bar)). A held key steps it once per keyboard auto-repeat ([`cockpit-input.md`](cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes)). `WeaponMounts_IdleAllCapacitors` (`00410d04`, code `0x2c`) calls `WeaponMount_WakeCapacitor` on every mount, writing the idle 820, but sets neither flag, so [the charge bar](#the-charge-bar) puts each target back on the next frame.

`WeaponMount_DemandFullCharge` (`0040f4f0`) would raise the target to 1200 in one step, but its only caller `WeaponMounts_DemandFullChargeOnArmed_Dead` (`00410d50`) has no reference of any kind anywhere in the image — neither a `CALL rel32` nor a stored address — so neither is ever reached.

For a fixed-cost weapon the target changes nothing but the cockpit bar. For a charge-up weapon the target *is* the shot strength: retail `PBEAM` at 960 does 937 damage every 48 ticks, and five presses of `[-]` make it 546 every 28.

### The charge bar

The row's charge bar is built as a slider over 0..`0x400` and carries the drag flag, and a commit on it would store its position in the gauge (`EnergyWeaponGauge_OnChildClick`, `00440ef0`). No press reaches it: the row's select gadget is registered first and its hit rect is the whole hardpoint row, which contains the bar in every retail `.GAU` ([`cockpit-input.md`](cockpit-input.md#where-retail-rects-overlap)). The keys are the only power-level control, and the manual does not mention them.

The slider still takes part in a once-a-frame exchange with the mount, `WeaponMount_PushEnergyGaugeState` (`0040f288`), in a direction set by `+0x34`, byte 1 of the mount's `+0x33` flag block ([`weapon-mounts.md`](weapon-mounts.md#elf-and-elf2)):

| `+0x34` | Direction |
|---|---|
| set | the charge target goes out to the slider as `(target << 10) / 1200` |
| clear | the slider's position comes back as the charge target, `position * 1200 >> 10` |

`WeaponMount_AdjustPowerLevel`, `WeaponMount_ZeroChargeTarget` (`004116ad`), `WeaponMount_DrainCapacitor` (`004116cf`) and `WeaponMount_CtorEnergy` set `+0x34` together with `+0x3c`, the same byte of the `+0x3b` block. On each pool turn `WeaponMount_RefireTick` ANDs `+0x33` with `+0x3b` and clears `+0x3b`, so `+0x34` holds for one turn after any of them and is clear the rest of the time. `WeaponMount_WakeCapacitor` is not among them: on the player's mounts the next push reads the slider back over the 820 it writes.

The hand-off depends on a push falling between any two pool turns, and one always does. `Sim_Run` alternates `Sim_MainTick`, the pool turns' only caller, with `Sim_RenderFrame`, whose `Player_PerFrameCockpitUpdate` runs the push; and `Sim_InitMissionSession` (`004614fc`) builds the cockpit and runs one `Player_PerFrameCockpitUpdate` of its own before the first `Sim_MainTick`. The redraws a modal panel runs push again with no pool turn between, which changes nothing: a set `+0x34` pushes the same value, and a clear one reads back the same slider. Only the local player's mounts have gauges, so only they take part.

With nothing else moving the slider, the read-back returns the value last pushed, and what remains is the round trip's loss, `((target << 10) / 1200) * 1200 >> 10`: at most two units, and none at 0 or 1200. The gauge factory, `WeaponMount_CreateEnergyGauge` (`0040e0e0`), seeds the slider with the raw target, 960 rather than 819, and the session's own push replaces the seed before any pool turn. A fresh mount therefore charges toward 960 for two pool turns and toward 959 from then on; a press of `[-]` from there takes it to 879 and then 878.

## Resolving the hit

`Bullet_FireBurst` calls `Sim_RaycastObjectList` (`00426528`) **before** it spawns any tracer, so the hit is already resolved when the visual is built — see [`beam-visuals.md`](beam-visuals.md) for the sound and tracer it then builds. The sweep itself is documented in [`hit-detection.md`](hit-detection.md#the-sweep--sim_raycastobjectlist-00426528) and the per-mech hit test in [`damage-system.md`](damage-system.md#direct-fire-damage-armor-then-part-deterministic-shield-gated); it clips at terrain first, shortens the ray per hit rather than stopping at the first, and applies damage inside the hit test.

The ray record's `+0x08` is passed along as a walk radius, but the thin-ray terrain mode never reads it.
