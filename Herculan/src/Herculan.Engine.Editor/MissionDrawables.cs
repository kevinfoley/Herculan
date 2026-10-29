using System.Numerics;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Editor;

/// <summary>
/// The GPU half of an <see cref="EditorMission"/>: its terrain and every model uploaded, and the scene items that
/// draw them. Nothing ticks, so each object's transform is set as it is uploaded and again only when an edit moves it
/// (<see cref="Move"/>). An object an edit adds is uploaded then (<see cref="Add"/>), its model too if nothing drawn
/// so far uses it, and one an edit takes away stops being drawn (<see cref="Remove"/>), its model kept for a redo. A
/// structure placed with parts collapsed shows their rubble, and one placed already fallen its wreck, as the object was
/// built (<see cref="BaseObject.CellFrames"/>, <see cref="BaseObject.ShowingHulk"/>).
/// Disposing it frees every upload, which is what opening another mission does before building the next one.
/// </summary>
internal sealed class MissionDrawables : IDisposable {
	private readonly GL _gl;
	private readonly List<IDisposable> _uploads = new();
	private readonly GpuMesh _terrainMesh;
	private readonly MeshVertex[] _terrainVertices;
	private readonly List<SceneItem> _items;
	private readonly Func<SceneObject, bool> _isWaiting;
	private readonly Func<SceneObject, bool> _inScene;
	private readonly MissionScene _scene;

	// The items of the objects out of the scene (MissionIndex.InScene), never drawn.
	private readonly List<SceneItem> _offScene = new();

	// Each model's uploads, by model key: a celled shape's cells or else its flat mesh, its ground plane and its texture.
	private readonly Dictionary<string, GpuMesh> _modelMeshes = new();
	private readonly Dictionary<string, GpuMesh[]> _modelCells = new();
	private readonly Dictionary<string, GpuTexture> _modelTextures = new();
	private readonly Dictionary<string, GpuMesh> _groundMeshes = new();

	/// <summary>
	/// Uploads <paramref name="mission"/>'s scene and points <paramref name="renderer"/> at its theater: the fog,
	/// sky and shading come off the zone and its theater as in Herculan.Engine.Host, rather than being hand-picked.
	/// </summary>
	public MissionDrawables(GL gl, SceneRenderer renderer, EditorMission mission, EditorSettings settings) {
		_gl = gl;
		_isWaiting = mission.Index.IsWaiting;
		_inScene = mission.Index.InScene;
		var scene = _scene = mission.Scene;

		// See Scene.Atmosphere. Without it the renderer has no sky gradient, so DrawSky paints nothing and the
		// view has no background at all.
		scene.Models.Atmosphere.ApplyTo(renderer);

		// The theater's shaded-surface colours, what a TSShadedPoly is actually drawn through. See
		// SurfaceRampTable.
		renderer.SetShadeRamps(scene.Models.ShadeRamps);
		renderer.SetPaletteRamp(scene.Models.PaletteRamp);

		_terrainVertices = scene.Models.TerrainMesh;
		var terrainMesh = _terrainMesh = Keep(new GpuMesh(gl, _terrainVertices));
		var terrainTexture = scene.Models.TerrainBank != null
			? Keep(scene.Models.TerrainBank.Atlas.Upload(gl, indexed: true))
			: null;

		foreach (var model in scene.Models.All) {
			UploadModel(model);
		}

		// The terrain, and the only item the measuring grid and the selected area's tint are painted onto.
		_items = new List<SceneItem> {
			new(terrainMesh, Matrix4x4.Identity, terrainTexture?.Handle) {
				ShowGrid = settings.ShowGrid, ShowHighlight = true, CellQuantisedFog = true, GroundFill = true
			}
		};

		// The terrain with each structure's ground plane painted in its cell order, as the simulator draws
		// them — see SceneRenderer.Render. Without it a structure's shadow lies in the terrain's own plane and
		// ties with it in the depth buffer. Nothing is filed by cell here, so a ground plane is drawn wherever
		// the camera is, as every object is.
		GroundLayer = new GroundShapeLayer(_items[0], scene.World.Terrain) { ShapesFollowWalk = false };

		foreach (var sceneObject in mission.Index.Objects) {
			Add(sceneObject);
		}
	}

