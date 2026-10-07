using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;
using Herculan.Engine.Input;
namespace Herculan.Engine.Cockpit;

/// <summary>
/// The CONTROLS panel — <c>ctl_alrt</c> (<c>ControlsPanel_Ctor</c>, <c>00457d1c</c>), what the [F12]
/// preferences panel's CONTROLS button raises. Four joystick axis assignments and one action per
/// joystick button, with the action list of the selected button shown alongside.
///
/// <para>This type is the panel's text and its state; its geometry is
/// <see cref="ControlsPanelLayout"/> and its pixels are drawn by
/// <see cref="Render.Cockpit.AlertPanelPainter.DrawControlsPanel"/>.</para>
///
/// <para><b>It is two panels in one file.</b> <c>str\CTL_ALRT.STR</c> carries thirteen groups, and
/// the constructor reads eight of them — title, the fourteen captions, the twenty-one action names,
/// the OPTIONS caption, then a three-word set for each axis row. When <c>PilotingRazor</c> (<c>004d25f5</c>) is set it
/// then reads five <i>more</i> and overwrites the title and all four axis sets with them, which is
/// how the same panel becomes RAZOR CONTROLS with flight words (PITCH / ROLL, YAW) in place of the
/// walker's. That flag is set by <c>DBSim_LoadScriptDat</c> as
/// <c>playerMechType == 8</c> — <see cref="RazorTypeIndex"/>, the RAZOR — and nothing else writes
/// it.</para>
///
/// <para><b>The bindings are twelve bytes of <c>data\prefs.cfg</c></b>, at
/// <see cref="Prefs.HercControlsBase"/> for a walker and
/// <see cref="Prefs.RazorControlsBase"/> for the RAZOR — a machine's set is its own.
/// An axis row's byte indexes its three-word set directly; a button row's byte is an action code
/// into the twenty-one names, also directly. See <see cref="ActionsFor"/> for the separate table
/// that says which of those actions a given button is <i>allowed</i>.</para>
/// </summary>
public sealed class ControlsPanel {
	/// <summary>The panel's own string table.</summary>
	public const string StringsFileName = "CTL_ALRT.STR";

	/// <summary>The <see cref="HercWorks.Core.Data.Struct.Herc.HercLUT"/> id whose machine flips the panel to its flight half.</summary>
	public const int RazorTypeIndex = 8;

	/// <summary>Group 0 — <c>HERC CONTROLS</c>.</summary>
	private const int TitleGroup = 0;

	/// <summary>Group 1 — the fourteen button captions, in widget order.</summary>
	private const int CaptionGroup = 1;

	/// <summary>Group 2 — the twenty-one action names a button row can be bound to, indexed by action code.</summary>
	private const int ActionGroup = 2;

	/// <summary>Group 3 — <c>OPTIONS</c>, the list's own caption.</summary>
	private const int OptionsCaptionGroup = 3;

	/// <summary>Groups 4-7 — three words each for JOYSTICK, THROTTLE, RUDDER and HAT.</summary>
	private const int FirstAxisGroup = 4;

	/// <summary>Group 8 — <c>RAZOR CONTROLS</c>, which replaces the title on the flight half.</summary>
	private const int RazorTitleGroup = 8;

	/// <summary>Groups 9-12 — the flight half's four axis word sets.</summary>
	private const int FirstRazorAxisGroup = 9;

	/// <summary>How many words an axis row can show.</summary>
	private const int AxisWordCount = 3;

