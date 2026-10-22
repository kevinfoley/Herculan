using System.Collections.Immutable;
using Herculan.Engine.Input;

namespace Herculan.Engine.Host.Settings;

/// <summary>One of an action's chords in a <see cref="KeyMap"/>, by its position: 0 is the primary key.</summary>
readonly record struct KeyRef(KeyAction Action, int Chord = 0);

/// <summary>
/// One key a <see cref="KeyRow"/> shows: chords held together, joined by "+" (most often one), or a numbered run from
/// <see cref="Held"/>'s chord to <see cref="RunEnd"/>'s, every action between them in <see cref="KeyAction"/> order
/// included.
/// </summary>
readonly record struct KeyRowEntry(ImmutableArray<KeyRef> Held, KeyRef? RunEnd = null) {
	/// <summary>Every action the entry stands for.</summary>
	public IEnumerable<KeyAction> Actions => RunEnd is { } end
		? Enumerable.Range((int)Held[0].Action, end.Action - Held[0].Action + 1).Select(action => (KeyAction)action)
		: Held.Select(key => key.Action);

	/// <summary>Every chord the entry shows.</summary>
	public IEnumerable<KeyRef> Shown => RunEnd is { } end ? [Held[0], end] : Held;
}

/// <summary>A line of the <see cref="KeysWindow"/>: its section, the <c>.lang</c> entry that says what it does, and its keys.</summary>
sealed record KeyRow(KeySection Section, string Description, ImmutableArray<KeyRowEntry> Keys);

/// <summary>
/// How the <see cref="KeysWindow"/> lays out a <see cref="KeyMap"/>'s actions: which actions share a line, which of
/// their chords it shows, and what the line says. The order and wording follow docs/retail/key-bindings.md for the
/// retail keys and docs/herculan/herculan-key-bindings.md for HERCULAN's own. The keys themselves are read from the map
/// when drawn (<see cref="KeyLabels"/>), so this names actions, never keys.
///
/// <para>Where the retail doc gives a line one key of several the map binds, as the arrows over their keypad twins, it
/// shows that one. A line may restate another section's action with what it does there, as the outside view's arrows,
/// which are the driving actions. Every action of <see cref="KeyMap.Retail"/> is on at least one line.</para>
/// </summary>
static class KeyRows {
	public static readonly ImmutableArray<KeyRow> All = Build();

