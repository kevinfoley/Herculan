# .BND — per-subsystem tuning source files

83 entries in `SIMVOL0.VOL`'s `bnd\` folder, one per DBSIM subsystem; filenames map to `DBSIM.EXE` translation units (`ACTOR`, `ALERT`, `BULLET`, `CAM`, `DEBRIS`, `FIRE`, `MECH`, `MECHSYS`, `OBJLIST`, `ROCKET`, `TERRAIN`, `TS_PART`, `PWEAPONS`, etc.). Contents are 6–394 bytes; small per-module tuning/config records, not per-entity arrays.

All offsets below are content-relative — the start of the entry's content in the archive. A copy unpacked by `ES2/VOL/extractVol.py` carries a further nine leading bytes belonging to the archive, not to this format; see [vol-archive.md](vol-archive.md).

DBSIM never reads a `.BND` file; see [Build-time-only source format](#build-time-only-source-format--values-compiled-into-dbsimexe-never-read-at-runtime). Decoding the other 82 is shelved for that reason.

## No shared header

There is no envelope, no format marker and no record tag: the first byte of a `.BND` file's content is already the first byte of its per-subsystem record, and its value differs between files (67 distinct values across the 83).

## CAM.BND's full 24-byte record

**A dword, then the ten constants of the camera's placement and steering, in the order the camera code uses them.** Each value after the dword is an immediate in `Cam_Update` (`004011a0`), `Cam_ApproachRate` (`00401c20`) or `Cam_ApproachZoom` (`00401c4c`); what each does is in [`external-views.md`](../simulation/external-views.md#the-camera-object--cam).

| Content offset | Type | Retail value | In `DBSIM.EXE` |
|---|---|---|---|
| 0 | `uint32` | `0x3134D036` | the [first dword](#the-first-dword) |
| 4 | `int16` | 2500 | the orbit's least distance, `0x9c4` in `Cam_Update` |
| 6 | `int16` | 30000 | the orbit's greatest distance, `0x7530` in `Cam_Update` |
| 8 | `int16` | `0x800` | the angle rates' Q8 scale, `Cam_ApproachRate` |
| 10 | `int16` | `0xc0` | the angle rates' step, `Cam_ApproachRate` |
| 12 | `int16` | `0x400` | the zoom rate's and free camera's speed's Q8 scale, `Cam_ApproachZoom` |
| 14 | `int16` | `0x50` | the zoom rate's and speed's step, `Cam_ApproachZoom` |
| 16 | `int16` | `0x3000` | the pitch limit, either way, `Cam_Update` modes 0 and 1 |
| 18 | `int16` | 550 | the eye's height over the ground, `0x226` in `Cam_Update` modes 0 and 1 |
| 20 | `int16` | 500 | the free camera's slope allowance, `Q10(500, …)` in `Cam_Update` mode 0 |
| 22 | `int16` | 8000 | the chase camera's delay, `0x1f40` in `Cam_Update` mode 3 |

The Java source (`herc-works-mdk-main/ES2Core/.../data/file/bnd/{Cam,Mech,MechSys,AppInput,MechView}.java`) carries sample values for 5 of the 83 files, and for `CAM.BND` for every byte; they match the retail bytes except at offset 14, where the notes say "50" for `0x50`.

Every other `.BND` file has an unrelated record shape. ES1's `CAM.BND` is this record without offsets 20–23, its eye height 100.

## The first dword

`CAM.BND`, `MECH.BND` and `MECHSYS.BND` open with a dword that, read as seconds since 1970, falls in February 1996: `0x3134D036`, `0x3133A4F2` and `0x3123B8F1`. Their shared byte 49 (`'1'`) at offset 3 is that dword's high byte.

**Other Java-annotated files** (`MECH.BND`, `MECHSYS.BND`, `AppInput.BND`, `MechView.BND`):
- `MECH.BND`: first 8 bytes match Java notes exactly (242, 164, 51, 49, 12, 0, 42, 0); bytes 8+ diverge. Record 394 bytes total; the Java notes document the first 16.
- `MECHSYS.BND`: 38-byte record; after first 5 bytes (241, 184, 35, 49, 75), stride `[UINT8 value][3×0x00]` at offsets 4,8,12,16,20,24,28 with values **75, 60, 45, 25, 18, 12, 6**.
- `AppInput.BND`: the Java notes document offset 0 (=84) of 23 bytes.
- `MechView.BND`: the Java notes document offsets 0-1.

## Build-time-only source format — values compiled into DBSIM.EXE, never read at runtime

Hardcoded instruction immediates in `dbsim-physics-notes.md` (rocket steering) and the weapon rows' sensor-dropout ranges ([`../simulation/cockpit-hud-widgets.md`](../simulation/cockpit-hud-widgets.md#sensor-dropout)) found in the disassembly match byte-exact values in their corresponding `.BND` files:
- `ROCKET.BND` at content offsets 6-7, 8-9, 14-15: `1280`, `3072`, `40000`
- `PWEAPONS.BND` at content offsets 58-65: `120, 360, 180, 1800` (contiguous), `WeaponGauge_Ctor`'s dark and shown ranges
- `CAM.BND` at every offset after the first dword: the camera's ten constants ([above](#cambnds-full-24-byte-record))

**Conclusion:** `.BND` files are human/build-tool source format whose values are baked directly into `DBSIM.EXE`'s code at build time. The retail game never opens `.bnd` files; there is no runtime loader.

## Not applicable to runtime

- **Not part of the "Dynamix resource" envelope** (`dfn-hfn-dci.md`). `ACTOR.BND`, `MECH.BND`, `CAM.BND` do not start with `[typeId:uint16][0x0028:uint16]`.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A universal 9-byte `.BND` envelope — `[0]=0x02`, `[1..2]` payload length, `[3..4]=0x0000`, `[5..8]` build stamp — followed by a 1-byte record tag | Those nine bytes are the VOL entry prefix, present on every entry of every type, and are absent from the content the game reads. The reading is convincing on an extracted `.BND` alone: the flag really is 0x02, the size field really does hold `fileSize - 10`, and `[3..4]` really is zero — because no `.BND` reaches 64 KB, so the size field's high half is always empty. The "build stamp" is the source file's MS-DOS date and time, which is why files built in the same batch share it. The "record tag" is just the record's first byte. See [vol-archive.md](vol-archive.md). |
| `CAM.BND`'s record is 25 bytes — one more than the Java notes account for | The 25th byte is the archive's per-entry trailer, which repeats the content's last byte. The record is 24 bytes. |

## Open

- **Deferred:** the layouts of the other 82 files, shelved because the game never reads them. `MECH.BND` looks like a per-mech-type array from about offset 8; `MECHSYS.BND`'s decreasing 75…6 run looks like distance or LOD tiers. If resumed:
  - `CAM.BND`'s layout above is the template.
  - Group the rest by payload length and diff within a family (`P*.BND` cockpit panels, `*_ALRT.BND` alert configs) — the approach that decoded `.DCI`.
  - Cross-reference fields against the per-subsystem constants in `dbsim-physics-notes.md`, `damage-system.md` and `weapon-damage-types.md`, the technique that established the format is build-time-only.
