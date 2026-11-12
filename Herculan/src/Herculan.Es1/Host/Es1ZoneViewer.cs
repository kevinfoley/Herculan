using System.Numerics;
using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Input;
using Herculan.Engine.Menus;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.View;
using Herculan.Es1.Input;
using Herculan.Es1.Mfd;
using Herculan.Es1.Panels;
using Herculan.Es1.Render;
using Herculan.Es1.Scene;
using Herculan.Es1.Shell;
using Herculan.Es1.Sim;
using Herculan.Es1.Sound;
using Herculan.Es1.View;
using Herculan.Es1.World;
using Silk.NET.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// The header words a launch overrides as engine aids, which retail has no way to name: the theater (word 0) and the
/// world file's variant and detail (words 9 and 8). Null leaves the word the mission's files hold.
/// </summary>
public sealed record Es1HeaderOverrides(int? Theater, int? WorldVariant, int? WorldDetail);

/// <summary>
/// Runs an ES1 mission as far as this engine ports one: the shell's single-mission load when a mission is named, the
/// simulator's world load from the install's <c>data\</c> files, the tick under the keyboard, the mouse and a controller,
/// the cockpit's views of the player's machine on the display's pages, with its screen edges and cursors, and the
/// pause and quit prompts, the objectives and the preferences panel (docs/herculan/plan-es1-first-mission.md, steps 4,
/// 6a and 6a2). This class
/// composes: the load and its files are <see cref="Es1SingleMission"/> and <see cref="Es1MissionFiles"/>, the world
/// <see cref="Es1World"/>, the tick <see cref="Es1SimulationRunner"/>, built by <see cref="Es1SimulationStart"/>, the zone <see cref="Es1ZoneScene"/>, the draw
/// <see cref="Es1WorldDraw"/>, the display and its views <see cref="Es1VideoMemory"/> and <see cref="Es1ViewManager"/>,
/// the frame on them <see cref="Es1FrameLoop"/>, and the monitor <see cref="Es1Monitor"/>.
///
/// <para>Each host frame runs the tick when the clock answers one, and after it the camera, the world drawn into the
/// display's draw page and the frame's end, as <c>Sim_Main</c>'s frame does (docs/es1/simulation/main-loop.md#the-frame);
/// and every host frame shows the monitor's picture of the display. The GL context is current in the window's update
/// as in its render, both on the one thread, so the tick's 3D pass is drawn there.</para>
///
/// <para><b>Not retail, awaiting the user's decision:</b> with no player's machine (the free camera only) no tick runs.
/// Retail's controls read <c>LocalPlayerMech</c> unchecked, so retail has no behaviour to port for this case.</para>
///
/// <para>F switches to a free camera, an engine aid with no retail counterpart, which shows the world over the whole
/// window with the player's machine drawn; the simulator still reads every key, so the keys the free camera moves by
/// also drive the machine. While the cockpit is shown the host's pointer is hidden over the window, the cockpit's cursor
/// standing in for it; the free camera shows it, and hides it while the right button looks. G, another engine aid,
/// switches the ground between textured and banded (<see cref="Es1BackdropOptions.ToggleGround"/>): with a simulation
/// running it overrides preferences option 5's choice until pressed again, without writing the option, and with none it
/// switches the viewer's own option 5. Closing the window quits; so does Esc with no player's machine, since
/// with one Esc is the simulator's key (docs/es1/simulation/main-loop.md#commands--sim_dispatchcommand-00059df8); and
/// so does whatever ends the mission: retail's way out, <c>Ctrl+Q</c>'s or <c>Ctrl+Esc</c>'s QUIT (plan decision 12), a
/// status panel's answer that ends it (docs/es1/simulation/main-loop.md#which-answer-ends-the-mission), and the death
/// flash (docs/es1/simulation/main-loop.md#the-death-flash--sim_deathflash-00058414). A mission's end runs
/// <see cref="Es1Shutdown.Run"/> before the window closes; closing the window, which retail has no counterpart of, runs
/// nothing and writes no file, so <c>data\exit.cfg</c> keeps the 0 the start wrote.</para>
///
/// <para><b>This engine's own:</b> HERCULAN's menu bar is over the mission in the window the shell's turns use
/// (<see cref="FrontEndWindow"/>), drawn over the monitor or the free camera, hidden until [Shift+Esc] or a gamepad's
/// Back raises it (<see cref="Es1HostKeyboard.MenuBarTakes"/>, <see cref="FrontEndWindow.BeginFrame"/>); the pointer at
/// the window's top does not. The mission runs on under it, as Earthsiege 2's does, the controller muted while the bar
/// or one of its windows is up (<see cref="Es1SimulationStart.MenuBarWindow"/>); the cockpit takes no mouse events while
/// the pointer is over the bar or one of its windows, where the system's pointer shows, and the engine aids' keys do
/// nothing while a field of its windows is typed into.</para>
/// </summary>
public static class Es1ZoneViewer {
	/// <summary>The clear colour when the theater's sky cannot be built: a mid grey, plainly not a retail sky.</summary>
	private static readonly Vector3 MissingSky = new(0.32f, 0.32f, 0.36f);

