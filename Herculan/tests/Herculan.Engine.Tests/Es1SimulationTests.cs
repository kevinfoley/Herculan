using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>ES1's tick, its commands and the player's controls, on constructed machines, groups and input blocks.</summary>
public class Es1SimulationTests {
	private const short Walk = 0, StopForward = 2;
	private const byte Keypad8 = 0x48;

	/// <summary>
	/// <c>Ctrl+1</c> takes <c>ThrottleLeverMode</c> from 0 to 1, then −1, then 1. Catches the test written <c>&gt;= 0</c>
	/// (0 going to −1) or the mode negated (0 staying 0).
	/// </summary>
	[Fact]
	public void CtrlOneStepsTheThrottleLeverModeFromZeroToOneThenFlipsIt() {
		var (sim, _) = NewSimulation();
		Assert.Equal(0, sim.Controls.ThrottleLeverMode);

		var steps = new List<short>();
		for (int i = 0; i < 3; i++) {
			sim.Dispatch(Command(Es1Simulation.Ctrl1));
			steps.Add(sim.Controls.ThrottleLeverMode);
		}

		Assert.Equal(new short[] { 1, -1, 1 }, steps);
	}

	/// <summary>
	/// Keypad 5, which neither the cockpit nor the dispatch's cases take, reaches the machine's command handler, which
	/// zeroes the player's throttle with its gauge flag and answers 1. Catches the dispatch not forwarding a code it has
	/// no case for, <c>0x4c</c> dropped from the handler's codes, or <c>+0x8d</c> left clear.
	/// </summary>
	[Fact]
	public void KeypadFiveZeroesThePlayersThrottleThroughTheMachinesHandler() {
		var (sim, player) = NewSimulation();
		player.Throttle = 0x200;

		short answer = sim.Dispatch(Command(Es1MechCommands.Keypad5));

		Assert.Equal(1, answer);
		Assert.Equal(0, player.Throttle);
		Assert.True(player.ThrottleChanged);
	}

	/// <summary>
	/// A group whose action has not fired does not tick its members; once it fires, the tick clears the action and the
	/// members tick from the next tick on. Catches the action test inverted, or the members ticked on the clearing tick.
	/// </summary>
	[Fact]
	public void AGroupHeldByItsActionTicksItsMembersOnlyFromTheTickAfterItFires() {
		var (sim, _) = NewSimulation();
		var member = NewMech(sim.Globals);
		var action = new Es1Action();
		var group = new Es1MissionGroup { Action = action };
		group.Members.Add(member);
		var held = new Es1Simulation(sim.Globals, [group], sim.LocalPlayer, null, sim.Input);
		int countdown = member.Behaviour.Countdown;

		held.Tick();
		Assert.Equal(countdown, member.Behaviour.Countdown);

		action.Activated = true;
		held.Tick();
		Assert.Null(group.Action);
		Assert.Equal(countdown, member.Behaviour.Countdown);

		held.Tick();
		Assert.NotEqual(countdown, member.Behaviour.Countdown);
	}

	/// <summary>
	/// In one tick the player's machine moves before its controls are read: the move hands the locomotion thread the
	/// playback rate the last tick left (0), and the throttle the keyboard gives this tick sets a new one only after.
	/// Catches the poll run before the groups.
	/// </summary>
	[Fact]
	public void TheMembersMoveRunsBeforeThePlayersInputInATick() {
		var (sim, player) = NewSimulation();
		player.Behaviour.SetState(Es1MechBehaviour.States[Es1MechBehaviour.Player], sim.Globals.DwellJitter);
		var group = new Es1MissionGroup();
		group.Members.Add(player);
		var ticking = new Es1Simulation(sim.Globals, [group], player, null, sim.Input);

		Keyboard.Keyboard.Dispatch(Keypad8);
		ticking.Tick();

		Assert.NotEqual(0, player.PlaybackRate);
		Assert.Equal(0, player.LocomotionThread!.Rate);
	}

	/// <summary>
	/// <c>Backspace</c> centres the turret in the poll that reads it, and twist input in a later poll stops the
	/// centring. Catches the key not setting <c>g_CenterTurretMode</c>, the centring tick not run while it is set, or
	/// input not clearing it.
	/// </summary>
	[Fact]
	public void BackspaceCentresTheTurretUntilTwistInput() {
		var (sim, player) = NewSimulation();
		player.Twist = 0x800;

		sim.Poll(Command(Es1Simulation.Backspace));
		Assert.True(sim.CenterTurretMode);
		Assert.InRange(player.Twist, 0, 0x7ff);

		var twist = Command(0);
		twist.Axes[Es1PlayerInputBlock.Twist] = 0x100;
		sim.Poll(twist);
		Assert.False(sim.CenterTurretMode);
	}

	private Es1KeyboardInput Keyboard { get; } = new(0);

	private (Es1Simulation Simulation, Es1Mech Player) NewSimulation() {
		var prefs = Es1Preferences.Default;
		var input = new Es1InputBuild(Keyboard, new Es1HostMouse(), prefs, Es1KeyjoyConfig.Parse(""), new Es1ControlsState(prefs), Es1NoStickPort.Instance);
		var globals = new Es1SimGlobals();
		var player = NewMech(globals);
		player.IsPlayer = true;
		return (new Es1Simulation(globals, [], player, null, input), player);
	}

	private static Es1PlayerInputBlock Command(ushort code) => new() { CommandWord = code };

	private static Es1Mech NewMech(Es1SimGlobals globals) {
		var chassis = new Es1HercSimDat {
			SpeedReverse = -100,
			SpeedForward = 245,
			SpeedAccelDecel = 50,
			GaitThreshold = 150,
			GaitThresholdReverse = -60,
			AnimId_Walk = Walk,
			AnimId_Run = 1,
			AnimId_StopMove = StopForward,
			AnimId_StopReverse = 3,
			AnimId_Death = 4,
			AnimId_TurnInPlace = 5,
			AnimId_TorsoTwist = -1,
			AnimId_TorsoPitch = -1,
			TorsoTwistMaxRate = 0x400,
			TorsoTwistAccel = 0x400,
			TorsoTwistLimit = 0x2000,
			TorsoPitchMaxRate = 0x400,
			TorsoPitchAccel = 0x400,
			TorsoPitchMax = 0x1000,
			TorsoPitchMin = -0x1000,
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

		var mech = new Es1Mech(new Es1MechType(chassis, animation), globals.DwellJitter);
		mech.LocomotionThread!.SetSequence(Walk, 0, 0);
		globals.AddLiveObject(mech);
		return mech;
	}
}