	/// <summary>
	/// The action codes each of the eight button rows may be bound to — <c>DAT_0049e619</c> for a
	/// walker, <c>DAT_0049e681</c> for the RAZOR, eight rows of thirteen bytes each, read by
	/// <c>ControlsPanel_ActionAt(row, slot)</c> (<c>00457cdc</c>).
	///
	/// <para><b>Code 0 terminates a row rather than meaning OFF.</b> The constructor counts a row's
	/// length by walking until it reads a zero, so although <c>OFF</c> is action name 0 it is never
	/// an offered choice; it is only what a row <i>displays</i> when its stored byte is 0. BUTTON 1
	/// is the extreme case: one entry, FIRE, and nothing else — the trigger cannot be rebound.</para>
	///
	/// <para>Rows alternate between two lists, one carrying NEXT WEAPON and the other PREV WEAPON,
	/// which is why the retail recommended set writes NEXT WEAPON into BUTTON 6 even though that row
	/// does not offer it. Nothing rejects the write and the readout shows it, so a retail install can
	/// sit on a binding its own option list cannot reach.</para>
	/// </summary>
	private static readonly byte[][] HercButtonActions = {
		new byte[] { 0x01 },
		new byte[] { 0x02, 0x08, 0x04, 0x03, 0x06, 0x0d, 0x10, 0x12, 0x13, 0x09, 0x0a, 0x05 },
		new byte[] { 0x02, 0x08, 0x04, 0x03, 0x06, 0x0d, 0x10, 0x12, 0x13, 0x09, 0x07, 0x05 },
		new byte[] { 0x02, 0x03, 0x06, 0x0d, 0x10, 0x12, 0x14, 0x0a, 0x07, 0x05, 0x0b, 0x0e },
		new byte[] { 0x06, 0x07, 0x0b, 0x0c, 0x0e, 0x04, 0x03, 0x10, 0x12, 0x13, 0x09, 0x0a },
		new byte[] { 0x0d, 0x08, 0x0b, 0x0c, 0x0e, 0x04, 0x03, 0x10, 0x12, 0x14, 0x09, 0x0a },
		new byte[] { 0x06, 0x07, 0x0b, 0x0c, 0x0e, 0x04, 0x03, 0x10, 0x12, 0x13, 0x09, 0x0a },
		new byte[] { 0x0d, 0x08, 0x0b, 0x0c, 0x0e, 0x04, 0x03, 0x10, 0x12, 0x14, 0x09, 0x0a },
	};

	/// <inheritdoc cref="HercButtonActions"/>
	private static readonly byte[][] RazorButtonActions = {
		new byte[] { 0x01 },
		new byte[] { 0x02, 0x08, 0x0d, 0x10, 0x12, 0x13, 0x09, 0x0a, 0x0b, 0x0c, 0x0e, 0x0f },
		new byte[] { 0x02, 0x08, 0x0d, 0x10, 0x12, 0x14, 0x09, 0x0a, 0x0b, 0x0c, 0x0e, 0x0f },
		new byte[] { 0x02, 0x08, 0x0d, 0x10, 0x12, 0x13, 0x09, 0x0a, 0x0b, 0x0c, 0x0e, 0x0f },
		new byte[] { 0x02, 0x08, 0x0d, 0x10, 0x12, 0x14, 0x09, 0x0a, 0x0b, 0x0c, 0x0e, 0x0f },
		new byte[] { 0x02, 0x08, 0x0d, 0x10, 0x12, 0x13, 0x09, 0x0a, 0x0b, 0x0c, 0x0e, 0x0f },
		new byte[] { 0x02, 0x08, 0x0d, 0x10, 0x12, 0x14, 0x09, 0x0a, 0x0b, 0x0c, 0x0e, 0x0f },
		new byte[] { 0x02, 0x08, 0x0d, 0x10, 0x12, 0x13, 0x09, 0x0a, 0x0b, 0x0c, 0x0e, 0x0f },
	};

	/// <summary>
	/// What RECOMMEND writes over all twelve options — <c>Input_RecommendedBindings</c> (<c>0045a258</c>), which returns a
	/// twelve-byte block it fills in place: the four axis modes, then one action code per button.
	/// The walker's set.
	///
	/// <para>That function also carries an arm that zeroes the block instead, taken when the first
	/// word of the capability block is 0. Nothing here reproduces it: the word is written
	/// unconditionally as 1 or 2 by <c>Input_QueryCapabilities</c> (<c>004777f8</c>), the only thing that fills that block, so the
	/// arm cannot be reached through its own input. With no stick it is not reached at all: RECOMMEND stays
	/// live, but the action switch that would run it needs a capability block (see <see cref="Click"/>).</para>
	/// </summary>
	private static readonly byte[] HercRecommended =
		{ 1, 0, 2, 2, 0x01, 0x02, 0x04, 0x06, 0x12, 0x13, 0x09, 0x0a };

