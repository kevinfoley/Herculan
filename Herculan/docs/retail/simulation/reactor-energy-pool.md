# DBSIM.EXE reactor and Master Energy Pool

The reactor and the pool are separate things: the reactor is a **rate** (`mech+0x256`), the pool is a **capacitor** (`mech+0x292`). Consumers draw on the pool, never on the reactor.

## The per-tick cycle — `Mech_PerTickSystemsUpdate` (`0041aa5c`)

Called once per live mech per tick from `Sim_MainTick` (`0045f464`). Its first five statements are the whole power model:

```
pool += IntegrateRateOverTick(reactorRate)      // ADD word ptr [EBX+0x292],AX
budget = pool - 500                             // ADD DX,0xfe0c
budget = weaponMountManager->vtable[0](budget, mech)
budget = Shield_RechargeTick(mech+0x222, budget)
pool   = budget + 500                           // overwrite, not subtract
pool   = clamp(pool, 0, 10000)
```

Consumption is not a subtraction: the pool is **rebuilt** each tick from whatever survives the pass.

- **Reserve = 500.** Held out of every arbitration, so a machine under sustained load settles at 500 rather than 0. Below it the budget goes negative, which energy mounts read as "give charge back".
- **Ceiling = 10000**, also the value `Mech_Constructor` writes at spawn — a HERC powers up full.
- **Order is weapons, then shields.** The manual's "movement, shields, and weapons, in that order" is wrong on both count and order; locomotion never reads the pool. The consequence it describes is real: shields only ever get what the weapons leave.

## Reactor output rate — `Mech_ComputeReactorRate` (`00417d08`)

```
rate = 20                                  // MOV ESI,0x14 — a literal
if   (mech+0xab) rate = Q10(200, 20)  = 3  // reactor critical
elif (mech+0xaa) rate = Q10(600, 20)  = 11 // reactor degraded
if (EnergyPod)
    rate += Q10(podCurve(podDamage), 20)   // up to +20; nothing past the curve's cutoff
```

