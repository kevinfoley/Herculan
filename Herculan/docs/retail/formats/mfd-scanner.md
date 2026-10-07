# The MFD's SCANNER screen

Mode 3, the F4 screen. Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project. The display that hosts it — F-key column, aux buttons, chrome, titles — is [`mfd.md`](mfd.md).

| Symbol | Address | Role |
|---|---|---|
| `MfdRadarScreen_Ctor` | `0043e70c` | Builds the plot geometry, the contact vector and four labels. |
| `MfdRadarScreen_Update` | `0043ebe0` | Rebuilds the contact list from the live object list. |
| `MfdRadarScreen_Paint` | `0043eecc` | Draws it. Screen vtable `+0`. |
| `MfdRadarScreen_UpdateAndPaint` | `0043eeb4` | Screen vtable `+4`: update then paint. |

The screen object is 0x524 bytes, the largest of the six. `MfdDisplay_Ctor` parks the inset rect pointer in the base at `+4` and the display's shared state block (`MfdDisplay+0xb1`) at `+8`, which is how the paint reaches the current range at `MfdDisplay+0xbd`.

| Offset | Contents |
|---|---|
| `+0x10`, `+0x14` | `MFD` bank frames 14 (dish) and 15 (wedge) |
| `+0x18` | Dish top-left, `inset + (0x26, 1)` |
| `+0x20` | Plot centre, `inset + (0x41, 0x1c)` |
| `+0x28` | Plot radius in GAU units, `0x19` |
| `+0x2c` | World units per device pixel; `+0x30` the current range |
| `+0x34` | Radar mode mirrored from `mech+0x96`, 1 = passive |
| `+0x38`-`+0x44` | The four corner labels |
| `+0x48` | Dish rect, top-left plus frame 14's own size |
| `+0x58`, `+0x60` | Reference line at `inset + (0x40, 3)`, player marker at `centre - (3, 0)` |
| `+0x68` | 100-entry contact vector, `{int x, int y, int colourId}` |
| `+0x518` | Live contact count; `+0x51c` the selected target's entry or null; `+0x520` the `TRG:` value |

## Art

Every piece is an `MFD` bank frame the rest of the display never touches — frames 14-18.

| Frame | Size | What it is |
|---|---|---|
| 14 | 110x110 | The dish: opaque palette 17 in the corners, a grey ring, **transparent interior** |
| 15 | 51x50 | The turret wedge — a filled quarter disc, palette 97 blue |
| 16 | 10x10 | The bracket drawn over the selected target, palette 12 |
| 17 | 6x54 | The fixed 12-o'clock reference line, dish top edge down to the centre |
| 18 | 14x7 | The player marker, an up-pointing triangle whose apex is the centre, palette 107 red |

Frame 14 is blitted **after** the wedge and covers everything outside its transparent interior, which is what confines the wedge to the dish without a clip.

### The wedge

`Bitmap_BlitRotatedScaled` builds its destination quad as `(0,0),(w,0),(w,h),(0,h)`, rotates each corner, and only then translates by the caller's position — so **that position is the pivot corner, not a top-left or a centre**. Frame 15 is authored as a quarter disc filling the quadrant right and below that corner for exactly this reason: its pivot goes on the plot centre and the sprite *is* the 90-degree arc.

The angle is `Mech_GetTorsoTwistAngle() - 0x6000`. The quadrant's bisector starts at 45 degrees, so turning it back 135 degrees points it straight up, which is where a centred turret belongs. A positive binary angle turns the quad clockwise on screen, so a turret twisted right swings the wedge right.

The floating repeater below builds the same arc from two lines instead.

## Plot

```
scale       = range / 25 >> XCoordShift          -- world units per device pixel
pixelOffset = worldOffset / scale                -- integer divide, so blips snap
```

A contact at the display range therefore lands 25 GAU units from the centre, inside the dish art's own 27-unit radius. Ranges are `MfdScannerRanges` (`004d1cf4`) = 50000 / 100000 / 200000 world units = 300 / 600 / 1200 m; `MfdDisplay_Ctor` writes all three as literals and boots at index 2. RANGE (`MfdDisplay_CycleScannerRange`, `00446fc8`) steps the index and wraps at 3, so the first press goes from 1200 m to 300 m.