	/// <inheritdoc cref="HercRecommended"/>
	private static readonly byte[] RazorRecommended =
		{ 1, 0, 2, 2, 0x01, 0x02, 0x0d, 0x0e, 0x12, 0x13, 0x09, 0x0a };

	private readonly string[] _captions;
	private readonly string[] _values;
	private readonly string[] _actionNames;
	private readonly byte[][] _buttonActions;
	private readonly StringFile _strings;
	private readonly SimulatorPreferences _preferences;
	private readonly int _optionBase;
	private readonly int _firstAxisGroup;

	private ControlsPanel(StringFile strings, SimulatorPreferences preferences, string title,
			string optionsCaption, string[] captions, string[] actionNames, byte[][] buttonActions,
			JoystickCapabilities capabilities, bool razor) {
		_strings = strings;
		_preferences = preferences;
		_optionBase = Prefs.ControlsBase(razor);
		_firstAxisGroup = razor ? FirstRazorAxisGroup : FirstAxisGroup;
		Title = title;
		OptionsCaption = optionsCaption;
		_captions = captions;
		_values = new string[ControlsPanelLayout.RowCount];
		_actionNames = actionNames;
		_buttonActions = buttonActions;
		// The field, not the property: the setter refreshes, and _values has to exist first.
		_capabilities = capabilities;
		IsRazor = razor;
		RefreshValues();
	}

	/// <summary>The panel's title — <c>HERC CONTROLS</c>, or <c>RAZOR CONTROLS</c> on the flight half.</summary>
	public string Title { get; }

	/// <summary>The OPTIONS list's own caption, drawn in the title font above the list.</summary>
	public string OptionsCaption { get; }

	/// <summary>Whether this is the flight half. Settled by the player's machine, not by the panel.</summary>
	public bool IsRazor { get; }

	/// <summary>
	/// What the input layer reports, and so which rows are live.
	///
	/// <para>Settable because the original re-reads it every time the panel goes up rather than
	/// holding what it was built with: <c>ControlsPanel_Run</c> (<c>00458650</c>) asks
	/// <c>Input_GetDevice(3)</c> (<c>0045c508</c>) and then <c>Input_QueryCapabilities</c> at the top of its own loop, and
	/// that function rebuilds its eight bytes from scratch on every call. So a stick plugged in
	/// mid-session lights the rows up.</para>
	///
	/// <para>Changing it refreshes the readouts, because a row's text is written only when its
	/// capability allows — <c>ControlsPanel_RefreshRow</c> (<c>00458d20</c>) sets none at all
	/// otherwise. Without that, a panel opened before the input layer knew what it had would keep the
	/// blanks it was refreshed with even once the rows went live.</para>
	/// </summary>
	public JoystickCapabilities Capabilities {
		get => _capabilities;
		set {
			if (_capabilities == value) {
				return;
			}

			_capabilities = value;
			RefreshValues();
		}
	}

	private JoystickCapabilities _capabilities;

	/// <summary>The fourteen button captions, in widget order.</summary>
	public IReadOnlyList<string> Captions => _captions;

	/// <summary>
	/// The twelve value readouts, in row order. A row the capabilities have greyed reads empty: the
	/// refresh (<c>ControlsPanel_RefreshRow</c>, <c>00458d20</c>) gates every one of its twelve cases on that row's capability and
	/// sets no text at all when it fails, rather than setting a placeholder.
	/// </summary>
	public IReadOnlyList<string> Values => _values;