`podCurve` is the damage curve every pod bonus shares, owned by [`equipment-pods.md`](equipment-pods.md#the-damage-curve-both-bonuses-share). The pod is `mech+0x313`, slot 3 of the array [`equipment-pods.md`](equipment-pods.md) describes.

**Base rate is uniform across the fleet.** The function never reads the mech type record. At the 25 Hz tick `IntegrateRateOverTick(20)` yields 6 pool units/tick, i.e. 150/s.

**The Energy Pod doubles the recharge rate, not the pool's capacity.** The manual says capacity. A pristine pod adds the full 20 to the base 20, while the ceiling is the literal 10000 in both `Mech_Constructor` and the clamp, so nothing can raise it. The manual's own aside, a modest increase in the Pool recharge rate, describes what the code does.

**Computed once, at spawn, from a pristine machine.** `Mech_ComputeReactorRate` has exactly one reference in the binary — the tail of `Mech_ConfigureLoadout` (`004175dc`), whose one call, in `DBSim_SpawnMissionObjects` (`004258a2`), comes before either way a machine spawns damaged: a roster record's [starting condition](component-damage.md#starting-condition--mech_applystartingcondition-004178e8) and a squad machine's [carried-over condition](component-damage.md#a-squad-machines-condition--mech_applysquadcondition-00415068). The two reactor flags are clear and the pod undamaged at that moment, so every HERC generates 20, or 40 with an Energy Pod, for the whole mission, and the degraded and critical terms above never apply.

Its sibling `Mech_ComputeShieldCapacity` (`00417bec`) is **not** like this: `Mech_ComponentDamageWrite` (`00417de4`) calls it as well as the spawn path, so shield capacity really does shrink as the generator is shot.

### Reactor damage flags

`Mech_ComponentDamageWrite` (`00417de4`) latches them off dependent-subpiece **5**'s damage:

| Subpiece 5 damage | Flag | `Mech_ComputeReactorRate` term | Also |
|---|---|---|---|
| ≤ 50% (`0x80`) | — | 20 | — |
| > 50%, < 75% (`0xc1`) | `mech+0xaa` | 11 (~59%) | movement penalty; alert sound for the player |
| ≥ 75% | `mech+0xab` | 3 (~20%) | movement penalty; alert sound |

Identified as the reactor by what reads them: the rate function and the movement penalty. Only the movement penalty is live, since the rate is worked out before either flag can be set. Both latch and are never cleared, and the check is gated on **both** being clear — so once `+0xaa` sets, `+0xab` is only reachable by a single hit crossing both thresholds at once.

## Weapon energy arbitration — `WeaponMounts_ArbitrateEnergy` (`004107e4`)

Vtable slot 0 of the mount-manager object at `mech+0x202`, for both the local (`00499238`) and remote (`00499338`) manager classes. The mounts it serves are in [`weapon-mounts.md`](weapon-mounts.md).

- Mounts are served one at a time, highest priority first. Priority is the mount's `+0x7b`, except a mount already mid-charge (`+0x43`) reports 10000 and jumps the queue.
- The player's selected mount (`manager+0x1d`) is served before the ranking is consulted; the AI passes `-1` and goes straight to the ranking.
- Per mount, `WeaponMount_ChargeCapacitor` (`0040f00c`) takes `min(chargeRate +0x7f, budget, capacitor deficit)` into `+0x7d` and passes the remainder on.
- Once any mount reports itself mid-charge, every mount after it targets zero instead and **bleeds its capacitor back into the pool** at 5/tick. One energy weapon charges at a time.
- Ammunition mounts consume nothing — their slot-`0x34` override returns the budget untouched.
- The equipment pods are mounts too and take the same turn. Every pod but one inherits the refire-timer turn and returns the budget untouched; the **Turbo Pod** is the only pod that draws on the pool, buying its charge back out of what the guns left, so it competes with the weapons for the same budget and a machine firing hard refills it slowly. The spend, the `min(20, budget, 2000 - charge)` buy-back and the rest of the pod are in [`equipment-pods.md`](equipment-pods.md#what-each-class-actually-overrides).
- PLAS (id 25) is half-efficiency: its deficit counts double and only half of what it draws is stored.
- The **unlimited energy and ammunition** setting refunds the whole pass's consumption, player only (`DAT_004a9ed6 == 0 && DAT_004a9edc == 1`): the mounts are served and charged as they otherwise would be, and the budget handed back to the pool is the one the pass was called with. Both globals are `script.dat` header fields and the practice missions screen is what sets them — see [`difficulty.md`](difficulty.md#the-two-sibling-cheats).

An idle machine draws nothing: every energy mount powers up with `+0x7d` already at `+0x7b`, so the deficit is zero until a shot is demanded.

## Cockpit readouts

The pool's one cockpit readout is the **Master Energy Pool meter**: `Player_PerFrameCockpitUpdate` (`0041b130`) computes `(pool << 10) / 10000` and pushes it to the LED bar at cockpit slot `+0x1e5`, whose range is `0x400` — see [`cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md#led-gauges). The shield rings and numbers show the shield array, not the pool ([`cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md#shieldsgauge)), and a weapon's charge bar shows its own capacitor ([`weapon-mounts.md`](weapon-mounts.md#energy)).

## Shield refill time

The recharge cap is per *tick*, not per unit time, but the tick is held to 25 Hz ([`dbsim-physics-notes.md`](dbsim-physics-notes.md#fixed-point-math-toolkit)), so the refill time of 28 s from empty ([`damage-system.md`](damage-system.md#recharge-tick--shield_rechargetick-00413b38)) does not vary with hardware. Retail takes about 30 s, matching.

At mission start the shield rings fade in black to green. That is the power-up animation, not charge: both facings are full from `Shield_Init` onward ([`cockpit-hud-widgets.md`](../formats/cockpit-hud-widgets.md#shield-rings-fill)).

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A damaged reactor lowers the reactor's output | `Mech_ComputeReactorRate` reads the two reactor flags, but its one call comes at spawn before anything can set them, so the output is always 20, or 40 with an Energy Pod |
