# DTS node posing — how a shape's geometry follows its skeleton (DBSIM.EXE)

For a machine, two mechanisms rewrite the shape before any of this runs — the hardpoint splice and the per-frame LOD root pick. See [`mech-shape-drawing.md`](mech-shape-drawing.md).

Every geometry group in a DTS shape is drawn through the transform of the node it names, taken from the shape instance's per-node world array. That array is what the animation pipeline in [Keyframe interpolation](#keyframe-interpolation) writes, so posing geometry is the same mechanism as posing the cockpit eye ([`mech-locomotion.md`](../simulation/mech-locomotion.md#cockpit-eye-and-bob)) — just applied to every node instead of one.

## The draw path

| Address | Role |
| --- | --- |
| `004758c8` | `TSGroup_RenderPolys` — DBSIM's counterpart to the VSHELL symbol of that name |
| `00476014` | Reads the group's own transform id and forwards it |
| `00476030` | `ShapeNode_InstallTransform` — composes the node's world transform with the object's model transform, and installs it |

`TSGroup_RenderPolys` binds the group's index/point/surface arrays to the poly-render globals, then walks `group+0x1c` calling each poly's vtable slot `+0x1c`. Before any of that it calls `00476014`, which is a one-line forwarder:

```c
ShapeNode_InstallTransform(group, out, (int)*(short *)(group + 4));   // group+4 == TSBasePart.Transform
```

`ShapeNode_InstallTransform` then, for a non-negative transform id whose node is not the one already installed, the first time that node is reached since the bind:

```c
Transform_Concat((short *)(id * 0x20 + g_ShapeNodeWorldTransforms), g_ShapeObjectModelTransformPtr, out);
Raster_SetModelTransform((undefined2 *)out);              // install as the current model transform
Raster_SaveState(&g_TSTransformStates[id + 1]);           // 0x110-byte slots
```

`g_ShapeNodeWorldTransforms` (`006b7bec`) is the shape instance's `+0x16` per-node world array (stride `0x20`, indexed by transform id). `g_ShapeObjectModelTransformPtr` (`006b7c14`) is `+0x20` of slot 0 of `g_TSTransformStates` (`006b7bf4`): the model-transform pointer `Raster_SaveState` recorded when `ShapeInst_BindNodeTransformArray` (`00475fd8`) saved the renderer's state into slot 0 as it bound the instance, so the transform the object itself is drawn with. `&DAT_006bb335` is a per-node flag array the bind clears: a node already composed has its state in slot `id + 1`, and a later group on it restores that slot (`Raster_RestoreState`) instead of composing again.

A negative transform id composes nothing. When a node's state is installed it restores slot 0, the object's own state; otherwise it leaves the current state standing.

`TSBasePart.Transform` at offset `+4` is the same field `Cockpit_TargetAnglesFromCameraBone` (`0041ef14`) and the cockpit eye resolve the mech type record's camera node (`typeRec+0x0c`) through — one field, one meaning, geometry and named nodes alike.

## The shape's own node transforms

A `TSShape` chunk ends, after its part list, with two counts and two arrays, which `TSShape_ReadFromStream` (`00490d5c`) reads in this order:

```
int16  nodeTransformCount
int16  sequenceCount
int16  sequenceFrameCounts[sequenceCount]          // see dts-billboards.md
byte   nodeTransforms[nodeTransformCount][0x20]
```

