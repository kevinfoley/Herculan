using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Cfg;
using Herculan.Engine.Content;
using Herculan.Engine.View;
namespace Herculan.Engine.Cockpit;

/// <summary>
/// The [F12] preferences panel — <c>ctl_alrt</c>'s sibling <c>PreferencesPanel_Ctor</c>
/// (<c>004566c4</c>), raised by <c>PreferencesPanel_Raise</c> (<c>0045cfd4</c>) on commands
/// <c>0x58</c> ([F12]) and <c>0x219</c> ([Alt+P]). Nine simulator options, each a button and a value
/// readout, plus CONTROLS and DONE.
///
/// <para>This type is the panel's text and its state; its geometry is
/// <see cref="PreferencesPanelLayout"/> and its pixels are drawn by
/// <see cref="Render.Cockpit.AlertPanelPainter.DrawPreferencesPanel"/>.</para>
///
/// <para><b>The captions are all in one file.</b> <c>str\PRF_ALRT.STR</c> carries five groups, and
/// the constructor loads them into five fields with the counts 1, 11, 2, 3 and 5 — the title, the
/// eleven button captions, and three sets of value words the readouts pick from. Which set an option
/// draws from, and how its byte indexes that set, is <c>PreferencesPanel_RefreshRow</c> (<c>004571f4</c>)'s nine-case switch; that
/// mapping is <see cref="Options"/>.</para>
///
/// <para><b>Modal, and the simulation does not tick behind it</b> — <c>PreferencesPanel_Raise</c>
/// raises <c>DAT_004d2576</c> for as long as the panel is up and restores whatever it was on the way
/// out. Behind it the camera circles the player's machine; that is the host's, through
/// <see cref="ExternalViewChain.BeginPanelOrbit"/>.</para>
/// </summary>
public sealed class PreferencesPanel {
	/// <summary>The panel's own string table: its title, its captions and its value words.</summary>
	public const string StringsFileName = "PRF_ALRT.STR";

	/// <summary>Group 0 — <c>PREFERENCES</c>.</summary>
	private const int TitleGroup = 0;

	/// <summary>Group 1 — the eleven button captions, in widget order.</summary>
	private const int CaptionGroup = 1;

	/// <summary>Group 2 — <c>OFF</c>, <c>ON</c>.</summary>
	private const int SwitchGroup = 2;

	/// <summary>Group 3 — <c>TEXT ONLY</c>, <c>VOICE ONLY</c>, <c>TEXT / VOICE</c>.</summary>
	private const int ChannelGroup = 3;

	/// <summary>Group 4 — <c>LOW</c>, <c>MED LOW</c>, <c>MED HIGH</c>, <c>HIGH</c>, <c>MAXIMUM</c>.</summary>
	private const int DetailGroup = 4;

	/// <summary>The identity map, for an option whose byte is already the index of the word it wants.</summary>
	private static readonly int[] Direct = { 0, 1, 2, 3, 4 };

	/// <summary><c>DAT_0049e2e4</c>, HERC DETAIL's map: five settings across all five words.</summary>
	private static readonly int[] FiveSteps = { 0, 1, 2, 3, 4 };

	/// <summary>
	/// <c>DAT_0049e2da</c>, <c>DAT_0049e2ee</c> and <c>DAT_0049e2f8</c> — one table each and all three
	/// identical: three settings spread over the five words as LOW, MED HIGH and MAXIMUM. That is a
	/// second source for TERRAIN DISTANCE having exactly three settings, the first being the
	/// three-entry draw-radius table it indexes (<see cref="Terrain.TerrainDetail.RadiusInCells"/>).
	/// </summary>
	private static readonly int[] ThreeSteps = { 0, 2, 4, 0, 0 };

	/// <summary>How a row answers a click — <c>PreferencesPanel_Run</c>'s (<c>00456d4c</c>) nine cases.</summary>
	public enum RowCycle {
		/// <summary>Flips between 0 and 1. MUSIC, SOUNDS and TERRAIN TEXTURE.</summary>
		Toggle,

		/// <summary>Steps forward on a left click and back on a right one.</summary>
		Step,

