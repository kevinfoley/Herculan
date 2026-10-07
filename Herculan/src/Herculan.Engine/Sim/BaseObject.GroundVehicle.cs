using Herculan.Engine.Numerics;
using Herculan.Engine.World;

namespace Herculan.Engine.Sim;

/// <summary>
/// The mobile ground vehicle — <c>StructureGroundVehicleVtable</c>'s <c>+0x18</c> tick slot
/// (<c>GroundVehicle_ThinkTick</c>, <c>0046a5d0</c>), and the only one of the five structure classes
/// that moves. Its fighting half
/// is borrowed whole from the armed tower; everything in this file is the moving half: a route
/// follow, a formation keep, one steering primitive, and a terrain conform that is the only thing
/// in the simulation that writes a structure's pitch and roll
/// (docs/retail/simulation/ground-vehicles.md).
///
/// <para><b>Nothing here is scaled by the tick length.</b> The original's steer, speed slew and
/// forward step are all per-call constants, the same way <see cref="SeekTurret"/>'s are — the
/// simulation assumes a fixed tick rate and this reproduces it rather than re-deriving a rate.</para>
/// </summary>
public sealed partial class BaseObject {
	/// <summary>
	/// <c>GroundVehicle_ThinkTick</c> (<c>0046a5d0</c>) — one tick of a ground vehicle. Returns
	/// whether the vehicle ran its movement half, which is what tells <see cref="Tick"/> to leave Z
	/// alone:
	/// <see cref="ConformToTerrain"/> has already set it.
	///
	/// <para>It fights first and moves second. <b>The fight is the armed tower's tick unchanged</b>
	/// for an armed type, with one addition — with no target held, the turret is seeked back toward
	/// centre by feeding <see cref="SeekTurret"/> its own current angles negated. An unarmed type
	/// runs the plain building tick instead.</para>
	///
	/// <para>The whole thing is gated on the group's first member answering
	/// <see cref="TargetClass.GroundVehicle"/>: a ground-vehicle type dropped into a group led by
	/// something else is an ordinary building and never moves.</para>
	///
	/// <para>A blocked step is handled the way <c>Mech_MovementTick</c> handles one — put the vehicle
	/// back where it started, stop it, and arm a back-off rather than detonate. The back-off is
	/// simpler than a machine's: no random side, just three seconds of crawling the other way, which
	/// <see cref="GroundVehicleLocomotion"/> applies by overriding the speed its steering asked
	/// for.</para>
	/// </summary>
	private bool GroundVehicleThinkTick(SimWorld world) {
		if (Group?.Leader is not { TargetClass: TargetClass.GroundVehicle }) {
			ThinkTick(world);
			return false;
		}

		if (Type.Armament == BaseArmament.None) {
			ThinkTick(world);
		} else {
			ArmedThinkTick(world);

			if (Target == null) {
				// Both axes end up driven negative: the elevation is handed in negated and the
				// traverse is negated inside the seek, so an idle turret walks back to centre.
				SeekTurret(world, (short)-_turretAngle[0], _turretAngle[1]);
			}
		}

		if (Destroyed) {
			return false;
		}

		var position = Position;
		short pitch = Pitch;
		short roll = Roll;
		int heading = Heading;

		GroundVehicleAdvance();
		ConformToTerrain(world);

		if (!GroundVehicleCollisionTest(world)) {
			return true;
		}

		_backoffReverse = Speed > 0;
		_backoffTimer = CollisionBackoffTime;
		Speed = 0;

		Position = position;
		Pitch = pitch;
		Roll = roll;
		Heading = heading;
		return true;
	}

	/// <summary>
	/// <c>GroundVehicle_Advance</c> (<c>0046a70c</c>) — steer, then step.
	///
	/// <para>The leader drives the route and everyone else keeps formation on it, where "the leader"
	/// is <see cref="FirstAbleMember"/> rather than the group's slot 0: unlike a HERC group, a ground
	/// convoy promotes when the vehicle in front is knocked out.</para>
	///
	/// <para>The step is <c>(0, speed, 0)</c> through the vehicle's own frame, so it drives along
	/// model Y — and along the frame <i>including</i> the lean <see cref="ConformToTerrain"/> gave it
	/// last tick, which foreshortens the ground distance covered on a slope.</para>
	/// </summary>
	private void GroundVehicleAdvance() {
		if (ReferenceEquals(FirstAbleMember(), this)) {
			GroundVehicleLeaderSteer();
		} else {
			GroundVehicleFollowerSteer();
		}

		Position = WorldFrame.TransformPoint(0, Speed, 0);
	}

