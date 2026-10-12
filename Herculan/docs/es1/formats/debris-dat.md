# DBSIM.EXE — `dat\DEBRIS.DAT` and `dts\DEBRIS.DTS`, the debris database

**ES1 has one set of wreckage pieces, which every destroyed thing throws from: a table of groups of pieces, and a shape file the pieces name.** ES2 has 21 such databases, a default, the structures' and one per chassis, each piece carrying angles, a mass and what it bursts into ([ES2's debris tables](../../retail/formats/debris-dat.md)); ES1's single database is loaded into globals at start-up, and its pieces name only a shape and a weight. A search of `GO.EXE`'s image for `debris` finds nothing. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](x32-executable.md#the-image)); ES2's are DBSIM's.

## The layout

Little-endian `int16` throughout, ES2's group header with 4-byte pieces where ES2's are 14:

```
int16 groupCount
groupCount × {
    int16 throwCount
    int16 pieceCount
    pieceCount × { int16 shape, int16 weight }
}
```

| Piece offset | Field |
|---|---|
| `+0x00` | the shape: an index into the root shapes of `dts\debris`, not checked against their count |
| `+0x02` | the weight: the piece's share of its group's weighted draw ([throwing a group](#throwing-a-group)) |

`SIMVOL0.VOL`'s `DEBRIS.DAT` is 54 bytes, four groups with nothing left over, every weight 20:

| Group | Throw count | Shapes |
|---|---|---|
| 0 | 3 | 0 |
| 1 | 3 | 0, 1 |
| 2 | 5 | 0, 1, 2 |
| 3 | 7 | 2, 3, 4 |

## The load

`Debris_LoadResources` (`0000607c`, `debris.cpp`) is a phase-2 subsystem loader. The static initialiser `Debris_RegisterSubsystem` (`000065f4`) registers it with `RegisterSubsystemLoader` (`00000ac4`), and `DebrisPool_FlushDeletes` (`00005ebc`), `ObjectPool_FlushDeleteQueue` on the pool below, at phase 5; the exit routine `Debris_StaticDtor` (`00006620`) frees the pool. In order, the loader:

1. builds the pool of thrown pieces with `Pool_Init(0, 0x4e, 0x96)` into `000aeef8`: 150 objects of `0x4e` bytes;
2. loads `dts\debris` with `Shape_LoadAllRoots` (`000635a8`), tag `0x140008`, its roots to the array at `000bc1c6` and their number to `000bc1ca`;
3. opens `dat\debris`, asserting at severity 5 (`debris.cpp` line `0x59`) when the stream status is set, reads the group count into `000bc1c4`, allocates count × 10 bytes at `000bc1c0` and reads each group with `Debris_LoadPieceList`;
4. closes the file.

A group is 10 bytes: the throw count at `+0`, the piece count at `+2`, the pieces at `+4` and their total weight at `+8`. `Debris_LoadPieceList` (`00005ed0`), given the group and the stream, reads the two counts, allocates 4 bytes a piece, zeroes the total, reads each piece's shape and weight, adds the weight to the total, and returns the greatest shape index, which the loader drops. ES2's `Debris_LoadPieceList` (`004083f8`) reads ES2's 14-byte pieces and turns their angles to BAM; ES2's `Debris_LoadResources` (`00408860`) builds a pool of 150 objects of `0x61` bytes and loads its default database, `def_deb`, through `Debris_LoadDatabase` (`0040874c`), which can bind a texture bank to a database's shapes; ES1 has no counterpart of `Debris_LoadDatabase` and binds none.

## Throwing a group

**A destroyed thing throws a random number of pieces, fewer than its group's throw count, each drawn at random by weight, so the same piece can be thrown twice and none may be thrown at all.** `Debris_ThrowGroup` (`00005fb8`), given the group, a second argument it passes on, and the shape array, in stack order:

1. draws *n* = `Math_RandomBelow` (`00063b3c`)(throw count), 0 to throw count − 1, from the generator at `000b5bd4`;
2. *n* times, draws *r* = `Math_RandomBelow`(total weight), walks the pieces from the first, taking each one's weight off *r* while *r* is greater than that piece's weight, and throws the piece it stops at: `00006168` with 0, the second argument and the piece's root shape, then `00006480` with the result ([Open](#open)).

ES2's `Debris_ThrowGroup` (`004086bc`) throws exactly the throw count, or every piece once when the count is 0, with the same weighted walk. In ES1 a throw count of 0 would divide by zero in `Math_RandomBelow`; every retail group's is 3 to 7. `es2_xref.py` finds five calls of `Debris_ThrowGroup`, in `00003978`, `00003a58`, `00009cd4`, `00013b44` and `0001b370`.

## Open

- **Open:** what `00006168` and `00006480` do with a thrown piece, what the `0x4e`-byte piece object holds, and how it moves and ends.
- **Open:** which destroyed things throw which group: the five callers of `Debris_ThrowGroup`.
