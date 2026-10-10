# `wld\world<N>.wld` — theater parameters

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

One file per theater variant. `World_LoadTheater` (`0042e010`) reads the one the `script.dat` header selects ([`script-dat.md`](script-dat.md#header-format)) field by field. Its fields drive the sky backdrop ([`../rendering/distance-fog-and-sky.md`](../rendering/distance-fog-and-sky.md#the-sky--hzline)), the distance colour bands ([`../rendering/distance-fog-and-sky.md`](../rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8)), the terrain texture bank ([`../rendering/terrain-texturing.md`](../rendering/terrain-texturing.md)), the ground-shape set ([`../simulation/ground-shapes.md`](../simulation/ground-shapes.md#the-shape-set--flatobj_loadresources-004097a8)) and the damage flash's palette ([`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md#the-damage-shake)).

## The `world<N>` descriptor — layout

`wld\WORLD0.WLD` … `WORLD9.WLD`, 310–313 bytes each, read field-by-field in this order:

| | |
|---|---|
| 8 x `int16` | the sky backdrop's `hzline` — [below](#the-hzline-shorts) |
| 6 x `int16` | ditto; two land in `DAT_004cfd76`/`DAT_004cfd78`. The second of the six, the file's tenth `int16` (byte 18), is `World_FlatSetSelector` (`0049aeea`), which picks the theater's ground-shape set ([`../simulation/ground-shapes.md`](../simulation/ground-shapes.md#the-shape-set--flatobj_loadresources-004097a8)); 1 in all ten retail files |
| `int32` count + count x `int32` | `WorldShades_BandsTagged` (`004cfd84`), the distance thresholds of the [colour bands](../rendering/distance-fog-and-sky.md#distance-colour-bands--worldshades_applyforobject-0042e8e8) for an object with a type tag: 16 entries, 4400 apart from 60000 — from 30000 in `WORLD4` |
| `int32` count + count x `int32` | `WorldShades_BandsTag0` (`004cfd88`), the same for tag 0: 16 entries, 4400 apart from 60000, in every retail file |
| `int16` rows, `int16` cols | sizes the pair of ramp tables that follow; 16 and 11 in every retail file |
| cols x `int32`, `int16`, cols x `int32` | expanded by `Palette_InterpolateIndexRanges` (`00430d08`) into `WorldShades_LevelRanges` (`004cfd7c`), one index range per column and band |
| 4 bytes, 4 bytes | a second, 1-wide ramp through the same expander, into `WorldShades_BlendRanges` (`004cfd80`) |
| `int16`, `int16`, `int32`, `int32` | the two `int32`s are `WorldShades_DistanceOffsets` (`004cfd6c`), the offsets a tag-5 object's distance takes |
| 5 NUL-terminated strings | `world24`, `clouds2`, `impact<N>`, **terrain bank**, `tex` |

The third string names the theater's impact palette, `IMPACT<n>.DPL` ([`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md#the-damage-shake)); the fourth, the terrain bank `Terrain_BindTextureBank` loads ([`../rendering/terrain-texturing.md`](../rendering/terrain-texturing.md#the-answer-end-to-end)). What becomes of the second is [`../rendering/distance-fog-and-sky.md`](../rendering/distance-fog-and-sky.md#open).

| descriptor | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|---|
| bank | urban | urban | bsnow | bsnow | volcan | volcan | ice | ice | moon | moon |

Five theaters, two variants each. The variant is **time of day**: the practice missions screen's `Day` / `Night` row writes it straight into the header field, and the ten retail files all carry `Day`. See [`../shell/main-menu.md`](../shell/main-menu.md#the-parameters). Which theater, variant and zone a mission runs is the `script.dat` header's — see [`script-dat.md`](script-dat.md#header-format).

### The `hzline` shorts

The first eight `int16`s, which `World_LoadTheater` hands to the theater's `hzline` object. What each one does is [`../rendering/distance-fog-and-sky.md`](../rendering/distance-fog-and-sky.md#the-object).

| Short | Byte | Field | Retail value |
|---|---|---|---|
| 0 | 0 | first-band offset | 2 |
| 1 | 2 | zenith colour | 208 |
| 2 | 4 | band height | 6 |
| 3 | 6 | band count | 16 in `WORLD0`, `WORLD2`, `WORLD6`; 15 in the other seven |
| 4 | 8 | first ground colour | 239 where short 3 is 16, 224 elsewhere |
| 5 | 10 | ground band height | 1 |
| 6 | 12 | ground band count | 1 |
| 7 | 14 | line offset | 0 |
