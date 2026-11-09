using Herculan.Es1.Input;
using Silk.NET.Input;
using Xunit;

namespace Herculan.Engine.Tests;

public class Es1KeyboardTests {
	private const byte Keypad8 = 0x48, Keypad4 = 0x4b, Enter = 0x1c, Q = 0x10, M = 0x32, Alt = 0x38, Control = 0x1d, Break = 0x80;

	/// <summary>
	/// Keypad 8 is claimed by the axis keys while they are on and queued as a command while they are off. Catches the
	/// subscribers walked oldest first, the axis keys' claim read as a pass-on, and the axis keys claiming while off.
	/// </summary>
	[Fact]
	public void AxisKeyIsQueuedOnlyWhileTheKeysAreOff() {
		var input = new Es1KeyboardInput(0);

		Type(input, Keypad8, Keypad8 | Break);
		Assert.Equal(0, input.PopCommand());

		input.AxisKeys.SetEnabled(false);
		Type(input, Keypad8);
		Assert.Equal(Keypad8, input.PopCommand());
	}

	/// <summary>
	/// <c>Enter</c> is held by the axis keys and queued as a command. Catches the axis keys claiming <c>Enter</c>, and
	/// the block's <c>Enter</c> byte read from another key.
	/// </summary>
	[Fact]
	public void EnterReachesTheAxisKeysAndTheQueue() {
		var input = new Es1KeyboardInput(0);

		Type(input, Enter);

		Assert.Equal(Enter, input.PopCommand());
		var block = input.BuildKeyboardAxes(slow: true);
		Assert.True(block.Enter);
		Assert.False(block.Space);
	}

	/// <summary>
	/// With the axis keys off, two keys typed and released queue their two make codes and nothing else. Catches the
	/// queue registered against key-ups, and the code masks indexed by the byte's low seven bits.
	/// </summary>
	[Fact]
	public void KeyUpIsNeverQueued() {
		var input = new Es1KeyboardInput(0);
		input.AxisKeys.SetEnabled(false);

		Type(input, Q, Q | Break, Keypad8, Keypad8 | Break);

		Assert.Equal(Q, input.PopCommand());
		Assert.Equal(Keypad8, input.PopCommand());
		Assert.Equal(0, input.PopCommand());
	}

	/// <summary>
	/// <c>Alt</c>'s own make code carries no <c>0x200</c> and its key-up does; the key between them does. Catches the
	/// modifiers updated before the code is made.
	/// </summary>
	[Fact]
	public void AltBitIsTakenFromBeforeTheByte() {
		var input = new Es1KeyboardInput(0);
		var altCodes = new List<ushort>();
		input.Keyboard.AddSubscriber(code => {
			altCodes.Add(code);
			return Es1Keyboard.Claimed;
		}, [Alt, Alt | Break]);

		Type(input, Alt, Q, Alt | Break, Q);

		Assert.Equal([Alt, Es1Keyboard.AltBit | Alt | Break], altCodes);
		Assert.Equal(Es1Keyboard.AltBit | Q, input.PopCommand());
		Assert.Equal(Q, input.PopCommand());
	}

	/// <summary>
	/// The ring takes 127 words and drops the 128th, keeping the first 127 in order. Catches a ring that holds all 128,
	/// and one that overwrites its oldest word.
	/// </summary>
	[Fact]
	public void QueueDropsThe128thWord() {
		var queue = new Es1KeyQueue();
		for (ushort word = 1; word <= 128; word++) {
			queue.Push(word);
		}

		for (ushort word = 1; word <= 127; word++) {
			Assert.Equal(word, queue.Pop());
		}

		Assert.Equal(0, queue.Pop());
		Assert.True(queue.Overflowed);
	}

