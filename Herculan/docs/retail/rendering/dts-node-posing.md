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

## Drawing a shape

**A shape is drawn by drawing its parts in order, unless it lies wholly off screen.** `TSShape_Render` (`004909ac`, `TSShape`'s vtable `+0x1c`) draws a shape, and `TSShapeInstance_Render` (`00490b10`, the plain shape instance's) draws the instance's shape the same way:

1. The per-sequence cell frames `TSCellAnimPart_Render` reads go to `006b7bf0` (`ShapeRender_BindCellFrames`): the shape's own array at `+0x1c`, or the instance's copy at `+8` ([`dts-billboards.md`](dts-billboards.md#tscellanimpart_render-004767e4)).
2. The shape's node transforms, `shape+0x18`, become the per-node array the groups are drawn through (`ShapeInst_BindNodeTransformArray`, `00475fd8`, [The draw path](#the-draw-path)). A `TSShape` loads node transforms of its own ([`../formats/dts-shape.md`](../formats/dts-shape.md#the-shapes-own-node-transforms)), so a plain shape instance draws its groups through the shape's; an animated instance points `shape+0x18` at its `+0x16` world array first ([The animated shape instance](#the-animated-shape-instance)).
3. When the byte `004a62f8` is set, `TSBounds_ScreenVisibility` (`0047613c`) tests the shape's bounding sphere against the screen. A result of 0 draws nothing. A result of 2, the sphere's box wholly inside the clip rect while the render context's clip mode (`+0x208`, [`../simulation/hud-target-indicator.md`](../simulation/hud-target-indicator.md)) is 1, sets the mode to 0, no clipping, for the draw and back to 1 after it. The byte is 1 in the image ([Open](#open)).
4. The active bitmap array is swapped for the shape's, `shape+0x26` ([`dts-texture-binding.md`](dts-texture-binding.md#tsbitmapparts-texture-lookup-vshell)), the parts are drawn (`TSPartList_Render`, `004766fc`), and the active array is put back.

## The animated shape instance

**An animated shape keeps a pose of its own for every node of its skeleton.** An `ANShapeInstance` extends the plain shape instance with:

| Offset | Field |
|---|---|
| `+4` | the shape |
| `+0xc` | the node count, int16: the shape's animation list `+0x16` |
| `+0xe` | a dirty byte per node: bit 0 the local transform changed, bit 1 the world transform was rebuilt |
| `+0x12` | a **local** transform per node, the 12-byte keyframe record `[eulerX, eulerY, eulerZ, x, y, z]` of int16s |
| `+0x16` | a **world** transform per node, the 32-byte transform `Transform_Concat` composes; the array `Cockpit_TargetAnglesFromCameraBone` and the cockpit eye read |
| `+0x1a` | ten thread slots ([Several threads on one shape](#several-threads-on-one-shape)) |

All three per-node arrays are indexed by transform id, and the shape's animation list is at `shape+0x2a`.

`ShapeInst_Construct` (`0047872c`) clears the ten slots, sizes the three arrays from the node count, sets each node's local transform to the keyframe-pool record the animation list's default-transform table names (`+0x18`, an int16 pool index per node), marks every node dirty and builds the world transforms. It then sets world slot 0 to the identity, and also fills a static transform at `006c572c` with the identity.

`ShapeInst_BuildWorldTransforms` (`00478b58`) walks the animation list's relation list, int16 `(parent, child)` pairs at `+0xc`, their count at `+0xa`, in list order:

- **parent -1:** when the child's bit 0 is set, its world transform is its local one expanded (`ShapeInst_ExpandRootTransform`, `00478b10`): `BuildEulerRotationMatrixQ14` of the three angles, the three int16 translations widened to the int32s at `+0x14`. The child's bit 1 is set.
- **otherwise:** when the child's bit 0 or the parent's bit 1 is set, its world transform is its expanded local one composed onto the parent's (`ShapeInst_ComposeChildTransform`, `00478ab8`: `Transform_Concat(local, parentWorld, out)`), and the child's bit 1 is set.

It then clears every dirty byte. A parent's pair has to come before its children's for them to follow it, and a node with neither bit keeps the world transform of the last build.

`ANShapeInstance_Render` (`00478c98`) composes world slot 0 onto the current model transform and installs it (`ShapeInst_PushModelTransform`, `00478c54`), points the shape's node-transform array (`shape+0x18`) at the instance's world array, draws through `TSShapeInstance_Render`, and reinstalls the model transform it found (`ShapeInst_PopModelTransform`, `00478c84`).

## Keyframe interpolation

DBSIM interpolates node poses between keyframes, by the same intra-frame fraction it ramps root motion with ([Root motion](../simulation/mech-locomotion.md#root-motion)). The pose pipeline is three calls, in `ShapeInstance_StepAnimation` (`00478c2c`):

| Address | Symbol | Role |
| --- | --- | --- |
| `004789a0` | `AnimThread_StepAll` | advances every thread by the timestep |
| `004799a4` | `AnimThread_EvalNodeLocals` | **the interpolator** — writes each node's local transform |
| `00478b58` | `ShapeInst_BuildWorldTransforms` | composes locals up the relation list into `shapeInst+0x16` |

The three steps work on the shape instance's per-node arrays ([The animated shape instance](#the-animated-shape-instance)).

`AnimThread_EvalNodeLocals`, per animated column:

- reads the transform-pool index at **(sequence, frame)** and at **(nextSequence, nextFrame)** — both cursors the thread already keeps;
- identical indices → copy the 12-byte record straight, no blend;
- otherwise → `Anim_BlendKeyframeTransforms` (`00492600`) at **`(frameAccumulator * 0x400 + frameDuration / 2) / frameDuration`**, a rounded Q10 fraction — the same `thread+0x1c / thread+0x1e` fraction root motion is ramped by. Pose and ground movement therefore ride one clock, which is what keeps the gait smooth at any speed: a slow gait stretches keyframes out in time and the pose keeps moving between them instead of stepping.
- columns whose part id is 0 are **skipped entirely**, so transform 0 keeps its default and never takes an animated pose. Column 0 of every sequence carries that sequence's root motion, not a pose, which is what the skip exists to keep out of the node array. Across all 18 retail HERCs column 0 holds part id 0 in every sequence, transform 0's parent is -1, and no node's chain reaches it — so the skip only matters to a caller that walks *all* transform ids, as a whole-skeleton pose does.

`Anim_BlendKeyframeTransforms` blends a 12-byte record (3 euler shorts, then 3 translation shorts): rotation along the **shortest arc** (bias by `0x10000`, subtract, fold back when over `0x7fff`), translation as a truncating lerp on the 16-bit difference, both `* q10 >> 10`. Fixed point throughout; no float.

The thread fields it reads are in [Animation threads](#animation-threads).

### Evaluation cadence — per tick, not per rendered frame

`ShapeInstance_StepAnimation` is called from `SimObject_ApplyRootMotion` (`0040250c`), which `Mech_IntegrateMotion` runs once per sim tick, so poses are re-blended once per tick. It is the one caller `es2_xref.py` finds; see [Open](#open).

There is no separate render rate for them to be per-frame at: `Time_BeginSimTick` (`004677bc`) spin-waits the whole loop to 40 ms, so **tick and frame are the same thing in DBSIM** (see [`dbsim-physics-notes.md`](../simulation/dbsim-physics-notes.md#fixed-point-math-toolkit)). A vanilla frame always shows a pose evaluated that same iteration, at 25 Hz.

## Several threads on one shape

A shape instance holds up to ten animation threads in slots at `+0x1a`, each playing one sequence. `ShapeInst_EvalAllNodeLocals` (`004789f4`) runs `AnimThread_EvalNodeLocals` over them **last slot first**, each overwriting the local transform of every node its sequence covers with no regard for what is already there. The **first** slot's writes are therefore the ones left standing.

The slots are kept in ascending order of the playing sequence's **priority**, the second short of the `ANSequence` header (`+6` in memory, after the tick count and before the ground-movement flag at `+8`), which `AnimThread_SequencePriority` (`00478d80`) reads. `ShapeInst_SortThreadsByPriority` (`004788c4`) bubble-sorts the slots, swapping only on strictly greater, so a sort never reorders threads of equal priority. Their order is registration order until one of them changes priority: once the thread registered first has sorted behind the other and then returned to the same priority, it stays behind. `ShapeInst_RegisterThread` (`00478930`) runs the sort on every registration and `AnimThread_StepAll` (`004789a0`) after every advance, so a thread changes rank on the step its sequence changes. The lowest priority wins a node two threads cover.

A HERC registers three, in the order `Mech_Constructor` builds them — locomotion, then the turret's twist and pitch ([`torso-aim.md`](../simulation/torso-aim.md#three-threads-per-machine)). The priorities the retail shape files carry:

| Sequence | Priority in the file |
|---|---|
| Walk, run, turn in place, the death fall | 0 |
| The two stop/step-off sequences (`typeRec+0x12`, `+0x14`) | 96 |
| Twist | 1 |
| Pitch | 2 (HEADHUNT 1) |

The SPIDER's list holds two sequences, and its turn in place and death fall are its stop sequence, so they carry its 96.

`Mech_Constructor` zeroes the 96. Right after registering the locomotion thread, and before the twist and pitch threads, it writes 0 into the priority of sequences `typeRec+0x12` and `+0x14` in the shape's own sequence table (`00415dbc`, `00415dd5`). The table belongs to the shape, so every machine of the type sees the change from then on. Every sequence a locomotion thread plays is therefore priority 0 at runtime, below the twist and pitch, and locomotion holds the first slot throughout, the step-off included; the twist follows it, and HEADHUNT's pitch, which ties the twist at 1, stays behind it by registration order.

On retail data the ranking decides no node, because the sequences a type record names cover disjoint nodes on every chassis, the stop sequences included. On 17 of the 18 bipeds locomotion covers nodes 1, 2, 3 and 5-10, twist covers 4 and pitch covers 11 (MONGOOSE 11 and 12). HEADHUNT's locomotion covers 1-3 and 6-11, its twist 5 and its pitch 4 and 12. PITBULL's locomotion covers 1-5 and 7-18, its twist 6 and its pitch 19. Column 0, which every sequence carries, is skipped by `AnimThread_EvalNodeLocals`.

The ranking also decides which thread carries root motion. `SimObject_ApplyRootMotion` seeds the first slot through `ShapeInst_SeedRootTransform` (`00478a70`) before stepping, and `AnimThread_StepAll` reads the first slot back after its re-sort. With locomotion first throughout, both are the locomotion thread on every step.

## Fleet shape

All 18 retail HERCs: geometry occupies **11 groups**, on transform ids **1-11**, out of 12 nodes (13 for MONGOOSE and HEADHUNT). Transform 0 carries sequence root motion and never places geometry.

**No node in any retail HERC has a rotation in its rest pose.** Every entry the `ANAnimList`'s default transforms point at has all three euler shorts zero, fleet-wide. Rotation is something an *animated* node acquires; a rest pose is pure translation.

## Animation threads

**A thread plays one sequence of the shape's animation list frame by frame, and moves on to another sequence either by a direct cut or through a transition the list offers from one of its frames.** It is a `0x52`-byte record:

| Offset | Field |
|---|---|
| `+0` | the animation list |
| `+4`, `+6` | the playing sequence and frame |
| `+8`, `+0xa` | the next sequence and frame, which the pose blends towards ([Keyframe interpolation](#keyframe-interpolation)) |
| `+0xc` | the armed frame: the frame of the playing sequence that starts a transition, -1 when none is armed |
| `+0xe`, `+0x10`, `+0x12`, `+0x14` | the armed transition: destination sequence, destination frame, duration, ground-movement keyframe |
| `+0x16`, `+0x18`, `+0x1a` | the request: sequence (-1 for none), frame (-1 for any), flags (bit 0 stops the thread on arrival) |
| `+0x1c` | the intra-frame offset, in animation ticks |
| `+0x1e` | the duration of the frame in play: the frame table's, or a transition's |
| `+0x20`–`+0x2c` | the frame's ground movement: the enable word, then the translation and rotation int16 triples ([Root motion](../simulation/mech-locomotion.md#root-motion)) |
| `+0x2e` | the stored root transform, 32 bytes |
| `+0x4e` | the playback rate, Q8: 256 plays at full speed, a negative rate backward |
| `+0x50` | flags: bit 0 the thread is at the request, bit 1 it is playing a transition frame |

It reads these fields of the animation list and its sequences:

| Field | Holds |
|---|---|
| list `+6` | the sequence table, a pointer per sequence |
| list `+0x12` | the keyframe pool, 12-byte local-transform records |
| list `+0x1e` | the transition table, 8-byte records of int16s: duration, destination sequence, destination frame, ground-movement keyframe |
| sequence `+0` | the vtable, whose four frame steps decide how the sequence ends ([Cyclic and one-shot sequences](#cyclic-and-one-shot-sequences)) |
| sequence `+6` | the priority |
| sequence `+8` | the ground-movement flag |
| sequence `+0xa` | the frame count |
| sequence `+0xc` | the frame table, 6-byte records of int16s: duration, transition count, first transition |
| sequence `+0x10`, `+0x12` | the column count, and each column's transform id |
| sequence `+0x16` | the keyframe-pool index of each frame's columns, frame by frame |

`AnimThread_Ctor` (`00478d3c`) stores the list, cuts to frame 0 of sequence 0 and sets the stored root transform to the identity.

### Starting a sequence

`AnimThread_SetSequence` (`004791a0`), `(thread, sequence, frame, offset)`, cuts to a frame. The playing and next sequence become `sequence`, the playing frame `frame`, the next frame the sequence's next step from it, the offset `offset` and the duration the frame table's. It loads the frame's ground movement and forgets any request: the request becomes `(-1, -1, 0)`, the armed frame -1 and the flags 1.

`AnimThread_SeekToPosition` (`00479238`), `(thread, sequence, position)`, parks a thread at a Q14 fraction of a sequence. It sums the sequence's frame durations, takes `t = Math_Q14Multiply(position, total - 1)`, walks the frames from 0 subtracting each one's duration while `t` is at least that duration, and cuts to the frame it stops on with `t` as the offset.

### Requesting a sequence

`AnimThread_SetTarget` (`00479570`), `(thread, sequence, frame, flags)`, stores the request and clears bit 0. When `sequence` is not -1 and the thread is not already there (playing `sequence`, on `frame`, at offset 0), it searches for a transition; otherwise it sets bit 0 and disarms.

`AnimThread_FindTransition` (`004792c8`) scans the playing sequence's frames in the direction of play: forward from the next frame, stepping with the sequence's next-frame step, or backward from the playing frame with its previous-frame step when the rate is negative. The scan ends when a step returns the frame it was given, as a one-shot does at its end, or comes back round to the frame it started from. A frame's transitions are its frame-table run in the transition table, and the first whose destination is the requested sequence arms the frame: the scanned frame goes to `+0xc` and the transition to `+0xe`–`+0x14`. With frame -1 requested that ends the search. Otherwise the destination sequence's short-of-target step for the direction of play is asked `(destination frame, requested frame, thread +0x1c)`, and only a 1 ends the search; a 0 goes on to the frame's next transition. When no transition qualifies the armed frame is -1, and `+0xe`–`+0x14` keep the last one tried.

`AnimThread_SetPlaybackRate` (`004795cc`) stores the rate and, when its sign changes, calls `AnimThread_SetTarget` again with the stored request, so the search reruns in the new direction.

### Advancing

`AnimThread_Advance` (`00479614`), `(thread, dt)`, does nothing while bit 0 and the request's stop flag are both set. Otherwise the step is `(dt × rate) >> 8`, an int16, and a step of 0 does nothing. With `o = offset + step`:

- **Holding at the start.** When `o < 0` on frame 0, frame 0 is not the armed frame and the sequence's previous step from frame 0 is 0 (a one-shot's), `o` becomes 0 and the next case applies.
- **`o == 0`.** The offset becomes 0. A transition frame in play (bit 1) is abandoned: the next sequence becomes the playing one, the next frame its next step from the playing frame, the duration the frame table's, the ground movement the frame's own, and bit 1 is cleared. The arrival test below sets bit 0.
- **`0 < o < duration`.** The offset becomes `o`.
- **Forward**, while `o` is non-zero and at least the duration:
  1. The frame's ground movement is committed to the stored root transform.
  2. The next sequence and frame become the playing ones.
  3. When that frame is the armed one, a transition frame starts: the next sequence and frame become the transition's destination, `o` drops by the old duration, the duration becomes the transition's, the armed frame -1, bit 1 is set, and the ground movement is the transition's keyframe, enabled. Otherwise the next frame becomes the sequence's next step, `o` drops by the old duration, the duration becomes the frame table's (a duration of 0 sets `o` to 0), bit 1 is cleared and the frame's ground movement is loaded. Either way the offset is 0.
  4. Arriving on the requested sequence at the requested frame, or any frame when it is -1, sets bit 0; with the stop flag `o` becomes 0.

  The offset then becomes `o`.
- **Backward**, while `o` is negative:
  1. The next sequence and frame are set to the playing ones.
  2. When the playing frame is the armed one, the transition frame is entered from its far end: the playing sequence and frame become the transition's destination, the duration the transition's, the armed frame -1, bit 1 is set, and the ground movement is the transition's keyframe. Otherwise the playing frame becomes the sequence's previous step, the duration the frame table's, bit 1 is cleared and the frame's ground movement is loaded.
  3. The frame's ground movement is taken back out of the stored root transform.
  4. When `o + duration` is not negative it becomes the offset and the loop ends; otherwise the offset is 0 and `o` grows by the duration.
  5. The arrival test is the forward one; with the stop flag the offset and `o` become 0.

How the ground movement is committed, taken out and read back as root motion is in [Root motion](../simulation/mech-locomotion.md#root-motion).

`AnimThread_ElapsedTicks` (`00478d98`) returns how far the thread is into its sequence: the durations of the frames from frame 0 to the playing frame, stepping with the next-frame step, plus the offset.

## Cyclic and one-shot sequences

An animation list holds two kinds of sequence, and the difference is the **chunk's class**, not a flag: `ANCyclicSequence` loops, plain `ANSequence` plays once. DBSIM reaches the frame step through the sequence object's own vtable, so one list mixes both freely.

| Slot | Cyclic | One-shot |
|---|---|---|
| `+0x20` next frame | `004786d8` — wraps to 0 past the last frame | `00478654` — **clamps**, returning the last frame forever |
| `+0x24` previous frame | `004786f8` — wraps to the last frame | `00478670` — holds at 0 |
| `+0x28` short of the target, forward: `(frame, target, offset)` | `00478714` — always 1 | `00478680` — 1 when `frame < target`, or `frame == target` and `offset == 0` |
| `+0x2c` short of the target, backward | `00478720` — always 1 | `004786ac` — 1 when `frame > target`, or `frame == target` and `offset == 0` |

The clamp is what makes a one-shot observable: `frame == nextFrame` is true only on a played-out non-cyclic sequence, and that equality is the sole end-of-sequence test in the simulation. See [`mech-locomotion.md`](../simulation/mech-locomotion.md#going-down).

**Every retail chassis carries exactly one one-shot**, and its own death sequence (mech type record offset 68) names it: index 7 on the 18 bipeds, 2 on the PITBULL, 1 on the SPIDER. The RAZOR's list holds a single sequence and its death sequence of 7 is out of range, which never bites because a flyer has no locomotion thread.

## Rejected readings

| Reading | Why it fails |
|---|---|
| The stop sequences' file priority of 96 ranks locomotion last while a HERC stands in a step-off sequence, handing the first slot, and with it root motion, to the twist thread | `Mech_Constructor` zeroes both stop sequences' priority before it registers the twist and pitch threads, so the 96 never ranks against another thread ([Several threads on one shape](#several-threads-on-one-shape)) |

## Open

- **Open:** what the byte `004a62f8` is for. A grep of the disassembly for its address finds only the two shape renders' reads.

- **Open:** no caller of `ShapeInstance_StepAnimation` other than `SimObject_ApplyRootMotion` found by `es2_xref.py`, and none of `AnimThread_ElapsedTicks`.