		/// <summary>
		/// Steps forward whichever button was used. STRUCTURE DETAIL and EFFECTS DETAIL, whose cases
		/// test the right-button flag and then call the <b>forward</b> step down both arms — the other
		/// two stepping rows call the backward one. A retail slip: those two rows cannot be stepped
		/// backwards at all.
		/// </summary>
		StepForwardOnly,

		/// <summary>
		/// Steps forward only, and only while the voice archive is present. PILOT MESSAGE and COMPUTER
		/// MESSAGE, whose cases are wrapped in <c>VoiceArchivePresent</c> (<c>0049e9cd</c>) — see <see cref="VoiceAvailable"/>.
		/// </summary>
		StepForwardIfVoice,
	}

	/// <summary>
	/// One option row: which <c>prefs.cfg</c> byte it shows, which string group it names it from, the
	/// table that turns the one into the other, how many values it cycles through, and how it answers
	/// a click. In panel order — <c>PreferencesPanel_RefreshRow</c> (<c>004571f4</c>)'s cases 0 to 8 for the first three fields and
	/// <c>PreferencesPanel_Run</c>'s for the last two, which are also the first nine widget indices.
	/// </summary>
	public static readonly (int Option, int Group, int[] Map, int Modulus, RowCycle Cycle)[] Options = {
		(Prefs.MusicOption, SwitchGroup, Direct, 2, RowCycle.Toggle),
		(Prefs.SoundsOption, SwitchGroup, Direct, 2, RowCycle.Toggle),
		(Prefs.PilotMessageOption, ChannelGroup, Direct, 3, RowCycle.StepForwardIfVoice),
		(Prefs.ComputerMessageOption, ChannelGroup, Direct, 3, RowCycle.StepForwardIfVoice),
		(Prefs.TerrainDistanceOption, DetailGroup, ThreeSteps, 3, RowCycle.Step),
		(Prefs.TerrainTextureOption, SwitchGroup, Direct, 2, RowCycle.Toggle),
		(Prefs.HercDetailOption, DetailGroup, FiveSteps, 5, RowCycle.Step),
		(Prefs.StructureDetailOption, DetailGroup, ThreeSteps, 3, RowCycle.StepForwardOnly),
		(Prefs.EffectsDetailOption, DetailGroup, ThreeSteps, 3, RowCycle.StepForwardOnly),
	};

	private readonly string[] _captions;
	private readonly string[] _values;
	private readonly StringFile _strings;
	private readonly SimulatorPreferences _preferences;

	private PreferencesPanel(StringFile strings, SimulatorPreferences preferences, string title,
			string[] captions, bool soundAvailable, bool voiceAvailable) {
		_strings = strings;
		_preferences = preferences;
		Title = title;
		_captions = captions;
		_values = new string[PreferencesPanelLayout.OptionCount];
		SoundAvailable = soundAvailable;
		VoiceAvailable = voiceAvailable;
		RefreshValues();
	}

	/// <summary>The panel's title, drawn centred in the plate's title bar.</summary>
	public string Title { get; }

	/// <summary>The eleven button captions, in widget order.</summary>
	public IReadOnlyList<string> Captions => _captions;

	/// <summary>The nine value readouts, in the same order as <see cref="Options"/>.</summary>
	public IReadOnlyList<string> Values => _values;

	/// <summary>
	/// Whether a sound device came up. <c>PreferencesPanel_Run</c> greys the first four rows — MUSIC,
	/// SOUNDS, PILOT MESSAGE and COMPUTER MESSAGE — when <c>SfxManager</c> is null, before it enters
	/// the modal loop.
	/// </summary>
	public bool SoundAvailable { get; }

	/// <summary>
	/// Whether the voice archive is on disk — <c>VoiceArchivePresent</c> (<c>0049e9cd</c>), which <c>Voice_ArchiveExists</c> (<c>00459d6c</c>) sets by
	/// building the localised <c>simvoice</c> name and simply trying to <c>fopen</c> it. Without it
	/// the two message rows are forced to 0 at startup and neither can be clicked off it, so the
	/// player cannot ask for voice the install does not have.
	/// </summary>
	public bool VoiceAvailable { get; }

