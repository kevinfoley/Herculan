using Silk.NET.Input;

namespace Herculan.Engine.Input;

/// <summary>
/// ES2's keys, as the handlers read them: docs/retail/key-bindings.md, less its developer keys (DeveloperKeys reads
/// those itself), and the keyboard keys of docs/herculan/herculan-key-bindings.md. The arrows and the keypad's are the
/// same scancodes to retail (<see cref="TapeKeys.KeysOf"/>), so each such action has both, the one the manual names
/// first.
/// </summary>
internal static class RetailKeyMap {
	public static KeyMap Build() {
		var bindings = new List<KeyBinding>();
		var section = KeySection.Driving;
		void Bind(KeyAction action, params KeyChord[] chords) =>
			bindings.Add(new KeyBinding(action, section, [.. chords]));

		Bind(KeyAction.ThrottleForward, Key.Up, Key.Keypad8);
		Bind(KeyAction.ThrottleBack, Key.Down, Key.Keypad2);
		Bind(KeyAction.SteerLeft, Key.Left, Key.Keypad4);
		Bind(KeyAction.SteerRight, Key.Right, Key.Keypad6);
		Bind(KeyAction.AllStop, Key.Keypad5);
		Bind(KeyAction.TurretLeft, Key.J);
		Bind(KeyAction.TurretRight, Key.K);
		Bind(KeyAction.TurretUp, Key.I);
		Bind(KeyAction.TurretDown, Key.M);
		Bind(KeyAction.CenterTurret, Key.Backspace);
		Bind(KeyAction.CenterLegs, Key.BackSlash);
		Bind(KeyAction.RazorThrottleUp, Key.KeypadAdd);
		Bind(KeyAction.RazorThrottleDown, Key.KeypadSubtract);
		Bind(KeyAction.DiagonalForwardLeft, Key.Keypad7, Key.Home);
		Bind(KeyAction.DiagonalForwardRight, Key.Keypad9, Key.PageUp);
		Bind(KeyAction.DiagonalBackLeft, Key.Keypad1, Key.End);
		Bind(KeyAction.DiagonalBackRight, Key.Keypad3, Key.PageDown);

		// [1] is row 1 and [0] row 10.
		section = KeySection.Weapons;
		Bind(KeyAction.Fire, Key.Space);
		for (int row = 0; row < 10; row++) {
			Bind(KeyAction.WeaponRow1 + row, NumberKey(row));
		}
		for (int row = 0; row < 10; row++) {
			Bind(KeyAction.ToggleChainRow1 + row, KeyChord.WithAlt(NumberKey(row)));
		}
		Bind(KeyAction.NextWeapon, Key.W);
		Bind(KeyAction.PreviousWeapon, KeyChord.WithAlt(Key.W));
		Bind(KeyAction.LinkWeapon, Key.L);
		Bind(KeyAction.NextChain, Key.GraveAccent);
		Bind(KeyAction.PowerUp, Key.Equal, Key.KeypadAdd);
		Bind(KeyAction.PowerDown, Key.Minus, Key.KeypadSubtract);

		section = KeySection.TargetingAndShields;
		Bind(KeyAction.NextTarget, Key.Enter);
		Bind(KeyAction.NearestTarget, Key.Apostrophe);
		Bind(KeyAction.ClearTarget, Key.Semicolon);
		Bind(KeyAction.NextTargetComponent, Key.Tab);
		Bind(KeyAction.RadarMode, Key.R);
		Bind(KeyAction.RadarRange, KeyChord.WithAlt(Key.R));
		Bind(KeyAction.ScannerNextTarget, KeyChord.WithAlt(Key.T));
		Bind(KeyAction.AutoTurretTracking, Key.T);
		Bind(KeyAction.ShieldsRear, Key.LeftBracket);
		Bind(KeyAction.ShieldsFront, Key.RightBracket);

		section = KeySection.DisplaysAndViews;
		Bind(KeyAction.MfdStatus, Key.F1);
		Bind(KeyAction.MfdFlashComm, Key.F2);
		Bind(KeyAction.MfdNavMap, Key.F3);
		Bind(KeyAction.MfdScanner, Key.F4);
		Bind(KeyAction.MfdTarget, Key.F5);
		Bind(KeyAction.MfdMissileCam, Key.F6);
		Bind(KeyAction.CommandDisplay, Key.F7);
		Bind(KeyAction.DamageDetail, Key.F8);
		Bind(KeyAction.LookLeft, Key.F9);
		Bind(KeyAction.LookRight, Key.F10);
		Bind(KeyAction.ViewBack, Key.Escape);
		Bind(KeyAction.OutsideView, Key.V);
		Bind(KeyAction.OtherHercsStatus, Key.D);
		Bind(KeyAction.DropNavMarker, KeyChord.WithAlt(Key.D));

		section = KeySection.FlashComm;
		Bind(KeyAction.FlashCommAttackMyTarget, Key.A);
		Bind(KeyAction.FlashCommIgnoreMyTarget, Key.G);
		Bind(KeyAction.FlashCommHelpMeOut, Key.H);
		Bind(KeyAction.FlashCommJoinOnMe, Key.O);
		Bind(KeyAction.FlashCommScanForHostiles, Key.C);
		Bind(KeyAction.FlashCommEmcon, Key.E);
		Bind(KeyAction.FlashCommFireAtWill, Key.F);
		Bind(KeyAction.FlashCommNextOrder, Key.Period);
		Bind(KeyAction.FlashCommPreviousOrder, Key.Comma);
		Bind(KeyAction.FlashCommTransmit, Key.X);
		Bind(KeyAction.SendAttackMyTarget, KeyChord.WithAlt(Key.A));
		Bind(KeyAction.SendIgnoreMyTarget, KeyChord.WithAlt(Key.G));
		Bind(KeyAction.SendHelpMeOut, KeyChord.WithAlt(Key.H));
		Bind(KeyAction.SendJoinOnMe, KeyChord.WithAlt(Key.O));
		Bind(KeyAction.SendScanForHostiles, KeyChord.WithAlt(Key.C));
		Bind(KeyAction.SendEmcon, KeyChord.WithAlt(Key.E));
		Bind(KeyAction.SendFireAtWill, KeyChord.WithAlt(Key.F));

		section = KeySection.CommandDisplay;
		Bind(KeyAction.CommandSquadmate1, Key.Number1);
		Bind(KeyAction.CommandSquadmate2, Key.Number2);
		Bind(KeyAction.CommandSquadmate3, Key.Number3);
		Bind(KeyAction.CommandDisengage, Key.D);
		Bind(KeyAction.CommandAttackEnemy, Key.A);
		Bind(KeyAction.CommandDefendPosition, Key.F);
		Bind(KeyAction.CommandPatrolGridpoint, Key.T);
		Bind(KeyAction.CommandGotoGridpoint, Key.G);
		Bind(KeyAction.CommandJoinOnMe, Key.O);
		Bind(KeyAction.CommandScanForHostiles, Key.C);
		Bind(KeyAction.CommandEmcon, Key.E);
		Bind(KeyAction.CommandPreviousOrder, Key.Comma);
		Bind(KeyAction.CommandNextOrder, Key.Period);
		Bind(KeyAction.CommandNextUnit, Key.Tab);
		Bind(KeyAction.CommandPick, Key.Enter);
		Bind(KeyAction.CommandZoomIn, Key.Equal, Key.KeypadAdd);
		Bind(KeyAction.CommandZoomOut, Key.Minus, Key.KeypadSubtract);
		Bind(KeyAction.CommandScrollUp, Key.Up, Key.Keypad8);
		Bind(KeyAction.CommandScrollDown, Key.Down, Key.Keypad2);
		Bind(KeyAction.CommandScrollLeft, Key.Left, Key.Keypad4);
		Bind(KeyAction.CommandScrollRight, Key.Right, Key.Keypad6);
		Bind(KeyAction.CommandRecenter, Key.Keypad5);
		Bind(KeyAction.CommandTransmit, Key.X);
		Bind(KeyAction.CommandCancel, Key.Backspace);

		section = KeySection.DamageDetail;
		Bind(KeyAction.DamageStructural, Key.S);
		Bind(KeyAction.DamageInternal, Key.I);
		Bind(KeyAction.DamageWeapons, Key.W);
		Bind(KeyAction.DamageSystemsUp, Key.Up, Key.Keypad8);
		Bind(KeyAction.DamageSystemsDown, Key.Down, Key.Keypad2);
		Bind(KeyAction.DamageHercLeft, Key.Left, Key.Keypad4);
		Bind(KeyAction.DamageHercRight, Key.Right, Key.Keypad6);

		section = KeySection.OutsideView;
		Bind(KeyAction.SwapCameraControls, Key.Enter, Key.Tab);
		Bind(KeyAction.ViewNextSquadmate, Key.N);

		section = KeySection.PanelsAndGame;
		Bind(KeyAction.MissionStatus, Key.Q);
		Bind(KeyAction.Pause, Key.P);
		Bind(KeyAction.QuitGame, KeyChord.WithCtrl(Key.Q));
		Bind(KeyAction.Objectives, Key.F11);
		Bind(KeyAction.Preferences, Key.F12, KeyChord.WithAlt(Key.P));
		Bind(KeyAction.OnlineManual, Key.Slash);
		Bind(KeyAction.PanelAccept, Key.Enter, Key.KeypadEnter);
		Bind(KeyAction.PanelCancel, Key.Escape);
		Bind(KeyAction.PanelNextButton, Key.Tab, Key.Keypad0, Key.Insert);
		Bind(KeyAction.FullScreen, KeyChord.WithAlt(Key.Enter), KeyChord.WithAlt(Key.KeypadEnter));
		Bind(KeyAction.LeaveFullScreen, KeyChord.WithAlt(Key.Escape), KeyChord.WithCtrl(Key.Escape));
		Bind(KeyAction.StopTape, KeyChord.WithCtrl(Key.E));

		section = KeySection.Herculan;
		Bind(KeyAction.MenuBar, KeyChord.WithShift(Key.Escape));
		Bind(KeyAction.MenuBack, Key.Escape);
		// PrintScreenCapture's hook takes it by its virtual-key code, ahead of the window's keys.
		Bind(KeyAction.CopyFrame, Key.PrintScreen);

		return new KeyMap(bindings);
	}

	private static Key NumberKey(int row) => row < 9 ? Key.Number1 + row : Key.Number0;
}
