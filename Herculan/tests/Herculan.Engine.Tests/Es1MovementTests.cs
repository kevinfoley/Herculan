using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;
using Herculan.Es1.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>ES1's move, its collision test and the behaviour tick, on constructed machines and shapes.</summary>
public class Es1MovementTests {
	private const short Walk = 0, StopForward = 2, BodyRadius = 750, Height = 1000;

	/// <summary>
	/// A move refused both ways restores the thread (its rate back to the saved one while the machine's is negated),
	/// zeroes the throttle with its gauge flag, and stops the machine. Catches either pop dropped, the throttle or
	/// <c>+0x8d</c> left, or the second refusal not zeroing the speed.
	/// </summary>
	[Fact]
	public void RefusedMoveRestoresTheThreadAndZeroesTheThrottle() {
		var globals = Globals(new Vec3i(0, 0, 0));
		var mech = NewMech(globals, new Vec3i(100, 100, 0));
		mech.Speed = 100;
		mech.PlaybackRate = 100;
		mech.Throttle = 0x200;
		short savedRate = mech.LocomotionThread!.Rate;

		Es1MechMovement.MovementTick(mech, globals);

		Assert.Equal(savedRate, mech.LocomotionThread.Rate);
		Assert.Equal(-100, mech.PlaybackRate);
		Assert.Equal(0, mech.Speed);
		Assert.Equal(0, mech.Throttle);
		Assert.True(mech.ThrottleChanged);
	}

	/// <summary>
	/// A refused machine other than the player's starts the unstick, 20000 long, ±5000 to the side and backwards for a
	/// forward speed; the player's does not. Catches the player test inverted or dropped, ES2's 10000, or the direction
	/// read after the bounce has negated the speed.
	/// </summary>
	[Fact]
	public void OnlyAMachineOtherThanThePlayersUnsticks() {
		var globals = Globals(new Vec3i(0, 0, 0));
		var other = NewMech(globals, new Vec3i(100, 100, 0));
		var player = NewMech(globals, new Vec3i(100, 100, 0));
		player.IsPlayer = true;
		globals.LiveObjects.Clear();
		other.Speed = player.Speed = 100;

		Es1MechMovement.MovementTick(other, globals);
		Es1MechMovement.MovementTick(player, globals);

		Assert.Equal(20000, other.UnstickCountdown);
		Assert.Equal(5000, Math.Abs((int)other.UnstickSide));
		Assert.True(other.UnstickReverse);
		Assert.Equal(0, player.UnstickCountdown);
		Assert.Equal(0, player.UnstickSide);
		Assert.False(player.UnstickReverse);
	}

	/// <summary>
	/// Another machine refuses the move nearer than both body radii together and is told it was run into; one farther, or
	/// one whose group still holds its action, does not. Catches one radius tested alone, the action test dropped, or
	/// slot <c>+0x6c</c> called on the mover.
	/// </summary>
	[Fact]
	public void AnotherMachineWithinBothRadiiRefusesTheMove() {
		var globals = Globals(null);
		var mech = NewMech(globals, new Vec3i(0, 0, 0));
		var other = NewMech(globals, new Vec3i(2 * BodyRadius - 10, 0, 0));

		Assert.Equal(1, Es1MechMovement.CollisionTest(mech, globals));
		Assert.True(other.RanInto);
		Assert.False(mech.RanInto);

		other.Group = new Es1MissionGroup { Action = new Es1Action() };
		Assert.Equal(0, Es1MechMovement.CollisionTest(mech, globals));

		other.Group = null;
		other.Position = new Vec3i(2 * BodyRadius + 10, 0, 0);
		Assert.Equal(0, Es1MechMovement.CollisionTest(mech, globals));
	}

	/// <summary>
	/// A point is tested in the shape's own frame: through the shape's rotation transposed, within the shape's radius.
	/// Catches the transpose dropped, which turns the point the other way and off the grid, or the radius test dropped,
	/// which lets a point in the grid's corner beyond the radius refuse.
	/// </summary>
	[Fact]
	public void APointInsideAZoneShapesVolumeRefusesIt() {
		var frame = Transform3.FromEuler(0, 0, 0x2000);
		frame.X = 10000;
		frame.Y = 20000;
		var shapes = new[] { new Es1ShapeObject(frame, 500, Volume()) };

		var inside = frame.TransformPoint(100, 300, 0);
		Assert.Equal(Height, Es1ShapeTests.WalkCollisionTest(shapes, inside));

		var corner = frame.TransformPoint(450, 450, 0);
		Assert.Equal(0, Es1ShapeTests.WalkCollisionTest(shapes, corner));
	}

