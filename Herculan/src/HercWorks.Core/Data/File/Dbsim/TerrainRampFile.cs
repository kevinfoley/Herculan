namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/RMP/ worldX.rmp — the theater's colour ramp, paired 1:1 by name with the
/// corresponding worldX.wld (see <see cref="WorldData"/>). Every flat-shaded face in DBSIM passes
/// its colour through this table before it reaches the framebuffer.
///   0 - INT32 - <see cref="ShadeLevels"/>, 32 in every retail file.
///   4 - INT32 - <see cref="DepthSlices"/>, 12 in every retail file.
///   8.. - <see cref="Rows"/>, DepthSlices * ShadeLevels rows of <see cref="RowLength"/> bytes
///     (98304 in every retail file, which is the whole of the rest).
///
/// <para>A row is 256 palette bytes — "this colour, at this brightness, at this distance" — so the
/// file is one colour ramp per palette index, sampled 32 ways for light and 12 ways for distance.
/// Hue is preserved throughout, which is what makes it a ramp rather than a remap. The consumer is
/// <c>Raster_ShadeRampRow</c> (<c>00468054</c>), whose whole body is the address arithmetic
/// <c>row = ((shade * (shadeLevels - 1) + depthBias) &amp; ~0xFF) + rampBase</c>, with
/// <c>depthBias</c> a whole number of 8192-byte depth slices set from the drawn object's range.
/// That is the original's distance fog. See docs/retail/rendering/distance-fog-and-sky.md.</para>
///
/// <para>The body's 256-byte period over 384 rows is the row length and 12 slices x 32 shades — a
/// shade table, not a 256 x 384 heightmap.</para>
/// </summary>
public class TerrainRampFile {
	/// <summary>Bytes in one row, one per palette index.</summary>
	public const int RowLength = 256;

	/// <summary>Brightness rows per depth slice — 32 in every retail file.</summary>
	public int ShadeLevels { get; set; } = 32;

	/// <summary>Distance slices — 12 in every retail file.</summary>
	public int DepthSlices { get; set; } = 12;

	/// <summary>
	/// The table itself, <see cref="DepthSlices"/> * <see cref="ShadeLevels"/> rows of
	/// <see cref="RowLength"/> bytes, slice-major. Row <c>0</c> is not black and the last row is not
	/// full brightness: measured against WORLD0's palette they land at 0.36x and 1.16x the source
	/// colour, so the ramp brightens as well as darkens and passes through unity around row 23.
	/// </summary>
	public byte[]? Rows { get; set; }
}
