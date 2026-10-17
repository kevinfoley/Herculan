# DBSIM.EXE — `dat\<chassis>.dat`, the chassis type record

Each of ES1's sixteen HERCs has a file of its movement, turret, animation, hit, shadow and shield numbers, which the simulator loads while it reads a mission's `data\script.dat`. The file is the first 192 bytes of ES2's 216-byte record ([ES2's type record](../../retail/simulation/mech-locomotion.md#mech-type-record)), at the same offsets: ES1's has none of ES2's last 24 bytes, the HUD speed scale, the stride calibration pair and the debris file name, and its loader rescales no speeds. A search of `GO.EXE`'s strings finds `\mechs.dba` and no `mechs` name list, where the same search of `DBSIM.EXE` finds `mechs`. The eight calls `es2_xref.py` finds to `GO.EXE`'s copy of `Ovl_ReadFile` (`ES1GO 00051918`) each read a `bnd\` file (`alert`, `ctl_alrt`, `cal_alrt`, `map`, `shellmap`, `ctl_mgr`, `main`, `imdevice`), and the calls to its path builder with the `dat` folder that a search of its decompile finds name `styles`, `zone<n>` and `mforms`. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## Which files

`nam\MECHS.NAM` lists the chassis ([name lists](nam-name-list.md#mod-lists)), and a chassis' type number is its place in the list:

| Type | File | Type | File | Type | File | Type | File |
|---|---|---|---|---|---|---|---|
| 0 | `ROADRUNN` | 4 | `PATRIOT` | 8 | `MONGOOSE` | 12 | `ACHILLES` |
| 1 | `GUNSLING` | 5 | `PANTHER` | 9 | `SCORPION` | 13 | `APOLLO` |
| 2 | `RAPTOR` | 6 | `HERCULES` | 10 | `MIRIMAC` | 14 | `BULLDOG` |
| 3 | `TOMAHAWK` | 7 | `COLOSSUS` | 11 | `RAMSES` | 15 | `SPIDER` |

`SIMVOL0.VOL`'s `dat\` holds a 192-byte file of each name, and no other `dat\` entry of 192 bytes.

Only the chassis given a slot are loaded. `Mech_LoadResources` (`000198f0`) sets the 16-byte slot map `g_MechTypeSlotIndex` (`000bc430`) to `0xff` and the slot count `g_MechTypeCount` (`000bc42e`) to 0, and `MechTypeSlot_Allocate` (`00019a40`) gives a type the next slot the first time it is asked for it. Its one call site found by `es2_xref.py` is a loop in `DBSim_LoadScriptDat` (`0001d850`) over types the script names ([Open](#open)). `Mechs_InitAllTypes` (`00019ee0`) then allocates the slot count's records and calls `MechType_InitOne` (`00019cf0`) for each type from 0 to 15 that has a slot, where ES2's walks 0x55 type numbers.

## The layout

Little-endian, read whole. File offset *N* is the type record's `+N+2`, as in ES2. Offsets in bold are ones the functions this page names read; the others hold values of ES2's kind at ES2's offsets, and what ES1 reads them for is [Open](#open).

| Offset | ES2's field | Retail values |
|---|---|---|
| **0** | max turn rate | 600 |
| **2**, **4** | max reverse speed, max forward speed | [per chassis](#per-chassis-values) |
| **6**, **8** | speed and turn-rate steps per tick | 30 (`SPIDER` 25); 150 |
| 10 | the part the cockpit eye rides | 5; `MONGOOSE` 10, `SPIDER` 12 |
| **12**–**18** | walk, run, forward stop, reverse stop sequences | 1, 2, 3, 4; `BULLDOG` 3, 4, 5, 6; `SPIDER` 0, 0, 1, 1 |
| 20 | ride height | 0; `COLOSSUS` 400 |
| 22, 24 | hit cylinder's centre height and radius | 750 and 1500 on `ROADRUNN`, `GUNSLING`, `SCORPION` and `MONGOOSE`; 1000 and 1000 on `SPIDER`; 1000 and 2500 on the rest |
| 26–32 | twist sequence, rate, rate step, limit | 0, 1000, 1000, 14000; `RAPTOR`'s step 250; `BULLDOG`'s limit 32767; `SPIDER` −1, 1500, 300, 14000 |
| 34–42 | pitch sequence, rate, rate step, limit up, limit down | 5, 800, 800, 6000, −4000; `RAPTOR` 700 and 175; up 3500 and down −2000 on `GUNSLING`, `SCORPION` and `MONGOOSE`, 8500 and −8000 on `ROADRUNN`; `BULLDOG` 7 and down −2000; `SPIDER` −1, 1000, 200 |
| **44** | walk-to-run speed | [per chassis](#per-chassis-values) |
| **46**–65 | the parts [whose nodes renumber the crude roots](../../retail/rendering/mech-shape-drawing.md#the-crude-roots-are-renumbered-at-load), signed bytes ended by a negative one | 1–5 and 12–17; `MONGOOSE` adds 10, 88 and 99; `BULLDOG` 1–5 and 12–25; `SPIDER` 1–4, 12, 14, 15 and 18–23 |
| 66 | the base of the [AI combat rating](../../retail/simulation/ai-targeting.md#relative-combat-rating) | 1000 |
| **68** | the death sequence | 7; `BULLDOG` 2, `SPIDER` 1 |
| **70** | — | 2: [the roots skipped in low memory](#low-memory) |
| 72 | leg count | 2; `BULLDOG` 4, `SPIDER` 0 |
| 74 | leaves no wreck | 0; `SPIDER` 1 |
| 76 | mass | [per chassis](#per-chassis-values); `BULLDOG` 20000, `SPIDER` 0 |
| 78, 80 | flyer flag, cockpit colour scheme | 0 |
| 82 | salvage scale | [per chassis](#per-chassis-values) |
| 84 | whether a hit can knock the weapon mounts out | 1; `BULLDOG` 0 |
| 86–97 | the chassis name, NUL-padded | `Roadrunner`, `Gunslinger`, `Raptor`, … in mixed case |
| 98, 100 | the pilot's eye, fore/aft and lift | 200 on types 0–7 (`RAPTOR` 300) and 0 on the rest; the lift [per chassis](#per-chassis-values) |
| 102, 104 | the outside view's orbit centre, fore/aft and height | 0; 1400 on `ROADRUNN`, `GUNSLING`, `SCORPION` and `MONGOOSE`, 2600 on `BULLDOG`, 800 on `SPIDER`, 1600 on the rest |
| 106 | — | 0; the load overwrites it with the type number |
| **108** | reverse walk-to-run speed | [per chassis](#per-chassis-values) |
| 110 | body radius | 750 |
| 112–121 | [the shadows](../../retail/simulation/ground-shapes.md#a-hercs-shadows): kind bytes from 112 ended by a negative one, part bytes from 117 | kinds 0, 0, 2 on parts 14, 15, 12; `BULLDOG` 0, 0, 0, 0 on 14, 15, 22, 23; `SPIDER` none |
| **122** | turn-in-place sequence | 6; `BULLDOG` and `SPIDER` 1 |
| 124–147 | twelve AI rating penalties | 500 |
| **148** | the texture group | [per chassis](#per-chassis-values) |
| **150**–**156** | where a foot plants: walking forward, walking backward and running (a fore-and-aft position), and where the falling body sounds (a height) | 325, −700, 50, 400 on `ROADRUNN` and `GUNSLING`; 325, −850, 50, 400 on `SCORPION` and `MONGOOSE`; 150, −350, 75, 800 on `COLOSSUS`; 160, 100, −100, 400 on `BULLDOG`; 100, 100, −100, 400 on `SPIDER`; 125, −300, 50, 400 on the rest |
| 158–169 | — | 0 |
| **170**–**174** | where a foot arms its next plant: walking forward, walking backward, running | 375, −750, 250 on `ROADRUNN` and `GUNSLING`; 375, −700, 250 on `SCORPION` and `MONGOOSE`; 200, −425, 275 on `COLOSSUS`; 250, 500, −125 on `BULLDOG`; 125, 500, −125 on `SPIDER`; 175, −400, 250 on the rest |
| 176–189 | — | 0 |
| 190 | shield capacity | 3500; `SPIDER` 0 |

`Mech_LocomotionTick` (`00012038`) reads offsets 0–8, 12–18, 44, 68, 108 and 122 in the roles ES2's gives them, 44 and 108 being the walk-to-run speeds its gait machine shifts at ([the control law](../simulation/mech-locomotion.md#the-control-law--mech_locomotiontick-00012038)), and `Mech_PlaceLegsOnGround` (`000144dc`) reads 150–156 and 170–174 ([shadows and footfalls](../simulation/mech-locomotion.md#shadows-and-footfalls--mech_placelegsonground-000144dc)). `BULLDOG` holds the values ES2's `PITBULL` holds where ES2 marks the PITBULL out: four legs, a twist limit of 32767, death sequence 2, mass 20000, four shadows and mounts that cannot be knocked out. `SPIDER` holds ES2's `SPIDER`'s in the fields that mark it out: no twist or pitch sequence, no legs, no wreck, mass 0, no shadows and no shield.

### Per-chassis values

| Type | Name | Reverse (2) | Forward (4) | Walk-run (44) | Reverse walk-run (108) | Mass (76) | Salvage (82) | Eye lift (100) | Texture group (148) |
|---|---|---|---|---|---|---|---|---|---|
| 0 | `Roadrunner` | −155 | 256 | 160 | −160 | 5000 | 1024 | 490 | 0 |
| 1 | `Gunslinger` | −155 | 270 | 160 | −160 | 5000 | 1024 | 660 | 0 |
| 2 | `Raptor` | −110 | 190 | 135 | −120 | 6000 | 1024 | 460 | 1 |
| 3 | `Tomahawk` | −120 | 180 | 135 | −125 | 6000 | 1024 | 30 | 1 |
| 4 | `Patriot` | −110 | 170 | 130 | −120 | 7000 | 1024 | 310 | 1 |
| 5 | `Panther` | −100 | 160 | 130 | −110 | 8000 | 1024 | 140 | 2 |
| 6 | `Hercules` | −100 | 150 | 125 | −125 | 10000 | 1024 | 40 | 2 |
| 7 | `Colossus` | −100 | 140 | 120 | −120 | 12000 | 1024 | 320 | 2 |
| 8 | `Mongoose` | −120 | 270 | 160 | −140 | 5000 | 600 | 560 | 3 |
| 9 | `Scorpion` | −120 | 270 | 160 | −140 | 5000 | 500 | 820 | 3 |
| 10 | `Mirimac` | −90 | 170 | 130 | −110 | 7000 | 1200 | 200 | 3 |
| 11 | `Ramses` | −110 | 170 | 135 | −120 | 6000 | 800 | 280 | 3 |
| 12 | `Achilles` | −85 | 160 | 130 | −110 | 10000 | 1500 | 0 | 3 |
| 13 | `Apollo` | −75 | 150 | 125 | −100 | 10000 | 2000 | 190 | 3 |
| 14 | `Bulldog` | −100 | 160 | 120 | −120 | 20000 | 2000 | 2000 | 3 |
| 15 | `Spider` | −200 | 300 | 600 | −60 | 0 | 0 | 0 | 3 |

## The load

`MechType_InitOne` (`00019cf0`), given the type number, follows [ES2's](../../retail/simulation/mech-locomotion.md#mech-type-record) with these differences:

1. It takes the type's name from `g_MechTypeNames` (`000bc440`), its slot from `MechTypeSlot_GetIndex` (`00019a30`), and the slot's record in `g_MechTypeTable` (`000bc444`), records of `0x1c4` bytes where ES2's are `0x21e`.
2. It reads 192 bytes of `dat\<name>` into the record's `+2` with `Ovl_ReadFile` (`000654bc`), which asserts at severity 5 (`util\ovl.cpp` lines `0x15` and `0x17`) when the file does not open or the read fails. It stores the slot at `+0` and the type number at `+0x6c`, over file offset 106.
3. It loads `dts\<name>` with `Shape_LoadAllRoots` (tag `0x1e0003`) into `+0xc6`, skipping the file's first *k* roots where *k* is file offset 70 when [`g_LowMemory`](#low-memory) is set and 0 when not; ES2's skips none. The root count goes to `+0xd2`, `g_ShapeDetailThresholds` (`00091dac`, ES2's seven values 75, 60, 45, 25, 18, 12, 6) to `+0xca`, and an array of root-count 6-byte `CONNECT_INFO` pairs (`CONNECT_INFO_Ctor`, `00019ea4`) to `+0xce`.
4. `MechType_RemapDetailRootTransforms` (`00019ba0`), given the part list at `+0x30` and the root set, is ES2's, finding a part through the root's vtable `+0x24` where ES2's uses `+0x20`. Its `Shape_RemapTransformIds` (`00019ac4`) switches on the part's class tag, read through vtable `+4` where ES2's reads `+0`, over ES2's tags and `0x1e0005`, which returns.
5. It loads `gl\<name>` with `GunLayout_LoadForMechType` (`0000c1d0`) into gun-layout slot `000bc44e` + the type's slot, collects the hardpoint bones with `GunLayout_CollectHardpointBones` (`0000befc`), and for each root builds its hardpoint slot list with `MechType_BindHardpointSlots` (`000025c4`), which finds a part's slot through vtable `+0x28` where ES2's uses `+0x24`. The three follow ES2's ([the hardpoint list](../../retail/formats/gun-layout-gl.md)); ES1's gun-layout assert is `gunlist.cpp` line `0x112`, severity 5.
6. Each root's `+0x26` takes the bank in `g_MechTextureGroupSlots` (`000bc450`) at file offset 148: the bank pointer itself, from four 4-byte slots, where ES2's takes the address of an 8-byte slot of seven. `Mech_LoadResources` loads `dba\light`, `dba\medium`, `dba\heavy` and `dba\enemy` into the four slots with `ClassItem_LoadFromFile`, where ES2's packs seven banks into atlases.
7. `PaperDoll_Load` (`00030388`), given the slot and the name, loads `dba\<name>` with `ClassItem_LoadFromFile` and reads `pdg\<name>` ([the paper doll](../../retail/formats/pdg-paper-doll.md)) into the slot's `0x5c`-byte record, which the load stores at `+0xc2`. ES2's chooses between two folders for the bank, asserts when `pdg\<name>` does not open, and shifts the coordinates and resolves the colours it reads; ES1's does none of these. `PaperDoll_InitTables` (`00030288`), which `Mechs_InitAllTypes` calls first, loads `dba\weapons` and `pdg\weapons` the same way.

It does no speed rescale, writes neither ES2's HUD speed scale at `+0xc2` nor its copy of the forward speed at `+0xc8`, and has no flyer or debris step.

### The type record in memory

| Offset | Size | Holds |
|---|---|---|
| `+0x00` | 2 | the slot |
| `+0x02` | 192 | the file; `+0x6c` overwritten with the type number |
| `+0xc2` | 4 | the paper-doll record |
| `+0xc6` | 4 | the root array |
| `+0xca` | 4 | `g_ShapeDetailThresholds` |
| `+0xce` | 4 | the `CONNECT_INFO` array |
| `+0xd2` | 2 | the root count |
| `+0xd4` | 240 | ten `0x18`-byte `CONFIG_PART`s |

`MECH_TYPE_DATA_Ctor` (`00019f40`) constructs the ten parts with `ConfigPart_Ctor` (`00019f78`), which stores four vptrs in turn (`00091e6e`, `00091e92`, `00091d7a`, `00091d46`) as ES2's stores four, and runs `ShapeInstance_CtorCellFrames` on the part's `+0x10`. ES2's record holds them at `+0xec`.

### Low memory

`g_LowMemory` (`000bda98`, `int16`) is set to 1 at `000595b1`, in the startup function `00059350`, when the heap `Mem_InitHeap` (`00001524`) returns is under `0x3c45b0` bytes (3,950,000); startup asserts under `0x26e8f0` (2,550,000). A search of the disassembly dump for the address finds that store and 14 compares. When it is set, `Mech_LoadResources` loads none of the four texture banks and `MechType_InitOne` skips file offset 70's count of roots, 2 in every retail file.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| File offset 70 means nothing, as ES2's loader does not read it | ES1's loader reads it as the number of shape roots to skip in low memory |

## Open

- **Open:** which of `data\script.dat`'s records the loop in `DBSim_LoadScriptDat` that allocates chassis slots takes its types from.
- **Open:** which ES1 functions read offsets 66, 78, 80, 86–97, 106, 124–147, 158–169 and 176–189, and whether each means what ES2's field does. The data alone puts them at ES2's offsets. The HERC's own functions read 10, 20–42, 72–76, 82, 84, 98–104, 108–121, 150–156, 170–174 and 190 in the roles ES2's gives them ([the HERC](../simulation/mech.md)).
- **Open:** what `g_LowMemory`'s other 12 readers change; which roots a skip of two leaves for close range; and what a root's `+0x26` draws with when the banks were not loaded.
- **Open:** the four handlers of `Shape_RemapTransformIds` (`00019aef`–`00019b93`, undefined bytes in Ghidra), read here only as far as its tag and handler tables at `00091e14`.
- **Open:** whether anything gives the ten `CONFIG_PART`s at `+0xd4` a model after `ConfigPart_Ctor` builds them with none; `Mech_Draw` hands them to `Mech_SpliceHardpointShapes` as the parts an empty hardpoint takes ([the hardpoints](../simulation/object-classes.md#the-hardpoints--mech_splicehardpointshapes-00002654)).
- **Open:** whether ES2's simulator reads its own file offset 70, which is 2 in `OUTLAW`, `PITBULL` and `SPIDER`, the three ES2 files compared for this page.
