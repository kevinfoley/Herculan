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
	private readonly Es1AppliedDetail _detail;
	private readonly List<(SceneItem Item, Es1PlacedShape Shape, object Object)> _zoneItems = new();
	private readonly List<SceneItem> _flatZoneItems = new();
	private readonly List<MechItems> _mechs = new();
	private readonly List<(SceneItem Item, Es1Shadow Shadow)> _shadowItems = new();

	/// <param name="detail">The detail setting the HERCs are drawn at, read on every <see cref="Update"/>.</param>
	/// <param name="projectiles">The projectile pool's draw; with none no projectile is drawn.</param>
	public Es1WorldDraw(GL gl, Es1ZoneScene scene, Es1World world, Es1MechModels models, Es1AppliedDetail detail,
			ICollection<IDisposable> owned, Es1ProjectileDraw? projectiles = null) {
		_scene = scene;
		_detail = detail;
		Projectiles = projectiles;

		var meshes = new Dictionary<int, GpuMesh>();
		for (int index = 0; index < scene.Shapes.Count; index++) {
			var shape = scene.Shapes[index];
			if (ModelOf(scene, shape) is not { Vertices.Length: > 0 } model) {
				continue;
			}

			if (!meshes.TryGetValue(shape.ShapeIndex, out var mesh)) {
				mesh = new GpuMesh(gl, model.Vertices, model.TriangleVertexCount, model.PointVertexCount);
				meshes[shape.ShapeIndex] = mesh;
				owned.Add(mesh);
			}

			// The zone's shape object, Zone_ShapeObjects' entry for the record, boxed once so the pick answers one object for it.
			object zoneObject = index < scene.ZoneShapes.Objects.Count ? scene.ZoneShapes.Objects[index] : shape;
			_zoneItems.Add((new SceneItem(mesh, WorldScale.ToRenderMatrix(shape.Frame)), shape, zoneObject));
			_flatZoneItems.Add(new SceneItem(mesh, WorldScale.ToRenderMatrix(shape.Frame)) { RampBlock = scene.Shading.FlatBlock });
		}

		foreach (var mech in world.MechPool) {
			_mechs.Add(new MechItems(gl, scene, mech, models));
			foreach (var shadow in mech.Shadows) {
				if (shadow.Root >= 0 && shadow.Root < models.ShadowRoots.Count && models.ShadowRoots[shadow.Root] is { } mesh) {
					_shadowItems.Add((new SceneItem(mesh, Matrix4x4.Identity), shadow));
				}
			}
		}
	}

	/// <summary>The shadows' items, drawn first.</summary>
	public IEnumerable<SceneItem> Shadows => _shadowItems.Select(entry => entry.Item);

	/// <summary>
	/// The zone shapes as <see cref="View.Es1RectScene.FlatZoneShapes"/> draws them, apart from the frame's own items:
	/// each of them, in <c>Zone_ShapeObjects</c>' order, always visible, with the ramps <c>WorldShades_ResetRamps</c>
	/// leaves (<see cref="Es1Shading.FlatBlock"/>).
	/// </summary>
	public IReadOnlyList<SceneItem> FlatZoneShapes => _flatZoneItems;

	/// <summary>The projectile pool's draw, whose items <see cref="Objects"/> ends with; null for none.</summary>
	public Es1ProjectileDraw? Projectiles { get; }

	/// <summary>The zone shapes', the HERCs' and the projectiles' items.</summary>
	public IEnumerable<SceneItem> Objects => _zoneItems.Select(entry => entry.Item).Concat(_mechs.SelectMany(m => m.Items))
		.Concat(Projectiles?.Items ?? []);

	/// <summary>
	/// The objects <c>ObjList_DrawFrameObjects</c> draws, each with its items, in the order <see cref="Objects"/> lists
	/// their items: each zone shape, as its <see cref="Es1ShapeObject"/>, then each HERC.
	/// </summary>
	public IEnumerable<(object Object, IEnumerable<SceneItem> Items)> FrameObjects =>
		_zoneItems.Select(entry => (entry.Object, (IEnumerable<SceneItem>)[entry.Item]))
			.Concat(_mechs.Select(mech => ((object)mech.Mech, mech.Items)));

	/// <summary>Places and culls every item for <paramref name="camera"/>; <paramref name="ridden"/> is the machine the camera rides in, or null.</summary>
	public void Update(Camera camera, Es1Mech? ridden) {
		var shading = _scene.Shading;
		foreach (var (item, shape, _) in _zoneItems) {
			var frame = shape.Frame;
			int distance = camera.Position.ApproxDistanceTo(new Vec3i(frame.X, frame.Y, frame.Z));
			item.Visible = frame.Z >= 0 && !shading.IsBeyondLastBand(Es1Shading.ZoneShapeTag, distance);
			item.RampBlock = shading.Block(Es1Shading.ZoneShapeTag, distance);
		}

		foreach (var mech in _mechs) {
			mech.Update(camera, shading, ridden, _detail.Setting);
		}

		Projectiles?.Update(camera);

		foreach (var (item, shadow) in _shadowItems) {
			var frame = shadow.DrawFrame;
			item.Transform = WorldScale.ToRenderMatrix(frame);
			item.Visible = camera.Position.ApproxDistanceTo(new Vec3i(frame.X, frame.Y, frame.Z)) < ShadowRange;
		}
	}

	private static MeshBuild? ModelOf(Es1ZoneScene scene, Es1PlacedShape shape) =>
		shape.ShapeIndex >= 0 && shape.ShapeIndex < scene.Models.Count ? scene.Models[shape.ShapeIndex] : null;

	/// <summary>
	/// One HERC's items: a piece per node, cell and BSP child of each root, and of each fitted mount's model, each posed
	/// through its node. <c>Mech_Draw</c> (<c>00012ac4</c>) splices the hardpoints of every root and draws the root
	/// <c>Shape_DrawAtDetailLevel</c> picks (docs/es1/simulation/object-classes.md#drawing): the pieces of that root, and in
	/// each of its hardpoint slots the mount's model under the node stamped on it, a blank part drawing nothing.
	/// </summary>
	private sealed class MechItems {
		private readonly Es1Mech _mech;
		private readonly List<(SceneItem Item, Es1MechPiece Piece, int Root)> _pieces = new();
		private readonly List<(SceneItem Item, Es1MechPiece Piece, int Hardpoint, int TransformId)> _mountPieces = new();
		private readonly Dictionary<BspTree, BspDrawGroup> _groups = new();
		private Transform3 _frame;

		public MechItems(GL gl, Es1ZoneScene scene, Es1Mech mech, Es1MechModels models) {
			_mech = mech;
			uint? texture = models.Textures.TryGetValue(mech.Type.Type, out uint handle) ? handle : null;
			var roots = models.PiecesOf(mech.Type.Type);
			for (int root = 0; root < roots.Count; root++) {
				foreach (var piece in roots[root]) {
					_pieces.Add((NewItem(piece, texture), piece, root));
				}
			}

			var mounts = mech.Mounts?.Mounts ?? [];
			for (int h = 0; h < mounts.Count; h++) {
				if (mounts[h] is not { } mount) {
					continue;
				}

				foreach (var piece in models.PiecesOfMount(gl, mount, scene)) {
					_mountPieces.Add((NewItem(piece, models.WeaponTexture), piece, h, mount.ModelTransformId));
				}
			}
		}

		public Es1Mech Mech => _mech;

		public IEnumerable<SceneItem> Items => _pieces.Select(entry => entry.Item).Concat(_mountPieces.Select(entry => entry.Item));

		private SceneItem NewItem(Es1MechPiece piece, uint? texture) {
			var item = new SceneItem(piece.Mesh, Matrix4x4.Identity, texture) { TextureWalk = piece.TextureWalk };
			if (piece.Leaf is { } leaf) {
				if (!_groups.TryGetValue(leaf.Tree, out var group)) {
					group = new BspDrawGroup(leaf.Tree, FrameToWorld);
					_groups[leaf.Tree] = group;
				}

				item.BspGroup = group;
				item.BspLeaf = leaf.Index;
				item.PaintLayer = piece.Layer;
			}

			return item;
		}

		public void Update(Camera camera, Es1Shading shading, Es1Mech? ridden, Es1DetailSetting detail) {
			_frame = _mech.Frame;
			int distance = camera.Position.ApproxDistanceTo(_mech.Position);

			// ObjList_RebuildFrame (00020de0) lists no object whose group holds an action
			// (docs/es1/rendering/polygon-shading.md#the-frames-draw).
			bool drawn = _mech != ridden && _mech.Position.Z >= 0
				&& !shading.IsBeyondLastBand((byte)Es1Mech.TypeTag, distance)
				&& _mech.Group?.Action == null;
			int? block = shading.Block((byte)Es1Mech.TypeTag, distance);

			var type = _mech.Type;
			int root = Es1ShapeDetail.SelectRoot(type.Roots.Count, type.ShapeRadius,
				Es1ShapeDetail.EyeDistance(_frame, camera.Position), detail, Es1MechType.DetailThresholds);

			// Every component intact stands each cell-animation part on cell 0; a HERC body's detail parts draw with bias 0.
			foreach (var (item, piece, pieceRoot) in _pieces) {
				item.Transform = FrameToWorld(piece.TransformId);
				item.RampBlock = block;
				item.Visible = drawn && pieceRoot == root && OnScreen(piece.Gate, camera, 0);
			}

			// A mount's model shows its own private cell frames, which the muzzle flash and the ELF's spin-up step.
			var slots = root < type.HardpointSlots.Count ? type.HardpointSlots[root] : [];
			var mounts = _mech.Mounts?.Mounts ?? [];
			foreach (var (item, piece, hardpoint, transformId) in _mountPieces) {
				item.Transform = FrameToWorld(transformId);
				item.RampBlock = block;
				int cell = mounts[hardpoint]?.CellFrame(piece.Gate.Sequence) ?? 0;
				item.Visible = drawn && hardpoint < slots.Length && slots[hardpoint] != null
					&& OnScreen(piece.Gate, camera, detail.MountBias, transformId, cell);
			}
		}

		/// <summary>
		/// Whether a piece's cell is cell <paramref name="cell"/> and its <c>TSDetailPart</c>, if any, picks its level with
		/// <paramref name="bias"/> from the eye's distance in the part's node, <paramref name="node"/> when given (a mount's
		/// model, whose parts all carry the stamped node).
		/// </summary>
		private bool OnScreen(CellGate gate, Camera camera, int bias, int? node = null, int cell = 0) {
			if (gate.IsGated && gate.Frame != cell) {
				return false;
			}

			if (gate.Detail is not { } detail) {
				return true;
			}

			int eye = Es1ShapeDetail.EyeDistance(NodeFrame(node ?? detail.TransformId), camera.Position);
			return detail.Select(eye, 1 << Es1ShapeDetail.FocalShift, bias) == gate.Level;
		}

		/// <summary>A node's posed transform in front of the machine's frame, in world units; the frame alone for -1.</summary>
		private Transform3 NodeFrame(int transformId) =>
			transformId < 0 || _mech.Shape == null ? _frame : Transform3.Concat(_mech.Shape.NodeTransform(transformId), _frame);

	/// <summary>A node's posed transform in front of the machine's frame, in render space; the frame alone for -1.</summary>
		private Matrix4x4 FrameToWorld(int transformId) => WorldScale.ToRenderMatrix(NodeFrame(transformId));
	}
}
