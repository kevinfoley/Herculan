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
/// The header words that a launch can override as engine aids. Retail has no way to name them: the theater (word 0), the
/// world file's variant (word 9) and its detail (word 8). A null leaves a word as the mission's files set it.
/// </summary>
public sealed record Es1HeaderOverrides(int? Theater, int? WorldVariant, int? WorldDetail);

/// <summary>
/// Runs an ES1 mission as far as this engine has ported one (docs/herculan/plan-es1-first-mission.md, steps 4, 6a and 6a2).
/// It covers:
/// <list type="bullet">
/// <item><description>the shell's single-mission load, when a mission is named;</description></item>
/// <item><description>the simulator's world load, from the install's <c>data\</c> files;</description></item>
/// <item><description>the tick, driven by the keyboard, the mouse and a controller;</description></item>
/// <item><description>the cockpit's views of the player's machine, shown on the display's pages with the cockpit's screen edges and cursors; and</description></item>
/// <item><description>the pause and quit prompts, the objectives and the preferences panel.</description></item>
/// </list>
///
/// This class only composes other types:
/// <list type="bullet">
/// <item><description><see cref="Es1SingleMission"/> does the load, and <see cref="Es1MissionFiles"/> reads its files.</description></item>
/// <item><description><see cref="Es1World"/> is the world.</description></item>
/// <item><description><see cref="Es1SimulationRunner"/> runs the tick, and <see cref="Es1SimulationStart"/> builds it.</description></item>
/// <item><description><see cref="Es1ZoneScene"/> is the zone.</description></item>
/// <item><description><see cref="Es1WorldDraw"/> draws the world.</description></item>
/// <item><description><see cref="Es1VideoMemory"/> is the display, and <see cref="Es1ViewManager"/> manages its views.</description></item>
/// <item><description><see cref="Es1FrameLoop"/> runs the frame on them.</description></item>
/// <item><description><see cref="Es1ScreenPresenter"/> presents the screen.</description></item>
/// </list>
///
/// <para>Each host frame runs a tick when the clock calls for one. After the tick it sets the camera, draws the world into the
/// display's draw page and ends the frame, as <c>Sim_Main</c>'s frame does (docs/es1/simulation/main-loop.md#the-frame). Every
/// host frame also shows the display's 320 × 200 screen. The window's update and its render run on the same thread, and both
/// have the GL context current, so the tick's 3D pass is drawn during the update.</para>
///
/// <para><b>Not retail, awaiting the user's decision:</b> with no player's machine (only the free camera), no tick runs.
/// Retail's controls read <c>LocalPlayerMech</c> without checking it, so retail has no behavior to port for this case.</para>
///
/// <para>F switches to a free camera, an engine aid with no retail counterpart. The free camera shows the world over the whole
/// window, with the player's machine drawn. The simulator still reads every key, so the keys that move the free camera also
/// drive the machine. While the cockpit is shown, the host's pointer is hidden over the window and the cockpit's cursor takes
/// its place. The free camera shows the pointer, and hides it only while the right button is held to look around.</para>
///
/// <para>G, another engine aid, switches the ground between textured and banded (<see cref="Es1BackdropOptions.ToggleGround"/>).
/// With a simulation running, it overrides the choice of preferences option 5 until it is pressed again, without writing the
/// option. With no simulation, it switches the viewer's own option 5.</para>
///
/// <para>Closing the window quits. Esc quits only when there is no player's machine, since with one Esc is the simulator's key
/// (docs/es1/simulation/main-loop.md#commands--sim_dispatchcommand-00059df8). The mission also quits when it ends: through
/// retail's way out (the QUIT from <c>Ctrl+Q</c> or <c>Ctrl+Esc</c>; plan decision 12), through a status panel's answer that ends it
/// (docs/es1/simulation/main-loop.md#which-answer-ends-the-mission), or through the death flash
/// (docs/es1/simulation/main-loop.md#the-death-flash--sim_deathflash-00058414). When a mission ends,
/// <see cref="Es1Shutdown.Run"/> runs before the window closes. Closing the window has no retail counterpart: it runs nothing and
/// writes no file, so <c>data\exit.cfg</c> keeps the 0 written when the run started.</para>
///
/// <para><b>This engine's own:</b> HERCULAN's menu bar is drawn over the mission, in a <see cref="FrontEndWindow"/> like the
/// shell's. It covers either the screen or the free camera. The bar stays hidden until one of these raises it: [Shift+Esc], a
/// gamepad's Back, or a joystick's Custom menu bar input (<see cref="Es1HostKeyboard.MenuBarTakes"/>,
/// <see cref="FrontEndWindow.BeginFrame"/>, <see cref="Es1ControllerPort.JoystickMenuBarInput"/>). The pointer at the top of
/// the window does not raise it. The mission keeps running under the bar, as Earthsiege 2's does. The controller is muted while
/// the bar or one of its windows is up (<see cref="Es1SimulationStart.MenuBarWindow"/>). While the pointer is over the bar or
/// one of its windows, the system's pointer shows and the cockpit receives no mouse events. The engine aids' keys do nothing while
/// a field in one of those windows is being typed into.</para>
/// </summary>
public static class Es1MissionHost {
	/// <summary>The clear color used when the theater's sky cannot be built. It is a mid gray, plainly not a retail sky.</summary>
	private static readonly Vector3 MissingSky = new(0.32f, 0.32f, 0.36f);

