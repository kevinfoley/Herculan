# DBSIM.EXE damage system — shields and the direct-fire/explosive damage pathways

Reverse-engineered from `DBSIM.EXE` disassembly (Ghidra project `ES2Recon`). All addresses are DBSIM.EXE virtual addresses. Confirmed against the official *Earthsiege 2 - On-Line Manual.pdf* where noted. See [`weapon-firing.md`](weapon-firing.md) for how a shot gets here in the first place, [`hit-detection.md`](hit-detection.md) for the raycast that delivers it and how each class decides what it struck, [`projectiles.md`](projectiles.md) for the travelling `Bullet` family's own lifecycle, [`dbsim-physics-notes.md`](dbsim-physics-notes.md) for movement/collision/rocket math, [`component-damage.md`](component-damage.md) for what happens once damage reaches a component — the `.DMG` health record, the cascade, going out of the fight, salvage — and [`weapon-damage-types.md`](weapon-damage-types.md) for what each `PROJ.DAT` class is, [`../formats/proj-dat.md`](../formats/proj-dat.md) for the per-weapon damage figures and [`weapon-mounts.md`](weapon-mounts.md#the-chance-path--the-destruction-roll) for weapon-mount destruction.

How a weapon's fire turns into a mech taking damage has two different pathways — **direct fire** (deterministic, single component, shield-gated) and **explosive/area-of-effect** (random, multi-component, distance falloff, also shield-gated but via a separate parallel implementation) — that share a common raycast entry point, `Sim_RaycastObjectList` ([`hit-detection.md`](hit-detection.md#the-sweep--sim_raycastobjectlist-00426528)), and converge on the same final health-writing primitive (`Mech_ComponentDamageWrite` (`00417de4`) for a mech). They differ in shield-absorption code (parallel but separate), in how many components get selected (one deterministic versus many random) and in whether there is a distance falloff.

## Direct-fire damage: armor-then-part, deterministic, shield-gated

**`Mech_DirectFireHitTest` (`00418ba8`, mech vtable `+0x20`), called by `Sim_RaycastObjectList` on every raycast candidate.** In order:

1. **Coarse range check.** Rejects the candidate outright when `200 + rayLength + typeRecord[0x1a] < |muzzle - mech|`, keeping the transform work off everything nowhere near the shot.
2. **Geometry and shield absorption — `Mech_ShieldAbsorb_DirectFire` (`00413cc4`).** The mech's centre of mass (`typeRecord+0x18` above its origin) is brought into **muzzle space**, where the ray is the Y axis, so the hit is two comparisons: the centre in front and within the ray's remaining length (an *unsigned* compare, which is what rejects anything behind the muzzle), and its 2D distance off the axis under the hit radius `typeRecord+0x1a`. Then `absorbed = min(incomingDamage, remainingShieldInZone)`, with both the incoming damage and the zone's charge reduced by it (a hard cap, [below](#the-shield-system)). The facing is picked by **where the muzzle sits in the mech's frame**, so it is the shooter's bearing that exposes the rear array.

