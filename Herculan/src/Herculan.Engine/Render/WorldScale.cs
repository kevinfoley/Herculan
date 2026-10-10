using System.Numerics;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// The one place where the simulation's integer world units become floating-point render units,
/// and where the original's Z-up axes become the renderer's Y-up ones. Simulation code never does
/// either conversion: per docs/herculan/planning.md's math decision, anything that feeds back into
/// simulation state stays in the original's fixed-point domain, so the float boundary is here and
/// nowhere else.
/// </summary>
public static class WorldScale {
	/// <summary>
	/// How many DBSIM world units make up one metre of rendered space: 1000 world units are 6 metres,
	/// so a world unit is 6 mm. This is the original's own constant, recovered from
	/// <c>Hud_WorldUnitsToMetres</c> (<c>00434228</c>) — see
	/// docs/retail/simulation/dbsim-physics-notes.md#world-units for the derivation and its corroborations.
	///
	/// <para>The original's own <i>displayed</i> distance is coarser than this constant in two ways
	/// that matter to a HUD but not to the scale factor: the integer divide quantises it to multiples
	/// of 6 m, and the distance itself is the octagonal <c>max + min/2</c> approximation
	/// (<c>Math_FastMagnitude2D</c>), which overshoots a true diagonal by up to ~12%.</para>
	/// </summary>
	public const float WorldUnitsPerMeter = 1000f / 6f;

	/// <summary>
	/// How many world units one raw DTS model unit spans — one, i.e. model coordinates are world
	/// coordinates with no conversion at all. Confirmed from game data; see
	/// docs/retail/simulation/dbsim-physics-notes.md#world-units for the evidence.
	///
	/// <para>Note this differs from the WinForms model viewer, which scales DTS points by 1/10 —
	/// that viewer picks whatever scale frames a model nicely in its own window and has no world to
	/// be consistent with.</para>
	/// </summary>
	public const float WorldUnitsPerDtsUnit = 1f;

	/// <summary>
	/// Converts a simulation position to render space. DBSIM is Z-up with X/Y as the ground plane;
	/// OpenGL here is Y-up. The mapping <c>(x, y, z) -> (x, z, -y)</c> is a rotation with
	/// determinant +1, not an axis swap — a bare swap would be a mirror reflection and would flip
	/// every triangle's winding.
	/// </summary>
	public static Vector3 ToRender(Vec3i world) =>
		new(world.X / WorldUnitsPerMeter, world.Z / WorldUnitsPerMeter, -world.Y / WorldUnitsPerMeter);

	/// <summary>Same mapping as <see cref="ToRender(Vec3i)"/>, for values already carrying a fractional part.</summary>
	public static Vector3 ToRender(float worldX, float worldY, float worldZ) =>
		new(worldX / WorldUnitsPerMeter, worldZ / WorldUnitsPerMeter, -worldY / WorldUnitsPerMeter);

	/// <summary>Converts a scalar distance in world units to render units.</summary>
	public static float DistanceToRender(int worldDistance) => worldDistance / WorldUnitsPerMeter;

	/// <summary>Converts a DTS model-space coordinate triple straight to render space.</summary>
	public static Vector3 DtsToRender(float dtsX, float dtsY, float dtsZ) =>
		ToRender(dtsX * WorldUnitsPerDtsUnit, dtsY * WorldUnitsPerDtsUnit, dtsZ * WorldUnitsPerDtsUnit);

	/// <summary>
	/// One of the simulation's own <see cref="Transform3"/>s as the render matrix that does the same
	/// thing to already-converted points: for any point <c>p</c>,
	/// <c>ToRender(t.TransformPoint(p)) == Vector3.Transform(ToRender(p), ToRenderMatrix(t))</c>.
	///
	/// <para>The rotation is conjugated through the axis map rather than merely copied, because
	/// <see cref="ToRender(Vec3i)"/> is a change of basis: a rotation about the simulation's Z is a
	/// rotation about render Y, and writing the Q14 entries straight into a matrix would leave the
	/// machine turning about the wrong axis. The metre scale cancels out of the conjugation, so only
	/// the translation carries it.</para>
	///
	/// <para>Both conventions here are row-vector — DBSIM's <c>p * M + t</c> and
	/// <see cref="Vector3.Transform(Vector3, Matrix4x4)"/>'s — so the two compose in the same order
	/// and a node matrix multiplied by an object matrix means node-then-object either way.</para>
	/// </summary>
	public static Matrix4x4 ToRenderMatrix(in Transform3 transform) {
		// The nine entries in the layout Transform3 documents: rows (m0 m1 m4), (m2 m3 m5), (m6 m7 m8).
		var rotation = new Matrix4x4(
			transform.M[0] / (float)SimTrig.One, transform.M[1] / (float)SimTrig.One, transform.M[4] / (float)SimTrig.One, 0f,
			transform.M[2] / (float)SimTrig.One, transform.M[3] / (float)SimTrig.One, transform.M[5] / (float)SimTrig.One, 0f,
			transform.M[6] / (float)SimTrig.One, transform.M[7] / (float)SimTrig.One, transform.M[8] / (float)SimTrig.One, 0f,
			0f, 0f, 0f, 1f);

		var result = RenderToSim * rotation * SimToRender;
		result.Translation = ToRender(transform.X, transform.Y, transform.Z);
		return result;
	}

	/// <summary>The axis half of <see cref="ToRender(Vec3i)"/>: <c>(x, y, z) -> (x, z, -y)</c>.</summary>
	private static readonly Matrix4x4 SimToRender = new(
		1f, 0f, 0f, 0f,
		0f, 0f, -1f, 0f,
		0f, 1f, 0f, 0f,
		0f, 0f, 0f, 1f);

	/// <summary>Its inverse: <c>(x, y, z) -> (x, -z, y)</c>.</summary>
	private static readonly Matrix4x4 RenderToSim = new(
		1f, 0f, 0f, 0f,
		0f, 0f, 1f, 0f,
		0f, -1f, 0f, 0f,
		0f, 0f, 0f, 1f);
}