	/// <summary>
	/// <c>GroundVehicle_LeaderSteer</c> (<c>0046a8e4</c>) — the group's route, one waypoint at a
	/// time. The same cursor rule the HERC AI follows: drive at the waypoint <i>after</i> the cursor
	/// and advance the cursor on arrival, so a route that has run out parks the vehicle.
	///
	/// <para>The bearing handed to <see cref="GroundVehicleDriveToPoint"/> is the route leg's own — the
	/// heading from the cursor's waypoint to the next one — which is what makes the drive converge
	/// onto the line between them rather than cutting the corner.</para>
	/// </summary>
	private void GroundVehicleLeaderSteer() {
		if (Group is not { } group || group.WaypointAt(group.RouteCursor + 1) is not { } next) {
			GroundVehicleLocomotion(0, 0);
			return;
		}

		var legFrom = group.WaypointAt(group.RouteCursor) ?? Position;
		short leg = Detection.HeadingToward(next, legFrom);

		if (GroundVehicleDriveToPoint(next, leg, 0)) {
			group.AdvanceRouteCursor();
		}
	}

	/// <summary>
	/// <c>GroundVehicle_FollowerSteer</c> (<c>0046a95c</c>) — hold station on the leader. Two
	/// arms, split on heading agreement rather than on range the way a HERC's four are.
	///
	/// <list type="bullet">
	/// <item><b>Pointed within 90° of the leader</b>, it drives at a point 20000 units ahead of its
	/// own formation post along the leader's heading, at the leader's speed trimmed by the
	/// along-track error taken in the leader's frame. There is no lateral steering term at all: the
	/// aim point being far ahead is what turns the follower in.</item>
	/// <item><b>Pointed further off than that</b>, it abandons the leader's heading and turns
	/// straight at the post, at a speed proportional to how far away it is.</item>
	/// </list>
	///
	/// <para>Both speeds are clamped to the same asymmetric <see cref="SpeedFull"/> /
	/// <see cref="SpeedReverseFull"/> pair — a ground vehicle reverses at a little over half its
	/// forward speed.</para>
	/// </summary>
	private void GroundVehicleFollowerSteer() {
		if (FirstAbleMember() is not { } leader) {
			GroundVehicleLocomotion(0, 0);
			return;
		}

		var post = FormationPostAround(leader.Position);
		short leaderHeading = (short)leader.Heading;
		short headingError = (short)(leaderHeading - (short)Heading);

		if (SaturatingAbs(headingError) >= FollowerHeadingBand) {
			short closing = ClampSpeed(GroundDistanceTo(post) >> 5);
			short bearing = Detection.HeadingToward(post, Position);
			GroundVehicleLocomotion((short)((short)(bearing - (short)Heading) >> SteerShift), closing);
			return;
		}

		var frame = leader.WorldFrame;
		frame.TransposeRotation();
		var local = frame.RotateVector(
			Position.X - post.X, Position.Y - post.Y, Position.Z - post.Z);

		// The leader's own +0x220, not its vtable +0x38: a structure's travel-speed slot answers zero
		// whatever it is doing, so nothing outside this family can see a vehicle moving.
		short leaderSpeed = leader is BaseObject vehicle ? vehicle.Speed : (short)0;
		short speed = ClampSpeed(leaderSpeed - (local.Y >> 5));
		var aim = SimTrig.OffsetPointByBearing(post, leaderHeading, FollowerAimLead);
		GroundVehicleDriveToPoint(aim, leaderHeading, speed);
	}

	/// <summary>
	/// <c>GroundVehicle_DriveToPoint</c> (<c>0046a854</c>) — drive at a point, and report whether it
	/// has been reached. The ground vehicle's counterpart to <c>Ai_DriveToPoint</c>, and the same
	/// <see cref="ArrivalRange"/>, but two things differ.
	///
	/// <para><b>It does not steer at the point.</b> It steers at a point offset from it along
	/// <paramref name="leg"/> by <c>9000 - range</c> — so while the vehicle is further out than 9000
	/// the aim point sits <i>short</i> of the destination, back down the incoming line, and inside
	/// 9000 it swings past it. That is what pulls a convoy onto its route leg instead of letting each
	/// vehicle cut its own corner.</para>
	///
	/// <para><b>And its steering gain is four times a HERC's</b> — the bearing error over 16, not
	/// over 64.</para>
	/// </summary>
	/// <param name="speed">Zero means "no speed stated", which takes <see cref="DefaultSpeed"/>.</param>
	private bool GroundVehicleDriveToPoint(Vec3i point, short leg, short speed) {
		int range = GroundDistanceTo(point);
		var aim = SimTrig.OffsetPointByBearing(point, leg, ApproachLead - range);
		short bearing = Detection.HeadingToward(aim, Position);

		GroundVehicleLocomotion((short)((short)(bearing - (short)Heading) >> SteerShift),
			speed != 0 ? speed : DefaultSpeed);
		return range < ArrivalRange;
	}

