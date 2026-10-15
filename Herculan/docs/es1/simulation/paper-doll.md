# DBSIM.EXE — the paper doll

**ES1's damage diagrams are ES2's: each chassis has a paper doll whose regions are repainted in a color for how damaged that part is, and each machine has a list of weapon icons placed over its diagram.** ES1's repaints three palette indices of a region's art where ES2's repaints one, picks fixed palette indices for the damage bands where ES2's looks them up in its color table, repaints one screen pixel per art pixel where ES2's repaints a block when it draws the art doubled, and takes each weapon's icon from a pair of frames. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's, described in ES2's [paper doll](../../retail/simulation/cockpit-hud-widgets.md#paper-doll) and [`.PDG` file](../../retail/formats/pdg-paper-doll.md). Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `000301ec`–`00030f43` and the initialized data `000932e2`–`0009338d`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `000301ec`, `00030214`, `00030253` (frameless, with no Ghidra function) and `0003025c`, each the code of `panel.cpp`'s ([where `panel.cpp` starts](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `000932e2` and `00093316`. No assert in the file names a source path, and the data holds none; ES2's names `pdamage.cpp`.

The code ends with the module's static initializer and the element constructor it uses ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)):

- **`PaperDoll_StaticInit` (`00030f00`)**, entry `000adc38` of the static constructor table, frameless with no Ghidra function: `__vec_ctor` builds 12 8-byte pairs at `WeaponIconSizes` (`000b0f18`) with `WeaponIconSize_ElementCtor`, and 17 `0x22`-byte weapon icons at `PaperDoll_StaticWeaponIcons` (`000b0f78`) with `PaperDollWeaponIcon_ElementCtor`. ES2's (`0043874c`) builds 28 pairs and no icons.
- **`WeaponIconSize_ElementCtor` (`00030f28`)**, (pair): allocates 8 bytes when given none and returns the pair. Its `RET` at `00030f40` is followed by one zero byte and the next object file at `00030f44` ([Open](#open)).

The exit table has no entry in the file: its entries run from `SimStrings_StaticDtor` (`000adcb8`) to `00036a54` (`000adcbc`).

The data holds, after the run's tables, `PaperDoll_TypeTable` (`00093348`), the string `weapons`, the folder name `pdg` twice, and the jump tables of `PaperDoll_RecolorRect` (`0009335c`), `PaperDoll_RecolorRectFromArt` (`0009336c`) and `Damage_PickRegionTint` (`0009337c`), each over modes 0 to 3.

## The records

**`PaperDoll_InitTables` (`00030288`)** and **`PaperDoll_Load` (`00030388`)** read `pdg\weapons` and each chassis' `pdg\<name>` ([the chassis files](../formats/chassis-dat.md#the-load)) into ES2's records: a `0x5c`-byte record per chassis type in `PaperDoll_TypeTable`, three `0x1c`-byte views, their `0x1c`-byte regions and the `0x14`-byte hardpoints ([`.PDG`](../../retail/formats/pdg-paper-doll.md)), with no coordinate shift and no color lookup. A region's `colorId` is therefore a palette index as the file states it. ES1's 17 `.PDG` files have ES2's layout ([what HercWorks reads](../../herculan/plan-es1-support.md#what-hercworks-reads)); `pdg\WEAPONS.PDG` holds 12 sizes, which fill `WeaponIconSizes`: 11×32, 9×9, 5×25, 8×7, 7×20, 7×7, 10×25, 12×9, 5×35, 10×6, 9×6 and 9×6.

The element constructors each allocate their record when given none and return it, constructing in place the points and rects inside it, as the compiler builds a subobject ([constructors](../formats/compiler.md#constructors-and-destructors)):

| ES1 | Address | Record | ES2 |
|---|---|---|---|
| `PaperDollType_ElementCtor` | `00030314` | the `0x5c`-byte record, its three views built with `__vec_ctor` and `PaperDollView_ElementCtor` | `00438765` |
| `PaperDollView_ElementCtor` | `00030344` | a `0x1c`-byte view, points at `+4` and `+0xc` | `004387a3` |
| `PaperDollRegion_ElementCtor` | `000304e0` | a `0x1c`-byte region, its rect at `+4` | `00438785` |
| `PaperDollHardpoint_ElementCtor` | `0003053c` | a `0x14`-byte hardpoint, its point at `+0` | `00438790` |
| `PaperDollWeaponIcon_ElementCtor` | `000307b0` | a `0x22`-byte icon, its point at `+4` and rect at `+0xc` | `00438798` |

**`PaperDoll_GetView` (`0003056c`)**, (view, machine): the record at the machine's type record (`+0x188`) `+0xc2`, plus view × `0x1c`; ES2's (`00437c68`) reads `+0x1f2` and `+0xda`. `es2_xref.py` finds no reference to it ([Open](#open)).

## The weapon icons

**`PaperDoll_BuildWeaponIcons` (`0003058c`)**, (machine), is ES2's ([weapon icons](../../retail/simulation/cockpit-hud-widgets.md#weapon-icons)) with these differences:

- The paper-doll record is the type record's (`+0x188`) `+0xc2`, and the hardpoint's mount the entry of the array at `+0` of the loadout at the machine's `+0x198`; ES2's reads `+0x1f2`, `+0xda` and `+4` of `+0x202`.
- The frame is the weapon template's `+0x50` × 2 plus the hardpoint's `frameOffset`, where ES2's adds the two: each icon number has two frames in `dba\weapons`.
- `blitFlags` 2 moves the icon left by the bitmap's width (its `int16` `+6`) less the `WEAPONS.PDG` width, with no shift for the doubled screen.

It finds each hardpoint's gun through **`Mech_GetGunLayout` (`0001a0f8`)**, (machine): `GunLayout_GetBySlot`(the `int16` at `+0` of the type record `+0x188`, plus the `int16` at `000bc44e`), ES2's (`00420634`), which reads `+0x1f2`. The layout's `int16` count is at `+0` and its `0x1a`-byte records from the pointer at `+2`, the slot byte at a record's `+0x17`, as ES2's. The builder returns the 8-byte {count, icons} holder from `operator new`. `es2_xref.py` finds one call of it, in `Sim_StartMission` at `0005b27a`, which walks the pool at `000af68c` with `Pool_Prev` and stores each object's list at its `+0x194` (ES2's `+0x1fe`).

## Tinting

**`PaperDoll_RecolorRect` (`00030818`)**, (mode, tint, key, rect), and **`PaperDoll_RecolorRectFromArt` (`00030ab4`)**, (mode, tint, key, artOrigin, art, rect), are ES2's ([tinting](../../retail/simulation/cockpit-hud-widgets.md#tinting)) on one pixel per screen pixel: each makes the display's (`000bdd28`) draw page `+0x3c` its source page `+0x38`, with `00098dd6` and `g_RasterRoutines` slot 16 (`0009df14`) when that display is current, offsets the rect by the canvas context's (`000bdbda`) `+0x210`, `+0x214` less its `+0x220`, `+0x224`, and walks it a row at a time, left to right, reading through slot 4 (`0009dee4`) and writing through slot 5 (`0009dee8`), ES2's get-pixel and put-pixel slots:

| Mode | A pixel is rewritten when it is | It becomes | The walk is skipped when tint = key |
|---|---|---|---|
| 0 | key, key + 1 or key + 2 | itself + (tint − key) | yes; in `PaperDoll_RecolorRectFromArt`, no |
| 1 | key | tint | yes |
| 2 | anything but 63 | itself + tint | no |
| 3 | key, key + 1 or key + 2 | tint | no |

`PaperDoll_RecolorRectFromArt`'s mode 0 tests the art's pixel instead of the screen's: from `art`'s pixels (`+0xe`), *width* its `int16` `+6`, at the rect's offset from `artOrigin`, offset the same way, one byte a pixel, adding *width* − (*x1* − *x0*) − 1 at each row's end. ES2's modes 0 and 3 test the key alone, its mode 2 spares `COLORS.DAT` id 19's index, it writes a doubled block per pixel when the bank is drawn doubled, and it does nothing when the rect's top row is above the screen.

**`Damage_PickRegionTint` (`00030dc4`)**, (out, region, damage), ES2's (`00438624`) on `Damage_ToConditionState`'s bands, writes the state and a palette index to the two dwords at *out*:

| Intact | State | Mode 0, 2 or 3 | Mode 1 |
|---|---|---|---|
| ≥ 90% | 0 | 54 | the region's `colorId` |
| ≥ 74% | 1 | 57 | `colorId` − 1, or 61 when `colorId` is 60 |
| ≥ 51% | 2 | 92 | `colorId` − 2, or 62 when `colorId` is 60 |
| ≥ 1% | 3 | 51 | 48 |
| 0 | 4 | 62 | 62 |

ES2's reads the five colors through its color table, ids 12, 15, 20, 9 and 18, and in mode 1 keeps `colorId` for states 1 and 2 and takes ids 6 and 17 for 3 and 4. A mode above 3 writes whatever `EDX` and `EDI` held.

**`Damage_ToConditionState` (`00030eb8`)**, (damage): ES2's (`00438700`), the same statements. `es2_xref.py` finds calls from `Player_DependentCondition` ([the squadmates](cockpit-panel.md#squadmate-and-pilot-helpers)) and `000336c6`.

`es2_xref.py` finds `PaperDoll_RecolorRect` called once from `000330b4` and five times from `000424f8`, `PaperDoll_RecolorRectFromArt` once from `000424f8`, and `Damage_PickRegionTint` once from `000330b4` and four times from `000424f8` ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorPaperDollCopy`, `ConfigPart_DtorPaperDollCopy`, `ConfigPart_GetClassTagPaperDollCopy`, `TSPartBase_DtorPaperDollCopy` | `000301ec`, `00030214`, `00030253`, `0003025c` | the run; slots `+0x1c` and `+4` of `000932e2` and `+0x1c` of `00093316` (`0003025c` by its padding `00030259`) |
| `PaperDoll_InitTables`, `PaperDoll_Load` | `00030288`, `00030388` | `004378d8`, `004379cc` |
| the element constructors | `00030314`, `00030344`, `000304e0`, `0003053c`, `000307b0`, `00030f28` | [the records](#the-records); `00030f28` none |
| `PaperDoll_GetView`, `PaperDoll_BuildWeaponIcons` | `0003056c`, `0003058c` | `00437c68`, `00437c8c` |
| `PaperDoll_RecolorRect`, `PaperDoll_RecolorRectFromArt`, `Damage_PickRegionTint`, `Damage_ToConditionState` | `00030818`, `00030ab4`, `00030dc4`, `00030eb8` | `00437e94`, `00438230`, `00438624`, `00438700` |
| `PaperDoll_StaticInit` | `00030f00` | `0043874c` |

## Globals

| | Address |
|---|---|
| `PaperDoll_TypeTable`, the records `PaperDoll_InitTables` allocates | `00093348` |
| `PaperDoll_WeaponIconBank`, `dba\weapons` | `000b0f14` |
| `WeaponIconSizes`, 12 {width, height} | `000b0f18` |
| `PaperDoll_StaticWeaponIcons`, 17 icons | `000b0f78` |

## Open

- **Open:** the next object file, from `00030f44`: `Label_Ctor`, `00030fc4` (the matcher's lead is ES2's `Label_SetRectAroundPoint`), `Label_SetRect`, `Label_SetText`, `00031224` (lead `Label_SetNumber`), `00031250`, `00031280`, `00031294` (lead `Label_SetTextWithHotkey`), and the bare `RET` at `00031314`, entry `000adc3c` of the static constructor table, followed by three zero bytes and `00031318`.
- **Open:** the screens that draw the diagram, `000330b4` and `000424f8`, unread. Their call counts match ES2's `MfdStatusScreen_Paint` and `HddDamageScreen_Update` ([ES2's](../../retail/simulation/mfd.md#viewport-and-condition-per-class)); which mode and damage reading each passes for a region is in their bodies. Also unread: `000336b0`, which calls `Damage_ToConditionState`.
- **Open:** what reads `PaperDoll_StaticWeaponIcons`: `es2_xref.py` finds `000b0f78` in `PaperDoll_StaticInit` alone. And what calls `PaperDoll_GetView` and `HeapPtr_DtorPaperDollCopy`, to which it finds no reference.
- **Open:** which region `colorId` values and modes ES1's `.PDG` files state, and so whether mode 1's 60 and the three-index key range meet retail art.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:0003058c` fires once for each machine as a mission starts, and the word at the returned holder's `+0` is that machine's `.PDG` hardpoint count.