`MfdRadarScreen_Update` walks the live object list and, for each object:

| Test | Effect |
|---|---|
| The viewing machine itself | skipped |
| `obj+0x99` or `obj+0xa4` | skipped — out of the fight |
| `group+0x14` | skipped — the group is still waiting on a deployment action |
| Target class 1 and `BASES.DAT +0x1e` | skipped — an **invulnerable** structure type, i.e. the three scenery types |
| `obj+0x95` clear and the object is Cybrid | skipped — see below |
| `range <= Math_FastMagnitude2D(dx, dy)` | skipped — ground-plane distance, against the *current* setting |

The survivor is rotated into the machine's own frame by `Math_BuildRotation2D(-heading)` and stored as `(x, -y)`, so the plot is **hull-relative with the nose up** while the wedge shows the turret. The selected target's entry is remembered at `+0x51c` and its range, in metres, at `+0x520`.

**Friendlies always plot; a Cybrid plots only once something has painted it** (`obj+0x95`, [`../simulation/target-selection.md`](../simulation/target-selection.md)). That is what makes the PASS/ACTIVE choice visible on this screen.

### The blinking ghost contact is dead code

The hostile branch does not simply skip. On every other coarse tick (`Time_GetCoarseTicks() & 0x20`) it looks for a stored position at `obj+0x1aa`, gated on `obj+0xa7 == 0`, and plots that instead — a blinking last-known-position marker.

**It can never run.** `Mech_Constructor`, `Flyer_Constructor` and the base-object prologue `Base_Construct` inlines five times all set `obj+0xa7 = 1` immediately before `ObjectList_Add`, and nothing in the image clears it. Nothing writes `obj+0x1aa` either. The same idiom appears in `Mech_ReceiveSquadOrder` (`00420ad4`) and `HddCommandScreen_DrawMarker` (`0044e92c`) and is equally dead there.

## Paint order

1. Flood the dish rect, palette index 17.
2. The wedge, rotated about the plot centre.
3. Frame 14 over it.
4. The passive-range ring, **only** when the machine is passive *and* `140000 < range` — so it appears on the 1200 m setting alone. `Raster_DrawEllipse` (`00488070`) with the brush in outline mode, colour id 11 → palette 15, green. 140000 is the range at which a scanner paints something that is not emitting back, the same figure the detection sweep uses.
5. The reference line, then the player marker.
6. Each contact as a 2x2 device-pixel fill in its own colour.
7. The bracket over the selected target's blip, offset `-2` GAU on both axes.
8. The four readouts.

### Contact colours

Eight `HudColorTable` entries, keyed on the object's target class and its group's side byte. Confirmed against the retail reference crop.

| Class | Human | Cybrid |
|---|---|---|
| 0 HERC | id 16 → palette 31, white | id 9 → palette 10, **red** |
| 1, 3 structure | id 6 → palette 98, blue | id 12 → palette 14, green |
| 2 flyer | id 17 → palette 24, grey | id 15 → palette 13, yellow |
| unrecognised | id 18 → palette 20 — unreachable with retail data | |

### Readouts

Four labels in the dish's four corners, where the circle leaves room. All in `ColorSchemePanels[13]` `GREEN`, all with background id `0x11`, all rects stated as a top-left plus a size, GAU relative to the inset origin.

| Rect | Align | Text |
|---|---|---|
| `0x26, 1` + `0xc x 5` | left | `RNG:` — `STRINGS0.STR` group 31 |
| `0x4f, 1` + `0x10 x 5` | right | the display range in metres |
| `0x26, 0x33` + `0xc x 5` | left | `TRG:` — group 30 |
| `0x4f, 0x33` + `0x10 x 5` | right | the selected target's range in metres |

Both values go through `Hud_WorldUnitsToMetres` and then `_itoa` into a four-byte field, whose digits the paint walks back to the far end filling what it passes with `'0'` — a **right-aligned, zero-padded four-character field**, which is why the power-up reads `1200` and `0000`.

`+0x520` is written **only while the selection is actually being plotted**, so `TRG:` holds its last value once the target leaves scanner range or the selection is cleared. Zero is simply the field's initial state.

## Buttons

`MfdButton_OnClick`'s own switch. RANGE, TARGET, PASS and ACTIVE are the four this screen shows.

