using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Herculan.Engine.Input;
using Herculan.Engine.Localization;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Menus;

/// <summary>
/// The <see cref="ControllerWindow"/>'s view of a device in <see cref="ControllerMode.Custom"/>: which device axis drives
/// each game function, how the throttle axis reads, and which action each button, axis end and hat direction sends, for a
/// HERC and, in a game with one (<see cref="ControllerRows.HasRazor"/>), a RAZOR. It holds the draft of one device's mode
/// and <see cref="ControllerProfile"/>, which the window loads and saves through <see cref="JoystickMapFile"/>.
/// <b>This engine's own</b> (docs/herculan/plan-controller-profiles.md); what the player sees is
/// docs/herculan/joystick-config.md, "Setting up a stick".
///
/// <para>A device whose section has no Custom settings shows its default profile (<see cref="ControllerProfile.ForDevice"/>)
/// and is saved with none until something is changed, so it goes on taking the default. A joystick reports no counts until
/// a frame after it connects, so its default is rebuilt while the counts change.</para>
/// </summary>
internal sealed class ControllerCustomView {
	// How far an axis has to move from where it rested to be the one Detect takes, as in ControllerWindow.
	private const float DetectTravel = 0.5f;

	// A gamepad's d-pad buttons, which are also its hat 0: the list offers the hat alone, so one control has one name.
	private const int FirstDPadButton = ControllerDevice.GamepadDPadUp;
	private const int LastDPadButton = ControllerDevice.GamepadDPadLeft;

	private static readonly Vector4 ErrorColour = new(1f, 0.45f, 0.4f, 1f);
	private static readonly JoystickHat[] Cardinals = [JoystickHat.North, JoystickHat.East, JoystickHat.South, JoystickHat.West];

	private static readonly ImmutableArray<ThrottleReading> HercReadings = [ThrottleReading.Rate, ThrottleReading.Lever, ThrottleReading.Bipolar];
	private static readonly ImmutableArray<ThrottleReading> RazorReadings = [ThrottleReading.Rate, ThrottleReading.Lever];

	private enum Machine {
		Herc,
		Razor,
	}

	// One row of a machine's list. A row the player has added may have no input or no action yet; it sends nothing, and
	// is not saved, until it has both.
	private sealed class Binding {
		public ControllerInput? Input;
		public KeyAction? Action;
	}

	private readonly LocalizationTable _localization;
	private readonly ControllerRows _rows;

	// The mode and the profile as the file had them, the profile null where the device's default applies.
	private ControllerMode _loadedMode;
	private ControllerProfile? _loadedProfile;

	// Whether the draft differs from the profile loaded, and whether it is the device's default, saved as no Custom
	// settings; _defaultCounts are the device counts that default was built for.
	private bool _edited;
	private bool _isDefault;
	private (int Axes, int Buttons, int Hats) _defaultCounts;

	private Dictionary<AxisFunction, ControllerAxis> _axes = [];
	private ThrottleReading _throttleReading;
	private ThrottleReading _razorThrottleReading;
	private readonly List<Binding>[] _bindings = [[], []];

	// Which actions the action lists show, "Common" each time a device is loaded.
	private ControllerActionList _actionList;

	// What Detect is listening for, and where the device's controls stood when it started.
	private AxisFunction? _detectingAxis;
	private Binding? _detectingBinding;
	private float[] _restingAxes = [];
	private bool[] _restingButtons = [];
	private JoystickHat[] _restingHats = [];

	public ControllerCustomView(LocalizationTable localization, ControllerRows rows) {
		_localization = localization;
		_rows = rows;
	}

	/// <summary>The device's mode as it will be saved.</summary>
	public ControllerMode Mode { get; set; }

	/// <summary>Whether Detect is listening for a control.</summary>
	public bool Listening => _detectingAxis != null || _detectingBinding != null;

	/// <summary>Whether the mode or the profile differs from what <see cref="Load"/> read.</summary>
	public bool Changed => _edited || Mode != _loadedMode;

	/// <summary>Stops Detect.</summary>
	public void StopDetecting() {
		_detectingAxis = null;
		_detectingBinding = null;
	}

