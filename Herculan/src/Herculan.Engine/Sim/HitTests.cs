using Herculan.Engine.Numerics;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Sim;

/// <summary>
/// The simulation's ray and area queries over a world's terrain and live objects: the shot raycast, the AI's shape
/// probe and the explosive blast sweep.
/// </summary>
public static class HitTests {
	/// <summary>
	/// <c>Sim_RaycastObjectList</c> (<c>00426528</c>) — the shared ray-versus-live-object query, which
	/// is a hit test and a damage application at once: each candidate's own
	/// <see cref="SimObject.DirectFireHitTest"/> resolves the geometry and applies whatever got
	/// through in the same call.
	///
	/// <para>The sweep <b>shortens the ray as it goes</b> and does not stop at the first hit: a
	/// candidate found later, but nearer, overwrites the one before it, because every subsequent
	/// candidate is tested against the shortened <see cref="WeaponShot.Distance"/> and can only pass
	/// if it is nearer still. It ends early only for a hit inside
	/// <see cref="WeaponShot.MinimumScanDistance"/>, which nothing can beat.</para>
	///
	/// <para>The terrain goes first, and is the reason an object sweep on its own would not do:
	/// <see cref="RaycastTerrain"/> shortens the ray at the ground before a single object is tested,
	/// so a machine standing behind a ridge cannot be shot through it.</para>
	///
	/// <para>Both of the original's exclusions are here: the attacker at the shot record's
	/// <c>+0x0e</c> (<see cref="WeaponShot.Owner"/>) and the second one at <c>+0x14</c>
	/// (<see cref="WeaponShot.Excluded"/>), which only a flyer's airframe contact probe ever fills.</para>
	///
	/// <para>The AI "something just shot at me" notification on each candidate's <c>+0x50</c> slot is
	/// here, and so is the friendly-fire complaint the original raises beside it, and the engagement
	/// pair below.</para>
	/// </summary>
	/// <returns>The distance the shot travelled before it hit something, or zero if it hit nothing.</returns>
	public static int Raycast(SimWorld world, WeaponShot shot) {
		// The mission difficulty scales both damage figures before anything is tested, which is where
		// the original puts it too. A shot with no attacker has no side to scale by and is left alone
		// — the original's own gate, and what keeps a flyer's airframe contacts out of it.
		if (shot.Owner is { } attacker) {
			shot.ApplyDifficultyScale(world.DamageScaleFor(attacker.Side));
		}

		bool hit = RaycastTerrain(world, shot);

		for (int i = 0; i < world.Objects.Count; i++) {
			var candidate = world.Objects[i];

			// An object whose mission group is still waiting on its action is skipped before any
			// geometry is touched — the original's own `group[+0x14] != 0` test, the same one
			// MechObject.CollisionTest and the frame submit make. It matters more here than
			// anywhere: an undeployed group is placed by the ordinary rules, so retail missions
			// routinely leave several of them stacked on a shared waypoint — the first stock
			// mission parks seven objects in three overlapping pairs — where they are invisible and
			// unticked but, without this, perfectly solid. Shots stopped on nothing.
			if (candidate.Removed || candidate.AwaitingDeployment
					|| ReferenceEquals(candidate, shot.Owner)
					|| ReferenceEquals(candidate, shot.Excluded)) {
				continue;
			}

			int struckAt = candidate.DirectFireHitTest(world, shot);
			if (struckAt == 0) {
				continue;
			}

			// The engagement pair, and the two halves land on opposite objects: the shooter is marked
			// engaged, the struck object's action fires. See docs/retail/simulation/mission-deployment.md, "An
			// object's own two actions".
			if (shot.Owner is { } firer
					&& ReferenceEquals(candidate, SimObject.SelectedTargetOf(firer))) {
				firer.Engaged = true;
				candidate.ActivateEngagementAction(world);
			}

			// "Something just shot at me", on the candidate's own +0x50 slot. The original puts it
			// exactly here — past the hit test, so only what the ray actually reached hears about it —
			// and skips it for a destroyed candidate, or an enemy one that is out of action. It applies
			// no damage. See docs/retail/simulation/hit-detection.md.
			if (shot.Owner is { } owner && !candidate.Destroyed
					&& (candidate.Side == owner.Side || !candidate.OutOfAction)) {
				// The player hitting anything on his own side outside his own group, structures
				// included: the player's own nearest squadmate complains, if it is close enough to
				// him to have seen it (docs/retail/simulation/ai-targeting.md, "Radio callouts").
				if (owner.LocallyPiloted && candidate.Side == owner.Side
						&& !ReferenceEquals(candidate.Group, owner.Group)
						&& owner.Group?.NearestLiveMember(owner) is MechObject witness
						&& witness.Position.ApproxDistanceTo(owner.Position) < FriendlyFireWitnessRange) {
					witness.FriendlyFireComplaint(world);
				}

				candidate.OnTakingFire(world, owner, shot.DamageArmor);
			}

			shot.Distance = struckAt;
			shot.HitObject = candidate;
			hit = true;

			if (struckAt < WeaponShot.MinimumScanDistance) {
				break;
			}
		}

		// The ground impact, which is the sweep's own job and not the ground's: the original keeps two
		// flags — "something was struck" and "an object was struck" — and spawns an effect at the ray's
		// far end when the first is set and the second is not. So a shot that ends in the dirt puts one
		// down and a shot that ends on a machine does not, even though the ground clipped the ray
		// first in both cases.
		//
		// It comes out of the ImpactFxGroup.Ground array, and unlike every object hit it is spawned
		// with no owner and with the sound suppressed (the constructor's last argument is 0 here and 1
		// at every other site).
		if (hit && shot.HitObject == null) {
			world.Effects.SpawnPickedImpactEffect(
				shot.ImpactFx(WeaponShot.ImpactFxGroup.Ground),
				shot.Muzzle.TransformPoint(0, shot.Distance, 0),
				owner: null,
				playSound: false);
		}

		// "My line of fire is blocked", on the shooter's own +0x64 slot, and the only caller of it in
		// the image. A shot that stopped nearer than the target it was aimed at and within 45 degrees
		// of the same bearing is the shooter hitting something in the way — which is what sends a
		// machine into `skirting` to walk around it. Only a machine implements the slot.
		//
		// A shot that reached the target does not count, which is what keeps the state off a machine
		// that is shooting perfectly well: the original clears the "what was struck" pointer, and only
		// that, when the candidate it just resolved is the shooter's own target.
		if (hit && shot.Owner is MechObject { Target: { } aimedAt } shooter
				&& !ReferenceEquals(shot.HitObject, aimedAt)) {
			var stopped = shot.Muzzle.TransformPoint(0, shot.Distance, 0);

			// The original measures the two the two different ways: the ground plane to where the shot
			// stopped, three dimensions to the target.
			int toStop = SimMath.FastMagnitude2D(
				shooter.Position.X - stopped.X, shooter.Position.Y - stopped.Y);

			if (toStop < shooter.Position.ApproxDistanceTo(aimedAt.Position)) {
				short spread = (short)(Detection.HeadingToward(stopped, shooter.Position)
					- Detection.HeadingToward(aimedAt.Position, shooter.Position));

				if (System.Math.Abs((int)spread) < BlockedLineOfFireArc) {
					shooter.OnLineOfFireBlocked();
				}
			}
		}

		return hit ? shot.Distance + 1 : 0;
	}

