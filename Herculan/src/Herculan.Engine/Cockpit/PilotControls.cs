using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Audio;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.View;
using Silk.NET.Input;
using static Herculan.Engine.Input.KeyChords;
using InputTape = HercWorks.Core.Data.File.Dbsim.InputTape;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The pilot's input to the machine: the keyboard's axis pairs and the stick combined into MechControls each
/// frame, the stick's buttons dispatched, and what a replaying tape holds in their place. Sim_PollPlayerInput
/// and Input_BuildPlayerDevice's share of the frame.
/// </summary>
public sealed class PilotControls {
	private readonly CockpitView _view;
	private readonly CockpitDisplays _displays;
	private readonly CockpitCommands _commands;
	private readonly MissionScene _scene;
	private readonly GameAudio _audio;
	private readonly SimulatorPreferences _preferences;
	private readonly TapePlayback _tape;
	private readonly TapeRecording _recording;
	private readonly DeveloperKeys _developerKeys;
	private readonly StagedStart _staging;
	private readonly PilotKeys _keys;

	private JoystickPilotInput _joystickInput = JoystickPilotInput.None;

	// CENTER LEGS has no latch of its own on the machine — MechObject reads it off the controls
	// record's rising edge, the way [\] reaches it — so a button press has to hold the flag up for
	// the one frame that record is built.
	private bool _joystickCenterBody;

	// Input_LatchButton(1, 1), the first button row — always FIRE — held masked until it is let go, which
	// also holds the axes still. Two things latch it: a flown round ending, and AlertPanel_Enter (00454630)
	// opening any modal panel. The joystick's own latches are JoystickBindings'; this one also masks
	// [Space], the row's key. DAT_0049ebe5 is the keyboard hold that goes with it — see
	// docs/retail/formats/joystick-input.md. Live input only: what a replay does with a latch is that doc's Open.
	private bool _fireRowLatched;
	private int _missileKeyboardHold;

	public PilotControls(SimulatorStart start, CockpitView view, CockpitDisplays displays, CockpitCommands commands,
			TapePlayback tape, TapeRecording recording, DeveloperKeys developerKeys, StagedStart staging) {
		_view = view;
		_displays = displays;
		_scene = start.Scene;
		_audio = start.Audio;
		_preferences = start.Preferences;
		_tape = tape;
		_recording = recording;
		_developerKeys = developerKeys;
		_staging = staging;
		_commands = commands;
		_keys = new PilotKeys(view, displays, commands, start.Scene, start.Audio);

		// The bindings themselves are the twelve bytes of prefs.cfg — nothing about them changes when the
		// hardware does, which is the whole point of the split; JoystickDeviceMap is what absorbs a modern
		// device's own shape.
		Bindings = new JoystickBindings {
			PilotingRazor = start.PilotingRazor,
			Keyjoy = start.DataDirectory is null
				? new Keyjoy()
				: Keyjoy.Load(Path.Combine(start.DataDirectory, Keyjoy.FileName)),
		};
	}

	/// <summary>
	/// The stick, once the window has an input context to enumerate it with. Its capabilities are what the
	/// CONTROLS panel greys its rows against, and the original re-reads them every time that panel goes up
	/// rather than at startup, so nothing read off it at load has to be the last word.
	/// </summary>
	public IJoystickSource? Joystick { get; set; }

	public JoystickBindings Bindings { get; }

	/// <summary>What the stick in use can do: the recording machine's during a replay, the attached one otherwise.</summary>
	public JoystickCapabilities StickCapabilities =>
		_tape.Playing ? _tape.Capabilities : Joystick?.Capabilities ?? JoystickCapabilities.None;

	// The JOYSTICK row on the turret pair, with a stick there to bind — when the camera-axis pointers,
	// and the first button row's hold, take the turret pair rather than the movement pair.
	private bool StickTurretPair => StickCapabilities.Present
		&& Bindings.Assignment(_preferences, 0) == JoystickAxisAssignment.Turret;

