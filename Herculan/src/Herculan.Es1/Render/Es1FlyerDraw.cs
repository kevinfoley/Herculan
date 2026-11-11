using System.Numerics;
using Herculan.Engine.Anim;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Scene;
using Herculan.Es1.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>
/// The flyers' share of the frame: each flyer of <c>g_FlyerPool</c> drawn as <c>Flyer_Draw</c> (<c>ES1DBSIM 0001ac34</c>)
/// draws it, <c>TSDetailPart_SetBias</c>(<c>g_TSDetailBiasForFlyers</c>), <c>Shape_DrawAtDetailLevel</c>, then the bias
/// back to 0 (docs/es1/simulation/object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08). A
/// flyer's instance is static and has no LOD table, so <c>Shape_DrawAtDetailLevel</c> draws its type's shape whole, every
/// node at rest, at the flyer's frame, each <c>TSDetailPart</c> at the level <see cref="Es1DetailSetting.ObjectBias"/>
/// picks; its textured faces take <c>dba\enemy</c> and are filled by the row walk as a HERC's are (plan decision 8). It
/// is listed as <c>ObjList_RebuildFrame</c> lists the live objects (docs/es1/rendering/polygon-shading.md#the-object-list):
/// not while its group's action holds it out, nor with its Z negative or past tag 6's last distance band; and it shades
/// in tag 6's band.
/// </summary>
public sealed class Es1FlyerDraw : IDisposable {
	private readonly Es1ZoneScene _scene;
	private readonly Es1AppliedDetail _detail;
	private readonly List<IDisposable> _owned = new();
	private readonly List<FlyerItems> _flyers = new();

	/// <summary>Uploads each flyer type's shape and lists an item set for each flyer of <paramref name="flyers"/>.</summary>
	/// <param name="detail">The detail setting the flyers are drawn at, read on every <see cref="Update"/>.</param>
	public Es1FlyerDraw(GL gl, Es1ZoneScene scene, IEnumerable<Es1Flyer> flyers, Es1AppliedDetail detail) {
		_scene = scene;
		_detail = detail;
		var types = new Dictionary<Es1FlyerType, TypePieces>();
		foreach (var flyer in flyers) {
			if (!types.TryGetValue(flyer.Type, out var pieces)) {
				pieces = Upload(gl, flyer.Type);
				types[flyer.Type] = pieces;
			}

			_flyers.Add(new FlyerItems(flyer, pieces));
		}
	}

	/// <summary>The flyers' items, in the order the live object list holds the flyers.</summary>
	public IEnumerable<SceneItem> Items => _flyers.SelectMany(flyer => flyer.Items);

	/// <summary>Each flyer with its items, for the pixel pick.</summary>
	public IEnumerable<(object Object, IEnumerable<SceneItem> Items)> FrameObjects =>
		_flyers.Select(flyer => ((object)flyer.Flyer, flyer.Items));

	/// <summary>
	/// Every flyer placed and culled for <paramref name="camera"/>, each detail level picked under a focal length of
	/// 2^<paramref name="focalShift"/>; with <paramref name="flatRamps"/> each takes <c>WorldShades_ResetRamps</c>' ramps
	/// (<see cref="Es1Shading.FlatBlock"/>) rather than its distance band's.
	/// </summary>
	public void Update(Camera camera, int focalShift, bool flatRamps = false) {
		foreach (var flyer in _flyers) {
			flyer.Update(camera, _scene.Shading, _detail.Setting.ObjectBias, focalShift, flatRamps);
		}
	}

	/// <summary>A type's pieces, one per node, cell and BSP child of its shape, and its nodes at rest.</summary>
	private sealed record TypePieces(IReadOnlyList<Es1MechPiece> Pieces, ShapeInstance? Rest, uint? Texture);

	private TypePieces Upload(GL gl, Es1FlyerType type) {
		TextureAtlas? atlas = type.TextureBank != null && _scene.Palette != null
			? TextureAtlas.Build(type.TextureBank, _scene.Palette)
			: null;
		uint? texture = null;
		if (atlas != null) {
			var uploaded = atlas.Upload(gl, indexed: true);
			_owned.Add(uploaded);
			texture = uploaded.Handle;
		}

		var pieces = Es1MechModels.UploadPieces(gl, DtsMeshBuilder.BuildSegments(type.Shape, atlas, _scene.Shading.Surfaces), _owned);
		return new TypePieces(pieces, type.Animation != null ? new ShapeInstance(type.Animation) : null, texture);
	}

	public void Dispose() {
		foreach (var owned in _owned) {
			owned.Dispose();
		}
	}

	/// <summary>One flyer's items, a piece each, posed through its type's nodes at rest.</summary>
	private sealed class FlyerItems {
		private readonly TypePieces _type;
		private readonly List<(SceneItem Item, Es1MechPiece Piece)> _pieces = new();
		private readonly Dictionary<BspTree, BspDrawGroup> _groups = new();
		private Transform3 _frame;

		public FlyerItems(Es1Flyer flyer, TypePieces type) {
			Flyer = flyer;
			_type = type;
			foreach (var piece in type.Pieces) {
				var item = new SceneItem(piece.Mesh, Matrix4x4.Identity, type.Texture);
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

		public Es1Flyer Flyer { get; }

		public IEnumerable<SceneItem> Items => _pieces.Select(entry => entry.Item);

		public void Update(Camera camera, Es1Shading shading, int bias, int focalShift, bool flat) {
			_frame = Flyer.Frame;
			int distance = camera.Position.ApproxDistanceTo(Flyer.Position);
			bool drawn = Flyer.Position.Z >= 0 && !shading.IsBeyondLastBand((byte)Es1Flyer.TypeTag, distance)
				&& Flyer.Group?.Action == null;
			int? block = flat ? shading.FlatBlock : shading.Block((byte)Es1Flyer.TypeTag, distance);
			// The effect lights are picked by its position and slot +0x14's radius.
			var anchor = (Flyer.Position, (int)Flyer.ShapeRadius);
			bool perspective = Es1MechModels.PerspectiveTextureMapping;
			foreach (var (item, piece) in _pieces) {
				item.Transform = FrameToWorld(piece.TransformId);
				item.TextureWalk = Es1MechModels.WalkOf(piece, perspective);
				item.RampBlock = block;
				item.LightAnchor = anchor;
				item.Visible = drawn && OnScreen(piece.Gate, camera, focalShift, bias);
			}
		}

		/// <summary>
		/// Whether a piece's cell is cell 0, the one each sequence of the static instance shows, and its
		/// <c>TSDetailPart</c>, if any, picks its level with <paramref name="bias"/> from the eye's distance in its node.
		/// </summary>
		private bool OnScreen(CellGate gate, Camera camera, int focalShift, int bias) {
			if (gate.IsGated && gate.Frame != 0) {
				return false;
			}

			if (gate.Detail is not { } detail) {
				return true;
			}

			int eye = Es1ShapeDetail.EyeDistance(NodeFrame(detail.TransformId), camera.Position);
			return Es1ShapeDetail.SelectLevel(detail, eye, focalShift, bias) == gate.Level;
		}

		/// <summary>A node's rest transform in front of the flyer's frame, in world units; the frame alone for -1.</summary>
		private Transform3 NodeFrame(int transformId) =>
			transformId < 0 || _type.Rest == null ? _frame : Transform3.Concat(_type.Rest.NodeTransform(transformId), _frame);

		/// <summary>A node's rest transform in front of the flyer's frame, in render space; the frame alone for -1.</summary>
		private Matrix4x4 FrameToWorld(int transformId) => WorldScale.ToRenderMatrix(NodeFrame(transformId));
	}
}
