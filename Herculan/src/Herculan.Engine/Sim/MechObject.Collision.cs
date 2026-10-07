using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// <c>Mech_CollisionTest</c> (<c>00418f74</c>) — whether <see cref="MovementTick"/>'s step is refused,
/// and what a refusal sets off: the AI's back-off, the damage of running into another machine, and
/// the player's slide down steep ground and the landing at its foot.
/// See docs/retail/simulation/mech-locomotion.md, "Collision".
/// </summary>
public sealed partial class MechObject {
	/// <summary>
	/// How long a HERC keeps backing away from something it walked into, in timer units.
	/// <c>Mech_MovementTick</c>'s own constant. Only AI machines arm it — a blocked player just
	/// stops.
	/// </summary>
	private const int CollisionBackoffTime = 10000;

	/// <summary>
	/// The side a collision commits an AI machine to while it clears, biasing
	/// <see cref="ObstacleAvoidance"/> for the length of the back-off. Which sign it takes is a
	/// coin flip, exactly as in the original.
	/// </summary>
	private const short CollisionBackoffSide = 5000;

	// Post-collision back-off, for AI machines: a countdown (mech+0x26e) during which desired speed
	// is pinned to one extreme so the machine walks itself clear of whatever it hit, plus the side
	// (mech+0x254) a coin flip committed it to, which biases the obstacle avoidance while it clears.
	private int _backoffTimer;
	private bool _backoffReverse;
	private short _backoffSide;

	// The player's slide down steep ground: the X/Y displacement added to the position every tick
	// the slide runs, so a velocity, and its flag. DBSIM keeps these as three globals because only one
	// mech is ever the player's; they are per-object here for the same reason SimWorld has no
	// globals.
	private int _slideX;
	private int _slideY;
	private bool _sliding;

	/// <summary>
	/// <c>mech+0x2b0</c> — the structure whose body radius this machine's position was inside at its
	/// last collision test, the last such of the sweep, or null. Its one reader is the frame's object
	/// filing, which files the machine under that structure's terrain cell for drawing. See
	/// docs/retail/simulation/mech-locomotion.md, "The structure a machine stands in".
	/// </summary>
	public SimObject? StandingIn { get; private set; }

	/// <summary>
	/// <c>Mech_CollisionTest</c> (<c>00418f74</c>) — whether the machine's new position is refused,
	/// either by another object or by the ground being too steep to stand on.
	///
	/// <para><b>The gap is asymmetric</b>, as the original's is: this machine contributes its own
	/// <see cref="HitRadius"/> and the other object its <see cref="SimObject.CollisionRadius"/>,
	/// which is a different figure with different values — zero for a flyer and for every static
	/// structure.</para>
	///
	/// <para>A structure the radius test walks past is stopped by its collision volume instead —
	/// <see cref="BaseObject.BlocksWalker"/>, a separate sweep in the original too. Between them no
	/// structure is walked through.</para>
	///
	/// <para>Running into another machine hurts both of them — see
	/// <see cref="CollisionDamage"/>. The sweep also records <see cref="StandingIn"/>, ahead of the gap
	/// test.</para>
	///
	/// <para>The sweep's first test <i>is</i> here: an object whose mission group carries an action
	/// is skipped outright, before any distance is measured. See
	/// <see cref="SimObject.AwaitingDeployment"/> for why the retail missions depend on it.</para>
	/// </summary>
	private bool CollisionTest(SimWorld world) {
		var position = Position;
		StandingIn = null;

		var objects = world.Objects;
		for (int i = 0; i < objects.Count; i++) {
			var other = objects[i];
			if (ReferenceEquals(other, this) || other.Removed || other.AwaitingDeployment) {
				continue;
			}

			var theirs = other.Position;
			int distance = SimMath.FastMagnitude3D(
				position.X - theirs.X, position.Y - theirs.Y, position.Z - theirs.Z);

			if (other.TargetClass == TargetClass.Structure && distance < (short)other.HitRadius) {
				StandingIn = other;
			}

			if (other.CollisionRadius == 0) {
				continue;
			}

			if (distance >= HitRadius + other.CollisionRadius) {
				continue;
			}

			// "Something ran into me", on whatever was blocked rather than only on a machine. Its one
			// reader is the ramming state's move slot -- see SimObject.RunInto.
			other.RunInto = true;

			// Only a machine is hurt by the impact -- the original's gate is the struck object's
			// target class, and MechObject is the only class that answers TargetClass.Herc.
			if (other is MechObject struck) {
				CollisionDamage(world, struck);
			}

			return true;
		}

		// The second object test, and a wholly separate one: every structure the radius test above
		// passes over is stopped here by its collision volume instead.
		if (Deployment.StructureInTheWay(world, position)) {
			return true;
		}

		var normal = world.Terrain.SurfaceNormalAt(position.X, position.Y);

		// Ground steeper than about 45 degrees is not walkable. Off the grid entirely counts as
		// steep, which keeps a computer-piloted machine inside the zone.
		bool tooSteep = normal is not { } face || System.Math.Abs(face.Z) < SteepNormalZ;

		if (!IsPlayer) {
			return tooSteep;
		}

		if (tooSteep) {
			// Uphill onto a cliff is refused outright; downhill onto one turns into a slide.
			if (!_sliding && _slideOrigin.Z < position.Z) {
				return true;
			}

			_sliding = true;
			// The original reads the normal unguarded here, through a null pointer off the grid
			// (docs/retail/simulation/mech-locomotion.md, "Open"); skipping it is this engine's.
			if (normal is { } slope) {
				_slideX += SimMath.Q10Multiply(10, slope.X);
				_slideY += SimMath.Q10Multiply(10, slope.Y);
			}
		} else if (_sliding) {
			_sliding = false;
			_lastSlideSpeed = SimMath.FastMagnitude2D(_slideX, _slideY);
			_slideX = 0;
			_slideY = 0;
			SlideLandingDamage(world, _lastSlideSpeed);
		}

		return false;
	}

