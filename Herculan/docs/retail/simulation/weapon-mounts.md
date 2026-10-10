# DBSIM.EXE weapon mounts: loadout, naming, magazines, capacitors, selection

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

Covers how a fit becomes mounts, what those mounts hold, and how the player arms them. What happens when the trigger is pulled is in [`weapon-firing.md`](weapon-firing.md); the pool arbitration they feed is in [`reactor-energy-pool.md`](reactor-energy-pool.md); the widgets they drive are in [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#weapon-hardpoint-rows); the template table itself is in [`../formats/weapons-dat-sim.md`](../formats/weapons-dat-sim.md).

## The join — `MechLoadout_ConstructWeaponMounts` (`0040fff8`)

**The chassis drives the join, not the fit.** `Mech_ConfigureLoadout` (`004175dc`) passes the mech type's hardpoint list (`Mech_GetGunLayout` (`00420634`) → the `gl\<HERC>.GL` record array) plus the mission's two parallel per-slot arrays. The factory walks the hardpoint records **in file order**, and each record's byte at `+0x17` indexes the fit arrays:

```
weaponId     = weaponRefs[record[0x17]]      // negative clamps to 0
secondaryKey = ammoTypes [record[0x17]]      // 5 is rewritten to 0
```

Consequences: the fit array's slot positions are load-bearing (compacting it fits the wrong weapon to the wrong hardpoint), a slot no hardpoint addresses contributes nothing, and the same `player.mec` entry produces a different panel on two different HERCs. Weapon id 0 and id 27 (`MINE`, which has no case in the factory's switch) build no mount; the array keeps the hole.

