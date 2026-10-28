using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// The shot record <c>Sim_RaycastObjectList</c> takes (docs/es1/simulation/world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0),
/// which a missile's, a gun round's and a beam's shot build on their stacks.
/// </summary>
/// <param name="ray"><c>+0</c>, the ray record, whose length the sweep cuts to where the shot stops.</param>
/// <param name="owner"><c>+0xe</c>, the machine that fired.</param>
public sealed class Es1Shot(Es1Ray ray, IEs1SimObject owner) {
	/// <inheritdoc cref="Es1Shot(Es1Ray, IEs1SimObject)" path="/param[@name='ray']"/>
	public Es1Ray Ray { get; } = ray;

	/// <summary><c>+4</c> and <c>+6</c>, the two damage figures: the projectile record's armor <c>+6</c> and shield <c>+4</c>.</summary>
	public short Armor { get; init; }

	/// <inheritdoc cref="Armor"/>
	public short Shield { get; init; }

	/// <summary><c>+8</c>, the projectile record's splash factor.</summary>
	public short SplashFactor { get; init; }

	/// <summary>
	/// <c>+0xa</c>'s impact effects, the projectile record's three arrays from <c>+0xc</c>; null for none, where retail would
	/// read through a null record, an engine guard.
	/// </summary>
	public ProjectileData.Projectile? Effects { get; init; }

	/// <inheritdoc cref="Es1Shot(Es1Ray, IEs1SimObject)" path="/param[@name='owner']"/>
	public IEs1SimObject Owner { get; } = owner;

	/// <summary><c>+0x12</c>: 5 for a gun round's or a beam's shot, the projectile record's subtype for a missile's.</summary>
	public short Kind { get; init; }
}

/// <summary>
/// <c>Sim_RaycastObjectList</c> (<c>ES1DBSIM 0001f1d0</c>): a shot's ray against the flat ground, the zone's shapes and the
/// live objects, stopping at the nearest (docs/es1/simulation/world.md#shots-against-objects--sim_raycastobjectlist-0001f1d0).
/// </summary>
public static class Es1ShotSweep {
	/// <summary>A hit nearer the ray's start than this ends the walk over the objects.</summary>
	public const int NearHit = 500;

	/// <summary>How near the owner a squadmate must stand to complain of friendly fire.</summary>
	public const int ComplaintRange = 30000;

	/// <summary>The bearing difference below which a blocked line of fire is reported, as a signed <c>int16</c>.</summary>
	public const short BlockedBearing = 0x2000;