It returns the ray's entry point into the hit cylinder, `alongAxis - (radius - offAxis)` floored at 1, which is what `Sim_RaycastObjectList` shortens the ray to. **A fully absorbed shot still returns a hit distance and still stops the ray** — shields do not let fire through to whatever is behind — and the caller spawns only a hit-spark effect.
3. **Component selection — `Mech_SelectStruckComponent` (`0040c9d4`).** Only reached if some damage penetrated shields. Tests the mech's `col\<NAME>.COL` hit-sphere model cluster by cluster to find the ONE component struck — **not** a random roll, unlike the explosion path. Decoded in [`hit-detection.md`](hit-detection.md#the-test--mech_selectstruckcomponent-0040c9d4). **Missing every sphere is a clean miss**: the shield cylinder is only a gate, and the shot passes on to whatever stands behind.
4. **Damage application — `Mech_ApplyDirectFireDamage` (`004188c8`).** Takes the splash factor off the top (`Math_Q10Multiply(shotData[+8], armorDamage)`, a Q10 fraction of the armour damage that already came through the shields — the [record's field](../formats/proj-dat.md#layout), which is not a per-weapon-type effectiveness scale: [`weapon-damage-types.md`](weapon-damage-types.md#weapon-type-effectiveness)) and **splits** the shot: the remainder goes to that component's general health via the mech's `+0x74` slot (`Mech_ComponentDamageWrite` (`00417de4`), see [`component-damage.md`](component-damage.md)), and the split-off share, when non-zero, becomes a 500-unit secondary explosion through this same machine's `+0x70` — a direct call, not a sweep, so it cannot reach anything standing beside it. That slot is `Mech_ApplyExplosiveDamage`, so the share runs through `Mech_ShieldAbsorb_Explosive` a second time, the facing's shields taking it again, before the per-component roll and falloff [below](#a-mech--mech_applyexplosivedamage-004187d0). Health is bucketed into 8 levels (`>>5` of the 0–256 Q8 percentage) for state-transition/alert purposes ([Open](#open)). A bucket change also rolls to knock out a weapon mount at that component — see [`weapon-mounts.md`](weapon-mounts.md#the-chance-path--the-destruction-roll).

This is fundamentally different in shape from the explosion path: precisely-aimed weapons hit what you aimed at; explosions spray damage around imprecisely.

The hit cylinder's centre height and radius, `typeRecord+0x18` and `+0x1a`, are fields of the [mech type record](mech-locomotion.md#mech-type-record).

`Mech_DirectFireHitTest` is invoked only polymorphically, as `obj[+0x20](...)`. **Beams do reach it through `Sim_RaycastObjectList` and nowhere else** — the beam dispatch was traced end to end in [`weapon-firing.md`](weapon-firing.md), and it calls `Bullet_FireBurst`, which calls the raycast; no path applies damage directly to a locked target.

## Explosive damage — the `+0x70` slot

Every simulation object carries a vtable `+0x70` that says what an explosion does to it. **Three functions call it, at four call sites, and only one of them is a sweep:**

| Caller | Shape | Damage | Radius |
|---|---|---|---|
| `Damage_ExplosiveBlastSweep` (`00426a20`) | sweep of the live-object list | the caller's | the caller's |
| `Mech_ApplyDirectFireDamage` (`004188c8`) | direct, on the struck object only | the shot's splash-factor share | 500 |
| `Mech_CollisionTest` (`00418f74`) | direct, on both parties — the two sites | momentum difference | 1200 |

The three implementations share the falloff's shape and nothing else. All take `(this, short damage, int *hitPoint, short blastRadius, void *attacker)`.

### The sweep — `Damage_ExplosiveBlastSweep` (`00426a20`)

Walks the live-object list; skips an object whose mission group still carries an action (see [`hit-detection.md`](hit-detection.md#the-sweep--sim_raycastobjectlist-00426528)) and the excluded object (`param_5`); calls `+0x70` on everything whose `distance - obj[+0x5c] < blastRadius`. **Surface to centre, not centre to centre** — the object's body radius is subtracted first. Nothing stops, shortens or orders the sweep: a wall between two objects shields neither.

Exactly **3 call sites**, all terminal events rather than routine fire:

1. **`Meteor_Tick` (`00409d2c`)** — the **drop pod** landing, not a missile. Detonates `(pos, 3000, 10000, 0, null)` the instant its altitude dips below the terrain. See [`mission-deployment.md`](mission-deployment.md).
2. **`Bullet_TickUpdate` (`0040b124`), the `type == 9` branch** — the **Plasma cannon**, and a bullet subtype rather than bullets as a class: `(pos, 4000, armourDamage, owner, null)`. It empties its own shot record first, so the blast is the whole of the weapon; see [`projectiles.md`](projectiles.md#the-plasma-branch).
3. **`Mech_BehaviourRamTick` (`0041e488`)** — an **AI ramming attack**: a block detonates `(pos, 3000, 2000, 0, self)`, excluding the machine itself, and then finishes it off. The state, its trigger and the self-destruction are [`ai-combat-states.md`](ai-combat-states.md#ramming-17--mech_behaviourramthink-0041e570)'s.

### Where a component stands — the `+0x58` slot

The falloff is measured from the component, not from the object's origin, and `+0x58` is what answers where the component is. Signature `(this, short componentIndex, int *out)` for both real implementations.

- **A mech — `Mech_ComponentPosition` (`0041b60c`).** Reads the `HercPiece` fields at `+0x0c` (a shape part id) and `+0x0e` (a pointer to a point). A piece with a null point answers **the machine's own position**, which is at its feet. Otherwise the point goes through the machine's world transform, composing the part's posed node transform first when the part id is `> 0` — note strictly greater, so part 0 would stay in the object frame; no retail `.COL` uses part 0. A part the shape does not have falls back to the identity transform at `006c572c`.

Those two fields are not in the `.DMG` file: they are bound at load from the `.COL`, to the centre of each cluster's bounding sphere — [`dmg-damage-file.md`](../formats/dmg-damage-file.md#the-piece-record).

A mech `.COL` names 6–16 of the 29 slots, so most of a HERC — every internal, and on most chassis the shoulders — is measured from the ground under the machine rather than from where the part is.
- **A structure — `Base_ComponentPosition` (`00406808`).** The component's own point from `BASES.DAT` (see [`../formats/bases-dat.md`](../formats/bases-dat.md#the-component-record-30-bytes)) through the structure's world transform. No node to resolve: a building's parts do not move. It is **not** the `BASECOL.DAT` geometry a shot is tested against, and several types put the blast point above the spheres.
- **A flyer** inherits the base class's `SimObject_ComponentPositionNoOp` (`00411a3c`), which is `push ebp; mov ebp,esp; pop ebp; ret` — it never writes its out-parameter, and nothing reaches it, but not because the slot goes unused. Of the nine `+0x58` call sites, six are already on a mech or a structure and one is a weapon mount's own unrelated vtable. The two that dispatch on an arbitrary object are both fenced off a flyer by a component index that comes back `-1` for it:
  - `Rocket_HomingSteer` (`0040a2fd`) asks only when `rocket+0x5a >= 0`, and `Rocket_Fire` fills that from the target's `+0x54`, which for a flyer is `SimObject_ComponentNearestAim_None` (`00411a44`) — a bare `return -1` — and for a HERC or a structure the live component nearest the round's aim ([`rockets.md`](rockets.md#spawning--rocket_fire-0040a9c4)).
  - `TargetingPod_ResolveAimPoint` (`0040e4dc`), behind `Player_ResolveTargetAimPoint`, asks at `0040e530` only when `pod+0x7d >= 0`, and `TargetingPod_ResetComponentLock` (`0040e484`) writes `-1` there for every target whose target class (`obj+0x1a8`) is not 0 — see [`target-selection.md`](target-selection.md#component-targeting--the-targeting-pod).

A port that resolves a component position generically has to keep one of those guards, or it will ask a flyer where its component 0 is and get an answer the original never had to produce.

### A mech — `Mech_ApplyExplosiveDamage` (`004187d0`)

1. **Facing.** The bearing to the hit point against the machine's own heading, front inside `0x4000` either way — the same ±90° window `Mech_GetShieldByHeading` uses. Unlike the direct-fire path it is the machine's facing that decides, not the geometry of a ray.
2. **Shields — `Mech_ShieldAbsorb_Explosive` (`00413c68`)**, the explosion path's own implementation of what `Mech_ShieldAbsorb_DirectFire` does for direct fire. Computes `scaledDamage = Math_Q10Multiply(1000, damage)`, takes it out of the chosen zone, and returns `overflow × 0x400 / 1000` if the zone went negative and 0 otherwise. A facing that swallows the blast ends it here. Confirms shields gate both pathways, matching the manual: "shields cause missiles to explode on contact, preventing most of their blast power from reaching the HERC's armor."

**The two scales are exact inverses.** Input is multiplied by `1000/1024` and output by `1024/1000`, so a blast of face value *d* removes `0.977d` from the facing and, against an empty one, hands the components behind it *d* — the same exchange rate direct fire has, and `PROJ.DAT`'s two damage figures are directly comparable. The return is a `short`, so an overflow past 32767 would wrap; the largest blast in the game is the drop pod's 10000, which reaches about 6400 against a full facing.
3. **A roll per component**, over a fixed 29 slots. Each live one draws `rand & 0xfff < 0x802` (≈51%) to be considered at all, so two identical blasts do not wreck the same parts.
4. **Distance and falloff.** The component's `+0x58` position against the hit point; inside `blastRadius` it takes `overflow × (blastRadius - distance) / blastRadius` through `+0x74`.

### A structure — `Base_ApplyExplosiveDamage` (`00404f20`)

No shields, no facing, and its parts stand where the type record says.

```
if (typeRec[+0x1e] != 0) return                        // invulnerable
if (wreck) return                                      // the three-part test Base_DirectFireHitTest opens with
if (typeRec[+0x38] == 0) return                        // no BASECOL.DAT model -> immune to blasts
for (i = 0; i < typeRec[+0x12]; i++) {
    if (!alive[i]) continue
    if ((rand & 0xfff) >= 0x1004) continue             // cannot fail -- see below
    d = |vtable+0x58(i) - hitPoint|
    if (d < blastRadius) vtable+0x74(i, (blastRadius - d) * damage / blastRadius, attacker)
}
```

`wreck` is the test [`hit-detection.md`](hit-detection.md#base_directfirehittest--00405038) gives, component 0's collapse stage included.

**The per-component roll can never fail.** `0x1004` is one above the largest value the `0xfff` mask can produce, so every live component is measured. The draw still advances the shared generator.

**A type with no `BASECOL.DAT` model is immune to explosions** however close they go off, even though direct fire still hurts it through the shape's collision volume — 40 of the 65 retail types.

### A flyer — the base slot `SimObject_ApplyExplosiveDamage` (`00411b3c`)

The flyer class does not override `+0x70`; it inherits the shared base implementation, which is why it is the simplest of the three. No roll, no component selection, no shields:

```
vtable+0x74(0, (blastRadius - (|hitPoint - obj[+0x26]| - obj[+0x5c])) * damage / blastRadius, attacker)
```

Component 0 is the only one a flyer has. There is no range test — the sweep has already made it — so the numerator cannot come out negative. The body radius subtracted is the same one the sweep subtracted, and a flyer's is zero (see [`hit-detection.md`](hit-detection.md#the-three-radius-slots)); for a class whose radius is not zero, a blast going off inside it scales by more than one.

### A collision — `Mech_CollisionTest` (`00418f74`)

**Walking into another machine hurts both of you**, and it is a direct call on each party rather than a sweep, so a third machine standing beside the crash takes nothing. The blocking half of the same function, and the structure record it also writes, are in [`mech-locomotion.md`](mech-locomotion.md#collision).

```
if (struck.targetClass != Herc) return                     // a structure or flyer blocks, unhurt
closing = ({0, struck.speed, 0} * struck.rotation) * transpose(mover.rotation)
impulse = Q10(moverTypeRec[+0x4e], mover.speed) - Q10(struckTypeRec[+0x4e], closing.y)
damage  = Q10(300, impulse)
if (damage <= 200) return
at = ( (mover.x + struck.x)/2, (mover.y + struck.y)/2,
       min(eyeNodeZ(mover), eyeNodeZ(struck)) )
mover.vtable+0x70 (damage, at, 1200, struck)
struck.vtable+0x70(damage, at, 1200, mover)
```

`typeRec+0x4e` is the chassis' mass and `closing.y` is the struck machine's travel resolved onto the mover's forward axis, so a head-on meeting adds and being rear-ended by something slower subtracts. Both machines take the same figure. The 200 threshold is what keeps a machine shuffling against a wall from grinding itself down.

**The impact point mixes two frames.** X and Y are the world midpoint of the two machines, but Z is the lower of the two cockpit-eye nodes' *model-space* heights — roughly torso height above each machine's own feet — used as though it were a world height. On level ground near sea level the two nearly agree; on a hill the blast goes off well below the machines.

## The shield system

**Struct layout** (confirmed in disassembly of `Shield_Init` `00413a90`): five consecutive `short` fields at `this+0x222`:

| Offset  | Field |
|---------|-------|
| `+0x222`| front charge |
| `+0x224`| rear charge |
| `+0x226`| balance, Q10 over 0–1024 (`0x200` = even) |
| `+0x228`| max — caps `front + rear`, raised by a Shield Pod |
| `+0x22a`| base max — the type's capacity before any pod; never written again |

**There is one pool, not two.** `+0x228` caps the *sum*; balance decides the split. `Shield_Init` sets both maxes to `baseValue` and both charges to `baseValue >> 1`, so a machine spawns full and evenly split.

**Capacity is a fleet-wide constant, not a per-type stat.** `baseValue` is `typeRecord+0xc0` — in asm, `ADD ESI,0x2` then `[ESI+0xbe]`, i.e. record-relative offset **190**, which is the file offset directly ([mech type record](mech-locomotion.md#mech-type-record)). Every retail HERC `.DAT` carries **3500** there; only the non-HERC SPIDER differs, at 0. A Shield Pod is the only thing that moves it.

**Capacity — `Mech_ComputeShieldCapacity` (`00417bec`).** Writes `+0x228` via `Shield_SetMax` (`00413ab8`). Called from `Mech_ConfigureLoadout`, where `Shield_RefillToBalance` (`00413ac8`) then refills to `max` at the current balance, **and again from `Mech_ComponentDamageWrite`** — so the array a machine can hold shrinks as its generator is shot, and both damage terms below are read fresh on each call rather than sampled at spawn.

```
capacity = 3500
if (generatorDamage > 0x80)                // dependent-subpiece 4, read inline
    capacity = Q10(3500, ((generatorDamage - 0x80) / 0x19) * -0x66 + 0x400)  // → 50% at worst
if (ShieldPod)
    capacity += Q10(podCurve(podDamage), 3500)                               // → doubles at best
```

`podDamage` is `Component_ReadDamagePercent(mech+0x206, pod.GL[+0x17] + 19)` — the pod's own hardpoint component, like any other mount's, so it is shooting the hardpoint a pod sits on that degrades it.

The pod's share is a fraction of the *undamaged* base, so a battered machine still gets the full pod bonus. `podCurve` is the damage curve every pod bonus shares, owned by [equipment-pods.md](equipment-pods.md#the-damage-curve-the-pod-bonuses-share).

**Getter:** `Mech_GetShieldByHeading` (`004154d0`, mech vtable `+0x34`) — given a heading angle, returns `+0x222` within ±90° of front, else `+0x224`.

Both absorption implementations (`Mech_ShieldAbsorb_DirectFire` (`00413cc4`) direct fire, `Mech_ShieldAbsorb_Explosive` (`00413c68`) explosions) are a hard cap — `min(damage, remainingCharge)` — not a threshold gate. A hit exceeding what a zone holds drains it to zero and its excess carries through in the same hit.

### Recharge tick — `Shield_RechargeTick` (`00413b38`)

Called once per mech per tick from `Mech_PerTickSystemsUpdate`, with whatever the weapon mounts left unclaimed. See [reactor-energy-pool.md](reactor-energy-pool.md) for where that budget comes from.

```
deficit  = max - (front + rear)
granted  = min(request, 5, deficit)         // CMP word ptr [EBP+0xc],0x5
newTotal = front + rear + granted
front   := moveToward(front, Q10(balance, newTotal), deficit < 0 ? 10000 : 0x41)
rear     = newTotal - front
return request - granted
```

**5 units per tick is the recharge-rate constant** and it is per *tick*, not per unit time. At 3500 capacity and DBSIM's hard 25 Hz cap that is 700 ticks — **28 s from empty**, confirmed against retail. The front slew runs whether or not anything was granted, which is why moving the balance redistributes charge on an already-full array. The `10000` step is effectively a snap, reachable only when `max` drops below the charge held.

**The draw does not scale with capacity.** `max` is read only to size the deficit; the 5 is an immediate, so a Shield Pod's doubled array costs the pool the same 5 per tick and takes twice as long — 56 s from empty — to get there. That is the manual's "without increasing the drain on your Master Energy Pool", and the same page's "You cannot divert extra power to the shields": the shield system has exactly one rate and nothing, pod or player, moves it. The clause names the absence of a scaling the code never had, but it is not vacuous for the family — the Turbo Pod *does* buy its charge out of the same leftover budget, in its own pool turn. A Shield Pod keeps the base pool turn, `WeaponMount_RefireTick`, which counts the mount's refire timer down and returns the budget untouched ([equipment-pods.md](equipment-pods.md#what-each-class-actually-overrides)).

### Balance-adjustment input — player's own mech only

`Player_PerFrameCockpitUpdate` (`0041b130`, run per frame for `LocalPlayerMech`) calls `Shield_BalanceInputRead` (`00413bc8`) unconditionally. That function:

- copies the gauge's 15-byte state block (`ShieldsGauge_GetStateBlock`, UI slot `+0x1e9`), and if either click flag is set calls `Shield_BalanceAdjust`, clearing the flag (once per press) — the click and key path that sets the flags is [`cockpit-input.md`](cockpit-input.md#8-worked-example-the-shield-balance-rocker);
- writes back `(front << 10) / baseMax`, `(rear << 10) / baseMax` and the raw balance, which the gauge paints ([`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#shieldsgauge)).

`Shield_BalanceAdjust` (`00413af8`) adds `±0x66` (102) to `+0x226`, clamped to `[0, 0x400]`. That is just short of a tenth of the range: five presses from the centre of `0x200` reach 1022 forward or 2 rear, and only the sixth, which the clamp stops, puts the whole array on one facing. At 2 the front still holds `Q10(2, max)` — 6 of a 3500 array — while the readout already prints 0 ([`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#readouts--shieldsgauge_updatereadouts-00444a68)). The manual binds `[` to rear and `]` to forward; direction 1 is the `+0x66` case, and balance is the front's share. Nothing is spent moving the balance.

The gauge paints charge as rings and balance as numbers, in two different places — [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#shieldsgauge).

Shield recharge is a background trickle on every mech, AI and player alike. Balance adjustment is player input layered on top, touching only the balance field, which the recharge tick reads back on the next tick. The two never call each other.

## Open

- **Open:** whether the 8-level health bucketing (`Mech_ApplyDirectFireDamage`'s `>>5` of the Q8 percentage) matches the manual's 5-color status system (Green/Yellow/Orange/Red/Gray).
