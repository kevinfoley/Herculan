# DBSIM.EXE — the MFD's missile camera screen

**ES1's MISSILE CAM screen is ES2's ([`MFDMissileView`](../../retail/simulation/mfd.md#mfdmissileview--mode-5)): while one of the player's guided rounds is in flight it shows the world from beside the round, and otherwise a `READY TO LAUNCH` or `NONE` caption over a `LOCK` legend that blinks while the gunsight has lock.** What a player would see differs: the view has a banded sky and ground with a skyline behind the shapes and no crosshair or ring over it; the shapes are drawn without distance shading; the screen flashes whenever any explosion goes off near the camera, for as long as that explosion lasts, changing color every frame; and the captions sit higher and further left. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts and ends

The code is `00036024`–`00036aef` and the initialized data `0009388e`–`000939ed`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `00036024`, `0003604c`, `0003608b` (frameless, with no Ghidra function) and `00036094`, each the code of `panel.cpp`'s ([where `panel.cpp` starts](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `0009388e` and `000938c2`.

The file's static initializer is not its last code. `MfdMissileView_StaticInit` (`00036a20`), entry `000adc40` of the static constructor table, and its exit routine `MfdMissileView_StaticDtor` (`00036a54`), entry `000adcbc` of the exit table, are followed after one `0x90` byte by the three destructors of the class the initializer builds ([the clip region](#the-viewport-and-the-camera)), whose last `RET 8` at `00036aeb` is followed by two zero bytes ([where object files meet](../formats/compiler.md#where-object-files-meet-in-the-code)). `00036af0` is another lone copy of the 40-byte destructor, which opens the next file: the NAV MAP screen's, whose constructor `00036b18` the display builds as mode 2 ([Open](#open)). No assert in the file names a source path.

## The screen object

**`MfdMissileViewScreen_Ctor` (`000360c0`)**, (screen, rect), allocates `0x41` bytes when given none and runs `MFDisplay_Ctor`. The display's constructor (`00039c64`) builds it as the screen of mode 5, at its `+0xaa`, with the rect `+0xb4` and the block `+0x7a` ([the status screen](mfd-status-screen.md#the-screen-object)).

| Offset | Holds | ES2's |
|---|---|---|
| `+0x00` | the rect | `+0x04` |
| `+0x04` | the display's block | `+0x08` |
| `+0x08` | the vptr, `MFDMissileViewVtable` (`0009391a`) | `+0x00` |
| `+0x0c` | the viewport | `+0x10` |
| `+0x10` | the camera | `+0x14` |
| `+0x14` | the horizon, an `hzline` | — |
| `+0x18` | the explosion the strike flash is showing, or 0 | — |
| `+0x1c` | the flash's color toggle, a byte | `+0x20` |
| `+0x1d` | the camera's last position, three dwords | — |
| `+0x29` | its last pitch, roll and heading, three `int16`s | — |
| `+0x2f`–`+0x3b` | labels 0 to 3 | `+0x33`–`+0x3f` |
| `+0x3f` | the repaint counter, a byte | `+0x43` |
| `+0x40` | the lock blink's latch, a byte | `+0x44` |

### The viewport and the camera

**The screen draws into a viewport of its own, centered on its rect, as ES2's does.** `GLViewport_Ctor` (`00069a84`), (viewport), allocates `0x239` bytes when given none, stores `0009b40a`, `0009b42e` and `0009b456` in turn at `+0`, constructs the rect at `+0x210`, copies `0x8b` dwords from the viewport the GL display's (`000bdd28`) `+0x40` points at over its own from `+4`, and clears the byte `+0x230` and the dwords `+0x231` and `+0x235`; `es2_xref.py` finds ten calls of it. The screen's constructor stores the rect at the viewport's `+0x210`–`+0x21c`, where ES2's takes one column off its right edge, and the origin `+0x220`/`+0x224` at −(*w* >> 1), −(*h* >> 1). It sets up the clip block `000b11c4` with one region, the pointer `000b13cc`, and the rect at `000b13d0`–`000b13dc`, sets the viewport's clip mode `+0x208` to 2, copies the `0x204`-byte block over the viewport's `+4` and clears its `+0x20c`.

`000b13cc` is `+4` of `MfdMissileView_ClipRegion` (`000b13c8`), a `GLRectangleRegion` the file's static initializer builds: `MfdMissileView_StaticInit` stores the copies `00093956`, `0009397a`, `000939a2` and `000939ca` of the class tables at `000b13c8` in turn and clears `000b13cc`, and `MfdMissileView_StaticDtor` stores `000939ca`, `000939a2` and `0009397a` in turn. Both are frameless with no Ghidra function. The class's destructors follow them, (object, flags), each storing its table and its bases' at `+0` and freeing the object when bit 0 of flags is set: `GLBase_DtorMissileViewCopy` (`00036a74`, slot `+0x1c` of `0009397a`), `GLRegion_DtorMissileViewCopy` (`00036a98`, of `000939a2`) and `GLRectangleRegion_DtorMissileViewCopy` (`00036ac0`, of `000939ca`, whose slot `+4` returns the class tag `0x28000c`), the code of [the gadget file's copies](cockpit-widgets.md#the-gadget-bases).

The camera is a `0x4a`-byte [camera object](external-views.md#the-camera): `TSCamera_Ctor`(camera, orientation (0, 0, 0), position (0, 0, 0), `0x80`, 7, the viewport), the inlined constructors of its five triples `+0x26` to `+0x44`, the vptr `0009392e` at `+0x12`, and `+0x32`, `+0x36` and `+0x44`–`+0x48` cleared. ES2's camera takes the same perspective shift 7 and near plane `0x80` ([`MFDMissileView`](../../retail/simulation/mfd.md#mfdmissileview--mode-5)). `0009392e` holds one slot, `Cam_DtorMissileViewCopy` (`00036498`), (camera, flags), which stores `0009392e` and then `00093946` at `+0x12`, runs `TSContext_Dtor`(camera, 0) and frees the camera when bit 0 of flags is set; `00093946` is this file's copy of `TSCamera`'s table, whose slot is `TSCamera_DtorMissileViewCopy` (`000364cc`), the code of `TSCamera_Dtor` (`0005b970`) with that table.

The horizon is an `hzline` (`Hzline_Ctor`) with band height `+0x54` 1, band count `+0x58` 3, ground band height `+0x5c` 1 and ground band count `+0x60` 3, `Hzline_SetColors`(horizon, line offset 0, ground `0x16`, zenith `0x10`), the skyline bank `dba\smlwld2` (`Hzline_LoadBitmapBank`) and the byte `+0x70` cleared, so no textured ground ([ES1's horizon](../rendering/ground-and-sky.md#the-horizon-line-and-the-sky), [ES2's fields](../../retail/rendering/distance-fog-and-sky.md#the-object)).

### The labels

Four `Label_Ctor`s in `ColorSchemePanels`' `cpred`, centered by `Label_SetRect` with zero margins, each at the rect's top-left plus (*x*, *y*), *w* wide and *h* tall, from four `int16` tables, `000938f4`, `000938fc`, `00093904` and `0009390c`; label 2 then takes `cpylw` and the fill color `0x31`:

| Label | *x*, *y* | *w* × *h* | Text |
|---|---|---|---|
| 0 | 0, 1 | `0x44` × `0xa` | `READY TO` |
| 1 | 0, 8 | `0x44` × `0xa` | `LAUNCH` or `NONE` |
| 2 | `0x1b`, `0x14` | `0xe` × `0xa` | none written ([Open](#open)) |
| 3 | `0x16`, `0x20` | `0x18` × `0xa` | `LOCK` |

The widths and heights are ES2's; ES2's sit 15 further right, labels 0 and 1 20 lower and 2 and 3 10 lower, each coordinate shifted by the video mode.

## Painting

**`MfdMissileViewScreen_Update` (`000368cc`)**, (screen), slot `+8`, is ES2's on ES1's fields: it raises the counter `+0x3f` to 2 while there is a tracked round (`MfdMissileView_TrackedRound`, `00093914`), a flash (`+0x18`) or lock, the byte `+0x9f` of the gunsight instrument (the cockpit's `+0x1f1`), which is its state block's byte `0x25` ([the state block](cockpit-gunsight-hud.md#the-state-block)); at 2 it runs the paint, slot `+4`, and at 1 `Widget_DrawToCockpit` over the rect, lowering the counter either way.

**`MfdMissileViewScreen_Paint` (`000364f8`)**, (screen), takes the first of these that applies:

1. **The strike flash.** With `+0x18` set, it walks `g_ExplosionPool` with `Pool_Prev` for that explosion. Found, it flips the byte `+0x1c`, fills the rect with the brush {0, `0x3c`} when the byte is 1 and {0, `0x3f`} when it is 0, and returns. Gone, it clears `+0x18`.
2. **The view.** With a tracked round, it walks the round pool (`000af0b4`) for it. Gone, it clears `MfdMissileView_TrackedRound` and takes the camera's pose from `+0x1d` and `+0x29`, where the last paint left it. Found, the pose is the round's position `+0x20`–`+0x28` and its pitch, roll and heading `+6`–`+0xa`, with Q14(500, cos *h*) added to *x* and Q14(500, cos(*h* − `0x4000`)) to *y*, *h* the heading: ES2's push of 500 units to the round's right. It saves the pose at `+0x1d` and `+0x29`. Then it walks `g_ExplosionPool` for an explosion whose `+0x20` lies within 10000 of the pose (`Point3I_DistanceBetween`); for the first, it stores it at `+0x18`, sets `+0x1c` to 1, fills the rect with {0, `0x3c`} and returns. With no such explosion and the round still tracked, it takes `CockpitView_GetShakeOffset`'s pair out of the viewport's rect, installs the viewport (`Raster_InstallRenderContext`), gives the camera the pose (position `+0`, angles `+0xc`), installs its projection (`Raster_InstallViewProjection`), draws the horizon with `Hzline_Draw`(horizon, 0, camera), sets `000928ea`, draws the objects with `ObjList_DrawFrameObjects`(0, camera), clears `000928ea`, puts back the render context it found and adds the pair back. While `000928ea` is set, `WorldShades_ApplyForObject` resets the ramps rather than shading an object by its distance.
3. **The labels**, with no tracked round: it fills the rect with {0, `0x3f`}; when `CockpitView_SumLauncherCounts` returns 0, label 1 reads `NONE`, and otherwise label 0 reads `READY TO` and label 1 `LAUNCH`. Label 3 reads `LOCK`, in `cpblue` with the fill `0x30` without lock, and with it in `cpylw` with the fill `0x3f` while bit `0x20` of the clock `000981c4` is clear and `0x35` while it is set. The words are `STRINGS0.STR` group 31 entries 1 to 4 ([the groups](../formats/str-strings.md#the-groups)).

ES2's flash is raised by the round ending short of its lifetime and lasts `0x1e` coarse ticks, alternating `COLORS.DAT` ids 19 and 16; its view floods the rect, draws the terrain with its own shading forced, and draws a crosshair and a ring over it ([`MFDMissileView`](../../retail/simulation/mfd.md#mfdmissileview--mode-5)).

**`CockpitView_SumLauncherCounts` (`00036930`)**, (screen), which it reaches by its padding entry `0003692e`, does not read its argument: over the cockpit's weapon rows (`+0x70`, count `+0x94`), for each row whose mount `+0x86` is set, it calls the mount's slot `+0x64` with (mount, &*n*) and adds the `int16` *n* when the call returns other than 5. ES2's (`00440348`) reads the count at `+0x98`, the mount at `+0xbd` and slot `+0x60`, and calls `_strlen` on a string first.

**`MfdMissileViewScreen_BlinkLockLabel` (`000369a8`)**, (screen), is ES2's on ES1's fields: when bit `0x20` of the clock `000981c4` differs from the latch `+0x40`, it stores it there and gives label 3 `cpylw` and the fill `0x35` (bit set) or `0x3f` (clear), returning 1; otherwise 0. `es2_xref.py` finds no reference to it, as for ES2's ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMissileViewCopy`, `ConfigPart_DtorMissileViewCopy`, `ConfigPart_GetClassTagMissileViewCopy`, `TSPartBase_DtorMissileViewCopy` | `00036024`, `0003604c`, `0003608b`, `00036094` | the run; slots `+0x1c` and `+4` of `0009388e` and `+0x1c` of `000938c2` |
| `MfdMissileViewScreen_Ctor`, `MfdMissileViewScreen_Paint`, `MfdMissileViewScreen_Update` | `000360c0`, `000364f8`, `000368cc` | `0043facc`, `0043fe1c`, `004402ec` |
| `Cam_DtorMissileViewCopy`, `TSCamera_DtorMissileViewCopy` | `00036498`, `000364cc` | — |
| `CockpitView_SumLauncherCounts`, `MfdMissileViewScreen_BlinkLockLabel` | `00036930`, `000369a8` | `00440348`, `004403bc` |
| `MfdMissileView_StaticInit`, `MfdMissileView_StaticDtor` | `00036a20`, `00036a54` | — |
| `GLBase_DtorMissileViewCopy`, `GLRegion_DtorMissileViewCopy`, `GLRectangleRegion_DtorMissileViewCopy` | `00036a74`, `00036a98`, `00036ac0` | — |
| `GLViewport_Ctor`, in the library region | `00069a84` | — |

## Globals

| | Address |
|---|---|
| the label tables: *x*, *y*, *w*, *h* | `000938f4`, `000938fc`, `00093904`, `0009390c` |
| `MfdMissileView_TrackedRound` | `00093914` |
| `MFDMissileViewVtable`: a zero dword, then the paint and the update | `0009391a` |
| the camera's table and this file's copy of `TSCamera`'s | `0009392e`, `00093946` |
| the skyline bank's name, `smlwld2` | `0009393a` |
| the copies of the clip region's class tables | `00093956`, `0009397a`, `000939a2`, `000939ca` |
| the clip block and `MfdMissileView_ClipRegion` | `000b11c4`, `000b13c8` |

## Open

- **Open:** what tracks a round: `es2_xref.py` finds the one store to `MfdMissileView_TrackedRound` at `00007843`, in `00007748`, read only around it: it stores the object `000074e4` returned when the byte `+0x9d` of the function's second argument is set (ES2's piloted-by-the-player byte `+0xa3` less 6).
- **Open:** what label 2 shows: the paint writes labels 0, 1 and 3 alone, as ES2's does.
- **Open:** a caller of `MfdMissileViewScreen_BlinkLockLabel`: `es2_xref.py` finds no branch or stored pointer to it or its padding.
- **Open:** what the mount's slot `+0x64` returns and writes, and so which rows `CockpitView_SumLauncherCounts` counts.
- **Open:** whether `dba\smlwld2` ships, and what the colors `0x10`–`0x18`, `0x31`, `0x3c` and `0x3f` look like in the theaters' palettes.
- **Open:** the NAV MAP screen's object file, from `00036af0` to the `RET` of its static initializer `00036e9c` (entry `000adc44`) at `00036eb3`, unread beyond its constructor's address and its table `000939f2`; the next file opens at `00036eb4`.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: fire a guided round with the MISSILE CAM screen up; `BP <CS>:000364f8` fires each frame with the dword at `00093914` non-zero, and the screen shows the view from beside the round with a banded sky.
