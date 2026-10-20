using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;
using Herculan.Es1.Host;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's view keys, the chain of views and the camera (docs/es1/simulation/main-loop.md#the-views--viewchain_apply-0005894c,
/// docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device, docs/es1/simulation/external-views.md#cam_update), on
/// constructed machines, views and input blocks.
/// </summary>
public class Es1ViewChainTests {
	/// <summary>The most ticks a view change is given to land.</summary>
	private const int MaxTicks = 12;

	/// <summary>
	/// The cockpit's keys in the forward view: <c>F9</c> queues the left glance (5) and <c>F10</c> the right (4), <c>Esc</c>
	/// the pan down (0); a number key with no weapon row still answers 1, and a code no case takes answers 0 while no
	/// instrument exists. Catches <c>F9</c> and <c>F10</c> swapped (ES2's targets), a taken code answering 0 (which would
	/// hand it on to the machine's handler), or an unlisted code answering 1.
	/// </summary>
	[Theory]
	[InlineData(Es1Cockpit.F9, 1, 5)]
	[InlineData(Es1Cockpit.F10, 1, 4)]
	[InlineData(Es1Cockpit.Esc, 1, 0)]
	[InlineData(0x03, 1, -1)]
	[InlineData(0x1e, 0, -1)]
	public void TheCockpitsKeysQueueTheirViewCommandsFromTheForwardView(int code, int answer, int pending) {
		var (views, _) = Views(NewMech(new Es1SimGlobals(), 0));

		Assert.Equal(answer, views.Cockpit.Command((ushort)code));
		Assert.Equal(pending, views.PendingCommand);
	}

	/// <summary>
	/// The view device's button 6 (<c>+0x1b</c>) queues the right glance, command 4, where ES2's <c>+0x20</c> queues 5.
	/// Catches the buttons wired in ES2's order.
	/// </summary>
	[Fact]
	public void TheViewDevicesButtonSixQueuesTheRightGlance() {
		var (views, _) = Views(NewMech(new Es1SimGlobals(), 0));
		var block = new Es1PlayerInputBlock();
		block.Buttons[6] = 1;

		views.Cockpit.PollViewDevice(block);

		Assert.Equal(4, views.PendingCommand);
	}

	/// <summary>
	/// <c>V</c> from the cockpit takes the chain to view 3 on the player through a squad camera in mode 1, the controls on
	/// the camera and the cockpit's widgets off; <c>Esc</c>, which the cockpit then passes on, brings it back to view 2 with
	/// the same camera in mode 2 and the controls on the machine. Catches <c>ViewChain_Apply</c> not run on the manager's
	/// staged flag, its step from 3 to 2 leaving the camera in the orbit or the camera driven, the widgets never turned off
	/// (so the cockpit swallows <c>Esc</c>) or never back on, and <c>V</c> or <c>Esc</c> queueing the wrong command.
	/// </summary>
	[Fact]
	public void VAndEscStepTheChainOutToTheOrbitAndBackToTheEye() {
		var world = World();
		var chain = world.Simulation.ViewChain;
		var views = chain.Manager!;

		world.TickUntil(Es1Simulation.V, () => chain.View == Es1ViewChain.OutsideView);
		var outside = chain.Camera;
		Assert.Same(chain.SquadCameras[0], outside);
		Assert.Equal(Es1Camera.Orbit, outside.Mode);
		Assert.Same(world.Player, outside.Object);
		Assert.True(world.Simulation.Controls.InputDrivesCamera);
		Assert.True(views.Cockpit.WidgetsOff);

		world.TickUntil(Es1Simulation.Esc, () => chain.View == Es1ViewChain.CockpitView);
		Assert.Same(outside, chain.Camera);
		Assert.Equal(Es1Camera.Attached, chain.Camera.Mode);
		Assert.False(world.Simulation.Controls.InputDrivesCamera);
		world.TickUntil(0, () => views.CurrentView == 0);
		Assert.False(views.Cockpit.WidgetsOff);
	}

	/// <summary>
	/// <c>N</c> in the outside view passes over a machine of another group and a squadmate below height 0 to the next
	/// squadmate, which becomes the viewed and chosen object in a camera of its own, in mode 1 with the controls on the
	/// camera. Catches the group test or the height test dropped from the walk, or the player's camera reused.
	/// </summary>
	[Fact]
	public void NSkipsOtherGroupsAndSunkenSquadmatesToTheNextSquadmate() {
		var world = World();
		var chain = world.Simulation.ViewChain;
		world.TickUntil(Es1Simulation.V, () => chain.View == Es1ViewChain.OutsideView);
		var playersCamera = chain.Camera;

		world.Simulation.Poll(new Es1PlayerInputBlock { CommandWord = Es1Simulation.N });

		Assert.Same(world.Squadmate, chain.Viewed);
		Assert.Same(world.Squadmate, chain.Chosen);
		Assert.Same(world.Squadmate, chain.Camera.Object);
		Assert.NotSame(playersCamera, chain.Camera);
		Assert.Equal(Es1Camera.Orbit, chain.Camera.Mode);
		Assert.True(world.Simulation.Controls.InputDrivesCamera);
	}