	/// <summary>
	/// Starts <paramref name="missionName"/> (with or without <c>.MSN</c>) as a single mission and runs the simulator on
	/// the files that writes, or with no mission named runs it on the files the install's <c>data\</c> holds, as
	/// <c>DBSIM.EXE</c> does. Returns the process exit code: 1 when <c>data\exit.cfg</c> does not open for writing or the
	/// mission does not load, and otherwise
	/// <see cref="Show"/>'s. Returning the code <c>data\exit.cfg</c> holds as the process's is this engine's own: retail's
	/// <c>Sim_Main</c> returns nothing, and the shell reads the file.
	///
	/// <para>With <paramref name="playTape"/> (<c>--play</c>, retail's <c>-p</c>) or <paramref name="demo"/>
	/// (<c>--demo</c>, <c>-D</c>; with <c>--play</c>, that tape in demo mode) the tape is opened and its bundle unpacked
	/// over the install's <c>data\</c> mission files and <c>tapes\prefs.cfg</c> and <c>tapes\keyjoy.cfg</c>, as
	/// <c>Sim_ParseCommandLine</c> does before the mission loads (<see cref="Es1TapePlayback.FromCommandLine"/>); the
	/// tape carries the mission, so a named mission is not loaded. Returns 1 when a named tape is not found or does not
	/// read.</para>
	///
	/// <para>With <paramref name="recordTape"/> (<c>--record</c>, retail's <c>-r</c>) the tape is created where the flag
	/// names it and the bundle packed from the install's files (<see cref="Es1TapeRecording.Start"/>), and every input
	/// build appends its frame. Retail packs as it parses the command line, after the shell has written the mission's
	/// <c>data\</c> files; here a named mission's load writes them, so the bundle is packed after that load, from the files
	/// the mission then runs on. Returns 1 when the tape does not open for writing.</para>
	/// </summary>
	/// <param name="imguiFontPath">The faces the menu bar's ImGui draws in (<see cref="FrontEndWindow"/>).</param>
	/// <param name="menus">HERCULAN's menu bar and its windows over the mission, which the window disposes once the mission has run.</param>
	/// <param name="window">
	/// The window the launcher's turns share (<see cref="Es1Launcher"/>), which stays open after the mission; none for a
	/// window of the simulator's own, closed with it.
	/// </param>
	/// <param name="soundOn">
	/// <c>000b5b68</c>, 1 in the image, which <c>-s</c> toggles: clear, the mission start makes no sound manager
	/// (<see cref="Es1Sound.MissionStartDevices"/>).
	/// </param>
	/// <param name="silent">The host's <c>--no-sound</c>: no audio device, so the sound manager plays into nothing.</param>
	public static int Run(string installRoot, string? missionName, Es1HeaderOverrides overrides, string imguiFontPath,
			string imguiItalicFontPath, IFrontEndMenus menus, string? playTape = null, bool demo = false,
			string? recordTape = null, HostWindow? window = null, bool soundOn = true, bool silent = false) {
		// Sim_Main's step 4, before the command line's parse (docs/es1/simulation/main-loop.md#starting--sim_main-00059350):
		// an int16 0 to data\exit.cfg, so a run that ends before Sim_Shutdown leaves 0, which ends the launcher's loop.
		// PLACEHOLDER: the heap's size to data\mem.got before it; this engine has no fixed heap to give the size of.
		if (!Es1Shutdown.WriteExitCode(installRoot, 0)) {
			Console.Error.WriteLine($"data\\{Es1Shutdown.ExitName} does not open for writing in {installRoot} (main.cpp line 0xf9 asserts here).");
			return Es1Shutdown.AssertExitStatus;
		}

		Es1TapePlayback? tape = null;
		if (playTape != null || demo) {
			if (!Es1TapePlayback.FromCommandLine(installRoot, playTape, demo, out tape, out string? tapeError)) {
				Console.Error.WriteLine(tapeError);
				return 1;
			}

			if (tape != null) {
				Console.WriteLine($"Tape {tape.FilePath}: {tape.Tape.Frames.Count} frames, {tape.RecordedSeconds:0.0} s as recorded, " +
					"unpacked over the install's data\\ mission files and tapes\\prefs.cfg and keyjoy.cfg. " +
					(tape.DemoMode ? "Any key ends the demo." : "Ctrl+E ends it."));
				if (missionName != null) {
					Console.WriteLine($"The tape carries its mission, so {missionName} is not loaded.");
					missionName = null;
				}
			}
		}

		if (missionName != null) {
			var random = Es1ShellRandom.AtStartup(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			if (Es1SingleMission.Start(installRoot, Es1SingleMission.MountShell(installRoot), missionName, random,
					out string? loadError) == null) {
				Console.Error.WriteLine(loadError);
				return 1;
			}

			Console.WriteLine($"Loaded {missionName} as a single mission into data\\script.dat, mission.var and player.mec.");
		}

		Es1TapeRecording? recording = null;
		if (recordTape != null) {
			recording = Es1TapeRecording.Start(installRoot, recordTape, out string? recordError);
			if (recording == null) {
				Console.Error.WriteLine(recordError);
				return 1;
			}

			Console.WriteLine($"Recording input to {Path.GetFullPath(recording.FilePath)}, its bundle packed from the install's data\\ files and keyjoy.cfg.");
		}

		// The sound file's globals, which the preferences' handlers switch from Prefs_Init's load on (step 2), before the
		// world they belong to is built.
		var sound = new Es1Sound();
		if (Es1MissionFiles.Read(installRoot, out string? error, tape?.DemoMode == true, sound) is not { } files) {
			Console.Error.WriteLine(error);
			return 1;
		}

		var header = files.Script.Header;
		header[0] = (short)(overrides.Theater ?? header[0]);
		header[9] = (short)(overrides.WorldVariant ?? header[9]);
		header[8] = (short)(overrides.WorldDetail ?? header[8]);
		var theater = new Es1Theater(header[0], header[9], header[8]);
		if (!theater.IsValid) {
			Console.Error.WriteLine($"No theater {theater.Index} with world variant {theater.WorldVariant} and detail {theater.WorldDetail}.");
			return 1;
		}

		// What the simulator mounts: SIMVOL0 (program mask 1) and ZONES (0x101), searched by ES2's precedence rule.
		// ES1's own volume code is unread (docs/es1/formats/zone-terrain.md#open), and its CD drive comes from ALT_DRIVE,
		// not drive.cfg, so no disc is mounted. Nothing ES1 reads takes the content's language, so it is English.
		var content = GameContent.MountInstall(installRoot, disc: null, Voln.DbsimProgram, GameLanguage.English);
		// The HERCs and the cockpit are loaded before the zone, unlike retail, so the scene is built in the live palette
		// with the player chassis' scheme installed (Es1ZoneScene.Palette); with no player's machine nothing is installed.
		Es1World world;
		Es1ZoneScene scene;
		Es1CockpitViews? views;
		try {
			var script = Es1MissionScript.Read(files);
			var formations = new Es1MechFormationsTransformer().Parse(content.ReadRequired("dat", "MFORMS.DAT"))
				?? new Es1MechFormations();
			// Flyer_LoadResources (0001c558) asserts when datforms does not open.
			var flyerFormations = new Es1FlyerFormationsTransformer().Parse(content.ReadRequired("dat", "FFORMS.DAT"))
				?? new Es1FlyerFormations();
			// PLACEHOLDER: Sim_StartMission's step 6 sets option 1 to 0 around the spawn (Prefs_SetOption(1, 0, 1), then
			// Prefs_RevertOption(1, 1)), which leaves Prefs_PreviousOptions[1] changed; not ported. The spawn runs before
			// Sound_Init here, with no sound manager, so it plays nothing, as retail's with the digital sounds off.
			world = Es1World.Spawn(script, content, formations, flyerFormations);
			world.Globals.Sound = sound;
			views = world.LocalPlayer is { } machine ? Es1CockpitViews.Load(content, machine.Type.CockpitName) : null;
			var scheme = views?.CockpitPalette is { } cockpit
				? new Es1CockpitScheme(cockpit, world.LocalPlayer!.Type.Type)
				: (Es1CockpitScheme?)null;
			scene = Es1ZoneScene.Load(content, theater, header[1], scheme);
			world.Globals.ZoneShapes = scene.ZoneShapes;
			// The ground shapes FlatObj_LoadResources loads, which the impact effects lay, by the world file's selector.
			world.Globals.FlatObjects.LoadShapes(content, scene.World?.FlatSetSelector ?? 0);
		} catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException) {
			Console.Error.WriteLine($"Cannot load the mission: {ex.Message}");
			return 1;
		}

		var player = world.LocalPlayer;
		Console.WriteLine(
			$"Zone {header[1]}, theater {theater.Name}, world {theater.WorldName}: {scene.Shapes.Count} shapes, " +
			$"{world.MechPool.Count} HERCs and {world.Globals.Flyers.Live.Count} flyers in {world.Groups.Count} groups" +
			(player != null ? $", the player in a {player.Type.Name}." : ", no player's machine."));
		if (views == null) {
			Console.Error.WriteLine("No cockpit view for the player's machine: the free camera only.");
		}

		Console.WriteLine("Keypad drives the HERC; F free camera (RMB + mouse to look, WASD/arrows, Q/E, Shift), " +
			"G textured/banded ground; close the window to quit" + (player == null ? " (or Esc)." : "."));
		string source = tape != null ? Path.GetFileName(tape.FilePath) : missionName ?? "data\\script.dat";
		return Show(installRoot, files.Preferences, content, scene, world, views, $"HERCULAN — Metaltech: Earthsiege — {source}, zone {header[1]}",
			tape, recording, window, Es1Sound.MissionStartDevices(installRoot, soundOn), silent,
			new MenuLayer(imguiFontPath, imguiItalicFontPath, menus));
	}

