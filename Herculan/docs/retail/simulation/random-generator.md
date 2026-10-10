# DBSIM.EXE pseudo-random generator — `Math_RandomNext` (`00492dd4`)

Reverse-engineered from `DBSIM.EXE` disassembly. All addresses are DBSIM.EXE virtual addresses.

A general math-library utility rather than a simulation subsystem: every randomised decision in the game comes out of here, from the load-time terrain material pass to the explosion's per-component roll. Its seeding, which makes every run draw the same stream, gets as much space below as the algorithm.

## The algorithm

An additive lagged Fibonacci generator over a 56-entry table of `short`s with two rotating byte cursors. One step is `table[i] += table[j]`, returning the new `table[i]`, then both cursors advance and wrap at 56 — the original's `== '8'` test. `i` is the state block's `+0x71` and `j` its `+0x70`.

`Math_RandomBelow` (`00492e18`) wraps it as `(next & 0x7fff) % bound` — the mask **drops the sign bit rather than taking an absolute value**, so a bounded draw is over the low fifteen bits, not the full sixteen, and is not quite uniform for a bound that does not divide `0x8000`.

Callers pass the state block's address and mask the result to the width they need: `& 0xfff` for the terrain material roll and for the explosion's per-component roll, `& 0x7f` for a beam node's jitter. The simulation's shared block is `0x4d261d`; sounds, messages and the cockpit's own effects draw on a [second generator](#the-presentation-generator) instead.

## Seeding — `Math_RandomSeed` (`00492d7c`)

**Seeding takes no input.** The state lives in BSS. `Math_RandomSeed` sets the two cursors to the literals `0x37` and `0x18`, `memmove`s 112 bytes from the static table at `004a6958`, and then calls `Math_RandomSeedNoOp` (`00492e3c`), which is `push ebp; pop ebp; ret`. It reads no clock and takes no value from its caller beyond the block's address.

`es2_xref.py` finds three calls to it. Two are in `Main_StaticInit` (`0045cbcd` and `0045cbd8`), one per block. The third is in `Math_RandomSeedAndSkip` (`00492da8`), a wrapper nothing in the image references: it seeds the block it is given, calls a second empty function (`Math_RandomSeedAndSkipNoOp`, `00492e41`), then discards `param_2` draws.

So the generator contributes no run-to-run variation: **DBSIM draws the same stream on every run**. The known wall-clock path into simulation state is `SimTickDelta`, whose measured 40 ms comes back as 41 or 42 under load and rescales every rate that tick (see [`dbsim-physics-notes.md`](dbsim-physics-notes.md)).

## The simulation's draws

Sixty-two `PUSH 0x4d261d` in the image besides the seeding one hand the simulation's block to a draw. With the presentation block's sixteen they are the 78 calls `es2_xref.py` finds to `Math_RandomNext` and `Math_RandomBelow` from outside the generator; `Math_RandomBelow`'s own call and `Math_RandomSeedAndSkip`'s make up the rest of `Math_RandomNext`'s fifty.

