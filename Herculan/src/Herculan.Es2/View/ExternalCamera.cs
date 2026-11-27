using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine;
using Herculan.Es2.Render;
using Herculan.Es2.Sim;
using Herculan.Es2.Terrain;

namespace Herculan.Es2.View;

/// <summary>
/// The outside view under <see cref="Engine.Settings.TweakSettingDefinitions.MouseOutsideView"/> — an orbit
/// camera the player drags round the object being viewed with the mouse (<see cref="Engine.View.MouseOrbitDrag"/>),
/// always facing back at it, drawn over the whole window.
///
/// <para><b>This engine's own camera, not retail's.</b> Every number below was picked to frame a
/// HERC nicely. Retail's outside view is <see cref="ViewCamera"/>'s orbit, steered by the controls
/// (docs/retail/simulation/external-views.md); this one exists because a mouse drag is easier to use, and
/// only the tweak reaches it. It circles the point retail's orbit does, the subject's
/// <see cref="SimObject.ViewMounts"/> orbit centre, so it frames whatever the chain views as retail's would: a
/// HERC's torso, 1400 to 1600 units above its origin, and the RAZOR's origin itself, where its type record puts
/// the centre.</para>
///
/// <para><see cref="Place"/> is stateless — it takes the orbit angles rather than owning them — so
/// the host is the one holding them between frames (alongside the drag that moves them), the same
/// way it already owns <c>cockpitViewKick</c> and the throttle gauge.</para>
/// </summary>
public static class ExternalCamera {
	/// <summary>How far the eye orbits from the machine, in metres.</summary>
	public const float DistanceMeters = 18f;

	/// <summary>How far above the orbit centre the eye starts, in metres, before the player has dragged anything.</summary>
	public const float StartHeightMeters = 5f;

	/// <summary>
	/// Least clearance kept above the ground under the eye, in metres, so orbiting into a rising slope
	/// does not bury the camera in it.
	/// </summary>
	public const float GroundClearanceMeters = 2f;

	/// <summary>
	/// How far <see cref="Place"/>'s <c>orbitPitchBam</c> can tip up or down from level with the aim
	/// point, each way, in the same BAM units as that parameter — 45 degrees. The engine's own choice,
	/// so a drag can't flip the eye over the top or bottom of the orbit.
	/// </summary>
	public const int MaxOrbitPitch = BinaryAngle.QuarterTurn / 2;

	/// <summary><see cref="MaxOrbitPitch"/> in radians, for a host tracking its own orbit state in floats.</summary>
	public const float MaxOrbitPitchRadians = MathF.PI / 4f;

	/// <summary>
	/// The orbit pitch, in radians, that puts the eye <see cref="StartHeightMeters"/> above the orbit centre, for a host
	/// to seed its own orbit state with before the player has dragged anything.
	/// </summary>
	public static readonly float DefaultOrbitPitchRadians = MathF.Atan2(StartHeightMeters, DistanceMeters);

	private static int Units(float meters) => (int)(meters * WorldScale.WorldUnitsPerMeter);

	/// <summary>
	/// Points a camera at a subject's orbit centre, from
	/// <see cref="DistanceMeters"/> away on the orbit the yaw and pitch describe.
	/// </summary>
	/// <param name="camera">The camera to place.</param>
	/// <param name="subject">The object the camera orbits.</param>
	/// <param name="terrain">The terrain, optional and only used for the ground clearance floor.</param>
	/// <param name="orbitYawBam">
	/// Orbit angle around the subject, in BAM (see <see cref="BinaryAngle"/>), offset from directly
	/// behind it — zero reproduces the view's original fixed chase position.
	/// </param>
	/// <param name="orbitPitchBam">
	/// Orbit angle above or below level with the aim point, in BAM, clamped to
	/// <see cref="MaxOrbitPitch"/> either way.
	/// </param>
	public static void Place(Camera camera, SimObject subject, HeightGrid? terrain, int orbitYawBam, int orbitPitchBam) {
		// A negative pitch (e.g. from BinaryAngle.FromRadians) arrives wrapped into the top of the BAM
		// range rather than as a small negative int — sign-extending the low 16 bits first (the same
		// trick BinaryAngle.Delta uses) recovers it before the clamp, so the bottom of the range doesn't
		// read as a huge positive value and clamp up to the top instead of down to the bottom.
		orbitPitchBam = Math.Clamp((int)(short)(orbitPitchBam & 0xffff), -MaxOrbitPitch, MaxOrbitPitch);

		// The point the camera orbits and stays aimed at: the orbit centre through the subject's frame,
		// as ViewCamera puts retail's.
		var orbitCentre = subject.ViewMounts.OrbitCentre;
		var center = subject.WorldFrame.TransformPoint(orbitCentre.X, orbitCentre.Y, orbitCentre.Z);

		// A HERC's forward vector is (-sin h, cos h) in world XY (see MissionScene.TransformOf), so
		// directly behind it is the negation of that; orbitYawBam turns the eye further around from
		// there. `horizontal` and `vertical` split the fixed orbit radius between the ground plane and
		// height the same way any spherical offset would, so orbitPitchBam = 0 sits level with the aim
		// point and the clamp above keeps it within 45 degrees of that either way.
		int radius = Units(DistanceMeters);
		int azimuth = (subject.Heading + orbitYawBam) & 0xffff;
		int horizontal = radius * BinaryAngle.Cos(orbitPitchBam) / BinaryAngle.TrigOne;
		int vertical = radius * BinaryAngle.Sin(orbitPitchBam) / BinaryAngle.TrigOne;

		int eyeX = center.X + horizontal * BinaryAngle.Sin(azimuth) / BinaryAngle.TrigOne;
		int eyeY = center.Y - horizontal * BinaryAngle.Cos(azimuth) / BinaryAngle.TrigOne;
		int eyeZ = center.Z + vertical;

		if (terrain != null) {
			eyeZ = Math.Max(eyeZ, terrain.HeightAtWorld(eyeX, eyeY) + Units(GroundClearanceMeters));
		}

		camera.Position = new Vec3i(eyeX, eyeY, eyeZ);

		// The eye always looks back at the point it orbits, wherever the drag has put it —
		// Detection.HeadingToward gives the ground bearing there, and camera yaw runs opposite to a
		// simulation heading (see MissionScene.TransformOf). Pitch is whatever tips the view onto the
		// aim point from however high the eye ended up, same as `horizontal` describes.
		camera.Yaw = -Detection.HeadingToward(center, camera.Position) & 0xffff;
		camera.Pitch = SimTrig.Atan2(center.Z - eyeZ, horizontal) & 0xffff;
		camera.Roll = 0;
	}
}