	/// <summary>Whether the panel is up. The simulation does not tick while it is.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>The button the pointer has pressed and a release over it would click, or -1.</summary>
	public int PressedButton { get; private set; } = -1;

	/// <summary>
	/// The widget [Return] presses — <c>panel+0x2f7</c>, see <see cref="AlertPanelFocus"/>, which the loop sets
	/// to widget 0, the JOYSTICK row, before its first pass, putting the pointer on it. A click does not move it
	/// here, unlike on the preferences panel; only the focus walk does.
	/// </summary>
	public int Focus => _focus.Index;

	private readonly AlertPanelFocus _focus = new(ControlsPanelLayout.Button, ControlsPanelLayout.ButtonCount);

	/// <inheritdoc cref="AlertPanelFocus.TakePointer"/>
	public bool TakeFocusPointer(out int panelX, out int panelY) => _focus.TakePointer(out panelX, out panelY);

	// Whether a flash ending has let the pointer's button up under it; the press survives it.
	private bool _pressPopped;
	private readonly AlertPanelPresses _presses = new();

	/// <summary>
	/// Which slot of each button row's own action list that row is sitting on — <c>panel+0x462</c>,
	/// eight bytes the original seeds by searching the row's list for the stored code and leaving 0
	/// when it is not there.
	/// </summary>
	private readonly int[] _slots = new int[JoystickCapabilities.MaxButtons];

	/// <summary>
	/// The row whose button is drawn highlighted, or -1 — <c>panel+0x472</c>. A click moves it to
	/// whichever of the twelve rows was hit, putting the one that had it back to
	/// <see cref="RowState"/>'s resting state.
	/// </summary>
	public int HighlightedRow { get; private set; } = -1;

	/// <summary>
	/// The button row whose actions the OPTIONS list is showing, 0-7, or -1 for none —
	/// <c>panel+0x476</c>. The panel opens with none, which is why a freshly raised panel's list is
	/// twelve blank labels.
	/// </summary>
	public int SelectedButtonRow { get; private set; } = -1;

	/// <summary>
	/// Widget state <paramref name="index"/>'s button paints in, which picks both its plate frame and
	/// its caption font. The twelve rows are built in state 3 and the bottom pair in state 0; a row
	/// the capabilities have greyed is put into state 2, and the highlighted row into state 0. A button shows
	/// pressed while the pointer holds it and while a press flash does, and a flash's end leaves it in 0
	/// (<see cref="AlertPanelPresses.RestedByFlash"/>).
	/// </summary>
	public int RowState(int index) {
		if ((index == PressedButton && !_pressPopped) || _presses.IsLit(index)) {
			return AlertPanelLayout.WidgetState.Pressed;
		}

		if (index >= ControlsPanelLayout.RowCount) {
			return AlertPanelLayout.WidgetState.Rest;
		}

		if (IsGreyed(index)) {
			return AlertPanelLayout.WidgetState.Disabled;
		}

		return index == HighlightedRow || _presses.RestedByFlash(index)
			? AlertPanelLayout.WidgetState.Rest
			: AlertPanelLayout.WidgetState.Option;
	}

	// The rows ControlsPanel_Run puts in state 2 before its loop starts.
	private bool IsGreyed(int index) => index < ControlsPanelLayout.RowCount && !Capabilities.RowEnabled(index);

	// ControlsPanel_OnClick's and ControlsPanel_HandleEvent's highlight move: the row that had it back to state
	// 3, the new one to 0.
	private void MoveHighlight(int index) {
		if (HighlightedRow >= 0) {
			_presses.Restate(HighlightedRow);
		}

		_presses.Restate(index);
		HighlightedRow = index;
	}

