namespace Herculan.Es1.Input;

/// <summary>
/// The input build: <c>Input_BuildSourceTable</c> (<c>ES1DBSIM 0004c95c</c>)'s set-up and
/// <c>Input_BuildPlayerDevice</c> (<c>0004cb14</c>), which turns the keyboard, the mouse and the joysticks into the
/// input block once a tick (docs/es1/simulation/input.md#the-input-build--input_buildplayerdevice-0004cb14). The tick
/// calls <see cref="Build"/> first.
/// </summary>
public sealed class Es1InputBuild {
	/// <summary><c>SimCommandMask</c> (<c>000958b8</c>): drops <c>0x800</c>, so a shifted key is the plain key.</summary>
	public const ushort SimCommandMask = 0x47ff;

	private readonly Es1KeyboardInput _keyboard;
	private readonly Es1DevicePoll _poll;
	private readonly Es1Preferences _preferences;
	private readonly Es1KeyjoyConfig _keyjoy;

	// 000958bc: source button 1 has toggled the stick mode and not been let go.
	private bool _toggleHeld;

	// 000958bf: the missile arm, 1 while a round is flown, 2 once button 0's latch holds with it at 1, 0 when the
	// latch is clear.
	private byte _missileArm;

	/// <summary>
	/// <c>Input_BuildSourceTable</c>'s part from the joystick devices on: the devices built on <paramref name="port"/>
	/// (<c>Input_CreateJoystickDevices</c>, which <c>Prefs_Init</c> runs last), the live block polled once, the four
	/// latches cleared, and <paramref name="keyjoy"/>, <c>Keyjoy_LoadConfig</c>'s read. Option 10 goes to the axis
	/// keys, which read it for <c>Ctrl+I</c> and <c>Ctrl+M</c>.
	/// </summary>
	public Es1InputBuild(Es1KeyboardInput keyboard, Es1HostMouse mouse, Es1Preferences preferences,
		Es1KeyjoyConfig keyjoy, Es1ControlsState controls, IEs1GamePort port) {
		_keyboard = keyboard;
		Joysticks = Es1JoystickDevices.Create(preferences, port);
		_poll = new Es1DevicePoll(keyboard, mouse, Joysticks);
		_preferences = preferences;
		_keyjoy = keyjoy;
		Controls = controls;
		keyboard.AxisKeys.ThrottleKeysOption = preferences.ThrottleKeys;
		LiveBlock = _poll.Poll();
		controls.Latches.Clear();
	}

	/// <summary>The joystick devices option 7 and option 9 name.</summary>
	public Es1JoystickDevices Joysticks { get; }

	/// <summary>The controls' state, which the simulation also writes.</summary>
	public Es1ControlsState Controls { get; }

	/// <summary>The cockpit's mouse, whose queue step 4 drains (<c>CockpitMouse_ProcessQueue</c>); none with no cockpit.</summary>
	public Es1CockpitMouse? CockpitMouse { get; set; }

	/// <summary><c>Input_LiveBlockPtr</c> (<c>000b1840</c>): the last poll, as the build left it.</summary>
	public Es1LiveBlock LiveBlock { get; private set; }

