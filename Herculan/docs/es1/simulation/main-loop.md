# DBSIM.EXE — the main loop: mission start, frame, tick and shutdown

**ES1's simulator runs ES2's main loop ([ES2's](../../retail/simulation/main-loop.md)) as a DOS program: it plays one mission per run, starts it with no loading picture, draws a frame for every simulation tick, and leaves its exit code in `data\exit.cfg` for `GO.EXE` where ES2's returns it.** What a player would see differently:

- every timer in the simulation runs in tenths of a second, so a countdown of 10000 counts lasts 3.9 seconds where ES2's lasts 4.9 ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40));
- a frame waits for two interrupts of the PC timer, which runs at 120 a second, so at most 60 frames are drawn a second, where ES2's waits 40 ms; a frame drawn within two interrupts still counts as 25 ms of play, so at 60 frames a second the game runs half again as fast as real time ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40));
- `Alt+S` freezes the simulation only while a tape records or plays, and the mission's trigger areas still fire while it is frozen;
- the keys differ: keypad 0 switches what the stick drives ([the stick mode](input.md#the-stick-mode)), `Ctrl+Esc` quits as `Ctrl+Q` does, `Esc` plays a sound as it leaves the outside view, `Ctrl+1` turns the throttle into a forward-only one and then switches it between forward only and a reverse only in which the forward control backs the HERC up ([the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)), with or without the throttle-lever option, where ES2's needs a throttle lever, and `'`, `\`, `/` and the developer keys of ES2's `-SPRUNKNOWN` have no case ([commands](#commands--sim_dispatchcommand-00059df8));
- the music is MIDI and follows the fight: one of three calm songs, picked at random, fades into its battle song when an enemy machine is in sight or close and back again once none is, checked every 5 seconds, where ES2 plays one CD track a mission ([the music](#the-music--music_tick-000585dc));
- sound `0x2d`, ES2's engine hum, is silent in the outside view and starts again in the cockpit once the opening sound `0x13` has ended, where ES2's outside view turns it up ([the frame's sounds](#the-frames-sounds--sim_framesoundtick-00058574));
- holding the mouse's second button while the first is down snaps the turret straight ahead, and a disabled HERC's turret swings back to center every tick ([the player's controls](#the-players-controls--sim_pollplayerinput-0005a0c0));
- a mission whose HERC starts within 40000 of a friendly structure opens with the lift, a ride up to the surface before play, where ES2's lift depends on its block's `+0x54` instead ([the lift start](lift-start.md));
- a destroyed HERC is not shown from outside: up to 3.9 seconds after its destruction the game stands still, the view shakes and the screen flashes through two palettes and fades to black, and the `YOU HAVE BEEN KILLED.` panel ends the mission, where ES2's camera circles the wreck first ([the death flash](#the-death-flash--sim_deathflash-00058414)).

Addresses are `ES1DBSIM` image addresses ([the X-32 executable](../formats/x32-executable.md#the-image)); ES2's are DBSIM's. The functions here remove their own arguments, so argument lists are in stack order ([calling conventions](../formats/compiler.md#calling-conventions)). The input read these functions call is [`input.md`](input.md)'s.

## Where the object files start and end

Seven object files, each opened by a run of `ConfigPart`'s copies ([module copies](../formats/compiler.md#vtables)), and between the first two the tape bundle's file, which opens with none ([where the tape code sits](../formats/tap-input-tape.md#where-the-code-sits)):

| Code | Opening run, tables | Holds | Ends |
|---|---|---|---|
| `00057378`–`0005749f` | `00057378`, tables `00096ad2`, `00096b06` | `Main_StaticInit` | its `RET` at `0005749e` and one zero byte |
| `00057680`–`00058d93` | `HeapPtr_DtorSimSessionCopy` (`00057680`), tables `00096b4a`, `00096b7e` | `DemoTape_PickRandom`, the preferences panel's raise, the camera copies, the lift, `Sim_DeathFlash`, the frame's sounds, the views | the exit routine `00058d5c` (exit table entry `000adcd4`), its `RET` at `00058d91` and two zero bytes |
| `00058d94`–`00059663`, `main.cpp` | `00058d94`, tables `00096d1e`, `00096d52` | the command line, `Sim_Main`, `Cam_AttachTo`, the path helpers | `Sim_DebugBreak`'s `RET` at `00059663` |
| `00059664`–`00059aa3` | `00059664`, tables `00097022`, `00097056` | `Sim_MainTick` | its `RET` at `00059aa2` and one zero byte |
| `00059aa4`–`00059d1f` | `00059aa4`, tables `0009708a`, `000970be` | `Sim_EndFrame`, `Sim_RenderFrame` | `Sim_RenderFrame`'s `RET` |
| `00059d20`–`0005a517` | `00059d20`, tables `000970f2`, `00097126` | `ViewChain_ToggleCameraControl`, `Sim_DispatchCommand`, `Sim_PollPlayerInput` | `Sim_PollPlayerInput`'s `RET 4` at `0005a512` and three zero bytes |
| `0005a518`–`0005b68b`, `init.cpp` | `0005a518`, tables `00097202`, `00097236` | the display, the sun, `Sim_StartMission`, the cameras' destructors, `Sim_Shutdown` | `Sim_Shutdown`'s `RET` at `0005b689` and two zero bytes |

The asserts that name `main.cpp` are `Sim_Main`'s (`0005937c`, `0005942f`, `0005948e`, `00059590`); `init.cpp`'s are `Sim_AssertCleanupHook`'s (`0005a841`) and `Sim_Shutdown`'s (`0005b635`). None of the last five has a static initializer. The exit routine `00058d5c` destroys `ViewChain_SpareCamera` (`TSContext_Dtor`(camera, 0), after storing the vtables `00096c5a` and `00096c6a` at its `+0x12`) and, while `DemoTape_NameTableBuilt` (`000b5cbc`) is set, the demo tapes' name table (`OwnedArray_Dtor`(table, 2), [the demo tape](../formats/tap-input-tape.md#the-demo-tape)). The file before the first ends with the static initializer `0005735c` (entry `000adc70`) and the exit routine `00057368` (exit table entry `000adcd0`), whose `RET` at `00057374` is followed by three zero bytes; the one after the last is the library region, from `__cinit` (`0005b68c`, [libraries against game code](../formats/compiler.md#libraries-against-game-code)). ES2's counterparts are in the [functions](#functions) table.

## Starting — `Sim_Main` (`00059350`)

(argc, argv), called by the startup ([startup](../formats/compiler.md#startup)). In order:

1. `Mem_InitHeap`(6,000,000) ([who makes the heap](memory.md#who-makes-the-heap)).
2. `Sim_PreParseCommandLine` (`00058e30`)(argv, argc): `-c` anywhere on the line sets `Sim_AltDriveAllowed` (`000b5bce`).
3. **The drive prefix.** `ALT_DRIVE`'s value, taken as null unless `-c` was given, when the variable `PREFER_ALT` is set, and the empty string otherwise, is copied to `000b5d9c` and passed to `00056ecc`. `Path_ApplyAltDrive` (`000592a4`) puts that prefix before every path it builds, less the path's own drive letter. With `PREFER_ALT` set and no `-c` the copy reads from a null pointer.
4. It writes the heap's size, 4 bytes, to `data\mem.got` and an `int16` 0 to `data\exit.cfg`, both through `Path_ApplyAltDrive`, asserting at severity 5 (lines `0xf1`, `0xf9`) when either does not open. So `exit.cfg` holds 0 after a run that ends before [the end](#the-end--sim_shutdown-0005b568) writes its code, as step 6's refusal and a fatal assert ([a fatal assert in a mission](asserts.md#a-fatal-assert-in-a-mission)) do.
5. `Sim_ParseCommandLine` (`00058e74`)(argv, argc), [below](#the-command-line).
6. Without `-eggplant` (`Sim_EggplantGiven`, `000b5bc4`) it closes the tape and `.dmp` files, turns the heap off, prints `To play EarthSiege, type ES.` and exits with status 1.
7. Subsystem phase 0; `VolumeGroup_SetBufferSize`(`0x200`); `VolRStream_SetGroup` with mask 1, extension `.vol`, the directory `ALT_DRIVE` (when `-c` was given) and `dirFirst` set when `PREFER_ALT` is ([the volume group](../formats/streams.md#the-volume-group)); subsystem phase 1.
8. The memory checks [`memory.md`](memory.md#who-makes-the-heap) lists, the first printing `You need %ld more bytes free to run...` with the shortfall (line `0x136`).
9. `Sim_StartMission` (`0005a9dc`), subsystem phase 3, and the frame loop ([the frame](#the-frame)).
10. After the loop, `Sim_Shutdown` (`0005b568`), `Mem_HeapOff`, and with `-eggplant` given and the quit flag clear, bit 0 of the byte `SimDisplay` (`000b5b5c`) points at cleared.

**`Sim_Main` returns no value, so the program's exit status is not the exit code.** Nothing after the loop sets `EAX` (`00059627`–`00059654`; `Mem_HeapOff` (`00001598`) only clears `Mem_UseHeap` (`000901d4`)), and the startup passes whatever `EAX` holds to `exit` (`00000173`, [startup](../formats/compiler.md#startup)): what `Sim_Shutdown`'s calls left there. The exit code reaches the shell through `data\exit.cfg` alone ([the end](#the-end--sim_shutdown-0005b568)).

Where ES2's `Sim_Run` differs: it reads `data\drive.cfg` instead of the environment, refuses without `-eggplant` in a message box, checks physical memory, after the loop saves the full-screen option and deletes the tape's two configuration files ([ES2's start](../../retail/simulation/main-loop.md#starting)), and its `WinMain` returns the exit code ([exit codes](../../retail/command-line.md#exit-codes)).

### The command line

`Sim_ParseCommandLine` (`00058e74`), (argv, argc), looks each argument that starts with `-` up by its next character in the 16 dwords at `00096e88` (`REPNE SCASD`) and jumps to the target 16 dwords after: `B` `00058ec8`, `D` `00058ed6`, `S` `00058f40`, `m` `00058f4c`, `C` `00058f58`, `s` `00058f67`, `P` `00058f74`, `t` `00058f80`, `V` `00058f98`, `W` `00058fb0`, `T` `00058fc8`, `p` `00058fe0`, `r` `000590cb`, `d` `0005918d`, `l` `000591d7` and `e` `000591e2`. Ghidra has not disassembled the case bodies (`00058ec8`–`0005921c`). Case-sensitive, and no `/`:

| Switch | Store | Effect |
|---|---|---|
| `-eggplant` or `-EGGPLANT` | `Sim_EggplantGiven` (`000b5bc4`) = 1 | Without it the simulator refuses ([above](#starting--sim_main-00059350)) |
| `-c` | `Sim_AltDriveAllowed` (`000b5bce`), by `Sim_PreParseCommandLine` | `ALT_DRIVE` is honored |
| `-B` | `000b5bcc` = 1 | `Sim_DebugBreak` (`00059658`) runs `INT3` while it is set |
| `-D` | `TapePlayback` (`000b5b76`), `DemoMode` (`000b5bd0`) = 1 | Plays `tapes\<name>.tap`, the name from `DemoTape_PickRandom` (`0005771c`) ([the demo tape](../formats/tap-input-tape.md#the-demo-tape)), which replaces any name given before, through `Config_BuildPath`; both flags go back to 0 when the pick is null or the tape does not open |
| `-S` | `000b5b67` = 1 | The page numbers [`Sim_StartMission`](#the-mission-start--sim_startmission-0005a9dc) gives the display |
| `-m` | `000bda94` = 1 | `Sim_StartMission` clears the monochrome monitor (`Mono_Clear`) |
| `-C<name>` | `CockpitChassis_SelectByName` (`00024394`)(name) | The cockpit's chassis ([the cockpit's scripts](../formats/ttm-scripts.md#the-simulator)) |
| `-s` | `Sim_SoundSwitch` (`000b5b68`) toggled from 1 | Cleared, `Sim_StartMission` passes `Sound_Init` digital device 0, so no sound manager is made ([the driver](../formats/sound-samples.md#the-driver)) |
| `-P` | `000b5b69` = 1 | No reader found by `es2_xref.py` besides this store ([Open](#open)) |
| `-t<n>` | `Sim_SetCommBox0PilotOverride` (`0003ea50`)(`atol`(n)) | The pilot in the first squad comm box ([who is in it](heads-down-display.md#who-is-in-it--hddgauge_loadpilotframes-0003dab4)) |
| `-V<n>`, `-W<n>`, `-T<n>` | `000b5bb2`, `000b5bb4`, `000b5bb6` | Set to -1 by `Main_StaticInit` (`00057414`); no other reader found ([Open](#open)) |
| `-p<name>` | `TapePlayback` = 1, whether or not the tape opens | Plays `<name>.tap` from the current folder, the name taken only when none was given before; its open is not tested ([the bundle](../formats/tap-input-tape.md#the-bundle)). A tape that runs out, or `Ctrl+E`, ends the mission, as under `-D` ([recording and playback](input.md#recording-and-playback)) |
| `-r<name>` | `TapeRecording` (`000b5b78`) = 1 | Records to `<name>.tap`, the name taken only when none was given before; its open is not tested ([the bundle](../formats/tap-input-tape.md#the-bundle)) |
| `-d` | `DmpFile` (`000b5b7e`) | Opens `<name>.dmp`, mode `wb` while recording (and closes it again) and `rb` otherwise; the name is the tape's ([the checkpoint file](../formats/tap-input-tape.md#the-checkpoint-file)) |
| `-l` | `g_LowMemory` (`000bda98`) = 1 | The low-memory mode ([low memory](../formats/chassis-dat.md#low-memory)) |

Where ES2's parsers differ ([ES2's switches](../../retail/command-line.md#dbsim)): ES2's `-c` stores a word, `+0x72` of its block, where ES1's honors `ALT_DRIVE`; ES2 has `-v`, `-Z`, `-X`, `-b`, `-a`, `-R`, `-E`/`-F`/`-G` and `-SPRUNKNOWN`, which ES1's lacks; ES2's `-l` sets `CockpitArt_LoadOnDemand` where ES1's sets the low-memory mode.

## The mission start — `Sim_StartMission` (`0005a9dc`)

`Sim_Main` calls it once. ES2's counterpart puts up a loading picture first ([ES2's](../../retail/simulation/mission-loading.md#showing-it)); ES1's shows nothing until the first frame. In order:

1. Adds `Sim_AssertCleanupHook` (`0005a7b8`) to the assert hooks with `Assert_AddHook` (`0005730c`), as ES2's does ([asserts](asserts.md#severities)). The hook runs `Sound_Shutdown`, `0006a5fc` on the current display (`000bdd28`) when there is one, closes the tape when `TapeFile` is set and then, inside that test, the `.dmp` file when `DmpFile` is set, runs `GndTex_Free`, and writes an `int16` 0 to `data\exit.cfg` through `Path_ApplyAltDrive` (`init.cpp` line 100, severity 5 when it does not open) ([a fatal assert in a mission](asserts.md#a-fatal-assert-in-a-mission)).
2. `Input_BuildSourceTable` ([setting up](input.md#setting-up--input_buildsourcetable-0004c95c)); `Sim_Timer` (`000b5baa`) = `DVTimer_Ctor` (`0006c6f8`)(0, `0x26d3`), the PC timer at 120 interrupts a second ([the rate](timer.md#the-rate)).
3. **Sound.** With `Sim_SoundSwitch` (`000b5b68`) set it reads `resource.cfg` (through `Path_ApplyAltDrive`) with `Profile_GetString` (`000511a4`) and `Profile_GetInt` (`00050ffc`) ([profile files](../formats/profile-files.md)): `[SoundFX]`'s `Driver#`, `IRQ`, `DMA` and `PortAddress` and `[Music]`'s `Driver#` and `PortAddress`, the hexadecimal ones through `sscanf` (`0006c871`) with `%X`, and passes them to `Sound_Init`; with it clear, digital device 0 ([the driver](../formats/sound-samples.md#the-driver)). In `Sound_Init`'s stack order they are `[Music]` `PortAddress`, `[SoundFX]` `PortAddress`, `DMA`, `IRQ`, `[Music]` `Driver#` and `[SoundFX]` `Driver#`. `IRQ` and `DMA` come from `Profile_GetInt` with a default of 0; the other four are read with `Profile_GetString` into a 25-byte buffer, its default argument 0. Only `[SoundFX]` `Driver#`'s text is tested: an empty one gives 0. The other three are written only through `sscanf`'s pointers to them in this function, so an empty `PortAddress` or `[Music]` `Driver#` passes on what the stack held ([Open](#open)). Then `Snc_MissionInit` (`0004e498`), which starts the lip-sync player's clock ([playing a line](../formats/snc-lip-sync.md#playing-a-line)), and with no sound manager (`Sound_Manager`, `000959ac`) `Prefs_SetOption`(1, 0, *i*) for options 0, 1 and 2.
4. `Light_ResetSystem`, `00075858`, `00066e00`, `Widget_LoadStylesAndFonts`; the two hooks `AlertPanel_EnterHook` (`00094d80`) and `AlertPanel_LeaveHook` (`00094d84`) = `SimInput_PushDisable` (`0005a5b4`) and `SimInput_PopEnable` (`0005a5d8`) ([small functions](#small-functions)); `Mono_Clear` with `-m`.
5. Subsystem phase 2; the mouse cursor from `Cursor_LoadCursorResource` (`00057640`), ES2's (`0045ce30`, to which `es2_xref.py` finds no reference in ES2): the class item `ClassItem_LoadResource` reads from a `VolRStream` on `dci\cursor`, the path `ResourcePath_BuildFolderName`(`dci`, `cursor`) builds; `DBSim_LoadScriptDat`; `ObjList_Add` of each `Zone_ShapeObjects` entry up to `Zone_ShapeCount` or the first null; `CockpitMouse_Init` (`000441cc`, [the cockpit's mouse](cockpit-mouse.md#the-callback-and-the-queue)); `CockpitMouseLive` (`000bd512`) = 0; `Time_InitSimClock` (`00063e64`, [the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)).
6. `DBSim_SpawnMissionObjects` ([the second pass](../formats/script-dat.md#the-second-pass)) with option 1 set to 0 around it (`Prefs_SetOption`(1, 0, 1), then `Prefs_RevertOption`(1, 1)).
7. Deletes every explosion (slot `+0xc` with 3, then `ExplosionPool_FlushDeletes`, `EffectLightPool_FlushDeletes` and `FlatObjPool_FlushDeletes` (`00006d50`) after each) and every debris piece (`DebrisPool_FlushDeletes` after each), as ES2's empties both pools.
8. `Light_CreateMissionSun` ([the sun](../rendering/polygon-shading.md)); `ObjList_ZoneShapeCount` = `ObjList_Count`.
9. **The player.** `LocalPlayerMech` (`000b5b86`) = the last machine of `g_MechPool` (`000af68c`) with `+0x9d`, the locally piloted byte, set; `ViewChain_Viewed` (`000bda9a`) = that machine, and `ViewChain_Chosen` (`000b5bbc`) too.
10. **The squad cameras.** One `0x4a`-byte camera for each member of the player's group (count `+0x12` of the group `+0x3f`), into the array at `000bda9e`: `TSCamera_CtorDefault`, the class's vtable `000972de` at `+0x12` and `+0x32`, `+0x36`, `+0x44`, `+0x46` and `+0x48` zeroed. ES2 builds its squad cameras at the same point.
11. **The display.** `SimDisplay` = `Sim_CreateDisplay` (`0005a5f4`); `GLDisplay_SetAttribute` (`0006a824`)(display, 640, 2), a screen 640 pixels wide; page origins 1 to 3 at `+0x50`–`+0x64` = (0, 200), (320, 0), (320, 200), each passed to the driver's routine at `0009df10`; `+0x88` = 0, `+0x8c` and the draw page `+0x3c` = 2 (0 with `-S`), the visible page `+0x34` = `+0x88`, and without `-S` `+0x3c` = 3, each passed to the driver while the display is current ([the video memory](../rendering/screen-pages.md#the-video-memory)).
12. **The main camera.** `ViewObjectPtr` (`000b5b8a`) = a `0x4a`-byte `TSCamera_Ctor`(camera, (0, 0, 0), (0, −6000, 0), `0x100`, 8, the render context `000b5b6e`), retyped and zeroed as the squad cameras; `Cam_AttachTo` (`0005923c`)(`ViewChain_Viewed`); mode `+0x36` = 2. ES2's main view takes a focal shift of 9 when `004d30c4` is `0x4b1` or more ([the views' focal length](../../retail/simulation/cockpit-views.md#the-views-focal-length)); ES1's is always 8.
13. **The cockpit.** `000b5bca` = 1 (the bring-up, under which `Widget_DrawToCockpit` copies nothing); `CockpitViewManagerInstance` = `CockpitViewManager_Ctor`(0, player), `CockpitViewManager_Publish`; `WeaponMounts_BuildGauges` on the player's mounts; `Cockpit_LoadSquadmatePilots`; `+0x194` = `PaperDoll_BuildWeaponIcons`(machine) for every machine of `g_MechPool`; `Cursor_SetImage` with the cursor of step 5, as the bitmap its dword `+4` plus 4 (0 when that dword is 0) and the hotspot at its `+8`, which draws nothing while the shown flag is still 0, its value in the image ([the cursor](../rendering/screen-pages.md#the-cursor)); `Player_PerFrameCockpitUpdate`, `Cam_Update`; the draw page again; `CockpitView_SetView` with view 1 and then 0, as ES2's ([both canopies resident](../../retail/simulation/cockpit-views.md)).
14. **The lift.** `LiftStart_Pending` (`000b5bb0`) = 1 when a structure of `g_BasePool` whose group (`+0x3f`) has side byte `+0x14` 0, the player's side, lies within 40000 of the player ([when it runs](lift-start.md#when-it-runs)). Then it calls `LiftStart_DarkenPalette` (`00057cf4`); otherwise it sets `PitchGradient_ForcedStep` (`000928ee`) = 8 and runs `Palette_BlendRangeByPitch`, `Palette_FlushDirtyRange` and a first `Sim_RenderFrame`. ES2's runs the same search over its structures (side byte `+0x12`) at the same point and discards the answer: its two lift calls, `LiftStart_DarkenPalette` here and `LiftStart_Rise` at the end, test its block's `+0x54` instead ([the lift start](../../retail/simulation/mission-deployment.md#the-lift-start)).
15. `000b5bca` = 0; `Palette_FlushDirtyRange`; `Widget_DrawToCockpit`(0, {0, 0, 320, 400}, 1), the whole of page `+0x8c` onto `+0x88`; the driver's routine at `0009df4c`, slot 30, `VGA8_ShowCursor`, which draws the cursor for the first time ([the cursor](../rendering/screen-pages.md#the-cursor)); `000b5b72` and `000b5b74` = option 6 (`000bd568`) and `ShapeDetail_ApplyDetailSetting`(option 6), the detail setting `Sim_RenderFrame` applies again when the option changes ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)); `000b5b96` = option 5 above 240 ([the ground](../rendering/ground-and-sky.md)); `Sound_SetMusicEnabled`(1) ([turning songs and sounds off](../formats/sound-samples.md#turning-songs-and-sounds-off)); with the lift, `LiftStart_Rise` (`00057ffc`, [the ride](lift-start.md#the-ride--liftstart_rise-00057ffc)); unless a tape plays, `CockpitMouseLive` = 1; `Sound_Play`(`0x13`).

`Sim_CreateDisplay` builds the display's two four-element arrays with `DisplayPageOrigin_ElementCtor` (`0005a79c`), (p): p, or `Mem_New`(8) when p is null.

## The frame

Each pass of `Sim_Main`'s loop:

1. Subsystem phase 4 ([the phases](#the-subsystem-phases)); `Sim_FrameSoundTick` (`00058574`, [the frame's sounds](#the-frames-sounds--sim_framesoundtick-00058574)); `Time_BeginSimTick` (`00063f40`) unless `TapePlayback` is set ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)).
2. It leaves the loop when `DemoAbort` (`000b5bd2`) is set or `Sim_MainTick` ([the tick](#the-tick--sim_maintick-00059700)) answers non-zero.
3. `Cam_Update`(`ViewObjectPtr`) ([`Cam_Update`](external-views.md#cam_update)).
4. `Sim_RenderFrame` (`00059c08`, [the draw](../rendering/ground-and-sky.md)), skipped while a tape plays with the simulation frozen. In order: subsystem phase 5; the detail setting when option 6 has changed; the view shake's offset, {0, total} from `CockpitView_GetShakeOffset` (`000249d4`, [the view shake](cockpit-views.md#the-view-shake)), read once into two locals and added to the render context `000b5b6e`'s origin `+0x220`/`+0x224`; `ObjList_RebuildFrame`; `Cockpit_DrawFrame`(`CockpitViewInstance`) every frame, whatever the view ([drawing it](cockpit-panel.md#drawing-it)); the 3D pass, the projection, the terrain and the frame's objects, unless the view manager's view `+0x10` is 1 with no change staged (`+0x18`); `Player_PerFrameCockpitUpdate`(`LocalPlayerMech`); slot `+0xc` of the gunsight `+0x1f1`; slot `+0x10` of the pilot channel, `PilotMessagePort_Update` ([the pilot channel](cockpit-messages.md#the-pilot-channel)); and the same two locals subtracted, so a shake step `Cockpit_DrawFrame` takes during the frame ([the hit shake](cockpit-panel.md#the-hit-shake)) leaves the origin where it was before the frame.
5. `Sim_EndFrame` (`00059b40`, [the frame's end](cockpit-views.md#cockpitview_stepviewtransition-00022904)), which ends with `Mem_TrackPeakUse` (`00059288`): `Mem_PeakUse` (`00096f08`) = the larger of itself and the heap's size less its bytes free (`000ae4b0` − `000ae4b8`, the heap object's `+4` and `+0xc`, [the heap](memory.md#the-heap)).
6. Subsystem phase 6: `Vcr_FlushCheckpoints` ([the phases](#the-subsystem-phases)).

ES2's loop also counts the frames, sleeps while the window has lost the focus, and draws every frame ([ES2's frame](../../retail/simulation/main-loop.md#the-frame)).

### The subsystem phases

`RegisterSubsystemLoader` (`00000ac4`), (phase, function), appends a 6-byte {function, `int16` phase} record to the table at `000bbcb0`, its count the `int16` at `0009011c`; `Subsystem_RunPhase` (`00000af4`), (phase), calls each function registered at that phase in table order, as ES2's (`00401d94`). `Sim_Main` runs phases 0 and 1 around the volume group, 3 once after `Sim_StartMission`, and 4 and 6 each frame; `Sim_StartMission` runs 2 and `Sim_RenderFrame` 5.

`es2_xref.py` finds 26 calls of `RegisterSubsystemLoader`, each in a static initializer that pushes the function and the phase just before the call, and every access `es2_xref.py --decode` finds to the table and its count lies in the two functions above. Of the 26:

- 16 register at phase 2, the resource loads (for one, [the weapons' load](../formats/weapons-dat-sim.md));
- 9 at phase 5, the pools' delete flushes (for one, [the explosions'](../formats/explos-dat.md));
- one at phase 6: `Vcr_StaticInit` (`00000d3c`, the static constructor table's entry `000adbb4`) registers `Vcr_FlushCheckpoints` (`00000c84`), [the checkpoint file](../formats/tap-input-tape.md#the-checkpoint-file)'s flush;
- none at phase 0, 1, 3 or 4, so those four runs call none of the functions found ([Open](#open)).

### The frame's sounds — `Sim_FrameSoundTick` (`00058574`)

No arguments; ES2's frame has no counterpart. In order:

1. `Music_Tick` ([the music](#the-music--music_tick-000585dc)).
2. **Sound `0x2d`.** In the view manager's view 4 (its `+0x10`), the outside view, with `0x2d` playing, `Sound_Stop`(`0x2d`); in any other view, with neither `0x2d` nor `0x13`, the sound `Sim_StartMission` plays last, playing, `Sound_Play`(`0x2d`). ES2's power-up plays its engine hum `0x2d` with the start-up sequence `0x13` and loops the hum all mission ([the cockpit power-up](../../retail/simulation/audio.md#the-cockpit-power-up)); its outside view sets the hum's volume to 100 ([the external view's step](cockpit-views.md#cockpitview_stepviewtransition-00022904)).
3. `Sound_UpdateListener` (`0004df98`)(`ViewObjectPtr`), which keeps the camera as the listener, places the sounds offered this frame and on every tenth call fades and pans every placed sound ([placed sounds](../formats/sound-samples.md#placed-sounds)).

#### The music — `Music_Tick` (`000585dc`)

**One of three calm songs, picked at random, gives way to its battle song when an enemy machine is in sight or close, and the battle song gives way back when none is and no flyer is far off.** No arguments. It does nothing without a sound manager or with preferences option 0 (`000bd562`) clear. A song is a catalog row ([the catalog](../formats/sound-samples.md#the-simulators-catalog)): a calm one *s* from 0 to 2, its battle one *s* + 3. *Now* is the clock `000981c4`, 60 a second ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)), so 300 is 5 seconds. In order:

1. **The first call**, `Music_Song` (`00096d0c`) still −1: `Music_Song` = `Math_RandomBelow`(`000b5c48`, 3); `Sound_Preload`(song); `Music_SongObject` (`00096d14`) = `SoundCatalog_Objects` (`000bd63c`)[song]; `Music_Battle` (`00096d0e`) = 0; `Music_NextCheck` (`00096d10`) = now + 300.
2. **The switch**, while `Music_Switching` (`00096d18`) is set: when the song's volume (`SoundObject_GetVolume`, `0005049c`, [playing](../formats/sound-samples.md#playing)) is not 0, it sets the volume 15 lower through the object's slot `+0x2c` and returns. `MidiSound_SetVolume`, that slot of a song, keeps the value between 0 and 100 ([the vtables](../formats/sound-samples.md#the-vtables)), so a song at 100 fades out over seven frames. At 0: `Sound_Stop`(song); `Music_Song` + 3 while `Music_Battle` is set, − 3 while it is clear; `Sound_Preload`; the new object at volume 100; `Music_Switching` = 0.
3. **The check**, once `Music_NextCheck` is less than now. An object of `LiveObjectList` is seen when it is neither disabled (`+0x9e`) nor destroyed (`+0x93`), its group (`+0x3f`) has no action (`+0x16`) and side byte `+0x14` 1, and either `TargetSelect_CanTarget` and `TargetSelect_InForwardCone` both answer non-zero for it from `CockpitViewInstance` or it lies within 25000 of the player. In the battle song the first seen ends the call. In the calm song the first seen sets `Music_Switching` and `Music_Battle`, `Music_NextCheck` = now + 300, and ends the walk. In the battle song with none seen it sets `Music_Switching` and walks `g_FlyerPool` oldest first: the first flyer neither disabled nor destroyed and more than `0x49f0` (18928) from the player, of either side, clears it again. With it still set, `Music_Battle` = 0 and `Music_NextCheck` = now + 300.
4. When the song is not playing, `Sound_Play`(song), so a song that ends starts again.

`es2_xref.py` finds calls of `Music_Tick` from `Sim_FrameSoundTick` and from `Prefs_ApplyMusicOption` (`0004ad0c`, preferences option 0's handler, [the option array](preferences.md#the-option-array)), (on), which calls `Sound_SetMusicEnabled` (`0004e294`)(on) ([turning songs and sounds off](../formats/sound-samples.md#turning-songs-and-sounds-off)) and then, with *on* not 0 and the byte `000b1734` clear, `Music_Tick` ([Open](#open)).

ES2 keeps ten music rows in its catalog but plays the CD ([ids 0-9 are music](../../retail/simulation/audio.md#ids-0-9-are-music)).

## The tick — `Sim_MainTick` (`00059700`)

No arguments. In order:

1. The input block = `Input_ReadPlayerInput` ([the input read](input.md#reading-the-players-input--input_readplayerinput-0004cb14)); during playback `SimTickDelta` = the block's `+0x29`, the frame's recorded delta. The test (`00059711`) follows the read, so on the frame that ends a tape, whose build has cleared `TapePlayback`, `SimTickDelta` keeps the last frame's delta, `Time_BeginSimTick` not having run on that pass. `SimTickDelta2`, which no tape carries, stays at `Time_InitSimClock`'s `0x100` through a playback ([the tick length](fixed-point-math.md#the-tick-length--time_beginsimtick-00063f40)).
2. Unless `Sim_Frozen` (`000b5b92`) is set, each pool walked oldest first with `Pool_Prev` from 0 ([object pools](memory.md#object-pools)), so a pool's walk also reaches what the walks before it, or its own elements' ticks, add to it on this tick, and steps on from an element it destroys:
   - every structure of `g_BasePool`, through slot `+0x1c`, whatever its group;
   - the explosions (`g_ExplosionPool`, `Explosion_TickUpdate` (`00005d74`), [the impact effects](impact-effects.md)), the smoke balls (`g_SmokeBallPool` (`000aef68`), `Effect_DriftAndAnimate` (`0000693c`)), the smoke emitters (`g_SmokePool` (`000aefd4`), `Smoke_Tick` (`00006b28`)) and the debris (`g_DebrisPool` (`000aeef8`), `Debris_TickUpdate` (`000064b8`), [the wreckage and smoke](destruction-effects.md)), an element whose tick answers non-zero destroyed at once through slot `+0xc` with 3;
   - `maybe_WeaponChain_EoHold` (`000b5bc6`) = 0;
   - the projectiles (`g_ProjectilePool`, `000af0b4`) through slot `+0x18`, destroyed the same way;
   - the groups, in order: one with no action (`+0x16`) runs `Group_TickMembers` ([the tick](mission-groups.md#the-tick)); one whose action has fired (its `+0xa` set) has the action cleared, so its members tick from the next tick on.
3. `Sim_StepOnce` (`000b5b9c`), when set, is cleared and the freeze set.
4. `Sim_PollPlayerInput` ([below](#the-players-controls--sim_pollplayerinput-0005a0c0)); a non-zero answer ends the tick with 1.
5. `Actions_EvaluateTriggers`(`LocalPlayerMech`) ([trigger areas](world.md#trigger-areas--actions_evaluatetriggers-0001f87c)), frozen or not, and `ViewChain_Apply` (`0005894c`, [the views](#the-views--viewchain_apply-0005894c)).
6. Unless frozen: `Sim_DetectionTick` once ([the sensor model](sensor-model.md)), then `Mech_PerTickSystemsUpdate` ([the systems tick](mech.md#the-systems-tick--mech_perticksystemsupdate-00014ee0)) for each machine of `g_MechPool`, oldest first, whose group has no action and that is not destroyed (`+0x93`).
7. `Mission_PollStatus`(`LocalPlayerMech`) ([the poll](mission-status.md#the-poll--mission_pollstatus-0000f454)), destroyed or not. Status 2 runs [the death flash](#the-death-flash--sim_deathflash-00058414) and ends the tick with 1. Another status of 0 or more puts up the status panel `StatusAlertPanel_Ctor` (`00046efc`)(0, 0, 1, status) ([the status panel](alert-panels.md#the-status-panel--statusalertpanel_ctor-00046efc)) after, for status 3, `Group_ApplyOutnumberedDamage` on the player's group ([going out of the fight](mission-groups.md#going-out-of-the-fight)), which shows status `0x12` instead when it destroys the player; it runs the panel through slot `+0xc` of its `+0x314`, ends the tick with 1 when the answer equals `Mission_EndingAnswer` (`00097158`)[the poll's status], 3 for a panel showing `0x12` ([which answer ends the mission](#which-answer-ends-the-mission)), and destroys the panel with `Alert_Dtor` (`00045d0c`)(panel, 3).
8. Otherwise 0.

Where ES2's differs ([ES2's tick](../../retail/simulation/main-loop.md#the-tick)): ES2's ticks the effect pools first, the projectiles next and only the structures whose group has arrived, and queues a finished effect for deletion; it runs its action timers and gates the triggers on the freeze; and with the player destroyed it runs the death camera in place of the poll, where ES1's polls and runs the death flash for status 2.

### Which answer ends the mission

**A panel the mission raises ends the mission when the player picks the button that leaves it: `RETURN TO BASE`, `ABORT MISSION` or `QUIT ANYWAY` where there are two, the one `CONTINUE` where the status already decides the end, and never for the boundary warning.** The panel answers 0 for its left button and 1 for its right; a one-button panel answers 0 for its button and for [Esc] ([the status panel](alert-panels.md#the-status-panel--statusalertpanel_ctor-00046efc)). `Mission_EndingAnswer` (`00097158`) holds the answer that ends the mission, an `int16` for each status from 0 to `0x12`: 1, 1, 0, 0, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0. For the statuses the poll raises ([the poll](mission-status.md#the-poll--mission_pollstatus-0000f454)):

| Status | Buttons ([what each status shows](alert-panels.md#what-each-status-shows)) | Its word | The mission ends on |
|---|---|---|---|
| 2, destroyed | none here: the death flash's panel, status 2's one `CONTINUE` | 0 | any answer, the death flash's answer being unread |
| 3, immobilised, and `0x12`, captured | `CONTINUE` | 0, status 3's for both | any answer |
| 6, failed | `CONTINUE`, `RETURN TO BASE` | 1 | `RETURN TO BASE` |
| 7, nearing the edge | `CONTINUE` | 1 | no answer: the mission goes on |
| 8, the area left | `CONTINUE` | 0 | any answer |
| 9, complete | `CONTINUE`, `RETURN TO BASE` | 1 | `RETURN TO BASE` |

`Q`'s case reads the same table for the status `Mission_StatusForAlert` gives it ([commands](#commands--sim_dispatchcommand-00059df8)), so its panel ends the mission on `ABORT MISSION` for 4 and 10, on `QUIT ANYWAY` for 11 to 16 and on `RETURN TO BASE` for 6 and 9, and on any answer for 2 and 3: it runs neither `Group_ApplyOutnumberedDamage` nor the death flash, so `Q` pressed with the HERC immobilised, or destroyed before the poll has seen it, ends the mission at once. Statuses 0, 1, 5 and `0x11` reach neither reader: `P` and `Ctrl+Q` take their panel's answer as it is, `Mission_Status` gives no 5, and `0x11` is `Prefs_LimitDetailInLowMemory`'s ([the detail setting](object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08)). `es2_xref.py --decode` finds the table read at those two places, `00059a7c` and `00059fbe`, each indexed by the status ([Open](#open)).

### The death flash — `Sim_DeathFlash` (`00058414`)

**Once the poll finds the player's HERC destroyed, nothing more moves: the view shakes for half a second, the screen jumps to one palette and fades through a second to black, the `YOU HAVE BEEN KILLED.` panel comes up on a black screen, and whatever is pressed the mission ends.** No arguments. It is ES2's `Sim_DeathFlash` (`0045dc34`, [the death flash](../../retail/rendering/cockpit-canopy-palette.md#interpolating-colors)), to which `es2_xref.py` finds no branch or stored pointer in DBSIM. `es2_xref.py` finds one call, from step 7 of the tick. The poll answers 2 at its first ask after the destruction, with no alert delay, and it asks once every 10000 counts, so the flash starts up to 3.9 seconds after the HERC is destroyed, the tick running as usual until then ([the poll](mission-status.md#the-poll--mission_pollstatus-0000f454)). Its clock is `000981c4`, 60 counts a second. In order:

1. Loads `dpl\death1`, `dpl\death2` and `dpl\world0` with `Palette_LoadDpl` (`00063584`), each path from `ResourcePath_BuildFolderName` with the folder name `000980d0` points at, `dpl`; the simulator's archives hold all three (2408, 2408 and 2388 bytes, read through `HercWorks.Core`), where ES2 ships no `death1` or `death2` ([ES2's](../../retail/rendering/cockpit-canopy-palette.md#interpolating-colors)).
2. `Sound_StopDigitalSounds` (`0004e34c`), which stops the playing digital sounds ([turning songs and sounds off](../formats/sound-samples.md#turning-songs-and-sounds-off)). The song plays on, and `Music_Tick` does not run during the flash.
3. The shake's end, the clock + 30, half a second; `CockpitView_SetShakeBand`(10) ([the view shake](cockpit-views.md#the-view-shake)); `Sound_Play`(`0x26`).
4. `death1`'s first 255 colors into the live palette from entry 0 (`Palette_InstallRange`, [installing colors](../rendering/palette.md#installing-colors)), then a cross fade from `death1` to `death2` over 120 counts ([the palette module](../../retail/rendering/cockpit-canopy-palette.md#palette-module)). Each pass while `Palette_StepCrossFade` answers more than 20, so about 100 counts, 1.7 seconds: `Sim_EndFrame`; while the clock is below the shake's end, `CockpitView_StepShake`(`Math_RandomBelow`(`000b5c48`, 10)); `Sim_RenderFrame`.
5. A cross fade from `death2` to black over 120 counts, 2 seconds, each pass while `Palette_StepCrossFade` answers non-zero: `Sim_EndFrame`, `Sim_RenderFrame`. The first fade's last step leaves the colors up to a sixth short of `death2`, so this one starts with a small jump.
6. `VGA8_ClearPage` ([the driver's](../rendering/screen-pages.md#lines-and-clipped-bitmaps)) with color 0 on the display's (`000bdd28`) `+0x88` page and then its `+0x8c`, through the driver's slot 35 (`0009df60`); `world0`'s first 255 colors installed the same way; `Sim_EndFrame`, which sends them and flips the two black pages.
7. The status panel, `StatusAlertPanel_Ctor`(0, 0, 0, 2): status 2's `YOU HAVE BEEN KILLED.` with one `CONTINUE` ([what each status shows](alert-panels.md#what-each-status-shows)), with *keepsScreen* 0, so it keeps no copy of the screen under it; run through slot `+0xc` of its `+0x314`, its answer unread, and destroyed with `GeneralAlert_Dtor`(panel, 3).

So steps 4 and 5 draw about 3.7 seconds of frames with no tick and no input read: the world and the cockpit are drawn as they stood. In the outside view (the view manager's view 4) `CockpitView_SetShakeBand` and `CockpitView_StepShake` do nothing, so it does not shake. The tick then ends with 1, and `Sim_Main` leaves the loop for [the end](#the-end--sim_shutdown-0005b568).

Where ES2's differs: it calls `CockpitView_ClearShake` before setting the band, sets its freeze (`004d2576`) to 1 before the first fade, where ES1's leaves `Sim_Frozen` as it is, mutes the effects where ES1's stops the digital sounds, and reads its clock through `Time_GetCoarseTicks`. ES2's tick runs [the player-death camera](../../retail/simulation/external-views.md#the-player-death-camera) in place of the poll and raises the status-2 alert when its countdown runs out, so ES2's player watches the wreck from outside where ES1's sees the view that was up flash and go black.

### The views — `ViewChain_Apply` (`0005894c`)

No arguments; ES2's ([moving between views](../../retail/simulation/external-views.md#moving-between-views--viewchain_apply)), on the view manager's step flag `+0x18` (`CockpitViewManagerPublished`), which `CockpitView_ProcessViewCommand` sets when it stages a command and `CockpitView_StepViewTransition` clears ([switching views](cockpit-views.md#cockpitview_processviewcommand-000226f0)). `ViewChain_CameraControlPreferred` is `000b5bb8`, 1 from `Main_StaticInit`. Against ES2's, it has no view 4, ES1 having no chase view; view 2 views the player alone, ES1 having no `Ctrl+F`; and it sets the camera's mode with no lock test, the camera having no lock ([the camera](external-views.md#the-camera)). In full:

1. **No change staged** (`+0x18` clear): in view 3 (`ViewChain_View`) with `ViewChain_Viewed` the player (`LocalPlayerMech`), `InputDrivesCamera` = `ViewChain_CameraControlPreferred`.
2. **A change staged**, with `ViewChain_PendingView` not `ViewChain_View`: `ViewChain_View` = the pending view, and then for view 1 the main camera's (`ViewObjectPtr`) mode `+0x36` = 0 and `InputDrivesCamera` = `ViewChain_CameraControlPreferred`; for view 2 `ViewChain_ViewObject`(`LocalPlayerMech`), mode 2 and `InputDrivesCamera` = 0; for view 3 `ViewChain_ViewObject`(`ViewChain_Chosen`), mode 1 and `ViewChain_CaptionFrames` = 2. A change staged with the pending view already current does nothing here.
3. **After either**, when `ViewChain_CaptionFrames` is not 0, `ViewChain_DrawCaption` and 1 off the count, so a count of 2 draws the caption on this tick and the next.

**`ViewChain_ViewObject` (`00058a44`)**, (object), is ES2's ([one camera per squadmate](../../retail/simulation/external-views.md#one-camera-per-squadmate--viewchain_viewobject)) on the squad cameras (`000bda9e`, their count the player's group's `+0x12`) and the spare `ViewChain_SpareCamera` (`000b5d38`). It builds the spare on its first call (the guard `000b5d34`): `TSCamera_CtorDefault`, the vtable `00096c5a` at `+0x12`, and `+0x32`, `+0x36`, `+0x44`, `+0x46` and `+0x48` zeroed. It walks the squad cameras in order and stops at the first that either already follows the object (`+0x32`) or follows none: one on the object becomes the main camera (`ViewObjectPtr`) with its mode `+0x36` set to 1; a free one is first made a copy of the main camera with `TSCamera_CopyFrom`, then becomes the main camera and is attached with `Cam_AttachTo`(object). A camera on the object after a free one in the array is not reached. With no camera found it does the same to the spare as to a free one. Then `ViewChain_Viewed` = the object, and `InputDrivesCamera` = 1 for an object other than the player and `ViewChain_CameraControlPreferred` for the player. `ViewChain_Apply`'s two calls enter it through the three `NOP`s at `00058a41`.

**`TSCamera_CopyFrom` (`00057af8`)**, (camera, from), copies every field but the vptr `+0x12`–`+0x15`, where ES2's `ViewChain_ViewObject` copies the fields inline: `TSCamera_CopyBaseFields` (`00057bb4`)(camera, from), entered through its padding `00057bb3`, which runs `TSCamera_CopyPose` (`00057be0`)(camera, from) on the position and the view's euler triple `+0`–`+0x11` and then copies the three dwords `+0x16`, `+0x1a` and `+0x1e`; then the fields `+0x22` to `+0x49` one by one, the object `+0x32` and the mode `+0x36` among them.

**`ViewChain_DrawCaption` (`00058b68`)** is ES2's caption ([what the external view shows](../../retail/simulation/external-views.md#what-the-external-view-shows)) on a 320 × 200 screen with no shift for wider modes. The font is `dfn\green6x8`, the dword `000bd556` that `Widget_LoadStylesAndFonts` fills sixth ([the loads](../formats/styles-dat.md#the-loads)), and *h* its height (`+10`). The band's top row is halfway between the bottom of `ExternalView_GetRect`'s rect (`+0xc`) and row 199, less *h* / 2, and at most 198 − *h*; the band {0, top, 319, top + *h*} is filled with the brush {0, `0x3f`}, with the installed render context's (`000bdbda`) clip mode `+0x208` and translation `+0x20c` at 0 and put back after. `HudFont_DrawString` (`00069e38`) draws two runs with the pen on the band's bottom row, top + *h*:

| x | Text |
|---|---|
| 50, the word `00096c3c` | `VIEW: ` and the name of the main camera's object `+0x32`: `YOU` for the player, `Squad_PilotName` for a squadmate (`Squad_IndexOf` not −1), the empty string `00096d19` for any other |
| 215, the word `00096c3e` | `CONTROL: ` and `CAMERA` while `InputDrivesCamera` is set, `HERC` while it is clear |

The strings are `STRINGS0`'s, the pointers `000bd4c4`[0] to [3] (group 32) and `000bd3e0` (group 14) ([the groups](../formats/str-strings.md#the-groups)). `es2_xref.py` also finds two calls of it from `00057820`.

## Commands — `Sim_DispatchCommand` (`00059df8`)

(block): the block's command word, a set-1 scancode plus `0x200` for `Alt` and `0x400` for `Ctrl` ([keyboard commands](../../retail/simulation/cockpit-input.md#keyboard-commands-are-scancodes)), goes to `CockpitWidgets_HandleCommand` ([keys](cockpit-panel.md)), then to `Sim_HandleWindowKey` (`00059dec`), which answers 0 for every code, then to its own cases, found by `REPNE SCASD` in the 15 dwords at `00097180` with their targets 15 dwords after; a code it has no case for goes to slot `+0x30` of `ViewChain_Viewed` (the player's HERC's is `Mech_HandleCommand`, [the cockpit](mech.md#the-cockpit)), whose answer it returns. It returns 1 for a code it handles plus the second term below.

| Code | Key | Effect |
|---|---|---|
| `0x21f` | `Alt+S` | Toggles `Sim_Frozen` while `TapePlayback` or `TapeRecording` is set |
| `0x2f` | `V` | In view (`ViewChain_View`, `000b5b8e`) 1 or 3 queues view command 3 and sets `ViewChain_PendingView` (`000b5bba`) to 2; in view 2 queues 2 and sets 3 ([the chain of views](../../retail/simulation/external-views.md#the-chain-of-views)) |
| `0x31` | `N` | In view 3 with the view manager's `+0x10` at 4: the next object of `LiveObjectList` after `ViewChain_Viewed` (`ObjectList_Next`, wrapping) whose height `+0x28` is not negative and whose group `+0x3f` is the player's, the walk an inner loop past negative heights inside an outer loop past other groups, with no stop, so it never ends when no object of the list, the viewed one included, qualifies; when the one found is not the viewed one, `ViewChain_ViewObject`(object) ([the views](#the-views--viewchain_apply-0005894c)), the camera's mode 1, `ViewChain_Chosen` = it and `ViewChain_CaptionFrames` (`000b5bc0`) = 2 |
| `0x0f`, `0x1c` | `Tab`, `Enter` | With the view manager's `+0x10` at 4, `ViewChain_ToggleCameraControl` (`00059dbc`) |
| `0x0e` | `Backspace` | `g_CenterTurretMode` = 1 |
| `0x10` | `Q` | `Mission_StatusForAlert`(1, `LocalPlayerMech`)'s status in the status panel `StatusAlertPanel_Ctor` (`00046efc`)(0, 0, 1, status), as in the tick; the second term is 1 when the answer equals `Mission_EndingAnswer`[status] ([which answer ends the mission](#which-answer-ends-the-mission)); then `MissionAlertArmed` (`000bc4b0`) = 0, so the next status the poll raises waits out the alert delay again ([the poll](mission-status.md#the-poll--mission_pollstatus-0000f454)) |
| `0x01` | `Esc` | Queues view command 3, `Sound_Play`(`0x2d`), `ViewChain_PendingView` = 2 |
| `0x401`, `0x410` | `Ctrl+Esc`, `Ctrl+Q` | The status panel (0, 0, 1, 1), `QUIT TO DOS?`; its answer, 1 for `QUIT`, is the second term, and a non-zero one sets `Sim_QuitFlag` (`000b5b9e`) |
| `0x19` | `P` | The status panel (0, 0, 1, 0), `PAUSE`, whose one button answers 0; its answer is the second term |
| `0x219`, `0x58` | `Alt+P`, `F12` | `PreferencesPanel_Raise` (`00057820`): the preferences panel over the outside view ([raising the panel](preferences.md#raising-the-panel--preferencespanel_raise-00057820)) |
| `0x402` | `Ctrl+1` | `ThrottleLeverMode` (`000919ce`) = −1 when it is above 0, and 1 otherwise ([the throttle](mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc)) |
| `0x52` | keypad 0 | `Input_ToggleStickMode` ([the stick mode](input.md#the-stick-mode)) |

Each panel case runs the panel through slot `+0xc` of its `+0x314` and then calls the panels' base destructor `Alert_Dtor` (`00045d0c`) with 3 ([destroying a panel](alert-panels.md#destroying-a-panel--alert_dtor-00045d0c)). A result of 2 ends the mission ([the tick](#the-tick--sim_maintick-00059700)), so `Ctrl+Q` answered yes ends it with `Sim_QuitFlag` set, and `Sim_Shutdown` gives exit code 0.

`ViewChain_ToggleCameraControl` (`00059dbc`), frameless: flips `ViewChain_CameraControlPreferred` (`000b5bb8`) between 0 and 1, and sets `ViewChain_CaptionFrames` to 2 when the camera's object `+0x32` is the player.

Where ES2's differs ([ES2's keys](../../retail/command-line.md#-sprunknown-the-developer-keys)): ES2's has `'` (`TargetSelect_Nearest`), `\` (center legs), `/` (the manual) and `0x14` (auto-track); its `Backspace` also clears the legs mode and the auto-track; its `V`, `N` and `Esc` refuse while the camera is locked and its `V` steps view 4 too; its `Ctrl+1` needs a throttle lever; `Alt+S` also answers the developer flag; and `F12` raises its preferences panel.

## The player's controls — `Sim_PollPlayerInput` (`0005a0c0`)

(block). In order:

1. `Sim_DispatchCommand`(block); its answer being 2 is the return value.
2. `CockpitView_PollViewDevice`(`CockpitViewInstance`, block) ([the cockpit](cockpit-panel.md)).
3. **The axes.** With `InputDrivesCamera` (`000b5b90`) clear the machine takes steering `+0xd`, throttle `+0xf`, twist `+0x11` and pitch `+0x13`, and the camera nothing; with it set the camera takes the two axes `+0x1d` and `+0x21` point at and the trigger byte `+0x15`, and the machine nothing ([the input block](input.md#the-input-block)).
4. **The gunsight drag**, ES2's ([the gunsight drag](../../retail/simulation/gunsight-drag.md)) with the gadget `+0x1f1`'s rect `+0`–`+0xc`: while the byte `+0xb` is set it arms when the pointer (`+2`, `+6`) is inside the rect, *x0* ≤ *x* ≤ *x1* and *y0* ≤ *y* ≤ *y1*, all four edges included, and then adds *dx*²/64 to the twist and *dy*²/32 to the pitch (`000b5de4`, `000b5de8`); `+0xb` clear disarms it. With `+0xb` and `+0xa` both set it zeroes the player's twist `+0x22e` and pitch `+0x230` and disarms.
5. `Cam_Steer`(`ViewObjectPtr`, trigger, the `+0x21` axis, the `+0x1d` axis), its throttle and steer ([the camera](external-views.md#the-camera)).
6. **The machine.** When `ViewChain_Viewed`'s class `+0x162` is 2 (a flyer) and the camera is not driven, `Flyer_ApplyFlightCommand` (`0001b794`)(viewed, throttle, steering) ([the player at a flyer's controls](flyers.md#the-player-at-a-flyers-controls)) and the viewed object's `+0xa9` = 1. Otherwise, when the player's `+0x33` is set: `Mech_ApplyThrottleInput`(player, throttle, steering); then with the player's target `+0x15e` and the range, `Math_DistanceBetweenPoints` from the target's `+0x20` to the player's, 0 with no target:
   - the player disabled (`+0x9e`): `g_CenterTurretMode` = 1;
   - otherwise, with no twist or pitch input, the auto-track latch (`+0x14` of the mounts `+0x198`) set and a live target, one whose destroyed byte `+0x93` is clear: the target's `+0x20` plus the first of the two offsets its slot `+0x34` gives, for a HERC `Mech_GetAimPoint`'s orbit centre (0, record `+0x68`, `+0x6a`) ([small functions](mech.md#small-functions)), goes to `Cockpit_TargetAnglesFromCameraBone` ([the cockpit's aim](mech-ai.md#weapons)), and the centering mode is cleared; with input, the centering mode is cleared;
   - then the centering mode runs `Mech_CenterTorsoTick`(player, range), and otherwise, unless tracking, `Mech_TorsoTwistTick` and `Mech_TorsoPitchTick` take the twist and pitch ([the turret](mech.md#the-turret)).
7. With the camera not driven, `WeaponMounts_FireTrigger`(mounts, player, block) ([the trigger](weapon-mounts.md)).
8. Button 2 (`+0x17`) pressed: `TargetSelect_Cycle` once a press (latch `000971fc`).
9. Button 3 (`+0x18`) pressed: in stick mode 1 the throttle `+0x226` = 0 and `+0x8d` = 1, the all-stop; in stick mode 2 `g_CenterTurretMode` = 1.

Where ES2's differs ([ES2's](../../retail/simulation/joystick-input.md#while-the-camera-has-the-controls)): ES2's passes the camera the pitch axis when a throttle lever is fitted, runs its legs mode, tracks through `Player_ResolveTargetAimPoint`, flies the RAZOR when the player's own type record says so, fires through `Mech_PlayerFireTick`, and runs the 19 button actions of the controls panel; it has no disabled-turret or second-button arm.

## The end — `Sim_Shutdown` (`0005b568`)

`Sim_Main` calls it after the loop: `CockpitMouseLive` = 0; when `TapeFile` is set, the `.dmp` file closed when `DmpFile` is set and then the tape, the `.dmp` file's close inside the tape's test; `GndTex_Free`; `Mission_WriteResults`(`LocalPlayerMech`) ([the results](world.md#writing-the-results--mission_writeresults-0001d698)), a file of which that does not open ending the program with 0 left in `data\exit.cfg` ([a fatal assert in a mission](asserts.md#a-fatal-assert-in-a-mission)); the exit code, 6 under `DemoMode`, 0 with `Sim_QuitFlag`, else 4 when the player is destroyed (`+0x93`) and `MissionModeFlag` (`000bc4a0`) is not 0, else 3, written as an `int16` to `data\exit.cfg` (assert `init.cpp` line `0x1a4`); `Sound_Shutdown`; `Sim_ShutdownNoOp1` and `Sim_ShutdownNoOp2` ([small functions](world.md#small-functions)). ES2's codes are the same, returned from `WinMain` ([exit codes](../../retail/command-line.md#exit-codes)); ES2's load zeroes its `MissionModeFlag`, ES1's keeps the header's mode word ([the header](../formats/script-dat.md#the-header)).

## Small functions

- **`Config_BuildPath` (`00059300`)**, (name): `data\` + name, or `tapes\` + name while `TapePlayback` is set, in the buffer `000b5dc0`, as ES2's.
- **`Path_ApplyAltDrive` (`000592a4`)**, (path): the prefix at `000b5d9c` + the path less a leading drive, in the buffer `000b5da0`.
- **`Cam_AttachTo` (`0005923c`)**, (object): slot `+0x34` of the object gives the eye and orbit offsets, which `Cam_AttachEye` and `Cam_AttachOrbit` set on `ViewObjectPtr`, as ES2's (`0045ed94`) with slot `+0x30`.
- **`SimInput_PushDisable` (`0005a5b4`)** pushes `SimInput_Enabled` (`00095818`) on the byte stack at `000b5df8`, its depth at `00097280`, and turns the keys off with `SimInput_SetEnabled`(0); **`SimInput_PopEnable` (`0005a5d8`)** pops it back through `SimInput_SetEnabled`. `AlertPanel_Enter` (`00045f00`) calls the first through `AlertPanel_EnterHook` (`00094d80`) and `AlertPanel_Leave` (`000460b8`) the second through `AlertPanel_LeaveHook` (`00094d84`), so the keys are off while a panel is up ([the in-mission panels](alert-panels.md#putting-a-panel-up-and-taking-it-down)).
- **`Sim_ReleaseSessionObjects` (`0005b534`)**: `DVTimer_Dtor` (`0006c758`)(`Sim_Timer`, 3) and, with the render context `000b5b6e` set, its slot `+0x1c` with 3, ES2's (`00461e74`). No reference to it found by `es2_xref.py`.
- **`Sim_DebugBreak` (`00059658`)**: `INT3` while `-B`'s `000b5bcc` is set. No reference to it found by `es2_xref.py`.
- The cameras' destructors: `TSOrthoCamera_DtorInitCopy` (`0005b4d4`), slot `+4` of `000972de`, and `TSCamera_DtorInitCopy` (`0005b508`), slot `+4` of `00097362`, the module's copies of the library's ([module copies](../formats/compiler.md#vtables)).

## Functions

| ES1 | Address | ES2 |
|---|---|---|
| `Cursor_LoadCursorResource`, in the tape bundle's file | `00057640` | `0045ce30` |
| `HeapPtr_DtorSimGlobalsCopy`, `ConfigPart_DtorSimGlobalsCopy`, `ConfigPart_GetClassTagSimGlobalsCopy`, `TSPartBase_DtorSimGlobalsCopy` | `00057378`, `000573a0`, `000573df`, `000573e8` | |
| `HeapPtr_DtorSimSessionCopy`, `ConfigPart_DtorSimSessionCopy`, `ConfigPart_GetClassTagSimSessionCopy`, `TSPartBase_DtorSimSessionCopy` | `00057680`, `000576a8`, `000576e7`, `000576f0` | |
| `TSCamera_CopyFrom`, `TSCamera_CopyBaseFields`, `TSCamera_CopyPose` | `00057af8`, `00057bb4`, `00057be0` | |
| `Sim_DeathFlash`, between `0x90` bytes at `00058413` and `00058572` | `00058414` | `Sim_DeathFlash` (`0045dc34`) |
| `Sim_FrameSoundTick`, `Music_Tick` | `00058574`, `000585dc` | |
| `ViewChain_Apply`, `ViewChain_ViewObject`, `ViewChain_DrawCaption` | `0005894c`, `00058a44`, `00058b68` | `0045de14`, `0045df18`, `0045e1ec` |
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

The seven class tags (`ConfigPart_GetClassTagSimGlobalsCopy` (`000573df`), `ConfigPart_GetClassTagSimSessionCopy` (`000576e7`), `00058dfb`, `000596cb`, `00059b0b`, `00059d87`, `0005a57f`), `ViewChain_ToggleCameraControl`, `Sim_HandleWindowKey`, the two `SimInput` hooks, `Sim_DebugBreak` and `Sim_ReleaseSessionObjects` have no Ghidra function. `Main_StaticInit` (`00057414`), the static constructor table's entry `000adc74`, the last code of its file, zeroes the `0x78`-byte block from `SimDisplay` (`000b5b5c`), sets the `+0x4a` dword to 1, `Sim_SoundSwitch` (`000b5b68`) to 1, `-V`/`-W`/`-T`'s words to -1, `000b5bb8` to 1 and `ViewChain_View` and `ViewChain_PendingView` to 2, clears `DemoAbort`, `DemoMode`, `Sim_AltDriveAllowed` and `000b5bca`, and seeds the two generators at `000b5bd4` and `000b5c48` with `Math_RandomSeed`, as ES2's `Main_StaticInit` (`0045cad8`) seeds its two.

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
| `ViewChain_CameraControlPreferred` | `000b5bb8` |
| `ViewChain_SpareCamera`, a `0x4a`-byte camera, and its guard byte | `000b5d38`, `000b5d34` |
| `Music_Song`, `Music_Battle`, `int16` | `00096d0c`, `00096d0e` |
| `Music_NextCheck`, `Music_SongObject` | `00096d10`, `00096d14` |
| `Music_Switching`, byte | `00096d18` |
| `Sound_Listener`, the camera | `000bd730` |
| `ViewChain_Viewed`, the object the controls and views follow | `000bda9a` |
| the squad cameras, one dword a member | `000bda9e` |
| `Mem_PeakUse` | `00096f08` |
| `Mission_EndingAnswer`, 19 `int16` ([which answer ends the mission](#which-answer-ends-the-mission)) | `00097158` |

## Open

- **Open:** the callees read for none of this file's functions: `00056ecc` (the drive prefix), `00075858`, `00066e00` and `0006a5fc`.
- **Open:** what `000b5b69` (`-P`) and `-V`, `-W` and `-T`'s words feed, and what sets `Sim_StepOnce`; `es2_xref.py` finds only the stores above and the tick's test, and a store through the block's base `000b5b5c` is not covered by that search.
- **Open:** what the bit `Sim_Main` clears in the display's first byte does.
- **Open:** the callees of [the music](#the-music--music_tick-000585dc) read for none of this file's functions: `0006edb3` and `0006f225`, the song player's calls (item 7's module D, [`hmp-music.md`](../formats/hmp-music.md)); and `0004ad0c`'s callers and what `000b1734` is.
- **Open:** a retail check of [the music](#the-music--music_tick-000585dc): in a mission with enemies, the music changes within about 5 seconds of the first enemy coming within 25000 or into view ahead. The calm songs, rows 0–2, are `BATTLE1A`–`BATTLE3A`, and the battle songs, rows 3–5, `BATTLE1B`–`BATTLE3B`; row `0x13` is `start3` and row `0x2d` the engine hum `herceng1` ([the catalog](../formats/sound-samples.md#the-simulators-catalog)). <!-- doc-lint: ok -->
- **Open:** what [the sound step](#the-mission-start--sim_startmission-0005a9dc) passes for a key `resource.cfg` lacks, `Profile_GetString` copying its default argument 0 as a string, and for an empty `PortAddress` or `[Music]` `Driver#`. The setup writes every key.
- **Open:** a registration `es2_xref.py` cannot see, such as a call of `RegisterSubsystemLoader` through a pointer, at phase 0, 1, 3 or 4.
- **Open:** how far step 15's copy reaches: its inclusive rect {0, 0, 320, 400} reaches column 320, one past a page's width, and row 400, and `Widget_DrawToCockpit` copies it with the cockpit's canvas context installed, in clip mode 1, which cuts it to the canvas rect, the `.GAU` record's first 16 bytes ([building it](cockpit-panel.md#building-it)), whose values in the retail files are not read here.
- **Open:** a reader of `Mission_EndingAnswer`'s words 0, 1, 5 and `0x11` (1, 1, 1 and 0); `es2_xref.py --decode` finds no instruction holding the address of word `0x11` (`0009717a`), and the two indexed reads above.
- **Open:** the death flash in the running game. A retail check: let the HERC be destroyed in the cockpit view; within about 4 seconds the effects cut off and an explosion sounds as the view shakes for half a second, the music playing on, the colors jump and fade to black over about 4 seconds with nothing moving, and `YOU HAVE BEEN KILLED.` comes up on a black screen, `CONTINUE` ending the mission. Pressing `Q` in the seconds between the destruction and the flash shows the same panel with no flash.
- **Open:** whether `Ctrl+Esc`, keypad 0 and `Esc`'s sound reach `Sim_DispatchCommand` in retail as the table says. A retail check: in a mission, `Ctrl+Esc` raises the same panel as `Ctrl+Q`.
- **Open:** a positive control in the running game for the done checklist ([when a module is done](../../herculan/plan-es1-support.md#when-a-module-is-done)). Proposed: `BP <CS>:00059700` fires once a frame; after a mission ends, `DATA\EXIT.CFG` holds 3 (`03 00`) and after `Ctrl+Q` answered yes 0.