	/// <summary>
	/// <c>Sim_RaycastShapes</c> (<c>00404ca0</c>) and the <c>Sim_RaycastShapeList</c>
	/// (<c>00404bc0</c>) it ends in — a ray between two world points against the structures' collision
	/// volumes, and nothing else: no terrain, no machines, no damage, no effects. It is the AI's shape
	/// probe, called by <see cref="MechObject"/>'s obstacle avoidance and its line-of-sight test (see
	/// docs/retail/simulation/hit-detection.md, "The shape probe").
	///
	/// <para>The ray is built as a shot's is — pointed from <paramref name="from"/> at
	/// <paramref name="to"/> by <see cref="SimTrig.EulerToward"/>, starting at
	/// <paramref name="from"/>, as long as the sqrt-free distance between them — and each candidate
	/// is tested by <see cref="BaseObject.VolumeRaycast"/>. Candidates are every structure
	/// <see cref="BaseObject.InShapeList"/> admits, oldest first as <c>Pool_Prev</c> walks the
	/// structure pool. The original gathers them with no deployment test, so a structure whose group
	/// has not arrived is one.</para>
	///
	/// <para>The sweep shortens the ray to each hit and stops at one inside
	/// <see cref="WeaponShot.MinimumScanDistance"/>, as <see cref="Raycast"/> does, so what it reports
	/// is the last structure struck: the nearest, unless an earlier one was inside that distance.</para>
	///
	/// <para>The original takes a clearance and a side filter as well. All three of its callers pass a
	/// clearance of 0 and a side of −1, "any", so neither is a parameter here.</para>
	/// </summary>
	/// <param name="world">The world whose structures are probed.</param>
	/// <param name="from">Where the ray starts, in world units.</param>
	/// <param name="to">Where it ends.</param>
	/// <param name="distance">How far along the ray the struck structure's volume was entered.</param>
	/// <param name="struck">The structure struck, or null for a miss.</param>
	/// <returns>Whether any structure was struck.</returns>
	internal static bool RaycastShapes(SimWorld world, Vec3i from, Vec3i to, out int distance, out BaseObject? struck) {
		var (pitch, roll, yaw) = SimTrig.EulerToward(to, from);
		var muzzle = Transform3.FromEuler(pitch, roll, yaw);
		muzzle.X = from.X;
		muzzle.Y = from.Y;
		muzzle.Z = from.Z;

		var muzzleInverse = muzzle.Inverted();
		int length = to.ApproxDistanceTo(from);

		distance = 0;
		struck = null;

		for (int i = 0; i < world.Objects.Count; i++) {
			if (world.Objects[i] is not BaseObject { Removed: false, InShapeList: true } candidate
					|| !candidate.VolumeRaycast(muzzle, muzzleInverse, length, ShapeProbeClearance,
						out int struckAt)) {
				continue;
			}

			length = struckAt;
			distance = struckAt;
			struck = candidate;

			if (struckAt < WeaponShot.MinimumScanDistance) {
				break;
			}
		}

		return struck != null;
	}

