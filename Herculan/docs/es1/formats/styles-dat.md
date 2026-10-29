# DBSIM.EXE and GO.EXE — `STYLES.DAT`, the widget styles

**Both programs load a small table of styles for their on-screen buttons and panels, alongside the fonts those widgets write in.** The file is a count and that many 64-byte records, which both loaders read whole; a record gives the colors and corner pieces of a bevelled frame. ES2's `SIMVOL0.VOL` carries the same 258 bytes as `dat\STYLES.DAT`, and a search of ES2's `DBSIM.EXE` and `VSHELL.EXE` for `styles` finds nothing. Addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)).

## The layout

```
int16 count
byte[0x40][count]
```

ES1 ships it three times with the same 258 bytes, four records: `SIMVOL0.VOL`'s `dat\STYLES.DAT`, `ZONES.VOL`'s `dat\STYLES.DAT` and `SHELL0.VOL`'s `cfg\STYLES.DAT`.

A record, as the simulator's `CornerFrame_Draw` (`00044954`) reads it ([drawing a frame from a style](../simulation/alert-panels.md#drawing-a-frame-from-a-style)):

| Offset | Holds |
|---|---|
| `+0` | the inside's color, `int32` |
| `+4`–`+0x10` | the outer ring's left, top, right and bottom edge colors, `int32`, 0 for no edge |
| `+0x14`–`+0x20` | the middle ring's, the same |
| `+0x24`–`+0x30` | the inner ring's, the same |
| `+0x34`, `+0x36`, `+0x38`, `+0x3a` | the `dba\corners` frames at the top left, top right, bottom left and bottom right, `int16` |
| `+0x3c`, `+0x3e` | a corner frame's width and height, `int16` |

In the retail file the thirteen colors of each record lie from 16 to 31, its corner frames are 4–7, 8–11, 12–15 and 16–19 in the four records, and every corner is 2 × 2. The simulator's panel buttons draw records 0, 1 and 2 for a button at rest, pressed and greyed ([the panel's buttons](../simulation/alert-panels.md#the-panels-buttons)).

## The loads

`WidgetStyles_Load` (`00044880`), given the path, opens it, reads the count into `000bd53c` and, when it is not 0, builds that many 64-byte records at `00094ccc` with `__vec_new` and `WidgetStyle_ElementCtor` (`00044910`, which allocates 64 bytes when given none and writes nothing) and reads them one at a time. It checks neither the stream nor the length, and frees no earlier table. A static exit routine at `0004492c` deletes the table and zeroes `00094ccc`. `ES1GO`'s `WidgetStyles_Load` (`00044aa4`) is the same code, the count at `ES1GO 000ab4ec` and the table at `ES1GO 00082654`, with `WidgetStyle_ElementCtor` at `ES1GO 00044b3f`; it closes the stream before destroying it.

- **The simulator.** `Widget_LoadStylesAndFonts` (`00045e10`), which `Sim_StartMission` calls (`0005abc2`), loads `dat\styles`, then `dfn\title`, `cpylw`, `active`, `pushed`, `inactive`, `green6x8` and `red6x8` through `CockpitFont_Load` (`00044d50`) into the dwords `WidgetFont_Title` (`000bd542`) to `WidgetFont_Red6x8` (`000bd55a`), and `dba\corners` through `CockpitCorners_Load` (`00044d88`) into `WidgetCorners` (`000bd53e`) ([the resource loaders](../simulation/alert-panels.md#the-resource-loaders-and-the-color-helpers)).
- **The shell.** `ES1GO`'s `Widget_LoadStylesAndFonts` (`00002818`) loads `dat\styles`, then `dfn\title`, `cpylw`, `active`, `pushed`, `inactive`, `nm_green` and `nm_red1` through `ES1GO 0004b757`, and `dba\corners` through `ES1GO 0004b7df`. `Shell_LoadData` (`ES1GO 00020630`), which runs before it, loads 13 fonts through `ES1GO 0004b757` into the 14 dwords `ES1GO 000aacc6`–`000aacfa`: `dfn\title`, `gray`, `active`, `pushed`, `inactive`, `nm_green`, `nm_red1`, then the `nm_green` handle again, `cstats_1`, `cstats_2`, `nm_white`, `nm_blue`, `nm_ylw` and `big_red`; `Shell_FreeData` (`ES1GO 00020ae3`) destroys each that is set through its slot `+0x1c` with 3 ([the shell's start-up](../shell/startup.md#the-shared-data)). Their caller, `Shell_Main` (`ES1GO 00012ec7`), then loads `CFG\styles.dat` with `WidgetStyles_Load` and `res32\corners.dba` with `ES1GO 0004b7df`. Of the shell's archives, `ZONES.VOL` holds `dat\STYLES.DAT` and `SHELL0.VOL` `cfg\STYLES.DAT`.

`es2_xref.py` finds the simulator's table pointer `00094ccc` read in `PanelButton_Ctor` (`000467c8`), which stores the addresses of records 0, 1 and 2 in the button, beside the loader and the exit routine, and `ES1GO`'s `00082654` at ten places in the shell.

## Open

- **Open:** `ES1GO 0004b757` and `ES1GO 0004b7df`, the shell's font and bitmap loaders, and the shell's ten readers of its table.
- **Open:** what `WidgetStyles_Load` leaves when its path is not in a mounted archive, as for the shell's second load if `CFG\styles.dat` were missing.
