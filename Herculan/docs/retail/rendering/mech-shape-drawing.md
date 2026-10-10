# How a machine's shape reaches the screen (DBSIM.EXE)

Everything below sits *above* [`dts-node-posing.md`](dts-node-posing.md), which starts at `TSGroup_RenderPolys`. Two of the three mechanisms here rewrite the shape before a poly is ever drawn, so a reader who starts at the poly renderer will conclude the file's geometry is what appears on screen. For a machine it is not.

## The chain

| Address | Role |
| --- | --- |
| `0042841c` | `Scene_SubmitFrameObjects` — files every object into `ObjList::drawTable` with its terrain cell. It makes no visibility decision for a machine beyond the deployment gate ([`../simulation/mission-deployment.md`](../simulation/mission-deployment.md#the-deployment-gate--group0x14)) |
| `0042883c` | `ObjList_DrawAfterTerrain` — calls vtable `+0x00` of each object in the no-cell bucket, after the terrain walk ([`terrain-drawing.md`](terrain-drawing.md#after-the-walk--objlist_drawafterterrain-0042883c)) |
| `004174c8` | `Mech_Draw`, the mech's `+0x00`: splices the hardpoints, then draws |
| `004033e4` | `Shape_DrawAtDetailLevel` — picks the LOD root, installs the transform, calls the shape instance's `+0x1c` |
| `00401fe4` | `SimObject_InstallModelTransform` — builds the Q14 euler matrix if dirty, installs it |

## The LOD root is chosen per frame, per object

A machine's `.DTS` roots are **complete alternate models**, not parts of one: `SAMSON.DTS` carries 7, descending 228 → 227 → 226 → 206 → 131 → 41 → 18 polys. `MechType_InitOne` loads them into a detail struct at `typeRec+0xde` — root array at `+0`, thresholds at `+4`, root count at `+0xc` — and `Mech_Constructor` (`00415bb0`) hands that struct to the object through `SimObjectBase_ConstructWithDetailTable` (`004033a4`), which stores it at `obj+0x41`. **Only the machine does.** `SimObjectBase_ConstructAnimated` and `SimObjectBase_ConstructStatic` both null that field, so a flyer, a structure and everything else draw the one root they were built with.

`Shape_DrawAtDetailLevel` then picks one every frame:

```c
radius = shapeInstance->shape->boundingRadius                   // shape+8
size   = (radius << DAT_006c60ac) / max(FastMagnitude3D(viewOffset) - radius, 1)
if (size == 0) size = 1
t      = Q10Multiply(g_ShapeDetailSizeScaleQ10, size)

i = min(rootCount - 1, g_ShapeDetailBias)                            // the detail bias
for (j = 0; i < rootCount - 1 && t < thresholds[j]; j++) i++;   // j starts at 0, not at i
shapeInstance->shape = roots[i];
... render ...
shapeInstance->shape = roots[0];                                // restored after
```

`DAT_006c60ac` is the view's perspective shift, installed per view by `Raster_InstallViewProjection` (`0048c1d8`) — `2^shift` is the focal length in pixels, so **`size` is the shape's projected radius in screen pixels** and the thresholds are a count of pixels. The subtraction measures to the near face of the bounding sphere rather than to its centre. Because the restore puts root 0 back after every draw, the radius read here is always root 0's, whichever root was last drawn.

The loop advances while the projected size is *below* the threshold, so a distant machine walks toward the crude roots and a close one stops at the starting index. That is the opposite sense to `TSDetailPart`'s ascending table — see [`dts-texture-binding.md`](dts-texture-binding.md#tsdetailpart-level-selection-and-structure-detail), which is the same shape of selection one level down, inside a shape rather than over its roots.

**The two indices are not the same index.** The root index starts at the bias and the threshold cursor starts at 0, and both advance together. At bias 0 they coincide and the walk reads as the plain descending-table lookup it looks like; at any other bias the machine starts that many roots down and is still compared against the finest thresholds. A nonzero bias therefore makes root 0 unreachable at any distance.

### Each root numbers its own nodes

**Every root carries its own `ANAnimList`, and in the file a node id means nothing outside the root that declares it.** The pose array is built from root 0 alone — see "The pose array is root 0's" below — and the load-time renumbering after it is what lets the other roots be drawn through it.

APOCA's seven roots, as parent>child pairs:

| Root | Sequences | Keyframes | Node tree |
|---|---|---|---|
| 0, 2 | 8 | 372 | `-1>0 -1>1 1>2 1>3 1>4 2>9 3>10 10>7 9>5 9>6 10>8 4>11` |
| 1, 3 | 1 | 21 | the same, less `-1>0` |
| 4 | 1 | 17 | `-1>1 1>2 1>3 1>4 2>7 3>8 7>5 8>6 4>9` |
| 5 | 1 | 16 | `-1>1 1>2 1>3 1>4 2>7 3>8 7>5 8>6` |
| 6 | 2 | 12 | `-1>1 1>2 1>3 1>4 2>5 3>6` |

The topology is the same machine throughout — pelvis 1, hips 2 and 3, torso mount 4, upper body hanging off 4, knees under the hips — but from root 4 down the numbering is **compacted**: dropping the camera node `0` and the separate lower-leg nodes closes the gaps, so root 0's upper body at node 11 is root 4's at node 9, and root 0's node 9 is a knee. A root that merely *drops* nodes keeps the rest where they were; a root that compacts renumbers them.

Across the 21 machine shapes, the leading four to six roots keep root 0's numbering and the crudest one to three compact it — 99 of 152 roots in total. The split is always a prefix.

### The pose array is root 0's

One per-node world transform array exists per object, and **every root is drawn through it**:

- `ShapeInst_Construct` (`0047872c`), the shape-instance constructor, sizes the dirty-flag, local and world arrays from `shape->animList->nodeCount` at `shapeInst+0xc` and allocates them once. The shape it reads is the one the instance is built with, root 0. Nothing reallocates them.
- `ShapeInst_BuildWorldTransforms` (`00478b58`) has exactly two callers — that constructor, and `ShapeInstance_StepAnimation` (`00478c2c`), which `SimObject_ApplyRootMotion` runs once per sim tick. `Shape_DrawAtDetailLevel` restores root 0 into the instance at the end of every draw, so **both callers run with root 0 installed** and the relation list walked is always root 0's.
- `ShapeInst_BindNodeTransformArray` (`00475fd8`) binds that one array to the render global; its two callers are the generic shape-render entry and exit. Nothing rebinds per root.
- `AnimThread_EvalNodeLocals` (`004799a4`) indexes the locals by the part ids of the sequence the **thread's own** anim list names — `thread+0` — not the drawn root's.

`Shape_LoadAllRoots` (`00474bcc`) loads every root, into a 100-slot buffer.

### The crude roots are renumbered at load

Right after loading the roots, `MechType_InitOne` calls `MechType_RemapDetailRootTransforms` (`00420090`, call at `004202b6`) with the detail struct and the chassis `.DAT`'s list of part ids at `typeRec+0x30` (record offset 46: signed bytes, ended by a negative one, at most 50). It rewrites every root after root 0 onto root 0's node numbering:

1. A 50-byte map per root, filled with `0xff`.
2. For each listed part id, the part's transform id (`part+4`) in root 0, found through the shape's vtable `+0x20` (`TSPartList_FindPart`), is stored in each other root's map at that root's own transform id for the same part.
3. `Shape_RemapTransformIds` (`0041ff88`) walks each other root through its map: a `TSGroup`'s and a `TSCellAnimPart`'s `+4`, and each entry of a `TSBSPPart`'s per-node transform array that is above 0, below the size of root 0's keyframe pool (the anim list's `+0x10`, which `ANAnimList_ReadFromStream` (`00491aa4`) fills with the transform count), and maps to a non-negative entry. It recurses through part lists, shapes, detail parts and the `ANShape`.
4. `Shape_RemapTransformId` (`0041ff68`) leaves a negative id alone and replaces any other with its signed map entry, so a `TSGroup` or `TSCellAnimPart` whose node no listed part reaches gets -1.

So once loaded, a compacted root's parts name root 0's joints, and drawing it through root 0's pose array puts each part on the joint it belongs to. Retail lists:

| Chassis | Part ids |
|---|---|
| ACHILLES, APOCA, CERBERUS, COLOSSUS, DIABLO, HYPERION, MAVERICK, MIRIMAC, OGRE, OUTLAW, RAMSES, RAPTOR2, SAMSON, SCARAB, STINGRAY, TOMAHAWK | 1-5, 12-17 |
| HEADHUNT | 1-5, 12-17, 44 |
| MONGOOSE | 1-5, 10, 12-17, 88, 99 |
| PITBULL | 1-5, 12-25 |
| SPIDER | 1-4, 12, 14, 15, 18-23 |
| RAZOR | 12 |

**The lists reach every node a crude root draws through.** Across the 21 machine shapes, every `TSGroup` and `TSCellAnimPart` transform id in roots 1 onward has a map entry, and so does every positive entry of every `TSBSPPart`'s per-node array, so the renumbering leaves no part that has a node at -1 and none on its own root's numbering. Some parts in SAMSON's and RAPTOR2's crude roots have no node in the file, and keep -1.

### The three tunables

| Global | Source | Value |
|---|---|---|
| `thresholds` | `g_ShapeDetailThresholds`, which `MechType_InitOne` writes into **every** type record's `+0xe2` | `{75, 60, 45, 25, 18, 12, 6}` — one shared table, not per-chassis data |
| `g_ShapeDetailSizeScaleQ10` | `ShapeDetail_ApplyHercDetailSetting` from the HERC DETAIL setting, through `g_HercDetailScaleValues` | `2000` for all five settings. The image's own initialiser leaves it at Q10 one (1024), so the runtime value is the load-bearing one |
| `g_ShapeDetailBias` | the same function, through `g_HercDetailBiasValues` | `{4, 3, 2, 1, 0}` by setting — setting 4 is the finest |

`ShapeDetail_ApplyHercDetailSetting` is called from `Sim_RenderFrame` (`0045fb9c`) and from `00461ddc` with the HERC DETAIL option ([`../simulation/preferences.md`](../simulation/preferences.md)) as its argument, so the setting is re-read as the player steps the row.

## Hardpoint attachment slots are overwritten every frame

**A machine's own `.DTS` carries one placeholder part per visible hardpoint, and DBSIM never draws it.** `Mech_Draw` replaces each one before rendering, with the fitted weapon's shape or with a blank record, so the shipped geometry appears nowhere in the original.

Built once, per LOD root, by `MechType_InitOne`:

- `GunLayout_CollectHardpointBones` (`0040fc50`) walks the `.GL` and emits each record's bone id (`+0x00`), or `-1` when its mounting code (`.GL +6`) is 4, the invisible mounting.
- `MechType_BindHardpointSlots` (`0040304c`) resolves each id through the shape's vtable `+0x24` to the **address of the part slot** holding the part whose id (`part+6`) is that id, and stores null for `-1`. The result is a `{void **slots; short count}` pair per root, at `typeRec+0xe6`.

Applied every frame by `Mech_SpliceHardpointShapes` (`004030d0`), for each root:

```c
replacement = mount->shape ? mount->shape : blankRecord;   // blanks from typeRec+0xec, 0x18 apart
replacement[+4] = (*slot)[+4];                             // inherit TSBasePart.Transform
replacement[+6] = (*slot)[+6];                             // inherit the part id
*slot = replacement;
```

**The match is on the part id, not on the transform node.** Verified against every retail chassis, where the ids are exactly the visible hardpoints' bones:

| Chassis | Visible hardpoints | Placeholder part ids |
|---|---|---|
| SAMSON | 7 | 8, 9, 10, 11, 18, 66, 77 |
| APOCA | 4 | 10, 11, 66, 77 |
| RAZOR | 4 | 10, 11, 66, 77 |
| OUTLAW | 3 | 8, 10, 11 |
| PITBULL | 1 | 5 |

The invisible mounting has to be excluded on its own merits, not merely for tidiness: SAMSON's bone 5 carries a real torso part, and splicing it would delete the machine's middle.

**A root without a hardpoint's part id draws no gun there.** The shape's vtable `+0x24` is `TSPartList_FindPartSlot` (`00476784`) on every part-list class: it checks its own children's ids, then asks each child in turn, and a leaf answers with `TSPartBase_FindPartSlot` (`00476134`), a bare `return 0`. A tree without the id therefore yields null, `MechType_BindHardpointSlots` stores it as it stores the invisible mounting's, and `Mech_SpliceHardpointShapes` passes over a null slot, still stepping past that hardpoint's blank record so the ones after it keep theirs. `SAMSON.DTS` roots 3-6, `COLOSSUS.DTS` roots 5 and 6, `OGRE.DTS` roots 4-6 and `OUTLAW.DTS` root 6 each lack one or more of their chassis' slot ids, and those crude models are drawn without those guns.

**Every retail slot is a direct child of its root's `TSBSPPart`**, so the walk paints the spliced shape in that child's turn, ordered against the machine's other children as the slot was ([`dts-texture-binding.md`](dts-texture-binding.md#tsbsppart-child-selection)). `HYPERION.DTS` root 5's slot for part 66 is the child its tree never reaches, so that root draws no gun there.

On most chassis the placeholders are recognisable in isolation — flat, two-sided, untextured, every slot of their surface record `0/1024` — but **the PITBULL's is an ordinary-looking `TSGroup`**, so that signature is a description of the usual case and not the rule.

## A destroyed component hides its own geometry

Every body part of a machine is a `TSCellAnimPart` of **three cells**: intact, damage-shaded (the same geometry, every poly moved to one dark ramp), and a bare `TSPoly` that draws nothing. `Component_DestroyAndCascade` (`0040d434`) ends by advancing that component's flipbook to the last cell:

```c
if (damageRecord[+3] >= 0)                                  // signed byte: the sequence this
    shapeInstance[+8][damageRecord[+3]] = 2;                // component drives, -1 for none
```

`shapeInstance+8` is the per-sequence cell-frame array `TSCellAnimPart_Render` indexes by the part's `animSequence` — see [`dts-billboards.md`](dts-billboards.md). So losing a component is drawn by stepping its parts to their blank cell, and the `.DMG` record's `+3` byte is the component-to-sequence map. The same byte gates the fire that component lights — see [`../simulation/destruction-effects.md`](../simulation/destruction-effects.md#who-catches-fire) — and the damage arithmetic behind it is in [`../simulation/component-damage.md`](../simulation/component-damage.md).

**The blank cell is blank because a `TSPoly` has no colour.** The surface index (`poly+0xc`) lives on `TSSolidPoly`, and all three flat renderers the exe ships — `TSSolidPoly_Render` (`00474db4`), `TSShadedPoly_Render` (`0047542c`) and `TSTexture4Poly_Render` (`00474e9c`) — resolve their fill through it. A plain `TSPoly` carries no such field on disk, so there is nothing for a renderer to fill it with, and stepping to the third cell is what removes the part. This is read off the chunk layout and the set of renderers that exist, not off a disassembled `TSPoly` vtable slot.

The same reasoning covers 14 plain `TSPoly`s reachable at cell 0 across every drawn root of the mech, flyer and structure libraries.

**No shape nests one `TSCellAnimPart` inside another** — across every `.DTS` a mission loads and both `.DGS` libraries, the deepest nesting is one. A piece of geometry therefore stands on at most one cell of one sequence, and whether it is drawn is a single test rather than a chain of them.

## Rejected readings

| Reading | Why it is wrong |
|---|---|
| The roots share one node space in the file, because each crude root's transform ids are a **subset** of root 0's | A subset of ids is not the same joints. The ids are drawn from one range because the numbering is compacted, not because a node kept its number: in the file APOCA's upper body is node 11 on root 0 and node 9 on root 4, where node 9 is a knee. They share root 0's numbering only after [the load-time renumbering](#the-crude-roots-are-renumbered-at-load) |
| A compacted root drawn through root 0's pose array puts its parts on whatever joint shares the number (APOCA root 4's upper body on a knee) | `MechType_RemapDetailRootTransforms` rewrites each crude root's transform ids to root 0's before any draw ([The crude roots are renumbered at load](#the-crude-roots-are-renumbered-at-load)) |
| A mech `.DTS` carries one `ANAnimList`, on its root shape | One **per root**, and they differ in every dimension — APOCA's root 0 declares 8 sequences over 372 keyframes and 12 nodes, its root 4 declares 1 over 17 and 9 |
| The `.DMG` record's `+0x03` byte is a HUD slot | It is the index of the `TSCellAnimPart` sequence the component drives, which the destruction path steps to its blank cell. The `= 2` write is a cell frame, not a damage state ([A destroyed component hides its own geometry](#a-destroyed-component-hides-its-own-geometry)) |
| A nonzero detail bias is harmless because the walk can still reach root 0 | The walk only ever advances, and it starts at the bias. `g_ShapeDetailBias` is the floor on how fine a machine is ever drawn, which is what makes the lowest HERC DETAIL setting a visible change at point-blank range and not only at distance |
