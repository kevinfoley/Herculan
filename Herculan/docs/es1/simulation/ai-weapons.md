# DBSIM.EXE — the HERC AI's weapons

**ES1's AI HERC aims and fires as ES2's does ([ES2's](../../retail/simulation/ai-weapons.md)): every combat state ends in `Ai_AimAndFire`, which turns the turret onto the target, picks a mount and fires it once the turret is on.** What a player would see differently:

- a structure is shot at its aim point, never at a component;
- the Cybrid side's aim scatter is 1000, 666, 333 or 0 by difficulty.

The functions are in [the HERC AI's object file](mech-ai.md#where-the-object-file-starts-and-ends), and the fields they read are [the HERC's](mech.md#the-fields) and [the AI's](mech-ai.md#the-fields). Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. Every function here removes its own arguments, so argument lists are in stack order, the machine first and the other arguments in the reverse of ES2's order ([calling conventions](../formats/compiler.md#calling-conventions)).

## Aiming and firing — `Ai_AimAndFire` (`00018894`)

**`Ai_AimAndFire` (`00018894`)**, (machine, target, aspect), the target 0 meaning `+0x15e`: a class-0 target goes to `Ai_AimAndFireAtMech`; any other is shot at its position plus the offset its slot `+0x34` gives. ES2's ([the tail](../../retail/simulation/ai-weapons.md#the-tail-every-combat-state-shares--ai_aimandfire-0041ea7c)) aims at a structure's first surviving component. `es2_xref.py` finds eight calls, and four dword occurrences of its padding entry `00018893`, at `00010eed`, `00012adb`, `00021e7b` and `0003317d` ([Open](#open)).

## Aiming at a machine — `Ai_AimAndFireAtMech` (`0001879c`)

**`Ai_AimAndFireAtMech` (`0001879c`)**, (machine, target, aspect): ES2's ([aiming at a machine](../../retail/simulation/ai-weapons.md#aiming-at-a-machine--ai_aimandfireatmech-0041e984)) on ES1's fields. In order:

1. **The arc** (`000187e2`): the bearing error, `Math_HeadingToward` from the machine's point `+0x20` to the target's less the heading `+0xa`, plus the type record's (`+0x188`) twist limit `+0x22`, against twice the limit, both cut to 16 bits and compared unsigned. At or above, `Mech_CenterTorsoTick`(machine, 0) and nothing more.
2. **The range**, `Math_DistanceBetweenPoints`, in 3D, between the two points.
3. **The point.** With the aim slot `+0x238` not negative and the range less 5000, compared unsigned, below 15000, so from 5000 to 19999, the target's slot `+0x5c`(target, point, aim slot) gives the component's position. Otherwise (`00018851`–`00018871`) the target's slot `+0x28` (`Mech_GetAimNodeTransform` for a HERC) answers the aim node's transform, and its translation, the transform's `+0x14`, goes through the target's frame: `Transform_ApplyToPoint` (`0005f58c`)(translation, `Context_GetTransform`(target + 6), point), the node's world position.
4. `Ai_FireAtPoint`(machine, target, aspect, point), called at its padding entry `000190ea`.

## The fire decision — `Ai_FireAtPoint` (`000190ec`)

**`Ai_FireAtPoint` (`000190ec`)**, (machine, target, aspect, point): ES2's ([the fire decision](../../retail/simulation/ai-weapons.md#the-fire-decision--ai_fireatpoint-0041f5a0)) on the latch `+0x242`, the selection skip `+0xaf` and the mount's slots `+0x30` (ready), `+0x64` (subtype) and `+0x2c` (fire). In order:

1. **The mount.** The range is `Math_DistanceBetweenPoints2D` from the point to the machine's `+0x20`. The latched mount stays while its ready slot answers non-zero and `WeaponMount_RangeAllows` (`0000ac14`)(mount, range) passes. Otherwise, with `+0xaf` set there is none and `+0xaf` is cleared; with it clear the mount is `Ai_ChooseWeapon`(machine, target, range, aspect)'s and the latch is cleared. With none, `Cockpit_TargetAnglesFromCameraBone`(machine, point) aims the turret and nothing fires.
2. **The lead** (`000191bb`–`000191e4`): with the projectile record's (the mount's `+0x20`) speed `+0xa` above 0, signed, and the target's speed not 0, the lead is (`int16`)((`int16`)(range ≫ 2) × (speed × 4) / projectile speed), the multiply and the signed divide in 32 bits, and `Math_OffsetPointByBearing`(lead, the target's heading `+0xa`, point) moves the point along the target's heading. The target's speed is its slot `+0x3c`, for a HERC `Mech_GetSpeed` (`00010e48`), Q10(2000, `+0x224`) ([small functions](mech.md#small-functions)). The quarter range is cut to 16 bits, so from a range of 131072 (786 m) it wraps.
3. **The scatter** (`000191f8`–`00019261`), for a group (`+0x3f`) whose side byte `+0x14` is not 0. The bound is `AiAimScatterByDifficulty` (`00091ce0`: 1000, 666, 333, 0) at script.dat header word 7 (`000bc4aa`), sign-extended and not range-checked, or at 3 when the word `MissionModeFlag` (`000bc4a0`) is not 0: `NEG`, `SBB` and `INC` at `00019204` make 1 of a zero word and 0 of any other. It is the index [`Damage_ScaleByDifficulty`](world.md#the-difficulty-scale--damage_scalebydifficulty-0001f82c) takes. With a bound not 0, three `Math_RandomBelow` (`00063b3c`)(`000b5bd4`, bound) draws, sign-extended, are added to the point's X, Y and Z, in that order. Each draw lies in [0, bound), so the scatter only raises the coordinates: the shot falls on the +X, +Y and +Z side of the point, up to 999 units (6 m) on each axis at difficulty 0, as in ES2 ([the scatter](../../retail/simulation/ai-weapons.md#the-fire-decision--ai_fireatpoint-0041f5a0)).
4. **The fire gate** (`0001927a`–`000192bb`). `Cockpit_TargetAnglesFromCameraBone`(machine, point) aims the turret and returns the residual, pitch at `+0` and yaw at `+4`. A mount whose subtype, slot `+0x64`(mount, 0), is 5, which is every mount but a missile launcher ([the subtypes](weapon-mount-classes.md#the-slots)), fires only when both residuals, sign-extended, plus 1000 compare unsigned below 2000, that is lie in [−1000, 1000). A missile launcher fires at once, however far the turret is off. ES2's gate is the same.
5. **The shot**: the fire slot `+0x2c`(mount, 0, machine), the free-shot flag 0; then the mount is latched when its template's (`+0x1c`) weapon id `+0x56` is 3, `ELF` ([the templates](../formats/weapons-dat-sim.md)), where ES2's latches 6 and 22.

**The AI fires a mount by its ready and fire slots alone, so an AI's ELF fires without spinning up, and an AI spends its rounds with unlimited ammunition on.** This function never calls a mount's trigger slot `+0x34`. An ELF spins up in its trigger, `ElfMount_TriggerHeld`, which sets the spin-up byte `+0x47` that its pool turn steps the flipbook under ([triggers](weapon-mount-classes.md#triggers), [charging](weapon-mount-classes.md#charging)), while its ready slot, `ElfMount_CanFire` (`0000b384`), tests the capacitor alone; so here an ELF fires as soon as it is charged. The free-shot flag 0 makes `WeaponMount_FireDispatch_Missile` (`0000af40`) and `GrenadeMount_FireDispatch` (`0000afd4`) take rounds off whatever `UnlimitedAmmoFlag` holds, where [`WeaponMounts_FireTrigger`](weapon-mounts.md#firing--weaponmounts_firetrigger-0000d2bc) passes the player 1 with it on: an AI launcher or ammunition gun runs dry as usual ([Open](#open)).

## Choosing a weapon — `Ai_ChooseWeapon` (`00018e50`)

**`Ai_ChooseWeapon` (`00018e50`)**, (machine, target, range, aspect): ES2's ([choosing a weapon](../../retail/simulation/ai-weapons.md#choosing-a-weapon--ai_chooseweapon-0041f358)) on ES1's fields:

1. **The floor**, `Math_MapRange` (`00065480`)(the fear `+0x240` sign-extended, 0, `0x400`, 150, −100), cut to an `int16`.
2. **The shield**, the target's slot `+0x38`(target, *f*), *f* 1 when the aspect, sign-extended, plus `0x4000` compares unsigned below `0x8000` (−`0x4000` to `0x3fff`) and 0 otherwise, ES2's boolean; and the jitter bound, 35 for a class-0 target (`+0x162`) and 30 for any other.
3. **The mounts**, those of the manager `+0x198` (its array at `+0`, its count `int16` `+4`) in order, by a signed 16-bit index. A null mount or one whose spent slot `+0x60` answers non-zero is passed over. For the rest it takes the subtype, slot `+0x64`(mount, 0), and `WeaponMount_RangeAllows`(mount, range); a subtype 0 in range sets the radar `+0x90` when it is clear and the word `+0x201` is 0. A mount of subtype 5 or 3, or whose lock word at the manager's `+6` + 2 × subtype is not 0, in range and ready (slot `+0x30`), is scored.
4. **The score**, from the projectile record `+0x20`: the damage is its armor damage `int16` `+6` against a shield of 0; against a shield, its shield damage `+4` when that is at most the shield, and otherwise the armor damage + 10000, in 32 bits. The score is Q10(damage, 100) − Q10(1000, the template's (`+0x1c`) cost `int16` `+0x34`) + *r*₂ × *r*₁, each Q10 the rounding `Math_Q10Multiply` (`0005e818`), whose first operand is 32-bit and second 16-bit ([the primitives](fixed-point-math.md#the-primitives)), so the damage, armor + 10000 included, is not cut and the cost is the 16-bit operand; *r*₁ and *r*₂ are two `Math_RandomBelow`(`000b5bd4`, jitter) draws, in that order, multiplied in 32 bits. A score above the best, the 32-bit score against the best sign-extended, becomes the best, cut to 16 bits, and its mount the choice (`00019069`–`00019081`).
5. **Running dry.** With no mount past the null and spent test and `+0x9f` clear, it sets `+0x9f`, runs `Action_Activate` (`0001cc5c`) on the action `+0x170` when there is one, and raises `MissionPollTimer_Count` (`000af79d`) to 1000 when it is below, a signed 16-bit compare (`000190cf`), as ES2's.

It returns the choice, or 0. Its body reads no `PlayerMech`: it has no counterpart of the group compare ES2's makes and discards.

## Aiming the torso — `Cockpit_TargetAnglesFromCameraBone` (`00018b78`)

**`Cockpit_TargetAnglesFromCameraBone` (`00018b78`)**, (machine, point): ES2's aim primitive ([aiming at a point](../../retail/simulation/torso-aim.md#aiming-at-a-point)) without the damping beyond 50000 and without the snap target, which ES1's turret ticks lack ([the turret](mech.md#the-turret)). In order:

1. it sets `TargetAngles_CalledOnce` (`000af710`) on its first call;
2. the node: slot `+0x24` of the shape (the shape instance `+0x2e`'s `+4`) with the type record's `int16` `+0xc` gives the part, and the node's transform is the 32-byte entry at the instance's `+0x16` + `0x20` × the part's `int16` `+4`, read from address 0 when the shape has no such part;
3. the point goes through the machine's transform (`Context_GetTransform` of `+6`) inverted, then through the node's inverted, each inverse the rotation transposed with the negated translation rotated by it, and the record's `+0x66` is subtracted from its Z;
4. `Math_EulerToward`((0, 0, 0), point) ([the helpers](fixed-point-math.md#helpers-that-match)), from the node's origin to the point, into `TargetAngles_Residual` (`000af714`, pitch at `+0`, yaw at `+4`);
5. the twist axis: *c* = clamp(Q10(250, yaw), ±`0x100`), then Q8(−*c*, |−*c*|), the value negated before the rounding multiply, |−`0x8000`| taken as `0x7fff`; the pitch axis Q8(*c*, |*c*|) from the pitch the same way, unnegated;
6. `Mech_TorsoTwistTick`(machine, twist axis) and `Mech_TorsoPitchTick`(machine, `Math_FastMagnitude3D` of the point of step 3, pitch axis); it returns `000af714`.

`es2_xref.py` finds three calls: `Ai_FireAtPoint` twice and `Sim_PollPlayerInput` (`0005a0c0`, [the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0)).

## Centring the torso — `Mech_CenterTorsoTick` (`000186f8`)

**`Mech_CenterTorsoTick` (`000186f8`)**, (machine, range): ES2's ([centring](../../retail/simulation/torso-aim.md#centring--backspace)) without the snap: −clamp(Q10(250, angle), ±`0x100`) on the twist `+0x22e` and the pitch `+0x230`. `es2_xref.py` finds five calls: `Ai_NavigationStep`, `Mech_BehaviourSkirtThink`, `Mech_BehaviourGuardThink`, `Ai_AimAndFireAtMech` and `Sim_PollPlayerInput` (`0005a0c0`, [the player's controls](main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Mech_CenterTorsoTick`, `Ai_AimAndFireAtMech`, `Ai_AimAndFire` | `000186f8`, `0001879c`, `00018894` | `0041e8d4`, `0041e984`, `0041ea7c` |
| `Cockpit_TargetAnglesFromCameraBone` | `00018b78` | `0041ef14` |
| `Ai_ChooseWeapon`, `Ai_FireAtPoint` | `00018e50`, `000190ec` | `0041f358`, `0041f5a0` |

Callee of another file read for them: `Math_MapRange` (`00065480`), ES2's `0047de3c`, which keeps the C convention and ES2's argument order.

## Globals

| | Address |
|---|---|
| `AiAimScatterByDifficulty`, 4 `int16` | `00091ce0` |
| `TargetAngles_CalledOnce`, `TargetAngles_Residual` | `000af710`, `000af714` |

## Open

- **Open:** what the four dword occurrences of `Ai_AimAndFire`'s padding entry are: they may be instruction bytes ([aiming and firing](#aiming-and-firing--ai_aimandfire-00018894)).
- **Open:** whether code outside this file spins up an AI machine's ELF or spares its rounds under unlimited ammunition; `Ai_FireAtPoint` does neither ([the fire decision](#the-fire-decision--ai_fireatpoint-000190ec)). A retail check: an enemy HERC armed with an ELF fires its first burst with no spin-up of the weapon's model before it; and with unlimited ammunition set in script.dat (header word 5 at 1, word 2 at 0), an enemy missile launcher's volleys stop once its rack is spent.
