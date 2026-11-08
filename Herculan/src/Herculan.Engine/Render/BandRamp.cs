using Herculan.Engine.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// One re-pointed shade ramp: <paramref name="Count"/> palette indices from <paramref name="Start"/>, which a Gouraud corner
/// at shade <c>s</c> takes entry <c>(s * Count) &gt;&gt; 8</c> of (<c>Raster_FillContourPolygon</c>, <c>004879c0</c>).
/// </summary>
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
	/// The ramps <paramref name="item"/> is drawn with re-pointed, seen from <paramref name="viewer"/>, at most
	/// <see cref="BandRamp.MaxPerItem"/>; empty leaves the palette's own. The same answer is the same list instance, which
	/// the renderer uploads once for every item that shares it.
	/// </summary>
	IReadOnlyList<BandRamp> For(SceneItem item, Vec3i viewer);
}
