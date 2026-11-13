using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File.Bnd;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The controls panel the preferences panel's CONTROLS raises, <c>ControlsPanel_Ctor</c> (<c>ES1DBSIM 00048408</c>) and
/// <c>ControlsAlertVtable</c> (<c>000952c6</c>) (docs/es1/simulation/preferences.md#the-controls-panel--controlspanel_ctor-00048408):
/// 222 × 109 over the middle of the screen on the plate <c>dba\ctl_alrt</c>, the title and captions of
/// <c>str\CTL_ALRT.STR</c>, JOYSTICK, JOY1 CONTROL, WCS, RCS and MOUSE with their values in words, CALIBRATE and DONE. It
/// keeps its own copies of options 7, 8, 10, 9 and 11 of <see cref="Es1Preferences"/> (the input read's), changes the
/// options at each click, and on DONE rebuilds the joystick devices and saves the five. It takes the family's keys alone
/// and calls no draw hook, so the view behind it stands still.
///
/// <para>The shell's OPTIONS > CONTROLS (screen 1320) is the shell's copy, <c>ControlsPanel_Ctor</c> (<c>ES1GO 00006050</c>)
/// (docs/es1/shell/options.md#the-panel--controlspanel_ctor-00006050), the same statements on the shell's globals: its
/// numbers and readouts are <see cref="Es1ControlsVariant.Shell"/>'s, its input and options its
/// <see cref="Es1AlertGlobals.Input"/>.</para>
/// </summary>
public sealed class Es1ControlsPanel : Es1AlertPanel {
	/// <summary>The panel's size, 222 × 109 on the 320 × 200 screen.</summary>
	public const int Width = 222, Height = 109;

	/// <summary>The widgets' indices; <c>ControlsPanel_OnClick</c> makes the action the index plus 1.</summary>
	public const int Joystick = 0, Joy1Control = 1, Wcs = 2, Rcs = 3, Mouse = 4, Calibrate = 5, Done = 6;

	/// <summary>The stick models option 7 steps over, <c>NONE</c> to <c>FLIGHTSTICK PRO</c>.</summary>
	public const int StickModels = 6;

	/// <summary>Option 7's <c>2 STANDARD JOYSTICKS</c>, which the RCS row exchanges with <c>1 STANDARD JOYSTICK</c>.</summary>
	public const byte TwoSticks = 2, OneStick = 1;

	/// <summary>The values option 8 steps over, <c>NO JOYSTICK</c>, <c>MOVEMENT CONTROL</c> and <c>TURRET CONTROL</c>.</summary>
	public const int StickControls = 3;

	/// <summary><c>ControlsPanel_SaveList</c> (<c>00095348</c>): the options <see cref="ApplyDevices"/> saves, 7, 8, 10, 9 and 11.</summary>
	public static readonly int[] SaveList = [7, 8, 10, 9, 11];

	/// <summary>The rows <see cref="Paint"/> refreshes, one per readout.</summary>
	private const int Rows = 5;

	/// <summary>The readouts' label flags.</summary>
	private const int ReadoutFlags = 1;

	/// <summary><c>str\ctl_alrt</c>'s six groups.</summary>
	private const int TitleGroup = 0, CaptionGroup = 1, NoYesGroup = 2, OffOnGroup = 3, StickModelGroup = 4,
		StickControlGroup = 5;

	private readonly Es1Label[] _readouts;

