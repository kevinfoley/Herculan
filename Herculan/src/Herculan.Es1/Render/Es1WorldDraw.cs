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
/// <c>ObjList_DrawFrameObjects</c> (<c>ES1DBSIM 00021008</c>) paints first, within 30000 of the camera, with the impact
/// effects' ground shapes; then the zone's shapes, the HERCs, the projectiles, the impact effects, the debris and the
/// smoke balls, each but the one
/// the camera rides in, one whose Z is negative, one past the last distance band and one whose group an action still
/// holds out of the mission (<c>ObjList_RebuildFrame</c>, <c>00020de0</c>), each lit by the sun and the effect lights
/// <c>LightManager_SelectLightsForObject</c> picks for it (<see cref="SceneItem.LightAnchor"/>).
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

	// The camera and ridden machine of the last Update, which DrawFlatView places the items back for.
	private (Camera Camera, Es1Mech? Ridden)? _placedFor;

	/// <param name="detail">The detail setting the HERCs are drawn at, read on every <see cref="Update"/>.</param>
	/// <param name="projectiles">The projectile pool's draw; with none no projectile is drawn.</param>
	/// <param name="explosions">The impact effects' draw; with none no effect is drawn.</param>
	/// <param name="debris">The debris' draw; with none no piece is drawn.</param>
	/// <param name="smoke">The smoke balls' draw; with none no ball is drawn.</param>
	public Es1WorldDraw(GL gl, Es1ZoneScene scene, Es1World world, Es1MechModels models, Es1AppliedDetail detail,
			ICollection<IDisposable> owned, Es1ProjectileDraw? projectiles = null, Es1ExplosionDraw? explosions = null,
			Es1DebrisDraw? debris = null, Es1SmokeDraw? smoke = null) {
		_scene = scene;
		_detail = detail;
		Projectiles = projectiles;
		Explosions = explosions;
		Debris = debris;
		Smoke = smoke;

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
			// The effect lights are picked by the object's position and its shape's radius, slot +0x14's.
			int radius = index < scene.ZoneShapes.Objects.Count ? scene.ZoneShapes.Objects[index].Radius : 0;
			var frame = shape.Frame;
			_zoneItems.Add((new SceneItem(mesh, WorldScale.ToRenderMatrix(frame)) {
				LightAnchor = (new Vec3i(frame.X, frame.Y, frame.Z), radius),
			}, shape, zoneObject));
			_flatZoneItems.Add(new SceneItem(mesh, WorldScale.ToRenderMatrix(shape.Frame)) { RampBlock = scene.Shading.FlatBlock });
		}

		foreach (var mech in world.MechPool) {
			_mechs.Add(new MechItems(gl, scene, mech, models));
			foreach (var shadow in mech.Shadows) {
				if (shadow is { Root: >= 0 } && shadow.Root < models.ShadowRoots.Count && models.ShadowRoots[shadow.Root] is { } mesh) {
					_shadowItems.Add((new SceneItem(mesh, Matrix4x4.Identity), shadow));
				}
			}
		}
	}

	/// <summary>The shadows' items and the impact effects' ground shapes, drawn first.</summary>
	public IEnumerable<SceneItem> Shadows => _shadowItems.Select(entry => entry.Item).Concat(Explosions?.GroundItems ?? []);

	/// <summary>
	/// The zone shapes as <see cref="View.Es1RectScene.FlatZoneShapes"/> draws them, apart from the frame's own items:
	/// each of them, in <c>Zone_ShapeObjects</c>' order, always visible, with the ramps <c>WorldShades_ResetRamps</c>
	/// leaves (<see cref="Es1Shading.FlatBlock"/>).
	/// </summary>
	public IReadOnlyList<SceneItem> FlatZoneShapes => _flatZoneItems;

	/// <summary>The projectile pool's draw, whose items <see cref="Objects"/> lists after the HERCs'; null for none.</summary>
	public Es1ProjectileDraw? Projectiles { get; }

	/// <summary>The impact effects' draw, whose items <see cref="Objects"/> lists after the projectiles'; null for none.</summary>
	public Es1ExplosionDraw? Explosions { get; }

	/// <summary>The debris' draw, whose items <see cref="Objects"/> lists after the impact effects'; null for none.</summary>
	public Es1DebrisDraw? Debris { get; }

	/// <summary>The smoke balls' draw, whose items <see cref="Objects"/> ends with; null for none.</summary>
	public Es1SmokeDraw? Smoke { get; }

	/// <summary>The zone shapes', the HERCs', the projectiles', the impact effects', the debris' and the smoke balls' items, in <c>ObjList_RebuildFrame</c>'s order.</summary>
	public IEnumerable<SceneItem> Objects => _zoneItems.Select(entry => entry.Item).Concat(_mechs.SelectMany(m => m.Items))
		.Concat(Projectiles?.Items ?? []).Concat(Explosions?.Items ?? []).Concat(Debris?.Items ?? [])
		.Concat(Smoke?.Items ?? []);

	/// <summary>The impact effects' and the smoke balls' billboards, drawn after <see cref="Objects"/>.</summary>
	public IReadOnlyList<SpriteBatch> Sprites => [.. Explosions?.Sprites ?? [], .. Smoke?.Sprites ?? []];

	/// <summary>
	/// The objects <c>ObjList_DrawFrameObjects</c> draws, each with its items, in the order <see cref="Objects"/> lists
	/// their items: each zone shape, as its <see cref="Es1ShapeObject"/>, then each HERC.
	/// </summary>
	public IEnumerable<(object Object, IEnumerable<SceneItem> Items)> FrameObjects =>
		_zoneItems.Select(entry => (entry.Object, (IEnumerable<SceneItem>)[entry.Item]))
			.Concat(_mechs.Select(mech => ((object)mech.Mech, mech.Items)));

	/// <summary>Places and culls every item for <paramref name="camera"/>; <paramref name="ridden"/> is the machine the camera rides in, or null.</summary>
	public void Update(Camera camera, Es1Mech? ridden) {
		_placedFor = (camera, ridden);
		Place(camera, ridden, Es1ShapeDetail.FocalShift, flat: false);
	}

	/// <summary>
	/// A second view of the frame's objects, <c>ObjList_DrawFrameObjects</c>(0, <paramref name="camera"/>) while
	/// <c>000928ea</c> is set (docs/es1/rendering/polygon-shading.md#the-distance-bands): every item placed and culled
	/// for <paramref name="camera"/> as <see cref="Update"/> places them, with no machine ridden, each detail level
	/// picked under a focal length of 2^<paramref name="focalShift"/>, and every object on <c>WorldShades_ResetRamps</c>'
	/// ramps (<see cref="Es1Shading.FlatBlock"/>) in place of its band's; then <paramref name="draw"/>; then every item
	/// placed again for the camera and machine of the last <see cref="Update"/>, so the items stand as the frame's own
	/// pass placed them, which the pick reads (<see cref="Es1WorldPick"/>).
	/// </summary>
	public void DrawFlatView(Camera camera, int focalShift, Action draw) {
		Place(camera, null, focalShift, flat: true);
		draw();
		if (_placedFor is { } last) {
			Place(last.Camera, last.Ridden, Es1ShapeDetail.FocalShift, flat: false);
		}
	}

	private void Place(Camera camera, Es1Mech? ridden, int focalShift, bool flat) {
		var shading = _scene.Shading;
		foreach (var (item, shape, _) in _zoneItems) {
			var frame = shape.Frame;
			int distance = camera.Position.ApproxDistanceTo(new Vec3i(frame.X, frame.Y, frame.Z));
			item.Visible = frame.Z >= 0 && !shading.IsBeyondLastBand(Es1Shading.ZoneShapeTag, distance);
			item.RampBlock = flat ? shading.FlatBlock : shading.Block(Es1Shading.ZoneShapeTag, distance);
		}

		foreach (var mech in _mechs) {
			mech.Update(camera, shading, ridden, _detail.Setting, focalShift, flat);
		}

		Projectiles?.Update(camera, flat);
		Explosions?.Update(camera, focalShift, flat);
		Debris?.Update(camera, focalShift, flat);
		Smoke?.Update(camera, focalShift, flat);

		foreach (var (item, shadow) in _shadowItems) {
			var frame = shadow.DrawFrame;
			item.Transform = WorldScale.ToRenderMatrix(frame);
			item.Visible = !shadow.Destroyed && camera.Position.ApproxDistanceTo(new Vec3i(frame.X, frame.Y, frame.Z)) < ShadowRange;
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

		/// <param name="focalShift">The installed projection's <c>Raster_FocalShift</c>, which the detail levels are picked under.</param>
		/// <param name="flat">Whether the ramps are <c>WorldShades_ResetRamps</c>' rather than the distance band's.</param>
		public void Update(Camera camera, Es1Shading shading, Es1Mech? ridden, Es1DetailSetting detail, int focalShift, bool flat) {
			_frame = _mech.Frame;
			int distance = camera.Position.ApproxDistanceTo(_mech.Position);

			// ObjList_RebuildFrame (00020de0) lists no object whose group holds an action
			// (docs/es1/rendering/polygon-shading.md#the-frames-draw).
			bool drawn = _mech != ridden && _mech.Position.Z >= 0
				&& !shading.IsBeyondLastBand((byte)Es1Mech.TypeTag, distance)
				&& _mech.Group?.Action == null;
			int? block = flat ? shading.FlatBlock : shading.Block((byte)Es1Mech.TypeTag, distance);

			var type = _mech.Type;
			int root = Es1ShapeDetail.SelectRoot(type.Roots.Count, type.ShapeRadius,
				Es1ShapeDetail.EyeDistance(_frame, camera.Position), detail, Es1MechType.DetailThresholds, focalShift);

			// Each cell-animation part shows the cell the damage set for its sequence (Es1Mech.CellFrame); a HERC body's
			// detail parts draw with bias 0.
			// The effect lights are picked once for the machine, by its position and slot +0x14's radius.
			var anchor = (_mech.Position, (int)_mech.ShapeRadius);
			foreach (var (item, piece, pieceRoot) in _pieces) {
				item.Transform = FrameToWorld(piece.TransformId);
				item.RampBlock = block;
				item.LightAnchor = anchor;
				item.Visible = drawn && pieceRoot == root
					&& OnScreen(piece.Gate, camera, focalShift, 0, null, _mech.CellFrame(piece.Gate.Sequence));
			}

			// A mount's model shows its own private cell frames, which the muzzle flash and the ELF's spin-up step.
			var slots = root < type.HardpointSlots.Count ? type.HardpointSlots[root] : [];
			var mounts = _mech.Mounts?.Mounts ?? [];
			foreach (var (item, piece, hardpoint, transformId) in _mountPieces) {
				item.Transform = FrameToWorld(transformId);
				item.RampBlock = block;
				item.LightAnchor = anchor;
				// A destroyed mount's model is cleared, and the hardpoint draws nothing (WeaponMount_Destroy).
				int cell = mounts[hardpoint]?.CellFrame(piece.Gate.Sequence) ?? 0;
				item.Visible = drawn && mounts[hardpoint]?.Model != null && hardpoint < slots.Length && slots[hardpoint] != null
					&& OnScreen(piece.Gate, camera, focalShift, detail.MountBias, transformId, cell);
			}
		}

		/// <summary>
		/// Whether a piece's cell is cell <paramref name="cell"/> and its <c>TSDetailPart</c>, if any, picks its level with
		/// <paramref name="bias"/> under a focal length of 2^<paramref name="focalShift"/> from the eye's distance in the
		/// part's node, <paramref name="node"/> when given (a mount's model, whose parts all carry the stamped node).
		/// </summary>
		private bool OnScreen(CellGate gate, Camera camera, int focalShift, int bias, int? node = null, int cell = 0) {
			if (gate.IsGated && gate.Frame != cell) {
				return false;
			}

			if (gate.Detail is not { } detail) {
				return true;
			}

			int eye = Es1ShapeDetail.EyeDistance(NodeFrame(node ?? detail.TransformId), camera.Position);
			return detail.Select(eye, 1 << focalShift, bias) == gate.Level;
		}

		/// <summary>A node's posed transform in front of the machine's frame, in world units; the frame alone for -1.</summary>
		private Transform3 NodeFrame(int transformId) =>
			transformId < 0 || _mech.Shape == null ? _frame : Transform3.Concat(_mech.Shape.NodeTransform(transformId), _frame);

	/// <summary>A node's posed transform in front of the machine's frame, in render space; the frame alone for -1.</summary>
		private Matrix4x4 FrameToWorld(int transformId) => WorldScale.ToRenderMatrix(NodeFrame(transformId));
	}
}
