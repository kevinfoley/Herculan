using System.Numerics;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Bsp;
using HercWorks.Core.Data.File.Dts.Part;

namespace Herculan.Engine.Render;

/// <summary>
/// One child of one <see cref="TSBSPPart"/>: which tree it hangs from and which of that part's
/// <c>Parts</c> it is. Geometry built under a BSP part carries one, and the renderer paints the
/// children of a tree in the order <see cref="BspTree.PaintOrder"/> gives — see
/// <see cref="BspDrawGroup"/>.
/// </summary>
/// <param name="Tree">The part's tree, shared by every piece built under that part in one build.</param>
/// <param name="Index">The child's index in the part's <c>Parts</c>.</param>
public readonly record struct BspLeaf(BspTree Tree, int Index);

/// <summary>
/// A <see cref="TSBSPPart"/>'s tree in render space, which orders the part's children back to front
/// from the eye each frame the way <c>TSBSPPart_Render</c> (<c>00476b0c</c>) and
/// <c>TSBSPPart_RenderNode</c> (<c>00476a1c</c>) do. docs/retail/rendering/dts-texture-binding.md,
/// "<c>TSBSPPart</c> child selection", has the walk.
///
/// <para>Each node's splitting plane is in a frame of its own: the node's transform id when it
/// carries one, which brings the eye into that node's space, and otherwise the part's own, which is
/// <c>-1</c> — the object — on every retail part. A plane is kept as the file stores it, converted to
/// render axes and units, so the side test is the original's <c>dot(normal, eye) - coeff</c> to the
/// sign.</para>
///
/// <para>A tree is built per mesh build, so the flat rest-pose pieces and the posed segments of one
/// shape each carry their own; <see cref="RestOffset"/> is what a rest-pose piece's frames sit at.</para>
/// </summary>
public sealed class BspTree {
	private readonly Vector4[] _planes;
	private readonly int[] _frames;
	private readonly short[] _front;
	private readonly short[] _back;
	private readonly Dictionary<int, Vector3> _restOffsets;
	private readonly Dictionary<short, int> _leafOfPart;
	private readonly int[] _leafNodes;
	private readonly bool[] _attachmentSlots;

	private readonly bool _whole;

	private BspTree(Vector4[] planes, int[] frames, short[] front, short[] back, int leafCount,
			Dictionary<int, Vector3> restOffsets, Dictionary<short, int> leafOfPart, int[] leafNodes,
			bool[] attachmentSlots, bool whole = false) {
		_whole = whole;
		_planes = planes;
		_frames = frames;
		_front = front;
		_back = back;
		LeafCount = leafCount;
		_restOffsets = restOffsets;
		_leafOfPart = leafOfPart;
		_leafNodes = leafNodes;
		_attachmentSlots = attachmentSlots;
	}

	/// <summary>How many children the part has, reached or not — the bound on a leaf index.</summary>
	public int LeafCount { get; }

