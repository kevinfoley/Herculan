using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Ai;

/// <summary>
/// The AI's movement primitives (docs/es1/simulation/mech-ai.md#navigation, which links ES2's derivation): the choice
/// <c>Ai_NavigationStep</c> (<c>ES1DBSIM 00017478</c>) makes between <c>Ai_DriveToPoint</c> (<c>00019580</c>),
/// <c>Ai_FollowRoute</c> (<c>00019604</c>) and <c>Ai_KeepFormation</c> (<c>00019660</c>), and the radar
/// <c>Ai_UpdateWeaponsFree</c> (<c>000161dc</c>) sets. Every steer is the bearing error shifted right by
/// <see cref="SteerShift"/> (docs/es1/simulation/mech-ai.md#the-state-functions).
/// </summary>
public static class Es1Navigation {
	/// <summary>The shift of a bearing error that makes a steer, encoded <c>SAR reg,0x64</c>, which the processor takes modulo 32.</summary>
	public const int SteerShift = 4;

	/// <summary>The ground range within which <see cref="DriveToPoint"/> answers that it has arrived.</summary>
	public const int ArrivalRange = 10000;

	/// <summary>The speed <see cref="DriveToPoint"/> asks for when the cruise speed <c>+0x1e6</c> is 0.</summary>
	public const short DefaultCruiseSpeed = 0xaa;

	/// <summary>The ground range to the post within which <see cref="KeepFormation"/> stands and matches the leader's heading.</summary>
	public const int OnStationRange = 2000;

	/// <summary>The ground range to the post up to which <see cref="KeepFormation"/> keeps station on a moving leader, and from which it closes at full speed.</summary>
	public const int StationKeepingRange = 25000;

	/// <summary>The leader's speeds that count as standing, −25 to 24: the unsigned test speed + 25 &lt; 50.</summary>
	public const int LeaderStandingBias = 25, LeaderStandingSpan = 50;

	/// <summary>The heading difference from the leader at and past which <see cref="KeepFormation"/> backs out of it.</summary>
	public const int FormationHeadingLimit = 0x2000;

	/// <summary>The shifts of the post's lateral and longitudinal offsets, and of the closing speed.</summary>
	public const int OffsetShift = 5, ClosingSpeedShift = 7;

	/// <summary>Full speed, and the steer's stop.</summary>
	public const short Full = 0x100;

	/// <summary>
	/// <c>Ai_NavigationStep</c>: with a squad verb (<c>+0x1d2</c>), <see cref="DriveToPoint"/> to the squad order's point
	/// <c>+0x1d4</c>; otherwise <see cref="FollowRoute"/> for the group's first member and <see cref="KeepFormation"/> for the
	/// others; then <see cref="UpdateWeaponsFree"/> and <c>Mech_CenterTorsoTick</c>(machine, 0).
	/// </summary>
	public static void NavigationStep(Es1Mech mech, Es1SimGlobals globals) {
		if (mech.SquadVerb != 0) {
			DriveToPoint(mech, mech.SquadPoint, globals);
		} else if (ReferenceEquals(Leader(mech), mech)) {
			FollowRoute(mech, globals);
		} else {
			KeepFormation(mech, globals);
		}

		UpdateWeaponsFree(mech);
		mech.Turret.CenterTorsoTick(globals.Clock, globals.TurretServos, 0);
	}

	/// <summary>
	/// <c>Ai_DriveToPoint</c>, (machine, <paramref name="point"/>): the control law at the cruise speed
	/// (<see cref="Es1Mech.CruiseSpeed"/>, or <see cref="DefaultCruiseSpeed"/> when it is 0) and the steer of the bearing
	/// error (<c>Math_HeadingToward</c>); answers whether <c>Math_DistanceBetweenPoints2D</c> from the point is under
	/// <see cref="ArrivalRange"/>, the range taken before the step.
	/// </summary>
	public static bool DriveToPoint(Es1Mech mech, Vec3i point, Es1SimGlobals globals) {
		short bearing = Es1MissionGroup.HeadingToward(mech.Position, point);
		int distance = Es1PlayerThink.DistanceBetweenPoints2D(point, mech.Position);
		short speed = mech.CruiseSpeed == 0 ? DefaultCruiseSpeed : mech.CruiseSpeed;
		mech.Locomotion.LocomotionTick(globals, 0, speed, Steer(bearing, mech.Heading));
		return distance < ArrivalRange;
	}

	/// <summary>
	/// <c>Ai_FollowRoute</c>: <c>Route_WaypointAt</c> of the waypoint after the group's cursor; with none the control law
	/// with nothing asked, otherwise <see cref="DriveToPoint"/> to it and, on arrival, <c>Route_AdvanceCursor</c>.
	/// </summary>
	public static void FollowRoute(Es1Mech mech, Es1SimGlobals globals) {
		// Retail reads the group through +0x3f unchecked; a machine with no group stands here, an engine guard.
		if (mech.Group is { } group && group.WaypointAt(group.RouteCursor + 1) is { } next) {
			if (DriveToPoint(mech, next, globals)) {
				group.AdvanceCursor();
			}
		} else {
			mech.Locomotion.LocomotionTick(globals, 0, 0, 0);
		}
	}

