# DBSIM.EXE — the weapon mount classes: building, firing, charging and gauges

**ES1's HERC weapons fire, charge their capacitors, count their rounds and drive their cockpit rows as ES2's do ([mount classes](../../retail/simulation/weapon-mounts.md#mount-classes), [weapon firing](../../retail/simulation/weapon-firing.md)), from the same kinds of mount: a base, an energy class with the ELF derived from it, an ammunition class with the grenade launcher derived from it, and an ECM class of ES1's own in place of ES2's five pods.** What a player would see differs in these ways, the first and last as far as this file's code decides them ([Open](#open)):

- **Damage short of destroying a hardpoint lowers nothing in its mount.** ES1's mounts have no condition notification: a gun keeps the template's refire delay and an energy weapon its fixed charge rate, both of which damage changes in ES2 ([losing a mount](#losing-a-mount)).
- **A hardpoint whose component is shot away disappears without a thrown gun;** of the two callers `es2_xref.py` finds, the chance roll on a direct hit is the one that throws it ([`WeaponMount_Destroy`](#losing-a-mount)).
- **A missile launcher's row reads the missile type followed by the rack's own name**, `ARH` and `10` making `ARH10`, where ES2's reads the type alone.
- **The grenade launcher, `GREN`, has a magazine of 8 and launches its throw through `00007b44`**, where ES2's grenade class serves `LAEW`, which has no rounds, and calls an empty function.
- **The ECM mount posts no message when its button is switched**, where ES2's ECM pod posts `JAMMING ENGAGED` or `JAMMING DISABLED`.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The mount manager, the factory and the classes' destructors are in [`gunlist.cpp`](weapon-mounts.md), the next object file.

## Where the object file starts and ends

The object file runs from `0000a2e0` to `0000baef` and holds ES2's mount code from `Shape_TranslatePointLists` (`0040dd4c`) to `WeaponMount_StaticInitNoOp` (`0040f780`), in ES2's order ([object files](../formats/compiler.md#where-object-files-meet-in-the-code)):

- **Its start.** The code before it ends with a `RET 8` at `0000a2da` and three zero bytes, and `0000a2e0` is a lone copy of the 40-byte destructor that opens object files, `HeapPtr_DtorMountsCopy`, byte for byte `HeapPtr_DtorGunlistCopy` (`0000baf0`), as the files at `0002fe5c` and `00035270` open ([vtables](../formats/compiler.md#vtables)). The function after it, `Shape_TranslatePointLists` (`0000a308`, [below](#moving-the-model-to-its-mount-point--shape_translatepointlists-0000a308)), is followed by `0x90` padding, not zero fill, up to `WeaponMount_RenderWithDetailBias` (`0000a4cc`). `es1_boilerplate.py scan` reads a frameless stub at `0000a304`; it is the destructor's own `POP EBP; RET 8`.
- **The rest of the run comes after the base constructor**: `TSPartBase_DtorMountsCopy` (`0000a764`), the class tag `ConfigPart_GetClassTagMountsCopy` (`0000a78e`) and `ConfigPart_DtorMountsCopy` (`0000a798`), the destructor slots of this file's copies `00090eda` and `00090f0e` of `TSPartBase`'s and `ConfigPart`'s tables, which `WeaponMount_CtorBase` stores. The file's copies of `gunlist.cpp`'s destructors and small slot functions come after the run (`0000a7d8`–`0000a843`), after the energy constructor (`0000a8c4`–`0000a94c`) and after the ammunition constructor (`0000aa3c`, `0000aa88`) ([the slots](#the-slots)).
- **Its end.** The last code is the file's static initializer, **`WeaponMount_StaticInitNoOp` (`0000baec`)**, a bare `RET` with no Ghidra function, entry `000adbec` of the static constructor table, the entry before `gunlist.cpp`'s `000adbf0`. Three zero bytes follow it, then [`gunlist.cpp`'s run](weapon-mounts.md#where-the-code-sits) at `0000baf0`. ES2's `WeaponMount_StaticInitNoOp` is a lone `RET` too.
- **Its data** follows `damage.cpp`'s source path (`00090de8`, pushed at `00009a51`): the two dispatch tables `Shape_TranslatePointLists` searches with `REPNE SCASD` (`00090e08` and `00090e60`), the base classes' table copies `00090e8a`, `00090eae`, `00090eda` and `00090f0e`, the five class tables from `00090f42`, `MissileAmmoNameTable` (`000910ac`) and the grenade's debug format at `0009112e`, before `gunlist.cpp`'s tables from `00091142`.

The code before `0000a2e0`, from `00009b8c`, is a file of its own, opened by the zero fill after `damage.cpp`'s code ([where the code sits](../formats/dmg-damage-file.md#where-the-code-sits)).

## The classes

| Class | Weapon ids | Constructor | Bytes | Tables |
|---|---|---|---|---|
| Base | — | `WeaponMount_CtorBase` (`0000a530`) | — | `WeaponMountBaseVtableMountsCopy` (`00090f42`) here, `WeaponMountBaseVtable` (`000914a2`) in `gunlist.cpp` |
| Energy | 4, 6–8, 12, 14, 15 | `WeaponMount_CtorEnergy` (`0000a84c`) | `0x7f` | `EnergyMountVtableMountsCopy` (`00090fb6`), `EnergyMountVtable` (`0009142e`) |
| ELF | 3 | `WeaponMount_CtorEnergy`, then the factory stores `ElfMountVtable` (`00091302`) | `0x82` | `ElfMountVtable` alone |
| Ammunition | 0–2, 9–11, 16 | `WeaponMount_CtorAmmunition` (`0000a9e4`) | `0x81` | `AmmunitionMountVtableMountsCopy` (`0009102a`), `AmmunitionMountVtable` (`00091516`) |
| Grenade | 5, `GREN` | `WeaponMount_CtorAmmunition`, then the factory stores `GrenadeMountVtable` (`00091376`) | `0x85` | `GrenadeMountVtable` alone |
| ECM | 13 | `EcmMount_Ctor` (`0000ab50`) | `0x7f` | `EcmMountVtable` (`000910be`) alone |

`MechLoadout_ConstructWeaponMounts` (`0000c31c`) picks the class by weapon id and allocates the bytes ([the factory](../formats/weapons-dat-sim.md#the-projectile-index)). A constructor stores this file's table; the factory stores `gunlist.cpp`'s over it for the ELF and the grenade alone, so energy, ammunition and ECM mounts keep this file's tables and run its copies. A class's two tables hold the same function in each slot, entered at the function or at its padding, except the slots holding the small functions each file has its own copies of: the class tag `+4`, the destructor `+0x1c`, the trigger `+0x34` and the stubs of [the slots](#the-slots). The destructors are `gunlist.cpp`'s, with their copies here ([the mount classes](weapon-mounts.md#the-mount-classes)); `EcmMount_Dtor` (`0000aba0`), the ECM's, has the code of `WeaponMount_DtorAmmunition` (`0000c7bc`) and stores `EcmMountVtable`, `WeaponMountBaseVtableMountsCopy`, `00090f0e` and `00090eda` in turn, as ES2's `EcmPod_Dtor` (`00411450`) tears down a pod.

The energy and ELF mounts are 4 bytes shorter than ES2's (`0x83` and `0x86`) because they have no charge rate: ES2's energy constructor stores 20 at `+0x7f`, and ES1's charge turn uses a literal instead ([charging](#charging)). Every field after it moves down 4: the ELF's cell timer is `+0x80`, ES2's `+0x84`. The ammunition and grenade mounts are ES2's sizes, with the grenade's throw distance at `+0x81`, as ES2's.

### The constructors

**`WeaponMount_CtorBase` (`0000a530`)**, (mount, projectile record, template, hardpoint record, machine), is ES2's ([the base constructor](../../retail/simulation/weapon-mounts.md#the-base-constructor--weaponmount_ctorbase-0040df30)) with ES1's calls and one field fewer:

1. With the hardpoint's mounting code (`.GL +6`) below 4 it opens the weapon model with `WeaponMount_ShapeForMountingCode` (`0000bd70`), and gives it its private cell frames at `+0x10`/`+0x14` with `ShapeInstance_CtorCellFrames`. It stores the table copies `00090e8a`, `00090eae`, `00090eda` and `00090f0e` in turn and then `WeaponMountBaseVtableMountsCopy`, where ES2's stores `ClassItem`'s, `TSBase`'s, `TSPartBase`'s and `CONFIG_PART`'s and then its base table.
2. It keeps the hardpoint record at `+0x18`, the template at `+0x1c` and the projectile record at `+0x20`.
3. `+0x5b` and `+0x5f` get `ShapeInst_AddThread` (`00001f48`, [threads](object-classes.md#animation-threads-and-root-motion))(machine, the hardpoint's dword `+2` or `+4`), or 0 when that record's `int16` is negative, where ES2's passes its shape instance to `ShapeInst_AddThread`.
4. With a model, it moves the model to the mount point: `Shape_TranslatePointLists`(`WeaponMount_MuzzleOffset`(mount), model) ([below](#moving-the-model-to-its-mount-point--shape_translatepointlists-0000a308)).
5. It zeroes ES2's fields (`+0x24`, `+0x2a`, `+0x31`, `+0x33`, `+0x3b`, 24 bytes from `+0x43`) and sets the selectable byte `+0x4c` to 1. It writes no refire scale: ES2's `+0x63` = `0x400` has no counterpart.

**`WeaponMount_CtorEnergy` (`0000a84c`)**, (mount, owner, projectile record, template, hardpoint record, machine), allocates `0x7f` bytes when given none, runs `WeaponMount_CtorBase` with all but its second argument, stores `EnergyMountVtableMountsCopy`, writes `Math_Q10Multiply`(1200, 820) = 960 to the charge target `+0x7b` and the capacitor `+0x7d`, sets the power-level hand-off bytes `+0x34` and `+0x3c` ([the charge bar](../../retail/simulation/weapon-firing.md#the-charge-bar)), and clears the gauge `+0x77`. ES2's (`0040e074`) also writes the charge rate `+0x7f`.

**`WeaponMount_CtorAmmunition` (`0000a9e4`)**, (mount, owner, projectile record, template, hardpoint record, machine), allocates `0x81` bytes when given none and is ES2's (`0040e140`, [ammunition](../../retail/simulation/weapon-mounts.md#ammunition)): the template's magazine `+0x3a` at `+0x7b`, the same shifted left 8 at `+0x7d`, the gauge cleared.

**`EcmMount_Ctor` (`0000ab50`)**, (mount, owner, template, hardpoint record, machine), allocates `0x7f` bytes when given none, runs `WeaponMount_CtorBase` with 0 for the projectile record, stores `EcmMountVtable`, and clears `+0x7d`, the gauge `+0x77` and the selectable byte `+0x4c`. ES2's `EcmPod_Ctor` (`0040e274`) runs `Pod_CtorBase`, which clears `+0x4c` too, and keeps the catalog id at `+0x77` and the gauge at `+0x79`.

None of the three derived constructors reads its second argument, the 1 or 0 `MechLoadout_ConstructWeaponMounts` passes on from its own ([building the manager](weapon-mounts.md#building-it--weaponmounts_ctorlocal-0000c808)); `WeaponMount_CtorBase` does not receive it.

### Moving the model to its mount point — `Shape_TranslatePointLists` (`0000a308`)

**A fitted weapon's model is moved to its hardpoint once, when the mount is built, and drawn moved from then on.** `Shape_TranslatePointLists`, (offset, part) in stack order, is ES2's ([the muzzle flash](../../retail/simulation/weapon-mounts.md#the-muzzle-flash), step 3) on the same classes, fields and tags: the model's groups add the offset to the points their polys use, and the part lists, shapes, cell-animation, detail and BSP parts pass it on to their children. Each mount's model is its own copy, loaded from the raw chunks by `WeaponMount_ShapeForMountingCode` ([the weapon models](weapon-mounts.md#the-weapon-models)), so the move lands in that copy alone. The offset is `WeaponMount_MuzzleOffset`'s sum, three `int16` added to a point's x, y and z.

Where ES1's differs in form, not in what it moves:

- it reads the part's class tag through slot `+4` and finds it among the eleven dwords of `Shape_TranslatePointListsTags` (`00090e08`) with `REPNE SCASD`, jumping through the eleven targets after them: `0x1e0005`, `0x1e0003` and `0x140005` return; `0x14000a` and `0x140014` move the group's points; `0x140006` calls the function again with the same arguments; `0x140015`, `0x140008`, `0x140007`, `0x14000b` and `0x14000c` call it for each child. A tag it does not list returns, so `0x1e0003` and `0x1e0005` return either way, as in ES2's;
- a group tests each poly's tag against the five dwords of `Shape_TranslatePointPolyTags` (`00090e60`: `0x140001`, `0x140002`, `0x140003`, `0x140009`, `0x14000f`) the same way, and keeps its marks at `Shape_TranslatePointMarks` (`000af178`, ES2's `004a98f0`);
- its case bodies, `0000a333`–`0000a4bf`, are jump targets inside the function that end at its one epilogue, `RET 8` at `0000a4c6`.

## The slots

A mount's slots stand 4 bytes above ES2's, as every ES1 table does ([vtables](../formats/compiler.md#vtables)). Slots `+4` to `+0x28` are `ConfigPart`'s and `TSPartBase`'s, with the class's destructor at `+0x1c` and **`WeaponMount_RenderWithDetailBias` (`0000a4cc`)** at `+0x20`: it is ES2's (`0040ded8`), setting the detail bias from `g_TSDetailBiasForMounts` (`000bc2b8`, [the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)) with `TSDetailPart_SetBias` (`000530a4`, a one-line setter of `g_TSDetailPartBias`, `0009615c`, as ES2's `004768ac`), running `ConfigPart_Render` (`00002718`) on the mount and setting the bias back to 0. From `+0x2c`, with the roles ES2's docs give them ([weapon firing](../../retail/simulation/weapon-firing.md), [the energy pool](../../retail/simulation/reactor-energy-pool.md#weapon-energy-arbitration--weaponmounts_arbitrateenergy-004107e4)):

| ES1 | ES2 | Role | Base | Energy | ELF | Ammunition | Grenade | ECM |
|---|---|---|---|---|---|---|---|---|
| `+0x2c` | `+0x28` | fire | `WeaponMount_PrepareShot` | `WeaponMount_FireDispatch_GunBeam` | `ElfMount_FireDispatch` | `WeaponMount_FireDispatch_Missile` | `GrenadeMount_FireDispatch` | `EcmMount_FireNoOp` |
| `+0x30` | `+0x2c` | ready | 0 | `WeaponMount_EnergyCanFire` | `ElfMount_CanFire` | `WeaponMount_AmmoCanFire` | as Ammunition | `EcmMount_CanFireNever` |
| `+0x34` | `+0x30` | trigger | `WeaponMount_TriggerHeld` | as Base | `ElfMount_TriggerHeld` | as Base | `GrenadeMount_TriggerHeld` | `EcmMount_TriggerHeldNever` |
| `+0x38` | `+0x34` | pool turn | `WeaponMount_RefireTick` | `WeaponMount_ChargeCapacitor` | `ElfMount_SpinUpAndChargeTick` | as Base | as Base | as Base |
| `+0x3c` | `+0x38` | power level | `WeaponMount_AdjustPowerLevelNoOp` | `WeaponMount_AdjustPowerLevel` | as Energy | as Base | as Base | as Base |
| `+0x40` | `+0x3c` | wake | `WeaponMount_WakeCapacitorNoOp` | `WeaponMount_WakeCapacitor` | as Energy | as Base | as Base | as Base |
| `+0x44` | `+0x40` | full charge | `WeaponMount_DemandFullChargeNoOp` | `WeaponMount_DemandFullCharge` | as Energy | as Base | as Base | as Base |
| `+0x48` | `+0x44` | zero the charge target | `WeaponMount_ZeroChargeTargetNoOp` | `WeaponMount_ZeroChargeTarget` | as Energy | as Base | as Base | as Base |
| `+0x4c` | `+0x48` | priority | `WeaponMount_GetEnergyPriorityZero` | `WeaponMount_GetEnergyPriority` | as Energy | as Base | as Base | as Base |
| `+0x50` | `+0x4c` | drain | `WeaponMount_DrainNoOp` | `WeaponMount_DrainCapacitor` | as Energy | `WeaponMount_DrainAmmo` | as Ammunition | as Base |
| `+0x54` | `+0x50` | gauge push | 0 | `WeaponMount_PushEnergyGaugeState` | as Energy | `WeaponMount_PushAmmoGaugeState` | `GrenadeMount_PushGaugeState` | `EcmMount_PushGaugeState` |
| `+0x58` | `+0x54` | counts in the combat rating | `WeaponMount_CountsInCombatRating_Always` | as Base | as Base | `WeaponMount_AmmoCountsInCombatRating` | as Ammunition | as Base |
| `+0x5c` | `+0x58` | gauge match | `WeaponMount_MatchGaugeNever` | `WeaponMount_EnergyMatchGauge` | as Energy | `WeaponMount_AmmoMatchGauge` | as Ammunition | as Base |
| `+0x60` | `+0x5c` | spent | `WeaponMount_IsSpent_Always` | `WeaponMount_EnergyIsSpent` | as Energy | `WeaponMount_AmmoIsSpent` | as Ammunition | as Base |
| `+0x64` | `+0x60` | ammunition subtype | `WeaponMount_GetEnergyAmmoType` | as Base | as Base | `WeaponMount_GetAmmoType` | as Ammunition | as Base |
| `+0x68` | `+0x64` | gauge factory | 0 | `WeaponMount_CreateEnergyGauge` | as Energy | `WeaponMount_CreateAmmoGauge` | as Ammunition | `EcmMount_CreateGauge` |

The ELF's table replaces the energy table's fire, ready, trigger and pool turn, and the grenade's replaces the ammunition table's fire, trigger and gauge push, the slots ES2's ELF and grenade tables replace ([ELF and ELF2](../../retail/simulation/weapon-mounts.md#elf-and-elf2)). Where ES2's base table holds `Rtl_PureVirtualCalled`, at ready, gauge push and gauge factory, ES1's holds 0. Every table ends with the gauge factory: each holds 0 at `+0x6c`, where ES2's `+0x68` holds the condition notification ([losing a mount](#losing-a-mount)).

The small slot functions and the ones ES1 has as ES2's, operands and the 4-byte shift aside:

- **`WeaponMount_MatchGaugeNever` (`0000a4f0`)**, frameless `XOR EAX,EAX; RET 8`, is ES2's (`0040df00`); **`WeaponMount_EnergyMatchGauge` (`0000a4f8`)** and **`WeaponMount_AmmoMatchGauge` (`0000a514`)**, (mount, gauge), return gauge = `+0x77`, as ES2's (`0040df08`, `0040df1c`).
- **`WeaponMount_GetEnergyAmmoType` (`0000ac54`)**, (mount, out), writes 0 through out when it is not null and returns 5; **`WeaponMount_GetAmmoType` (`0000ac70`)**, (mount, out), returns the projectile record's subtype `+2` when its type `+0` is 0, a rocket, writing the rounds `+0x7b` through out, and otherwise does what `WeaponMount_GetEnergyAmmoType` does. ES2's are `0040e62c` and `0040e644`.
- **`WeaponMount_EnergyIsSpent` (`0000b300`)** returns the destroyed byte `+0x49`; **`WeaponMount_AmmoIsSpent` (`0000b314`)** returns 1 when the rounds `+0x7b` are 0 or the mount is destroyed; **`WeaponMount_AmmoCountsInCombatRating` (`0000b8ec`)** returns whether the rounds are at least the magazine `+0x3a` shifted right 3. ES2's are `0040ed34`, `0040ed48` and `0040f520`.
- **`WeaponMount_EnergyCanFire` (`0000b278`)**, **`WeaponMount_AmmoCanFire` (`0000b340`)** and **`ElfMount_CanFire` (`0000b384`)**, (mount), are ES2's readiness tests (`0040ecdc`, `0040ed6c`, `0040eda0`; [energy](../../retail/simulation/weapon-mounts.md#energy), [ELF and ELF2](../../retail/simulation/weapon-mounts.md#elf-and-elf2)).
- **`WeaponMount_AdjustPowerLevel` (`0000b820`)**, (mount, up), **`WeaponMount_WakeCapacitor` (`0000b884`)**, **`WeaponMount_DemandFullCharge` (`0000b8a4`)** and **`WeaponMount_GetEnergyPriority` (`0000b8c4`)**, (mount), are ES2's (`0040f48c`, `0040f4d8`, `0040f4f0`, `0040f504`; [power level](../../retail/simulation/weapon-firing.md#power-level--weaponmount_adjustpowerlevel-0040f48c)): ±80 to the charge target, clamped to 0..1200, with `+0x34` and `+0x3c` set; 820 unless destroyed; the mid-charge byte `+0x43` and 1200; and the charge target, or 10000 while mid-charge.
- **`WeaponMount_HardpointBoneId` (`0000ac40`)** returns the hardpoint record's `int16` `+0`, as ES2's (`0040e61c`); `es2_xref.py` finds one call, from `Mech_ConfigureLoadout` (`00012c00`, [construction](mech.md#construction)).
- **`WeaponMount_MuzzleOffset` (`0000b918`)** is ES2's (`0040f540`): `WeaponMountTemplate_SideMuzzleOffset` (`0000bbd4`) plus the hardpoint's mount-point offset `+0x10`–`+0x14`, returned as a pointer to the sum.
- The ECM's stubs are frameless: **`EcmMount_FireNoOp` (`0000af3c`)** removes three arguments, **`EcmMount_TriggerHeldNever` (`0000acab`)** returns 0 and removes two, **`EcmMount_CanFireNever` (`0000b270`)** returns 0 and removes one. ES2's pods hold `Pod_FireNoOp` (`0040e95c`), `Pod_TriggerHeldNever` (`0040e678`) and `Pod_CanFireNever` (`0040ecd4`).

## Firing

`WeaponMounts_FireTrigger` calls the armed mount's fire slot with (mount, free-shot flag, machine) ([firing](weapon-mounts.md#firing--weaponmounts_firetrigger-0000d2bc)).

**`WeaponMount_PrepareShot` (`0000add4`)**, (mount, free-shot flag, machine), the base class's fire slot and the prologue every dispatch runs, is ES2's ([where the shot comes from](../../retail/simulation/weapon-firing.md#where-the-shot-comes-from--weaponmount_prepareshot-0040e788)) with these differences, and like ES2's never reads the flag:

- It resolves the hardpoint's bone (`.GL +0`) through slot `+0x24` of the shape instance under the machine's `+0x2e` (ES2's slot `+0x20` under `+0x34`), and with no node it uses a null frame, where ES2's substitutes the transform at `006c572c`.
- The machine's transform is `Context_GetTransform`(machine + 6), where ES2's builds it from the machine's angles when its `+0x32` flag is clear. It has no spectator swap.
- The convergence rotation goes through `000647f4`, where ES2's calls `Transform_ConcatRotation`.
- The refire countdown `+0x31` is the template's `+0x4c` itself, where ES2's scales it by the mount's `+0x63`.

Its results go where ES2's go: the shot transform at `WeaponMount_ShotTransform` (`000af144`, its translation at `000af158`), the muzzle's world point at `WeaponMount_ShotMuzzlePoint` (`000af164`) and the firing angles at `WeaponMount_ShotEuler` (`000af170`); it sets byte 0 of `+0x33` and `+0x3b`.

The dispatches call ES1's shot functions where ES2's call theirs, with ES2's arguments in stack order; the last is the projectile record's subtype `+2` ([Open](#open)):

| ES1 | ES2 |
|---|---|
| `000088cc`(power, machine, range, shot transform, subtype) | `Bullet_FireBurst` |
| `000081d4`(charge, machine, angles, muzzle point, subtype) | `Bullet_FirePowered` |
| `00008134`(machine, angles, muzzle point, subtype) | `Bullet_Fire` |
| `00007748`(0, machine, angles, muzzle point, subtype) | `Rocket_Fire`, given the machine's slot `+0x38` answer where ES1's passes 0 |
| `00007b44`(throw distance `+0x81`, machine, angles, muzzle point, subtype) | `Grenade_FireNoOp` (`0040acac`), a bare `RET` |

- **`WeaponMount_FireDispatch_GunBeam` (`0000b024`)** is ES2's (`0040ea58`, [the beam branch](../../retail/simulation/weapon-firing.md#the-beam-branch), [the gun branches](../../retail/simulation/weapon-firing.md#the-gun-branches)) without `EMP2`'s second volley: after the charge-up shots, three when the template's barrels `+0x3c` are 3, it always empties the capacitor, and it never touches `+0x4d`.
- **`ElfMount_FireDispatch` (`0000b218`)** is ES2's (`0040ec64`): the cost `+0x38` taken off the capacitor unconditionally and the burst fired at 1200.
- **`WeaponMount_FireDispatch_Missile` (`0000af40`)** is ES2's (`0040e964`, [the ammunition dispatch](../../retail/simulation/weapon-firing.md#the-ammunition-dispatch)): it spends `+0x38` rounds unless the shot is free, clearing the rounds and the selectable byte at 0 or below, then fires a rocket for a record of type 0 and otherwise a bullet, raising the muzzle flash `+0x44` on a visible hardpoint.
- **`GrenadeMount_FireDispatch` (`0000afd4`)** spends one round unless the shot is free, clears the charging byte `+0x46` and passes the throw distance `+0x81` to `00007b44`. ES2's (`0040ea04`) does the same and then calls `Grenade_FireNoOp`.

### Triggers

**`ElfMount_TriggerHeld` (`0000acb4`)**, (mount, input), is ES2's spin-up (`0040e680`, [ELF and ELF2](../../retail/simulation/weapon-mounts.md#elf-and-elf2)) on the input's byte `+0x15` (ES2's `+0x0d`), with the cell timer at `+0x80`. It has no ELF2 branch, and it rewinds the flipbook on release without ES2's test for a model.

**`GrenadeMount_TriggerHeld` (`0000ad54`)**, (mount, input), is ES2's hold-to-throw (`0040e71c`) on the input's byte `+0x15`: a press sets `+0x46` and the throw distance `+0x81` to 20000 and returns 0; held, the distance grows 1000 a call and the slot returns 0 until it passes 100000; a release, or passing 100000, returns 1, and while still held it also calls `0004d8c8`(1, 1), where ES2's calls `Input_LatchButton`(1, 1). `GREN` carries a magazine of 8 ([retail values](../formats/weapons-dat-sim.md#retail-values)), so `WeaponMount_AmmoCanFire` passes and the hold-to-throw runs.

## Charging

**`WeaponMount_RefireTick` (`0000b408`)**, (mount, mid-charge seen, budget), the base and ammunition pool turn, is ES2's (`0040ef94`): unless destroyed, `WeaponMount_AndFlagBlocks` (`0000b4a8`, entered at its padding `0000b4a5`; ES2's `0040f881`) ANDs `+0x33` with `+0x3b`, `+0x3b` is cleared, `Math_CountdownTimerTick` counts `+0x31` down, and a playing muzzle flash steps one cell; it returns the budget.

**`WeaponMount_ChargeCapacitor` (`0000b4d4`)**, (mount, mid-charge seen, budget), the energy pool turn, is ES2's (`0040f00c`, [weapon energy arbitration](../../retail/simulation/reactor-energy-pool.md#weapon-energy-arbitration--weaponmounts_arbitrateenergy-004107e4)) with a fixed rate: it draws min(rate, budget, deficit), the rate 20, or 60 while the mount's mid-charge byte `+0x43` is set, where ES2's draws its charge rate `+0x7f`. It has no `PLAS` rule, ES1 having no `PLAS`. A deficit at or below 0 clears `+0x43` and gives back up to 5, as ES2's.

**`ElfMount_SpinUpAndChargeTick` (`0000b748`)** is ES2's (`0040f3d8`) with the cell timer at `+0x80`, and without ES2's test for a model or an empty sequence list before it reads the flipbook.

## Gauges

The gauge factories build the row and keep it at `+0x77` ([the weapon rows](cockpit-panel.md#the-weapon-rows)):

- **`WeaponMount_CreateEnergyGauge` (`0000a97c`)** is ES2's (`0040e0e0`, [the charge bar](../../retail/simulation/weapon-firing.md#the-charge-bar)): `CockpitView_CreateEnergyWeaponGauge`(`CockpitViewInstance`, mount, name, row) and one set-state with the raw charge target in the block's dword 1. **`WeaponMount_CreateAmmoGauge` (`0000ab20`)** is ES2's (`0040e20c`) through `CockpitView_CreateAmmoWeaponGauge`. Both pass the name from `WeaponMount_GetDisplayName`, which ES2's gauge reads itself; the row is the hardpoint's byte `+7`.
- **`EcmMount_CreateGauge` (`0000abec`)** passes (`CockpitViewInstance`, mount, row) to `CockpitView_CreatePodGauge`, as ES2's `EcmPod_CreateGauge` (`0040e3bc`) does, keeping the gauge at `+0x77` (ES2's `+0x79`).

The gauge pushes are ES2's, through the gauge's set-state slot:

- **`WeaponMount_PushEnergyGaugeState` (`0000b5b8`)** and **`WeaponMount_PushAmmoGaugeState` (`0000b67c`)**, (mount, armed, in the chain, ready), are ES2's (`0040f288`, `0040f330`; [the charge bar](../../retail/simulation/weapon-firing.md#the-charge-bar), [ammunition](../../retail/simulation/weapon-mounts.md#ammunition)).
- **`GrenadeMount_PushGaugeState` (`0000b714`)** runs `WeaponMount_PushAmmoGaugeState` with its arguments, then `Mono_Printf`(`0x2a`, `0x17`, `"grenade dist: %ld"`, `+0x81`), the throw distance on the monochrome debug screen. ES2's (`0040f3b8`) only forwards.
- **`EcmMount_PushGaugeState` (`0000b588`)**, (mount, armed, in the chain, ready), reads only the mount: it copies the low byte of the first word of the block `00037b50`(gauge) returns, the pod row's button ([keys](cockpit-panel.md#keys-and-the-view-device)), into `+0x7b` and `+0x7d`. ES2's `EcmPod_Tick` (`0040f184`, [the two ticks](../../retail/simulation/equipment-pods.md#what-the-two-ticks-do-with-the-button)) posts `JAMMING ENGAGED` or `JAMMING DISABLED` when the button changes and writes the block back with the destroyed byte; ES1's posts nothing and writes nothing back.

**`WeaponMount_GetDisplayName` (`0000aa9c`)**, (mount), is ES2's ([names](../../retail/simulation/weapon-mounts.md#names--weaponmount_getdisplayname-0040e18c)) into `WeaponMount_DisplayNameBuffer` (`000af1dc`): the template's name `+0x52`, or for a rocket record (type 0) the subtype's from `MissileAmmoNameTable` (`000910ac`: `SARH`, `ARH`, `ARM`, `EO`), to which ES1's appends the template's name with `strcat`. A launcher's template names are its rack sizes, `6`, `8` and `10` ([the names](../formats/weapons-dat-sim.md#the-load)), so a rack of ten `ARH` reads `ARH10`. `es2_xref.py` finds three calls: the two gauge factories and `00042751`.

## The ECM

ES1's ECM is a mount of its own class, found by weapon id where ES2 files its ECM pod into `mech+0x307` ([`WeaponMounts_FindEcmMount`](weapon-mounts.md#lookups-over-the-mounts)). It cannot fire or be armed: its ready and trigger slots answer 0, its gauge match is `WeaponMount_MatchGaugeNever` and its constructor clears the selectable byte. Its row is a pod row whose button `EcmMount_PushGaugeState` copies into `+0x7b` and `+0x7d` each frame ES1's per-frame pass runs ([the per-frame pass](weapon-mounts.md#the-per-frame-pass--weaponmounts_perframeupdate-0000cf78)). Two readers take it from there, ES2's pair on ES1's offsets ([ECM](../../retail/simulation/missile-lock.md#ecm)):

- **`Mech_IsEcmSwitchedOn` (`00014e68`)**, (machine), is ES2's (`0041aa10`): 0 without an ECM mount; with one, the mount's `+0x7b` for a machine whose byte `+0x9d` is set (ES2's piloted byte `+0xa3`), and otherwise the machine's signed byte `+0x90` (ES2's radar mode `+0x96`).
- **`Mech_GetEcmPodSwitch` (`00014eb4`)**, (machine), is ES2's (`0041aa44`): the ECM mount's `+0x7d`, or 0. ES2's reads the pod's `+0x7f`.

`es2_xref.py` finds one call of each, from `Mech_PerTickSystemsUpdate` (`00014ee0`, [the systems tick](mech.md#the-systems-tick--mech_perticksystemsupdate-00014ee0)), which sets the target's `+0x9c` from the switch and the machine's jamming byte `+0x9b` from the reading.

## Losing a mount

**ES1's mounts have no condition notification, so damage short of destroying a hardpoint lowers nothing in the mount.** ES2's slot `+0x68` ([the certain path](../../retail/simulation/weapon-mounts.md#the-certain-path--the-condition-notification)) has no counterpart: ES1's tables end at the gauge factory, the refire countdown is the template's own figure and the charge rate a literal, so the mount has no field for damage to lower. Whether code outside the mounts weakens a damaged weapon is [Open](#open).

**`WeaponMount_Destroy` (`0000b968`)**, (mount, throw, machine), is ES2's (`0040f57c`, [losing a mount](../../retail/simulation/weapon-mounts.md#losing-a-mount)): with the mount not already destroyed, it clears the model `+0x10`, sets `+0x49`, and works out the mount point in the world from the hardpoint's bone. Only when `throw` is non-zero and the hardpoint is visible does it throw the gun: the root `template+0x26` of `WeaponModels_Mechwpn2Roots` (`000bc2c8`) through `00006168`, given the detail bias from `g_TSDetailBiasForMounts` at its `+0x4a`, which the piece's draw `Debris_Draw` (`000065c4`) hands to `TSDetailPart_SetBias` ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)), and launched through `00006378` with the third angle of a `Math_EulerToward` bearing from the machine to the mount point and the hardpoint's dword `+0x18`. ES2's always throws a visible gun and takes the flag as the debris' lifetimes. `es2_xref.py` finds two calls:

- `Mech_ComponentDamageWrite` (`00013250`, [the damage write](mech.md#the-damage-write--mech_componentdamagewrite-00013250)), after passing the damage table at the machine's `+0x19c` to `00009f7c`, walks the manager's mounts and destroys, with `throw` 0, each one not already destroyed whose component's `int16` (`.GL +0x17` + 19) in the array at the table's `+8` reads 0.
- `Mech_DirectFireHitTest` (`00013b44`, [the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)) runs ES2's destruction roll ([the chance path](../../retail/simulation/weapon-mounts.md#the-chance-path--the-destruction-roll)): for a component from 19 up that the hit did not leave reading `0x100`, with the `int16` `+0x56` of the record at the machine's `+0x188` not 0 (ES2's `typeRec+0x56`), `Math_RandomNext` & `0xfff` below 3 × `0x29`, or 10 × `0x29` when the byte `+0x14` of the machine's `+0x3f` is set, takes the mount `WeaponMounts_MountForHardpointSlot`(manager, component − 19) returns, destroys it with `throw` 1, and clears the component's `int16` in that array.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMountsCopy`, `Shape_TranslatePointLists`, `WeaponMount_RenderWithDetailBias` | `0000a2e0`, `0000a308`, `0000a4cc` | —, `0040dd4c`, `0040ded8` |
| `WeaponMount_MatchGaugeNever`, `WeaponMount_EnergyMatchGauge`, `WeaponMount_AmmoMatchGauge`, `WeaponMount_CtorBase` | `0000a4f0`, `0000a4f8`, `0000a514`, `0000a530` | `0040df00`, `0040df08`, `0040df1c`, `0040df30` |
| `WeaponMount_CtorEnergy`, `WeaponMount_CreateEnergyGauge`, `WeaponMount_CtorAmmunition`, `WeaponMount_GetDisplayName`, `WeaponMount_CreateAmmoGauge` | `0000a84c`, `0000a97c`, `0000a9e4`, `0000aa9c`, `0000ab20` | `0040e074`, `0040e0e0`, `0040e140`, `0040e18c`, `0040e20c` |
| `EcmMount_Ctor`, `EcmMount_Dtor`, `EcmMount_CreateGauge`, `WeaponMount_HardpointBoneId`, `WeaponMount_GetEnergyAmmoType`, `WeaponMount_GetAmmoType` | `0000ab50`, `0000aba0`, `0000abec`, `0000ac40`, `0000ac54`, `0000ac70` | `0040e274`, `00411450`, `0040e3bc`, `0040e61c`, `0040e62c`, `0040e644` |
| `EcmMount_TriggerHeldNever`, `ElfMount_TriggerHeld`, `GrenadeMount_TriggerHeld`, `WeaponMount_PrepareShot`, `EcmMount_FireNoOp`, `WeaponMount_FireDispatch_Missile` | `0000acab`, `0000acb4`, `0000ad54`, `0000add4`, `0000af3c`, `0000af40` | `0040e678`, `0040e680`, `0040e71c`, `0040e788`, `0040e95c`, `0040e964` |
| `GrenadeMount_FireDispatch`, `WeaponMount_FireDispatch_GunBeam`, `ElfMount_FireDispatch`, `EcmMount_CanFireNever`, `WeaponMount_EnergyCanFire` | `0000afd4`, `0000b024`, `0000b218`, `0000b270`, `0000b278` | `0040ea04`, `0040ea58`, `0040ec64`, `0040ecd4`, `0040ecdc` |
| `WeaponMount_EnergyIsSpent`, `WeaponMount_AmmoIsSpent`, `WeaponMount_AmmoCanFire`, `ElfMount_CanFire`, `WeaponMount_RefireTick`, `WeaponMount_AndFlagBlocks` | `0000b300`, `0000b314`, `0000b340`, `0000b384`, `0000b408`, `0000b4a8` | `0040ed34`, `0040ed48`, `0040ed6c`, `0040eda0`, `0040ef94`, `0040f881` |
| `WeaponMount_ChargeCapacitor`, `EcmMount_PushGaugeState`, `WeaponMount_PushEnergyGaugeState`, `WeaponMount_PushAmmoGaugeState`, `GrenadeMount_PushGaugeState`, `ElfMount_SpinUpAndChargeTick` | `0000b4d4`, `0000b588`, `0000b5b8`, `0000b67c`, `0000b714`, `0000b748` | `0040f00c`, `0040f184`, `0040f288`, `0040f330`, `0040f3b8`, `0040f3d8` |
| `WeaponMount_AdjustPowerLevel`, `WeaponMount_WakeCapacitor`, `WeaponMount_DemandFullCharge`, `WeaponMount_GetEnergyPriority`, `WeaponMount_AmmoCountsInCombatRating` | `0000b820`, `0000b884`, `0000b8a4`, `0000b8c4`, `0000b8ec` | `0040f48c`, `0040f4d8`, `0040f4f0`, `0040f504`, `0040f520` |
| `WeaponMount_MuzzleOffset`, `WeaponMount_Destroy`, `WeaponMount_StaticInitNoOp` | `0000b918`, `0000b968`, `0000baec` | `0040f540`, `0040f57c`, `0040f780` |
| Module copies: `TSPartBase_DtorMountsCopy`, `ConfigPart_GetClassTagMountsCopy`, `ConfigPart_DtorMountsCopy`, and the `*MountsCopy` copies of `gunlist.cpp`'s destructors and slot functions | `0000a764`, `0000a78e`, `0000a798`; `0000a7d8`–`0000a843`, `0000a8c4`–`0000a94c`, `0000aa3c`, `0000aa88` | — |
| Callees: `ConfigPart_Render`, `Mech_IsEcmSwitchedOn`, `Mech_GetEcmPodSwitch`, `TSDetailPart_SetBias` | `00002718`, `00014e68`, `00014eb4`, `000530a4` | `0040316c`, `0041aa10`, `0041aa44`, `004768ac` |

`ConfigPart_Render` (`00002718`) is ES2's (`0040316c`) on ES1's globals: with a model at `+0x10`, it makes the model's `+0x26` the current bank (`000bdb5e`) and the part's frames `+0x14` the current cell frames (`000bd912`) around `00052e18`(model), between `00052890` and `0005298c`(part, frame), and restores both. ES2's calls `TSPartList_Render` there and ends with `Render_NoOp`.

## Globals

| | Address |
|---|---|
| `WeaponMountBaseVtableMountsCopy`, `EnergyMountVtableMountsCopy`, `AmmunitionMountVtableMountsCopy`, `EcmMountVtable` | `00090f42`, `00090fb6`, `0009102a`, `000910be` |
| `MissileAmmoNameTable`, four name pointers | `000910ac` |
| `WeaponMount_ShotTransform`, `WeaponMount_ShotMuzzlePoint`, `WeaponMount_ShotEuler` | `000af144`, `000af164`, `000af170` |
| `WeaponMount_DisplayNameBuffer` | `000af1dc` |
| `Shape_TranslatePointListsTags`, `Shape_TranslatePointPolyTags`, `Shape_TranslatePointMarks` | `00090e08`, `00090e60`, `000af178` |

## Open

- **Open:** what the dwords at `0009fb40` and on are, where `es2_xref.py` finds the values `0000a304` (inside `HeapPtr_DtorMountsCopy`) and `0000a514` stored: a run of code addresses from `0000a064` to `0000a524`, mostly 16 bytes apart and not in address order, that point inside functions rather than at their entries.
- **Open:** ES1's shot functions `00007748`, `00007b44`, `00008134`, `000081d4` and `000088cc`, called where ES2's call `Rocket_Fire`, `Grenade_FireNoOp`, `Bullet_Fire`, `Bullet_FirePowered` and `Bullet_FireBurst`; with them, what `GREN`'s throw does with the record its projectile index 22 points past the table's end at ([the projectile index](../formats/weapons-dat-sim.md#the-projectile-index)), and what name `WeaponMount_GetDisplayName` makes from it.
- **Open:** the callees `000647f4` (the convergence product), `00006168` and `00006378` (the thrown gun), `0004d8c8` (the grenade's button latch) and `00037b50` (the pod row's block).
- **Open:** what sets the mid-charge byte `+0x43`, under which a capacitor charges at 60 a tick, besides `WeaponMount_DemandFullCharge`, the full-charge slot `WeaponMounts_DemandFullChargeOnArmed` calls, to which `es2_xref.py` finds no reference ([arming, chaining and linking](weapon-mounts.md#arming-chaining-and-linking)).
- **Open:** whether code outside the mount classes and the HERC's file weakens a damaged weapon: the HERC's damage write destroys a mount only when its component's flag clears, and its systems tick reads no mount's condition ([the HERC](mech.md)); and whether anything besides the ECM mount posts a jamming message when its button is switched.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: a missile launcher's cockpit row reads the missile type and the rack size run together (`ARH10` for a rack of ten `ARH`); `BP <CS>:0000b968` fires when a hardpoint is lost, with the second argument 1 when a gun is thrown off and 0 when the hardpoint vanishes. Two retail checks settle the Open item above: an energy weapon on a hardpoint damaged into the red charges as fast as on an undamaged one, and switching the ECM on brings up no `JAMMING` message.
