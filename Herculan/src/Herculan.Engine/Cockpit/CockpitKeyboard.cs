using Herculan.Engine.Sim.Ai;
using Herculan.Engine.Audio;
using Herculan.Engine.Input;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Herculan.Engine.View;
using Silk.NET.Input;
using static Herculan.Engine.Input.KeyChords;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The cockpit displays' keys: the MFD's screens and buttons, FLASH COMM, the Heads-Down Display's two pages
/// and their own keyboards, and the side glances. These are Sim_DispatchCommand's and
/// CockpitWidgets_HandleCommand's cases, which no key reaches while a modal panel is up: the panel's own loop
/// hands each key to the panel alone (docs/retail/simulation/preferences.md#preferences-and-controls-dbsimexe). So
/// each block acts only while no modal is up, and an edged key still refreshes its latch under a panel, so a
/// key held as the panel closes does not fire.
/// </summary>
public sealed class CockpitKeyboard(CockpitDisplays displays, CockpitView view, CockpitCommands commands,
		MissionScene scene, GameAudio audio) {
	// FLASH COMM's seven order keys, in the order MfdFlashComm_HandleAltKey (00446c10) and MfdDisplay_KeyDispatch (004469c0) both switch on their
	// scancodes: which row each selects, and — for the two rows that carry two orders — which verb has to
	// be showing before [Alt] will transmit it. -1 means transmit whatever the row reads.
	private static readonly (Key Key, int Row, int Verb)[] FlashCommKeys = {
		(Key.A, 0, -1),   // ATTACK MY TARGET
		(Key.G, 1, -1),   // IGNORE MY TARGET
		(Key.H, 2, -1),   // HELP ME OUT!
		(Key.O, 3, -1),   // JOIN ON ME
		(Key.C, 4, (int)SquadCommand.ScanForHostiles),
		(Key.E, 4, (int)SquadCommand.Emcon),
		(Key.F, 5, -1),   // FIRE AT WILL / HOLD YOUR FIRE
	};

	private readonly KeyLatch[] _flashCommKeys = FlashCommKeys.Select(_ => new KeyLatch()).ToArray();
	private readonly KeyLatch _flashCommTransmit = new();
	private readonly KeyLatch _flashCommNextRow = new();
	private readonly KeyLatch _flashCommPreviousRow = new();
	private readonly KeyLatch _navMarker = new();

	// [D], [Alt+R] and [Alt+T], which press the MFD's SELECT, RANGE and TARGET buttons.
	private readonly KeyLatch _mfdSelect = new();
	private readonly KeyLatch _mfdRange = new();
	private readonly KeyLatch _mfdTarget = new();

	private readonly KeyLatch _glanceLeft = new();
	private readonly KeyLatch _glanceRight = new();

	// The command display's order hotkeys are the manual's own, and they are not a table in the code: each
	// STRINGS0 group 0 entry carries the index of its hotkey character within its own text, and the screen's
	// scancode dispatch maps that character to the order. Listed here in order-list order.
	private static readonly Key[] HddCommandKeys = { Key.D, Key.A, Key.F, Key.T, Key.G, Key.O, Key.C, Key.E };
	private static readonly Key[] HddPilotKeys = { Key.Number1, Key.Number2, Key.Number3 };
	private static readonly Key[] HddArrowKeys = { Key.Up, Key.Down, Key.Left, Key.Right };

	// The keypad's arrows, which are the same scancodes to the original (TapeKeys.KeysOf). With the axis keys
	// off on the display, they reach its key dispatch as the arrows do.
	private static readonly Key[] HddPadArrowKeys = { Key.Keypad8, Key.Keypad2, Key.Keypad4, Key.Keypad6 };

	private static bool HddArrowHeld(IKeyState controls, int arrow) =>
		controls.IsKeyPressed(HddArrowKeys[arrow]) || controls.IsKeyPressed(HddPadArrowKeys[arrow]);

	private readonly KeyLatch[] _hddOrderKeys = HddCommandKeys.Select(_ => new KeyLatch()).ToArray();
	private readonly KeyLatch[] _hddPilotKeys = HddPilotKeys.Select(_ => new KeyLatch()).ToArray();
	private readonly KeyLatch[] _hddArrowKeys = HddArrowKeys.Select(_ => new KeyLatch()).ToArray();
	private readonly KeyLatch[] _hddDamageArrowKeys = HddArrowKeys.Select(_ => new KeyLatch()).ToArray();
	private readonly KeyLatch _hddPreviousOrder = new();
	private readonly KeyLatch _hddNextOrder = new();
	private readonly KeyLatch _hddZoomIn = new();
	private readonly KeyLatch _hddZoomOut = new();
	private readonly KeyLatch _hddZoomInPad = new();
	private readonly KeyLatch _hddZoomOutPad = new();
	private readonly KeyLatch _hddRecentre = new();
	private readonly KeyLatch _hddTransmit = new();
	private readonly KeyLatch _hddCancel = new();
	private readonly KeyLatch _hddPick = new();
	private readonly KeyLatch _hddCycleUnit = new();

	/// <summary>Every display key, in the order the frame reads them.</summary>
	/// <param name="controls">The keyboard, or null while the debug panel has it.</param>
	/// <param name="modalPanelUp">Whether a modal panel holds the keys.</param>
	/// <param name="pilotInput">Whether the machine is being piloted, live or from a tape.</param>
	/// <param name="pointer">The pointer in window pixels, for [Enter] on the command display's map.</param>
	/// <param name="framebufferWidth">The window's framebuffer width, for the layout the pointer is read on.</param>
	/// <param name="framebufferHeight">And its height.</param>
	public void Read(IKeyState? controls, bool modalPanelUp, bool pilotInput, (float X, float Y) pointer,
			int framebufferWidth, int framebufferHeight) {
		// F1-F6 pick the MFD screen, the same keys and the same order as the original's own mode buttons
		// — button i of the display's F-key column dispatches SetMode(i), and this sets the same value.
		// Selecting one also pans back up to the cockpit, which is the manual's own rule for leaving the
		// Heads-Down Display ("select an MFD screen [F1]-[F6], press [Esc], or click the top of the
		// screen") and matches view command 1, the "up" half of the pair at 0042a3f4.
		if (controls != null && !modalPanelUp && !view.CockpitWidgetsOff && ReadMfdMode(controls) is { } requestedMfdMode) {
			displays.SetMfdMode(requestedMfdMode);
			view.RequestHeadsDown(headsDown: false);
		}

		if (controls != null && scene.World is { } flashCommWorld) {
			ReadFlashCommKeys(controls, flashCommWorld, modalPanelUp);
		}

		if (controls != null && pilotInput) {
			ReadMfdButtonKeys(controls, modalPanelUp);
		}

		// F7 (Command Display) and F8 (Damage Detail) are the two HDD functions, and per the manual
		// either one opens the display — so each both pans down and selects its own screen, which is
		// what the display's own two page buttons dispatch (HddDisplay_SetPage (0044a5e4) with the button's index).
		if (displays.Art?.HeadsDown != null && controls != null && !modalPanelUp && !view.CockpitWidgetsOff) {
			if (controls.IsKeyPressed(Key.F7)) {
				displays.Hud = displays.Hud with { Hdd = HddPage.CommandDisplay };
				view.RequestHeadsDown(headsDown: true);
			} else if (controls.IsKeyPressed(Key.F8)) {
				displays.Hud = displays.Hud with { Hdd = HddPage.DamageDetail };
				view.RequestHeadsDown(headsDown: true);
			}
		}

		// F9 and F10 are the manual's left and right windows, view commands 5 and 4. On the key's edge,
		// not its level: from a glance the opposite key is a return, so a held key would otherwise go on
		// to start the other glance the moment the strip got back.
		if (displays.Art != null && controls != null) {
			bool glanceLeft = _glanceLeft.Press(controls, Key.F9);
			bool glanceRight = _glanceRight.Press(controls, Key.F10);
			if (glanceLeft && !modalPanelUp) {
				view.CommandGlance(GlanceSide.Left);
			} else if (glanceRight && !modalPanelUp) {
				view.CommandGlance(GlanceSide.Right);
			}
		}

		ReadHddCommandKeys(controls, modalPanelUp, pointer, framebufferWidth, framebufferHeight);

		// The damage detail's three component categories, on the manual's own [S]/[I]/[W] bindings — the
		// same three the display's up/down arrow buttons step through. Only while that screen is actually
		// down: [S] and [W] are also two thirds of this host's camera movement, and the original has no
		// such clash because its own [S]/[I]/[W] only mean anything on this screen either.
		if (controls != null && !modalPanelUp && view.Pan.AtHeadsDown && displays.Hud.Hdd == HddPage.DamageDetail
			&& !CtrlHeld(controls) && !AltHeld(controls) && ReadHddDamageView(controls) is { } damageView) {
			displays.Hud = displays.Hud with { HddDamage = damageView };
		}

		// The four arrows press the display's own four arrow buttons, as HddDisplay_KeyDispatch (00449fcc)
		// does on either page: here, up and down step the category and left and right the herc. Once per
		// press, since each is one step.
		if (controls != null && displays.HddHasArrows && displays.Hud.Hdd == HddPage.DamageDetail) {
			bool modified = CtrlHeld(controls) || AltHeld(controls) || modalPanelUp;
			for (int i = 0; i < HddArrowKeys.Length; i++) {
				if (_hddDamageArrowKeys[i].Press(HddArrowHeld(controls, i)) && !modified) {
					commands.PressHddButtonByKey(HddLayout.Widget.ArrowUp + i);
				}
			}
		} else {
			foreach (var latch in _hddDamageArrowKeys) {
				latch.Reset();
			}
		}
	}

	// FLASH COMM's own keyboard, from the two dispatches that share it. The bare letters
	// (MfdDisplay_KeyDispatch (004469c0)'s tail) only move the cursor and only while the page is up; the same letters with
	// [Alt] (MfdFlashComm_HandleAltKey, 00446c10) select the row and transmit it in one go, from whichever screen is showing,
	// which is why they are the shortcuts the manual gives. Each letter is the one its order's own
	// attribute byte draws in red.
	//
	// Two of the six positions carry two orders, and the two keys that share them are not
	// interchangeable: [C] SCAN FOR HOSTILES and [E] EMCON both select row 4, but each only transmits
	// while that row is showing its own verb, so [Alt+C] on a row already reading EMCON selects and
	// says nothing. [F] has no such partner — row 5 transmits whichever of FIRE AT WILL and HOLD YOUR
	// FIRE it currently reads.
	private void ReadFlashCommKeys(IKeyState controls, SimWorld flashCommWorld, bool modalPanelUp) {
		var flashComm = displays.FlashComm;
		bool flashCommUp = displays.FlashCommHasKeyboard;

		// The widgets are off in the external view, which is where these letters would go through them.
		bool alt = AltHeld(controls) && !view.CockpitWidgetsOff;

		// Every key here is a bare or an [Alt] code. With [Ctrl] down the code is another one — [Ctrl+F],
		// and the developer keys' [Ctrl+Alt+D], [.] and [,] — so none of them lands here.
		bool ctrl = CtrlHeld(controls);

		bool Transmit() {
			bool accepted = flashComm.Transmit(flashCommWorld, flashCommWorld.PlayerMech?.Group);
			audio.Director?.Play(SoundId.ButtonClick);
			return accepted;
		}

		for (int i = 0; i < FlashCommKeys.Length; i++) {
			var (key, row, requiredVerb) = FlashCommKeys[i];
			if (!_flashCommKeys[i].Press(controls, key) || ctrl || modalPanelUp) {
				continue;
			}

			if (!alt) {
				if (flashCommUp) {
					flashComm.Select(row, flashCommIsUp: true);
				}

				continue;
			}

			// The [Alt] arm writes the screen's own row first and only then tests the verb, so a key
			// whose order is not the one showing still moves the cursor. MfdFlashComm_SelectRow (00447130) is what refuses
			// to move the display's row from another screen.
			flashComm.Select(row, flashCommIsUp: displays.Hud.Mfd == MfdMode.FlashComm);
			if (requiredVerb < 0 || flashComm.SelectedVerb == requiredVerb) {
				Transmit();
			}
		}

		if (flashCommUp) {
			// [X] presses XMIT, which is aux button 10 — the same press a click on the button makes.
			if (_flashCommTransmit.Press(controls, Key.X) && !modalPanelUp) {
				commands.PressMfdButtonByKey(MfdLayout.TransmitButton);
			}

			// [.] and [,] walk the list past any row the squad cannot take.
			if (_flashCommNextRow.Press(controls, Key.Period) && !ctrl && !modalPanelUp) {
				flashComm.StepRow(1);
			}
			if (_flashCommPreviousRow.Press(controls, Key.Comma) && !ctrl && !modalPanelUp) {
				flashComm.StepRow(-1);
			}
		}

		// [Alt+D] is command 0x220, which the cockpit view claims in its own handler before the panel
		// below it ever sees it: it drops a nav marker rather than transmitting DISENGAGE.
		if (alt && _navMarker.Press(controls, Key.D) && !ctrl && !modalPanelUp
				&& flashCommWorld.PlayerMech is { } marking) {
			displays.NavMarker.Drop(marking.Position);
		}
	}

	// The MFD's three button keys, from MfdDisplay_KeyDispatch (004469c0). Each presses its button only
	// on a screen that shows it, the same press a click makes. The dispatch returns before its switch
	// unless the MFD is on screen: with the Heads-Down Display down [D] is that display's order hotkey
	// instead, and neither the external view nor this engine's free camera, where [D] strafes, shows
	// the cockpit.
	private void ReadMfdButtonKeys(IKeyState controls, bool modalPanelUp) {
		bool selectKey = _mfdSelect.Press(controls, Key.D);
		bool rangeKey = _mfdRange.Press(controls, Key.R);
		bool targetKey = _mfdTarget.Press(controls, Key.T);
		bool mfdKeys = !modalPanelUp && !view.Pan.AtHeadsDown && !view.ExternalViewActive
			&& !CtrlHeld(controls);
		bool alt = AltHeld(controls);

		// [D] (0x20) is SELECT: the manual's "status of the other HERCs" on F1, a target step on F5.
		if (mfdKeys && !alt && selectKey) {
			commands.PressMfdButtonByKey(MfdLayout.SelectButton);
		}

		// [Alt+R] (0x213) is RANGE on the scanner. Anywhere else it steps the range all the same,
		// through MfdDisplay_CycleScannerRange rather than the button, so without a click.
		if (mfdKeys && alt && rangeKey) {
			if (!commands.PressMfdButtonByKey(MfdLayout.RangeButton)) {
				displays.CycleScannerRange();
			}
		}

		// [Alt+T] (0x214) is TARGET, which only the scanner shows.
		if (mfdKeys && alt && targetKey) {
			commands.PressMfdButtonByKey(MfdLayout.TargetButton);
		}
	}

	// The command display's own keyboard, from the manual's COMMAND DISPLAY table and the screen's
	// own key dispatch (0044cc40, which switches on scancodes and matches that table exactly). Gated
	// on the screen actually being down, the same way [S]/[I]/[W] are gated on the damage screen:
	// most of these letters are also cockpit or camera bindings in this host, and in the original
	// they mean nothing anywhere else either.
	//
	// Everything here fires on the key's own edge. The original's dispatch is a keydown handler.
	private void ReadHddCommandKeys(IKeyState? controls, bool modalPanelUp, (float X, float Y) pointer,
			int framebufferWidth, int framebufferHeight) {
		if (controls == null || displays.HddCommand is not { } command
			|| !view.Pan.AtHeadsDown || displays.Hud.Hdd != HddPage.CommandDisplay) {
			foreach (var latch in _hddOrderKeys.Concat(_hddPilotKeys).Concat(_hddArrowKeys)) {
				latch.Reset();
			}

			_hddPreviousOrder.Reset();
			_hddNextOrder.Reset();
			_hddZoomIn.Reset();
			_hddZoomOut.Reset();
			_hddZoomInPad.Reset();
			_hddZoomOutPad.Reset();
			_hddRecentre.Reset();
			_hddTransmit.Reset();
			_hddCancel.Reset();
			_hddPick.Reset();
			_hddCycleUnit.Reset();
			return;
		}

		// The screen's dispatch matches bare scancodes, so a key under [Ctrl] or [Alt] — a developer
		// key, most of the letters here — is not one of its own.
		// An order key is a click on its row, HddCommandScreen_SynthesizeListClick (0044d598) queueing one at the
		// row's corner, so the key for the order already armed leaves it armed, pick and all, as the click does.
		bool modified = CtrlHeld(controls) || AltHeld(controls) || modalPanelUp;
		for (int i = 0; i < HddCommandKeys.Length; i++) {
			if (_hddOrderKeys[i].Press(controls, HddCommandKeys[i]) && !modified && command.SelectedOrder != (HddOrder)i) {
				command.SelectOrder((HddOrder)i);
			}
		}

		// [,] and [.] walk the list without the pointer, and only once an order is already armed —
		// both functions return immediately otherwise.
		if (_hddPreviousOrder.Press(controls, Key.Comma) && !modified) {
			command.StepOrder(-1);
		}
		if (_hddNextOrder.Press(controls, Key.Period) && !modified) {
			command.StepOrder(1);
		}

		// [1]-[3] pick the pilot, left to right, which is what the number under each comm box says. The
		// display's key dispatch (HddDisplay_KeyDispatch, 00449fcc) presses the comm box's own widget for these, and the two
		// magnifiers' and the four arrows' for theirs, so all nine go through the button's press — which
		// is what holds them back while the sensor dropout has the display dark.
		for (int slot = 0; slot < HddPilotKeys.Length; slot++) {
			if (_hddPilotKeys[slot].Press(controls, HddPilotKeys[slot]) && !modified) {
				commands.PressHddButtonByKey(HddLayout.Widget.PilotBox0 + slot);
			}
		}

		// [+] and [-], the two magnifiers.
		// `|`, not `||`, so both of a pair's latches are refreshed every frame.
		if ((_hddZoomIn.Press(controls, Key.Equal) | _hddZoomInPad.Press(controls, Key.KeypadAdd)) && !modalPanelUp) {
			commands.PressHddButtonByKey(HddLayout.Widget.ZoomIn);
		}
		if ((_hddZoomOut.Press(controls, Key.Minus) | _hddZoomOutPad.Press(controls, Key.KeypadSubtract))
				&& !modalPanelUp) {
			commands.PressHddButtonByKey(HddLayout.Widget.ZoomOut);
		}

		// The arrows scroll the map, held rather than edged: the four pan functions are written to be
		// called repeatedly and clamp themselves against the mission box. Dark, a press is one press of
		// the arrow's button, held back with the rest. Keypad [5] drops the scroll and puts the map back
		// on the machine.
		//
		// Each of the original's presses flashes the arrow, and a held key presses it again at the
		// keyboard's repeat rate, faster than the flash runs out; a held arrow here is that run of presses,
		// so it flashes every frame it pans.
		bool dark = displays.Dropouts.HeadsDown.Dark;
		for (int i = 0; i < HddArrowKeys.Length; i++) {
			if (_hddArrowKeys[i].Press(HddArrowHeld(controls, i)) && dark && !modified) {
				commands.PressHddButtonByKey(HddLayout.Widget.ArrowUp + i);
			} else if (!dark && !modified && HddArrowHeld(controls, i)) {
				displays.FlashPress(CockpitWidgetId.Hdd(HddLayout.Widget.ArrowUp + i));
			}
		}

		if (!displays.Dropouts.HeadsDown.Dark && !modified) {
			command.View.Pan(
				(HddArrowHeld(controls, 3) ? 1 : 0) - (HddArrowHeld(controls, 2) ? 1 : 0),
				(HddArrowHeld(controls, 0) ? 1 : 0) - (HddArrowHeld(controls, 1) ? 1 : 0));
		}
		if (_hddRecentre.Press(controls, Key.Keypad5) && !modalPanelUp) {
			command.View.Recentre();
		}

		// [X] and [Backspace] press XMIT and CANCEL — HddCommandScreen_KeyDispatch (0044cc40)'s 0x2d and 0x0e.
		if (_hddTransmit.Press(controls, Key.X) && !modalPanelUp) {
			commands.PressHddButtonByKey(HddLayout.Widget.Transmit);
		}
		if (_hddCancel.Press(controls, Key.Backspace) && !modalPanelUp) {
			commands.PressHddButtonByKey(HddLayout.Widget.Cancel);
		}

		// [Enter] clicks the map for the armed order — on the unit last picked, or under the pointer.
		if (_hddPick.Press(controls, Key.Enter) && !modified) {
			commands.PickOnHddMapByEnter(pointer.X, pointer.Y, framebufferWidth, framebufferHeight);
		}

		// [Tab] steps the pick through the units the armed order can take.
		if (_hddCycleUnit.Press(controls, Key.Tab) && !modified) {
			commands.CycleHddUnitByTab();
		}
	}

	// The MFD screen the function keys are asking for, or null when none of them is down — returning null
	// rather than a default keeps the display on whatever screen it was already showing.
	private static MfdMode? ReadMfdMode(IKeyState keyboard) {
		Key[] keys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6 };
		for (int i = 0; i < keys.Length; i++) {
			if (keyboard.IsKeyPressed(keys[i])) {
				return (MfdMode)i;
			}
		}

		return null;
	}

	// The Heads-Down damage screen's component category, or null when none of its three keys is down —
	// same rule as ReadMfdMode: returning null leaves the screen on whatever it was already showing.
	private static HddDamageView? ReadHddDamageView(IKeyState keyboard) {
		if (keyboard.IsKeyPressed(Key.S)) {
			return HddDamageView.Structural;
		}

		if (keyboard.IsKeyPressed(Key.I)) {
			return HddDamageView.Internal;
		}

		return keyboard.IsKeyPressed(Key.W) ? HddDamageView.Weapons : null;
	}
}