	/// <summary>The clearance every caller of <see cref="RaycastShapes"/> passes.</summary>
	private const int ShapeProbeClearance = 0;

	/// <summary>
	/// <c>Damage_ExplosiveBlastSweep</c> (<c>00426a20</c>) — the area-of-effect counterpart of
	/// <see cref="Raycast"/>: instead of following a ray it walks the whole live-object list once and
	/// offers the blast to everything standing inside it.
	///
	/// <para>The range test is <b>surface to centre, not centre to centre</b>: an object's own
	/// <see cref="SimObject.HitRadius"/> is subtracted before the comparison, so a large machine is
	/// caught by a blast that a small one standing in the same place would be outside of.</para>
	///
	/// <para>What the blast then does is the object's own business — <see cref="SimObject.ExplosiveDamage"/>,
	/// which is implemented for a machine and nothing else. Unlike the raycast the sweep does not
	/// stop, shorten or care about order: everything in range is hit, and a wall between two of them
	/// does not shield either, which is the original's behaviour and not a simplification.</para>
	///
	/// <para>Exactly three call sites, all terminal events rather than routine fire, and all three
	/// reach it here: the drop pod touching down (<see cref="MeteorObject"/>), a plasma round going
	/// off (<see cref="Projectile"/>), and a machine ending its ramming charge on something — see
	/// MechObject.Ramming.cs.</para>
	/// </summary>
	/// <param name="world">The world whose objects the blast reaches.</param>
	/// <param name="hitPoint">Where the explosion went off, in world units.</param>
	/// <param name="blastRadius">How far it reaches, and the denominator of each victim's falloff.</param>
	/// <param name="damage">The blast's damage figure, before any victim's shields scale it.</param>
	/// <param name="attacker">Who set it off, for the kill credit.</param>
	/// <param name="excluded">
	/// One object the blast passes over — the sweep's own <c>param_5</c>, which the machine death
	/// throe uses to keep a wreck from blowing itself up a second time.
	/// </param>
	/// <returns>
	/// Whether the blast <i>caught</i> anything — set beside the damage call and so before that call
	/// can decide the target's shields swallowed it, which is the original's own arrangement. The
	/// drop pod's <c>+0x4c</c> latch is the one caller that reads it; see <see cref="MeteorObject"/>.
	/// </returns>
	public static bool ExplosiveBlastSweep(SimWorld world, Vec3i hitPoint, int blastRadius, short damage,
			SimObject? attacker, SimObject? excluded) {
		bool hit = false;

		for (int i = 0; i < world.Objects.Count; i++) {
			var candidate = world.Objects[i];

			// A group still waiting on its arrival action is not in the mission, so it is not in the
			// blast either — the same gate the raycast and the frame submit make, and the same reason:
			// undeployed groups sit stacked on shared waypoints where a single explosion would
			// otherwise catch all of them at once.
			if (candidate.Removed || candidate.AwaitingDeployment
					|| ReferenceEquals(candidate, excluded)) {
				continue;
			}

			if (candidate.Position.ApproxDistanceTo(hitPoint) - candidate.HitRadius >= blastRadius) {
				continue;
			}

			candidate.ExplosiveDamage(world, damage, hitPoint, blastRadius, attacker);
			hit = true;
		}

		return hit;
	}

