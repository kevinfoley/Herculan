# Impact effects (`dat\EXPLOS.DAT`, DBSIM.EXE `EXPLO.CPP`)

Addresses are DBSIM virtual addresses.

What happens where a shot lands. An effect is a `dts\EXPLOS.DTS` root standing still at the point of impact, playing its flipbook of billboards ([`../formats/dts-billboards.md`](../formats/dts-billboards.md)) through exactly once — unlike a fire, which loops the same kind of flipbook until it burns out ([`destruction-effects.md`](destruction-effects.md#fire)). Like a tracer or a travelling round it is not a machine: it lives in a pool of its own, `g_ExplosionPool` (`DAT_004a96a2`), which `Sim_MainTick` walks first in its effect-pool pass — ahead of the smoke, fire, debris and meteor pools, the tracer and round pool (`DAT_004a9746`) and the machine list — so nothing can shoot it and nothing collides with it. The pool holds 40 effects; a spawn into a full pool builds nothing.

The tables, the texture banks and the shape file it draws from are [`../formats/explos-dat.md`](../formats/explos-dat.md). Everything below refers to that file's type row by offset.

## Construction — `Explosion_Construct` (`00407f1c`)

`(effect, typeId, worldPoint, ownerObject, playSound)`. Resolves the shape through the two tables, records the type id (`effect+0x41`) and the owner (`effect+0x57`), resets the shape instance's frame counter for the type's sequence to 0, loads the countdown from the frame interval (`+0x02`), and optionally lays a ground shape and builds the light. `playSound` gates the sound (`+0x24`), not the effect. What the light is and what it does to a shape is [`../formats/effect-lights.md`](../formats/effect-lights.md); the ground shape, which no retail row asks for, is [`ground-shapes.md`](ground-shapes.md#an-impact-effects-shape).

The owner is the object the effect belongs to for drawing ([below](#drawing)): the struck structure or machine at their hit tests, the structure at its collapse and death sequence, the machine at a component's loss. A flyer's hit test, a terrain hit and a debris piece's burst pass none.

## Tick — `Explosion_TickUpdate` (`0040813c`)

```
if (CountdownTimerTick(effect+0x4a) != 0) return alive;   // counter is the short at +0x4b
if (animSequence < 0) return finished;    // no flipbook to step
frame = (frame + 1) % shapeFrameCount;
if (frame == 0) return finished;          // the flipbook wrapped: the effect is over
light?.SetIntensity(row[+0x08 + frame*2]);    // the intensity ramp
groundShape?.frame = (groundShape.frame + 1) % groundShapeFrameCount;
timer = row[+0x02];                       // into effect+0x4b
return alive;
```

`Sim_MainTick` calls it directly, once per pool entry, and queues a finished effect for deletion; the effect class's vtable slot `+0x14` is a stub, not this function. Nothing moves the effect and nothing else can stop it. The frame count comes off the loaded shape (`shape+0x20`'s per-sequence array), not off the table. The intensity ramp has twelve entries, and a longer flipbook reads on past it into the proximity radius at `+0x20` and the sound id at `+0x24`. In `EXPLOS.DAT` with `EXPLOS.DTS` two light-bearing rows do: row 7 (root 18, 15 frames) reads 0, 0 and 30 on its last three frames, and row 16 (root 5, 14 frames) reads 32 and 0 on its last two.

The tick argument is the record base, not the counter: `Math_CountdownTimerTick` reads the `short` at `+1` from the pointer it is given. See the countdown-timer entry in [`dbsim-physics-notes.md`](dbsim-physics-notes.md).

## Which effect a shot spawns

Every `PROJ.DAT` record carries three four-entry impact-effect arrays. The shot record's `+0x0a` (see [`weapon-firing.md`](weapon-firing.md#the-shot-record)) points at all three as one twelve-entry `short` array, indexed `group * 4 + (rand & 3)` — so all four ids in a group are equally likely and the file's own field order is the group order.

| Group | `PROJ.DAT` array | Spawned by | Site |
|---|---|---|---|
| 0 | shield | a shot the struck facing's shields **fully** absorbed | `Mech_DirectFireHitTest`, base `+0` |
| 1 | ground | a shot ending on **terrain**; also an armour hit that left the struck component in the health band it was already in | `Sim_RaycastObjectList` tail, base `+8`; `Mech_ApplyDirectFireDamage` with `group == 1` |
| 2 | armour | an armour hit that dropped the component's health band; also the only array the non-mech hit tests (`Base_DirectFireHitTest`, `00405038`, and `Flyer_DirectFireHitTest`, `00421c8c`) use | `Mech_ApplyDirectFireDamage` with `group == 2`, base `+0x10` |

**All 27 retail records carry byte-identical ground and armour arrays**, so groups 1 and 2 are indistinguishable on real data.

### The terrain impact is the raycast's own job

`Sim_RaycastObjectList` (`00426528`) keeps two flags — *something was struck* and *an object was struck* — and at its tail spawns an effect when the first is set and the second is not:

```
point = rayTransform.TransformPoint(0, rayLength, 0);
Explosion_Construct(alloc(pool), impactFx[4 + (rand & 3)], point, owner: 0, playSound: false);
```

So a shot that ends in the dirt puts one down and a shot that ends on a machine does not, even though the ground clipped the ray first in both cases. **The ground pseudo-object `Sim_RaycastTerrain` reports has nothing to do with it** ([`hit-detection.md`](hit-detection.md#the-sweep--sim_raycastobjectlist-00426528)). Unlike the object-hit spawns this one passes no owner and suppresses the sound.

The object-hit sites spawn from inside the hit test itself, at `transform(0, hitDistance, 0)` off the shot's frame, and do so whether or not the sweep goes on to find something nearer.

## Drawing

`Scene_SubmitFrameObjects` (`0042841c`) walks `g_ExplosionPool` once a frame, after the machine lists, and asks `Explosion_IsHiddenFromOwnerCockpit` (`00408240`) whether to skip each effect. The answer is no when the effect has an owner and the cockpit camera is attached to that owner ([`external-views.md`](external-views.md)), unless the type id is 2 or 11 to 14, which are always drawn. So from inside the cockpit of the machine being hit, the impact effects on its own hull are not drawn. A drawn effect is filed into the draw table ([`../formats/distance-fog-and-sky.md`](../formats/distance-fog-and-sky.md)) under its owner's cached terrain cell (`Explosion_GetOwnerDrawCell`, `00408228`: `owner+0x1e8`, the pair this walk stored on the owner earlier in the same pass), or under the cell its own position falls in when it has no owner.

## Open

- **Unported:** the owner rule under [Drawing](#drawing): the owner-attached-camera suppression and the filing under the owner's cell.
- **Unported:** the 40-entry pool limit: a forty-first simultaneous effect is not built.
- **Open:** which `EXPLOS.DAT` types are 2 and 11 to 14, and why those escape the owner rule.