	/// <summary>
	/// The twelve OPTIONS rows as they read now: the selected button row's action names, blank past
	/// the end of its list, and blank throughout when no row is selected — <c>ControlsPanel_FillOptionList</c> (<c>004593f8</c>), whose
	/// two out-of-range cases both point at an empty string.
	/// </summary>
	public IReadOnlyList<string> OptionRows() {
		var rows = new string[ControlsPanelLayout.OptionRowCount];
		var actions = SelectedButtonRow < 0 ? Array.Empty<byte>() : ActionsFor(SelectedButtonRow);
		for (int i = 0; i < rows.Length; i++) {
			rows[i] = i < actions.Length ? ActionName(actions[i]) : string.Empty;
		}

		return rows;
	}

	/// <summary>The action codes button row <paramref name="row"/> (0-7) may be bound to.</summary>
	public byte[] ActionsFor(int row) =>
		row >= 0 && row < _buttonActions.Length ? _buttonActions[row] : Array.Empty<byte>();

	/// <summary>The name of action code <paramref name="code"/>, or empty when it is outside the group.</summary>
	public string ActionName(int code) =>
		code >= 0 && code < _actionNames.Length ? _actionNames[code] : string.Empty;

	/// <summary>
	/// Reads the panel's text out of the mounted archives and resolves the twelve readouts against
	/// <paramref name="preferences"/>. Returns null when the string table is missing.
	/// </summary>
	/// <param name="razor">
	/// Whether the player's machine is the RAZOR — <c>PilotingRazor</c> (<c>004d25f5</c>). It picks both the title and
	/// the axis word sets, and which twelve bytes of the file the bindings come from.
	/// </param>
	/// <param name="capabilities">What the input layer reports. Defaults to no stick.</param>
	public static ControlsPanel? Build(GameContent content, SimulatorPreferences? preferences,
			bool razor = false, JoystickCapabilities capabilities = default) {
		ArgumentNullException.ThrowIfNull(content);

		if (SimStrings.Load(content, StringsFileName) is not { } strings) {
			return null;
		}

		var captions = new string[ControlsPanelLayout.ButtonCount];
		for (int i = 0; i < captions.Length; i++) {
			captions[i] = strings.Text(CaptionGroup, i) ?? string.Empty;
		}

		return new ControlsPanel(strings, preferences ?? SimulatorPreferences.Defaults(),
			strings.Text(razor ? RazorTitleGroup : TitleGroup, 0) ?? string.Empty,
			strings.Text(OptionsCaptionGroup, 0) ?? string.Empty,
			captions,
			strings.Group(ActionGroup).Select(entry => entry.Text).ToArray(),
			razor ? RazorButtonActions : HercButtonActions,
			capabilities, razor);
	}

	/// <summary>
	/// Re-reads all twelve readouts from the option array — <c>ControlsPanel_RefreshRow</c> (<c>00458d20</c>) for every case at
	/// once, which is what the panel's own paint does.
	/// </summary>
	private void RefreshValues() {
		for (int row = 0; row < _values.Length; row++) {
			if (!Capabilities.RowEnabled(row)) {
				_values[row] = string.Empty;
				continue;
			}

			byte option = _preferences[_optionBase + row];
			_values[row] = row < ControlsPanelLayout.AxisRowCount
				? _strings.Text(_firstAxisGroup + row, Math.Clamp((int)option, 0, AxisWordCount - 1))
					?? string.Empty
				: ActionName(option);
		}
	}

	/// <summary>
	/// Steps an axis row's mode — <c>ControlsPanel_Run</c> (<c>00458650</c>)'s cases 1 to 4, which are the plain
	/// <see cref="SimulatorPreferences.Step"/> over the row's three words, forward on a left click and
	/// back on a right one, when the row's capability is there. Either way an axis row drops the OPTIONS
	/// list back to showing nothing, which a button row does not.
	/// </summary>
	private void CycleAxis(int row, bool rightButton) {
		if (Capabilities.RowEnabled(row)) {
			_preferences.Step(_optionBase + row, AxisWordCount, forward: !rightButton);
		}

		SelectedButtonRow = -1;
		RefreshValues();
	}

