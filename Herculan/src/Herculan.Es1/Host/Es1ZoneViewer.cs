using System.Numerics;
using HercWorks.Core.Data.File.Msn.Es1;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.View;
using Herculan.Es1.Render;
using Herculan.Es1.Scene;
using Herculan.Es1.World;
using Silk.NET.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// Shows an ES1 mission's zone with nothing simulated: the shapes its zone file places, from a free camera. The
/// first step of docs/herculan/plan-es1-support.md#phase-2--the-engine, which runs no part of the simulator.
///
/// <para>The sky, the ground and the skyline are <see cref="Es1Backdrop"/>'s, the shapes drawn over them with no
/// depth fade, as ES1's are, and coloured by their distance bands (<see cref="Es1Shading"/>).</para>
///
/// <para>The shapes are drawn with the depth test, where retail sorts its object list and paints it farthest first
/// with box comparisons for the zone's shapes, whose ordering is Open
/// (docs/es1/rendering/polygon-shading.md#the-frames-draw).</para>
///
/// <para>G switches the ground between textured and banded, which the preferences' option 5 does in retail; the
/// viewer starts textured.</para>
/// </summary>
public static class Es1ZoneViewer {
	/// <summary>The folder the shell reads missions from, <c>msn\%s</c> (docs/es1/formats/msn-mission-file.md#loading-a-mission).</summary>
	public const string MissionFolder = "msn";

	/// <summary>The clear colour when the theater's sky cannot be built: a mid grey, plainly not a retail sky.</summary>
	private static readonly Vector3 MissingSky = new(0.32f, 0.32f, 0.36f);

	/// <summary>The camera's starting height over the ground, in metres. The viewer's own choice.</summary>
	private const float StartHeightMeters = 40f;

	/// <summary>
	/// Loads <paramref name="missionName"/> (with or without <c>.MSN</c>) from the install at
	/// <paramref name="installRoot"/> and shows it in <paramref name="theater"/> until the window closes.
	/// Returns the process exit code.
	/// </summary>
	public static int Run(string installRoot, string missionName, Es1Theater theater) {
		if (!theater.IsValid) {
			Console.Error.WriteLine($"No theater {theater.Index} with world variant {theater.WorldVariant} and detail {theater.WorldDetail}.");
			return 1;
		}

		// What the simulator mounts: SIMVOL0 (program mask 1) and ZONES (0x101), searched by ES2's precedence rule.
		// ES1's own volume code is unread (docs/es1/formats/zone-terrain.md#open), and its CD drive comes from ALT_DRIVE,
		// not drive.cfg, so no disc is mounted.
		var content = GameContent.MountInstall(installRoot, disc: null, Voln.DbsimProgram);

		string fileName = Path.HasExtension(missionName) ? missionName : missionName + ".MSN";
		if (content.Read(MissionFolder, fileName) is not { } missionBytes
				|| new Es1MissionFileTransformer().Parse(missionBytes) is not Es1MissionFile mission) {
			Console.Error.WriteLine($"No ES1 mission {MissionFolder}\\{fileName} in {installRoot}.");
			return 1;
		}

		short zone;
		try {
			zone = Es1MissionZone.Of(mission);
		} catch (NotSupportedException unsupported) {
			Console.Error.WriteLine($"Cannot load {fileName}: {unsupported.Message}");
			return 1;
		}

		var scene = Es1ZoneScene.Load(content, theater, zone);
		int unmodelled = scene.Shapes.Count(shape => ModelOf(scene, shape) == null);
		Console.WriteLine(
			$"{fileName}: zone {zone}, theater {theater.Name}, palette {theater.PaletteName}, world {theater.WorldName}. " +
			$"{scene.Shapes.Count} shapes from a library of {scene.Models.Count}, {unmodelled} without a model.");
		if (scene.Horizon == null) {
			Console.Error.WriteLine($"{theater.WorldName}.WLD or {theater.PaletteName}.DPL is missing or unusable: no sky or ground is drawn.");
		}

		Console.WriteLine("RMB + mouse to look, WASD/arrows to move, Q/E down/up, Shift boosts, G textured/banded ground, Esc quits.");

		Show(scene, $"HERCULAN — Metaltech: Earthsiege — {Path.GetFileNameWithoutExtension(fileName)}, zone {zone}");
		return 0;
	}

	private static MeshBuild? ModelOf(Es1ZoneScene scene, Es1PlacedShape shape) =>
		shape.ShapeIndex >= 0 && shape.ShapeIndex < scene.Models.Count ? scene.Models[shape.ShapeIndex] : null;