	/// <summary>
	/// <c>ControlsPanel_Ctor</c>(panel, <paramref name="rect"/>, <paramref name="keepsScreen"/>): the variant's size,
	/// centered (<c>AlertPanel_CenterRect</c>, at (48, 45)) with no rect given and at the rect's corner with one; the
	/// plate; the string table's six groups; the buttons, each with its caption label in <c>cpylw</c> centered with the
	/// margin (0, the button's caption <i>y</i>), registered with the panel's root and added to the list; the title label
	/// in <c>title</c> on a bar the variant's margin wider than the title, centered with the margin (0, the title's
	/// <i>y</i>); the readouts left-aligned with the variant's margin on its fill, in its font; the action cleared; the
	/// last widget, DONE, the cancel widget. The preferences panel builds it with no rect, keeping the screen under itself.
	/// </summary>
	/// <param name="variant">The binary's numbers and readouts; none is the simulator's (<see cref="Es1ControlsVariant.Simulator"/>).</param>
	public Es1ControlsPanel(Es1AlertGlobals globals, Es1Rect? rect = null, bool keepsScreen = true, Es1ControlsVariant? variant = null)
		: base(globals, PanelRect(rect, variant ?? Es1ControlsVariant.Simulator), keepsScreen) {
		var look = variant ?? Es1ControlsVariant.Simulator;
		var content = globals.Content;
		var widgets = globals.Widgets;
		Plate = content != null ? Es1WidgetResources.LoadFrames(content, "CTL_ALRT")?.FirstOrDefault() : null;
		// str\ctl_alrt, through ResourcePath_BuildFolderName, into the base's string table.
		Strings = content != null ? SimStrings.LoadUntranslated(content, "CTL_ALRT.STR") : null;
		Title = Strings?.Text(TitleGroup, 0);

		for (int index = 0; index < look.ButtonRects.Count; index++) {
			var widget = new Es1PanelButton(index, this, look.ButtonRects[index]);
			var caption = new Es1Label(widgets.Cpylw);
			caption.SetRect(Es1Label.Centered, look.ButtonRects[index], 0, look.CaptionMarginY[index]);
			widget.Caption = caption;
			Root.RegisterClickable(widget);
			AddWidget(widget);
		}

		TitleLabel = new Es1Label(widgets.Title);
		int titleWidth = widgets.Title?.Measure(Title ?? "") ?? 0;
		int barLeft = (Region.X1 - Region.X0 - titleWidth - look.TitleBarMargin) >> 1;
		TitleBar = new Es1Rect(barLeft, look.TitleTop, barLeft + titleWidth + look.TitleBarMargin, look.TitleTop + look.TitleHeight);
		TitleLabel.SetRect(Es1Label.Centered, TitleBar, 0, look.TitleMarginY);

		_readouts = new Es1Label[look.ReadoutRects.Count];
		var readoutFont = look.ReadoutFont ?? widgets.Green6x8;
		for (int i = 0; i < _readouts.Length; i++) {
			_readouts[i] = new Es1Label(readoutFont) { FillColor = look.ReadoutFill };
			_readouts[i].SetRect(ReadoutFlags, look.ReadoutRects[i], look.ReadoutMarginX, look.ReadoutMarginY);
		}

		Action = 0;
		Cancel = Widgets.Count - 1;
	}

	/// <summary>The panel's rect: <c>AlertPanel_CenterRect</c> of the variant's size with no rect, <c>AlertPanel_SetRect</c> at the rect's corner with one.</summary>
	private static Es1Rect PanelRect(Es1Rect? rect, Es1ControlsVariant look) =>
		rect is { } at ? SetRect(look.Height, look.Width, at.Y0, at.X0) : CenterRect(look.Height, look.Width);

	/// <summary><c>+0x318</c>: the title, <c>CONTROLS</c> in the retail table.</summary>
	public string? Title { get; }

	/// <summary><c>+0x384</c>: the readouts, rows 1 to 5.</summary>
	public IReadOnlyList<Es1Label> Readouts => _readouts;

	/// <summary><c>+0x398</c>: the clicked widget's index plus 1, which the loop's pass takes; 0 for none.</summary>
	public int Action { get; private set; }

	/// <summary><c>+0x2e7</c>: the click was the right button's, which steps a row back.</summary>
	public bool RightButton { get; private set; }

	/// <summary><c>+0x39c</c>: the panel's copy of option 7, the stick model, which row 1 shows.</summary>
	public byte StickModel { get; private set; }

