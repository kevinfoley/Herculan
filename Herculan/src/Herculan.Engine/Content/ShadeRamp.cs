using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Engine.Content;

/// <summary>
/// The theater's colour ramp, <c>rmp\WORLD&lt;n&gt;.RMP</c> — the table every flat-shaded face in the
/// original passes its colour through before it reaches the framebuffer.
///
/// <para><b>Layout</b> — parsed by <see cref="TerrainRampFile"/> in HercWorks.Core, which carries
/// the format writeup. In short: <c>int32 shadeLevels</c>, <c>int32 depthSlices</c>, then
/// <c>depthSlices * shadeLevels * 256</c> bytes, reading 32 and 12 in every retail file. A row is
/// 256 palette bytes — "this colour, at this brightness" — so the whole file is one colour ramp per
/// palette index, sampled 32 ways for light and 12 ways for distance. Hue is preserved throughout,
/// which is what makes it a ramp rather than a remap.</para>
///
/// <para><b>The rows are not a 0..1 fade.</b> Row <c>0</c> lands at <b>0.36x</b> the source colour
/// and row <c>31</c> at <b>1.16x</b> — the ramp brightens as well as darkens, and passes through
/// unity around row 23, so the neutral row is not the top one. Measured in
/// docs/retail/rendering/terrain-lighting.md's "The ramp rows are not a 0..1 fade".
/// <see cref="Render.PaletteRampTable"/> is that table, expanded through the palette at load.</para>
///
/// <para>The consumer is <c>Raster_ShadeRampRow</c> (<c>00468054</c>), which is the whole of the address arithmetic:</para>
/// <code>
/// row = ((shade * (shadeLevels - 1) + depthBias) &amp; ~0xFF) + rampBase
/// </code>
/// <para>with <c>depthBias</c> a whole number of 8192-byte depth slices that
/// <c>Raster_SetDepthFadeFromDistance(distance)</c> sets from how far the object being drawn is —
/// zero for anything inside half the visibility range, climbing to the last slice at the far edge.
/// That is the original's distance fog. <see cref="DepthSliceFor"/> is that calculation and
/// <see cref="FogColor"/> is where it ends up.</para>
///
/// <para><b>Who gets faded:</b> terrain cells and every drawn object, projectiles included, each
/// from its own range. A flat solid face is <i>not</i> pinned to row 15 at distance. The three
/// callers and the trace behind that are docs/retail/rendering/distance-fog-and-sky.md's "What gets
/// faded".</para>
///
/// <para>The engine renders that fade the same way, as a ramp row: both
/// <see cref="Render.PaletteRampTable"/> and <see cref="Render.SurfaceRampTable"/> carry every slice
/// and the fragment shader picks one with <see cref="DepthSliceFor"/>'s own formula, so a fogged
/// pixel is still the exact byte the original would have written. It differs only in where the
/// distance is measured: per fragment here, per cell and per object there.</para>
///
/// <para>A <c>TSGouraudPoly</c> has no ramp row to bias and is not fogged, as in the original. A
/// surface in a theater whose ramp did not load falls back to <see cref="Render.SceneRenderer"/>'s
/// blend toward <see cref="FogColor"/> over the same interval, which is a poorer likeness than it
/// looks — why is in docs/retail/rendering/distance-fog-and-sky.md's "Rejected readings".</para>
/// </summary>
public sealed class ShadeRamp {
	/// <summary>The folder the theater loader opens this from.</summary>
	public const string ResourceFolder = "rmp";

	/// <summary>
	/// The shade a flat, unlit face is drawn at — <c>TSSolidPoly_Render</c> (<c>00474db4</c>) passes
	/// <c>Raster_ShadeRampRow</c> (<c>00468054</c>) a literal <c>0x80</c> and never computes a light term at all. With the
	/// retail 32 levels that lands on row 15, a little under half brightness.
	/// </summary>
	public const int UnlitShade = 0x80;

	private readonly byte[] _rows;

	private ShadeRamp(int shadeLevels, int depthSlices, byte[] rows) {
		ShadeLevels = shadeLevels;
		DepthSlices = depthSlices;
		_rows = rows;
	}

	/// <summary>How many brightness rows the ramp has — 32 in every retail file.</summary>
	public int ShadeLevels { get; }

	/// <summary>How many distance slices it has — 12 in every retail file.</summary>
	public int DepthSlices { get; }

	/// <summary>Bytes in one row, one per palette index.</summary>
	public const int RowLength = 256;

	/// <summary>
	/// Loads <c>rmp\&lt;name&gt;.RMP</c>. Returns null when the resource is missing or too short for
	/// the size its own header states, in which case flat-shaded faces keep the fallback colouring.
	/// </summary>
	/// <param name="content">Mounted archives.</param>
	/// <param name="name">The theater's base name, the same one its <c>.DPL</c> uses — e.g. <c>WORLD2</c>.</param>
	public static ShadeRamp? Load(GameContent content, string? name) {
		if (string.IsNullOrWhiteSpace(name) || content.Read(ResourceFolder, name + ".RMP") is not { } bytes
				|| new TerrainRampFileTransformer().Parse(bytes) is not TerrainRampFile rmp
				|| rmp.Rows == null || rmp.ShadeLevels <= 0 || rmp.DepthSlices <= 0) {
			return null;
		}

		if ((long)rmp.Rows.Length < (long)rmp.ShadeLevels * rmp.DepthSlices * RowLength) {
			return null;
		}

		return new ShadeRamp(rmp.ShadeLevels, rmp.DepthSlices, rmp.Rows);
	}

