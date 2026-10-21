# The simulator's main loop (DBSIM.EXE)

DBSIM plays one mission per run. `WinMain`'s body, `Sim_Run` (`0045f144`), starts the mission, runs one simulation tick and one drawn frame per pass of its loop until the mission ends, and shuts down. Addresses are DBSIM's.

## Starting

`Sim_Run`, in order: reads `data\drive.cfg` into `DriveCfg_Directory`; parses the command line ([`../command-line.md`](../command-line.md#dbsim)); without `-eggplant` shows a message box and returns; runs subsystem phases 0 and 1; asserts under 8 MB of physical memory and sets `CockpitArt_LoadOnDemand` under 12 MB; calls `Sim_InitMissionSession` (`004614fc`, [the mission's bring-up](mission-loading.md#showing-it)); runs subsystem phase 3.

## The frame

Each pass of the loop:

1. Subsystem phase 4, then `Time_BeginSimTick` unless `TapePlayback` is set ([timer units](dbsim-physics-notes.md#timer-units)).
2. While `004d25f6` is set, which `Sim_Suspend` sets when the window loses the focus, `Sleep(100)` and `Win_PumpMessages`, a `WM_QUIT` ending the loop.
3. Otherwise `Sim_MainTick` ([the tick](#the-tick)), `Cam_Update(ViewObjectPtr)` ([`external-views.md`](external-views.md#the-camera-object--cam)), `Sim_RenderFrame` ([the frame's draw](../rendering/terrain-drawing.md#the-frame)), `Sim_EndFrame` ([`cockpit-views.md`](cockpit-views.md)), subsystem phase 6, and `Sim_FrameCount` + 1.

The loop ends when `DemoAbort` or the quit flag `004d2582` is set, or `Sim_MainTick` answers non-zero.

## The tick

`Sim_MainTick` (`0045f464`) runs its passes in a fixed order:

1. `Input_BuildPlayerDevice` ([`joystick-input.md`](joystick-input.md#applying-the-bindings--input_buildplayerdevice-0045a7f4)); during tape playback it copies the frame's delta from the input block's `+0x2e` into `SimTickDelta`.
2. Unless the freeze `004d2576` is set: the effect pools — explosions, smoke balls, smoke, fire, debris, meteors — then the projectiles (vtable `+0x14`), then every structure whose group has entered the mission (vtable `+0x18`), each pool walked last to first with `Pool_Prev` and an object that answers non-zero queued for deletion.
3. The same gate: the mission's groups in order, `Group_DeploymentCheck` for one still to arrive and otherwise `Group_OrderTick`, which runs `Mech_AiTick` (`00411cec`) on each member in turn: reassess, move, then think ([`ai-dispatch.md`](ai-dispatch.md)).
4. A set `004d2580` (`Alt+keypad +`) moves into the freeze, so one tick runs ([the developer keys](../command-line.md#-sprunknown-the-developer-keys)).
5. `Sim_PollPlayerInput` — the player's commands, control laws and fire; a non-zero answer ends the tick with 1.
6. Unless frozen: `ActionTimers_Tick` and `Actions_EvaluateTriggers` ([`mission-deployment.md`](mission-deployment.md#the-four-ways-an-action-activates)); then `ViewChain_Apply`; then, the same gate, `Sim_DetectionTick` and `Mech_PerTickSystemsUpdate` for every deployed machine still standing, last to first.
7. With the player's machine standing, `Mission_PollStatus`, and on status 3 `Group_ApplyOutnumberedDamage` on the player's group, then the status alert ([the status alert](mission-objectives.md#the-status-alert--gnl_alrt-00455934)); with it destroyed and the simulation not frozen, the death camera ([`external-views.md`](external-views.md#the-player-death-camera)). The poll's alert ends the tick with 1 when the button pressed is the one that ends the mission for that status; the death camera's, raised for status 2 once its countdown runs out, ends it whatever is pressed.

## The end

After the loop `Sim_Run` stops the music and sound, saves option 6 when the full-screen state has changed ([`preferences.md`](preferences.md)), deletes `tapes\prefs.cfg` and `tapes\keyjoy.cfg` when `TapePlayback` is still set ([`../formats/tap-input-tape.md`](../formats/tap-input-tape.md#the-bundle)), and calls `Sim_Shutdown` (`00461eec`): `CockpitMouseLive` = 0, the checkpoint file and the tape closed, `Mission_WriteResults`(`LocalPlayerMech`) ([what the mission leaves the shell](mission-objectives.md#what-the-mission-leaves-the-shell--mission_writeresults-0042412c)), the exit code ([exit codes](../command-line.md#exit-codes)), and `Sim_ShutdownNoOp1` and `Sim_ShutdownNoOp2`.
