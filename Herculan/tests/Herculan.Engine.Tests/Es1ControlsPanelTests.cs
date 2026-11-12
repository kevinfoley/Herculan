using System.Runtime.CompilerServices;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's controls panel (docs/es1/simulation/preferences.md#the-controls-panel--controlspanel_ctor-00048408) on a
/// constructed display and input read, with no files loaded, keys pushed onto the command queue, a constructed game
/// port, and the options saved to a temporary folder.
/// </summary>
public class Es1ControlsPanelTests {
	/// <summary>The most passes a test runs a panel's loop for before it fails.</summary>
	private const int MaxPasses = 600;

	private const ushort Esc = 0x01, Tab = 0x0f, Return = 0x1c;

	/// <summary>Option 7's FLIGHTSTICK PRO, the last model, whose next step forward is NONE.</summary>
	private const byte FlightstickPro = 5;

	/// <summary>
	/// From FLIGHTSTICK PRO, [Return] on JOYSTICK steps option 7 to NONE, but the stick's trigger pressing it steps on to
	/// 1 STANDARD JOYSTICK, so a fire button cannot choose NONE. Catches the trigger's extra step dropped, or taken
	/// whether or not the trigger is held.
	/// </summary>
	[Fact]
	public void TheTriggerCannotStepTheStickModelToNone() {
		var keyed = Rig.Start(Prefs((Es1Preferences.StickConfigurationOption, FlightstickPro)));
		using (var passes = keyed.Run()) {
			keyed.Pass(passes, Return);
		}

		Assert.Equal(0, keyed.Options.StickConfiguration);

		var fired = Rig.Start(Prefs((Es1Preferences.StickConfigurationOption, FlightstickPro)));
		using (var passes = fired.Run()) {
			// The first pass, the trigger up, drops the latch AlertPanel_Enter set on it; the second presses JOYSTICK.
			fired.Pass(passes, 0);
			fired.Port.Buttons = 1;
			fired.Pass(passes, 0);
		}

		Assert.Equal(Es1ControlsPanel.OneStick, fired.Options.StickConfiguration);
	}

	/// <summary>
	/// RCS turned on under 2 STANDARD JOYSTICKS sets option 7 to 1 STANDARD JOYSTICK, and turned off again puts 2
	/// STANDARD JOYSTICKS back. Catches either half of the exchange dropped, or the swap's marker <c>+0x3a1</c> not kept
	/// between the two presses.
	/// </summary>
	[Fact]
	public void TheRudderExchangesTwoSticksForOneAndBack() {
		var rig = Rig.Start(Prefs((Es1Preferences.StickConfigurationOption, Es1ControlsPanel.TwoSticks)));
		using var passes = rig.Run();
		for (int i = 0; i < Es1ControlsPanel.Rcs; i++) {
			rig.Pass(passes, Tab);
		}

		rig.Pass(passes, Return);
		Assert.Equal((Es1ControlsPanel.OneStick, true), (rig.Options.StickConfiguration, rig.Options.ThirdAxis));

		rig.Pass(passes, Return);
		Assert.Equal((Es1ControlsPanel.TwoSticks, false), (rig.Options.StickConfiguration, rig.Options.ThirdAxis));
	}

