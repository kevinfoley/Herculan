using System.Numerics;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Scene;
using Herculan.Es1.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// The structures' share of the frame: each structure of <c>g_BasePool</c> drawn as <c>Structure_DrawWithDetailBias</c>
/// (<c>ES1DBSIM 00002d18</c>) draws it, <c>TSDetailPart_SetBias</c>(<c>g_TSDetailBiasForStructures</c>),
/// <c>Shape_DrawAtDetailLevel</c>, then the bias back to 0 (docs/es1/simulation/structures.md#drawing). A structure has
/// no LOD table, so <c>Shape_DrawAtDetailLevel</c> draws the shape its instance holds whole, the standing shape or the
/// wreck, at the structure's frame: a radar mast's or gun tower's nodes posed by its threads, each cell part at the cell
/// its sequence stands on, each <c>TSDetailPart</c> at the level <see cref="Es1DetailSetting.ObjectBias"/> picks. A
/// shape's textured faces take its bank, <c>dba\basetex</c> for a standing shape and none for a wreck, and are filled by
/// the row walk as a HERC's are (plan decision 8). It is listed as <c>ObjList_RebuildFrame</c> lists the live objects
/// (docs/es1/rendering/polygon-shading.md#the-object-list): not while its group's action holds it out, nor with its Z
/// negative or past tag 5's last distance band; and it shades in tag 5's band, both at the distance its radius offsets
/// (<see cref="Es1Shading.Band"/>).
/// </summary>
public sealed class Es1BaseDraw : IDisposable {
	private readonly Es1ZoneScene _scene;
	private readonly Es1AppliedDetail _detail;
	private readonly List<IDisposable> _owned = new();
	private readonly Dictionary<Es1BaseShape, IReadOnlyList<Es1MechPiece>> _shapes = new();
	private readonly Dictionary<DynamixBitmapArray, (TextureAtlas Atlas, uint Handle)> _banks = new();
	private readonly List<StructureItems> _structures = new();

	/// <summary>Uploads each shape a structure of <paramref name="structures"/> stands or falls as, and lists an item set for each structure.</summary>
	/// <param name="types">The structures' tables, whose wrecks a standing structure can fall to; none uploads no wreck.</param>
	/// <param name="detail">The detail setting the structures are drawn at, read on every <see cref="Update"/>.</param>
	public Es1BaseDraw(GL gl, Es1ZoneScene scene, IEnumerable<Es1Base> structures, Es1BaseTypes? types, Es1AppliedDetail detail) {
		_scene = scene;
		_detail = detail;
		foreach (var structure in structures) {
			var items = new StructureItems(structure);
			Add(gl, items, structure.StandingShape);
			if (types?.Hulk(structure.Type.HulkIndex) is { } hulk) {
				Add(gl, items, hulk);
			}

			_structures.Add(items);
		}
	}

	/// <summary>The structures' items, in the order the live object list holds the structures.</summary>
	public IEnumerable<SceneItem> Items => _structures.SelectMany(structure => structure.Items);

	/// <summary>Each structure with its items, for the pixel pick.</summary>
	public IEnumerable<(object Object, IEnumerable<SceneItem> Items)> FrameObjects =>
		_structures.Select(structure => ((object)structure.Structure, structure.Items));

	/// <summary>
	/// Every structure placed and culled for <paramref name="camera"/>, each detail level picked under a focal length of
	/// 2^<paramref name="focalShift"/>; with <paramref name="flatRamps"/> each takes <c>WorldShades_ResetRamps</c>' ramps
	/// (<see cref="Es1Shading.FlatBlock"/>) rather than its distance band's.
	/// </summary>
	public void Update(Camera camera, int focalShift, bool flatRamps = false) {
		foreach (var structure in _structures) {
			structure.Update(camera, _scene.Shading, _detail.Setting.ObjectBias, focalShift, flatRamps);
		}
	}

	private void Add(GL gl, StructureItems items, Es1BaseShape shape) {
		if (!_shapes.TryGetValue(shape, out var pieces)) {
			pieces = Upload(gl, shape);
			_shapes[shape] = pieces;
		}

		uint? texture = shape.TextureBank is { } bank && _banks.TryGetValue(bank, out var uploaded) ? uploaded.Handle : null;
		items.Add(shape, pieces, texture);
	}