	/// <summary>
	/// The row whose button is drawn highlighted, or -1 — <c>panel+0x3b6</c>, the same mechanism
	/// <see cref="ControlsPanel.HighlightedRow"/> is. Only the nine option rows take it; CONTROLS and
	/// DONE do not.
	/// </summary>
	public int HighlightedRow { get; private set; } = -1;

	/// <summary>
	/// The widget state button <paramref name="index"/> paints in. The nine option rows are built in
	/// state 3, the bottom pair in state 0, a row with no sound device behind it is greyed to 2, and
	/// the highlighted row drops to 0. A button shows pressed while the pointer holds it and while a press
	/// flash does, and a flash's end leaves it in 0 (<see cref="AlertPanelPresses.RestedByFlash"/>).
	/// </summary>
	public int RowState(int index) {
		if ((index == PressedButton && !_pressPopped) || _presses.IsLit(index)) {
			return AlertPanelLayout.WidgetState.Pressed;
		}

		if (index >= PreferencesPanelLayout.OptionCount) {
			return AlertPanelLayout.WidgetState.Rest;
		}

		if (IsGreyed(index)) {
			return AlertPanelLayout.WidgetState.Disabled;
		}

		return index == HighlightedRow || _presses.RestedByFlash(index)
			? AlertPanelLayout.WidgetState.Rest
			: AlertPanelLayout.WidgetState.Option;
	}

	// The rows PreferencesPanel_Run puts in state 2 before its loop starts: the first four, with no sound device.
	private bool IsGreyed(int index) => !SoundAvailable && index < PreferencesPanelLayout.SoundRowCount;

	/// <summary>
	/// The widget [Return] and the trigger press — <c>panel+0x2f7</c>, see <see cref="AlertPanelFocus"/>. The loop
	/// focuses widget 0, MUSIC, before its first pass, and coming back from the controls panel focuses DONE, the
	/// cancel widget; both put the pointer on it. A click moves the focus to the widget clicked
	/// (<c>PreferencesPanel_OnClick</c>, <c>004573a8</c>) without going through <c>AlertPanel_SetFocus</c>, so the
	/// pointer stays where it clicked.
	/// </summary>
	public int Focus => _focus.Index;

	private readonly AlertPanelFocus _focus = new(PreferencesPanelLayout.Button, PreferencesPanelLayout.ButtonCount);

	/// <inheritdoc cref="AlertPanelFocus.TakePointer"/>
	public bool TakeFocusPointer(out int panelX, out int panelY) => _focus.TakePointer(out panelX, out panelY);

	// Whether a flash ending has let the pointer's button up under it; the press survives it.
	private bool _pressPopped;
	private readonly AlertPanelPresses _presses = new();

	/// <summary>Whether the panel is up. The simulation does not tick while it is.</summary>
	public bool IsOpen { get; private set; }

	/// <summary>
	/// The button the pointer has pressed and a release over it would click, or -1. It paints the pressed
	/// frame of its own bank while it is held (<c>PanelButton_Paint</c>, <c>00454ff8</c>); see
	/// <see cref="RowState"/>.
	/// </summary>
	public int PressedButton { get; private set; } = -1;

	/// <summary>
	/// Reads the panel's captions out of the mounted archives and resolves the nine readouts against
	/// <paramref name="preferences"/>. Returns null when the string table is missing, since a panel
	/// with no captions is not worth putting up.
	/// </summary>
	/// <param name="preferences">
	/// The install's own <c>data\prefs.cfg</c>, or null for the all-zero array
	/// <c>Prefs_LoadOptions</c> leaves behind when there is no file to read.
	/// </param>
	public static PreferencesPanel? Build(GameContent content, SimulatorPreferences? preferences,
			bool soundAvailable = true, bool voiceAvailable = true) {
		ArgumentNullException.ThrowIfNull(content);

		if (SimStrings.Load(content, StringsFileName) is not { } strings) {
			return null;
		}

		var captions = new string[PreferencesPanelLayout.ButtonCount];
		for (int i = 0; i < captions.Length; i++) {
			captions[i] = strings.Text(CaptionGroup, i) ?? string.Empty;
		}

		return new PreferencesPanel(strings, preferences ?? SimulatorPreferences.Defaults(),
			strings.Text(TitleGroup, 0) ?? string.Empty, captions, soundAvailable, voiceAvailable);
	}

