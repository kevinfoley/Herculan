using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;
using Herculan.Es1.Gunsight;
using Herculan.Es1.Host;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's cockpit target selection (docs/es1/simulation/cockpit-panel.md#the-target-selection), the gunsight's part of
/// <c>Player_PerFrameCockpitUpdate</c> (docs/es1/simulation/mech.md#the-cockpit) and the target box
/// (docs/es1/simulation/cockpit-gunsight-hud.md#the-target-box), on constructed machines in the gunsight tests' cockpit:
/// the player at the origin facing +Y on side 0, then in the live list a Cybrid behind it, one ahead and one ahead to the
/// right, each radar-visible.
/// </summary>
public class Es1TargetSelectionTests {
	/// <summary>
	/// <c>Tab</c>'s walk takes the first object after the selection that is on the other side and in the cone, wrapping
	/// past the list's end, and stays put when the walk comes back to its start; each new selection reaches the gunsight's
	/// block, armed. Catches the walk not wrapping, a walk back at its start clearing or changing the selection, the cone
	/// test or the side test dropped (the player's machine or the Cybrid behind it taken), or the hand-over to the
	/// gunsight left out.
	/// </summary>
	[Fact]
	public void CycleStepsThroughTheConeAndWrapsToItsStart() {
		var w = World();
		var targets = w.Rig.Views.Cockpit.Targets;

		Assert.Same(w.Ahead, targets.Cycle());
		Assert.Same(w.Ahead, w.Rig.Gunsight.State.Selection);
		Assert.True(w.Rig.Gunsight.State.SelectionArmed);
		Assert.Same(w.Right, targets.Cycle());
		Assert.Same(w.Ahead, targets.Cycle());

		w.Right.RadarVisible = false;
		Assert.Same(w.Ahead, targets.Cycle());
	}

	/// <summary>
	/// The cone holds ±8999 of the bearing less the heading with the turret's twist added, its bounds included. Catches the
	/// bound made strict, the twist subtracted or left out, or ES2's cone of another width.
	/// </summary>
	[Fact]
	public void ForwardConeHoldsItsBoundWithTheTwistAdded() {
		var w = World();
		var targets = w.Rig.Views.Cockpit.Targets;
		short bearing = Es1MissionGroup.HeadingToward(w.Player.Position, w.Right.Position);

		foreach (var (edge, inside) in new[] { (8999, true), (9000, false), (-8999, true), (-9000, false) }) {
			w.Player.Twist = (short)(edge - bearing);
			Assert.Equal(inside, targets.InForwardCone(w.Right));
		}
	}

	/// <summary>
	/// <c>Cockpit_DrawFrame</c> keeps a radar-visible selection 110000 away, inside the 120000 radar range, and drops it
	/// once the radar no longer shows it and the player holds no contact. Catches the drop left out, its test inverted, or
	/// the sensor model's 100000 radar reach taken for the selection's range.
	/// </summary>
	[Fact]
	public void DrawFrameDropsASelectionTheRadarNoLongerShows() {
		var w = World();
		var cockpit = w.Rig.Views.Cockpit;
		w.Ahead.Position = new Vec3i(0, 110000, 0);
		cockpit.Targets.Cycle();

		cockpit.DrawFrame();
		Assert.Same(w.Ahead, cockpit.Selection);

		w.Ahead.RadarVisible = false;
		cockpit.DrawFrame();
		Assert.Null(cockpit.Selection);
	}

	/// <summary>
	/// <c>TargetSelect_SetObject</c> walks the selection from its own cursor, which starts at the list's start: asked for
	/// the Cybrid to the right with nothing selected, it cycles to the one ahead and walks the cursor back to it before
	/// reaching the one asked for, and answers false; asked again it goes on from there and lands on it. Asked for the
	/// Cybrid behind, outside the cone, it walks back to the selection it began with. Catches the walk started from the
	/// selection's place in the list instead of the cursor, the cursor reset between calls, or a refused object taken.
	/// </summary>
	[Fact]
	public void SetObjectWalksFromItsOwnCursor() {
		var w = World();
		var targets = w.Rig.Views.Cockpit.Targets;

		Assert.False(targets.SetObject(w.Right));
		Assert.Same(w.Ahead, targets.Selection);

		Assert.True(targets.SetObject(w.Right));
		Assert.Same(w.Right, targets.Selection);
		Assert.Same(w.Right, w.Rig.Gunsight.State.Selection);

		Assert.False(targets.SetObject(w.Behind));
		Assert.Same(w.Right, targets.Selection);
	}