	/// <summary>
	/// The orbit keeps its eye at height 100, ES1's floor: zooming out from an eye that would sit below it tilts the orbit
	/// to put the eye there; with no zoom the camera stays where the call began, its distance alone stepped. Catches the
	/// floor tested at the ground's 0, or the two branches swapped.
	/// </summary>
	[Fact]
	public void TheOrbitKeepsItsEyeAtHeightOneHundred() {
		var mech = NewMech(new Es1SimGlobals(), 50);
		var zooming = new Es1Camera();
		zooming.AttachTo(mech);
		zooming.Speed = 1;

		zooming.Update();

		Assert.InRange(zooming.Position.Z, Es1Camera.Floor - 2, Es1Camera.Floor + 2);
		Assert.Equal(zooming.OrbitAngles.Pitch, zooming.View.X);

		var still = new Es1Camera();
		still.AttachTo(mech);
		still.Position = new Vec3i(1, 2, 3);
		still.View = (7, 8, 9);
		still.Rates = (0x100, 0, 0);

		still.Update();

		Assert.Equal(new Vec3i(1, 2, 3), still.Position);
		Assert.Equal(((short)7, (short)8, (short)9), still.View);
		Assert.Equal(((short)0, (short)0, (short)0), still.OrbitAngles);
		Assert.Equal(Es1Camera.MinDistance, still.Distance);
	}

	/// <summary>The view manager on views whose canvas origins are the retail <c>.VUE</c> files', and the frame loop on it.</summary>
	private static (Es1ViewManager Views, Es1FrameLoop Frames) Views(Es1Mech player) {
		var records = new (int X, int Y)[] { (0, 0), (0, 197), (320, 0), (-320, 0) }
			.Select(origin => new Vue.Entry {
				ViewportX0 = 0, ViewportY0 = 0, ViewportX1 = 319, ViewportY1 = 140, CenterX = -160, CenterY = -70,
				CanvasOriginX = origin.X, CanvasOriginY = origin.Y,
			})
			.ToArray();
		var clips = records.Select(_ => Es1ClipBlock.FromRect(0, 0, 319, 140)).ToArray();
		var files = new Es1CockpitViews("TEST", records, clips, new HercWorks.Core.Data.File.Dyn.DynamixBitmap?[4], null);
		var views = new Es1ViewManager(files, new Es1VideoMemory(), player);
		var frames = new Es1FrameLoop(views, () => { });
		frames.StartMission();
		return (views, frames);
	}

	/// <summary>
	/// The player and a squadmate in the squad, a sunken squadmate between them in the live list after a machine of
	/// another group, and the simulation with its chain of views on the view manager.
	/// </summary>
	private static TestWorld World() {
		var globals = new Es1SimGlobals();
		var player = NewMech(globals, 0);
		player.IsPlayer = true;
		var enemy = NewMech(globals, 0);
		var sunk = NewMech(globals, -1);
		var mate = NewMech(globals, 0);
		var squad = new Es1MissionGroup();
		foreach (var member in new[] { player, sunk, mate }) {
			squad.Members.Add(member);
			member.Group = squad;
		}

		enemy.Group = new Es1MissionGroup();
		enemy.Group.Members.Add(enemy);

		var (views, frames) = Views(player);
		var chain = new Es1ViewChain(player, views);
		var prefs = Es1Preferences.Default;
		var input = new Es1InputBuild(new Es1KeyboardInput(0), new Es1HostMouse(), prefs, Es1KeyjoyConfig.Parse(""),
			new Es1ControlsState(prefs), Es1NoStickPort.Instance);
		return new TestWorld(new Es1Simulation(globals, [], player, null, input, chain), frames, player, mate);
	}

	private static Es1Mech NewMech(Es1SimGlobals globals, int z) {
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

		var mech = new Es1Mech(new Es1MechType(chassis, animation), globals.DwellJitter) { Position = new Vec3i(0, 0, z) };
		globals.LiveObjects.Add(mech);
		return mech;
	}

	private sealed record TestWorld(Es1Simulation Simulation, Es1FrameLoop Frames, Es1Mech Player, Es1Mech Squadmate) {
		/// <summary>
		/// Ticks with <paramref name="code"/> once and then none, each tick's controls and views followed by the frame's
		/// end, until <paramref name="done"/>; fails after <see cref="MaxTicks"/>.
		/// </summary>
		public void TickUntil(ushort code, Func<bool> done) {
			for (int tick = 0; tick < MaxTicks; tick++) {
				Simulation.Poll(new Es1PlayerInputBlock { CommandWord = tick == 0 ? code : (ushort)0 });
				Simulation.ViewChain.Apply(Simulation.Controls);
				int retraces = 0;
				foreach (var _ in Frames.RunFrame(frozen: false)) {
					Assert.True(++retraces < 1000, "The frame's end never finished.");
				}

				if (done()) {
					return;
				}
			}

			Assert.Fail($"The views did not get there in {MaxTicks} ticks.");
		}
	}
}
