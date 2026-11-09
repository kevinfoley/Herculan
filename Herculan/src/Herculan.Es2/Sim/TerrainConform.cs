using Herculan.Engine.Numerics;
using Herculan.Es2.Terrain;

namespace Herculan.Es2.Sim;

/// <summary>
/// <c>SimObject_ConformToTerrain</c> (<c>004029d8</c>) — sits a flat footprint on the ground under
/// it. Two callers in the original: a ground vehicle's own tick (<see cref="BaseObject"/>) and
/// <c>FlatObj_Draw</c> (<c>0040991c</c>), which runs it on every ground shape it draws
/// (<see cref="GroundShape"/>).
///
/// <para>Four ground samples, at <paramref name="radius"/> forward, back, left and right in the
/// object's own frame. Pitch is the arctangent of the fore-aft drop over the span between those two
/// samples and roll the same across the beam; Z is the mean of all four, so the object rides on the
/// average of the ground under its footprint rather than on the point its origin happens to sit
/// over.</para>
/// </summary>
internal static class TerrainConform {
	/// <param name="terrain">The ground being sat on.</param>
	/// <param name="frame">
	/// The object's frame as it stands, lean included: the original rotates its four probe offsets
	/// through the object's current matrix, so the previous conform's pitch and roll carry into the
	/// next one's probes.
	/// </param>
	/// <param name="radius">The object's own shape radius — its vtable <c>+0x10</c>.</param>
	/// <returns>The new pitch, roll and Z. The heading is left as it was.</returns>
	internal static (short Pitch, short Roll, int Z) Apply(HeightGrid terrain, in Transform3 frame,
			int radius) {
		int front = SampleGround(terrain, frame, 0, radius);
		int back = SampleGround(terrain, frame, 0, -radius);
		int left = SampleGround(terrain, frame, -radius, 0);
		int right = SampleGround(terrain, frame, radius, 0);

		return (
			(short)SimTrig.Atan2(front - back, radius * 2),
			(short)SimTrig.Atan2(left - right, radius * 2),
			(front + back + left + right) >> 2);
	}

	/// <summary>
	/// The ground height under one probe. The offset is rotated by the object's frame but added to
	/// its position in X and Y only, which is what keeps the four probes on the ground plane whatever
	/// the current lean is.
	/// </summary>
	private static int SampleGround(HeightGrid terrain, in Transform3 frame, int x, int y) {
		var offset = frame.RotateVector(x, y, 0);
		return terrain.HeightAtWorld(frame.X + offset.X, frame.Y + offset.Y);
	}
}
