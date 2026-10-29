# The aim point — vtable `+0x24`

The point on an object that a homing weapon steers at, the HUD target indicator marks, and line of sight is drawn from. A Targeting Pod replaces it with one of the selected machine's components — [`targeting-pod.md`](targeting-pod.md).

`Rocket_HomingSteer` (`0040a254`), `Bullet_HomingSteer` (`0040aff0`) and the HUD target indicator (`Player_ResolveTargetAimPoint`, `0041b728`) share one branch verbatim: call the target's vtable `+0x24`, and if it returns a record, transform that record's `+0x14` by the target's own rotation; otherwise use the raw origin.

## The record is a shape node transform

**The record is a shape node transform, not a bounding box.** It is `shapeInstance+0x16 + index*0x20`, the same 0x20-byte per-node array `Mech_ComponentGeometryTest_Candidate` indexes — 9 matrix shorts then a 3-int translation at `+0x14`..`+0x1f`. So `+0x14` is the node's model-space position and `+0x1c` is its Z, which is what the [line-of-sight test](sensors.md#line-of-sight--detection_lineofsight-00412608) adds to the object's own.

## Which node is per class

- **Mech** — `Mech_GetAimNodeTransform` (`00417b98`) pushes the type record's `+0x0c` (`.DAT` file offset 10, the camera node, [`mech-locomotion.md`](mech-locomotion.md#mech-type-record)) as the part id, so a HERC is aimed at **through its cockpit node**, the same one the pilot's eye rides. It walks and leans with the machine. Retail rises are 7.2 m (HEADHUNT) to 10.4 m (ACHILLES) above the model origin, which sits on the ground.
- **Structure** — all five structure vtables install `Base_GetAimNodeTransform` (`00403548`), which fills a static record with a fixed matrix and the translation `(0, 0, BASES.DAT +0x2c)`, the type's aim-point height (1000 to 2000; [`bases-dat.md`](../formats/bases-dat.md), [`structure-behaviour.md`](structure-behaviour.md#what-a-structure-is-aimed-at)). A structure is aimed at that far up its side, the same point its own `+0x30` (`Base_GetAimPoint`, `0040351c`) gives, and sights from that height. A turret standing just behind a rise stays in line of sight over the rise's edge because of it; at 500 it drops out.
- **Flyer** — installs `SimObject_GetAimNodeTransform_None` (`00411a9c`), which is `return 0`, so a flyer is aimed at its raw origin and sights from the literal 500.

## Rejected readings

| Obvious reading | Actually |
|---|---|
| Structures sight from the literal 500 because they install the `return 0` stub at vtable `+0x24` | That stub (`00411a9c`) is the flyer's and the base class's; all five structure vtables install `Base_GetAimNodeTransform` (`00403548`). See [Which node is per class](#which-node-is-per-class) |
