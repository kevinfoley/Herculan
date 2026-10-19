using System.Numerics;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.View;
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
/// simulator's world load from the install's <c>data\</c> files, and the forward view of the player's machine, with
/// nothing ticking (docs/herculan/plan-es1-first-mission.md, step 3). This class composes: the load and its files are
/// <see cref="Es1SingleMission"/> and <see cref="Es1MissionFiles"/>, the world <see cref="Es1World"/>, the zone
/// <see cref="Es1ZoneScene"/>, the draw <see cref="Es1WorldDraw"/>, and the view <see cref="Es1ForwardView"/>.
///
/// <para>F switches to a free camera, an engine aid with no retail counterpart, which shows the world over the whole
/// window with the player's machine drawn; G switches the preferences' option 5 between <see cref="TexturedDetail"/>
/// and <see cref="BandedDetail"/>, the ground between textured and banded.</para>
/// </summary>
public static class Es1ZoneViewer {
	/// <summary>Option 5 while the ground is textured: its largest value, which the viewer starts at, the user's choice.</summary>
	private const int TexturedDetail = 255;

	/// <summary>Option 5 while the ground is banded: the largest value below 240, which draws 248 of 250 points.</summary>
	private const int BandedDetail = Es1GroundSpecks.DetailLimit - 1;

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
		} catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException) {
			Console.Error.WriteLine($"Cannot load the mission: {ex.Message}");
			return 1;
		}

		var player = world.LocalPlayer;
		if (views != null) {
			// Sim_StartMission's order (docs/es1/simulation/cockpit-views.md#the-view-manager).
			views.SetView(1);
			views.SetView(0);
		}

		Console.WriteLine(
			$"Zone {header[1]}, theater {theater.Name}, world {theater.WorldName}: {scene.Shapes.Count} shapes, " +
			$"{world.MechPool.Count} HERCs in {world.Groups.Count} groups" +
			(player != null ? $", the player in a {player.Type.Name}." : ", no player's machine."));
		if (views == null) {
			Console.Error.WriteLine("No cockpit view for the player's machine: the free camera only.");
		}

		Console.WriteLine("F free camera (RMB + mouse to look, WASD/arrows, Q/E, Shift), G textured/banded ground, Esc quits.");
		Show(content, scene, world, views, $"HERCULAN — Metaltech: Earthsiege — {missionName ?? "data\\script.dat"}, zone {header[1]}");
		return 0;
	}

	private static void Show(GameContent content, Es1ZoneScene scene, Es1World world, Es1CockpitViews? views, string title) {
		using var window = new EngineWindow(title);

		Silk.NET.OpenGL.GL? glContext = null;
		SceneRenderer? renderer = null;
		Es1Backdrop? backdrop = null;
		Es1WorldDraw? draw = null;
		Es1ForwardView? forward = null;
		Es1MechModels? models = null;
		var disposables = new List<IDisposable>();
		IKeyboard? keyboard = null;
		IMouse? mouse = null;

		var camera = new Camera();
		var eye = world.LocalPlayer is { } player ? new Es1EyeCamera(player) : null;
		var freeCamera = new EditorCamera();
		if (eye != null) {
			freeCamera.ResetTo(eye.Update().Position, eye.Update().View.Z);
		}

		bool free = eye == null || views == null;
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
					GroundDetail = TexturedDetail,
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
			draw = new Es1WorldDraw(gl, scene, world, models, disposables);
			if (views != null) {
				forward = new Es1ForwardView(gl, views, scene.Palette);
				disposables.Add(forward);
			}

			keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
			mouse = input.Mice.Count > 0 ? input.Mice[0] : null;

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
			if (keyboard?.IsKeyPressed(Key.Escape) == true) {
				window.Close();
				return;
			}

			scene.Clouds?.Advance(deltaSeconds);

			bool groundKey = keyboard?.IsKeyPressed(Key.G) == true;
			if (groundKey && !groundKeyHeld && backdrop != null) {
				backdrop.GroundDetail = backdrop.GroundDetail == TexturedDetail ? BandedDetail : TexturedDetail;
			}

			groundKeyHeld = groundKey;

			bool freeKey = keyboard?.IsKeyPressed(Key.F) == true;
			if (freeKey && !freeKeyHeld && eye != null && views != null) {
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
			} else {
				eye!.ApplyTo(camera, views!.Window);
			}

			draw?.Update(camera, free ? null : eye?.Machine);
		};

		window.Render += (_, _) => {
			if (renderer == null || draw == null) {
				return;
			}

			var size = window.FramebufferSize;
			if (free || forward == null) {
				renderer.Clear();
				DrawWorld(glContext!, renderer, backdrop, draw, camera, 0, 0, size.X, size.Y);
				return;
			}

			forward.Render(size, (x, y, width, height) => DrawWorld(glContext!, renderer, backdrop, draw, camera, x, y, width, height));
		};

		window.Closing += () => {
			renderer?.Dispose();
			foreach (var disposable in disposables) {
				disposable.Dispose();
			}
		};

		window.Run();
	}

	/// <summary>
	/// One view of the world: the backdrop and the shadows, then over them the zone's shapes and the HERCs, the two passes
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
}
