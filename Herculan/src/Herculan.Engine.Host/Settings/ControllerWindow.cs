using System.Numerics;
using HercWorks.Core.Io;
using Herculan.Engine.Host.Localization;
using Herculan.Engine.Input;
using Herculan.Engine.Platform;
using Herculan.Engine.Sim;
using ImGuiNET;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Settings;

/// <summary>
/// The floating "Controller" window opened from the menu bar: which axes, hat and buttons of a connected controller
/// stand for the stick, throttle, rudder, hat and eight buttons the retail bindings name, and the radar toggle, which is
/// this engine's own (<see cref="JoystickDeviceMap.RadarToggle"/>). It edits the install's
/// <c>data\herculan-joystick.cfg</c> (<see cref="JoystickMapFile"/>; docs/herculan/joystick-config.md), and is this
/// engine's own: retail reads a stick's axes in a fixed order and has nothing to set. What each control does stays
/// with retail's CONTROLS panel. Above the device's own settings, and only while a gamepad is connected, "Mouse emulation" chooses the stick, its speed and the triggers
/// a gamepad works the pointer with in the front end and the mission editor (<see cref="JoystickMapFile.Mouse"/>), which
/// serve every gamepad whichever device is being edited.
///
/// <para>What the player sees and does is docs/herculan/joystick-config.md, "Setting up a stick". Save hands the file to
/// a mission's stick (<see cref="JoystickSource.Configure"/>); the front end reads no stick, and the next mission reads
/// the file. Cancel, the title-bar close and [Esc] (<see cref="HostMenuBar.BackOut"/>) discard the edit.</para>
/// </summary>
sealed class ControllerWindow {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 600f;

	// How far an axis has to move from where it rested when Detect was pressed to be the one meant: half its
	// travel, so a stick brushed on the way to the throttle is not taken for it.
	private const float DetectTravel = 0.5f;

	// The rows, in the order drawn: the four axes, the hat, BUTTON 1-8, then the radar toggle.
	private const int ThrottleRow = 2;
	private const int AxisRows = 4;
	private const int HatRow = AxisRows;
	private const int FirstButtonRow = HatRow + 1;
	private const int RadarRow = FirstButtonRow + JoystickCapabilities.MaxButtons;
	private const int NotDetecting = -1;

	private static readonly string[] AxisRowKeys =
		["controller.stick_x", "controller.stick_y", "controller.throttle", "controller.rudder"];

	private static readonly Vector4 LitColour = new(0.45f, 0.85f, 0.45f, 1f);
	private static readonly Vector4 ErrorColour = new(1f, 0.45f, 0.4f, 1f);

	// The device input a row is filled from, for telling which rows share one: an axis row takes its whole axis (Sign 0),
	// a button row a device button (Axis false, Sign 0) or one direction of an axis.
	private readonly record struct Bound(bool Axis, int Index, int Sign);

	private readonly LocalizationTable _localization;
	private readonly Func<string?> _dataDirectory;
	private readonly (string key, Action callback)[] _closeButtons;

	private IInputContext? _input;
	private JoystickSource? _live;
	private string? _error;

	// The file as it will be saved, the device being edited (GLFW's slot), and its map. _loaded is the map as it came out
	// of the file or the defaults, so a device only looked at, and not edited, gets no section of its own.
	private JoystickMapFile _file = new();
	private int _slot = JoystickDeviceMap.Unmapped;
	private JoystickDeviceMap _draft = new();
	private JoystickDeviceMap? _loaded;

	// The row Detect is listening for, and where the device's axes and buttons stood when it started.
	private int _detecting = NotDetecting;
	private float[] _restingAxes = [];
	private bool[] _restingButtons = [];

	/// <param name="dataDirectory">The game's <c>data</c> folder the map is read from and saved to, or null when there
	/// is none.</param>
	public ControllerWindow(LocalizationTable localization, Func<string?> dataDirectory) {
		_localization = localization;
		_dataDirectory = dataDirectory;
		_closeButtons = [("controller.defaults", ApplyDefaults), ("general.save", Save), ("general.cancel", Cancel)];
	}

	/// <summary>Whether the window is up.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>Raised by Save with the file it wrote, for whatever reads it outside a mission.</summary>
	public event Action<JoystickMapFile>? Saved;

