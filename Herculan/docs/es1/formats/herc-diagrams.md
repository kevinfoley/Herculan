# GO.EXE — the HERC diagram tables

**The shell shows each HERC as a diagram built from sprites: a picture of each mounted weapon in its place, marks over the parts that are damaged, and an area per mount that can be clicked.** Three families of `SHELL0.VOL` `cfg\` tables position them, per chassis: `cfg\<chassis>.dat`, `cfg\<chassis>dmg1.dat` and `cfg\hotspot.dat`, all loaded once by `Shell_LoadData` (`00020630`), which also loads the shell's fonts and the battle set ([which battle set](bst-battle-sets.md#which-battle-set)). ES2 keeps the same kinds of geometry in `gam\arm_*.dat` and `gam\arm_hots.dat` ([ES2's screen-layout families](../../retail/formats/herc-catalogs.md#the-screen-layout-families)). Addresses are `ES1GO` image addresses ([the X-32 executable](x32-executable.md#the-image)).

The chassis are numbered as a HERC record's chassis field ([the HERC record](car-hercs-and-stock.md#the-herc-record)), and both per-chassis path tables list them in one order:

| Chassis | Layout (`000804d0`) | Damage marks (`0008057c`) | Mounts (`000803c8`) |
|---|---|---|---|
| 0 | `CFG\ROAD.DAT` | `CFG\ROADDMG1.DAT` | 3 |
| 1 | `CFG\GUNS.DAT` | `CFG\GUNNDMG1.DAT` | 3 |
| 2 | `CFG\RAPT.DAT` | `CFG\RAPTDMG1.DAT` | 4 |
| 3 | `CFG\TOMA.DAT` | `CFG\TOMADMG1.DAT` | 5 |
| 4 | `CFG\PATR.DAT` | `CFG\PATRDMG1.DAT` | 6 |
| 5 | `CFG\PANT.DAT` | `CFG\PNTHDMG1.DAT` | 7 |
| 6 | `CFG\HERC.DAT` | `CFG\HERCDMG1.DAT` | 8 |
| 7 | `CFG\COLO.DAT` | `CFG\COLODMG1.DAT` | 9 |

`Shell_LoadData` (`00020630`) calls `HercLayout_Load`, `HercDamageMarks_Load`, `LoadDamageDat_Shell` ([the value tables](herc-catalogs.md#the-load)), `HercHotspots_Load` and `HercArt_LoadAllBanks` in that order, from `000208ce` to `000208eb`. `Shell_FreeData` (`00020ae3`) frees the three tables with `HercLayout_Free` (`00021fbc`), `HercDamageMarks_Free` (`000223a1`) and `HercHotspots_Free` (`000227e4`).

## The mount pictures

`cfg\<chassis>.dat` lists, for each mount of the chassis, a record per weapon the mount can show:

```
int16    mount count
mount count x {
  int16  record count
  record count x {
    int16  frame
    int16  weapon        -- 17 for an empty mount
    int16  x, y          -- the first point
    int16  x, y          -- the second point
  }
}
```

Each retail file's mount count is its chassis's count from the table at `000803c8`.

`HercLayout_Load` (`00021d0c`) opens each of the eight files through `VolRStream_CtorFromPath`, reads the mount count into the word at `000aae0e` + 6 × chassis and an array of that many 6-byte mounts into the dword at `000aae0a` + 6 × chassis, and for each mount reads its record count into the mount's `+4` and makes an array of that many 20-byte records (`HercLayout_RecordCtor`, `00021f71`) at its `+0`. A record is the weapon at `+0`, the frame at `+2`, and the two points as `int32` pairs at `+4` and `+0xc`, each coordinate sign-extended. It destroys each stream through its vtable. `HercLayout_Free` frees the record arrays and the mount arrays.

Four lookups take a weapon, a mount and a chassis, with no bounds test on the mount or the chassis, and search the mount's records for the first whose weapon matches: `HercLayout_Find` (`00022024`) returns the record, or null; `HercLayout_FindFrame` (`00022086`) its frame, or −1; `HercLayout_FindFirstPoint` (`000220ec`) and `HercLayout_FindSecondPoint` (`0002219f`) fill a point with the record's first or second point, or (0, 0).

`Herc_BuildDiagram` (`00023883`) builds a HERC's diagram. `es2_xref.py` finds seven calls: from `Herc_Read`, when a HERC list is read ([the HERC lists](car-hercs-and-stock.md#the-herc-lists)), and from `000241f9`, `00028e50`, `00029d0f`, `00029d68` and twice `Hangar_AdvanceMarkedHerc` (`00029e01`). It sets `+0x70` to −1, clears `0007ff98`, takes the chassis's `RES32\<chassis>WEP1.DBA` and `<chassis>OUTL.DBA` banks into `+0x9c` and `+0xa0`, loading each the first time a chassis needs it into the tables at `0007ff9c` and `0007ffbc`, and calls `00023605` with the HERC and the word `Herc_Read` hands it. For chassis 7 it makes a 34-byte element at `+0x98`; for the others `+0x98` is null. It makes the mount arrays, 38-byte elements at `+0xa4` (`00023d70`) and 34-byte elements at `+0x94` (`00023f2c`), and for each mount:

1. when the mount's status word is 0 (`Herc_GetMountStatus`, `00024b23`, [the HERC record](car-hercs-and-stock.md#the-herc-record)), makes the mount empty (weapon 17) and sets the word to 100 (`Herc_SetStatus`(HERC, 100, mount, `0x786`), `00024c14`);
2. calls `HercLayout_FindFrame`, and passes the record's second point (`HercLayout_FindSecondPoint`) to `000216e0` for the mount's `+0x94` element;
3. sets the mount's `+0xa4` element with `MountPicture_Set` (`00022d78`), giving it the mount's status word;
4. on chassis 7, when mount 2 or 3 is not empty, passes the point (`0x56`, `0x19`) to `000216e0` for the `+0x98` element and sets the byte `0007ff8e`;
5. sets to 0 the mount's word when its weapon is 9, 10 or 11 and the word is 5.

Last, when the `+0x98` element exists and `0007ff8e` is clear, it passes (0, 0) to `000216e0` for it.

`Herc_FreeDiagram` (`0002330e`), given a HERC, deletes the `+0xa4` array (`__vec_delete`, `0005547c`, with `000232df` for each 38-byte element) and the `+0x94` array (with `00021519` for each 34-byte element), destroys the `+0x98` element with `00021519`(element, 3), nulling each pointer it frees, and, when the byte `Shell_LowMemory` (`0007fd09`) is set, calls `000228aa` with the chassis and nulls the bank pointers `+0x9c` and `+0xa0`. `es2_xref.py` finds three calls: from `000232a4`, which [`Hangar_Clear`](car-hercs-and-stock.md#the-herc-lists) destroys each HERC with, `000241cd`, and `Hangar_ReadStatusBlock` (`00028e50`), which frees and rebuilds a HERC's diagram around the status block it reads ([the HERC record](car-hercs-and-stock.md#the-herc-record)).

`MountPicture_Set`, given an element, a value, a weapon, a mount and a chassis, stores the record `HercLayout_Find` returns at the element's `+0`. For weapon 17 it passes (0, 0) to `000216e0` for the element's `+4`. For any other weapon, when the value is below `0x33` it loads the chassis's `RES32\<chassis>WEPD.DBA` into the table at `0007fffc` the first time, and passes the record's first point; at `0x33` or more it passes the first point only when the chassis's `WEP1` bank is loaded. `HercArt_LoadAllBanks` (`0002280f`) loads all eight chassis' `WEP1`, `OUTL`, `DMG1` and `WEPD` banks into their tables, and the `BILD` banks of chassis 2 to 7 into the table at `0008001c`.

`es2_xref.py` finds `HercLayout_FindFrame`, `HercLayout_FindSecondPoint` and `MountPicture_Set` called from `Herc_BuildDiagram` and `00024788`, `HercLayout_Find` from `MountPicture_Set`, and no branch to and no stored pointer of `HercLayout_FindFirstPoint`.

ES2's `gam\arm_*.dat` places a mount's picture from a group per weapon id with a part id per mount ([ES2's armory layout](../../retail/formats/herc-catalogs.md#gamarm_dat--armory-layout)); ES1's file groups by mount and keys each record by weapon, has no leading records, holds `int16` coordinates, and is read with no inset.

## The damage marks

`cfg\<chassis>dmg1.dat` holds, for each of six component groups and each of three damage levels, the marks drawn over the diagram from the chassis's `RES32\<chassis>DMG1.DBA`:

```
6 x {                  -- component group
  3 x {                -- damage level 1 to 3
    int16  mark count
    mark count x {
      int16  frame
      int32  x
      int32  y
    }
  }
}
```

`HercDamageMarks_Load` (`00022252`) opens the eight files in turn through one `VolRStream` and for each list reads the count into the word at `000aae3a` + `0x6c` × chassis + `0x12` × group + 6 × level and makes an array of that many 10-byte marks (`HercDamageMarks_MarkCtor`, `0002236c`) at the dword after it, reading each mark whole. `HercDamageMarks_Free` deletes the 144 arrays.

`Herc_DrawDamageMarks` (`00024366`), given a HERC, loads its chassis's `DMG1` bank into the table at `0007ffdc` the first time, and for each group *g* from 0 to 5 takes the level `Repair_LevelForCondition` (`000389fb`) returns for the mean of external area *g*, `Herc_GetStatus` (`00024b86`)(HERC, *g*, `0x785`) ([the HERC record](car-hercs-and-stock.md#the-herc-record)). For a level of 1 to 3 it draws each mark of the group's list for that level: `Bitmap_Blit` of the bank's frame at (*x*, *y*), once with the word at `+0x38` of the canvas `000ab616` set to the word `0007fccc` and once with it put back, each time also through the routine `000865f4` when the canvas is the current one. `es2_xref.py` finds one call, from `000241f9`.

## The clickable areas

`cfg\hotspot.dat` holds, per chassis, one area per mount:

```
uint16   chassis count
chassis count x {
  uint16  area count
  area count x {
    uint16  x, y        -- a corner
    uint16  x, y        -- the opposite corner
  }
}
```

The retail file holds the eight chassis, each with its mount count of areas from `000803c8`.

`HercHotspots_Load` (`00022404`) reads the chassis count and for each chassis the area count, makes an array of that many 20-byte areas (`HercHotspots_AreaCtor`, `000226b4`, which zeroes all five dwords) in the dword at `0008003c` + 4 × chassis, with no bound against the table's eight entries, and reads each area's two corners, zero-extended, into a rect at `+0`: left the smaller x, top the smaller y, right the larger x, bottom the larger y. `HercHotspots_Free` deletes the arrays through `HercHotspots_AreaDtor` (`000227a5`), which destroys the object at `+0x10`, when there is one, through its vtable's `+0x1c` with 3, and frees the area when bit 0 of its second argument is set.

ES2's `gam\arm_hots.dat` holds the same kind of per-mount areas as `int32` corners in a fixed order with a chassis id per group ([ES2's clickable regions](../../retail/formats/herc-catalogs.md#gamarm_hotsdat-and-gamrpr_hotsdat--the-clickable-regions)); ES1's corners are `uint16`, put in order at load, and its groups carry no id.

## Files no loader names

`cfg\CONFIG.DAT`, 21,600 bytes, and `cfg\TEST.DAT`, 58 bytes, are in `SHELL0.VOL`. A byte search of `GO.EXE` and of `DBSIM.EXE` finds neither `config.dat` nor `test.dat`, where the same search of `GO.EXE` finds `hotspot.dat`, and neither file is among `cfg\filename.bin`'s fourteen names. `CONFIG.DAT` is 1,080 blocks of 20 bytes laid out as a mount picture record is in memory, an `int16` pair and four `int32`; its first nonzero blocks hold `ROAD.DAT`'s points. `TEST.DAT` opens `02 00 05 00` and does not walk as a layout file.

## Open

- **Open:** what the frame of a mount picture record selects and what `000216e0` does with the points; what the `+0x94` and `+0x98` elements draw; what `00023605` and `00024788` compute; and what `HercLayout_FindFirstPoint` serves, to which `es2_xref.py` finds no branch and no stored pointer.
- **Open:** which six component groups and which damage levels the mark lists are, through `Repair_LevelForCondition` (`000389fb`) and the external areas at `0007ef54`, and what the word `+0x38` of the canvas set to `0007fccc` does to the first of the two blits.
- **Open:** what reads the areas at `0008003c`: a search of the disassembly for the address finds only `HercHotspots_Load` and `HercHotspots_Free`.
- **Open:** what `CONFIG.DAT` and `TEST.DAT` are.