	/// <summary>The menu bar's faces and menus, which <see cref="Show"/> builds its <see cref="FrontEndWindow"/> with.</summary>
	private sealed record MenuLayer(string FontPath, string ItalicFontPath, IFrontEndMenus Menus);

	/// <summary>
	/// The window, until it closes. Returns the exit code <see cref="Es1Shutdown.Run"/> wrote when the mission ended,
	/// <see cref="Es1Shutdown.AssertCleanup"/>'s status when the tick raised a fatal assert
	/// (<see cref="Es1FatalAssertException"/>), and 0 when the window closed without an ending, which runs no shutdown and
	/// writes no file.
	/// </summary>
	/// <param name="tape">The tape playing, which the cockpit's mouse and the simulation read; none with no tape.</param>
	/// <param name="recording">The tape recording, which the simulation's input build appends to; none with no recording.</param>
	/// <param name="host">The launcher's shared window, or none for one of this turn's own.</param>
	/// <param name="devices">Step 3's six values from <c>resource.cfg</c>, which <c>Sound_Init</c> takes.</param>
	/// <param name="silent">No audio device: the sound plays into nothing.</param>
	/// <param name="menuLayer">The menu bar over the mission (the class summary).</param>
	private static int Show(string installRoot, Es1Preferences preferences, GameContent content, Es1ZoneScene scene,
			Es1World world, Es1CockpitViews? views, string title, Es1TapePlayback? tape, Es1TapeRecording? recording,
			HostWindow? host, Es1SoundDevices devices, bool silent, MenuLayer menuLayer) {
		using var owned = host == null ? new HostWindow() : null;
		using var window = new FrontEndWindow(host ?? owned!, title, screenshotPath: null, menuLayer.FontPath,
			menuLayer.ItalicFontPath, CaseInsensitivePath.Combine(installRoot, Es1MissionFiles.DataFolder), menuLayer.Menus);
		int exitCode = 0;

		// The audio device, opened for this turn as Earthsiege 2's shell opens its own, and closed with the window.
		string? audioFailure = null;
		var device = silent ? null : OpenAlBackend.TryCreate(out audioFailure);
		IAudioBackend audio = device as IAudioBackend ?? new NullAudioBackend();
		if (audioFailure != null) {
			Console.Error.WriteLine(device != null
				? $"Audio: {audioFailure}."
				: $"Audio unavailable ({audioFailure}) — the mission runs silent.");
		}

		Silk.NET.OpenGL.GL? glContext = null;
		SceneRenderer? renderer = null;
		SpriteRenderer? sprites = null;
		Es1Backdrop? backdrop = null;
		Es1WorldDraw? draw = null;
		Es1Monitor? monitor = null;
		Es1FrameLoop? frameLoop = null;
		Es1MechModels? models = null;
		var disposables = new List<IDisposable>();
		IKeyboard? keyboard = null;
		IMouse? mouse = null;
		Es1SimulationStart? simulation = null;

		var camera = new Camera();
		var cockpitCamera = new Camera();

		// With a simulation running the scene reads the simulator's options, which the preferences panel changes, each
		// frame; with none the viewer's own. Sim_StartMission applies the detail setting before the first frame.
		var backdropOptions = new Es1BackdropOptions(world.LocalPlayer != null ? preferences : null);
		var detail = new Es1AppliedDetail(preferences);
		// g_TSDetailBiasForMounts as the detail setting last applied it, which a thrown gun keeps.
		world.Globals.MountDetailBias = () => detail.Setting.MountBias;

		// Sim_StartMission's steps 9 to 13 that build no GL object: the views and their cockpit, and the chain of views
		// with its cameras, the main one on the player's eye. Step 9's LocalPlayerMech is set here, before step 14's lift
		// search reads it; Es1Simulation sets it again when the simulation is built.
		var player = world.LocalPlayer;
		world.Globals.LocalPlayerMech = player;
		// Step 3's Sound_Init, through the volumes the simulator mounts, and Snc_MissionInit, which puts the lip-sync
		// player on the PC timer; then, with no sound manager, Prefs_SetOption(1, 0, i) for options 0, 1 and 2.
		var sound = world.Globals.Sound;
		sound.Init(devices, preferences.LowMemory, world.Globals.VoiceLines, content.Read, audio);
		world.Globals.VoiceLines.MissionInit(world.Globals.Clock);
		if (sound.Manager == null) {
			for (int option = Es1Preferences.MusicOption; option <= Es1Preferences.RadioOption; option++) {
				preferences.SetOption(true, 0, option);
			}
		}

		Console.WriteLine(sound.Manager != null
			? $"Sound: digital device {devices.DigitalDevice:X4}, {sound.CatalogCount} catalog rows."
			: $"Sound: no sound manager ({Es1ResourceCfg.FileName}'s [SoundFX] Driver# is empty, missing, or -s was given).");
		// The mouse driver, which the host's mouse is attached to once the window has one; and step 5's
		// CockpitMouse_Init on it, which runs before the cockpit is built.
		var hostMouse = new Es1HostMouse();
		// Step 4's Widget_LoadStylesAndFonts, which the in-mission panels draw with.
		var widgets = Es1WidgetResources.Load(content);
		Es1ViewManager? manager = null;
		if (views != null && player != null) {
			var display = new Es1VideoMemory();
			var cockpitMouse = new Es1CockpitMouse(display.Cursor) { Tape = tape };
			cockpitMouse.Init(hostMouse);
			manager = new Es1ViewManager(views, display, player, cockpitMouse, world.Globals, preferences) {
				// The canvas a window wider than 4:3 shows around the monitor (plan decision 30).
				Canvas = new Es1Canvas(),
			};
		}

		var chain = player != null ? new Es1ViewChain(player, manager, Es1ViewCaption.Load(content)) : null;
		var freeCamera = new EditorCamera();
		if (chain != null) {
			chain.Camera.Update();
			freeCamera.ResetTo(chain.Camera.Position, chain.Camera.View.Z);
		}

		bool free = manager == null;
		bool looking = false;
		bool lookNeedsResync = false;
		bool groundKeyHeld = false;
		bool freeKeyHeld = false;
		Vector2 lastMousePos = Vector2.Zero;

		bool escapeQuits = false;

		// HERCULAN's pause menu over the mission, which the controller's PauseMenu input raises (Start by default).
		var pauseHost = new Es1PauseMenuHost(() => simulation?.Runner, () => frameLoop);
		var pauseMenu = new PauseMenu(menuLayer.Menus.Localization, pauseHost);
		bool pauseInputHeld = false;

		// The squad orders and the screens round the middle of the screen, which the controller's wheel inputs raise.
		var radials = new Es1PadRadials(() => simulation?.Runner, () => simulation?.Port);
		IInputContext? inputContext = null;

		window.Load += (gl, input) => {
			window.Attach(gl, input);
			glContext = gl;
			inputContext = input;
			if (scene.Horizon != null && scene.Palette != null) {
				backdrop = new Es1Backdrop(gl, scene.Horizon, scene.Ground, scene.Skyline, scene.Clouds, scene.Specks,
						scene.Palette) {
					Options = backdropOptions,
				};
				disposables.Add(backdrop);
			}

			// ES1 fades nothing with depth (docs/es1/rendering/polygon-shading.md), so the renderer's fog stays off.
			renderer = new SceneRenderer(gl) {
				Sky = null,
				SkyColor = MissingSky,
				FogEnabled = false,
				LightDirection = Es1Sun.Direction,
				SunShadeSlope = Es1Sun.ShadeSlope,
				EffectLights = world.Globals.EffectLights,
			};
			var spriteRenderer = new SpriteRenderer(gl);
			sprites = spriteRenderer;
			disposables.Add(spriteRenderer);
			var shadeRamps = scene.Shading.Table ?? SurfaceRampTable.Build(scene.Shading.Surfaces);
			var paletteRamp = scene.TheaterPalette != null && scene.Palette != null
				? Es1TextureShades.RampTable(scene.TheaterPalette, scene.Palette)
				: PaletteRampTable.Build(scene.Shading.Surfaces);
			renderer.SetShadeRamps(shadeRamps);
			renderer.SetPaletteRamp(paletteRamp);

			models = Es1MechModels.Upload(gl, content, world.Types.Values.OfType<Es1MechType>(), scene);
			disposables.Add(models);
			var projectileDraw = new Es1ProjectileDraw(gl, content, scene, world.Globals.Projectiles);
			disposables.Add(projectileDraw);
			var explosionDraw = new Es1ExplosionDraw(gl, content, scene, world.Globals.Explosions);
			disposables.Add(explosionDraw);
			var debrisDraw = new Es1DebrisDraw(gl, scene, world.Globals.Debris, models);
			disposables.Add(debrisDraw);
			var smokeDraw = new Es1SmokeDraw(gl, content, scene, world.Globals.Smoke);
			disposables.Add(smokeDraw);
			var flyerDraw = new Es1FlyerDraw(gl, scene, world.Globals.Flyers.Live, detail);
			disposables.Add(flyerDraw);
			var structureDraw = new Es1BaseDraw(gl, scene, world.Globals.Structures.Live, world.Globals.Structures.Types, detail);
			disposables.Add(structureDraw);
			draw = new Es1WorldDraw(gl, scene, world, models, detail, disposables, projectileDraw, explosionDraw, debrisDraw,
				smokeDraw, flyerDraw, structureDraw);
			if (manager != null && chain != null) {
				monitor = new Es1Monitor(gl, manager.Display, manager);
				disposables.Add(monitor);
				var shownMonitor = monitor;
				manager.GlanceReach = () => shownMonitor.GlanceReach(window.FramebufferSize);
				manager.CanvasShown = () => shownMonitor.ShowsCanvas(window.FramebufferSize);
				monitor.SetPalette(scene.Palette);
				// The cockpit script's palette commands upload to the same live palette, and the shield display's rings are
				// installed into it.
				manager.Cockpit.Scripts.UploadPalette = monitor.SetPalette;
				manager.Cockpit.Shields.InstallPaletteRange = monitor.InstallRange;
				monitor.Fit(window.FramebufferSize);
				var worldDraw = draw;
				var sceneRenderer = renderer;
				// A cockpit rect's view, the NAV MAP's or the MISSILE CAM's, from a camera of its own (plan decision 17); the
				// MISSILE CAM's horizon is its own hzline, with no clouds or scattered points.
				var rectCamera = new Camera();
				Es1Backdrop? missileHorizon = null;
				if (scene.Palette is { } palette && Es1MfdMissileCamScreen.Horizon(palette) is { } horizon) {
					missileHorizon = new Es1Backdrop(gl, horizon, null,
						Es1Skyline.Load(content, Es1MfdMissileCamScreen.SkylineBank, palette), null, null, palette) {
						Options = backdropOptions,
						FocalPixels = 1 << Es1MfdMissileCamScreen.FocalShift,
					};
					disposables.Add(missileHorizon);
				}

				// The world's colours follow the live palette as the cockpit's do, rebuilt before a pass when it changed.
				var liveMonitor = monitor;
				var worldPalette = new Es1WorldPalette(sceneRenderer, shadeRamps, paletteRamp,
					new[] { backdrop, missileHorizon }.OfType<Es1Backdrop>().ToArray(), liveMonitor);
				monitor.WorldPass = (x, y, width, height) => {
					worldPalette.Follow(liveMonitor);
					DrawWorld(gl, sceneRenderer, spriteRenderer, backdrop, worldDraw, cockpitCamera, x, y, width, height);
				};
				monitor.ViewPass = (view, size, viewport) => {
					worldPalette.Follow(liveMonitor);
					DrawRectView(gl, sceneRenderer, spriteRenderer, worldDraw, missileHorizon, rectCamera, view, size, viewport);
				};
				// The gunsight's pick, answered from what that pass drew (plan decision 16).
				var pick = new Es1WorldPick(gl, sceneRenderer, worldDraw);
				disposables.Add(pick);
				monitor.WorldPick = viewport => pick.ObjectAt(cockpitCamera, viewport);
				// Cam_Update on ViewObjectPtr, whichever camera the views have made it; the 3D pass draws from it with the
				// installed window's projection, the machine the camera rides in left out of the draw.
				frameLoop = new Es1FrameLoop(manager, () => chain.Camera.Update(), () => {
					var viewCamera = chain.Camera;
					viewCamera.ApplyTo(cockpitCamera, manager.Window);
					worldDraw.Update(cockpitCamera, viewCamera.MountedObject);
				}, detail, () => chain.Camera);
				// The death flash's palettes and fades are installed into the same live palette.
				frameLoop.InstallPaletteRange = monitor.InstallRange;
				frameLoop.LivePalette = () => liveMonitor.Palette;
				frameLoop.World = scene.World;
				frameLoop.StartMission(Es1CursorImage.Load(content, "CURSOR"),
					() => Panels.Es1WidgetResources.LoadFrames(content, "INTRO")?.FirstOrDefault());
			}

			keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
			mouse = input.Mice.Count > 0 ? input.Mice[0] : null;
			hostMouse.Pointer = () => monitor?.CockpitPointer(window.FramebufferSize, window.ClientSize)
				?? Es1Monitor.Pointer(window.FramebufferSize, window.ClientSize);
			hostMouse.HostHasPointer = () => window.ImGuiWantsMouse;
			if (mouse != null) {
				hostMouse.Attach(mouse);
			}

			// The window as ES1 last ran.
			var fullScreenKey = Es1FullScreen.Start(window);
			if (world.LocalPlayer != null) {
				simulation = new Es1SimulationStart(world, installRoot, preferences, input, keyboard, hostMouse,
					chain, widgets, content, tape, recording, fullScreenKey) {
					MenuBarWindow = window,
					HostMenusMute = () => pauseMenu.Showing || radials.MutesStick,
				};
				disposables.Add(simulation);
			} else if (keyboard is { } device) {
				// With no simulator to take the keyboard, the toggle and the menu bar read it themselves, and an [Esc] neither
				// takes quits.
				device.KeyDown += (_, key, _) => {
					if (!fullScreenKey.KeyDown(key, device.IsKeyPressed) && !Es1HostKeyboard.MenuBarTakes(window, key)
							&& key == Key.Escape) {
						escapeQuits = true;
					}
				};
				device.KeyUp += (_, key, _) => fullScreenKey.KeyUp(key);
			}

			// As in both other hosts: DTS geometry isn't reliably wound, so nothing is backface-culled.
			gl.Disable(Silk.NET.OpenGL.EnableCap.CullFace);

			if (mouse != null) {
				mouse.MouseDown += (m, button) => {
					if (button == MouseButton.Right && free && !window.ImGuiWantsMouse) {
						looking = true;
						lookNeedsResync = true;
						m.Cursor.CursorMode = CursorMode.Disabled;
					}
				};

				mouse.MouseUp += (m, button) => {
					if (button == MouseButton.Right && looking) {
						looking = false;
						m.Cursor.CursorMode = CursorMode.Normal;
					}
				};
			}
		};

		window.Update += deltaSeconds => {
			sound.UpdateDriver();
			audio.Update();
			hostMouse.TakeBackPointer();

			// With no simulation, an [Esc] the menu bar did not take quits; with one, Esc is the simulator's view key and the
			// window's close quits.
			if (escapeQuits) {
				window.Close();
				return;
			}

			var runner = simulation?.Advance();

			// The pause menu's input raises the menu or resumes from it as it goes down, read while the window has the focus.
			bool pauseInput = window.HasFocus && simulation?.Port?.HostActionInput(KeyAction.PauseMenu) != null;
			if (pauseInput && !pauseInputHeld) {
				pauseMenu.Start();
			}

			pauseInputHeld = pauseInput;

			// A radial opens only where its choice's key would be heard: in the cockpit view with its widgets on, since the
			// [Alt] order keys and the function keys go through them, and with nothing else holding the input. A replay's keys
			// are the tape's. Not Es1FrameLoop.Waiting, which also holds while the frame's work waits on a retrace past the
			// host frame, as it does most host frames.
			if (inputContext != null) {
				bool radialsAllowed = window.HasFocus && !free && runner is { Simulation: var running }
					&& !running.TapePlayback && (running.Panels?.OpenCount ?? 0) == 0 && running.Cockpit is { WidgetsOff: false }
					&& !pauseMenu.IsOpen && !window.MenuBarUp;
				radials.Update(inputContext, radialsAllowed);
			}

			bool ended;
			try {
				ended = frameLoop != null
					? frameLoop.AdvanceHostFrame(deltaSeconds, runner)
					: runner?.AdvanceHostFrame(TimeSpan.FromSeconds(deltaSeconds)) == Es1SimulationRunner.Frame.MissionEnded;
			} catch (Es1FatalAssertException assert) {
				// Assert_Report's fatal path: the hooks, Sim_AssertCleanupHook's int16 0 to data\exit.cfg among them, then
				// __exit(1); no shutdown runs.
				Console.Error.WriteLine(assert.Message);
				exitCode = Es1Shutdown.AssertCleanup(installRoot, sound);
				window.Close();
				return;
			}

			pauseHost.Update();

			// Sim_Main leaves its loop for Sim_Shutdown; only a tick ends the mission, so the runner is there.
			if (ended && runner != null) {
				exitCode = Es1Shutdown.Run(runner.Simulation, installRoot, runner.AxisKeys.DemoMode);
				Console.WriteLine($"Mission over: exit code {exitCode}.");
				window.Close();
				return;
			}

			scene.Clouds?.Advance(deltaSeconds);

			// The engine aids' keys are the menu bar's while a field of its windows is typed into.
			bool aidKeys = !window.ImGuiWantsKeyboard;
			bool groundKey = aidKeys && keyboard?.IsKeyPressed(Key.G) == true;
			if (groundKey && !groundKeyHeld) {
				backdropOptions.ToggleGround();
			}

			groundKeyHeld = groundKey;

			bool freeKey = aidKeys && keyboard?.IsKeyPressed(Key.F) == true;
			if (freeKey && !freeKeyHeld && manager != null) {
				free = !free;
				camera = new Camera();
			}

			freeKeyHeld = freeKey;

			// The first sample after the pointer is grabbed only re-syncs, for the reason the mission editor's
			// Program.cs gives at lookNeedsResync.
			Vector2 lookDelta = Vector2.Zero;
			if (looking && mouse != null) {
				var position = mouse.Position;
				if (lookNeedsResync) {
					lookNeedsResync = false;
				} else {
					lookDelta = position - lastMousePos;
				}

				lastMousePos = position;
			}

			if (free) {
				freeCamera.Update(deltaSeconds, keyboard, lookDelta, looking, acceptKeyboardInput: aidKeys);
				freeCamera.ApplyTo(camera);
			}

			// The host's pointer is hidden over the window while the cockpit's cursor stands in for it, and shown over the
			// menu bar and its windows.
			if (mouse != null && !looking) {
				var mode = !free && monitor != null && !window.ImGuiWantsMouse ? CursorMode.Hidden : CursorMode.Normal;
				if (mouse.Cursor.CursorMode != mode) {
					mouse.Cursor.CursorMode = mode;
				}
			}
		};

		window.Render += (delta, _) => {
			window.BeginFrame(delta);
			if (renderer != null && draw != null) {
				var size = window.FramebufferSize;
				if (free || monitor == null) {
					draw.Update(camera, null);
					renderer.Clear();
					DrawWorld(glContext!, renderer, sprites!, backdrop, draw, camera, 0, 0, size.X, size.Y);
				} else {
					monitor.Present(size);
				}
			}

			pauseMenu.Draw();
			if (window.RadialFont is { } radialFont) {
				radials.Draw(radialFont);
			}

			window.DrawMenuBar(revealOnHover: false);
		};

		window.Closing += () => {
			window.DisposeImGui();
			hostMouse.Detach();
			renderer?.Dispose();
			foreach (var disposable in disposables) {
				disposable.Dispose();
			}

			audio.Dispose();
		};

		window.Run();
		return exitCode;
	}

