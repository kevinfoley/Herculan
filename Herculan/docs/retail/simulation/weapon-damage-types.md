# DBSIM.EXE weapon damage types — what the `PROJ.DAT` classes are

Reverse-engineered from `DBSIM.EXE` disassembly (Ghidra project `ES2Recon`). All addresses are DBSIM.EXE virtual addresses. Confirmed against the official *Earthsiege 2 - On-Line Manual.pdf* where noted.

Covers which projectile class each weapon's shot is and whether any weapon type changes what a defence does. The records and their retail values are in [`../formats/proj-dat.md`](../formats/proj-dat.md); what a hit does to the target is in [`damage-system.md`](damage-system.md) and [`component-damage.md`](component-damage.md); the mount side of a weapon, including losing one to damage, is in [`weapon-mounts.md`](weapon-mounts.md).

## Weapon-type effectiveness

The manual says EMP cannons and the ELF are effective against shields, that energy weapons generally are and projectile weapons are not, and that lasers have limited effect on them.

**None of it is a rule in the code.** The whole of what lives there is the two damage figures each [`PROJ.DAT` record](../formats/proj-dat.md#layout) carries, one against shields and one against armour, and the manual's weapon characters are those figures: the EMP records' shield damage is several times their armour damage, the autocannons' the reverse. `Mech_DirectFireHitTest` (`00418ba8`), `Mech_ShieldAbsorb_DirectFire` (`00413cc4`), `Mech_ShieldAbsorb_Explosive` (`00413c68`) and `Mech_ApplyDirectFireDamage` (`004188c8`) read from the shot record only its ray, its two damage figures, its splash factor, its effect arrays, its owner and a rocket subtype id that sets the target's scanner ([`target-selection.md`](target-selection.md#how-an-ai-machines-radar-is-set)), so nothing scales either figure by the target's defence type. The one multiply that looks like a type scale, `Math_Q10Multiply(shotData[+8], armorDamage)` in `Mech_ApplyDirectFireDamage`, is the splash factor, which diverts a share of the hit into a secondary explosion ([`damage-system.md`](damage-system.md#direct-fire-damage-armor-then-part-deterministic-shield-gated)).

`Bullet_FireBurst` builds the shot record right before its `Sim_RaycastObjectList` (`00426528`) call from the firing weapon's record: [`weapon-firing.md`](weapon-firing.md#the-shot-record) has the layout, and [`../formats/proj-dat.md`](../formats/proj-dat.md#layout) the power scale applied to the two figures.

## `Type` — a firing-mechanism selector

The five callers of `Proj_LookupRecord` ([`../formats/proj-dat.md`](../formats/proj-dat.md#lookup)) and the three of `Bullet_FireBurst` each hardcode a literal `Type`. `Bullet_FireBurst`'s are the two weapon-mount fire dispatches (`WeaponMount_FireDispatch_GunBeam`, call at `0040eae0`; `ElfMount_FireDispatch`, call at `0040ecc5`) and `Base_TransportThinkTick` (`00404a65`), so a **structure fires beams through the same path a HERC does**. Each `Type` is a genuinely different projectile *class* (different vtable, different construction):

| `Type` | Constructor | Object kind | Real `PROJ.DAT` shape |
|---|---|---|---|
| `0` | `Rocket_Construct` (`0040a948`) | the launcher round (14-byte type table `ROCKETS.DAT`, vtable `RocketVtable` (`00498448`)) — see [`rockets.md`](rockets.md) | 5 entries, splash factor 500 uniformly, real `Speed`, armor≫shield |
| `2` | `Bullet_Construct` (`0040af6c`) | the travelling gun round (own 14-byte type table `BULLETS.DAT`, own vtable `BulletVtable` (`00498628`)) — see [`projectiles.md`](projectiles.md) | mixed: ATC20/35/50-shaped progression *and* EMP-shaped high-shield entries — splash factor 0 for all but subtype id 9 ([Plasma cannon](#plasma-cannon)) |
| `3` | `Grenade_Construct` (`0040ac3c`) | a cut `Grenade` class with no caller found, see below | 3 entries, shield==armor exactly, splash factor 1000/500/500, named by no weapon template |
| `4` | `Bullet_FireBurst` (`0040bf74`) | **no persistent simulated object at all** — resolves its raycast hit synchronously inside the call itself, then spawns pure-visual tracer segments | every `Type=4` record has `Speed=0`, no exceptions |

`Type=4`'s "no persistent object, resolves at the call site, always `Speed=0`" combination is the concrete mechanical definition of a beam/hitscan weapon. Only `0` and `2` are live classes: the ammunition dispatch (`WeaponMount_FireDispatch_Missile`) tests for `Type == 0` and sends everything else to `Bullet_Fire`, and `Rocket_Fire` always builds the `Type 0` class.

**No path into `Type 3` is found** ([Open](../formats/proj-dat.md#open)): no weapon template's `PROJ.DAT` index names records 7–9, `GrenadeMount_FireDispatch` calls `Grenade_FireNoOp` rather than the constructor, and `es2_xref.py` finds no reference to `Grenade_Construct`. Everything downstream hangs on that constructor: it is the only `Proj_LookupRecord` caller that passes a category of 3; `GrenadeVtable` (`004984fc`) is installed only by it; and that vtable's per-tick slot is `Grenade_TickNoOp` (`0040acb4`), a bare `return 0`, so an instance would never move and never die.

**The class is `GRENADE`, and the binary says so itself**: its Borland class record is at `0040acdc` ([`../formats/borland-rtti.md`](../formats/borland-rtti.md)), and `GrenadeVtable` points back to it from `-0x0c`. The record's destructor (`0040ad2c`) is the one in the vtable's `+0x08` slot. Its one base is `PROJECTILE` (`0040c2d0`), the same as for `ROCKET` (`0040ab3d`) and `BULLET` (`0040b5fd`). That makes it their sibling, which the vtables confirm: all three install `ProjectileBaseVtable` before their own. `ROCKET` and `BULLET` each also have a pointer-type record (`"ROCKET *"`, `"BULLET *"`), but `GRENADE` has none, so no code handles it through a typed pointer.

So `Type 3` is a cut **grenade** weapon class, not an unnamed stub, and its three `PROJ.DAT` records are that weapon's data left in the shipped file — see [`../cut-content.md`](../cut-content.md#projectiles).

Which weapons each class carries, from the weapon templates' `PROJ.DAT` indices ([`../formats/proj-dat.md`](../formats/proj-dat.md#the-retail-records)):
- **`Type 4` (beam)** is the lasers, the particle beams and the two ELFs. The ELFs' records (150/200, 200/300) are the two far below the others' 1000-plus figures.
- **`Type 2` (real flight time, no splash)** is the autocannons and the EMP cannons, and the Plasma cannon as the one outlier.
- **`Type 0`** is the missile weapons: its five subtype ids are the five `ROCKETS.DAT` records, and the four the `MSL` launchers reach are `SARH`/`ARH`/`ARM`/`EO` while `BMSL` takes the fifth. `Type 3`'s three entries are data for the grenade class, which no weapon template names.

A weapon's `(Type, id)` pair is set upstream, in the mount template table ([`../formats/weapons-dat-sim.md`](../formats/weapons-dat-sim.md)) via each template's `PROJ.DAT` index, and the fired shot resolves its record again by that pair ([`../formats/proj-dat.md`](../formats/proj-dat.md#lookup)). The subtype id also indexes `BULLETS.DAT`/`ROCKETS.DAT`/`BEAM.DAT` for model data.

### Plasma cannon

The one `Type 2` record with a splash factor (shield and armour damage both 3000, factor 1000) is subtype id 9. The `Bullet` class's vtable (`BulletVtable` (`00498628`)) per-tick slot (`+0x14`) is `Bullet_TickUpdate` (`0040b124`), whose `type == 9` branch (checked via `*(char*)(this+0x41) == '\t'`) calls the explosion formula directly instead of the ordinary single-target hit path — `this+0x41` is exactly where every projectile constructor (`Rocket_Construct`/`Bullet_Construct`/`Grenade_Construct`) stores its own subtype id argument, so this is checking subtype id 9 on a live `Bullet` instance. `(Type=2, id=9)` is mechanically a `Bullet` (real flight time, unlike true `Beam`s) that explodes with splash on impact (unlike every other `Bullet`), matching the manual's Plasma description exactly.