	/// <summary>Everything drawn, the terrain first.</summary>
	public IReadOnlyList<SceneItem> Items => _items;

	/// <summary>The terrain item and the structures' ground planes painted onto it.</summary>
	public GroundShapeLayer GroundLayer { get; }

	/// <summary>
	/// The items of the objects in the scene whose group waits on an action, which the settings may stop drawing. Those of
	/// an object out of the scene (<see cref="MissionIndex.InScene"/>) are not among them: they are never drawn.
	/// </summary>
	public List<SceneItem> WaitingItems { get; } = new();

	/// <summary>
	/// Draws <paramref name="sceneObject"/> where its simulation object stands, uploading its model first if nothing
	/// drawn so far uses it. An object with no model draws nothing.
	/// </summary>
	public void Add(SceneObject sceneObject) {
		if (DrawnModel(sceneObject) is not { } model || _objects.ContainsKey(sceneObject)) {
			return;
		}

		UploadModel(model);
		var frames = sceneObject.Object.CellFrames;
		uint? textureHandle = _modelTextures.TryGetValue(model.Key, out var texture) ? texture.Handle : null;
		var draw = new ObjectDraw { Transform = MissionScene.TransformOf(sceneObject) };
		var transform = draw.Transform;
		if (_modelCells.TryGetValue(model.Key, out var cellMeshes)) {
			// Every sequence on the cell its object shows, as the simulator gates them (WorldDrawItems): the first,
			// or for a structure placed with parts collapsed (BaseObject's starting condition) their rubble. Every
			// detail part at its finest level. The ground plane stays with _groundMeshes below.
			var groups = new Dictionary<BspTree, BspDrawGroup>();
			for (int i = 0; i < cellMeshes.Length; i++) {
				var cell = model.Cells[i];
				if (cell.Ground || !cell.Gate.VisibleIn(frames)
						|| (cell.Gate.Detail is { } detail && cell.Gate.Level != detail.LevelCount - 1)) {
					continue;
				}

				var part = new SceneItem(cellMeshes[i], transform, textureHandle);
				draw.Items.Add(part);
				if (cell.Leaf is { } leaf) {
					if (!groups.TryGetValue(leaf.Tree, out var group)) {
						groups[leaf.Tree] = group = BspDrawGroup.AtRest(leaf.Tree, () => draw.Transform);
					}

					part.BspGroup = group;
					part.BspLeaf = leaf.Index;
					part.PaintLayer = cell.Layer;
				}
			}
		} else if (_modelMeshes.TryGetValue(model.Key, out var mesh)) {
			draw.Items.Add(new SceneItem(mesh, transform, textureHandle));
		} else {
			return;
		}

		// Filed as the simulator's submit files a structure: by its position and its body radius.
		if (_groundMeshes.TryGetValue(model.Key, out var groundMesh)) {
			var groundItem = new SceneItem(groundMesh, transform, textureHandle);
			draw.Items.Add(groundItem);
			draw.GroundShape = GroundLayer.Shapes.Count;
			GroundLayer.Shapes.Add(new GroundShapeDraw(groundItem, sceneObject.Object.Position,
				sceneObject.Object.HitRadius));
		}

		_objects[sceneObject] = draw;
		_items.AddRange(draw.Items);
		FileItems(sceneObject, draw);
	}

	// The model <sceneObject> is drawn as: its own, or for a structure placed already fallen the wreck its type leaves, which
	// the simulator draws in the building's place (WorldDrawItems.RefreshWreckItems); its own where the type leaves none.
	// Nothing ticks, so the choice is made once: a starting condition edited builds a new object (SceneSync).
	private SceneModel? DrawnModel(SceneObject sceneObject) =>
		sceneObject.Object is BaseObject { ShowingHulk: true } structure && _scene.Hulk(structure.Type.HulkTypeIndex) is { } hulk
			? hulk
			: sceneObject.Model;