	/// <summary>
	/// Steps a button row to the next action in <i>its own</i> list — <c>ControlsPanel_StepButton</c> (<c>00459320</c>) forward and
	/// <c>0045938c</c> back.
	///
	/// <para>The stored byte is an action code but the cycling is over the row's list by <b>slot
	/// index</b>, which the panel holds separately (<c>panel+0x462</c>) and wraps against that row's
	/// own length (<c>panel+0x46a</c>). So a row whose stored code is not in its list — which retail's
	/// own RECOMMEND can produce, see <see cref="HercButtonActions"/> — starts stepping from slot 0
	/// rather than from what it is showing.</para>
	/// </summary>
	private void CycleButton(int buttonRow, bool rightButton) {
		var actions = ActionsFor(buttonRow);
		if (actions.Length == 0) {
			return;
		}

		int slot = _slots[buttonRow] + (rightButton ? -1 : 1);
		if (slot >= actions.Length) {
			slot = 0;
		} else if (slot < 0) {
			slot = actions.Length - 1;
		}

		_slots[buttonRow] = slot;
		_preferences.Set(_optionBase + ControlsPanelLayout.AxisRowCount + buttonRow, actions[slot]);
		RefreshValues();
	}

	/// <summary>
	/// Writes the recommended set over all twelve options — <c>ControlsPanel_Run</c> (<c>00458650</c>)'s case 13, which
	/// pushes each of <see cref="HercRecommended"/>'s bytes through <c>Prefs_SetOption</c> in turn.
	///
	/// <para>The eight button bytes take a detour: the original does not store the recommended code
	/// directly but searches that row's list for it, stores the slot it found, and then writes back
	/// <b>whatever code that slot holds</b> — so a recommended code the row does not offer collapses
	/// to slot 0's code instead. Reproduced rather than corrected, because it is what decides what the
	/// panel ends up showing.</para>
	/// </summary>
	private void ApplyRecommended() {
		var recommended = IsRazor ? RazorRecommended : HercRecommended;

		for (int row = 0; row < ControlsPanelLayout.RowCount; row++) {
			byte wanted = recommended[row];
			if (row < ControlsPanelLayout.AxisRowCount) {
				_preferences.Set(_optionBase + row, wanted);
				continue;
			}

			int buttonRow = row - ControlsPanelLayout.AxisRowCount;
			var actions = ActionsFor(buttonRow);
			int slot = Array.IndexOf(actions, wanted);
			_slots[buttonRow] = slot < 0 ? 0 : slot;
			_preferences.Set(_optionBase + row,
				actions.Length == 0 ? wanted : actions[_slots[buttonRow]]);
		}

		RefreshValues();
	}

	/// <summary>
	/// Puts the panel up, with no row highlighted and the OPTIONS list blank, and re-seeds each button
	/// row's slot from what the option array currently holds — the constructor's own search, which
	/// leaves a row whose stored code is not in its list pointing at slot 0.
	/// </summary>
	public void Open() {
		IsOpen = true;
		PressedButton = -1;
		_pressPopped = false;
		HighlightedRow = -1;
		SelectedButtonRow = -1;
		_presses.Reset();
		_focus.Set(0);

		for (int buttonRow = 0; buttonRow < _slots.Length; buttonRow++) {
			int slot = Array.IndexOf(ActionsFor(buttonRow),
				_preferences[_optionBase + ControlsPanelLayout.AxisRowCount + buttonRow]);
			_slots[buttonRow] = slot < 0 ? 0 : slot;
		}

		RefreshValues();
	}

	/// <summary>
	/// Takes the panel down, however it was dismissed. Writing the options back is DONE's own action, not
	/// this — see <see cref="Click"/>.
	/// </summary>
	public void Close() {
		IsOpen = false;
		PressedButton = -1;
		_pressPopped = false;
		_presses.Reset();
		_focus.Clear();
	}

