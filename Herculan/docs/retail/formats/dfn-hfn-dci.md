# .DFN / .HFN / .DCI — bitmap fonts and cursor images

Reverse-engineered from `VSHELL.EXE`/`DBSIM.EXE` disassembly in the `ES2Recon` Ghidra project, not from the Java source (`ES2TransferApi`/etc. never covered these). Cross-checked against real retail files (`ES2/VOL/simvol0/dfn/`, `ES2/VOL/simvol0/dci/`, `ES2/VOL/SHELL0/DFN/`).

## The shared "Dynamix resource" envelope

All of `.DFN`, `.HFN`, `.DCI`, the bitmap arrays `.DBA`/`.HBA`/`.HB0-2`/`.DB0-2`, and the embedded per-image bitmap sub-header share one 4-byte envelope shape at the start of the entry's content ([`vol-archive.md`](vol-archive.md)):

```
[0..1] uint16 typeId   -- distinguishes the specific resource kind
[2..3] uint16 0x0028   -- constant across the whole family
```

Confirmed `typeId` values, each written as the envelope's 4 bytes read **big-endian**:

| typeId (BE dword) | Kind |
|---|---|
| `0x01002800` | `.DBA`/`.HBA`/`.HB0-2`/`.DB0-2` — bitmap array |
| `0x0E002800` | Embedded single-image sub-header inside the above |
| `0x0B002800` | `.DCI` — cursor image, below |
| `0x05002800` | `.DFN`/`.HFN` — bitmap font, below |