	/// <summary>
	/// The tick runs the reassess only at a count of 0, then the move before the think, and a think's non-zero answer
	/// zeroes the count; <c>+0xa9</c> skips the think once and is cleared; the first tick after the install is not
	/// counted. Catches the reassess run every tick, the think's answer ignored, the order swapped, the skip not cleared,
	/// or the first tick counted.
	/// </summary>
	[Fact]
	public void AiTickReassessesOnlyAtCountZeroAndTheThinkZeroesIt() {
		var globals = Globals(null);
		var mech = NewMech(globals, new Vec3i(0, 0, 0));
		var calls = new List<string>();
		var state = new Es1BehaviourDescriptor("test", 1000, 0,
			think: (_, _) => { calls.Add("think"); return 1; },
			move: (_, _) => { calls.Add("move"); return 1; },
			reassess: (_, _) => { calls.Add("reassess"); return 1; },
			lineIndex: 0);
		mech.Behaviour.SetState(state, globals.DwellJitter);

		Es1MechBehaviour.AiTick(mech, globals);
		Assert.Equal(new[] { "move", "think" }, calls);
		Assert.Equal(0, mech.Behaviour.Countdown);
		Assert.Equal(0, mech.Behaviour.TickCount);

		calls.Clear();
		mech.ThinkSuppressed = true;
		Es1MechBehaviour.AiTick(mech, globals);
		Assert.Equal(new[] { "reassess", "move" }, calls);
		Assert.False(mech.ThinkSuppressed);
		Assert.Equal(1, mech.Behaviour.TickCount);
	}

	/// <summary>
	/// Every machine is built deciding; the reassess installs player for the player's machine once its countdown has run
	/// out, and leaves another machine deciding. Catches the player arm of <c>Mech_AiSelectBehaviour</c> dropped, which
	/// leaves the player's machine with no move.
	/// </summary>
	[Fact]
	public void ThePlayersMachineReachesPlayerFromDeciding() {
		var globals = Globals(null);
		var player = NewMech(globals, new Vec3i(0, 0, 0));
		var other = NewMech(globals, new Vec3i(50000, 0, 0));
		player.IsPlayer = true;
		Assert.Same(Es1MechBehaviour.States[Es1MechBehaviour.Deciding], player.Behaviour.Descriptor);

		for (int tick = 0; tick < 3; tick++) {
			Es1MechBehaviour.AiTick(player, globals);
			Es1MechBehaviour.AiTick(other, globals);
		}

		Assert.Same(Es1MechBehaviour.States[Es1MechBehaviour.Player], player.Behaviour.Descriptor);
		Assert.Same(Es1MechBehaviour.States[Es1MechBehaviour.Deciding], other.Behaviour.Descriptor);
	}

	/// <summary>Globals whose zone has one unrotated shape at <paramref name="zoneShape"/>, or none.</summary>
	private static Es1SimGlobals Globals(Vec3i? zoneShape) {
		var globals = new Es1SimGlobals();
		if (zoneShape is { } at) {
			var frame = Transform3.Identity;
			frame.X = at.X;
			frame.Y = at.Y;
			frame.Z = at.Z;
			globals.ZoneShapes = new Es1ZoneShapes([new Es1ShapeObject(frame, 2000, Volume())]);
		}

		return globals;
	}

	/// <summary>A one-cell volume, 512 units a side from the shape's origin, <see cref="Height"/> tall.</summary>
	private static Es1GridShape Volume() {
		var heights = new int[256];
		heights[1] = Height;
		heights[255] = Height;
		return new Es1GridShape(new GridShapeCollision(1, 1, 0, 0, 9, heights, [[1]]));
	}

	private static Es1Mech NewMech(Es1SimGlobals globals, Vec3i position) {
		var chassis = new Es1HercSimDat {
			SpeedReverse = -100,
			SpeedForward = 245,
			GaitThresholdReverse = -60,
			AnimId_Walk = Walk,
			AnimId_Run = 1,
			AnimId_StopMove = StopForward,
			AnimId_StopReverse = 3,
			AnimId_Death = 4,
			AnimId_TurnInPlace = 5,
			AnimId_TorsoTwist = -1,
			AnimId_TorsoPitch = -1,
			BodyRadius = BodyRadius,
		};
		var sequences = Enumerable.Range(0, 6).Select(_ => new ANSequence {
			Frames = [new ANSequenceFrame { Duration = 100 }, new ANSequenceFrame { Duration = 100 }],
			PartIds = [0],
			TransformIndices = [0, 0],
		}).ToArray();
		var animation = ShapeAnimation.From(new ANAnimList {
			Sequences = sequences,
			Transforms = [new ANAnimListTransform()],
			DefaultTransforms = [0],
		})!;

		var mech = new Es1Mech(new Es1MechType(chassis, animation), globals.DwellJitter) { Position = position };
		mech.LocomotionThread!.SetSequence(Walk, 0, 0);
		globals.AddLiveObject(mech);
		return mech;
	}
}
