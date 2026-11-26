using Herculan.Engine.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// One re-pointed shade ramp: a run of palette indices, which a Gouraud corner
/// at shade <c>s</c> takes entry <c>(s * Count) &gt;&gt; 8</c> of (<c>Raster_FillContourPolygon</c>, <c>004879c0</c>), as
/// does a shaded face of an item that re-points it (<see cref="SceneItem.RepointedRamps"/>).
/// </summary>
/// <param name="Ramp">The ramp's number.</param>
/// <param name="Start">The first palette index.</param>
/// <param name="Count">The number of palette indices.</param>
public readonly record struct BandRamp(int Ramp, int Start, int Count) {
	/// <summary>The most ramps one item re-points that the scene program takes.</summary>
	public const int MaxPerItem = 8;
}

/// <summary>
/// What re-points an item's <c>TSGouraudPoly</c> ramps before its draw (<see cref="SceneRenderer.DistanceBands"/>), from
/// what the item draws and how far it is from the view.
/// </summary>
public interface IBandRampSource {
	/// <summary>
	/// The ramps an item is drawn with re-pointed, seen from a viewer, at most
	/// <see cref="BandRamp.MaxPerItem"/>; empty leaves the palette's own. The same answer is the same list instance, which
	/// the renderer uploads once for every item that shares it.
	/// </summary>
	/// <param name="item">The item.</param>
	/// <param name="viewer">The viewer's position.</param>
	IReadOnlyList<BandRamp> For(SceneItem item, Vec3i viewer);
}