	/// <summary>
	/// <c>Sim_RaycastTerrain</c> (<c>00428048</c>) — the ray-versus-ground query the shared raycast
	/// runs before it looks at any object. It rebuilds the ray's far end from the shot's own frame,
	/// walks the heightmap with <see cref="HeightGrid.RayWalk"/>, and measures the ground hit back
	/// to the muzzle; a hit nearer than the ray's current length clips it there.
	///
	/// <para>The measured distance uses the sim's sqrt-free magnitude, as the original does, so it
	/// carries that approximation's direction-dependent error, as every other 3D range in the
	/// simulation does.</para>
	///
	/// <para>The original also hands the sweep a pseudo-object standing in for the ground, so that
	/// the AI notification path has something to name. Nothing here consumes that yet, so a terrain
	/// clip leaves <see cref="WeaponShot.HitObject"/> null and records the point instead.</para>
	/// </summary>
	/// <returns>Whether the shot was clipped at the ground.</returns>
	private static bool RaycastTerrain(SimWorld world, WeaponShot shot) {
		// The ray is the muzzle transform's Y axis, so its far end is that frame's own
		// (0, distance, 0) — the same construction the shot itself was built from.
		var muzzle = new Vec3i(shot.Muzzle.X, shot.Muzzle.Y, shot.Muzzle.Z);
		var end = shot.Muzzle.TransformPoint(0, shot.Distance, 0);

		if (!world.Terrain.RayWalk(muzzle, end, world.ThinRay, out var ground)) {
			return false;
		}

		int distance = ground.ApproxDistanceTo(muzzle);
		if (distance >= shot.Distance) {
			return false;
		}

		shot.Distance = distance;
		shot.GroundHit = ground;
		return true;
	}

	/// <summary>
	/// Half-arc, either side of the bearing to the target, inside which a shot that stopped short
	/// counts as the shooter's own line of fire being blocked. 45°; see
	/// docs/retail/simulation/ai-combat-states.md.
	/// </summary>
	private const int BlockedLineOfFireArc = 0x2000;

	/// <summary>
	/// How near the player the squadmate <see cref="Raycast"/> picks must be for it to complain about
	/// the player's shot at a friendly outside the group. See docs/retail/simulation/ai-targeting.md,
	/// "Radio callouts".
	/// </summary>
	private const int FriendlyFireWitnessRange = 30000;
}
