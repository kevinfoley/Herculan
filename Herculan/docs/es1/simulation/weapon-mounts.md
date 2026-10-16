# DBSIM.EXE — the weapon mount manager

**ES1's HERC handles its weapons as ES2's does ([the manager](../../retail/simulation/weapon-mounts.md#the-manager--mech0x202)): one weapon is armed at a time, three fire chains say which weapons the armed one steps through, two identical weapons on opposite hardpoints can be linked to fire together, and the reactor's energy charges one weapon at a time.** What a player would see differs in three ways. A linked pair's two rows each light ready on their own weapon's state, where ES2's light only when both halves are ready. Alt with a row's number reaches rows 1 to 9, ES1's nine rows. The ECM, weapon id 13, is the one mount that starts outside chain I, where ES2 keeps its five equipment pods out. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the code sits

The code from `0000c7bc` to `0000d68b` is the end of `gunlist.cpp`, the object file that holds `Weapons_LoadResourceTables` (`0000bf48`), whose asserts name it ([the load](../formats/weapons-dat-sim.md#the-load)). The file opens at `0000baf0` with its run of `ConfigPart`'s copies, after a `RET` at `0000baec` and three zero bytes ([module copies](../formats/compiler.md#vtables)). Between there and `0000d68b`, `es1_boilerplate.py scan` finds no other copy of the 40-byte destructor, and a scan for a return byte followed by zero fill to a 4-byte boundary finds one hit, at `0000c044` inside an instruction of `Weapons_LoadResourceTables`. The last code is the file's static initializer, **`Weapons_RegisterSubsystem` (`0000d67c`)**, entry `000adbf0` of the static constructor table, frameless and with no Ghidra function: it passes `Weapons_LoadResourceTables` and 2 to `RegisterSubsystemLoader` (`00000ac4`), as ES2's `Weapons_RegisterSubsystem` (`00411110`) does. Its `RET` at `0000d688` is followed by three zero bytes and `HeapPtr_DtorSimObjectCopy` (`0000d68c`), which opens [the next file](sim-object-base.md#where-the-object-file-starts-and-ends) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). This page covers the code from `0000c7bc` ([Open](#open)).

The page holds the mount manager in ES2's order, `WeaponMounts_CtorLocal` (`0000c808`) to `WeaponMounts_ToggleChainMember` (`0000d5e4`), against ES2's `004104ec` to `004110ac`, and two class functions besides: **`WeaponMount_DtorAmmunition` (`0000c7bc`)**, (mount, flags), slot `+0x1c` of the ammunition mounts' table `00091516`, which stores `00091516`, the mount base's `000914a2` and `ConfigPart`'s `00091142`, runs `HeapBlockOwner_Dtor` on `+0x10`, stores `00091176`, runs `TSBase_Dtor` and frees the mount on flags bit 0, as ES2's `004115a5` does; and **`WeaponMount_RangeAllows` (`0000ac14`)**, (mount, range), ES2's (`0040e5f8`): 1 when the template's (`+0x1c`) `int32` `+0x2c` < range < its `int32` `+0x30`.

## The manager

The machine holds its manager at `+0x198`, where ES2's holds it at `+0x202`. `00012c00`, the function ES2's `Mech_ConfigureLoadout` (`004175dc`) pairs with, builds it: with the machine's byte `+0x9d` set, the local manager through `WeaponMounts_CtorLocal`; otherwise the `0x14`-byte base through `MechLoadout_ConstructWeaponMounts` (`0000c31c`) alone, which stores the base table `000912f2`. ES2's tests `+0xa3`.

| ES1 | ES2 | Holds |
|---|---|---|
| `+0x00` | `+0x04` | the mount array, holes kept |
| `+0x04` | `+0x08` | the mount count, `int16` |
| `+0x06` | `+0x0a` | the five `int16` missile-lock flags, one per subtype |
| `+0x10` | `+0x00` | the vptr: `000912f2` for the base, `0009158a` for the local manager |
| `+0x14`–`+0x34` | the same | as ES2's: TRACK's latch `+0x14`, the chain-changed byte `+0x16`, single fire `+0x18`, the current chain `+0x1c`, the armed mount `+0x1d`, the three chains `+0x1f`/`+0x25`/`+0x2b` (each a 6-byte array of one `int16` per mount and its count), the range to the selected target `+0x31` |

The base is `0x14` bytes and the local manager `0x35`, as ES2's. A mount's slots stand 4 bytes above ES2's, as every ES1 table does ([vtables](../formats/compiler.md#vtables)): the fire slot is `+0x2c`, ready `+0x30`, trigger `+0x34`, the pool turn `+0x38`, the power level `+0x3c`, wake `+0x40`, full charge `+0x44`, priority `+0x4c`, the gauge push `+0x54`, the gauge match `+0x5c`, the ammunition subtype `+0x64` and the gauge factory `+0x68`.

`0009158a` holds a zero dword and one slot, the padding entry `0000c991` of **`WeaponMounts_ArbitrateEnergyWithSelection` (`0000c994`)**, (manager, budget), which passes (manager, the armed mount `+0x1d`, budget) to `WeaponMounts_ArbitrateEnergy`. ES2's table holds it at `+0`, and ES2's passes the machine on as well.

## Building it — `WeaponMounts_CtorLocal` (`0000c808`)

(manager, ammunition types, weapon ids, hardpoints, count, machine): allocates `0x35` bytes when given none, runs `MechLoadout_ConstructWeaponMounts`(manager, 1, ammunition types, weapon ids, hardpoints, count, machine), where `00012c00` passes 0 in the second place for a base manager, and then builds the three chains as ES2's does ([the manager](../../retail/simulation/weapon-mounts.md#the-manager--mech0x202)):

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
- **`WeaponMounts_MountForHardpointSlot` (`0000ca04`)**, (manager, slot), returns the first mount whose hardpoint's byte `+0x17` is the slot, or 0. `es2_xref.py` finds one call, from `00013b44`.
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

ES2's (`00410f78`) handles the same codes with ten row codes, `0x202`–`0x20b`. `es2_xref.py` finds one call, from `00011050`, which passes the manager of `PlayerMech` (`000bc42a`).

## Readiness

**`WeaponMounts_MountIsReady` (`0000cdb0`)**, (manager, mount), is ES2's ([readiness](../../retail/simulation/weapon-mounts.md#readiness--weaponmounts_mountisready-00410970)) without the link term: the mount's ready slot `+0x30`; then, when the range `+0x31` is not 0, `WeaponMount_RangeAllows`; then the subtype from slot `+0x64` must be 5 or 3 or have its lock flag set at `+0x06` + 2 × subtype. ES2's also asks the partner of a linked mount, so its pair is ready only when both halves are; ES1's answers for the one mount, and the per-frame pass lights each row of a pair by its own answer.

**`WeaponMounts_ChainReady` (`0000ce1c`)**, (manager, mount), returns 1 without asking while single fire `+0x18` or `000b5bc6` is set, and otherwise `WeaponMounts_MountIsReady`, as ES2's does ([the chain hold](../../retail/simulation/weapon-mounts.md#arming--weaponmounts_selectbygauge-004106ac-and-weaponmounts_setselection-00410708)). **`WeaponMounts_AdvanceToReady` (`0000ce54`)**, (manager), is ES2's: with a mount armed that `WeaponMounts_ChainReady` refuses, it steps the selection by +1 until one passes, clearing single fire, and otherwise puts the original back.

## The per-frame pass — `WeaponMounts_PerFrameUpdate` (`0000cf78`)

(manager, range), ES2's (`00410b40`) in order:

1. With `000b5bc8` set and `000b5bc6` clear, it steps the selection by +1 and clears single fire.
2. It stores the range at `+0x31` and runs `WeaponMounts_AdvanceToReady`.
3. It reads the console panel's dword block through `00037ed4` (the panel at `CockpitViewInstance`'s `+0x1dd`, ES2's `+0x1e1`): the chain in the low `int16`, TRACK in byte 2, LINK in byte 3. With `+0x16` set it clears it and puts its own chain in the block; otherwise `WeaponMounts_SetChain` takes the block's. TRACK sets or clears `+0x14`, LINK runs `WeaponMounts_ToggleLink`; it clears byte 3 and writes the block back through `00037e78`.
4. For each mount: armed is the index against `+0x1d`. A linked mount that is neither destroyed (`+0x49`) nor unselectable (`+0x4c` clear) counts as armed when its partner is the armed one; one that is either has both halves' `+0x4b` flipped and hands the selection to its partner. It then calls the gauge push, slot `+0x54`, with (mount, ready, in the current chain, armed), ready from `WeaponMounts_MountIsReady`.

`es2_xref.py` finds one call, from `000155a8`, which passes its machine's manager `+0x198`.

## Energy — `WeaponMounts_ArbitrateEnergy` (`0000cc2c`)

(manager, selection, budget): ES2's ([weapon energy arbitration](../../retail/simulation/reactor-energy-pool.md#weapon-energy-arbitration--weaponmounts_arbitrateenergy-004107e4)) in its serving order and its mid-charge rule: the selected mount first when it is not negative, then the rest by priority slot `+0x4c`, each given the pool turn `+0x38`(mount, mid-charge seen, budget) and passing the remainder on. It differs from ES2's in two ways:

- It takes no machine and calls no mount's fire slot. ES2's calls a served mount's fire slot when `WeaponMount_AutoFireDue` (`0040ede8`) answers non-zero for it ([`EMP2`'s second volley](../../retail/simulation/weapon-firing.md#the-gun-branches)).
- Its refund needs only `000bc4a0` = 0 and `000bc4a6` = 1, script.dat header words 2 and 5 ([the header](../formats/script-dat.md#the-header)): the budget it was given goes back for every machine, where ES2's refunds the locally piloted machine alone.

`es2_xref.py` finds two calls, from `WeaponMounts_ArbitrateEnergyWithSelection` and from `0000c5bc`, in the part of the file this page does not cover.

## Firing — `WeaponMounts_FireTrigger` (`0000d2bc`)

(manager, machine, trigger): ES2's ([the trigger](../../retail/simulation/weapon-firing.md#weaponmounts_firetrigger-in-order)) on ES1's slots: with a mount armed, the armed mount and, when linked, its partner must both pass ready `+0x30` and then the trigger slot `+0x34`(mount, trigger); then the armed mount's fire slot `+0x2c`(mount, free-shot flag, machine), single fire cleared when it is no longer ready, `000b5bc6` and `000b5bc8` set when its subtype (`+0x64`) is 3, and the partner fired the same way. The free-shot flag is 1 when `000bc4a0` is 0 and `000bc4a6` is 1. It returns nothing, where ES2's returns 1 when it fired. `es2_xref.py` finds one call, from `0005a0c0`, which passes the player machine `000b5b86`, and two dwords holding its padding entry `0000d2bb`, at `0003d7fc` and `0003d8f0` ([Open](#open)).

`000b5bc6` and `000b5bc8` are ES2's `004d25aa` and `004d25ac`, the electro-optical launch's hold on the chain ([the chain hold](../../retail/simulation/weapon-mounts.md#arming--weaponmounts_selectbygauge-004106ac-and-weaponmounts_setselection-00410708)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `WeaponMount_RangeAllows`, `WeaponMount_DtorAmmunition` | `0000ac14`, `0000c7bc` | `0040e5f8`, `004115a5` |
| `WeaponMounts_CtorLocal`, `ARRAY_short_Ctor`, `WeaponMounts_ArbitrateEnergyWithSelection`, `WeaponMounts_BuildGauges`, `WeaponMounts_MountForHardpointSlot` | `0000c808`, `0000c96c`, `0000c994`, `0000c9b0`, `0000ca04` | `004104ec`, `00411892`, `0041194a`, `00410644`, `00410670` |
| `WeaponMounts_SelectByGauge`, `WeaponMounts_SetSelection`, `WeaponMounts_StepSelection`, `WeaponMounts_ArbitrateEnergy` | `0000ca70`, `0000cb00`, `0000cb54`, `0000cc2c` | `004106ac`, `00410708`, `0041074c`, `004107e4` |
| `WeaponMounts_MountIsReady`, `WeaponMounts_ChainReady`, `WeaponMounts_AdvanceToReady`, `WeaponMounts_SetChain`, `WeaponMounts_PerFrameUpdate` | `0000cdb0`, `0000ce1c`, `0000ce54`, `0000cf10`, `0000cf78` | `00410970`, `00410a04`, `00410a3c`, `00410ae4`, `00410b40` |
| `WeaponMounts_XorChainBit`, `WeaponMounts_IdleAllCapacitors`, `WeaponMounts_DemandFullChargeOnArmed`, `WeaponMounts_FireTrigger` | `0000d180`, `0000d1c4`, `0000d22c`, `0000d2bc` | `00410cd0`, `00410d04`, `00410d50`, `00410dbc` |
| `WeaponMounts_SetTrackLatch`, `WeaponMounts_ToggleLink`, `WeaponMounts_HandleCommand`, `WeaponMounts_ToggleChainMember`, `Weapons_RegisterSubsystem` | `0000d408`, `0000d41c`, `0000d4b4`, `0000d5e4`, `0000d67c` | `00410f04`, `00410f14`, `00410f78`, `004110ac`, `00411110` |

## Globals

| | Address |
|---|---|
| `WeaponMountsLocalVtable`, the local manager's table | `0009158a` |
| `WeaponMounts_CommandCodes`, 17 codes and 17 targets | `00091598` |
| `maybe_WeaponChain_EoHold` and `maybe_WeaponChain_EoStepPending` | `000b5bc6`, `000b5bc8` |
| `MissionModeFlag` and `UnlimitedAmmoFlag`, script.dat header words 2 and 5 | `000bc4a0`, `000bc4a6` |
| `PlayerMech` | `000bc42a` |

## Open

- **Open:** `gunlist.cpp` from `0000baf0` to `0000c7bb`: the mount classes' constructors and slots, the loader and the hardpoint layouts, partly named from earlier batches. `es1_boilerplate.py scan` proposes the run at `0000baf0`, an `RLERStream` destructor copy at `0000bdd8` and the frameless slots `0000c692`, `0000c6ed`–`0000c6ff`, `0000c7ad` and `0000c7b3`.
- **Open:** the console panel's functions `00037ce4`, `00037e78` and `00037ed4`, which the manager calls where ES2's calls `ConsoleButtons_SetChainNumber`, `ConsoleButtons_SetStateBlock` and `ConsoleButtons_GetStateBlock` ([the weapon rows' file](cockpit-weapon-gauges.md#open)).
- **Open:** the second argument `MechLoadout_ConstructWeaponMounts` passes on to the mount constructors, 1 from `WeaponMounts_CtorLocal` and 0 from `00012c00`, which its description calls the owner.
- **Open:** what clears `000b5bc6`; ES2's `Sim_MainTick` clears `004d25aa` each tick ([the missile camera](../../retail/simulation/rockets.md#the-missile-camera)).
- **Open:** what reaches `WeaponMounts_DemandFullChargeOnArmed`, to which `es2_xref.py` finds no branch or stored pointer, and what the two dwords at `0003d7fc` and `0003d8f0` holding `WeaponMounts_FireTrigger`'s padding entry are.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in the cockpit, `BP <CS>:0000cf78` fires every frame; `W` moves the armed row to the next weapon in the chain, and Alt with a row's number takes that row out of the chain or puts it back (`BP <CS>:0000d5e4`).