	/// <summary>
	/// A key the panel's own handler (<c>ControlsPanel_HandleEvent</c>, <c>00458f9c</c>) answers. [Return]
	/// presses the <see cref="Focus"/> widget, and [Esc] presses the cancel widget, which the constructor set to
	/// the last one it built — DONE. Either press is a left click on the widget, unless it is greyed, and flashes
	/// it; DONE's flash holds the panel up one more frame, see <see cref="Present"/>. The focus walk lands on a
	/// greyed row as on any other. The handler's stick half is <see cref="PressButtonRow"/>.
	/// </summary>
	/// <param name="nowTicks"><c>Time_GetCoarseTicks</c>, on a clock that runs while the panel is up.</param>
	/// <returns>True when the key was the panel's to answer.</returns>
	public bool HandleKey(AlertPanelKey key, long nowTicks) =>
		IsOpen && _focus.AnswerKey(key, ControlsPanelLayout.DoneButton, index => PressWidget(index, nowTicks));

	// AlertPanel_PressWidget: unless the widget is greyed, its click, then its flash.
	private void PressWidget(int index, long nowTicks) {
		if (!IsGreyed(index)) {
			Click(index, rightButton: false);
			_presses.Flash(index, nowTicks);
		}
	}

	/// <summary>
	/// The tail of one pass of the panel's loop — <c>AlertPanel_Present</c> (<c>00454ab0</c>), as far as the
	/// buttons go: services the press flashes, then closes the panel if its close flag survives the hold a
	/// still-queued flash puts on it (<see cref="AlertPanelPresses.HoldClose"/>).
	/// </summary>
	public void Present(long nowTicks) {
		if (!IsOpen) {
			return;
		}

		var ended = _presses.Service(nowTicks);
		if (PressedButton >= 0 && ended.Contains(PressedButton)) {
			_pressPopped = true;
		}

		if (_presses.HoldClose()) {
			Close();
		}
	}

	/// <summary>
	/// A mouse press inside the panel, in the panel's own device pixels. A greyed row is not armed:
	/// <c>Widget_HitTestChildren</c> skips any widget in state 2 outright, so a disabled row is not
	/// merely inert but invisible to the hit test.
	/// </summary>
	public void PointerDown(float panelX, float panelY) {
		if (!IsOpen) {
			return;
		}

		PressedButton = -1;
		_pressPopped = false;
		for (int i = 0; i < ControlsPanelLayout.ButtonCount; i++) {
			if (!IsGreyed(i) && ControlsPanelLayout.Button(i).Contains(panelX, panelY)) {
				PressedButton = i;
				return;
			}
		}
	}

	/// <summary>How many of the eight button rows the capabilities leave live.</summary>
	private int LiveButtonRows =>
		Capabilities.Present ? Math.Min(Capabilities.ButtonCount, JoystickCapabilities.MaxButtons) : 0;

	/// <summary>
	/// A mouse release, in the same space: the <see cref="Click"/> of the button, when press and release both
	/// landed on it.
	/// </summary>
	/// <param name="rightButton">
	/// Whether the release was of the right button — <c>panel+0x2ff</c>. Every row on this panel steps
	/// backwards on it, unlike two of the preferences panel's.
	/// </param>
	public void PointerUp(float panelX, float panelY, bool rightButton = false) {
		if (!IsOpen) {
			return;
		}

		int pressed = PressedButton;
		PressedButton = -1;
		_pressPopped = false;
		if (pressed < 0 || !ControlsPanelLayout.Button(pressed).Contains(panelX, panelY)) {
			return;
		}

		Click(pressed, rightButton);
	}

