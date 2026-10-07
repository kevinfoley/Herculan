using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Render;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Scene;

/// <summary>
/// Everything about how a zone looks into the distance, taken from the zone and its theater rather
/// than chosen: how far the original draws, where inside that its fade begins, what colour it ends
/// on, and the banded sky above it.
///
/// <para>Both numbers come out of <c>Terrain_DrawCellQuad</c>, which per cell installs the
/// visibility range as <c>grid[+0x10c] &lt;&lt; grid[+0x108]</c> — the view radius in cells scaled
/// to world units (<see cref="HeightGrid.VisibilityRange"/>) — and then fades against it through
/// <c>Raster_SetDepthFadeFromDistance</c>, which is flat until half the range and saturated at it
/// (<see cref="ShadeRamp.DepthSliceFor"/>). The colour is the theater ramp's own far slice
/// (<see cref="ShadeRamp.FogColor"/>).</para>
///
/// <para>Retail zones run 344 m to 1376 m at the highest detail setting, and 6/14ths of that at the
/// lowest (see <see cref="TerrainDetail"/>) — a far shorter draw distance than the engine's
/// previous hand-picked 900 m/9000 m, which was a guess made before any of this was traced and left
/// distant terrain unfogged where the original has it fully washed out.</para>
///
/// <para>The engine spends this the original's own way, by reading the theater ramp at one of its
/// twelve depth slices — see <see cref="ShadeRamp"/> and <see cref="Render.PaletteRampTable"/>. The
/// two numbers here are what the slice is chosen against, and what the blend fallback for a surface
/// with no ramp row fades over.</para>
/// </summary>
/// <param name="FogStart">Render units (metres) at which the fade begins — half the visibility range.</param>
/// <param name="FogEnd">Render units at which it is total — the visibility range.</param>
/// <param name="FogColor">
/// What it fades to, or null when the theater's ramp or palette did not load, in which case the
/// renderer keeps its own fallback.
/// </param>
/// <param name="CellSize">
/// The zone's cell size in render units — the grain the terrain's own fog is measured at, since
/// <c>Terrain_DrawCellQuad</c> fogs a cell at a time and from its nearest corner. See
/// <see cref="Render.SceneRenderer.FogCellSize"/>.
/// </param>
/// <param name="Sky">
/// The theater's banded sky (<see cref="SkyGradient"/>), or null when its palette did not load. How
/// closely <paramref name="FogColor"/> matches the colour retail paints at the horizon, per theater,
/// is docs/retail/formats/distance-fog-and-sky.md's "Where the two meet".
/// </param>
public readonly record struct Atmosphere(float FogStart, float FogEnd, Vector3? FogColor,
		float CellSize, SkyGradient? Sky) {
	/// <summary>
	/// Reads all of it off the loaded zone and theater. <paramref name="shading"/> may be null (no
	/// ramp), which costs the colours but not the distances: those come from the grid alone.
	/// <paramref name="world"/> is the theater's <c>.WLD</c>, which says which palette entries the
	/// sky runs over and how its bands are laid out.
	/// </summary>
	public static Atmosphere From(HeightGrid terrain, SurfaceShading? shading, WorldData world) {
		long range = terrain.VisibilityRange;
		return new Atmosphere(
			WorldScale.DistanceToRender((int)(range / 2)),
			WorldScale.DistanceToRender((int)range),
			shading?.Ramp.FogColor(shading.Palette),
			WorldScale.DistanceToRender(terrain.CellSize),
			SkyGradient.From(world, shading?.Palette));
	}

	/// <summary>
	/// Applies this to a renderer. Also sets the flat <see cref="SceneRenderer.SkyColor"/> the frame
	/// is cleared to, so that whatever the gradient does not cover is at least the right hue.
	/// </summary>
	public void ApplyTo(SceneRenderer renderer) {
		renderer.FogStart = FogStart;
		renderer.FogEnd = FogEnd;
		renderer.FogCellSize = CellSize;
		if (FogColor is { } color) {
			renderer.FogColor = color;
		}

		renderer.Sky = Sky;
		if (Sky is { } sky) {
			renderer.SkyColor = sky.Zenith;
		}
	}

	/// <summary>
	/// Clips the camera at the same distance the fade ends, which is where the original stops
	/// drawing: <c>Terrain_BuildDrawRegionQuad</c> (<c>0046d220</c>) builds the terrain draw region
	/// as a square of half-width <c>grid[+0x10c] &lt;&lt; grid[+0x108]</c> around the viewer — the
	/// same <see cref="HeightGrid.VisibilityRange"/> the fog is measured against.
	///
	/// <para>The shapes differ: the original's region is a world-axis-aligned square, so along its
	/// diagonals it reaches a further 41%, while a far plane is uniform in every direction. That
	/// difference is invisible because the fade is already total at
	/// <see cref="FogEnd"/> — everything in the gap is drawn flat in the fog colour, which in seven
	/// theaters of ten is the colour the sky paints below the horizon where the terrain stops (see
	/// <see cref="SkyGradient"/>).
	/// So the far plane is set to the range itself rather than to its diagonal.</para>
	/// </summary>
	public void ApplyTo(Camera camera) {
		camera.FarPlane = FogEnd;
	}
}
