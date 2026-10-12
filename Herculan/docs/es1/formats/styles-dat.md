# DBSIM.EXE and GO.EXE — `STYLES.DAT`, the widget styles

**Both programs load a small table of styles for their on-screen buttons and panels, alongside the fonts those widgets write in.** The file is a count and that many 64-byte records, which both loaders read whole; what the bytes of a record mean is [Open](#open). ES2's `SIMVOL0.VOL` carries the same 258 bytes as `dat\STYLES.DAT`, and a search of ES2's `DBSIM.EXE` and `VSHELL.EXE` for `styles` finds nothing. Addresses are `ES1DBSIM` image addresses unless `ES1GO` is named ([the X-32 executable](x32-executable.md#the-image)).

## The layout

```
int16 count
byte[0x40][count]
```

ES1 ships it three times with the same 258 bytes, four records: `SIMVOL0.VOL`'s `dat\STYLES.DAT`, `ZONES.VOL`'s `dat\STYLES.DAT` and `SHELL0.VOL`'s `cfg\STYLES.DAT`.

## The loads

`WidgetStyles_Load` (`00044880`), given the path, opens it, reads the count into `000bd53c` and, when it is not 0, builds that many 64-byte records at `00094ccc` with `__vec_new` and `WidgetStyle_ElementCtor` (`00044910`, which allocates 64 bytes when given none and writes nothing) and reads them one at a time. It checks neither the stream nor the length, and frees no earlier table. A static exit routine at `0004492c` deletes the table and zeroes `00094ccc`. `ES1GO`'s `WidgetStyles_Load` (`00044aa4`) is the same code, the count at `ES1GO 000ab4ec` and the table at `ES1GO 00082654`, with `WidgetStyle_ElementCtor` at `ES1GO 00044b3f`; it closes the stream before destroying it.

- **The simulator.** `Widget_LoadStylesAndFonts` (`00045e10`), which `Sim_StartMission` calls (`0005abc2`), loads `dat\styles`, then `dfn\title`, `cpylw`, `active`, `pushed`, `inactive`, `green6x8` and `red6x8` through `00044d50` into the dwords `000bd542` to `000bd55a`, and `dba\corners` through `00044d88` into `000bd53e`.
- **The shell.** `ES1GO`'s `Widget_LoadStylesAndFonts` (`00002818`) loads `dat\styles`, then `dfn\title`, `cpylw`, `active`, `pushed`, `inactive`, `nm_green` and `nm_red1` through `ES1GO 0004b757`, and `dba\corners` through `ES1GO 0004b7df`. Its caller, `ES1GO 00012ec7`, then loads `CFG\styles.dat` with `WidgetStyles_Load` and `res32\corners.dba` with `ES1GO 0004b7df`. Of the shell's archives, `ZONES.VOL` holds `dat\STYLES.DAT` and `SHELL0.VOL` `cfg\STYLES.DAT`.

`es2_xref.py` finds the simulator's table pointer `00094ccc` read in `000467c8` (the addresses of records 0, 1 and 2 stored at `+0x3c`, `+0x40` and `+0x44` of the object it builds) beside the loader and the exit routine, and `ES1GO`'s `00082654` at ten places in the shell.

## Open

- **Open:** the fields of a style record. Read as `int16`, each of the four is thirteen values from 16 to 31 with a zero after each, then four consecutive values (4–7, 8–11, 12–15, 16–19) and two 2s, which look like palette indices and font or bitmap numbers; no reader of the record's bytes has been read.
- **Open:** `00044d50`, `00044d88`, `ES1GO 0004b757` and `ES1GO 0004b7df`, the font and bitmap loaders, and what `000467c8` builds.
- **Open:** what `WidgetStyles_Load` leaves when its path is not in a mounted archive, as for the shell's second load if `CFG\styles.dat` were missing.