	/// <summary>
	/// The frame's pilot input: the machine's controls, the camera's axes, the round's steering, and the observer
	/// camera's keys — from the player, the tape, or nobody while a modal is up.
	/// </summary>
	public void Update(IKeyState? controls, bool modalPanelOpen, IKeyState? freeCameraKeys) {
		var pilotMech = _view.PilotMech;

		// A modal takes the player's input away entirely — the stick as well as the keyboard, and not just
		// the command keys the key handler already gates. Each of these panels runs a loop of its own in
		// the original (AlertPanel_Enter, poll the device, present) and that loop never calls Sim_MainTick,
		// so Sim_PollPlayerInput and its action switch do not run at all while one is up.
		// Nothing the player does on the stick reaches the machine, which is what makes pressing a button
		// on the CONTROLS panel safe: it picks that button's row and does not also fire what it is bound to.
		//
		// Every edge latch is refreshed rather than left alone, so a key or button pressed to work the
		// panel does not fire the moment the panel goes down.
		// A replay pilots whether or not the live player has gone to the free camera: the machine is the
		// tape's to drive, and the camera is only a way to watch it.
		bool pilotInput = _view.Piloting || _tape.Playing;
		if (pilotInput && pilotMech != null && controls != null && modalPanelOpen) {
			pilotMech.Controls = MechControls.Neutral;
			_view.TakeCameraAxes(MechControls.Neutral);
			_joystickInput = JoystickPilotInput.None;
			_joystickCenterBody = false;
			Bindings.Suspend(Joystick?.Read() ?? JoystickReading.Neutral);
			_fireRowLatched = true;
			_keys.Swallow(controls);
		} else if (pilotInput && pilotMech != null && controls != null) {
			// The stick, read once and used twice: its axes go into MechControls at the bottom of this
			// block and its button edges are dispatched here. Both come out of the same twelve bytes of
			// prefs.cfg, resolved by JoystickBindings — the panel edits those bytes live, so a rebinding
			// takes effect on the next tick with nothing to reload. A replay's stick is the tape's.
			var stickReading = _tape.Playing || Joystick is null ? JoystickReading.Neutral : Joystick.Read();
			_joystickInput = _tape.Playing
				? TapeJoystickInput()
				: Joystick is null
					? JoystickPilotInput.None
					: Bindings.Resolve(stickReading, Joystick.Capabilities, _preferences);
			_recording.AddStick(_joystickInput, _tape.Playing);

			_joystickCenterBody = false;
			foreach (var action in _joystickInput.Pressed) {
				ApplyJoystickAction(action, pilotMech);
			}

			// The hat under VIEWS. The original passes its four bytes straight to
			// CockpitView_PollViewDevice (00432b14), which queues view commands 1, 0, 5 and 4 — up, down,
			// and the left and right glances. Level-triggered like the original's, which the glance gate
			// makes safe: a held direction repeats a command the gate then ignores.
			if (_joystickInput.Views.HasFlag(JoystickHat.North)) {
				_view.RequestHeadsDown(headsDown: false);
			} else if (_joystickInput.Views.HasFlag(JoystickHat.South)) {
				_view.RequestHeadsDown(headsDown: true);
			} else if (_joystickInput.Views.HasFlag(JoystickHat.West)) {
				_view.CommandGlance(GlanceSide.Left);
			} else if (_joystickInput.Views.HasFlag(JoystickHat.East)) {
				_view.CommandGlance(GlanceSide.Right);
			}

			_keys.Apply(controls, pilotMech);

			bool commandHasKeys = _displays.HddCommandHasKeyboard;

			// A replay's axes are the tape's, which already hold whatever the keyboard contributed when it
			// was recorded, and they change only when a frame is taken.
			if (_tape.Playing) {
				if (_tape.Frame is { } tapeFrame && !_tape.FrameUnderPanel) {
					pilotMech.Controls = TapeControls(tapeFrame,
						centerTorso: !commandHasKeys && controls.IsKeyPressed(Key.Backspace),
						centerBody: _joystickCenterBody || controls.IsKeyPressed(Key.BackSlash));
					_view.TakeCameraAxes(pilotMech.Controls);
				}
			} else {
				PilotFromLiveInput(pilotMech, controls, _displays.HddHasArrows, commandHasKeys, stickReading);
			}
		} else {
			if (!_tape.Playing) {
				_scene.Camera.Input = FlyCameraInput(controls);
				if (pilotMech != null) {
					pilotMech.Controls = MechControls.Neutral;
				}

				_view.TakeCameraAxes(MechControls.Neutral);
			}

			// Hands off the stick, but keep swallowing whatever is held on it: a button pressed to dismiss
			// a panel must not also fire its action the moment the panel goes down.
			_joystickInput = JoystickPilotInput.None;
			Bindings.Suspend(Joystick?.Read() ?? JoystickReading.Neutral);
		}

		// What a round the player is flying reads out of the input block: the two axes its camera-axis
		// pointers address and the trigger. While the controls are on a camera, or on the round, they are
		// the steering and throttle axes; otherwise they follow the JOYSTICK row, as Input_BuildPlayerDevice
		// sets them at its tail. Built from the controls just made, before the machine is handed none of
		// them below.
		if (pilotInput && pilotMech != null) {
			var built = pilotMech.Controls;
			_scene.World.MissileSteer = !_view.ControlsOnCamera && StickTurretPair
				? new MissileSteerInput(built.TorsoTwist, built.TorsoPitch, built.Fire)
				: new MissileSteerInput(built.Turn, built.Throttle, built.Fire);
		} else {
			_scene.World.MissileSteer = default;
		}

		// The controls on the camera — the outside view's default, [Enter]'s swap and [Ctrl+T]'s hand-off —
		// or on an electro-optical round in flight, over whichever of the two above built them. The axes
		// went to the view's camera axes and the round instead; the two centring commands are dispatcher
		// cases and still reach the machine. In the original, Sim_PollPlayerInput's camera branch tests
		// InputDrivesCamera alone: under it the machine gets no steering, throttle or twist, skips
		// Mech_PlayerFireTick and Mech_ApplyThrottleInput ignores a lever, but it keeps the pitch axis
		// while a stick with a throttle answers, which a lever on the turret pair or the hat under HAT = 2
		// can be moving (docs/retail/formats/joystick-input.md, "While the camera has the controls"). That
		// gate is the live stick's even during a replay, the capability block being rebuilt from the device
		// on every call. A round flown with InputDrivesCamera clear takes the original's ordinary branch,
		// which this neutralises as well.
		if (pilotInput && pilotMech != null && _view.ControlsOnCamera) {
			var liveStick = Joystick?.Capabilities ?? JoystickCapabilities.None;
			bool keepsPitch = _view.ControlsDriveCamera && liveStick.Present && liveStick.HasThrottle;
			pilotMech.Controls = MechControls.Neutral with {
				TorsoPitch = keepsPitch ? pilotMech.Controls.TorsoPitch : (short)0,
				CenterTorso = pilotMech.Controls.CenterTorso,
				CenterBody = pilotMech.Controls.CenterBody,
			};
		}

		// The free camera during a replay, which the live keyboard flies while the tape pilots.
		if (_tape.Playing && !_view.Piloting) {
			_scene.Camera.Input = FlyCameraInput(freeCameraKeys);
		}
	}

