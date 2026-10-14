using System.Numerics;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Editor;

/// <summary>
/// The GPU half of an <see cref="EditorMission"/>: its terrain and every model uploaded, and the scene items that
/// draw them, with transforms baked once since nothing moves in the editor. Disposing it frees every upload, which
/// is what opening another mission does before building the next one.
/// </summary>
internal sealed class MissionDrawables : IDisposable {
	private readonly List<IDisposable> _uploads = new();

	/// <summary>
	/// Uploads <paramref name="mission"/>'s scene and points <paramref name="renderer"/> at its theater: the fog,
	/// sky and shading come off the zone and its theater as in Herculan.Engine.Host, rather than being hand-picked.
	/// </summary>
	public MissionDrawables(GL gl, SceneRenderer renderer, EditorMission mission, EditorSettings settings) {
		var scene = mission.Scene;

		// See Scene.Atmosphere. Without it the renderer has no sky gradient, so DrawSky paints nothing and the
		// view has no background at all.
		scene.Models.Atmosphere.ApplyTo(renderer);

		// The theater's shaded-surface colours, what a TSShadedPoly is actually drawn through. See
		// SurfaceRampTable.
		renderer.SetShadeRamps(scene.Models.ShadeRamps);
		renderer.SetPaletteRamp(scene.Models.PaletteRamp);

		var terrainMesh = Keep(new GpuMesh(gl, scene.Models.TerrainMesh));
		var terrainTexture = scene.Models.TerrainBank != null
			? Keep(scene.Models.TerrainBank.Atlas.Upload(gl, indexed: true))
			: null;

		var modelMeshes = new Dictionary<string, GpuMesh>();
		var modelCells = new Dictionary<string, GpuMesh[]>();
		var modelTextures = new Dictionary<string, GpuTexture>();
		var groundMeshes = new Dictionary<string, GpuMesh>();
		foreach (var model in scene.Models.All) {
			// A shape split by cell — a structure or a flyer — is drawn a piece at a time as the simulator
			// draws it, so that a TSBSPPart's children and a decal's paint layer are painted in order
			// (BspDrawGroup); the flat mesh would leave them to the depth test.
			if (model.Cells.Length > 0) {
				modelCells[model.Key] = model.Cells
					.Select(cell => Keep(new GpuMesh(gl, cell.Vertices, cell.TriangleVertexCount, cell.PointVertexCount)))
					.ToArray();
			} else {
				modelMeshes[model.Key] = Keep(new GpuMesh(gl, model.Mesh, model.TriangleVertexCount, model.PointVertexCount));
			}

			if (model.GroundMesh is { Vertices.Length: > 0 } ground) {
				groundMeshes[model.Key] = Keep(new GpuMesh(gl, ground.Vertices, ground.TriangleVertexCount,
					ground.PointVertexCount));
			}

			if (model.Atlas != null) {
				modelTextures[model.Key] = Keep(model.Atlas.Upload(gl, indexed: true));
			}
		}

		// The terrain, and the only item the measuring grid is painted onto.
		var built = new List<SceneItem> {
			new(terrainMesh, Matrix4x4.Identity, terrainTexture?.Handle) { ShowGrid = settings.ShowGrid, CellQuantisedFog = true, GroundFill = true }
		};

		// The terrain with each structure's ground plane painted in its cell order, as the simulator draws
		// them — see SceneRenderer.Render. Without it a structure's shadow lies in the terrain's own plane and
		// ties with it in the depth buffer. Nothing is filed by cell here, so a ground plane is drawn wherever
		// the camera is, as every object is.
		GroundLayer = new GroundShapeLayer(built[0], scene.World.Terrain) { ShapesFollowWalk = false };

		foreach (var sceneObject in scene.Objects) {
			if (sceneObject.Model is not { } model) {
				continue;
			}

			uint? textureHandle = modelTextures.TryGetValue(model.Key, out var texture) ? texture.Handle : null;
			var transform = MissionScene.TransformOf(sceneObject);
			bool waiting = mission.Index.IsWaiting(sceneObject);
			if (modelCells.TryGetValue(model.Key, out var cellMeshes)) {
				// What the flat mesh is: every sequence on its first cell and every detail part at its finest
				// level. The ground plane stays with groundMeshes below.
				var groups = new Dictionary<BspTree, BspDrawGroup>();
				for (int i = 0; i < cellMeshes.Length; i++) {
					var cell = model.Cells[i];
					if (cell.Ground || !cell.Gate.VisibleIn(null)
							|| (cell.Gate.Detail is { } detail && cell.Gate.Level != detail.LevelCount - 1)) {
						continue;
					}

					var part = new SceneItem(cellMeshes[i], transform, textureHandle);
					if (cell.Leaf is { } leaf) {
						if (!groups.TryGetValue(leaf.Tree, out var group)) {
							groups[leaf.Tree] = group = BspDrawGroup.AtRest(leaf.Tree, () => transform);
						}

						part.BspGroup = group;
						part.BspLeaf = leaf.Index;
						part.PaintLayer = cell.Layer;
					}

					built.Add(part);
					if (waiting) {
						WaitingItems.Add(part);
					}
				}
			} else if (modelMeshes.TryGetValue(model.Key, out var mesh)) {
				var item = new SceneItem(mesh, transform, textureHandle);
				built.Add(item);
				if (waiting) {
					WaitingItems.Add(item);
				}
			} else {
				continue;
			}

			// Filed as the simulator's submit files a structure: by its position and its body radius.
			if (groundMeshes.TryGetValue(model.Key, out var groundMesh)) {
				var groundItem = new SceneItem(groundMesh, MissionScene.TransformOf(sceneObject), textureHandle);
				GroundLayer.Shapes.Add(new GroundShapeDraw(groundItem, sceneObject.Object.Position,
					sceneObject.Object.HitRadius));
				if (waiting) {
					WaitingItems.Add(groundItem);
				}
			}
		}

		Items = built.ToArray();
	}

	/// <summary>Everything drawn, the terrain first.</summary>
	public SceneItem[] Items { get; }

	/// <summary>The terrain item and the structures' ground planes painted onto it.</summary>
	public GroundShapeLayer GroundLayer { get; }

	/// <summary>The items of the objects whose group waits on an action, which the settings may stop drawing.</summary>
	public List<SceneItem> WaitingItems { get; } = new();

	public void Dispose() {
		foreach (var upload in _uploads) {
			upload.Dispose();
		}

		_uploads.Clear();
	}

	private T Keep<T>(T upload) where T : IDisposable {
		_uploads.Add(upload);
		return upload;
	}
}
