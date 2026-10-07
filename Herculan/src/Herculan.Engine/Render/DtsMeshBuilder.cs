using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Bsp;
using HercWorks.Core.Data.File.Dts.Part;
using HercWorks.Core.Data.File.Dts.Poly;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Gl;

namespace Herculan.Engine.Render;

/// <summary>
/// Which cell of which cell-animation sequence a piece of geometry belongs to, and so whether it is
/// on screen: a <see cref="TSCellAnimPart"/> draws exactly one of its children, the one its
/// sequence's entry in the shape instance's cell-frame array names
/// (<see cref="Sim.ShapeCellFrames"/>).
///
/// <para><see cref="Ungated"/> is geometry under no cell-animation part at all, which is always
/// drawn. No retail shape nests one cell-animation part inside another — checked across every
/// <c>.DTS</c> a mission loads and both <c>.DGS</c> libraries — so one gate per piece of geometry is
/// the whole of the condition rather than the innermost of a chain.</para>
/// </summary>
/// <param name="Sequence">The part's <see cref="TSCellAnimPart.AnimSequence"/>, or -1 for ungated.</param>
/// <param name="Frame">Which child of that part, or -1 for ungated.</param>
/// <param name="Detail">
/// The <see cref="TSDetailPart"/> this piece is one level of, or null for geometry under none — the
/// second thing that decides whether a piece is drawn, and per frame rather than per damage state.
/// Only <see cref="DtsMeshBuilder.BuildCells"/>, <see cref="DtsMeshBuilder.BuildSegments"/> and
/// <see cref="DtsMeshBuilder.BuildDetailLevels"/> build every level; the other builds take the finest
/// and leave this null. No retail shape nests a detail
/// part inside another or inside a cell-animation part, though cells inside a level are common, so
/// one detail gate and one cell gate are the whole of the condition.
/// </param>
/// <param name="Level">Which level of <paramref name="Detail"/>, or -1.</param>
public readonly record struct CellGate(short Sequence, short Frame, PartDetail? Detail = null,
		short Level = -1) {
	/// <summary>Geometry no cell-animation part encloses — always drawn.</summary>
	public static CellGate Ungated { get; } = new(-1, -1);

	/// <summary>Whether this piece is drawn only while its sequence stands on its frame.</summary>
	public bool IsGated => Sequence >= 0;

	/// <summary>Whether this piece is drawn only while its detail part selects its level.</summary>
	public bool IsDetailGated => Detail != null;

	/// <summary>Whether <paramref name="frames"/> puts this piece on screen.</summary>
	public bool VisibleIn(Sim.ShapeCellFrames? frames) =>
		!IsGated || (frames?[Sequence] ?? 0) == Frame;
}

/// <summary>
/// One node's share of a shape's geometry: the triangles of every group that hangs from a single
/// transform <i>and</i> stands on one cell of one animation sequence, in that node's own space
/// rather than the shape's.
///
/// <para>A segment is drawn with the node's posed transform in front of the object's own, so the
/// animation thread moving the node moves the geometry. Its vertices are therefore <i>not</i>
/// interchangeable with <see cref="DtsMeshBuilder.BuildRoot"/>'s flat mesh, which has the rest pose
/// already baked into it.</para>
/// </summary>
/// <param name="TransformId">The node, in the id space <c>ShapeInstance.NodeTransform</c> takes.
/// -1 for geometry no node places, which is drawn at the shape's origin.</param>
/// <param name="Gate">The cell this segment stands on — see <see cref="CellGate"/>.</param>
/// <param name="Vertices">Triangles, outline edges then points in the node's own space, ready to
/// upload — see <see cref="MeshBuild"/>.</param>
/// <param name="TriangleVertexCount">Where the outline edges start — see <see cref="MeshBuild"/>.</param>
/// <param name="PointVertexCount">How many trailing vertices are points — see <see cref="MeshBuild"/>.</param>
/// <param name="Leaf">The <see cref="TSBSPPart"/> child this segment is, or null — see <see cref="BspLeaf"/>.</param>
public readonly record struct MeshSegment(int TransformId, CellGate Gate, MeshVertex[] Vertices,
	int TriangleVertexCount, int PointVertexCount = 0, BspLeaf? Leaf = null);

