using Herculan.Engine.Input;
using Herculan.Engine.Menus;
using Silk.NET.Input;
using static Herculan.Engine.Menus.KeyListEntry;

namespace Herculan.Es1.Host;

/// <summary>
/// What the menu bar's Keys window lists for Metaltech: Earthsiege: the simulator's keys, laid out and worded as
/// docs/es1/key-bindings.md gives them, then the keys of HERCULAN's own that ES1 takes (<see cref="Input.Es1FullScreenKey"/>,
/// <see cref="Input.Es1HostKeyboard.MenuBarTakes"/>). A row whose meaning is Earthsiege 2's takes its <c>.lang</c> entry;
/// the rest are <c>keys.row.es1_*</c>. ES1 reads a key by its place, as the DOS keyboard's scancodes give it
/// (<see cref="Input.Es1HostKeyboard"/>), so the list names keys by their place (<see cref="KeyNaming.Position"/>).
/// A key the simulator's handlers read as another is shown as the doc and the manual show it: the grey arrows and
/// <c>Insert</c> for the keypad's keys they send.
/// </summary>
public static class Es1KeyList {
	public static KeyList All { get; } = Build();

	private static KeyList Build() {
		var list = new KeyListBuilder("keys.row.");
		static KeyListEntry Alt(Key key) => Of(KeyChord.WithAlt(key));
		static KeyListEntry Ctrl(Key key) => Of(KeyChord.WithCtrl(key));

		list.Section("keys.section.piloting");
		list.Row("throttle", Of(Key.Up), Of(Key.Down));
		list.Row("steer", Of(Key.Left), Of(Key.Right));
		list.Row("all_stop", Of(Key.Keypad5));
		list.Row("turret_twist", Of(Key.J), Of(Key.K));
		list.Row("turret_pitch", Of(Key.I), Of(Key.M));
		list.Row("es1_turret_full_speed", Alt(Key.J), Alt(Key.K), Alt(Key.I), Alt(Key.M));
		list.Row("es1_center_turret", Of(Key.Backspace));
		list.Row("diagonals", Of(Key.Keypad7), Of(Key.Keypad9), Of(Key.Keypad1), Of(Key.Keypad3));
		list.Row("es1_stick_mode", Of(Key.Insert));
		list.Row("es1_keypad_turret", Of(Key.Up), Of(Key.Down), Of(Key.Left), Of(Key.Right));
		list.Row("es1_throttle_direction", Ctrl(Key.Number1));
		list.Row("es1_wcs_throttle", Ctrl(Key.I), Ctrl(Key.M));

		list.Section("keys.section.weapons");
		list.Row("fire", Of(Key.Space));
		list.Row("es1_select_row", Run(Key.Number1, Key.Number9));
		list.Row("chain_row", Run(KeyChord.WithAlt(Key.Number1), KeyChord.WithAlt(Key.Number9)));
		list.Row("next_weapon", Of(Key.W), Alt(Key.W));
		list.Row("es1_link_weapon", Of(Key.L));
		list.Row("es1_next_chain", Of(Key.GraveAccent), Alt(Key.GraveAccent));
		list.Row("es1_next_chain_keep_weapon", Of(Key.B));
		list.Row("es1_power_level", Of(Key.Equal), Of(Key.Minus), Of(Key.KeypadAdd), Of(Key.KeypadSubtract));

		list.Section("keys.section.targeting");
		list.Row("next_target", Of(Key.Tab), Of(Key.T));
		list.Row("es1_turret_tracking", Alt(Key.T));
		list.Row("radar_mode", Of(Key.R));
		list.Row("es1_scanner_radar_mode", Of(Key.D));
		list.Row("es1_radar_range", Alt(Key.R));
		list.Row("shields", Of(Key.LeftBracket), Of(Key.RightBracket));

		list.Section("keys.section.displays");
		list.Row("mfd_status", Of(Key.F1));
		list.Row("mfd_flash_comm", Of(Key.F2));
		list.Row("mfd_nav_map", Of(Key.F3));
		list.Row("mfd_scanner", Of(Key.F4));
		list.Row("mfd_target", Of(Key.F5));
		list.Row("mfd_missile_cam", Of(Key.F6));
		list.Row("command_display", Of(Key.F7));
		list.Row("damage_detail", Of(Key.F8));
		list.Row("look_sideways", Of(Key.F9), Of(Key.F10));
		list.Row("view_back", Of(Key.Escape));
		list.Row("outside_view", Of(Key.V));

		list.Section("keys.section.flash_comm");
		list.Row("flash_comm_pick", Of(Key.A), Of(Key.G), Of(Key.H), Of(Key.O), Of(Key.C), Of(Key.E), Of(Key.F));
		list.Row("step_orders", Of(Key.Comma), Of(Key.Period));
		list.Row("flash_comm_transmit", Of(Key.X));
		list.Row("flash_comm_send", Alt(Key.A), Alt(Key.G), Alt(Key.H), Alt(Key.O), Alt(Key.C), Alt(Key.E), Alt(Key.F));

		list.Section("keys.section.command_display");
		list.Row("es1_command_squadmate", Of(Key.Number1), Of(Key.Number2), Of(Key.Number3));
		list.Row("command_order", Of(Key.D), Of(Key.A), Of(Key.F), Of(Key.T), Of(Key.G), Of(Key.O), Of(Key.C), Of(Key.E));
		list.Row("step_orders", Of(Key.Comma), Of(Key.Period));
		list.Row("command_next_unit", Of(Key.Tab));
		list.Row("command_pick", Of(Key.Enter));
		list.Row("command_zoom", Of(Key.Equal), Of(Key.Minus), Of(Key.KeypadAdd), Of(Key.KeypadSubtract));
		list.Row("command_scroll", Of(Key.Up), Of(Key.Down), Of(Key.Left), Of(Key.Right));
		list.Row("command_recenter", Of(Key.Keypad5));
		list.Row("command_transmit", Of(Key.X), Of(Key.Backspace));

		list.Section("keys.section.damage_detail");
		list.Row("damage_show", Of(Key.S), Of(Key.I), Of(Key.W));
		list.Row("damage_step", Of(Key.Up), Of(Key.Down));
		list.Row("damage_herc", Of(Key.Left), Of(Key.Right));

		list.Section("keys.section.outside_view");
		list.Row("camera_swing", Of(Key.Up), Of(Key.Down), Of(Key.Left), Of(Key.Right));
		list.Row("camera_zoom", Together(Key.Space, Key.Up), Together(Key.Space, Key.Down));
		list.Row("camera_swap", Of(Key.Tab), Of(Key.Enter));
		list.Row("es1_camera_next", Of(Key.N));
		list.Row("camera_back", Of(Key.V), Of(Key.Escape));

		list.Section("keys.section.panels");
		list.Row("mission_status", Of(Key.Q));
		list.Row("pause", Of(Key.P));
		list.Row("quit_game", Ctrl(Key.Q), Ctrl(Key.Escape));
		list.Row("objectives", Of(Key.F11));
		list.Row("preferences", Alt(Key.P), Of(Key.F12));
		list.Row("panel_accept", Of(Key.Enter));
		list.Row("panel_cancel", Of(Key.Escape));
		list.Row("es1_panel_step", Of(Key.Tab), Alt(Key.Tab));
		list.Row("es1_panel_next_wcs", Of(Key.Insert));
		list.Row("stop_tape", Ctrl(Key.E));
		list.Row("es1_freeze_tape", Alt(Key.S));

		list.Section("keys.section.herculan");
		list.Row("menu_bar", Of(KeyChord.WithShift(Key.Escape)));
		list.Row("menu_back", Of(Key.Escape));
		list.Row("full_screen", Alt(Key.Enter));

		return list.Build(KeyNaming.Position);
	}
}
