using System.Numerics;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Data.File.Dts.Bsp;
using HercWorks.Core.Data.File.Dts.Part;

namespace Herculan.Engine.Render;

/// <summary>
/// The part walk: which children of each container part are built — one cell or every cell of a
/// <see cref="TSCellAnimPart"/>, the finest or every level of a <see cref="TSDetailPart"/>, the
/// reachable children of a <see cref="TSBSPPart"/> — and where a part's node puts it at the rest pose.
/// </summary>
public static partial class DtsMeshBuilder {
	/// <summary>Safety bound on the transform parent chain, in case a file's relations form a cycle.</summary>
	private const int MaxTransformChainSteps = 64;

	/// <summary>
	/// Walks the node tree looking for geometry-bearing groups. Container nodes are descended into;
	/// <see cref="TSBitmapPart"/> carries no geometry (it is a camera-facing billboard, which needs
	/// per-frame geometry this builder doesn't produce) and is skipped.
	/// </summary>
	private static void Collect(TSObject? node, ANAnimList? animList, Collector sink,
			TextureAtlas? atlas, SurfaceShading? shading, int cellFrame = 0,
			IReadOnlySet<short>? hiddenPartIds = null) {
		// A hardpoint attachment slot is never drawn as it stands in the file — see
		// AttachmentPartIds. The original overwrites the part pointer every frame; skipping the part
		// puts the same nothing on screen for an empty hardpoint, and the fitted case is already
		// drawn separately from MECHWPNS.DTS.
		if (hiddenPartIds != null && node is TSBasePart { IdNumber: var partId }
				&& hiddenPartIds.Contains(partId)) {
			return;
		}

		switch (node) {
			case null:
				return;

			case ANShape shape:
				// An ANShape brings its own animation list into scope for everything beneath it, unless
				// the caller places the root through another's (Collector.PoseList).
				CollectParts(shape.Parts, sink.PoseList ?? shape.AnimationList ?? animList, sink, atlas,
					shading, cellFrame,
					hiddenPartIds);
				break;

			case TSDetailPart detailPart when sink.AllDetailLevels:
				CollectEveryDetail(detailPart, animList, sink, atlas, shading, cellFrame, hiddenPartIds);
				break;

			case TSDetailPart detailPart:
				CollectHighestDetail(detailPart, animList, sink, atlas, shading, cellFrame, hiddenPartIds);
				break;

			case TSCellAnimPart cellAnimPart:
				// Consecutive frames of one moving sub-part — a rocket's exhaust flame, say. Walking
				// all of them stacks every frame of the motion on top of itself, so exactly one cell
				// is taken, the way TSCellAnimPart_Render (004767e4) takes one:
				// children[counter % childCount].
				//
				// Unless the simulation is what moves this counter, which it is for a machine's body
				// parts and a structure's: there the mesh cannot be built around one cell, because
				// which cell is showing is per-object damage state. Collector.AllCells walks all of
				// them, gated, and the renderer picks — see MeshCell.
				if (cellAnimPart.Parts is { Length: > 0 } cells) {
					if (sink.AllCells) {
						// Kept rather than replaced: a cell inside a detail level stays that level's.
						var outer = sink.Gate;
						for (int i = 0; i < cells.Length; i++) {
							sink.Gate = outer with { Sequence = cellAnimPart.AnimSequence, Frame = (short)i };
							Collect(cells[i], animList, sink, atlas, shading, cellFrame, hiddenPartIds);
						}

						sink.Gate = outer;
						break;
					}

					Collect(cells[((cellFrame % cells.Length) + cells.Length) % cells.Length],
						animList, sink, atlas, shading, cellFrame, hiddenPartIds);
				}
				break;

			case TSBSPPart bspPart:
				CollectBspPart(bspPart, animList, sink, atlas, shading, cellFrame, hiddenPartIds);
				break;

			case TSBSPGroup bspGroup:
				AppendGroup(bspGroup, animList, sink, atlas, shading);
				break;

			case TSGroup group:
				AppendGroup(group, animList, sink, atlas, shading);
				break;

			case TSPartList partList:
				CollectParts(partList.Parts, animList, sink, atlas, shading, cellFrame, hiddenPartIds);
				break;
		}
	}

