using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Herculan.Es1.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>ES1's throttle and control law on a constructed chassis whose sequences have no transitions.</summary>
public class Es1LocomotionTests {
	private const short Walk = 0, Run = 1, StopForward = 2, StopReverse = 3, Death = 4, TurnInPlace = 5, Twist = 6, Pitch = 7;
	private const short ForwardTop = 245, TopTurn = 1000, Threshold = 100;
	private const short FilePriority = 96;

	/// <summary>
	/// The constructor zeroes the two stop sequences' priority in the shared table and leaves the others. Catches the
	/// zeroing dropped, or indexed by the walk and run fields (<c>+0x0e</c>, <c>+0x10</c>) in place of <c>+0x12</c>, <c>+0x14</c>.
	/// </summary>
	[Fact]
	public void ConstructionZeroesOnlyTheStopSequencesPriority() {
		var mech = NewMech();
		var sequences = mech.Type.Animation!.Sequences;

		Assert.Equal(0, sequences[StopForward].Priority);
		Assert.Equal(0, sequences[StopReverse].Priority);
		Assert.All(new[] { Walk, Run, Death, TurnInPlace, Twist, Pitch }, s => Assert.Equal(FilePriority, sequences[s].Priority));
	}

	/// <summary>
	/// Pulling back from a small forward throttle stops it at 0 for a tick before it goes into reverse. Catches the
	/// sign-change test dropped, which takes it straight from <c>0x20</c> to <c>0x20 − 0x91</c>.
	/// </summary>
	[Fact]
	public void ThrottleWaitsATickAtZeroBeforeChangingSign() {
		var mech = NewMech();
		var globals = new Es1SimGlobals();
		mech.Throttle = 0x20;

		mech.Locomotion.ApplyThrottleInput(globals, 0x100, 0, 0);
		Assert.Equal(0, mech.Throttle);
		Assert.True(mech.ThrottleChanged);

		mech.Locomotion.ApplyThrottleInput(globals, 0x100, 0, 0);
		Assert.Equal(-0x91, mech.Throttle);
	}

	/// <summary>
	/// Mode 1 keeps the throttle at or above 0 and mode −1 at or below it, with the axis negated in −1. Catches the two
	/// bounds swapped, a lever-mode test off by one (<c>&lt; 0</c> for <c>&lt; 1</c>), or the axis' negation lost.
	/// </summary>
	[Fact]
	public void LeverModeClampsTheThrottleToOneSide() {
		Assert.Equal(0, Es1Locomotion.MoveThrottle(0, -0x91, 1));
		Assert.Equal(0, Es1Locomotion.MoveThrottle(0, 0x91, -1));
		Assert.Equal(-0x91, Es1Locomotion.MoveThrottle(0, -0x91, 0));
		Assert.Equal(Es1Locomotion.ThrottleFull, Es1Locomotion.MoveThrottle(0x3c0, 0x91, 1));

		var mech = NewMech();
		mech.Locomotion.ApplyThrottleInput(new Es1SimGlobals(), -0x100, 0, -1);
		Assert.Equal(-0x91, mech.Throttle);
	}

	/// <summary>
	/// The tent is 0 up to speed 45, the top turn rate half way to the top speed and half of it at the top speed, and
	/// 0 when a gait row held the turn. Catches ES2's tent left in: a speed of 1 to 44 taken as 45 and a rise from half
	/// the top turn rate, which give 500 at 45 and 750 at 95.
	/// </summary>
	[Theory]
	[InlineData(44, false, 0)]
	[InlineData(45, false, 0)]
	[InlineData(95, false, 500)]
	[InlineData(145, false, TopTurn)]
	[InlineData(-145, false, TopTurn)]
	[InlineData(ForwardTop, false, TopTurn / 2)]
	[InlineData(145, true, 0)]
	public void TurnTentRisesFromNothingAtSpeed45(short speed, bool heldTurn, short expected) {
		Assert.Equal(expected, Es1Locomotion.TurnTent(speed, ForwardTop, TopTurn, heldTurn));
	}

	/// <summary>
	/// In the run, a speed rising, or falling but still at the walk-to-run speed or above, leaves the playback rate as
	/// it was. Catches either row taking ES2's rate, the new speed.
	/// </summary>
	[Theory]
	[InlineData(200, 0)] // falling to 190
	[InlineData(150, ForwardTop)] // rising to 160
	public void RunRowsLeaveThePlaybackRate(short speed, short desired) {
		var mech = NewMech();
		mech.LocomotionThread!.SetSequence(Run, 0, 0);
		mech.Speed = speed;
		mech.PlaybackRate = 123;

		mech.Locomotion.LocomotionTick(new Es1SimGlobals(), 0, desired, 0);

		Assert.NotEqual(speed, mech.Speed);
		Assert.Equal(123, mech.PlaybackRate);
	}

	/// <summary>A steady walk at a crawl plays at its own speed. Catches ES2's crawl lift, which plays it at 60.</summary>
	[Fact]
	public void SteadyCrawlPlaysAtItsSpeed() {
		var mech = NewMech();
		mech.LocomotionThread!.SetSequence(Walk, 0, 0);
		mech.Speed = 20;

		mech.Locomotion.LocomotionTick(new Es1SimGlobals(), 0, 20, 0);

		Assert.Equal(20, mech.PlaybackRate);
	}

	/// <summary>
	/// At full steer and the tent's peak the turn rate reaches the top turn rate and the heading takes it. Catches
	/// ES2's Q10 1600 gain on the tent left in, which turns 1600/1024 times as fast.
	/// </summary>
	[Fact]
	public void FullSteerAtThePeakTurnsAtTheTopTurnRate() {
		var mech = NewMech();
		mech.LocomotionThread!.SetSequence(Walk, 0, 0);
		mech.Speed = 145;
		mech.RotationWord = 1;

		mech.Locomotion.LocomotionTick(new Es1SimGlobals(), 0, 145, 0x100);

		Assert.Equal(TopTurn, mech.TurnRate);
		Assert.Equal(TopTurn, mech.Rotation.Z);
		Assert.Equal(0, mech.RotationWord);
	}

	private static Es1Mech NewMech() {
		var chassis = new Es1HercSimDat {
			SpeedTurn = TopTurn,
			SpeedReverse = -100,
			SpeedForward = ForwardTop,
			SpeedAccelDecel = 10,
			TurnAccelDecel = 0x7fff,
			AnimId_Walk = Walk,
			AnimId_Run = Run,
			AnimId_StopMove = StopForward,
			AnimId_StopReverse = StopReverse,
			AnimId_Death = Death,
			AnimId_TurnInPlace = TurnInPlace,
			AnimId_TorsoTwist = Twist,
			AnimId_TorsoPitch = Pitch,
			GaitThreshold = Threshold,
			GaitThresholdReverse = -60,
		};
		return new Es1Mech(new Es1MechType(chassis, Animation()), new Es1DwellJitter());
	}

	/// <summary>Eight two-frame sequences over one node, each at <see cref="FilePriority"/>, with no transitions.</summary>
	private static ShapeAnimation Animation() {
		var sequences = Enumerable.Range(0, 8).Select(_ => new ANSequence {
			Frames = [new ANSequenceFrame { Duration = 100 }, new ANSequenceFrame { Duration = 100 }],
			PartIds = [0],
			TransformIndices = [0, 0],
			Priority = FilePriority,
		}).ToArray();
		var list = new ANAnimList {
			Sequences = sequences,
			Transforms = [new ANAnimListTransform()],
			DefaultTransforms = [0],
		};
		return ShapeAnimation.From(list)!;
	}
}