	/// <summary>
	/// <c>Ai_KeepFormation</c>: the post is the leader's position, the group's first member's, moved by the machine's own
	/// slot (<c>Mech_ApplyFormationOffset</c>, <c>00012ed0</c>, called directly). With <i>d</i> the ground range to the post
	/// (<c>Math_DistanceBetweenPoints2D</c>) and <i>h</i> the machine's heading less the leader's:
	/// <list type="bullet">
	/// <item>under <see cref="OnStationRange"/>, speed 0 and the steer (−<i>h</i>) &gt;&gt; 4, matching the leader's heading;</item>
	/// <item>up to <see cref="StationKeepingRange"/> with the leader moving: under <see cref="FormationHeadingLimit"/> of
	/// <i>h</i>, the machine's offset from the post rotated into the leader's frame (<c>Math_TransposeRotation2D</c>,
	/// <c>Math_Rotate2DPoint</c>) gives a lateral <i>x</i> and a longitudinal <i>y</i>, the steer −((<i>h</i> &gt;&gt; 4) +
	/// (−<i>x</i> &gt;&gt; 5)) and the speed the leader's <c>+0x224</c> + (−<i>y</i> &gt;&gt; 5), both as <c>int16</c>;
	/// otherwise the speed −<see cref="Full"/>, steering +<see cref="Full"/> for <i>h</i> under 1 and −<see cref="Full"/>
	/// above;</item>
	/// <item>otherwise the bearing error's steer at <i>d</i> &gt;&gt; 7, or <see cref="Full"/> from
	/// <see cref="StationKeepingRange"/>.</item>
	/// </list>
	/// </summary>
	public static void KeepFormation(Es1Mech mech, Es1SimGlobals globals) {
		// Retail reads the leader through the group unchecked; with none, nothing is asked here, an engine guard.
		if (Leader(mech) is not { } leader) {
			mech.Locomotion.LocomotionTick(globals, 0, 0, 0);
			return;
		}

		var post = Es1Formation.ApplyOffset(mech, leader.Position, globals.MechFormations);
		int distance = Es1PlayerThink.DistanceBetweenPoints2D(post, mech.Position);
		short bearingError = (short)(Es1MissionGroup.HeadingToward(mech.Position, post) - mech.Heading);
		short headingDifference = (short)(mech.Heading - leader.Heading);

		if (distance < OnStationRange) {
			mech.Locomotion.LocomotionTick(globals, 0, 0, (short)(-headingDifference >> SteerShift));
			return;
		}

		if (distance <= StationKeepingRange && (uint)(leader.Speed + LeaderStandingBias) >= LeaderStandingSpan) {
			int magnitude = headingDifference == short.MinValue ? short.MaxValue : Math.Abs((int)headingDifference);
			if (magnitude < FormationHeadingLimit) {
				// The leader's rotation block transposed: Math_TransposeRotation2D swaps the two off-diagonal entries.
				var frame = leader.Frame;
				(frame.M[1], frame.M[2]) = (frame.M[2], frame.M[1]);
				var position = mech.Position;
				var (lateral, longitudinal) = Es1Formation.Rotate2D(frame, position.X - post.X, position.Y - post.Y);
				int turn = (headingDifference >> SteerShift) + (-lateral >> OffsetShift);
				short speed = (short)(leader.Speed + (-longitudinal >> OffsetShift));
				mech.Locomotion.LocomotionTick(globals, 0, speed, (short)-turn);
			} else {
				mech.Locomotion.LocomotionTick(globals, 0, -Full, headingDifference < 1 ? Full : (short)-Full);
			}

			return;
		}

		short closing = distance < StationKeepingRange ? (short)(distance >> ClosingSpeedShift) : Full;
		mech.Locomotion.LocomotionTick(globals, 0, closing, (short)(bearingError >> SteerShift));
	}

	/// <summary>
	/// <c>Ai_UpdateWeaponsFree</c>: the radar <c>+0x90</c> from <see cref="Es1Mech.RadarKeptOn"/> (<c>+0xac</c>) when the
	/// group's first member is the player's machine (its <c>+0x9d</c>), from <see cref="Es1Mech.RadarSetting"/>
	/// (<c>+0x91</c>) otherwise. Retail reads the first member unchecked; with none, the radar setting here.
	/// </summary>
	public static void UpdateWeaponsFree(Es1Mech mech) =>
		mech.RadarOn = Leader(mech) is { IsPlayer: true } ? mech.RadarKeptOn : mech.RadarSetting;

	/// <summary>The bearing error's steer: (<c>int16</c>)(<paramref name="bearing"/> − <paramref name="heading"/>) &gt;&gt; <see cref="SteerShift"/>.</summary>
	public static short Steer(short bearing, short heading) => (short)((short)(bearing - heading) >> SteerShift);

	/// <summary>
	/// The group's first member, the first pointer of the array at the group's <c>+0xe</c>; null for a machine with no group
	/// or a group whose first ref built no machine, which retail would read through.
	/// </summary>
	public static Es1Mech? Leader(Es1Mech mech) => mech.Group is { Members: [Es1Mech first, ..] } ? first : null;
}
