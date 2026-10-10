using System.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// What F3's NAV MAP draws that the layout does not supply — the machine it is centred on and the
/// relief under it. Everything else about the screen is a constant; see <see cref="MfdNavMap"/>.
/// </summary>
/// <param name="CentreX">World x the map is centred on: the viewing machine's own, <c>obj+0x26</c>.</param>
/// <param name="CentreY">World y, <c>obj+0x2a</c>.</param>
/// <param name="Heading">
/// The same machine's heading, <c>obj+0x10</c> — the hull's, read raw. Not
/// <see cref="CockpitHudState.Heading"/>, which runs the power-up sweep.
/// </param>
/// <param name="Raster">
/// The mission's terrain raster, or null when there is none to draw. One bitmap for both maps: the
/// paint blits the same <c>HddMapTerrainRaster</c> (<c>004d1d7a</c>) the Heads-Down Display's command display does — see
/// <see cref="HddMapRaster"/>.
/// </param>
public readonly record struct MfdNavMapState(int CentreX, int CentreY, short Heading, HddMapRaster? Raster);

/// <summary>
/// The MFD's NAV MAP screen, mode 2 — <c>MfdMapScreen_Paint</c> (<c>004405e4</c>): the terrain
/// raster centred on the machine and turned heading-up, under a cross. See docs/retail/simulation/mfd.md,
/// "<c>MFDMap</c> — mode 2".
/// </summary>
public static class MfdNavMap {
	/// <summary>
	/// World units per device pixel, 8.8 fixed. Projected exactly as <see cref="HddMapView"/>
	/// projects, at this scale.
	/// </summary>
	public const int Scale = 200000;

	/// <summary>The centre cross's <c>COLORS.DAT</c> id, <c>DAT_004d3c20</c>.</summary>
	public const int CrossColorId = 16;

	/// <summary>How far each arm of the cross reaches from the centre, GAU units.</summary>
	public const int CrossArm = 1;

	/// <summary>What the screen draws this frame for <paramref name="viewer"/>.</summary>
	public static MfdNavMapState Build(SimObject viewer, HddMapRaster? raster) =>
		new(viewer.Position.X, viewer.Position.Y, unchecked((short)viewer.Heading), raster);

	/// <summary>
	/// A world point's position on the map before the heading turn, device pixels from the map's
	/// centre — y down, since world +y is up the screen.
	/// </summary>
	public static Vector2 Project(MfdNavMapState state, int worldX, int worldY) =>
		new((worldX - (long)state.CentreX) * (1 << HddMapView.ScaleShift) / (float)Scale,
			-(worldY - (long)state.CentreY) * (1 << HddMapView.ScaleShift) / (float)Scale);
}
