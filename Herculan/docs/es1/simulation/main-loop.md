# DBSIM.EXE — the main loop: mission start, frame, tick and shutdown

**ES1's simulator runs ES2's main loop ([ES2's](../../retail/simulation/main-loop.md)) as a DOS program: it plays one mission per run, starts it with no loading picture, draws a frame for every simulation tick, and leaves its exit code in `data\exit.cfg` for `GO.EXE` where ES2's returns it.** What a player would see differently:

- every timer in the simulation runs in tenths of a second, so a countdown of 10000 counts lasts 3.9 seconds where ES2's lasts 4.9 ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40));
- a frame waits for two interrupts of the PC timer, which runs at 120 a second, so at most 60 frames are drawn a second, where ES2's waits 40 ms; a frame drawn within two interrupts still counts as 25 ms of play, so at 60 frames a second the game runs half again as fast as real time ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40));
- `Alt+S` freezes the simulation only while a tape records or plays, and the mission's trigger areas still fire while it is frozen;
- the keys differ: keypad 0 switches what the stick drives ([the stick mode](input.md#the-stick-mode)), `Ctrl+Esc` quits as `Ctrl+Q` does, `Esc` plays a sound as it leaves the outside view, `Ctrl+1` turns the throttle into a forward-only one and then switches it between forward only and reverse only, with or without the throttle-lever option, where ES2's needs a throttle lever, and `'`, `\`, `/` and the developer keys of ES2's `-SPRUNKNOWN` have no case ([commands](#commands--sim_dispatchcommand-00059df8));
- holding the mouse's second button while the first is down snaps the turret straight ahead, and a disabled HERC's turret swings back to center every tick ([the player's controls](#the-players-controls--sim_pollplayerinput-0005a0c0));
- a mission whose HERC starts within 40000 of a friendly structure opens with the lift, where ES2's lift depends on its block's `+0x54` instead ([the mission start](#the-mission-start--sim_startmission-0005a9dc)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The input build these functions call is [`input.md`](input.md)'s.

## Where the object files start and end

Five object files, each opened by a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)):

| Code | Opening run, tables | Holds | Ends |
|---|---|---|---|
| `00058d94`–`00059663`, `main.cpp` | `00058d94`, tables `00096d1e`, `00096d52` | the command line, `Sim_Main`, `Cam_AttachTo`, the path helpers | `Sim_DebugBreak`'s `RET` at `00059663` |
| `00059664`–`00059aa3` | `00059664`, tables `00097022`, `00097056` | `Sim_MainTick` | its `RET` at `00059aa2` and one zero byte |
| `00059aa4`–`00059d1f` | `00059aa4`, tables `0009708a`, `000970be` | `Sim_EndFrame`, `Sim_RenderFrame` | `Sim_RenderFrame`'s `RET` |
| `00059d20`–`0005a517` | `00059d20`, tables `000970f2`, `00097126` | `ViewChain_ToggleCameraControl`, `Sim_DispatchCommand`, `Sim_PollPlayerInput` | `Sim_PollPlayerInput`'s `RET 4` at `0005a512` and three zero bytes |
| `0005a518`–`0005b68b`, `init.cpp` | `0005a518`, tables `00097202`, `00097236` | the display, the sun, `Sim_StartMission`, the cameras' destructors, `Sim_Shutdown` | `Sim_Shutdown`'s `RET` at `0005b689` and two zero bytes |

The asserts that name `main.cpp` are `Sim_Main`'s (`0005937c`, `0005942f`, `0005948e`, `00059590`); `init.cpp`'s are `Sim_AssertCleanupHook`'s (`0005a841`) and `Sim_Shutdown`'s (`0005b635`). None of the five has a static initializer. The file before ends with the exit routine `00058d5c` (exit table entry `000adcd4`) and two zero bytes; the one after is the library region, from `__cinit` (`0005b68c`, [libraries against game code](../formats/compiler.md#libraries-against-game-code)). ES2's counterparts are in the [functions](#functions) table.

## Starting — `Sim_Main` (`00059350`)

(argc, argv), called by the startup ([startup](../formats/compiler.md#startup)). In order:

1. `Mem_InitHeap`(6,000,000) ([who makes the heap](memory.md#who-makes-the-heap)).
2. `Sim_PreParseCommandLine` (`00058e30`)(argv, argc): `-c` anywhere on the line sets `Sim_AltDriveAllowed` (`000b5bce`).
3. **The drive prefix.** `ALT_DRIVE`'s value, taken as null unless `-c` was given, when the variable `PREFER_ALT` is set, and the empty string otherwise, is copied to `000b5d9c` and passed to `00056ecc`. `Path_ApplyAltDrive` (`000592a4`) puts that prefix before every path it builds, less the path's own drive letter. With `PREFER_ALT` set and no `-c` the copy reads from a null pointer.
4. It writes the heap's size, 4 bytes, to `data\mem.got` and an `int16` 0 to `data\exit.cfg`, both through `Path_ApplyAltDrive`, asserting at severity 5 (lines `0xf1`, `0xf9`) when either does not open.
5. `Sim_ParseCommandLine` (`00058e74`)(argv, argc), [below](#the-command-line).
6. Without `-eggplant` (`Sim_EggplantGiven`, `000b5bc4`) it closes the tape and `.dmp` files, turns the heap off, prints `To play EarthSiege, type ES.` and exits with status 1.
7. Subsystem phase 0; `VolumeGroup_SetBufferSize`(`0x200`); `VolRStream_SetGroup` with mask 1, extension `.vol`, the directory `ALT_DRIVE` (when `-c` was given) and `dirFirst` set when `PREFER_ALT` is ([the volume group](../formats/streams.md#the-volume-group)); subsystem phase 1.
8. The memory checks [`memory.md`](memory.md#who-makes-the-heap) lists, the first printing `You need %ld more bytes free to run...` with the shortfall (line `0x136`).
9. `Sim_StartMission` (`0005a9dc`), subsystem phase 3, and the frame loop ([the frame](#the-frame)).
10. After the loop, `Sim_Shutdown` (`0005b568`), `Mem_HeapOff`, and with `-eggplant` given and the quit flag clear, bit 0 of the byte `SimDisplay` (`000b5b5c`) points at cleared.

Where ES2's `Sim_Run` differs: it reads `data\drive.cfg` instead of the environment, refuses without `-eggplant` in a message box, checks physical memory, and after the loop saves the full-screen option and deletes the tape's two configuration files ([ES2's start](../../retail/simulation/main-loop.md#starting)).

### The command line

`Sim_ParseCommandLine` (`00058e74`), (argv, argc), looks each argument that starts with `-` up by its next character in the 16 dwords at `00096e88` (`REPNE SCASD`) and jumps to the target 16 dwords after; Ghidra has not disassembled the case bodies (`00058ec8`–`0005921c`). Case-sensitive, and no `/`:

| Switch | Store | Effect |
|---|---|---|
| `-eggplant` or `-EGGPLANT` | `Sim_EggplantGiven` (`000b5bc4`) = 1 | Without it the simulator refuses ([above](#starting--sim_main-00059350)) |
| `-c` | `Sim_AltDriveAllowed` (`000b5bce`), by `Sim_PreParseCommandLine` | `ALT_DRIVE` is honored |
| `-B` | `000b5bcc` = 1 | `Sim_DebugBreak` (`00059658`) runs `INT3` while it is set |
| `-D` | `TapePlayback` (`000b5b76`), `DemoMode` (`000b5bd0`) = 1 | Plays `tapes\<name>.tap`, the name from `0005771c`, through `Config_BuildPath`; both flags go back to 0 when it does not open |
| `-S` | `000b5b67` = 1 | The page numbers [`Sim_StartMission`](#the-mission-start--sim_startmission-0005a9dc) gives the display |
| `-m` | `000bda94` = 1 | `Sim_StartMission` clears the monochrome monitor (`Mono_Clear`) |
| `-C<name>` | `CockpitChassis_SelectByName` (`00024394`)(name) | The cockpit's chassis ([the cockpit's scripts](../formats/ttm-scripts.md#the-simulator)) |
| `-s` | `000b5b68` toggled from 1 | Cleared, `Sim_StartMission` passes `Sound_Init` digital device 0, so no sound manager is made ([the driver](../formats/sound-samples.md#the-driver)) |
| `-P` | `000b5b69` = 1 | No reader found by `es2_xref.py` besides this store ([Open](#open)) |
| `-t<n>` | `0003ea50`(`atol`(n)) | |
| `-V<n>`, `-W<n>`, `-T<n>` | `000b5bb2`, `000b5bb4`, `000b5bb6` | Set to -1 by `Main_StaticInit` (`00057414`); no other reader found ([Open](#open)) |
| `-p<name>` | `TapePlayback` = 1 | Plays `<name>.tap` ([recording and playback](input.md#recording-and-playback)) |
| `-r<name>` | `TapeRecording` (`000b5b78`) = 1 | Records to `<name>.tap` |
| `-d` | `DmpFile` (`000b5b7e`) | Opens `<name>.dmp`, mode `wb` while recording (and closes it again) and `rb` otherwise; the name is the tape's |
| `-l` | `g_LowMemory` (`000bda98`) = 1 | The low-memory mode ([low memory](../formats/chassis-dat.md#low-memory)) |

The name of `-p`, `-r` and `-D` is `TapeStem` (`000b5b7a`), the first one given; the extension is forced to `tap`. `-p` opens the tape, `rb`, as `TapeFile` (`000b5b82`) and unpacks seven files from it with `0005756c`: `data\script.dat`, `data\player.mec`, `data\mission.var`, `tapes\prefs.cfg`, `data\restore.dat`, `data\object.str` and `tapes\keyjoy.cfg`, the two configuration files through `Config_BuildPath` (`00059300`), which gives `tapes\` once `TapePlayback` is set. `-r` opens it `wb`, packs the same seven with `000574a0` from `data\` (`keyjoy.cfg` from the prefix's root), and closes it. ES2's bundle is the same seven ([the bundle](../../retail/formats/tap-input-tape.md#the-bundle)).

Where ES2's parsers differ ([ES2's switches](../../retail/command-line.md#dbsim)): ES2's `-c` stores a word, `+0x72` of its block, where ES1's honors `ALT_DRIVE`; ES2 has `-v`, `-Z`, `-X`, `-b`, `-a`, `-R`, `-E`/`-F`/`-G` and `-SPRUNKNOWN`, which ES1's lacks; ES2's `-l` sets `CockpitArt_LoadOnDemand` where ES1's sets the low-memory mode.

## The mission start — `Sim_StartMission` (`0005a9dc`)

`Sim_Main` calls it once. ES2's counterpart puts up a loading picture first ([ES2's](../../retail/simulation/mission-loading.md#showing-it)); ES1's shows nothing until the first frame. In order:

1. Hands `Sim_AssertCleanupHook` (`0005a7b8`) to `0005730c`, where ES2's registers its hook with `Assert_AddHook`. The hook runs `Sound_Shutdown`, `0006a5fc` on the current display, closes the tape and `.dmp` files, `GndTex_Free`, and writes an `int16` 0 to `data\exit.cfg` (`init.cpp` line 100).
2. `Input_BuildSourceTable` ([setting up](input.md#setting-up--input_buildsourcetable-0004c95c)); `Sim_Timer` (`000b5baa`) = `DVTimer_Ctor` (`0006c6f8`)(0, `0x26d3`), the PC timer at 120 interrupts a second ([the rate](timer.md#the-rate)).
3. **Sound.** With `000b5b68` set it reads `resource.cfg` (through `Path_ApplyAltDrive`) with `Profile_GetString` (`000511a4`) and `Profile_GetInt` (`00050ffc`) ([profile files](../formats/profile-files.md)): `[SoundFX]`'s `Driver#`, `IRQ`, `DMA` and `PortAddress` and `[Music]`'s `Driver#` and `PortAddress`, the hexadecimal ones through `sscanf` (`0006c871`) with `%X`, and passes them to `Sound_Init`; with it clear, digital device 0 ([the driver](../formats/sound-samples.md#the-driver)). Then `0004e498`, and with no sound manager (`000959ac`) `Prefs_SetOption`(1, 0, *i*) for options 0, 1 and 2.
4. `Light_ResetSystem`, `00075858`, `00066e00`, `Widget_LoadStylesAndFonts`; the two hooks at `00094d80` and `00094d84` = `SimInput_PushDisable` (`0005a5b4`) and `SimInput_PopEnable` (`0005a5d8`) ([small functions](#small-functions)); `Mono_Clear` with `-m`.
5. Subsystem phase 2; the cursor `00057640` returns; `DBSim_LoadScriptDat`; `ObjList_Add` of each `Zone_ShapeObjects` entry up to `Zone_ShapeCount` or the first null; `000441cc`; `maybe_CockpitMouseLive` (`000bd512`) = 0; `Time_InitSimClock` (`00063e64`, [the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)).
6. `DBSim_SpawnMissionObjects` ([the second pass](../formats/script-dat.md#the-second-pass)) with option 1 set to 0 around it (`Prefs_SetOption`(1, 0, 1), then `Prefs_RevertOption`(1, 1)).
7. Deletes every explosion (slot `+0xc` with 3, then `ExplosionPool_FlushDeletes`, `EffectLightPool_FlushDeletes` and `00006d50` after each) and every debris piece (`DebrisPool_FlushDeletes` after each), as ES2's empties both pools.
8. `Light_CreateMissionSun` ([the sun](../rendering/polygon-shading.md)); `ObjList_ZoneShapeCount` = `ObjList_Count`.
9. **The player.** `LocalPlayerMech` (`000b5b86`) = the last machine of `g_MechPool` (`000af68c`) with `+0x9d`, the locally piloted byte, set; `ViewChain_Viewed` (`000bda9a`) = that machine, and `ViewChain_Chosen` (`000b5bbc`) too.
10. **The squad cameras.** One `0x4a`-byte camera for each member of the player's group (count `+0x12` of the group `+0x3f`), into the array at `000bda9e`: `TSCamera_CtorDefault`, the class's vtable `000972de` at `+0x12` and `+0x32`, `+0x36`, `+0x44`, `+0x46` and `+0x48` zeroed. ES2 builds its squad cameras at the same point.
11. **The display.** `SimDisplay` = `Sim_CreateDisplay` (`0005a5f4`); `0006a824`(display, 640, 2); page origins 1 to 3 at `+0x50`–`+0x64` = (0, 200), (320, 0), (320, 200), each passed to the driver's routine at `0009df10`; `+0x88` = 0, `+0x8c` and the draw page `+0x3c` = 2 (0 with `-S`), the visible page `+0x34` = `+0x88`, and without `-S` `+0x3c` = 3, each passed to the driver while the display is current ([the views](cockpit-views.md#the-view-manager)).
12. **The main camera.** `ViewObjectPtr` (`000b5b8a`) = a `0x4a`-byte `TSCamera_Ctor`(camera, (0, 0, 0), (0, −6000, 0), `0x100`, 8, the render context `000b5b6e`), retyped and zeroed as the squad cameras; `Cam_AttachTo` (`0005923c`)(`ViewChain_Viewed`); mode `+0x36` = 2. ES2's main view takes a focal shift of 9 when `004d30c4` is `0x4b1` or more ([the views' focal length](../../retail/simulation/cockpit-views.md#the-views-focal-length)); ES1's is always 8.
13. **The cockpit.** `000b5bca` = 1 (the bring-up, under which `Widget_DrawToCockpit` copies nothing); `CockpitViewManagerInstance` = `CockpitViewManager_Ctor`(0, player), `CockpitViewManager_Publish`; `WeaponMounts_BuildGauges` on the player's mounts; `Cockpit_LoadSquadmatePilots`; `+0x194` = `PaperDoll_BuildWeaponIcons`(machine) for every machine of `g_MechPool`; `Cursor_SetImage` with the cursor of step 5; `Player_PerFrameCockpitUpdate`, `Cam_Update`; the draw page again; `CockpitView_SetView` with view 1 and then 0, as ES2's ([both canopies resident](../../retail/simulation/cockpit-views.md)).
14. **The lift.** `LiftStart_Pending` (`000b5bb0`) = 1 when a structure of `g_BasePool` whose group (`+0x3f`) has side byte `+0x14` 0, the player's side, lies within 40000 of the player. Then it calls `00057cf4`; otherwise it sets `000928ee` = 8 and runs `Palette_BlendRangeByPitch`, `Palette_FlushDirtyRange` and a first `Sim_RenderFrame`. ES2's runs the same search over its structures (side byte `+0x12`) at the same point and discards the answer: its two lift calls, `LiftStart_DarkenPalette` here and `LiftStart_Rise` at the end, test its block's `+0x54` instead ([the lift start](../../retail/simulation/mission-deployment.md#the-lift-start)).
15. `000b5bca` = 0; `Palette_FlushDirtyRange`; `Widget_DrawToCockpit`(0, {0, 0, 320, 400}, 1), the whole of page `+0x8c` onto `+0x88`; the driver's routine at `0009df4c`; `000b5b72` and `000b5b74` = option 6 (`000bd568`) and `00057c08`(option 6), the pair `Sim_RenderFrame` tests; `000b5b96` = option 5 above 240 ([the ground](../rendering/ground-and-sky.md)); `0004e294`(1); with the lift, `00057ffc`; unless a tape plays, `maybe_CockpitMouseLive` = 1; `Sound_Play`(`0x13`).

`Sim_CreateDisplay` builds the display's two four-element arrays with `DisplayPageOrigin_ElementCtor` (`0005a79c`), (p): p, or `operator_new`(8) when p is null.

## The frame

Each pass of `Sim_Main`'s loop:

1. Subsystem phase 4; `00058574`; `Time_BeginSimTick` (`00063f40`) unless `TapePlayback` is set ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)).
2. It leaves the loop when `DemoAbort` (`000b5bd2`) is set or `Sim_MainTick` ([the tick](#the-tick--sim_maintick-00059700)) answers non-zero.
3. `Cam_Update`(`ViewObjectPtr`) ([`Cam_Update`](external-views.md#cam_update)).
4. `Sim_RenderFrame` (`00059c08`, [the draw](../rendering/ground-and-sky.md)), skipped while a tape plays with the simulation frozen.
5. `Sim_EndFrame` (`00059b40`, [the frame's end](cockpit-views.md#cockpitview_stepviewtransition-00022904)), which ends with `Mem_TrackPeakUse` (`00059288`): `Mem_PeakUse` (`00096f08`) = the larger of itself and the heap's size less its bytes free (`000ae4b0` − `000ae4b8`, the heap object's `+4` and `+0xc`, [the heap](memory.md#the-heap)).
6. Subsystem phase 6.

ES2's loop also counts the frames, sleeps while the window has lost the focus, and draws every frame ([ES2's frame](../../retail/simulation/main-loop.md#the-frame)).

## The tick — `Sim_MainTick` (`00059700`)

No arguments. In order:

1. The input block = `Input_BuildPlayerDevice` ([the input build](input.md#the-input-build--input_buildplayerdevice-0004cb14)); during playback `SimTickDelta` = the block's `+0x29`, the frame's recorded delta.
2. Unless `Sim_Frozen` (`000b5b92`) is set, each pool walked last to first with `Pool_Prev`:
   - every structure of `g_BasePool`, through slot `+0x1c`, whatever its group;
   - the explosions (`g_ExplosionPool`, `00005d74`), the pool `000aef68` (`0000693c`), the pool `000aefd4` (`00006b28`) and the debris (`000aeef8`, `000064b8`), an element whose tick answers non-zero destroyed at once through slot `+0xc` with 3;
   - `maybe_WeaponChain_EoHold` (`000b5bc6`) = 0;
   - the projectiles (`000af0b4`) through slot `+0x18`, destroyed the same way;
   - the groups, in order: one with no action (`+0x16`) runs `Group_TickMembers` ([the tick](mission-groups.md#the-tick)); one whose action has fired (its `+0xa` set) has the action cleared, so its members tick from the next tick on.
3. `Sim_StepOnce` (`000b5b9c`), when set, is cleared and the freeze set.
4. `Sim_PollPlayerInput` ([below](#the-players-controls--sim_pollplayerinput-0005a0c0)); a non-zero answer ends the tick with 1.
5. `Actions_EvaluateTriggers`(`LocalPlayerMech`) ([trigger areas](world.md#trigger-areas--actions_evaluatetriggers-0001f87c)), frozen or not, and `0005894c`, where ES2's runs `ViewChain_Apply`.
6. Unless frozen: `Sim_DetectionTick` ([the sensor model](sensor-model.md)) and `Mech_PerTickSystemsUpdate` ([the systems tick](mech.md#the-systems-tick--mech_perticksystemsupdate-00014ee0)) for each machine of `g_MechPool`, last to first, whose group has no action and that is not destroyed (`+0x93`).
7. `Mission_PollStatus`(`LocalPlayerMech`) ([the poll](mission-status.md#the-poll--mission_pollstatus-0000f454)), destroyed or not. Status 2 calls `00058414` and ends the tick with 1. Another status of 0 or more puts up the status panel `00046efc`(0, 0, 1, status) after, for status 3, `Group_ApplyOutnumberedDamage` on the player's group ([going out of the fight](mission-groups.md#going-out-of-the-fight)), which shows status `0x12` instead when it destroys the player; it runs the panel through slot `+0xc` of its `+0x314`, ends the tick with 1 when the answer equals the word at `00097158`[status], and destroys the panel with `00045d0c`(panel, 3).
8. Otherwise 0.

Where ES2's differs ([ES2's tick](../../retail/simulation/main-loop.md#the-tick)): ES2's ticks the effect pools first, the projectiles next and only the structures whose group has arrived, and queues a finished effect for deletion; it runs its action timers and gates the triggers on the freeze; and with the player destroyed it runs the death camera in place of the poll, where ES1's polls and hands status 2 to `00058414`.

## Commands — `Sim_DispatchCommand` (`00059df8`)

(block): the block's command word, a set-1 scancode plus `0x200` for `Alt` and `0x400` for `Ctrl` ([keyboard commands](../../retail/simulation/cockpit-input.md#keyboard-commands-are-scancodes)), goes to `CockpitWidgets_HandleCommand` ([keys](cockpit-panel.md)), then to `Sim_HandleWindowKey` (`00059dec`), which answers 0 for every code, then to its own cases, found by `REPNE SCASD` in the 15 dwords at `00097180` with their targets 15 dwords after; a code it has no case for goes to slot `+0x30` of `ViewChain_Viewed` (the player's HERC's is `Mech_HandleCommand`, [the cockpit](mech.md#the-cockpit)), whose answer it returns. It returns 1 for a code it handles plus the second term below.

| Code | Key | Effect |
|---|---|---|
| `0x21f` | `Alt+S` | Toggles `Sim_Frozen` while `TapePlayback` or `TapeRecording` is set |
| `0x2f` | `V` | In view (`ViewChain_View`, `000b5b8e`) 1 or 3 queues view command 3 and sets `ViewChain_PendingView` (`000b5bba`) to 2; in view 2 queues 2 and sets 3 ([the chain of views](../../retail/simulation/external-views.md#the-chain-of-views)) |
| `0x31` | `N` | In view 3 with the view manager's `+0x10` at 4: the next object of `LiveObjectList` after `ViewChain_Viewed` (`ObjectList_Next`) whose `+0x28` is not negative and whose group is the player's; when it is not the viewed one, `00058a44`(object), the camera's mode 1, `ViewChain_Chosen` = it and `ViewChain_CaptionFrames` (`000b5bc0`) = 2 |
| `0x0f`, `0x1c` | `Tab`, `Enter` | With the view manager's `+0x10` at 4, `ViewChain_ToggleCameraControl` (`00059dbc`) |
| `0x0e` | `Backspace` | `g_CenterTurretMode` = 1 |
| `0x10` | `Q` | `Mission_StatusForAlert`(1, `LocalPlayerMech`)'s status in the panel `00046efc`, as in the tick; the second term is 1 when the answer ends the mission; `MissionAlertArmed` = 0 |
| `0x01` | `Esc` | Queues view command 3, `Sound_Play`(`0x2d`), `ViewChain_PendingView` = 2 |
| `0x401`, `0x410` | `Ctrl+Esc`, `Ctrl+Q` | The panel `00046efc`(0, 0, 1, 1); its answer is the second term, and a non-zero one sets `Sim_QuitFlag` (`000b5b9e`) |
| `0x19` | `P` | The panel `00046efc`(0, 0, 1, 0); its answer is the second term |
| `0x219`, `0x58` | `Alt+P`, `F12` | `00057820`, where ES2's raises the preferences panel |
| `0x402` | `Ctrl+1` | `ThrottleLeverMode` (`000919ce`) = −1 when it is above 0, and 1 otherwise ([the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)) |
| `0x52` | keypad 0 | `Input_ToggleStickMode` ([the stick mode](input.md#the-stick-mode)) |

A result of 2 ends the mission ([the tick](#the-tick--sim_maintick-00059700)), so `Ctrl+Q` answered yes ends it with `Sim_QuitFlag` set, and `Sim_Shutdown` gives exit code 0.

`ViewChain_ToggleCameraControl` (`00059dbc`), frameless: flips `000b5bb8` between 0 and 1, and sets `ViewChain_CaptionFrames` to 2 when the camera's object `+0x32` is the player.

Where ES2's differs ([ES2's keys](../../retail/command-line.md#-sprunknown-the-developer-keys)): ES2's has `'` (`TargetSelect_Nearest`), `\` (center legs), `/` (the manual) and `0x14` (auto-track); its `Backspace` also clears the legs mode and the auto-track; its `V`, `N` and `Esc` refuse while the camera is locked and its `V` steps view 4 too; its `Ctrl+1` needs a throttle lever; `Alt+S` also answers the developer flag; and `F12` raises its preferences panel.

## The player's controls — `Sim_PollPlayerInput` (`0005a0c0`)

(block). In order:

1. `Sim_DispatchCommand`(block); its answer being 2 is the return value.
2. `CockpitView_PollViewDevice`(`CockpitViewInstance`, block) ([the cockpit](cockpit-panel.md)).
3. **The axes.** With `InputDrivesCamera` (`000b5b90`) clear the machine takes steering `+0xd`, throttle `+0xf`, twist `+0x11` and pitch `+0x13`, and the camera nothing; with it set the camera takes the two axes `+0x1d` and `+0x21` point at and the trigger byte `+0x15`, and the machine nothing ([the input block](input.md#the-input-block)).
4. **The gunsight drag**, ES2's ([the gunsight drag](../../retail/simulation/joystick-input.md#the-gunsight-drag)) with the gadget `+0x1f1`'s rect `+0`–`+0xc`: while the byte `+0xb` is set it arms when the pointer (`+2`, `+6`) is inside the rect and then adds *dx*²/64 to the twist and *dy*²/32 to the pitch (`000b5de4`, `000b5de8`); `+0xb` clear disarms it. With `+0xb` and `+0xa` both set it zeroes the player's twist `+0x22e` and pitch `+0x230` and disarms.
5. `Cam_Steer`(`ViewObjectPtr`, trigger, the two camera axes) ([the camera](external-views.md#the-camera)).
6. **The machine.** When `ViewChain_Viewed`'s class `+0x162` is 2 (a flyer) and the camera is not driven, `0001b794`(viewed, throttle, steering) and the viewed object's `+0xa9` = 1. Otherwise, when the player's `+0x33` is set: `Mech_ApplyThrottleInput`(player, throttle, steering); then with the player's target `+0x15e` and its range:
   - the player disabled (`+0x9e`): `g_CenterTurretMode` = 1;
   - otherwise, with no twist or pitch input, the auto-track latch (`+0x14` of the mounts `+0x198`) set and a live target: the target's `+0x20` plus the offset its slot `+0x34` gives goes to `Cockpit_TargetAnglesFromCameraBone`, and the centering mode is cleared; with input, the centering mode is cleared;
   - then the centering mode runs `Mech_CenterTorsoTick`(player, range), and otherwise, unless tracking, `Mech_TorsoTwistTick` and `Mech_TorsoPitchTick` take the twist and pitch ([the turret](mech.md#the-turret)).
7. With the camera not driven, `WeaponMounts_FireTrigger`(mounts, player, block) ([the trigger](weapon-mounts.md)).
8. Button 2 (`+0x17`) pressed: `TargetSelect_Cycle` once a press (latch `000971fc`).
9. Button 3 (`+0x18`) pressed: in stick mode 1 the throttle `+0x226` = 0 and `+0x8d` = 1, the all-stop; in stick mode 2 `g_CenterTurretMode` = 1.

Where ES2's differs ([ES2's](../../retail/simulation/joystick-input.md#while-the-camera-has-the-controls)): ES2's passes the camera the pitch axis when a throttle lever is fitted, runs its legs mode, tracks through `Player_ResolveTargetAimPoint`, flies the RAZOR when the player's own type record says so, fires through `Mech_PlayerFireTick`, and runs the 19 button actions of the controls panel; it has no disabled-turret or second-button arm.

## The end — `Sim_Shutdown` (`0005b568`)

`Sim_Main` calls it after the loop: `maybe_CockpitMouseLive` = 0; the `.dmp` file and the tape closed; `GndTex_Free`; `Mission_WriteResults`(`LocalPlayerMech`) ([the results](world.md#writing-the-results--mission_writeresults-0001d698)); the exit code, 6 under `DemoMode`, 0 with `Sim_QuitFlag`, else 4 when the player is destroyed (`+0x93`) and `MissionModeFlag` (`000bc4a0`) is not 0, else 3, written as an `int16` to `data\exit.cfg` (assert `init.cpp` line `0x1a4`); `Sound_Shutdown`; `Sim_ShutdownNoOp1` and `Sim_ShutdownNoOp2` ([small functions](world.md#small-functions)). ES2's codes are the same, returned from `WinMain` ([exit codes](../../retail/command-line.md#exit-codes)); ES2's load zeroes its `MissionModeFlag`, ES1's keeps the header's mode word ([the header](../formats/script-dat.md#the-header)).

## Small functions

- **`Config_BuildPath` (`00059300`)**, (name): `data\` + name, or `tapes\` + name while `TapePlayback` is set, in the buffer `000b5dc0`, as ES2's.
- **`Path_ApplyAltDrive` (`000592a4`)**, (path): the prefix at `000b5d9c` + the path less a leading drive, in the buffer `000b5da0`.
- **`Cam_AttachTo` (`0005923c`)**, (object): slot `+0x34` of the object gives the eye and orbit offsets, which `Cam_AttachEye` and `Cam_AttachOrbit` set on `ViewObjectPtr`, as ES2's (`0045ed94`) with slot `+0x30`.
- **`SimInput_PushDisable` (`0005a5b4`)** pushes `SimInput_Enabled` (`00095818`) on the byte stack at `000b5df8`, its depth at `00097280`, and turns the keys off with `SimInput_SetEnabled`(0); **`SimInput_PopEnable` (`0005a5d8`)** pops it back through `SimInput_SetEnabled`. `00045f00` calls the first through `00094d80` and `000460b8` the second through `00094d84`.
- **`Sim_ReleaseSessionObjects` (`0005b534`)**: `DVTimer_Dtor` (`0006c758`)(`Sim_Timer`, 3) and, with the render context `000b5b6e` set, its slot `+0x1c` with 3, ES2's (`00461e74`). No reference to it found by `es2_xref.py`.
- **`Sim_DebugBreak` (`00059658`)**: `INT3` while `-B`'s `000b5bcc` is set. No reference to it found by `es2_xref.py`.
- The cameras' destructors: `TSOrthoCamera_DtorInitCopy` (`0005b4d4`), slot `+4` of `000972de`, and `TSCamera_DtorInitCopy` (`0005b508`), slot `+4` of `00097362`, the module's copies of the library's ([module copies](../formats/compiler.md#vtables)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `HeapPtr_DtorMainCopy`, `ConfigPart_DtorMainCopy`, `ConfigPart_GetClassTagMainCopy`, `TSPartBase_DtorMainCopy` | `00058d94`, `00058dbc`, `00058dfb`, `00058e04` | |
| `Sim_PreParseCommandLine`, `Sim_ParseCommandLine` | `00058e30`, `00058e74` | `WinMain_ParseSwitches` (`0045e6b0`), `Sim_ParseCommandLine` (`0045e73c`) |
| `Cam_AttachTo`, `View_GetActiveObject`, `Mem_TrackPeakUse` | `0005923c`, `00059280`, `00059288` | `0045ed94`, `0045eddc` |
| `Path_ApplyAltDrive`, `Config_BuildPath`, `Sim_Main`, `Sim_DebugBreak` | `000592a4`, `00059300`, `00059350`, `00059658` | `Path_PrefixBaseDir` (`0045ede4`), `0045eea4`, `Sim_Run` (`0045f144`) |
| `HeapPtr_DtorMainTickCopy`, `ConfigPart_DtorMainTickCopy`, `ConfigPart_GetClassTagMainTickCopy`, `TSPartBase_DtorMainTickCopy` | `00059664`, `0005968c`, `000596cb`, `000596d4` | |
| `Sim_MainTick` | `00059700` | `0045f464` |
| `HeapPtr_DtorFrameCopy`, `ConfigPart_DtorFrameCopy`, `ConfigPart_GetClassTagFrameCopy`, `TSPartBase_DtorFrameCopy` | `00059aa4`, `00059acc`, `00059b0b`, `00059b14` | |
| `Sim_EndFrame`, `Sim_RenderFrame` | `00059b40`, `00059c08` | `0045fa98`, `0045fb9c` |
| `HeapPtr_DtorSimCommandCopy`, `ConfigPart_DtorSimCommandCopy`, `ConfigPart_GetClassTagSimCommandCopy`, `TSPartBase_DtorSimCommandCopy` | `00059d20`, `00059d48`, `00059d87`, `00059d90` | |
| `ViewChain_ToggleCameraControl`, `Sim_HandleWindowKey`, `Sim_DispatchCommand`, `Sim_PollPlayerInput` | `00059dbc`, `00059dec`, `00059df8`, `0005a0c0` | `0045fd2c`, `0045fd60`, `0045fdac`, `00460764` |
| `HeapPtr_DtorInitCopy`, `ConfigPart_DtorInitCopy`, `ConfigPart_GetClassTagInitCopy`, `TSPartBase_DtorInitCopy` | `0005a518`, `0005a540`, `0005a57f`, `0005a588` | |
| `SimInput_PushDisable`, `SimInput_PopEnable`, `Sim_CreateDisplay`, `DisplayPageOrigin_ElementCtor` | `0005a5b4`, `0005a5d8`, `0005a5f4`, `0005a79c` | |
| `Sim_AssertCleanupHook`, `Light_CreateMissionSun`, `TSLight_Dtor`, `TSSunLight_Dtor` | `0005a7b8`, `0005a888`, `0005a964`, `0005a99c` | `004611f8`, `00461240`, `0046209e`, `00462003` |
| `Sim_StartMission`, `TSOrthoCamera_DtorInitCopy`, `TSCamera_DtorInitCopy` | `0005a9dc`, `0005b4d4`, `0005b508` | `Sim_InitMissionSession` (`004614fc`) |
| `Sim_ReleaseSessionObjects`, `Sim_Shutdown` | `0005b534`, `0005b568` | `00461e74`, `00461eec` |

The five class tags (`00058dfb`, `000596cb`, `00059b0b`, `00059d87`, `0005a57f`), `ViewChain_ToggleCameraControl`, `Sim_HandleWindowKey`, the two `SimInput` hooks, `Sim_DebugBreak` and `Sim_ReleaseSessionObjects` have no Ghidra function. `Main_StaticInit` (`00057414`), the static constructor table's entry `000adc74` in the file before, zeroes the `0x78`-byte block from `SimDisplay` (`000b5b5c`), sets the `+0x4a` dword to 1, `000b5b68` to 1, `-V`/`-W`/`-T`'s words to -1, `000b5bb8` to 1 and `ViewChain_View` and `ViewChain_PendingView` to 2, clears `DemoAbort`, `DemoMode`, `Sim_AltDriveAllowed` and `000b5bca`, and seeds the two generators at `000b5bd4` and `000b5c48` with `Math_RandomSeed`, as ES2's `Main_StaticInit` (`0045cad8`) seeds its two.

## Globals

The flags lie in the block `Main_StaticInit` zeroes, `000b5b5c`–`000b5bd3`.

| | Address |
|---|---|
| `TapePlayback`, `TapeRecording`, `int16` | `000b5b76`, `000b5b78` |
| `TapeStem`, the name `-p`, `-r` or `-D` gave | `000b5b7a` |
| `DmpFile`, `TapeFile`, `FILE *` | `000b5b7e`, `000b5b82` |
| `ViewObjectPtr`, the main camera | `000b5b8a` |
| `ViewChain_View`, `ViewChain_PendingView`, `ViewChain_Chosen`, `ViewChain_CaptionFrames` | `000b5b8e`, `000b5bba`, `000b5bbc`, `000b5bc0` |
| `InputDrivesCamera`, `int16` | `000b5b90` |
| `Sim_Frozen`, `Sim_StepOnce`, `Sim_QuitFlag`, `int16` | `000b5b92`, `000b5b9c`, `000b5b9e` |
| `Sim_Timer`, the PC timer ([the PC timer](timer.md)) | `000b5baa` |
| `LiftStart_Pending`, `int16` | `000b5bb0` |
| `Sim_EggplantGiven`, `DemoMode`, `DemoAbort`, `Sim_AltDriveAllowed`, `int16` | `000b5bc4`, `000b5bd0`, `000b5bd2`, `000b5bce` |
| `ViewChain_Viewed`, the object the controls and views follow | `000bda9a` |
| the squad cameras, one dword a member | `000bda9e` |
| `maybe_CockpitMouseLive`, byte | `000bd512` |
| `Mem_PeakUse` | `00096f08` |

## Open

- **Open:** the callees read for none of this file's functions: `00058574` in the frame; `0005894c` and `00058a44` (in `ViewChain_Apply`'s and `ViewChain_ViewObject`'s places), `00058414` (status 2), `00057820` (`F12`), `0005771c` (the demo tape's name), `0005756c` and `000574a0` (the bundle) and `00057cf4` and `00057ffc` (the lift), all in the file before; `00056ecc` (the drive prefix), `0003ea50` (`-t`), `0005730c`, `0004e498`, `0004e294`, `00057640`, `000441cc`, `00075858`, `00066e00`, `0006a824`, `0006a5fc`, the status panel `00046efc` and `00045d0c`, and `0001b794` (the flyer's controls).
- **Open:** what `000b5b69` (`-P`) and `-V`, `-W` and `-T`'s words feed, and what sets `Sim_StepOnce`; `es2_xref.py` finds only the stores above and the tick's test, and a store through the block's base `000b5b5c` is not covered by that search.
- **Open:** what `000b5bb8`, which `ViewChain_ToggleCameraControl` flips, and the camera's mode 2 select, and what the bit `Sim_Main` clears in the display's first byte does.
- **Open:** which retail missions place a friendly structure within 40000 of the player's start, and so open with the lift (`00057cf4` and `00057ffc` unread). A retail check: such a mission brightens through a palette shift before play starts, and one that does not starts at once.
- **Open:** whether `Ctrl+Esc`, keypad 0 and `Esc`'s sound reach `Sim_DispatchCommand` in retail as the table says. A retail check: in a mission, `Ctrl+Esc` raises the same panel as `Ctrl+Q`.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: `BP <CS>:00059700` fires once a frame; after a mission ends, `DATA\EXIT.CFG` holds 3 (`03 00`) and after `Ctrl+Q` answered yes 0.