	/// <summary>
	/// Keypad 8 moves a quarter of <c>0x100</c> with <i>slow</i> and the whole without it or with <c>Alt</c>; <c>M</c>
	/// moves a quarter whatever <i>slow</i> says, and the whole with <c>Alt</c>. Hand-worked from the shifts 6 and 8.
	/// Catches ES2's half-travel shift, <i>slow</i> applied to keys 8–11, and the <c>Alt</c> bit lost on its way to the
	/// build.
	/// </summary>
	[Theory]
	[InlineData(new byte[] { Keypad8 }, true, -0x40, 0)]
	[InlineData(new byte[] { Keypad8 }, false, -0x100, 0)]
	[InlineData(new byte[] { Alt, Keypad8 }, true, -0x100, 0)]
	[InlineData(new byte[] { M }, false, 0, -0x40)]
	[InlineData(new byte[] { Alt, M }, true, 0, -0x100)]
	public void KeyTravelIsAQuarterOrTheWhole(byte[] scancodes, bool slow, int firstY, int secondY) {
		var input = new Es1KeyboardInput(0);

		Type(input, scancodes);
		var block = input.BuildKeyboardAxes(slow);

		Assert.Equal(firstY, block.FirstY);
		Assert.Equal(secondY, block.SecondY);
		Assert.Equal(0, block.FirstX);
		Assert.Equal(0, block.SecondX);
	}

	/// <summary>
	/// A key pressed and released between two builds moves the axis in the first and not the second. Catches the
	/// key-up clearing the held byte before a build has seen it, and the build leaving a released key held.
	/// </summary>
	[Fact]
	public void TapBetweenBuildsCountsForOneBuild() {
		var input = new Es1KeyboardInput(0);

		Type(input, Keypad4, Keypad4 | Break);

		Assert.Equal(-0x40, input.BuildKeyboardAxes(slow: true).FirstX);
		Assert.Equal(0, input.BuildKeyboardAxes(slow: true).FirstX);
	}

	/// <summary>
	/// The grey <c>Up</c> sends keypad 8's byte with no <c>E0</c>, repeats after the delay and not on the host's own
	/// repeated key-down, stops on its key-up, and the keypad's <c>Enter</c> sends <c>Enter</c>'s byte. Catches a prefix
	/// sent before a grey key, the host's repeats sent beside the typematic's, a repeat outliving its key, and a grey
	/// key left unmapped.
	/// </summary>
	[Fact]
	public void HostSendsSetOneBytesWithTheTypematicRepeat() {
		var sent = new List<byte>();
		var host = new Es1HostKeyboard(sent.Add, (Delay: 0.5, Interval: 0.1));

		host.KeyDown(Key.Up);
		host.KeyDown(Key.Up);
		host.Advance(0.4);
		host.Advance(0.15);
		host.KeyUp(Key.Up);
		host.Advance(1.0);
		host.KeyDown(Key.KeypadEnter);

		Assert.Equal([Keypad8, Keypad8, Keypad8 | Break, Enter], sent);
	}

	/// <summary>
	/// [Alt+Enter] toggles once and sends only the <c>Alt</c>'s bytes, through the host's repeat and the key-up; with
	/// <c>Ctrl</c> also held, as AltGr is, and once <c>Alt</c> is up, <c>Enter</c> is the game's. Catches the toggle's
	/// <c>Enter</c> reaching the game, so a dialog that closes on it closes; a host repeat toggling again; its key-up
	/// sending a break byte; and AltGr+Enter taken.
	/// </summary>
	[Fact]
	public void AltEnterTogglesWithoutTheGameSeeingEnter() {
		var sent = new List<byte>();
		int toggles = 0;
		var host = new Es1HostKeyboard(sent.Add, (Delay: 0.5, Interval: 0.1)) {
			FullScreenKey = new Es1FullScreenKey(() => toggles++),
		};

		host.KeyDown(Key.AltLeft);
		host.KeyDown(Key.Enter);
		host.KeyDown(Key.Enter);
		host.KeyUp(Key.Enter);
		host.KeyDown(Key.ControlLeft);
		host.KeyDown(Key.KeypadEnter);
		host.KeyUp(Key.KeypadEnter);
		host.KeyUp(Key.ControlLeft);
		host.KeyUp(Key.AltLeft);
		host.KeyDown(Key.Enter);

		Assert.Equal(1, toggles);
		Assert.Equal([Alt, Control, Enter, Enter | Break, Control | Break, Alt | Break, Enter], sent);
	}

	private static void Type(Es1KeyboardInput input, params int[] bytes) {
		foreach (int b in bytes) {
			input.Keyboard.Dispatch((byte)b);
		}
	}

	private static void Type(Es1KeyboardInput input, byte[] bytes) {
		foreach (byte b in bytes) {
			input.Keyboard.Dispatch(b);
		}
	}
}
