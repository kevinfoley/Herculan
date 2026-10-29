using System.Numerics;
using Herculan.Engine.Render;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Where <see cref="PaintLayers"/> cuts a slot's paint order: at a poly lying over an earlier one, as the
/// TDF logo lies over its structure wall (docs/retail/rendering/dts-texture-binding.md, "Poly order
/// within a group"). Synthetic quads in render units, facing +Z unless a test says otherwise.
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

	/// <summary>
	/// A poly painted after the logo stays on the logo's layer even where it lies over the wall: only the
	/// run it would join, which starts at the logo, is tested.
	/// </summary>
	[Fact]
	public void PolysAfterTheCutStayOnItsLayer() {
		var layers = PaintLayers.Assign(Quad(0, 0, 4).Concat(Quad(1, 1, 2)).Concat(Quad(2, 2.5f, 3.5f)));

		Assert.Equal(1, layers.LayerOf(null, 2));
	}

	/// <summary>
	/// A logo standing a little off its wall's plane, as the retail ones do, still lies over it, and one
	/// well clear of it does not: 0.2 and 0.6 render units are about 33 and 100 DTS units, either side of
	/// the tolerance.
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

	/// <summary>
	/// A post face drawn from its front only, flush under a facade drawn from both sides whose normal
	/// points the other way, as on structure types 22 and 23: the facade's back is seen from the
	/// post's side, so it lies over the post.
	/// </summary>
	[Fact]
	public void ATwoSidedPolyLiesOverAFlushFaceWhicheverWayItsNormalPoints() {
		var layers = PaintLayers.Assign(Quad(0, 1, 2, side: 1)
			.Concat(Quad(1, 0, 4, normal: -Vector3.UnitZ, side: 0)));

		Assert.Equal(1, layers.LayerOf(null, 1));
	}

	/// <summary>Two one-sided faces back to back are never seen together, so neither cuts the other's run.</summary>
	[Fact]
	public void OneSidedFacesBackToBackDoNotCut() {
		var layers = PaintLayers.Assign(Quad(0, 1, 2, side: 1)
			.Concat(Quad(1, 0, 4, normal: -Vector3.UnitZ, side: 1)));

		Assert.False(layers.Layered(null));
	}

	/// <summary>
	/// A square from (from, from) to (to, to) in the plane Z = z, as two triangles of one poly, facing
	/// <paramref name="normal"/> (+Z by default) and drawn from <paramref name="side"/> (both by default).
	/// </summary>
	private static PaintLayers.Face[] Quad(int polyId, float from, float to, float z = 0f,
			CellGate? gate = null, BspLeaf? slot = null, Vector3? normal = null, int side = 0) {
		var a = new Vector3(from, from, z);
		var b = new Vector3(to, from, z);
		var c = new Vector3(to, to, z);
		var d = new Vector3(from, to, z);
		PaintLayers.Face Face(Vector3 p, Vector3 q, Vector3 r) =>
			new(polyId, slot, -1, gate ?? CellGate.Ungated, side, FacingGate.None, normal ?? Vector3.UnitZ, p, q, r);
		return new[] { Face(a, b, c), Face(a, c, d) };
	}
}
