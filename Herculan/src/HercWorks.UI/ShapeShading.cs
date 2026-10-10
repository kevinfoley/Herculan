using System.Drawing;
using System.Numerics;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;

namespace HercWorks.UI;

/// <summary>
/// How a DTS poly becomes a colour in the model viewer: the theater's <c>.DPL</c> palette and its
/// <c>.RMP</c> ramp, and the one mission sun. A port of the engine's <c>Render.MissionSun</c>,
/// <c>Content.ShadeRamp</c> and <c>Render.SurfaceShading</c> — the toolkit does not reference the
/// engine (docs/herculan/planning.md), so the few pure functions it needs are repeated here. The
/// mechanisms and their RE are docs/retail/rendering/dts-texture-binding.md's "Poly types and their colour
/// mechanisms".
///
/// <para>Every method returns null when what it needs is not loaded, and the caller falls back to
/// its placeholder rather than to a guessed colour.</para>
/// </summary>
public sealed class ShapeShading {
	/// <summary>The fixed shade <c>TSSolidPoly_Render</c> and <c>TSShadedPoly_Render</c> pass <c>Raster_ShadeRampRow</c>.</summary>
	public const int UnlitShade = 0x80;

	private const int RowLength = 256;
	private const int Intensity = 0x100;
	private const int NormalLength = 0x800;
	private const int DirectionLength = 0x1000;
	private const int FaceShadeBias = 0x400000;

	private readonly DynamixPalette _palette;
	private readonly TerrainRampFile? _ramp;

	public ShapeShading(DynamixPalette palette, TerrainRampFile? ramp) {
		_palette = palette;
		_ramp = ramp is { Rows: { } rows } r
			&& r.ShadeLevels > 0 && r.DepthSlices > 0
			&& rows.Length >= r.ShadeLevels * r.DepthSlices * RowLength
				? r
				: null;

		// A palette slot with no entry draws mid grey, as the engine's SurfaceRampTable palette row does.
		BandPalette = new int[RowLength];
		for (int index = 0; index < RowLength; index++) {
			BandPalette[index] = (PaletteColor(index) ?? Color.FromArgb(255, 128, 128, 128)).ToArgb();
		}
	}

	/// <summary>
	/// The palette as ARGB, indexed by palette byte: what a <c>TSGouraudPoly</c>'s contour bands are
	/// drawn in, each band being a whole palette index between its corners' ramp entries.
	/// </summary>
	public int[] BandPalette { get; }

	/// <summary>Whether a usable <c>.RMP</c> is loaded; without one only the Gouraud chain resolves.</summary>
	public bool HasRamp => _ramp != null;

	/// <summary>
	/// The direction the sun's light travels in the viewer's render space (<c>(x, z, -y)</c> of the
	/// sim's Z-up world), built as <c>Light_CreateMissionSun</c> (<c>00461240</c>) builds it: the
	/// world Y axis turned by Euler angles (-6000, 0, 21000), Z composed after X. The model is shown
	/// at heading 0, so its own axes are the world's.
	/// </summary>
	public static Vector3 SunDirection { get; } = ComputeSunDirection();

