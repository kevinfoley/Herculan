# DBSIM.EXE — `dat\EXPLOS.DAT`, the impact-effect table

ES1's impact effects, what plays where a shot lands or a part comes down, are rows of one table: which flipbook shape each draws and from which bitmap bank, how fast it steps, whether it lights its surroundings, and its sound. ES2's file ([ES2's `EXPLOS.DAT`](../../retail/formats/explos-dat.md)) splits the shape and bank into a table of its own before 40-byte rows; ES1's carries them in each 44-byte row. A search of `GO.EXE`'s image for `explos` finds nothing. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The layout

```
int16 count
byte[0x2c][count]
```

The retail file is 794 bytes: 18 rows, nothing left over.

### The row

Reached as `table + typeId × 0x2c` (`Explosion_GetTypeRecord`, `00005734`, from the effect's type byte at `+0x3b`). It is ES2's row with ES2's shape-table pair inserted at `+0x02`. Offsets in bold are ones the functions this page names read.

| Offset | ES2's field | Retail values |
|---|---|---|
| **`+0x00`** | the root of `dts\explos` the effect draws ([ES2's `+0x00`](../../retail/formats/explos-dat.md#type-row-0x28-bytes)) | 0 to 17, each once |
| **`+0x02`** | the shape row's cell-animation sequence; negative means no flipbook | 0; 1 on rows 4, 9, 10, 12, 13 and 15–17; −1 on row 6 |
| **`+0x04`** | the shape row's texture bank: which `dba\explo<n>` | 1 to 8 |
| **`+0x06`** | the frame interval | 200 |
| **`+0x08`** | the ground shape | 1 on rows 9, 10 and 15–17, 0 on the rest |
| **`+0x0a`** | the light mode | 0, 1 or 2 |
| `+0x0c`–`+0x23` | the twelve-step intensity ramp | 50, 100, 200, 255, 200, 255, 150, 255, 200, 150, 0, 0 on the lit rows but two: 255 and 100 alternating on row 7, and 50, 100, 150, 100, 150, 105, 75, 50, 25, 0, 0, 0 on row 8; 0 on the unlit rows |
| `+0x24` | the proximity radius, `int32` | 20000 on the rows with a ground shape, 0 on the rest |
| **`+0x28`** | the sound id | 28 to 36 |
| **`+0x2a`** | the object class | 1 on the rows with a ground shape, 0 on the rest |

## The load

`Explosion_LoadResources` (`00005764`) is a phase-2 subsystem loader, registered by the static initialiser at `00005e78` with `RegisterSubsystemLoader` (`00000ac4`); the same initialiser registers `ExplosionPool_FlushDeletes` (`00005750`) at phase 5. Against [ES2's](../../retail/formats/explos-dat.md#resources-loaded-with-it):

1. It builds the effect pool, `Pool_Init(0, 0x51, 0x28)` into `g_ExplosionPool` (`000aee88`): 40 effects of `0x51` bytes, where ES2's are `0x5b`.
2. It loads nine banks, `dba\explo0` to `dba\explo8`, from the name template `explo666` (`000907f4`, the index written from the sixth character), into a 9-entry pointer array at `000bc1bc`. ES2's loads fifteen, or thirteen `EXPLO<n>S` in low memory; ES1's has no low-memory set.
3. It opens `dat\explos`, asserting at severity 5 (`explo.cpp` line `0x3f`) when the stream status is set, reads the count into `000bc1b4` and that many `0x2c`-byte rows into `g_ExplosionTypeTable` (`000bc1b0`), and keeps the greatest `+0x00` plus one at `000bc1ba`.
4. It loads `dts\explos` with `Shape_LoadAllRoots` (tag `0x140008`) into `g_ExplosionShapes` (`000bc1b6`). For row *i* from 0 to the root count less one, it stores bank `+0x04` of row *i* in the `+0x26` of root `+0x00` of row *i*. A row past the root count would bind no bank; the retail file has 18 roots, one per row, so every row binds its bank.

What reads the rows, the effect's construction, tick and end, is [the impact effects](../simulation/impact-effects.md).

## The shapes

Read through `HercWorks.Core`, the retail shapes are:

- **`dts\explos`**, 18 roots. Each is a flipbook of bitmap parts on one cell-animation sequence ([bitmap parts](dts-shape.md#drawing-and-finding-parts)), but root 7, 4 cells of solid faces, and root 1, one bitmap. Roots 6, 8 and 17 have a radius of 0, the radius the draw picks the effect's lights by ([drawing](../simulation/impact-effects.md#drawing)).
- **The banks.** No row names bank 0, so `dba\explo0` is loaded and never bound.
- **The ground shape** is root 1 of the ground-shape set, `dts\flat` or `dts\flat2` by the world file's selector ([the ground-shape set](wld-world.md#the-ground-shape-set)); its cells are [Open](#open).
- **The light ramp.** The lit rows 16 and 17 play 13 frames, so their last step, frame 12, reads the low byte of `+0x24`, the radius 20000's, 32 ([the tick](../simulation/impact-effects.md#the-tick--explosion_tickupdate-00005d74)).

## Open

- **Open:** the cells of root 1 of `dts\flat` and of `dts\flat2`, an impact effect's ground shape, through `HercWorks.Core`: the port's reading found 5 cells of solid faces in one of the two.
