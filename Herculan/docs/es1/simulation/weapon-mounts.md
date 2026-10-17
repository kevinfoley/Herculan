# DBSIM.EXE — `gunlist.cpp`: weapon models, hardpoint lists, mount classes and the mount manager

**ES1's HERC handles its weapons as ES2's does ([the manager](../../retail/simulation/weapon-mounts.md#the-manager--mech0x202)): one weapon is armed at a time, three fire chains say which weapons the armed one steps through, two identical weapons on opposite hardpoints can be linked to fire together, and the reactor's energy charges one weapon at a time.** Each fitted weapon carries its own copy of its model, built from a table of raw shapes that exists only while the mission's HERCs are built, and the kinds of mount behind the weapons are ES2's, with an ECM class of its own in place of ES2's five equipment pods. What a player would see differs in three ways. A linked pair's two rows each light ready on their own weapon's state, where ES2's light only when both halves are ready. Alt with a row's number reaches rows 1 to 9, ES1's nine rows. The ECM, weapon id 13, is the one mount that starts outside chain I, where ES2 keeps its five equipment pods out. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the code sits

`gunlist.cpp` is the code from `0000baf0` to `0000d68b`, the object file that holds `Weapons_LoadResourceTables` (`0000bf48`), whose asserts name it ([the load](../formats/weapons-dat-sim.md#the-load)). It opens, after a `RET` at `0000baec` and three zero bytes, with its run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)): `HeapPtr_DtorGunlistCopy` (`0000baf0`), `ConfigPart_DtorGunlistCopy` (`0000bb18`), the frameless class tag `ConfigPart_GetClassTagGunlistCopy` (`0000bb57`) and `TSPartBase_DtorGunlistCopy` (`0000bb60`), the slots of the file's copies `00091142` and `00091176` of `ConfigPart`'s and `TSPartBase`'s tables. Between there and `0000d68b`, `es1_boilerplate.py scan` finds no other copy of the 40-byte destructor, and a scan for a return byte followed by zero fill to a 4-byte boundary finds one hit, at `0000c044` inside an instruction of `Weapons_LoadResourceTables`. The last code is the file's static initializer, **`Weapons_RegisterSubsystem` (`0000d67c`)**, entry `000adbf0` of the static constructor table, frameless and with no Ghidra function: it passes `Weapons_LoadResourceTables` and 2 to `RegisterSubsystemLoader` (`00000ac4`), as ES2's `Weapons_RegisterSubsystem` (`00411110`) does. Its `RET` at `0000d688` is followed by three zero bytes and `HeapPtr_DtorSimObjectCopy` (`0000d68c`), which opens [the next file](sim-object-base.md#where-the-object-file-starts-and-ends) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)).

In between, in address order: the template reader `WeaponMountTemplate_ReadRecord` (`0000bb8c`) and [the weapon models](#the-weapon-models) (`0000bbd4`–`0000bdd8`); two [lookups over a manager's mounts](#lookups-over-the-mounts) (`0000be04`, `0000be6c`); the loader with [the hardpoint lists](#the-hardpoint-lists) and the projectile lookups (`0000befc`–`0000c2d0`, [the load](../formats/weapons-dat-sim.md#the-load)); the factory `MechLoadout_ConstructWeaponMounts` (`0000c31c`, [the projectile index](../formats/weapons-dat-sim.md#the-projectile-index)); the slot functions of [the mount classes](#the-mount-classes) (`0000c5bc`–`0000c7bc`); and [the manager](#the-manager), `WeaponMounts_CtorLocal` (`0000c808`) to `WeaponMounts_ToggleChainMember` (`0000d5e4`), in the order of ES2's `004104ec` to `004110ac`.

## The weapon models

Each mount carries its own copy of its weapon's model, built from a raw shape chunk, as ES2's do ([the muzzle flash](../../retail/simulation/weapon-mounts.md#the-muzzle-flash)):

- **`WeaponModels_LoadShapeChunks` (`0000bc60`)** is ES2's (`0040f998`) on `dts\mechwpns`, or on `dts\mwpnsb` while `g_LowMemory` (`000bda98`) is set; ES2's has no low-memory file. It allocates with `operator_new`, asserts at `gunlist.cpp` line `0x7d`, and keeps the buffers at `WeaponModels_ShapeChunks` (`000bc2d2`), their lengths at `WeaponModels_ShapeChunkSizes` (`000bc372`) and the count, an `int32`, at `WeaponModels_ShapeChunkCount` (`000bc412`). The two arrays are `0xa0` bytes apart, room for 40 chunks, where ES2's are `0xf0` apart. `ClassItem_TagTotalSize` (`000555ec`) is ES2's (`0047a434`).
- **`WeaponModels_FreeShapeChunks` (`0000bd40`)** is ES2's (`0040fa88`). `DBSim_SpawnMissionObjects` (`0001e638`) builds the HERC records and the `player.mec` entries between the two calls, as ES2's does.
- **`WeaponMount_ShapeForMountingCode` (`0000bd70`)**, (template, mounting code), is ES2's (`0040fab0`): chunk `template[0x22 + code × 2]` opened as a `MemRWStream` on its stack, the shape loaded from it with `ClassItem_LoadAfterPeek`, and the `wpntex` bank `WeaponTexBank` (`000af1f0`), which `Weapons_LoadResourceTables` loads, stored at the shape's `+0x26`. `es2_xref.py` finds one call, from [`WeaponMount_CtorBase`](weapon-mount-classes.md#the-constructors) (`0000a530`). The stream's destructor is inlined with the file's copy `0009127e` of `MemRWStream`'s table, whose slot `+4` is `MemRWStream_DtorGunlistCopy` (`0000bdd8`), the code of `MemRWStream_Dtor` (`00064dc4`).
- **`WeaponMountTemplate_SideMuzzleOffset` (`0000bbd4`)**, (template, mounting code), is ES2's (`0040f904`, [the side offset](../../retail/simulation/weapon-firing.md#where-the-shot-comes-from--weaponmount_prepareshot-0040e788)), building its vector in `WeaponMountTemplate_SideMuzzleOffsetVec` (`000af1f8`). `es2_xref.py` finds one call, from `WeaponMount_MuzzleOffset` (`0000b918`), in [the object file before this one](weapon-mount-classes.md#the-slots).

## The hardpoint lists

ES1 keeps each chassis' `.GL` hardpoint list ([the layout](../../retail/formats/gun-layout-gl.md)) as ES2 does. **`GunLayout_AllocTable` (`0000c1b8`)**, ES2's (`0040fecc`), allocates `GunLayout_Count` (`000bc2c0`) 6-byte entries {`int16` count, record array} at `GunLayout_Table` (`000bc2bc`); `es2_xref.py` finds one call, from `DBSim_LoadScriptDat` (`0001d850`). `GunLayout_Reserve` (`0000c1a0`) hands out entries, `GunLayout_GetBySlot` (`0000c18c`) indexes the table, and `GunLayout_LoadForMechType` (`0000c1d0`) reads a file into an entry, building its 26-byte records with `__vec_new` and **`GunLayoutRecord_ElementCtor` (`0000c274`)**, entered at its padding `0000c271`, which returns the record after the empty constructor of its member at `+0x10`, the mount-point offset, as ES2's (`004117a5`) does. `GunLayout_CollectHardpointBones` (`0000befc`) is ES2's (`0040fc50`).

## The mount classes

**ES1's mounts are ES2's classes with an ECM class of its own in place of ES2's five pods**; the classes, their constructors and their slots are in [the object file before this one](weapon-mount-classes.md#the-classes). Each class but the ECM has a table in this file as well: `WeaponMountBaseVtable` (`000914a2`), `EnergyMountVtable` (`0009142e`), `AmmunitionMountVtable` (`00091516`), and `ElfMountVtable` (`00091302`) and `GrenadeMountVtable` (`00091376`), which the factory stores over the constructor's for the ELF and the grenade; the others' are stored by their destructors alone ([module copies](../formats/compiler.md#vtables)).

The destructors give the hierarchy. `ElfMount_Dtor` stores `00091302`, `0009142e`, `000914a2` and `ConfigPart`'s `00091142` in turn, `GrenadeMount_Dtor` stores `00091376`, `00091516`, `000914a2` and `00091142`, and the energy and ammunition destructors store their own table, `000914a2` and `00091142`. Each of those four returns at once for a null mount, then runs `HeapBlockOwner_Dtor` on `+0x10`, stores `TSPartBase`'s `00091176`, runs `TSBase_Dtor` and frees the mount on flags bit 0, as ES2's do (`004119c3`, `00411969`, `0041165a`, `004115a5`); `WeaponMount_DtorEnergy` is byte for byte `WeaponMount_DtorAmmunition`, operands aside. `WeaponMount_DtorBase` stores `000914a2` and `00091142` and makes the same two calls, but neither tests for a null mount nor frees it, and never reads its flags, where ES2's (`00411743`) frees on bit 0.

The slot functions in this file ([the slots](weapon-mount-classes.md#the-slots)):

- **`WeaponMount_TriggerHeld` (`0000c768`)**, (mount, input), returns the input record's byte `+0x15`, where ES2's (`0040f8ad`) reads `+0x0d` ([the trigger](../../retail/simulation/weapon-firing.md#the-trigger-is-polled-not-dispatched)). `WeaponMounts_FireTrigger` passes it its own third argument.
- **`WeaponMount_ZeroChargeTarget` (`0000c628`)** and **`WeaponMount_DrainCapacitor` (`0000c664`)**, (mount), are ES2's (`004116ad`, `004116cf`, [the charge bar](../../retail/simulation/weapon-firing.md#the-charge-bar)); **`WeaponMount_DrainAmmo` (`0000c708`)**, (mount), is ES2's (`004115f8`), which zeroes the rounds `+0x7b`.
- The stubs are frameless, `NOP`s before a `RET`, where ES2's are framed functions with the same results: `WeaponMount_AdjustPowerLevelNoOp` (`0000c6ed`) removes two arguments and returns; `WeaponMount_WakeCapacitorNoOp` (`0000c6f3`), `WeaponMount_DemandFullChargeNoOp` (`0000c6f7`), `WeaponMount_ZeroChargeTargetNoOp` (`0000c6fb`) and `WeaponMount_DrainNoOp` (`0000c7ad`) remove one; `WeaponMount_GetEnergyPriorityZero` (`0000c6ff`) returns 0; `WeaponMount_CountsInCombatRating_Always` (`0000c692`) and `WeaponMount_IsSpent_Always` (`0000c7b3`) return 1 in `AX`. ES2's are `004111ce`, `004111d3`, `004111d8`, `004111dd`, `0040f89f`, `004111e2`, `004111e9` and `0040f8a4`.

## The manager

The machine holds its manager at `+0x198`, where ES2's holds it at `+0x202`. `Mech_ConfigureLoadout` (`00012c00`, [construction](mech.md#construction)) builds it: with the machine's byte `+0x9d` set, the local manager through `WeaponMounts_CtorLocal`; otherwise the `0x14`-byte base through `MechLoadout_ConstructWeaponMounts` (`0000c31c`) alone, which stores the base table `WeaponMountsBaseVtable` (`000912f2`). ES2's tests `+0xa3`.

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00` | `+0x04` | the mount array, holes kept |
| `+0x04` | `+0x08` | the mount count, `int16` |
| `+0x06` | `+0x0a` | the five `int16` missile-lock flags, one per subtype |
| `+0x10` | `+0x00` | the vptr: `000912f2` for the base, `0009158a` for the local manager |
| `+0x14`–`+0x34` | the same | as ES2's: TRACK's latch `+0x14`, the chain-changed byte `+0x16`, single fire `+0x18`, the current chain `+0x1c`, the armed mount `+0x1d`, the three chains `+0x1f`/`+0x25`/`+0x2b` (each a 6-byte array of one `int16` per mount and its count), the range to the selected target `+0x31` |

The base is `0x14` bytes and the local manager `0x35`, as ES2's. The mount slots the manager calls are in [the mount classes](#the-mount-classes)' table.

Each table holds a zero dword and one slot, the energy turn. `000912f2`'s is the padding entry `0000c5bb` of **`WeaponMounts_ArbitrateEnergyNoSelection` (`0000c5bc`)**, (manager, budget), which returns `WeaponMounts_ArbitrateEnergy`(manager, `0xffff`, budget), as ES2's (`00411a1d`) does with −1. `0009158a`'s is the padding entry `0000c991` of **`WeaponMounts_ArbitrateEnergyWithSelection` (`0000c994`)**, (manager, budget), which passes (manager, the armed mount `+0x1d`, budget) to `WeaponMounts_ArbitrateEnergy`. ES2's tables hold them at `+0`, and ES2's pass the machine on as well.

### Lookups over the mounts

Two functions walk a manager's mount array, skipping holes:

- **`WeaponMounts_RoundsByMissileType` (`0000be6c`)**, (manager, rounds), is ES2's (`0040fbdc`, [missile lock](../../retail/simulation/missile-lock.md)) on slot `+0x64`: it zeroes the five `int16` at rounds and adds each mount's rounds to its subtype's entry unless the subtype is 5. `es2_xref.py` finds one call, from `Mech_PerTickSystemsUpdate` (`00014ee0`, [the systems tick](mech.md#the-systems-tick--mech_perticksystemsupdate-00014ee0)).
- **`WeaponMounts_FindEcmMount` (`0000be04`)**, (manager), returns the first mount whose template's (`+0x1c`) `int16` `+0x56` is 13, the ECM, or 0. `es2_xref.py` finds calls from `Mech_IsEcmSwitchedOn` (`00014e68`) and `Mech_GetEcmPodSwitch` (`00014eb4`) ([the ECM](weapon-mount-classes.md#the-ecm)). ES2's `MechLoadout_FileEquipmentPods` (`0040fb2c`) instead files its five pods into an array at `mech+0x307` once the mounts are built ([equipment pods](../../retail/simulation/equipment-pods.md)).

## Building it — `WeaponMounts_CtorLocal` (`0000c808`)

(manager, ammunition types, weapon ids, hardpoints, count, machine): allocates `0x35` bytes when given none, runs `MechLoadout_ConstructWeaponMounts`(manager, 1, ammunition types, weapon ids, hardpoints, count, machine), where `Mech_ConfigureLoadout` passes 0 in the second place for a base manager (the factory passes it on to the mount constructors, which never read it: [the constructors](weapon-mount-classes.md#the-constructors)), and then builds the three chains as ES2's does ([the manager](../../retail/simulation/weapon-mounts.md#the-manager--mech0x202)):

1. `__vec_ctor` runs **`ARRAY_short_Ctor` (`0000c96c`)**, by its padding entry `0000c96a`, over the three 6-byte arrays from `+0x1f`; it allocates 6 bytes when given none and clears the dword at `+0`.
2. It stores `0009158a` at `+0x10`, and gives each array the count and `operator_new`(count × 2).
3. It sets `+0x1d` to `0xff`, then walks the mounts: a mount whose template's (`+0x1c`) `int16` `+0x56` is 13, the ECM, is in no chain; every other mount is in chain I and not in II or III, and the first of them in mount order becomes the armed one. ES2's keeps its five pods, ids `0x12` and `0x1d`–`0x20`, out of the chains.
4. `WeaponMounts_SetChain`(manager, 0), and `+0x16` set to 1.

**`WeaponMounts_BuildGauges` (`0000c9b0`)**, (manager), calls each mount's gauge factory, slot `+0x68`, as ES2's (`00410644`) does with slot `+0x64`. `es2_xref.py` finds one call, from `Sim_StartMission` (`0005a9dc`).

## Arming, chaining and linking

These are ES2's ([arming, chaining and linking](../../retail/simulation/weapon-mounts.md#arming-chaining-and-linking)) on ES1's offsets and slots:

- **`WeaponMounts_SetSelection` (`0000cb00`)**, (manager, index): when the index differs from `+0x1d`, a linked mount (`+0x4b`) whose hardpoint (`+0x18`) partner offset `+0x16` is negative hands the selection to its partner; stores the index at `+0x1d` and clears `000b5bc8`.
- **`WeaponMounts_StepSelection` (`0000cb54`)**, (manager, step), steps `+0x1d` by ±1 with wrap until it reaches a mount that is selectable (`+0x4c`), unlinked or the first half of a pair, and in the current chain, or comes back to where it started, then sets it with `WeaponMounts_SetSelection`.
- **`WeaponMounts_SetChain` (`0000cf10`)**, (manager, chain): when the chain differs from `+0x1c`, stores it, steps the selection when the armed mount is not in the new chain, and passes the chain to `00037ce4` with the console panel at `CockpitViewInstance`'s `+0x1dd`, where ES2's calls `ConsoleButtons_SetChainNumber` with the panel `Cockpit_GetConsoleButtons` returns ([Open](#open)).
- **`WeaponMounts_XorChainBit` (`0000d180`)**, (manager, index), flips the mount's entry in the current chain.
- **`WeaponMounts_ToggleChainMember` (`0000d5e4`)**, (manager, row), reads only the row's low 16 bits: the first mount whose hardpoint's byte `+7` is the row and whose template's `int32` `+0x30` is above 0 has its chain entry flipped, and, when its priority slot `+0x4c` answers 0, its wake slot `+0x40` called; it returns 1, or 0 when no mount matches. A right click on a row reaches it ([the energy row](cockpit-weapon-gauges.md#the-energy-row)).
- **`WeaponMounts_ToggleLink` (`0000d41c`)**, (manager): with a mount armed, flips `+0x4b` on it and on the mount its partner offset names, when that offset is not 0, the partner exists and the two templates' `+0x56` agree.
- **`WeaponMounts_SetTrackLatch` (`0000d408`)**, (manager), stores 1 at `+0x14`.
- **`WeaponMounts_MountForHardpointSlot` (`0000ca04`)**, (manager, slot), returns the first mount whose hardpoint's byte `+0x17` is the slot, or 0. `es2_xref.py` finds one call, from `Mech_DirectFireHitTest` (`00013b44`, [the hit test](mech.md#the-hit-test--mech_directfirehittest-00013b44)).
- **`WeaponMounts_IdleAllCapacitors` (`0000d1c4`)**, (manager), calls each mount's wake slot `+0x40`.
- **`WeaponMounts_DemandFullChargeOnArmed` (`0000d22c`)**, (manager), calls the armed mount's full-charge slot `+0x44` and clears every other mount's `+0x43`. `es2_xref.py` finds no reference to it, nor to ES2's `WeaponMounts_DemandFullChargeOnArmed_Dead` (`00410d50`) ([Open](#open)).

**`WeaponMounts_HandleCommand` (`0000d4b4`)**, (manager, code), finds the code among the 17 dwords at `00091598` by `REPNE SCASD` and jumps through the 17 targets after them, whose code from `0000d4de` to `0000d5d0` Ghidra has as undefined bytes. It returns 1 for these codes and 0 for any other:

| Code | Action |
|---|---|
| `0x2c` | `WeaponMounts_IdleAllCapacitors` |
| `0x30` | the next chain, wrapping after 2, through `WeaponMounts_SetChain`, and `+0x16` set to 1 |
| `0x11`, `0x211` | `WeaponMounts_StepSelection` by +1 or −1, and single fire `+0x18` cleared |
| `0x0c`, `0x0d` | with a mount armed, its power-level slot `+0x3c` with whether the code was `0x0d` |
| `0x4a`, `0x4e` | the same, with whether the code was `0x4e` |
| `0x202`–`0x20a` | `WeaponMounts_ToggleChainMember` with the code less `0x202` |

ES2's (`00410f78`) handles the same codes with ten row codes, `0x202`–`0x20b`. `es2_xref.py` finds one call, from `Mech_HandleCommand` (`00011050`, [the cockpit](mech.md#the-cockpit)), which passes the manager of `PlayerMech` (`000bc42a`).

## Readiness

**`WeaponMounts_MountIsReady` (`0000cdb0`)**, (manager, mount), is ES2's ([readiness](../../retail/simulation/weapon-mounts.md#readiness--weaponmounts_mountisready-00410970)) without the link term: the mount's ready slot `+0x30`; then, when the range `+0x31` is not 0, `WeaponMount_RangeAllows`; then the subtype from slot `+0x64` must be 5 or 3 or have its lock flag set at `+0x06` + 2 × subtype. ES2's also asks the partner of a linked mount, so its pair is ready only when both halves are; ES1's answers for the one mount, and the per-frame pass lights each row of a pair by its own answer. **`WeaponMount_RangeAllows` (`0000ac14`)**, (mount, range), in [the object file before this one](weapon-mount-classes.md), is ES2's (`0040e5f8`): 1 when the template's (`+0x1c`) `int32` `+0x2c` < range < its `int32` `+0x30`.

**`WeaponMounts_ChainReady` (`0000ce1c`)**, (manager, mount), returns 1 without asking while single fire `+0x18` or `000b5bc6` is set, and otherwise `WeaponMounts_MountIsReady`, as ES2's does ([the chain hold](../../retail/simulation/weapon-mounts.md#arming--weaponmounts_selectbygauge-004106ac-and-weaponmounts_setselection-00410708)). **`WeaponMounts_AdvanceToReady` (`0000ce54`)**, (manager), is ES2's: with a mount armed that `WeaponMounts_ChainReady` refuses, it steps the selection by +1 until one passes, clearing single fire, and otherwise puts the original back.

## The per-frame pass — `WeaponMounts_PerFrameUpdate` (`0000cf78`)

(manager, range), ES2's (`00410b40`) in order:

1. With `000b5bc8` set and `000b5bc6` clear, it steps the selection by +1 and clears single fire.
2. It stores the range at `+0x31` and runs `WeaponMounts_AdvanceToReady`.
3. It reads the console panel's dword block through `00037ed4` (the panel at `CockpitViewInstance`'s `+0x1dd`, ES2's `+0x1e1`): the chain in the low `int16`, TRACK in byte 2, LINK in byte 3. With `+0x16` set it clears it and puts its own chain in the block; otherwise `WeaponMounts_SetChain` takes the block's. TRACK sets or clears `+0x14`, LINK runs `WeaponMounts_ToggleLink`; it clears byte 3 and writes the block back through `00037e78`.
4. For each mount: armed is the index against `+0x1d`. A linked mount that is neither destroyed (`+0x49`) nor unselectable (`+0x4c` clear) counts as armed when its partner is the armed one; one that is either has both halves' `+0x4b` flipped and hands the selection to its partner. It then calls the gauge push, slot `+0x54`, with (mount, ready, in the current chain, armed), ready from `WeaponMounts_MountIsReady`.

`es2_xref.py` finds one call, from `Player_PerFrameCockpitUpdate` (`000155a8`, [the cockpit](mech.md#the-cockpit)), which passes its machine's manager `+0x198` and the distance to its target.

## Energy — `WeaponMounts_ArbitrateEnergy` (`0000cc2c`)

(manager, selection, budget): ES2's ([weapon energy arbitration](../../retail/simulation/reactor-energy-pool.md#weapon-energy-arbitration--weaponmounts_arbitrateenergy-004107e4)) in its serving order and its mid-charge rule: the selected mount first when it is not negative, then the rest by priority slot `+0x4c`, each given the pool turn `+0x38`(mount, mid-charge seen, budget) and passing the remainder on. It differs from ES2's in two ways:

- It takes no machine and calls no mount's fire slot. ES2's calls a served mount's fire slot when `WeaponMount_AutoFireDue` (`0040ede8`) answers non-zero for it ([`EMP2`'s second volley](../../retail/simulation/weapon-firing.md#the-gun-branches)).
- Its refund needs only `000bc4a0` = 0 and `000bc4a6` = 1, script.dat header words 2 and 5 ([the header](../formats/script-dat.md#the-header)): the budget it was given goes back for every machine, where ES2's refunds the locally piloted machine alone.

`es2_xref.py` finds two calls, from `WeaponMounts_ArbitrateEnergyWithSelection` and `WeaponMounts_ArbitrateEnergyNoSelection`.

## Firing — `WeaponMounts_FireTrigger` (`0000d2bc`)

(manager, machine, trigger): ES2's ([the trigger](../../retail/simulation/weapon-firing.md#weaponmounts_firetrigger-in-order)) on ES1's slots: with a mount armed, the armed mount and, when linked, its partner must both pass ready `+0x30` and then the trigger slot `+0x34`(mount, trigger); then the armed mount's fire slot `+0x2c`(mount, free-shot flag, machine), single fire cleared when it is no longer ready, `000b5bc6` and `000b5bc8` set when its subtype (`+0x64`) is 3, and the partner fired the same way. The free-shot flag is 1 when `000bc4a0` is 0 and `000bc4a6` is 1. It returns nothing, where ES2's returns 1 when it fired. `es2_xref.py` finds one call, from `Sim_PollPlayerInput` (`0005a0c0`, [the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0)), which passes the player machine `000b5b86` and the input block while the camera is not driven, and two dwords holding its padding entry `0000d2bb`, at `0003d7fc` and `0003d8f0` ([Open](#open)).

`000b5bc6` and `000b5bc8` are ES2's `004d25aa` and `004d25ac`, the electro-optical launch's hold on the chain ([the chain hold](../../retail/simulation/weapon-mounts.md#arming--weaponmounts_selectbygauge-004106ac-and-weaponmounts_setselection-00410708)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorGunlistCopy`, `ConfigPart_DtorGunlistCopy`, `ConfigPart_GetClassTagGunlistCopy`, `TSPartBase_DtorGunlistCopy` | `0000baf0`, `0000bb18`, `0000bb57`, `0000bb60` | —, —, —, `0049224b` |
| `WeaponMountTemplate_ReadRecord`, `WeaponMountTemplate_SideMuzzleOffset`, `WeaponModels_LoadShapeChunks`, `WeaponModels_FreeShapeChunks`, `WeaponMount_ShapeForMountingCode`, `MemRWStream_DtorGunlistCopy` | `0000bb8c`, `0000bbd4`, `0000bc60`, `0000bd40`, `0000bd70`, `0000bdd8` | `0040f8bc`, `0040f904`, `0040f998`, `0040fa88`, `0040fab0`, — |
| `WeaponMounts_FindEcmMount`, `WeaponMounts_RoundsByMissileType`, `GunLayout_CollectHardpointBones`, `Weapons_LoadResourceTables`, `WeaponMountTemplate_Ctor` | `0000be04`, `0000be6c`, `0000befc`, `0000bf48`, `0000c0b4` | —, `0040fbdc`, `0040fc50`, `0040fc8c`, `0041178d` |
| `WeaponMountTemplate_GetByWeaponId`, `GunLayout_GetBySlot`, `GunLayout_Reserve`, `GunLayout_AllocTable`, `GunLayout_LoadForMechType`, `GunLayoutRecord_ElementCtor` | `0000c178`, `0000c18c`, `0000c1a0`, `0000c1b8`, `0000c1d0`, `0000c274` | `0040fe84`, `0040fea0`, `0040feb4`, `0040fecc`, `0040fee8`, `004117a5` |
| `Proj_LookupRecordByIndex`, `Proj_LookupRecord`, `MechLoadout_ConstructWeaponMounts`, `WeaponMounts_ArbitrateEnergyNoSelection` | `0000c2bc`, `0000c2d0`, `0000c31c`, `0000c5bc` | `0040ffb0`, `0040ffc8`, `0040fff8`, `00411a1d` |
| `ElfMount_Dtor`, `WeaponMount_ZeroChargeTarget`, `WeaponMount_DrainCapacitor`, `WeaponMount_CountsInCombatRating_Always`, `GrenadeMount_Dtor` | `0000c5d4`, `0000c628`, `0000c664`, `0000c692`, `0000c69c` | `004119c3`, `004116ad`, `004116cf`, `004111e9`, `00411969` |
| `WeaponMount_AdjustPowerLevelNoOp`, `WeaponMount_WakeCapacitorNoOp`, `WeaponMount_DemandFullChargeNoOp`, `WeaponMount_ZeroChargeTargetNoOp`, `WeaponMount_GetEnergyPriorityZero` | `0000c6ed`, `0000c6f3`, `0000c6f7`, `0000c6fb`, `0000c6ff` | `004111ce`, `004111d3`, `004111d8`, `004111dd`, `004111e2` |
| `WeaponMount_DrainAmmo`, `WeaponMount_DtorEnergy`, `WeaponMount_TriggerHeld`, `WeaponMount_DtorBase`, `WeaponMount_DrainNoOp`, `WeaponMount_IsSpent_Always`, `WeaponMount_DtorAmmunition` | `0000c708`, `0000c71c`, `0000c768`, `0000c778`, `0000c7ad`, `0000c7b3`, `0000c7bc` | `004115f8`, `0041165a`, `0040f8ad`, `00411743`, `0040f89f`, `0040f8a4`, `004115a5` |
| `WeaponMounts_CtorLocal`, `ARRAY_short_Ctor`, `WeaponMounts_ArbitrateEnergyWithSelection`, `WeaponMounts_BuildGauges`, `WeaponMounts_MountForHardpointSlot` | `0000c808`, `0000c96c`, `0000c994`, `0000c9b0`, `0000ca04` | `004104ec`, `00411892`, `0041194a`, `00410644`, `00410670` |
| `WeaponMounts_SelectByGauge`, `WeaponMounts_SetSelection`, `WeaponMounts_StepSelection`, `WeaponMounts_ArbitrateEnergy` | `0000ca70`, `0000cb00`, `0000cb54`, `0000cc2c` | `004106ac`, `00410708`, `0041074c`, `004107e4` |
| `WeaponMounts_MountIsReady`, `WeaponMounts_ChainReady`, `WeaponMounts_AdvanceToReady`, `WeaponMounts_SetChain`, `WeaponMounts_PerFrameUpdate` | `0000cdb0`, `0000ce1c`, `0000ce54`, `0000cf10`, `0000cf78` | `00410970`, `00410a04`, `00410a3c`, `00410ae4`, `00410b40` |
| `WeaponMounts_XorChainBit`, `WeaponMounts_IdleAllCapacitors`, `WeaponMounts_DemandFullChargeOnArmed`, `WeaponMounts_FireTrigger` | `0000d180`, `0000d1c4`, `0000d22c`, `0000d2bc` | `00410cd0`, `00410d04`, `00410d50`, `00410dbc` |
| `WeaponMounts_SetTrackLatch`, `WeaponMounts_ToggleLink`, `WeaponMounts_HandleCommand`, `WeaponMounts_ToggleChainMember`, `Weapons_RegisterSubsystem` | `0000d408`, `0000d41c`, `0000d4b4`, `0000d5e4`, `0000d67c` | `00410f04`, `00410f14`, `00410f78`, `004110ac`, `00411110` |
| `WeaponMount_RangeAllows`, in [the file before this one](weapon-mount-classes.md) | `0000ac14` | `0040e5f8` |
| Library callees: `ClassItem_TagTotalSize`, `MemRWStream_Dtor` | `000555ec`, `00064dc4` | `0047a434`, VSHELL `004536a1` |

## Globals

| | Address |
|---|---|
| `WeaponTexBank`, the `dba\wpntex` bank | `000af1f0` |
| `WeaponMountTemplate_SideMuzzleOffsetInit` and `WeaponMountTemplate_SideMuzzleOffsetVec`, the side offset's construction flag and vector | `000af1f4`, `000af1f8` |
| `GunLayout_Table` and `GunLayout_Count` | `000bc2bc`, `000bc2c0` |
| `WeaponMountTemplate_Table` and `WeaponMountTemplate_Count` | `000bc2c2`, `000bc2c6` |
| `WeaponModels_Mechwpn2Roots` and `WeaponModels_Mechwpn2RootCount`, the `dts\mechwpn2` roots the loader loads | `000bc2c8`, `000bc2cc` |
| `WeaponModels_ShapeChunks`, `WeaponModels_ShapeChunkSizes` and `WeaponModels_ShapeChunkCount` | `000bc2d2`, `000bc372`, `000bc412` |
| `WeaponMountsBaseVtable` and `WeaponMountsLocalVtable`, the managers' tables | `000912f2`, `0009158a` |
| `ElfMountVtable`, `GrenadeMountVtable`, `EnergyMountVtable`, `WeaponMountBaseVtable`, `AmmunitionMountVtable` | `00091302`, `00091376`, `0009142e`, `000914a2`, `00091516` |
| `WeaponMounts_CommandCodes`, 17 codes and 17 targets | `00091598` |
| `maybe_WeaponChain_EoHold` and `maybe_WeaponChain_EoStepPending` | `000b5bc6`, `000b5bc8` |
| `MissionModeFlag` and `UnlimitedAmmoFlag`, script.dat header words 2 and 5 | `000bc4a0`, `000bc4a6` |
| `PlayerMech` | `000bc42a` |

## Open

- **Open:** the console panel's functions `00037ce4`, `00037e78` and `00037ed4`, which the manager calls where ES2's calls `ConsoleButtons_SetChainNumber`, `ConsoleButtons_SetStateBlock` and `ConsoleButtons_GetStateBlock` ([the weapon rows' file](cockpit-weapon-gauges.md#open)).
- **Open:** what clears `000b5bc6`; ES2's `Sim_MainTick` clears `004d25aa` each tick ([the missile camera](../../retail/simulation/rockets.md#the-missile-camera)).
- **Open:** what reaches `WeaponMounts_DemandFullChargeOnArmed`, to which `es2_xref.py` finds no branch or stored pointer, and what the two dwords at `0003d7fc` and `0003d8f0` holding `WeaponMounts_FireTrigger`'s padding entry are.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in the cockpit, `BP <CS>:0000cf78` fires every frame; `W` moves the armed row to the next weapon in the chain, and Alt with a row's number takes that row out of the chain or puts it back (`BP <CS>:0000d5e4`). At mission load, `BP <CS>:0000bd70` fires once per visible hardpoint between one hit each on `0000bc60` and `0000bd40`.