Each node transform is the 32-byte transform record `Transform_Concat` composes ([`sim-object-layout.md`](../simulation/sim-object-layout.md#the-objects-frame-is-a-transform-and-its-position-is-that-transforms-translation)). The loader builds the array with `Transform_Ctor` and reads the bytes straight over it, so the file holds each record exactly as memory does. `TSShapeInstance_Render` (`00490b10`) binds the shape's array as `_DAT_006b7bec` through `ShapeInst_BindNodeTransformArray` (`00475fd8`) before drawing, so a plain shape instance, which has no per-node array of its own, draws its groups through the shape's transforms where an animated instance uses its `+0x16` world array.

**No retail shape has any.** All 479 shape roots in the 55 retail `.DTS` files carry a count of 0.

## Keyframe interpolation

DBSIM interpolates node poses between keyframes, by the same intra-frame fraction it ramps root motion with ([Root motion](../simulation/mech-locomotion.md#root-motion)). The pose pipeline is three calls, in `ShapeInstance_StepAnimation` (`00478c2c`):

| Address | Symbol | Role |
| --- | --- | --- |
| `004789a0` | `AnimThread_StepAll` | advances every thread by the timestep |
| `004799a4` | `AnimThread_EvalNodeLocals` | **the interpolator** — writes each node's local transform |
| `00478b58` | `ShapeInst_BuildWorldTransforms` | composes locals up the relation list into `shapeInst+0x16` |

Three arrays on the shape instance: `+0x12` per-node **local** transforms (stride `0xc`), `+0xe` per-node dirty flags, `+0x16` per-node **world** transforms (stride `0x20`, indexed by transform id — the array `Cockpit_TargetAnglesFromCameraBone` and the cockpit eye read).

`AnimThread_EvalNodeLocals`, per animated column:

- reads the transform-pool index at **(sequence, frame)** and at **(nextSequence, nextFrame)** — both cursors the thread already keeps;
- identical indices → copy the 12-byte record straight, no blend;
- otherwise → `Anim_BlendKeyframeTransforms` (`00492600`) at **`(frameAccumulator * 0x400 + frameDuration / 2) / frameDuration`**, a rounded Q10 fraction — the same `thread+0x1c / thread+0x1e` fraction root motion is ramped by. Pose and ground movement therefore ride one clock, which is what keeps the gait smooth at any speed: a slow gait stretches keyframes out in time and the pose keeps moving between them instead of stepping.
- columns whose part id is 0 are **skipped entirely**, so transform 0 keeps its default and never takes an animated pose. Column 0 of every sequence carries that sequence's root motion, not a pose, which is what the skip exists to keep out of the node array. Across all 18 retail HERCs column 0 holds part id 0 in every sequence, transform 0's parent is -1, and no node's chain reaches it — so the skip only matters to a caller that walks *all* transform ids, as a whole-skeleton pose does.

`Anim_BlendKeyframeTransforms` blends a 12-byte record (3 euler shorts, then 3 translation shorts): rotation along the **shortest arc** (bias by `0x10000`, subtract, fold back when over `0x7fff`), translation as a truncating lerp on the 16-bit difference, both `* q10 >> 10`. Fixed point throughout; no float.

`ShapeInst_ExpandRootTransform` (`00478b10`) confirms the local record's layout as `[eulerX, eulerY, eulerZ, x, y, z]` shorts, and thread field offsets are confirmed here too: `+4` sequence, `+6` frame, `+8` nextSequence, `+10` nextFrame, `+0x1c` frameAccumulator, `+0x1e` frameDuration.

### Evaluation cadence — per tick, not per rendered frame

`ShapeInstance_StepAnimation`'s **only** caller is `SimObject_ApplyRootMotion` (`0040250c`), which `Mech_IntegrateMotion` runs once per sim tick. So poses are re-blended once per tick.

There is no separate render rate for them to be per-frame at: `Time_BeginSimTick` (`004677bc`) spin-waits the whole loop to 40 ms, so **tick and frame are the same thing in DBSIM** (see [`dbsim-physics-notes.md`](../simulation/dbsim-physics-notes.md#fixed-point-math-toolkit)). A vanilla frame always shows a pose evaluated that same iteration, at 25 Hz.

## Several threads on one shape

A shape instance holds up to ten animation threads in slots at `+0x1a`, each playing one sequence. `ShapeInst_EvalAllNodeLocals` (`004789f4`) runs `AnimThread_EvalNodeLocals` over them **last slot first**, each overwriting the local transform of every node its sequence covers with no regard for what is already there. The **first** slot's writes are therefore the ones left standing.

The slots are kept in ascending order of the playing sequence's **priority**, the second short of the `ANSequence` header (`+6` in memory, after the tick count and before the ground-movement flag at `+8`), which `AnimThread_SequencePriority` (`00478d80`) reads. `ShapeInst_SortThreadsByPriority` (`004788c4`) bubble-sorts the slots, swapping only on strictly greater, so threads of equal priority never change places and stay in registration order. `ShapeInst_RegisterThread` (`00478930`) runs the sort on every registration and `AnimThread_StepAll` (`004789a0`) after every advance, so a thread changes rank on the step its sequence changes. The lowest priority wins a node two threads cover.

A HERC registers three, in the order `Mech_Constructor` builds them — locomotion, then the turret's twist and pitch ([`torso-aim.md`](../simulation/torso-aim.md#three-threads-per-machine)). Their retail priorities:

| Sequence | Priority |
|---|---|
| Walk, run, turn in place, the death fall | 0 |
| The two stop/step-off sequences (`typeRec+0x12`, `+0x14`) | 96 |
| Twist | 1 |
| Pitch | 2 (HEADHUNT 1) |

So locomotion holds the first slot while the machine walks, runs or turns, and the last while it stands in a step-off sequence, which is the sequence every HERC starts in.

On retail data the ranking decides no node, because the sequences a type record names cover disjoint nodes on every chassis. On 17 of the 18 bipeds locomotion covers nodes 1, 2, 3 and 5-10, twist covers 4 and pitch covers 11 (MONGOOSE 11 and 12). HEADHUNT's locomotion covers 1-3 and 6-11, its twist 5 and its pitch 4 and 12. PITBULL's locomotion covers 1-5 and 7-18, its twist 6 and its pitch 19. Column 0, which every sequence carries, is skipped by `AnimThread_EvalNodeLocals`.

What the ranking does decide is which thread carries root motion. `SimObject_ApplyRootMotion` seeds the first slot through `ShapeInst_SeedRootTransform` (`00478a70`) before stepping, and `AnimThread_StepAll` reads the first slot back after its re-sort: the twist thread while locomotion plays a step-off sequence. See [Open](#open).

## Fleet shape

All 18 retail HERCs: geometry occupies **11 groups**, on transform ids **1-11**, out of 12 nodes (13 for MONGOOSE and HEADHUNT). Transform 0 carries sequence root motion and never places geometry.

**No node in any retail HERC has a rotation in its rest pose.** Every entry the `ANAnimList`'s default transforms point at has all three euler shorts zero, fleet-wide. Rotation is something an *animated* node acquires; a rest pose is pure translation.

## Cyclic and one-shot sequences

An animation list holds two kinds of sequence, and the difference is the **chunk's class**, not a flag: `ANCyclicSequence` loops, plain `ANSequence` plays once. DBSIM reaches the frame step through the sequence object's own vtable, so one list mixes both freely.

| Slot | Cyclic | One-shot |
|---|---|---|
| `+0x20` next frame | `004786d8` — wraps to 0 past the last frame | `00478654` — **clamps**, returning the last frame forever |
| `+0x24` previous frame | `004786f8` — wraps to the last frame | `00478670` — holds at 0 |

The clamp is what makes a one-shot observable: `frame == nextFrame` is true only on a played-out non-cyclic sequence, and that equality is the sole end-of-sequence test in the simulation. See [`mech-locomotion.md`](../simulation/mech-locomotion.md#going-down).

**Every retail chassis carries exactly one one-shot**, and its own death sequence (mech type record offset 68) names it: index 7 on the 18 bipeds, 2 on the PITBULL, 1 on the SPIDER. The RAZOR's list holds a single sequence and its death sequence of 7 is out of range, which never bites because a flyer has no locomotion thread.

## Open

- **Open:** what reading the root from the first slot does on the step locomotion enters or leaves a step-off sequence. `ShapeInst_SeedRootTransform` seeds the slot that is first before `AnimThread_StepAll` advances, and the re-sort can put a different thread first by the time the root is read back.