	/// <summary>
	/// One button's click, however it came — <c>ControlsPanel_OnClick</c> (<c>00458ebc</c>), which decides
	/// between selecting and acting, and <c>ControlsPanel_Run</c> (<c>00458650</c>)'s switch, which does the
	/// acting.
	///
	/// <para><b>A button row takes two clicks to change.</b> The click handler selects the row only
	/// when it is not already the selected one; when it is, it queues the row's action instead, which
	/// steps it. So the first click on a button row points the OPTIONS list at it and the second and
	/// subsequent clicks walk it down that list. An axis row is never "selected", so every click on
	/// one steps it — and clears the list selection, which is why clicking an axis row after a button
	/// row empties the OPTIONS box.</para>
	///
	/// <para><b>The switch runs only with a capability block</b> — a stick <c>Input_GetDevice(3)</c> answered
	/// for. Without one every row is greyed and cannot be clicked, RECOMMEND does nothing, and DONE takes the
	/// panel down without writing the options back. With one, DONE calls <c>ControlsPanel_Save</c>
	/// (<c>00459140</c>) and then <c>Prefs_CommitOptions</c> (<c>00459878</c>), which write this panel's own
	/// options and nothing else (<see cref="SimulatorPreferences.Save"/>,
	/// <see cref="SimulatorPreferences.Commit"/>), and RECOMMEND writes the recommended set. Every write goes
	/// through <see cref="SimulatorPreferences.Set"/>, which runs the option's handler as the original's
	/// <c>Prefs_SetOption</c> does.</para>
	///
	/// <para>DONE also sets the close flag, so the panel comes down at the frame's <see cref="Present"/>.</para>
	/// </summary>
	private void Click(int index, bool rightButton) {
		if (index < ControlsPanelLayout.RowCount) {
			MoveHighlight(index);
		}

		bool buttonRow = index >= ControlsPanelLayout.AxisRowCount && index < ControlsPanelLayout.RowCount;
		int row = index - ControlsPanelLayout.AxisRowCount;
		bool acts = true;
		if (buttonRow && row != SelectedButtonRow) {
			SelectedButtonRow = row;
			acts = false;
		}

		if (index == ControlsPanelLayout.DoneButton) {
			_presses.RequestClose();
		}

		if (!acts || !Capabilities.Present) {
			return;
		}

		if (index < ControlsPanelLayout.AxisRowCount) {
			CycleAxis(index, rightButton);
		} else if (buttonRow) {
			if (row < LiveButtonRows) {
				CycleButton(row, rightButton);
			}
		} else if (index == ControlsPanelLayout.RecommendButton) {
			ApplyRecommended();
			SelectedButtonRow = -1;
		} else if (index == ControlsPanelLayout.DoneButton) {
			_preferences.Save(SimulatorPreferences.ControlsPanelOptions(IsRazor));
			_preferences.Commit();
		}
	}

	/// <summary>
	/// A press of physical joystick button <paramref name="row"/> (0-7) while the panel is up, which
	/// picks that button's row rather than pressing a widget — the first half of
	/// <c>ControlsPanel_HandleEvent</c> (<c>00458f9c</c>), and why this panel has a handler of its own
	/// (docs/retail/simulation/preferences.md, "What a joystick button does").
	///
	/// <para>The rule is the mouse's: a press on the selected row steps it, and a press on a row the stick has
	/// that is not the selected one selects it and takes <see cref="HighlightedRow"/>. Either way the row's
	/// widget flashes, though no click reaches it.</para>
	///
	/// <para>The caller passes the device's own eight buttons, the trigger among them — this is not
	/// the post-binding set, in which the trigger's slot is zeroed — and owns the press-once latch.
	/// See the host's <c>ReadControlsPanelJoystick</c>.</para>
	/// </summary>
	/// <param name="nowTicks"><c>Time_GetCoarseTicks</c>, on a clock that runs while the panel is up.</param>
	public void PressButtonRow(int row, long nowTicks) {
		if (!IsOpen || row < 0 || row >= JoystickCapabilities.MaxButtons) {
			return;
		}

		int widget = row + ControlsPanelLayout.AxisRowCount;
		if (row == SelectedButtonRow) {
			CycleButton(row, rightButton: false);
			_presses.Flash(widget, nowTicks);
		} else if (row < LiveButtonRows) {
			SelectedButtonRow = row;
			_presses.Flash(widget, nowTicks);
			MoveHighlight(widget);
		}
	}
}