	/// <summary>A shape's pieces, one per node, cell and BSP child of its geometry.</summary>
	private IReadOnlyList<Es1MechPiece> Upload(GL gl, Es1BaseShape shape) {
		if (shape.Geometry is not { } geometry) {
			return [];
		}

		TextureAtlas? atlas = null;
		if (shape.TextureBank is { } bank) {
			if (_banks.TryGetValue(bank, out var uploaded)) {
				atlas = uploaded.Atlas;
			} else if (_scene.Palette != null && TextureAtlas.Build(bank, _scene.Palette) is { } built) {
				atlas = built;
				var texture = built.Upload(gl, indexed: true);
				_owned.Add(texture);
				_banks[bank] = (atlas, texture.Handle);
			}
		}

		return Es1MechModels.UploadPieces(gl, DtsMeshBuilder.BuildSegments(geometry, atlas, _scene.Shading.Surfaces), _owned);
	}

	public void Dispose() {
		foreach (var owned in _owned) {
			owned.Dispose();
		}
	}

	/// <summary>One structure's items, a piece each of every shape it can hold, only the held shape's shown.</summary>
	private sealed class StructureItems(Es1Base structure) {
		private readonly List<(SceneItem Item, Es1MechPiece Piece, Es1BaseShape Shape)> _pieces = new();
		private readonly Dictionary<(Es1BaseShape, BspTree), BspDrawGroup> _groups = new();
		private Transform3 _frame;

		public Es1Base Structure { get; } = structure;

		public IEnumerable<SceneItem> Items => _pieces.Select(entry => entry.Item);

		public void Add(Es1BaseShape shape, IReadOnlyList<Es1MechPiece> pieces, uint? texture) {
			foreach (var piece in pieces) {
				var item = new SceneItem(piece.Mesh, Matrix4x4.Identity, texture);
				if (piece.Leaf is { } leaf) {
					if (!_groups.TryGetValue((shape, leaf.Tree), out var group)) {
						group = new BspDrawGroup(leaf.Tree, id => FrameToWorld(shape, id));
						_groups[(shape, leaf.Tree)] = group;
					}

					item.BspGroup = group;
					item.BspLeaf = leaf.Index;
					item.PaintLayer = piece.Layer;
				}

				_pieces.Add((item, piece, shape));
			}
		}

		public void Update(Camera camera, Es1Shading shading, int bias, int focalShift, bool flat) {
			_frame = Structure.Frame;
			int distance = camera.Position.ApproxDistanceTo(Structure.Position);
			// Tag 5's band and cull take the distance offset its slot +0x14 radius picks.
			int radius = Structure.ShapeRadius;
			bool drawn = Structure.Position.Z >= 0 && !shading.IsBeyondLastBand((byte)Es1Base.TypeTag, distance, radius)
				&& Structure.Group?.Action == null;
			int? block = flat ? shading.FlatBlock : shading.Block((byte)Es1Base.TypeTag, distance, radius);
			// The effect lights are picked by its position and slot +0x14's radius.
			var anchor = (Structure.Position, (int)Structure.ShapeRadius);
			var held = Structure.Shape;
			bool perspective = Es1MechModels.PerspectiveTextureMapping;
			foreach (var (item, piece, shape) in _pieces) {
				item.Transform = FrameToWorld(shape, piece.TransformId);
				item.TextureWalk = Es1MechModels.WalkOf(piece, perspective);
				item.RampBlock = block;
				item.LightAnchor = anchor;
				item.Visible = drawn && ReferenceEquals(shape, held) && OnScreen(shape, piece.Gate, camera, focalShift, bias);
			}
		}

		/// <summary>
		/// Whether a piece's cell is the cell its sequence stands on in the instance's cell array, which the wreck shares,
		/// and its <c>TSDetailPart</c>, if any, picks its level with <paramref name="bias"/> from the eye's distance in its
		/// node.
		/// </summary>
		private bool OnScreen(Es1BaseShape shape, CellGate gate, Camera camera, int focalShift, int bias) {
			if (gate.IsGated && gate.Frame != Structure.CellFrame(gate.Sequence)) {
				return false;
			}

			if (gate.Detail is not { } detail) {
				return true;
			}

			int eye = Es1ShapeDetail.EyeDistance(NodeFrame(shape, detail.TransformId), camera.Position);
			return detail.Select(eye, 1 << focalShift, bias) == gate.Level;
		}

		/// <summary>
		/// A node's transform in front of the structure's frame, in world units: posed by the instance's threads for the
		/// standing shape of a radar mast or gun tower, the frame alone for −1 and for a shape no thread poses.
		/// </summary>
		private Transform3 NodeFrame(Es1BaseShape shape, int transformId) =>
			transformId < 0 || Structure.Instance == null || !ReferenceEquals(shape, Structure.StandingShape)
				? _frame
				: Transform3.Concat(Structure.Instance.NodeTransform(transformId), _frame);

		private Matrix4x4 FrameToWorld(Es1BaseShape shape, int transformId) => WorldScale.ToRenderMatrix(NodeFrame(shape, transformId));
	}
}
