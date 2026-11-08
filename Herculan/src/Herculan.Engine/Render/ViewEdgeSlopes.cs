namespace Herculan.Engine.Render;

/// <summary>
/// How far a view reaches either side of its axis at unit depth: the slope, across over depth, of
/// its left and right edges and, up over depth, of its top and bottom. For DBSIM's view these are
/// the projection centre's distance from each edge of the view rect over the focal length — the
/// <c>cx</c>, <c>w − cx</c>, <c>cy</c> and <c>h − cy</c> over <c>2^s</c> that
/// <c>ViewFrustum_Build</c> multiplies by the far distance (docs/retail/rendering/terrain-drawing.md, "The
/// planes"). See <see cref="Camera.EdgeSlopes"/>.
/// </summary>
public readonly record struct ViewEdgeSlopes(float Left, float Right, float Top, float Bottom);
