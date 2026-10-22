using Herculan.Engine.Audio;
using Herculan.Engine.Input;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using static Herculan.Engine.Input.KeyChords;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The keyboard's commands to the machine itself: all stop, the shield balance, the weapon panel, the radar,
/// Automatic Turret Tracking, and target selection. Each fires on its key-down edge and on each of its
/// auto-repeats, because the original dispatches a command per key-down event, repeats included
/// (docs/retail/simulation/cockpit-input.md#how-a-keystroke-becomes-one-of-those-codes): a held key goes on
/// toggling or stepping at the keyboard's repeat rate.
/// </summary>
/// <param name="razorThrottleKeys">Whether keypad <c>-</c> and <c>+</c> are the RAZOR's throttle keys this frame —
/// <see cref="PilotControls.RazorThrottleKeys"/>.</param>
/// <param name="keyMap">Where each key is read from.</param>
public sealed class PilotKeys(CockpitView view, CockpitDisplays displays, CockpitCommands commands, MissionScene scene,
		GameAudio audio, Func<bool> razorThrottleKeys, KeyMap keyMap) {
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
	// wrap-around the row's own printed digit uses ((slot + 1) % 10). Each row's [Alt] chain toggle keeps a
	// latch of its own on the same key, as [Alt+W] does beside [W].
	private const int WeaponRowCount = 10;

	private readonly KeyLatch[] _weaponRow = Enumerable.Range(0, WeaponRowCount).Select(_ => new KeyLatch()).ToArray();
	private readonly KeyLatch[] _chainRow = Enumerable.Range(0, WeaponRowCount).Select(_ => new KeyLatch()).ToArray();
	private readonly KeyLatch _nextWeapon = new();
	private readonly KeyLatch _previousWeapon = new();
	private readonly KeyLatch _link = new();
	private readonly KeyLatch _chain = new();
	private readonly KeyLatch _powerUp = new();
	private readonly KeyLatch _powerDown = new();
	private readonly KeyLatch _powerUpPad = new();
	private readonly KeyLatch _powerDownPad = new();

	/// <summary>
	/// Every latch brought up to date and nothing acted on, which is what a modal wants: no machine is listening
	/// while one is up, and a key pressed to work the panel must not fire as it closes. Its next auto-repeat
	/// does, as each repeat reaches the original's dispatcher once the panel is down.
	/// </summary>
	public void Swallow(IKeyState controls) {
		_allStop.Press(controls.IsKeyPressed(keyMap.KeyOf(KeyAction.AllStop)));
		_shieldRear.Press(controls.IsKeyPressed(keyMap.KeyOf(KeyAction.ShieldsRear)));
		_shieldFront.Press(controls.IsKeyPressed(keyMap.KeyOf(KeyAction.ShieldsFront)));
		_radar.Press(controls.IsKeyPressed(keyMap.KeyOf(KeyAction.RadarMode)));
		_autoTrack.Press(!displays.HddCommandHasKeyboard
			&& controls.IsKeyPressed(keyMap.KeyOf(KeyAction.AutoTurretTracking)));
		_cycleTarget.Press(Unmodified(controls) && controls.IsKeyPressed(keyMap.KeyOf(KeyAction.NextTarget)));
		_nearestTarget.Press(controls.IsKeyPressed(keyMap.KeyOf(KeyAction.NearestTarget)));
		_clearTarget.Press(controls.IsKeyPressed(keyMap.KeyOf(KeyAction.ClearTarget)));
		_cycleComponent.Press(Unmodified(controls) && controls.IsKeyPressed(keyMap.KeyOf(KeyAction.NextTargetComponent)));
		ApplyWeaponKeys(controls, null);
	}

	/// <summary>The machine's keys, acted on.</summary>
	public void Apply(IKeyState controls, MechObject pilotMech) {
		// Keypad [5], all stop: zero the throttle and let the gauge follow the machine this frame
		// rather than putting the old setting straight back. Held, it stops again on each repeat until
		// another key is pressed, since only the key pressed last repeats.
		if (_allStop.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.AllStop))) {
			pilotMech.AllStop();
		}

		// [[] and []], the manual's shield-balance keys — rear and forward. The original clears the
		// gauge's flag byte after acting on it, so a press or a repeat nudges once, not once a tick.
		// Nothing is spent moving the balance; it changes where the next recharge tick puts the charge it
		// is already holding.
		//
		// They click, because in the original the key does not call the adjust at all: Mech_HandleCommand
		// (004157c8) hands scancodes 0x1a/0x1b to Widget_PressChild on the shield gauge, which fires the
		// facing's own press slot — Widget_ForwardClickToOwner, which calls slot +8 of its second vtable
		// (0049ca01), and that slot is Widget_ClickSound: catalog id 0x11. Pressing the widget is also
		// what makes the two input routes agree by construction.
		if (_shieldRear.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.ShieldsRear))) {
			pilotMech.Shields.AdjustBalance(towardFront: false);
			audio.Director?.Play(SoundId.ButtonClick);
		}
		if (_shieldFront.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.ShieldsFront))) {
			pilotMech.Shields.AdjustBalance(towardFront: true);
			audio.Director?.Play(SoundId.ButtonClick);
		}

		ApplyWeaponKeys(controls, pilotMech.Weapons);

		// [R] switches the radar between PASSIVE and ACTIVE. Worth knowing before wondering why
		// nothing can be targeted: passive, this machine only ever knows about what it can see inside
		// visual range, and in the original what makes a distant enemy targetable is usually that
		// *enemy's* radar being on — which is AI behaviour the engine does not have yet. [Alt+R] is
		// another code, 0x213, the MFD's range key, and no handler gives it the radar.
		if (_radar.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.RadarMode)) && !view.CockpitWidgetsOff
				&& !AltHeld(controls)) {
			pilotMech.ToggleScanner(scene.World);
		}

		// [T] toggles ATT. The command display owns [T] as an order hotkey while it is down, so the
		// two are split the same way the arrows and [Backspace] are, and for the same reason. [Alt+T]
		// is 0x214, the MFD's TARGET key.
		if (_autoTrack.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.AutoTurretTracking), !displays.HddCommandHasKeyboard)
				&& Unmodified(controls)) {
			// Sim_DispatchCommand's 0x14 case toggles the TRACK widget and, if that turned it off,
			// latches the centring mode — so [T] off brings the turret home rather than leaving it
			// wherever the tracker had it. Backspace's own case is the mirror image. A refused toggle
			// skips the tail.
			if (pilotMech.ToggleAutoTrack(scene.World) && !pilotMech.Weapons.AutoTrack) {
				pilotMech.LatchCenterTorso();
			}
		}

		// Target selection. It is the cockpit's, not the machine's, so it is driven from here and
		// copied onto the machine each frame — see TargetSelection. [Enter] and [;] are the widgets'
		// cases and go dead with them in the external view, where [Enter] is the view's own; ['] is the
		// dispatcher's and keeps working.
		if (scene.Targeting is { } targeting) {
			bool widgetsOff = view.CockpitWidgetsOff;
			bool cycleTarget = _cycleTarget.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.NextTarget),
				Unmodified(controls));
			bool nearestTarget = _nearestTarget.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.NearestTarget));
			bool clearTarget = _clearTarget.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.ClearTarget));

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
		bool cycleComponentKey = _cycleComponent.PressOrRepeat(controls, keyMap.KeyOf(KeyAction.NextTargetComponent),
			!view.Pan.AtHeadsDown && Unmodified(controls));
		if (cycleComponentKey && !view.CockpitWidgetsOff) {
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
	// All fire on each key-down event, auto-repeats included. [Space] is the exception and is not here — the
	// trigger is a held state read straight off the device struct, so it travels with the rest of the pilot's
	// input in MechControls.
	//
	// A null mounts is the swallow: every latch is brought up to date and nothing acts.
	private void ApplyWeaponKeys(IKeyState keyboard, WeaponMounts? mounts) {
		bool alt = AltHeld(keyboard);

		// [Ctrl+Alt] and a number is 0x602-0x60a, the developer keys' step size, and [Alt+keypad +] is
		// 0x24e, their single tick. Neither is a weapon command.
		bool ctrl = CtrlHeld(keyboard);

		// In the heads-down view CockpitWidgets_HandleCommand offers a code to the display's key dispatch alone, and
		// that takes [W], [-], [=] and the keypad's pair, so Sim_DispatchCommand never passes them on to the weapon
		// manager; nor does the widget tree arm a row for a bare number there. [Alt] and a number or [W] is another
		// code, which the display does not take. See CockpitKeyboard.ReadHddDisplayKeys.
		bool headsDown = view.Pan.AtHeadsDown;

		for (int slot = 0; slot < WeaponRowCount; slot++) {
			// Both latches are read every frame, and [Alt] decides which of the two keys acts.
			bool rowKey = _weaponRow[slot].PressOrRepeat(keyboard, keyMap.KeyOf(KeyAction.WeaponRow1 + slot));
			bool chainKey = _chainRow[slot].PressOrRepeat(keyboard, keyMap.KeyOf(KeyAction.ToggleChainRow1 + slot));
			if ((alt ? chainKey : rowKey) && !ctrl) {
				// [Alt] and a number is command 0x202-0x20b, which the weapon manager answers itself; the
				// bare number is 0x02-0x0b, which CockpitWidgets_HandleCommand answers by pressing the
				// row's own select gadget. That is why only the bare key can toggle a pod.
				//
				// The press clicks whenever the slot has a gauge, whatever it goes on to do: Widget_PressChild
				// calls the gadget's OnClick (ChainedWeaponSelectGadget_OnClick, 00442920, or a pod row's
				// WeaponSelectGadget_OnClick, 00442458), which calls its mixin's sound slot, Widget_ClickSound,
				// after the gauge's handler.
				if (alt) {
					mounts?.ToggleChain(slot);
				} else if (!view.CockpitWidgetsOff && !headsDown && mounts?.BySlot(slot) != null) {
					mounts.PressRow(slot);
					audio.Director?.Play(SoundId.ButtonClick);
				}
			}
		}

		bool nextWeapon = _nextWeapon.PressOrRepeat(keyboard, keyMap.KeyOf(KeyAction.NextWeapon));
		bool previousWeapon = _previousWeapon.PressOrRepeat(keyboard, keyMap.KeyOf(KeyAction.PreviousWeapon));
		if ((alt ? previousWeapon : nextWeapon) && (alt || !headsDown)) {
			mounts?.CycleSelection(alt ? -1 : 1);
		}

		// [L] and [`] reach the console only through CockpitWidgets_HandleCommand, which offers keys to the
		// forward panels outside the heads-down view and to nothing with the widgets off; the weapon
		// manager answers neither code.
		bool consoleKeys = mounts != null && !view.CockpitWidgetsOff && !view.Pan.AtHeadsDown;
		if (_link.PressOrRepeat(keyboard, keyMap.KeyOf(KeyAction.LinkWeapon)) && consoleKeys) {
			commands.PressConsoleButtonByKey(ConsoleButton.Link);
		}
		if (_chain.PressOrRepeat(keyboard, keyMap.KeyOf(KeyAction.NextChain)) && consoleKeys) {
			commands.PressConsoleButtonByKey(ConsoleButton.Chain);
		}

		// [-] and [=], with the keypad's own pair alongside them, move the armed energy weapon's power
		// level, one step of 0x50 out of 1200 per key-down event. The keypad's pair is the RAZOR's
		// throttle instead, which Input_KeyjoyAxisKey (0045a308) takes before the command queue sees it.
		// `|`, not `||`, so both of a pair's latches are refreshed every frame. The keypad's key is each action's
		// second chord.
		bool padPower = !razorThrottleKeys();
		var powerUpKeys = keyMap.Chords(KeyAction.PowerUp);
		var powerDownKeys = keyMap.Chords(KeyAction.PowerDown);
		bool powerUp = _powerUp.PressOrRepeat(keyboard, powerUpKeys[0].Key)
			| (_powerUpPad.PressOrRepeat(keyboard, powerUpKeys[1].Key) && !alt && padPower);
		bool powerDown = _powerDown.PressOrRepeat(keyboard, powerDownKeys[0].Key)
			| (_powerDownPad.PressOrRepeat(keyboard, powerDownKeys[1].Key) && padPower);
		if (powerUp && !headsDown) {
			mounts?.AdjustPower(raise: true);
		}
		if (powerDown && !headsDown) {
			mounts?.AdjustPower(raise: false);
		}
	}
}
