using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Engine.Content;

/// <summary>
/// The theater's sky backdrop — retail's <c>hzline</c>, the flat palette fills
/// <c>Hzline_Draw</c> (<c>0042ebe8</c>) paints behind the terrain every frame: a run of bands from
/// the horizon colour up to the zenith colour above a projected horizon line, and the horizon colour
/// over everything below it. The draw, the fields and the rolled-view geometry are in
/// docs/retail/rendering/distance-fog-and-sky.md, "The sky — hzline".
///
/// <para>The renderer evaluates it per pixel rather than filling rects and quads, from
/// <see cref="Place"/>'s line and <see cref="BandAt"/>'s rule, which give the same band at every
/// retail pixel. Measurements are in the original's 640x480-mode pixels, scaled to the window by
/// the view's focal length, as the rest of the projection is.</para>
///
/// <para>The line here is unbounded where retail's has ends; retail's spans every retail view at
/// any roll and pitch (docs/retail/rendering/distance-fog-and-sky.md, "The horizon line"), so the
/// two draw the same picture.</para>
/// </summary>
public sealed class SkyGradient {
	/// <summary>
	/// The most bands the sky shader carries. Retail theaters use 15 or 16; a file asking for more
	/// is clamped to this rather than overrunning the uniform array.
	/// </summary>
	public const int MaxBands = 32;

	/// <summary>The original's focal length in its own pixels — <see cref="Render.Camera.FocalLengthPixels"/>.</summary>
	private const float RetailFocalPixels = Render.Camera.FocalLengthPixels;

	private SkyGradient(Vector3[] bands, int bandHeight, int horizonGap, int lineOffset) {
		Bands = bands;
		BandHeight = bandHeight;
		HorizonGap = horizonGap;
		LineOffset = lineOffset;
	}

	/// <summary>
	/// The band colours outward from the horizon: <c>[0]</c> the horizon colour
	/// (<see cref="WorldData.HorizonColor"/>), each next one palette entry lower, and the last the
	/// zenith colour (<see cref="WorldData.ZenithColor"/>).
	/// </summary>
	public Vector3[] Bands { get; }

	/// <inheritdoc cref="WorldData.SkyBandHeight"/>
	public int BandHeight { get; }

	/// <summary>
	/// <see cref="WorldData.HorizonGap"/> halved, as <c>Hzline_FillSky</c> uses it: the rows between
	/// the line and the first band past the horizon colour.
	/// </summary>
	public int HorizonGap { get; }

	/// <summary>
	/// How far below the projected horizon the line is drawn, in rows: <see cref="WorldData.HorizonOffset"/>
	/// plus half of it again, as <c>Hzline_BuildHorizon</c> and <c>Hzline_DrawWithOffset</c> apply it.
	/// </summary>
	public int LineOffset { get; }

	/// <summary>The zenith colour, and what the frame is cleared to.</summary>
	public Vector3 Zenith => Bands[^1];

	/// <summary>The horizon colour, which also fills everything below the line.</summary>
	public Vector3 Horizon => Bands[0];

	/// <summary>
	/// Builds the sky from a theater's <c>.WLD</c> and palette. Returns null when the palette is
	/// missing, does not carry every entry the run names, or the file's band fields are unusable, in
	/// which case the renderer falls back to a flat sky.
	/// </summary>
	public static SkyGradient? From(WorldData world, DynamixPalette? palette) {
		int count = Math.Min((int)world.SkyBandCount, MaxBands);
		if (palette == null || count < 1 || world.SkyBandHeight < 1) {
			return null;
		}

		var bands = new Vector3[count];
		for (int band = 0; band < count; band++) {
			if (!palette.Colors.TryGetValue(world.HorizonColor - band, out var entry)) {
				return null;
			}

			var color = entry.GetColor();
			bands[band] = new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
		}

		int offset = world.HorizonOffset;
		return new SkyGradient(bands, world.SkyBandHeight, world.HorizonGap >> 1, offset + (offset >> 1));
	}