	/// <summary>Starts editing <paramref name="device"/>'s mode and profile as <paramref name="file"/> holds them.</summary>
	public void Load(JoystickMapFile file, ControllerDevice? device) {
		StopDetecting();
		Mode = _loadedMode = device == null ? ControllerMode.Custom : file.ModeFor(device.IsGamepad, device.Guid);
		_loadedProfile = device == null ? null : file.ProfileFor(device.IsGamepad, device.Guid);
		_edited = false;
		_isDefault = _loadedProfile == null;
		_actionList = ControllerActionList.Common;
		Fill(_loadedProfile ?? Default(device));
	}

	/// <summary>
	/// Puts the mode and profile into <paramref name="file"/>'s section for <paramref name="device"/>: no Custom settings
	/// while the draft is the device's default, else the draft, the rows with an input and an action.
	/// </summary>
	public void Store(JoystickMapFile file, ControllerDevice device) {
		var profile = _isDefault ? null : _edited ? Build() : _loadedProfile;
		file.SetProfile(device.IsGamepad, device.Guid, device.Name, Mode, profile);
		_loadedMode = Mode;
		_loadedProfile = profile;
		_edited = false;
	}

	/// <summary>Puts back the device's default profile, keeping the mode.</summary>
	public void Defaults(ControllerDevice? device) {
		StopDetecting();
		Fill(Default(device));
		_isDefault = true;
		_edited = true;
	}

	/// <summary>Draws the view for <paramref name="device"/>, inside the window, and binds what Detect is listening for.</summary>
	public void Draw(ControllerDevice device) {
		if (_isDefault && !device.IsGamepad && Counts(device) != _defaultCounts) {
			Fill(Default(device));
		}

		Detect(device);

		if (!_rows.HasRazor) {
			MachineView(Machine.Herc, device);
			return;
		}

		if (ImGui.BeginTabBar("##machine")) {
			foreach (var machine in (ReadOnlySpan<Machine>)[Machine.Herc, Machine.Razor]) {
				if (ImGui.BeginTabItem(Text(machine == Machine.Herc ? "controller.herc" : "controller.razor") + "###" + machine)) {
					MachineView(machine, device);
					ImGui.EndTabItem();
				}
			}

			ImGui.EndTabBar();
		}
	}

	private void MachineView(Machine machine, ControllerDevice device) {
		var clashes = Clashes(machine);
		ImGui.SeparatorText(Text("controller.axes"));
		AxisRows(machine, device, clashes);
		ThrottleReadingCombo(machine);
		ImGui.SeparatorText(Text("controller.actions"));
		ActionListCombo();
		BindingRows(machine, device, clashes);
	}

	// "Common" or "All", for both machines' action lists.
	private void ActionListCombo() {
		string Label(ControllerActionList list) =>
			Text(list == ControllerActionList.Common ? "controller.action_list_common" : "controller.action_list_all");

		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
		if (ImGui.BeginCombo(Text("controller.action_list") + "###actionList", Label(_actionList))) {
			foreach (var list in (ReadOnlySpan<ControllerActionList>)[ControllerActionList.Common, ControllerActionList.All]) {
				if (ImGui.Selectable(Label(list), list == _actionList)) {
					_actionList = list;
				}
			}

			ImGui.EndCombo();
		}
		Tooltip("controller.action_list_tip");
	}

