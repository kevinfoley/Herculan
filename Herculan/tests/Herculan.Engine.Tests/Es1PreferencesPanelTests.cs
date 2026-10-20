using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts.Anim;
using Herculan.Engine.Anim;
using Herculan.Es1.Host;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's preferences panel and <c>PreferencesPanel_Raise</c> (docs/es1/simulation/preferences.md), on constructed machines
/// and views with no files loaded, keys pushed onto the command queue, and the options saved to a temporary folder.
/// </summary>
public class Es1PreferencesPanelTests {
	private const long Second = TimeSpan.TicksPerSecond;

	/// <summary>The most host frames or passes a test waits for a panel to go up or down.</summary>
	private const int MaxFrames = 600;

	private const ushort F12 = 0x58, Tab = 0x0f, Esc = 0x01, EqualsKey = 0x0d, MinusKey = 0x0c;

	/// <summary>
	/// A bar raised by 32 from 0 reads 32, 64, …, 224, then 255, then 0; lowered from 255 it reads 223, 191, …, 31, then
	/// 0, then 255: nine settings each way. Catches the clamp to 255 or 0 dropped (a byte wrapping past it), the wrap
	/// from the end dropped, or the step taken from the wrong end.
	/// </summary>
	[Fact]
	public void ABarStepsByThirtyTwoAndWrapsBothWays() {
		var options = Es1Preferences.Default;

		var raised = Enumerable.Range(0, 9).Select(_ => options.IncreaseOption(true, 0x20, Es1Preferences.SkyOption)).ToArray();
		var lowered = Enumerable.Range(0, 9).Select(_ => options.DecreaseOption(true, 0x20, Es1Preferences.SkyOption)).ToArray();

		Assert.Equal([32, 64, 96, 128, 160, 192, 224, 255, 0], raised.Select(b => (int)b));
		Assert.Equal([255, 223, 191, 159, 127, 95, 63, 31, 0], lowered.Select(b => (int)b));
	}

	/// <summary>
	/// [=] and [-] step only a focused bar: with MUSIC focused they change nothing, and with SKY focused (four [Tab]s on)
	/// [=] raises it by 32 and [-] lowers it again. Catches the keys pressing whatever is focused, or [-] pressing with
	/// the left button's flags, which would raise the bar.
	/// </summary>
	[Fact]
	public void EqualsAndMinusStepOnlyAFocusedBar() {
		var rig = Rig.Start();
		var options = rig.Simulation.Input.Preferences;
		var panel = new Es1PreferencesPanel(rig.Panels);
		using var passes = panel.Run(new StrongBox<int>()).GetEnumerator();
		Assert.True(passes.MoveNext());

		Pass(rig, passes, EqualsKey);
		Pass(rig, passes, MinusKey);
		Assert.Equal(0, options[Es1Preferences.MusicOption]);

		for (int i = 0; i < 4; i++) {
			Pass(rig, passes, Tab);
		}

		Assert.Equal(Es1PreferencesPanel.Sky, panel.Focus);
		Pass(rig, passes, EqualsKey);
		Assert.Equal(32, options[Es1Preferences.SkyOption]);
		Pass(rig, passes, MinusKey);
		Assert.Equal(0, options[Es1Preferences.SkyOption]);
		Assert.Equal(0, options[Es1Preferences.MusicOption]);
	}

	/// <summary>
	/// [Esc], which presses DONE, writes options 0 to 6 from memory over <c>data\prefs.cfg</c>'s first seven bytes and
	/// keeps its other ten, though none of the seven differs from its load-time copy (plan decision 13); and with the
	/// file gone it writes all 17 from memory. Catches retail's <c>Prefs_OptionChanged</c> gate put back, a dump of the
	/// whole array over the file, or the missing file left unwritten.
	/// </summary>
	[Fact]
	public void DoneSavesOptionsZeroToSixUnchangedOrNotKeepingTheOtherBytes() {
		string install = Directory.CreateTempSubdirectory("es1-prefs-").FullName;
		try {
			Directory.CreateDirectory(Path.Combine(install, "data"));
			string path = Path.Combine(install, "data", "prefs.cfg");
			byte[] memory = Enumerable.Range(0, Es1Preferences.OptionCount).Select(i => (byte)(i % 2)).ToArray();
			byte[] disk = Enumerable.Range(0, Es1Preferences.OptionCount).Select(i => (byte)(100 + i)).ToArray();
			File.WriteAllBytes(path, disk);
			var options = Es1Preferences.FromFile(memory, path);
			var rig = Rig.Start(options);

			CloseWithEsc(rig);

			Assert.Equal(memory[..7].Concat(disk[7..]), File.ReadAllBytes(path));

			File.Delete(path);
			CloseWithEsc(rig);

			Assert.Equal(memory, File.ReadAllBytes(path));
		} finally {
			Directory.Delete(install, true);
		}
	}

