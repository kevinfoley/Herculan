using System.Numerics;
using Herculan.Engine.Render;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Where <see cref="PaintLayers"/> cuts a slot's paint order: at a poly lying over an earlier one, as the
/// TDF logo lies over its structure wall (docs/retail/rendering/dts-texture-binding.md, "Poly order
/// within a group"). Synthetic quads in render units, all facing +Z.
/// </summary>
public class PaintLayersTests {
	/// <summary>The logo after its wall in one group: the logo is painted in a layer over the wall's.</summary>
	[Fact]
	public void ALaterPolyOverAnEarlierOneStartsALayer() {
		var layers = PaintLayers.Assign(Quad(0, 0, 4).Concat(Quad(1, 1, 2)));

		Assert.True(layers.Layered(null));
		Assert.Equal(0, layers.LayerOf(null, 0));
		Assert.Equal(1, layers.LayerOf(null, 1));
	}

	/// <summary>A poly painted after the logo stays on the logo's layer, so the logo never covers it.</summary>
	[Fact]
	public void PolysAfterTheCutStayOnItsLayer() {
		var layers = PaintLayers.Assign(Quad(0, 0, 4).Concat(Quad(1, 1, 2)).Concat(Quad(2, 10, 12)));

		Assert.Equal(1, layers.LayerOf(null, 2));
	}

	/// <summary>
	/// A logo standing a little off its wall's plane still lies over it — <c>BASES.DGS</c> shape 1's is
	/// 19.4 DTS units behind it at one edge — and one well clear of it does not.
	/// </summary>
	[Theory]
	[InlineData(0.2f, true)]
	[InlineData(0.6f, false)]
	public void APolyOffThePlaneLiesOverItWithinTheTolerance(float offset, bool layered) {
		var layers = PaintLayers.Assign(Quad(0, 0, 4).Concat(Quad(1, 1, 2, z: -offset)));

		Assert.Equal(layered, layers.Layered(null));
	}

	/// <summary>Two cells of one sequence are never on screen together, so neither cuts the other's run.</summary>
	[Fact]
	public void AlternativeCellsDoNotCut() {
		var layers = PaintLayers.Assign(Quad(0, 0, 4, gate: new CellGate(0, 0))
			.Concat(Quad(1, 1, 2, gate: new CellGate(0, 1))));

		Assert.False(layers.Layered(null));
	}

	/// <summary>Neighbours sharing an edge share no area.</summary>
	[Fact]
	public void EdgeNeighboursDoNotCut() {
		var layers = PaintLayers.Assign(Quad(0, 0, 4).Concat(Quad(1, 4, 8)));

		Assert.False(layers.Layered(null));
	}

	/// <summary>Two children of a part are ordered by the part's walk, not by a cut.</summary>
	[Fact]
	public void PolysInOtherSlotsDoNotCut() {
		var child = new BspLeaf(BspTree.Whole(), 0);
		var layers = PaintLayers.Assign(Quad(0, 0, 4).Concat(Quad(1, 1, 2, slot: child)));

		Assert.False(layers.Layered(null));
		Assert.False(layers.Layered(child));
	}

	/// <summary>A square from (from, from) to (to, to) in the plane Z = z, as two triangles of one poly.</summary>
	private static PaintLayers.Face[] Quad(int polyId, float from, float to, float z = 0f,
			CellGate? gate = null, BspLeaf? slot = null) {
		var a = new Vector3(from, from, z);
		var b = new Vector3(to, from, z);
		var c = new Vector3(to, to, z);
		var d = new Vector3(from, to, z);
		PaintLayers.Face Face(Vector3 p, Vector3 q, Vector3 r) =>
			new(polyId, slot, -1, gate ?? CellGate.Ungated, 0, FacingGate.None, Vector3.UnitZ, p, q, r);
		return new[] { Face(a, b, c), Face(a, c, d) };
	}
}