	// One row per game function: the device axis driving it, its invert and deadzone, Detect, and where the axis stands.
	private void AxisRows(Machine machine, ControllerDevice device, Dictionary<object, string> clashes) {
		if (!ImGui.BeginTable("##axes", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) {
			return;
		}

		ImGui.TableSetupColumn(Text("controller.function"), ImGuiTableColumnFlags.WidthStretch, 1f);
		ImGui.TableSetupColumn(Text("controller.input"), ImGuiTableColumnFlags.WidthStretch, 0.8f);
		ImGui.TableSetupColumn(Text("controller.invert"), ImGuiTableColumnFlags.WidthFixed);
		ImGui.TableSetupColumn(Text("controller.deadzone_column"), ImGuiTableColumnFlags.WidthStretch, 0.8f);
		ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
		ImGui.TableSetupColumn(Text("controller.position"), ImGuiTableColumnFlags.WidthStretch, 0.7f);
		ImGui.TableHeadersRow();

		foreach (var function in Functions(machine)) {
			var axis = _axes.GetValueOrDefault(function, ControllerAxis.Unbound);
			ImGui.PushID((int)function);
			ImGui.TableNextRow();
			ImGui.TableNextColumn();
			ImGui.TextUnformatted(FunctionLabel(function));

			ImGui.TableNextColumn();
			if (AxisCombo(axis.Index, device, clashes.GetValueOrDefault(function)) is { } index) {
				SetAxis(function, axis with { Index = index });
			}

			ImGui.TableNextColumn();
			bool invert = axis.Invert;
			ImGui.BeginDisabled(!axis.IsBound);
			if (ImGui.Checkbox("##invert", ref invert)) {
				SetAxis(function, axis with { Invert = invert });
			}

			ImGui.TableNextColumn();
			int deadzone = axis.Deadzone;
			ImGui.SetNextItemWidth(-1f);
			if (ImGui.SliderInt("##deadzone", ref deadzone, 0, JoystickReading.RawFull - 1, "%d", ImGuiSliderFlags.AlwaysClamp)) {
				SetAxis(function, axis with { Deadzone = deadzone });
			}
			ImGui.EndDisabled();
			Tooltip("controller.deadzone_tip");

			ImGui.TableNextColumn();
			DetectButton(_detectingAxis == function, "controller.detect_axis", () => StartDetecting(device, function, null));

			ImGui.TableNextColumn();
			float position = Position(device, axis);
			ImGui.ProgressBar((position + 1f) / 2f, new Vector2(-1f, 0f), position.ToString("+0.00;-0.00", CultureInfo.InvariantCulture));
			ImGui.PopID();
		}

		ImGui.EndTable();
	}

	// Where an axis stands for its function, −1 to +1: inverted as set, and 0 inside its deadzone.
	private static float Position(ControllerDevice device, ControllerAxis axis) {
		if (!axis.IsBound) {
			return 0f;
		}

		float position = Math.Clamp(device.Axis(axis.Index), -1f, 1f) * (axis.Invert ? -1f : 1f);
		return MathF.Abs(position) * JoystickReading.RawFull <= axis.Deadzone ? 0f : position;
	}

	// How the machine's throttle axis is read.
	private void ThrottleReadingCombo(Machine machine) {
		var current = machine == Machine.Herc ? _throttleReading : _razorThrottleReading;
		ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.6f);
		if (ImGui.BeginCombo(Text("controller.throttle_reading") + "###reading", ReadingLabel(current))) {
			foreach (var reading in machine == Machine.Herc ? HercReadings : RazorReadings) {
				if (ImGui.Selectable(ReadingLabel(reading), reading == current) && reading != current) {
					if (machine == Machine.Herc) {
						_throttleReading = reading;
					} else {
						_razorThrottleReading = reading;
					}

					Touch();
				}
			}

			ImGui.EndCombo();
		}
		Tooltip("controller.throttle_reading_tip");
	}

	private string ReadingLabel(ThrottleReading reading) => Text(reading switch {
		ThrottleReading.Rate => "controller.reading_rate",
		ThrottleReading.Lever => "controller.reading_lever",
		_ => "controller.reading_bipolar",
	});

