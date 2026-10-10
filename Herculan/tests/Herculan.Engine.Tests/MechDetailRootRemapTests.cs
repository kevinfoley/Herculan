using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Part;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim.Anim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The load-time renumbering of a machine's crude LOD roots against the retail shapes — see
/// docs/retail/rendering/mech-shape-drawing.md, "The crude roots are renumbered at load".
///
/// <para>Skips silently with no install present, as the rest of the suite does.</para>
/// </summary>
public class MechDetailRootRemapTests {
	/// <summary>
	/// Every <see cref="TSGroup"/> and <see cref="TSCellAnimPart"/> in a crude root that has a node
	/// lands on one of root 0's: the retail part lists reach every node those roots draw through. A
	/// part with no node (-1) keeps none.
	/// </summary>
	[Theory]
	[MemberData(nameof(MechLocomotionTests.Hercs), MemberType = typeof(MechLocomotionTests))]
	public void EveryCrudeRootPartLandsOnARootZeroNode(string herc) {
		if (Load(herc) is not { } loaded) {
			return;
		}

		var (roots, partIds) = loaded;
		var before = new Dictionary<TSBasePart, short>(ReferenceEqualityComparer.Instance);
		for (int level = 1; level < roots.Count; level++) {
			foreach (var part in RemappedParts(roots[level])) {
				before[part] = part.Transform;
			}
		}

		MechDetailRootRemap.Apply(roots, partIds);

		int nodeCount = ShapeAnimation.FirstAnimList(roots[0])?.DefaultTransforms?.Length ?? 0;
		for (int level = 1; level < roots.Count; level++) {
			foreach (var part in RemappedParts(roots[level])) {
				if (before[part] < 0) {
					Assert.Equal(before[part], part.Transform);
				} else {
					Assert.InRange(part.Transform, 0, nodeCount - 1);
				}
			}
		}
	}

	/// <summary>
	/// Each listed part takes the transform id the same part has in root 0 — on APOCA, whose root 4
	/// compacts its numbering and so is where an unrenumbered draw puts the upper body on a knee.
	/// </summary>
	[Fact]
	public void ListedPartsTakeRootZerosTransformIds() {
		if (Load("APOCA") is not { } loaded) {
			return;
		}

		var (roots, partIds) = loaded;
		MechDetailRootRemap.Apply(roots, partIds);

		foreach (byte raw in partIds) {
			sbyte partId = (sbyte)raw;
			if (partId < 0) {
				break;
			}

			if (Find(roots[0], partId) is not { } reference) {
				continue;
			}

			for (int level = 1; level < roots.Count; level++) {
				if (Find(roots[level], partId) is { } part
						&& (part.GetType() == typeof(TSGroup) || part is TSCellAnimPart)) {
					Assert.Equal(reference.Transform, part.Transform);
				}
			}
		}
	}

	private static (IReadOnlyList<TSObject> Roots, byte[] PartIds)? Load(string herc) {
		if (GameInstall.Locate(null) is not { } root
				|| GameContent.MountSimulator(root) is not { } content
				|| content.Read("dts", herc + ".DTS") is not { } dtsBytes
				|| content.Read("dat", herc + ".DAT") is not { } datBytes
				|| new DTSModelTransformer().Parse(dtsBytes) is not DynamixThreeSpaceModel { Roots: { Count: > 1 } roots }
				|| new HercSimDataTransformer().Parse(datBytes) is not HercSimDat data) {
			return null;
		}

		return (roots, data.ModelLoDBoneIds);
	}

	/// <summary>The parts whose own id the renumbering rewrites, as Shape_RemapTransformIds walks them.</summary>
	private static IEnumerable<TSBasePart> RemappedParts(TSObject? node) {
		if (node is TSGroup group && node.GetType() == typeof(TSGroup)) {
			yield return group;
			yield break;
		}

		if (node is TSCellAnimPart cellAnimPart) {
			yield return cellAnimPart;
		}

		if (node is TSPartList { Parts: { } children }) {
			foreach (var child in children) {
				foreach (var part in RemappedParts(child)) {
					yield return part;
				}
			}
		}
	}

	private static TSBasePart? Find(TSObject? node, int partId) {
		if (node is not TSBasePart part) {
			return null;
		}

		if (part.IdNumber == partId) {
			return part;
		}

		foreach (var child in (part as TSPartList)?.Parts ?? Array.Empty<TSObject>()) {
			if (Find(child, partId) is { } found) {
				return found;
			}
		}

		return null;
	}
}
