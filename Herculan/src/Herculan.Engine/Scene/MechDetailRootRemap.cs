using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Bsp;
using HercWorks.Core.Data.File.Dts.Part;
using Herculan.Engine.Sim.Anim;

namespace Herculan.Engine.Scene;

/// <summary>
/// Port of <c>MechType_RemapDetailRootTransforms</c> (<c>00420090</c>): rewrites every LOD root of a
/// machine's shape after the first onto root 0's node numbering, in place, once at load, so every
/// root can be posed through root 0's one animation. See docs/retail/rendering/mech-shape-drawing.md,
/// "The crude roots are renumbered at load".
/// </summary>
public static class MechDetailRootRemap {
	/// <summary>
	/// Length of one root's map — the original's 50-byte stack row, and the most part ids it reads.
	/// </summary>
	private const int MapLength = 50;

	/// <summary>
	/// Renumbers <paramref name="roots"/> 1 onward through the parts <paramref name="partIds"/> lists,
	/// the chassis <c>.DAT</c>'s signed bytes ended by a negative one
	/// (<c>HercSimDat.ModelLoDBoneIds</c>). Does nothing to a shape with one root.
	///
	/// <para>The original reads root 0's part without a null check, and indexes its 50-byte row with
	/// whatever transform id it meets. This engine skips a part root 0 does not have and treats an
	/// id outside the row as unmapped, where the original would read through a null pointer or past
	/// the row; no retail chassis reaches either case.</para>
	/// </summary>
	public static void Apply(IReadOnlyList<TSObject> roots, IReadOnlyList<byte> partIds) {
		if (roots.Count <= 1) {
			return;
		}

		var maps = new sbyte[roots.Count][];
		for (int level = 1; level < roots.Count; level++) {
			maps[level] = new sbyte[MapLength];
			Array.Fill(maps[level], (sbyte)-1);
		}

		for (int i = 0; i < partIds.Count && i < MapLength; i++) {
			sbyte partId = (sbyte)partIds[i];
			if (partId < 0) {
				break;
			}

			if (FindPart(roots[0], partId) is not { } reference) {
				continue;
			}

			for (int level = 1; level < roots.Count; level++) {
				if (FindPart(roots[level], partId) is { Transform: >= 0 and < MapLength } part) {
					maps[level][part.Transform] = (sbyte)reference.Transform;
				}
			}
		}

		// The bound on a TSBSPPart's per-node entries is the size of root 0's keyframe pool — the
		// int16 at its ANAnimList +0x10, which ANAnimList_ReadFromStream (00491aa4) fills with the
		// transform count.
		int keyframeCount = ShapeAnimation.FirstAnimList(roots[0])?.Transforms?.Length ?? 0;

		for (int level = 1; level < roots.Count; level++) {
			RemapTransformIds(roots[level], maps[level], keyframeCount);
		}
	}

	/// <summary>
	/// <c>TSPartList_FindPart</c> (<c>00476744</c>) and <c>TSPartBase_FindPart</c>
	/// (<c>00476120</c>): the part itself when its id matches, else the first match among its
	/// children, depth first.
	/// </summary>
	private static TSBasePart? FindPart(TSObject? node, int partId) {
		if (node is not TSBasePart part) {
			return null;
		}

		if (part.IdNumber == partId) {
			return part;
		}

		if (part is TSPartList { Parts: { } children }) {
			foreach (var child in children) {
				if (FindPart(child, partId) is { } found) {
					return found;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// <c>Shape_RemapTransformIds</c> (<c>0041ff88</c>), which switches on the exact class tag:
	/// a <see cref="TSGroup"/> (not a <see cref="TSBSPGroup"/>) and a <see cref="TSCellAnimPart"/>
	/// have their own id remapped, a <see cref="TSBSPPart"/> its per-node entries, and every list
	/// class is descended into except through a <see cref="TSGroup"/>.
	/// </summary>
	private static void RemapTransformIds(TSObject? node, sbyte[] map, int keyframeCount) {
		switch (node) {
			case TSGroup group when node.GetType() == typeof(TSGroup):
				group.Transform = RemapTransformId(group.Transform, map);
				return;

			case TSCellAnimPart cellAnimPart:
				cellAnimPart.Transform = RemapTransformId(cellAnimPart.Transform, map);
				break;

			case TSBSPPart bspPart when bspPart.Transforms is { } transforms:
				for (int i = 0; i < transforms.Length; i++) {
					short id = transforms[i];
					if (id > 0 && id < keyframeCount && id < MapLength && map[id] >= 0) {
						transforms[i] = map[id];
					}
				}
				break;

			case TSPartList:
				break;

			default:
				return;
		}

		foreach (var child in ((TSPartList)node).Parts ?? Array.Empty<TSObject>()) {
			RemapTransformIds(child, map, keyframeCount);
		}
	}

	/// <summary>
	/// <c>Shape_RemapTransformId</c> (<c>0041ff68</c>): a negative id stays, any other becomes its
	/// map entry, which is -1 where the map was never set.
	/// </summary>
	private static short RemapTransformId(short id, sbyte[] map) =>
		id < 0 ? id : id < MapLength ? map[id] : (short)-1;
}