	// One row per binding: its device input, its action, Detect and Remove; then Add.
	private void BindingRows(Machine machine, ControllerDevice device, Dictionary<object, string> clashes) {
		var bindings = _bindings[(int)machine];
		Binding? removed = null;
		if (bindings.Count > 0 && ImGui.BeginTable("##bindings", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg)) {
			ImGui.TableSetupColumn(Text("controller.input"), ImGuiTableColumnFlags.WidthStretch, 0.8f);
			ImGui.TableSetupColumn(Text("controller.action"), ImGuiTableColumnFlags.WidthStretch, 1.6f);
			ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableHeadersRow();

			for (int i = 0; i < bindings.Count; i++) {
				var binding = bindings[i];
				ImGui.PushID(i);
				ImGui.TableNextRow();
				ImGui.TableNextColumn();
				if (device.IsGamepad && binding.Input == ControllerProfile.GamepadMenuBarInput) {
					MenuBarRow(device);
					ImGui.PopID();
					continue;
				}

				if (InputCombo(binding.Input, device, clashes.GetValueOrDefault(binding)) is { } input) {
					binding.Input = input.IsMapped ? input.Input : null;
					Touch();
				}

				ImGui.TableNextColumn();
				if (ActionCombo(binding.Action, machine, device) is { } action) {
					binding.Action = action.IsMapped ? action.Action : null;
					Touch();
				}

				ImGui.TableNextColumn();
				DetectButton(_detectingBinding == binding, "controller.detect_input", () => StartDetecting(device, null, binding));

				ImGui.TableNextColumn();
				if (ImGui.Button(Text("controller.remove") + "##remove")) {
					removed = binding;
				}

				ImGui.PopID();
			}

			ImGui.EndTable();
		}

		if (removed != null) {
			bindings.Remove(removed);
			if (_detectingBinding == removed) {
				StopDetecting();
			}

			Touch();
		}

		if (ImGui.Button(Text("controller.add") + "##add")) {
			var added = new Binding();
			bindings.Add(added);
			StartDetecting(device, null, added);
			Touch();
		}
		Tooltip("controller.add_tip");
	}

	// A gamepad's Select/Back and the menu bar, which every gamepad profile has (JoystickMapFile.ProfileFor): shown, not
	// edited, with no Detect or Remove.
	private void MenuBarRow(ControllerDevice device) {
		string input = InputLabel(ControllerProfile.GamepadMenuBarInput, device);
		ImGui.AlignTextToFramePadding();
		ImGui.TextDisabled(input);
		Tooltip(input);
		ImGui.TableNextColumn();
		ImGui.AlignTextToFramePadding();
		ImGui.TextDisabled(ActionLabel(KeyAction.MenuBar));
		Tooltip(input);

		void Tooltip(string name) {
			if (ImGui.IsItemHovered()) {
				ScaledImGui.Tooltip(string.Format(Text("controller.menu_bar_input"), name));
			}
		}
	}

	// A choice from a combo: Mapped false for "None".
	private readonly record struct InputChoice(bool IsMapped, ControllerInput Input);

	private readonly record struct ActionChoice(bool IsMapped, KeyAction Action);

	// A combo of "None" and every input the device has: its buttons, but a gamepad's d-pad, which is its hat, and its Back,
	// which opens the menu bar (HostMenuBar.RaiseOrBackOut); each axis toward either end; and each hat's four directions. The input
	// chosen, or null when nothing new was.
	private InputChoice? InputCombo(ControllerInput? current, ControllerDevice device, string? clash) {
		InputChoice? chosen = null;
		if (BeginRedCombo("##input", current is { } shown ? InputLabel(shown, device) : Text("controller.unmapped"), clash)) {
			if (ImGui.Selectable(Text("controller.unmapped"), current == null)) {
				chosen = new InputChoice(false, default);
			}

			foreach (var input in InputsOf(device)) {
				if (ImGui.Selectable(InputLabel(input, device), input == current)) {
					chosen = new InputChoice(true, input);
				}
			}

			ImGui.EndCombo();
		}

		bool same = chosen is { } choice && (choice.IsMapped ? choice.Input == current : current == null);
		return same ? null : chosen;
	}

	private static IEnumerable<ControllerInput> InputsOf(ControllerDevice device) {
		for (int i = 0; i < device.ButtonCount; i++) {
			if (!IsDPadButton(device, i) && !(device.IsGamepad && i == ControllerDevice.GamepadBack)) {
				yield return ControllerInput.Button(i);
			}
		}

		for (int i = 0; i < device.AxisCount; i++) {
			yield return ControllerInput.Axis(i, 1);
			yield return ControllerInput.Axis(i, -1);
		}

		for (int i = 0; i < device.HatCount; i++) {
			foreach (var direction in Cardinals) {
				yield return ControllerInput.Hat(i, direction);
			}
		}
	}