	/// <summary>
	/// <b>Walking into another machine hurts both of you</b>, through the same explosive-damage slot
	/// a blast uses. It is a direct call on each party rather than a sweep, so a third machine
	/// standing beside the crash takes nothing.
	///
	/// <para><b>Only a machine.</b> A structure or an aircraft blocks the move and is not hurt by
	/// it.</para>
	///
	/// <para><b>What lands is a difference of momenta, not a speed.</b> Each side's speed is weighed
	/// by its own <see cref="MechTypeRecord.Mass"/> in Q10, and the other machine's is taken as the
	/// component of <i>its</i> travel along <i>this</i> machine's forward axis — so a head-on meeting
	/// adds (the term is negative and is subtracted) and being rear-ended by something slower
	/// subtracts. Both machines then take the same figure. The threshold under it is what keeps a
	/// machine shuffling against a wall from grinding itself down.</para>
	///
	/// <para><b>The impact point mixes two frames</b>, and that is the original's arithmetic: X and Y
	/// are the world midpoint of the two machines, but Z is the lower of the two cockpit-eye nodes'
	/// <i>model-space</i> heights — roughly torso height above each machine's own feet — used as
	/// though it were a world height. Reproduced as-is; see KNOWN_ISSUES.md.</para>
	/// </summary>
	private void CollisionDamage(SimWorld world, MechObject other) {
		// Rotation only: the original copies the two matrices without their translations, so this is
		// a direction being re-expressed, not a point being moved.
		var mine = Rotation();
		mine.TransposeRotation();

		var theirTravel = other.Rotation().RotateVector(0, other.Speed, 0);
		int closing = mine.RotateVector(theirTravel.X, theirTravel.Y, theirTravel.Z).Y;

		int impulse = SimMath.Q10Multiply(Type.Mass, Speed)
			- SimMath.Q10Multiply(other.Type.Mass, closing);
		int damage = SimMath.Q10Multiply(CollisionDamageScale, impulse);

		if (damage <= CollisionDamageThreshold) {
			return;
		}

		var theirs = other.Position;
		var at = new Vec3i(
			(theirs.X + Position.X) >> 1,
			(theirs.Y + Position.Y) >> 1,
			System.Math.Min(EyeNodeHeight(), other.EyeNodeHeight()));

		ExplosiveDamage(world, (short)damage, at, CollisionBlastRadius, other);
		other.ExplosiveDamage(world, (short)damage, at, CollisionBlastRadius, this);
	}

