using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Scene;

/// <summary>
/// One machine's LOD chain as the draw items hold it: every root's items, uploaded together, and which of
/// them is currently drawn. See <see cref="Herculan.Engine.Render.ShapeDetail"/> for the selection
/// and docs/retail/formats/mech-shape-drawing.md for the mechanism it ports.
/// </summary>
/// <param name="Subject">The machine, whose position the distance to the eye is measured to.</param>
/// <param name="ShapeRadius">Root 0's own bounding radius in world units.</param>
/// <param name="Roots">The items of each root, finest first. A root that failed to upload is empty.</param>
public sealed record MechDetailChain(SimObject Subject, int ShapeRadius, SceneItem[][] Roots) {
	/// <summary>The root currently selected, which starts at 0 as the build leaves it.</summary>
	public int Active { get; set; }
}

/// <summary>The distances and focal length both the kept and the rebuilt items choose their detail by.</summary>
public static class DetailMetrics {
	// The focal length of the view being drawn, in its own pixels. Retail's is the video mode's fixed
	// 2^9 = 512 over 480 rows (docs/retail/formats/cockpit-views.md); taking it off the window instead keeps the
	// detail thresholds a count of pixels on the screen actually being drawn, which is what makes them a
	// measure of apparent size rather than of a 1996 monitor's.
	public static int FocalPixels(int framebufferHeight) => Math.Max((int)MathF.Round(
		framebufferHeight * Camera.FocalLengthPixels / Camera.FocalViewHeightPixels), 1);

	// Eye to a detail part's own node, in world units, for an object drawn at `transform`. To the node
	// rather than to the object's origin: the original measures after binding the part's transform, so
	// the distance is to where that node sits.
	public static int Distance(PartDetail detail, System.Numerics.Matrix4x4 transform, System.Numerics.Vector3 eye) {
		var node = System.Numerics.Vector3.Transform(detail.Origin, transform);
		return (int)Math.Min(System.Numerics.Vector3.Distance(node, eye) * WorldScale.WorldUnitsPerMeter, int.MaxValue);
	}
}