	/// <summary>
	/// <c>GroundVehicle_LocomotionTick</c> (<c>0046a798</c>) — the ground vehicle's whole control
	/// law, and the counterpart of <c>Mech_LocomotionTick</c>: everything above decides a steer and a
	/// speed and hands them here.
	///
	/// <para>The speed is slewed toward the demand at <see cref="SpeedSlew"/> a tick. The steer is
	/// clamped to one stick's travel and turned straight into heading at
	/// <see cref="SteerGain"/> — no turn rate, no inertia, no gear.</para>
	///
	/// <para><b>A back-off in progress overrides the speed and only the speed.</b> The vehicle keeps
	/// steering wherever its navigation wants while it crawls away from whatever it hit.</para>
	/// </summary>
	private void GroundVehicleLocomotion(short steer, short speed) {
		if (SimMath.CountdownTimerTick(ref _backoffTimer) != 0) {
			speed = _backoffReverse ? (short)-BackoffSpeed : BackoffSpeed;
		}

		steer = ClampAxis(steer);

		short current = Speed;
		SimMath.RateLimitedMoveToward(ref current, speed, SpeedSlew);
		Speed = current;

		Heading = (Heading + SimMath.Q8Multiply(SteerGain, steer)) & 0xffff;
	}

	/// <summary>
	/// <c>SimObject_ConformToTerrain</c> (<c>004029d8</c>) — sit the vehicle on the ground it is
	/// standing on, probing at <see cref="SimObject.ShapeRadius"/>; see <see cref="TerrainConform"/>.
	/// The one thing in the simulation that writes a structure's <see cref="SimObject.Pitch"/> and
	/// <see cref="SimObject.Roll"/>.
	/// </summary>
	private void ConformToTerrain(SimWorld world) {
		var (pitch, roll, z) = TerrainConform.Apply(world.Terrain, WorldFrame, ShapeRadius);
		Pitch = pitch;
		Roll = roll;
		Position = new Vec3i(Position.X, Position.Y, z);
	}

	/// <summary>
	/// <c>GroundVehicle_CollisionTest</c> (<c>0046a510</c>) — whether the step just taken is
	/// refused. <c>Mech_CollisionTest</c>'s two object sweeps standing alone: the same asymmetric
	/// <see cref="SimObject.HitRadius"/>-against-<see cref="SimObject.CollisionRadius"/> pair, the
	/// same group-action gate, and the same structure-volume sweep behind it.
	///
	/// <para>Two things a machine's does that this does not: there is no terrain test — a ground
	/// vehicle will drive up anything — and a collision does no damage to what it hit. It does still
	/// set <see cref="SimObject.RunInto"/> on the blocker, which makes this the second of the two
	/// writers a charging HERC detonates on.</para>
	/// </summary>
	private bool GroundVehicleCollisionTest(SimWorld world) {
		var position = Position;
		var objects = world.Objects;

		for (int i = 0; i < objects.Count; i++) {
			var other = objects[i];
			if (ReferenceEquals(other, this) || other.Removed || other.AwaitingDeployment
					|| other.CollisionRadius == 0) {
				continue;
			}

			var theirs = other.Position;
			int distance = SimMath.FastMagnitude3D(
				position.X - theirs.X, position.Y - theirs.Y, position.Z - theirs.Z);

			if (distance >= HitRadius + other.CollisionRadius) {
				continue;
			}

			other.RunInto = true;
			return true;
		}

		// The vehicle is itself a structure with a collision volume, and passes itself as the
		// gather's exclusion so it is not stopped by its own footprint.
		return Deployment.StructureInTheWay(world, position, this);
	}

	/// <summary>
	/// <c>Group_FirstAbleMember</c> (<c>0046a4b8</c>) — the first of the group's members that is
	/// neither crippled nor destroyed, and this family's whole notion of a leader. Null once every
	/// member is out of the fight.
	///
	/// <para>The original's loop gives up after four members whatever the group's real size, so a
	/// fifth vehicle can never lead; the cap is reproduced.</para>
	/// </summary>
	private SimObject? FirstAbleMember() {
		if (Group is not { } group) {
			return null;
		}

		int count = System.Math.Min(group.Members.Count, MaxLeaderCandidates);
		for (int i = 0; i < count; i++) {
			if (!group.Members[i].Neutralised) {
				return group.Members[i];
			}
		}

		return null;
	}

