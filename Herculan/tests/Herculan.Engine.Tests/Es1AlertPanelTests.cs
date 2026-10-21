using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Anim;
using Herculan.Es1.Host;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's in-mission panels (docs/es1/simulation/alert-panels.md) and the tick's suspension while one is up (plan decision
/// 11), on constructed machines and views, with no files loaded, and keys pushed onto the command queue.
/// </summary>
public class Es1AlertPanelTests {
	private const long Second = TimeSpan.TicksPerSecond;

	/// <summary>The most host frames a test waits for a panel to go up or down.</summary>
	private const int MaxFrames = 600;

	private const ushort P = 0x19, CtrlQ = 0x410, Tab = 0x0f, Return = 0x1c, AltTab = 0x20f, Keypad0 = 0x52, F11 = 0x57;

	/// <summary>
	/// The status panel, 222 × 109, stands at (48, 45)–(270, 154), centered on the 320 × 200 screen by its last column
	/// and row, 319 and 199. Catches the centering on 320 and 200 (49, 45), on ES2's 640 × 480, or the size swapped.
	/// </summary>
	[Fact]
	public void TheStatusPanelIsCenteredOnTheScreensLastColumnAndRow() {
		var rig = Rig.Start();

		var panel = new Es1StatusAlertPanel(rig.Panels, Es1StatusAlertPanel.PauseStatus);

		Assert.Equal(new Es1Rect(48, 45, 270, 154), panel.Rect);
	}

	/// <summary>
	/// The pause prompt has one 80 × 20 button at <i>x</i> 71, the quit prompt two at 22 and 120, and status 19, past
	/// the table, none; each button is registered with the panel's root, which a press at its rect finds. Catches the
	/// jump table's cases moved, the buttons' size, or a button left off the root, which no click would reach.
	/// </summary>
	[Fact]
	public void TheButtonsAreLaidOutByStatus() {
		var rig = Rig.Start();

		var pause = new Es1StatusAlertPanel(rig.Panels, Es1StatusAlertPanel.PauseStatus);
		var quit = new Es1StatusAlertPanel(rig.Panels, Es1StatusAlertPanel.QuitStatus);
		var past = new Es1StatusAlertPanel(rig.Panels, Es1StatusAlertPanel.StatusCount);

		Assert.Equal([new Es1Rect(71, 70, 151, 90)], pause.Widgets.Select(widget => widget.Rect));
		Assert.Equal([new Es1Rect(22, 70, 102, 90), new Es1Rect(120, 70, 200, 90)], quit.Widgets.Select(widget => widget.Rect));
		Assert.Empty(past.Widgets);
		Assert.Equal(1, quit.Root.HitTestChildren(120, 70));
	}

	/// <summary>
	/// On the quit prompt with CONTINUE focused, [Alt+Tab] focuses QUIT, wrapping back, and puts the pointer on its
	/// center, (120 + 40 + 48, 70 + 10 + 45); keypad 0 is taken but walks the focus only with preferences option 10 on.
	/// Catches [Alt+Tab] lost to the command mask (ES2's [Shift+Tab]), the walk not wrapping, the center not moved by the
	/// panel's corner, or keypad 0 walking without option 10.
	/// </summary>
	[Fact]
	public void AltTabWalksBackAndKeypadZeroWalksOnOnlyWithOptionTen() {
		var rig = Rig.Start();
		var panel = new Es1StatusAlertPanel(rig.Panels, Es1StatusAlertPanel.QuitStatus);
		panel.SetFocus(0);

		Assert.Equal(1, panel.HandleEvent(new Es1PlayerInputBlock { CommandWord = AltTab }));
		Assert.Equal(1, panel.Focus);
		Assert.Equal((208, 125), rig.Views.Display.Cursor.Position);

		Assert.Equal(1, panel.HandleEvent(new Es1PlayerInputBlock { CommandWord = Keypad0 }));
		Assert.Equal(1, panel.Focus);

		var options = new byte[Es1Preferences.OptionCount];
		options[Es1Preferences.ThrottleKeysOption] = 1;
		var withOption = Rig.Start(Es1Preferences.FromFile(options));
		var walking = new Es1StatusAlertPanel(withOption.Panels, Es1StatusAlertPanel.QuitStatus);
		walking.SetFocus(1);
		walking.HandleEvent(new Es1PlayerInputBlock { CommandWord = Keypad0 });
		Assert.Equal(0, walking.Focus);
	}

	/// <summary>
	/// A panel saves the page on the screen under it as it goes up, draws over it, and puts it back as it goes down.
	/// Catches the save made from the draw page rather than the page on the screen, or the restore dropped.
	/// </summary>
	[Fact]
	public void APanelPutsBackTheScreenUnderIt() {
		var rig = Rig.Start();
		var display = rig.Views.Display;
		int shown = display.PairVisible;
		for (int y = 0; y < Es1VideoMemory.PageHeight; y++) {
			for (int x = 0; x < Es1VideoMemory.PageWidth; x++) {
				display.DrawPage = shown;
				display.Plot(x, y, (ushort)(1 + (x + y) % 7));
			}
		}

		var before = Page(display, shown);
		var panel = new Es1StatusAlertPanel(rig.Panels, Es1StatusAlertPanel.PauseStatus);
		panel.Enter();
		Assert.NotEqual(before, Page(display, shown));

		panel.Leave();
		Assert.Equal(before, Page(display, shown));
	}

	/// <summary>
	/// <c>P</c> puts up the pause prompt inside the tick, which then stands while the host frames go on, no other tick
	/// starting, until [Return] presses CONTINUE; the tick then finishes, the panel's answer 0 ending nothing, and the
	/// ticks go on. Catches the tick run on past the panel, a tick started while it is up, or the answer ending the
	/// mission.
	/// </summary>
	[Fact]
	public void PausesTickStandsUntilContinueAndThenGoesOn() {
		var rig = Rig.Start();
		rig.Keys.Keyboard.Queue.Push(P);

		Assert.False(rig.AdvanceUntil(() => rig.Panels.OpenCount == 1));
		int ticks = rig.Pacer.Ticks;
		for (int frame = 0; frame < 30; frame++) {
			Assert.False(rig.Advance());
		}

		Assert.Equal(ticks, rig.Pacer.Ticks);
		Assert.True(rig.Frames.Waiting);

		rig.Keys.Keyboard.Queue.Push(Return);
		Assert.False(rig.AdvanceUntil(() => rig.Panels.OpenCount == 0));
		Assert.False(rig.AdvanceUntil(() => rig.Pacer.Ticks > ticks));
		Assert.False(rig.Simulation.QuitFlag);
	}

	/// <summary>
	/// <c>Ctrl+Q</c>'s quit prompt answers 1 for QUIT, which [Tab] focuses and [Return] presses: the tick goes on to end
	/// the mission with <c>Sim_QuitFlag</c> set. Catches the panel's answer not added to the dispatch's 1, the flag not
	/// set, or the tick's end not reaching the frame loop.
	/// </summary>
	[Fact]
	public void CtrlQsQuitEndsTheMission() {
		var rig = Rig.Start();
		rig.Keys.Keyboard.Queue.Push(CtrlQ);
		Assert.False(rig.AdvanceUntil(() => rig.Panels.OpenCount == 1));

		rig.Keys.Keyboard.Queue.Push(Tab);
		rig.Keys.Keyboard.Queue.Push(Return);

		Assert.True(rig.AdvanceUntil(() => false));
		Assert.True(rig.Simulation.QuitFlag);
	}

	/// <summary>
	/// While the pause prompt stands for a second, the PC timer runs on, the clock counting 60 a second, so the first
	/// tick after it measures the whole stand and takes the longest tick, <c>0x1c2</c>. Catches the timer held still
	/// while the tick stands, which would give that tick the shortest length.
	/// </summary>
	[Fact]
	public void ThePcTimerRunsOnWhileThePanelIsUp() {
		var rig = Rig.Start();
		var clock = rig.Simulation.Globals.Clock;
		rig.Keys.Keyboard.Queue.Push(P);
		Assert.False(rig.AdvanceUntil(() => rig.Panels.OpenCount == 1));
		uint before = clock.Clock;
		int ticks = rig.Pacer.Ticks;

		for (int frame = 0; frame < 60; frame++) {
			rig.Advance();
		}

		Assert.InRange(clock.Clock - before, 59u, 61u);
		rig.Keys.Keyboard.Queue.Push(Return);
		Assert.False(rig.AdvanceUntil(() => rig.Pacer.Ticks > ticks));
		Assert.Equal(0x1c2, rig.Pacer.Deltas[^1]);
	}

	/// <summary>
	/// The objectives panel reads the one group of the install's <c>data\object.str</c> and puts on its seven labels the
	/// lines that are not empty, in order, no further than the seventh. Catches an empty line left as a gap, ES2's status
	/// rule of stopping at the first empty line, an eighth line shown, or the file read from elsewhere than <c>data\</c>.
	/// </summary>
	[Fact]
	public void TheObjectivesShowTheFirstSevenLinesThatAreNotEmpty() {
		string install = Directory.CreateTempSubdirectory("es1-objectives-").FullName;
		try {
			string[] lines = ["A", "", "B", "C", "", "D", "E", "F", "G", "H"];
			var table = new StringFile { Groups = [lines.Select(line => new StringFile.Entry(line, [])).ToArray()] };
			Directory.CreateDirectory(Path.Combine(install, "data"));
			File.WriteAllBytes(Path.Combine(install, "data", Es1ObjectivesPanel.ObjectivesName), new StringFileTransformer().Write(table)!);
			var rig = Rig.Start(installRoot: install);

			var panel = new Es1ObjectivesPanel(rig.Panels);

			Assert.Equal(lines, panel.Objectives);
			Assert.Equal(["A", "B", "C", "D", "E", "F", "G"], Es1ObjectivesPanel.LinesShown(panel.Objectives));
		} finally {
			Directory.Delete(install, true);
		}
	}

	/// <summary>
	/// <c>F11</c> puts up the objectives panel from inside the dispatch, which stands at each pass of its loop until
	/// [Return] presses <c>RETURN</c>; the cockpit then answers 1, so the dispatch answers 1 and the code goes no further.
	/// Catches the panel not raised, the case run on without waiting, or the cockpit answering 0, which would hand
	/// <c>F11</c> on to the machine's handler.
	/// </summary>
	[Fact]
	public void F11StandsInTheObjectivesPanelAndAnswersOneWhenItCloses() {
		var rig = Rig.Start();
		var answer = new StrongBox<short>();
		using var passes = rig.Simulation.DispatchCommand(new Es1PlayerInputBlock { CommandWord = F11 }, answer).GetEnumerator();

		Assert.True(passes.MoveNext());
		Assert.Equal(1, rig.Panels.OpenCount);
		Assert.True(passes.MoveNext());

		rig.Keys.Keyboard.Queue.Push(Return);
		for (int pass = 0; passes.MoveNext(); pass++) {
			Assert.True(pass < MaxFrames, $"The panel was still up after {MaxFrames} passes.");
		}

		Assert.Equal(0, rig.Panels.OpenCount);
		Assert.Equal(1, answer.Value);
	}

	/// <summary>
	/// A panel's canvas cuts a draw to the panel's rect, edges included, as clip mode 1 does: a line from left of the panel
	/// to right of it, on the objectives panel's rect at (2, 45), is drawn from column 2 to 317 and nowhere else, which is
	/// what leaves its title bar's frame at (−20, −20) and its plate's columns past 315 unseen. Catches the cut dropped or
	/// an edge column lost.
	/// </summary>
	[Fact]
	public void APanelsCanvasCutsADrawToItsRect() {
		var rig = Rig.Start();
		var display = rig.Views.Display;
		int shown = display.PairVisible;
		display.DrawPage = shown;
		for (int x = 0; x < Es1VideoMemory.PageWidth; x++) {
			display.Plot(x, 50, 1);
		}

		var rect = Es1AlertPanel.CenterRect(Es1ObjectivesPanel.Height, Es1ObjectivesPanel.Width);
		new Es1PanelCanvas(display, rect).DrawLine(-2, 5, 400, 5, 9);

		var page = Page(display, shown);
		var row = Enumerable.Range(0, Es1VideoMemory.PageWidth).Select(x => page[50 * Es1VideoMemory.PageWidth + x]).ToArray();
		Assert.Equal(Enumerable.Range(0, Es1VideoMemory.PageWidth).Select(x => x is >= 2 and <= 317 ? (ushort)9 : (ushort)1), row);
	}

	/// <summary>The simulation on a player's machine and constructed views, the panels on its display, and the frame loop paced by its clock.</summary>
	private sealed record Rig(Es1Simulation Simulation, Es1ViewManager Views, Es1FrameLoop Frames, Es1KeyboardInput Keys,
			SimulationPacer Pacer) {
		public Es1AlertGlobals Panels => Simulation.Panels!;

		public static Rig Start(Es1Preferences? preferences = null, string? installRoot = null) {
			var globals = new Es1SimGlobals();
			var player = NewMech(globals);
			player.IsPlayer = true;
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

			preferences ??= Es1Preferences.Default;
			var keys = new Es1KeyboardInput(0);
			var input = new Es1InputBuild(keys, new Es1HostMouse(), preferences, Es1KeyjoyConfig.Parse(""),
				new Es1ControlsState(preferences), Es1NoStickPort.Instance) {
				CockpitMouse = views.Mouse,
			};
			var simulation = new Es1Simulation(globals, [], player, null, input, new Es1ViewChain(player, views)) {
				Panels = new Es1AlertGlobals(views.Display, views.Mouse, input, null, null) {
					EnterHook = keys.AxisKeys.PushDisable,
					LeaveHook = keys.AxisKeys.PopEnable,
					InstallRoot = installRoot,
				},
			};
			return new Rig(simulation, views, frames, keys, new SimulationPacer(simulation));
		}

		/// <summary>One host frame of 1/60 s; whether the mission ended.</summary>
		public bool Advance() => Frames.Advance(TimeSpan.FromTicks(Second / 60), Pacer);

		/// <summary>Host frames until <paramref name="done"/> or the mission ends, which it returns; fails after <see cref="MaxFrames"/>.</summary>
		public bool AdvanceUntil(Func<bool> done) {
			for (int frame = 0; frame < MaxFrames; frame++) {
				if (Advance()) {
					return true;
				}

				if (done()) {
					return false;
				}
			}

			Assert.Fail($"Not there in {MaxFrames} host frames.");
			return false;
		}
	}

	/// <summary>The simulation's clock and tick as the frame loop's pacer, counting the ticks and keeping each one's length.</summary>
	private sealed class SimulationPacer(Es1Simulation simulation) : IEs1FramePacer {
		public int Ticks { get; private set; }

		public List<short> Deltas { get; } = [];

		public bool Frozen => simulation.Frozen;

		public bool AdvanceClock(TimeSpan elapsed) => simulation.Globals.Clock.AdvanceHostFrame(elapsed);

		public IEnumerable<Es1Wait> Tick(StrongBox<bool> ended) {
			Ticks++;
			Deltas.Add(simulation.Globals.Clock.TickDelta);
			return simulation.MainTick(ended);
		}

		public void Hold(TimeSpan elapsed) => simulation.Globals.Clock.Hold(elapsed);
	}

	/// <summary>The cells of page <paramref name="page"/>, read as the page's source.</summary>
	private static ushort[] Page(Es1VideoMemory display, int page) {
		display.SourcePage = page;
		return display.ReadRect(0, 0, Es1VideoMemory.PageWidth - 1, Es1VideoMemory.PageHeight - 1);
	}

	private static Es1Mech NewMech(Es1SimGlobals globals) {
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

		var mech = new Es1Mech(new Es1MechType(chassis, animation), globals.DwellJitter);
		globals.AddLiveObject(mech);
		return mech;
	}
}