	/// <summary>
	/// Re-reads all nine readouts from the option array — <c>PreferencesPanel_RefreshRow</c> (<c>004571f4</c>) for every case at once,
	/// which is what the panel's own paint does.
	/// </summary>
	private void RefreshValues() {
		for (int i = 0; i < _values.Length; i++) {
			var (option, group, map, _, _) = Options[i];
			int word = map[Math.Clamp((int)_preferences[option], 0, map.Length - 1)];
			_values[i] = _strings.Text(group, word) ?? string.Empty;
		}
	}

	/// <summary>
	/// Steps row <paramref name="row"/> the way its click case does, and refreshes its readout. A row
	/// whose rule refuses the click leaves the option alone.
	/// </summary>
	private void Cycle(int row, bool rightButton) {
		if (row < 0 || row >= Options.Length) {
			return;
		}

		var (option, _, _, modulus, cycle) = Options[row];
		switch (cycle) {
			case RowCycle.Toggle:
				_preferences.Toggle(option);
				break;
			case RowCycle.Step:
				_preferences.Step(option, modulus, forward: !rightButton);
				break;
			case RowCycle.StepForwardOnly:
				_preferences.Step(option, modulus);
				break;
			case RowCycle.StepForwardIfVoice:
				if (VoiceAvailable) {
					_preferences.Step(option, modulus);
				}

				break;
		}

		RefreshValues();
	}

	/// <summary>
	/// Puts the panel up. [F12] is scancode <c>0x58</c>; like the objectives panel's own key, nothing
	/// in the panel's loop answers it again, so a second press does not take it back down.
	/// </summary>
	public void Open() {
		IsOpen = true;
		PressedButton = -1;
		_pressPopped = false;
		HighlightedRow = -1;
		ControlsRequested = false;
		_presses.Reset();
		_focus.Set(0);
	}

	/// <summary>
	/// Takes the panel down, however it was dismissed, and writes its nine options back —
	/// <c>PreferencesPanel_Run</c> calls <c>PreferencesPanel_Save</c> (<c>004574cc</c>) at
	/// <c>0045707a</c> as it closes.
	///
	/// <para><b>However it was dismissed is the whole of it.</b> The original's revert,
	/// <c>PreferencesPanel_Revert</c> (<c>004574e0</c>), is unreferenced, so leaving this panel
	/// saves whichever button leaves it.</para>
	/// </summary>
	public void Close() {
		IsOpen = false;
		PressedButton = -1;
		_pressPopped = false;
		_presses.Reset();
		_focus.Clear();
		_preferences.Save(SimulatorPreferences.PreferencesPanelOptions);
	}

	/// <summary>
	/// A key the panel's own handler (<c>AlertPanel_HandleEvent</c>, <c>00454e10</c>) answers. [Return]
	/// presses the <see cref="Focus"/> widget, and [Esc] presses the cancel widget, which this panel sets to
	/// the last one it built — DONE. Either press is a left click on the widget and flashes it; DONE takes the
	/// panel down in the same pass, before the flash has drawn. The focus walk lands on a greyed row as on any
	/// other, and pressing one does nothing.
	/// </summary>
	/// <param name="nowTicks"><c>Time_GetCoarseTicks</c>, on a clock that runs while the panel is up.</param>
	/// <returns>True when the key was the panel's to answer.</returns>
	public bool HandleKey(AlertPanelKey key, long nowTicks) =>
		IsOpen && _focus.AnswerKey(key, PreferencesPanelLayout.DoneButton, index => PressWidget(index, nowTicks));

	/// <summary>The same handler's stick half — see <see cref="AlertPanelFocus.AnswerStick"/>.</summary>
	/// <returns>True when either button was the panel's to answer.</returns>
	public bool HandleStick(bool trigger, bool button2, long nowTicks) =>
		IsOpen && _focus.AnswerStick(trigger, button2, index => PressWidget(index, nowTicks));

