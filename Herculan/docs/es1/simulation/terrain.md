# DBSIM.EXE — terrain.cpp, the zone's shapes and the object list

**ES1's ground is flat, and what stands on it, the ridges, spires and buildings of a zone, are shapes the simulator loads with the mission. `terrain.cpp` loads them, tells a walking machine, a shot or a line of sight whether it meets one, and draws them, with every other object in the world, from one list sorted far to near.** The load and the shapes' objects are in [`zone-terrain.md`](../formats/zone-terrain.md#the-simulators-load), and the list's draw and sort in [`polygon-shading.md`](../rendering/polygon-shading.md#which-objects-are-drawn). This page places the module and holds the rest: the tests against the shapes, the static initialiser, and the screen pick and occlusion probe that ride on the draw. Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's.

## Where the module starts and ends

`terrain.cpp`'s code is `0002019c`–`00021d37` and its initialised data `00092422`–`00092575`. Both open, as `world.cpp`'s do ([where `world.cpp` starts](world.md#where-the-module-starts)), with a copy of `ConfigPart`'s destructors and tables ([module copies](../formats/compiler.md#vtables)): the four functions `0002019c`, `000201c4`, `00020203` and `0002020c`, each identical after normalisation to `world.cpp`'s, and the tables `00092422` and `00092456`. `Terrain_LoadZoneShapes` (`00020238`), which asserts `terrain.cpp`, follows the run. The data then holds the theater names and `Terrain_TheaterNames` (`000924a0`), `zoneXXXX`, the zone shape vtable `000924be`, the source path at `000924da`, `dgs`, the module's copies of `TS_OBJ`'s, `MAT_OBJ`'s and `DRAWABLE`'s vtables (`000924fa`, `0009251a`, `0009253a`), and the object list's globals from `00092558` ([Globals](#globals)).

The next run, `00021d38`, `00021d60`, `00021d9f` and `00021da8`, each identical after normalisation to `world.cpp`'s, with its tables at `00092576` and `000925aa`, opens `mechview.cpp` ([where `mechview.cpp` starts and ends](cockpit-views.md#where-the-module-starts-and-ends)).

| Range | What | Where |
|---|---|---|
| `0002019c`–`0002020c` | the run | [module copies](../formats/compiler.md#vtables) |
| `00020238`–`000206b4` | the zone's load and its shape objects' classes | [the simulator's load](../formats/zone-terrain.md#the-simulators-load) |
| `000206c8`, `00020738` | a zone shape's dividing line | [the zone shape object](../formats/zone-terrain.md#the-zone-shape-object) |
| `0002075c`–`00020b70` | the tests against the shapes | [below](#the-tests-against-the-shapes) |
| `00020c2c`–`00021d04` | the object list, its pick and its probe | [which objects are drawn](../rendering/polygon-shading.md#which-objects-are-drawn), [below](#the-object-list) |

ES2's counterparts lie in three places: the tests after its `GridShape` class, from `Structure_WalkCollisionTest` (`00427c68`) to `Sim_RaycastShapeVolume` (`00427da8`); the shape probe `Sim_RaycastShapes` (`00404ca0`) among the structures' functions; and the object list in `objlist.cpp`, whose static initialiser is `ObjList_RegisterSubsystem` (`00428de8`). ES1's `GridShape` is the last class of `world.cpp` ([the collision volume](world.md#the-collision-volume--gridshape)).

## The tests against the shapes

**A zone shape carries ES2's collision volume, a coarse height field ([the collision volume](../../retail/simulation/hit-detection.md#the-collision-volume--the-dgs-records-height-field)), and three tests ask the zone's shapes whether a point stands inside one or a ray passes through one.** Each walks a pointer array of objects whose shape is their shape instance's (`+0x2e`) `+4`; an object with no shape instance gives shape 0, a null pointer the test reads through.

### A point — `Zone_WalkCollisionTest` (`00020860`)

`Zone_WalkCollisionTest`, (radius, point) in stack order, calls `Structure_WalkCollisionTest` (`0002075c`) with `Zone_ShapeCount`, `Zone_ShapeObjects` and its two arguments. `es2_xref.py` finds one call, from `Mech_CollisionTest` (`00014174`, [collision](mech-locomotion.md#collision--mech_collisiontest-00014174)), which passes the radius its machine's slot `+0x60` returns and the machine's position, and returns this function's answer as its own when nothing nearer refused the move.

`Structure_WalkCollisionTest`, (count, objects, radius, point) in stack order, is ES2's ([the queries](../../retail/simulation/hit-detection.md#the-collision-volume--the-dgs-records-height-field)). For each object whose `Math_DistanceBetweenPoints2D` from its position (`+0x20`) to the point is less than its shape's radius (`+8`), it turns the point into the shape's frame, the point less the position rotated (`Math_Rotate2DPoint`) by the transposed (`Math_TransposeRotation2D`) 2 × 2 XY block, the first 8 bytes, of the object's rotation (`Context_GetTransform` of the orientation block `+6`), and returns the first non-zero `GridShape_HeightAt`(shape, 0, that point) in `EAX`, as that call answered it. With a count of 0 or none answering it returns 0 (`XOR EAX,EAX`), and it reads neither the radius nor the point's Z. Where ES2's builds the rotation from the object's angles itself when it is not yet built, ES1's asks `Context_GetTransform`, which does the same. `es2_xref.py` finds a second call, from `Structure_GatherWalkCandidates` ([the structures' tests](#the-structures-tests)).

### A ray — `Zone_RaycastShapeObjects` (`00020b50`)

`Zone_RaycastShapeObjects`, (ray, result), passes `Zone_ShapeCount`, `Zone_ShapeObjects`, the ray and the result to `Sim_RaycastShapeVolume` (`00020894`). `es2_xref.py` finds two calls, from `Sim_RaycastObjectList` ([shots against objects](world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0)) and from `Sim_RaycastShapes` ([below](#a-line-between-two-points--sim_raycastshapes-00020b70)).

`Sim_RaycastShapeVolume`, (count, objects, ray, result) in stack order, is ES2's march around `GridShape_Raycast` ([the queries](../../retail/simulation/hit-detection.md#the-collision-volume--the-dgs-records-height-field)), with ES2's ray record: the frame pointer at `+0`, the length at `+4`, the `int16` clearance at `+8` and the world-to-ray transform at `+0xa`. For each object, with *s* its shape, *r* = *s*'s radius (`+8`) + the clearance, and the length the ray's `+4` as it stands, shortened by any earlier hit of the walk:

1. *d* = `Math_DistanceBetweenPoints` from the object's position to the ray's start, the frame's `+0x14`. `RaycastShapeMinDistance` (`000afa40`) takes *d* when *d* is less than it. When *d* is no more than *r* + the length, `RaycastShapeMinDistance` is set to 0 and the test goes on.
2. The position, through the ray's `+0xa`, must have |*x*| < *r* and −*r* < *y* < *r* + the length; *z* is not tested.
3. The object's transform (`Context_GetTransform`), inverted with `Transform_TransposeRotation` and the negated translation through `Transform_RotatePoint`, takes the ray's start and its end, (0, length, 0) through the frame, into the shape's frame, and `GridShape_Raycast` (`0001fccc`)(*s*, clearance, the two points, out) marches between them ([`GridShape`](world.md#the-collision-volume--gridshape)).
4. On a hit the ray's length becomes `Math_DistanceBetweenPoints` from the hit point, out's `+4`, to the start, both in the shape's frame; the result gets that length at `+0` and the object at `+4`; and the walk ends when the length is less than 500.

It returns 1 in `AX` when anything was hit, otherwise 0. ES2's result takes the object's `+0xc` where ES1's takes the object. `es2_xref.py` finds two calls besides `Zone_RaycastShapeObjects`'s: from `00003a58`, the base's hit test (slot `+0x24`, [the difficulty scale](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c)), with a count of 1, and from `Sim_RaycastShapeList` ([the structures' tests](#the-structures-tests)).

`RaycastShapeMinDistance_Clear` (`00020880`) zeroes `RaycastShapeMinDistance` and `RaycastShapeMinDistance_Get` (`0002088c`) returns it, ES2's `00427d98` and `00427da0`; `es2_xref.py` finds their four calls in `Sim_RaycastObjectList`, a clear before each of its two reads. A distance is never negative, so after a clear step 1 never lowers the value: unless something reaches it through a pointer ([Open](#open)), it stays 0, and so do both figures the shot sweep stores, at `000afa1c` and `000afa18`. A grep of the disassembly for `000afa40` by address finds the five accesses in these three functions. ES2's `Sim_RaycastShapeVolume` treats its `RaycastShapeMinDistance` (`004aab54`) the same way.

### A line between two points — `Sim_RaycastShapes` (`00020b70`)

`Sim_RaycastShapes`, (clearance, to, from, result) in stack order, is ES2's shape probe ([the shape probe](../../retail/simulation/hit-detection.md#the-shape-probe--sim_raycastshapes-00404ca0)) over the zone's shapes. It builds a ray record on its stack as ES2's does: the frame from `Math_EulerToward`(*from*, *to*), pointing along *to* − *from* ([`Math_EulerToward`](fixed-point-math.md#helpers-that-match)), and `BuildEulerRotationMatrixQ14`, with *from* as its translation, the length `Math_DistanceBetweenPoints`(*from*, *to*), the clearance, and the world-to-ray transform at `+0xa`, the frame's rotation transposed with the negated translation rotated by it. It passes the record and the result to `Zone_RaycastShapeObjects` and returns its answer in `AX`. ES2's also takes a side and tests its structures; ES1's has no side and tests every zone shape, so the result's `+4` is the zone shape struck, the structures having a probe of their own ([below](#the-structures-tests)). `es2_xref.py` finds five calls: from `Detection_LineOfSight` (`0000e55c`, [line of sight](sensor-model.md#line-of-sight--detection_lineofsight-0000e55c)), twice from `Mech_AiObstacleAvoidance` (`00011c80`, [obstacle avoidance](mech-locomotion.md#obstacle-avoidance--mech_aiobstacleavoidance-00011c80)), from `Ai_LineOfSightBlocked` (`00017a00`, [skirting](mech-ai.md#skirting)) and from `Flyer_MovementTick` (`0001b084`), the flyer's look-ahead ([the move](flyers.md#the-move--flyer_movementtick-0001b084)).

### The structures' tests

**Three functions among the structures' code, before `Base_LoadResources` (`000046ac`), run the walk and ray tests over the mission's structures, the objects of `g_BasePool` (`000aed70`), as ES2's do over its structure pool.** Each walks the pool oldest first with `Pool_Prev` (`00063a20`) ([object pools](memory.md#object-pools)), puts the structures it keeps into a 40-entry pointer array on its stack, with no test of the count against that size (ES2's arrays hold 140; the most structure records a retail mission places is 33, `SM_PTL01`'s, by `HercWorks.Query structures --type all`), and hands the array and the count to `Structure_WalkCollisionTest` or `Sim_RaycastShapeVolume` above. `DBSim_LoadScriptDat` makes the pool through `Structure_InitPool` (`00004574`), `Pool_Init`(0, `0x1ba`, the live base count) ([the second pass](../formats/script-dat.md#the-second-pass)), so while no structure is spawned the pool holds no in-use node, `Pool_Prev` answers 0 at once, and every gather is empty.

A type *has a collision model* when its record's `+0x3a`, the 6-byte slot the load gives a type whose `BASES.DAT` `+0x06` is non-zero, is not 0: types 5, 6 and 8 ([the load](../formats/bases-dat.md#the-load)). A structure's type record is its `+0x188`.

- **`Structure_GatherWalkCandidates` (`0000375c`)**, (radius, point) in stack order, keeps each structure whose type has no collision model and returns `Structure_WalkCollisionTest`(count, array, radius, point)'s answer in `EAX`. With no such structure in the pool the count is 0 and the answer 0. ES2's ([collision](../../retail/simulation/mech-locomotion.md#collision)) also takes an object to pass over, keeps an animated type that has fallen to a wreck, and drops a structure that is gone; ES1's tests neither the destroyed byte nor the collapse, so a destroyed structure of types 0–4 or 7 stops a walker with whatever shape its instance holds, the hulk once the collapse ends. `es2_xref.py` finds one call, from `Mech_CollisionTest` ([collision](mech-locomotion.md#collision--mech_collisiontest-00014174)).
- **`Sim_RaycastShapeList` (`000037e0`)**, (side, ray, result), keeps each structure that is a wreck, destroyed (`+0x93`) with its collapse count `+0x1a0` at 0, or whose type has no collision model; with a side of 0 or more, only one whose group's (`+0x3f`) side byte `+0x14` equals it. It returns `Sim_RaycastShapeVolume`(count, array, ray, result)'s answer in `AX`, 0 for a count of 0. ES2's ([the shape probe](../../retail/simulation/hit-detection.md#the-shape-probe--sim_raycastshapes-00404ca0)) is the same gather on its own wreck test, reads its side at the group's `+0x12`, and makes no call for a count of 0.
- **`Structure_RaycastShapes` (`000038b8`)**, (side, clearance, to, from, result), builds `Sim_RaycastShapes`'s ray record ([above](#a-line-between-two-points--sim_raycastshapes-00020b70)) and returns `Sim_RaycastShapeList`(side, the record, result)'s answer in `AX`. ES2's `Sim_RaycastShapes` (`00404ca0`) is the two in one: ES1 probes the structures here and the zone's shapes in `Sim_RaycastShapes`. `es2_xref.py` finds four calls: two from `Mech_AiObstacleAvoidance`, with a side of −1, one from `Ai_LineOfSightBlocked`, with its machine's group's side byte, and one from `Flyer_MovementTick` (`0001b084`), the flyer's look-ahead ([the move](flyers.md#the-move--flyer_movementtick-0001b084)).

A structure becomes destroyed in `Base_ApplyDamage` (`00003978`, [damage](structures.md#damage--base_applydamage-00003978)), and a wreck when its collapse puts the type's hulk into the shape instance's (`+0x2e`) `+4` ([the collapse](structures.md#the-collapse)), which the tests above read as the structure's shape. `Base_DirectFireHitTest` (`00003a58`, [hits](structures.md#hits--base_directfirehittest-00003a58)) runs `Sim_RaycastShapeVolume` on that one structure for a wreck or a type with no collision model.

## The object list

The list, the frame's draw and the sort are in [`polygon-shading.md`](../rendering/polygon-shading.md#which-objects-are-drawn). The module also holds:

- **`ObjList_RegisterSubsystem` (`00021518`)**, its static initialiser, with no arguments and no frame: `RegisterSubsystemLoader`(2, `ObjList_Init`), so the list's arrays are allocated at subsystem phase 2, then `__vec_ctor` over the four 8-byte probe corners at `000afa54` with `ObjProbe_CornerElementCtor` (`00021538`), which allocates 8 bytes when given none, writes nothing and returns the element. Its address is the dword at `000adc18`, among the static constructors `Runtime_RunStaticCtors` runs ([static construction](../formats/compiler.md#static-construction-and-exit)). ES2's (`00428de8`) registers `ObjList_LoadResources` the same way.
- **`ObjListSortNode_ElementCtor` (`00021580`)**, the sort nodes' element constructor, which `ObjList_AllocSortNodes` passes to `__vec_new` by its padding entry `0002157f`: it allocates `0x1d` bytes when given none, runs the inlined constructor of the member at `+0x11`, which tests the sizes `0xc` and 8 and stores nothing, and returns the node. ES2's is `00429655`.
- **`ObjList_FreeSortNodes` (`000215c8`)**, frameless: `operator_delete` of `ObjList_SortNodes` (`00092570`), then 0 there. `es2_xref.py` finds no reference to it ([Open](#open)). ES2 has the same code at `00428f20`.
- **`ObjList_PointNearSegmentAxis` (`0002161c`)**, (point, entry) in stack order, is ES2's (`00428f70`): 0 when `ObjList_PointNearSegment`(point, entry) answers non-zero or the entry's `int16` at `+0x30` is 0; otherwise the point is rotated, with no translation, by the transposed rotation that faces along the entry's line (`Math_EulerToward` of its ends `+0x24` and `+0x18`, `BuildEulerRotationMatrixQ14`, `Transform_TransposeRotation`, `Transform_RotatePoint`), and it answers 1 when `labs` of both its X and its Z is less than `+0x30`. `es2_xref.py` finds no reference to it, nor to ES2's ([Open](#open)).

### The pixel pick and the occlusion probe

**Two screen tests ride on the object list's draw, as in ES2 ([ES2's](../../retail/rendering/terrain-drawing.md#the-pixel-pick-and-the-occlusion-probe)): the pick finds which object was drawn last at a point of the screen, and the probe finds whether objects drawn after one object cover the corners of a square around it.** ES1's functions are ES2's with the object's vptr at `+2`, so the probe calls slots `+8` and `+0x14` where ES2's calls `+4` and `+0x10`, and ES1's `ObjList_DrawFrameObjects` runs the pick's set-up and restore and the probe's test. Both read and write screen pixels through the display driver's routines at `0009dee4` (read a pixel at a point) and `0009dee8` (write a value there) ([the display drivers](../rendering/polygon-shading.md#the-display-drivers)).

The pick:

1. **`ObjPick_Arm` (`00020d28`)**, (point): copies the point's two `int32` to `ObjPick_Point` (`000afa48`) and sets `ObjPick_Pending` (`000afa45`).
2. **`ObjList_DrawFrameObjects`**, with the pick pending, before `ObjList_SetSortEye`: stores the GL display (`000bdd28`) at `000afa8c`, points the display's `+0x38` at its `+0x8c`, storing that at `00098dd6` and passing it to the driver's routine at `0009df14` when it is still the GL display, saves the pixel under the point at `ObjPick_SavedPixel` (`000afa90`), writes 1 there, and zeroes `ObjPick_Object` (`000afa50`).
3. **`ObjList_DrawEntryRender`**, after each entry's draw while the pick is pending, reads the pixel into `000afa94` and, when it is not 1, stores the entry's object in `ObjPick_Object` and the pixel in `ObjPick_SavedPixel`, and writes 1 again. So `ObjPick_Object` ends as the last object drawn over the point, and `ObjPick_SavedPixel` as the colour it drew there; ES2's stores the pixel the same way (`00428807`).
4. **`ObjList_DrawFrameObjects`**, after `ObjList_SortAndDraw`, writes `ObjPick_SavedPixel` back and clears `ObjPick_Pending`.

`ObjPick_IsIdle` (`00020d50`) is 1 when `ObjPick_Pending` is clear, and `ObjPick_GetObject` (`00020d48`) returns `ObjPick_Object`. `es2_xref.py` finds one call of `ObjPick_Arm`, from `Gunsight_ClickGadget_OnClick` (`000337dc`), the gunsight surface's click slot ([the click surface](cockpit-gunsight-hud.md#the-click-surface)), where it finds none of ES2's. It finds the calls of the other two in `Gunsight_UpdateAndPaint` (`00034f3c`), which, while `Gunsight_PickArmed` (`000936d8`) is set and `ObjPick_IsIdle` answers non-zero, passes the cockpit (its `+0x10`) and a non-zero `ObjPick_GetObject` to `TargetSelect_SetObject` (`0002d78c`), and clears `Gunsight_PickArmed` ([the per-frame update](cockpit-gunsight-hud.md#painting-and-the-per-frame-update)), so a click on the gunsight surface selects the object drawn last at the point.

The probe:

1. **`ObjProbe_Arm` (`0002125c`)**, (object): projects the object's position (slot `+8`) to the screen with the identity model transform (`Raster_SetModelTransform`(0), `Raster_ModelToView`, `Raster_PerspectiveDivide`, `Raster_ProjectToScreen`) and takes its radius *r* (slot `+0x14`). With *e* = `Math_FastMagnitude3D` of `g_EyeInModelSpace` − *r*, 1 when that is less than 1, the half-size *h* is (*r* << `Raster_FocalShift`) / *e*, 1 when that is 0. It stores the corners (*x* − *h*, *y* − *h*), (*x* + *h*, *y* − *h*), (*x* + *h*, *y* + *h*) and (*x* − *h*, *y* + *h*) in `ObjProbe_Corners` (`000afa54`), sets `ObjProbe_Armed` (`0009255a`) and stores the object in `ObjProbe_Object` (`000afa88`). Each corner's byte in `ObjProbe_CornerFlags` (`000afa84`) becomes 1 when the first corner lies outside the clip rect of the installed render context (`000bdbda`, its `+0x210` to `+0x21c`), and 0 otherwise: the loop tests the first corner four times, as ES2's does. ES2's replaces *e* with 1 only when it is 0, so with the eye inside the object's radius its half-size is negative.
2. **`ObjProbe_MarkCorners` (`00021404`)**, which `ObjList_DrawEntryRender` calls by its padding entry `00021403` after drawing `ObjProbe_Object` while the probe is armed: stores the GL display at `000afa8c` and, for each corner whose flag is 0, saves the pixel in `ObjProbe_SavedPixels` (`000afa74`) and writes 2 there.
3. **`ObjProbe_TestCorners` (`0002146c`)**, which `ObjList_DrawFrameObjects` calls by its padding entry `0002146a` last, while the probe is armed: points the display's page as step 2 of the pick does; for each corner whose flag is 0, writes the saved pixel back when the pixel still reads 2 and otherwise sets the flag, something drawn after the object having covered it; and clears `ObjProbe_Armed`.

`ObjProbe_IsIdle` (`000213d0`) is 1 when `ObjProbe_Armed` is clear; ES2 has the same code at `00428aa8`. `ObjProbe_IsHidden` (`000213dc`), given an argument it does not read, is 1 when the four flags add up to 4, every corner covered or off the screen; ES2's takes no argument. `es2_xref.py` finds no reference to `ObjProbe_Arm`, `ObjProbe_IsIdle` or `ObjProbe_IsHidden`, nor to ES2's `ObjProbe_Arm` and `ObjProbe_IsHidden` ([Open](#open)).

## Functions

| ES1 | Address | Notes |
|---|---|---|
| `HeapPtr_DtorTerrainCopy`, `ConfigPart_DtorTerrainCopy`, `ConfigPart_GetClassTagTerrainCopy`, `TSPartBase_DtorTerrainCopy` | `0002019c`, `000201c4`, `00020203`, `0002020c` | the run that opens the module ([module copies](../formats/compiler.md#vtables)); `es2_xref.py` finds no branch or stored pointer to `0002019c`, and the tables' slots `+4` and `+0x1c` hold the other three, `0002020c` by its padding `00020209` |
| `Terrain_LoadZoneShapes`, `TheaterShapeRecord_ElementCtor` | `00020238`, `000204d8` | [the simulator's load](../formats/zone-terrain.md#the-simulators-load) |
| `TSObj_RenderCopy`, `TSObj_GetPositionCopy`, `ZoneShapeObj_Dtor`, `TSObj_ReturnZeroCopy`, `TSObj_GetRadiusCopy` | `00020550`, `00020570`, `00020584`, `000205d3`, `000205dc` | [the zone shape object](../formats/zone-terrain.md#the-zone-shape-object) |
| `TSObj_DtorCopy`, `MatObj_GetPositionCopy`, `MatObj_DtorCopy`, `Drawable_GetRadiusCopy`, `Drawable_DtorCopy` | `00020624`, `0002066c`, `00020680`, `000206aa`, `000206b4` | the same |
| `ZoneShapeObj_TransformSegment`, `ZoneShapeObj_WorldSegment` | `000206c8`, `00020738` | the same |
| `Structure_WalkCollisionTest`, `Zone_WalkCollisionTest` | `0002075c`, `00020860` | ES2's `00427c68` and none |
| `RaycastShapeMinDistance_Clear`, `RaycastShapeMinDistance_Get` | `00020880`, `0002088c` | ES2's `00427d98`, `00427da0` |
| `Sim_RaycastShapeVolume`, `Zone_RaycastShapeObjects`, `Sim_RaycastShapes` | `00020894`, `00020b50`, `00020b70` | ES2's `00427da8`, none, `00404ca0` |
| `ObjList_Init`, `ObjListEntry_Ctor` | `00020c2c`, `00020c80` | [the object list](../rendering/polygon-shading.md#the-object-list) |
| `ObjPick_Arm`, `ObjPick_GetObject`, `ObjPick_IsIdle` | `00020d28`, `00020d48`, `00020d50` | ES2's `004282a0`, `004282c0`, `004282c8` |
| `ObjList_Add`, `ObjList_Remove`, `ObjList_RebuildFrame`, `ObjList_DrawEntryRender`, `ObjList_DrawFrameObjects` | `00020d5c`, `00020d84`, `00020de0`, `00020f40`, `00021008` | [which objects are drawn](../rendering/polygon-shading.md#which-objects-are-drawn) |
| `ObjProbe_Arm`, `ObjProbe_IsIdle`, `ObjProbe_IsHidden`, `ObjProbe_MarkCorners`, `ObjProbe_TestCorners` | `0002125c`, `000213d0`, `000213dc`, `00021404`, `0002146c` | ES2's `00428960`, `00428aa8`, `00428ab8`, `00428ae0`, `00428b38` |
| `ObjList_RegisterSubsystem`, `ObjProbe_CornerElementCtor` | `00021518`, `00021538` | ES2's `00428de8` and none |
| `ObjList_SetSortEye`, `ObjList_AllocSortNodes`, `ObjListSortNode_ElementCtor`, `ObjList_FreeSortNodes` | `00021554`, `00021560`, `00021580`, `000215c8` | ES2's `ObjListSortNode_ElementCtor` is `00429655` |
| `ObjList_PointNearSegment`, `ObjList_PointNearSegmentAxis`, `ObjList_SortCompareSegment`, `ObjList_SegmentEndpointNear`, `ObjList_SortCompareSegments` | `000215e0`, `0002161c`, `000216c4`, `000219fc`, `00021a4c` | [objects beside a dividing line](../rendering/polygon-shading.md#objects-beside-a-dividing-line) |
| `ObjList_EntryCompare`, `ObjList_TreeInsert`, `ObjList_TreeInsertSegments`, `ObjList_TreeInsertRest`, `ObjList_TreeDraw`, `ObjList_SortAndDraw` | `00021b20`, `00021ba8`, `00021bec`, `00021c60`, `00021cd0`, `00021d04` | [the frame's draw](../rendering/polygon-shading.md#the-frames-draw) |

## Globals

| | Address |
|---|---|
| `Terrain_TheaterNames`, four pointers | `000924a0` |
| `Zone_TheaterShapeRoots`, `Zone_ShapeObjects`, `Zone_ShapeCount`, `Zone_TheaterShapeRecords` | `000bc580`, `000bc584`, `000bc588`, `000bc58a` |
| `ZoneShapeObj_WorldSegment`'s buffer and its first-use byte | `000afa28`, `000afa24` |
| `RaycastShapeMinDistance` | `000afa40` |
| `ObjList_Objects`, the 512 object pointers, and `ObjList_Count`, their `int16` count | `000bc58e`, `00092558` |
| `ObjList_ZoneShapeCount`, the count `ObjList_RebuildFrame` resets `ObjList_Count` to | `000bc596` |
| `ObjList_Entries`, the 512 draw entries, and `ObjList_DrawList`, the pointers to those kept for the sort | `000bc598`, `000bc59c` |
| the draw entries' vtable, its slot `+4` `ObjList_DrawEntryRender` | `0009255e` |
| `ObjList_SortEye` and `ObjList_SortNodes` | `0009256c`, `00092570` |
| `ObjList_Blend`, the word `ObjList_DrawFrameObjects` stores its first argument in | `000afa98` |
| `ObjPick_Pending`, `ObjPick_Point`, `ObjPick_Object`, `ObjPick_SavedPixel` | `000afa45`, `000afa48`, `000afa50`, `000afa90` |
| `ObjProbe_Armed`, `ObjProbe_Corners`, `ObjProbe_SavedPixels`, `ObjProbe_CornerFlags`, `ObjProbe_Object` | `0009255a`, `000afa54`, `000afa74`, `000afa84`, `000afa88` |
| the GL display the pick and the probe store, and the pixel the pick last read | `000afa8c`, `000afa94` |

## Open

- **Open:** what the point at `000b16d4` holds when [the click slot](cockpit-gunsight-hud.md#the-click-surface) arms the pick.
- **Open:** what arms the occlusion probe and reads its answer: `es2_xref.py` finds no reference to `ObjProbe_Arm`, `ObjProbe_IsIdle` or `ObjProbe_IsHidden`.
- **Open:** what reads `000afa8c` and `000afa94`: a grep of the disassembly for them by address finds stores alone. And what `00098dd6` and the driver routine at `0009df14` do with the page they are given.
- **Open:** what calls `ObjList_PointNearSegmentAxis` and `ObjList_FreeSortNodes`, to which `es2_xref.py` finds no reference, and what the entry's `int16` at `+0x30` holds: `ObjList_DrawFrameObjects` writes no `+0x30`.
- **Open:** whether anything reaches `RaycastShapeMinDistance` through a pointer, which would make the shot sweep's two stored figures other than 0: a grep of the disassembly by address finds its five accesses in `RaycastShapeMinDistance_Clear`, `RaycastShapeMinDistance_Get` and `Sim_RaycastShapeVolume`.
- **Open:** what the second pointer array `ObjList_Init` allocates, `000bc592`, holds.
- **Open:** ES2's copies of `ObjProbe_IsIdle` (`00428aa8`) and `ObjList_FreeSortNodes` (`00428f20`) have no Ghidra function and no `known_symbols` entry.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: in DOSBox-X's debugger, `BP <CS>:00020238` set before a mission loads fires once, with the dwords at `[ESP+4]` and `[ESP+8]` the theater and the zone, header words 0 and 1 of the install's `DATA\SCRIPT.DAT`.
