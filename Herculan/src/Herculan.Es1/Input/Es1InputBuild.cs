using Herculan.Engine.Settings;

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

	// 000958bf: the missile arm, 1 while a missile is flown, 2 once button 0's latch holds with it at 1, 0 when the
	// latch is clear.
	private byte _missileArm;

	/// <summary>
	/// <c>Input_BuildSourceTable</c>'s part from the joystick devices on: option 11's handler,
	/// <see cref="ApplyMouseOption"/>, registered and run with the option as loaded, as <c>Prefs_Init</c> registers it
	/// and <c>Prefs_Load</c> runs it; the devices built on <paramref name="port"/> (<c>Input_CreateJoystickDevices</c>,
	/// which <c>Prefs_Init</c> runs last); the live block polled once; the four latches cleared; and
	/// <paramref name="keyjoy"/>, <c>Keyjoy_LoadConfig</c>'s read. The axis keys read option 10 from
	/// <paramref name="preferences"/> for <c>Ctrl+I</c> and <c>Ctrl+M</c>.
	/// </summary>
	public Es1InputBuild(Es1KeyboardInput keyboard, Es1HostMouse mouse, Es1Preferences preferences,
		Es1KeyjoyConfig keyjoy, Es1ControlsState controls, IEs1GamePort port) {
		_keyboard = keyboard;
		Mouse = mouse;
		_preferences = preferences;
		preferences.SetHandler(Es1Preferences.MouseOption, ApplyMouseOption);
		ApplyMouseOption(preferences[Es1Preferences.MouseOption]);
		Joysticks = Es1JoystickDevices.Create(preferences, port);
		_poll = new Es1DevicePoll(keyboard, mouse, Joysticks);
		_keyjoy = keyjoy;
		Controls = controls;
		keyboard.AxisKeys.ThrottleKeysOption = () => preferences.ThrottleKeys;
		LiveBlock = _poll.Poll();
		controls.Latches.Clear();
	}

	/// <summary>The mouse driver, <c>Input_GetMouseDevice</c>'s, which <c>Input_BuildSourceTable</c> always builds.</summary>
	public Es1HostMouse Mouse { get; }

	/// <summary>
	/// <c>Prefs_MouseOptionApplied</c> (<c>000956c0</c>): option 11 as <see cref="ApplyMouseOption"/> last applied it,
	/// <c>0xff</c> in the image, so the first call always applies.
	/// </summary>
	public byte MouseOptionApplied { get; private set; } = 0xff;

	/// <summary>
	/// The mouse input device's enabled byte (<c>+0x3a</c>), which <c>InputDevice_Enable</c> and
	/// <c>InputDevice_Disable</c> set and clear; while it is clear the live block takes no pointer and no buttons
	/// (<see cref="Es1DevicePoll.MouseDeviceEnabled"/>).
	/// </summary>
	public bool MouseDeviceEnabled {
		get => _poll.MouseDeviceEnabled;
		set => _poll.MouseDeviceEnabled = value;
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
	/// <c>PlayerInputBlock</c> (<c>000b17ac</c>), the block the last <see cref="Build"/> filled and returned; none before
	/// the first. Each build fills a new block here, so a block a caller holds is never changed by a later build.
	/// </summary>
	public Es1PlayerInputBlock? PlayerInputBlock { get; set; }

	/// <summary>
	/// <b>This engine's own:</b> the controller's Custom profile this frame (<see cref="Es1CustomControls"/>), or null while
	/// it is in Original mode, when option 7's configuration reads the stick as retail does. While it answers a frame, a
	/// live build takes the stick's axes, its trigger and buttons 4–7 from it in place of the live block's and the
	/// configuration's (steps 3, 6 and 7). A throttle the profile reads as a lever (<see cref="Es1CustomFrame.LeverTarget"/>)
	/// goes in as the rate that takes <see cref="PlayerThrottle"/> to the lever's position
	/// (<see cref="Sim.Es1Locomotion.LeverAxis"/>), so the simulation and a recording take a rate, as retail's do.
	/// </summary>
	public Func<Es1CustomFrame?>? Custom { get; set; }

	/// <summary>
	/// <b>This engine's own:</b> the player's machine's throttle <c>+0x226</c>, which a Custom profile's lever is moved
	/// toward (<see cref="Custom"/>); with none a lever moves nothing.
	/// </summary>
	public Func<short>? PlayerThrottle { get; set; }

	/// <summary>The preferences the build reads, <c>data\prefs.cfg</c>'s options.</summary>
	public Es1Preferences Preferences => _preferences;

	/// <summary>The tape <c>-p</c> or <c>-D</c> opened, whose frames step 2 reads; none with no tape.</summary>
	public Es1TapePlayback? Tape { get; init; }

	/// <summary><c>TapePlayback</c> (<c>000b5b76</c>): a tape is playing, until step 2 ends it.</summary>
	public bool TapePlayback => Tape?.Playing == true;

	/// <summary>The tape <c>-r</c> started, to which step 8 appends every frame; none with no recording.</summary>
	public Es1TapeRecording? Recording { get; init; }

	/// <summary><c>TapeRecording</c> (<c>000b5b78</c>): <c>-r</c>'s tape was started; never cleared.</summary>
	public bool TapeRecording => Recording != null;

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

		// 2. The tape's frame, which fills the block and the source buttons; a tape that ends clears TapePlayback, and the
		// steps below then build this frame live.
		byte[]? tapeSources = PlayFrame(live, block);

		// 3. Live, the keyboard's axes; the source buttons are the live block's +0x1d. In playback the keyboard's pairs
		// are not built, and what steps 5 and 6 do to them only the live step 7 reads. The faster keyboard aiming tweak
		// takes the first pair as aiming when step 6 will move it onto the second; like the slow keys, it goes by the mode
		// at entry.
		bool playing = TapePlayback;
		bool camera = Controls.InputDrivesCamera;
		bool missileFlown = Controls.MissileFlown;
		Es1KeyboardAxesBlock? keys = playing ? null : _keyboard.BuildKeyboardAxes((modeAtEntry == 1 && !camera) || missileFlown,
			TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.FasterKeyboardAiming),
			firstPairAims: modeAtEntry == 1 && !camera && !missileFlown);
		int[] kb = keys is { } built ? [built.FirstX, built.FirstY, built.SecondX, built.SecondY] : new int[4];

		// A Custom profile's stick is steering, throttle, twist and pitch in that order, and its source buttons the trigger
		// alone.
		var custom = playing ? null : Custom?.Invoke();
		int[] stick = custom is { } read ? [read.Steering, read.Throttle, read.Twist, read.Pitch] : live.StickAxes;
		byte[] sourceButtons = playing ? tapeSources!
			: custom is { } held ? [held.Fire ? (byte)1 : (byte)0, 0, 0, 0]
			: live.StickButtons;

		// 4. The frame's mouse events on the cockpit's widgets, which step 8 records; none with no cockpit.
		var mouseEvents = CockpitMouse?.ProcessQueue() ?? [];
		if (sourceButtons[1] == 0) {
			_toggleHeld = false;
		} else if (modeAtEntry != 0 && !_toggleHeld) {
			mode = Controls.ToggleStickMode();
			_toggleHeld = true;
		}

		// 5. The missile arm.
		var latches = Controls.Latches;
		if (missileFlown) {
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

		// 6. The keyboard's pairs against the stick mode, the camera and a flown missile.
		if (!camera && !missileFlown) {
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
			if (missileFlown && !_keyjoy.MissileReversed) {
				kb[1] = -kb[1];
			}

			// The stick's X and Y steer the camera or the missile; a Custom profile's turret twist and pitch do.
			int from = custom != null ? 2 : 0;
			for (int axis = 0; axis < 2; axis++) {
				if (stick[from + axis] != 0) {
					kb[axis] = stick[from + axis];
					stick[from + axis] = 0;
				}
			}

			kb[2] = 0;
			kb[3] = 0;
			Array.Clear(stick);
		}

		if (_keyjoy.TiltReversed) {
			kb[3] = -kb[3];
		}

		bool throttleKeys = _preferences.ThrottleKeys && !missileFlown;

		// 7. Live, the configuration's sources, the latches, then the pointer, mouse, buttons 4-7 and command word. In
		// playback the pointer and the mouse bytes stay 0.
		if (keys is { } live7) {
			byte[] keyButtons = [live7.Button(0), live7.Button(1), live7.Button(2), live7.Button(3)];
			// A lever waits while the camera has the controls or a missile is flown, as step 6 zeroes a stick's throttle.
			int? lever = camera || missileFlown || PlayerThrottle == null ? null : custom?.LeverTarget;
			// An end-to-end lever runs the way ThrottleLeverMode says, reverse under Ctrl+1's −1; a bipolar lever's own sign
			// is its direction, which the mode's range then cuts.
			short leverMode = Controls.ThrottleLeverMode;
			short? leverAxis = lever is { } target
				? Sim.Es1Locomotion.LeverAxis(PlayerThrottle!(), leverMode < 0 && !custom!.BipolarLever ? -target : target, leverMode)
				: null;
			var (axes, buttons) = custom != null
				? ChooseCustom(kb, stick, keyButtons, sourceButtons[0], throttleKeys, leverAxis)
				: Es1StickConfigurations.Choose(_preferences.StickConfiguration, mode, _preferences.ThirdAxis, throttleKeys,
					new Es1InputSources(kb, stick, keyButtons, live.StickButtons[..4]));
			latches.Apply(buttons);
			Array.Copy(axes, block.Axes, 4);
			Array.Copy(buttons, block.Buttons, 4);
			block.PointerX = live.PointerX;
			block.PointerY = live.PointerY;
			Array.Copy(live.MouseButtons, block.MouseButtons, 3);
			Array.Copy(custom?.Views ?? live.StickButtons[4..8], 0, block.Buttons, 4, 4);
			block.CommandWord = live.CommandWord;
		}

		// 8. Recording: the block as it stands, before the camera's axes and Backturn, with the source buttons step 4 read
		// and the mouse events it handled, in playback as live.
		Recording?.Append(block, sourceButtons, mouseEvents);

		// 9. The camera's axes, and option 10's throttle step.
		if (!camera && !missileFlown) {
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

		PlayerInputBlock = block;
		return block;
	}

	/// <summary>
	/// <b>This engine's own:</b> step 7's choice for a Custom profile (<see cref="Custom"/>), in place of option 7's
	/// configuration: each of the profile's four functions on its own axis whatever the stick mode, the keyboard's pair
	/// where the function reads 0, as every configuration takes the keyboard's; option 10 keeps the throttle on the keys
	/// as it does there. Button 0 is the profile's trigger or <c>Space</c>, and buttons 1–3 the keyboard's. A lever's
	/// <paramref name="leverAxis"/> is the throttle axis alone, the keyboard's throttle keys dropped, as Earthsiege 2's
	/// lever zeroes its axis; option 10 turns the lever off as it turns a stick's throttle off.
	/// </summary>
	private static (short[] Axes, byte[] Buttons) ChooseCustom(int[] kb, int[] stick, byte[] keyButtons, byte trigger,
			bool throttleKeys, short? leverAxis) {
		static int Or(int stick, int keyboard) => stick != 0 ? stick : keyboard;
		short[] axes = [
			(short)Or(stick[Es1PlayerInputBlock.Steering], kb[0]),
			(short)(throttleKeys ? kb[1] : leverAxis ?? Or(stick[Es1PlayerInputBlock.Throttle], kb[1])),
			(short)Or(stick[Es1PlayerInputBlock.Twist], kb[2]),
			(short)Or(stick[Es1PlayerInputBlock.Pitch], kb[3]),
		];
		byte[] buttons = [trigger != 0 ? trigger : keyButtons[0], keyButtons[1], keyButtons[2], keyButtons[3]];
		return (axes, buttons);
	}

	/// <summary>
	/// Step 2, playback, while <see cref="TapePlayback"/> (docs/es1/simulation/input.md#recording-and-playback): on the
	/// first frame the cursor's position into <see cref="Es1TapePlayback.CursorPos"/> (<c>Cursor_GetPosition</c>, latch
	/// <c>000958be</c>); the frame read; under <c>DemoMode</c> a live command word sets <c>DemoAbort</c>. A short read, or
	/// the live command <see cref="Es1TapePlayback.StopCommand"/>, under <c>-p</c> as under <c>-D</c>, sets
	/// <c>DemoAbort</c>, closes the tape, sets <c>CockpitMouseLive</c> and zeroes the live word, and the caller builds the
	/// frame live. Otherwise each mouse event goes onto the cockpit's queue with its recorded time
	/// (<c>CockpitMouseQueue_Push</c>) and its point becomes <c>Input_CursorPos</c>, and the record fills the block's
	/// command word, axes, buttons 0–7 from bits 0–7 and <c>+0x29</c>. Returns the four source buttons, bits 8–11, while
	/// the tape plays, and none otherwise.
	/// </summary>
	private byte[]? PlayFrame(Es1LiveBlock live, Es1PlayerInputBlock block) {
		if (Tape is not { Playing: true } tape) {
			return null;
		}

		if (tape.CursorLatch) {
			// With no cockpit, which retail always has, the origin, an engine guard.
			tape.CursorPos = CockpitMouse?.Cursor.GetPosition() ?? (0, 0);
			tape.CursorLatch = false;
		}

		var frame = tape.Read();
		var axisKeys = _keyboard.AxisKeys;
		if (axisKeys.DemoMode && live.CommandWord != 0) {
			axisKeys.DemoAbort = true;
		}

		// Sim_HandleWindowKey (00059dec) gets the live word and answers 0 for every code.
		if (frame == null || live.CommandWord == Es1TapePlayback.StopCommand) {
			axisKeys.DemoAbort = true;
			tape.Close();
			if (CockpitMouse != null) {
				CockpitMouse.Live = true;
			}

			live.CommandWord = 0;
			return null;
		}

		foreach (var mouse in frame.MouseEvents) {
			CockpitMouse?.Push((uint)mouse.Time, new Es1MouseRecord(mouse.X, mouse.Y, mouse.Buttons));
			tape.CursorPos = (mouse.X, mouse.Y);
		}

		block.CommandWord = (ushort)frame.CommandWord;
		Array.Copy(frame.Axes, block.Axes, block.Axes.Length);
		int bits = (ushort)frame.Buttons;
		for (int i = 0; i < block.Buttons.Length; i++) {
			block.Buttons[i] = (byte)((bits >> i) & 1);
		}

		block.ReplayTickDelta = frame.TickDelta;
		return [(byte)((bits >> 8) & 1), (byte)((bits >> 9) & 1), (byte)((bits >> 10) & 1), (byte)((bits >> 11) & 1)];
	}

	/// <summary>
	/// <c>Prefs_ApplyMouseOption</c> (<c>0004ad54</c>), option 11's handler (docs/es1/simulation/preferences.md#the-option-array):
	/// when <paramref name="value"/> differs from <see cref="MouseOptionApplied"/>, it is kept there and, with the mouse
	/// device <c>Input_BuildSourceTable</c> always builds, the driver turned off for 0 (<c>Mouse_DisableDriver</c>) and
	/// on otherwise (<c>Mouse_EnableDriver</c>).
	/// </summary>
	public void ApplyMouseOption(byte value) {
		if (value == MouseOptionApplied) {
			return;
		}

		MouseOptionApplied = value;
		if (value == 0) {
			Mouse.DisableDriver();
		} else {
			Mouse.EnableDriver();
		}
	}
}
