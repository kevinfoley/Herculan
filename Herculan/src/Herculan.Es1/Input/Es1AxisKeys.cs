namespace Herculan.Es1.Input;

/// <summary>
/// The axis keys: <c>Input_KeyjoyAxisKey</c> (<c>ES1DBSIM 0004c6ec</c>), the keyboard subscriber that holds the
/// keypad's eight keys, <c>M</c>, <c>J</c>, <c>K</c>, <c>I</c>, <c>Space</c> and <c>Enter</c> in three 14-byte key-state
/// blocks, and <c>SimInput_SetEnabled</c> (<c>0004d82c</c>), which registers it and turns the keys on and off
/// (docs/es1/simulation/input.md#the-axis-keys--input_keyjoyaxiskey-0004c6ec,
/// #the-simulators-two-subscribers and #small-functions), with <c>SimInput_PushDisable</c> and
/// <c>SimInput_PopEnable</c> (docs/es1/simulation/main-loop.md#small-functions). <see cref="BuildKeyboardAxes"/>
/// reads the blocks.
/// </summary>
public sealed class Es1AxisKeys {
	/// <summary>The keys the blocks hold: keys 0–7 the keypad's, 8–11 <c>M</c>, <c>J</c>, <c>K</c>, <c>I</c>, 12 <c>Space</c>, 13 <c>Enter</c>.</summary>
	public const int KeyCount = 14;

	/// <summary>
	/// <c>KeyjoyWantedCodes</c> (<c>0009586c</c>): the fourteen keys' make codes in key order, then the same fourteen
	/// with the key-up bit.
	/// </summary>
	private static readonly byte[] WantedCodes = BuildWantedCodes();

	private const byte EnterScancode = 0x1c;

	// The two scancodes the throttle step's table at 00095898 acts on: I subtracts the step, M adds it.
	private const byte ThrottleUpScancode = 0x17, ThrottleDownScancode = 0x32;
	private const short ThrottleStepSize = 0x40;

	private readonly Es1Keyboard _keyboard;
	private readonly Es1KeySubscriber _subscriber;

	// SimInput_PushDisable's byte stack at 000b5df8, indexed by the byte 00097280.
	private readonly byte[] _enabledStack = new byte[256];
	private byte _stackDepth;

	/// <param name="keyboard">The keyboard <see cref="SetEnabled"/> registers the axis keys with.</param>
	public Es1AxisKeys(Es1Keyboard keyboard) {
		_keyboard = keyboard;
		_subscriber = OnKey;
	}

	/// <summary><c>Input_KeyHeld</c> (<c>000b1844</c>): the key is held, or was pressed since the last build.</summary>
	internal byte[] Held { get; } = new byte[KeyCount];

	/// <summary>The release-pending block (<c>000b1854</c>): the key went up before a build saw it held.</summary>
	internal byte[] ReleasePending { get; } = new byte[KeyCount];

	/// <summary>The seen block (<c>000b1864</c>): a build has counted the key since it was pressed.</summary>
	internal byte[] Seen { get; } = new byte[KeyCount];

	/// <summary><c>SimInput_Enabled</c> (<c>00095818</c>): the axis keys hold keys rather than passing them on.</summary>
	public bool Enabled { get; private set; }

	/// <summary><c>Input_KeyAltHeld</c> (<c>000b18a0</c>): the <c>Alt</c> bit, <c>0x200</c>, of the last code the axis keys held.</summary>
	public short AltHeld { get; private set; }

	/// <summary>
	/// <c>Input_KeyThrottleStep</c> (<c>00095896</c>): what option 10's <c>Ctrl</c>+<c>I</c> and <c>Ctrl</c>+<c>M</c> have
	/// stepped since the input build last took it as the throttle axis and cleared it.
	/// </summary>
	public short ThrottleStep { get; set; }

	/// <summary>Preferences option 10 (<c>000bd56c</c>), set by the input build's owner.</summary>
	public bool ThrottleKeysOption { get; set; }

	/// <summary><c>DemoMode</c>, set by the owner of a demo's playback: the axis keys claim all 28 codes and raise <see cref="DemoAbort"/>.</summary>
	public bool DemoMode { get; set; }

	/// <summary><c>DemoAbort</c>, which an axis key under <see cref="DemoMode"/> sets; its owner reads and clears it.</summary>
	public bool DemoAbort { get; set; }