	/// <summary>Whether Detect is listening for a control to bind.</summary>
	public bool Listening => IsOpen && _detecting != NotDetecting;

	/// <summary>Takes the window's input context once it exists, and a mission's stick, which Save reconfigures.</summary>
	public void Attach(IInputContext input, JoystickSource? live = null) {
		_input = input;
		_live = live;
	}

	/// <summary>
	/// Opens the window on the device a mission reads (<see cref="ControllerDevice.Pick"/>), with its saved map, or its
	/// defaults when the file has none.
	/// </summary>
	public void Open() {
		if (IsOpen) {
			return;
		}

		_file = MapPath() is { } path ? JoystickMapFile.Load(path) : new JoystickMapFile();
		Edit(_input == null ? null : ControllerDevice.Pick(_input, _file));
		_error = null;
		_detecting = NotDetecting;
		IsOpen = true;
	}

	/// <summary>Closes the window and discards the edit.</summary>
	public void Cancel() {
		IsOpen = false;
		_detecting = NotDetecting;
	}

	/// <summary>
	/// Draws the window, when it is up. Call once per frame inside the ImGui frame. <paramref name="centre"/> is when it
	/// is centred on the window (<see cref="PanelCentring.Condition"/>).
	/// </summary>
	public void Draw(ImGuiCond centre) {
		if (!IsOpen) {
			return;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), centre, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin(Text("controller.title") + "###controller", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			var device = CurrentDevice();
			DeviceRow(device);
			if (device != null) {
				Detect(device);
			} else {
				ImGui.TextWrapped(Text("controller.none_connected"));
			}

			if (_input != null && ControllerDevice.Connected(_input).Any(connected => connected.IsGamepad)) {
				MouseEmulation();
			}

			if (device != null) {
				ImGui.SeparatorText(Text("controller.inputs"));
				DeviceState(device);
				ImGui.SeparatorText(Text("controller.controls"));
				ControlRows(device);
				ImGui.SeparatorText(Text("controller.options"));
				Options(device);
			}

			ImGui.Spacing();
			ImGui.PushTextWrapPos(0f);
			ImGui.TextDisabled(Text("controller.actions_note"));
			ImGui.PopTextWrapPos();

			if (_error != null) {
				ImGui.PushStyleColor(ImGuiCol.Text, ErrorColour);
				ImGui.TextWrapped(_error);
				ImGui.PopStyleColor();
			}

			ImGui.Separator();
			LocalizedImGuiHelpers.DrawButtonRow(_localization, _closeButtons);
		}

		ImGui.End();

		if (!stayOpen && IsOpen) {
			Cancel();
		}
	}

	// The device being edited, as a combo of every connected one, and which devices its map serves. Choosing another
	// keeps the edit made to this one, to be saved with it.
	private void DeviceRow(ControllerDevice? device) {
		int current = device?.Slot ?? JoystickDeviceMap.Unmapped;
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
		if (ImGui.BeginCombo(Text("controller.device") + "###device",
				device == null ? Text("controller.no_device") : DeviceLabel(device))) {
			if (_input != null) {
				foreach (var connected in ControllerDevice.Connected(_input)) {
					int slot = connected.Slot;
					if (ImGui.Selectable(DeviceLabel(connected) + "###device" + slot, slot == current) && slot != current) {
						Keep(device);
						Edit(connected);
					}
				}
			}

			ImGui.EndCombo();
		}

		if (device?.IsGamepad == true) {
			ImGui.SameLine();
			ImGui.TextDisabled(Text("controller.gamepad_layout"));
			Tooltip("controller.gamepad_layout_tip");
		}

		if (device != null) {
			ImGui.TextDisabled(Text(device.IsGamepad ? "controller.saved_for_gamepads"
				: device.Guid != null ? "controller.saved_for_model"
				: "controller.saved_for_joysticks"));
		}
	}

	// The device in the slot being edited. One unplugged since is replaced by the device a mission would read now.
	private ControllerDevice? CurrentDevice() {
		if (_input == null) {
			return null;
		}

		if (ControllerDevice.At(_input, _slot) is { } device) {
			return device;
		}

		var next = ControllerDevice.Pick(_input, _file);
		if (next != null) {
			Edit(next);
		}

		return next;
	}