| Button | Action |
|---|---|
| 8 RANGE | `MfdDisplay_CycleScannerRange` (`00446fc8`) — step the zoom index, wrapping at 3 |
| 9 TARGET | `TargetSelect_Cycle`, so it does what [Enter] does. Its case is shared with 7 SELECT, which branches on the current mode and takes the `TargetSelect_Cycle` arm in every mode but 0 — the scanner is mode 3 — see [`mfd.md`](mfd.md) |
| 11 PASS | `mech+0x96 = 0`, then light itself and clear ACTIVE |
| 12 ACTIVE | `mech+0x96 = 1`, and the reverse |

PASS and ACTIVE are latching buttons whose lit state is the machine's radar mode, not their own click history: `MfdRadarScreen_Update` compares `mech+0x96` against its mirror at `+0x34` every frame and re-presses whichever button matches when they disagree, which is how [R] moves them.

## Update cadence

`MfdDisplay_Update` dispatches one screen's update slot, the current one's, and repaints the scanner every frame while it is up ([`mfd.md`](mfd.md#paint-order)). While it is *not* up the repeater below calls the same update itself, so the contact list is never more than a frame old whichever screen the display is showing.

The `radar` bank's ten frames play at the dish's position once, when the cockpit powers up, and never again: the display frees the bank the frame its power-up is done, so a later switch to F4 paints the screen at once. See [`cockpit-hud-widgets.md`](cockpit-hud-widgets.md#scanner-dish-grows).

## The floating repeater

`HudScanner_Paint` (`0043f2b0`), reached from `Gunsight_Paint` (`0043d5c8`) and `Gunsight_UpdateAndPaint` (`0043d6dc`) through the one-line `Gunsight_PaintHudScanner` (`0043e0ec`). It belongs to the **gunsight complex**, not the MFD — see [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md) — and is the last thing that complex draws, after every child.

It reaches the scanner screen object through `CockpitView+0x1ed`'s `+0xd9`, calls that screen's update slot to rebuild the contact list, and **returns immediately when that screen is the display's current one**. So the repeater and the F4 screen are never on screen together: the repeater is what the player sees on F1, F2, F3, F5 and F6.

### Geometry

Top-left is **`.GAU` offset 1196/1200**, two more ints of the gunsight block that `Gau_RovingGunsightWidget` reads into the widget at `+0x10b`/`+0x10f`. Position is per herc:

| Herc | Point | Herc | Point |
|---|---|---|---|
| APOCA | `40,27` | RAPTOR2 | `54,29` |
| COLOSSUS | `50,11` | RAZOR | `15,20` |
| MAVERICK | `50,29` | SAMSON | `51,5` |
| OGRE | `67,80` | TOMAHAWK | `53,28` |
| OUTLAW | `44,28` | | |

The extent is not in the file: the paint squares off `0x2e` GAU units from that point on both axes, so the circle is 92x92 device with a 46-pixel radius. **The plot scale divides by that half-size, not by the screen's `0x19`**, so a contact at the display range lands on the rim rather than short of it. Range is the same setting the F4 screen's RANGE button sets.

### What it draws

Nothing it shares with the screen beyond two sprites — no dish, no wedge sprite, no background flood, no reference line, no range ring and no readouts.

1. The circle: `Raster_DrawEllipse` (`00488070`) over the 92x92 rect with the brush in outline mode, colour id 9 → palette 10, red.
2. The turret arc as **two lines** from the centre to the rim (`Raster_DrawLine`, `004838f8`), at `Mech_GetTorsoTwistAngle() +/- 0x2000` — the same 90 degrees the screen's wedge sprite covers, drawn with the pen in the same red. Each endpoint is the point `(0, -radius)` rotated, so both reach the rim exactly.
3. `MFD` frame 18, the player marker, at `centre - (3 << XCoordShift, 0)` as on the screen.
4. Each contact as two filled discs: radius 2 in colour id 19 (palette 16, black) with radius 1 in the contact's own colour inside it. Both radii are literal device pixels, unshifted, so a blip is the same size in every video mode.
5. `MFD` frame 16, the target bracket, over the selected contact.

Verified against `Reference/Targeting.png`: SAMSON's `51,5` puts the circle at device `102,10 - 194,102`, which is where that capture's is.
