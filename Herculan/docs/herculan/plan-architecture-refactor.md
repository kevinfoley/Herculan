# Plan — architecture refactor of `Herculan.Engine` and `Herculan.Engine.Host`

Make each namespace hold one kind of code, keep game rules out of the host, and point dependencies one way, without changing any algorithm, data layout or behaviour ported from the retail game.

Stages 1 to 3 are built; the rest is planned. Each item is re-read against the code before it is done: the review behind this plan was partly delegated, and an item's claim is a lead until then.

## Why

- **`Content` was a grab-bag.** Of its 73 files, 19 loaded data files; 50 were the cockpit's runtime state and logic, and the rest were install and input code.
- **Retail game rules lived in the host, which no test project references.** By their own doc comments, `PlayerCockpitUpdate`, `PilotControls`, `ModalPanels`, `CampaignLoop` and `GameInProgress` port the player's per-frame cockpit update, the input poll's button switch, the alert panels' key handling and the save-slot load and save. [`planning.md`](planning.md#engine-internal-architecture) asks for a thin host.
- **Dependencies pointed both ways.** Sim imported Audio, Cockpit and Render, Gl imported Render, and Sim read the global `TweakSettings.Current`. Render still uses Sim in seven files.
- **Large classes bundle unrelated jobs.** `SimWorld` (pools and tick, mission state, effects, raycasts, shot spawning, latches), `MissionScene.Load` (session bootstrap, mesh preload, write-back of shape data), `GameAudio` (audio stack plus the message-port clock and the power-up rule), `DebugPanel` (view, probes and view options, read by the frame stepper and renderer).
- **Earthsiege 1 support is under consideration.** Its executables are a different build (not Borland, an X-32 DOS extender) with a different shell (`GO.EXE`) and audio (`.SFX`, HMP through HMI SOS), but share the 3Space asset layer: VOL, the `TS*`/`GL*`/`AN*` classes, `.DTS`/`.DBA`/`.DPL`/`.DFN` headers, and 548 of 556 `.SNC` files byte-identical. A refactor that separates the asset layer from game-specific code serves that split.

## Constraints

