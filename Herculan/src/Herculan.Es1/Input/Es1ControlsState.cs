namespace Herculan.Es1.Input;

/// <summary>
/// The controls' state the input build reads and the simulation also writes: the stick mode, which keypad 0 toggles,
/// <c>ThrottleLeverMode</c>, which <c>Ctrl+1</c> flips, whether the camera has the controls, and the four press-once
/// latches the weapons and panels set. The simulation owns one; preferences options 8 and 10 seed it as
/// <c>Prefs_Load</c> (<c>ES1DBSIM 0004a744</c>) runs their handlers.
/// </summary>
public sealed class Es1ControlsState {
	private readonly Es1Preferences _preferences;

	/// <summary>
	/// The state <c>Prefs_Load</c>'s handlers leave: <c>Input_SetStickMode</c> (<c>0004ad88</c>) copies option 8 into
	/// <see cref="StickMode"/>, and <c>Input_SetThrottleLeverMode</c> (<c>0004ad94</c>) sets
	/// <see cref="ThrottleLeverMode"/> to 1 when option 10 is not 0 and to 0 when it is
	/// (docs/es1/simulation/input.md#the-stick-mode, docs/es1/simulation/mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc).
	/// </summary>
	public Es1ControlsState(Es1Preferences preferences) {
		_preferences = preferences;
		StickMode = preferences.StickMode;
		ThrottleLeverMode = (short)(preferences.ThrottleKeys ? 1 : 0);
	}

	/// <summary><c>Input_StickMode</c> (<c>00095819</c>): 0, 1 (movement) or 2 (turret).</summary>
	public byte StickMode { get; set; }

	/// <summary><c>ThrottleLeverMode</c> (<c>000919ce</c>): 0, 1 (forward only) or −1 (reverse only).</summary>
	public short ThrottleLeverMode { get; set; }

	/// <summary>
	/// <c>InputDrivesCamera</c> (<c>000b5b90</c>): the camera has the controls. Clear at the start; the chain of views
	/// sets it (<see cref="View.Es1ViewChain"/>).
	/// </summary>
	public bool InputDrivesCamera { get; set; }

	/// <summary><c>maybe_WeaponChain_EoHold</c>: a round is being flown.</summary>
	// PLACEHOLDER: false until the rounds of plan step 7.
	public bool RoundInFlight { get; set; }

	/// <summary>The four press-once latches, <c>Input_ButtonLatch</c> and <c>Input_ButtonLatchState</c>.</summary>
	public Es1ButtonLatches Latches { get; } = new();

	/// <summary>
	/// <c>Input_ToggleStickMode</c> (<c>0004d8e1</c>): while option 8 (<c>000bd56a</c>) is not 0, the mode becomes 2 when
	/// it is 1 and 1 otherwise; returns the mode. Keypad 0 and source button 1 call it
	/// (docs/es1/simulation/input.md#the-stick-mode).
	/// </summary>
	public byte ToggleStickMode() {
		if (_preferences.StickMode != 0) {
			StickMode = (byte)(StickMode == 1 ? 2 : 1);
		}

		return StickMode;
	}
}

/// <summary>
/// The four press-once latches: <c>Input_ButtonLatch</c> (<c>ES1DBSIM 000bd634</c>) and <c>Input_ButtonLatchState</c>
/// (<c>000bd638</c>), four bytes each, which <c>Input_BuildSourceTable</c> clears and <c>Input_BuildPlayerDevice</c>
/// applies to the input block's buttons 0–3 (docs/es1/simulation/input.md#the-input-build--input_buildplayerdevice-0004cb14;
/// ES2's docs/retail/simulation/joystick-input.md#a-latched-first-row-holds-the-axes).
/// </summary>
public sealed class Es1ButtonLatches {
	public const int Count = 4;

	private readonly bool[] _latched = new bool[Count];
	private readonly byte[] _state = new byte[Count];

	/// <summary>
	/// <c>Input_LatchButton</c> (<c>0004d8c8</c>)(state, button 1–4): latches the button with
	/// <paramref name="state"/> (docs/es1/simulation/input.md#small-functions).
	/// </summary>
	public void Latch(byte state, int button) {
		_latched[button - 1] = true;
		_state[button - 1] = state;
	}

	/// <summary><c>Input_BuildSourceTable</c>'s clear of the four latches.</summary>
	public void Clear() {
		Array.Clear(_latched);
		Array.Clear(_state);
	}

	/// <summary>Button <paramref name="index"/>, 0–3, is latched.</summary>
	public bool IsLatched(int index) => _latched[index];

	/// <summary>Button 0 is latched with a state that is not 0, the test that zeroes the camera's axes.</summary>
	internal bool FirstHeld => _latched[0] && _state[0] != 0;

	/// <summary>
	/// The input build's pass over buttons 0–3: a latched button reading its latched state reads the opposite
	/// (the state xor 1), and one reading anything else drops its latch.
	/// </summary>
	internal void Apply(byte[] buttons) {
		for (int i = 0; i < Count; i++) {
			if (!_latched[i]) {
				continue;
			}

			if (_state[i] == buttons[i]) {
				buttons[i] = (byte)(_state[i] ^ 1);
			} else {
				_latched[i] = false;
			}
		}
	}
}