	/// <summary><c>+0x39d</c>: the panel's copy of option 8, the stick control, which row 2 shows.</summary>
	public byte StickControl { get; private set; }

	/// <summary><c>+0x39e</c>: the panel's copy of option 10, the WCS throttle, which row 3 shows.</summary>
	public byte ThrottleKeys { get; private set; }

	/// <summary><c>+0x39f</c>: the panel's copy of option 9, the RCS rudder, which row 4 shows.</summary>
	public byte Rudder { get; private set; }

	/// <summary><c>+0x3a0</c>: the panel's copy of option 11, the mouse, which row 5 shows.</summary>
	public byte MouseOn { get; private set; }

	/// <summary><c>+0x3a1</c>: 2 while the RCS row has swapped <c>2 STANDARD JOYSTICKS</c> for <c>1 STANDARD JOYSTICK</c>, or option 7 was 2 when the panel ran.</summary>
	public byte RudderSwappedModel { get; private set; }

	/// <summary><c>+0x3a2</c>: the stick control <c>NONE</c> put aside, which a stick model puts back; 1 for none.</summary>
	public byte KeptStickControl { get; private set; }

	/// <summary><c>+0x3a3</c>: the RCS row's byte as last toggled or put aside by <c>2 STANDARD JOYSTICKS</c>.</summary>
	public byte KeptRudder { get; private set; }

	/// <summary><c>+0x3a4</c>, <c>+0x3a5</c>: options 7 and 9 as last applied to the joystick devices.</summary>
	public byte AppliedStickModel { get; private set; }

	public byte AppliedRudder { get; private set; }

	/// <summary>
	/// <b>This engine's own:</b> whether JOYSTICK, JOY1 CONTROL, RCS and CALIBRATE are locked, as they are while a
	/// controller's Custom profile says what the stick does (<see cref="Es1AlertInput.StickOptionsOverridden"/>), read as
	/// the panel runs. Locked, the four buttons are in state 2, greyed, which neither a click nor a key presses
	/// (<see cref="Es1AlertPanel.PressWidget"/>), as the preferences panel's MUSIC, SOUNDS and RADIO are with no sound
	/// manager; the readouts of options 7, 8 and 9 are filled and show no word, retail having none for a profile; the
	/// focus starts on WCS; and WCS, MOUSE and DONE work as ever, DONE saving the three options as they stand.
	/// </summary>
	public bool StickRowsLocked { get; private set; }

