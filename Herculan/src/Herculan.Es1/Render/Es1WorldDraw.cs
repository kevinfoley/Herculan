using System.Numerics;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Scene;
using Herculan.Es1.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// What one frame draws (docs/es1/rendering/polygon-shading.md#which-objects-are-drawn): the shadows, which
/// <c>ObjList_DrawFrameObjects</c> (<c>ES1DBSIM 00021008</c>) paints first, within 30000 of the camera; then the zone's
/// shapes and the HERCs, each but the one the camera rides in, one whose Z is negative, one past the last distance band
/// and one whose group an action still holds out of the mission (<c>ObjList_RebuildFrame</c>, <c>00020de0</c>).
///
/// <para>Retail paints the object list sorted, farthest first, with no depth test; the shapes and HERCs here are drawn
/// with it, a divergence the user chose (KNOWN_ISSUES.md). The shadows keep retail's order against them: they are drawn
/// in a pass of their own before the objects, so every object paints over them.</para>
/// </summary>
public sealed class Es1WorldDraw {
	/// <summary>How far from the camera a shadow is drawn.</summary>
	public const int ShadowRange = 30000;

	private readonly Es1ZoneScene _scene;
	private readonly List<(SceneItem Item, Es1PlacedShape Shape)> _zoneItems = new();
	private readonly List<MechItems> _mechs = new();
	private readonly List<(SceneItem Item, Es1Shadow Shadow)> _shadowItems = new();

	public Es1WorldDraw(GL gl, Es1ZoneScene scene, Es1World world, Es1MechModels models, ICollection<IDisposable> owned) {
		_scene = scene;

		var meshes = new Dictionary<int, GpuMesh>();
		foreach (var shape in scene.Shapes) {
			if (ModelOf(scene, shape) is not { Vertices.Length: > 0 } model) {
				continue;
			}

			if (!meshes.TryGetValue(shape.ShapeIndex, out var mesh)) {
				mesh = new GpuMesh(gl, model.Vertices, model.TriangleVertexCount, model.PointVertexCount);
				meshes[shape.ShapeIndex] = mesh;
				owned.Add(mesh);
			}

			_zoneItems.Add((new SceneItem(mesh, WorldScale.ToRenderMatrix(shape.Frame)), shape));
		}

		foreach (var mech in world.MechPool) {
			_mechs.Add(new MechItems(mech, models));
			foreach (var shadow in mech.Shadows) {
				if (shadow.Root >= 0 && shadow.Root < models.ShadowRoots.Count && models.ShadowRoots[shadow.Root] is { } mesh) {
					_shadowItems.Add((new SceneItem(mesh, Matrix4x4.Identity), shadow));
				}
			}
		}
	}

	/// <summary>The shadows' items, drawn first.</summary>
	public IEnumerable<SceneItem> Shadows => _shadowItems.Select(entry => entry.Item);

	/// <summary>The zone shapes' and the HERCs' items.</summary>
	public IEnumerable<SceneItem> Objects => _zoneItems.Select(entry => entry.Item).Concat(_mechs.SelectMany(m => m.Items));

	/// <summary>Places and culls every item for <paramref name="camera"/>; <paramref name="ridden"/> is the machine the camera rides in, or null.</summary>
	public void Update(Camera camera, Es1Mech? ridden) {
		var shading = _scene.Shading;
		foreach (var (item, shape) in _zoneItems) {
			var frame = shape.Frame;
			int distance = camera.Position.ApproxDistanceTo(new Vec3i(frame.X, frame.Y, frame.Z));
			item.Visible = frame.Z >= 0 && !shading.IsBeyondLastBand(Es1Shading.ZoneShapeTag, distance);
			item.RampBlock = shading.Block(Es1Shading.ZoneShapeTag, distance);
		}

		foreach (var mech in _mechs) {
			mech.Update(camera, shading, ridden);
		}

		foreach (var (item, shadow) in _shadowItems) {
			var frame = shadow.DrawFrame;
			item.Transform = WorldScale.ToRenderMatrix(frame);
			item.Visible = camera.Position.ApproxDistanceTo(new Vec3i(frame.X, frame.Y, frame.Z)) < ShadowRange;
		}
	}

	private static MeshBuild? ModelOf(Es1ZoneScene scene, Es1PlacedShape shape) =>
		shape.ShapeIndex >= 0 && shape.ShapeIndex < scene.Models.Count ? scene.Models[shape.ShapeIndex] : null;

	/// <summary>One HERC's items: a piece per node, cell and BSP child of its drawn root, each posed through its node.</summary>
	private sealed class MechItems {
		private readonly Es1Mech _mech;
		private readonly List<(SceneItem Item, Es1MechPiece Piece)> _pieces = new();
		private readonly Dictionary<BspTree, BspDrawGroup> _groups = new();
		private Transform3 _frame;

		public MechItems(Es1Mech mech, Es1MechModels models) {
			_mech = mech;
			uint? texture = models.Textures.TryGetValue(mech.Type.Type, out uint handle) ? handle : null;
			foreach (var piece in models.PiecesOf(mech.Type.Type)) {
				var item = new SceneItem(piece.Mesh, Matrix4x4.Identity, texture);
				if (piece.Leaf is { } leaf) {
					if (!_groups.TryGetValue(leaf.Tree, out var group)) {
						group = new BspDrawGroup(leaf.Tree, FrameToWorld);
						_groups[leaf.Tree] = group;
					}

					item.BspGroup = group;
					item.BspLeaf = leaf.Index;
					item.PaintLayer = piece.Layer;
				}

				_pieces.Add((item, piece));
			}
		}

		public IEnumerable<SceneItem> Items => _pieces.Select(entry => entry.Item);

		public void Update(Camera camera, Es1Shading shading, Es1Mech? ridden) {
			_frame = _mech.Frame;
			int distance = camera.Position.ApproxDistanceTo(_mech.Position);

			// ObjList_RebuildFrame (00020de0) lists no object whose group holds an action
			// (docs/es1/rendering/polygon-shading.md#the-frames-draw).
			bool drawn = _mech != ridden && _mech.Position.Z >= 0
				&& !shading.IsBeyondLastBand((byte)Es1Mech.TypeTag, distance)
				&& _mech.Group?.Action == null;
			int? block = shading.Block((byte)Es1Mech.TypeTag, distance);
			foreach (var (item, piece) in _pieces) {
				item.Transform = FrameToWorld(piece.TransformId);
				item.RampBlock = block;

				// Every component intact stands each cell-animation part on cell 0. PLACEHOLDER: a TSDetailPart draws its
				// finest level; no HERC's root 0 holds one, and the detail bias ES1 sets is step 6's (TSDetailPart_SetBias).
				item.Visible = drawn && (!piece.Gate.IsGated || piece.Gate.Frame == 0)
					&& (!piece.Gate.IsDetailGated || piece.Gate.Level == 0);
			}
		}

		/// <summary>A node's posed transform in front of the machine's frame, in render space; the frame alone for -1.</summary>
		private Matrix4x4 FrameToWorld(int transformId) =>
			WorldScale.ToRenderMatrix(transformId < 0 || _mech.Shape == null
				? _frame
				: Transform3.Concat(_mech.Shape.NodeTransform(transformId), _frame));
	}
}