	/// <summary>
	/// A <see cref="TSBSPPart"/>'s children, each tagged with the part's <see cref="BspTree"/> and its
	/// own index, so the renderer can paint them in the walk's order for the eye — see
	/// <see cref="BspDrawGroup"/>. Only the children the tree reaches are built
	/// (<see cref="ReachableLeaves"/>).
	///
	/// <para>A part inside a child of another would tag its geometry with the inner part's child
	/// alone. No retail shape nests one part in another.</para>
	/// </summary>
	private static void CollectBspPart(TSBSPPart part, ANAnimList? animList, Collector sink,
			TextureAtlas? atlas, SurfaceShading? shading, int cellFrame,
			IReadOnlySet<short>? hiddenPartIds) {
		if (part.Parts is not { } parts) {
			return;
		}

		var tree = BspTree.From(part, frame => {
			var offset = ResolveTransformOffset(frame, animList);
			return WorldScale.DtsToRender(offset.X, offset.Y, offset.Z);
		}, hiddenPartIds);

		var outer = sink.Leaf;
		foreach (int leaf in ReachableLeaves(part)) {
			sink.Leaf = new BspLeaf(tree, leaf);
			Collect(parts[leaf], animList, sink, atlas, shading, cellFrame, hiddenPartIds);
		}

		sink.Leaf = outer;
	}

	/// <summary>
	/// The children of a <see cref="TSBSPPart"/> its tree reaches, in file order — the set
	/// <c>TSBSPPart_Render</c> (<c>00476b0c</c>) draws: it walks the tree from node 0 through
	/// <c>TSBSPPart_RenderNode</c> (<c>00476a1c</c>), and a child no node names is never drawn.
	/// docs/retail/rendering/dts-texture-binding.md, "<c>TSBSPPart</c> child selection", lists the retail
	/// shapes that carry one.
	/// </summary>
	internal static TSObject[] ReachableParts(TSBSPPart part) =>
		ReachableLeaves(part).Select(leaf => part.Parts![leaf]).ToArray();

	/// <summary>
	/// The indices of <see cref="ReachableParts"/>, ascending. The walk visits both sides of every
	/// node whichever side the eye is on, so the set is fixed and the mesh can be built around it;
	/// the order it paints them in is <see cref="BspTree.PaintOrder"/>'s, per frame.
	///
	/// <para>A tree with no nodes, or one whose links leave the node array or loop, is this engine's
	/// own handling — no retail shape has either: an empty tree reaches nothing, and a link past the
	/// array or back to a node already walked is not followed.</para>
	/// </summary>
	internal static int[] ReachableLeaves(TSBSPPart part) {
		if (part.Parts is not { Length: > 0 } parts || part.Nodes is not { Length: > 0 } nodes) {
			return Array.Empty<int>();
		}

		var reached = new bool[parts.Length];
		var walked = new bool[nodes.Length];
		var pending = new Stack<int>();
		pending.Push(0);

		while (pending.Count > 0) {
			int index = pending.Pop();
			if (index < 0 || index >= nodes.Length || walked[index]) {
				continue;
			}

			walked[index] = true;
			foreach (short link in new[] { nodes[index].Front, nodes[index].Back }) {
				if (link < 0) {
					continue;
				}

				if ((link & 0x4000) == 0) {
					pending.Push(link);
				} else if ((link & 0x3fff) < parts.Length) {
					reached[link & 0x3fff] = true;
				}
			}
		}

		return Enumerable.Range(0, parts.Length).Where(i => reached[i]).ToArray();
	}

	private static void CollectParts(TSObject[]? parts, ANAnimList? animList, Collector sink,
			TextureAtlas? atlas, SurfaceShading? shading, int cellFrame = 0,
			IReadOnlySet<short>? hiddenPartIds = null) {
		if (parts == null) {
			return;
		}

		foreach (var part in parts) {
			Collect(part, animList, sink, atlas, shading, cellFrame, hiddenPartIds);
		}
	}

