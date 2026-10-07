using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Render;

/// <summary>
/// One <see cref="EffectLight"/> as it applies to one drawn object — what <c>LightManager_SelectLightsForObject</c> (<c>00407098</c>)
/// synthesises into the active light list just before that object is drawn.
/// </summary>
/// <param name="Directional">
/// Whether this is the far approximation (the original's light type 1) rather than a real point
/// light (type 2). The two carry the same falloff and differ only in their angular term; see
/// docs/retail/formats/effect-lights.md, "What a light contributes".
/// </param>
/// <param name="Vector">
/// For a directional light, the unit direction its light travels, pointing from the light at the
/// object — the same convention as <see cref="MissionSun.Direction"/>. For a point light, the
/// light's position in render space.
/// </param>
/// <param name="Intensity">
/// Brightness this object sees, 0-255. A directional light's is already attenuated by distance
/// here, which is where the original spends it; a point light's is the slot's own, its falloff
/// deferred to the shade calculation.
/// </param>
public readonly record struct SelectedEffectLight(bool Directional, Vector3 Vector, float Intensity);

/// <summary>
/// <c>LightManager_SelectLightsForObject</c> (<c>00407098</c>) — picks which of <see cref="EffectLightField"/>'s slots light one drawn
/// object, and what each one looks like from where that object stands.
///
/// <para>The original runs this from <c>ObjList_DrawEntryRender</c> (<c>0042876c</c>), once per
/// depth-sorted render entry with the entry's cached position and bounding radius, and registers
/// what it produces into the ordinary ten-slot active light list beside the mission sun. Here the
/// result is a small span the renderer uploads as uniforms instead, so the sun stays where it is
/// and the effect lights are what varies per object.</para>
///
/// <para>Everything below is in the simulation's own integer world units, because the distance is
/// <see cref="Vec3i.ApproxDistanceTo"/> and the branch test is the sim's own arctangent — the
/// conversion to render space happens only on the vectors that leave. The derivation is
/// docs/retail/formats/effect-lights.md, "Per-object selection".</para>
///
/// <para>Where the shading departs from the original. A point light is measured to the corner the
/// shader is lighting rather than to the poly's stored centre point, as the sun's own term is; the
/// difference is bounded by the poly's size. The arithmetic is float from the selection outward,
/// where the original truncates twice inside the point term (<c>dot / (|disp| + 1)</c> and the divide
/// by the range sum). The manager's camera position (<c>mgr+0x00</c>) is not modelled; its only
/// consumer has no callers. Terrain stays unlit: its shade is baked at zone load, as the original
/// bakes it, and the shader skips the accumulate for a vertex carrying a baked shade byte.</para>
/// </summary>
public static class EffectLightSelection {
	/// <summary>
	/// How many effect lights one object can be lit by: the room <c>Light_Register</c>'s ten-entry active
	/// list leaves beside the mission sun. Never reached: the handle pool allows
	/// <see cref="EffectLightField.HandleCount"/> effect lights at once.
	/// </summary>
	public const int MaxPerObject = 9;

	/// <summary>
	/// The angular size, in the sim's binary angle unit, below which an object is far enough from a
	/// light to take the directional approximation — 8000, which is 43.9 degrees. The test is on the
	/// angle the object's own bounding radius subtends from the light, so <b>small means far</b>:
	/// a light only becomes a real point light once it is within about one bounding radius.
	/// </summary>
	public const int DirectionalAngle = 8000;

	/// <summary>
	/// The numerator of a point light's falloff, <c>B * 0x20</c>, divided by the world units a render
	/// unit spans — so that the shade term is
	/// <c>intensity * PointFalloff * cos / distanceInRenderUnits</c> and the shader never has to know
	/// the world scale. The original's <c>A</c> is zero, so the denominator carries the distance
	/// alone; see docs/retail/formats/effect-lights.md, "What a light contributes".
	/// </summary>
	public const float PointFalloff =
		EffectLightField.FalloffRange * EffectLightField.FalloffScale / WorldScale.WorldUnitsPerMeter;

	/// <summary>
	/// Fills <paramref name="selected"/> with the lights that reach an object of radius
	/// <paramref name="shapeRadius"/> standing at <paramref name="position"/>, and returns how many.
	/// Slots are taken in index order and the surplus past <paramref name="selected"/>'s length is
	/// dropped (see <see cref="MaxPerObject"/>).
	/// </summary>
	/// <param name="position">The object's position in world units — <c>SimObject.Position</c>.</param>
	/// <param name="shapeRadius">
	/// Its drawn radius in world units — <c>SimObject_GetShapeRadius</c> (vtable <c>+0x10</c>), which
	/// is the figure the original's render entry caches at <c>+0x10</c>.
	/// </param>
	public static int Select(EffectLightField field, Vec3i position, int shapeRadius,
			Span<SelectedEffectLight> selected) {
		int count = 0;

		for (int i = 0; i < field.Slots.Count && count < selected.Length; i++) {
			var slot = field.Slots[i];
			if (!slot.IsLive) {
				continue;
			}

			int distance = position.ApproxDistanceTo(slot.Position);
			if (distance >= slot.CullRadius) {
				continue;
			}

			// The original's own shift: both the distance and the radius are dropped to 1/32 of a
			// world unit before either is used, which is what keeps the attenuation divide below in
			// range of the 16-bit-era arithmetic it was written for.
			int d = distance >> 5;

			// Argument order is (y, x) here and (x, y) in the original — Math_Atan2Guarded takes the
			// x first. This is atan(radius / distance) either way, and reading it the other way round
			// mirrors the test about the 45-degree line and swaps the two branches.
			if (d != 0 && SimTrig.Atan2Guarded(shapeRadius >> 5, d) < DirectionalAngle) {
				int attenuation = System.Math.Min(EffectLightField.MaxIntensity,
					(EffectLightField.FalloffRange << 8) / (d + EffectLightField.FalloffOffset));

				Vector3 travel = WorldScale.ToRender(position - slot.Position);
				if (travel.LengthSquared() > 1e-12f) {
					selected[count++] = new SelectedEffectLight(true, Vector3.Normalize(travel),
						slot.Intensity * attenuation >> 8);
				}

				continue;
			}

			selected[count++] = new SelectedEffectLight(false, WorldScale.ToRender(slot.Position),
				slot.Intensity);
		}

		return count;
	}
}