Each resource is one record: the envelope, a `uint32` size of what follows, and that many bytes, rounded up to an even length with one zero byte. A bitmap array's body is an `int16` frame count, an `int16` ([Open](#open)), then that many [bitmap records](#the-bitmap-record), each rounded up the same way; a `.DBM` is one bitmap record. All 358 bitmap arrays and 10 `.DBM` in v1.0's archives end there except `dba\CORNERS.DBA`, which carries 606 bytes past its 24 frames: a bitmap body without its record header, then three whole bitmap records ([Open](#open)).

`.DFN`/`.HFN`/`.DCI` are dispatched by a generic class-registry loader in `DBSIM.EXE` (`ClassItem_ReadTypeTag` (`0047a5a8`) → `ClassItem_FindHandler` (`0047a394`)). Specific loaders: `Panel_LoadWrapper` (`00430f58`, fonts), `Cursor_LoadWrapper` (`00430fb0`, cursors).

### The bitmap record

A bitmap record (`0x0E002800`) holds, after the envelope's size, a 13-byte header, the image data, and then the extra dwords. `GLBitmap_ReadFromStream` (`00481c80`; VSHELL `00453838`) reads the header in this order into the bitmap object, whose fields [`../rendering/dts-billboards.md`](../rendering/dts-billboards.md#tsbitmappart_render-004762e8) lists:

```
+0x00  int16  rows          -- height
+0x02  int16  cols          -- width
+0x04  uint8  bitsPerPixel
+0x05  uint8  flags         -- bits 0-3 the bitmap type, bit 4 opaque; the reader takes bits 0-4
+0x06  uint8  packing       -- 0 raw, 1 RLE, 3 LZH
+0x07  uint32 dataLength
+0x0b  int16  extraCount
+0x0d  [dataLength bytes]     image data
       [extraCount x uint32]  extra dwords
```

The reader reads the data when `dataLength` is non-zero and `extraCount * 4` bytes of extra dwords when `extraCount` is.

## `.DCI` — cursor image

7 real files in `ES2/VOL/simvol0/dci/`: `{CURSOR,ECURSOR,MCURSOR,NCURSOR,PCURSOR,SCURSOR,WCURSOR}.DCI`.

Unlike a bitmap array, `.DCI` is a single embedded bitmap with an extra **hotspot** field spliced between the outer envelope and the sub-header.

Confirmed layout (offsets relative to the start of file content, i.e. after the 9-byte VOL prefix):

```
0x00  uint16 typeId       = 0x000B   (BE dword 0x0B002800)
0x02  uint16              = 0x0028   (constant marker)
0x04  uint32 totalSize    -- content size below this field
0x08  int32  hotspotX     -- cursor click-point X, CONFIRMED (see below)
0x0C  int32  hotspotY     -- cursor click-point Y, CONFIRMED (see below)
0x10  --- embedded bitmap record starts here (typeId 0x0E002800) ---
0x10  uint16 typeId       = 0x000E
0x12  uint16              = 0x0028
0x14  uint32 subSize
0x18  int16  rows
0x1A  int16  cols
0x1C  uint8  bitsPerPixel  -- 8 (indexed color) in all 7 files
0x1D  uint8  flags         -- 0 in all 7 files
0x1E  uint8  packing       -- 0 in all 7 files
0x1F  uint32 dataLength    -- rows*cols in all 7 files (1 byte/pixel)
0x23  int16  extraCount    -- 0 in all 7 files
0x25  [dataLength bytes]   pixel data (0x00 = background, one non-zero indexed color = the cursor's "ink")
```

**Hotspot field (click-point coordinates), verified against all 7 files by their directional prefix.** Each hotspot lands on an inked pixel: the tip of each edge arrow, the centre of the others. Width is `cols` and height `rows`; read the other way round, `ECURSOR`'s hotspot x of 7 would fall outside its image.

| File | width×height | hotspot (x,y) | Shown ([`../simulation/cockpit-input.md`](../simulation/cockpit-input.md#9-cursor-rendering)) |
|---|---|---|---|
| CURSOR.DCI | 8×7 | (3,3) | the default in the forward and off-forward slots |
| MCURSOR.DCI | 8×7 | (3,3) | while an HDD order waits for a map pick |
| ECURSOR.DCI | 8×7 | (7,3) | edge-strip arrow, east |
| WCURSOR.DCI | 8×7 | (0,3) | edge-strip arrow, west |
| NCURSOR.DCI | 8×8 | (3,0) | edge-strip arrow, north |
| SCURSOR.DCI | 8×8 | (3,7) | edge-strip arrow, south |
| PCURSOR.DCI | 16×9 | (4,4) | over the gunsight's click surface |

The bitmap header from `0x18` is the ordinary one ([The bitmap record](#the-bitmap-record)), read by the same code as any other bitmap item. In all 7 files the pixels end exactly at `0x18 + subSize`, the envelope rounds its own length up to even with one zero byte, and one more zero byte follows it. `PCURSOR.DCI` carries 96 further bytes past its envelope, zero but for pairs of `0x3C` ([Open](#open)). Preserve them as raw when parsing.

## `.DFN` / `.HFN` — bitmap font

DBSIM's only HUD text mechanism, and VSHELL's. Not a widget-layout resource: the seven consumer functions in `DBSIM.EXE` pass the loaded object as an opaque handle to the generic label constructors (`Label_Ctor` (`004387ac`)/`Label_SetRect` (`00438884`)/`Label_SetText` (`00438920`)) alongside a display string.

Two sets: `simvol0/dfn/*.DFN` and `simvol0/hfn/*.HFN` (26 and 25 files — the 18 `ColorSchemePanels` fonts plus spares), and `SHELL0/DFN/*.DFN` (`FONT`, `FONT2`, `MAP`, `BLACK`). Same format throughout. `.HFN` is the 640-wide video mode's set and `.DFN` the 320-wide one's, selected by `VideoMode_PanelMode == 3`; they are separate art, not a 2x scale of each other (cell heights 13 and 10, glyph counts 217 and 223).

v1.10 replaces six of these for its translations ([`../retail-builds.md`](../retail-builds.md)), through archives whose entries take precedence: `SHELL1.VOL` carries `BLACK`, `FONT`, `FONT2` and `MAP`, and `SIMPATCH.VOL` the `.DFN` and `.HFN` of `CPYLW` and `GREEN6X8`. Every replacement has 223 glyphs, the 217-glyph ones gaining codes 249 to 254; `ü` (252) is redrawn in all of them, and the two `.HFN` also redraw `à` to `÷` (224 to 247).

### Layout

Offsets relative to content start, i.e. after the 9-byte VOL prefix.

```
0x00  uint16 typeId     = 0x0005      (BE dword 0x05002800)
0x02  uint16            = 0x0028
0x04  uint32 totalSize  -- content size below this field
0x08  int16  glyphCount
0x0a  int16             -- 0 in every retail file
0x0c  int16  firstCharCode           -- 32 in every retail file
0x0e  int16  cellHeight
0x10  int16             -- -1 in every retail file
0x12  int16  cellHeight              -- repeated
0x14  int16  baseline                -- 8 (.DFN) / 9 (.HFN)
0x16  int16  bitsPerPixel            -- 8 in every retail file
0x18  int16             -- 0 in every retail file
0x1a  int16  inkHeight               -- 8 (.DFN) / 11 (.HFN)
0x1c  int16  arrayCount              -- 0 in every retail file; when non-zero, the class's size
                                        functions count arrayCount x 4 bytes before the glyph pool,
                                        but both EXEs' readers and writers move arrayCount x 16
                                        (Stream_ReadDwords of arrayCount << 2)
0x1e  uint32 poolLength
0x22  [poolLength bytes]              glyph pool
      [glyphCount x uint32]           each glyph's start offset into the pool
      [glyphCount x uint8]            each glyph's width
```

A glyph is `width * cellHeight` bytes, row-major, one palette index per pixel. **Verified across all 54 retail font files: every glyph's pool slice is exactly `width * cellHeight` bytes, no exceptions** — so the width byte and the gap between consecutive offsets state the same fact twice.

The declared width is the advance, art included: cells carry their own right-hand spacing column, so a run is laid out by summing widths with no extra tracking. Glyph art is proportional — in `ACTIVE.HFN`, `1` is 3 wide, `S` 6, `0` and `A` 8.

Index 0 is transparent and **every retail file uses exactly one other value as its ink**. That is what makes the 18 colour-scheme fonts copies of one typeface: a widget picks its text colour by picking which font to hand the label constructor, never by passing a colour.

| `.HFN` | ink | `.HFN` | ink |
|---|---|---|---|
| `WHITE` | 30 | `HUD1` | 72 |
| `GRAY` | 25 | `HUD2` | 73 |
| `GREEN` | 14 | `HUD3` | 74 |
| `DARK` | 19 | `CPGREEN` | 15 |
| `RED` | 10 | `ACTIVE` | 24 |

`ColorSchemePanels` (`0049b0ac`) is the 18-entry loaded-font array; see [`cockpit-hud-widgets.md`](../simulation/cockpit-hud-widgets.md#hud-fonts) for the load order and which widget takes which entry.

### `inkHeight` and label placement

`inkHeight` (`0x1a`) is the height a label centres by, and the only vertical metric the label code reads — `cellHeight` is what the glyph *art* occupies. `Label_SetRect` (`00438884`) and the glyph blitter (`HudFont_DrawGlyph`, `00482428`) read this field and no other, so the inked band is centred in the rect and the remaining `cellHeight - inkHeight` rows hang below as descender space. Both sets leave exactly 2: 11 of 13 (`.HFN`), 8 of 10 (`.DFN`). Centring `cellHeight` instead sits every label 1.5 device pixels high.

`bitsPerPixel` (`0x16`) is read by the same blitter, alongside `cellHeight` from `0x0e` (via `HudFont_CellHeight` (`00482410`)) and the glyph width from the per-glyph width byte (via `HudFont_GlyphWidth` (`0048238c`)).

Full placement formula, including the horizontal rule: [`mfd.md`](../simulation/mfd.md), "Label placement".

### Label background

A label paints its rect before its text, in the colour at the label object's field `0x1d` — `0x2e` for a weapon row, `0x11` for the scanner's four readouts, `DAT_004d3c26` (`COLORS.DAT` id 19, palette 16, black) for the shield readouts. That is why retail's shield "100" sits on solid black rather than on the bezel art under it.

The first two are **raw palette indices** and the third a logical id: a constructor's immediate is already an index, only a data file's number goes through `COLORS.DAT`. See [`colors-dat.md`](colors-dat.md).

### Consumers

`HddGauge_LoadPilotFrames` (`0044a7c0`), `HddCommandScreen_RefreshOrders` (`0044ddec`), `HddDamageScreen_Update` (`00450c54`), `PanelAmbience_Ctor` (`00451e94`), `MfdStatusScreen_Paint` (`0043a5a0`), `MfdMissileViewScreen_Paint` (`0043fe1c`), `HddCommandScreen_Update` (`0044c960`). VSHELL loads `MAP.DFN` (`ShellMap_DfnPanelPtr`, `00471ca8`) but never reads it back — that load is vestigial.

## Ruled out: `.BND` and `.SNC`

Real files checked (`ACTOR.BND`, `MECH.BND`, `CAM.BND`, `PA_01000.SNC`, `PA_02000.SNC`) do NOT start with `[typeId][0x0028]` after the VOL prefix. Both are separate formats: see [`bnd-notes.md`](bnd-notes.md) and [`snc-lip-sync.md`](snc-lip-sync.md).

## Open

- **Open:** the `.DFN`/`.HFN` header shorts at `0x0a` and `0x18`. They are 0 in every retail file and have no consumer found.
- **Open:** `PCURSOR.DCI`'s 96 bytes past its envelope. The cursor's load reads one class item, which ends at the envelope; what reads these bytes is the open question. They may be a second image layer (an AND-mask or outline) specific to this cursor.
- **Open:** whether DBSIM.EXE (not VSHELL) loads the SHELL0 fonts (`FONT.DFN`, `FONT2.DFN`, `BLACK.DFN`).
- **Open:** the bitmap array's second `int16`, after the frame count.
- **Open:** what a bitmap record's extra dwords hold, and what reads them from the bitmap object's `+0x16`.
- **Open:** `dba\CORNERS.DBA`'s 606 bytes past its declared frames — whether anything reads them, or they are left over from an earlier, longer version of the file.