	/// <summary>
	/// The landing at the bottom of a slide — the fourth and last thing
	/// <see cref="SimWorld.Difficulty"/> scales, and the player's alone, since only the player's
	/// machine slides.
	///
	/// <para>Six leg components are each written a figure drawn independently: a base of
	/// <c>slideSpeed</c> scaled by the difficulty, plus a roll over three times that base. So the
	/// spread is wide and no two legs take the same damage, and a harder setting hurts more — this
	/// is the one difficulty table that runs against the player in both directions at once, since it
	/// is their own machine it is applied to.</para>
	///
	/// <para>The damage goes through <see cref="ComponentDamageWrite"/> with no attacker, so it
	/// cascades and can cripple or immobilise exactly as a shot would, and nothing is credited with
	/// the kill if it finishes the machine off. An invulnerable machine takes none of it, because
	/// that gate is inside the write.</para>
	///
	/// <para>The landing also jolts the cockpit, through <see cref="CockpitHits"/> — the second of
	/// the shake's two triggers, and the ungated one: the direct-fire site tests who is flying and
	/// how far gone the cockpit is, and this one calls it on any landing that got past the speed
	/// threshold. See docs/retail/formats/cockpit-canopy-palette.md, "The damage shake".</para>
	/// </summary>
	private void SlideLandingDamage(SimWorld world, int slideSpeed) {
		if (slideSpeed <= SlideDamageMinimumSpeed) {
			return;
		}

		// Short, and deliberately so: the original's own casts, and a long enough slide wraps them.
		short baseDamage = unchecked((short)SimMath.Q10Multiply(
			SlideDamageScale[world.Difficulty], slideSpeed));
		short spread = unchecked((short)(baseDamage * 3));

		for (int component = FirstLegComponent; component <= LastLegComponent; component++) {
			ComponentDamageWrite(world, (short)component,
				unchecked((short)(world.Random.NextBelow(spread) + baseDamage)), null);
		}

		CockpitHits++;
		world.Sounds?.Play(SoundId.Collision);
	}

	/// <summary>
	/// <c>SlideDamageScaleByDifficulty</c> (<c>0049a058</c>) — the Q10 factor the slide's speed becomes damage through, by
	/// <see cref="SimWorld.Difficulty"/>. Unlike the other three difficulty tables this one is only
	/// ever applied to the player's own machine.
	/// </summary>
	public static readonly short[] SlideDamageScale = { 400, 800, 1200, 1600 };

	/// <summary>A slide no faster than this, in world units a tick, lands for nothing — the original's literal <c>0xfa</c>.</summary>
	private const int SlideDamageMinimumSpeed = 0xfa;

	/// <summary>
	/// The six leg components the landing writes, which the original names by literal index rather
	/// than reading any table: the two upper legs and what hangs off them.
	/// </summary>
	private const int FirstLegComponent = 7;

	/// <inheritdoc cref="FirstLegComponent"/>
	private const int LastLegComponent = 12;

	/// <summary>
	/// The model-space height of the machine's cockpit-eye node, or zero when the shape has no such
	/// node — the original's own fallback, which reads an identity transform. Only
	/// <see cref="CollisionDamage"/> reads it.
	/// </summary>
	private int EyeNodeHeight() {
		int transformId = Animation?.TransformIdOfPart(Type.CameraPartId) ?? -1;
		return transformId < 0 || Shape == null ? 0 : Shape.NodeTransform(transformId).Z;
	}

	/// <summary>The Q10 factor the momentum difference is scaled by before it becomes damage.</summary>
	private const int CollisionDamageScale = 300;

	/// <summary>Below this the collision does nothing at all — the original's literal 200.</summary>
	private const int CollisionDamageThreshold = 200;

	/// <summary>
	/// How far the impact reaches on each machine, in world units. Small: it is applied through the
	/// explosive path, so this is the denominator each component's falloff is measured against, and
	/// 1200 keeps it to the parts nearest the point of contact.
	/// </summary>
	private const int CollisionBlastRadius = 0x4b0;

	/// <summary>
	/// How shallow a surface normal's vertical component may get before the ground counts as
	/// unwalkable — <c>Mech_CollisionTest</c>'s own threshold, against normals scaled to
	/// <see cref="Terrain.HeightGrid.NormalOne"/>. It works out at about 45 degrees.
	/// </summary>
	private const int SteepNormalZ = 0x5aa;

	private Vec3i _slideOrigin;
	private int _lastSlideSpeed;

	/// <summary>
	/// How fast the last slide down a steep face was moving when it ended, in world units a tick.
	/// Above 250 the original applies leg damage on landing.
	/// </summary>
	public int LastSlideSpeed => _lastSlideSpeed;
}
