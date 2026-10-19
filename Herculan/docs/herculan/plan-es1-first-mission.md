# Plan — ES1's first playable mission

**The first playable slice of *Metaltech: Earthsiege*: a mission loaded in the ES1 zone viewer with the player's HERC under the player's control — walking, aiming, targeting and firing from ES1's cockpit, on ES1's tick — against enemy HERCs that take damage, and then run ES1's AI.** The player's mechanics come first and the AI last, so each player mechanic can be checked against retail on its own before anything else moves. The work is port first: the ES1 docs already hold the HERC ([`mech.md`](../es1/simulation/mech.md)), the frame loop and tick ([`main-loop.md`](../es1/simulation/main-loop.md)), the input ([`input.md`](../es1/simulation/input.md)), the cockpit ([`cockpit-views.md`](../es1/simulation/cockpit-views.md), [`cockpit-panel.md`](../es1/simulation/cockpit-panel.md)) and the AI ([`mech-ai.md`](../es1/simulation/mech-ai.md)), and reverse-engineering is limited to the gaps a port step meets, read as a targeted batch before that step. This plan carries out [phase 2](plan-es1-support.md#phase-2--the-engine) of the ES1 plan for the simulation.

## Rules

- Behaviour is ported from what the binary shows. Nothing is invented to reach a playable build sooner; where the slice lacks a retail feature, the code does nothing in its place, tagged `// PLACEHOLDER:` and listed under [what the slice leaves out](#what-the-slice-leaves-out) until its step lands.
- No shortcut that a later step has to undo: a structure retail builds (the weapon mounts, the view's window) is built as retail builds it, even when the step that uses it fully comes later.
- The ES1 docs describe ES1; the port's types and departures go in doc comments on the C#, citing the doc section.

## The slice

| In the slice | Source |
|---|---|
| The shell's single-mission load: `data\script.dat`, `data\mission.var` and `data\player.mec` built from the `.MSN` as it writes them | [`msn-mission-file.md`](../es1/formats/msn-mission-file.md#loading-a-mission), [`script-dat.md`](../es1/formats/script-dat.md#writing), [`career-log-and-player-mec.md`](../es1/formats/career-log-and-player-mec.md#the-player-file) |
| HERCs constructed from their chassis files, fitted with their weapons and placed by their groups; the player's chosen | [`mech.md#construction`](../es1/simulation/mech.md#construction), [`weapon-mounts.md`](../es1/simulation/weapon-mounts.md), [`weapon-mount-classes.md`](../es1/simulation/weapon-mount-classes.md), [`script-dat.md#the-second-pass`](../es1/formats/script-dat.md#the-second-pass) |
| The forward view: the `.VUE` record's window and projection, its clip regions, the canopy art and the eye-point camera | [`cockpit-views.md`](../es1/simulation/cockpit-views.md), [`hd-ed-clip-regions.md`](../es1/formats/hd-ed-clip-regions.md), [`external-views.md`](../es1/simulation/external-views.md) |
| The tick: length, order, one tick a frame | [`main-loop.md`](../es1/simulation/main-loop.md#the-tick--sim_maintick-00059700), [`fixed-point-math.md`](../es1/simulation/fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40) |
| Keyboard and joystick input: throttle, turning, the turret, the stick mode | [`input.md`](../es1/simulation/input.md), [`main-loop.md`](../es1/simulation/main-loop.md#the-players-controls--sim_pollplayerinput-0005a0c0) |
| Movement: throttle, locomotion, gait and leg animation, root motion, collision with the zone's shapes, ground placement, shadows | [`mech-locomotion.md`](../es1/simulation/mech-locomotion.md), ES2's [`mech-locomotion.md`](../retail/simulation/mech-locomotion.md) |
| The cockpit: its keys, the target selection, the gunsight and its drag, the throttle gauge, the view changes, the instruments | [`cockpit-panel.md`](../es1/simulation/cockpit-panel.md), [`cockpit-gunsight-hud.md`](../es1/simulation/cockpit-gunsight-hud.md), [`mech.md#the-cockpit`](../es1/simulation/mech.md#the-cockpit) |
| Weapons and damage: firing, rounds, hit tests, component damage, collisions, impact effects | [`weapon-mounts.md`](../es1/simulation/weapon-mounts.md), [`mech.md#hits-and-damage`](../es1/simulation/mech.md#hits-and-damage), [`world.md`](../es1/simulation/world.md) |
| The AI: groups, the state table, steering, formation and routes, the sensor model and target selection, and the actions and triggers that gate a group's tick | [`mech-ai.md`](../es1/simulation/mech-ai.md), [`mission-groups.md`](../es1/simulation/mission-groups.md), [`sensor-model.md`](../es1/simulation/sensor-model.md), [`sim-object-base.md`](../es1/simulation/sim-object-base.md) |

### What the slice leaves out

Until its step lands, each of these does nothing, tagged `// PLACEHOLDER:` in code:

- **The cockpit's instruments**, until step 6. The canopy art is drawn from step 3; the widgets, gauges, MFDs and `.TTM` scripts are not, and the cockpit's keys, the gunsight's target selection and the throttle gauge's exchange, which `Player_PerFrameCockpitUpdate` runs ([`mech.md#the-cockpit`](../es1/simulation/mech.md#the-cockpit)), do nothing: the player has no target, so the turret's tracking does nothing either.
- **The views and the mouse**, until step 6: `ViewChain_Apply`, `CockpitView_PollViewDevice` and the view, status-panel and preferences commands take their keys and do nothing; the pointer reads 0, the frame's mouse events (`000444d0`) are not read, and the gunsight drag does not arm. The mouse's buttons are read, so the two-button turret snap works.
- **The player's think**, until step 6: `Mech_BehaviourPlayerThink` posts the cockpit's messages and raises the flags the mission's end reads; it answers 0.
- **Firing and damage**, until step 7. The mounts are built and fitted; no machine fires, no damage is applied, and the flags the status and the AI read (`+0x93`, `+0x9e`, `+0x9f`) stay clear. Collision damage and the ram blast do nothing; a collision still refuses the move. The systems tick, `Mech_PerTickSystemsUpdate` (energy, shields, countdowns, ECM and lock), does not run.
- **The AI**, until step 8. Every machine is built in `deciding`, whose reassess installs `player` for the player's machine and nothing for the others, so they stand where they were placed; their shadows are placed by that reassess, where retail's state's move would place them. Obstacle avoidance, the trigger areas and the sensor model do nothing.
- **Sound**, no step yet: the footfalls, the fall, the refused move's thump, the turret's servo, the frame's engine hum and the adaptive music (`Sim_FrameSoundTick`, [the frame's sounds](../es1/simulation/main-loop.md#the-frames-sounds--sim_framesoundtick-00058574)) play nothing; their latches and state run ([Open](#open)).
- **Tapes**, no step yet: `-p`, `-r` and `-D` are not taken, so the input build's recording and playback and `DemoMode` do nothing.
- **The joystick**, until step 5.
- **Flyers and bases as simulation objects.** Blocks 7 and 8 are not spawned (their constructors `0001ac58` and `00003cbc` are unread); the zone's placed shapes still draw.
- **The mission's end.** The status panel that ends the mission (`00046efc`) is unread; the mission runs until the window closes.
- **An unread edge case of step 3b**, no step yet: a cockpit scheme above 7, whose `0x40` word retail reads past its table ([the color scheme](../es1/simulation/cockpit-views.md#the-color-scheme)), keeps the second range's colour at `0x40`.
- **The shell's after-mission files and a career's load**: a single mission's load writes no `car\sm_play.mec` or `car\sm_play.gam`, which only the shell reads back, and a career's mode 1 waits for the shell.

## Decisions

Made with the user:

1. **The animation classes are the core's.** `ShapeAnimation`, `AnimationThread`, `ShapeInstance` and `SkeletonPose` are in `Herculan.Engine.Anim`, since the 3Space classes belong to the core ([how the two games share code](planning.md#how-the-two-games-share-code)). ES1's are ES2's but for one rounding: a seek scales the position with ES1's rounding Q14 multiply ([differences](../es1/rendering/dts-node-posing.md#differences-from-es2)). The caller of a seek names its arithmetic (`SeekRounding`), so neither game's behaviour changes and the core tests no game.
2. **The tick is paced as retail paces it.** ES1 runs one tick a frame, its length the time the frame took (`SimTickDelta`, Q8 with 1.0 = 100 ms, held to 25–176 ms), and a frame waits at least two PC timer ticks ([the tick length](../es1/simulation/fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)). The timer runs at 120 interrupts a second ([the PC timer](../es1/simulation/timer.md#the-rate)), so retail's minimum frame time is two of them, 16.7 ms, which the tick's floor counts as 25 ms: a machine that draws a frame that fast plays at 1.5 times real time, where the hardware of its day took 25 ms or more a frame and played in real time. An ES1 pacer runs a tick once **three** interrupts' time, 25 ms, has passed, with the elapsed time counted in 120 Hz interrupts as the tick's length under retail's clamp and arithmetic, and draws every host frame from the latest tick, as retail draws no frame between ticks. The three-interrupt wait is this engine's departure from retail's two, the user's choice, so the game plays in real time as it did on period hardware; it is tagged in code and listed in `KNOWN_ISSUES.md` when step 4 lands.
3. **The view is view 0 as retail builds it.** The world is drawn in the window and with the projection of the chassis's `.VUE` record, clipped by its clip regions, under the canopy art from `db0\<chassis>` ([the view manager](../es1/simulation/cockpit-views.md#the-view-manager), [the canopy art](../es1/simulation/cockpit-views.md#loading-and-freeing-the-canopy-art)), from the eye-point camera `Cam_AttachTo` attaches. The instruments come in step 6. The viewer's free camera stays on a key, as an engine aid with no retail counterpart.
4. **The sensor model is ported** from [`sensor-model.md`](../es1/simulation/sensor-model.md): the AI's target choice reads the contacts and radar flags `Sim_DetectionTick` sets.
5. **The player's mechanics come before the AI**: movement, the joystick, the cockpit, then weapons and damage, so each is checked against retail on its own; the AI is the last step.
6. **A named mission loads as a single mission, and the simulator runs on the install's `data\` files.** The shell's mode-0 load writes `data\script.dat`, `data\mission.var` and `data\player.mec` into the install, and the simulator reads whatever those files hold, as `DBSIM.EXE` does, so it also runs on a set retail's shell wrote. A career's mode 1 waits for the shell. `--theater` and `--world` override header words 0, 8 and 9, as engine aids.
7. **The 320×200 screen is shown at 4:3**, as a CRT showed it, with the `.VUE` projection in the 320×200 screen's pixels.
8. **A textured face is filled as retail fills it**, column by column with the texture interpolated in screen space ([bitmap fills](../es1/rendering/polygon-shading.md#bitmap-fills-brush-modes-5-and-7)), the walk run in the fragment shader as the clouds' is. The perspective-correct mapping the core's renderer already does stays behind a code path for a tweak ([Open](#open)).

## Steps

Each step is one subagent brief, one review (diff, both builds, the tests), one in-app check by the user against retail where retail can be seen, and one commit. An RE step is an [ES1 batch](handoff-es1-data-formats.md#how-the-batches-were-run) limited to the functions named, written up in the ES1 docs before the port reads them.

**Step 0 — the animation seam** (decision 1). Move the four classes; the ES2 tests and `Es1BoundaryTests` pass unchanged, and ES2's game is unchanged in the app.

**Step 1 — RE: the HERC's body.** What the movement and the draw rest on and the docs only name:

- `Mech_LocomotionTick` (`00012038`) and its callees: ES1's control law against ES2's ([`mech-locomotion.md#control-law-mech_locomotiontick`](../retail/simulation/mech-locomotion.md#control-law-mech_locomotiontick)), the reverse top speed, the reactor's speed cut, and which function reads chassis offset 44, the walk-to-run threshold ES2's gait reads ([`chassis-dat.md#open`](../es1/formats/chassis-dat.md#open));
- the time `Mech_IntegrateMotion` (`00014148`) passes the root-motion step, which sets ES1's walking speed in world units a second;
- the footfall half of `Mech_PlaceLegsOnGround` (`000144dc`), with chassis offsets 150–156 and 170–174;
- the throttle's clamp at each `ThrottleLeverMode` value, 1 and −1;
- the object base's helpers the HERC calls: root motion `0000215c`, the transform save and restore `00002184`/`00002248`, adding a thread `00001f48`, the base constructor `00002ac8`, and the instance and thread set-up `00001da0`–`0000205c` ([`dts-node-posing.md#open`](../es1/rendering/dts-node-posing.md#open));
- the loadout's callees `Mech_ConfigureLoadout` makes that the docs only name (`000096e8`, `00009ad4`, `000099e4`, `000092e4`, `00009b28`);
- the draw, `Mech_Draw` (`00012ac4`) through `00002654` and `00002b30` and the `CONNECT_INFO` records, which put the weapon shapes on the HERC.

**Step 2 — RE: the mission start.** Who fills the slot-to-pool maps `000af818`, `000af914` and `000af97c`; how a stored heading decodes (`script-dat.md`'s Open: 45° and 225° store alike); the code that programs the PC timer's divisor, the word `+4` of the structure `000bdd0c` points to ([the tick length](../es1/simulation/fixed-point-math.md#open)), which gives the timer's rate; and, in place, three questions another doc already answers: `mission-groups.md`'s reader of the group's `+0x20`/`+0x0c` (`Mech_AttachToGroup`), `mission-status.md`'s writer of `+0x99`/`+0x9a` (`Mech_BehaviourPlayerThink`), and `mech.md`'s writer of `ThrottleLeverMode` (the Ctrl+1 command in `main-loop.md`).

**Step 2b — RE: the single-mission load.** What the shell's mode-0 load hands the simulator and the docs leave Open: what sets the mode `000aad56` and the words `000aacbc`–`000aacc4` that become header words 5–9 for a single mission; the shell's generator at `0009aec0`, its seed and the order of the load's draws; and the mode-0 squad build, from `car\sm_init.dat` through `car\sm_play.mec` to `data\player.mec` (`MsnGen_LoadMission`'s step 8 and `PlayerMec_Write`'s mode-0 path), so that a port writes the three `data\` files as retail does (decision 6).

**Step 3 — port: the mission's machines and the forward view.** In `HercWorks.Core`, models of ES1's `script.dat` blocks and `data\player.mec`, and the shell's mode-0 load that writes them and `data\mission.var` from an `Es1MissionFile` as `MsnGen_LoadMission` (`ES1GO 00035873`) and `WriteScriptDatFile` (`ES1GO 0003445b`) do; in `Herculan.Es1`, the simulator's read of the three files, the simulation world (object pool, groups, the player's machine), the HERC constructed from its chassis file, its loadout configured (`Mech_ConfigureLoadout`, the mount manager and its mounts) and placed by `Mech_AttachToGroup`, drawn posed at rest with its weapon shapes and shadows; and view 0 (decision 3). The user's check: the HERCs stand where retail's do at a mission's start, by landmarks, and the forward view's window, canopy and field of view match retail's.

**Step 3b — RE, then port: what step 3 stopped at.** An ES1DBSIM batch on `0000a308` (a weapon model moved to its mount point), `00057c08` (the detail scale and bias `Shape_DrawAtDetailLevel` reads) and the crude roots' remap handlers `00019aef`–`00019b93`, the `dpl\cockpit` entries `CockpitViewManager_LoadViews` installs, and the order of `+0x9d` against `Mech_ConfigureLoadout` in `DBSim_SpawnMissionObjects`; and in `ES1GO`, the two places retail's `SM_SCR01` set departs from the documented load ([`msn-mission-file.md#open`](../es1/formats/msn-mission-file.md#open)). Then the port: the weapon models on their hardpoints, the detail levels, the cockpit's colours, and the column walk (decision 8) with the perspective-correct code path. The user's check: the HERCs' weapons, their look at a distance, the canopy's colours and the textured faces against retail's.

**Step 4 — port: the tick and the player's movement.** The pacer (decision 2), `Sim_MainTick`'s order with the slice's placeholders, ES1's input build from the keyboard, `Sim_PollPlayerInput`, the throttle, `Mech_LocomotionTick`, the gait and leg animation, root motion, `Mech_CollisionTest` against the zone's shapes and other machines, ground placement, the turret ticks and torso centring. ES1's rounding fixed-point primitives join the core's `SimMath`. The user's check: walking and running pace, the legs' animation, turning and the turret, against the same chassis in retail.

**Step 5 — the joystick.** RE of the live input block's source (`0004c2d0`), the axis scale and the dead zone; then the six configurations and the stick mode ([`input.md`](../es1/simulation/input.md#the-joystick-configurations)).

**Step 6 — the cockpit.** RE of what the panel's port meets unread: a body repair of `CockpitWidgets_HandleCommand`'s case bodies and the callees and instrument constructors [`cockpit-panel.md`'s Open](../es1/simulation/cockpit-panel.md#open) lists, and the weapon-row file from `00037810` ([`cockpit-weapon-gauges.md#open`](../es1/simulation/cockpit-weapon-gauges.md#open)). Then the port: the cockpit object and its drawing, the keys, the view changes and glances, the target selection, the gunsight and its drag, the throttle gauge's exchange, `Player_PerFrameCockpitUpdate`, and the instruments with their `.TTM` scripts. The user's check: each key and instrument against retail's.

**Step 7 — weapons and damage.** RE of `damage.cpp` to `gunlist.cpp`, `collide.cpp`, the round modules `rocket.cpp` and `bullet.cpp`, and the impact effects `explo.cpp` and `debris.cpp`, limited to what firing, a hit and its effect run; then the port: the player's trigger, the mounts' firing, the rounds, `Mech_DirectFireHitTest`, blasts, `Mech_ComponentDamageWrite` and its cascade, collision damage, and the effects. The user's check: the player's weapons against standing enemy HERCs, their refire, damage and destruction against retail's.

**Step 8 — the AI.** The HERC state table's AI states and `Mech_AiSelectBehaviour`'s AI arm (the table, `Mech_AiTick` and `Group_TickMembers` run from step 4), the steering, formation and routes, the actions and triggers that gate a group's tick, the sensor model and `Ai_SelectTarget`, and the AI's aim and fire. States that need what the slice leaves out (attacking a flyer) keep their transitions and do nothing, tagged. The skirting's structure probe, `Structure_RaycastShapes` (`000038b8`), answers 0 while no base is spawned ([the structures' tests](../es1/simulation/terrain.md#the-structures-tests)). The user's check: an enemy group finds the player, closes, flanks and fights as retail's does.

After step 8, by the module rows of [step 0.7](plan-es1-support.md#phase-0--tooling-and-measurement), each module taken to [done](plan-es1-support.md#when-a-module-is-done) before the next:

1. Flyers: `mechsys.cpp` to `flyersys.cpp` and `flyersys.cpp`.
2. Effects and structures: `explo.cpp`, `debris.cpp`, `base.cpp`.
3. The mission's end: the status panel `00046efc` and what it closes.
4. The rest of the cockpit: the weapon-row file from `00037810` (the pod row, console buttons, select gadgets and slider from `000380fc`, [`cockpit-weapon-gauges.md#open`](../es1/simulation/cockpit-weapon-gauges.md#open)), the files from `00038f0c` and `000397f0`, the display's run at `00039bb4`, `phdd.cpp`, `alert.cpp`, `cal_alrt.cpp`.
5. `vcr.cpp`, `memory.cpp` and the rest of the library region ([`handoff-es1-library-region.md`](handoff-es1-library-region.md)); then the shell.

## Retail checks

Each is cheap to see and settles something a step rests on:

- **The PC timer's rate**: breakpoints on `Pit_SetDivisor` (`0006d18c`) and `Time_InitSimClock` (`00063e64`) under DOSBox-X confirm the divisor 9,939 and the 120 interrupts a second step 2 read from the code ([the PC timer](../es1/simulation/timer.md#open)).
- **Walking speed**: the time a HERC of one chassis takes at full throttle between two of a zone's placed shapes, against the port after step 4.
- **Starting positions**: the player's and an enemy group's position at a mission's start, against the port after step 3, the port run on the three `data\` files retail's shell wrote for that start.

## Open

- **Open:** every placeholder [the slice leaves out](#what-the-slice-leaves-out), each an **Unported:** item in `ROADMAP.md` once its step lands.
- **Open:** a step for ES1's sound, which the slice and the order after step 8 lack: the samples and songs `Sound_Play`, `Sound_PlayAt` and `Sound_Stop` play, the listener, and `Music_Tick`'s adaptive music ([the frame's sounds](../es1/simulation/main-loop.md#the-frames-sounds--sim_framesoundtick-00058574)).
- **Open:** whether the HERC state table handles every group order word the retail missions hold ([`mech-ai.md#open`](../es1/simulation/mech-ai.md#open)): `HercWorks.Query orders` over the 77 `.MSN` finds −1, 1, 3, 6, 8, 10 and 11 at group `0x32`; checked against the table before step 8.
- **Open:** a tweak that maps textured faces with perspective in place of retail's column walk (decision 8): the code path that selects it lands with step 3b; the tweak's setting, its name and its localized strings are not built.
