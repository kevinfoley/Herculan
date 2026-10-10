# Terrain lighting (DBSIM.EXE)

Terrain is lit **at zone load**, and the result is stored in the height grid. Nothing about it is recomputed per frame; the only relight is the second `Terrain_BuildSurface` at the end of the structure-footprint pass, which levels the ground under each structure and so has to rebuild the normals it was lit from — see [`../simulation/terrain-heightmap.md`](../simulation/terrain-heightmap.md#structure-footprints--the-flattening-pass).

```
Terrain_BuildSurface (0046c1dc)              per zone load, and after footprint flattening
 └─ Terrain_BuildCellSurfaceAndShade (0046c2ec)   per cell
     ├─ Terrain_BuildCellSurface (0046bed8)       diagonal selector + both face normals
     ├─ Math_NormalizeVec3ShortToLength (0046c138)  both normals -> length 0x800
     ├─ cell[+0xd] = Light_ComputeShadeForNormal(cell+0x1)    near triangle
     └─ cell[+0xe] = Light_ComputeShadeForNormal(cell+0x7)    far triangle

Terrain_DrawCellQuad (0046d344)              per cell, per frame
 ├─ shades[0] = cell[+0xd]  (near tri)  /  cell[+0xe]  (far tri)
 └─ Raster_DrawTexturedPolyNear (0046865c) / Raster_SetupTexturedSpan (00468078)
     └─ mode 1: DAT_004a09c4 = ((shades[0] * (shadeLevels-1)) + depthBias) << 8
```

That last line is `Raster_ShadeRampRow` inlined — the shade byte is a row of the theater's `world<n>.rmp`, the same table a flat solid poly resolves through. See [`distance-fog-and-sky.md`](distance-fog-and-sky.md) for `depthBias`.

## The shade calculation

`Light_ComputeShadeForNormal` (`0048c060`) is the per-normal sibling of `Light_ComputeShadeForFace`. It walks the active light list. Terrain is shaded at zone load, when the mission sun — type 1, directional — is the list's only entry; the dynamic lights an impact effect registers ([`effect-lights.md`](effect-lights.md)) come and go long afterwards and can never reach a baked cell:

```
dot = normal . lightDirection             // plain integer dot, no normalisation
if (dot < 0) shade -= (intensity * dot) >> 22
shade = min(shade, 255)
```

**The magnitudes are the whole story.** Normals arrive at length `0x800`, the sun's direction vector is length `0x1000`, intensity is `0x100`, so `|dot| = 0x800 * 0x1000 * cos` and the expression collapses to:

```
shade = min(255, 512 * cos)
```

It **saturates at cos 0.5** — every surface within 60° of facing the sun draws at the ramp's top row, not on a falloff. Flat ground sits at cos 0.544 and is therefore pinned at 255. That is why retail terrain reads as evenly lit with shading confined to the steeper slopes, and it is not a curve any Lambert term reproduces.

The contribution is gated on `dot < 0`, so the light's direction field points the way the light travels, into the surfaces it lights. There is no ambient light anywhere in the binary, so a face turned away gets shade 0 — ramp row 0, not black (see below).

## The sun

**This section is the canonical derivation**; [`dts-texture-binding.md`](dts-texture-binding.md) and `Render/MissionSun` reference it.

One hardcoded directional light per mission, created unconditionally by `Light_CreateMissionSun` (`00461240`) from constants compiled into DBSIM. No mission or theater file contributes, and no ambient light is created anywhere in the binary.

```
angles = (-6000, 0, 21000)                     // Vec3Short, 0x10000 per full circle
BuildEulerRotationMatrixQ14(angles, m)         // 0047eaac
RotateVectorByMatrixQ14((0, 0x1000, 0), m, d)  // 0047ffb4
intensity = 0x100
```

`BuildEulerRotationMatrixQ14` reads a 1024-entry quarter-wave cosine table at `DAT_004a25dc` in Q14, indexed `round(angle / 16)` with the usual quadrant reflection.

Because the rotated vector is `(0, 0x1000, 0)`, only the matrix's middle column is used. The matrix writes it as `m[2] = ±cosX·sinZ`, `m[3] = cosX·cosZ`, `m[5] = sinX` — Z composed after X. With X = `-6000` (-32.96°) and Z = `21000` (115.34°) that is **(±0.758, -0.359, -0.544)** at length `0x1000` in Z-up world space: horizontal component 0.839, vertical 0.544. Composing X after Z instead gives world Z +0.233, which would leave every flat cell facing away from the sun and the whole zone at shade 0.

## The ramp rows are not a 0..1 fade

Measured on `WORLD0.RMP` against `WORLD0.DPL` (luminance out over luminance in, summed across the palette):

| row | 0 | 8 | 15 | 23 | 30 | 31 |
|---|---|---|---|---|---|---|
| multiplier | 0.36 | 0.62 | 0.79 | 1.00 | 1.15 | 1.16 |

The ramp **brightens as well as darkens** and passes through unity around row 23 — the neutral row is not the top one.

Flat ground's saturated shade of 255 selects row 30 (`255 * 31 / 256`), i.e. **1.15x** the texture's own palette colour.

## Shading mode

`DAT_004aab30` selects the span routine's shading mode. Mode 2 is Gouraud, taking a shade per vertex out of the shades array; mode 1 takes `shades[0]` alone as one flat row for the whole polygon. `Terrain_DrawCellQuad` only ever fills `shades[0]`, leaving the other three entries of the array untouched — so retail terrain is flat-shaded per triangle, mode 1, and mode 2 is not something the terrain path is set up to use.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| A directional Lambert term (`0.35 + 0.65 * lambert`) over terrain | Terrain is not lit per frame at all; the shade is baked at zone load. It also put flat ground at 0.70x where the original puts it at 1.15x — about 1.6x too dark |
| Ramp row 0 is near black and row 31 is full brightness | Both halves are wrong; the ramp brightens as well as darkens and passes through unity around row 23 |
| A per-row brightness multiplier over an expanded RGB texel, in place of the indexed lookup | The `.RMP` is a per-colour remap: it preserves hue, compresses unevenly, and collapses distinct colours near its ends. No scalar reproduces it, and a row multiplier above 1 clips already-bright texels |
| Cell bytes `+0xd`/`+0xe` are unwritten | `Terrain_BuildCellSurfaceAndShade` writes both, one per triangle |