	private static void Show(Es1ZoneScene scene, string title) {
		using var window = new EngineWindow(title);

		SceneRenderer? renderer = null;
		Es1Backdrop? backdrop = null;
		var disposables = new List<IDisposable>();
		SceneItem[] items = [];
		Es1PlacedShape[] itemShapes = [];
		IKeyboard? keyboard = null;
		IMouse? mouse = null;

		var (min, max) = Bounds(scene);
		var camera = new Camera();
		var freeCamera = new EditorCamera {
			WorldPosition = new Vector3((min.X + max.X) / 2f, min.Y,
				StartHeightMeters * WorldScale.WorldUnitsPerMeter),
		};

		bool looking = false;
		bool lookNeedsResync = false;
		bool groundKeyHeld = false;
		Vector2 lastMousePos = Vector2.Zero;

		window.Load += (gl, input) => {
			if (scene.Horizon != null && scene.Palette != null) {
				backdrop = new Es1Backdrop(gl, scene.Horizon, scene.Ground, scene.Skyline, scene.Palette);
				disposables.Add(backdrop);
			}

			// ES1 fades nothing with depth (docs/es1/rendering/polygon-shading.md), so the renderer's fog stays off.
			renderer = new SceneRenderer(gl) {
				Sky = null,
				Backdrop = backdrop,
				SkyColor = MissingSky,
				FogEnabled = false,
			};
			renderer.SetShadeRamps(scene.Shading.Table ?? SurfaceRampTable.Build(scene.Shading.Surfaces));
			renderer.SetPaletteRamp(PaletteRampTable.Build(scene.Shading.Surfaces));

			var built = new List<SceneItem>();
			var placed = new List<Es1PlacedShape>();

			var meshes = new Dictionary<int, GpuMesh>();
			foreach (var shape in scene.Shapes) {
				if (ModelOf(scene, shape) is not { Vertices.Length: > 0 } model) {
					continue;
				}

				if (!meshes.TryGetValue(shape.ShapeIndex, out var mesh)) {
					mesh = new GpuMesh(gl, model.Vertices, model.TriangleVertexCount, model.PointVertexCount);
					meshes[shape.ShapeIndex] = mesh;
					disposables.Add(mesh);
				}

				built.Add(new SceneItem(mesh, WorldScale.ToRenderMatrix(shape.Frame)));
				placed.Add(shape);
			}

			items = built.ToArray();
			itemShapes = placed.ToArray();
			keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
			mouse = input.Mice.Count > 0 ? input.Mice[0] : null;

			// As in both other hosts: DTS geometry isn't reliably wound, so nothing is backface-culled.
			gl.Disable(Silk.NET.OpenGL.EnableCap.CullFace);

			if (mouse != null) {
				mouse.MouseDown += (m, button) => {
					if (button == MouseButton.Right) {
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

			bool groundKey = keyboard?.IsKeyPressed(Key.G) == true;
			if (groundKey && !groundKeyHeld && backdrop != null) {
				backdrop.TexturedGround = !backdrop.TexturedGround;
			}

			groundKeyHeld = groundKey;

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

			freeCamera.Update(deltaSeconds, keyboard, lookDelta, looking, acceptKeyboardInput: true);
			freeCamera.ApplyTo(camera);
			ApplyBands(scene.Shading, camera, items, itemShapes);
		};

		window.Render += (_, _) => {
			if (renderer == null) {
				return;
			}

			var size = window.FramebufferSize;
			renderer.Clear();
			renderer.Render(camera, items, 0, 0, size.X, size.Y);
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
	/// What <c>ObjList_DrawFrameObjects</c> (<c>ES1DBSIM 00021008</c>) settles for each zone shape before drawing it
	/// (docs/es1/rendering/polygon-shading.md#the-frames-draw): a shape whose z is negative, or whose distance from the
	/// camera by <c>Math_DistanceBetweenPoints</c> is past the last band, is not drawn, and the rest read their band's
	/// block of <see cref="Es1Shading.Table"/>.
	/// </summary>
	private static void ApplyBands(Es1Shading shading, Camera camera, SceneItem[] items, Es1PlacedShape[] shapes) {
		for (int i = 0; i < items.Length; i++) {
			var frame = shapes[i].Frame;
			int distance = camera.Position.ApproxDistanceTo(new Vec3i(frame.X, frame.Y, frame.Z));
			items[i].Visible = frame.Z >= 0 && !shading.IsBeyondLastBand(distance);
			items[i].RampBlock = shading.Table != null ? shading.Band(distance) : null;
		}
	}

	/// <summary>The least and greatest X and Y over the zone's shapes, in world units; the origin for a zone with none.</summary>
	private static (Vector2 Min, Vector2 Max) Bounds(Es1ZoneScene scene) {
		if (scene.Shapes.Count == 0) {
			return (Vector2.Zero, Vector2.Zero);
		}

		var min = new Vector2(float.MaxValue);
		var max = new Vector2(float.MinValue);
		foreach (var shape in scene.Shapes) {
			var point = new Vector2(shape.Frame.X, shape.Frame.Y);
			min = Vector2.Min(min, point);
			max = Vector2.Max(max, point);
		}

		return (min, max);
	}
}