	/// <summary>
	/// A left click on the gunsight arms the pick at the click's point; the next frame's 3D pass asks the world layer for
	/// the cell under that point of the page it draws on, and the gunsight's update in the same frame hands the answer to
	/// <c>TargetSelect_SetObject</c> and clears its flag. With the walk's cursor left past the Cybrid ahead, the walk
	/// reaches the one picked, ahead to the left, where <c>Tab</c> would take the one to the right. Catches the pass not
	/// answering the pick, the answer read after the gunsight's update (a frame late), the point taken without the drawn
	/// page's origin, the answer not handed to the selection, or the flag left set.
	/// </summary>
	[Fact]
	public void AClickOnTheGunsightSelectsTheObjectDrawnAtItsPointInTheSameFrame() {
		var w = World();
		var views = w.Rig.Views;
		var cockpit = views.Cockpit;
		var left = NewMech(cockpit.Globals, w.Ahead.Group!, new Vec3i(-5000, 30000, 0));
		var frames = new Es1FrameLoop(views, () => { });
		frames.StartMission();
		Assert.False(cockpit.Targets.SetObject(w.Right));
		var layer = new PickingLayer(left);
		views.Display.Layer = layer;

		Click(views.Mouse, 100, 60);
		var pick = cockpit.Globals.ObjectPick;
		Assert.True(w.Rig.Gunsight.PickArmed);
		Assert.False(pick.IsIdle);
		var display = views.Display;
		int address = display.Address(display.DrawPage, pick.Point.X, pick.Point.Y);

		RunFrame(frames);
		Assert.Equal([(address % Es1VideoMemory.Width, address / Es1VideoMemory.Width)], layer.Asked);
		Assert.True(pick.IsIdle);
		Assert.False(w.Rig.Gunsight.PickArmed);
		Assert.Same(left, cockpit.Selection);
	}

	/// <summary>
	/// A pick armed in the rear view, which draws no 3D pass, stays pending across its frame with the gunsight's flag set;
	/// back in the forward view, a point below the window answers no object without asking the world layer, the flag is
	/// cleared and the selection left alone. Catches the pick answered outside the 3D pass, the flag cleared while the
	/// pick is pending, or a point outside the window read from the layer.
	/// </summary>
	[Fact]
	public void APickWaitsForThe3DPassAndFindsNothingOutsideItsWindow() {
		var w = World();
		var views = w.Rig.Views;
		var cockpit = views.Cockpit;
		var frames = new Es1FrameLoop(views, () => { });
		frames.StartMission();
		var layer = new PickingLayer(w.Ahead);
		views.Display.Layer = layer;
		var pick = cockpit.Globals.ObjectPick;

		views.SetView(1);
		w.Rig.Gunsight.PickArmed = true;
		pick.Arm(100, 150);
		RunFrame(frames);
		Assert.False(pick.IsIdle);
		Assert.True(w.Rig.Gunsight.PickArmed);

		views.SetView(0);
		RunFrame(frames);
		Assert.True(pick.IsIdle);
		Assert.False(w.Rig.Gunsight.PickArmed);
		Assert.Empty(layer.Asked);
		Assert.Null(cockpit.Selection);
	}

