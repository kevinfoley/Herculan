using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Anim;
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
/// <param name="TextureWalk">Its textured faces' table for the row walk, or null — see <see cref="SceneItem.TextureWalk"/>.</param>
public sealed record Es1MechPiece(GpuMesh Mesh, int TransformId, CellGate Gate, BspLeaf? Leaf, int Layer,
	GpuTextureWalkTable? TextureWalk = null);

/// <summary>
/// The GPU side of the HERCs: per chassis the pieces of every root, textured from its bank; per fitted mount the pieces
/// of its own moved model, textured from <c>dba\wpntex</c>; and the flat shape set's roots its shadows draw. Which root
/// and which pieces a frame draws is <see cref="Es1WorldDraw"/>'s.
///
/// <para>A hardpoint's placeholder part is never built: <c>Mech_SpliceHardpointShapes</c> (<c>ES1DBSIM 00002654</c>) puts
/// a mount or a blank part in its slot every frame (docs/es1/simulation/object-classes.md#the-hardpoints--mech_splicehardpointshapes-00002654).</para>
/// </summary>
public sealed class Es1MechModels : IDisposable {
	private readonly Dictionary<short, IReadOnlyList<IReadOnlyList<Es1MechPiece>>> _pieces = new();
	private readonly Dictionary<Es1WeaponMount, IReadOnlyList<Es1MechPiece>> _mountPieces = new();
	private readonly List<IDisposable> _owned = new();

	/// <summary><c>dba\wpntex</c>'s atlas, which the weapon models and the thrown guns are built from; null when the install lacks it.</summary>
	public TextureAtlas? WeaponAtlas { get; private set; }

	/// <summary>The flat shape set's roots, by root, null where a root has no geometry.</summary>
	public IReadOnlyList<GpuMesh?> ShadowRoots { get; private set; } = [];

	/// <summary>
	/// Whether the HERCs' textured faces are mapped with perspective, through the mesh's own homogeneous mapping
	/// (<see cref="MeshVertex.UvWeight"/>), in place of retail's row walk in screen space
	/// (docs/es1/rendering/polygon-shading.md#bitmap-fills-brush-modes-5-and-7). Off: the pieces carry the walk's face
	/// tables. Read when the pieces are uploaded. The code path of a tweak that is not built
	/// (docs/herculan/plan-es1-first-mission.md#open).
	/// </summary>
	public static bool PerspectiveTextureMapping { get; set; }

	/// <summary>The texture handle of each chassis' bank, by type.</summary>
	public IReadOnlyDictionary<short, uint> Textures => _textures;

	private readonly Dictionary<short, uint> _textures = new();

	/// <summary>The texture handle of <c>dba\wpntex</c>, which <c>WeaponMount_ShapeForMountingCode</c> gives every weapon model; null when the install lacks it.</summary>
	public uint? WeaponTexture { get; private set; }

	/// <summary>Uploads the pieces of every type in <paramref name="types"/> and the flat set the theater's world file selects.</summary>
	public static Es1MechModels Upload(GL gl, GameContent content, IEnumerable<Es1MechType> types, Es1ZoneScene scene) {
		var models = new Es1MechModels();
		foreach (var type in types) {
			models.UploadType(gl, type, scene);
		}

		// Weapons_LoadResourceTables (0000bf48) loads dba\wpntex while g_LowMemory is 0 (docs/es1/formats/weapons-dat-sim.md#the-load).
		if (scene.Palette != null && content.Read("dba", "WPNTEX.DBA") is { } wpntex
				&& new DynamixBitmapArrayTransformer().Parse(wpntex) is { } weaponBank
				&& TextureAtlas.Build(weaponBank, scene.Palette) is { } weaponAtlas) {
			models.WeaponAtlas = weaponAtlas;
			var texture = weaponAtlas.Upload(gl, indexed: true);
			models._owned.Add(texture);
			models.WeaponTexture = texture.Handle;
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

	/// <summary>The pieces of each of <paramref name="type"/>'s roots, by root; empty for a type that did not upload.</summary>
	public IReadOnlyList<IReadOnlyList<Es1MechPiece>> PiecesOf(short type) => _pieces.GetValueOrDefault(type) ?? [];

	/// <summary>
	/// The pieces of <paramref name="mount"/>'s model, built and uploaded on the first call; empty for a mount with no
	/// model. Each mount's model is its own moved copy, so its pieces are its own.
	/// </summary>
	public IReadOnlyList<Es1MechPiece> PiecesOfMount(GL gl, Es1WeaponMount mount, Es1ZoneScene scene) {
		if (_mountPieces.TryGetValue(mount, out var known)) {
			return known;
		}

		var pieces = mount.Model != null ? Upload(gl, DtsMeshBuilder.BuildSegments(mount.Model, WeaponAtlas, scene.Shading.Surfaces)) : [];
		_mountPieces[mount] = pieces;
		return pieces;
	}

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

		// A crude root's transform ids are root 0's once renumbered, so its rest pose is root 0's animation list's.
		var hidden = DtsMeshBuilder.AttachmentPartIds(type.GunLayout);
		var rootPoses = ShapeAnimation.FirstAnimList(type.Roots[0]);
		_pieces[type.Type] = type.Roots.Select((root, index) => Upload(gl,
			DtsMeshBuilder.BuildSegments(root, atlas, scene.Shading.Surfaces, hidden, index > 0 ? rootPoses : null))).ToArray();
	}

	private IReadOnlyList<Es1MechPiece> Upload(GL gl, IEnumerable<MeshSegment> segments) {
		var pieces = new List<Es1MechPiece>();
		foreach (var segment in segments) {
			if (segment.Vertices.Length == 0) {
				continue;
			}

			var mesh = new GpuMesh(gl, segment.Vertices, segment.TriangleVertexCount, segment.PointVertexCount);
			_owned.Add(mesh);

			GpuTextureWalkTable? walk = null;
			if (!PerspectiveTextureMapping && segment.WalkFaces is { } faces) {
				walk = new GpuTextureWalkTable(gl, TextureWalkTable.Pack(faces));
				_owned.Add(walk);
			}

			pieces.Add(new Es1MechPiece(mesh, segment.TransformId, segment.Gate, segment.Leaf, segment.Layer, walk));
		}

		return pieces;
	}

	public void Dispose() {
		foreach (var owned in _owned) {
			owned.Dispose();
		}
	}
}
