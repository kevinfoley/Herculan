# `simvol0/gl/<HERC>.GL` — hardpoint list

Reverse-engineered from `DBSIM.EXE` in the `ES2Recon` Ghidra project; all addresses are DBSIM virtual addresses.

One file per chassis: the list of weapon hardpoints the machine has, in the order the simulator walks them. `GunLayout_LoadForMechType` (`0040fee8`, asserts in `GUNLIST.CPP`) reads it; `Mech_GetGunLayout` (`00420634`) returns the record array for a mech type. What the simulator builds from it is [`../simulation/weapon-mounts.md`](../simulation/weapon-mounts.md#the-join--mechloadout_constructweaponmounts-0040fff8).

```
int16   count
count x 26-byte record
```

## Record

| Offset | Size | Field | Role |
|---|---|---|---|
| `+0x00` | int16 | bone id | the model bone the gun rides and the shot leaves from (`WeaponMount_HardpointBoneId`, `0040e61c`); `GunLayout_CollectHardpointBones` (`0040fc50`) reports -1 in its place when the mounting code is 4 |
| `+0x02` | int16 | convergence pitch node | negative on every retail chassis; the mount constructor maps a negative value to zero, and zero is what lets the gun convergence apply — [`../simulation/weapon-firing.md`](../simulation/weapon-firing.md#gun-convergence--mech_convergegunsonrange-0041a74c) |
| `+0x04` | int16 | convergence yaw node | as `+0x02` |
| `+0x06` | byte | mounting code | 0 on top, 1 underneath, 2 left side, 3 right side, 4 invisible. Codes below 4 are drawn; the code picks the weapon model shape and the side of the muzzle offset — [`weapons-dat-sim.md`](weapons-dat-sim.md), [`../simulation/weapon-firing.md`](../simulation/weapon-firing.md#where-the-shot-comes-from--weaponmount_prepareshot-0040e788) |
| `+0x07` | byte | fire-chain number | the cockpit weapon row this mount owns: handed to the gauge factory as a `.GAU` weapon-slot index, so the panel prints it as `n+1` |
| `+0x10` | 3 x int16 | mount-point offset | X, Y, Z in the bone's space, where the weapon sits; the barrel length from the template is added on top for the muzzle |
| `+0x16` | int8 | link partner offset | signed; how far away in the mount array this hardpoint's LINK partner sits. Retail chassis pair mirrored left/right hardpoints with ±1 |
| `+0x17` | byte | fit slot | index into the mission's weapon-id and ammunition-type arrays; also names the mount's damage component, slot + 19 |

`+0x08`–`+0x0f` (four int16) and `+0x18` (int16) have no assigned role; see [Open](#open).

## Open

- **Open:** `+0x08`–`+0x0f` and `+0x18`.