**Every mount block starts zeroed.** The factory opens by pushing a 200000-byte arena that `Arena_Push` (`00474ab0`) has just `calloc`'d, and bump-allocates every mount out of it through `Arena_Alloc` (`0047a1bc`), which does no zeroing of its own but never needs to. Its fallback for an absent or full arena, `Mem_NewArray`, zeroes anyway. A field no constructor writes therefore reads zero — the Targeting Pod's cached damage is one ([`equipment-pods.md`](equipment-pods.md#where-a-pod-reads-its-damage)).

### The hardpoint list

The record layout is in [`../formats/gun-layout-gl.md`](../formats/gun-layout-gl.md). The loadout path reads three fields: `+0x07`, the fire-chain number, is **the cockpit weapon row this mount owns**; `+0x16` is the link partner offset; `+0x17` is the fit slot. The rest of the record is placement data this system does not touch.

Row order and mount order are different orderings of the same list. SAMSON.GL, against the retail `player.mec`, yields rows `EMP, ATC35, ATC35, ARH, EMP, ATC35, SHIELD POD, ATC35` from a fit array reading `ATC35, SHIELD, ATC35, ATC35, ATC35, EMP, EMP, MSL10`.

### The second loadout array is the ammunition type

Both mission formats carry it beside the weapon ids — `player.mec` ([`../shell/campaign-loop.md`](../shell/campaign-loop.md#launching-a-mission--game_exportmissionhandoff-0040f0d4)) and `script.dat` block 7 ([`../formats/script-dat.md`](../formats/script-dat.md)) — and it is what a missile launcher is loaded with, not a round count.

Retail data puts a filler `5` in every non-launcher slot. Verified against the retail mission: every slot whose weapon id is `MSL10` reads 1 here and every other slot reads 5.

## Mount classes

The factory's switch on the weapon id picks one of five classes, four of them live. Nothing else decides what a mount is:

| Class | Ctor | Weapon ids | Carries | Gauge |
|---|---|---|---|---|
| Ammunition | `WeaponMount_CtorAmmunition` | 1–5, 13–16, 21 | rounds | numeric (`CockpitView_CreateAmmoWeaponGauge` (`00432124`) → `AmmoWeaponGauge_Ctor` (`00440f78`)) |
| Energy | `WeaponMount_CtorEnergy` | 7–12, 17, 19, 20, 23–25, 28 | a capacitor | LED bar (`CockpitView_CreateEnergyWeaponGauge` (`00432074`) → `EnergyWeaponGauge_Ctor` (`00440a68`)) |
| ELF | `WeaponMount_CtorEnergy`, then vtable `ElfMountVtable` | 6, 22 | a capacitor | LED bar, as Energy |
| Grenade | `WeaponMount_CtorAmmunition`, then vtable `GrenadeMountVtable` | 26 (`LAEW`) | rounds — none, for `LAEW` | numeric, as Ammunition |
| Pod | `EcmPod_Ctor`/`TargetingPod_Ctor`/`ShieldPod_Ctor`/`TurboPod_Ctor`/`EnergyPod_Ctor` | 18, 29–32 | nothing, bar the Turbo Pod's charge | name only (`CockpitView_CreatePodGauge` → one of three `PodGauge` classes) |

The ELF and Grenade cases are the two that are not just a constructor call: the factory runs the energy or ammunition constructor and then **overwrites the object's vtable pointer** — with `ElfMountVtable` (`004992c0`) or `GrenadeMountVtable` (`00499248`). An ELF therefore shares every field with an energy mount and differs only in the destructor and the four slots that table replaces — see [ELF and ELF2](#elf-and-elf2).

The Grenade class is `LAEW`'s, the cut Locust Launcher ([`../cut-content.md`](../cut-content.md)). Over the ammunition class it replaces the trigger with a hold-to-throw (`GrenadeMount_TriggerHeld`, `0040e71c`: a press starts a charge at `+0x81` of 20000 that grows 1000 a call, and the release, or passing 100000, asks for the shot), the fire dispatch with one that spends a round and calls an empty `Grenade_FireNoOp` (`GrenadeMount_FireDispatch`, `0040ea04`), and the gauge refresh with a forwarder to the ammunition one. `LAEW`'s template carries a magazine of 0 ([`../formats/weapons-dat-sim.md`](../formats/weapons-dat-sim.md)), so `WeaponMount_AmmoCanFire` never passes and none of it runs.

Five pod classes hang off `Pod_CtorBase` (`0040e234`) and are laid out differently past `+0x77`. Only the ECM and Turbo pods override a tick, and only they have a button on their cockpit row; the Shield, Targeting and Energy pods inherit the base's `Pod_TickBase`, and all but the Turbo Pod inherit the base's free pool turn. The Targeting Pod's one override is its condition notification — see [`equipment-pods.md`](equipment-pods.md#what-each-class-actually-overrides) and [the button split](equipment-pods.md#only-two-pods-have-a-button). The table below is the two weapon-carrying classes only.

Shared mount fields mean different things per class:

| Offset | Ammunition | Energy |
|---|---|---|
| `+0x10`, `+0x14` | the weapon model and its private cell-frame array — see [The muzzle flash](#the-muzzle-flash) | as ammunition |
| `+0x44` | muzzle flash playing | as ammunition |
| `+0x7b` | rounds remaining | charge target, **and** the pool-arbitration priority |
| `+0x7d` | rounds in 256ths | capacitor level |
| `+0x7f` | — | charge rate, a flat 20 |
| `+0x31` | refire countdown | refire countdown |
| `+0x33`, `+0x3b` | fired-recently blocks | fired-recently blocks (the ELF sustain reads `+0x33`); byte 1, `+0x34`/`+0x3c`, is the power-level hand-off ([`weapon-firing.md`](weapon-firing.md#the-charge-bar)) |
| `+0x43` | — | mid-charge flag |
| `+0x47`, `+0x48`, `+0x84` | — | ELF spin-up running / latched / cell timer |
| `+0x49` | destroyed | destroyed |
| `+0x4b` | LINK engaged | LINK engaged |

### Ammunition

`WeaponMount_CtorAmmunition` (`0040e140`) reads the magazine size from the template's field at `+0x3a` and powers the mount up holding a full one: `+0x7b = size`, `+0x7d = size << 8`. The gauge prints `+0x7d >> 8`, and `+0x7d` lags the real count: `WeaponMount_PushAmmoGaugeState` (`0040f330`) decays it toward `+0x7b * 256` at 250 per 125 ms, which is what makes the cockpit counter roll rather than jump. The fire dispatch spends `+0x7b` — see [`weapon-firing.md`](weapon-firing.md#the-ammunition-dispatch).

The retail magazine sizes are listed with the field in [`../formats/weapons-dat-sim.md`](../formats/weapons-dat-sim.md).

### Energy

`WeaponMount_CtorEnergy` (`0040e074`) writes `Q10Multiply(820, 1200) = 960` into **both** `+0x7b` and `+0x7d` and `20` into `+0x7f` — literals, identical for every energy weapon. A HERC powers up with its capacitors full.

`+0x7b` is a *request*, not a capacity: the mount's **power level**, a control the manual never mentions. `WeaponMount_AdjustPowerLevel` (`0040f48c`) is what the pilot moves it with, and on the player's own mounts it then passes through the charge bar once a frame — see [`weapon-firing.md`](weapon-firing.md#power-level--weaponmount_adjustpowerlevel-0040f48c). The charge bar's denominator is the fixed 1200, so a mount at its spawn charge fills 960/1200 = four-fifths of its bar and only a mount turned up ever fills it.

Readiness (`WeaponMount_EnergyCanFire`) is `!destroyed && refireTimer == 0 && charge >= threshold`, where the threshold comes from the template's `+0x36`/`+0x38` pair: `max(+0x36, +0x7b)` when `+0x36 < +0x38`, otherwise `+0x38` outright. Real templates carry both shapes — `EMP` reads (350, 10000), `LAS100` (80, 80). The ammunition equivalent (`WeaponMount_AmmoCanFire`) is `!destroyed && refireTimer == 0 && rounds != 0`.

## ELF and ELF2

`ElfMountVtable` (`004992c0`) replaces four of the energy class's slots besides the destructor (`ElfMount_Dtor`, `004119c3`, at `+0x18`); from `+0x38` on the two tables are word for word the same. The charge, power-level, wake, priority and gauge slots are **not** among them, so an ELF carries and charges an ordinary capacitor and prints an ordinary bar.

| Slot | Energy | ELF |
|---|---|---|
| `+0x28` fire | `WeaponMount_FireDispatch_GunBeam` | `ElfMount_FireDispatch` |
| `+0x2c` ready | `WeaponMount_EnergyCanFire` | `ElfMount_CanFire` |
| `+0x30` trigger | `WeaponMount_TriggerHeld` | `ElfMount_TriggerHeld` |
| `+0x34` pool turn | `WeaponMount_ChargeCapacitor` | `ElfMount_SpinUpAndChargeTick` |

**`ElfMount_CanFire` is why an ELF cannot be re-triggered until its capacitor is full.** It reads the same two template fields as the energy test and drops the branch between them, so the threshold is always `max(+0x36, +0x7b)`:

```
!destroyed && ( charge >= max(template+0x36, chargeTarget)
                || (mount+0x33 && charge >= template+0x38) )
```

Both ELFs read `+0x36` = 400 against a charge target of 960, so a fresh trigger pull needs the full capacitor. The second clause is the sustain: `+0x33` means the mount fired on the previous tick, and while it is set the bar drops to one shot's 70, so the weapon empties itself over successive ticks and can only start again once it has climbed all the way back. There is no refire-timer term — which is consistent with both ELFs carrying a `+0x4c` of zero.

`+0x33` and `+0x3b` are two 8-byte blocks. `WeaponMount_PrepareShot` sets byte 0 of both on firing; `WeaponMount_RefireTick` **ands** `+0x33` with `+0x3b` (`WeaponMount_AndFlagBlocks`, `0040f881`) and then clears `+0x3b`. So `+0x33` survives only while the mount fires on every tick.

**`ElfMount_FireDispatch`** subtracts `template+0x38` unconditionally rather than capping it at the charge — the last partial shot takes the capacitor slightly negative, which is what ends the burst — and passes `Bullet_FireBurst` a **fixed 1200** as the shot power instead of the charge spent. Every shot of a burst therefore lands at full strength however far the capacitor has drained, which is what makes the ELF the damage outlier the manual describes from small `PROJ.DAT` figures.

**`ElfMount_TriggerHeld` is a spin-up.** A press sets `+0x47` and returns 0 — no shot that tick. `ElfMount_SpinUpAndChargeTick` then advances the muzzle-flash flipbook one cell per tick until the last, at which point it latches `+0x48` and clears `+0x47`; from then on the slot returns the trigger byte and the weapon fires every tick until release, which clears `+0x48` and rewinds the flipbook to cell zero. **ELF2 skips it**: the slot opens by forcing both flags to 1 whenever `template+0x56` (the record's self-index) is 22.

**The flipbook is what sets the delay's length**, so it is data, not a constant: the spin-up runs for as many ticks as the weapon model has cells. Both ELFs carry `MECHWPNS.DTS` shape 4, seven cells, so both take seven ticks — about 0.28 s at 25 Hz.

The spin-up only runs while the mount is **ready**: `WeaponMounts_FireTrigger` tests vtable `+0x2c` before it reaches `+0x30` at all. So an ELF whose capacitor is still filling does not spin up, and one that empties mid-burst keeps its latch until the trigger is released after it has recharged.

## The muzzle flash

**It is the weapon's own model playing its cell animation.** Nothing spawns an effect for it, and there is no muzzle-flash resource of any kind — `dts\FIRE.DTS` is the burning-object effect, not this (see [`destruction-effects.md`](destruction-effects.md#fire)).

Every hardpoint whose mounting code is visible (`.GL +6 < 4`) gets its own copy of the weapon model when the mount is built:

1. `WeaponMount_CtorBase` (`0040df30`) calls `WeaponMount_ShapeForMountingCode` (`0040fab0`), which instantiates `MECHWPNS.DTS`'s shape `template[0x22 + code * 2]` from the raw chunk `WeaponModels_LoadShapeChunks` (`0040f998`) cached, and binds it the `wpntex` atlas at `shape+0x26`.
2. `ShapeInstance_CtorCellFrames` (`00402fc0`) stores the shape at `mount+0x10` and allocates `mount+0x14` as a **private copy** of its per-sequence frame array, `shape+0x24` entries long.
3. `Shape_TranslatePointLists` (`0040dd4c`) translates the shape's own point lists by `WeaponMount_MuzzleOffset` — the mount point, not the muzzle. That private copy is why every mount can carry the same weapon and still sit and flash independently.
4. `Mech_ConfigureLoadout` reads the hardpoint's bone from `WeaponMount_HardpointBoneId` (`0040e61c`, the `.GL` record's `+0`, the same bone the shot leaves from) and `Shape_StampTransformId` (`00417530`) stamps that transform id onto every part of the shape, so it rides the machine's own skeleton.

The animation is `WeaponMount_RefireTick`'s tail, and it is the whole of it:

```
if (mount+0x44) {
    cell = (cell + 1) % *shape+0x20      // cell is element 0 of the private array
    if (cell == 0) mount+0x44 = 0
}
```

`mount+0x44` is raised by both fire dispatches whenever the hardpoint is visible — the ammunition class on its `Bullet` branch only, since a rocket comes off a rail rather than out of a barrel and lights nothing. `*shape+0x20` is the shape's `SequenceList[0]`; retail weapons carry two to seven cells, so a flash lasts two to seven ticks. Cell zero is the gun at rest. Nothing restarts a flash already running, so a weapon firing every tick shows a cycling book rather than one stuck on its first cell.

**The ELF's dispatch does not raise `+0x44`.** Its flipbook is driven by the spin-up instead, which leaves it parked on the last cell for as long as the burst lasts.

## Losing a mount

Two independent paths take a hardpoint out, and both end at `WeaponMount_Destroy` (`0040f57c`), which is idempotent — its whole body sits under a test of the destroyed byte:

```c
mount+0x10 = 0;      // drop the weapon model: the gun stops being drawn on the chassis
mount+0x49 = 1;      // destroyed: charges nothing, fires nothing, cannot be armed, prints OFFLINE
```

A visibly-mounted hardpoint then throws its own gun as a debris object ([`destruction-effects.md`](destruction-effects.md#spawn-sites)). The path picks the piece's kind, not who is flying: the certain notification below passes 0 and throws a piece that bursts, the destruction roll passes 1 and throws one that just falls.

### The certain path — the condition notification

`Mech_ComponentDamageWrite` snapshots **every** mount's component reading before its write and hands both readings to every mount afterwards, through the mount's vtable `+0x68`. The snapshot has to cover all of them because the write cascades: a hit on a shoulder can move a mount several components away. The component a mount reads is `.GL +0x17` + 19 — see [the destruction roll](#the-chance-path--the-destruction-roll).

`WeaponMount_ConditionChangedBase` (`0040ee0c`), the base class' whole slot: a component reading 256 destroys the mount, with no roll.

`WeaponMount_ConditionChanged` (`0040ee90`), the ammunition and Grenade classes: the base first, then, only while the mount is not empty (`+0x7b != 0`) and the reading is past `0x80`:

| `PROJ.DAT` type | Effect per 25 points of damage past `0x80` |
|---|---|
| `Rocket` | Rolls `rand & 0x3ff < 300` — a shade under 30% — and the first success destroys the mount. One hit crossing several steps rolls several times |
| `Bullet` | Sets the refire scale `mount+0x63 = 0x400 - steps * 0x66` |
| `Beam` | Neither |

**A damaged gun fires faster, not slower.** `WeaponMount_PrepareShot` arms `Q10Multiply(mount+0x63, template+0x4c)`, so halving the scale halves the delay. It reads backwards for damage and it is what the original does; see [`KNOWN_ISSUES.md`](../../../KNOWN_ISSUES.md).

`WeaponMount_ConditionChangedEnergy` (`0040ee38`), the energy and ELF classes: the base first, then the charge rate `+0x7f` is reset to 20 and, with the reading past `0x80`, lowered by `Q10(20, steps * 100)` — 19, 17, 15, 13 and 11 over the five steps. **A damaged energy weapon recharges more slowly**, and nothing else about it changes: no cook-off roll and no refire scale, whatever its `PROJ.DAT` type. The Targeting Pod's own slot is in [`equipment-pods.md`](equipment-pods.md#where-a-pod-reads-its-damage); the other pods keep the base.

### The chance path — the destruction roll

Components **19-28** are the machine's weapon mounts. The component a mount occupies is its `.GL` record's `+0x17` plus 19, which is also how `Mech_ConfigureLoadout` registers each mount's collision and damage records — the weapon's own hit spheres and armour, from its template ([`../formats/collision-spheres.md`](../formats/collision-spheres.md#a-fitted-weapon-brings-its-own-spheres), [`../formats/dmg-damage-file.md`](../formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece)); `WeaponMounts_MountForHardpointSlot` (`00410670`) is the lookup back, matching on `.GL +0x17` rather than on a position in the mount array.

`Mech_ApplyDirectFireDamage` (`004188c8`) rolls once for a hit that moved a mount component into a new damage band:

```c
if (after != 0x100 && typeRec+0x56 != 0 && component > 0x12) {
    odds = (obj[+0x45][+0x12] == 0) ? 3 : 10;            // the mission group's side byte
    if ((rand & 0xfff) < odds * 0x29) {                  // 123/4096 (~3%) or 410/4096 (~10%)
        WeaponMount_Destroy(mountFor(component), mech, 1);
        mech+0x20e[component] = 0;                       // clear the active flag FIRST
        if (side == 1) queueSalvage(template+0x56, (0x100 - Q10(500, after)) * 100 >> 8);
        Component_ApplyDamageAndCascade(component, 10000);
    }
}
```

Four things a port has to keep:

- **The chassis gates it.** `typeRec+0x56` is record offset 84 (the record sits at `MECH_TYPE_DATA[i]+2`), and the PITBULL alone states zero — its mounts are immune to the roll, though not to the certain path. See [`mech-locomotion.md`](mech-locomotion.md#mech-type-record).
- **The odds depend on whose machine it is**: about 3% for the player's side, about 10% for the Cybrids.
- **The order of the three writes.** Clearing the active flag before the flat 10000 is what stops the component cascading, so losing a gun does not take the shoulder it hangs off with it. `Component_ApplyDamageAndCascade` does **not** test the active flag — the flag gates `Mech_ComponentDamageWrite` at its entry and `Component_DestroyAndCascade`, and neither of those is reached here.
- **The Cybrid branch queues salvage.** `Salvage_QueueWeapon` (`00426ac8`) appends the destroyed weapon's catalog id (`template+0x56`) to the mission's salvage list, with a condition taken from just under half the component's reading, so a gun knocked off a half-wrecked mount comes home in better shape than the mount reads. The list is what the player recovers after the mission ([`component-damage.md`](component-damage.md#what-a-wreck-is-worth--mech_salvagevalue-00418e60)).

## Names — `WeaponMount_GetDisplayName` (`0040e18c`)

**The simulator does not use the shell catalog's names.** `Weapons_LoadResourceTables` (`0040fc8c`) walks a 33-entry string-pointer array at `00498eb0` as it reads the template table and stores one pointer into each record's `+0x52`; that is what a gauge prints. The two spellings are tabulated per id in [`../formats/weapons-dat.md`](../formats/weapons-dat.md#the-weapon-id-space--three-spellings-per-weapon) alongside each weapon's full name.

The name is chosen off the **resolved projectile**, not the weapon id: when the mount's `PROJ.DAT` record is a `Rocket`, the gauge prints that record's own subtype from a four-entry table at `004989c8` — `SARH`, `ARH`, `ARM`, `EO` — so a launcher is named by what is loaded in it. This is why the retail player's `MSL10` hardpoint reads `ARH`. Ids 13–16's own names are bare round counts (`"6"`, `"8"`, `"10"`, `"24"`) precisely because a launcher never prints them.

The subtype index is unbounded in the original. `MISSL` (id 21) points straight at the `BMSL` record, subtype 4, and reads one past the four-entry table. `BMSL` is Bull armament and no player HERC can mount it.

A pod row is the one place the name is decorated. `PodGauge_Ctor` (`00441524`) seeds its 11-char buffer with a literal space, appends the mount name, then appends `STRINGS0.STR` group 3 (`" POD"`) into whatever room is left — `" SHIELD POD"` exactly fills it. The Heads-Down Display's weapon list (`HddDamageScreen_Update`, `00450c54`) takes the undecorated name, so the same pod reads `SHIELD` there.

## The manager — `mech+0x202`

`mech+0x202` points at a separately allocated manager with its own vtable. `Mech_ConfigureLoadout` (`004175dc`) allocates it on every spawn and equip change: 0x14 bytes and `MechLoadout_ConstructWeaponMounts` alone, which builds the base object (the mount array and its count), or 0x35 bytes and `WeaponMounts_CtorLocal` (`004104ec`) on top when `mech+0xa3` is set, which extends it for a locally-simulated machine with the selection and the fire groups. A remote machine gets the base class and never has either read.

| Offset | Field |
|---|---|
| `+0x04` | mount array (holes preserved) |
| `+0x08` | mount count |
| `+0x0a` | per-subtype missile-lock flags, written every tick by `Mech_PerTickSystemsUpdate` and read by the launcher fire path — see [`missile-lock.md`](missile-lock.md) |
| `+0x1c` | current fire group, 0–2 |
| `+0x1d` | armed mount index, `0xff` for none |
| `+0x1f`, `+0x25`, `+0x2b` | the three fire-group arrays, one `short` per mount |
| `+0x31` | range to the selected target — see [Readiness](#readiness--weaponmounts_mountisready-00410970) |
| `+0x14` | TRACK's latch — Automatic Turret Tracking, read by the input path's turret block and the per-frame cockpit update ([`torso-aim.md`](torso-aim.md#automatic-turret-tracking--t)) |
| `+0x18` | single-fire flag, below |

**Every non-pod mount starts in group I and groups II and III start empty.** The constructor writes `group == wanted` into all three arrays with `wanted` fixed at 0 for a weapon and −1 for a pod. The initial selection is the first non-pod mount in *mount* order.

`WeaponMounts_PerFrameUpdate` (`00410b40`), the per-frame pass, reads the console panel's three-field input block, applies the chain and LINK commands from it, and then pushes three flags to each row's gauge: armed, in the current group, and ready.

## Readiness — `WeaponMounts_MountIsReady` (`00410970`)

**One predicate answers two questions**: it is what lights a row's state box green rather than red, and it is what the firing chain skips a mount on. Three gates in order, plus the link recursion:

```
mount->vtable+0x2c                                       // the class's own CanFire
&& (manager+0x31 == 0 || WeaponMount_RangeAllows(mount, manager+0x31))
&& (subtype == 5 || subtype == 3 || manager[0x0a + subtype*2])
&& (followLink || !mount+0x4b || MountIsReady(partner, followLink: 1))
```

**The range gate is what makes a weapon that cannot reach the target red and skippable.** `WeaponMount_RangeAllows` (`0040e5f8`) is the weapon's engagement window, `template+0x2c < range < template+0x30`, both bounds exclusive. The upper bound is the weapon's range, 15000–75000 across the table; **the lower bound is zero for every one of the 33 retail templates**, so a minimum range exists in the format and never bites ([`../formats/weapons-dat-sim.md`](../formats/weapons-dat-sim.md)). It is not the player's alone: the AI's weapon choice and its ELF latch ask the same test, against the ground-plane range to the point being shot at ([`ai-weapons.md`](ai-weapons.md#the-fire-decision--ai_fireatpoint-0041f5a0)).

`manager+0x31` is the distance to the **selected target**, written by `Player_PerFrameCockpitUpdate` (`0041b130`) through `WeaponMounts_PerFrameUpdate`'s argument, and `Math_DistanceBetweenPoints` measures it from the machine to `target+0x26` each frame. **Zero when nothing is selected**, and the gate is skipped outright on zero — which is what stops every row going red on a machine with no target rather than the window's exclusive lower bound failing them all. The store happens *before* the chain advance, so both readers see the same frame's range.

The measurement's origin is the machine's own position, except while `DAT_0049ef5c` is set and this is the local player, when it is the watched object `DAT_004d2708`, the spectator camera ([`external-views.md`](external-views.md#the-spectator-flag--dat_0049ef5c)).

The third gate is **missile lock**, not ammunition: the mount's `vtable+0x60` subtype must have its flag up in `manager+0x0a`. Two subtypes are exempt — 5, which is "not a launcher", and 3, the electro-optical missile, which never latches a flag because the pilot flies it ([`missile-lock.md`](missile-lock.md)). Without that exemption an EO launcher's row could never go green.

**The armed weapon fires anyway.** `WeaponMounts_FireTrigger` asks the mount's own `vtable+0x2c` and `+0x30` and never consults this predicate, so range never stops a shot — it only decides which weapon the chain hands you and what colour the lamp is. See [`weapon-firing.md`](weapon-firing.md).

## Arming, chaining and linking

**Arming a weapon and putting it in a fire chain are different actions**, and the cockpit offers each of them two ways:

| Action | Key | Mouse | Reaches |
|---|---|---|---|
| Arm a row | `[1]`–`[0]` | left-click the row | `WeaponMounts_SelectByGauge` (`004106ac`) |
| Add/remove a row from the current chain | `[Alt]`+`[1]`–`[0]` | right-click the row | `WeaponMounts_ToggleChainMember` (`004110ac`) |
| Step the armed weapon | `[W]` / `[Alt]`+`[W]` | — | `WeaponMounts_StepSelection` (`0041074c`) |
| Link the armed weapon | `[L]` | the LINK button | `WeaponMounts_ToggleLink` (`00410f14`) |
| Next fire chain | `` [`] `` | the chain button | `WeaponMounts_SetChain` (`00410ae4`) |

The two routes converge rather than duplicating: `CockpitWidgets_HandleCommand` answers a number key by indexing the cockpit's own ten-gauge array at `CockpitViewInstance+0x70`, calling `WeaponMounts_SelectByGauge` on that gauge and then pressing its select gadget — the same gadget the mouse hits, with the left-button bit. So the key runs the arm twice over, which is harmless, and anything the gadget does beyond arming it gets as well.

The mouse's own split is not a modifier but the **button**: a row gadget's click handler (`EnergyWeaponGauge_OnChildClick` (`00440ef0`) for an energy row, `AmmoWeaponGauge_OnChildClick` (`004414b4`) for an ammunition one) branches on bit 1 of the value it is handed, and that value is the mouse-button word `0049db6c` — bit 0 left, bit 1 right. **A pod's row is the exception**: its handler takes no value at all, so both buttons toggle the pod and neither chains it ([`equipment-pods.md`](equipment-pods.md#only-two-pods-have-a-button)).

The command codes are [scancodes](cockpit-input.md#keyboard-commands-are-scancodes): `0x26` is `L` and `0x29` is `` ` ``, which is how `ConsoleButtons_HandleCommand` (`004421a0`) binds them to the console panel's LINK and chain children; `0x11`/`0x211` are `W`/`Alt+W`; `0x02`–`0x0b` and `0x202`–`0x20b` are the two number-key banks. `Sim_DispatchCommand` (`0045fdac`) is the dispatcher every code passes through.

### Arming — `WeaponMounts_SelectByGauge` (`004106ac`) and `WeaponMounts_SetSelection` (`00410708`)

`WeaponMounts_SelectByGauge` finds the mount whose own gauge pointer (`+0x77`) matches, requires `+0x4c`, calls `WeaponMounts_SetSelection` and sets the single-fire flag. **A pod fails this twice over**: its `+0x4c` is cleared by `Pod_CtorBase` (`0040e234`), and its gauge-match slot (`WeaponMount_MatchGaugeNever`, `0040df00`) returns 0 unconditionally.

`WeaponMounts_SetSelection` is the only writer of `+0x1d`, and it normalises a linked pair onto its first half — arming the right-hand weapon of a linked pair arms the left-hand one instead, since the second half's partner offset is negative. That is what keeps a pair's two rows agreeing.

**Single fire (`+0x18`).** Set by arming a weapon by hand, cleared by `[W]`/`[Alt]`+`[W]` and by firing. While it is set, the per-frame pass leaves the selection alone however unready the weapon is; while it is clear, `WeaponMounts_AdvanceToReady` (`00410a3c`) hands the selection to the next mount in the chain that is [ready](#readiness--weaponmounts_mountisready-00410970). That is the whole of the manual's "select a weapon to single-fire … once you fire, the current firing chain will resume". The gate is not in the advance itself but in `WeaponMounts_ChainReady` (`00410a04`), the wrapper it asks: with `+0x18` set that returns ready without testing anything, so the chain cannot step. The flag is cleared at the key handler, not inside `WeaponMounts_StepSelection` (`0041074c`), so the chain switch and the per-frame advance both step the selection without clearing it.

`WeaponMounts_StepSelection` steps to the next mount that is selectable, in the current chain, and either unlinked or the *first* half of a linked pair.

### Chaining — `WeaponMounts_ToggleChainMember` (`004110ac`)

Finds the mount by its `.GL` fire-chain byte rather than by gauge pointer, requires the template's range (int32 at `0x30`, [`weapons-dat-sim.md`](../formats/weapons-dat-sim.md)) to be positive, and **XORs** its bit in the current chain's array. It does not arm anything. The gate amounts to "is a weapon at all": every pod carries a range of zero.

### Linking — `WeaponMounts_ToggleLink` (`00410f14`)

Two conditions, both from the chassis: the armed mount's `.GL` record names a partner (`+0x16` non-zero), and that partner carries the **same weapon id**. Both halves' `+0x4b` flip together. The manual states the same rule from the other side — "any two identical weapons mounted symmetrically on the HERC (on opposite hard points)".

Linking is visible because `WeaponMounts_PerFrameUpdate` lights a linked mount's row when its *partner* is the armed one, so both rows of a pair draw armed together. [Readiness](#readiness--weaponmounts_mountisready-00410970) is joined too, so a pair is ready only when both halves are. A destroyed or empty half unlinks the pair and hands the selection to the survivor.

One LINK press runs the toggle **three** times: the button's own click handler (`ConsoleButtons_SetLinkLatch`, `0044202c`), the manager's next per-frame pass reading the button's latch byte, and that pass writing the byte back so the widget sees it change and calls the handler again. Three flips of one bit is one flip.

### Console buttons

`ConsoleButtons_OnChildClick` switches on the child index: 0 advances the chain group and wraps at 3, 1 toggles link, 2 toggles auto-track. `ConsoleButton_Paint` then takes each one's frame from a different field — CHAIN and LINK from the shared press byte `+0x1b`, so they light only while held, and TRACK from its own `+0x40` latch. **LINK never stays lit**; the link state lives on the mounts.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The ELF's cell timer at `+0x84` is a per-cell interval, so the spin-up's length is a rate rather than a cell count | `ElfMount_TriggerHeld` zeroes it on the press and `ElfMount_SpinUpAndChargeTick` zeroes it again after every advance, and `Math_CountdownTimerTick` clamps at zero, so it expires on every tick it is asked. Nothing in the retail build ever gives it a non-zero value |