	/// <summary>
	/// NONE chosen with a stick fitted and DONE pressed destroys the stick's device and enables none in its place (plan
	/// decision 14), so the live block reads no stick, and sets option 8 and the stick mode to 0. Catches the rebuild
	/// skipped, retail's re-enable of the freed device ported, or the stick control left on.
	/// </summary>
	[Fact]
	public void NoneLeavesNoStickEnabledAfterDone() {
		var rig = Rig.Start(Prefs((Es1Preferences.StickConfigurationOption, FlightstickPro), (Es1Preferences.StickModeOption, 1)));
		var joysticks = rig.Input.Joysticks;
		var stick = joysticks.First!;
		Assert.True(stick.InputEnabled);

		using (var passes = rig.Run()) {
			rig.Pass(passes, Return);
			rig.Close(passes);
		}

		Assert.Equal(((byte)0, (byte)0, (byte)0), (rig.Options.StickConfiguration, rig.Options.StickMode, rig.Input.Controls.StickMode));
		Assert.Null(joysticks.First);
		Assert.Null(joysticks.Second);
		Assert.False(stick.InputEnabled);
		rig.Port.Axes[0] = 128;
		rig.Port.Buttons = 1;
		var live = rig.Input.Read();
		Assert.Equal([0, 0, 0, 0], rig.Input.LiveBlock.StickAxes);
		Assert.Equal(0, live.Buttons[0]);
	}

	/// <summary>
	/// DONE writes options 7, 8, 10, 9 and 11 from memory over <c>data\prefs.cfg</c>'s bytes and keeps its other twelve,
	/// the four unchanged ones too, and commits the options, so the changed one's load-time copy is its new value; with
	/// the file gone it writes all 17. Catches the preferences panel's list saved in place of the controls panel's, the
	/// whole array dumped over the file, the save gated on a change, or <c>Prefs_CommitOptions</c> not run.
	/// </summary>
	[Fact]
	public void DoneSavesOptionsSevenToElevenAndCommits() {
		string install = Directory.CreateTempSubdirectory("es1-controls-").FullName;
		try {
			string path = Path.Combine(install, "prefs.cfg");
			byte[] disk = Enumerable.Range(0, Es1Preferences.OptionCount).Select(i => (byte)(100 + i)).ToArray();
			File.WriteAllBytes(path, disk);
			byte[] memory = new byte[Es1Preferences.OptionCount];
			memory[Es1Preferences.StickModeOption] = 2;
			memory[Es1Preferences.MouseOption] = 1;
			var rig = Rig.Start(Es1Preferences.FromFile(memory, path));

			using (var passes = rig.Run()) {
				for (int i = 0; i < Es1ControlsPanel.Wcs; i++) {
					rig.Pass(passes, Tab);
				}

				rig.Pass(passes, Return);
				rig.Close(passes);
			}

			byte[] expected = (byte[])disk.Clone();
			foreach (int option in Es1ControlsPanel.SaveList) {
				expected[option] = rig.Options[option];
			}

			Assert.Equal(1, rig.Options[Es1Preferences.ThrottleKeysOption]);
			Assert.Equal(1, rig.Options.Loaded(Es1Preferences.ThrottleKeysOption));
			Assert.Equal(expected, File.ReadAllBytes(path));

			File.Delete(path);
			using (var passes = rig.Run()) {
				rig.Close(passes);
			}

			Assert.Equal(Enumerable.Range(0, Es1Preferences.OptionCount).Select(i => rig.Options[i]), File.ReadAllBytes(path));
		} finally {
			Directory.Delete(install, true);
		}
	}

	/// <summary>
	/// Option 11 at 0 in <c>data\prefs.cfg</c> turns the mouse driver off at the mission start, since
	/// <c>Prefs_MouseOptionApplied</c> starts at <c>0xff</c>, so a host move leaves the pointer where it was; MOUSE on the
	/// controls panel turns it back on. Catches option 11's handler not registered, or its applied byte starting at 0.
	/// </summary>
	[Fact]
	public void TheMouseOptionTurnsTheDriverOffAndOn() {
		var rig = Rig.Start(Prefs((Es1Preferences.MouseOption, 0)));
		var mouse = rig.Input.Mouse;
		Assert.False(mouse.DriverEnabled);

		mouse.OnButton(Silk.NET.Input.MouseButton.Left, true);
		Assert.Equal(0, mouse.Buttons);

		using var passes = rig.Run();
		for (int i = 0; i < Es1ControlsPanel.Mouse; i++) {
			rig.Pass(passes, Tab);
		}

		rig.Pass(passes, Return);
		Assert.Equal(1, rig.Options[Es1Preferences.MouseOption]);
		Assert.True(mouse.DriverEnabled);
	}