	// Starts editing device: its map in the file being edited, else its defaults.
	private void Edit(ControllerDevice? device) {
		_detecting = NotDetecting;
		_slot = device?.Slot ?? JoystickDeviceMap.Unmapped;
		_draft = device?.MapIn(_file) ?? device?.Defaults() ?? new JoystickDeviceMap();
		_loaded = _draft;
	}

	// Puts the edit made to device into the file being edited, unless nothing was changed.
	private void Keep(ControllerDevice? device) {
		if (device != null && !ReferenceEquals(_draft, _loaded)) {
			_file.Set(device.IsGamepad, device.Guid, device.Name, _draft);
			_loaded = _draft;
		}
	}

	// Which stick moves the pointer and how fast, and which trigger is the left button while a gamepad works as the mouse.
	private void MouseEmulation() {
		ImGui.SeparatorText(Text("controller.mouse_emulation"));
		ImGui.PushTextWrapPos(0f);
		ImGui.TextDisabled(Text("controller.mouse_emulation_note"));
		ImGui.PopTextWrapPos();

		var mouse = _file.Mouse;
		if (SideCombo("controller.cursor_control", "###cursor", mouse.CursorStick,
				(GamepadSide.Left, "controller.left_stick"), (GamepadSide.Right, "controller.right_stick")) is { } stick) {
			_file.Mouse = mouse with { CursorStick = stick };
		}

		int speed = mouse.CursorSpeed;
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
		if (ImGui.SliderInt(Text("controller.cursor_speed") + "###cursorSpeed", ref speed,
				GamepadMouseSettings.MinCursorSpeed, GamepadMouseSettings.MaxCursorSpeed, "%d%%", ImGuiSliderFlags.AlwaysClamp)) {
			_file.Mouse = _file.Mouse with { CursorSpeed = speed };
		}
		Tooltip("controller.cursor_speed_tip");

		mouse = _file.Mouse;
		if (SideCombo("controller.clicking", "###clicking", mouse.LeftClickTrigger,
				(GamepadSide.Right, "controller.right_trigger_clicks"), (GamepadSide.Left, "controller.left_trigger_clicks")) is { } trigger) {
			_file.Mouse = mouse with { LeftClickTrigger = trigger };
		}
	}

	// A combo of two choices; the one chosen, or null when nothing new was.
	private GamepadSide? SideCombo(string labelKey, string id, GamepadSide current,
			(GamepadSide Side, string Key) first, (GamepadSide Side, string Key) second) {
		GamepadSide? chosen = null;
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
		if (ImGui.BeginCombo(Text(labelKey) + id, Text(current == first.Side ? first.Key : second.Key))) {
			foreach (var (side, key) in (ReadOnlySpan<(GamepadSide, string)>)[first, second]) {
				if (ImGui.Selectable(Text(key), side == current)) {
					chosen = side;
				}
			}

			ImGui.EndCombo();
		}

		return chosen == current ? null : chosen;
	}