	private static bool IsDPadButton(ControllerDevice device, int button) =>
		device.IsGamepad && button is >= FirstDPadButton and <= LastDPadButton;

	/// <summary>An input's name on <paramref name="device"/> as the window shows it, a known gamepad's buttons by the names printed on them.</summary>
	internal string InputLabel(ControllerInput input, ControllerDevice? device) => input.Kind switch {
		ControllerInputKind.Button => GamepadControlNames.Label(device, input.Index, Text),
		ControllerInputKind.Axis => GamepadControlNames.AxisDirectionLabel(device, input.Index, input.AxisSign, Text),
		_ => GamepadControlNames.HatLabel(device, input.Index, input.Direction, Text),
	};

	// A combo of "None" and the machine's listed actions (ControllerActions.Listed) under their sections' headings, an
	// action the game lacks greyed, and for a gamepad without the menu bar, which is its Back's (MenuBarRow). An action bound already shows as itself, listed or not. The action chosen, or null
	// when nothing new was.
	private ActionChoice? ActionCombo(KeyAction? current, Machine machine, ControllerDevice device) {
		ActionChoice? chosen = null;
		ImGui.SetNextItemWidth(-1f);
		bool lacking = current is { } shown && !_rows.Actions.Contains(shown);
		if (lacking) {
			ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
		}

		bool open = ImGui.BeginCombo("##action", current is { } action ? ActionLabel(action) : Text("controller.unmapped"),
			ImGuiComboFlags.HeightLarge);
		if (lacking) {
			ImGui.PopStyleColor();
			if (!open) {
				Tooltip("controller.action_other_game_tip");
			}
		}

		if (open) {
			if (ImGui.Selectable(Text("controller.unmapped"), current == null)) {
				chosen = new ActionChoice(false, default);
			}

			KeySection? section = null;
			foreach (var (entrySection, entry) in ControllerActions.Listed(_actionList, machine == Machine.Razor, device.IsGamepad)) {
				if (device.IsGamepad && entry == KeyAction.MenuBar) {
					continue;
				}

				if (entrySection != section) {
					section = entrySection;
					ImGui.SeparatorText(Text(KeysWindow.SectionEntry(entrySection)));
				}

				bool other = !_rows.Actions.Contains(entry);
				if (other) {
					ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
				}

				if (ImGui.Selectable(ActionLabel(entry) + "###" + entry, entry == current)) {
					chosen = new ActionChoice(true, entry);
				}

				if (other) {
					ImGui.PopStyleColor();
					Tooltip("controller.action_other_game_tip");
				}
			}

			ImGui.EndCombo();
		}

		bool same = chosen is { } choice && (choice.IsMapped ? choice.Action == current : current == null);
		return same ? null : chosen;
	}

	private string ActionLabel(KeyAction action) => Text(ControllerActions.LabelKey(action));

	private string AxisLabel(int axis, ControllerDevice device) => GamepadControlNames.AxisLabel(device, axis, Text);

	// A combo of "None" and each device axis; the axis chosen, or null when nothing new was. An index the device does not
	// have, from a file written for another one, shows as itself until it is changed.
	private int? AxisCombo(int current, ControllerDevice device, string? clash) {
		int? chosen = null;
		if (BeginRedCombo("##axis", current == JoystickDeviceMap.Unmapped ? Text("controller.unmapped") : AxisLabel(current, device), clash)) {
			if (ImGui.Selectable(Text("controller.unmapped"), current == JoystickDeviceMap.Unmapped)) {
				chosen = JoystickDeviceMap.Unmapped;
			}

			for (int i = 0; i < device.AxisCount; i++) {
				if (ImGui.Selectable(AxisLabel(i, device) + "###axis" + i, i == current)) {
					chosen = i;
				}
			}

			ImGui.EndCombo();
		}

		return chosen == current ? null : chosen;
	}

	// Opens a full-width combo on preview. While clash names what else the same input drives, the preview is red and
	// hovering it says what.
	private static bool BeginRedCombo(string id, string preview, string? clash) {
		ImGui.SetNextItemWidth(-1f);
		if (clash != null) {
			ImGui.PushStyleColor(ImGuiCol.Text, ErrorColour);
		}

		bool open = ImGui.BeginCombo(id, preview);
		if (clash != null) {
			ImGui.PopStyleColor();
			if (!open && ImGui.IsItemHovered()) {
				ScaledImGui.Tooltip(clash);
			}
		}

		return open;
	}