	/// <summary>
	/// The sweep of <paramref name="shot"/>, which cuts its ray's length to where the shot stops, answering that length + 1
	/// after a strike and 0 otherwise. In order:
	/// <list type="number">
	/// <item>the ground: with the ray's end below Z 0, the length becomes 0 when the start is below it too, and otherwise,
	/// with <i>d</i> the negated <c>Math_ElevationAngle</c> from the start to the end and not 0, (<i>z</i> &lt;&lt; 14) /
	/// <c>Math_CosBam</c>(<i>d</i> − <c>0x4000</c>), <i>z</i> the start's Z; a strike, but no object struck;</item>
	/// <item>the zone's shapes (<see cref="Es1ZoneShapes.RaycastShapeObjects"/>), a hit a strike whose shape becomes the
	/// blocker;</item>
	/// <item>the live objects in list order but the owner and one whose group holds an action, each through its hit test
	/// (<see cref="IEs1SimObject.HitTest"/>): a hit on the other side sets the owner's <see cref="IEs1SimObject.Engaged"/>,
	/// activates the object's engagement action (<see cref="Es1SensorModel.ActivateEngagementAction"/>) and clears the
	/// blocker, one on the owner's side becomes it; the length becomes the hit's and the walk goes on while
	/// it is <see cref="NearHit"/> or more;</item>
	/// <item>after a strike that struck no object, the impact effect at the ray's end, its Z raised to 0, entry
	/// (<c>Math_RandomNext</c> &amp; 3) of the ground array;</item>
	/// <item>with a blocker and the owner's target, the blocked line of fire.</item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// <c>RaycastShapeMinDistance</c>, which retail stores at <c>000afa1c</c> and <c>000afa18</c>, is not kept: it is 0 after
	/// each clear (docs/es1/simulation/terrain.md#a-ray--zone_raycastshapeobjects-00020b50) and nothing is known to read the
	/// two stores.
	/// </remarks>
	public static int Raycast(Es1Shot shot, Es1SimGlobals globals) {
		var ray = shot.Ray;
		var owner = shot.Owner;
		var start = ray.Start;
		bool struck = false;
		bool objectStruck = false;
		object? blocker = null;

		// 1. The ground, against the ray's end before any cut.
		var end = ray.Frame.TransformPoint(0, ray.Length, 0);
		if (end.Z < 0) {
			short d = (short)-ElevationAngle(start, end);
			if (start.Z < 0) {
				ray.Length = 0;
			} else if (d != 0) {
				// Math_CosBam answers 0 for d of 1 to 7, a ray dipping under the ground at less than 0.04°, which retail
				// divides by; here the length stands.
				short cosine = SimTrig.Cos(unchecked((short)(d - 0x4000)));
				if (cosine != 0) {
					ray.Length = unchecked(start.Z << 14) / cosine;
				}
			}

			struck = true;
		}

		// 2. The zone's shapes.
		if (globals.ZoneShapes.RaycastShapeObjects(ray, out var shapeHit)) {
			ray.Length = shapeHit.Distance;
			blocker = shapeHit.Shape;
			struck = true;
		}

		// 3. The live objects. Retail reads the groups with no test for a null one; a member with none is walked here and
		// matches no side.
		byte? ownerSide = owner.Group?.Side;
		foreach (var simObject in globals.LiveObjects) {
			if (ReferenceEquals(simObject, owner) || simObject.Group?.Action != null) {
				continue;
			}

			int distance = simObject.HitTest(shot, globals);
			if (distance == 0) {
				continue;
			}

			bool sameSide = simObject.Group?.Side == ownerSide;
			if (!sameSide) {
				owner.Engaged = true;
				Es1SensorModel.ActivateEngagementAction(simObject, globals);
				blocker = null;
			} else {
				blocker = simObject;
			}

			if (!simObject.Destroyed && (sameSide || (!simObject.Disarmed && !simObject.Immobilized))) {
				// PLACEHOLDER: for a player's shot on its own side in another group, Group_NearestLiveMember (0001cea4) of the
				// owner's group within ComplaintRange to Mech_AiFriendlyFireComplaint (00019300); then the object's slot
				// +0x54 (object, Armor and Shield, owner), Mech_AiOnTakingFire on a HERC. Both are plan step 8's AI.
			}

			ray.Length = distance;
			struck = true;
			objectStruck = true;
			if (distance < NearHit) {
				break;
			}
		}

		if (!struck) {
			return 0;
		}

		// 4. The impact where no object was struck: the generator is drawn whether or not the effect is made.
		if (!objectStruck) {
			var point = ray.Frame.TransformPoint(0, ray.Length, 0);
			if (point.Z < 0) {
				point = new Vec3i(point.X, point.Y, 0);
			}

			int k = globals.Random.Next() & 3;
			// Explosion_Construct (00005af0)(null, point, entry 4 + k of the shot's effect table); a shot with no
			// effects (Effects) makes none.
			if (shot.Effects?.ImpactFXGround[k] is { } effect) {
				globals.Explosions.Construct(point, effect, globals.Random);
			}
		}

		// 5. The blocked line of fire.
		if (blocker != null && owner.Target is { } target) {
			var rayEnd = ray.Frame.TransformPoint(0, ray.Length, 0);
			var position = owner.Position;
			if (SimMath.FastMagnitude2D(rayEnd.X - position.X, rayEnd.Y - position.Y) < target.Position.ApproxDistanceTo(position)) {
				short toEnd = (short)(Es1MissionGroup.HeadingToward(position, rayEnd) - owner.Heading);
				short toTarget = (short)(Es1MissionGroup.HeadingToward(position, target.Position) - owner.Heading);
				if ((short)(toEnd - toTarget) < BlockedBearing) {
					// PLACEHOLDER: the owner's slot +0x68 (owner, the blocker's +6), Mech_AiOnLineOfFireBlocked on a HERC,
					// plan step 8's AI.
				}
			}
		}

		return ray.Length + 1;
	}

	/// <summary>
	/// <c>Math_ElevationAngle</c> (<c>00063cf0</c>)(<paramref name="a"/>, <paramref name="b"/>): the elevation of
	/// <paramref name="b"/> seen from <paramref name="a"/>, <c>Math_Atan2Guarded</c> of the ground distance and the height
	/// difference (docs/es1/simulation/world.md#functions).
	/// </summary>
	public static short ElevationAngle(Vec3i a, Vec3i b) =>
		(short)SimTrig.Atan2Guarded(b.Z - a.Z, SimMath.FastMagnitude2D(b.X - a.X, b.Y - a.Y));
}