	/// <summary>
	/// <c>AlertPanel_PressWidget</c> (<c>00454dcc</c>): unless the widget is greyed, its click with the left
	/// button, then its press flash.
	/// </summary>
	private void PressWidget(int index, long nowTicks) {
		if (IsGreyed(index)) {
			return;
		}

		Click(index, rightButton: false);
		if (IsOpen) {
			_presses.Flash(index, nowTicks);
		}
	}

	/// <summary>
	/// The tail of one pass of the panel's loop — <c>PreferencesPanel_Present</c> (<c>00457180</c>), as far
	/// as the buttons go: services the press flashes. It has no hold on the close flag; DONE closes the
	/// panel in <see cref="Click"/>.
	/// </summary>
	public void Present(long nowTicks) {
		if (!IsOpen) {
			return;
		}

		var ended = _presses.Service(nowTicks);
		if (PressedButton >= 0 && ended.Contains(PressedButton)) {
			_pressPopped = true;
		}
	}

	/// <summary>
	/// The controls panel this one raised has come down: <c>PreferencesPanel_Run</c> repaints and focuses
	/// the cancel widget, DONE, which puts the pointer on it.
	/// </summary>
	public void ReturnFromControls() {
		if (IsOpen) {
			_focus.Set(PreferencesPanelLayout.DoneButton);
		}
	}

	/// <summary>
	/// A mouse press inside the panel, in the panel's own device pixels. Arms whichever button it
	/// landed on, the way <c>Widget_OnMouseDown</c> does.
	/// </summary>
	public void PointerDown(float panelX, float panelY) {
		if (!IsOpen) {
			return;
		}

		PressedButton = -1;
		_pressPopped = false;
		for (int i = 0; i < PreferencesPanelLayout.ButtonCount; i++) {
			if (!IsGreyed(i) && PreferencesPanelLayout.Button(i).Contains(panelX, panelY)) {
				PressedButton = i;
				return;
			}
		}
	}

	/// <summary>
	/// Whether the last release pressed CONTROLS, which the original answers by constructing
	/// <see cref="ControlsPanel"/> over this panel and running it. Read and cleared by the host, which
	/// owns that panel's lifetime; this panel stays up behind it, as it does in the original.
	/// </summary>
	public bool ControlsRequested { get; private set; }

	/// <summary>Clears <see cref="ControlsRequested"/> once the host has acted on it.</summary>
	public void ClearControlsRequest() => ControlsRequested = false;

	/// <summary>
	/// A mouse release, in the same space: the <see cref="Click"/> of the button, when press and release
	/// both landed on it. <c>Widget_OnMouseUp</c> re-hit-tests before it calls the click, so a press dragged
	/// off its button never fires.
	/// </summary>
	/// <param name="rightButton">
	/// Whether the release was of the right button — <c>panel+0x2ff</c>, which the click handler takes
	/// from bit 1 of the click value. Two of the nine rows step backwards on it.
	/// </param>
	public void PointerUp(float panelX, float panelY, bool rightButton = false) {
		if (!IsOpen) {
			return;
		}

		int pressed = PressedButton;
		PressedButton = -1;
		_pressPopped = false;
		if (pressed < 0 || !PreferencesPanelLayout.Button(pressed).Contains(panelX, panelY)) {
			return;
		}

		Click(pressed, rightButton);
	}

	/// <summary>
	/// One button's click, however it came — <c>PreferencesPanel_OnClick</c> (<c>004573a8</c>), then the
	/// action <c>PreferencesPanel_Run</c> takes on it. An option row takes the highlight, putting the row
	/// that had it back to state 3; every button takes the focus. Then DONE closes the panel, CONTROLS raises
	/// the controls panel, and an option row steps its setting.
	/// </summary>
	private void Click(int index, bool rightButton) {
		if (index < PreferencesPanelLayout.OptionCount) {
			if (HighlightedRow >= 0) {
				_presses.Restate(HighlightedRow);
			}

			_presses.Restate(index);
			HighlightedRow = index;
		}

		_focus.Move(index);

		if (index < PreferencesPanelLayout.OptionCount) {
			Cycle(index, rightButton);
		} else if (index == PreferencesPanelLayout.DoneButton) {
			Close();
		} else if (index == PreferencesPanelLayout.ControlsButton) {
			ControlsRequested = true;
		}
	}
}
