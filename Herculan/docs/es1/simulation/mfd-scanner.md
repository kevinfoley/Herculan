# DBSIM.EXE — the MFD's scanner screen

**ES1's SCANNER screen is ES2's ([`MFDRadar`](../../retail/simulation/mfd-scanner.md)) drawn more plainly: a fixed picture behind a plot of one-pixel contacts, each colored by its side and kind, a small square around the selected target, the range and the target's distance in the bottom corners, and an `ACT` or `PASS` legend at the top left.** What a player would see differs: there is no turret wedge, range ring, reference line or player marker; a friend is one color whatever it is, where ES2's colors each kind; the plot turns with the view's heading; an indestructible structure is plotted; and the target's distance reads `0000` whenever the target is off the plot, where ES2's holds the last value. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00035270`–`00035bdb` and the initialized data `0009385c`–`00093879`. The code opens with `HeapPtr_DtorScannerCopy` (`00035270`), a lone copy of the 40-byte destructor that opens each run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)), with no run after it; `es2_xref.py` finds no reference to it ([Open](#open)). `MfdRadarScreen_Paint`'s `RET 4` at `00035bd7` is followed by two zero bytes and the next file's own lone copy at `00035bdc`, [the FLASH COMM screen's](mfd-flash-comm.md#where-the-object-file-starts-and-ends) ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). The file has no static initializer, and no assert in it names a source path.

The data holds the bank pointer `MfdRadar_RadarBank` (`0009385c`), `MFDRadarVtable` (`00093862`) and the name `radar` (`00093872`).

## The screen object

**`MfdRadarScreen_Ctor` (`00035298`)**, (screen, rect), allocates `0x54` bytes when given none and runs the base `MFDisplay_Ctor` (`00039c50`), which stores the rect at `+0`. The display's constructor (`00039c64`) builds it as the screen of mode 3, at the display's `+0xa2`, with the display's `+0xb4` as its rect, and points its `+4` at the display's `+0x7a`, as it does for every screen ([the status screen](mfd-status-screen.md#the-screen-object)).

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the rect, a pointer | `+0x04` |
| `+0x04` | the display's block; its `+0xc` is the range | `+0x08` |
| `+0x08` | the vptr, `MFDRadarVtable` | `+0x00` |
| `+0x0c` | frame 0 of the `radar` bank | — |
| `+0x10` | where frame 0 goes: the rect's top-left | — |
| `+0x18` | the plot's center: the rect's top-left + (`0x22`, `0x16`) | `+0x20` |
| `+0x20` | the plot's radius, 21 | `+0x28`, 25 |
| `+0x24` | world units per pixel, 1 until the first paint | `+0x2c` |
| `+0x28` | the range `+0x24` was computed for, 0 at first | `+0x30` |
| `+0x2c` | 1 passive, 0 active; 1 at first | `+0x34` |
| `+0x30` | the `ACT`/`PASS` label | — |
| `+0x34`, `+0x38` | the `DST:` caption and the target's distance | `+0x40`, `+0x44` |
| `+0x3c`, `+0x40` | the `RNG:` caption and the range | `+0x38`, `+0x3c` |
| `+0x44` | a rect, constructed and not written ([Open](#open)) | — |

It loads the `radar` bank once from the folder `000980f8` names into `MfdRadar_RadarBank` and keeps its first frame. The five labels are `Label_Ctor`s in `ColorSchemePanels`' `cpblue` with no fill color and zero margins, placed with `Label_SetRect` relative to the rect's top-left:

| Label | Rect | Align | Text |
|---|---|---|---|
| `+0x3c` | `3, 0x21` – `0xf, 0x26` | left | `RNG:` |
| `+0x40` | `3, 0x26` – `0x13, 0x2b` | left | the range in meters |
| `+0x34` | `0x36, 0x21` – `0x42, 0x26` | right | `DST:` |
| `+0x38` | `0x32, 0x26` – `0x42, 0x2b` | right | the target's distance in meters |
| `+0x30` | `1, 2` – `0xf, 7` | centered | `ACT` or `PASS` |

So the readouts sit at the bottom left and bottom right, where ES2's are at the dish's four corners ([readouts](../../retail/simulation/mfd-scanner.md#readouts)). No coordinate is shifted by a video mode.

`MFDRadarVtable` holds a zero dword, the paint and the update. **`MfdRadarScreen_Update` (`0003558c`)**, (screen), slot `+8`, calls the paint, slot `+4`. ES2's update slot rebuilds the contact list before its paint ([update cadence](../../retail/simulation/mfd-scanner.md#update-cadence)); ES1's paint does that work itself and keeps no list.

## The paint

**`MfdRadarScreen_Paint` (`000355a4`)**, (screen):

1. When the range (the block's `+0xc`) differs from `+0x28`, it stores it there and sets `+0x24` to range / 21, truncated. ES2's divides by 25 and shifts by `VideoMode_XCoordShift` every paint ([plot](../../retail/simulation/mfd-scanner.md#plot)).
2. It installs a pen and a brush, both {0, 0}, at the render context's (`000bdbda`) `+0x228` and `+0x22c`, and blits frame 0 of `radar` at `+0x10` with `Bitmap_Blit`. ES2's floods its dish and draws the dish, wedge and ring from `MFD` frames 14 and 15 ([paint order](../../retail/simulation/mfd-scanner.md#paint-order)).
3. `+0x2c` becomes 1 when the byte `+0x90` of the cockpit's machine (`CockpitViewInstance`'s `+0x1ff`; ES2's radar mode `+0x96`) is 0, else 0. The legend `+0x30` takes `ColorSchemePanels`' `cpred` and `STRINGS0.STR` group 25 entry 1, `PASS`, when it is 1, and `cpgreen` and entry 0, `ACT`, when it is 0 ([the groups](../formats/str-strings.md#the-groups)).
4. It plots each object of `LiveObjectList` as below.
5. When the selection was plotted, it outlines the square from (*x* − 2, *y* − 2) to (*x* + 2, *y* + 2) around its pixel with `Raster_FillRect` and the brush {4, `0x3b`} ([outlines](../../retail/rendering/polygon-fill.md#rectangles-and-ellipses)), where ES2's blits `MFD` frame 16.
6. It writes `DST:` (group 27) into `+0x34`, the selection's distance into `+0x38`, `RNG:` (group 26) into `+0x3c` and the range in meters into `+0x40`, each number through `Hud_WorldUnitsToMetres` and `ltoa` and then right-aligned and zero-padded in a four-character field, as ES2's are.

### The contacts

An object is skipped when it is the cockpit's machine, when its byte `+0x9e` or `+0x93` is set (ES2's `+0xa4` and `+0x99`), or when the dword `+0x16` of its group (`+0x3f`) is non-zero (ES2's group `+0x14`). Its position is its `+0x20`/`+0x24` when its byte `+0x8f` (ES2's `+0x95`) is set or its side byte (the group's `+0x14`) is not 1. Otherwise, on a paint while bit `0x20` of the clock `000981c4` is clear, it is the point at `+0x164` when the byte `+0xa1` is clear, and the object is skipped in every other case: ES2's blinking last-known position, with `+0xa1` and `+0x164` at ES2's `+0xa7` and `+0x1aa` less 6 and `0x46` ([the ghost contact](../../retail/simulation/mfd-scanner.md#the-blinking-ghost-contact), [Open](#open)). ES2's also skips an indestructible structure type; ES1's has no such test.

With (*dx*, *dy*) the position less the machine's `+0x20`/`+0x24`, and *h* the `int16` `+0x10` of the view object (`View_GetActiveObject`, the camera's heading; [the camera](external-views.md#the-camera)):

```
d = Math_FastMagnitude2D(dx, dy), 1 if 0
skip unless d < range (+0x28)
if dy > 0:  d = -d
a = 0x4000 - Math_AsinBam((dx << 14) / d) + h          -- int16
x = Q14(d, Math_CosBam(a))          / +0x24 + center.x
y = Q14(d, Math_CosBam(a - 0x4000)) / +0x24 + center.y  -- each division truncated
```

With *h* 0 this puts the contact at (*dx*, −*dy*) / scale from the center, north up; *h* turns the plot. ES2's rotates by the machine's own heading with `Math_BuildRotation2D` ([plot](../../retail/simulation/mfd-scanner.md#plot)). A contact at the range lands 21 pixels from the center.

The contact is one pixel, `Gfx_PlotPixel` with the pen {0, *color*}, *color* a palette index:

| Side 0 | Otherwise: class 0 | class 1 | class 2 | any other class |
|---|---|---|---|---|
| `0x32` | `0x35` | `0x37` | `0x3b` | `0x3e` |

The class is the object's `int16` `+0x162`. ES2's draws a 2 × 2 block in a `COLORS.DAT` color for each class and side ([contact colours](../../retail/simulation/mfd-scanner.md#contact-colours)).

When the object is the cockpit's selection (`+0x20c`), the paint keeps its pixel for step 5 and its distance, `Hud_WorldUnitsToMetres`(|*d*|), for `+0x38`. The distance starts at 0 on every paint, so it reads `0000` while the selection is not plotted.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorScannerCopy` | `00035270` | the code of `HeapPtr_DtorPanelCopy` (`0002acdc`) |
| `MfdRadarScreen_Ctor`, `MfdRadarScreen_Update`, `MfdRadarScreen_Paint` | `00035298`, `0003558c`, `000355a4` | `0043e70c`, `0043eeb4`, `0043eecc` with `MfdRadarScreen_Update` (`0043ebe0`) |

## Globals

| | Address |
|---|---|
| `MfdRadar_RadarBank` | `0009385c` |
| `MFDRadarVtable`: a zero dword, then the paint and the update | `00093862` |

## Open

- **Open:** what calls `HeapPtr_DtorScannerCopy`, to which `es2_xref.py` finds no branch or stored pointer.
- **Open:** what the rect at `+0x44` is for: the constructor builds it and `MfdRadarScreen_Paint` does not read it.
- **Open:** whether the view object's heading is the hull's or the turret's while the player is in the cockpit, and so whether the plot is hull-relative as ES2's is.
- **Open:** whether ES1's constructors set `+0xa1` before an object enters the list, as ES2's set `+0xa7`, which would leave the blinking last-known position unreachable here too; and what `+0x164` holds.
- **Open:** what the `radar` bank's frame 0 shows, and how many frames the bank has.
- **Open:** what writes the range, the dword at the display's `+0x86` (`+0xc` of the block at `+0x7a`), and what writes the machine's `+0x90`; ES1's display code from `00039c64` is unread beyond its screen constructors.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: with the scanner up and a target selected, `BP <CS>:000355a4` fires each frame, and the screen shows a 5 × 5 square around one contact and `DST:` over a nonzero four-digit distance.
