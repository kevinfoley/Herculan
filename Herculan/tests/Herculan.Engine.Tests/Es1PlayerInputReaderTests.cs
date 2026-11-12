using Herculan.Es1.Input;
using Silk.NET.Input;
using Xunit;

namespace Herculan.Engine.Tests;

public class Es1PlayerInputReaderTests {
	private const byte Keypad8 = 0x48, Keypad9 = 0x49, Keypad3 = 0x51, Space = 0x39, I = 0x17, Ctrl = 0x1d, Break = 0x80;

	/// <summary>
	/// In configuration 1 the steering reads the stick's X where it has moved and the keyboard's first X where it
	/// reads 0. Catches the keyboard fallback dropped, and the keyboard preferred over a moved stick.
	/// </summary>
	[Fact]
	public void StickAxisAtZeroFallsBackToTheKeyboard() {
		var sources = new Es1InputSources([0x40, 0, 0, 0], [0, 0, 0, 0], new byte[4], new byte[4]);
		Assert.Equal(0x40, Es1StickConfigurations.Choose(1, 0, false, false, sources).Axes[Es1PlayerInputBlock.Steering]);

		sources.Stick[0] = 0x55;
		Assert.Equal(0x55, Es1StickConfigurations.Choose(1, 0, false, false, sources).Axes[Es1PlayerInputBlock.Steering]);
	}

	/// <summary>
	/// In stick mode 1 keypad 8 moves from the throttle onto the pitch, negated, at a quarter's travel. Catches the move
	/// skipped, the pitch not negated, and the mode read from somewhere other than option 8's handler.
	/// </summary>
	[Fact]
	public void StickModeOneMovesTheKeyboardsFirstPairOntoTheSecondWithItsPitchNegated() {
		var input = new Es1KeyboardInput(0);
		var reader = NewReader(input, Prefs((Es1Preferences.StickModeOption, 1)));

		Type(input, Keypad8);
		var block = reader.Read();

		Assert.Equal(0, block.ThrottleAxis);
		Assert.Equal(0x40, block.PitchAxis);
		Assert.Equal(Es1CameraAxes.Movement, block.CameraAxes);
	}

	/// <summary>
	/// With <c>BACKTURN = REVERSE</c> keypad 3 (steering right, throttle positive) steers left, and keypad 9 (throttle
	/// negative) steers right. Catches Backturn applied whatever the throttle's sign, and on the wrong sign.
	/// </summary>
	[Fact]
	public void BackturnNegatesTheSteeringOnlyWhileTheThrottleAxisIsPositive() {
		var input = new Es1KeyboardInput(0);
		var reader = NewReader(input, Prefs(), "BACKTURN = REVERSE");

		Type(input, Keypad3);
		var backing = reader.Read();
		Type(input, Keypad3 | Break, Keypad9);
		var forward = reader.Read();

		Assert.True(backing.ThrottleAxis > 0);
		Assert.Equal(-0x100, backing.SteeringAxis);
		Assert.True(forward.ThrottleAxis < 0);
		Assert.Equal(0x100, forward.SteeringAxis);
	}

	/// <summary>
	/// With option 10 on, <c>Ctrl+I</c>'s step reaches the throttle axis on the next read and not the one after, and
	/// the option's handler sets <c>ThrottleLeverMode</c> to 1 for any value but 0. Catches the step not put on the
	/// throttle, the step not cleared once taken, and the handler storing the option's value.
	/// </summary>
	[Fact]
	public void ThrottleKeysPutTheStepOnTheThrottleAxisOnce() {
		var input = new Es1KeyboardInput(0);
		var reader = NewReader(input, Prefs((Es1Preferences.ThrottleKeysOption, 5)));

		Type(input, Ctrl, I, I | Break, Ctrl | Break);

		Assert.Equal(1, reader.Controls.ThrottleLeverMode);
		Assert.Equal(-0x40, reader.Read().ThrottleAxis);
		Assert.Equal(0, reader.Read().ThrottleAxis);
	}