	private static Vector3 ComputeSunDirection() {
		const float RadiansPerRawUnit = MathF.PI * 2f / 65536f;
		var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -6000f * RadiansPerRawUnit);
		var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 21000f * RadiansPerRawUnit);
		Vector3 world = Vector3.Transform(Vector3.UnitY, qz * qx);
		return Vector3.Normalize(new Vector3(world.X, world.Z, -world.Y));
	}

	/// <summary>
	/// <c>Light_ComputeShadeForFace</c> (<c>0048bedc</c>) for the sun alone — <c>clamp(128 + 256 *
	/// facing, 0, 255)</c>, in the original's integer form.
	/// </summary>
	/// <param name="renderNormal">The face's normal, already turned toward the eye.</param>
	public static int ShadeForFace(Vector3 renderNormal) {
		if (renderNormal.LengthSquared() <= 1e-12f) {
			return 0;
		}

		float facing = -Vector3.Dot(Vector3.Normalize(renderNormal), SunDirection);
		long dot = (long)(facing * NormalLength * DirectionLength);
		long biased = (-dot - FaceShadeBias) >> 1;
		return biased >= 0 ? 0 : (int)Math.Clamp(-((Intensity * biased) >> 22), 0, 255);
	}

	/// <summary>
	/// Which <c>.RMP</c> row <c>Raster_ShadeRampRow</c> (<c>00468054</c>) picks for a shade —
	/// <c>(shade * (levels - 1)) / 256</c>, unfogged. Null without a ramp.
	/// </summary>
	public int? RampRow(int shade) =>
		_ramp is { } ramp ? Math.Clamp(shade * (ramp.ShadeLevels - 1) / RowLength, 0, ramp.ShadeLevels - 1) : null;

	private byte? AtRow(int paletteIndex, int row) =>
		_ramp?.Rows is { } rows ? rows[row * RowLength + (paletteIndex & 0xff)] : null;

	/// <summary>The <c>.RMP</c> byte for a palette index at a shade.</summary>
	private byte? RampLookup(int paletteIndex, int shade) =>
		RampRow(shade) is { } row ? AtRow(paletteIndex, row) : null;

	private Color? PaletteColor(int index) =>
		_palette.Colors.TryGetValue(index & 0xff, out var entry)
			? Color.FromArgb(255, entry.GetColor().R, entry.GetColor().G, entry.GetColor().B)
			: null;

	/// <summary>A plain <c>TSSolidPoly</c> value: a palette index through the ramp at the fixed unlit shade.</summary>
	public Color? Solid(int paletteIndex) =>
		RampLookup(paletteIndex, UnlitShade) is { } ramped ? PaletteColor(ramped) : null;

	/// <summary>Whether two solid values ramp to the same byte, in which case no outline is drawn.</summary>
	public bool SolidSame(int a, int b) => RampLookup(a, UnlitShade) == RampLookup(b, UnlitShade);

	/// <summary>
	/// <c>Palette_ShadeRampLookup</c> (<c>00430e34</c>): which palette index a material ramp reaches at
	/// a shade, entry <c>(shade * length) &gt;&gt; 8</c>. A ramp number past the table falls back to
	/// ramp 0, as the original's does. A <c>TSGouraudPoly</c> corner's palette index.
	/// </summary>
	public int? RampedPaletteIndex(int rampNumber, int shade) {
		if (_palette.ShadeRamps is not { Count: > 0 } ramps) {
			return null;
		}

		int index = rampNumber & 0xff;
		if (index >= ramps.Count) {
			index = 0;
		}

		var ramp = ramps[index];
		if (ramp is not { Length: > 0 }) {
			return null;
		}

		int position = Math.Min(Math.Clamp(shade, 0, 255) * ramp.Length / 256, ramp.Length - 1);
		return ramp[position];
	}

	/// <summary>A <c>TSShadedPoly</c>: the ramp's entry at the face's shade, then the <c>.RMP</c> at the fixed row.</summary>
	public Color? Shaded(int rampNumber, int shade) =>
		RampedPaletteIndex(rampNumber, shade) is { } index && RampLookup(index, UnlitShade) is { } ramped
			? PaletteColor(ramped)
			: null;

	/// <summary>
	/// A <c>TSTexture4Poly</c> texel: <c>Raster_ShadeRampRow(shade)[texel]</c>, the row coming from
	/// the face's shade through <see cref="RampRow"/> and the column from the texel.
	/// </summary>
	public Color? TexelAtRow(byte paletteIndex, int row) =>
		AtRow(paletteIndex, row) is { } ramped ? PaletteColor(ramped) : null;

	/// <summary>The palette itself, for an unlit decode.</summary>
	public Color? Unlit(byte paletteIndex) => PaletteColor(paletteIndex);
}