	/// <param name="part">The part, whose <c>Nodes</c> and <c>Transforms</c> are read.</param>
	/// <param name="restOffset">
	/// Where a transform id's frame sits at the rest pose, in render space — what a piece baked at the
	/// rest pose is placed by, and so what its node planes have to be measured in.
	/// </param>
	/// <param name="attachmentPartIds">
	/// The machine's hardpoint attachment slots (<c>DtsMeshBuilder.AttachmentPartIds</c>), whose children
	/// are overwritten before every draw — see <see cref="IsAttachmentSlot"/>. Null for a shape with none.
	/// </param>
	public static BspTree From(TSBSPPart part, Func<int, Vector3> restOffset,
			IReadOnlySet<short>? attachmentPartIds = null) {
		var nodes = part.Nodes ?? Array.Empty<TSBSPPartNode>();
		var planes = new Vector4[nodes.Length];
		var frames = new int[nodes.Length];
		var front = new short[nodes.Length];
		var back = new short[nodes.Length];
		var restOffsets = new Dictionary<int, Vector3>();

		// The plane's DTS normal and coefficient in render axes: DtsToRender maps (x, y, z) to
		// (x, z, -y) scaled, so dot(normal, p) - coeff keeps its sign with the normal mapped the same
		// way and the coefficient carried through the same scale.
		float scale = WorldScale.WorldUnitsPerDtsUnit / WorldScale.WorldUnitsPerMeter;
		for (int i = 0; i < nodes.Length; i++) {
			var normal = nodes[i].Normal;
			planes[i] = new Vector4(normal?.X ?? 0, normal?.Z ?? 0, -(normal?.Y ?? 0), nodes[i].Coeff * scale);

			short transform = part.Transforms is { } transforms && i < transforms.Length ? transforms[i] : (short)-1;
			frames[i] = transform != -1 ? transform : part.Transform;
			front[i] = nodes[i].Front;
			back[i] = nodes[i].Back;

			if (!restOffsets.ContainsKey(frames[i])) {
				restOffsets[frames[i]] = restOffset(frames[i]);
			}
		}

		// A hardpoint attachment slot is a child of the machine's part, and what is spliced into it is
		// drawn where the walk reaches that child — see LeafOfPart. Every retail slot is a direct child.
		var leafOfPart = new Dictionary<short, int>();
		var parts = part.Parts ?? Array.Empty<TSObject>();
		var leafNodes = new int[parts.Length];
		var attachmentSlots = new bool[parts.Length];
		var leafFrames = new HashSet<int>();
		for (int i = 0; i < parts.Length; i++) {
			if (parts[i] is TSBasePart { IdNumber: not 0 } child) {
				leafOfPart.TryAdd(child.IdNumber, i);
				attachmentSlots[i] = attachmentPartIds?.Contains(child.IdNumber) ?? false;
			}

			leafNodes[i] = FirstNode(parts[i]);
			if (leafNodes[i] >= 0) {
				leafFrames.Add(leafNodes[i]);
			}
		}

		foreach (int frame in leafFrames) {
			restOffsets.TryAdd(frame, restOffset(frame));
		}

		return new BspTree(planes, frames, front, back, parts.Length, restOffsets, leafOfPart, leafNodes,
			attachmentSlots);
	}

	/// <summary>
	/// A tree of one child and no planes, which <see cref="PaintOrder"/> always paints: the geometry of a
	/// shape that no <see cref="TSBSPPart"/> holds, made into a group only so that its paint layers are
	/// painted through the stencil as a part's children are (<see cref="PaintLayers"/>). This engine's
	/// own; it stands for no record in the file.
	/// </summary>
	public static BspTree Whole() =>
		new(Array.Empty<Vector4>(), Array.Empty<int>(), Array.Empty<short>(), Array.Empty<short>(), 1,
			new Dictionary<int, Vector3>(), new Dictionary<short, int>(), new[] { -1 }, new bool[1], whole: true);

	/// <summary>The distinct frames the node planes and the children are in, each once.</summary>
	public IEnumerable<int> Frames => _restOffsets.Keys;

	/// <summary>
	/// Where <paramref name="frame"/> sits at the rest pose, in render space; zero for a frame neither
	/// a node plane nor a child is in.
	/// </summary>
	public Vector3 RestOffset(int frame) => _restOffsets.GetValueOrDefault(frame);

	/// <summary>
	/// The node drawing child <paramref name="leaf"/> installs, or -1 for a child placed by no node.
	/// <c>TSGroup_BindNodeTransform</c> (<c>00476014</c>) is the first thing every part's render does —
	/// <c>TSGroup_RenderPolys</c> (<c>004758c8</c>), <c>TSCellAnimPart_Render</c> (<c>004767e4</c>) before it
	/// picks its cell, <c>TSPartList_Render</c> (<c>004766fc</c>) — so this is the first non-negative
	/// <c>TSBasePart.Transform</c> in the child's render order. Every child of every retail
	/// <see cref="TSBSPPart"/> installs at most one node, the same one in each of its cells, so the one
	/// id stands for the whole child; read off every <c>.DTS</c> root the game ships.
	/// </summary>
	public int LeafNode(int leaf) => leaf >= 0 && leaf < _leafNodes.Length ? _leafNodes[leaf] : -1;

