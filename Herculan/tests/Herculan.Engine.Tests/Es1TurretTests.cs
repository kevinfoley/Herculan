using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Herculan.Es1.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>ES1's turret ticks, centring, gun convergence and servo latch on a constructed chassis.</summary>
public class Es1TurretTests {
	private const short Twist = 0, Pitch = 1;
	private const short TwistLimit = 14000, PitchMax = 3500, PitchMin = -2000;

	/// <summary>
	/// A full stick past either end stops each axis on its own bounds: the twist at ± its one limit, the pitch at its
	/// minimum. Catches the clamp dropped, the twist's lower bound taken as +limit or 0, or the pitch's minimum and
	/// maximum swapped or replaced by the twist's symmetric pair.
	/// </summary>
	[Fact]
	public void EachAxisStopsAtItsLimits() {
		var mech = NewMech(maxRate: 1000, accel: 0);
		var clock = new Es1SimClock();
		var servos = new Es1TurretServos();

		mech.Twist = TwistLimit - 500;
		mech.TwistRate = 1000;
		mech.Turret.TwistTick(clock, servos, Es1Turret.AxisFull);
		Assert.Equal(TwistLimit, mech.Twist);

		mech.Twist = -TwistLimit + 500;
		mech.TwistRate = -1000;
		mech.Turret.TwistTick(clock, servos, -Es1Turret.AxisFull);
		Assert.Equal(-TwistLimit, mech.Twist);

		mech.Pitch = PitchMin + 500;
		mech.PitchRate = -1000;
		mech.Turret.PitchTick(clock, servos, 0, -Es1Turret.AxisFull);
		Assert.Equal(PitchMin, mech.Pitch);
	}

	/// <summary>
	/// Centring drives each axis against its angle: a twist of 2000 asks for the full −<c>0x100</c>, a pitch of −1000 for
	/// +244. Catches the sign lost, the clamp dropped (−488 on the twist), or ES2's truncating Q10 multiply (+245 on the
	/// pitch).
	/// </summary>
	[Fact]
	public void CentringDrivesEachAxisAgainstItsAngle() {
		// A rate at full stick of 1.0 and an acceleration that reaches any target in a tick, so each rate is its axis.
		var mech = NewMech(maxRate: 0x100, accel: 0x7fff);
		mech.Twist = 2000;
		mech.Pitch = -1000;

		mech.Turret.CenterTorsoTick(new Es1SimClock(), new Es1TurretServos(), 0);

		Assert.Equal(-0x100, mech.TwistRate);
		Assert.Equal(244, mech.PitchRate);
	}

	/// <summary>
	/// At a range, a gun at the sight line's height points along it level, one beside it toes in level, and a hole is
	/// passed over; range 0 squares them all up. Catches the eye's lift not taken off the muzzle's Z (both pitch), the
	/// points given to <c>Math_EulerToward</c> in the wrong order, or range 0 leaving the last angles.
	/// </summary>
	[Fact]
	public void ConvergenceTiltsAtARangeAndClearsAtZero() {
		const short eyeLift = 500;
		var onTheLine = Mount(0, eyeLift);
		var beside = Mount(300, eyeLift);
		Es1WeaponMount?[] mounts = [onTheLine, null, beside];

		Es1Turret.ConvergeGuns(mounts, 5000, eyeLift);
		Assert.Equal(((short)0, (short)0, (short)0), onTheLine.Convergence);
		Assert.Equal(0, beside.Convergence.Pitch);
		Assert.NotEqual(0, beside.Convergence.Yaw);

		Es1Turret.ConvergeGuns(mounts, 0, eyeLift);
		Assert.Equal(((short)0, (short)0, (short)0), beside.Convergence);
	}

	/// <summary>
	/// The servo latch arms only past <c>0xc0</c> either way with the angle moved, holds while it moves, and clears when it
	/// stops. Catches <c>&gt;=</c> for <c>&gt;</c>, the angle-moved test dropped, a negative axis not taken as its
	/// magnitude, or the still-angle stop dropped.
	/// </summary>
	[Fact]
	public void ServoLatchArmsPastC0WithTheAngleMoved() {
		var servo = new Es1TurretServo();

		servo.Update(Es1TurretServo.Deflection, 100);
		Assert.False(servo.Latched);

		servo.Update(Es1TurretServo.Deflection + 1, 100);
		Assert.False(servo.Latched);

		servo.Update(-Es1TurretServo.Deflection - 1, 200);
		Assert.True(servo.Latched);

		servo.Update(Es1TurretServo.Deflection + 1, 300);
		Assert.True(servo.Latched);

		servo.Update(Es1TurretServo.Deflection + 1, 300);
		Assert.False(servo.Latched);
	}

	private static Es1Mech NewMech(short maxRate, short accel) {
		var chassis = new Es1HercSimDat {
			AnimId_TorsoTwist = Twist,
			TorsoTwistMaxRate = maxRate,
			TorsoTwistAccel = accel,
			TorsoTwistLimit = TwistLimit,
			AnimId_TorsoPitch = Pitch,
			TorsoPitchMaxRate = maxRate,
			TorsoPitchAccel = accel,
			TorsoPitchMax = PitchMax,
			TorsoPitchMin = PitchMin,
		};
		return new Es1Mech(new Es1MechType(chassis, Animation()), new Es1DwellJitter());
	}

	/// <summary>Two eight-frame sequences over one node with no transitions, so a seek lands anywhere in either.</summary>
	private static ShapeAnimation Animation() {
		var sequences = Enumerable.Range(0, 2).Select(_ => new ANSequence {
			Frames = Enumerable.Range(0, 8).Select(_ => new ANSequenceFrame { Duration = 100 }).ToArray(),
			PartIds = [0],
			TransformIndices = Enumerable.Repeat((short)0, 8).ToArray(),
		}).ToArray();
		var list = new ANAnimList {
			Sequences = sequences,
			Transforms = [new ANAnimListTransform()],
			DefaultTransforms = [0],
		};
		return ShapeAnimation.From(list)!;
	}

	/// <summary>An ECM on an invisible hardpoint at (<paramref name="x"/>, 0, <paramref name="z"/>), so no side offset applies.</summary>
	private static Es1WeaponMount Mount(short x, short z) {
		var hardpoint = new GunLayout.HardpointEntry {
			ConvergencePitchNode = -1,
			ConvergenceYawNode = -1,
			MountingCode = Es1MechType.InvisibleMounting,
			Offset = [x, 0, z],
		};
		return new Es1WeaponMount(Es1MountKind.Ecm, Es1WeaponMounts.EcmWeapon, hardpoint, new Weapons.WeaponMountTemplate(),
			null, null, _ => throw new InvalidOperationException("no convergence node"));
	}
}
