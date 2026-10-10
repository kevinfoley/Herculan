# The HUD target indicator

The front window's target box, its off-screen arrow, and the reticle's on-target frames. Children 4 and 5 of the gunsight complex — see [`cockpit-gunsight-hud.md`](cockpit-gunsight-hud.md) for the complex itself and [`target-selection.md`](target-selection.md) for what makes a selection.

## Where the selection reaches the HUD

| Step | Symbol | What it does |
|---|---|---|
| 1 | `Player_PerFrameCockpitUpdate` (`0041b130`) | Computes the target's world aim point via `Player_ResolveTargetAimPoint` (`0041b728`) and parks it at `CockpitView+0x26c`..`+0x27e` (`CockpitView_SetTargetBlock`, `00434a24`) |
| 2 | `Gunsight_UpdateAndPaint` (`0043d6dc`, gunsight vtable `+8`) | Reads that block back into the gunsight's own state block at `+0xb1` |
| 3 | `Gunsight_SetValues` (`0043d98c`) | Copies the state block into children 4 and 5 (`+0xe7`, `+0xeb`) |

The 38-byte state block, offsets from the gunsight's `+0xb1` and from a child's `+0x14`:

| Offset | Field |
|---|---|
| 0, 2, 4 | Machine heading, turret twist, turret pitch |
| 6 | Selected object, or 0 |
| 10, 14, 18 | Its world aim point |
| 22 | The target's own heading |
| 24 | Whether a Targeting Pod has singled out a component — `CockpitView+0x27c`, the pod's **present flag**. The component id beside it at `+0x27e` never reaches the gunsight; only the MFD reads that |
| 28 | The target's shape radius (vtable `+0x10`), which sizes the box |
| 32 | Literal 2000, unread |
| 36 | **Indicator armed.** Set to 1 by all three selection entry points and never cleared; the box's paint refuses to draw until it is set |
| 37 | `mech+0x9b`, missile lock |