	// For each function and binding of the machine whose input something else of the machine also uses, a sentence naming
	// the others: two functions on one axis, a binding on either end of a function's axis, or two bindings on one input. A
	// row with no action sends nothing, so it clashes with nothing.
	private Dictionary<object, string> Clashes(Machine machine) {
		var entries = new List<(object Key, string Name, int Axis, ControllerInput? Input)>();
		foreach (var function in Functions(machine)) {
			if (_axes.GetValueOrDefault(function, ControllerAxis.Unbound) is { IsBound: true } axis) {
				entries.Add((function, FunctionLabel(function), axis.Index, null));
			}
		}

		foreach (var binding in _bindings[(int)machine]) {
			if (binding is { Input: { } input, Action: { } action }) {
				entries.Add((binding, ActionLabel(action), input.Kind == ControllerInputKind.Axis ? input.Index : -1, input));
			}
		}

		var clashes = new Dictionary<object, string>();
		foreach (var entry in entries) {
			var others = entries.Where(other => !ReferenceEquals(other.Key, entry.Key) && Shares(entry, other))
				.Select(other => other.Name).ToList();
			if (others.Count > 0) {
				clashes[entry.Key] = string.Format(Text("controller.also_drives"), string.Join(", ", others));
			}
		}

		return clashes;

		// Two bindings share an input when it is the same one; a function shares its axis with anything on that axis.
		static bool Shares((object Key, string Name, int Axis, ControllerInput? Input) a, (object Key, string Name, int Axis, ControllerInput? Input) b) =>
			a.Input is { } first && b.Input is { } second ? first == second : a.Axis >= 0 && a.Axis == b.Axis;
	}

	// Detect, or Cancel while this row is listening.
	private void DetectButton(bool listening, string tipKey, Action start) {
		if (ImGui.Button(Text(listening ? "controller.detecting" : "controller.detect") + "##detect")) {
			if (listening) {
				StopDetecting();
			} else {
				start();
			}
		}

		if (listening && ImGui.IsItemHovered()) {
			ScaledImGui.Tooltip(Text(tipKey));
		}
	}

	// Notes where everything rests, so Detect takes what moves from there and not what is already held or off-centre.
	private void StartDetecting(ControllerDevice device, AxisFunction? function, Binding? binding) {
		_detectingAxis = function;
		_detectingBinding = binding;
		_restingAxes = Enumerable.Range(0, device.AxisCount).Select(device.Axis).ToArray();
		_restingButtons = Enumerable.Range(0, device.ButtonCount).Select(device.Button).ToArray();
		_restingHats = Enumerable.Range(0, device.HatCount).Select(device.Hat).ToArray();
	}

	// Binds what is listening to whatever has moved since Detect was pressed: for a function, an axis moved half its
	// travel; for a binding, a hat pushed in a new direction, then a new button press, then an axis moved half its travel
	// toward the end it moved to. The hat goes first so a gamepad's d-pad, which is also four buttons, is taken as its hat.
	private void Detect(ControllerDevice device) {
		if (_detectingAxis is { } function) {
			for (int i = 0; i < device.AxisCount && i < _restingAxes.Length; i++) {
				if (MathF.Abs(device.Axis(i) - _restingAxes[i]) > DetectTravel) {
					SetAxis(function, _axes.GetValueOrDefault(function, ControllerAxis.Unbound) with { Index = i });
					StopDetecting();
					return;
				}
			}

			return;
		}

		if (_detectingBinding is not { } binding) {
			return;
		}

		ControllerInput? input = null;
		for (int i = 0; i < device.HatCount && i < _restingHats.Length && input == null; i++) {
			var pushed = device.Hat(i) & ~_restingHats[i];
			foreach (var direction in Cardinals) {
				if ((pushed & direction) != 0) {
					input = ControllerInput.Hat(i, direction);
					break;
				}
			}
		}

		for (int i = 0; i < device.ButtonCount && i < _restingButtons.Length && input == null; i++) {
			if (device.Button(i) && !_restingButtons[i] && !IsDPadButton(device, i)
					&& !(device.IsGamepad && i == ControllerDevice.GamepadBack)) {
				input = ControllerInput.Button(i);
			}
		}

		for (int i = 0; i < device.AxisCount && i < _restingAxes.Length && input == null; i++) {
			float moved = device.Axis(i) - _restingAxes[i];
			if (MathF.Abs(moved) > DetectTravel) {
				input = ControllerInput.Axis(i, moved > 0 ? 1 : -1);
			}
		}

		if (input != null) {
			binding.Input = input;
			StopDetecting();
			Touch();
		}
	}