	// Every axis, button and hat the device reports, as it reports them.
	private void DeviceState(ControllerDevice device) {
		const int Columns = 3;
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		float width = (ImGui.GetContentRegionAvail().X - spacing * (Columns - 1)) / Columns;
		for (int i = 0; i < device.AxisCount; i++) {
			if (i % Columns != 0) {
				ImGui.SameLine();
			}

			float position = device.Axis(i);
			ImGui.ProgressBar((Math.Clamp(position, -1f, 1f) + 1f) / 2f, new Vector2(width, 0f),
				$"{Format("controller.axis", i)}  {position:+0.00;-0.00}");
		}

		if (device.ButtonCount > 0) {
			ImGui.TextUnformatted(Text("controller.buttons"));
			for (int i = 0; i < device.ButtonCount; i++) {
				ImGui.SameLine();
				Lit(device.Button(i), i.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}
		}

		for (int i = 0; i < device.HatCount; i++) {
			var hat = device.Hat(i);
			ImGui.TextUnformatted(Format("controller.hat", i));
			ImGui.SameLine();
			Lit(hat != JoystickHat.None, HatText(hat));
		}
	}

	// One row per control the bindings name: which device input fills it, its invert, Detect, and what the simulator
	// reads from it through the map being edited.
	private void ControlRows(ControllerDevice device) {
		var reading = JoystickSource.Read(_draft, device);
		var clashes = Clashes();
		if (!ImGui.BeginTable("##controls", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) {
			return;
		}

		ImGui.TableSetupColumn(Text("controller.control"), ImGuiTableColumnFlags.WidthStretch, 1.1f);
		ImGui.TableSetupColumn(Text("controller.input"), ImGuiTableColumnFlags.WidthStretch, 1.2f);
		ImGui.TableSetupColumn(Text("controller.invert"), ImGuiTableColumnFlags.WidthFixed);
		ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
		ImGui.TableSetupColumn(Text("controller.reads"), ImGuiTableColumnFlags.WidthStretch, 1f);
		ImGui.TableHeadersRow();

		for (int row = 0; row < AxisRows; row++) {
			ImGui.PushID(row);
			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(Text(AxisRowKeys[row]));

			ImGui.TableNextColumn();
			if (InputCombo(AxisOf(row), device.AxisCount, "controller.axis", clashes[row]) is { } axis) {
				_draft = WithAxis(row, axis);
			}

			ImGui.TableNextColumn();
			bool invert = InvertOf(row);
			if (ImGui.Checkbox("##invert", ref invert)) {
				_draft = WithInvert(row, invert);
			}
			// A gamepad's throttle slot is the right stick's up and down, which no lever reading applies to.
			if (row == ThrottleRow) {
				Tooltip(device.IsGamepad ? "controller.invert_throttle_gamepad_tip" : "controller.invert_throttle_tip");
			}

			ImGui.TableNextColumn();
			DetectButton(row, device);

			ImGui.TableNextColumn();
			int value = ValueOf(reading, row);
			ImGui.ProgressBar((Math.Clamp((float)value / MechControls.AxisFull, -1f, 1f) + 1f) / 2f, new Vector2(-1f, 0f),
				value.ToString(System.Globalization.CultureInfo.InvariantCulture));
			ImGui.PopID();
		}

		ImGui.PushID(HatRow);
		ImGui.TableNextRow();
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(Text("controller.hat_row"));
		ImGui.TableNextColumn();
		if (InputCombo(_draft.HatIndex, device.HatCount, "controller.hat", clash: null) is { } hatIndex) {
			_draft = _draft with { HatIndex = hatIndex };
		}
		ImGui.TableNextColumn();
		ImGui.TableNextColumn();
		DetectButton(HatRow, device);
		ImGui.TableNextColumn();
		Lit(reading.Hat != JoystickHat.None, HatText(reading.Hat));
		ImGui.PopID();

		for (int i = 0; i < JoystickCapabilities.MaxButtons; i++) {
			int row = FirstButtonRow + i;
			ImGui.PushID(row);
			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(Format("controller.button_row", i + 1));
			ImGui.TableNextColumn();
			if (ButtonCombo(ButtonOf(i), device, clashes[row]) is { } button) {
				_draft = WithButton(i, button);
			}
			ImGui.TableNextColumn();
			ImGui.TableNextColumn();
			DetectButton(row, device);
			ImGui.TableNextColumn();
			Lit(reading.Button(i), Text(reading.Button(i) ? "controller.pressed" : "controller.released"));
			ImGui.PopID();
		}

		ImGui.PushID(RadarRow);
		ImGui.TableNextRow();
		ImGui.TableNextColumn();
		ImGui.TextUnformatted(Text("controller.radar_toggle"));
		Tooltip("controller.radar_toggle_tip");
		ImGui.TableNextColumn();
		if (ButtonCombo(_draft.RadarToggle, device, clashes[RadarRow]) is { } radar) {
			_draft = _draft with { RadarToggle = radar };
		}
		ImGui.TableNextColumn();
		ImGui.TableNextColumn();
		DetectButton(RadarRow, device);
		ImGui.TableNextColumn();
		bool radarHeld = JoystickSource.RadarToggleHeld(_draft, device);
		Lit(radarHeld, Text(radarHeld ? "controller.pressed" : "controller.released"));
		ImGui.PopID();

		ImGui.EndTable();
	}

	// The map's options that are not a choice of input. Centre-idle reads a throttle lever, and a gamepad's controls
	// read none (GamepadControls), so it is greyed for one.
	private void Options(ControllerDevice device) {
		int deadzone = _draft.Deadzone;
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
		if (ImGui.SliderInt(Text("controller.deadzone") + "###deadzone", ref deadzone, 0, JoystickReading.RawFull - 1)) {
			_draft = _draft with { Deadzone = deadzone };
		}
		Tooltip("controller.deadzone_tip");

		bool diagonals = _draft.HatDiagonals;
		if (ImGui.Checkbox(Text("controller.hat_diagonals") + "###diagonals", ref diagonals)) {
			_draft = _draft with { HatDiagonals = diagonals };
		}
		Tooltip("controller.hat_diagonals_tip");

		bool bipolar = _draft.BipolarThrottle;
		ImGui.BeginDisabled(device.IsGamepad);
		if (ImGui.Checkbox(Text("controller.centre_idle") + "###bipolar", ref bipolar)) {
			_draft = _draft with { BipolarThrottle = bipolar };
		}
		ImGui.EndDisabled();
		Tooltip(device.IsGamepad ? "controller.centre_idle_gamepad_tip" : "controller.centre_idle_tip");

		bool razorPitch = _draft.InvertRazorPitch;
		if (ImGui.Checkbox(Text("controller.invert_razor_pitch") + "###razorpitch", ref razorPitch)) {
			_draft = _draft with { InvertRazorPitch = razorPitch };
		}
		Tooltip("controller.invert_razor_pitch_tip");
	}

	// For each row, which other rows its device input also fills, as a sentence naming them, or null where it fills only
	// its own. Two button rows on an axis's opposite directions do not share it; an axis row and a button row on either
	// direction of its axis do, since moving the axis presses the button.
	private string?[] Clashes() {
		var bound = new Bound?[RadarRow + 1];
		for (int row = 0; row < AxisRows; row++) {
			bound[row] = AxisOf(row) is var axis && axis != JoystickDeviceMap.Unmapped ? new Bound(true, axis, 0) : null;
		}

		for (int i = 0; i < JoystickCapabilities.MaxButtons; i++) {
			bound[FirstButtonRow + i] = BoundOf(ButtonOf(i));
		}

		bound[RadarRow] = BoundOf(_draft.RadarToggle);

		var clashes = new string?[bound.Length];
		for (int row = 0; row < bound.Length; row++) {
			if (bound[row] is not { } input) {
				continue;
			}

			var others = new List<string>();
			for (int other = 0; other < bound.Length; other++) {
				if (other != row && bound[other] is { } shared && shared.Axis == input.Axis && shared.Index == input.Index
						&& (shared.Sign == 0 || input.Sign == 0 || shared.Sign == input.Sign)) {
					others.Add(RowLabel(other));
				}
			}

			if (others.Count > 0) {
				clashes[row] = string.Format(Text("controller.also_drives"), string.Join(", ", others));
			}
		}

		return clashes;
	}

	private static Bound? BoundOf(JoystickButtonSource source) =>
		source.IsMapped ? new Bound(source.IsAxis, source.Index, source.AxisSign) : null;

	private string RowLabel(int row) =>
		row < AxisRows ? Text(AxisRowKeys[row])
		: row == HatRow ? Text("controller.hat_row")
		: row == RadarRow ? Text("controller.radar_toggle")
		: Format("controller.button_row", row - FirstButtonRow + 1);

	// Opens a Device input combo on preview. While clash names other rows the same input fills, the preview is red and
	// hovering it says which.
	private static bool BeginInputCombo(string preview, string? clash) {
		ImGui.SetNextItemWidth(-1f);
		if (clash != null) {
			ImGui.PushStyleColor(ImGuiCol.Text, ErrorColour);
		}

		bool open = ImGui.BeginCombo("##input", preview);
		if (clash != null) {
			ImGui.PopStyleColor();
			if (!open && ImGui.IsItemHovered()) {
				ScaledImGui.Tooltip(clash);
			}
		}

		return open;
	}

	// A combo of "none" and each of the device's count inputs; the input chosen, or null when nothing was. An index the
	// device does not have, from a file written for another one, shows as itself until it is changed.
	private int? InputCombo(int current, int count, string labelKey, string? clash) {
		int? chosen = null;
		if (BeginInputCombo(current == JoystickDeviceMap.Unmapped ? Text("controller.unmapped") : Format(labelKey, current), clash)) {
			if (ImGui.Selectable(Text("controller.unmapped"), current == JoystickDeviceMap.Unmapped)) {
				chosen = JoystickDeviceMap.Unmapped;
			}

			for (int i = 0; i < count; i++) {
				if (ImGui.Selectable(Format(labelKey, i), i == current)) {
					chosen = i;
				}
			}

			ImGui.EndCombo();
		}

		return chosen == current ? null : chosen;
	}

	// InputCombo for a button row, which takes each device button and then each axis in either direction.
	private JoystickButtonSource? ButtonCombo(JoystickButtonSource current, ControllerDevice device, string? clash) {
		JoystickButtonSource? chosen = null;
		if (BeginInputCombo(SourceLabel(current), clash)) {
			if (ImGui.Selectable(Text("controller.unmapped"), !current.IsMapped)) {
				chosen = JoystickButtonSource.Unmapped;
			}

			for (int i = 0; i < device.ButtonCount; i++) {
				var button = JoystickButtonSource.Button(i);
				if (!device.Reserved(i) && ImGui.Selectable(SourceLabel(button), button == current)) {
					chosen = button;
				}
			}

			for (int i = 0; i < device.AxisCount; i++) {
				foreach (int sign in (ReadOnlySpan<int>)[1, -1]) {
					var axis = JoystickButtonSource.Axis(i, sign);
					if (ImGui.Selectable(SourceLabel(axis), axis == current)) {
						chosen = axis;
					}
				}
			}

			ImGui.EndCombo();
		}

		return chosen == current ? null : chosen;
	}

	private string SourceLabel(JoystickButtonSource source) =>
		!source.IsMapped ? Text("controller.unmapped")
		: source.IsAxis ? string.Format(Text("controller.axis_direction"), source.Index, source.AxisSign > 0 ? "+" : "-")
		: Format("controller.button", source.Index);

	// Detect, or Cancel while this row is listening. Pressing it notes where everything rests, so Detect takes what
	// moves from there and not what is already held or off-centre, such as a trigger that rests at one end.
	private void DetectButton(int row, ControllerDevice device) {
		bool listening = _detecting == row;
		if (ImGui.Button(Text(listening ? "controller.detecting" : "controller.detect") + "##detect")) {
			if (listening) {
				_detecting = NotDetecting;
				return;
			}

			_detecting = row;
			_restingAxes = Enumerable.Range(0, device.AxisCount).Select(device.Axis).ToArray();
			_restingButtons = Enumerable.Range(0, device.ButtonCount).Select(device.Button).ToArray();
		}

		if (listening && ImGui.IsItemHovered()) {
			ScaledImGui.Tooltip(Text(row < AxisRows ? "controller.detect_axis" : row == HatRow ? "controller.detect_hat" : "controller.detect_button"));
		}
	}

	// Binds the listening row to whatever has moved since Detect was pressed.
	private void Detect(ControllerDevice device) {
		if (_detecting == NotDetecting) {
			return;
		}

		if (_detecting < AxisRows) {
			for (int i = 0; i < device.AxisCount && i < _restingAxes.Length; i++) {
				if (MathF.Abs(device.Axis(i) - _restingAxes[i]) > DetectTravel) {
					_draft = WithAxis(_detecting, i);
					_detecting = NotDetecting;
					return;
				}
			}
		} else if (_detecting == HatRow) {
			for (int i = 0; i < device.HatCount; i++) {
				if (device.Hat(i) != JoystickHat.None) {
					_draft = _draft with { HatIndex = i };
					_detecting = NotDetecting;
					return;
				}
			}
		} else {
			// A button row, or the radar toggle, takes a new button press, or an axis moved far enough, toward the end it
			// moved to.
			for (int i = 0; i < device.ButtonCount && i < _restingButtons.Length; i++) {
				if (device.Button(i) && !_restingButtons[i] && !device.Reserved(i)) {
					_draft = WithSource(_detecting, JoystickButtonSource.Button(i));
					_detecting = NotDetecting;
					return;
				}
			}

			for (int i = 0; i < device.AxisCount && i < _restingAxes.Length; i++) {
				float moved = device.Axis(i) - _restingAxes[i];
				if (MathF.Abs(moved) > DetectTravel) {
					_draft = WithSource(_detecting, JoystickButtonSource.Axis(i, moved > 0 ? 1 : -1));
					_detecting = NotDetecting;
					return;
				}
			}
		}
	}

	private void ApplyDefaults() {
		_detecting = NotDetecting;
		_draft = CurrentDevice()?.Defaults() ?? new JoystickDeviceMap();
		_file.Mouse = new GamepadMouseSettings();
	}

	private void Save() {
		if (MapPath() is not { } path) {
			_error = Text("controller.no_data_folder");
			return;
		}

		// The device on screen is saved as shown, even unedited, since Save is pressed for it.
		if (CurrentDevice() is { } device) {
			_file.Set(device.IsGamepad, device.Guid, device.Name, _draft);
		}

		try {
			_file.Save(path);
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_error = string.Format(Text("settings.write_failed"), JoystickMapFile.FileName, ex.Message);
			return;
		}

		_live?.Configure(_file);
		Saved?.Invoke(_file);
		Cancel();
	}

	private string? MapPath() =>
		_dataDirectory() is { } directory && Directory.Exists(directory)
			? CaseInsensitivePath.Combine(directory, JoystickMapFile.FileName)
			: null;

	private int AxisOf(int row) => row switch {
		0 => _draft.StickXAxis,
		1 => _draft.StickYAxis,
		2 => _draft.ThrottleAxis,
		_ => _draft.RudderAxis,
	};

	private JoystickDeviceMap WithAxis(int row, int axis) => row switch {
		0 => _draft with { StickXAxis = axis },
		1 => _draft with { StickYAxis = axis },
		2 => _draft with { ThrottleAxis = axis },
		_ => _draft with { RudderAxis = axis },
	};

	private bool InvertOf(int row) => row switch {
		0 => _draft.InvertStickX,
		1 => _draft.InvertStickY,
		2 => _draft.InvertThrottle,
		_ => _draft.InvertRudder,
	};

	private JoystickDeviceMap WithInvert(int row, bool invert) => row switch {
		0 => _draft with { InvertStickX = invert },
		1 => _draft with { InvertStickY = invert },
		2 => _draft with { InvertThrottle = invert },
		_ => _draft with { InvertRudder = invert },
	};

	private static int ValueOf(JoystickReading reading, int row) => row switch {
		0 => reading.StickX,
		1 => reading.StickY,
		2 => reading.Throttle,
		_ => reading.Rudder,
	};

	private JoystickButtonSource ButtonOf(int index) =>
		index < _draft.Buttons.Count ? _draft.Buttons[index] : JoystickButtonSource.Unmapped;

	// The map with row's button source changed: one of BUTTON 1-8, or the radar toggle.
	private JoystickDeviceMap WithSource(int row, JoystickButtonSource source) =>
		row == RadarRow ? _draft with { RadarToggle = source } : WithButton(row - FirstButtonRow, source);

	private JoystickDeviceMap WithButton(int index, JoystickButtonSource button) {
		var buttons = new JoystickButtonSource[JoystickCapabilities.MaxButtons];
		for (int i = 0; i < buttons.Length; i++) {
			buttons[i] = i == index ? button : ButtonOf(i);
		}

		return _draft with { Buttons = buttons };
	}

	// The hat's directions as words, or "centred".
	private string HatText(JoystickHat hat) {
		if (hat == JoystickHat.None) {
			return Text("controller.centred");
		}

		var parts = new List<string>(2);
		if (hat.HasFlag(JoystickHat.North)) {
			parts.Add(Text("controller.up"));
		}
		if (hat.HasFlag(JoystickHat.South)) {
			parts.Add(Text("controller.down"));
		}
		if (hat.HasFlag(JoystickHat.West)) {
			parts.Add(Text("controller.left"));
		}
		if (hat.HasFlag(JoystickHat.East)) {
			parts.Add(Text("controller.right"));
		}

		return string.Join(" + ", parts);
	}

	private static string DeviceLabel(ControllerDevice device) => $"{device.Slot}: {device.Name}";

	private static void Lit(bool lit, string text) {
		if (lit) {
			ImGui.TextColored(LitColour, text);
		} else {
			ImGui.TextDisabled(text);
		}
	}

	// Shown over a greyed item too, whose tooltip says why it is greyed.
	private void Tooltip(string key) {
		if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ScaledImGui.Tooltip(Text(key));
		}
	}

	private string Text(string key) => _localization.GetStringOrKey(key);

	private string Format(string key, int value) => string.Format(Text(key), value);
}
