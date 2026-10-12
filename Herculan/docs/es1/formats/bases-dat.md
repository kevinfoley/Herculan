# DBSIM.EXE — `dat\BASES.DAT`, the structure type table

ES1's nine structure types are fixed 54-byte records: each names its shape, its wreck and which of two shape sets it belongs to, and the rest of its numbers sit after them. ES2's ([ES2's `BASES.DAT`](../../retail/formats/bases-dat.md)) are variable-length, each carrying its destructible parts inline. A search of `GO.EXE`'s image for `bases` finds nothing. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The layout

```
int16 count
byte[0x36][count]
```

The retail file is 488 bytes: 9 records, nothing left over.

### The type record

The loader reads each record into the first 54 bytes of a 62-byte runtime record, so the file's offsets are the record's. Offsets in bold are ones the loader reads.

| Offset | Holds | Retail values |
|---|---|---|
| `+0x00` | — | the record's own index, 0 to 8 |
| **`+0x02`** | the shape's index in its set | 0 to 5 |
| **`+0x04`** | the wreck type in `dgs\bhulks` | 0 to 8; type 5 has 6, type 6 has 7, type 7 has 5 |
| **`+0x06`** | 0: the first shape set; non-zero: the second, and the type's `BASECOL.DAT` model is installed | 1 on type 5, 2 on types 6 and 8; ES2's field here is the [animation thread count](../../retail/formats/bases-dat.md#the-type-record), non-zero on its animated types |
| `+0x08`–`+0x35` | 23 `int16` | below |

| Type | `+0x02` | `+0x04` | `+0x06` | `+0x08`, `+0x0a` | `+0x1c` | `+0x1e`–`+0x24` | `+0x26`, `+0x28` | `+0x2a`, `+0x2c` | `+0x30` | `+0x34` |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 0 | 0 | 0 | 15000, 5000 | 200 | 0, 0, 2440, 900 | 0, 0 | −1, 0 | 0 | 0 |
| 1 | 1 | 1 | 0 | 15000, 5000 | 200 | 0, −5300, 4700, 600 | 0, 0 | −1, 0 | 0 | 0 |
| 2 | 2 | 2 | 0 | 2000, 5000 | 400 | −680, −3580, 870, 1000 | 0, 0 | 0, 256 | 0 | 0 |
| 3 | 3 | 3 | 0 | 15000, 5000 | 200 | 0, −20, 270, 300 | 0, 0 | −1, 0 | 0 | 0 |
| 4 | 4 | 4 | 0 | 15000, 5000 | 200 | 0, 750, 1320, 1200 | 0, 0 | −1, 0 | 0 | 0 |
| 5 | 0 | 6 | 1 | 5000, 5000 | 200 | 0, −490, 2280, 300 | 100, 0 | −1, 0 | 4000 | 0 |
| 6 | 1 | 7 | 2 | 5000, 5000 | 200 | 0, 0, 2700, 250 | 30, 100 | −1, 0 | 2000 | 0 |
| 7 | 5 | 5 | 0 | 15000, 5000 | 200 | 4000, 1300, 3590, 600 | 0, 0 | −1, 0 | 0 | 0 |
| 8 | 2 | 8 | 2 | 5000, 2000 | 200 | 0, 0, 3640, 700 | 0, 0 | 0, 256 | 2000 | 1 |

In every record `+0x0c`, `+0x0e`, `+0x12`, `+0x14` and `+0x2e` are 0, `+0x10` is 1000, `+0x32` is 1500, and `+0x16`–`+0x1a` are 1000, 1000, 1000 except type 2's 2800, 2700, 6000.

## The load

`Base_LoadResources` (`000046ac`) is a phase-2 subsystem loader, registered by the static initialiser at `00004b80` with `RegisterSubsystemLoader` (`00000ac4`). In order:

1. It reads `dat\bforms` (asserting at severity 5, `base.cpp` line `0x270`, when the stream status is set): an `int16` count to `000bc18e` and that many `0x38`-byte records into an array `__vec_new` builds at `000bc190`.
2. It opens `dat\bases` (line `0x27b`), reads the count into `000bc188`, builds that many `0x3e`-byte records at `g_BaseTypeTable` (`000bc18a`) with `__vec_new`, and allocates count × 6 bytes at `000bc194`. For each type it reads `0x36` bytes into its record and sets the record's `+0x3a` to the type's 6-byte slot at `000bc194` when `+0x06` is non-zero, and to 0 when not.
3. It opens `dat\basecol` (line `0x296`) and calls `000094f0` with each type's slot and the stream, in type order, for every type.
4. Over the types it keeps the greatest `+0x02` plus one of the first set at `000bc174` and of the second set at `000bc16a`, and the greatest `+0x04` plus one at `000bc17e`: 6, 3 and 9 in retail.
5. It loads `dgs\bhulks` with `Shape_LoadAllRoots` (tag `0x2bc0001`) into `000bc180`, and allocates two zeroed arrays of `000bc174` pointers (`000bc176`, `000bc17a`) and two of `000bc16a` pointers (`000bc16c`, `000bc170`).
6. When `g_LowMemory` ([low memory](chassis-dat.md#low-memory)) is 0 it loads `dba\basetex` into `000bc184`.

ES2's `Base_LoadResources` (`00405fac`) also reads `dat\lc_wpns` between the formations and the types, and reads each type field by field with its component array. ES1's type takes its collision model when `+0x06` is non-zero, where ES2's takes it when [`+0x30`](../../retail/formats/bases-dat.md#the-type-record) is.

## Open

- **Open:** what ES1's structure code reads at `+0x00` and `+0x08`–`+0x35`. The values are of ES2's kinds (damage, positions, an idle flipbook of −1 or 0 with a 256 interval at `+0x2a`, `+0x2c`, a 1500 at `+0x32`) but not at ES2's offsets, ES1's record having no component array.
- **Open:** which of `dgs\bases`, `dgs\bases_e`, `dts\bases_an` and `dts\bases_ae`, the names beside the loader's strings at `000906d6`–`000906f2`, each shape set draws from, and what fills the four pointer arrays.
- **Open:** `000094f0`, which reads one `BASECOL.DAT` model per type into its 6-byte slot.
