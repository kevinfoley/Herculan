using System.Numerics;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
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
/// so does retail's way out, <c>Ctrl+Q</c>'s or <c>Ctrl+Esc</c>'s QUIT, which ends the mission (plan decision 12).</para>
/// </summary>
public static class Es1ZoneViewer {
	/// <summary>The clear colour when the theater's sky cannot be built: a mid grey, plainly not a retail sky.</summary>
	private static readonly Vector3 MissingSky = new(0.32f, 0.32f, 0.36f);

	/// <summary>
	/// Starts <paramref name="missionName"/> (with or without <c>.MSN</c>) as a single mission and runs the simulator on
	/// the files that writes, or with no mission named runs it on the files the install's <c>data\</c> holds, as
	/// <c>DBSIM.EXE</c> does. Returns the process exit code.
	/// </summary>
	public static int Run(string installRoot, string? missionName, Es1HeaderOverrides overrides) {
		if (missionName != null) {
			var random = Es1ShellRandom.AtStartup(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			if (Es1SingleMission.Start(installRoot, Es1SingleMission.MountShell(installRoot), missionName, random,
					out string? loadError) == null) {
				Console.Error.WriteLine(loadError);
				return 1;
			}

			Console.WriteLine($"Loaded {missionName} as a single mission into data\\script.dat, mission.var and player.mec.");
		}

		if (Es1MissionFiles.Read(installRoot, out string? error) is not { } files) {
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
		// not drive.cfg, so no disc is mounted.
		var content = GameContent.MountInstall(installRoot, disc: null, Voln.DbsimProgram);
		// The HERCs and the cockpit are loaded before the zone, unlike retail, so the scene is built in the live palette
		// with the player chassis' scheme installed (Es1ZoneScene.Palette); with no player's machine nothing is installed.
		Es1World world;
		Es1ZoneScene scene;
		Es1CockpitViews? views;
		try {
			var script = Es1MissionScript.Read(files);
			var formations = new Es1MechFormationsTransformer().Parse(content.ReadRequired("dat", "MFORMS.DAT"))
				?? new Es1MechFormations();
			world = Es1World.Spawn(script, content, formations);
			views = world.LocalPlayer is { } machine ? Es1CockpitViews.Load(content, machine.Type.CockpitName) : null;
			var scheme = views?.CockpitPalette is { } cockpit
				? new Es1CockpitScheme(cockpit, world.LocalPlayer!.Type.Type)
				: (Es1CockpitScheme?)null;
			scene = Es1ZoneScene.Load(content, theater, header[1], scheme);
			world.Globals.ZoneShapes = scene.ZoneShapes;
		} catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException) {
			Console.Error.WriteLine($"Cannot load the mission: {ex.Message}");
			return 1;
		}

		var player = world.LocalPlayer;
		Console.WriteLine(
			$"Zone {header[1]}, theater {theater.Name}, world {theater.WorldName}: {scene.Shapes.Count} shapes, " +
			$"{world.MechPool.Count} HERCs in {world.Groups.Count} groups" +
			(player != null ? $", the player in a {player.Type.Name}." : ", no player's machine."));
		if (views == null) {
			Console.Error.WriteLine("No cockpit view for the player's machine: the free camera only.");
		}

		Console.WriteLine("Keypad drives the HERC; F free camera (RMB + mouse to look, WASD/arrows, Q/E, Shift), " +
			"G textured/banded ground; close the window to quit" + (player == null ? " (or Esc)." : "."));
		Show(installRoot, files.Preferences, content, scene, world, views, $"HERCULAN — Metaltech: Earthsiege — {missionName ?? "data\\script.dat"}, zone {header[1]}");
		return 0;
	}

	private static void Show(string installRoot, Es1Preferences preferences, GameContent content, Es1ZoneScene scene,
			Es1World world, Es1CockpitViews? views, string title) {
		using var window = new EngineWindow(title);

		Silk.NET.OpenGL.GL? glContext = null;
		SceneRenderer? renderer = null;
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

		// Sim_StartMission's steps 9 to 13 that build no GL object: the views and their cockpit, and the chain of views
		// with its cameras, the main one on the player's eye.
		var player = world.LocalPlayer;
		// Step 3's Sound_Init, of which the voice lines' slots are ported, and Snc_MissionInit, which puts the lip-sync
		// player on the PC timer. PLACEHOLDER: the rest of Sound_Init, the sound manager, its drivers and the catalog;
		// no plan step ports ES1's sound yet.
		world.Globals.VoiceLines.InitSlots(preferences.LowMemory);
		world.Globals.VoiceLines.MissionInit(world.Globals.Clock);
		// The mouse driver, which the host's mouse is attached to once the window has one; and step 5's
		// CockpitMouse_Init on it, which runs before the cockpit is built.
		var hostMouse = new Es1HostMouse();
		// Step 4's Widget_LoadStylesAndFonts, which the in-mission panels draw with.
		var widgets = Es1WidgetResources.Load(content);
		Es1ViewManager? manager = null;
		if (views != null && player != null) {
			var display = new Es1VideoMemory();
			var cockpitMouse = new Es1CockpitMouse(display.Cursor);
			cockpitMouse.Init(hostMouse);
			manager = new Es1ViewManager(views, display, player, cockpitMouse, world.Globals, preferences);
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

		window.Load += (gl, input) => {
			glContext = gl;
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
			};
			renderer.SetShadeRamps(scene.Shading.Table ?? SurfaceRampTable.Build(scene.Shading.Surfaces));
			renderer.SetPaletteRamp(scene.TheaterPalette != null && scene.Palette != null
				? Es1TextureShades.RampTable(scene.TheaterPalette, scene.Palette)
				: PaletteRampTable.Build(scene.Shading.Surfaces));

			models = Es1MechModels.Upload(gl, content, world.Types.Values.OfType<Es1MechType>(), scene);
			disposables.Add(models);
			var projectileDraw = new Es1ProjectileDraw(gl, content, scene, world.Globals.Projectiles);
			disposables.Add(projectileDraw);
			draw = new Es1WorldDraw(gl, scene, world, models, detail, disposables, projectileDraw);
			if (manager != null && chain != null) {
				monitor = new Es1Monitor(gl, manager.Display);
				disposables.Add(monitor);
				monitor.SetPalette(scene.Palette);
				// The cockpit script's palette commands upload to the same live palette, and the shield display's rings are
				// installed into it.
				manager.Cockpit.Scripts.UploadPalette = monitor.SetPalette;
				manager.Cockpit.Shields.InstallPaletteRange = monitor.InstallRange;
				monitor.Fit(window.FramebufferSize);
				var worldDraw = draw;
				var sceneRenderer = renderer;
				monitor.WorldPass = (x, y, width, height) =>
					DrawWorld(gl, sceneRenderer, backdrop, worldDraw, cockpitCamera, x, y, width, height);
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

				monitor.ViewPass = (view, size, viewport) =>
					DrawRectView(gl, sceneRenderer, worldDraw, missileHorizon, rectCamera, view, size, viewport);
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
				frameLoop.StartMission(Es1CursorImage.Load(content, "CURSOR"));
			}

			keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
			mouse = input.Mice.Count > 0 ? input.Mice[0] : null;
			hostMouse.Pointer = () => Es1Monitor.Pointer(window.FramebufferSize, window.ClientSize);
			if (mouse != null) {
				hostMouse.Attach(mouse);
			}

			if (world.LocalPlayer != null) {
				simulation = new Es1SimulationStart(world, installRoot, preferences, input, keyboard, hostMouse,
					chain, widgets, content);
				disposables.Add(simulation);
			}

			// As in both other hosts: DTS geometry isn't reliably wound, so nothing is backface-culled.
			gl.Disable(Silk.NET.OpenGL.EnableCap.CullFace);

			if (mouse != null) {
				mouse.MouseDown += (m, button) => {
					if (button == MouseButton.Right && free) {
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
			// With a simulation running, Esc is the simulator's view key; the window's close quits.
			if (simulation == null && keyboard?.IsKeyPressed(Key.Escape) == true) {
				window.Close();
				return;
			}

			var runner = simulation?.Advance();
			bool ended = frameLoop != null
				? frameLoop.AdvanceHostFrame(deltaSeconds, runner)
				: runner?.AdvanceHostFrame(TimeSpan.FromSeconds(deltaSeconds)) == Es1SimulationRunner.Frame.MissionEnded;
			if (ended) {
				window.Close();
				return;
			}

			scene.Clouds?.Advance(deltaSeconds);

			bool groundKey = keyboard?.IsKeyPressed(Key.G) == true;
			if (groundKey && !groundKeyHeld) {
				backdropOptions.ToggleGround();
			}

			groundKeyHeld = groundKey;

			bool freeKey = keyboard?.IsKeyPressed(Key.F) == true;
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
				freeCamera.Update(deltaSeconds, keyboard, lookDelta, looking, acceptKeyboardInput: true);
				freeCamera.ApplyTo(camera);
			}

			// The host's pointer is hidden over the window while the cockpit's cursor stands in for it.
			if (mouse != null && !looking) {
				var mode = !free && monitor != null ? CursorMode.Hidden : CursorMode.Normal;
				if (mouse.Cursor.CursorMode != mode) {
					mouse.Cursor.CursorMode = mode;
				}
			}
		};

		window.Render += (_, _) => {
			if (renderer == null || draw == null) {
				return;
			}

			var size = window.FramebufferSize;
			if (free || monitor == null) {
				draw.Update(camera, null);
				renderer.Clear();
				DrawWorld(glContext!, renderer, backdrop, draw, camera, 0, 0, size.X, size.Y);
				return;
			}

			monitor.Present(size);
		};

		window.Closing += () => {
			hostMouse.Detach();
			renderer?.Dispose();
			foreach (var disposable in disposables) {
				disposable.Dispose();
			}
		};

		window.Run();
	}

	/// <summary>
	/// One view of the world: the backdrop and the shadows, then over them the zone's shapes, the HERCs and the projectiles, the two passes
	/// <see cref="Es1WorldDraw"/> describes. The depth between the passes is cleared so no object is hidden by a shadow.
	/// </summary>
	private static void DrawWorld(Silk.NET.OpenGL.GL gl, SceneRenderer renderer, Es1Backdrop? backdrop, Es1WorldDraw draw, Camera camera,
			int x, int y, int width, int height) {
		renderer.Backdrop = backdrop;
		renderer.Render(camera, draw.Shadows, x, y, width, height);
		gl.Clear(Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);
		renderer.Backdrop = null;
		renderer.Render(camera, draw.Objects, x, y, width, height);
	}

	/// <summary>
	/// A camera's view into a cockpit rect of <paramref name="size"/> screen pixels, drawn in <paramref name="viewport"/>
	/// from <paramref name="camera"/> placed and projected as <paramref name="view"/> says: for
	/// <see cref="Es1RectScene.FlatZoneShapes"/>, <see cref="Es1WorldDraw.FlatZoneShapes"/> with no backdrop; for
	/// <see cref="Es1RectScene.MissileCamera"/>, the forward view's pass (<see cref="DrawWorld"/>) over
	/// <paramref name="missileHorizon"/> with the items placed for the camera (<see cref="Es1WorldDraw.DrawFlatView"/>).
	/// </summary>
	private static void DrawRectView(Silk.NET.OpenGL.GL gl, SceneRenderer renderer, Es1WorldDraw draw,
			Es1Backdrop? missileHorizon, Camera camera, Es1RectView view, (int Width, int Height) size,
			(int X, int Y, int Width, int Height) viewport) {
		Es1Camera.ApplyTo(camera, view, size.Width, size.Height);
		if (view.Scene == Es1RectScene.MissileCamera) {
			if (missileHorizon != null) {
				missileHorizon.CentrePageColumn = view.Left + view.Centre.X;
			}

			draw.DrawFlatView(camera, view.FocalShift,
				() => DrawWorld(gl, renderer, missileHorizon, draw, camera, viewport.X, viewport.Y, viewport.Width, viewport.Height));
			return;
		}

		IReadOnlyList<SceneItem> items = view.Scene switch {
			Es1RectScene.FlatZoneShapes => draw.FlatZoneShapes,
			_ => [],
		};

		// PLACEHOLDER: the faces take the sun alone. Retail draws the view outside the object list's per-object light
		// selection (docs/es1/rendering/polygon-shading.md#effect-lights), and which effect lights its faces then take
		// is not settled; this engine's ES1 draw lights nothing else either.
		renderer.Backdrop = null;
		renderer.Render(camera, items, viewport.X, viewport.Y, viewport.Width, viewport.Height);
	}
}