	/// <summary>
	/// Every level of a <see cref="TSDetailPart"/>, each under its own <see cref="CellGate"/> naming
	/// one shared <see cref="PartDetail"/> — which is what lets the renderer pick a level per object
	/// per frame, as <c>TSDetailPart_Render</c> (<c>004768bc</c>) does, without rebuilding anything.
	/// </summary>
	private static void CollectEveryDetail(TSDetailPart detailPart, ANAnimList? animList,
			Collector sink, TextureAtlas? atlas, SurfaceShading? shading, int cellFrame = 0,
			IReadOnlySet<short>? hiddenPartIds = null) {
		if (detailPart.Parts is not { Length: > 0 } parts) {
			return;
		}

		var offset = ResolveGroupOffset(detailPart, animList);
		var detail = new PartDetail(detailPart.Radius, detailPart.Details ?? Array.Empty<short>(),
			parts.Length, WorldScale.DtsToRender(offset.X, offset.Y, offset.Z));

		var outer = sink.Gate;
		for (int level = 0; level < parts.Length; level++) {
			sink.Gate = outer with { Detail = detail, Level = (short)level };
			Collect(parts[level], animList, sink, atlas, shading, cellFrame, hiddenPartIds);
		}

		sink.Gate = outer;
	}

	/// <summary>
	/// A <see cref="TSDetailPart"/> holds several complete alternate representations of the same
	/// sub-structure, paired 1:1 with ascending on-screen-size thresholds in <c>Details</c>. This
	/// takes the <b>last</b> one, which is the level <c>TSDetailPart_Render</c> (<c>004768bc</c>)
	/// draws at bias 0 for an object close enough — see <see cref="PartDetail.Select"/>. It is what
	/// every build but <see cref="BuildCells"/>, <see cref="BuildSegments"/> and
	/// <see cref="BuildDetailLevels"/> draws, at any distance.
	///
	/// <para>Picking the part paired with the largest <i>threshold</i> is not the same rule, though
	/// it agrees on every retail shape (all of them end at 255). It would diverge on a file whose
	/// thresholds were not ascending.</para>
	/// </summary>
	private static void CollectHighestDetail(TSDetailPart detailPart, ANAnimList? animList,
			Collector sink, TextureAtlas? atlas, SurfaceShading? shading, int cellFrame = 0,
			IReadOnlySet<short>? hiddenPartIds = null) {
		if (detailPart.Parts is not { Length: > 0 } parts) {
			return;
		}

		Collect(parts[^1], animList, sink, atlas, shading, cellFrame, hiddenPartIds);
	}

	/// <summary>
	/// Walks a part's transform-id parent chain summing translations — a group's, to place its points,
	/// or a detail part's, to find where its level selection measures from.
	///
	/// <para>Rotation is deliberately left unapplied here, and costs nothing: no retail shape's rest
	/// pose carries one. Every node of all 18 HERCs has a zero-rotation default transform, so this
	/// sum and <see cref="BuildSegments"/>'s full composition agree to the last vertex — checked
	/// against the built meshes' own bounds. Rotation is what an animated node acquires, and that
	/// path applies it.</para>
	/// </summary>
	private static Vector3 ResolveGroupOffset(TSBasePart group, ANAnimList? animList) =>
		ResolveTransformOffset(group.Transform, animList);

	/// <summary>
	/// <see cref="ResolveGroupOffset"/> for a transform id rather than a part's — where a
	/// <see cref="TSBSPPart"/> node's plane sits at the rest pose (<see cref="BspTree.RestOffset"/>).
	/// </summary>
	private static Vector3 ResolveTransformOffset(int transformId, ANAnimList? animList) {
		if (animList?.Relations == null || animList.Transforms == null || animList.DefaultTransforms == null) {
			return Vector3.Zero;
		}

		var parentOf = new Dictionary<int, int>();
		foreach (var relation in animList.Relations) {
			parentOf[relation.Y] = relation.X;
		}

		Vector3 offset = Vector3.Zero;

		for (int step = 0; transformId != -1 && step < MaxTransformChainSteps; step++) {
			if (transformId < 0 || transformId >= animList.DefaultTransforms.Length) {
				break;
			}

			int transformIndex = animList.DefaultTransforms[transformId];
			if (transformIndex < 0 || transformIndex >= animList.Transforms.Length) {
				break;
			}

			if (animList.Transforms[transformIndex].Translation is { } translation) {
				offset += new Vector3(translation.X, translation.Y, translation.Z);
			}

			if (!parentOf.TryGetValue(transformId, out int parentId)) {
				break;
			}
			transformId = parentId;
		}

		return offset;
	}
}
