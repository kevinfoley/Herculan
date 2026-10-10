# .BND — per-subsystem tuning source files

83 entries in `SIMVOL0.VOL`'s `bnd\` folder, one per DBSIM subsystem; filenames map to `DBSIM.EXE` translation units (`ACTOR`, `ALERT`, `BULLET`, `CAM`, `DEBRIS`, `FIRE`, `MECH`, `MECHSYS`, `OBJLIST`, `ROCKET`, `TERRAIN`, `TS_PART`, `PWEAPONS`, etc.). Contents are 6–394 bytes; small per-module tuning/config records, not per-entity arrays.

All offsets below are content-relative — the start of the entry's content in the archive. A copy unpacked by `ES2/VOL/extractVol.py` carries a further nine leading bytes belonging to the archive, not to this format; see [vol-archive.md](vol-archive.md).

DBSIM never reads a `.BND` file; see [Build-time-only source format](#build-time-only-source-format--values-compiled-into-dbsimexe-never-read-at-runtime). Decoding the other 82 is shelved for that reason.

## No shared header

There is no envelope, no format marker and no record tag: the first byte of a `.BND` file's content is already the first byte of its per-subsystem record, and its value differs between files (67 distinct values across the 83).

## CAM.BND's full 24-byte record

The Java source (`herc-works-mdk-main/ES2Core/.../data/file/bnd/{Cam,Mech,MechSys,AppInput,MechView}.java`) has **sample-value-annotated byte layouts** for 5 of the 83 files; for `CAM.BND` specifically it accounts for **every byte of the record**:

| Content offset | Type | Retail value |
|---|---|---|
| 0 | UINT8 | 54 |
| 1 | UINT8 | 208 |
| 2 | UINT8 | 52 |
| 3 | UINT8 | 49 |
| 4-5 | UINT16 LE | 2500 |
| 6-7 | UINT16 LE | 30000 |
| 8 | UINT8 | 0 |
| 9 | UINT8 | 8 |
| 10 | UINT8 | 192 |
| 11 | UINT8 | 0 |
| 12 | UINT8 | 0 |
| 13 | UINT8 | 4 |
| 14 | UINT8 | 80 |
| 15 | UINT8 | 0 |
| 16 | UINT8 | 0 |
| 17 | UINT8 | 48 |
| 18 | UINT8 | 38 |
| 19 | UINT8 | 2 |
| 20-21 | UINT16 LE | 500 |
| 22-23 | UINT16 LE | 8000 |

All 22 numeric fields but one match the Java author's sample values exactly. Offset 14: author's notes say "50" but retail is `0x50` = 80 (likely hex transcription).

Every other `.BND` file has an unrelated record shape.

Offset 3 (49 = ASCII `'1'`) appears at the same offset in `CAM`, `MECH` and `MECHSYS`. What the fields mean is [Open](#open).

**Other Java-annotated files** (`MECH.BND`, `MECHSYS.BND`, `AppInput.BND`, `MechView.BND`):
- `MECH.BND`: first 8 bytes match Java notes exactly (242, 164, 51, 49, 12, 0, 42, 0); bytes 8+ diverge. Record 394 bytes total; the Java notes document the first 16.
- `MECHSYS.BND`: 38-byte record; after first 5 bytes (241, 184, 35, 49, 75), stride `[UINT8 value][3×0x00]` at offsets 4,8,12,16,20,24,28 with values **75, 60, 45, 25, 18, 12, 6**.
- `AppInput.BND`: the Java notes document offset 0 (=84) of 23 bytes.
- `MechView.BND`: the Java notes document offsets 0-1.

## Build-time-only source format — values compiled into DBSIM.EXE, never read at runtime

Hardcoded instruction immediates in `dbsim-physics-notes.md` (rocket steering) and the weapon rows' sensor-dropout ranges ([`../simulation/cockpit-hud-widgets.md`](../simulation/cockpit-hud-widgets.md#sensor-dropout)) found in the disassembly match byte-exact values in their corresponding `.BND` files:
- `ROCKET.BND` at content offsets 6-7, 8-9, 14-15: `1280`, `3072`, `40000`
- `PWEAPONS.BND` at content offsets 58-65: `120, 360, 180, 1800` (contiguous), `WeaponGauge_Ctor`'s dark and shown ranges

**Conclusion:** `.BND` files are human/build-tool source format whose values are baked directly into `DBSIM.EXE`'s code at build time. The retail game never opens `.bnd` files; there is no runtime loader.

## Not applicable to runtime

- **Not part of the "Dynamix resource" envelope** (`dfn-hfn-dci.md`). `ACTOR.BND`, `MECH.BND`, `CAM.BND` do not start with `[typeId:uint16][0x0028:uint16]`.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A universal 9-byte `.BND` envelope — `[0]=0x02`, `[1..2]` payload length, `[3..4]=0x0000`, `[5..8]` build stamp — followed by a 1-byte record tag | Those nine bytes are the VOL entry prefix, present on every entry of every type, and are absent from the content the game reads. The reading is convincing on an extracted `.BND` alone: the flag really is 0x02, the size field really does hold `fileSize - 10`, and `[3..4]` really is zero — because no `.BND` reaches 64 KB, so the size field's high half is always empty. The "build stamp" is the source file's MS-DOS date and time, which is why files built in the same batch share it. The "record tag" is just the record's first byte. See [vol-archive.md](vol-archive.md). |
| `CAM.BND`'s record is 25 bytes — one more than the Java notes account for | The 25th byte is the archive's per-entry trailer, which repeats the content's last byte. The record is 24 bytes. |

## Open

- **Deferred:** what `CAM.BND`'s fields mean. the four 16-bit fields at offsets 4, 6, 20 and 22 (2500, 30000, 500, 8000) may be camera near/far or zoom-range values; offset 3, shared with `MECH` and `MECHSYS`, may be a format sub-version byte. Matching them to immediates in DBSIM's camera code would settle both.
- **Deferred:** the layouts of the other 82 files, shelved because the game never reads them. `MECH.BND` looks like a per-mech-type array from about offset 8; `MECHSYS.BND`'s decreasing 75…6 run looks like distance or LOD tiers. If resumed:
  - `CAM.BND`'s layout above is the template.
  - Group the rest by payload length and diff within a family (`P*.BND` cockpit panels, `*_ALRT.BND` alert configs) — the approach that decoded `.DCI`.
  - Cross-reference fields against the per-subsystem constants in `dbsim-physics-notes.md`, `damage-system.md` and `weapon-damage-types.md`, the technique that established the format is build-time-only.
