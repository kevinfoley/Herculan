using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// The one directional light every mission gets, and the shade byte it produces for a surface.
///
/// <para><c>Light_CreateMissionSun</c> (<c>00461240</c>) builds the sun once per mission from
/// constants compiled into DBSIM, with no ambient light beside it, so a face angled away from the sun
/// gets shade 0, not a floor (docs/retail/rendering/terrain-lighting.md#the-sun).</para>
///
/// <para>There are two shade calculations with different curves: <see cref="ShadeFor"/> for terrain
/// and <see cref="ShadeForFace"/> for shapes
/// (docs/retail/rendering/dts-texture-binding.md#two-shade-calculations--terrain-and-shapes-use-different-ones).</para>
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
	/// Reduces to <c>clamp(128 + 256 * facing, 0, 255)</c>, half as steep as <see cref="ShadeFor"/>'s
	/// curve with an edge-on face at 128 rather than 0.
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