	/// <summary>
	/// The machine's controls from one tape frame: its axes, with this install's keyjoy.cfg Backturn applied as
	/// the original applies it on playback, and its trigger.
	/// </summary>
	public MechControls TapeControls(InputTape.Frame frame, bool centerTorso, bool centerBody) {
		var axes = Bindings.Combine(
			new JoystickPilotInput(InputTapePlayer.AxesOf(frame), false, Array.Empty<JoystickAction>()),
			PilotAxes.Centred);

		return new MechControls(axes.Steer, axes.Throttle,
			ThrottleLever: Bindings.ThrottleLeverMode(StickCapabilities, _preferences),
			TorsoTwist: axes.TorsoTwist,
			TorsoPitch: axes.TorsoPitch,
			CenterTorso: centerTorso,
			CenterBody: centerBody,
			Fire: InputTapePlayer.TriggerOf(frame));
	}

	// Stick sign convention is the device's, not the game's: forward and left are negative. No
	// throttle lever, so the throttle's range spans both directions and holding [Down] takes the
	// machine through zero into reverse — see MechControls.ThrottleLever.
	//
	// [I]/[M]/[J]/[K] aim the turret and [Backspace] re-centres it, which is the manual's own
	// keyboard turret set. The turret's axes are rates, so holding a key sweeps it rather than
	// putting it somewhere; [Backspace] latches until either axis is touched again.
	//
	// [\] is the other half of that pair, Center Body: it walks the legs round under the turret
	// instead of bringing the turret back, taking the steering and the twist axis until they line
	// up. It latches on the keypress, and [Backspace] cancels it.
	// A held key is worth MechControls.KeyboardAxis, half a stick's travel — DBSIM's own keyboard
	// scale, and the difference between turning at the machine's rate and at twice it.
	//
	// Retail's own bindings, from the manual's keyboard table and its throttle section: left and right
	// arrows steer, up and down arrows open and close the throttle, and keypad [5] is all stop. The
	// manual says the numeric keypad with NUM LOCK off, which on a real keyboard is the same key as the
	// arrow cluster; both are accepted here since a host window has no NUM LOCK to read.
	//
	// While the Heads-Down Display is down the four arrows are its own instead of steering, which is
	// what the manual binds them to there: they scroll the command display's map and, on the damage
	// detail, step the herc and the category being inspected. The keypad keeps steering throughout,
	// so the machine is never left without a stick; the command display also keeps [Backspace]
	// cancelling a transmission rather than re-centring the turret. This is the one place the two
	// keyboards are separated rather than allowed to overlap, because working the display and
	// turning the machine with the same press is the one overlap that would fight the player.
	//
	// The stick is combined with all of that rather than replacing it: the original registers the
	// keyboard's two axis pairs in the same source table as the joystick's four axes and takes
	// whichever has moved, so a pilot can steer with one hand and nudge with the other. What the
	// stick reaches at all is the twelve binding bytes' business — see JoystickBindings.
	private void PilotFromLiveInput(MechObject mech, IKeyState keys, bool hddHasArrows, bool commandHasKeys,
			JoystickReading stickReading) {
		// The first button row's latch: a flown round's end and a modal panel ask for it, and letting go
		// of the row — [Space] and the stick's first button — drops it. While it holds, the trigger reads
		// released and the axes are held below.
		if (_scene.World.TakeFireRowLatch()) {
			_fireRowLatched = true;
		}

		if (_fireRowLatched && !keys.IsKeyPressed(Key.Space) && !stickReading.Button(0)) {
			_fireRowLatched = false;
		}

		bool fireRowIsTrigger = Bindings.Action(_preferences, 0) == JoystickAction.Fire;
		bool rowZeroLatched = _fireRowLatched
			|| (Bindings.ButtonLatched(0) && !fireRowIsTrigger);

		// Under the developer flag an arrow held with Ctrl or Alt is a move or turn key, and this engine
		// takes it off the steering and throttle axes. Retail keeps it on them — see KNOWN_ISSUES.md.
		bool arrowsAreCommands = _developerKeys.Enabled && (CtrlHeld(keys) || AltHeld(keys));
		var keyboardAxes = new PilotAxes(
			(short)((arrowsAreCommands ? 0
				: hddHasArrows
				? Axis(keys, Key.Keypad6, Key.Keypad4)
				: Axis(keys, Key.Right, Key.Left, Key.Keypad6, Key.Keypad4)) * MechControls.KeyboardAxis),
			(short)((arrowsAreCommands ? 0
				: hddHasArrows
				? Axis(keys, Key.Keypad2, Key.Keypad8)
				: Axis(keys, Key.Down, Key.Up, Key.Keypad2, Key.Keypad8)) * MechControls.KeyboardAxis),
			TurretAxis(Axis(keys, Key.K, Key.J), _staging.HeldTwist),
			TurretAxis(Axis(keys, Key.I, Key.M), _staging.HeldPitch));

		// DAT_0049ebe5: the keyboard's first pair goes dead from the moment the first button row is
		// latched while a round is being flown until that row is let go, so the arrows steering the
		// round do not walk the machine off once it has gone.
		if (_scene.World.MissileFlown) {
			_missileKeyboardHold = 1;
		}

		if (rowZeroLatched && _missileKeyboardHold == 1) {
			_missileKeyboardHold = 2;
		} else if (!rowZeroLatched) {
			_missileKeyboardHold = 0;
		}

		if (_missileKeyboardHold != 0) {
			keyboardAxes = keyboardAxes with { Steer = 0, Throttle = 0 };
		}

		// Flying the round, the keyboard's pitch is the other way up unless keyjoy.cfg's Missile says
		// Reverse. The stick's is left alone.
		if (_scene.World.MissileFlown && !Bindings.Keyjoy.ReverseMissile) {
			keyboardAxes = keyboardAxes with { Throttle = (short)-keyboardAxes.Throttle };
		}

		// While the controls drive the camera or the round the stick is re-pointed first, and what the
		// tape records is that — the device's own axes, which the camera and the round read.
		bool onCamera = _view.ControlsOnCamera;
		var recordedAxes = onCamera
			? Bindings.CombineForCamera(stickReading, StickCapabilities, _preferences, keyboardAxes)
			: Bindings.CombineBeforeBackturn(_joystickInput, keyboardAxes);

		// The first button row latched holds the pair the camera axes point at still until it is let
		// go — after the tape has the axes, as the original zeroes them after it records.
		var held = recordedAxes;
		if (!onCamera && rowZeroLatched) {
			held = StickTurretPair
				? held with { TorsoTwist = 0, TorsoPitch = 0 }
				: held with { Steer = 0, Throttle = 0 };
		}

		var axes = Bindings.ApplyBackturn(held);

		mech.Controls = new MechControls(
			axes.Steer,
			axes.Throttle,
			// Set when the stick has a lever and it is bound to THROTTLE rather than to the turret —
			// Input_SetThrottleLeverMode's own pair of conditions (00459d20). It is what closes the
			// throttle clamp to one side of zero, so a lever pilot cannot walk backwards through the
			// detent the way a keyboard one does.
			ThrottleLever: Bindings.ThrottleLeverMode(StickCapabilities, _preferences),
			TorsoTwist: axes.TorsoTwist,
			TorsoPitch: axes.TorsoPitch,
			CenterTorso: !commandHasKeys && keys.IsKeyPressed(Key.Backspace),
			CenterBody: _joystickCenterBody || keys.IsKeyPressed(Key.BackSlash),
			// [Space] is held, not pressed — see MechControls.Fire. Holding it keeps the armed weapon
			// firing as fast as its refire delay and its capacitor allow. So is the joystick trigger,
			// for the same reason and through the same byte.
			Fire: _staging.HeldFire || (_joystickInput.Fire && !(_fireRowLatched && fireRowIsTrigger))
				|| (keys.IsKeyPressed(Key.Space) && !_fireRowLatched));
		_view.TakeCameraAxes(mech.Controls);

		// A tape records the axes ahead of Backturn, which playback applies again.
		_recording.SetHeld(recordedAxes, mech.Controls.Fire, stickReading.Buttons);
	}