**The zone's roll comes first.** DBSIM plays one mission per run: `Sim_Run` (`0045f144`) calls `Sim_InitMissionSession` once. That loads the zone inside `DBSim_LoadScriptDat` (`Terrain_LoadZone` at `004252f7`) before `DBSim_SpawnMissionObjects` (`0046171f`) builds the first object, and every caller `es2_xref.py` finds for the functions below is a pass of the tick, a method of a live object, the spawn or the zone loader. So the zone's material roll ([`../rendering/terrain-texturing.md`](../rendering/terrain-texturing.md#retail-numbers)) takes the head of the stream, and a zone scatters the same way on every load ([Open](#open)). The spawn's `Mech_ApplyStartingCondition` draws next.

**Everything else draws inside `Sim_MainTick` (`0045f464`)**, whose passes run in a fixed order:

1. `Input_BuildPlayerDevice`.
2. Unless `004d2576` is set: the effect pools — explosions, smoke balls, smoke, fire, debris, meteors — then the projectiles (vtable `+0x14`), then every structure whose group has entered the mission (vtable `+0x18`), each pool walked last to first with `Pool_Prev`.
3. The same gate: the mission's groups in order, `Group_DeploymentCheck` for one still to arrive and otherwise `Group_OrderTick`, which runs `Mech_AiTick` (`00411cec`) on each member in turn: reassess, move, then think.
4. `Sim_PollPlayerInput` — the player's commands, control laws and fire.
5. Unless `004d2576`: `ActionTimers_Tick` and `Actions_EvaluateTriggers`; then `ViewChain_Apply`; then, the same gate, `Sim_DetectionTick` and `Mech_PerTickSystemsUpdate` for every deployed machine still standing, last to first.
6. `Mission_PollStatus`, and on status 3 `Group_ApplyOutnumberedDamage` on the player's group.

The 62 sites, by function, with the callers `es2_xref.py` finds:

| Functions (sites) | Reached from |
|---|---|
| `TerrainZone_PopulateFromBitmap` (1), `TerrainZone_LoadHeightmap` (1) | the zone load; the second is the ASCII fallback no retail zone takes |
| `Mech_ApplyStartingCondition` (2) | `DBSim_SpawnMissionObjects` |
| `Base_ArmedThinkTick` (2), `Base_TransportThinkTick` (1), `Base_DeathSequenceTick` (3) | structure vtable `+0x18`, the structure pass (and `GroundVehicle_ThinkTick` for the first); the third from `Base_ThinkTick` and the other two |
| `Group_DeploymentCheck` (4), `Meteor_Construct` (2) | the group pass; the second from the first |
| `Mech_MovementTick` (1), `Mech_BehaviourFleeThink` (1), `Mech_BehaviourRamThink` (2) | pointers stored in the behaviour state tables, whose move and think slots `Mech_AiTick` dispatches |
| `Mech_CollisionTest` (6) | `Mech_MovementTick` and `Mech_BehaviourRamTick` |
| `Mech_AiSelectAimComponent` (2), `Mech_CompareCombatRating` (2) | `Mech_AiCombatReassess`, `Mech_BehaviourGuardThink`, `Mech_AiOnTakingFire`; the second from mech vtable `+0x4c` |
| `Ai_FireAtPoint` (3), `Ai_ChooseWeapon` (2) | `Ai_AimAndFire` and `Ai_AimAndFireAtMech`; the second from the first |
| `Sim_DetectionTick` (1), `Detection_LineOfSight` (1) | the detection pass; the second from its sweep and contact decay and from `Mech_PerTickSystemsUpdate` |
| `Mech_PerTickSystemsUpdate` (1) | the per-machine pass |
| `Bullet_Fire` (2), `BeamTracer_Ctor` (3), `Sim_RaycastObjectList` (1) | a shot: structure fire, the mounts' fire dispatch and `Flyer_AttackRun`; `Bullet_FireBurst`; the projectile pass (`Bullet_TickUpdate`, `Rocket_TickUpdate`), `Bullet_FireBurst` and `Razor_MovementTick` |
| `Mech_DirectFireHitTest` (1), `Base_DirectFireHitTest` (2), `Flyer_DirectFireHitTest` (1) | vtable `+0x20` |
| `Mech_ApplyDirectFireDamage` (2), `Mech_ApplyExplosiveDamage` (1), `Base_ApplyExplosiveDamage` (1), `Base_ApplyDamage` (1), `WeaponMount_ConditionChanged` (1) | `Mech_DirectFireHitTest` and `Razor_MovementTick`; vtable `+0x70`; `+0x70`; `+0x74`; mount vtable `+0x68` |
| `Mech_SpreadImpactDamage` (2), `Component_SpillIntoDependents` (1) | `Mech_LocomotionTick`, `Mech_ApplyStartingCondition`, `Mech_ApplyOutnumberedDamage`; the component cascade |
| `Debris_Construct`, `Debris_Launch`, `Debris_ThrowGroup`, `Debris_ThrowAtBearing`, `Debris_ThrowRandomBearing` (1 each) | `Component_DestroyAndCascade`, `WeaponMount_Destroy`, `Debris_Throw`, `Debris_ThrowGroupAt` |

A draw reached through a shot, a hit test, damage or debris falls in whichever pass dealt the blow — the projectile pass for a travelling round, the structure pass for a turret's fire, the group pass for a machine's, the player's input poll for the player's. Each pass runs once a tick, so under a tape the order of the draws is set by the frames and the `SimTickDelta` the tape carries.

## The presentation generator

`0x4d268f` is the block directly after the simulation's — `0x4d261d + 0x72`, one 56-entry table and its two cursors — and a generator in its own right. The static initialiser `Main_StaticInit` (`0045cad8`) seeds the two back to back through `Math_RandomSeed` (`00492d7c`, called at `0045cbc8`–`0045cbd8`), so both start in the same state and diverge only through their own draws. Every consumer is presentation — sounds, messages, portraits, smoke, cockpit shakes and the sensor dropout — so none of them moves a simulation roll, and a consumer that never runs costs the simulation nothing either.

Its draw sites are the sixteen `PUSH 0x4d268f` in the image besides that seeding one:

| Site | Function | Draw |
|---|---|---|
| `004080ca` | `Explosion_Construct` | `Math_RandomBelow(0x32)`, discarded — the sound it then plays is the type record's `+0x24` plus 10 |
| `00409298` | `Smoke_Construct` (`0040923c`) | `next & 3`, the first of the four shape variants of smoke type `+0x28` that the emitter cycles through ([Open](#open)) |
| `0042f86f` | `TexPoly_RandomPointOnCircle` (`0042f860`), `TexPoly` vtable `+0x24` | `next`, a random angle; the point that far round a circle of the given radius ([Open](#open)) |
| `0042f9c7` | `TexPoly_SetupRings` (`0042f970`), `TexPoly` vtable `+0x20` | `next`, one random angle per ring ([Open](#open)) |
| `0043404e`, `004340cc` | `Cockpit_StartHitShake`, `Cockpit_HitShakeTick` | `(next & 0xffff) % 10`, the palette flash interval |
| `004340ea` | `Cockpit_HitShakeTick` | `(next & 0xffff) % 5`, the shake step |
| `00435cb5` | `PilotMessagePort_Post` (`00435c48`), the squad port's post | `(next & 0xffff) % variants`, drawn only for two or more |
| `00436a67` | `MessagePort_PickVariant` | the same |
| `00438d7f` | `PanelGauge_RollDuration` (`00438d6c`) | `(next & 0xffff) % (hi - lo) + lo`, not drawn when `hi == lo` — the [sensor dropout](cockpit-hud-widgets.md#sensor-dropout)'s spell lengths |
| `0044b138` | `HddGauge_PaintPilotFrame` | `next % 3`, discarded — [`heads-down-display.md`](heads-down-display.md#the-three-paints) |
| `0044b381` | `HddGauge_PaintScream` | `Math_RandomBelow(0x14)` |
| `0045db2f` | `LiftStart_Rise` (`0045d840`) | `(next & 0xffff) % 5`, the lift's closing shake step, once a frame for `0x1e` coarse ticks — [`mission-deployment.md`](mission-deployment.md#the-lift-start) |
| `0045dcfb` | `Sim_DeathFlash` (`0045dc34`) | `Math_RandomBelow(10)`, the same kind of shake step — [`../rendering/cockpit-canopy-palette.md`](../rendering/cockpit-canopy-palette.md#palette-module) |
| `00462753`, `004627ff` | `Sound_Play`, `Sound_PlayAt` | `Math_RandomBelow` over the sound's variation count |

## Open

- **Deferred:** the callers of the virtual slots and table entries in [the draw-site table](#the-simulations-draws) — the hit tests' `+0x20`, `Mech_CompareCombatRating`'s `+0x4c`, the damage slots `+0x68`, `+0x70` and `+0x74`, and the behaviour state tables — are not enumerated, so a draw reached through one of them from outside `Sim_MainTick`, from a render path say, is not ruled out.
- **Open:** what constructs `SMOKE`. `Sim_MainTick` ticks a list of them through `Smoke_Tick` (`004092dc`), each releasing a `SMOKE_BALL` every 200 ticks while its count lasts, but `es2_xref.py` finds no branch or pointer reaching `Smoke_Construct` or landing anywhere from `00409200` to `00409240`.
- **Deferred:** whether `TexPoly` is ever built. Its constructor, `TexPoly_Ctor` (`0042f700`), has no reference `es2_xref.py` finds, the class name appears only in its own RTTI record — not in the persistence name table beside `TSTexture4Poly` — and no retail `.DTS` names it.
- **Deferred:** the zone roll's place at the head of the stream rests on the callers `es2_xref.py` finds ([The simulation's draws](#the-simulations-draws)). A retail screenshot of a zone's ground beside the scatter the stream's first draws give would confirm it.
