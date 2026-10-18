using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Es1.Scene;
using Herculan.Es1.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Es1.Render;

/// <summary>One uploaded piece of a HERC's shape: the geometry one node places, on one cell, in one BSP child.</summary>
/// <param name="Mesh">The geometry, in the node's own space.</param>
/// <param name="TransformId">The node, -1 for geometry no node places.</param>
/// <param name="Gate">The cell it stands on.</param>
/// <param name="Leaf">The <c>TSBSPPart</c> child it is, or null.</param>
/// <param name="Layer">Its paint layer in that child.</param>
public sealed record Es1MechPiece(GpuMesh Mesh, int TransformId, CellGate Gate, BspLeaf? Leaf, int Layer);

/// <summary>
/// The GPU side of the HERCs: per chassis the pieces of the root it is drawn as, textured from its bank, and the flat
/// shape set's roots its shadows draw.
///
/// <para>PLACEHOLDER: every HERC draws its root 0 at any distance. <c>Shape_DrawAtDetailLevel</c> (<c>ES1DBSIM 00002b30</c>)
/// picks a root per frame by its projected size, but the scale and bias it reads are set by <c>00057c08</c> from tables
/// indexed by the preferences' option 6 (unread, docs/es1/simulation/object-classes.md#open), and the crude roots need
/// <c>MechType_RemapDetailRootTransforms</c>, whose handlers are unread (see <see cref="Es1MechType"/>).</para>
///
/// <para>A hardpoint's placeholder part is never drawn: <c>Mech_SpliceHardpointShapes</c> (<c>00002654</c>) puts a mount
/// or a blank part in its slot every frame. PLACEHOLDER: the mounts' weapon models are not drawn until <c>0000a308</c>,
/// which moves each model's points to its mount point, is read (docs/es1/simulation/weapon-mount-classes.md#open); a blank
/// part draws nothing in retail too.</para>
/// </summary>
public sealed class Es1MechModels : IDisposable {
	private readonly Dictionary<short, IReadOnlyList<Es1MechPiece>> _pieces = new();
	private readonly List<IDisposable> _owned = new();

	/// <summary>The flat shape set's roots, by root, null where a root has no geometry.</summary>
	public IReadOnlyList<GpuMesh?> ShadowRoots { get; private set; } = [];

	/// <summary>
	/// The texture handle of each chassis' bank, by type. PLACEHOLDER: the renderer maps these with perspective, where
	/// retail walks each face by columns in screen space (docs/es1/rendering/polygon-shading.md#bitmap-fills-brush-modes-5-and-7);
	/// step 3b ports the walk and keeps the perspective mapping as a tweak's code path (docs/herculan/plan-es1-first-mission.md#decisions).
	/// </summary>
	public IReadOnlyDictionary<short, uint> Textures => _textures;

	private readonly Dictionary<short, uint> _textures = new();

	/// <summary>Uploads the pieces of every type in <paramref name="types"/> and the flat set the theater's world file selects.</summary>
	public static Es1MechModels Upload(GL gl, GameContent content, IEnumerable<Es1MechType> types, Es1ZoneScene scene) {
		var models = new Es1MechModels();
		foreach (var type in types) {
			models.UploadType(gl, type, scene);
		}

		// FlatObj_LoadResources (00006cf8): dts\flat for a selector of 0, dts\flat2 otherwise.
		string set = (scene.World?.FlatSetSelector ?? 0) == 0 ? "FLAT" : "FLAT2";
		if (content.Read("dts", set + ".DTS") is { } bytes && new DTSModelTransformer().Parse(bytes) is DynamixThreeSpaceModel { Roots: { } roots }) {
			models.ShadowRoots = roots.Select(root => {
				var build = DtsMeshBuilder.BuildRoot(root, atlas: null, scene.Shading.Surfaces);
				if (build.Vertices.Length == 0) {
					return null;
				}

				var mesh = new GpuMesh(gl, build.Vertices, build.TriangleVertexCount, build.PointVertexCount);
				models._owned.Add(mesh);
				return mesh;
			}).ToArray();
		}

		return models;
	}

	/// <summary>The pieces of <paramref name="type"/>'s drawn root, empty for a type that did not upload.</summary>
	public IReadOnlyList<Es1MechPiece> PiecesOf(short type) => _pieces.GetValueOrDefault(type) ?? [];

	private void UploadType(GL gl, Es1MechType type, Es1ZoneScene scene) {
		if (_pieces.ContainsKey(type.Type) || type.Roots.Count == 0) {
			return;
		}

		TextureAtlas? atlas = type.TextureBank != null && scene.Palette != null
			? TextureAtlas.Build(type.TextureBank, scene.Palette)
			: null;
		if (atlas != null) {
			var texture = atlas.Upload(gl, indexed: true);
			_owned.Add(texture);
			_textures[type.Type] = texture.Handle;
		}

		var hidden = DtsMeshBuilder.AttachmentPartIds(type.GunLayout);
		var segments = DtsMeshBuilder.BuildSegments(type.Roots[0], atlas, scene.Shading.Surfaces, hidden);
		var pieces = new List<Es1MechPiece>();
		foreach (var segment in segments) {
			if (segment.Vertices.Length == 0) {
				continue;
			}

			var mesh = new GpuMesh(gl, segment.Vertices, segment.TriangleVertexCount, segment.PointVertexCount);
			_owned.Add(mesh);
			pieces.Add(new Es1MechPiece(mesh, segment.TransformId, segment.Gate, segment.Leaf, segment.Layer));
		}

		_pieces[type.Type] = pieces;
	}

	public void Dispose() {
		foreach (var owned in _owned) {
			owned.Dispose();
		}
	}
}