	/// <summary>
	/// Where the line falls for a view, in window coordinates (y up), as <c>Hzline_BuildHorizon</c>
	/// (<c>0042ec68</c>) places it: the projection centre moved <c>f·tan(pitch)</c> along the rolled
	/// screen vertical, then <see cref="LineOffset"/> rows down.
	/// </summary>
	/// <param name="pitch">The view's pitch, <c>view+0x10</c>, as a binary angle.</param>
	/// <param name="roll">The view's roll, <c>view+0x12</c>, as a binary angle.</param>
	/// <param name="centre">The projection centre in window coordinates, y up.</param>
	/// <param name="focalPixels">The view's focal length in window pixels.</param>
	public HorizonPlacement Place(short pitch, short roll, Vector2 centre, float focalPixels) {
		float scale = focalPixels / RetailFocalPixels;
		float pitchRadians = pitch * (MathF.PI / 32768f);
		float rollRadians = roll * (MathF.PI / 32768f);
		float sinRoll = MathF.Sin(rollRadians);
		float cosRoll = MathF.Cos(rollRadians);

		// The original divides by the Q14 cosine with a zero floored to 1.
		float cosPitch = MathF.Cos(pitchRadians);
		if (MathF.Abs(cosPitch) < 1f / 16384f) {
			cosPitch = 1f / 16384f;
		}

		float lift = focalPixels * MathF.Sin(pitchRadians) / cosPitch;

		// Screen-down offset (sin r, cos r) * lift, turned into window y-up; then the row offset.
		var mid = new Vector2(centre.X + lift * sinRoll, centre.Y - lift * cosRoll - LineOffset * scale);
		return new HorizonPlacement(mid, new Vector2(-sinRoll, cosRoll), scale, roll != 0, cosRoll);
	}

	/// <summary>
	/// Which entry of <see cref="Bands"/> a pixel takes, from its distance above the line in the
	/// original's pixels, measured along <see cref="HorizonPlacement.Up"/>.
	///
	/// <para><b>Level</b>, <c>Hzline_FillSky</c>'s <c>Raster_FillRect</c> rects: band <c>i</c> spans
	/// rows <c>t + (i - 1)·h</c> to <c>t + i·h - 1</c> above the line's row, each band's top row being
	/// painted over by the next.</para>
	///
	/// <para><b>Rolled</b>, its quads: band <c>i</c>'s far edge stands <c>h·i + (t - h)·cos²r</c> from
	/// the line, the vertical term counting one band fewer than the level fill's. Then
	/// <c>Hzline_FillGround</c> fills the side of the line away from the sky, the line moved one row
	/// down, in the horizon colour.</para>
	///
	/// The same rule runs in Sky.glsl; this copy exists so it can be tested.
	/// </summary>
	public int BandAt(float distanceAbove, bool rolled, float cosRoll) {
		int last = Bands.Length - 1;
		float t = HorizonGap;
		float h = BandHeight;
		if (!rolled) {
			return Math.Clamp((int)MathF.Floor((distanceAbove + 0.5f - t) / h) + 1, 0, last);
		}

		// The ground fill's edge is the line moved one row straight down, which is -cos r along Up,
		// and it covers the side away from the sky — screen-below upright, screen-above inverted.
		if (distanceAbove < -cosRoll) {
			return 0;
		}

		return Math.Clamp((int)MathF.Ceiling((distanceAbove - (t - h) * cosRoll * cosRoll) / h), 0, last);
	}
}

/// <summary>
/// One frame's horizon line, from <see cref="SkyGradient.Place"/>.
/// </summary>
/// <param name="Mid">A point on the line, in window coordinates, y up.</param>
/// <param name="Up">Unit normal of the line toward the sky, in window coordinates.</param>
/// <param name="Scale">Window pixels per original pixel.</param>
/// <param name="Rolled">Whether the roll is non-zero, which switches <c>Hzline_FillSky</c> from rects to quads.</param>
/// <param name="CosRoll">The roll's cosine.</param>
public readonly record struct HorizonPlacement(Vector2 Mid, Vector2 Up, float Scale, bool Rolled, float CosRoll);
