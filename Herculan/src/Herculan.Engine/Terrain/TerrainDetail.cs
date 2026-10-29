using Herculan.Engine.Content;
using HercWorks.Core.Data.File.Cfg;

namespace Herculan.Engine.Terrain;

/// <summary>
/// The simulator's terrain-detail setting, which is the whole of how far the game draws.
///
/// <para><c>Terrain_SetupVisibleRegion</c> (<c>0046ca98</c>) sets the grid's draw radius every frame
/// as <c>grid[+0x10c] = (short)DAT_004a0bcc[DAT_004d1fc3]</c> — a three-entry table indexed by the
/// setting — and then <c>&gt;&gt;= (cellSizeShift - 14)</c> when the cell size shift is above 14, which only
/// ever applies to the two shift-15 zones. Everything downstream is that one number:
/// <c>Terrain_BuildDrawRegionQuad</c> draws a square of <c>radius &lt;&lt; cellSizeShift</c> world units
/// around the viewer, and <c>Terrain_DrawCellQuad</c> installs the same distance as the visibility
/// range the fog is measured against (<see cref="HeightGrid.VisibilityRange"/>). So the setting moves
/// the far edge of the world and the fog together, as one.</para>
///
/// <para>The original re-reads the setting every frame; this reads it once at zone load, which is
/// equivalent while nothing changes it mid-mission.</para>
/// </summary>
public static class TerrainDetail {
	/// <summary>
	/// The table at <c>DAT_004a0bcc</c>, in cells — the draw radius each of the option's three
	/// settings selects. Its neighbour at <c>DAT_0049e2da</c> maps the same three onto the option
	/// panel's labels (<c>PreferencesPanel_RefreshRow</c> (<c>004571f4</c>) case 4), which is the second source for there being
	/// exactly three.
	/// </summary>
	public static readonly int[] RadiusInCells = { 6, 10, 14 };

	/// <summary>
	/// The setting used when none can be read. The highest, which is what the retail install this was
	/// measured against is set to — and the one whose draw distance the reference captures show.
	/// </summary>
	public const int DefaultLevel = 2;

	/// <summary>
	/// The draw radius one setting selects, in cells, with the shift-15 correction applied — the
	/// whole of <c>Terrain_SetupVisibleRegion</c>'s write.
	/// </summary>
	public static int RadiusFor(int level, int cellSizeShift) {
		int radius = RadiusInCells[Math.Clamp(level, 0, RadiusInCells.Length - 1)];
		return cellSizeShift > 14 ? radius >> (cellSizeShift - 14) : radius;
	}

	/// <summary>
	/// The setting the install at <paramref name="dataDirectory"/> is on, or
	/// <see cref="DefaultLevel"/> when there is no readable preferences file there. Reading it rather
	/// than picking one keeps the engine's draw distance in step with the retail install beside it,
	/// which is what a side-by-side capture is comparing against.
	///
	/// <para>The file itself is <see cref="SimulatorPreferences"/>, which owns the layout and the
	/// index this setting sits at; here it is one of nine options the [F12] panel shows.</para>
	/// </summary>
	/// <param name="dataDirectory">The game's <c>data</c> folder — where its <c>script.dat</c> is.</param>
	public static int LevelFrom(string? dataDirectory) =>
		SimulatorPreferences.Load(dataDirectory) is { } prefs
			? Math.Clamp(prefs[Prefs.TerrainDistanceOption], 0, RadiusInCells.Length - 1)
			: DefaultLevel;
}