- **Retail structure stays.** No change to an algorithm, a call order, a retail-mirrored state layout (`WeaponMount`, `BaseObject`, the `SimObject` hierarchy), `MissionLoader`'s two passes, or the sim's direct sound calls. Regrouping `SimWorld`'s fields, which gather DBSIM globals rather than mirror one struct, is allowed.
- **No speculative ES1 abstraction.** No game-version interfaces or switches until the ES1 survey (below) shows where the games differ — the same rule [`planning.md`](planning.md#rendering) applies to the render backend.
- **Folder is namespace**, as throughout both projects.
- **No nested namespace shares a short name with another namespace or a type.** A nested namespace captures qualified references and `cref`s inside its parent: `Render.Terrain` would capture `Terrain.HeightGrid` in `Camera.cs`, `Sim.Mission` would capture the `Mission` type, `Cockpit.Console` would capture `System.Console`. `Render.Cockpit` beside `Engine.Cockpit`, and `Host.Shell` beside `Engine.Shell`, already exist and are accepted.
- **Small companion types stay in their class's file** (an enum or record used only beside that class). Independent classes get their own file.
- **Shell types keep the `Shell` prefix**; dropping it would collide with Sim and Render names (`Map`, `Text`, `Palette`).

## Stage 1 — moves, renames and file splits

No logic changes; the compiler catches most mistakes.

Built:

- `Content` split: data loaders stay in `Content`; cockpit state and logic is `Cockpit` (one flat namespace, as `Sim` is); `GameInstall`, `RetailInstaller` and `LauncherLanguage` are `Install`; `JoystickCapabilities` is in `Input`.
- The GL-free camera and view state machines (`CockpitGlance`, `CockpitHitShake`, `CockpitPan`, `CockpitViewKick`, `ExternalCamera`, `ExternalViewChain`, `ExternalViewLayout`, `PlayerTrail`, `ViewCamera`) are `View`. `Camera` stays in `Render`: it produces the render-space eye and direction.
- `EngineWindow`, `ExecutableIcon`, `PrintScreenCapture` and `ScaledImGui` are `Platform`, in the engine assembly because both hosts use them.
- `CockpitReadouts` is `PlayerCockpitUpdate`, after the retail function it ports.
- `ShellWeaponUnit`, `ShellBayMachine` (with `ShellMachineStatus`), `ShellBayPilot`, `ShellLaunchRefusalDialog` and `ShellEndOfGameDialog` have their own files.
- Partial-file splits along retail prefixes: `WeaponMount` (`.Charge`, `.Gauge`, `.Fire`, `.Condition`); `MechObject` (the throttle state and `Mech_MovementTick` to `.Locomotion`, the collision test and what a refusal sets off to `.Collision`, the turret commands to `.Torso`, and `.Combat` split into `.Damage` and `.Scoring`); `BaseObject` (`.Damage`, `.DeathSequence`); `ShellHangar` (`.Armory`, `.Squad`, `.Grants`, `.Save`); `ShellMap` (`.Paint`, `.Intro`, with `ZoneRelief` and `ShellMapArt` in their own files); `DtsMeshBuilder` (`.Parts`, `.Polys`, `.Emit`).

## Stage 2 — dependencies one way

Built:

- `ISoundSink`, `SoundId` and `SoundReach` are in Sim, and Sim does not import Audio. A null `SoundReach.MinRange` keeps the catalog row's rolloff start, so the drop-pod tweak does not read `SoundCatalog`.
- Sim does not import Cockpit: the charge-bar range is `WeaponMount.ChargeBarRange`, which `ChargeBarSlider` reads; `script.dat` block 1's box is `World.MissionBox` (retail `Mission_Box`), held as `SimWorld.MissionBox`, and the command display's 60000-unit pan margin is `HddMap.Margin`. Sim's remaining references to Cockpit types are qualified `cref`s.
- Sim does not import Render: `FlyCameraObject.ApplyTo(Camera)` is an extension in `View` (`FlyCameraView`), beside `ViewCamera.ApplyTo`, since a `Camera` member taking a Sim object would add a Render→Sim use.
- Gl does not import Render: `GpuTexture` takes pixels and a size, and `TextureAtlas.Upload` makes one.
- Sim reads its tweaks from `SimWorld.Tweaks`, which `MissionScene.Load` sets to `TweakSettings.Current` and which defaults to every setting at its default. `AnimationThread.SeekToPosition` takes the sub-tick choice as an argument, so the animation layer reads no setting; its callers, and `BaseObject`'s constructor through `MissionScene`, pass the world's.

Standing rule:

- No new dependency from asset-layer code (Content, Render, Gl, Platform, Audio back end) onto ES2 game code. The existing Render→Sim uses are left until the ES1 survey decides the split.

## Stage 3 — thin host

Built:

- The seams the moves needed. `ModalPanels` moves the pointer through `IPointerDevice`, which `SimulatorInput` implements, takes the keyboard and whether the mission is over as arguments, and reports an answer that ends the mission through `TakeMissionEnding`, for the host's `MissionOutcome` to act on. `CockpitCommands` reaches the manual and the full-screen toggle through `ISystemButtonActions`, which `WindowKeys` implements. The debug panel's eye pin is `View.SteadyEye`, which `CockpitView` owns. The stick is an `IJoystickSource`; the host's `JoystickSource` opens it and announces it.
- `PlayerCockpitUpdate`, `PilotControls`, `ModalPanels`, `CockpitCommands`, `CockpitKeyboard`, `PilotKeys`, `CockpitView`, `CockpitDisplays`, `DeveloperKeys` and `SimulatorStaging` are in `Engine.Cockpit`, and `Host.Simulator.Cockpit` is gone. What they read came with them: `SimulatorStart` is in `Scene`, `ShellLaunch` in `World`, and `TapePlayback`, `TapeRecording` and the key state (`IKeyState`, `KeyChords`, `KeyLatch`) in `Input`.
- `StagingOptions` is `StagedStart` (the state a mission powers up in) and `StagedScreenshot` (what the capture waits for). `SimulatorStaging.AfterFrame` says when to capture; the host captures and closes the window.
- `GameInProgress` is in `Engine.Shell`. `CampaignLoop`'s decisions are `ShellCampaignLoop`, which returns the launch or the debrief rather than acting on a window; the host's `CampaignScreens` does what follows on screen.
- `WorldDrawItems`, `TransientDrawItems`, `SceneUploads` and `DrawFiling` (the detail-level and part selection the draw pass ports), with `MechDetailChain` and `DetailMetrics`, are in `Engine.Scene`, where the editor can use them. They are in Scene rather than Render because they read `MissionScene` and Sim objects, which Render may not take on (Stage 2's standing rule); Scene already depends on Render, Gl and Sim.
- `DebugPanel` is three types in `Host.Debugging`: `DebugOptions` (what the renderer and the cockpit eye read), `DebugProbes` (the walk and shot measurements the host and the frame stepper take) and the panel itself.

## ES1 survey — gates Stage 4

Before Stage 4, enough ES1 reverse engineering to decide, per subsystem (formats, sim objects, mission start-up, shell, audio), whether ES1 matches ES2, differs in data only, or differs in structure. Three questions decide the most:

- Which compiler built ES1 — it decides whether any layout-mirroring code can be shared.
- Whether the modules both simulators name (`bullet`, `collide`, `damage`, `debris`, `flyersys`, `mechsys`) match function by function on a sample.
- Whether the fixed-point math routines are identical.

Where the static `.DAT` tables live — `World`'s (`BaseTypeTable`, the formation tables, `BaseCollisionTable`) and Sim's (`BulletCatalog`, `WeaponCatalog`, `MechTypeRecord` and the rest) — and whether they are named Table or Catalog, is decided with that split: the tables are where the two games' data formats are most likely to differ.

The answer, and how the two games share code (a shared core with one assembly per game, or version branches), goes in [`planning.md`](planning.md).

## Stage 4 — responsibility splits

Code moves verbatim and keeps its call order.

- `SimWorld` owns `MissionRuntime` (counters, actions, objectives, salvage), `EffectPools` (effects, debris, fires, lights) and `PlayerMissileState`; the raycasts and blast sweep become a static `HitTests`. `Tick`'s order is unchanged.
- `GameAudio` keeps building the audio stack; the message-port clock and the power-up announcement move to `Cockpit`; the `Music_*` layer leaves `SoundDirector`.
- `MissionScene.Load` splits into a sim bootstrap and a model set, last: the `Bind*` order and the `SimRandom` draws must not move, and mesh building is not to draw from `SimRandom`.

## Names considered and rejected

| Name | Why not |
|---|---|
| `Missions` for all of `World` | `World` also holds static game tables (`BaseTypeTable`, the formation tables, `TheaterDescriptor`), which are not missions. `World` — a mission's world as described before the simulation runs — fits both halves. |
| `Menus` for `Host.Settings` | It holds only settings; "menus" reads as navigation. |
| `Tweaks` for `Engine.Settings` | It also holds the generic `SettingDefinition<T>` base. |
| `ShellViewport` for `ShellScreenLayout` | It is deliberately the shell's counterpart of `CockpitScreenLayout`; renaming one breaks the pair. |
| `IMusicPlayer` for `ICdAudio` | `ICdAudio` models the original's CD audio device, as its members' doc comments say, and `IMusicPlayer` reads like `IMusicSource`. |
| `MovieWindow` for `MovieHost` | `ShellHost`, `SimulatorHost` and `MovieHost` are one host per run mode, by design. |
| `Host/Windows`, `Host/Devices`, `Host/Startup` | "Windows" reads as the OS; `KeyState` also holds the replay keys; `HostSession` and `Launcher` are not start-up. |

## Verification

Every step:

- `dotnet build Herculan/HerculanEngine.sln` at 0 warnings and `dotnet test` green.
- Broken `cref`s, which the normal build does not report: build with `--no-incremental -p:GenerateDocumentationFile=true "-p:NoWarn=CS1591%3BCS1573"`, keep the CS1574/CS0419/CS1580/CS1584 lines, and compare them against the same build of `main` (a worktree of `origin/main`) as a set — file name and message, without line numbers, since a move shifts lines and MSBuild prints each warning more than once. The count depends on how the lines are de-duplicated, so the set is the check, not a number.
- Usings a move left unnecessary: with `dotnet_diagnostic.IDE0005.severity = warning` added to `.editorconfig` for the run, build with `-p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true` (IDE0005 needs the documentation file to see `cref` uses) and fix the hits in touched files with `dotnet format style Herculan/HerculanEngine.sln --diagnostics IDE0005 --severity warn --include <files>`. IDE0005 reports one diagnostic per run of consecutive unneeded usings, at its first line, so deleting the reported lines by hand leaves the rest of each run. Restore `.editorconfig` afterwards.
- Extracted declarations are diffed line for line against the original file.
- `doc_lint.py` and `doc_links.py --code` report nothing new.
- Renames go through `tools/scripts/rename_symbol` where it builds.

Stages 3 and 4 move code whose order is behaviour. Their check would be a replay diff — record input tapes over a few missions, dump simulation state, and compare before and after — but replays are not deterministic, so the check is made in process instead. `SimulatorRig`, in the engine tests, builds the cockpit stack over a demo tape's mission without a window and runs the update half of `SimulatorHost.OnUpdate` in its order; `CockpitSessionTests` drives a scripted session through it and digests a snapshot of the state taken every frame. Run it before and after a move with `HERCULAN_RIG_LOG` set to a file path, and compare the two per-frame logs: they must be identical. `SimulatorRig` mirrors the host's update order, so it changes when that order does.

## Open

- **Open:** replay determinism, which a replay diff of these stages needs (see [Verification](#verification)).
- **Open:** `tools/scripts/rename_symbol` fails to build on the .NET 8.0.1xx SDK (CS9057: its analyzers need compiler 4.12); renames in such an environment are done by hand and checked by the build.
- **Open:** retail behaviour still in the host: `SimulationStepper` (the fixed-timestep accumulator and a replay's ticks), the [Esc] back-out from the external view, a glance and the Heads-Down Display in `WindowKeys.ReadMenuBarEscapeKey`, and `SimulatorHost.OnUpdate`'s order, which `SimulatorRig` mirrors for the tests.
- **Open:** the ES1 survey's three questions.