	/// <summary>
	/// Starts <paramref name="missionName"/> (with or without <c>.MSN</c>) as a single mission and runs the simulator on the files
	/// written by that load. With no mission named, it runs the simulator on the files already in the install's <c>data\</c> folder,
	/// as <c>DBSIM.EXE</c> does. Returns the process exit code: 1 when <c>data\exit.cfg</c> cannot be opened for writing or the
	/// mission fails to load, and otherwise the code from <see cref="Show"/>. Returning the code held in <c>data\exit.cfg</c> as the
	/// process's exit code is an addition of this engine: retail's <c>Sim_Main</c> returns nothing, and the shell reads the file.
	///
	/// <para>With <paramref name="playTape"/> (<c>--play</c>, retail's <c>-p</c>) or <paramref name="demo"/> (<c>--demo</c>,
	/// retail's <c>-D</c>; combined with <c>--play</c>, that tape plays in demo mode), the host opens the tape and unpacks its bundle
	/// over the install's <c>data\</c> mission files, <c>tapes\prefs.cfg</c> and <c>tapes\keyjoy.cfg</c>. <c>Sim_ParseCommandLine</c>
	/// does the same before the mission loads (<see cref="Es1TapePlayback.FromCommandLine"/>). The tape carries its own mission, so
	/// a named mission is not loaded. Returns 1 when a named tape is not found or cannot be read.</para>
	///
	/// <para>With <paramref name="recordTape"/> (<c>--record</c>, retail's <c>-r</c>), the host creates the tape at the path given
	/// by the flag and packs the install's files into its bundle (<see cref="Es1TapeRecording.Start"/>). Every input build then
	/// appends its frame. Retail packs the bundle while it parses the command line, after the shell has written the mission's
	/// <c>data\</c> files. Here a named mission's load writes those files, so the bundle is packed after that load, from the
	/// mission's own files. Returns 1 when the tape cannot be opened for writing.</para>
	/// </summary>
	/// <param name="imguiFontPath">The font file for the menu bar's ImGui text (<see cref="FrontEndWindow"/>).</param>
	/// <param name="menus">HERCULAN's menu bar and its windows over the mission. The window disposes of them once the mission has run.</param>
	/// <param name="window">
	/// The launcher's window, shared by the shell, the movies and the missions it runs (<see cref="Es1Launcher"/>). It stays open
	/// after the mission. When null, the simulator opens a window of its own and closes it with the mission.
	/// </param>
	/// <param name="soundOn">
	/// The flag at <c>000b5b68</c>, which is 1 in the image and which <c>-s</c> toggles. When it is clear, starting the mission creates
	/// no sound manager (<see cref="Es1Sound.MissionStartDevices"/>).
	/// </param>
	/// <param name="silent">The host's <c>--no-sound</c>. It opens no audio device, so the sound manager plays into nothing.</param>
	public static int Run(string installRoot, string? missionName, Es1HeaderOverrides overrides, string imguiFontPath,
			string imguiItalicFontPath, IFrontEndMenus menus, string? playTape = null, bool demo = false,
			string? recordTape = null, HostWindow? window = null, bool soundOn = true, bool silent = false) {
		// Sim_Main's step 4, which runs before the command line is parsed (docs/es1/simulation/main-loop.md#starting--sim_main-00059350):
		// write an int16 0 to data\exit.cfg. A run that ends before Sim_Shutdown therefore leaves 0, which ends the launcher's loop.
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

		// The sound file's globals. The preferences' handlers start switching them when Prefs_Init loads (step 2), so they exist
		// before the world they belong to is built.
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

		// The simulator mounts SIMVOL0 (program mask 1) and ZONES (0x101), and searches them by ES2's precedence rule.
		// ES1's own volume code has not been read (docs/es1/formats/zone-terrain.md#open). ES1 takes its CD drive from ALT_DRIVE, not
		// drive.cfg, so no disc is mounted. ES1 reads no file that varies by language, so the language is English.
		var content = GameContent.MountInstall(installRoot, disc: null, Voln.DbsimProgram, GameLanguage.English);
		// Unlike retail, the HERCs and the cockpit are loaded before the zone. The scene is therefore built in the live palette, with
		// the scheme of the player's chassis already installed (Es1ZoneScene.Palette). With no player's machine, nothing is installed.
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
			// Prefs_RevertOption(1, 1)). That leaves Prefs_PreviousOptions[1] changed, which is not ported. Here the spawn runs before
			// Sound_Init, with no sound manager, so it plays nothing. Retail's spawn is silent the same way when the digital sounds are
			// off.
			world = Es1World.Spawn(script, content, formations, flyerFormations);
			world.Globals.Sound = sound;
			views = world.LocalPlayer is { } machine ? Es1CockpitViews.Load(content, machine.Type.CockpitName) : null;
			var scheme = views?.CockpitPalette is { } cockpit
				? new Es1CockpitScheme(cockpit, world.LocalPlayer!.Type.Type)
				: (Es1CockpitScheme?)null;
			scene = Es1ZoneScene.Load(content, theater, header[1], scheme);
			world.Globals.ZoneShapes = scene.ZoneShapes;
			world.Globals.ImpactPalette.Load(scene.ImpactPalette);
			// FlatObj_LoadResources loads the ground shapes that the impact effects lay. The world file's selector chooses the set.
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

	/// <summary>The menu bar's font paths and menus, which <see cref="Show"/> passes to its <see cref="FrontEndWindow"/>.</summary>
	private sealed record MenuLayer(string FontPath, string ItalicFontPath, IFrontEndMenus Menus);

	/// <summary>
	/// Runs the window until it closes. Returns the exit code that <see cref="Es1Shutdown.Run"/> wrote when the mission ended. When
	/// the tick raised a fatal assert (<see cref="Es1FatalAssertException"/>), it returns the status from
	/// <see cref="Es1Shutdown.AssertCleanup"/>. When the window closed without the mission ending, it returns 0, because that runs
	/// no shutdown and writes no file.
	/// </summary>
	/// <param name="tape">The tape being played, which the cockpit's mouse and the simulation read. Null when no tape is playing.</param>
	/// <param name="recording">The tape being recorded. The simulation appends a frame to it on each input read. Null when nothing is recorded.</param>
	/// <param name="host">The launcher's shared window. When null, this run opens a window of its own.</param>
	/// <param name="devices">The six values from <c>resource.cfg</c> for step 3, which are passed to <c>Sound_Init</c>.</param>
	/// <param name="silent">No audio device: the sound plays into nothing.</param>
	/// <param name="menuLayer">The menu bar over the mission (the class summary).</param>
	private static int Show(string installRoot, Es1Preferences preferences, GameContent content, Es1ZoneScene scene,
			Es1World world, Es1CockpitViews? views, string title, Es1TapePlayback? tape, Es1TapeRecording? recording,
			HostWindow? host, Es1SoundDevices devices, bool silent, MenuLayer menuLayer) {
		using var owned = host == null ? new HostWindow() : null;
		using var window = new FrontEndWindow(host ?? owned!, title, screenshotPath: null, menuLayer.FontPath,
			menuLayer.ItalicFontPath, CaseInsensitivePath.Combine(installRoot, Es1MissionFiles.DataFolder), menuLayer.Menus);
		int exitCode = 0;

		// The audio device. It is opened for this run, as Earthsiege 2's shell opens its own, and closed with the window.
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
		Es1ScreenPresenter? presenter = null;
		Es1FrameLoop? frameLoop = null;
		Es1MechModels? models = null;
		var disposables = new List<IDisposable>();
		IKeyboard? keyboard = null;
		IMouse? mouse = null;
		Es1SimulationStart? simulation = null;

		var camera = new Camera();
		var cockpitCamera = new Camera();

		// With a simulation running, the scene reads the simulator's options every frame, and the preferences panel changes those.
		// With none, it reads the viewer's own options. Sim_StartMission applies the detail setting before the first frame.
		var backdropOptions = new Es1BackdropOptions(world.LocalPlayer != null ? preferences : null);
		var detail = new Es1AppliedDetail(preferences);
		// g_TSDetailBiasForMounts, as set by the detail setting that was applied last. A thrown gun keeps this value.
		world.Globals.MountDetailBias = () => detail.Setting.MountBias;

		// The parts of Sim_StartMission's steps 9 to 13 that build no GL object: the views and their cockpit, and the chain of views
		// with its cameras (the main camera sits at the player's eye). Step 9 sets LocalPlayerMech here, before step 14's lift search
		// reads it. Es1Simulation sets it again when the simulation is built.
		var player = world.LocalPlayer;
		world.Globals.LocalPlayerMech = player;
		// Step 3's Sound_Init, which reads from the volumes the simulator mounts, then Snc_MissionInit, which puts the lip-sync player
		// on the PC timer. With no sound manager, Prefs_SetOption(1, 0, i) then runs for options 0, 1 and 2.
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
		// The mouse driver. The host's mouse is attached to it once the window has one. Step 5's CockpitMouse_Init also runs on it,
		// before the cockpit is built.
		var hostMouse = new Es1HostMouse();
		// Step 4's Widget_LoadStylesAndFonts. The in-mission panels draw with what it loads.
		var widgets = Es1WidgetResources.Load(content);
		Es1ViewManager? manager = null;
		if (views != null && player != null) {
			var display = new Es1VideoMemory();
			var cockpitMouse = new Es1CockpitMouse(display.Cursor) { Tape = tape };
			cockpitMouse.Init(hostMouse);
			manager = new Es1ViewManager(views, display, player, cockpitMouse, world.Globals, preferences) {
				// The canvas shown around the screen in a window wider than 4:3 (plan decision 30).
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

		// HERCULAN's pause menu over the mission. The controller's PauseMenu input raises it (Start by default).
		var pauseHost = new Es1PauseMenuHost(() => simulation?.Runner, () => frameLoop);
		var pauseMenu = new PauseMenu(menuLayer.Menus.Localization, pauseHost);
		bool pauseInputHeld = false;
		bool menuBarInputHeld = false;

		// The radial menus around the middle of the screen, for squad orders and screens. The controller's wheel inputs raise them.
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

			// The renderer's fog is ES2's: it fades depth through the .RMP lighting table and its distance bands, so it stays off.
			// ES1 has no lighting table. Its distance fade comes from two sources: its own bands, which Es1Shading uses to re-point each
			// object's ramps (docs/es1/rendering/polygon-shading.md#the-distance-bands), and the textured ground's fade tables
			// (Es1TexturedGround, docs/es1/rendering/ground-and-sky.md#the-textured-ground).
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
			// The palette that the effects' billboards are shown through. It is the zone's until the live palette changes.
			var billboardPalette = scene.Palette != null ? new PaletteTexture(gl, scene.Palette) : null;
			if (billboardPalette != null) {
				disposables.Add(billboardPalette);
			}

			var explosionDraw = new Es1ExplosionDraw(gl, content, scene, world.Globals.Explosions, billboardPalette);
			disposables.Add(explosionDraw);
			var debrisDraw = new Es1DebrisDraw(gl, scene, world.Globals.Debris, models);
			disposables.Add(debrisDraw);
			var smokeDraw = new Es1SmokeDraw(gl, content, scene, world.Globals.Smoke, billboardPalette);
			disposables.Add(smokeDraw);
			var flyerDraw = new Es1FlyerDraw(gl, scene, world.Globals.Flyers.Live, detail);
			disposables.Add(flyerDraw);
			var structureDraw = new Es1BaseDraw(gl, scene, world.Globals.Structures.Live, world.Globals.Structures.Types, detail);
			disposables.Add(structureDraw);
			draw = new Es1WorldDraw(gl, scene, world, models, detail, disposables, projectileDraw, explosionDraw, debrisDraw,
				smokeDraw, flyerDraw, structureDraw);
			if (manager != null && chain != null) {
				presenter = new Es1ScreenPresenter(gl, manager.Display, manager);
				disposables.Add(presenter);
				var shownPresenter = presenter;
				manager.GlanceReach = () => shownPresenter.GlanceReach(window.FramebufferSize);
				manager.CanvasShown = () => shownPresenter.ShowsCanvas(window.FramebufferSize);
				presenter.SetPalette(scene.Palette);
				// The cockpit script's palette commands upload to the same live palette, and the shield display's rings are
				// installed into it.
				manager.Cockpit.Scripts.UploadPalette = presenter.SetPalette;
				manager.Cockpit.Shields.InstallPaletteRange = presenter.InstallRange;
				// The hit shake's palette switches make whole palettes live on it.
				var impactPresenter = presenter;
				world.Globals.ImpactPalette.LivePalette = () => impactPresenter.Palette;
				world.Globals.ImpactPalette.SetLivePalette = presenter.SetPalette;
				presenter.Fit(window.FramebufferSize);
				var worldDraw = draw;
				var sceneRenderer = renderer;
				// A cockpit rect's view (the NAV MAP's or the MISSILE CAM's) is drawn from a camera of its own (plan decision 17).
				// The MISSILE CAM's horizon is its own hzline, with no clouds or scattered points.
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

				// The world's colors follow the live palette, as the cockpit's do. They are rebuilt before a pass when the palette has
				// changed.
				var livePresenter = presenter;
				var worldPalette = new Es1WorldPalette(sceneRenderer, shadeRamps, paletteRamp,
					new[] { backdrop, missileHorizon }.OfType<Es1Backdrop>().ToArray(), billboardPalette, livePresenter);
				presenter.WorldPass = (x, y, width, height) => {
					worldPalette.Follow(livePresenter);
					DrawWorld(gl, sceneRenderer, spriteRenderer, backdrop, worldDraw, cockpitCamera, x, y, width, height);
				};
				presenter.ViewPass = (view, size, viewport) => {
					worldPalette.Follow(livePresenter);
					DrawRectView(gl, sceneRenderer, spriteRenderer, worldDraw, missileHorizon, rectCamera, view, size, viewport);
				};
				// The gunsight's pick is answered from what that pass drew (plan decision 16).
				var pick = new Es1WorldPick(gl, sceneRenderer, worldDraw);
				disposables.Add(pick);
				presenter.WorldPick = viewport => pick.ObjectAt(cockpitCamera, viewport);
				// Cam_Update on ViewObjectPtr, which holds whichever camera the views have selected. The 3D pass draws from that camera with
				// the installed window's projection, and leaves out the machine that carries the camera.
				frameLoop = new Es1FrameLoop(manager, () => chain.Camera.Update(), () => {
					var viewCamera = chain.Camera;
					viewCamera.ApplyTo(cockpitCamera, manager.Window);
					worldDraw.Update(cockpitCamera, viewCamera.MountedObject);
				}, detail, () => chain.Camera);
				// The death flash's palettes and fades are installed into the same live palette.
				frameLoop.InstallPaletteRange = presenter.InstallRange;
				frameLoop.LivePalette = () => livePresenter.Palette;
				frameLoop.World = scene.World;
				frameLoop.StartMission(Es1CursorImage.Load(content, "CURSOR"),
					() => Panels.Es1WidgetResources.LoadFrames(content, "INTRO")?.FirstOrDefault());
			}

			keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
			mouse = input.Mice.Count > 0 ? input.Mice[0] : null;
			hostMouse.Pointer = () => presenter?.CockpitPointer(window.FramebufferSize, window.ClientSize)
				?? Es1ScreenPresenter.Pointer(window.FramebufferSize, window.ClientSize);
			hostMouse.HostHasPointer = () => window.ImGuiWantsMouse;
			if (mouse != null) {
				hostMouse.Attach(mouse);
			}

			// Starts the window as the last [Alt+Enter] left it.
			var fullScreenKey = Es1FullScreen.Start(window);
			if (world.LocalPlayer != null) {
				simulation = new Es1SimulationStart(world, installRoot, preferences, input, keyboard, hostMouse,
					chain, widgets, content, tape, recording, fullScreenKey) {
					MenuBarWindow = window,
					HostMenusMute = () => pauseMenu.Showing || radials.MutesStick,
				};
				disposables.Add(simulation);
			} else if (keyboard is { } device) {
				// With no simulator, the full-screen toggle and the menu bar read the keyboard themselves. [Esc] quits unless the menu bar
				// handles it (Es1HostKeyboard.MenuBarTakes).
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

			// With no simulation, [Esc] quits unless it is a menu bar key: [Shift+Esc] raises the bar, and [Esc] backs out of it while it is
			// up (Es1HostKeyboard.MenuBarTakes). With one, Esc is the simulator's view key, so only closing the window quits.
			if (escapeQuits) {
				window.Close();
				return;
			}

			var runner = simulation?.Advance();

			// Each press of the pause menu's input opens the menu, or closes it to resume. Holding it does nothing more. It is read only
			// while the window has focus.
			bool pauseInput = window.HasFocus && simulation?.Port?.HostActionInput(KeyAction.PauseMenu) != null;
			if (pauseInput && !pauseInputHeld) {
				pauseMenu.Start();
			}

			pauseInputHeld = pauseInput;

			// Each press of a joystick's menu bar input raises the bar or backs out of it, as a gamepad's Select/Back does.
			var menuBarInput = window.HasFocus ? simulation?.Port?.JoystickMenuBarInput : null;
			if (menuBarInput is { } pressed && !menuBarInputHeld) {
				window.MenuBarInputPressed(pressed);
			}

			menuBarInputHeld = menuBarInput != null;

			// A radial opens only where the keys its choices stand in for would work: in the cockpit view with its widgets on, since the
			// [Alt] order keys and the function keys go through the widgets, and with nothing else holding the input. A replay takes its
			// keys from the tape, so none opens. Not Es1FrameLoop.Waiting: that also holds while the frame's work waits for a retrace
			// beyond the host frame, which is most host frames.
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
				// Assert_Report's fatal path runs the hooks (among them Sim_AssertCleanupHook, which writes an int16 0 to data\exit.cfg),
				// then __exit(1). No shutdown runs.
				Console.Error.WriteLine(assert.Message);
				exitCode = Es1Shutdown.AssertCleanup(installRoot, sound);
				window.Close();
				return;
			}

			pauseHost.Update();

			// Sim_Main leaves its loop for Sim_Shutdown. Only a tick ends the mission, so the runner exists here.
			if (ended && runner != null) {
				exitCode = Es1Shutdown.Run(runner.Simulation, installRoot, runner.AxisKeys.DemoMode);
				Console.WriteLine($"Mission over: exit code {exitCode}.");
				window.Close();
				return;
			}

			scene.Clouds?.Advance(deltaSeconds);

			// The engine aids' keys do nothing while you type in a menu bar field.
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

			// After the pointer is grabbed, the first sample only re-syncs. See comments on lookNeedsResync in the mission editor's
			// Program.cs.
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

			// Hide or show the host cursor depending on where it's located.
			if (mouse != null && !looking) {
				var mode = !free && presenter != null && !window.ImGuiWantsMouse ? CursorMode.Hidden : CursorMode.Normal;
				if (mouse.Cursor.CursorMode != mode) {
					mouse.Cursor.CursorMode = mode;
				}
			}
		};

		window.Render += (delta, _) => {
			window.BeginFrame(delta);
			if (renderer != null && draw != null) {
				var size = window.FramebufferSize;
				if (free || presenter == null) {
					draw.Update(camera, null);
					renderer.Clear();
					DrawWorld(glContext!, renderer, sprites!, backdrop, draw, camera, 0, 0, size.X, size.Y);
				} else {
					presenter.Present(size);
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
	/// One view of the world. It draws the backdrop and the shadows, then the zone's shapes, the HERCs, the projectiles
	/// and the impact effects (the two passes described by <see cref="Es1WorldDraw"/>), and last the effects' billboards.
	/// The depth buffer is cleared between the passes so that no object is hidden by a shadow.
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
	/// Draws a camera's view into a cockpit rect of <paramref name="size"/> screen pixels, in <paramref name="viewport"/>. The
	/// <paramref name="camera"/> is placed and projected as <paramref name="view"/> says. What it draws depends on the view's scene:
	/// for <see cref="Es1RectScene.FlatZoneShapes"/>, <see cref="Es1WorldDraw.FlatZoneShapes"/> with no backdrop; for
	/// <see cref="Es1RectScene.HddMapZoneShapes"/>, <see cref="Es1WorldDraw.HddMapZoneShapes"/> the same way; for
	/// <see cref="Es1RectScene.MissileCamera"/>, the forward view's pass (<see cref="DrawWorld"/>) over
	/// <paramref name="missileHorizon"/>, with the items placed for the camera (<see cref="Es1WorldDraw.DrawFlatView"/>).
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

		// The faces are lit by the sun alone, which diverges from retail (KNOWN_ISSUES.md). Retail's NAV MAP and command-screen map
		// faces are also lit by the effect lights chosen for the last object drawn by a world pass, still in the state they had then
		// (docs/es1/rendering/polygon-shading.md#effect-lights). The rect views' items carry no light anchor.
		renderer.Backdrop = null;
		renderer.Render(camera, items, viewport.X, viewport.Y, viewport.Width, viewport.Height);
	}
}