	/// <summary>Stops drawing <paramref name="sceneObject"/>. Its model's uploads stay, for a redo to draw it again.</summary>
	public void Remove(SceneObject sceneObject) {
		if (!_objects.Remove(sceneObject, out var draw)) {
			return;
		}

		foreach (var item in draw.Items) {
			_items.Remove(item);
			WaitingItems.Remove(item);
			_offScene.Remove(item);
		}

		if (draw.GroundShape >= 0) {
			GroundLayer.Shapes.RemoveAt(draw.GroundShape);
			foreach (var other in _objects.Values) {
				if (other.GroundShape > draw.GroundShape) {
					other.GroundShape--;
				}
			}
		}
	}

	/// <summary>
	/// Lists again which drawn objects are out of the scene and which in it wait on an action, for an index built again.
	/// </summary>
	public void RefreshWaiting() {
		foreach (var item in WaitingItems.Concat(_offScene)) {
			item.Visible = true;
		}

		WaitingItems.Clear();
		_offScene.Clear();
		foreach (var (sceneObject, draw) in _objects) {
			FileItems(sceneObject, draw);
		}
	}

	// Files an object's items as out of the scene, hidden for good, or as waiting, for the settings to show or hide.
	private void FileItems(SceneObject sceneObject, ObjectDraw draw) {
		if (!_inScene(sceneObject)) {
			foreach (var item in draw.Items) {
				item.Visible = false;
			}

			_offScene.AddRange(draw.Items);
		} else if (_isWaiting(sceneObject)) {
			WaitingItems.AddRange(draw.Items);
		}
	}

	/// <summary>The objects drawn now: in the scene, and not hidden as waiting by the settings.</summary>
	public IEnumerable<SceneObject> Shown =>
		_objects.Where(entry => entry.Value.Items.Count > 0 && entry.Value.Items[0].Visible).Select(entry => entry.Key);

	/// <summary>Re-places <paramref name="sceneObject"/>'s items where its simulation object now stands.</summary>
	public void Move(SceneObject sceneObject) {
		if (!_objects.TryGetValue(sceneObject, out var draw)) {
			return;
		}

		draw.Transform = MissionScene.TransformOf(sceneObject);
		foreach (var item in draw.Items) {
			item.Transform = draw.Transform;
		}

		if (draw.GroundShape >= 0) {
			var shape = GroundLayer.Shapes[draw.GroundShape];
			GroundLayer.Shapes[draw.GroundShape] = shape with { Position = sceneObject.Object.Position };
		}
	}

	/// <summary>Uploads again the run of terrain vertices a re-levelling rebuilt (<see cref="TerrainLeveller.Changed"/>).</summary>
	public void UploadTerrain(int firstVertex, int vertexCount) =>
		_terrainMesh.Update(firstVertex, _terrainVertices.AsSpan(firstVertex, vertexCount));

	// Uploads a model's meshes and texture, once. A shape split by cell — a structure or a flyer — is drawn a piece at
	// a time as the simulator draws it, so that a TSBSPPart's children and a decal's paint layer are painted in order
	// (BspDrawGroup); the flat mesh would leave them to the depth test.
	private void UploadModel(SceneModel model) {
		if (_modelCells.ContainsKey(model.Key) || _modelMeshes.ContainsKey(model.Key)) {
			return;
		}

		if (model.Cells.Length > 0) {
			_modelCells[model.Key] = model.Cells
				.Select(cell => Keep(new GpuMesh(_gl, cell.Vertices, cell.TriangleVertexCount, cell.PointVertexCount)))
				.ToArray();
		} else {
			_modelMeshes[model.Key] = Keep(new GpuMesh(_gl, model.Mesh, model.TriangleVertexCount, model.PointVertexCount));
		}

		if (model.GroundMesh is { Vertices.Length: > 0 } ground) {
			_groundMeshes[model.Key] = Keep(new GpuMesh(_gl, ground.Vertices, ground.TriangleVertexCount,
				ground.PointVertexCount));
		}

		if (model.Atlas != null) {
			_modelTextures[model.Key] = Keep(model.Atlas.Upload(_gl, indexed: true));
		}
	}

	// One object's items, and the transform its BSP draw groups read each frame.
	private sealed class ObjectDraw {
		public Matrix4x4 Transform;
		public readonly List<SceneItem> Items = new();
		public int GroundShape = -1;
	}

	private readonly Dictionary<SceneObject, ObjectDraw> _objects = new(ReferenceEqualityComparer.Instance);

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