/// <summary>
/// One cell's share of a shape's geometry, at the rest pose <see cref="DtsMeshBuilder.BuildRoot"/>
/// bakes — what a shape that has cells the simulation drives but no nodes to pose has to be split
/// into. A structure and a flyer are both drawn this way; a machine, which does animate, is split by
/// node as well and uses <see cref="MeshSegment"/> instead.
/// </summary>
/// <param name="Gate">The cell this piece stands on — see <see cref="CellGate"/>.</param>
/// <param name="Vertices">Triangles, outline edges then points, placed, ready to upload.</param>
/// <param name="TriangleVertexCount">Where the outline edges start — see <see cref="MeshBuild"/>.</param>
/// <param name="PointVertexCount">How many trailing vertices are points — see <see cref="MeshBuild"/>.</param>
/// <param name="Leaf">The <see cref="TSBSPPart"/> child this piece is, or null — see <see cref="BspLeaf"/>.</param>
public readonly record struct MeshCell(CellGate Gate, MeshVertex[] Vertices, int TriangleVertexCount,
	int PointVertexCount = 0, BspLeaf? Leaf = null);

/// <summary>
/// A built mesh: filled triangles first, then the outline edges that are drawn over them as lines,
/// then single points, in one array so a single vertex buffer carries all three.
///
/// <para>The outline is not decoration. <c>TSSolidPoly_Render</c> (<c>00474db4</c>) resolves
/// <i>two</i> colours for every flat solid face — the side's fill and line entries, both through the
/// theater ramp at the same fixed shade — and hands both to the polygon fill
/// <c>PolyFill_FillThenOutline</c> (<c>0048d518</c>), which fills in the first and then, whenever
/// the two resolve differently, re-draws the same polygon's edge loop in the second. That second
/// pass is the line range. See <see cref="DtsMeshBuilder"/>'s <c>ResolveSolidColors</c>.
/// <c>TSShadedPoly_Render</c> (<c>0047542c</c>) does the same with its line entry run through the
/// shaded chain at the face's light, which the range carries as a ramp for the shader to resolve —
/// see <see cref="MeshVertex.OutlineFillRamp"/>.</para>
///
/// <para>The points are one-vertex polys, which the same fill draws as a single pixel
/// (docs/retail/formats/dts-texture-binding.md, "<c>TSSolidPoly</c> — palette index, unlit, fill plus
/// outline").</para>
/// </summary>
/// <param name="Vertices">Triangle corners in <c>[0, TriangleVertexCount)</c>, line-segment
/// endpoint pairs after it, and the last <paramref name="PointVertexCount"/> points.</param>
/// <param name="TriangleVertexCount">Always a multiple of three; the line range between it and the
/// points is a multiple of two.</param>
/// <param name="PointVertexCount">How many trailing vertices are points.</param>
public readonly record struct MeshBuild(MeshVertex[] Vertices, int TriangleVertexCount,
		int PointVertexCount = 0) {
	public static MeshBuild Empty { get; } = new(Array.Empty<MeshVertex>(), 0);

	/// <summary>How many vertices belong to the outline pass.</summary>
	public int OutlineVertexCount => Vertices.Length - TriangleVertexCount - PointVertexCount;
}

