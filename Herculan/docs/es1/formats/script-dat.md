# GO.EXE and DBSIM.EXE — data\script.dat and data\mission.var

ES1's shell hands a mission to the simulator in two files, as ES2's does ([ES2's `script.dat`](../../retail/formats/script-dat.md)): `data\script.dat`, the mission's surviving records with the fields the simulator needs, and `data\mission.var`, the campaign flags. The shell writes both when it loads a mission ([loading a mission](msn-mission-file.md#loading-a-mission)) and the simulator reads both when it loads the world. ES1's `script.dat` has nine blocks where ES2's has thirteen, and there is no `data\mission.str`. Addresses are `ES1GO`'s for the writer and `ES1DBSIM`'s for the reader.

## Writing

`WriteScriptDatFile` (`ES1GO 0003445b`) writes to the `FileRWStream` the mission load opened before reading the rows. That stream opens without truncating ([streams](streams.md#the-file-classes)), so a mission shorter than the one written before it leaves that one's tail in the file, as ES2's does.

The header is ten `int16`, the [header words](msn-mission-file.md#header-patches) at `000ab21a`–`000ab22c`, except that offset 4 holds the load's mode (`000aad56`) in place of word 2.

Nine blocks follow, each an `int16` count of the row's surviving records whose GUID is not `-1` and then those records' fields, refs resolved ([the layout](msn-mission-file.md#the-layout)):

| Block | `.MSN` row | Fields, by `.MSN` offset | Bytes |
|---|---|---|---|
| 1 | points | X, Y, Z (`0x0a`–`0x15`) | 12 |
| 2 | headings | `0x08` | 2 |
| 3 | waypoint groups | the waypoint count, then each waypoint | 2 + 2 per waypoint |
| 4 | trigger areas | `0x06`, `0x08`, `0x0a` | 6 |
| 5 | actions | `0x06`, `0x08`, `0x0a`–`0x19`, `0x1a`, `0x1c` | 24 |
| 6 | mechs | `0x08`–`0x69` | 98 |
| 7 | flyers | `0x08`–`0x3f` | 56 |
| 8 | bases | `0x08`–`0x17` | 16 |
| 9 | groups | `0x06`–`0x57` | 82 |

A block record is the listed fields in `.MSN` order, so an offset into it is the `.MSN` offset less the first listed field's: less `0x08` for mechs, flyers and bases, `0x06` for groups, actions and trigger areas, and `0x0a` for points.

`data\mission.var` is the 100 `int16` flags at `ES1GO 000ab244`, 200 bytes, written after the rows are read through a `FileWStream`, which truncates.

## Reading

`DBSim_LoadScriptDat` (`ES1DBSIM 0001d850`) is the world load's first pass:

1. It clears the roster slot arrays: for mechs `000af7b4`, `000af818` and `000af87c`, 100 bytes each; for flyers `000af8e0` and `000af914`, and for bases `000af948`, `000af97c` and `000af9b0`, 50 each.
2. It reads `data\mission.var`'s 200 bytes into `000bc4b8` and sets flag 7 (`000bc4c6`) to 0. The file must open (severity-5 assert, `world.cpp` line `0x8d`).
3. It reads `data\player.mec` ([Open](#open)).
4. It opens `data\script.dat` (assert at line `0xb2`) and reads the header into `000bc49c` and the nine blocks, with the strides above. It keeps:

| Block | Kept |
|---|---|
| 1 | every point, at `000bc480`, and the least and greatest X and Y at `000af7a4`–`000af7b0` |
| 2 | every heading, at `000bc484`, as the file's value shifted left 14 into 16 bits |
| 3 | every waypoint group, at `000bc48a`, as its count and a pointer to an array of pointers to its points |
| 4 | every trigger area, resolved by `0001cb48` into a 10-byte record at `000bc490` |
| 5 | every action as a 16-byte record at `000bc496`: the type and verb, the number of area refs before the first negative one with a pointer to those areas, and the two words from `.MSN` `0x1a` and `0x1c` at `+0xc` and `+0xe` |
| 6, 7, 8 | the type of each record, from block offsets `0x28`, `0x2c` and `0` (`.MSN` `0x30`, `0x34` and `0x08`) |
| 9 | from the second record on, each member ref (block `0x34`, `.MSN` `0x3a`) that is not negative marks its slot live in the roster block `0x28` names; a base member also stores block `0x44` (`.MSN` `0x4a`) for the slot, which is passed with the base's type to `000045a4` |

5. It marks each `data\player.mec` entry live as a mech slot after the block's count, as ES2's pass marks the squad ([placement](../../retail/formats/script-dat.md#placement--the-actual-rule)).
6. Among the calls that follow, `Terrain_LoadZoneShapes` (`00020238`) loads the shapes of the zone in header word 1, from the shape library of the theater in word 0 ([`zone-terrain.md`](zone-terrain.md#the-simulators-load)).

A second function, `0001e638`, opens `data\script.dat` again, where ES2's [second pass](../../retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps) builds the objects ([Open](#open)).

The headings in the two `.MSN` files walked by hand (`SM_SCT50`, `B0_C2_60`) are multiples of 45 from 0 to 315. Shifted left 14 into 16 bits, a value keeps only its two low bits, so 45 and 225 both become `0x4000` ([Open](#open)).

## Open

- **Open:** what `0001e638`, which opens `data\script.dat` after `DBSim_LoadScriptDat`, reads, and so what the simulator makes of the fields the first pass discards.
- **Open:** whether ES1's headings are degrees, and what the simulator does with them beyond the first pass's shift.
- **Open:** `data\player.mec` as `DBSim_LoadScriptDat` reads it: a word to `000af9fa`, a count to `000af9e2`, that many `0x7e`-byte entries at `000af9e4` (four words, two arrays of as many words as the fourth word says, a word, 26 bytes, 20 bytes and 20 bytes), then 17 bytes to `000af9e8`; what each field is.
- **Open:** what header words 2 to 9 mean to the simulator, and what `000bc4b8`'s flags are used for once loaded.