	/// <summary>
	/// <c>F12</c> from the forward view goes to view 4 with the external rect above the panel and the camera orbiting the
	/// player, its heading rate reaching −<c>0x200</c> under the orbit hook; after [Esc] the tick goes on with view 0,
	/// the default rect, the camera as it was (mode 2, still), <c>Sim_Frozen</c> clear again, the cursor following the
	/// mouse and no frame draw skipped. Catches the camera, the rect, the view or the freeze not put back, the hook's
	/// steer taken as the throttle, or the skip left set.
	/// </summary>
	[Fact]
	public void TheRaiseOrbitsThePlayerAndPutsBackTheViewTheCameraAndTheFreeze() {
		var rig = Rig.Start();
		var views = rig.Views;
		var camera = rig.Simulation.ViewChain.Camera;
		rig.Keys.Keyboard.Queue.Push(F12);

		Assert.False(rig.AdvanceUntil(() => rig.Panels.OpenCount == 1));
		Assert.Equal(Es1CockpitViews.ExternalView, views.CurrentView);
		Assert.Equal(Es1PreferencesPanel.Top - 1, views.ExternalViewRect.ViewportY1);
		Assert.Equal((Es1Camera.Orbit, rig.Player), (camera.Mode, camera.Object));
		Assert.True(rig.Simulation.Frozen);
		Assert.False(rig.AdvanceUntil(() => camera.Rates.Heading == -0x200));

		int ticks = rig.Pacer.Ticks;
		rig.Keys.Keyboard.Queue.Push(Esc);
		Assert.False(rig.AdvanceUntil(() => rig.Pacer.Ticks > ticks));

		Assert.Equal(0, views.CurrentView);
		Assert.Equal(Six(Es1ViewManager.ExternalViewDefaultRect), Six(views.ExternalViewRect));
		Assert.Equal((Es1Camera.Attached, rig.Player, ((short)0, (short)0, (short)0)), (camera.Mode, camera.Object, camera.Rates));
		Assert.False(rig.Simulation.Frozen);
		Assert.True(views.Mouse.FollowsMouse);
		Assert.False(views.Cockpit.SkipFrameDraw);
	}

	/// <summary>
	/// A bar of range 255 on columns 227 to 298 at 128 lights 227 to 227 + (128 · (71 · <c>0x10000</c> / 255) &gt;&gt; 16)
	/// = 262 in its shades, each column's own parity picking it, 49 for odd 227 and 50 for even 228, and leaves 263 to 298
	/// unlit. Catches the shades picked by the offset from the start, the unlit run starting at the lit end, or the span
	/// rounded other than by the integer division.
	/// </summary>
	[Fact]
	public void ABarLightsItsColumnsInTheirOwnShadeUpToTheValue() {
		var display = new Es1VideoMemory();
		var canvas = new Es1PanelCanvas(display, new Es1Rect(0, 0, 319, 199));
		var bar = new Es1LedBarGraph(0xff, new Es1Rect(227, 32, 298, 39)) { UnlitColor = 0x3f, EvenShade = 50, OddShade = 49 };

		bar.RepaintToValue(canvas, 128);

		var row = Enumerable.Range(0, 320).Select(x => (int)display.Cell(display.DrawPage, x, 35)).ToArray();
		Assert.Equal(Enumerable.Range(227, 36).Select(x => x % 2 == 0 ? 50 : 49), row[227..263]);
		Assert.All(row[263..299], cell => Assert.Equal(0x3f, cell));
		Assert.Equal(0, row[226]);
		Assert.Equal(0, row[299]);
	}

	/// <summary>A <c>.VUE</c> record's rect and projection pair.</summary>
	private static (int, int, int, int, int, int) Six(Vue.Entry r) =>
		(r.ViewportX0, r.ViewportY0, r.ViewportX1, r.ViewportY1, r.CenterX, r.CenterY);

	/// <summary>One pass of the panel's loop after <paramref name="key"/> is queued; fails when the panel has closed.</summary>
	private static void Pass(Rig rig, IEnumerator<Es1Wait> passes, ushort key) {
		rig.Keys.Keyboard.Queue.Push(key);
		Assert.True(passes.MoveNext(), "The panel closed.");
	}

	/// <summary>A preferences panel run until [Esc] closes it.</summary>
	private static void CloseWithEsc(Rig rig) {
		var panel = new Es1PreferencesPanel(rig.Panels);
		using var passes = panel.Run(new StrongBox<int>()).GetEnumerator();
		Assert.True(passes.MoveNext());
		rig.Keys.Keyboard.Queue.Push(Esc);
		for (int pass = 0; passes.MoveNext(); pass++) {
			Assert.True(pass < MaxFrames, $"The panel was still up after {MaxFrames} passes.");
		}

		panel.Destroy();
	}

	/// <summary>The simulation on a player's machine and constructed views, the panels on its display, and the frame loop paced by its clock and handed to it.</summary>
	private sealed record Rig(Es1Simulation Simulation, Es1Mech Player, Es1ViewManager Views, Es1FrameLoop Frames,
			Es1KeyboardInput Keys, SimulationPacer Pacer) {
		public Es1AlertGlobals Panels => Simulation.Panels!;

		public static Rig Start(Es1Preferences? preferences = null) {
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
				},
				Frame = frames,
			};
			return new Rig(simulation, player, views, frames, keys, new SimulationPacer(simulation));
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

	/// <summary>The simulation's clock and tick as the frame loop's pacer, counting the ticks.</summary>
	private sealed class SimulationPacer(Es1Simulation simulation) : IEs1FramePacer {
		public int Ticks { get; private set; }

		public bool Frozen => simulation.Frozen;

		public bool AdvanceClock(TimeSpan elapsed) => simulation.Globals.Clock.AdvanceHostFrame(elapsed);

		public IEnumerable<Es1Wait> Tick(StrongBox<bool> ended) {
			Ticks++;
			return simulation.MainTick(ended);
		}

		public void Hold(TimeSpan elapsed) => simulation.Globals.Clock.Hold(elapsed);
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
		globals.LiveObjects.Add(mech);
		return mech;
	}
}