	/// <summary>
	/// <c>Base_ApplyFormationOffset</c> (<c>00405c04</c>) applied to a live anchor — the same
	/// <c>BFORMS.DAT</c> offset that spread this structure over its group's spawn point, which a
	/// follower re-reads every tick it holds station. Slot 0 takes none.
	///
	/// <para><b>The heading is the group's slot-0 member's, not the anchor's.</b>
	/// <c>Formation_RotateAndAddOffset</c> (<c>00411d64</c>) reaches past its caller for
	/// <c>group+0xc</c>'s first entry, and the group's member array is never compacted — so once the
	/// vehicle in the lead slot is knocked out, a convoy is anchored on its new leader while still
	/// dressed on the wreck's last heading.</para>
	/// </summary>
	private Vec3i FormationPostAround(Vec3i anchor) {
		if (FormationOffset is not { } offset) {
			return anchor;
		}

		int heading = Group?.Leader?.Heading ?? 0;
		short cos = BinaryAngle.Cos(heading);
		short sin = BinaryAngle.Sin(heading);

		return new Vec3i(
			anchor.X + (int)(((long)offset.X * cos - (long)offset.Y * sin + 0x2000) >> 14),
			anchor.Y + (int)(((long)offset.X * sin + (long)offset.Y * cos + 0x2000) >> 14),
			anchor.Z);
	}

	/// <summary>
	/// This structure's <c>BFORMS.DAT</c> slot offset, resolved once at mission load by the lookup
	/// that placed it. Null for the group's first-claimed slot and for a formation that states none.
	/// </summary>
	public (int X, int Y)? FormationOffset { get; set; }

	/// <summary><c>base+0x220</c> — how far the vehicle steps along model Y each tick.</summary>
	public short Speed { get; private set; }

	/// <summary>
	/// <c>base+0x223</c> — what is left of the post-collision back-off, in timer units rather than
	/// milliseconds (<see cref="SimMath.CountdownTimerTick"/>).
	/// </summary>
	private short _backoffTimer;

	/// <summary><c>base+0x227</c> — whether that back-off crawls backwards. Set from the speed the
	/// collision interrupted, so a vehicle backs out the way it came in.</summary>
	private bool _backoffReverse;

	/// <summary>How many members the original's leader scan looks at, whatever the group holds.</summary>
	private const int MaxLeaderCandidates = 4;

	/// <summary>Forward speed clamp — the same <c>0x100</c> a HERC's throttle axis runs to.</summary>
	private const short SpeedFull = 0x100;

	/// <summary>Reverse speed clamp, and not the mirror of <see cref="SpeedFull"/>.</summary>
	private const short SpeedReverseFull = -0x96;

	/// <summary>Speed used when a caller states none — the original's literal <c>0xaa</c>.</summary>
	private const short DefaultSpeed = 0xaa;

	/// <summary>How far the speed may move toward its demand in one tick.</summary>
	private const short SpeedSlew = 0x1e;

	/// <summary>Q8 gain from clamped steer to heading change — a little over 200 BAM at full lock.</summary>
	private const short SteerGain = 200;

	/// <summary>How far a bearing error is divided down before it becomes a steer.</summary>
	private const int SteerShift = 4;

	/// <summary>Speed the back-off crawls at, in whichever direction it picked.</summary>
	private const short BackoffSpeed = 200;

	/// <summary>How long a blocked vehicle backs off for — 3000 timer units, about 1.5 seconds.</summary>
	private const short CollisionBackoffTime = 3000;

	/// <summary>Ground range inside which a destination counts as reached.</summary>
	private const int ArrivalRange = 10000;

	/// <summary>Where <see cref="GroundVehicleDriveToPoint"/>'s aim point crosses its destination.</summary>
	private const int ApproachLead = 9000;

	/// <summary>How far ahead of its post a follower aims, along the leader's heading.</summary>
	private const int FollowerAimLead = 20000;

	/// <summary>
	/// Heading disagreement at which a follower stops flying formation and turns for its post.
	/// </summary>
	private const short FollowerHeadingBand = 0x4000;

	/// <summary>The original's own asymmetric speed clamp.</summary>
	private static short ClampSpeed(int value) =>
		value >= SpeedFull ? SpeedFull
		: value < SpeedReverseFull + 1 ? SpeedReverseFull
		: (short)value;
}
