# DBSIM.EXE — the 3Space object classes: construction, root motion, the transform save and the shape draw

**Every shaped object in ES1's simulator, the HERCs, flyers, structures, rounds and effects, is built, animated, moved by its animation and drawn by the classes ES2's simulator has, with the same steps on fields 6 bytes lower.** These are the classes under ES2's simulation objects ([two hierarchies, one root](../../retail/simulation/sim-object-layout.md#two-hierarchies-one-root)): `DRAWABLE`, `MAT_OBJ`, `TS_OBJ`, `ANIM_OBJ` and `DET_ANIM_OBJ`, the light `TINY_OBJ` and `REG_OBJ` with their `TS_` forms, and the `CONFIG_PART` a fitted weapon is drawn through. What differs is how ES1's compiler builds them: each constructor allocates for itself when given no object, and the tables lead with a zero dword ([vtables](../formats/compiler.md#vtables)). These two files hold no counterpart of ES2's `SimObject_ConformToTerrain` (`004029d8`), which ES2's file has between the transform save and its static initializer.

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order, the object first and the other arguments in the reverse of ES2's order ([calling conventions](../formats/compiler.md#calling-conventions)). The class names are ES2's: ES1's tables carry no class name, and each class here is the one whose constructor, destructor and slots match ES2's.

## Where the object files start and end

Two object files, with no assert naming a source path in either ([object files](../formats/compiler.md#where-object-files-meet-in-the-code)):

- **`000016e8`–`0000252b`, the classes and the transform save.** It opens after `memory.cpp`, whose static initializer (`000016dc`, table entry `000adbb8`) ends with a `RET` at `000016e6` and one zero byte. Its own static initializer, `SimObject_StaticInit` (`0000230c`, entry `000adbbc`, no Ghidra function), stands after the transform save, and its three element constructors follow it; the last, `Transform_CtorTSObjCopy`, ends with a `RET 4` at `00002528`, and one zero byte follows. Its functions are ES2's from `TinyObj_GetPosition` (`00401f08`) to `AnimThread_ElementCtor` (`00402c36`) with the `TINY_OBJ`, `REG_OBJ`, `MAT_OBJ` and `TS_OBJ` destructors ES2 places elsewhere, in an order of their own.
- **`0000252c`–`00002c7b`, `CONFIG_PART` and `DET_ANIM_OBJ`.** It opens with `ShapeInstance_CtorCellFrames` and no copy of the 40-byte destructor; `Shape_DrawAtDetailLevel`'s `RET 4` at `00002c76` is followed by three zero bytes and the run of `ConfigPart`'s copies that opens the next file at `00002c7c`. It has no static initializer. Its functions are ES2's from `ShapeInstance_CtorCellFrames` (`00402fc0`) to `Shape_DrawAtDetailLevel` (`004033e4`), in ES2's order, with the `DET_ANIM_OBJ` destructor ES2 places at `00426f98` and this file's copies of `TS_OBJ`'s small slots.

## The classes

| Class | Table | Bytes | Built by | Destructor | ES2's table |
|---|---|---|---|---|---|
| `DRAWABLE` | `DrawableBaseVtable` (`0009020a`) | — | inlined in every constructor below | `Drawable_Dtor` (`000018d4`) | `004a14bc` |
| `TINY_OBJ` | `TinyObjVtable` (`0009022a`) | `0x10` | inlined in `TSTinyObj_Ctor` | `TinyObj_Dtor` (`000018f8`) | `00497321` |
| `TS_TINY_OBJ` | `TSTinyObjVtable` (`0009024a`) | `0x14` | `TSTinyObj_Ctor` (`00001870`) | `TSTinyObj_Dtor` (`00001944`) | `00497301` |
| `REG_OBJ` | `RegObjVtable` (`0009027a`), and `RegObjContextVtable` (`0009026a`) for its `TSContext` | `0x1c` | inlined in `TSRegObj_Ctor` | `RegObj_Dtor` (`00001a38`) | `004972d5` |
| `TS_REG_OBJ` | `TSRegObjVtable` (`000902aa`), and `TSRegObjContextVtable` (`0009029a`) | `0x20` | `TSRegObj_Ctor` (`00001988`) | `TSRegObj_Dtor` (`00001aa4`) | `004972a9` |
| `MAT_OBJ` | `MatObjVtable` (`000902ca`) | `0x2e` | inlined in the `TS_OBJ` constructors | `MatObj_Dtor` (`00001cb0`) | `00497289` |
| `TS_OBJ` | `TSObjVtable` (`000902ea`) | `0x3b` | `TSObj_Ctor` (`00001b04`), `SimObjectBase_ConstructShaped` (`00001da0`) | `TSObj_Dtor` (`00001d10`) | `004a0b98` |
| `ANIM_OBJ` | `AnimObjVtable` (`0009030a`), and this file's copy `AnimObjVtableDetAnimObjCopy` (`000903e6`) | `0x3b` | inlined in the `DET_ANIM_OBJ` constructors | `AnimObj_Dtor` (`00001f98`) | `004973ac` |
| `DET_ANIM_OBJ` | `DetAnimObjVtable` (`00090406`) | `0x3f` | the three `SimObjectBase_Construct…` [below](#the-constructors) | `DetAnimObj_Dtor` (`00002a48`) | `004973cc` |
| `CONFIG_PART` | `ConfigPartVtableDetAnimObjCopy` (`000903ae`), one of the 44 [copies](../formats/compiler.md#vtables) | `0x18` | `ConfigPart_CreateArray` (`00002788`) | `ConfigPart_DtorDetAnimObjCopy` (`000028e0`) | `0049a494` |

Each table's slots are ES2's at the slot 4 bytes lower ([the HERC's table](mech.md#the-hercs-table) shows the same shift):

| Table | `+0x04` draw | `+0x08` position | `+0x0c` destructor | `+0x10` | `+0x14` radius |
|---|---|---|---|---|---|
| `DRAWABLE` | 0 | 0 | `Drawable_Dtor` | `TSObj_ReturnZero` (`000018e8`) | `Drawable_GetRadius` (`000018ed`) |
| `TINY_OBJ` | `TinyObj_InstallModelTransform` (`000017a8`) | `TinyObj_GetPosition` (`00001764`) | `TinyObj_Dtor` | the same | the same |
| `TS_TINY_OBJ` | `TSTinyObj_Draw` (`00001924`) | `TinyObj_GetPosition` | `TSTinyObj_Dtor` | the same | the same |
| `REG_OBJ` | `Raster_SetModelTransformFromPose` (`00001808`) | `RegObj_GetPosition` (`00001a2a`) | `RegObj_Dtor` | the same | the same |
| `TS_REG_OBJ` | `TSRegObj_Draw` (`00001a84`) | `RegObj_GetPosition` | `TSRegObj_Dtor` | the same | the same |
| `MAT_OBJ` | `SimObject_InstallModelTransform` (`00001854`) | `MatObj_GetPosition` (`00001c9c`) | `MatObj_Dtor` | the same | the same |
| `TS_OBJ` | `TSObj_Render` (`00001cdc`) | `TSObj_GetPosition` (`00001cfc`) | `TSObj_Dtor` | the same | `TSObj_GetRadius` (`00001d58`) |
| `ANIM_OBJ` | `TSObj_Render` | `TSObj_GetPosition` | `AnimObj_Dtor` | the same | `TSObj_GetRadius` |
| `ANIM_OBJ`'s copy | `TSObj_RenderDetAnimObjCopy` (`000029c4`) | `TSObj_GetPositionDetAnimObjCopy` (`000029e4`) | `AnimObj_Dtor` | `TSObj_ReturnZeroDetAnimObjCopy` (`000029f8`) | `TSObj_GetRadiusDetAnimObjCopy` (`00002a00`) |
| `DET_ANIM_OBJ` | `Shape_DrawAtDetailLevel` (`00002b30`) | `TSObj_GetPositionDetAnimObjCopy` | `DetAnimObj_Dtor` | `TSObj_ReturnZeroDetAnimObjCopy` | `TSObj_GetRadiusDetAnimObjCopy` |

The second tables of `REG_OBJ` and `TS_REG_OBJ`, stored at the object's `+0x18`, hold at `+4` the thunks `REG_OBJ_DtorThunk` (`00001a20`) and `TS_REG_OBJ_DtorThunk` (`00001a78`), which take 6 from the object (`ADD [ESP+4],-6`) and jump to the class's destructor, as ES2's take `0xc`. `CONFIG_PART`'s table is [`compiler.md`'s](../formats/compiler.md#vtables), its class tag at `+4` being this file's `ConfigPart_GetClassTagDetAnimObjCopy` (`000028d6`, `XOR EAX,EAX`, `RET 4`) and its draw at `+0x20` `ConfigPart_Render` (`00002718`, [the mount classes' callees](weapon-mount-classes.md#functions)). The `TSPartBase` table this file stores before it, `TSPartBaseVtableDetAnimObjCopy` (`0009037a`), has `TSPartBase_DtorDetAnimObjCopy` (`000028ac`) at `+0x1c`.

The HERC file's own copies of these tables are in [the other tables](mech.md#the-other-tables).

## The fields

`TS_OBJ` and the classes below it hold ES2's fields 6 bytes lower; the HERC's own fields continue from there ([the HERC's fields](mech.md#the-fields)).

| ES1 | ES2 | Field |
|---|---|---|
| `+0x02` | `+0x00` | the table |
| `+0x06` | `+0x0c` | the orientation block, `0x28` bytes: the `int16` Euler angles at `+0x06`, `+0x08` and `+0x0a` (heading), the `0x20`-byte transform at `+0x0c` (the Q14 rotation, then the position at `+0x20`, `+0x24`, `+0x28`) and at `+0x2c` the word that says the rotation is built from the angles |
| `+0x2e` | `+0x34` | the shape instance |
| `+0x32`–`+0x39` | `+0x38`–`+0x3f` | eight bytes the constructors clear; `+0x33` (ES2's `+0x39`) set means the instance is an animated one, and threads and root motion run on it |
| `+0x3b` | `+0x41` | `DET_ANIM_OBJ`'s LOD table |

The orientation block is ES2's `Context`. `Context_GetTransform` (`000016e8`) builds its rotation from the angles when the word is 0 and returns the transform, where ES2's inlines the same steps in each reader ([the frame](../../retail/simulation/sim-object-layout.md#the-objects-frame-is-a-transform-and-its-position-is-that-transforms-translation)). **`Context_SetFromTransform` (`00001718`)**, (block, transform), sets the angles from the transform's rotation with `Transform_RotationToEuler`, copies the `0x20` bytes in and sets the word to 1; `es2_naming.py match` finds no ES2 counterpart and `es2_xref.py` no caller ([Open](#open)).

`TINY_OBJ` holds a position in two `int32`s at `+0x06` and `+0x0a` and a heading at `+0x0e`, and `TS_TINY_OBJ` its shape instance at `+0x10` (ES2's `+0x0c`, `+0x10`, `+0x14` and `+0x16`). `REG_OBJ` holds a `TSContext` at `+0x06` ([object layout](../formats/compiler.md#object-layout)): its position at `+0x06`, its Euler angles at `+0x12` and its table at `+0x18`; `TS_REG_OBJ` its shape instance at `+0x1c` (ES2's context at `+0x0c`, instance at `+0x22`).

## The constructors

Each takes (object, argument), allocates its class's size with `operator_new` when the object is null and returns 0 when that fails, and returns the object.

- **`TSObj_Ctor` (`00001b04`)**, (object, shape): ES2's `SimObjectBase_Constructor` (`00402188`): `DRAWABLE`'s table, the orientation block zeroed, `MAT_OBJ`'s and `TS_OBJ`'s tables, a static shape instance from `TSShapeInstance_Ctor` (`00063080`) at `+0x2e`, 0 for a null shape, and `+0x32`–`+0x39` cleared.
- **`SimObjectBase_ConstructShaped` (`00001da0`)**, (object, shape): the same with an animated instance, `ShapeInst_Construct` (`00061224`, [the instance](../rendering/dts-node-posing.md)), and `+0x33` set. ES2's (`00402278`).
- **`SimObjectBase_ConstructAnimated` (`00002970`)**, (object, shape): `SimObjectBase_ConstructShaped`, `ANIM_OBJ`'s table copy and `DET_ANIM_OBJ`'s table, and the LOD table `+0x3b` set to 0. ES2's (`0040332c`). `es2_xref.py` finds two calls, both from `00003cbc`.
- **`SimObjectBase_ConstructStatic` (`00002a74`)**, (object, shape): the same on `TSObj_Ctor`, so the instance is static and `+0x33` stays clear. ES2's (`00403368`). `es2_xref.py` finds calls from `00003cbc` and `0001ac58`.
- **`SimObjectBase_ConstructWithDetailTable` (`00002ac8`)**, (object, LOD table): `SimObjectBase_ConstructShaped` on the table's first root, the two tables, and the table at `+0x3b`. ES2's (`004033a4`); `Mech_Constructor` (`00011330`) passes the type record's `+0xc6` ([the type record in memory](../formats/chassis-dat.md#the-type-record-in-memory)), the one call `es2_xref.py` finds.
- **`TSTinyObj_Ctor` (`00001870`)**, (object, shape): `DRAWABLE`'s and `TINY_OBJ`'s tables, the position and heading zeroed, `TS_TINY_OBJ`'s table and a static instance at `+0x10`. ES2's (`0040201c`).
- **`TSRegObj_Ctor` (`00001988`)**, (object, shape): `DRAWABLE`'s table, the `TSContext` built with `TSContext_CtorWithFrame` (`0005b7d0`) from a zero orientation and a zero position, the two `REG_OBJ` tables, the two `TS_REG_OBJ` tables and a static instance at `+0x1c`. ES2's (`004020b8`).

`es2_xref.py` finds no reference to `TSTinyObj_Ctor` or `TSRegObj_Ctor` ([Open](#open)).

## The destructors

Each takes (object, flags), returns at once for a null object (except `Drawable_Dtor`, which tests nothing), stores its class's table, runs its base's destruction inline or by call, and calls `operator_delete` on the object when flags bit 0 is set. `Drawable_Dtor` stores `DRAWABLE`'s table and frees nothing.

- **`TSObj_Dtor` (`00001d10`)** destroys the shape instance through its slot `+0x1c` with 3, then stores `MAT_OBJ`'s and `DRAWABLE`'s tables; **`MatObj_Dtor` (`00001cb0`)** and **`TinyObj_Dtor` (`000018f8`)** only store their tables. ES2's `TSObj_Dtor` (`0046b890`), `MatObj_Dtor` (`0046b97d`), `TinyObj_Dtor` (`00402f94`).
- **`AnimObj_Dtor` (`00001f98`)**: with `+0x33` set, takes each thread off the instance (`ShapeInst_RemoveThread` on its first slot, `+0x1a`, until it is empty), deletes each, destroys the instance and zeroes `+0x2e`; then `TS_OBJ`'s steps. ES2's (`00402400`). **`DetAnimObj_Dtor` (`00002a48`)** stores its table and runs `AnimObj_Dtor`(object, 0); ES2's (`00426f98`). `es2_xref.py` finds `AnimObj_Dtor` called from 12 places, the HERC's destructors among them.
- **`TSTinyObj_Dtor` (`00001944`)**, **`TSRegObj_Dtor` (`00001aa4`)** and **`RegObj_Dtor` (`00001a38`)** are ES2's (`00402eb3`, `00402cfa`, `00402e02`): the instance destroyed through its slot `+0x1c` with 3, and the `TSContext` through `TSContext_Dtor` (`0005b878`).

## Animation threads and root motion

**`ShapeInst_AddThread` (`00001f48`)**, (object, sequence): ES2's (`00402374`): a thread from `AnimThread_Ctor` (`000619f0`) on the instance's shape's animation list (`+0x2a`), cut to frame 0 of the sequence with `AnimThread_SetSequence`, playback rate 0, registered on the instance with `ShapeInst_RegisterThread`; it returns the thread. ES1's constructor allocates the thread itself, and neither function tests the sequence: a caller that must skip a negative one tests it, which `Mech_Constructor` does for the twist and pitch threads and not for the locomotion thread ([construction](mech.md#construction)). `es2_xref.py` finds six calls, from `00003cbc`, `Mech_Constructor` and `WeaponMount_CtorBase`.

**`SimObject_ApplyRootMotion` (`0000205c`)**, (object, time, instance), and **`SimObject_ApplyRootMotionIfEnabled` (`0000215c`)**, (object, time), which calls it with the instance at `+0x2e` while `+0x33` is set: ES2's ([root motion](../../retail/simulation/mech-locomotion.md#root-motion)). The instance's root is seeded with the identity, the animation stepped by the time with `ShapeInstance_StepAnimation`, node 0's world transform read back, its translation put through the object's rotation (`Context_GetTransform`, which builds the rotation first if the angles changed) and added to the position, its rotation's Euler angles added to the object's, the rotation word cleared, and node 0 reset to the identity. `es2_xref.py` finds `SimObject_ApplyRootMotionIfEnabled` called from `Mech_IntegrateMotion` ([the move](mech-locomotion.md#the-move)), `Mech_ConfigureLoadout` with time 0, `Mech_ComponentDamageWrite` with time 1, `00002df8` and `00003058`.

## Saving and restoring the transform

**`SimObject_PushTransform` (`00002184`)** and **`SimObject_PopTransform` (`00002248`)**, (object): ES2's ([what a refused move does](../../retail/simulation/mech-locomotion.md#what-a-refused-move-does)). The push copies the object's `0x20`-byte transform (after `Context_GetTransform`), each of the instance's ten thread slots' records (`+0x1a`, `0x52` bytes each, a slot holding none skipped) and, for the instance's node count `+0xc`, its dirty flags (`+0xe`), 12-byte locals (`+0x12`) and 32-byte world transforms (`+0x16`) into the save buffers; the pop copies them back the other way. ES1 copies each thread record whole in one move, where ES2's copies it field by field; the bytes are the same. `es2_xref.py` finds `Mech_MovementTick` the only caller of both.

| Buffer | ES1 | Size | ES2 |
|---|---|---|---|
| the transform | `SimObjectSave_Transform` (`000ae4d0`) | `0x20` | `004a8d54` |
| the threads | `SimObjectSave_AnimThreads` (`000ae4f0`) | 10 × `0x52` | `SimObjectSave_AnimThreads` (`004a8d74`) |
| the dirty flags | `SimObjectSave_NodeDirtyFlags` (`000ae824`) | 32 bytes | — |
| the locals | `SimObjectSave_NodeLocals` (`000ae844`) | 30 × 12 | `SimObjectSave_NodeLocals` (`004a90c6`) |
| the world transforms | `SimObjectSave_NodeTransforms` (`000ae9ac`) | 30 × `0x20` | `SimObjectSave_NodeTransforms` (`004a922e`) |

`SimObject_StaticInit` (`0000230c`) builds the last three arrays with `__vec_ctor` and the element constructors `AnimThread_ElementCtor` (`00002350`, entered at `0000234e`), `maybe_Point3SPair_CtorTSObjCopy` (`00002440`) and `Transform_CtorTSObjCopy` (`000024b8`), ES2's `SimObject_StaticInit` (`00402bb8`). `AnimThread_ElementCtor` allocates `0x52` bytes when given none and inlines its members' empty constructors, where ES2's (`00402c36`) returns its argument; the other two are `maybe_Point3SPair_Ctor`'s and `Transform_Ctor`'s code ([the instance's arrays](../rendering/dts-node-posing.md#differences-from-es2)).

## Drawing

### The LOD root — `Shape_DrawAtDetailLevel` (`00002b30`)

`DET_ANIM_OBJ`'s draw, (object): ES2's ([the LOD root](../../retail/rendering/mech-shape-drawing.md#the-lod-root-is-chosen-per-frame-per-object)) on ES1's fields and globals. With a LOD table at `+0x3b` whose root count (`+0xc`) is not 0, it measures the shape's radius (`+8` of the instance's shape) against `Math_FastMagnitude3D` of `g_EyeInModelSpace` (`000b5ed4`) after `SimObject_InstallModelTransform`, takes (radius << `Raster_FocalShift`) / max(distance − radius, 1), at least 1, scales it by `Math_Q10Multiply` with `g_ShapeDetailSizeScaleQ10` (`000903e0`, `int16`), starts the root index at the lesser of `g_ShapeDetailBias` (`000903e2`, `int16`) and the root count less 1, and walks the thresholds (`+4`, `int32`) from the first as ES2's does. It installs that root in the instance, installs the transform again, draws the instance through its slot `+0x20` and puts root 0 back. ES1's multiply rounds ([the primitives](fixed-point-math.md#the-primitives)).

The image holds the scale 1024 and the bias 0; the detail setting replaces both before the first frame ([below](#the-detail-setting--shapedetail_applydetailsetting-00057c08)). The thresholds are `g_ShapeDetailThresholds` (`00091dac`), seven `int32` {75, 60, 45, 25, 18, 12, 6}, which the chassis load puts in the type record's `+0xca` ([the load](../formats/chassis-dat.md#the-load)), and the roots after root 0 draw through root 0's nodes because that load renumbers them ([the crude roots](../../retail/rendering/mech-shape-drawing.md#the-crude-roots-are-renumbered-at-load)).

**A port draws a HERC's LOD root by these steps, each frame.** With *r* root 0's radius (shape `+8`) and *d* `Math_FastMagnitude3D` of the eye in the model's space: *size* = (*r* << the view's focal shift) / max(*d* − *r*, 1), 1 when that is 0; *t* = `Math_Q10Multiply`(*size*, scale), ES1's rounding multiply; *i* = min(bias, root count − 1), *j* = 0; while *i* < root count − 1 and *t* < thresholds[*j*], *i* and *j* both go up by 1. Root *i* is drawn, and root 0 put back. The main view's focal shift is 8 ([the main camera](main-loop.md#the-mission-start--sim_startmission-0005a9dc), step 12), so *size* is the root's radius in 320 × 200 pixels at its near face.

### The detail setting — `ShapeDetail_ApplyDetailSetting` (`00057c08`)

**One preferences option sets how fine every shaped object is drawn: which LOD root a HERC starts from and how soon it steps down, and how fine the detail parts of HERCs' weapons, structures and flyers are.** `ShapeDetail_ApplyDetailSetting`, (value), is ES2's `ShapeDetail_ApplyHercDetailSetting` (`0045d474`, [the three tunables](../../retail/rendering/mech-shape-drawing.md#the-three-tunables)) with ES1's keys and two more outputs. It takes the first of five keys the value is at most, in two key tables, and copies that slot of each value table, the normal ones or, while `g_LowMemory` (`000bda98`) is set, the low-memory ones. A value above the last key leaves every output as it was.

| Keys | Output | Normal | Low memory |
|---|---|---|---|
| `ShapeDetail_ScaleKeys` (`00096bc4`): 50, 100, 150, 200, 300 | `g_ShapeDetailSizeScaleQ10` (`000903e0`), the LOD scale | `ShapeDetail_ScaleValues` (`00096bd0`): 2000 in all five | `ShapeDetail_ScaleValuesLowMemory` (`00096c0c`): 2000, 3000, 4000, 4000, 4000 |
| `ShapeDetail_BiasKeys` (`00096bdc`): 50, 100, 150, 200, 300 | `g_ShapeDetailBias` (`000903e2`), the first LOD root | `ShapeDetail_RootBiasValues` (`00096be8`): 4, 3, 2, 1, 0 | `ShapeDetail_RootBiasValuesLowMemory` (`00096c18`): 2, 1, 0, 0, 0 |
| the same | `g_TSDetailBiasForMounts` (`000bc2b8`) | `ShapeDetail_MountBiasValues` (`00096bf4`): 2, 2, 1, 1, 0 | `ShapeDetail_MountBiasValuesLowMemory` (`00096c24`): 1, 1, 0, 0, 0 |
| the same | `g_TSDetailBiasForStructures` (`000bc198`) and `g_TSDetailBiasForFlyers` (`000bc460`) | `ShapeDetail_ObjectBiasValues` (`00096c00`): 2, 2, 1, 1, 0 | `ShapeDetail_ObjectBiasValuesLowMemory` (`00096c30`): 2, 1, 1, 1, 1 |

Each table is five `int16` and a zero word; the scale and the root bias are `int16`, the three detail-part biases `int32`. ES2's keys are 0 to 4, its HERC DETAIL setting's, and it writes the first three outputs from the first three normal columns; its structures take their bias from its STRUCTURE DETAIL setting ([`TSDetailPart` levels](../../retail/rendering/dts-texture-binding.md#tsdetailpart-level-selection-and-structure-detail)).

The value is preferences option 6, the byte `000bd568`, which `Prefs_Load` (`0004a744`) reads from byte 6 of `data\prefs.cfg` and the preferences panel's SHAPES row shows on a bar and steps by 32 ([its rows](preferences.md#its-rows)). `Sim_StartMission` stores it in `000b5b72` and `000b5b74` and calls the function with it before the first frame ([the mission start](main-loop.md#the-mission-start--sim_startmission-0005a9dc), step 15); `Sim_RenderFrame` (`00059c08`) copies it into `000b5b72` each frame after subsystem phase 5 and, when that differs from `000b5b74`, calls the function with it and copies it into `000b5b74`. The option's handler, which `Prefs_Init` (`0004afe8`) registers with `Prefs_SetHandler` (`0004ac4c`), is `Prefs_LimitDetailInLowMemory` (`0004adb0`): with `g_LowMemory` set and the option above 160 it sets the option to 160 while the options are being loaded (the byte `000b1734` set), with `Prefs_SetOption`(1, 160, 6), whose call of the handler finds 160 and does nothing; otherwise it puts the option back to its value before its last write with `Prefs_UndoSetOption` (`0004a900`)(6), running no handler ([the option array](preferences.md#the-option-array)), and raises the status panel `StatusAlertPanel_Ctor` (`00046efc`)(0, 0, 1, `0x11`), `INSUFFICIENT MEMORY FOR MAXIMUM DETAIL.` ([what each status shows](alert-panels.md#what-each-status-shows)), runs it through slot `+0xc` of its `+0x314` and destroys it with `GeneralAlert_Dtor` (`000472b0`)(panel, 3). A low-memory option therefore reaches the fourth slot of each table at most.

The three detail-part biases reach `TSDetailPart_Render` through `TSDetailPart_SetBias` (`000530a4`), set around one class's draw and put back to 0 after it, as in ES2's:

| Draw | ES1 | Bias | ES2's |
|---|---|---|---|
| a structure's, slot `+4` of the three tables `00003cbc` stores (`00090522`, `0009059e`, `0009061a`) | `Structure_DrawWithDetailBias` (`00002d18`): the bias, `Shape_DrawAtDetailLevel`, 0 | `g_TSDetailBiasForStructures` | `Structure_DrawWithDetailBias` (`004034f4`), from STRUCTURE DETAIL |
| a flyer's, slot `+4` of `00091fea`, which `0001ac58` stores | `Flyer_Draw` (`0001ac34`), the same | `g_TSDetailBiasForFlyers` | `Flyer_Draw` (`004215cc`) |
| a debris piece's, slot `+4` of `000908e6`, which `00006168` stores | `Debris_Draw` (`000065c4`): the bias, `SimObject_InstallModelTransform`, the instance `+0x2e`'s slot `+0x20`, 0 | the piece's own `+0x4a` | `Debris_Draw` (`00408e6c`), from its `+0x50` |
| a fitted weapon's | `WeaponMount_RenderWithDetailBias` ([the mount classes](weapon-mount-classes.md#the-slots)) | `g_TSDetailBiasForMounts` | `WeaponMount_RenderWithDetailBias` |

These are the four callers of `TSDetailPart_SetBias` `es2_xref.py` finds.

### The hardpoints — `Mech_SpliceHardpointShapes` (`00002654`)

(hardpoint slot list, blank parts, mounts): ES2's ([hardpoint attachment slots](../../retail/rendering/mech-shape-drawing.md#hardpoint-attachment-slots-are-overwritten-every-frame)). `Mech_Draw` (`00012ac4`) calls it once per LOD root, with the root's 6-byte `CONNECT_INFO` entry from the type record's `+0xce` (the list `MechType_BindHardpointSlots` built: an array of part-slot addresses and its count), the record's ten blank `CONFIG_PART`s at `+0xd4` and the machine's manager at `+0x198`, whose first six bytes are the mount array and its count ([the manager](weapon-mounts.md#the-manager)), then calls `Shape_DrawAtDetailLevel`. It walks the slots and the mounts together, index by index, and stops at the end of the slots; while the mount array lasts, a slot that holds a part is given the mount, or the blank part when the array's entry is empty, with the slot's part's transform id (`+4`) and part id (`+6`) copied over, and the blank cursor moves `0x18` for each slot a mount entry was taken for. The mount is drawn through its own slot `+0x20`, `WeaponMount_RenderWithDetailBias` (`0000a4cc`, [the slots](weapon-mount-classes.md#the-slots)), which draws the weapon model at its `+0x10`; a blank part, which `ConfigPart_Ctor` (`00019f78`) builds with no model, draws through `ConfigPart_Render` (`00002718`), which with no model draws nothing.

### The light objects

- **`TinyObj_GetPosition` (`00001764`)**, (object): copies `+0x06` and `+0x0a` into the static triple `TinyObj_PositionBuffer` (`000ae4c4`), whose third dword it zeroes on the first call under the latch `000ae4c0`, and returns it. ES2's (`00401f08`).
- **`TinyObj_InstallModelTransform` (`000017a8`)**: the position from the object's slot `+8`, a rotation built from (0, 0, `+0x0e`) with `BuildEulerRotationMatrixQ14`, installed with `Raster_SetModelTransform`; **`TSTinyObj_Draw` (`00001924`)** runs it and draws the instance at `+0x10` through its slot `+0x20`. ES2's (`00401f50`, `00402e98`).
- **`Raster_SetModelTransformFromPose` (`00001808`)**: the rotation from the angles at `+0x12` and the position at `+0x06`, installed; **`TSRegObj_Draw` (`00001a84`)** runs it and draws the instance at `+0x1c`; **`RegObj_GetPosition` (`00001a2a`)**, frameless, returns the object + 6. ES2's (`00401fa8`, `00402cd4`, `00402cef`).

## Configurable parts — `CONFIG_PART`

A `CONFIG_PART` is a `0x18`-byte shape part whose `+0x10` holds a model and `+0x14` the model's private cell frames (`ShapeInstance_CtorCellFrames`, `0000252c`).

- **`ConfigPart_CreateArray` (`00002788`)**, (array, models, source): ES2's (`004031d4`): the source at the array's `+6`, its `int16` count (`+4`) at the array's `+4`, a new array of that many pointers at `+0`, each a new part built inline (`ClassItem`'s, `TSBase`'s and `TSPartBase`'s tables, `ShapeInstance_CtorCellFrames` on `+0x10` with the next model, `CONFIG_PART`'s table). ES1 steps the model cursor for each element whether its allocation succeeded or not, where ES2's steps it only for a built part.
- **`ConfigPartArray_StepFrame` (`00002920`)**, (array, index): ES2's (`004032d4`): the part's first cell frame becomes (frame + 1) modulo the first word of its model's `+0x20`, and it answers 1 when that wrapped to 0.
- **`ConfigPart_DtorDetAnimObjCopy` (`000028e0`)** is the 44 copies' destructor ([vtables](../formats/compiler.md#vtables)), `HeapBlockOwner_Dtor` (`00002598`) on `+0x10` then `TSBase_Dtor`.

`es2_xref.py` finds no reference to `ConfigPart_CreateArray` or `ConfigPartArray_StepFrame` ([Open](#open)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Context_GetTransform`, `Context_SetFromTransform` | `000016e8`, `00001718` | — |
| `TinyObj_GetPosition`, `TinyObj_InstallModelTransform`, `Raster_SetModelTransformFromPose`, `SimObject_InstallModelTransform` | `00001764`, `000017a8`, `00001808`, `00001854` | `00401f08`, `00401f50`, `00401fa8`, `00401fe4` |
| `TSTinyObj_Ctor`, `Drawable_Dtor`, `TSObj_ReturnZero`, `Drawable_GetRadius`, `TinyObj_Dtor` | `00001870`, `000018d4`, `000018e8`, `000018ed`, `000018f8` | `0040201c`, `00478615`, `Stub_ReturnZero`, `Drawable_GetRadiusZero`, `00402f94` |
| `TSTinyObj_Draw`, `TSTinyObj_Dtor` | `00001924`, `00001944` | `00402e98`, `00402eb3` |
| `TSRegObj_Ctor`, `REG_OBJ_DtorThunk`, `RegObj_GetPosition`, `RegObj_Dtor` | `00001988`, `00001a20`, `00001a2a`, `00001a38` | `004020b8`, `00402e3a`, `00402cef`, `00402e02` |
| `TS_REG_OBJ_DtorThunk`, `TSRegObj_Draw`, `TSRegObj_Dtor` | `00001a78`, `00001a84`, `00001aa4` | `00402d9c`, `00402cd4`, `00402cfa` |
| `TSObj_Ctor`, `MatObj_GetPosition`, `MatObj_Dtor` | `00001b04`, `00001c9c`, `00001cb0` | `SimObjectBase_Constructor`, `00402c75`, `0046b97d` |
| `TSObj_Render`, `TSObj_GetPosition`, `TSObj_Dtor`, `TSObj_GetRadius` | `00001cdc`, `00001cfc`, `00001d10`, `00001d58` | —, `SimObject_GetPosition`, `0046b890`, `SimObject_GetShapeRadius` |
| `SimObjectBase_ConstructShaped`, `ShapeInst_AddThread`, `AnimObj_Dtor` | `00001da0`, `00001f48`, `00001f98` | `00402278`, `00402374`, `00402400` |
| `SimObject_ApplyRootMotion`, `SimObject_ApplyRootMotionIfEnabled`, `SimObject_PushTransform`, `SimObject_PopTransform` | `0000205c`, `0000215c`, `00002184`, `00002248` | `0040250c`, `00402604`, `00402628`, `004027fc` |
| `SimObject_StaticInit`, `AnimThread_ElementCtor`, `maybe_Point3SPair_CtorTSObjCopy`, `Transform_CtorTSObjCopy` | `0000230c`, `00002350`, `00002440`, `000024b8` | `00402bb8`, `00402c36`, the element constructors' code |
| `ShapeInstance_CtorCellFrames`, `HeapBlockOwner_Dtor`, `MechType_BindHardpointSlots`, `Mech_SpliceHardpointShapes`, `ConfigPart_Render` | `0000252c`, `00002598`, `000025c4`, `00002654`, `00002718` | `00402fc0`, `00403020`, `0040304c`, `004030d0`, `0040316c` |
| `ConfigPart_CreateArray`, `TSPartBase_DtorDetAnimObjCopy`, `ConfigPart_GetClassTagDetAnimObjCopy`, `ConfigPart_DtorDetAnimObjCopy`, `ConfigPartArray_StepFrame` | `00002788`, `000028ac`, `000028d6`, `000028e0`, `00002920` | `004031d4`, —, —, `00420730`, `004032d4` |
| `SimObjectBase_ConstructAnimated`, the four `TS_OBJ` slot copies, `DetAnimObj_Dtor` | `00002970`, `000029c4`–`00002a00`, `00002a48` | `0040332c`, —, `00426f98` |
| `SimObjectBase_ConstructStatic`, `SimObjectBase_ConstructWithDetailTable`, `Shape_DrawAtDetailLevel` | `00002a74`, `00002ac8`, `00002b30` | `00403368`, `004033a4`, `004033e4` |

## Globals

| | Address |
|---|---|
| the class tables, [above](#the-classes) | `0009020a`–`00090406` |
| `g_ShapeDetailSizeScaleQ10`, `g_ShapeDetailBias`, `int16` | `000903e0`, `000903e2` |
| the detail setting's tables and detail-part biases, [above](#the-detail-setting--shapedetail_applydetailsetting-00057c08) | `00096bc4`–`00096c3b`, `000bc198`, `000bc2b8`, `000bc460` |
| `TinyObj_PositionBuffer` and its latch | `000ae4c4`, `000ae4c0` |
| the save buffers, [above](#saving-and-restoring-the-transform) | `000ae4d0`–`000ae9ac` |

## Open

- **Open:** what the manual calls preferences option 6, the panel's SHAPES. A retail check: SHAPES at its lowest changes how near HERCs switch to their crude models, and the manual's name for it.
- **Open:** what reaches `Context_SetFromTransform`, `TSTinyObj_Ctor`, `TSRegObj_Ctor`, `ConfigPart_CreateArray` and `ConfigPartArray_StepFrame`, to which `es2_xref.py` finds no branch or stored pointer (the one dword it finds holding `00001717`, at `000584ed`, is a call's displacement in `Sim_Main`'s loop); and whether anything else uses the `TINY_OBJ` and `REG_OBJ` tables, which only these files' constructors and destructors store.
- **Open:** the callers `es2_xref.py` finds and this batch did not read: `00003cbc` and `0001ac58` (the `DET_ANIM_OBJ` constructors and `ShapeInst_AddThread`), `00002df8` and `00003058` (root motion).
- **Open:** a positive control in the running game ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: `BP <CS>:00002654` fires once per LOD root for each HERC drawn in a frame, and with a fitted weapon at a hardpoint the slot's part afterwards is the mount, so the weapon shows on the HERC.