`Player_ResolveTargetAimPoint` has two branches. With a Targeting Pod fitted (`mech+0x30b`) and the target inside 30000 units — 180 m, the manual's "close range" — it hands off to `TargetingPod_ResolveAimPoint` (`0040e4dc`) for a component aim point and a component id; otherwise it takes the target's vtable `+0x24` aim node and writes 0 to the component id. The pod side is [`target-selection.md`](target-selection.md#component-targeting--the-targeting-pod)'s.

## Child 4 — the reticle

`GunsightChild_CtorBase` plus vtable `0049c124`, painted by `Gunsight_ReticlePaint` (`0043b7e0`). Blitted centred on the `.GAU`'s reticle point (offset 1136), which is also the `.VUE` projection centre. Its own rect (that point ± offset 1144) is never read.

`HUD` bank frame, in the paint's own order of tests:

| Frame | When |
|---|---|
| 0 | Nothing selected, the target is behind the near plane, or it projects further than `5 << CoordShift` from the reticle on either axis |
| 2 | It projects inside that tolerance |
| 1 | …and the armed missile mount has lock |

## Child 5 — the box and the arrow

`Gunsight_TargetIndicator_Ctor` (`0043b928`) ctor, painted by `Gunsight_TargetIndicatorPaint` (`0043b950`). Two independent tests on the same projected point; either, both or neither piece is drawn.

The aim point is transformed to view space (`Raster_ModelToView`, `0048c470`), projected (`Raster_PerspectiveDivide`, `Raster_ProjectToScreen`) and compared against the reticle point. A depth inside the view's near plane (`view+0x1e`) marks it **behind**: no box, and the arrow's direction comes from re-projecting the synthetic view-space point `(±10000, 1024, 0)` whose sign is the real point's own view-space x — 5000 device pixels to one side of the reticle on its own row, so the arrow points level left or right.

### The box

Drawn when the target is in front **and** further than `5 << CoordShift` from the reticle — the span over which child 4 shows its on-target frame instead, so the two never coexist.

```
halfHeight = (shapeRadius >> 1) * focal / distance     -- focal = 1 << view+0x1a; distance is
                                                          eye to aim point, Math_GroundDistanceBetweenPoints (004927c4)
top    = screenY - halfHeight
bottom = screenY + halfHeight
height = clamp(bottom - top, 25, 75)                   -- both edges moved, integer halving
box    = (screenX - height/2, top, screenX + height/2, bottom)
```

The 25/75 clamp is **not** coordinate-shifted, unlike the tolerance beside it. Transcribed as-is.

Four `HUD` bank frames, base 3 unlocked and base 7 when `mech+0x9b` is set:

| Frame | Unlocked size | Locked size | Placement |
|---|---|---|---|
| base+0 | 23x23 | 21x21 | Pip, centred on the target |
| base+1 | 12x12 | 12x12 | Corner bracket, blitted four times with blit flags 0/2/1/3 into the box's corners |
| base+2 | 7x1 | 8x1 | Tick at the box's left and right edges, on the **target's** row |
| base+3 | 1x7 | 1x7 | Tick above and below, on the target's column |

The minimum box side of 25 is two corner brackets plus one pixel, so at its smallest the box closes into an unbroken frame.

The brackets and ticks are drawn only when state-block offset 24 is 0. A Targeting Pod that has singled out a component reduces the box to the bare pip.

### The arrow

Drawn when the projected point is not inside the `.GAU`'s gunsight area (offset 1148) — or whenever the target is behind. Every retail file places that rect well inside the canopy's window opening ([per herc](../formats/gau-cockpit-layout.md#per-herc-values)), which is what keeps the arrow off the cockpit frame.

The apex sits where the ray from the reticle to the target crosses that rect's border: solve for y on the vertical border the target is on, and if that lands outside the rect solve for x on the horizontal one instead. The base is `10 << YCoordShift` back down the ray, `(6 << YCoordShift) / 2` to either side. (The original builds that triangle about the origin and rotates it by the crossing's bearing less a quarter turn — `Math_Atan2Bam` (`0047d220`) then `Math_BuildRotation2D` (`0047ea24`) — which comes to the same thing. Both literals use the *vertical* shift on both axes, with no effect in any retail video mode.)

It is a flat-filled polygon, the only piece of the indicator that is not a sprite: `COLORS.DAT` id 12 (palette 14, green), or id 9 (palette 10, red) with lock.

## Why the box goes behind the cockpit frame

**The box is the one HUD element the canopy covers**, and that is a property of the render context it is drawn through rather than of draw order.

A render context (`0x239` bytes) carries a clip block at `ctx+4`, which `Raster_InstallRenderContext` (`00480c38`) installs as `PTR_DAT_004a362c`. Its mode sits at `ctx+0x208`: 0 none, 1 a single rect at `ctx+0x210`, 2 the region list the block itself holds. While `ctx+0x20c` is 1, `Bitmap_BlitClipDispatch` (`004886cc`) first moves every blit by the rect's top-left less the origin at `ctx+0x220`/`+0x224`. Two contexts matter:

| Context | Built by | Clip |
|---|---|---|
| `CockpitViewInstance+4` | `Gau_BuildCockpitWidgets` (`00431bf8`), through `Cockpit_CreateCanvasContext` (`00430ea0`) | Mode 1, rect = the whole cockpit canvas, translation on |
| The one under it | `CockpitView_ApplyViewState` (`00429e60`) loads the current view's `0x204`-byte block into it | Mode 2, regions = the herc's `.HD`/`.ED` canopy cutout (see [`../formats/hd-ed-clip-regions.md`](../formats/hd-ed-clip-regions.md)) |

`Cockpit_PushCanvasContext` (`004311e0`) pushes the current context and installs the canvas one; `Cockpit_PopRenderContext` (`00431210`) pops. Every widget paint runs inside such a pair, which is why the console instruments — outside the canopy cutout — can draw at all.

The mode reaches the pixels through both of `Bitmap_BlitClipDispatch`'s (`004886cc`) blitters:

- A type-1 bitmap (transparent RLE, `bitmap+5 & 0xf`) goes to `Bitmap_BlitTransparent` (`00488cec`), which rejects against the context's rect, then sets a per-call flag from `clipMode == 2` and sends every pixel run it emits to the region-clipped span writer (`g_RasterRoutines_SpanCopyRegion` (`004a5820`) / `g_RasterRoutines_SpanFillRegion` (`004a5828`)) rather than the plain one (`g_RasterRoutines_SpanCopy` (`004a581c`) / `g_RasterRoutines_SpanFill` (`004a5824`)).
- Any other bitmap goes, in modes 1 and 2, to `g_RasterRoutines` slot 28 (`004a5834`) with the context's rect. Driver 3's slot 28, `Driver3_BlitClippedRegion` (`00489dd2`), reads the mode itself: in mode 1 it blits clipped to that rect, and in mode 2 it ignores the rect and blits once per region — clipped to a rect region's rect, and for a span region once per row, clipped to that row's span.

So a bitmap drawn in mode 2 is cut to the regions scanline by scanline — following an A-pillar's slope, not a rectangle — whichever blitter draws it.

`ActiveScanlineClipSpans` is a different mechanism for the same regions, flattened per scanline; its only readers are the polygon rasterizers.

**Child 5's paint is the only widget that opts in.** It calls `Cockpit_PopRenderContext` before the box's blits and `Cockpit_PushCanvasContext` after, so the box alone is drawn in the canopy-clipped context. The reticle, the heading tape, the rotation indicator, the readouts and the arrow all stay in the canvas context and are never cut. Confirmed against `Reference/Targeting 2.png`, where the box's right half is cut along the right A-pillar while the arrow beside it is whole.

## Open

- **Deferred:** whether DBSIM ever draws a type-1 bitmap. Driver 3's span writers, `Driver3_SpanCopy` (`0048a382`) and `Driver3_SpanFill` (`0048a4a7`), index the row table with `EAX` after `MOV AX,DS` has replaced the low word of the row they loaded, so `Bitmap_BlitTransparent`'s runs would land on the row numbered `(y & 0xffff0000) | DS` rather than on row y.