	private void SetAxis(AxisFunction function, ControllerAxis axis) {
		if (axis.IsBound) {
			_axes[function] = axis;
		} else {
			_axes.Remove(function);
		}

		Touch();
	}

	// The draft has been changed by hand, so it is saved as it stands rather than as the device's default.
	private void Touch() {
		_edited = true;
		_isDefault = false;
	}

	private ControllerProfile Default(ControllerDevice? device) {
		if (device == null) {
			_defaultCounts = default;
			return new ControllerProfile();
		}

		_defaultCounts = Counts(device);
		return ControllerProfile.ForDevice(device.IsGamepad, device.AxisCount, device.ButtonCount, device.HatCount);
	}

	private static (int, int, int) Counts(ControllerDevice device) => (device.AxisCount, device.ButtonCount, device.HatCount);

	// Shows profile, its bindings in ControllerInput's order.
	private void Fill(ControllerProfile profile) {
		_axes = new Dictionary<AxisFunction, ControllerAxis>(profile.Axes);
		_throttleReading = profile.ThrottleReading;
		_razorThrottleReading = profile.RazorThrottleReading;
		FillBindings(Machine.Herc, profile.HercBindings);
		FillBindings(Machine.Razor, profile.RazorBindings);

		void FillBindings(Machine machine, ImmutableDictionary<ControllerInput, KeyAction> bindings) {
			_bindings[(int)machine].Clear();
			_bindings[(int)machine].AddRange(bindings.OrderBy(entry => entry.Key)
				.Select(entry => new Binding { Input = entry.Key, Action = entry.Value }));
		}
	}

	// The draft as a profile: the rows with an input and an action, the first row on an input winning.
	private ControllerProfile Build() => new() {
		Axes = _axes.ToImmutableDictionary(),
		ThrottleReading = _throttleReading,
		RazorThrottleReading = _razorThrottleReading,
		HercBindings = Bindings(Machine.Herc),
		RazorBindings = Bindings(Machine.Razor),
	};

	private ImmutableDictionary<ControllerInput, KeyAction> Bindings(Machine machine) {
		var bindings = new Dictionary<ControllerInput, KeyAction>();
		foreach (var binding in _bindings[(int)machine]) {
			if (binding is { Input: { } input, Action: { } action }) {
				bindings.TryAdd(input, action);
			}
		}

		return bindings.ToImmutableDictionary();
	}

	private static ImmutableArray<AxisFunction> Functions(Machine machine) =>
		machine == Machine.Herc ? ControllerProfile.HercFunctions : ControllerProfile.RazorFunctions;

	private string FunctionLabel(AxisFunction function) => Text(function switch {
		AxisFunction.Steer => "controller.function.steer",
		AxisFunction.Throttle => "controller.function.throttle",
		AxisFunction.TurretTwist => "controller.function.turret_twist",
		AxisFunction.TurretPitch => "controller.function.turret_pitch",
		AxisFunction.RazorRoll => "controller.function.razor_roll",
		AxisFunction.RazorPitch => "controller.function.razor_pitch",
		AxisFunction.RazorYaw => "controller.function.razor_yaw",
		_ => "controller.function.razor_throttle",
	});

	// Shown over a greyed item too.
	private void Tooltip(string key) {
		if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ScaledImGui.Tooltip(Text(key));
		}
	}

	private string Text(string key) => _localization.GetStringOrKey(key);
}