	/// <summary>
	/// <c>Player_PerFrameCockpitUpdate</c> takes the gunsight's selection as the machine's target, moving the holders'
	/// counts, setting the target-changed byte and putting out the lamp, and gives the gunsight the machine's angles,
	/// except the heading and the twist while the machine is immobilized. Catches the counts not moved off the old target,
	/// the changed byte or the lamp's clear dropped, or the immobilized rule left out or applied to the pitch.
	/// </summary>
	[Fact]
	public void CockpitUpdateMakesTheSelectionTheTarget() {
		var w = World();
		var cockpit = w.Rig.Views.Cockpit;
		var player = w.Player;
		var state = w.Rig.Gunsight.State;
		cockpit.Targets.Cycle();
		player.LockLamp = true;
		player.Rotation = (0, 0, 0x1234);

		Es1PlayerCockpitUpdate.Run(player, cockpit);
		Assert.Same(w.Ahead, player.Target);
		Assert.Equal(1, w.Ahead.TargetHolders);
		Assert.True(player.TargetChanged);
		Assert.False(player.LockLamp);
		Assert.Equal(0x1234, state.Heading);

		cockpit.Targets.Cycle();
		player.Immobilized = true;
		player.Rotation = (0, 0, 0x2000);
		player.Twist = 0x300;
		player.Pitch = 0x40;
		Es1PlayerCockpitUpdate.Run(player, cockpit);
		Assert.Same(w.Right, player.Target);
		Assert.Equal(0, w.Ahead.TargetHolders);
		Assert.Equal(1, w.Right.TargetHolders);
		Assert.Equal(0x1234, state.Heading);
		Assert.Equal(0, state.Twist);
		Assert.Equal(0x40, state.Pitch);
	}

	/// <summary>
	/// A target of radius 1000 standing 40000 ahead with its foot on the sight line spans 12 rows, (2000 &lt;&lt; 8) /
	/// 40000, so its rows move 6 apart to make 25: the box (148, 52)–(172, 76) about the projection centre (160, 70).
	/// Without lock its corners are Ls of 6 along the edges; with lock filled triangles, the corner itself left and the
	/// square's inner corner filled; the colour is 1 for side 0 and <c>0x35</c> for any other. Catches the clamp dropped
	/// or applied to one row, the box placed on the lower point, the lock's sense or the side colours swapped, or the
	/// corners' length not held to 6.
	/// </summary>
	[Theory]
	[InlineData(false, 1)]
	[InlineData(true, 0)]
	public void TargetBoxIsHeldTo25RowsAndDrawnBySide(bool lockLamp, byte side) {
		var (rig, box) = Box(new Vec3i(0, 40000, 500), 1000, side, lockLamp);
		byte color = side == 0 ? Es1GunsightTargetIndicator.SideZeroColor : Es1GunsightTargetIndicator.OtherSideColor;

		Assert.Equal(new Es1Rect(148, 52, 172, 76), box.Box);
		Assert.Equal(!lockLamp, Pixel(rig, 148, 52) == color);
		Assert.Equal(lockLamp, Pixel(rig, 154, 58) == color);
		Assert.Equal(!lockLamp, Pixel(rig, 151, 52) == color);
		Assert.NotEqual(color, Pixel(rig, 155, 52));
		Assert.NotEqual(color, Pixel(rig, 149, 53));
	}

	/// <summary>
	/// A target of radius 400 at 2000 ahead and below spans rows 61 to 163, which move 13 together to make 75: the box
	/// (123, 74)–(197, 150), whose lower corners fall below row 140, the forward view's clip region, and are not drawn,
	/// though the cockpit's canvas reaches row 199. Catches the clamp above 75 dropped or moving the rows apart, or the box
	/// drawn through the canvas context instead of the scene's clip regions.
	/// </summary>
	[Fact]
	public void TargetBoxIsHeldTo75RowsAndCutToTheScene() {
		var (rig, box) = Box(new Vec3i(0, 2000, -528), 400, 1, lockLamp: false);

		Assert.Equal(new Es1Rect(123, 74, 197, 150), box.Box);
		Assert.Equal(Es1GunsightTargetIndicator.OtherSideColor, Pixel(rig, 123, 74));
		Assert.NotEqual(Es1GunsightTargetIndicator.OtherSideColor, Pixel(rig, 123, 150));
	}

	/// <summary>A target behind the eye places no box. Catches the depth test dropped, which would draw it mirrored.</summary>
	[Fact]
	public void TargetBoxIsNotPlacedBehindTheEye() {
		var (_, box) = Box(new Vec3i(0, -40000, 500), 1000, 1, lockLamp: false);

		Assert.Equal(default, box.Box);
	}