/// <summary>
/// Flattens a parsed DTS model tree into triangles in render space, carrying the texture, colour and
/// shading each poly type resolves to.
///
/// <para>This is the engine's counterpart to <c>HercWorks.UI.DtsGeometryBuilder</c>, and it is a
/// separate type on purpose rather than shared code: that one produces GDI+ <c>Color</c> values for
/// a software rasterizer inside a Windows-only WinForms tool, while this produces GPU vertices and
/// must stay clear of System.Drawing so the engine keeps building for Linux/macOS (see
/// docs/herculan/planning.md's target-platform decision). The tree-walking rules are the same but for
/// one, the <see cref="TSBSPPart"/> walk (<see cref="ReachableLeaves"/>, <see cref="BspTree"/>),
/// which only this side follows; each rule below is annotated with what the UI builder established —
/// worth keeping the two in sync if either side changes.</para>
///
/// <para>Every poly resolves a surface pair <b>per side</b>, and the side the eye is on picks one per
/// frame in the shader — see <see cref="ResolveSide"/> and <see cref="MeshVertex.Side"/>. Below,
/// "the value" is the drawn side's fill entry in <c>Surfaces[ColorIndexId / 4]</c>.</para>
///
/// <para><see cref="TSTexture4Poly"/> polys resolve to real texture through the chain established in
/// docs/retail/formats/dts-texture-binding.md: the value is a frame index into the mesh's bound
/// <c>.DBA</c> bank, and the four UV corners are the frame's own rect.</para>
///
/// <para><b>The untextured poly types are three separate mechanisms</b>, distinguished by what their
/// value means:</para>
/// <list type="bullet">
/// <item><see cref="TSShadedPoly"/> and <see cref="TSGouraudPoly"/> — a <b>ramp number</b> into the
/// theater palette's shade-ramp table, with the face's light level picking a step along it. The two
/// spend it through different chains; see <see cref="SurfaceShading"/>. Nearly every surface of a
/// HERC or a building is one of these. Resolved by <see cref="ResolveShadeRamp"/>, and the lookup
/// happens per fragment (<see cref="MeshVertex.ShadeRamp"/>, <see cref="SurfaceRampTable"/>) because
/// the shade depends on the face's world normal and one mesh serves every instance of a type.</item>
/// <item>Plain <see cref="TSSolidPoly"/> — a <b>palette index</b>, through the theater ramp at a
/// fixed shade, never lit. <see cref="ResolveSolidColors"/>.</item>
/// </list>
///
/// <para>Pass a <see cref="TextureAtlas"/> and a <see cref="SurfaceShading"/> to resolve all three.
/// Without them, surfaces fall back to <see cref="FallbackColor"/>.</para>
/// </summary>
public static partial class DtsMeshBuilder {
	private readonly struct Triangle {
		/// <inheritdoc cref="Gl.MeshVertex.SolidPaletteIndex"/>
		public int SolidPaletteIndex { get; }

		public Vector3 A { get; }
		public Vector3 B { get; }
		public Vector3 C { get; }

		/// <summary>The same corners before the node's own placement — see <see cref="MeshSegment"/>.</summary>
		public Vector3 LocalA { get; }

		/// <inheritdoc cref="LocalA" />
		public Vector3 LocalB { get; }

		/// <inheritdoc cref="LocalA" />
		public Vector3 LocalC { get; }

		/// <summary>The transform id of the node this triangle's group hangs from, or -1.</summary>
		public int TransformId { get; }

		/// <summary>The cell-animation cell this triangle stands on — see <see cref="CellGate"/>.</summary>
		public CellGate Gate { get; }

		/// <summary>The <see cref="TSBSPPart"/> child this triangle was built under, or null.</summary>
		public BspLeaf? Leaf { get; init; }

		public Vector3 Color { get; }
		public Vector2 UvA { get; }
		public Vector2 UvB { get; }
		public Vector2 UvC { get; }

		/// <summary>
		/// The three corners' UV weights, or all zero when <see cref="UvA"/>..<see cref="UvC"/> are
		/// plain coordinates — see <see cref="MeshVertex.UvWeight"/>.
		/// </summary>
		public (float A, float B, float C) UvWeights { get; }

		/// <summary>Which twin of a coincident pair wins — see <see cref="DropCoincidentTwins"/>.</summary>
		public int Rank { get; }

		/// <summary>
		/// Which source poly this triangle was fanned out of, unique across the whole build. Only
		/// <see cref="OutlineEdge"/> reads it: an outline belongs to a poly, so it has to disappear
		/// with that poly when <see cref="DropCoincidentTwins"/> discards it.
		/// </summary>
		public int PolyId { get; }

		/// <summary>Whether <see cref="Color"/> is final — see <see cref="MeshVertex.Unlit"/>.</summary>
		public bool Unlit { get; }

		/// <summary>The material ramp this face's surface names, or -1 — see <see cref="MeshVertex.ShadeRamp"/>.</summary>
		public int ShadeRamp { get; }

		/// <summary>
		/// The shape's own per-corner normals, for a <see cref="TSGouraudPoly"/> — see
		/// <see cref="ResolveVertexNormals"/>. Null for every other poly, whose three corners share
		/// <see cref="FaceNormal"/>.
		/// </summary>
		public (Vector3 A, Vector3 B, Vector3 C)? VertexNormals { get; }

		/// <summary>
		/// The source poly's own stored normal — see <see cref="ResolveFaceNormal"/>. Null when the
		/// poly's normal index does not resolve, and <see cref="EmitTriangle"/> falls back to the
		/// winding.
		/// </summary>
		public Vector3? FaceNormal { get; }

		/// <summary>The face's front/back decision point, both spaces — see <see cref="MeshVertex.FaceCenter"/>.</summary>
		public PolyFace Face { get; }

		/// <summary>Which side of the poly this copy draws — see <see cref="MeshVertex.Side"/>.</summary>
		public int Side { get; init; }

		/// <summary>
		/// Whether this copy is lit as the poly's back while the poly faces the eye — see
		/// <see cref="SideLook.LitAsBack"/>.
		/// </summary>
		public bool LitAsBack { get; }

		public Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 color, int rank, int polyId,
				Vector3 localA, Vector3 localB, Vector3 localC, int transformId, CellGate gate,
				PolyFace face, int side, bool litAsBack = false,
				Vector2 uvA = default, Vector2 uvB = default, Vector2 uvC = default,
				bool unlit = false, int shadeRamp = -1,
				(Vector3 A, Vector3 B, Vector3 C)? vertexNormals = null,
				Vector3? faceNormal = null,
				(float A, float B, float C) uvWeights = default,
				int solidPaletteIndex = -1) {
			Face = face;
			Side = side;
			LitAsBack = litAsBack;
			Unlit = unlit;
			ShadeRamp = shadeRamp;
			SolidPaletteIndex = solidPaletteIndex;
			VertexNormals = vertexNormals;
			FaceNormal = faceNormal;
			PolyId = polyId;
			A = a;
			B = b;
			C = c;
			LocalA = localA;
			LocalB = localB;
			LocalC = localC;
			TransformId = transformId;
			Gate = gate;
			Color = color;
			Rank = rank;
			UvA = uvA;
			UvB = uvB;
			UvC = uvC;
			UvWeights = uvWeights;
		}
	}

	/// <summary>
	/// What a poly's front/back decision is made from, carried by every primitive the poly emits:
	/// its stored normal (null when the index does not resolve) and its stored centre in both spaces
	/// a <see cref="Triangle"/> is kept in. See <see cref="MeshVertex.FaceCenter"/>.
	/// </summary>
	private readonly record struct PolyFace(Vector3? Normal, Vector3 Center, Vector3 LocalCenter);

	/// <summary>
	/// One edge of a flat solid poly's outline pass, in the same two spaces a <see cref="Triangle"/>
	/// is kept in — see <see cref="MeshBuild"/>. A one-vertex poly's single point is one of these too,
	/// with both ends the same vertex.
	/// </summary>
	private readonly struct OutlineEdge {
		/// <inheritdoc cref="Gl.MeshVertex.SolidPaletteIndex"/>
		public int SolidPaletteIndex { get; }

		/// <inheritdoc cref="Triangle.Face" />
		public PolyFace Face { get; }

		/// <inheritdoc cref="Triangle.Side" />
		public int Side { get; init; }

		public OutlineEdge(Vector3 a, Vector3 b, Vector3 localA, Vector3 localB,
				Vector3 color, int transformId, CellGate gate, int polyId, PolyFace face, int side,
				bool standalone = false, int solidPaletteIndex = -1, int shadeRamp = -1,
				int outlineFillRamp = -1) {
			Face = face;
			Side = side;
			SolidPaletteIndex = solidPaletteIndex;
			ShadeRamp = shadeRamp;
			OutlineFillRamp = outlineFillRamp;
			A = a;
			B = b;
			LocalA = localA;
			LocalB = localB;
			Color = color;
			TransformId = transformId;
			Gate = gate;
			PolyId = polyId;
			Standalone = standalone;
		}

		/// <summary>
		/// Whether this edge is a <b>line poly</b> or a <b>point poly</b> — a two- or one-vertex
		/// <see cref="TSSolidPoly"/>, which is the whole of what its poly draws — rather than the
		/// outline pass over a filled face. A standalone edge has no triangle to outlive, so
		/// <see cref="SurvivingOutlines"/> keeps it unconditionally.
		/// </summary>
		public bool Standalone { get; }

		public Vector3 A { get; }
		public Vector3 B { get; }
		public Vector3 LocalA { get; }
		public Vector3 LocalB { get; }

		/// <summary>
		/// The side's ramped line colour, already final for a solid poly's outline, which is never lit.
		/// A shaded poly's outline is resolved per fragment from <see cref="ShadeRamp"/> instead.
		/// </summary>
		public Vector3 Color { get; }

		/// <summary>
		/// A <c>TSShadedPoly</c> outline's line ramp, lit like its fill, or -1 for an unlit edge — see
		/// <see cref="MeshVertex.ShadeRamp"/>.
		/// </summary>
		public int ShadeRamp { get; }

		/// <inheritdoc cref="MeshVertex.OutlineFillRamp"/>
		public int OutlineFillRamp { get; }

		public int TransformId { get; }

		/// <inheritdoc cref="Triangle.Gate" />
		public CellGate Gate { get; }

		/// <inheritdoc cref="Triangle.Leaf" />
		public BspLeaf? Leaf { get; init; }

		/// <summary>The poly this edge belongs to — see <see cref="Triangle.PolyId"/>.</summary>
		public int PolyId { get; }
	}

	/// <summary>
	/// What the tree walk fills in: the two passes the original draws every flat solid face in, plus
	/// the counter that ties one to the other.
	/// </summary>
	private sealed class Collector {
		public List<Triangle> Triangles { get; } = new();

		public List<OutlineEdge> Outlines { get; } = new();

		/// <summary>One-vertex polys, each a single <see cref="OutlineEdge"/> whose two ends coincide.</summary>
		public List<OutlineEdge> Points { get; } = new();

		/// <summary>
		/// Whether the walk descends into <i>every</i> cell of a <see cref="TSCellAnimPart"/>, tagging
		/// each with the gate it is drawn under, rather than picking the one cell
		/// <see cref="Collect"/>'s <c>cellFrame</c> names. Set for the shapes damage takes apart,
		/// where which cell is showing is per-object state the mesh cannot be built around.
		/// </summary>
		public bool AllCells { get; init; }

		/// <summary>
		/// Whether the walk descends into <i>every</i> level of a <see cref="TSDetailPart"/>, tagging
		/// each with the gate it is drawn under, rather than taking the finest. Set wherever
		/// <see cref="AllCells"/> is, and by <see cref="BuildDetailLevels"/> without it: the level is
		/// chosen per object per frame, from how far away the object is, so one mesh cannot be built
		/// around it.
		/// </summary>
		public bool AllDetailLevels { get; init; }

		/// <summary>
		/// The cell and detail level the walk is currently inside, pushed and restored around each
		/// cell-animation child and each detail level. <see cref="CellGate.Ungated"/> everywhere else.
		/// </summary>
		public CellGate Gate { get; set; } = CellGate.Ungated;

		/// <summary>
		/// The <see cref="TSBSPPart"/> child the walk is currently inside, pushed and restored around
		/// each one, or null outside every part.
		/// </summary>
		public BspLeaf? Leaf { get; set; }

		private int _nextPolyId;

		/// <summary>Claims the next <see cref="Triangle.PolyId"/>, once per source poly.</summary>
		public int NextPolyId() => _nextPolyId++;
	}

	/// <summary>
	/// How good a triangle is as the survivor of a coincident group, highest wins. The ordering is
	/// the whole point of <see cref="DropCoincidentTwins"/> and is documented there.
	/// </summary>
	private static class Ranks {
		/// <summary>A texture poly with no atlas frame behind it — a placeholder colour, worst option.</summary>
		public const int UnresolvedTexture = 0;

		/// <summary>An ordinary flat-shaded poly, carrying a real surface colour.</summary>
		public const int FlatShaded = 1;

		/// <summary>A texture poly resolved to real atlas pixels — what the original draws.</summary>
		public const int Textured = 2;
	}

	/// <summary>
	/// Builds every top-level root in the model into one mesh, each at its highest detail level.
	///
	/// <para>A DTS file's roots are fully independent objects — <c>SAMSON.DTS</c>'s roots are LOD
	/// variants of one mech, while <c>BASES_AN.DTS</c>'s are unrelated buildings bundled together —
	/// and nothing in the file distinguishes the two cases; that knowledge lives in the game engine.
	/// A caller that wants one specific object should use <see cref="BuildRoot"/> and pick. Merging
	/// all roots is right only when the file is known to hold a single object.</para>
	/// </summary>
	public static MeshBuild BuildAll(DynamixThreeSpaceModel model, TextureAtlas? atlas = null, SurfaceShading? shading = null) {
		var sink = new Collector();
		if (model.Roots != null) {
			foreach (var root in model.Roots) {
				Collect(root, null, sink, atlas, shading);
			}
		}
		return Emit(sink);
	}

	/// <summary>
	/// Builds one top-level root at its highest detail level, with every
	/// <see cref="TSCellAnimPart"/> under it showing cell <paramref name="cellFrame"/>.
	/// </summary>
	/// <param name="cellFrame">
	/// Which cell of the shape's flipbook to bake. Zero — the rest pose — for everything the engine
	/// draws statically; a launcher round builds one mesh per cell and picks between them as its own
	/// frame counter moves, see <see cref="CellFrameCount"/>.
	/// </param>
	/// <param name="hiddenPartIds">
	/// <see cref="TSBasePart.IdNumber"/>s to leave out of the mesh entirely — a machine's hardpoint
	/// attachment slots, from <see cref="AttachmentPartIds"/>. Null for every shape that has none.
	/// </param>
	public static MeshBuild BuildRoot(TSObject root, TextureAtlas? atlas = null,
			SurfaceShading? shading = null, int cellFrame = 0,
			IReadOnlySet<short>? hiddenPartIds = null) {
		var sink = new Collector();
		Collect(root, null, sink, atlas, shading, cellFrame, hiddenPartIds);
		return Emit(sink);
	}

	/// <summary>
	/// The <see cref="TSBasePart.IdNumber"/>s in a machine's own <c>.DTS</c> that are <b>hardpoint
	/// attachment slots</b>: the parts DBSIM overwrites every frame, and so never draws as the file
	/// ships them.
	///
	/// <para><b>The mechanism.</b> <c>MechType_InitOne</c> (<c>004201a8</c>) builds, per LOD root, a
	/// list of part slots — one per hardpoint — through <c>GunLayout_CollectHardpointBones</c> (<c>0040fc50</c>), which emits each
	/// <c>.GL</c> record's <see cref="GunLayout.HardpointEntry.BoneId"/> when its mounting code is
	/// under <see cref="Sim.WeaponMount.InvisibleMounting"/> and <c>-1</c> otherwise, and
	/// <c>MechType_BindHardpointSlots</c> (<c>0040304c</c>), which resolves each id to the address of the shape's part slot holding
	/// the part with that id. The mech's own draw (<c>Mech_Draw</c> (<c>004174c8</c>), mech vtable <c>+0</c>) then
	/// runs <c>Mech_SpliceHardpointShapes</c> (<c>004030d0</c>) before rendering anything, replacing each slot's contents with either
	/// the fitted mount's weapon shape or a blank record from <c>typeRec+0xec</c>, inheriting the
	/// placeholder's node transform and id. Empty or fitted, the shipped geometry is always
	/// overwritten.</para>
	///
	/// <para><b>Why the engine skips them instead of splicing.</b> The fitted case is already drawn,
	/// out of <c>MECHWPNS.DTS</c> at the mount's own frame — see
	/// <see cref="Scene.SceneModelLibrary.MechWeapon"/>. What was missing was the other half: an
	/// unspliced placeholder was being drawn as flat untextured geometry standing at every hardpoint,
	/// which retail shows on no machine.</para>
	///
	/// <para>Verified against all four retail chassis, where the ids are exactly the visible
	/// hardpoints' bones: SAMSON 7 (8, 9, 10, 11, 18, 66, 77), OUTLAW 3, APOCA 4 and PITBULL 1. The
	/// invisible mounting is excluded on its own merits — SAMSON's bone 5 carries a real torso part,
	/// and splicing it would delete the machine's middle.</para>
	///
	/// <para><b>Bone id 0 is not supported</b>, and no retail chassis uses it: the original resolves
	/// one slot per hardpoint, where matching on the id here would hide every part that carries the
	/// default id of zero.</para>
	/// </summary>
	public static IReadOnlySet<short> AttachmentPartIds(GunLayout? hardpoints) {
		var ids = new HashSet<short>();
		foreach (var hardpoint in hardpoints?.Hardpoints ?? Array.Empty<GunLayout.HardpointEntry>()) {
			if (hardpoint.MountingCode < Sim.WeaponMount.InvisibleMounting && hardpoint.BoneId != 0) {
				ids.Add(hardpoint.BoneId);
			}
		}

		return ids;
	}

	/// <summary>
	/// How many cells the shape's flipbook has — <c>TSShape.SequenceList[0]</c>, which is the
	/// per-sequence frame-count array the original mods its own counter by
	/// (<c>shape+0x20</c>, read by <c>Bullet_TickUpdate</c> and <c>Rocket_TickUpdate</c>).
	///
	/// <para>Sequence zero only: every retail <see cref="TSCellAnimPart"/> in a projectile shape
	/// carries <c>AnimSequence == 0</c>, and so does every <c>ROCKETS.DAT</c> record's own sequence
	/// field. A shape with no flipbook reports one frame, which is the shape itself.</para>
	/// </summary>
	public static int CellFrameCount(TSObject? root) => CellFrameCount(root, 0);

	/// <summary>
	/// The same array read at an arbitrary sequence, which is what a structure's idle flipbook needs:
	/// <c>Base_ThinkTick</c> steps the sequence its <c>BASES.DAT</c> record names
	/// (<see cref="World.BaseType.AnimCellSequence"/>) rather than sequence zero, and takes its
	/// modulus from that sequence's own entry.
	/// </summary>
	public static int CellFrameCount(TSObject? root, int sequence) =>
		CellFrameCount((root as TSShape)?.SequenceList, sequence);

	/// <summary>
	/// The same read straight off a frame-count array, for a shape whose list is not on a
	/// <see cref="TSShape"/> object — a <c>.DGS</c> record's, which is
	/// <see cref="HercWorks.Core.Data.File.Dgs.GridShape.SequenceList"/>.
	/// </summary>
	public static int CellFrameCount(short[]? sequences, int sequence) =>
		sequences != null && sequence >= 0 && sequence < sequences.Length && sequences[sequence] > 1
			? System.Math.Min((int)sequences[sequence], MaxCellFrames)
			: 1;

	/// <summary>Guard against a file claiming a flipbook longer than anything could reasonably hold.</summary>
	private const int MaxCellFrames = 64;

	/// <summary>
	/// The same geometry as <see cref="BuildRoot"/>, split by the node each part hangs from and left
	/// in that node's own space — what a shape has to be to animate.
	///
	/// <para>DBSIM draws a shape exactly this way. <c>TSGroup_RenderPolys</c> (<c>004758c8</c>)
	/// begins by calling <c>00476014</c>, which takes the group's own <c>TSBasePart.Transform</c>
	/// (field +4), looks the node's world transform up in the shape instance's per-node array, and
	/// composes it with the current object-to-view transform before a single poly is drawn
	/// (<c>Concat(nodeWorld[transform], objectToView)</c>, then <c>Raster_SetModelTransform</c> (<c>0048c338</c>) installs it). Every
	/// group in the shape is placed by its own node, and it is the animation thread that moves those
	/// nodes.</para>
	///
	/// <para>Contrast <see cref="BuildRoot"/>, which bakes each group at the rest pose that
	/// <see cref="ResolveGroupOffset"/> works out and hands back one rigid mesh. That is still what a
	/// structure wants — nothing animates it — but it is why a HERC's legs never moved.</para>
	///
	/// <para>Coincident-twin removal (<see cref="DropCoincidentTwins"/>) runs across the whole shape
	/// first, in the shared rest-pose space, exactly as it does for the flat build: a textured poly
	/// and its flat-shaded twin always belong to the same group, so splitting afterwards keeps the
	/// same survivor either way.</para>
	///
	/// <para>Every cell of every <see cref="TSCellAnimPart"/> is built, each into its own segment
	/// under its own <see cref="CellGate"/>, because a machine's cells are damage state rather than
	/// a flipbook the shape can be built around — see <see cref="Sim.ShapeCellFrames"/>. The
	/// renderer draws the segment whose gate the object's cell frames name and leaves the rest
	/// alone.</para>
	/// </summary>
	/// <param name="hiddenPartIds"><inheritdoc cref="BuildRoot" path="/param[@name='hiddenPartIds']"/></param>
	public static MeshSegment[] BuildSegments(TSObject root, TextureAtlas? atlas = null,
			SurfaceShading? shading = null, IReadOnlySet<short>? hiddenPartIds = null) {
		var sink = new Collector { AllCells = true, AllDetailLevels = true };
		Collect(root, null, sink, atlas, shading, cellFrame: 0, hiddenPartIds);
		return EmitSegments(sink);
	}

	/// <summary>
	/// The same geometry as <see cref="BuildRoot"/> at cell zero — the placed rest pose — but split
	/// by the cell each piece stands on, so that a shape the simulation takes apart can lose a part
	/// without being rebuilt.
	///
	/// <para>This is the flat-mesh counterpart of <see cref="BuildSegments"/>, for the two classes
	/// that have cells damage drives but no nodes anything poses: a structure, whose parts collapse
	/// one at a time (<see cref="Sim.BaseObject.CellFrames"/>), and a flyer, which loses components
	/// like a machine but is drawn rigid.</para>
	///
	/// <para>A shape with no cell-animation parts comes back as a single ungated piece, which is
	/// <see cref="BuildRoot"/>'s mesh exactly.</para>
	/// </summary>
	/// <param name="hiddenPartIds"><inheritdoc cref="BuildRoot" path="/param[@name='hiddenPartIds']"/></param>
	public static MeshCell[] BuildCells(TSObject root, TextureAtlas? atlas = null,
			SurfaceShading? shading = null, IReadOnlySet<short>? hiddenPartIds = null) {
		var sink = new Collector { AllCells = true, AllDetailLevels = true };
		Collect(root, null, sink, atlas, shading, cellFrame: 0, hiddenPartIds);
		return EmitCells(sink);
	}

	/// <summary>
	/// <see cref="BuildRoot"/>'s mesh at <paramref name="cellFrame"/>, split by <see cref="TSDetailPart"/>
	/// level alone — for a shape whose cells are a flipbook built one mesh per cell, but whose detail
	/// levels are still chosen per object per frame: a weapon, a launcher round, a piece of debris.
	///
	/// <para>Every piece comes back ungated by cell, since <paramref name="cellFrame"/> has already
	/// picked the one cell each cell-animation part shows; no retail shape nests a detail part inside
	/// a cell-animation part, so that pick cannot hide a level. Empty when the shape has no detail
	/// part, which leaves <see cref="BuildRoot"/>'s single mesh as the whole of it.</para>
	/// </summary>
	public static MeshCell[] BuildDetailLevels(TSObject root, TextureAtlas? atlas = null,
			SurfaceShading? shading = null, int cellFrame = 0) {
		var sink = new Collector { AllDetailLevels = true };
		Collect(root, null, sink, atlas, shading, cellFrame);
		var pieces = EmitCells(sink);
		return pieces.Any(piece => piece.Gate.IsDetailGated) ? pieces : Array.Empty<MeshCell>();
	}

	/// <summary>
	/// Axis-aligned bounds of a built mesh, as (min, max) in render units. Used to sit a model on the
	/// ground and to derive <see cref="Sim.SimObject.ShapeRadius"/>, the original's vtable
	/// <c>+0x10</c>, which it reads off the shape the same way.
	/// </summary>
	public static (Vector3 Min, Vector3 Max) Bounds(IReadOnlyList<MeshVertex> vertices) {
		if (vertices.Count == 0) {
			return (Vector3.Zero, Vector3.Zero);
		}

		var min = new Vector3(float.MaxValue);
		var max = new Vector3(float.MinValue);
		foreach (var vertex in vertices) {
			min = Vector3.Min(min, vertex.Position);
			max = Vector3.Max(max, vertex.Position);
		}
		return (min, max);
	}
}
