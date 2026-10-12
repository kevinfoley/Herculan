# DBSIM.EXE — `dmg\<NAME>.DMG`

Each ES1 machine type's damage file gives its components and internals their armour, as ES2's does: ES1's simulator reads it with ES2's code and ES2's layout ([ES2's `.DMG`](../../retail/formats/dmg-damage-file.md)). Of the 18 files in `SIMVOL0.VOL`'s `dmg\`, 17 walk that layout to their last byte; `HOVTANK.DMG` does not, and the simulator's path to it ends the program before it opens the file. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The load

`Damage_LoadMechDmgFile` (`00009a2c`) follows ES2's: it opens the path with `VolRStream_CtorFromPath`, asserting at severity 5 (`damage.cpp` line `0x85`) when the stream status is set, takes the next 12-byte slot of the table at `000bc2b0` by the count word `00090dfe`, which it increments, reads the file into it with `HercPiece_LoadTable` (`00009930`), destroys the stream and returns the slot. `HercPiece_LoadTable` reads ES2's layout into ES2's slot: the `int16` internal count into `+0x0a` and that many `int16` maxima into a new array at `+0x04`, the `int16` piece count into `+0x08`, and that many 18-byte pieces into a new array at `+0x00`, each with `HercPiece_ReadRecord` (`00009890`).

Two functions call it, the two that push the `dmg` folder name (`000980e0`, at `000117e7` and `0001c73e`):

- `Mech_Constructor` (`00011330`), ES2's, builds a mech of a chassis type and loads `dmg\<name>` into `+0x190` and `col\<name>` into `+0x18c`, the name being `g_MechTypeNames` (`000bc440`) at the type record's `+0x6c`, the chassis' type number ([the chassis load](chassis-dat.md#the-load)). So it reads the sixteen `MECHS.NAM` names only. Its damage arrays are 21 internals by 28 components where ES2's are 22 by 29 (`00009b8c`'s arguments `0x15` and `0x1c`).
- `FlyerType_LoadResources` (`0001c6c8`) loads `dmg\<name>` for a flyer type ([the flyer load](bullets-skimmer-dat.md#the-load)), after `dat\<name>`.

## The files

| Files | Internals |
|---|---|
| `ACHILLES`, `APOLLO`, `BULLDOG`, `COLOSSUS`, `GUNSLING`, `HERCULES`, `MIRIMAC`, `MONGOOSE`, `PANTHER`, `PATRIOT`, `RAMSES`, `RAPTOR`, `ROADRUNN`, `SCORPION`, `TOMAHAWK` | 21 |
| `SPIDER` | 12 |
| `SKIMMER` | 1 |

These are the sixteen chassis `MECHS.NAM` names and the two flyer types `FLYERS.NAM` names, `SKIMMER` and `HOVTANK`.

## `HOVTANK.DMG`

28 bytes that do not fit the layout: read by it, they hold three internals (0, −300, 0) and a piece count of 150, where 16 bytes remain. The flyer load for type 1 reaches `dmg\HOVTANK` only after reading `dat\HOVTANK` with `Ovl_ReadFile`, whose failed open is a severity-5 assert, fatal unless the debugger flag `000b5bcc` is set ([severities](../simulation/asserts.md#severities)); no ES1 volume holds `dat\HOVTANK`, so a mission placing a `HOVTANK` ends the program there.

Read as words, the bytes are an `int16` 3, six Q16 values (−300, 150, 0, 150, 300, 150) and an `int16` 1500:

```
03 00 | 00 00 d4 fe | 00 00 96 00 | 00 00 00 00 | 00 00 96 00 | 00 00 2c 01 | 00 00 96 00 | dc 05
```

## Open

- **Open:** what `HOVTANK.DMG`'s bytes are. `es2_xref` finds two calls of `Damage_LoadMechDmgFile` and one of `HercPiece_LoadTable`, both on the paths above, and no reference to `dmg` other than those two pushes; a reader of the file in another layout was not found.