	/// <summary>
	/// One turret axis: the key pair, or whatever <c>--turret</c> is holding when no key is down. Never
	/// past full deflection, so holding a key during a <c>--turret</c> run cannot ask for more rate than
	/// a stick can.
	/// </summary>
	private static short TurretAxis(int keys, short held) =>
		keys != 0 ? (short)(keys * MechControls.KeyboardAxis) : held;

	// The frame's stick, in the shape a live one is resolved to. Its buttons are already past the
	// press-once latch, so a set bit is an action this frame; the first set bit claims the tick's one
	// action, as the first pressed button does live — see JoystickBindings.
	private JoystickPilotInput TapeJoystickInput() {
		if (_tape.Frame is not { } frame || _tape.FrameUnderPanel) {
			return JoystickPilotInput.None;
		}

		var pressed = Array.Empty<JoystickAction>();
		for (int i = 0; i < InputTapePlayer.RecordedButtonCount; i++) {
			if (!InputTapePlayer.ButtonOf(frame, i)) {
				continue;
			}

			var action = Bindings.Action(_preferences, i);
			if (action is not (JoystickAction.Off or JoystickAction.Fire)) {
				pressed = new[] { action };
			}

			break;
		}

		return new JoystickPilotInput(InputTapePlayer.AxesOf(frame), InputTapePlayer.TriggerOf(frame),
			pressed, InputTapePlayer.HatOf(frame));
	}

