# `dat\<name>_DEB.DAT` (debris tables)

The wreckage a destroyed thing throws is a *database*: a `.DAT` of groups and pieces and a `.DTS` of the shapes the pieces name, under one base name. `Debris_LoadDatabase` (`0040874c`) loads the pair, and `Debris_LoadPieceList` (`004083f8`) reads each group's pieces. What a group does once thrown, and how one index reaches two databases, is [`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#debris). Addresses are DBSIM virtual addresses.

## The files

Retail ships 21, all in the sim volume's `dat\` and `dts\`:

| Database | Groups | Used by |
|---|---|---|
| `DEF_DEB` | 6 | every spawn site; loaded once at startup |
| `BASE_DEB` | 8 | structures |
| one per HERC chassis, 19 | 7, or 8 for `PITB` | that chassis' own component destruction. The base name is the 12-byte NUL-padded string at offset 204 of the mech type record ([`../simulation/mech-locomotion.md`](../simulation/mech-locomotion.md#mech-type-record)) |

`Debris_LoadDatabase` builds a 12-byte struct, `{ group* groups; int16 groupCount; TSShape** shapes; int16 shapeCount }`. Its third argument, when non-zero, is written into every loaded shape's bound-bank pointer (`shape+0x26`), which is how a chassis' wreckage ends up painted in the chassis' colours.

## Layout

```
int16 groupCount
groupCount × {
    int16 throwCount
    int16 pieceCount
    pieceCount × 14-byte piece
}
```

`throwCount` is 0 to throw every piece the group holds, otherwise the number of pieces drawn from it at random ([`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#throwing-a-group)). Walking this shape consumes all 21 retail files exactly, with nothing left over in any of them. A copy pulled out of a VOL to disk keeps that container's entry prefix, which a parse has to skip.

Retail groups: the chassis tables are one-piece groups with a throw count of 0, `DEF_DEB` has 1 to 5 pieces a group with throw count 3 or 4, and `BASE_DEB`'s first six groups have 6 pieces and throw 2 of them (5 for group 4) where its last two have 3 pieces and throw 9 and 3.

### The piece, 14 bytes

| Offset | Type | Meaning |
|---|---|---|
| `+0x00` | `int16` | shape index: root of the matching `.DTS` |
| `+0x02` | `int16` | weight: share of the group's weighted draw; retail states 10, or 20 on some `DEF_DEB` pieces |
| `+0x04` | `int16` | child group: the group this piece bursts into where it ends, `-1` for none |
| `+0x06` | `int16` | destroy effect: the [`EXPLOS.DAT`](explos-dat.md) type that goes off there, `-1` for none |
| `+0x08` | `int16` | orientation yaw, **degrees**, `-1` = leave the spawn frame alone |
| `+0x0a` | `int16` | throw yaw, **degrees** relative to the above, `-1` = throw on a random bearing |
| `+0x0c` | `int16` | mass: divides the throw speed; retail 800-4000 (`BASE_DEB` 800-1000, `DEF_DEB` 1000-1500, a chassis 1500-4000) |

`Debris_LoadPieceList` multiplies both angles by 182 as it reads them (`65536 / 360 ≈ 182.04`, degrees to BAM) unless the raw value is the `-1` sentinel, and accumulates the group's total weight, which the file does not store. The file's own values are degrees; the conversion is a load-time step.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A flat table of 9 `int16` entries behind a count | The file is nested: a group count, then per group a throw count, a piece count and that many **14**-byte pieces. Only that shape consumes all 21 retail files exactly |
