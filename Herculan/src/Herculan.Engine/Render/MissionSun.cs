using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// The one directional light every mission gets, and the shade byte it produces for a surface.
///
/// <para><c>Light_CreateMissionSun</c> (<c>00461240</c>) runs once per mission, unconditionally, and
/// builds the sun from constants compiled into DBSIM: direction
/// <c>rotate((0,0x1000,0), eulerMatrix(-6000,0,21000))</c> in the sim's Z-up world space, intensity
/// <c>0x100</c>. No mission or theater file contributes to it, and no ambient light is created
/// anywhere in the binary — a face angled away from the sun gets shade 0, not a floor. The
/// derivation is docs/retail/rendering/terrain-lighting.md's "The sun".</para>
///
/// <para><b>There are two shade calculations, not one</b>, and they are different curves. Both walk
/// the active light list; both reduce, for the one directional light, to a function of
/// <c>facing = -cos</c> between the surface normal and the direction the light travels (positive for
/// a surface turned toward it). Surface normals carry length <c>0x800</c> and the sun's direction
/// vector <c>0x1000</c>, so the raw dot is <c>0x800000 * cos</c>.</para>
///
/// <list type="bullet">
/// <item><b><c>Light_ComputeShadeForNormal</c> (<c>0048c060</c>)</b> — what <c>Terrain_BuildSurface</c> bakes a terrain cell with.
/// <see cref="ShadeFor"/>.
/// <code>
/// t = dot;
/// if (t &lt; 0) shade -= (intensity * t) &gt;&gt; 22;      // = 512 * facing
/// </code></item>
/// <item><b><c>Light_ComputeShadeForFace</c> (<c>0048bedc</c>)</b> — what every poly renderer of a
/// <i>shape</i> calls, <c>TSShadedPoly_Render</c> and <c>TSTexture4Poly_Render</c> alike.
/// <see cref="ShadeForFace"/>.
/// <code>
/// t = (dot - 0x400000) &gt;&gt; 1;
/// if (t &lt; 0) shade -= (intensity * t) &gt;&gt; 22;      // = 128 + 256 * facing
/// </code></item>
/// </list>
///
/// <para>The <c>- 0x400000</c> bias and <c>&gt;&gt; 1</c> are the only difference: a shape's falloff
/// is half as steep, an edge-on face sits at 128 rather than 0, and shade reaches 0 at 120 degrees
/// off the light rather than 90. Both saturate near facing 0.5, so flat ground (facing 0.544) is
/// pinned at 255 either way. The side-by-side comparison is
/// docs/retail/rendering/dts-texture-binding.md's "Two shade calculations — terrain and shapes use different
/// ones".</para>
/// </summary>
public static class MissionSun {
	/// <summary>The sun's intensity, the literal <c>0x100</c> the light's setter is called with.</summary>
	public const int Intensity = 0x100;

	/// <summary>The length surface normals are normalised to before the dot product.</summary>
	public const int NormalLength = 0x800;

	/// <summary>The length the sun's direction vector carries.</summary>
	public const int DirectionLength = 0x1000;

	/// <summary>The largest shade byte the calculation can return.</summary>
	public const int MaxShade = 255;

	/// <summary>
	/// Direction the sun's light travels, in render space, as a unit vector.
	///
	/// <para><c>Light_CreateMissionSun</c> (<c>00461240</c>) builds it as
	/// <c>RotateVectorByMatrixQ14((0, 0x1000, 0), BuildEulerRotationMatrixQ14(-6000, 0, 21000))</c>,
	/// which works out to <c>(±0.758, -0.359, -0.544)</c> in the sim's Z-up world — horizontal
	/// component 0.839, vertical 0.544. Z is composed after X, not before; getting that backwards
	/// leaves every flat cell facing away from the sun. See docs/retail/rendering/terrain-lighting.md's
	/// "The sun".</para>
	/// </summary>
	public static Vector3 Direction { get; } = ComputeDirection();

	private static Vector3 ComputeDirection() {
		// The sim's angle unit is a full circle per 0x10000, per BuildEulerRotationMatrixQ14.
		const float RadiansPerRawUnit = MathF.PI * 2f / 65536f;
		float xRadians = -6000f * RadiansPerRawUnit;
		float zRadians = 21000f * RadiansPerRawUnit;

		var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, xRadians);
		var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, zRadians);
		Vector3 worldDirection = Vector3.Transform(new Vector3(0f, 1f, 0f), qz * qx);

		// DBSIM world is Z-up with X/Y the ground plane; render space is Y-up — same mapping as
		// WorldScale.ToRender, which is a rotation, so the dot product below is unaffected by
		// computing it here rather than in world space.
		Vector3 renderDirection = new(worldDirection.X, worldDirection.Z, -worldDirection.Y);
		return Vector3.Normalize(renderDirection);
	}

	/// <summary>
	/// The shade byte <c>Light_ComputeShadeForNormal</c> (<c>0048c060</c>) returns for a surface with this normal, 0-255 — the
	/// <b>terrain</b> curve. See <see cref="ShadeForFace"/> for shape geometry, which is a different
	/// one.
	/// </summary>
	/// <param name="renderNormal">The surface normal in render space; need not be unit length.</param>
	public static int ShadeFor(Vector3 renderNormal) {
		if (Facing(renderNormal) is not { } facing || facing <= 0f) {
			return 0;
		}

		long dot = (long)(facing * NormalLength * DirectionLength);
		return (int)Math.Min((Intensity * dot) >> 22, MaxShade);
	}

	/// <summary>
	/// The shade byte <c>Light_ComputeShadeForFace</c> (<c>0048bedc</c>) returns for one face of a
	/// shape, 0-255 — what <c>TSShadedPoly_Render</c> and <c>TSTexture4Poly_Render</c> light with.
	/// Reduces to <c>clamp(128 + 256 * facing, 0, 255)</c>; see this type's doc comment for the
	/// derivation and for why it is not the same as <see cref="ShadeFor"/>.
	/// </summary>
	/// <param name="renderNormal">
	/// The face normal in render space, already turned to meet the eye — the original runs
	/// <c>TSPoly_FrontBackVisibilityTest</c> and negates the normal before it gets here.
	/// </param>
	public static int ShadeForFace(Vector3 renderNormal) {
		if (Facing(renderNormal) is not { } facing) {
			return 0;
		}

		// Kept in the original's integer form rather than folded to 128 + 256f, so the truncation
		// lands where the original's does.
		long dot = (long)(facing * NormalLength * DirectionLength);
		long biased = (-dot - FaceShadeBias) >> 1;
		if (biased >= 0) {
			return 0;
		}

		return (int)Math.Clamp(-((Intensity * biased) >> 22), 0, MaxShade);
	}

	/// <summary>
	/// The <c>0x400000</c> <c>Light_ComputeShadeForFace</c> subtracts from the dot product before
	/// halving it — half the dot's own full-scale magnitude, which is what puts the zero crossing at
	/// <c>facing -0.5</c> instead of at <c>0</c>.
	/// </summary>
	public const int FaceShadeBias = 0x400000;

	/// <summary>
	/// How far a surface is turned toward the light: <c>-cos</c> between the normal and the
	/// direction the light travels, so positive means lit. Null for a degenerate normal.
	/// </summary>
	private static float? Facing(Vector3 renderNormal) =>
		renderNormal.LengthSquared() > 1e-12f
			? -Vector3.Dot(Vector3.Normalize(renderNormal), Direction)
			: null;
}