	private static ImmutableArray<KeyRow> Build() {
		var rows = new List<KeyRow>();
		var section = KeySection.Driving;
		void Row(string id, params KeyRowEntry[] keys) => rows.Add(new KeyRow(section, "keys.row." + id, [.. keys]));

		Row("throttle", Key(KeyAction.ThrottleForward), Key(KeyAction.ThrottleBack));
		Row("steer", Key(KeyAction.SteerLeft), Key(KeyAction.SteerRight));
		Row("all_stop", Key(KeyAction.AllStop));
		Row("turret_twist", Key(KeyAction.TurretLeft), Key(KeyAction.TurretRight));
		Row("turret_pitch", Key(KeyAction.TurretUp), Key(KeyAction.TurretDown));
		Row("center_turret", Key(KeyAction.CenterTurret));
		Row("center_legs", Key(KeyAction.CenterLegs));
		Row("razor_throttle", Key(KeyAction.RazorThrottleUp), Key(KeyAction.RazorThrottleDown));
		Row("diagonals", Key(KeyAction.DiagonalForwardLeft), Key(KeyAction.DiagonalForwardRight),
			Key(KeyAction.DiagonalBackLeft), Key(KeyAction.DiagonalBackRight));

		section = KeySection.Weapons;
		Row("fire", Key(KeyAction.Fire));
		Row("select_row", Run(KeyAction.WeaponRow1, KeyAction.WeaponRow10));
		Row("chain_row", Run(KeyAction.ToggleChainRow1, KeyAction.ToggleChainRow10));
		Row("next_weapon", Key(KeyAction.NextWeapon), Key(KeyAction.PreviousWeapon));
		Row("link_weapon", Key(KeyAction.LinkWeapon));
		Row("next_chain", Key(KeyAction.NextChain));
		Row("power_level", Key(KeyAction.PowerUp), Key(KeyAction.PowerDown), Key(KeyAction.PowerUp, 1), Key(KeyAction.PowerDown, 1));

		section = KeySection.TargetingAndShields;
		Row("next_target", Key(KeyAction.NextTarget));
		Row("nearest_target", Key(KeyAction.NearestTarget));
		Row("clear_target", Key(KeyAction.ClearTarget));
		Row("target_component", Key(KeyAction.NextTargetComponent));
		Row("radar_mode", Key(KeyAction.RadarMode));
		Row("radar_range", Key(KeyAction.RadarRange));
		Row("scanner_next_target", Key(KeyAction.ScannerNextTarget));
		Row("turret_tracking", Key(KeyAction.AutoTurretTracking));
		Row("shields", Key(KeyAction.ShieldsRear), Key(KeyAction.ShieldsFront));

		// F1 to F6 a line each, since each brings up its own screen.
		section = KeySection.DisplaysAndViews;
		Row("mfd_status", Key(KeyAction.MfdStatus));
		Row("mfd_flash_comm", Key(KeyAction.MfdFlashComm));
		Row("mfd_nav_map", Key(KeyAction.MfdNavMap));
		Row("mfd_scanner", Key(KeyAction.MfdScanner));
		Row("mfd_target", Key(KeyAction.MfdTarget));
		Row("mfd_missile_cam", Key(KeyAction.MfdMissileCam));
		Row("command_display", Key(KeyAction.CommandDisplay));
		Row("damage_detail", Key(KeyAction.DamageDetail));
		Row("look_sideways", Key(KeyAction.LookLeft), Key(KeyAction.LookRight));
		Row("view_back", Key(KeyAction.ViewBack));
		Row("outside_view", Key(KeyAction.OutsideView));
		Row("other_hercs", Key(KeyAction.OtherHercsStatus));
		Row("nav_marker", Key(KeyAction.DropNavMarker));

		section = KeySection.FlashComm;
		Row("flash_comm_pick", Key(KeyAction.FlashCommAttackMyTarget), Key(KeyAction.FlashCommIgnoreMyTarget),
			Key(KeyAction.FlashCommHelpMeOut), Key(KeyAction.FlashCommJoinOnMe), Key(KeyAction.FlashCommScanForHostiles),
			Key(KeyAction.FlashCommEmcon), Key(KeyAction.FlashCommFireAtWill));
		Row("step_orders", Key(KeyAction.FlashCommNextOrder), Key(KeyAction.FlashCommPreviousOrder));
		Row("flash_comm_transmit", Key(KeyAction.FlashCommTransmit));
		Row("flash_comm_send", Key(KeyAction.SendAttackMyTarget), Key(KeyAction.SendIgnoreMyTarget), Key(KeyAction.SendHelpMeOut),
			Key(KeyAction.SendJoinOnMe), Key(KeyAction.SendScanForHostiles), Key(KeyAction.SendEmcon), Key(KeyAction.SendFireAtWill));

		section = KeySection.CommandDisplay;
		Row("command_squadmate", Key(KeyAction.CommandSquadmate1), Key(KeyAction.CommandSquadmate2), Key(KeyAction.CommandSquadmate3));
		Row("command_order", Key(KeyAction.CommandDisengage), Key(KeyAction.CommandAttackEnemy), Key(KeyAction.CommandDefendPosition),
			Key(KeyAction.CommandPatrolGridpoint), Key(KeyAction.CommandGotoGridpoint), Key(KeyAction.CommandJoinOnMe),
			Key(KeyAction.CommandScanForHostiles), Key(KeyAction.CommandEmcon));
		Row("step_orders", Key(KeyAction.CommandPreviousOrder), Key(KeyAction.CommandNextOrder));
		Row("command_next_unit", Key(KeyAction.CommandNextUnit));
		Row("command_pick", Key(KeyAction.CommandPick));
		Row("command_zoom", Key(KeyAction.CommandZoomIn), Key(KeyAction.CommandZoomOut), Key(KeyAction.CommandZoomIn, 1),
			Key(KeyAction.CommandZoomOut, 1));
		Row("command_scroll", Key(KeyAction.CommandScrollUp), Key(KeyAction.CommandScrollDown), Key(KeyAction.CommandScrollLeft),
			Key(KeyAction.CommandScrollRight));
		Row("command_recenter", Key(KeyAction.CommandRecenter));
		Row("command_transmit", Key(KeyAction.CommandTransmit), Key(KeyAction.CommandCancel));

		section = KeySection.DamageDetail;
		Row("damage_show", Key(KeyAction.DamageStructural), Key(KeyAction.DamageInternal), Key(KeyAction.DamageWeapons));
		Row("damage_step", Key(KeyAction.DamageSystemsUp), Key(KeyAction.DamageSystemsDown));
		Row("damage_herc", Key(KeyAction.DamageHercLeft), Key(KeyAction.DamageHercRight));

		// The driving and firing actions, as the outside view takes them.
		section = KeySection.OutsideView;
		Row("camera_swing", Key(KeyAction.ThrottleForward), Key(KeyAction.ThrottleBack), Key(KeyAction.SteerLeft),
			Key(KeyAction.SteerRight));
		Row("camera_zoom", Held(KeyAction.Fire, KeyAction.ThrottleForward), Held(KeyAction.Fire, KeyAction.ThrottleBack));
		Row("camera_swap", Key(KeyAction.SwapCameraControls), Key(KeyAction.SwapCameraControls, 1));
		Row("camera_next", Key(KeyAction.ViewNextSquadmate));
		Row("camera_back", Key(KeyAction.OutsideView), Key(KeyAction.ViewBack));

		// The panels' Enter and full screen's Alt+Enter leave out the keypad's Enter, and Tab's line its keypad 0,
		// which the retail doc counts as the same keys.
		section = KeySection.PanelsAndGame;
		Row("mission_status", Key(KeyAction.MissionStatus));
		Row("pause", Key(KeyAction.Pause));
		Row("quit_game", Key(KeyAction.QuitGame));
		Row("objectives", Key(KeyAction.Objectives));
		Row("preferences", Key(KeyAction.Preferences), Key(KeyAction.Preferences, 1));
		Row("online_manual", Key(KeyAction.OnlineManual));
		Row("panel_accept", Key(KeyAction.PanelAccept));
		Row("panel_cancel", Key(KeyAction.PanelCancel));
		Row("panel_next", Key(KeyAction.PanelNextButton), Key(KeyAction.PanelNextButton, 2));
		Row("full_screen", Key(KeyAction.FullScreen));
		Row("leave_full_screen", Key(KeyAction.LeaveFullScreen), Key(KeyAction.LeaveFullScreen, 1));
		Row("stop_tape", Key(KeyAction.StopTape));

		section = KeySection.Herculan;
		Row("menu_bar", Key(KeyAction.MenuBar));
		Row("menu_back", Key(KeyAction.MenuBack));
		Row("copy_frame", Key(KeyAction.CopyFrame));

		return [.. rows];
	}

	private static KeyRowEntry Key(KeyAction action, int chord = 0) => new([new KeyRef(action, chord)]);

	private static KeyRowEntry Run(KeyAction first, KeyAction last) => new([new KeyRef(first)], new KeyRef(last));

	private static KeyRowEntry Held(params KeyAction[] actions) => new([.. actions.Select(action => new KeyRef(action))]);
}