	/// <summary>
	/// <c>Input_KeyjoyAxisKey</c> (<c>0004c6ec</c>), called by the keyboard with one of its 28 codes. Answers
	/// <see cref="Es1Keyboard.Claimed"/> or <see cref="Es1Keyboard.PassOn"/>.
	/// </summary>
	public int OnKey(ushort code) {
		if (DemoMode) {
			DemoAbort = true;
			return Es1Keyboard.Claimed;
		}

		if (ThrottleKeysOption && (code & Es1Keyboard.CtrlBit) != 0 && (code & Es1Keyboard.AltBit) == 0) {
			// The four dwords at 00095898 hold I and M and their key-ups; only the two make codes step.
			switch (code & 0xff) {
				case ThrottleUpScancode:
					ThrottleStep -= ThrottleStepSize;
					break;
				case ThrottleDownScancode:
					ThrottleStep += ThrottleStepSize;
					break;
			}

			return Es1Keyboard.Claimed;
		}

		if (!Enabled) {
			return Es1Keyboard.PassOn;
		}

		// The code's low seven bits against the 28-byte list, the index folded into 0-13; only the first fourteen
		// can match them.
		int index = 0;
		while (index < WantedCodes.Length && WantedCodes[index] != (code & 0x7f)) {
			index++;
		}

		if (index > KeyCount) {
			index -= KeyCount;
		}

		if (index >= KeyCount) {
			return Es1Keyboard.PassOn;
		}

		if ((code & Es1Keyboard.BreakBit) == 0) {
			Held[index] = 1;
			ReleasePending[index] = 0;
		} else if (Seen[index] == 0) {
			ReleasePending[index] = 1;
		} else {
			ReleasePending[index] = 0;
			Held[index] = 0;
			Seen[index] = 0;
		}

		AltHeld = (short)(code & Es1Keyboard.AltBit);
		return (code & 0x7f) == EnterScancode ? Es1Keyboard.PassOn : Es1Keyboard.Claimed;
	}

	/// <summary>
	/// <c>SimInput_SetEnabled</c> (<c>0004d82c</c>): unchanged, answers 1; turning the keys on registers
	/// <see cref="OnKey"/> against <see cref="WantedCodes"/> in a new slot and answers what the keyboard does, the flag
	/// set even when every slot is taken; turning them off clears the three blocks. Retail first answers 0 when there
	/// is no keyboard device, which the constructor's keyboard rules out; and it registers only while the byte
	/// <c>00095894</c> is clear, of which <c>es2_xref.py</c> finds one read and no write (input.md's Open), so it is not
	/// ported.
	/// </summary>
	public int SetEnabled(bool enabled) {
		if (enabled == Enabled) {
			return 1;
		}

		int answer = 1;
		if (enabled) {
			answer = _keyboard.AddSubscriber(_subscriber, WantedCodes) ? 1 : 0;
		}

		Enabled = enabled;
		if (!enabled) {
			ResetKeyState();
		}

		return answer;
	}

	/// <summary><c>SimInput_PushDisable</c> (<c>0005a5b4</c>): pushes <see cref="Enabled"/> and turns the keys off.</summary>
	public void PushDisable() {
		_enabledStack[_stackDepth++] = (byte)(Enabled ? 1 : 0);
		SetEnabled(false);
	}

	/// <summary><c>SimInput_PopEnable</c> (<c>0005a5d8</c>): turns the keys back to the state <see cref="PushDisable"/> pushed.</summary>
	public void PopEnable() => SetEnabled(_enabledStack[--_stackDepth] != 0);

	/// <summary>Clears the three key-state blocks, as <see cref="SetEnabled"/>(false) does.</summary>
	public void ResetKeyState() {
		Array.Clear(Held);
		Array.Clear(ReleasePending);
		Array.Clear(Seen);
	}

	/// <summary><c>Input_BuildKeyboardAxes</c> (<c>0004c818</c>) over the blocks: <see cref="Es1KeyboardAxes.Build"/>.</summary>
	public Es1KeyboardAxesBlock BuildKeyboardAxes(bool slow) => Es1KeyboardAxes.Build(this, slow);

	private static byte[] BuildWantedCodes() {
		byte[] makes = [0x47, 0x48, 0x49, 0x4b, 0x4d, 0x4f, 0x50, 0x51, 0x32, 0x24, 0x25, 0x17, 0x39, EnterScancode];
		return [.. makes, .. makes.Select(code => (byte)(code | Es1Keyboard.BreakBit))];
	}
}