	/// <summary>
	/// The preferences panel's CONTROLS raises the controls panel over it, and [Esc] there closes the controls panel
	/// alone, the preferences panel's loop going on. Catches CONTROLS left raising nothing, or the controls panel's close
	/// taken as the preferences panel's.
	/// </summary>
	[Fact]
	public void ControlsRaisesTheControlsPanelOverThePreferences() {
		var rig = Rig.Start(Es1Preferences.Default);
		var preferences = new Es1PreferencesPanel(rig.Panels);
		using var passes = preferences.Run(new StrongBox<int>()).GetEnumerator();
		Assert.True(passes.MoveNext());
		for (int i = 0; i < Es1PreferencesPanel.Controls; i++) {
			rig.Pass(passes, Tab);
		}

		rig.Pass(passes, Return);
		Assert.Equal(2, rig.Panels.OpenCount);

		rig.Pass(passes, Esc);
		Assert.Equal(1, rig.Panels.OpenCount);
		Assert.Equal(Es1PreferencesPanel.Done, preferences.Focus);
	}

	private static Es1Preferences Prefs(params (int Option, byte Value)[] options) {
		var bytes = new byte[Es1Preferences.OptionCount];
		bytes[Es1Preferences.MouseOption] = 1;
		foreach (var (option, value) in options) {
			bytes[option] = value;
		}

		return Es1Preferences.FromFile(bytes);
	}

	/// <summary>The panels' globals on a display, the cockpit's mouse and an input read on a port with every axis answering.</summary>
	private sealed record Rig(Es1AlertGlobals Panels, Es1PlayerInputReader Input, Es1KeyboardInput Keys, TestPort Port) {
		public Es1Preferences Options => Input.Preferences;

		public static Rig Start(Es1Preferences preferences) {
			var display = new Es1VideoMemory();
			var mouse = new Es1CockpitMouse(display.Cursor);
			var hostMouse = new Es1HostMouse();
			mouse.Init(hostMouse);
			var keys = new Es1KeyboardInput(0);
			var port = new TestPort();
			var input = new Es1PlayerInputReader(keys, hostMouse, preferences, Es1KeyjoyConfig.Parse(""),
				new Es1ControlsState(preferences), port);
			return new Rig(new Es1AlertGlobals(display, mouse, input, null, null), input, keys, port);
		}

		/// <summary>A controls panel's loop, run to its first pass's wait.</summary>
		public IEnumerator<Es1Wait> Run() {
			var passes = new Es1ControlsPanel(Panels).Run(new StrongBox<int>()).GetEnumerator();
			Assert.True(passes.MoveNext());
			return passes;
		}

		/// <summary>One pass of a loop after <paramref name="key"/> is queued, none for 0; fails when the loop has ended.</summary>
		public void Pass(IEnumerator<Es1Wait> passes, ushort key) {
			if (key != 0) {
				Keys.Keyboard.Queue.Push(key);
			}

			Assert.True(passes.MoveNext(), "The panel closed.");
		}

		/// <summary>[Esc], which presses DONE, and the loop run to its end.</summary>
		public void Close(IEnumerator<Es1Wait> passes) {
			Keys.Keyboard.Queue.Push(Esc);
			for (int pass = 0; passes.MoveNext(); pass++) {
				Assert.True(pass < MaxPasses, $"The panel was still up after {MaxPasses} passes.");
			}
		}
	}

	/// <summary>A game port on which all four axes answer, with settable values and buttons.</summary>
	private sealed class TestPort : IEs1GamePort {
		public int[] Axes { get; } = new int[4];

		public byte Buttons { get; set; }

		public bool AxisAnswers(int axis) => true;

		public int ReadAxis(int axis) => Axes[axis];
	}
}
