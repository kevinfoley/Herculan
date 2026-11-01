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

The art banks are `RES32\<name><kind>.DBA`, and their names differ from the `cfg\` files': `WEP1`, `OUTL` and `WEPD` (the path tables `000800f4`, `00080264` and `000801ac`) take `ROAD`, `GUNS`, `RAPT`, `TOMA`, `PATR`, `PNTH`, `HERC` and `COLL` for chassis 0 to 7; `DMG1` (`0008031c`) `ROAD`, `GUNN`, `RAPT`, `TOMA`, `PATR`, `PNTH`, `HERC` and `COLO`; and `BILD` (`000803b0`, chassis 2 to 7 only) `RAPT`, `TOMA`, `PATR`, `PNTH`, `HERC` and `COLO`.

`Shell_LoadData` (`00020630`) calls `HercLayout_Load`, `HercDamageMarks_Load`, `LoadDamageDat_Shell` ([the value tables](herc-catalogs.md#the-load)), `HercHotspots_Load` and `HercArt_LoadAllBanks` in that order, from `000208ce` to `000208eb`. `Shell_FreeData` (`00020ae3`) frees the three tables with `HercLayout_Free` (`00021fbc`), `HercDamageMarks_Free` (`000223a1`) and `HercHotspots_Free` (`000227e4`), and the banks with `HercArt_FreeAllBanks` (`00022981`), which `Hangar_Clear` also calls: `HercArt_FreeChassisBanks` (`000228aa`)(chassis) for each of the eight chassis, which destroys the chassis' `WEP1`, `OUTL`, `DMG1`, `WEPD` and `BILD` banks that are loaded (`GLBitmapArray_Dtor` with 3) and nulls their pointers. After that each bank loads again the first time it is needed: `Herc_BuildDiagram` loads `WEP1` and `OUTL`, `Herc_SetBodyPicture` `BILD`, `MountPicture_Set` `WEPD` and `Herc_DrawDamageMarks` `DMG1`; `MountPicture_Set`'s test of `WEP1` follows a build, which has loaded it.

## Where the code lies

The diagrams' code is two object files.

- **The sprites' file**, `00021334` to `00021d0b`: `ShellBackdrop_SetPicture` and `ShellBackdrop_Draw` ([the backdrop](../shell/menus.md#the-backdrop)), the 34-byte sprite ([sprites](#sprites)) and the file's copies of the region destructors, `GLRectangleRegion_DtorSpriteCopy` (`000215b8`), `GLRegion_DtorSpriteCopy` (`00021623`) and `GLBase_DtorSpriteCopy` (`0002166c`). The `RET 4` at `0002132f` before it is followed by two zero bytes, and its last function's `RET 4` at `00021d08` by one.
- **The HERC's file**, `00021d0c` to `0002562b`: the three tables' loads, the art banks, `PlayerMec_Write` ([the player file](career-log-and-player-mec.md#the-player-file)), the HERC class ([the HERC record](car-hercs-and-stock.md#the-herc-record)) with its diagram, and `GLBase_DtorHercCopy` (`000231c4`), `GLRegion_DtorHercCopy` (`000231f0`) and `GLRectangleRegion_DtorHercCopy` (`00023239`). It ends as [the compiler's files](compiler.md#where-object-files-meet-in-the-code) with a static initializer do: `Herc_StaticInit` (`00025090`), the static constructor table's entry `0009a254`, then its exit routine `Herc_StaticExit` (`00025609`, a bare `ret`, the exit table's `0009a298`) and the point constructor `Point_DefaultCtorHercCopy` (`0002560e`) that the initializer's `__vec_ctor` calls; that one's `RET 4` at `00025626` is followed by three zero bytes and `ImDevice_LoadBnd` (`0002562c`). `Herc_StaticInit` fills the body and building positions ([the body](#the-body)).

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

Four lookups take a weapon, a mount and a chassis, with no bounds test on the mount or the chassis, and search the mount's records for the first whose weapon matches: `HercLayout_Find` (`00022024`) returns the record, or null; `HercLayout_FindFrame` (`00022086`) its frame, or −1; `HercLayout_FindFirstPoint` (`000220ec`) and `HercLayout_FindSecondPoint` (`0002219f`) fill a point with the record's first or second point, or (0, 0). A record's frame is the mount's outline in the chassis' `OUTL` bank and its weapon's picture in the `WEP1` bank, one less in the `WEPD` bank; its first point places the picture and its second the outline ([the HERC's diagram](#the-hercs-diagram)).

ES2's `gam\arm_*.dat` places a mount's picture from a group per weapon id with a part id per mount ([ES2's armory layout](../../retail/formats/herc-catalogs.md#gamarm_dat--armory-layout)); ES1's file groups by mount and keys each record by weapon, has no leading records, holds `int16` coordinates, and is read with no inset.

## The HERC's diagram

**A HERC's picture is a stack of sprites: the body, then each mount's weapon, the outline of the mount being looked at, and on the Colossus an extra piece over the shoulders.** The HERC holds them from `+0x72` to `+0xa7`:

| Offset | Holds |
|---|---|
| `+0x70` | `int16` the current mount, −1 for none |
| `+0x72` | the body, a sprite ([the body](#the-body)) |
| `+0x94` | an array of a sprite per mount: the mount's outline, `OUTL` frame (the layout record's frame) at the record's second point |
| `+0x98` | on chassis 7, a sprite: `WEP1` frame 43 at (86, 25) while mount 2 or 3 holds a weapon; null on the others |
| `+0x9c`, `+0xa0` | the chassis' `RES32\<chassis>WEP1.DBA` and `<chassis>OUTL.DBA` banks |
| `+0xa4` | an array of a 38-byte mount picture per mount: the layout record at `+0`, the weapon's picture, a sprite, at `+4` |

`Herc_BuildDiagram` (`00023883`)(HERC, remaining) builds them. `es2_xref.py` finds seven calls: from `Herc_Read`, when a HERC list is read ([the HERC lists](car-hercs-and-stock.md#the-herc-lists)), and from `Herc_DrawInBay` (`000241f9`), `00028e50`, `00029d0f`, `Hangar_BuildIntoSelected` (`00029d68`) and twice `Hangar_AdvanceMarkedHerc` (`00029e01`). `Hangar_AdvanceMarkedHerc` passes as *remaining* the marked slot's word `Hangar_MarkedMissionsLeft` (`00080898`), the count of missions the marked HERC still needs ([the marked HERC](../shell/between-missions.md#the-marked-herc)); `Herc_DrawInBay` passes that word for the marked HERC and 0 for any other. It sets `+0x70` to −1, clears `Herc_MountHighlightShown` (`0007ff98`), takes the two banks into `+0x9c` and `+0xa0`, loading each the first time a chassis needs it into the tables at `0007ff9c` and `0007ffbc`, and sets the body with `Herc_SetBodyPicture`(HERC, remaining). For chassis 7 it makes a sprite at `+0x98` (`Sprite_Ctor`, `00023f2c`); for the others `+0x98` is null. When the HERC's mount count `+2` is not 0 it makes the mount arrays, `+0xa4` with `MountPicture_Ctor` (`00023d70`), which zeroes the record and builds the sprite as `Sprite_Ctor` does, and `+0x94` with `Sprite_Ctor`; with a count of 0 the arrays an earlier build made stay. It frees neither the arrays nor the `+0x98` sprite it replaces, so a build with no `Herc_FreeDiagram` before it leaves the old ones allocated. Each mount's steps touch only that mount:

1. when the mount's status word is 0 (`Herc_GetMountStatus`, `00024b23`, [the HERC record](car-hercs-and-stock.md#the-herc-record)), makes the mount empty (weapon 17) and sets the word to 100 (`Herc_SetStatus`(HERC, 100, mount, `0x786`), `00024c14`);
2. sets the mount's outline sprite with `Sprite_Set`, *keep* 0, to the picture of the `OUTL` bank `+0xa0` (its array `+8`) at the frame `HercLayout_FindFrame` gives, at the point `HercLayout_FindSecondPoint` gives; for a weapon the mount's records lack, the frame −1 reads the dword before the array, with no test;
3. sets the mount's picture with `MountPicture_Set` (`00022d78`), giving it the mount's status word;
4. on chassis 7, when mount 2 or 3 is not empty, sets the `+0x98` sprite to `WEP1` frame 43 at (`0x56`, `0x19`) and sets the byte `Herc_ColossusTopShown` (`0007ff8e`);
5. sets to 0 the mount's ammunition word `+0x18` when its weapon is 9, 10 or 11 and the word is 5.

Last, when the `+0x98` sprite exists and `Herc_ColossusTopShown` is clear, it sets it to no bitmap at (0, 0). `Herc_BuildDiagram` never clears the byte, and only `Herc_SetCurrentMountWeapon` does ([the mount highlight](#the-mount-highlight)): once a Colossus with a shoulder weapon has been built, every later build of any HERC leaves the byte set, and a later Colossus with mounts 2 and 3 empty keeps its new `+0x98` sprite with no bitmap.

`Herc_FreeDiagram` (`0002330e`), given a HERC, deletes the `+0xa4` array (`__vec_delete`, `0005547c`, with `MountPicture_Dtor` (`000232df`), which destroys the sprite with `Sprite_Dtor`(sprite, 2)) and the `+0x94` array (with `Sprite_Dtor` (`00021519`) for each), destroys the `+0x98` sprite with `Sprite_Dtor`(sprite, 3), nulling each pointer it frees, and, when the byte `Shell_LowMemory` (`0007fd09`) is set, frees the chassis' banks with `HercArt_FreeChassisBanks` and nulls `+0x9c` and `+0xa0`. `es2_xref.py` finds three calls: from `Herc_Dtor` (`000232a4`), which [`Hangar_Clear`](car-hercs-and-stock.md#the-herc-lists) destroys each HERC with, `Herc_EraseFromBay` (`000241cd`), and `Hangar_ReadStatusBlock` (`00028e50`), which frees and rebuilds a HERC's diagram around the status block it reads ([the HERC record](car-hercs-and-stock.md#the-herc-record)).

`MountPicture_Set`, given an element, a value, a weapon, a mount and a chassis, stores the record `HercLayout_Find` returns at the element's `+0` and sets the element's sprite at the record's first point: for weapon 17 to no bitmap at (0, 0); for any other weapon, when the value is below `0x33`, to frame (the record's frame − 1) of the chassis's `RES32\<chassis>WEPD.DBA`, which it loads into the table at `0007fffc` the first time; at `0x33` or more to the record's frame of the `WEP1` bank, only when that bank is loaded. So a weapon in a mount whose condition is 50 or below shows its damaged picture. The picture is the bank's array `+8` at that frame, with no bounds test; the point is the record's first point (`+4`, `+8`), and *keep* is 0. For a weapon the mount's records lack, `HercLayout_Find` gives null and the weapon-other-than-17 paths read the frame through it. `MountPicture_SetBitmap` (`00022cb5`)(element, value, chassis) chooses the bitmap the same way from the record already stored and sets it without moving the sprite, and `MountPicture_Clear` (`00022c51`)(element) nulls the record and sets the sprite to no bitmap at (0, 0); `es2_xref.py` finds no branch to either and no stored pointer. `HercArt_LoadAllBanks` (`0002280f`) loads all eight chassis' `WEP1`, `OUTL`, `DMG1` and `WEPD` banks into their tables, and the `BILD` banks of chassis 2 to 7 into the table at `0008001c`.

`es2_xref.py` finds `HercLayout_FindFrame`, `HercLayout_FindSecondPoint` and `MountPicture_Set` called from `Herc_BuildDiagram` and `Herc_SetCurrentMountWeapon` (`00024788`), `HercLayout_Find` from `MountPicture_Set`, and no branch to and no stored pointer of `HercLayout_FindFirstPoint`.

### The body

`Herc_SetBodyPicture` (`00023605`)(HERC, remaining) sets the body sprite `+0x72` by the chassis and *remaining*: the finished body, `WEP1` frame 0 at the chassis' point of `Herc_BodyPositions` (`000a0398`), for *remaining* 0; a building frame of the chassis' `RES32\<chassis>BILD.DBA`, loaded into the table at `0008001c` the first time, at the matching point of `Herc_BuildPositions` (`000a03d8`); or no bitmap at the body's point. `Herc_StaticInit` fills both tables; a building point is the `int32` pair at `000a03d8` + 16 × chassis + 8 × (*remaining* − 1).

| Chassis | Body point | *remaining* and picture | Building points |
|---|---|---|---|
| 0, 1 | (77, 99), (77, 89) | 0 the body; any other none | |
| 2, 3, 4 | (83, 64), (86, 53), (75, 57) | 0 the body; 1 `BILD` frame 0; 2 none | (84, 97), (86, 94), (84, 92) |
| 5, 6, 7 | (73, 37), (72, 37), (58, 26) | 0 the body; 1 `BILD` frame 0; 2 `BILD` frame 1; 3 none | (78, 50) and (78, 85), (78, 51) and (78, 91), (74, 41) and (74, 82) |

Frame 0 takes the chassis' first building point and frame 1 its second. A chassis 2 to 7 *remaining* other than those listed leaves the sprite as it was. The counts match the chassis' words of `Chassis_BuildMissions` (`000808d8`), 1, 1, 2, 2, 2, 3, 3 and 3, that `Hangar_MarkedHercProgress` divides by ([the marked HERC](../shell/between-missions.md#the-marked-herc)).

## Sprites

**A sprite is a bitmap at a point that saves what is under it when drawn and puts it back when erased.** It is 34 bytes:

| Offset | Holds |
|---|---|
| `+0` | byte, set once drawn |
| `+1` | byte, how the background is kept: 0 in a buffer of its own, any other on the page `0007fcce` |
| `+2` | the bitmap, or null |
| `+6` | a `GLRectangleRegion` (its vptr, then the region at `+0xa`), its rect left `+0xe`, top `+0x12`, right `+0x16`, bottom `+0x1a` |
| `+0x1e` | the saved background buffer, or null |

`Sprite_Ctor` (`00023f2c`)(sprite), allocating 34 bytes for a null one, builds the region with a zero rect and zeroes the bitmap, the buffer and the drawn byte. `Sprite_Set` (`000216e0`)(sprite, keep, x, y, bitmap) stores the bitmap and *keep* at `+1`, the rect's corner (*x*, *y*), frees the buffer, and with a bitmap sets the far corner to (*x* + the bitmap's width `+6`, *y* + its height `+4`) and, for *keep* 0, allocates a buffer of `Region_PixelCount` bytes; with none it writes 0 to the right `+0x16` twice and leaves the bottom. It clears the drawn byte. The raster's rects are inclusive (`Rect_PixelCount` (`00050640`) counts (*x1* − *x0* + 1) × (*y1* − *y0* + 1)), so the region a sprite saves, restores and copies is one column and one row larger than its bitmap, while its dirty box is the bitmap's own size. `Sprite_SetBitmap` (`000216c7`)(sprite, bitmap) stores the bitmap alone and clears the drawn byte.

`Sprite_Draw` (`000217a9`)(sprite), with a bitmap, on the canvas `000ab616` with its source page `+0x34` set to page 2 (`0007fccc`): for *keep* 0 saves the region of page 2 into the buffer (`Region_Save`); otherwise sets the canvas's draw page `+0x38` to the page `0007fcce` (3 in the image, where `0007fccc` holds 2) and copies the region from page 2 to it (`Region_CopyBetweenPages`). It then blits the bitmap at the corner on page 2, copies the region from page 2 to the draw page the canvas held, adds a dirty box of delay 0 of the bitmap's size at the corner ([dirty boxes](../rendering/screen-pages.md#dirty-boxes)), puts the source page back and sets the drawn byte. `Sprite_Erase` (`00021a6e`)(sprite), with a bitmap and the drawn byte set, puts the background back on page 2, from the buffer with `Region_Restore` for *keep* 0 and from page `0007fcce` otherwise, copies the region to the draw page and adds the same dirty box; it leaves the drawn byte set. `Sprite_Dtor` (`00021519`)(sprite, flags) frees the buffer and clears the drawn byte (`Sprite_FreeBackground`, `00021698`), runs the region's destructors and frees the sprite when bit 0 of *flags* is set. The diagram's sprites are all made with *keep* 0. `MountPicture_Erase` (`00022ea8`)(element) erases the element's sprite.

## Drawing the diagram

`Herc_DrawMounts` (`00024670`)(HERC) goes through as many places as the HERC's own mount count `+2`, not the chassis' count from `000803c8`, in the order `Herc_MountDrawOrder` (`000803d8`) gives, nine `int16` per chassis, so a place past the chassis' mounts reads the row's 0; for each it draws the mount's picture (`Sprite_Draw` of the `+0xa4` element's `+4`); then, while `Herc_MountHighlightShown` is set and the mount is the current mount `+0x70`, its outline from `+0x94`; then, on chassis 7 with `Herc_ColossusTopShown` set and the mount 3, the `+0x98` sprite. `Herc_EraseMounts` (`00024561`)(HERC) erases the same sprites in the reverse order, each mount's `+0x98` and outline before its picture.

| Chassis | Mount order |
|---|---|
| 0, 1 | 1, 2, 0 |
| 2 | 3, 2, 1, 0 |
| 3 | 4, 3, 2, 1, 0 |
| 4 | 5, 2, 4, 1, 3, 0 |
| 5 | 1, 6, 4, 3, 5, 2, 0 |
| 6 | 1, 6, 4, 3, 7, 5, 2, 0 |
| 7 | 1, 8, 3, 6, 4, 2, 5, 7, 0 |

The body and the damage marks are drawn under the mounts ([drawing the HERC into its bay](../shell/hangar.md#drawing-the-herc-into-its-bay)).

### The mount highlight

**The outline of one mount is drawn over the weapons to show which mount is being looked at.** `Herc_HighlightMount` (`00024326`)(HERC, mount) erases the mounts, makes the mount current (`+0x70`), sets `Herc_MountHighlightShown` when the mount is not −1 and clears it otherwise, and draws the mounts. `Herc_HighlightNextMount` (`000242a6`) and `Herc_HighlightPreviousMount` (`000242e8`), given a HERC, do the same with the current mount plus 1 modulo the mount count, which divides by 0 for a HERC with no mounts, or less 1, below 0 to the last (from −1 too), and set the flag; `Herc_HideMountHighlight` (`00024287`) clears the flag and keeps the current mount. `es2_xref.py` finds one call of each, from `Hangar_SelectMount` (`00029a14`), `Hangar_ClearSelectedMount` (`0002961c`), `Hangar_NextMount` (`00029a52`) and `Hangar_PrevMount` (`00029a87`) in turn ([`arming.md`](../shell/arming.md)).

`Herc_SetCurrentMountWeapon` (`00024788`)(HERC, weapon), for a current mount other than −1, gives the mount the weapon, or 17 when its status word is 0 (`Herc_CurrentMountStatus`, `00024b51`, `Herc_GetMountStatus` of `+0x70`), and sets its outline and its picture as `Herc_BuildDiagram` does, both looked up by the weapon it was given. When the frame `HercLayout_FindFrame` found for that weapon is not 0, −1 for a weapon the records lack included, on chassis 7 with mount 2 or 3 current, it sets the `+0x98` sprite to `WEP1` frame 43 at (86, 25) and sets `Herc_ColossusTopShown` when the flag was clear, or, with the flag set and the other of mounts 2 and 3 holding weapon 0, sets the sprite to no bitmap at (0, 0) and clears the flag. Last it sets the mount's ammunition word `+0x18` to 5 for a weapon outside 9 to 11, and to 0 for one of them whose word is 5. `es2_xref.py` finds calls from `Herc_ReadOverDiagram` (`000240bc`) and `Hangar_FitWeapon` (`00029abc`).

`Herc_ReadOverDiagram`(HERC, stream) reads through the stream's slot `+0x1c` two `int16` it does not keep, then for each mount of the HERC's count `+2` an `int16` weapon, which it gives the mount as the current one with `Herc_SetCurrentMountWeapon`, putting `+0x70` back afterwards, then the mount count's `int16` words into `+0x18`, an `int16` into `+0x2c` and 66 bytes into the status block `+0x2e`, as `Herc_ReadFromStream` (`0002352f`) does; `es2_xref.py` finds no branch to it and no stored pointer.

Three setters change one field: `Herc_SetCurrentMountAmmo` (`00024a1e`)(HERC, word) the current mount's word `+0x18`, `Herc_SetCurrentMountStatus` (`00024aea`)(HERC, word) its status word in the block's per-mount words at `+0x5c`, and `Herc_SetWord2C` (`00024a57`)(HERC, word) `+0x2c`; the first two do nothing when `+0x70` is −1. `es2_xref.py` finds `Herc_SetCurrentMountAmmo` called from `Arming_SelectMissileType` (`0002e47f`), `Herc_SetCurrentMountStatus` from `Hangar_FitWeapon` (`00029abc`), and no branch to `Herc_SetWord2C` and no stored pointer.

## The status lines

**The lists of a HERC's parts with their condition, such as `100% LFT LEG SERVO`, are built from `eng\watext.bin` lines whose first three characters are overwritten with the condition.** `Herc_BuildStatusLines` (`00024d99`)(HERC, kind) takes, for kind `0x781` or `0x784`, the lines of the list `0007ff6c`, `0x80` to `0x88`, for `0x785` those of `0007ff80`, `0x89` to `0x8e` (`WaText_LinesFromList`, [the build](../shell/menus.md#opening-a-menu--menu_open-0003969e)), and for `0x786` a new array of one 16-byte line per mount in the buffer `000a0308`, the array at `000a0304`: line `0xa0`, `---- NONE`, for weapon 17 and line `0x8f` + weapon otherwise. It fills them with `Herc_FormatStatusLines` (`00024f9e`)(HERC, lines, kind), which writes `%3d` of the condition over the first three characters of each line *i* that does not start with `-`: `Herc_GetStatus`(HERC, *i*, `0x784`) for `0x781` and `0x784`, (HERC, *i*, `0x785`) for `0x785`, `Herc_GetMountStatus`(HERC, *i*) for `0x786`. The three format strings, `000806e0`, `000806e4` and `000806e8`, are each `%3d`. The lines of kinds `0x781`, `0x784` and `0x785` are `Text_GetString`'s pointers into `eng\watext.bin`'s own buffers, so the condition written over their first three characters stays in the shell's text until the next format; the `0x786` lines are copied into their 16-byte slots with no length test; and any other kind leaves the lines pointer an uninitialized local that the format is given. `Herc_FormatStatusLine` (`00024ef4`)(HERC, *i*, line, kind) does the same to one line. So the internal words 0 to 8 are labelled LFT LEG SERVO, RGT LEG SERVO, LFT TRUNK SERVO, RGT TRUNK SERVO, SENSORS, ENGINE, HYDRAULICS, GYROS and LIFE SUPPORT, and the external areas 0 to 5 COCKPIT, LEFT TRUNK, RIGHT TRUNK, CHASSIS, LEFT LEG and RIGHT LEG ([the HERC record](car-hercs-and-stock.md#the-herc-record)). `es2_xref.py` finds `Herc_BuildStatusLines` called from `Hangar_SelectedComponentLines` (`000377cd`) and `Herc_FormatStatusLine` from `Hangar_FormatSelectedStatusLine` (`00030478`) ([Open](#open)).

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

`Herc_DrawDamageMarks` (`00024366`), given a HERC, loads its chassis's `DMG1` bank into the table at `0007ffdc` the first time, and for each group *g* from 0 to 5 takes the level `Repair_LevelForCondition` (`000389fb`) returns for the mean of external area *g*, `Herc_GetStatus` (`00024b86`)(HERC, *g*, `0x785`) ([the HERC record](car-hercs-and-stock.md#the-herc-record)). So the groups are the external areas the status lines label COCKPIT, LEFT TRUNK, RIGHT TRUNK, CHASSIS, LEFT LEG and RIGHT LEG ([the status lines](#the-status-lines)). For a level of 1 to 3 (`Repair_LevelForCondition` answers 0 to 4, and levels 0 and 4 draw nothing) it draws each mark of the group's list for that level: `Bitmap_Blit` of the bank's frame at (*x*, *y*), the mark's two `int32` at `+2` and `+6`, once with the canvas `000ab616`'s draw page `+0x38` set to page 2 (`0007fccc`) and once with it put back, each time also through the routine `000865f4` when the canvas is the current one. So each mark is drawn on page 2, the background the sprites save and restore, and on the page the display was drawing to ([sprites](#sprites)). `es2_xref.py` finds one call, from `Herc_DrawInBay` (`000241f9`).

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

`HercHotspots_Load` (`00022404`) reads the chassis count and for each chassis the area count, makes an array of that many 20-byte areas (`HercHotspots_AreaCtor`, `000226b4`, which zeroes all five dwords) in the dword at `0008003c` + 4 × chassis, with no bound against the table's eight entries, and reads each area's two corners, zero-extended, into a rect at `+0`: left the smaller x, top the smaller y, right the larger x, bottom the larger y. A press in the hangar's screens reads them: `ShellMouse_OnLeftPress` (`0001c7d8`), for a press outside the active window, tests the point against the areas of the selected HERC's chassis through the dword `0007fce4`, which the cases of screens 1800, 1900, 1920 and 2000 set to `0008003c`, and posts the area's index with 1702 ([the mouse](../shell/menu-input.md#the-mouse)). `HercHotspots_Free` deletes the arrays through `HercHotspots_AreaDtor` (`000227a5`), which destroys the object at `+0x10`, when there is one, through its vtable's `+0x1c` with 3, and frees the area when bit 0 of its second argument is set.

ES2's `gam\arm_hots.dat` holds the same kind of per-mount areas as `int32` corners in a fixed order with a chassis id per group ([ES2's clickable regions](../../retail/formats/herc-catalogs.md#gamarm_hotsdat-and-gamrpr_hotsdat--the-clickable-regions)); ES1's corners are `uint16`, put in order at load, and its groups carry no id.

## Files no loader names

`cfg\CONFIG.DAT`, 21,600 bytes, and `cfg\TEST.DAT`, 58 bytes, are in `SHELL0.VOL`. A byte search of `GO.EXE` and of `DBSIM.EXE` finds neither `config.dat` nor `test.dat`, where the same search of `GO.EXE` finds `hotspot.dat`, and neither file is among `cfg\filename.bin`'s fourteen names. `CONFIG.DAT` is 1,080 blocks of 20 bytes laid out as a mount picture record is in memory, an `int16` pair and four `int32`; its first nonzero blocks hold `ROAD.DAT`'s points. `TEST.DAT` opens `02 00 05 00` and does not walk as a layout file.

## Open

- **Open:** what `HercLayout_FindFirstPoint` serves, to which `es2_xref.py` finds no branch and no stored pointer; and what reads `MountPicture_SetBitmap` (`00022cb5`), `MountPicture_Clear` (`00022c51`), `Herc_ReadOverDiagram` (`000240bc`) and `Herc_SetWord2C` (`00024a57`), for which it finds none either.
- **Open:** what the page `0007fcce` holds that a sprite with *keep* other than 0 saves its background on.
- **Open:** whether `Herc_SetCurrentMountWeapon`'s test of weapon 0 on the Colossus' other shoulder mount, rather than 17, the empty mount, leaves the `+0x98` piece drawn over an emptied pair of mounts: a retail check, on the ARMING screen, emptying mounts 2 and 3 of a Colossus.
- **Open:** which screens show the status lines: `Herc_BuildStatusLines` is called from `Hangar_SelectedComponentLines` (`000377cd`) and `Herc_FormatStatusLine` from `Hangar_FormatSelectedStatusLine` (`00030478`).
- **Open:** what `CONFIG.DAT` and `TEST.DAT` are.