	/// <summary>
	/// <c>Input_BuildPlayerDevice</c>: one tick's input block, in retail's order. The stick mode read at entry decides
	/// the keyboard's slow keys and whether source button 1 may toggle it; the mode after that toggle decides the rest.
	/// </summary>
	public Es1PlayerInputBlock Build() {
		var block = new Es1PlayerInputBlock();
		byte modeAtEntry = Controls.StickMode;
		byte mode = modeAtEntry;

		// 1. The live block and its command word, skipping words whose scancode byte is 0, masked and stored back.
		var live = LiveBlock = _poll.Poll();
		ushort word = live.CommandWord;
		while (word != 0 && (word & 0xff) == 0) {
			word = _poll.ReadKeyboardCommand();
		}

		live.CommandWord = (ushort)(word & SimCommandMask);

		// 2. PLACEHOLDER: the tape's playback, which fills the block from TapeFile under -p and -D, and DemoMode are not
		// ported; no plan step retires them.

		// 3. The keyboard's axes; the source buttons are the live block's +0x1d.
		bool camera = Controls.InputDrivesCamera;
		bool roundFlown = Controls.RoundInFlight;
		var keys = _keyboard.BuildKeyboardAxes((modeAtEntry == 1 && !camera) || roundFlown);
		int[] kb = [keys.FirstX, keys.FirstY, keys.SecondX, keys.SecondY];
		int[] stick = live.StickAxes;

		// 4. The frame's mouse events on the cockpit's widgets; what CockpitMouse_ProcessQueue returns only the tape's
		// recording reads, and tapes are not taken.
		CockpitMouse?.ProcessQueue();
		if (live.StickButtons[1] == 0) {
			_toggleHeld = false;
		} else if (modeAtEntry != 0 && !_toggleHeld) {
			mode = Controls.ToggleStickMode();
			_toggleHeld = true;
		}

		// 5. The missile arm.
		var latches = Controls.Latches;
		if (roundFlown) {
			_missileArm = 1;
		}

		if (latches.FirstHeld && _missileArm == 1) {
			_missileArm = 2;
		} else if (!latches.IsLatched(0)) {
			_missileArm = 0;
		}

		if (_missileArm != 0) {
			kb[0] = 0;
			kb[1] = 0;
		}

		// 6. The keyboard's pairs against the stick mode, the camera and a flown round.
		if (!camera && !roundFlown) {
			if (mode == 1) {
				if (kb[0] != 0) {
					kb[2] = kb[0];
					kb[0] = 0;
				}

				if (kb[1] != 0) {
					kb[3] = -kb[1];
					kb[1] = 0;
				}
			}
		} else {
			if (roundFlown && !_keyjoy.MissileReversed) {
				kb[1] = -kb[1];
			}

			for (int axis = 0; axis < 2; axis++) {
				if (stick[axis] != 0) {
					kb[axis] = stick[axis];
					stick[axis] = 0;
				}
			}

			kb[2] = 0;
			kb[3] = 0;
			stick[2] = 0;
			stick[3] = 0;
		}

		if (_keyjoy.TiltReversed) {
			kb[3] = -kb[3];
		}

		bool throttleKeys = _preferences.ThrottleKeys && !roundFlown;

		// 7. The configuration's sources, the latches, then the pointer, mouse, buttons 4-7 and command word.
		byte[] stickButtons = live.StickButtons[..4];
		var sources = new Es1InputSources(kb, stick, [keys.Button(0), keys.Button(1), keys.Button(2), keys.Button(3)], stickButtons);
		var (axes, buttons) = Es1StickConfigurations.Choose(_preferences.StickConfiguration, mode, _preferences.ThirdAxis, throttleKeys, sources);
		latches.Apply(buttons);
		Array.Copy(axes, block.Axes, 4);
		Array.Copy(buttons, block.Buttons, 4);
		block.PointerX = live.PointerX;
		block.PointerY = live.PointerY;
		Array.Copy(live.MouseButtons, block.MouseButtons, 3);
		Array.Copy(live.StickButtons, 4, block.Buttons, 4, 4);
		block.CommandWord = live.CommandWord;

		// 8. PLACEHOLDER: the tape's recording, which appends the frame to <TapeStem>.tap under -r, is not ported; no
		// plan step retires it.

		// 9. The camera's axes, and option 10's throttle step.
		if (!camera && !roundFlown) {
			int first = mode == 2 ? Es1PlayerInputBlock.Twist : Es1PlayerInputBlock.Steering;
			if (latches.FirstHeld) {
				block.Axes[first] = 0;
				block.Axes[first + 1] = 0;
			}

			block.CameraAxes = mode == 2 ? Es1CameraAxes.Turret : Es1CameraAxes.Movement;
			if (throttleKeys) {
				block.Axes[Es1PlayerInputBlock.Throttle] = _keyboard.AxisKeys.ThrottleStep;
			}

			_keyboard.AxisKeys.ThrottleStep = 0;
		} else {
			block.CameraAxes = Es1CameraAxes.Movement;
		}

		// 10. Backturn.
		if (_keyjoy.BackturnReversed && block.Axes[Es1PlayerInputBlock.Throttle] > 0) {
			block.Axes[Es1PlayerInputBlock.Steering] = (short)-block.Axes[Es1PlayerInputBlock.Steering];
		}

		return block;
	}
}