	/// <summary>
	/// One view of the world: the backdrop and the shadows, then over them the zone's shapes, the HERCs, the projectiles
	/// and the impact effects, the two passes <see cref="Es1WorldDraw"/> describes, and last the effects' billboards. The
	/// depth between the passes is cleared so no object is hidden by a shadow.
	/// </summary>
	private static void DrawWorld(Silk.NET.OpenGL.GL gl, SceneRenderer renderer, SpriteRenderer sprites, Es1Backdrop? backdrop,
			Es1WorldDraw draw, Camera camera, int x, int y, int width, int height) {
		renderer.Backdrop = backdrop;
		renderer.Render(camera, draw.Shadows, x, y, width, height);
		gl.Clear(Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);
		renderer.Backdrop = null;
		renderer.Render(camera, draw.Objects, x, y, width, height);
		sprites.Render(camera, draw.Sprites, width, height);
	}

	/// <summary>
	/// A camera's view into a cockpit rect of <paramref name="size"/> screen pixels, drawn in <paramref name="viewport"/>
	/// from <paramref name="camera"/> placed and projected as <paramref name="view"/> says: for
	/// <see cref="Es1RectScene.FlatZoneShapes"/>, <see cref="Es1WorldDraw.FlatZoneShapes"/> with no backdrop; for
	/// <see cref="Es1RectScene.HddMapZoneShapes"/>, <see cref="Es1WorldDraw.HddMapZoneShapes"/> the same way; for
	/// <see cref="Es1RectScene.MissileCamera"/>, the forward view's pass (<see cref="DrawWorld"/>) over
	/// <paramref name="missileHorizon"/> with the items placed for the camera (<see cref="Es1WorldDraw.DrawFlatView"/>).
	/// </summary>
	private static void DrawRectView(Silk.NET.OpenGL.GL gl, SceneRenderer renderer, SpriteRenderer sprites, Es1WorldDraw draw,
			Es1Backdrop? missileHorizon, Camera camera, Es1RectView view, (int Width, int Height) size,
			(int X, int Y, int Width, int Height) viewport) {
		Es1Camera.ApplyTo(camera, view, size.Width, size.Height);
		if (view.Scene == Es1RectScene.MissileCamera) {
			if (missileHorizon != null) {
				missileHorizon.CentrePageColumn = view.Left + view.Centre.X;
			}

			draw.DrawFlatView(camera, view.FocalShift,
				() => DrawWorld(gl, renderer, sprites, missileHorizon, draw, camera, viewport.X, viewport.Y, viewport.Width, viewport.Height));
			return;
		}

		IReadOnlyList<SceneItem> items = view.Scene switch {
			Es1RectScene.FlatZoneShapes => draw.FlatZoneShapes,
			Es1RectScene.HddMapZoneShapes => draw.HddMapZoneShapes,
			_ => [],
		};

		// PLACEHOLDER: the faces take the sun alone. Retail draws the view outside the object list's per-object light
		// selection (docs/es1/rendering/polygon-shading.md#effect-lights), and which effect lights its faces then take
		// is not settled; this engine's ES1 draw lights nothing else either.
		renderer.Backdrop = null;
		renderer.Render(camera, items, viewport.X, viewport.Y, viewport.Width, viewport.Height);
	}
}
