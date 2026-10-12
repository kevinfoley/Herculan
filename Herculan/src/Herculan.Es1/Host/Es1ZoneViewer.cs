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
using Herculan.Es1.Scene;
using Herculan.Es1.World;
using Silk.NET.Input;

namespace Herculan.Es1.Host;

/// <summary>
/// Shows an ES1 mission's zone with nothing simulated: the shapes its zone file places, from a free camera. The
/// first step of docs/herculan/plan-es1-support.md#phase-2--the-engine, which runs no part of the simulator.
///
/// <para>PLACEHOLDER: the ground, the sky and the distance fade are not ES1's. How ES1 draws them is Open
/// (docs/es1/formats/wld-world.md#open), so the ground is a flat grey plane under the measuring grid and the sky one
/// flat colour, both plainly not retail, and nothing fades. The shapes' colours are
/// <see cref="Es1PlaceholderShading"/>'s.</para>
/// </summary>
public static class Es1ZoneViewer {
	/// <summary>The folder the shell reads missions from, <c>msn\%s</c> (docs/es1/formats/msn-mission-file.md#loading-a-mission).</summary>
	public const string MissionFolder = "msn";

	/// <summary>PLACEHOLDER: the flat sky colour.</summary>
	private static readonly Vector3 PlaceholderSky = new(0.32f, 0.32f, 0.36f);

	/// <summary>PLACEHOLDER: the flat ground colour.</summary>
	private static readonly Vector3 PlaceholderGround = new(0.45f, 0.45f, 0.45f);

	/// <summary>How far the placeholder ground reaches past the zone's outermost shapes, in world units.</summary>
	private const int GroundMargin = 200_000;

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
		Console.WriteLine("RMB + mouse to look, WASD/arrows to move, Q/E down/up, Shift boosts, Esc quits.");

		Show(scene, $"HERCULAN — Metaltech: Earthsiege — {Path.GetFileNameWithoutExtension(fileName)}, zone {zone}");
		return 0;
	}

	private static MeshBuild? ModelOf(Es1ZoneScene scene, Es1PlacedShape shape) =>
		shape.ShapeIndex >= 0 && shape.ShapeIndex < scene.Models.Count ? scene.Models[shape.ShapeIndex] : null;

	private static void Show(Es1ZoneScene scene, string title) {
		using var window = new EngineWindow(title);

		SceneRenderer? renderer = null;
		var disposables = new List<IDisposable>();
		SceneItem[] items = [];
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
		Vector2 lastMousePos = Vector2.Zero;

		window.Load += (gl, input) => {
			renderer = new SceneRenderer(gl, editorGrid: true) {
				Sky = null,
				SkyColor = PlaceholderSky,
				FogEnabled = false,
			};
			renderer.SetShadeRamps(SurfaceRampTable.Build(scene.Shading));
			renderer.SetPaletteRamp(PaletteRampTable.Build(scene.Shading));

			var ground = new GpuMesh(gl, GroundQuad(min, max));
			disposables.Add(ground);
			var built = new List<SceneItem> { new(ground, Matrix4x4.Identity) { ShowGrid = true } };

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
			}

			items = built.ToArray();
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

	/// <summary>PLACEHOLDER: the ground, one grey quad at height 0 reaching <see cref="GroundMargin"/> past the shapes.</summary>
	private static MeshVertex[] GroundQuad(Vector2 min, Vector2 max) {
		var up = new Vector3(0f, 1f, 0f);
		Vector3 Corner(float x, float y) => WorldScale.ToRender(x, y, 0f);

		var a = Corner(min.X - GroundMargin, min.Y - GroundMargin);
		var b = Corner(max.X + GroundMargin, min.Y - GroundMargin);
		var c = Corner(max.X + GroundMargin, max.Y + GroundMargin);
		var d = Corner(min.X - GroundMargin, max.Y + GroundMargin);
		return [
			new(a, up, PlaceholderGround), new(b, up, PlaceholderGround), new(c, up, PlaceholderGround),
			new(a, up, PlaceholderGround), new(c, up, PlaceholderGround), new(d, up, PlaceholderGround),
		];
	}
}
