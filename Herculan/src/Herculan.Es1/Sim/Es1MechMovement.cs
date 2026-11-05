using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// A HERC's move: <c>Mech_MovementTick</c> (<c>ES1DBSIM 00014844</c>), the transform's save and restore around it, and
/// <c>Mech_CollisionTest</c> (<c>00014174</c>) (docs/es1/simulation/mech-locomotion.md#the-move).
/// </summary>
public static class Es1MechMovement {
	/// <summary>The unstick countdown a refused move loads into a machine other than the player's.</summary>
	public const int UnstickTime = 20000;

	/// <summary>The unstick side's magnitude, drawn either way.</summary>
	public const short UnstickSideMagnitude = 5000;

	/// <summary>
	/// <c>Mech_MovementTick</c>: the transform saved, <c>Mech_IntegrateMotion</c>, and on a refusal the transform restored,
	/// the unstick or the player's thump, the throttle zeroed with <c>+0x8d</c> set, and the bounce outside a transition
	/// frame or the cut to the forward stop sequence inside one; then <c>Mech_PlaceLegsOnGround</c>. Returns 1
	/// (docs/es1/simulation/mech-locomotion.md#the-move, docs/retail/simulation/mech-locomotion.md#what-a-refused-move-does).
	/// </summary>
	public static short MovementTick(Es1Mech mech, Es1SimGlobals globals) {
		var chassis = mech.Type.Chassis;
		var saved = PushTransform(mech);
		mech.Locomotion.IntegrateMotion(globals.Clock);
		// Mech_MovementTick tests the answer's low 16 bits.
		if ((short)CollisionTest(mech, globals) != 0) {
			PopTransform(mech, saved);
			if (!mech.IsPlayer) {
				mech.UnstickCountdown = UnstickTime;
				mech.UnstickSide = (globals.Random.Next() & 1) == 0 ? UnstickSideMagnitude : (short)-UnstickSideMagnitude;
				mech.UnstickReverse = mech.Speed >= 1;
			} else {
				// PLACEHOLDER: Sound_Play (0004de84)(0x29), the collision thump; no plan step ports sound yet.
			}

			mech.Throttle = 0;
			mech.ThrottleChanged = true;
			if (mech.LocomotionThread is not { InTransition: true }) {
				// The bounce: the speed negated and held to the record's +0x6e and +0x06, the rate negated, the step again.
				short speed = (short)-mech.Speed;
				mech.Speed = speed >= chassis.SpeedForward ? chassis.SpeedForward
					: speed <= chassis.GaitThresholdReverse ? chassis.GaitThresholdReverse
					: speed;
				mech.PlaybackRate = (short)-mech.PlaybackRate;
				mech.Locomotion.IntegrateMotion(globals.Clock);
				if ((short)CollisionTest(mech, globals) != 0) {
					PopTransform(mech, saved);
					mech.Speed = 0;
				}
			} else {
				mech.LocomotionThread.SetSequence(chassis.AnimId_StopMove, 0, 0);
			}
		}

		Es1Footfalls.PlaceLegsOnGround(mech, globals.Sound);
		return 1;
	}

	/// <summary>
	/// <c>Mech_CollisionTest</c> (<c>00014174</c>): non-zero when the machine's position is refused, by another live object
	/// within both body radii, a structure's collision volume, or a zone shape's
	/// (docs/es1/simulation/mech-locomotion.md#collision--mech_collisiontest-00014174). The answer is the refusing test's:
	/// 1 for an object, otherwise the volume's height as the shape tests answer it.
	/// </summary>
	public static int CollisionTest(Es1Mech mech, Es1SimGlobals globals) {
		short radius = mech.BodyRadius;
		foreach (var other in globals.LiveObjects) {
			// Retail reads the group's action through +0x3f unchecked; an object with no group is taken as holding none.
			if (ReferenceEquals(other, mech) || other.Group is { Action: not null }) {
				continue;
			}

			short otherRadius = other.BodyRadius;
			if (otherRadius == 0) {
				continue;
			}

			var p = other.Position;
			var q = mech.Position;
			if (SimMath.FastMagnitude3D(q.X - p.X, q.Y - p.Y, q.Z - p.Z) < radius + otherRadius) {
				other.RunInto();
				if (other.TargetClass == 0 && other is Es1Mech struck) {
					Es1MechDamage.CollisionDamage(mech, struck, globals);
				}

				return 1;
			}
		}

		int structure = Es1ShapeTests.GatherWalkCandidates(globals.Structures.Live, mech.Position);
		if ((short)structure != 0) {
			return 1;
		}

		return globals.ZoneShapes.WalkCollisionTest(mech.Position);
	}

	/// <summary>
	/// What <c>SimObject_PushTransform</c> (<c>00002184</c>) saves (docs/es1/simulation/object-classes.md#saving-and-restoring-the-transform):
	/// the position and each thread's record. Retail also saves the rotation and the node arrays; here the frame is built
	/// from the angles and the nodes are posed from the threads, so the position and the threads hold the same state. The
	/// angles are not in retail's save either, so a refused step keeps its turn.
	/// </summary>
	private readonly record struct TransformSave(Vec3i Position, AnimationThread.State[] Threads);

	/// <summary><c>SimObject_PushTransform</c> (<c>00002184</c>).</summary>
	private static TransformSave PushTransform(Es1Mech mech) =>
		new(mech.Position, mech.Shape?.Threads.Select(thread => thread.Capture()).ToArray() ?? []);

	/// <summary><c>SimObject_PopTransform</c> (<c>00002248</c>).</summary>
	private static void PopTransform(Es1Mech mech, in TransformSave saved) {
		mech.Position = saved.Position;
		if (mech.Shape is { } shape) {
			for (int i = 0; i < saved.Threads.Length; i++) {
				shape.Threads[i].Restore(saved.Threads[i]);
			}
		}
	}
}
