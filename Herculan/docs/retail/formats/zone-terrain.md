# `dat\zoneNNNN.dat`, `dba\zoneNNNN.dba` and `dat\mat0.dat` — terrain zones

Reverse-engineered from `DBSIM.EXE` disassembly (Ghidra project `ES2Recon`); all addresses are DBSIM virtual addresses.

A terrain zone is two files in `ZONES.VOL`: a 16-byte header, `dat\zoneNNNN.dat`, and a heightmap, `dba\zoneNNNN.dba`, where `NNNN` is the zone number in decimal, unpadded. Every zone also reads the shared material table `dat\mat0.dat`. The shell reads the same two zone files for its mission map ([`../shell/mission-map.md`](../shell/mission-map.md)), and either program takes them from loose files ahead of the archive ([below](#loose-zone-files)). `Terrain_LoadZone` (`0042789c`) reads the header and `TerrainZone_LoadHeightmap` (`0046c650`) the other two, into the height grid described in [`../simulation/terrain-heightmap.md`](../simulation/terrain-heightmap.md); what a cell's material draws is [`../rendering/terrain-texturing.md`](../rendering/terrain-texturing.md). Which zone a mission runs is the `script.dat` header's ([`script-dat.md`](script-dat.md#header-format)).

## Loading pipeline

Confirmed against real files in `ES2/VOL/ZONES.VOL`.

1. `Terrain_LoadZone(zoneIndex)` builds the base name `zoneNNNN` and reads a **16-byte per-zone header** resource at `dat\zoneNNNN.dat` (`ZONES.VOL\DAT\ZONE*.DAT`, always exactly 16 bytes): four LE `int32`s — `[0]` width shift and `[1]` height shift (redundant, re-derived from the bitmap itself later), `[2]` cell shift, `[3]` height scale. E.g. `ZONE504.DAT` = `07 00 00 00 07 00 00 00 0E 00 00 00 95 00 00 00` → width and height shift 7 (128×128 cells), cell shift 14, height scale 149.
2. `TerrainZone_LoadHeightmap` (`0046c650`) loads the shared material table from `dat\mat0` ([below](#datmat0--the-material-table)), then opens `dba\zoneNNNN.dba`. Every real zone resolves to `.dba` and goes through the generic `ClassItem_LoadResource` polymorphic loader — the same registry-dispatch architecture as `.DFN`/`.HFN`/`.DCI` — into `TerrainZone_PopulateFromBitmap` (`0046c3c0`). Any other extension falls back to a plain `fopen`/`fscanf` ASCII format (`"%d %d %d %d"` header = width shift, height shift, maximum raw height, minimum raw height, then one `%d` per cell) — a level-design/debug path; no loose files of this kind exist in retail data.
3. **`TerrainZone_PopulateFromBitmap`: a zone's heightmap is literally an ordinary Dynamix bitmap** — the same 8-bit-indexed container used for `.DBM`/`.DBA` textures elsewhere (see [`dfn-hfn-dci.md`](dfn-hfn-dci.md)). Each pixel byte (minus a small bias) becomes one cell's raw height byte, the grid stored vertically flipped against the image: image row `r` fills cell row `height - r - 1`, so a heightmap drawn as a top-down picture with north at the top lines up with a world whose +Y runs north. The width and height shifts are re-derived from the bitmap's own dimensions rather than trusted from the zone header. **Verified byte-exact against every real file in `ES2/VOL/ZONES.VOL/DBA/`:** 128×128 zones are exactly 16418 bytes (`128*128 + 34`-byte bitmap header), 256×256 zones exactly 65570 bytes (`256*256 + 34`). Every header's width and height shifts match its heightmap except `ZONE7000.DAT`'s, which declares 7 and 7 beside a 256×256 heightmap; the game plays it at 256×256, the size the image gives.

## Loose zone files

Every read of a zone's files, in both programs, opens the path with a `VolRStream`, whose open tries it as a loose file relative to the current directory, the install, before it searches the archives ([`vol-archive.md`](vol-archive.md#loose-files-on-disk-carry-no-prefix)). So a zone placed in the install as `DAT\ZONE<N>.DAT` and `DBA\ZONE<N>.DBA`, content only, is read in place of `ZONES.VOL`'s zone of that number, and a number `ZONES.VOL` lacks is read from the loose files alone.

| Program | Function | Paths it opens |
|---|---|---|
| DBSIM | `Terrain_LoadZone` (`0042789c`) | `dat\zone<N>.dat`, built by `ResourcePath_BuildFolderName` (`00492ae0`) and opened with `VolRStream_CtorFromPath` (`00472e08`) |
| DBSIM | `TerrainZone_LoadHeightmap` (`0046c650`) | `dat\mat0.dat` and `dba\zone<N>.dba`, the same way |
| VSHELL | `ShellMap_LoadZone` (`00424b48`) | `dat\zone<N>.dat`, opened with `VolRStream_CtorFromPath` (`00402ad9`) |
| VSHELL | `HeightGrid_Load` (`00429010`) | `data\mat0.dat` and `dba\zone<N>.dba`, the same way |

The shell's `data\mat0.dat` is the install's loose `DATA\MAT0.DAT`: none of v1.0's eleven archives has an entry at that path.

The two programs build the name differently. DBSIM writes the number with `_itoa` over the `XXXX` of the literal `zoneXXXX` (`ZoneFilenameTemplate`, `0049aaf1`), copied into a 9-byte stack buffer; a 5-digit number's terminating zero lands one byte past it, in stack padding the function never uses. VSHELL formats `zone%d` into a 12-byte stack buffer. So every zone number from 0 to 32767 builds its name in both, and numbers up to 9999 keep the 8.3 form, `ZONE9999.DAT`, that the archive's entries have. A negative number loads no terrain in DBSIM, which zeroes three globals and returns, and VSHELL reports it as a failed assert.

## `dat\mat0` — the material table

`count` × 8-byte records, confirmed against real `ES2/VOL/simvol0/dat/MAT0.DAT`. Field 0 is a frame index into the theater's terrain bank, field 1 the block shift; what each does is [`../rendering/terrain-texturing.md`](../rendering/terrain-texturing.md#mat0s-two-fields).

`MAT0.DAT` holds 13 records: `{0,6}`, `{1,6}`, `{2,5}`, … — field 0 ascending (frame index), field 1 per-material tiling shift.

## Open

- **Open:** the rest of `MAT0.DAT`. The file is 244 bytes ([`vol-archive.md`](vol-archive.md)); a count and 13 eight-byte records account for 108 of them.
