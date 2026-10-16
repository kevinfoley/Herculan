# DBSIM.EXE — the gunsight HUD

**ES1's front-window HUD is built from ES2's gunsight children ([the gunsight complex](../../retail/simulation/cockpit-gunsight-hud.md)): sliding bars that track an angle, a compass tape, and a clickable surface over the view.** Two differences a player could see: ES1 draws both of its sliding bars, the horizontal rotation indicator and a vertical one, each from a bitmap bank of its own, where ES2's vertical bar paints nothing; and a click on the gunsight surface arms the pixel pick that selects the object under a point, which ES2's click leaves unarmed. This page covers the object file from its start to `00033c20`; the rest is [Open](#open). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Where the object file starts

The code starts at `00033740`, after [the status screen's](mfd-status-screen.md#where-the-object-file-starts-and-ends) zero byte, and the initialized data at `00093662`. Both open with the copy of `ConfigPart`'s destructors and tables that starts each object file which carries one ([module copies](../formats/compiler.md#vtables)): `00033740`, `00033768`, `000337a7` (frameless, with no Ghidra function) and `000337b0`, each the code of `panel.cpp`'s ([where `panel.cpp` starts](cockpit-panel.md#where-the-module-starts-and-ends)), and the tables `00093662` and `00093696`. The file holds ES2's `Gunsight_SetValues` (`00035090`) and `Gunsight_GetStateBlock` (`00035180`) ([callees of other modules](cockpit-panel.md#callees-of-other-modules)); where it ends is [Open](#open).

## The slide bars

**ES1's sliding bars are ES2's rotation gauges ([rotation indicator](../../retail/simulation/cockpit-gunsight-hud.md#rotation-indicator)) with the child's rect at `+0` and the vptr at `+0x10`.** The fields from `+0x24` are ES2's: the limits at `+0x24` and `+0x26`, the travel (the rect's width or height) at `+0x28`, the range at `+0x2c`, the track's point at `+0x2e` and `+0x32`, and the value at `+0x3a`.

- **`GunsightChild_CtorBase` (`000337fc`)**, (child, rect), copies the rect to `+0`; the allocation at its head is the rect's own constructor inlined, as in [`PanelGauge_Ctor`](cockpit-widgets.md#the-gauge-base). ES2's also stores a vtable, with only pure-virtual slots.
- **`HudSlideBar_CtorBase` (`0003385c`)**, (bar, rect), stores `RotationGaugeVtable` (`000936de`) and sets ES2's limits ±`0x4800`, range and middle value. **`HudSlideBar_SetLimits` (`000338cc`)**, (bar, max, min), and **`HudSlideBar_AddDelta` (`00033910`)**, (bar, delta), are ES2's; the table's three slots before them are 0, where ES2's hold `Rtl_PureVirtualCalled`.

Each subclass loads its bank once from the folder `000980f8` names and draws from it:

| | Horizontal: the rotation indicator | Vertical |
|---|---|---|
| Constructor, `0x3c` bytes | `HudRotationIndicator_Ctor` (`00033968`) | `HudSlideBarV_Ctor` (`00033ac4`) |
| Bank | `hudhslid`, `HudHSlidBank` (`000936c8`) | `hudvslid`, `HudVSlidBank` (`000936cc`) |
| Recompute: travel; track point | `HudRotationIndicator_Recompute` (`000339dc`): *x1* − *x0*; (*x0*, *y0* − 1) | `HudSlideBarV_Recompute` (`00033b38`): *y1* − *y0*; (*x0*, *y0*) |
| Paint | `HudRotationIndicator_Paint` (`00033a04`) | `HudSlideBarV_Paint` (`00033b60`) |
| Bar position *p* | (*p*, *y0*) | (*x0*, *p*) |
| *p* | *x0* + *t* − 15 | *y0* + *t* − 7 |
| Frame 2 while the value lies strictly within | ±300 | ±400 |

With *t* = ((value − min) & `0xffff`) × travel / range, unsigned, plus 1 when the value is negative, each paint blits frame 0, the track, at the track point and then the bar, frame 2 near the middle and frame 1 elsewhere, with `Bitmap_Blit` inside `Cockpit_PushCanvasContext` and `Cockpit_PopRenderContext`. ES2's rotation indicator draws `HUD` frames 11, 13 and 12, lifts its track `2 << YCoordShift` and takes `15 << XCoordShift`; ES2's vertical class, `HudSlideBar_CtorHidden`'s, has empty paint and recompute slots. Each class's repaint, **`HudRotationIndicator_Repaint` (`000339c4`)** and **`HudSlideBarV_Repaint` (`00033b20`)**, calls its paint, slot `+4`. The tables are `HFixRotationGaugeVtable` (`000936fe`) and `VFixRotationGaugeVtable` (`00093726`), ES2's in order: paint, repaint, recompute, add delta, set limits.

**The builder.** The gunsight's builder `00034aec`, read only around its calls of these constructors, builds after the click surface (`00034ba7`) the heading tape (`00034c43`, limits ±`0xe38`), the rotation indicator (`00034c87`) over the heading tape's rect moved in 15 at each end and down 10, limits ±`0x38e3`, and the vertical bar (`00034cd7`) over a rect of its own from the layout block it is given (`+0x20`), limits ±`0x1f40`, and hands each of the three to `00034e94`. `HudRotationIndicator_Ctor` is also the base of the heading tape's constructor (`00033c20`) and is called from `0003477b`.

## The click surface

**`Gunsight_ClickSurface_Ctor` (`000345bc`)**, (child, rect), is ES2's ([the gunsight complex](../../retail/simulation/cockpit-gunsight-hud.md#front-window-hud--the-gunsight-complex), child 0) on ES1's layout: `0x28` bytes, built on **`GunsightChild_CtorWithFields` (`00033ed8`)**, (child, rect), which adds the dwords 3, 6, `0xa0` and `0x3a` at `+0x14`–`+0x20` ([Open](#open)), with `HUDRovingGunsightVtable` (`0009379e`) at `+0x10`. Its `0x20`-byte gadget, kept at `+0x24` and registered with `Widget_RegisterClickable` on the cockpit's clickable list, takes `Widget_CtorRect`'s rect and then `000937b2` and `HUDRovingGunsightGadgetVtable` (`000937da`) at its `+0x1c`.

**`Gunsight_ClickGadget_OnClick` (`000337dc`)**, (gadget, flags), is that gadget's click slot (`+0xc`): with bit 0 of flags set, it sets the byte `Gunsight_PickArmed` (`000936d8`) and arms [the pixel pick](terrain.md#the-pixel-pick-and-the-occlusion-probe) with `ObjPick_Arm` at the point `000b16d4`, which the widget input code stores (`00044564`, `0004466d`, `00044755`). ES2's slot, `Gunsight_ClickGadget_OnClickNoOp` (`0043b338`), tests the same bit and returns, and `es2_xref.py` finds no reference to ES2's `ObjPick_Arm` ([ES2's pick](../../retail/rendering/terrain-drawing.md#the-pixel-pick-and-the-occlusion-probe)). What `00034f3c` does with the picked object is in [ES1's pick](terrain.md#the-pixel-pick-and-the-occlusion-probe).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorGunsightCopy`, `ConfigPart_DtorGunsightCopy`, `ConfigPart_GetClassTagGunsightCopy`, `TSPartBase_DtorGunsightCopy` | `00033740`, `00033768`, `000337a7`, `000337b0` | the run; slots `+0x1c` and `+4` of `00093662` and `+0x1c` of `00093696` |
| `Gunsight_ClickGadget_OnClick`, `Gunsight_ClickSurface_Ctor` | `000337dc`, `000345bc` | `0043b338`, `0043c120` |
| `GunsightChild_CtorBase`, `GunsightChild_CtorWithFields` | `000337fc`, `00033ed8` | `0043b344`; `00033ed8` none |
| `HudSlideBar_CtorBase`, `HudSlideBar_SetLimits`, `HudSlideBar_AddDelta` | `0003385c`, `000338cc`, `00033910` | `0043b378`, `0043b3c0`, `0043b3f8` |
| `HudRotationIndicator_Ctor`, `HudRotationIndicator_Repaint`, `HudRotationIndicator_Recompute`, `HudRotationIndicator_Paint` | `00033968`, `000339c4`, `000339dc`, `00033a04` | `0043b438`, `0043e685`, `0043b460`, `0043b4a4` |
| `HudSlideBarV_Ctor`, `HudSlideBarV_Repaint`, `HudSlideBarV_Recompute`, `HudSlideBarV_Paint` | `00033ac4`, `00033b20`, `00033b38`, `00033b60` | `0043b54c`, `0043e677`, `0043b56c`, `0043b574` |

## Globals

| | Address |
|---|---|
| `HudHSlidBank`, `HudVSlidBank` | `000936c8`, `000936cc` |
| `Gunsight_PickArmed`, a byte | `000936d8` |
| `RotationGaugeVtable`, `HFixRotationGaugeVtable`, `VFixRotationGaugeVtable` | `000936de`, `000936fe`, `00093726` |
| `HUDRovingGunsightVtable`, `HUDRovingGunsightGadgetVtable` | `0009379e`, `000937da` |

## Open

- **Open:** the rest of the object file from `00033c20`, unread: the heading tape (`00033c20`, `00033c7c`, `00033d08`, `00033dd4`, `00033e94`, `00033eac`, its table `0009374e`), the classes built on `GunsightChild_CtorWithFields` (`00033f1c` with `00033f4c` and `00033f64`, table `00093776`; `0003403c` with `000340a8` and `000340c0`, table `0009378a`), the click surface's paint and repaint (`000346a4`, `00034660`) and the gadget's other slots (`000937b2`, `000937da`), and `000345a0`, `00034710`–`00034f3c`, `0003518c`, `00035270`. ES2's counterparts are the rest of [the gunsight complex](../../retail/simulation/cockpit-gunsight-hud.md); where the file ends is part of this.
- **Open:** what the dwords `GunsightChild_CtorWithFields` stores at `+0x14`–`+0x20` are; ES2's children have no such fields.
- **Open:** which child slots `Gunsight_SetValues` hands the twist's and the pitch's deltas to (`+0xa8` and `+0xac`), and so whether the vertical bar tracks the pitch, as ES2's hidden one does. That follows if `00034e94` appends to an array at `+0xa0`.
- **Open:** the art of `hudhslid` and `hudvslid`, what their frames 1 and 2 show; what the point `000b16d4` holds is in [ES1's pick](terrain.md#open).
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:000337dc` fires on a left click over the gunsight surface with bit 0 of the dword at `[ESP+8]` set, and `BP <CS>:00033b60` fires as the cockpit draws, showing that the vertical bar is painted.