	/// <summary>
	/// Whether child <paramref name="leaf"/> is a hardpoint attachment slot, which the original
	/// overwrites before each draw (<c>Mech_SpliceHardpointShapes</c>, <c>004030d0</c>): with the fitted
	/// mount, whose render installs the slot's node (<c>ConfigPart_Render</c>, <c>0040316c</c>), or with a
	/// blank record, whose render installs nothing. Its <see cref="LeafNode"/> is the node the mount
	/// inherits.
	/// </summary>
	public bool IsAttachmentSlot(int leaf) => leaf >= 0 && leaf < _attachmentSlots.Length && _attachmentSlots[leaf];

	private static int FirstNode(TSObject? part) {
		if (part is TSBasePart { Transform: >= 0 } placed) {
			return placed.Transform;
		}

		if (part is TSPartList { Parts: { } children }) {
			foreach (var child in children) {
				int node = FirstNode(child);
				if (node >= 0) {
					return node;
				}
			}
		}

		return -1;
	}

	/// <summary>
	/// Which child is the part whose <c>TSBasePart.IdNumber</c> is <paramref name="partId"/> — how
	/// a weapon finds the hardpoint slot <c>Mech_SpliceHardpointShapes</c> (<c>004030d0</c>) puts its
	/// shape into, which the walk then draws in that child's turn. A child the tree never reaches
	/// is still found, and what is put there is never drawn, as in the original.
	/// </summary>
	public bool TryGetLeafOfPart(short partId, out int leaf) => _leafOfPart.TryGetValue(partId, out leaf);

	/// <summary>
	/// The children in the order the original paints them for an eye at <paramref name="eyeInFrame"/>,
	/// first painted first. At each node the side of the plane the eye is on is painted last, an eye
	/// on the plane counting as in front: this is <c>TSBSPPart_RenderNode</c>'s branch for
	/// <c>maybe_g_DepthBufferEnabled</c> zero, its value in the image. A child no node names is never
	/// written.
	///
	/// <para>A link past the node array is not followed, and nor is a walk deeper than the node count,
	/// which only a looping tree reaches — this engine's own guards; no retail tree has either.</para>
	/// </summary>
	/// <param name="eyeInFrame">The eye in a given frame's own space, render units — see <see cref="Frames"/>.</param>
	/// <param name="order">Receives the leaf indices; <see cref="LeafCount"/> long is always enough.</param>
	/// <returns>How many leaves were written.</returns>
	public int PaintOrder(Func<int, Vector3> eyeInFrame, Span<int> order) {
		int count = 0;
		if (_whole && order.Length > 0) {
			order[count++] = 0;
		} else if (_planes.Length > 0) {
			Walk(0, 0, eyeInFrame, order, ref count);
		}

		return count;
	}

	private void Walk(int node, int depth, Func<int, Vector3> eyeInFrame, Span<int> order, ref int count) {
		if (node < 0 || node >= _planes.Length || depth > _planes.Length) {
			return;
		}

		var plane = _planes[node];
		float side = Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), eyeInFrame(_frames[node])) - plane.W;
		bool eyeBehind = side < 0f;

		Visit(eyeBehind ? _front[node] : _back[node], depth, eyeInFrame, order, ref count);
		Visit(eyeBehind ? _back[node] : _front[node], depth, eyeInFrame, order, ref count);
	}

	private void Visit(short link, int depth, Func<int, Vector3> eyeInFrame, Span<int> order, ref int count) {
		if (link < 0) {
			return;
		}

		if ((link & 0x4000) == 0) {
			Walk(link, depth + 1, eyeInFrame, order, ref count);
		} else if ((link & 0x3fff) < LeafCount && count < order.Length) {
			order[count++] = link & 0x3fff;
		}
	}
}