	/// <summary>
	/// Which row <c>Raster_ShadeRampRow</c> (<c>00468054</c>) selects for a given shade byte: <c>(shade * (levels - 1)) / 256</c>,
	/// clamped to the table. The truncation is the original's own <c>&amp; ~0xFF</c>.
	/// </summary>
	public int RowFor(int shade) =>
		Math.Clamp(shade * (ShadeLevels - 1) / RowLength, 0, ShadeLevels - 1);

	/// <summary>
	/// The palette byte a colour index resolves to at one shade — the ramp lookup itself.
	/// </summary>
	/// <param name="paletteIndex">The surface's own colour index, 0-255.</param>
	/// <param name="shade">A shade byte, 0-255; <see cref="UnlitShade"/> for a flat solid face.</param>
	public byte Lookup(int paletteIndex, int shade) => Lookup(paletteIndex, shade, 0);

	/// <summary>
	/// The same lookup at one of the ramp's depth slices — the original's distance fog, which
	/// <c>Raster_ShadeRampRow</c> applies by adding whole slices to the row offset.
	/// </summary>
	/// <param name="depthSlice">0 for unfogged, up to <see cref="DepthSlices"/> - 1 at the far edge;
	/// <see cref="DepthSliceFor"/> works it out from a distance.</param>
	public byte Lookup(int paletteIndex, int shade, int depthSlice) {
		int slice = Math.Clamp(depthSlice, 0, DepthSlices - 1);
		return _rows[(slice * ShadeLevels + RowFor(shade)) * RowLength + (paletteIndex & 0xff)];
	}

	/// <summary>
	/// The same lookup addressed by row number rather than by shade byte, for callers that walk the
	/// whole ramp instead of resolving one surface — see <see cref="Render.PaletteRampTable"/>.
	/// </summary>
	public byte AtRow(int paletteIndex, int row, int depthSlice = 0) {
		int slice = Math.Clamp(depthSlice, 0, DepthSlices - 1);
		int clampedRow = Math.Clamp(row, 0, ShadeLevels - 1);
		return _rows[(slice * ShadeLevels + clampedRow) * RowLength + (paletteIndex & 0xff)];
	}

	/// <summary>
	/// Which depth slice something at <paramref name="distance"/> is drawn in — a direct port of
	/// <c>Raster_SetDepthFadeFromDistance</c> (<c>00467fec</c>), whose result it expresses as a slice
	/// number rather than the byte offset the original carries around.
	///
	/// <code>
	/// if (d >= range)     d = range;
	/// if (d &lt;= range/2)   return 0;
	/// t = min((d - range/2) * 2 / range, 1)      // Q16
	/// return t * (depthSlices - 1)               // Q16
	/// </code>
	///
	/// <para>So nothing inside half the visibility range is fogged at all, and the fade runs over the
	/// outer half only.</para>
	/// </summary>
	/// <param name="distance">Distance from the view, in world units.</param>
	/// <param name="visibilityRange">The far limit, in world units — see
	/// <see cref="Terrain.HeightGrid.VisibilityRange"/>.</param>
	public int DepthSliceFor(long distance, long visibilityRange) {
		if (visibilityRange <= 0) {
			return 0;
		}

		long clamped = Math.Min(distance, visibilityRange);
		long half = visibilityRange / 2;
		if (clamped <= half) {
			return 0;
		}

		double t = Math.Min((double)(clamped - half) * 2 / visibilityRange, 1);
		return Math.Clamp((int)(t * (DepthSlices - 1)), 0, DepthSlices - 1);
	}

	/// <summary>
	/// The colour everything the theater draws converges to at the far edge of visibility: the
	/// commonest output of the last depth slice at <see cref="UnlitShade"/>, expanded through the
	/// palette.
	///
	/// <para>Taking the commonest rather than any one index is what makes this a fog colour and not a
	/// sample: by the last slice the ramp has collapsed almost the whole palette onto two to four
	/// distinct bytes, one of them covering the large majority.</para>
	///
	/// <para>How it compares, per theater, with the colour retail's sky paints at the horizon is
	/// docs/retail/rendering/distance-fog-and-sky.md's "Where the two meet".</para>
	/// </summary>
	public Vector3? FogColor(DynamixPalette? palette) {
		if (palette == null) {
			return null;
		}

		Span<int> counts = stackalloc int[RowLength];
		for (int index = 0; index < RowLength; index++) {
			counts[Lookup(index, UnlitShade, DepthSlices - 1)]++;
		}

		int best = 0;
		for (int candidate = 1; candidate < RowLength; candidate++) {
			if (counts[candidate] > counts[best]) {
				best = candidate;
			}
		}

		if (!palette.Colors.TryGetValue(best, out var entry)) {
			return null;
		}

		var color = entry.GetColor();
		return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
	}

	/// <summary>
	/// One flat surface colour as the engine wants it: the ramp lookup expanded through the theater's
	/// own palette. Returns null when the palette has no entry for the resolved byte.
	/// </summary>
	/// <param name="depthSlice">Which depth slice to read — see <see cref="DepthSliceFor"/>.</param>
	public Vector3? Resolve(int paletteIndex, int shade, DynamixPalette? palette, int depthSlice = 0) {
		byte resolved = Lookup(paletteIndex, shade, depthSlice);
		if (palette == null || !palette.Colors.TryGetValue(resolved, out var entry)) {
			return null;
		}

		var color = entry.GetColor();
		return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
	}
}
