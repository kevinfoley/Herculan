using Herculan.Engine.Numerics;

namespace Herculan.Es1.View;

/// <summary>What a world pass into a rect of the cockpit draws (<see cref="Es1RectView"/>).</summary>
public enum Es1RectScene {
	/// <summary>
	/// Every zone shape, each through its slot <c>+4</c> outside the object list, with no distance band and no cull, its
	/// ramps as <c>WorldShades_ResetRamps</c> leaves them: the NAV MAP's (docs/es1/simulation/mfd-nav-map.md#the-paint).
	/// </summary>
	FlatZoneShapes,

	/// <summary>
	/// The MISSILE CAM's (docs/es1/simulation/mfd-missile-camera.md#painting): its own horizon, <c>Hzline_Draw</c> with
	/// no canvas origin, which paints every pixel of the rect; then the frame's objects through
	/// <c>ObjList_DrawFrameObjects</c>(0, camera) while <c>000928ea</c> is set, as the frame's own pass draws them but
	/// from this camera, which rides in nothing, under its projection, each on <c>WorldShades_ResetRamps</c>' ramps,
	/// the last band's cull still applied (docs/es1/rendering/polygon-shading.md#the-distance-bands).
	/// </summary>
	MissileCamera,
}

/// <summary>
/// A camera's view of the world drawn into a rect of the draw page by <see cref="Es1VideoMemory.DrawWorldView"/>: the
/// camera's place, as <c>Raster_InstallViewProjection</c> (<c>ES1DBSIM 0005bf68</c>) installs a <c>TSCamera</c>'s,
/// its near plane and focal shift, where its axis lands in the rect, where the rect lies on the page, and what is
/// drawn. Retail draws such a view with its software rasterizer into the rect's pixels; this engine draws it on the GPU
/// at the window's resolution, as it draws view 0's world (docs/herculan/plan-es1-first-mission.md#decisions, decision
/// 17, this engine's departure).
/// </summary>
/// <param name="Position">The camera's position, <c>+0</c>.</param>
/// <param name="Angles">Its angles <c>+0xc</c>: pitch, roll and heading.</param>
/// <param name="NearPlane">The near plane, in world units.</param>
/// <param name="FocalShift">The perspective shift: the focal length is 2^shift pixels.</param>
/// <param name="Centre">The projection centre, in pixels from the rect's top-left.</param>
/// <param name="Left">The page column of the rect's left edge.</param>
/// <param name="Scene">What is drawn.</param>
public readonly record struct Es1RectView(Vec3i Position, (short Pitch, short Roll, short Heading) Angles, int NearPlane,
	int FocalShift, (int X, int Y) Centre, int Left, Es1RectScene Scene) {
	/// <summary>Whether the scene paints every pixel of the rect, so every cell is the pass's whatever its depth.</summary>
	public bool FillsRect => Scene == Es1RectScene.MissileCamera;
}
