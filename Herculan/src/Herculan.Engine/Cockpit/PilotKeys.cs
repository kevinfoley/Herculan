using Herculan.Engine.Audio;
using Herculan.Engine.Input;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Silk.NET.Input;
using static Herculan.Engine.Input.KeyChords;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The keyboard's commands to the machine itself: all stop, the shield balance, the weapon panel, the radar,
/// Automatic Turret Tracking, and target selection. Each fires on its own key-down edge — the original
/// dispatches a command per keypress, so holding one does nothing.
/// </summary>
public sealed class PilotKeys(CockpitView view, CockpitDisplays displays, CockpitCommands commands, MissionScene scene,
		GameAudio audio) {
	private readonly KeyLatch _allStop = new();
	private readonly KeyLatch _shieldRear = new();
	private readonly KeyLatch _shieldFront = new();

	// The manual's three target keys, on the scancodes DBSIM's own command handlers switch on:
	// [Enter] (0x1c) cycles, ['] (0x28) takes the nearest, and [;] (0x27) clears.
	private readonly KeyLatch _cycleTarget = new();
	private readonly KeyLatch _nearestTarget = new();
	private readonly KeyLatch _clearTarget = new();

	// And [Tab] (0x0f), the fourth, which is the Targeting Pod's rather than the selection's: it steps
	// the component lock on whatever is already selected. See MechObject.CycleTargetComponent.
	private readonly KeyLatch _cycleComponent = new();

	// [R], the manual's radar mode: PASSIVE at power-up, ACTIVE once pressed. It is the input target
	// selection needs at any real range — see MechObject.ToggleScanner.
	private readonly KeyLatch _radar = new();

	// [T], Automatic Turret Tracking — the same toggle as the console's TRACK button, on the scancode
	// (0x14) Sim_DispatchCommand switches on. Turning it *off* also centres the turret there, which is
	// the one asymmetry in the pair.
	private readonly KeyLatch _autoTrack = new();

	// The weapon panel's row keys, in row order: [1] is row 1 and [0] is row 10, which is the same
	// wrap-around the row's own printed digit uses ((slot + 1) % 10).
	private static readonly Key[] WeaponRowKeys = {
		Key.Number1, Key.Number2, Key.Number3, Key.Number4, Key.Number5,
		Key.Number6, Key.Number7, Key.Number8, Key.Number9, Key.Number0,
	};

	private readonly KeyLatch[] _weaponRow = WeaponRowKeys.Select(_ => new KeyLatch()).ToArray();
	private readonly KeyLatch _cycleWeapon = new();
	private readonly KeyLatch _link = new();
	private readonly KeyLatch _chain = new();
	private readonly KeyLatch _powerUp = new();
	private readonly KeyLatch _powerDown = new();

	/// <summary>
	/// Every latch brought up to date and nothing acted on, which is what a modal wants: no machine is listening
	/// while one is up, and a key pressed to work the panel must not fire as it closes.
	/// </summary>
	public void Swallow(IKeyState controls) {
		_allStop.Press(controls.IsKeyPressed(Key.Keypad5));
		_shieldRear.Press(controls.IsKeyPressed(Key.LeftBracket));
		_shieldFront.Press(controls.IsKeyPressed(Key.RightBracket));
		_radar.Press(controls.IsKeyPressed(Key.R));
		_autoTrack.Press(!displays.HddCommandHasKeyboard && controls.IsKeyPressed(Key.T));
		_cycleTarget.Press(Unmodified(controls) && controls.IsKeyPressed(Key.Enter));
		_nearestTarget.Press(controls.IsKeyPressed(Key.Apostrophe));
		_clearTarget.Press(controls.IsKeyPressed(Key.Semicolon));
		_cycleComponent.Press(Unmodified(controls) && controls.IsKeyPressed(Key.Tab));
		ApplyWeaponKeys(controls, null);
	}

	/// <summary>The machine's keys, acted on.</summary>
	public void Apply(IKeyState controls, MechObject pilotMech) {
		// Keypad [5], all stop: zero the throttle and let the gauge follow the machine this frame
		// rather than putting the old setting straight back. On its own edge, so holding it does not
		// fight a throttle the player is trying to open again.
		if (_allStop.Press(controls.IsKeyPressed(Key.Keypad5))) {
			pilotMech.AllStop();
		}

		// [[] and []], the manual's shield-balance keys — rear and forward. Both fire on their own
		// edge: the original clears the gauge's flag byte after acting on it, so a held key nudges
		// once, not once a tick. Nothing is spent moving the balance; it changes where the next
		// recharge tick puts the charge it is already holding.
		//
		// They click, because in the original the key does not call the adjust at all: Mech_HandleCommand
		// (004157c8) hands scancodes 0x1a/0x1b to Widget_PressChild on the shield gauge, which fires the
		// facing's own press slot — Widget_ForwardClickToOwner, which calls slot +8 of its second vtable
		// (0049ca01), and that slot is Widget_ClickSound: catalog id 0x11. Pressing the widget is also
		// what makes the two input routes agree by construction.
		if (_shieldRear.Press(controls.IsKeyPressed(Key.LeftBracket))) {
			pilotMech.Shields.AdjustBalance(towardFront: false);
			audio.Director?.Play(SoundId.ButtonClick);
		}
		if (_shieldFront.Press(controls.IsKeyPressed(Key.RightBracket))) {
			pilotMech.Shields.AdjustBalance(towardFront: true);
			audio.Director?.Play(SoundId.ButtonClick);
		}

		ApplyWeaponKeys(controls, pilotMech.Weapons);

		// [R] switches the radar between PASSIVE and ACTIVE. Worth knowing before wondering why
		// nothing can be targeted: passive, this machine only ever knows about what it can see inside
		// visual range, and in the original what makes a distant enemy targetable is usually that
		// *enemy's* radar being on — which is AI behaviour the engine does not have yet. [Alt+R] is
		// another code, 0x213, the MFD's range key, and no handler gives it the radar.
		if (_radar.Press(controls.IsKeyPressed(Key.R)) && !view.CockpitWidgetsOff && !AltHeld(controls)) {
			pilotMech.ToggleScanner(scene.World);
		}

		// [T] toggles ATT. The command display owns [T] as an order hotkey while it is down, so the
		// two are split the same way the arrows and [Backspace] are, and for the same reason. [Alt+T]
		// is 0x214, the MFD's TARGET key.
		if (_autoTrack.Press(!displays.HddCommandHasKeyboard && controls.IsKeyPressed(Key.T)) && Unmodified(controls)) {
			// Sim_DispatchCommand's 0x14 case toggles the TRACK widget and, if that turned it off,
			// latches the centring mode — so [T] off brings the turret home rather than leaving it
			// wherever the tracker had it. Backspace's own case is the mirror image.
			if (!pilotMech.ToggleAutoTrack(scene.World)) {
				pilotMech.LatchCenterTorso();
			}
		}

		// Target selection. It is the cockpit's, not the machine's, so it is driven from here and
		// copied onto the machine each frame — see TargetSelection. [Enter] and [;] are the widgets'
		// cases and go dead with them in the external view, where [Enter] is the view's own; ['] is the
		// dispatcher's and keeps working.
		if (scene.Targeting is { } targeting) {
			bool widgetsOff = view.CockpitWidgetsOff;
			bool cycleTarget = _cycleTarget.Press(Unmodified(controls) && controls.IsKeyPressed(Key.Enter));
			bool nearestTarget = _nearestTarget.Press(controls.IsKeyPressed(Key.Apostrophe));
			bool clearTarget = _clearTarget.Press(controls.IsKeyPressed(Key.Semicolon));

			// On the scanner and TARGET STATUS, [Enter] presses the MFD's own TARGET or SELECT button, which
			// steps the selection. In the heads-down view it is the display's key instead — see
			// CockpitKeyboard.
			if (cycleTarget && !widgetsOff && !view.Pan.AtHeadsDown && !commands.PressMfdTargetButtonByEnter()) {
				targeting.Cycle();
			}
			if (nearestTarget) {
				targeting.SelectNearest();
			}
			if (clearTarget && !widgetsOff) {
				targeting.Clear();
			}
		}

		// [Tab] steps the Targeting Pod's component lock. CockpitWidgets_HandleCommand hands scancode
		// 0x0f to the pod only when the view is not the heads-down one; while the display is down the
		// same case goes to its own key dispatch, where the command display's [Tab] steps its unit pick —
		// see CockpitKeyboard.
		bool cycleComponentKey = !view.Pan.AtHeadsDown && Unmodified(controls) && controls.IsKeyPressed(Key.Tab);
		if (_cycleComponent.Press(cycleComponentKey) && !view.CockpitWidgetsOff) {
			pilotMech.CycleTargetComponent();
		}
	}

	// The weapon panel's keyboard set, on the manual's own bindings. Every one of these reaches exactly
	// the same call the corresponding mouse action does — the original routes them together too, through
	// the cockpit's ten-gauge array (CockpitViewInstance+0x70) and the console button panel.
	//
	//   [1]..[0]        arm that row                       -> WeaponMounts_ToggleChainMember (004110ac)'s sibling, WeaponMounts_SelectByGauge (004106ac)
	//   [Alt]+[1]..[0]  add/remove that row from the chain -> WeaponMounts_ToggleChainMember
	//   [W] / [Alt]+[W] step the armed weapon forward/back -> WeaponMounts_StepSelection (0041074c)
	//   [L] / [`]       press the console's LINK / CHAIN   -> ConsoleButtons_HandleCommand (004421a0)
	//   [-] / [=]       lower/raise the armed weapon's power -> the armed mount's vtable +0x38
	//
	// All fire on their own key-down edge: they are toggles and steps, not held states. [Space] is the
	// exception and is not here — the trigger is a held state read straight off the device struct, so it
	// travels with the rest of the pilot's input in MechControls.
	//
	// A null mounts is the swallow: every latch is brought up to date and nothing acts.
	private void ApplyWeaponKeys(IKeyState keyboard, WeaponMounts? mounts) {
		bool alt = AltHeld(keyboard);

		// [Ctrl+Alt] and a number is 0x602-0x60a, the developer keys' step size, and [Alt+keypad +] is
		// 0x24e, their single tick. Neither is a weapon command.
		bool ctrl = CtrlHeld(keyboard);

		for (int slot = 0; slot < WeaponRowKeys.Length; slot++) {
			if (_weaponRow[slot].Press(keyboard, WeaponRowKeys[slot]) && !ctrl) {
				// [Alt] and a number is command 0x202-0x20b, which the weapon manager answers itself; the
				// bare number is 0x02-0x0b, which CockpitWidgets_HandleCommand answers by pressing the
				// row's own select gadget. That is why only the bare key can toggle a pod.
				if (alt) {
					mounts?.ToggleChain(slot);
				} else if (!view.CockpitWidgetsOff) {
					mounts?.PressRow(slot);
				}
			}
		}

		if (_cycleWeapon.Press(keyboard, Key.W)) {
			mounts?.CycleSelection(alt ? -1 : 1);
		}

		// [L] and [`] reach the console only through CockpitWidgets_HandleCommand, which offers keys to the
		// forward panels outside the heads-down view and to nothing with the widgets off; the weapon
		// manager answers neither code.
		bool consoleKeys = mounts != null && !view.CockpitWidgetsOff && !view.Pan.AtHeadsDown;
		if (_link.Press(keyboard, Key.L) && consoleKeys) {
			commands.PressConsoleButtonByKey(ConsoleButton.Link);
		}
		if (_chain.Press(keyboard, Key.GraveAccent) && consoleKeys) {
			commands.PressConsoleButtonByKey(ConsoleButton.Chain);
		}

		// [-] and [=], with the keypad's own pair alongside them, move the armed energy weapon's power
		// level. Also an edge: each press is one step of 0x50 out of 1200.
		bool powerUpKey = keyboard.IsKeyPressed(Key.Equal) || (keyboard.IsKeyPressed(Key.KeypadAdd) && !alt);
		bool powerDownKey = keyboard.IsKeyPressed(Key.Minus) || keyboard.IsKeyPressed(Key.KeypadSubtract);
		if (_powerUp.Press(powerUpKey)) {
			mounts?.AdjustPower(raise: true);
		}
		if (_powerDown.Press(powerDownKey)) {
			mounts?.AdjustPower(raise: false);
		}
	}
}