	/// <summary>
	/// Button 0 latched with state 1 reads 0 while <c>Space</c> holds it at 1, and the latch drops once it reads 0, so
	/// the next press reads 1. Catches the latched state passed through unchanged, and a latch never dropped.
	/// </summary>
	[Fact]
	public void LatchedButtonReadsTheOppositeOfItsStateUntilItDiffers() {
		var input = new Es1KeyboardInput(0);
		var reader = NewReader(input, Prefs());
		reader.Controls.Latches.Latch(1, 1);

		Type(input, Space);
		Assert.Equal(0, reader.Read().Buttons[0]);
		Assert.True(reader.Controls.Latches.IsLatched(0));

		// No read marks Space seen, so its release is pending through one more read, which still reads it held.
		Type(input, Space | Break);
		reader.Read();
		Assert.Equal(0, reader.Read().Buttons[0]);
		Assert.False(reader.Controls.Latches.IsLatched(0));

		Type(input, Space);
		Assert.Equal(1, reader.Read().Buttons[0]);
	}

	/// <summary>
	/// <c>REVERSE</c> before any key word stops the read, so a later <c>TILT = REVERSE</c> is not seen; a <c>#</c> line
	/// is skipped whole. Catches the stop rule dropped, and a comment read as words.
	/// </summary>
	[Fact]
	public void KeyjoyReverseWithNoKeyStopsTheRead() {
		Assert.False(Es1KeyjoyConfig.Parse("= REVERSE\nTILT = REVERSE").TiltReversed);
		Assert.True(Es1KeyjoyConfig.Parse("tilt = reverse").TiltReversed);

		var commented = Es1KeyjoyConfig.Parse("# TILT = REVERSE\nBACKTURN = REVERSE");
		Assert.False(commented.TiltReversed);
		Assert.True(commented.BackturnReversed);
	}

	/// <summary>
	/// The host's right button reads at <c>+0xb</c> on every read from its press to its release. Catches the button
	/// read as a one-tick event, and the right button put at the left's byte.
	/// </summary>
	[Fact]
	public void MouseButtonHoldsFromOneHostEventToTheNext() {
		var input = new Es1KeyboardInput(0);
		var mouse = new Es1HostMouse();
		// Option 11 on, or Prefs_ApplyMouseOption turns the driver off at the start.
		var prefs = Prefs((Es1Preferences.MouseOption, 1));
		var reader = new Es1PlayerInputReader(input, mouse, prefs, Es1KeyjoyConfig.Parse(""), new Es1ControlsState(prefs), Es1NoStickPort.Instance);

		mouse.OnButton(MouseButton.Right, true);
		Assert.Equal(new byte[] { 0, 1, 0 }, reader.Read().MouseButtons);
		Assert.Equal(new byte[] { 0, 1, 0 }, reader.Read().MouseButtons);

		mouse.OnButton(MouseButton.Right, false);
		Assert.Equal(new byte[] { 0, 0, 0 }, reader.Read().MouseButtons);
	}

	private static Es1PlayerInputReader NewReader(Es1KeyboardInput input, Es1Preferences prefs, string keyjoy = "") =>
		new(input, new Es1HostMouse(), prefs, Es1KeyjoyConfig.Parse(keyjoy), new Es1ControlsState(prefs), Es1NoStickPort.Instance);

	private static Es1Preferences Prefs(params (int Option, byte Value)[] options) {
		var bytes = new byte[Es1Preferences.OptionCount];
		foreach (var (option, value) in options) {
			bytes[option] = value;
		}

		return Es1Preferences.FromFile(bytes);
	}

	private static void Type(Es1KeyboardInput input, params int[] scancodes) {
		foreach (int scancode in scancodes) {
			input.Keyboard.Dispatch((byte)scancode);
		}
	}
}