	/// <summary>The options the rows show and the loop changes, the binary's.</summary>
	private Es1Preferences Options => Globals.Input.Preferences;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>ControlsPanel_Run</c> (<c>00048810</c>) (docs/es1/simulation/preferences.md#its-loop--controlspanel_run-00048810):
	/// options 7, 8, 10, 9 and 11 copied to the panel, with <see cref="RudderSwappedModel"/> 2 for option 7 at 2,
	/// <see cref="KeptStickControl"/> option 8 or 1 for 0, <see cref="KeptRudder"/> option 9, and the applied pair options
	/// 7 and 9; <see cref="Es1AlertPanel.Enter"/> and the focus on JOYSTICK. Then until the close byte is set, a pass: the
	/// input read (the shell's <c>Shell_PollInput</c>), its block to the family's keys
	/// (<see cref="Es1AlertPanel.HandleEvent"/>), the action's case with each row it changes refreshed, the action cleared,
	/// and each widget's per-frame call; then <see cref="Es1AlertPanel.Leave"/>, and bit 0 of the close byte the answer.
	/// Retail's loop goes round with no wait; here a pass that leaves the close byte clear waits for the next host frame,
	/// the tick standing there (plan decision 11). The loop draws on the page on the screen alone and calls no draw hook.
	/// </summary>
	public override IEnumerable<Es1Wait> Run(StrongBox<int> answer) {
		var options = Options;
		StickModel = options[Es1Preferences.StickConfigurationOption];
		StickControl = options[Es1Preferences.StickModeOption];
		ThrottleKeys = options[Es1Preferences.ThrottleKeysOption];
		Rudder = options[Es1Preferences.ThirdAxisOption];
		MouseOn = options[Es1Preferences.MouseOption];
		RudderSwappedModel = StickModel == TwoSticks ? TwoSticks : (byte)0;
		KeptStickControl = StickControl != 0 ? StickControl : (byte)1;
		KeptRudder = Rudder;
		AppliedStickModel = StickModel;
		AppliedRudder = Rudder;

		StickRowsLocked = Globals.Input.StickOptionsOverridden;
		foreach (int widget in (int[])[Joystick, Joy1Control, Rcs, Calibrate]) {
			Widgets[widget].State = StickRowsLocked ? Es1Widget.Hidden : Es1Widget.Up;
		}

		Enter();
		SetFocus(StickRowsLocked ? Wcs : Joystick);
		while (Close == 0) {
			var block = Globals.Input.Read();
			HandleEvent(block);
			if (Action != 0) {
				TakeAction(Action, block);
				Action = 0;
			}

			foreach (var widget in Widgets) {
				widget.Tick();
			}

			if (Close == 0) {
				yield return Es1Wait.NextHostFrame;
			}
		}

		Leave();
		answer.Value = Close & 1;
	}

	/// <summary><c>ControlsPanel_GetButtonCaption</c> (<c>00048e80</c>)(panel, <paramref name="index"/>): the caption <c>+0x31c</c>[index].</summary>
	public override string? GetButtonCaption(int index) => Strings?.Text(CaptionGroup, index);

	/// <summary>
	/// Slot <c>+0x10</c>, <c>ControlsPanel_OnClick</c> (<c>00048db0</c>)(panel, <paramref name="flags"/>,
	/// <paramref name="child"/>): bit 1 of the flags, the right button, kept; the child's index in the widget list plus 1
	/// made the action, and for DONE the close byte 3.
	/// </summary>
	public override void OnChildClick(short flags, Es1Widget child) {
		RightButton = (flags & 2) != 0;
		int index = IndexOf(child);
		if (index == -1) {
			return;
		}

		Action = index + 1;
		if (index == Done) {
			Close = 3;
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>ControlsPanel_Paint</c> (<c>00048c58</c>): the plate blitted with flags 0 at the panel's corner;
	/// the title through slot <c>+8</c>; each widget's paint; then rows 1 to 5 refreshed.
	/// </summary>
	protected override void Paint() {
		Canvas.Blit(Plate, Region.X0, Region.Y0);
		SetTitleText(Title);
		foreach (var widget in Widgets) {
			widget.Paint();
		}

		for (int row = 1; row <= Rows; row++) {
			RefreshRow(row);
		}
	}

	/// <summary>
	/// <c>ControlsPanel_RefreshRow</c> (<c>00048ce4</c>)(panel, <paramref name="row"/>), between the cursor's hide and
	/// show: row 1 puts the stick model's name by <see cref="StickModel"/> on readout 0, row 2 the stick control by
	/// <see cref="StickControl"/> on readout 1, rows 3 and 4 <c>NO</c> or <c>YES</c> by <see cref="ThrottleKeys"/> and
	/// <see cref="Rudder"/> on readouts 2 and 3, and row 5 <c>OFF</c> or <c>ON</c> by <see cref="MouseOn"/> on readout
	/// 4. A value past its group's words, where retail reads past the table, draws no text, and a readout the variant
	/// lacks, where retail reads past the panel's five, draws nothing: engine guards. Rows 1, 2 and 4 show no word while
	/// <see cref="StickRowsLocked"/>.
	/// </summary>
	public void RefreshRow(int row) {
		var cursor = Globals.Display.Cursor;
		cursor.Hide();
		switch (row) {
			case 1:
				SetReadout(0, StickRowsLocked ? "" : Strings?.Text(StickModelGroup, StickModel));
				break;
			case 2:
				SetReadout(1, StickRowsLocked ? "" : Strings?.Text(StickControlGroup, StickControl));
				break;
			case 3:
				SetReadout(2, Strings?.Text(NoYesGroup, ThrottleKeys));
				break;
			case 4:
				SetReadout(3, StickRowsLocked ? "" : Strings?.Text(NoYesGroup, Rudder));
				break;
			case 5:
				SetReadout(4, Strings?.Text(OffOnGroup, MouseOn));
				break;
		}

		cursor.Show();
	}

	private void SetReadout(int index, string? text) {
		if (index < _readouts.Length) {
			_readouts[index].SetText(Canvas, text);
		}
	}

	/// <summary>
	/// <c>ControlsPanel_ApplyDevices</c> (<c>00048e98</c>)
	/// (docs/es1/simulation/preferences.md#rebuilding-the-joystick-devices--controlspanel_applydevices-00048e98): when
	/// <see cref="StickModel"/> or <see cref="Rudder"/> differs from the pair last applied, the joystick devices rebuilt
	/// for options 7 and 9 (<see cref="Es1AlertInput.RebuildJoysticks"/>) and the pair stored; when option 11 differs from
	/// <see cref="MouseOn"/>, the mouse device enabled or disabled by <see cref="MouseOn"/>, though the two never differ
	/// here, the MOUSE row setting both from one toggle; and options 7, 8, 10, 9 and 11 (<see cref="SaveList"/>) written to
	/// <c>data\prefs.cfg</c> whether or not they changed. The shell's (<c>00006bed</c>) is the same statements on its own
	/// devices (docs/es1/shell/options.md#the-panel--controlspanel_ctor-00006050).
	/// </summary>
	public void ApplyDevices() {
		var input = Globals.Input;
		var options = input.Preferences;
		if (StickModel != AppliedStickModel || Rudder != AppliedRudder) {
			input.RebuildJoysticks();
			AppliedStickModel = StickModel;
			AppliedRudder = Rudder;
		}

		if (options[Es1Preferences.MouseOption] != MouseOn) {
			input.SetMouseDeviceEnabled(MouseOn != 0);
		}

		options.SaveSelectedOptions(SaveList);
	}

	/// <summary>
	/// The action's case of <c>ControlsPanel_Run</c>'s pass, with the pass's input <paramref name="block"/>
	/// (docs/es1/simulation/preferences.md#its-loop--controlspanel_run-00048810). Every write applies the option's
	/// handler.
	/// </summary>
	private void TakeAction(int action, Es1PlayerInputBlock block) {
		var input = Globals.Input;
		switch (action - 1) {
			case Joystick:
				StepStickModel(input, block);
				break;
			case Joy1Control:
				// With a stick model, option 8 steps forward or back over its three values until it is not 0.
				if (StickModel != 0) {
					do {
						StickControl = RightButton
							? input.StepOptionBack(StickControls, Es1Preferences.StickModeOption)
							: input.StepOption(StickControls, Es1Preferences.StickModeOption);
					} while (StickControl == 0);

					RefreshRow(2);
				}

				break;
			case Wcs:
				ThrottleKeys = input.ToggleOption(Es1Preferences.ThrottleKeysOption);
				RefreshRow(3);
				break;
			case Rcs:
				ToggleRudder(input);
				break;
			case Mouse:
				MouseOn = input.ToggleOption(Es1Preferences.MouseOption);
				RefreshRow(5);
				break;
			case Calibrate:
				if (Rudder != 0 || StickModel != 0) {
					ApplyDevices();

					// Engine departure, by design (plan decision 9): retail raises the calibration panel here, built on the
					// stack with 00049154(that, this panel, 1), run through its loop 000496f0 and destroyed with
					// 00049678(that, 2) (the shell's 00006f15, 00007631 and 00007561). A modern controller gives calibrated
					// values, so no panel is raised and CALIBRATE only rebuilds the devices.
					SetFocus(Cancel);
				}

				break;
			case Done:
				ApplyDevices();
				input.CommitAll();
				break;
		}
	}

	/// <summary>
	/// Action 1, JOYSTICK: option 7 stepped over the six models, forward or with the right button back, and forward once
	/// more when that gives <c>NONE</c> while the pass's trigger (the block's button 0) is held; row 1 refreshed.
	/// With <c>NONE</c> and a stick control set, the control put aside in <see cref="KeptStickControl"/> and option 8
	/// set to 0; with a stick model and no stick control, option 8 set back from <see cref="KeptStickControl"/>; either
	/// way row 2 refreshed. With a stick model, then: under <c>2 STANDARD JOYSTICKS</c> with the RCS row's byte set, the
	/// byte put aside in <see cref="KeptRudder"/> and xored with 1; under another model, the byte set from
	/// <see cref="KeptRudder"/> when that is set; either way row 4 refreshed. Option 9 itself is not written.
	/// </summary>
	private void StepStickModel(Es1AlertInput input, Es1PlayerInputBlock block) {
		StickModel = RightButton
			? input.StepOptionBack(StickModels, Es1Preferences.StickConfigurationOption)
			: input.StepOption(StickModels, Es1Preferences.StickConfigurationOption);
		if (StickModel == 0 && block.Buttons[0] != 0) {
			StickModel = input.StepOption(StickModels, Es1Preferences.StickConfigurationOption);
		}

		RefreshRow(1);
		if (StickModel == 0) {
			if (StickControl != 0) {
				KeptStickControl = StickControl;
				StickControl = 0;
				input.SetOption(0, Es1Preferences.StickModeOption);
				RefreshRow(2);
			}

			return;
		}

		if (StickControl == 0) {
			StickControl = KeptStickControl;
			input.SetOption(StickControl, Es1Preferences.StickModeOption);
			RefreshRow(2);
		}

		if (StickModel == TwoSticks) {
			if (Rudder != 0) {
				KeptRudder = Rudder;
				Rudder ^= 1;
				RefreshRow(4);
			}
		} else if (KeptRudder != 0) {
			Rudder = KeptRudder;
			RefreshRow(4);
		}
	}

	/// <summary>
	/// Action 4, RCS: option 9 toggled into the RCS row's byte and <see cref="KeptRudder"/>, row 4 refreshed. Turned on
	/// under <c>2 STANDARD JOYSTICKS</c>, option 7 set to <c>1 STANDARD JOYSTICK</c> and <see cref="RudderSwappedModel"/>
	/// to 2; otherwise, turned off with <see cref="RudderSwappedModel"/> at 2, option 7 set back to
	/// <c>2 STANDARD JOYSTICKS</c> and <see cref="RudderSwappedModel"/> to 0; either way row 1 refreshed.
	/// </summary>
	private void ToggleRudder(Es1AlertInput input) {
		Rudder = input.ToggleOption(Es1Preferences.ThirdAxisOption);
		KeptRudder = Rudder;
		RefreshRow(4);
		if (Rudder != 0 && StickModel == TwoSticks) {
			RudderSwappedModel = TwoSticks;
			StickModel = OneStick;
			input.SetOption(OneStick, Es1Preferences.StickConfigurationOption);
			RefreshRow(1);
		} else if (Rudder == 0 && RudderSwappedModel == TwoSticks) {
			RudderSwappedModel = 0;
			StickModel = TwoSticks;
			input.SetOption(TwoSticks, Es1Preferences.StickConfigurationOption);
			RefreshRow(1);
		}
	}
}

/// <summary>
/// What a binary's copy of the controls panel takes from its own globals: the sizes and places the simulator's
/// <c>ControlsPanel_Ctor</c> has as immediates and tables and the shell's reads from <c>CTL_ALRT.BND</c>, and the
/// readouts' font and fill (docs/es1/shell/options.md#the-panel--controlspanel_ctor-00006050).
/// </summary>
/// <param name="ReadoutFont">The readouts' font; none is the widgets' <c>green6x8</c>.</param>
public sealed record Es1ControlsVariant(int Width, int Height, int TitleBarMargin, int TitleTop, int TitleHeight, int TitleMarginY,
		IReadOnlyList<Es1Rect> ButtonRects, IReadOnlyList<int> CaptionMarginY, IReadOnlyList<Es1Rect> ReadoutRects, int ReadoutMarginX,
		int ReadoutMarginY, byte ReadoutFill, HudFont? ReadoutFont) {
	/// <summary>
	/// The simulator's: 222 × 109; the title bar 20 wider than the title from <i>y</i> 4 to 21, the title's margin −11; the
	/// seven buttons from the tables <c>00095244</c> (<i>x</i>), <c>00095254</c> (<i>y</i>), <c>00095264</c> (width, 60)
	/// and <c>00095274</c> (height, 10), JOYSTICK, JOY1 CONTROL, WCS, RCS, MOUSE, CALIBRATE, DONE, each caption's margin
	/// −2 from <c>00095284</c>; the five readouts from <c>00095294</c> to <c>000952b8</c> with the margin (2, −2), in
	/// <c>green6x8</c> on <c>0x3f</c>.
	/// </summary>
	public static Es1ControlsVariant Simulator { get; } = new(Es1ControlsPanel.Width, Es1ControlsPanel.Height, 20, 4, 17, -11,
		[
			new(20, 29, 80, 39), new(20, 43, 80, 53), new(20, 60, 80, 70), new(20, 74, 80, 84), new(113, 60, 173, 70),
			new(39, 93, 99, 103), new(122, 93, 182, 103),
		],
		[-2, -2, -2, -2, -2, -2, -2],
		[new(84, 29, 200, 39), new(84, 43, 200, 53), new(84, 60, 106, 70), new(84, 74, 106, 84), new(177, 60, 200, 70)],
		2, -2, 0x3f, null);

	/// <summary>The shell's readouts' fill, <c>0x1f</c>.</summary>
	public const byte ShellReadoutFill = 0x1f;

	/// <summary>
	/// The shell's (<c>ES1GO 00006050</c>): every number from <paramref name="bnd"/>, <c>CtlAlert_Bnd</c>; each rect
	/// (<i>x</i>, <i>y</i>, <i>x</i> + width, <i>y</i> + height) and the title bar from its top to its top plus its height;
	/// the readouts in <paramref name="readoutFont"/>, <c>Shell_Fonts</c> entry 5, <c>nm_green</c>, on
	/// <see cref="ShellReadoutFill"/>. Retail's file gives the simulator's numbers.
	/// </summary>
	public static Es1ControlsVariant Shell(Es1CtlAlertBnd bnd, HudFont? readoutFont) {
		var buttons = new Es1Rect[bnd.ButtonCount];
		var captions = new int[buttons.Length];
		for (int i = 0; i < buttons.Length; i++) {
			var (x, y, width, height) = bnd.Button(i);
			buttons[i] = new Es1Rect(x, y, x + width, y + height);
			captions[i] = bnd.CaptionOffsetY(i);
		}

		var readouts = new Es1Rect[bnd.ReadoutCount];
		for (int i = 0; i < readouts.Length; i++) {
			var (x, y, width, height) = bnd.Readout(i);
			readouts[i] = new Es1Rect(x, y, x + width, y + height);
		}

		var (offsetX, offsetY) = bnd.ReadoutOffset;
		return new(bnd.Width, bnd.Height, bnd.TitleBarMargin, bnd.TitleTop, bnd.TitleHeight, bnd.TitleOffsetY, buttons, captions,
			readouts, offsetX, offsetY, ShellReadoutFill, readoutFont);
	}
}
