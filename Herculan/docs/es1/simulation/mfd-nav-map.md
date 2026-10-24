# DBSIM.EXE — the MFD's NAV MAP screen

**ES1's NAV MAP is not ES2's terrain map ([`MFDMap`](../../retail/simulation/mfd.md#mfdmap--mode-2)): it draws the shapes the zone file places, seen from a camera straight above the viewpoint at a fixed height, on a flat background, with a five-pixel cross at the center.** What a player would see differs: no ground is drawn, only the zone's shapes, without distance shading; and the picture follows the camera the player is looking through, where ES2's follows the player's machine; and it is redrawn twice a second rather than every frame. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00036af0`–`00036eb3` and the initialized data `000939ee`–`00093a05`, between [the missile camera's](mfd-missile-camera.md#where-the-object-file-starts-and-ends) and [the weapon rows'](cockpit-weapon-gauges.md#where-the-object-file-starts-and-ends). The code opens with `HeapPtr_DtorNavMapCopy` (`00036af0`), a lone copy of the 40-byte destructor that opens each run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)), with no run after it; `es2_xref.py` finds no reference to it ([Open](#open)). It closes with the file's static initializer, `MfdMapScreen_StaticInit` (`00036e9c`), entry `000adc44` of the static constructor table, frameless and with no Ghidra function, whose `RET` at `00036eb3` stands right before the next file's run at `00036eb4` ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). No assert in the file names a source path.

The data is four zero bytes, `MFDNavMapVtable` (`000939f2`) and eight zero bytes. `MfdMapScreen_StaticInit` stores {`0xc000`, 0, 0} in the three `int16` of `MfdMapScreen_ViewAngles` (`000b13e0`), as ES2's `MfdMapScreen_StaticInit` (`004407f0`) does at `0049c454`: a pitch straight down.

## The screen object

**`MfdMapScreen_Ctor` (`00036b18`)**, (screen, rect), allocates `0x24` bytes when given none and runs `MFDisplay_Ctor` (`00039c50`), which stores the rect at `+0`. `MfdDisplay_Ctor` (`00039c64`, [building the display](mfd-display.md#building-it)) builds it as the screen of mode 2, at its `+0x9e`, with the rect `+0xb4` and the block `+0x7a` ([the status screen](mfd-status-screen.md#the-screen-object)).

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the rect, a pointer | `+0x04` |
| `+0x04` | the display's block, which the screen does not read | `+0x08` |
| `+0x08` | the vptr, `MFDNavMapVtable` | `+0x00` |
| `+0x0c` | the viewport | `+0x10` |
| `+0x10` | the camera | `+0x14`, the view |
| `+0x14`–`+0x20` | a copy of the rect | `+0x18`–`+0x24` |

It saves the installed render context (`000bdbda`), stores the vptr, builds the viewport with `GLViewport_Ctor`(0) ([the missile camera's](mfd-missile-camera.md#the-viewport-and-the-camera)), copies the rect to `+0x14`, sets the viewport's origin `+0x220`/`+0x224` to −((*x1* − *x0*) >> 1), −((*y1* − *y0*) >> 1) and its clip mode `+0x208` to 1, as ES2's does, and builds the camera with `TSCamera_Ctor`(0, `MfdMapScreen_ViewAngles`, position (0, 0, 0), near plane `0x80`, perspective shift 7, the viewport). It then reinstalls the context it saved. ES2's view takes shift 8 and near plane `0x100`. The head of the constructor holds the inlined constructors of the rect copy's two points, which write nothing ([constructors](../formats/compiler.md#constructors-and-destructors)).

`MFDNavMapVtable` holds a zero dword, then at `+4`, the paint slot, **`MfdMapScreen_PaintNoOp` (`00036c28`)**, a frameless `RET 4` with no Ghidra function, and at `+8`, the update slot, `MfdMapScreen_Paint` by its padding entry `00036c2b`. ES2's table holds its empty paint and the map's paint in the same two places ([`MFDMap`](../../retail/simulation/mfd.md#mfdmap--mode-2)). The display calls the update every 30 clock counts, half a second, while the screen is up, so the map first shows at the first of these after the switch to NAV MAP ([drawing the display](mfd-display.md#drawing-it)); ES2's draws it every frame. It calls it between `Cockpit_DrawFrame`'s canvas push and pop, so the installed render context is the canvas context, with its translation flag `+0x20c` set, and the cross sits at the rect's center.

## The paint

**`MfdMapScreen_Paint` (`00036c2c`)**, (screen):

1. It installs the brush {0, `0x17`} at the installed render context's `+0x22c` and fills the rect (`+0`) with `Raster_FillRect`. ES2's fills with `COLORS.DAT` id 19, and first returns while the view manager's view-change flag is set; ES1's has no such test.
2. It saves the context's rect `+0x210`–`+0x21c` and origin `+0x220`/`+0x224`, sets the viewport's rect to the rect's copy `+0x14` less that origin, and copies the viewport's rect and origin over the context's. ES2's installs the viewport itself, with the context's clip block and clip mode copied into it; ES1's draws into the context it found, its clip block and translation flag left as they were.
3. It takes the position of the active view object, `View_GetActiveObject` ([the camera](external-views.md#the-camera)): the camera goes to that object's `+0` and `+4`, at a height of 200000, with the angles (the first `int16` of `MfdMapScreen_ViewAngles`, the view object's `int16` `+0xe`, its `int16` `+0x10`), so pitched straight down and turned with the view's roll and heading. It installs the camera's projection (`Raster_InstallViewProjection`), whose center is the rect's center, then a null model transform (`Raster_SetModelTransform`) and the flat ramps of `WorldShades_ResetRamps` ([the ramps](../rendering/polygon-shading.md#the-zone-shapes)). With a focal length of 2⁷ = 128, a point at height 0 maps 1562.5 world units, about 9.4 m, to a pixel of the 320-wide screen, the scale of ES2's 781.25 to a device pixel of its 640-wide modes. ES2's camera stands over the cockpit's machine with pitch alone, and its heading turns the terrain bitmap instead ([`MFDMap`](../../retail/simulation/mfd.md#mfdmap--mode-2)).
4. It draws each object of `Zone_ShapeObjects` (`000bc584`), `Zone_ShapeCount` (`000bc588`) of them, stopping at a null, through slot `+4` of the table at its `+2`, `TSObj_RenderCopy` ([the zone shape object](../formats/zone-terrain.md#the-zone-shape-object)). ES2's blits the command display's terrain raster.
5. It installs the pen {0, `0x3c`} at the context's `+0x228` and plots five pixels with `Gfx_PlotPixel`: (0, −1), (0, 0), (0, 1), (−1, 0) and (1, 0), a plus sign at the origin it set, the rect's center. ES2's draws its cross in `COLORS.DAT` id 16 with two `Raster_DrawLine` calls, each from −(1 ≪ the video mode's shift) to 1 ≪ it.
6. It puts back the context's rect and origin and copies the rect to the display with `Widget_DrawToCockpit`(0, rect, 1), as ES2's does.

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorNavMapCopy` | `00036af0` | the code of `HeapPtr_DtorPanelCopy` (`0002acdc`) |
| `MfdMapScreen_Ctor`, `MfdMapScreen_PaintNoOp`, `MfdMapScreen_Paint` | `00036b18`, `00036c28`, `00036c2c` | `00440494`, `004405dc`, `004405e4` |
| `MfdMapScreen_StaticInit` | `00036e9c` | `004407f0` |

## Globals

| | Address |
|---|---|
| `MFDNavMapVtable`: a zero dword, then the paint and the update | `000939f2` |
| `MfdMapScreen_ViewAngles`, three `int16` | `000b13e0` |

## Open

- **Open:** what calls `HeapPtr_DtorNavMapCopy`, to which `es2_xref.py` finds no branch or stored pointer.
- **Open:** whether the view object's roll is ever nonzero while the MFD is drawn, which would turn the map about its center.
- **Open:** what the colors `0x17` and `0x3c` look like in the theaters' palettes.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: with NAV MAP (F3) up, `BP <CS>:00036c2c` fires about twice a second, and the screen shows the zone's shapes from above with a cross at its center and no ground.