	// One joystick button's action — Sim_PollPlayerInput's own switch (00460764), nineteen cases for codes
	// 2-20, which it runs over SimOptions[ControlsOptionBase + 4 + button] for the one button that claims
	// the tick's slot.
	//
	// FIRE and OFF have no case, there or here; JoystickBindings keeps both out of Pressed while still
	// letting them claim the slot (docs/retail/formats/joystick-input.md#the-buttons).
	//
	// Every case reaches the same code a key or a click does, which is also true in the original — the
	// switch is almost entirely made of calls into the widget tree and the mech's own command handler
	// rather than of gameplay of its own.
	private void ApplyJoystickAction(JoystickAction action, MechObject mech) {
		switch (action) {
			case JoystickAction.Target:
				_scene.Targeting?.Cycle();
				break;

			case JoystickAction.TargetNearest:
				_scene.Targeting?.SelectNearest();
				break;

			case JoystickAction.CenterLegs:
				_joystickCenterBody = true;
				break;

			case JoystickAction.CenterTurret:
				mech.LatchCenterTorso();
				break;

			// The lever's sense, and only when there is a lever bound to the throttle, so on a stick without
			// one this button does nothing at all. The original tests the capability block's +4 and the
			// walker's THROTTLE row through a literal, even in a RAZOR (docs/retail/formats/joystick-input.md,
			// "The buttons"); this tests the current machine's row, which differs from it in a RAZOR whose
			// THROTTLE row is not the walker's.
			case JoystickAction.ChangeDirection
				when Bindings.ThrottleLeverMode(StickCapabilities, _preferences) != 0:
				Bindings.ThrottleLeverInverted = !Bindings.ThrottleLeverInverted;
				break;

			// Command 0x14, the same one [T] dispatches: toggling ATT off also latches the centring mode,
			// so the turret comes home rather than staying where the tracker left it.
			case JoystickAction.AttitudeToggle:
				if (!mech.ToggleAutoTrack(_scene.World)) {
					mech.LatchCenterTorso();
				}

				break;

			case JoystickAction.AllStop:
				mech.AllStop();
				break;

			// Mech commands 0x1a and 0x1b. They press the gauge's own facing widget rather than calling the
			// adjust, which is why they click — see the bracket keys.
			case JoystickAction.ShieldsFront:
			case JoystickAction.ShieldsRear:
				mech.Shields.AdjustBalance(towardFront: action == JoystickAction.ShieldsFront);
				_audio.Director?.Play(SoundId.ButtonClick);
				break;

			// The original's two branches both send a scancode to CockpitWidgets_HandleCommand, which ignores
			// everything while the widgets are off — so neither does anything from the external view.
			case JoystickAction.HddView when !_view.CockpitWidgetsOff:
				// A toggle, which is what the action's two branches were plainly meant to be. The original
				// tests the view manager's pointer rather than the view, so it can only ever leave the HDD
				// (docs/retail/formats/joystick-input.md, "HDD VIEW can only leave").
				_view.RequestHeadsDown(headsDown: !_view.Pan.HeadsDownRequested);
				break;

			case JoystickAction.CockpitView when !_view.CockpitWidgetsOff:
				_view.RequestHeadsDown(headsDown: false);
				break;

			// LINK WEAPON and NEXT CHAIN hand scancodes 0x26 and 0x29 to the console panel's own key slot, past the
			// widget tree's gates, so they press its LINK and CHAIN buttons from any view.
			case JoystickAction.LinkWeapon:
				_commands.PressConsoleButtonByKey(ConsoleButton.Link);
				break;

			// MfdDisplay_CycleMode (00446e14): step the MFD's mode, wrapping at six. Selecting a screen also pans back up,
			// which is the manual's own rule for leaving the heads-down display.
			case JoystickAction.MfdDisplays:
				_displays.SetMfdMode((MfdMode)(((int)_displays.Hud.Mfd + 1) % MfdLayout.ModeCount));
				_view.RequestHeadsDown(headsDown: false);
				break;

			// Weapon-manager command 0x202, which WeaponMounts_HandleCommand answers with
			// ToggleChainMember(0) — [Alt]+[1], row 1's fire-chain membership, and not a general toggle
			// despite the caption.
			case JoystickAction.WeaponToggle:
				mech.Weapons.ToggleChain(0);
				break;

			case JoystickAction.NextChain:
				_commands.PressConsoleButtonByKey(ConsoleButton.Chain);
				break;

			case JoystickAction.NextWeapon:
				mech.Weapons.CycleSelection(1);
				break;

			case JoystickAction.PreviousWeapon:
				mech.Weapons.CycleSelection(-1);
				break;

			// OUTSIDE VIEW is [V]'s step without its lock test, and CHASE VIEW the chase view's — see
			// ExternalViewChain.
			case JoystickAction.OutsideView:
				_view.Chain?.ToggleOutside(fromKeyboard: false);
				break;

			case JoystickAction.ChaseView:
				_view.Chain?.ToggleChase();
				break;
		}
	}
}