	/// <summary>
	/// The box of a selection at <paramref name="position"/> of radius <paramref name="radius"/> on side
	/// <paramref name="side"/>, painted after the mission start in view 0 with the view of a camera at the origin facing +Y.
	/// </summary>
	private static (Es1GunsightTests.TestRig Rig, Es1GunsightTargetIndicator Box) Box(Vec3i position, int radius, byte side,
			bool lockLamp) {
		var w = World();
		var rig = w.Rig;
		new Es1FrameLoop(rig.Views, () => { }).StartMission();
		rig.Views.InstallViewProjection(new Es1Camera());
		w.Ahead.Group = new Es1MissionGroup { Side = side };
		var state = rig.Gunsight.State;
		state.Selection = w.Ahead;
		state.SelectionArmed = true;
		state.SelectionPosition = position;
		state.SelectionRadius = radius;
		state.LockLamp = lockLamp;

		rig.Gunsight.TargetIndicator.Paint();
		return (rig, rig.Gunsight.TargetIndicator);
	}

	private static ushort Pixel(Es1GunsightTests.TestRig rig, int x, int y) {
		var display = rig.Views.Display;
		return display.Cell(display.DrawPage, x, y);
	}

	private static TestWorld World() {
		var globals = new Es1SimGlobals();
		var cybrids = new Es1MissionGroup { Side = 1 };
		var player = NewMech(globals, new Es1MissionGroup { Side = 0 }, Vec3i.Zero);
		player.IsPlayer = true;
		var behind = NewMech(globals, cybrids, new Vec3i(0, -20000, 0));
		var ahead = NewMech(globals, cybrids, new Vec3i(0, 20000, 0));
		var right = NewMech(globals, cybrids, new Vec3i(5000, 30000, 0));
		return new TestWorld(Es1GunsightTests.Rig(globals, player), player, behind, ahead, right);
	}

	private static Es1Mech NewMech(Es1SimGlobals globals, Es1MissionGroup group, Vec3i position) {
		var chassis = new Es1HercSimDat {
			AnimId_StopMove = 0,
			AnimId_StopReverse = 0,
			AnimId_TorsoTwist = -1,
			AnimId_TorsoPitch = -1,
		};
		var animation = ShapeAnimation.From(new ANAnimList {
			Sequences = [new ANSequence { Frames = [new ANSequenceFrame { Duration = 100 }], PartIds = [0], TransformIndices = [0] }],
			Transforms = [new ANAnimListTransform()],
			DefaultTransforms = [0],
		})!;
		var mech = new Es1Mech(new Es1MechType(chassis, animation), globals.DwellJitter) {
			Position = position,
			Group = group,
			RadarVisible = true,
		};
		group.Members.Add(mech);
		globals.AddLiveObject(mech);
		return mech;
	}

	/// <summary>A left press and release at (<paramref name="x"/>, <paramref name="y"/>), handled as the input build handles them.</summary>
	private static void Click(Es1CockpitMouse mouse, int x, int y) {
		mouse.Push(1, new Es1MouseRecord(x, y, Es1HostMouse.LeftBit));
		mouse.Push(2, new Es1MouseRecord(x, y, 0));
		mouse.ProcessQueue();
	}

	/// <summary>One frame after a tick, every retrace it waits taken at once.</summary>
	private static void RunFrame(Es1FrameLoop frames) {
		int retraces = 0;
		foreach (var _ in frames.RunFrame(frozen: false)) {
			Assert.True(++retraces < 1000, "The frame's end never finished.");
		}
	}

	private sealed record TestWorld(Es1GunsightTests.TestRig Rig, Es1Mech Player, Es1Mech Behind, Es1Mech Ahead, Es1Mech Right);

	/// <summary>A world layer that draws nothing and answers <paramref name="answer"/> for every cell it is asked, recording each.</summary>
	private sealed class PickingLayer(object answer) : IEs1WorldLayer {
		public List<(int X, int Y)> Asked { get; } = [];

		public void DrawWorld(int x, int y, int width, int height, IReadOnlyList<(int X, int Y, int Width)> keep) {
		}

		public bool[] DrawWorldView(int x, int y, int width, int height, Es1RectView view) => new bool[width * height];

		public void Copy(int sourceX, int sourceY, int targetX, int targetY, int width, int height) {
		}

		public object? ObjectAt(int x, int y) {
			Asked.Add((x, y));
			return answer;
		}
	}
}
