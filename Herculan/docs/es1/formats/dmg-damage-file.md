# DBSIM.EXE — `dmg\<NAME>.DMG`

Each ES1 machine type's damage file gives its components and internals their armour, as ES2's does: ES1's simulator reads it with ES2's code and ES2's layout ([ES2's `.DMG`](../../retail/formats/dmg-damage-file.md)). Of the 18 files in `SIMVOL0.VOL`'s `dmg\`, 17 walk that layout to their last byte; `HOVTANK.DMG` does not. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The load

`Damage_LoadMechDmgFile` (`00009a2c`) follows ES2's: it opens the path with `VolRStream_CtorFromPath`, asserting at severity 5 (`damage.cpp` line `0x85`) when the stream status is set, takes the next 12-byte slot of the table at `000bc2b0` by the count word `00090dfe`, which it increments, reads the file into it with `HercPiece_LoadTable` (`00009930`), destroys the stream and returns the slot. `HercPiece_LoadTable` reads ES2's layout into ES2's slot: the `int16` internal count into `+0x0a` and that many `int16` maxima into a new array at `+0x04`, the `int16` piece count into `+0x08`, and that many 18-byte pieces into a new array at `+0x00`, each with `HercPiece_ReadRecord` (`00009890`). `FlyerType_LoadResources` (`0001c6c8`) passes it `dmg\<name>` ([the flyer load](bullets-skimmer-dat.md#the-load)), and `00011330` pushes the same `dmg` folder name at `000117e7`.

## The files

| Files | Internals |
|---|---|
| `ACHILLES`, `APOLLO`, `BULLDOG`, `COLOSSUS`, `GUNSLING`, `HERCULES`, `MIRIMAC`, `MONGOOSE`, `PANTHER`, `PATRIOT`, `RAMSES`, `RAPTOR`, `ROADRUNN`, `SCORPION`, `TOMAHAWK` | 21 |
| `SPIDER` | 12 |
| `SKIMMER` | 1 |

These are the sixteen chassis `MECHS.NAM` names and the two flyer types `FLYERS.NAM` names, `SKIMMER` and `HOVTANK`.

`HOVTANK.DMG` is 28 bytes. Read by the layout, it holds three internals (0, −300, 0) and a piece count of 150, and the 16 bytes after the count are two 8-byte piece heads, the second with a dependent count of 1500: `HercPiece_LoadTable` would read 150 pieces where 16 bytes remain. The flyer load reads `dmg\HOVTANK` only after `dat\HOVTANK`, which no ES1 volume holds ([the flyer load](bullets-skimmer-dat.md#the-load)).

## Open

- **Open:** what `HOVTANK.DMG`'s bytes are. After an `int16` 3 they read as six Q16 values, −300, 150, 0, 150, 300 and 150, and an `int16` 1500.
- **Open:** what `00011330` loads from `dmg\`, and which types it loads for.
