using System.Runtime.CompilerServices;
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
/// keeps its own copies of options 7, 8, 10, 9 and 11 of <see cref="Es1Preferences"/> (the input build's), changes the
/// options at each click, and on DONE rebuilds the joystick devices and saves the five. It takes the family's keys alone
/// and calls no draw hook, so the view behind it stands still.
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

	/// <summary>
	/// The seven buttons in widget order, from the tables <c>00095244</c> (<i>x</i>), <c>00095254</c> (<i>y</i>),
	/// <c>00095264</c> (width, 60) and <c>00095274</c> (height, 10): JOYSTICK, JOY1 CONTROL, WCS, RCS, MOUSE, CALIBRATE,
	/// DONE.
	/// </summary>
	private static readonly Es1Rect[] ButtonRects = [
		new(20, 29, 80, 39), new(20, 43, 80, 53), new(20, 60, 80, 70), new(20, 74, 80, 84), new(113, 60, 173, 70),
		new(39, 93, 99, 103), new(122, 93, 182, 103),
	];

	/// <summary>The five readouts' rects, the tables <c>00095294</c> to <c>000952b8</c>.</summary>
	private static readonly Es1Rect[] ReadoutRects = [
		new(84, 29, 200, 39), new(84, 43, 200, 53), new(84, 60, 106, 70), new(84, 74, 106, 84), new(177, 60, 200, 70),
	];

	/// <summary>The captions' margin, each button's word of the table <c>00095284</c>.</summary>
	private const int CaptionMarginY = -2;

	/// <summary>The readouts' fill color, flags and margin.</summary>
	private const byte ReadoutFill = 0x3f;
	private const int ReadoutFlags = 1, ReadoutMarginX = 2, ReadoutMarginY = -2;

	/// <summary>The title bar: from <i>y</i> 4 to 21, 20 wider than the title; the title's margin.</summary>
	private const int TitleTop = 4, TitleBottom = 21, TitleBarMargin = 20, TitleMarginY = -11;

	/// <summary><c>str\ctl_alrt</c>'s six groups.</summary>
	private const int TitleGroup = 0, CaptionGroup = 1, NoYesGroup = 2, OffOnGroup = 3, StickModelGroup = 4,
		StickControlGroup = 5;

	private readonly Es1Label[] _readouts = new Es1Label[ReadoutRects.Length];

	/// <summary>
	/// <c>ControlsPanel_Ctor</c>(panel, <paramref name="rect"/>, <paramref name="keepsScreen"/>): 222 × 109, centered
	/// (<c>AlertPanel_CenterRect</c>, at (48, 45)) with no rect given and at the rect's corner with one; the plate; the
	/// string table's six groups; the seven buttons, each with its caption label in <c>cpylw</c> centered with the margin
	/// (0, −2), registered with the panel's root and added to the list; the title label in <c>title</c> on a bar 20 wider
	/// than the title, centered with the margin (0, −11); the five readouts in <c>green6x8</c>, left-aligned with the
	/// margin (2, −2) on a fill of <c>0x3f</c>; the action cleared; DONE the cancel widget. The preferences panel builds it
	/// with no rect, keeping the screen under itself.
	/// </summary>
	public Es1ControlsPanel(Es1AlertGlobals globals, Es1Rect? rect = null, bool keepsScreen = true)
		: base(globals, rect is { } at ? SetRect(Height, Width, at.Y0, at.X0) : CenterRect(Height, Width), keepsScreen) {
		var content = globals.Content;
		var widgets = globals.Widgets;
		Plate = content != null ? Es1WidgetResources.LoadFrames(content, "CTL_ALRT")?.FirstOrDefault() : null;
		// str\ctl_alrt, through ResourcePath_BuildFolderName, into the base's string table.
		Strings = content != null ? SimStrings.LoadUntranslated(content, "CTL_ALRT.STR") : null;
		Title = Strings?.Text(TitleGroup, 0);

		for (int index = 0; index < ButtonRects.Length; index++) {
			var widget = new Es1PanelButton(index, this, ButtonRects[index]);
			var caption = new Es1Label(widgets.Cpylw);
			caption.SetRect(Es1Label.Centered, ButtonRects[index], 0, CaptionMarginY);
			widget.Caption = caption;
			Root.RegisterClickable(widget);
			AddWidget(widget);
		}

		TitleLabel = new Es1Label(widgets.Title);
		int titleWidth = widgets.Title?.Measure(Title ?? "") ?? 0;
		int barLeft = (Region.X1 - Region.X0 - titleWidth - TitleBarMargin) >> 1;
		TitleBar = new Es1Rect(barLeft, TitleTop, barLeft + titleWidth + TitleBarMargin, TitleBottom);
		TitleLabel.SetRect(Es1Label.Centered, TitleBar, 0, TitleMarginY);

		for (int i = 0; i < _readouts.Length; i++) {
			_readouts[i] = new Es1Label(widgets.Green6x8) { FillColor = ReadoutFill };
			_readouts[i].SetRect(ReadoutFlags, ReadoutRects[i], ReadoutMarginX, ReadoutMarginY);
		}

		Action = 0;
		Cancel = Done;
	}

	/// <summary><c>+0x318</c>: the title, <c>CONTROLS</c> in the retail table.</summary>
	public string? Title { get; }

	/// <summary><c>+0x384</c>: the five readouts, rows 1 to 5.</summary>
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

	/// <summary>The options the rows show and the loop changes, the input build's.</summary>
	private Es1Preferences Options => Globals.Input.Preferences;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>ControlsPanel_Run</c> (<c>00048810</c>) (docs/es1/simulation/preferences.md#its-loop--controlspanel_run-00048810):
	/// options 7, 8, 10, 9 and 11 copied to the panel, with <see cref="RudderSwappedModel"/> 2 for option 7 at 2,
	/// <see cref="KeptStickControl"/> option 8 or 1 for 0, <see cref="KeptRudder"/> option 9, and the applied pair options
	/// 7 and 9; <see cref="Es1AlertPanel.Enter"/> and the focus on JOYSTICK. Then until the close byte is set, a pass: the
	/// input build, its block to the family's keys (<see cref="Es1AlertPanel.HandleEvent"/>), the action's case with each
	/// row it changes refreshed, the action cleared, and each widget's per-frame call; then
	/// <see cref="Es1AlertPanel.Leave"/>, and bit 0 of the close byte the answer. Retail's loop goes round with no wait;
	/// here a pass that leaves the close byte clear waits for the next host frame, the tick standing there (plan decision
	/// 11). The loop draws on the page on the screen alone and calls no draw hook.
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

		Enter();
		SetFocus(Joystick);
		while (Close == 0) {
			var block = Globals.Input.Build();
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

		for (int row = 1; row <= _readouts.Length; row++) {
			RefreshRow(row);
		}
	}

	/// <summary>
	/// <c>ControlsPanel_RefreshRow</c> (<c>00048ce4</c>)(panel, <paramref name="row"/>), between the cursor's hide and
	/// show: row 1 puts the stick model's name by <see cref="StickModel"/> on readout 0, row 2 the stick control by
	/// <see cref="StickControl"/> on readout 1, rows 3 and 4 <c>NO</c> or <c>YES</c> by <see cref="ThrottleKeys"/> and
	/// <see cref="Rudder"/> on readouts 2 and 3, and row 5 <c>OFF</c> or <c>ON</c> by <see cref="MouseOn"/> on readout
	/// 4. A value past its group's words, where retail reads past the table, draws no text, an engine guard.
	/// </summary>
	public void RefreshRow(int row) {
		var cursor = Globals.Display.Cursor;
		cursor.Hide();
		switch (row) {
			case 1:
				_readouts[0].SetText(Canvas, Strings?.Text(StickModelGroup, StickModel));
				break;
			case 2:
				_readouts[1].SetText(Canvas, Strings?.Text(StickControlGroup, StickControl));
				break;
			case 3:
				_readouts[2].SetText(Canvas, Strings?.Text(NoYesGroup, ThrottleKeys));
				break;
			case 4:
				_readouts[3].SetText(Canvas, Strings?.Text(NoYesGroup, Rudder));
				break;
			case 5:
				_readouts[4].SetText(Canvas, Strings?.Text(OffOnGroup, MouseOn));
				break;
		}

		cursor.Show();
	}

	/// <summary>
	/// <c>ControlsPanel_ApplyDevices</c> (<c>00048e98</c>)
	/// (docs/es1/simulation/preferences.md#rebuilding-the-joystick-devices--controlspanel_applydevices-00048e98): when
	/// <see cref="StickModel"/> or <see cref="Rudder"/> differs from the pair last applied, the joystick devices rebuilt
	/// for options 7 and 9 (<see cref="Es1JoystickDevices.Rebuild"/>, whose doc comment gives this engine's departure
	/// for <c>NONE</c>, plan decision 14) and the pair stored; when option 11 differs from <see cref="MouseOn"/>, the mouse
	/// device enabled or disabled by <see cref="MouseOn"/>, though the two never differ here, the MOUSE row setting both
	/// from one toggle; and options 7, 8, 10, 9 and 11 (<see cref="SaveList"/>) written to <c>data\prefs.cfg</c> whether
	/// or not they changed.
	/// </summary>
	public void ApplyDevices() {
		var input = Globals.Input;
		var options = input.Preferences;
		if (StickModel != AppliedStickModel || Rudder != AppliedRudder) {
			input.Joysticks.Rebuild(options);
			AppliedStickModel = StickModel;
			AppliedRudder = Rudder;
		}

		if (options[Es1Preferences.MouseOption] != MouseOn) {
			input.MouseDeviceEnabled = MouseOn != 0;
		}

		options.SaveSelectedOptions(SaveList);
	}

	/// <summary>
	/// The action's case of <c>ControlsPanel_Run</c>'s pass, with the pass's input <paramref name="block"/>
	/// (docs/es1/simulation/preferences.md#its-loop--controlspanel_run-00048810). Every write applies the option's
	/// handler.
	/// </summary>
	private void TakeAction(int action, Es1PlayerInputBlock block) {
		var options = Options;
		switch (action - 1) {
			case Joystick:
				StepStickModel(options, block);
				break;
			case Joy1Control:
				// With a stick model, option 8 steps forward or back over its three values until it is not 0.
				if (StickModel != 0) {
					do {
						StickControl = RightButton
							? options.StepOptionBack(true, StickControls, Es1Preferences.StickModeOption)
							: options.StepOption(true, StickControls, Es1Preferences.StickModeOption);
					} while (StickControl == 0);

					RefreshRow(2);
				}

				break;
			case Wcs:
				ThrottleKeys = options.ToggleOption(true, Es1Preferences.ThrottleKeysOption);
				RefreshRow(3);
				break;
			case Rcs:
				ToggleRudder(options);
				break;
			case Mouse:
				MouseOn = options.ToggleOption(true, Es1Preferences.MouseOption);
				RefreshRow(5);
				break;
			case Calibrate:
				if (Rudder != 0 || StickModel != 0) {
					ApplyDevices();

					// PLACEHOLDER: the calibration panel, built on the stack with 00049154(that, this panel, 1), run through
					// its loop 000496f0 and destroyed with 00049678(that, 2), is not ported: what it shows and writes is
					// docs/es1/simulation/preferences.md's Open item, and the slice leaves the joystick's calibration out
					// (plan, "What the slice leaves out"); a modern controller gives calibrated values (plan decision 9).
					// No plan step retires it.
					SetFocus(Cancel);
				}

				break;
			case Done:
				ApplyDevices();
				options.CommitOptions(true);
				break;
		}
	}

	/// <summary>
	/// Action 1, JOYSTICK: option 7 stepped over the six models, forward or with the right button back, and forward once
	/// more when that gives <c>NONE</c> while the pass's trigger (the block's <c>+0x15</c>) is held; row 1 refreshed.
	/// With <c>NONE</c> and a stick control set, the control put aside in <see cref="KeptStickControl"/> and option 8
	/// set to 0; with a stick model and no stick control, option 8 set back from <see cref="KeptStickControl"/>; either
	/// way row 2 refreshed. With a stick model, then: under <c>2 STANDARD JOYSTICKS</c> with the RCS row's byte set, the
	/// byte put aside in <see cref="KeptRudder"/> and xored with 1; under another model, the byte set from
	/// <see cref="KeptRudder"/> when that is set; either way row 4 refreshed. Option 9 itself is not written.
	/// </summary>
	private void StepStickModel(Es1Preferences options, Es1PlayerInputBlock block) {
		StickModel = RightButton
			? options.StepOptionBack(true, StickModels, Es1Preferences.StickConfigurationOption)
			: options.StepOption(true, StickModels, Es1Preferences.StickConfigurationOption);
		if (StickModel == 0 && block.Buttons[0] != 0) {
			StickModel = options.StepOption(true, StickModels, Es1Preferences.StickConfigurationOption);
		}

		RefreshRow(1);
		if (StickModel == 0) {
			if (StickControl != 0) {
				KeptStickControl = StickControl;
				StickControl = 0;
				options.SetOption(true, 0, Es1Preferences.StickModeOption);
				RefreshRow(2);
			}

			return;
		}

		if (StickControl == 0) {
			StickControl = KeptStickControl;
			options.SetOption(true, StickControl, Es1Preferences.StickModeOption);
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
	private void ToggleRudder(Es1Preferences options) {
		Rudder = options.ToggleOption(true, Es1Preferences.ThirdAxisOption);
		KeptRudder = Rudder;
		RefreshRow(4);
		if (Rudder != 0 && StickModel == TwoSticks) {
			RudderSwappedModel = TwoSticks;
			StickModel = OneStick;
			options.SetOption(true, OneStick, Es1Preferences.StickConfigurationOption);
			RefreshRow(1);
		} else if (Rudder == 0 && RudderSwappedModel == TwoSticks) {
			RudderSwappedModel = 0;
			StickModel = TwoSticks;
			options.SetOption(true, TwoSticks, Es1Preferences.StickConfigurationOption);
			RefreshRow(1);
		}
	}
}
