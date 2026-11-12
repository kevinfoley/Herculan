using System.Runtime.CompilerServices;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The preferences panel <c>F12</c> and <c>Alt+P</c> raise, <c>PreferencesPanel_Ctor</c> (<c>ES1DBSIM 00047578</c>) and
/// <c>PrefsAlertVtable</c> (<c>000951ae</c>) (docs/es1/simulation/preferences.md#the-preferences-panel--preferencespanel_ctor-00047578):
/// a strip 320 × 89 along the bottom of the screen on the plate <c>dba\prf_alrt</c>, the title and captions of
/// <c>str\PRF_ALRT.STR</c>, MUSIC, SOUNDS, RADIO and HORIZON with their values in words, SKY, GROUND and SHAPES with LED
/// bars, and CONTROLS and DONE. Its loop changes options 0 to 6 of <see cref="Es1Preferences"/> (the input read's) and
/// draws a frame of the outside view through its draw hook each pass (<see cref="Es1PreferencesRaise"/>).
/// </summary>
public sealed class Es1PreferencesPanel : Es1AlertPanel {
	/// <summary>The panel's rect, <c>AlertPanel_SetRect</c>(89, 319, 111, 0): (0, 111)–(319, 200).</summary>
	public const int Left = 0, Top = 111, Width = 319, Height = 89;

	/// <summary>The rows, options 0 to 6.</summary>
	public const int RowCount = 7;

	/// <summary>The widgets' indices, which <c>PreferencesPanel_OnClick</c> makes the action.</summary>
	public const int Music = 0, Sounds = 1, Radio = 2, Horizon = 3, Sky = 4, Ground = 5, Shapes = 6, Controls = 7, Done = 8;

	/// <summary><c>PreferencesPanel_SaveList</c> (<c>0009519c</c>): the options DONE saves, 0 to 6.</summary>
	public static readonly int[] SaveList = [0, 1, 2, 3, 4, 5, 6];

	/// <summary>The commands <c>PreferencesPanel_HandleEvent</c> looks up, the four dwords at <c>00095224</c>: [=], keypad [+], [-] and keypad [-].</summary>
	public const ushort EqualsKey = 0x0d, KeypadPlus = 0x4e, MinusKey = 0x0c, KeypadMinus = 0x4a;

	/// <summary>A bar's step, the amount the panel hands <c>Prefs_IncreaseOption</c> and <c>Prefs_DecreaseOption</c>.</summary>
	public const byte BarStep = 0x20;

	/// <summary>
	/// The nine buttons in widget order, each its button index (its caption's, of <c>+0x34c</c>) and its rect, from the
	/// tables <c>0009510c</c> to <c>00095148</c>: MUSIC, SOUNDS, RADIO, HORIZON, SKY, GROUND, SHAPES, CONTROLS, DONE.
	/// </summary>
	private static readonly (int Button, Es1Rect Rect)[] Buttons = [
		(0, new Es1Rect(21, 32, 81, 42)),
		(1, new Es1Rect(21, 44, 81, 54)),
		(2, new Es1Rect(21, 56, 81, 66)),
		(3, new Es1Rect(21, 68, 81, 78)),
		(6, new Es1Rect(158, 31, 220, 41)),
		(7, new Es1Rect(158, 43, 220, 53)),
		(8, new Es1Rect(158, 55, 220, 65)),
		(4, new Es1Rect(155, 70, 227, 80)),
		(5, new Es1Rect(228, 70, 300, 80)),
	];

	/// <summary>The four readouts' rects, the tables <c>0009515c</c> to <c>00095174</c>.</summary>
	private static readonly Es1Rect[] ReadoutRects = [
		new(85, 32, 145, 42), new(85, 44, 145, 54), new(85, 56, 145, 66), new(85, 68, 145, 78),
	];

	/// <summary>The three bars' rects, the tables <c>0009517c</c> to <c>00095194</c>.</summary>
	private static readonly Es1Rect[] BarRects = [new(227, 32, 298, 39), new(227, 44, 298, 51), new(227, 56, 298, 63)];

	/// <summary>The bars' range, unlit color and shades.</summary>
	private const short BarRange = 0xff;
	private const byte BarUnlit = 0x3f, BarEvenShade = 50, BarOddShade = 49;

	/// <summary>The readouts' fill color, flags and margin.</summary>
	private const byte ReadoutFill = 0x3f;
	private const int ReadoutFlags = 1, ReadoutMarginX = 2, ReadoutMarginY = -1;

	/// <summary>The captions' margin.</summary>
	private const int CaptionMarginY = -1;

	/// <summary>The title bar: from <i>y</i> 4 to 21, 20 wider than the title; the title's margin.</summary>
	private const int TitleTop = 4, TitleBottom = 21, TitleBarMargin = 20, TitleMarginY = -11;

	/// <summary><c>str\prf_alrt</c>'s four groups.</summary>
	private const int TitleGroup = 0, CaptionGroup = 1, OffOnGroup = 2, RadioGroup = 3;

	private readonly Es1Label[] _readouts = new Es1Label[ReadoutRects.Length];
	private readonly Es1LedBarGraph[] _bars = new Es1LedBarGraph[BarRects.Length];
	private readonly byte[] _repaint = new byte[RowCount];

	/// <summary>
	/// <c>PreferencesPanel_Ctor</c>(0, 0), as <c>PreferencesPanel_Raise</c> builds it, so the panel keeps no screen: the
	/// rect; the plate; the string table's four groups; the nine buttons, each with its caption label in <c>cpylw</c>
	/// centered with the margin (0, −1), registered with the panel's root and added to the list, CONTROLS and DONE after
	/// the other seven; the title label in <c>title</c> on a bar 20 wider than the title, centered with the margin
	/// (0, −11); the four readouts in <c>green6x8</c>, left-aligned with the margin (2, −1) on a fill of <c>0x3f</c>; the
	/// three LED bars of range 255, unlit <c>0x3f</c> and shades 50 and 49; DONE the cancel widget; no draw hook and the
	/// repaint counters clear.
	/// </summary>
	public Es1PreferencesPanel(Es1AlertGlobals globals)
		: base(globals, SetRect(Height, Width, Top, Left), keepsScreen: false) {
		var content = globals.Content;
		var widgets = globals.Widgets;
		Plate = content != null ? Es1WidgetResources.LoadFrames(content, "PRF_ALRT")?.FirstOrDefault() : null;
		// str\prf_alrt, through ResourcePath_BuildFolderName, into the base's string table.
		Strings = content != null ? SimStrings.LoadUntranslated(content, "PRF_ALRT.STR") : null;
		Title = Strings?.Text(TitleGroup, 0);

		foreach (var (button, rect) in Buttons) {
			var widget = new Es1PanelButton(button, this, rect);
			var caption = new Es1Label(widgets.Cpylw);
			caption.SetRect(Es1Label.Centered, rect, 0, CaptionMarginY);
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

		for (int i = 0; i < _bars.Length; i++) {
			_bars[i] = new Es1LedBarGraph(BarRange, BarRects[i]) {
				UnlitColor = BarUnlit, EvenShade = BarEvenShade, OddShade = BarOddShade,
			};
		}

		Cancel = Done;
	}

	/// <summary>
	/// Whether the sound manager (<c>000959ac</c>) exists, which <c>resource.cfg</c> decides (plan decision 25); without
	/// it <see cref="Run"/> greys MUSIC, SOUNDS and RADIO. With no sound file in the globals, a test's, it counts as present.
	/// </summary>
	public bool SoundManagerPresent => Globals.Sound is not { } sound || sound.Manager != null;

	/// <summary><c>+0x348</c>: the title, <c>PREFERENCES</c> in the retail table.</summary>
	public string? Title { get; }

	/// <summary><c>+0x318</c>: the four readouts, rows 0 to 3.</summary>
	public IReadOnlyList<Es1Label> Readouts => _readouts;

	/// <summary><c>+0x334</c>: the three bars, rows 4 to 6.</summary>
	public IReadOnlyList<Es1LedBarGraph> Bars => _bars;

	/// <summary><c>+0x340</c>: the widget a click acted on, which the loop's pass takes; −1 for none.</summary>
	public int Action { get; private set; } = -1;

	/// <summary><c>+0x2e7</c>: the click was the right button's, which lowers a bar.</summary>
	public bool RightButton { get; private set; }

	/// <summary><c>+0x384</c>, the draw hook <c>PreferencesPanel_SetDrawHook</c> (<c>00048180</c>) installs; none draws nothing.</summary>
	public Action? DrawHook { get; set; }

	/// <summary><c>+0x388</c>–<c>+0x38e</c>: each row's repaint counter, the passes still to refresh it on.</summary>
	public IReadOnlyList<byte> RepaintCounters => _repaint;

	/// <summary>The options the rows show and the loop changes, the input read's.</summary>
	private Es1Preferences Options => Globals.Input.Preferences;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>PreferencesPanel_Run</c> (<c>00047a88</c>)
	/// (docs/es1/simulation/preferences.md#the-loop--preferencespanel_run-00047a88): with no sound manager, MUSIC, SOUNDS
	/// and RADIO greyed; the action cleared; <see cref="Es1AlertPanel.Enter"/> and the focus on MUSIC; the panel copied
	/// from the page on the screen to the other page, the cursor hidden around it, and the page on the screen drawn on
	/// again. Then until the close byte is set, a pass: the input read, its block to <see cref="HandleEvent"/>, the
	/// action's case, and <see cref="CallDrawHook"/>, which yields its wait for the monitor; then
	/// <see cref="Es1AlertPanel.Leave"/>, and bit 0 of the close byte the answer. A pass that waits for no retrace, with
	/// no draw hook, ends with the next host frame instead, the tick standing there (plan decision 11). The step after
	/// the leave with a screen buffer does not run: this panel keeps none.
	/// </summary>
	public override IEnumerable<Es1Wait> Run(StrongBox<int> answer) {
		if (!SoundManagerPresent) {
			for (int widget = Music; widget <= Radio; widget++) {
				Widgets[widget].State = Es1Widget.Hidden;
			}
		}

		Action = -1;
		Enter();
		SetFocus(Music);
		var display = Globals.Display;
		display.DrawPage = display.PairDrawn;
		display.Cursor.Hide();
		// Region_CopyBetweenPages of the panel's region at the offset (0, 0), the source page the one on the screen.
		display.CopyRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, 0, 0);
		display.Cursor.Show();
		display.DrawPage = display.PairVisible;

		while (Close == 0) {
			HandleEvent(Globals.Input.Read());
			if (Action == Controls) {
				foreach (var wait in RunControls()) {
					yield return wait;
				}
			} else if (Action != -1) {
				TakeAction(Action);
			}

			Action = -1;

			bool waited = false;
			foreach (var wait in CallDrawHook()) {
				waited = true;
				yield return wait;
			}

			if (!waited && Close == 0) {
				yield return Es1Wait.NextHostFrame;
			}
		}

		Leave();
		answer.Value = Close & 1;
	}

	/// <summary><c>PreferencesPanel_GetButtonCaption</c> (<c>00048168</c>)(panel, <paramref name="index"/>): the caption <c>+0x34c</c>[index].</summary>
	public override string? GetButtonCaption(int index) => Strings?.Text(CaptionGroup, index);

	/// <summary>
	/// Slot <c>+0x10</c>, <c>PreferencesPanel_OnClick</c> (<c>000480f4</c>)(panel, <paramref name="flags"/>,
	/// <paramref name="child"/>): bit 1 of the flags, the right button, kept; the child's index in the widget list made
	/// the action, when it is in the list.
	/// </summary>
	public override void OnChildClick(short flags, Es1Widget child) {
		RightButton = (flags & 2) != 0;
		int index = IndexOf(child);
		if (index != -1) {
			Action = index;
		}
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>PreferencesPanel_HandleEvent</c> (<c>00048078</c>)(panel, <paramref name="block"/>): the
	/// family's keys first (<see cref="Es1AlertPanel.HandleEvent"/>), answering 1 when they act; otherwise [=] and keypad
	/// [+] press the focused widget with the flags 1, and [-] and keypad [-] with the flags 2, only while the focus is
	/// SKY, GROUND or SHAPES; any of the four answers 1, any other code 0.
	/// </summary>
	public override int HandleEvent(Es1PlayerInputBlock block) {
		if (base.HandleEvent(block) != 0) {
			return 1;
		}

		short flags;
		switch (block.CommandWord) {
			case EqualsKey or KeypadPlus:
				flags = 1;
				break;
			case MinusKey or KeypadMinus:
				flags = 2;
				break;
			default:
				return 0;
		}

		if (Focus is Sky or Ground or Shapes) {
			PressWidget(flags, Focus);
		}

		return 1;
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>PreferencesPanel_Paint</c> (<c>00047e90</c>): the plate blitted with flags 0 at the panel's
	/// corner; the title through slot <c>+8</c>; each widget's paint; then rows 0 to 6 refreshed.
	/// </summary>
	protected override void Paint() {
		Canvas.Blit(Plate, Region.X0, Region.Y0);
		SetTitleText(Title);
		foreach (var widget in Widgets) {
			widget.Paint();
		}

		for (int row = 0; row < RowCount; row++) {
			RefreshRow(row);
		}
	}

	/// <summary>
	/// <c>PreferencesPanel_RefreshRow</c> (<c>00047f84</c>)(panel, <paramref name="row"/>): rows 0, 1 and 3 put
	/// <c>OFF</c> or <c>ON</c> by their option on their readout, row 2 the message word by option 2 on readout 2, and rows
	/// 4, 5 and 6 hand option 4, 5 or 6 to their bar's slot <c>+8</c>. An option past its words, where retail reads past
	/// the table, draws no text, an engine guard.
	/// </summary>
	public void RefreshRow(int row) {
		var options = Options;
		switch (row) {
			case Music or Sounds or Horizon:
				_readouts[row].SetText(Canvas, Strings?.Text(OffOnGroup, options[row]));
				break;
			case Radio:
				_readouts[row].SetText(Canvas, Strings?.Text(RadioGroup, options[row]));
				break;
			case Sky or Ground or Shapes:
				_bars[row - Sky].RepaintToValue(Canvas, options[row]);
				break;
		}
	}

	/// <summary>
	/// The action's case of <c>PreferencesPanel_Run</c>'s pass: MUSIC, SOUNDS and HORIZON toggle their option; RADIO
	/// steps option 2 over its three values while <c>VoiceArchivePresent</c> is set; SKY, GROUND and SHAPES raise their
	/// option by <see cref="BarStep"/>, or lower it after a right-button click; each of these, RADIO only when it steps,
	/// sets its row's repaint counter to 2. Every write applies the option's handler. DONE as <see cref="Save"/> gives
	/// it, then setting the close byte 3. CONTROLS, whose controls panel's loop waits, is <see cref="RunControls"/>, which
	/// <see cref="Run"/> runs in its place.
	/// </summary>
	private void TakeAction(int action) {
		var options = Options;
		switch (action) {
			case Music or Sounds or Horizon:
				options.ToggleOption(true, action);
				_repaint[action] = 2;
				break;
			case Radio:
				if (options.VoiceArchivePresent) {
					options.StepOption(true, 3, Es1Preferences.RadioOption);
					_repaint[action] = 2;
				}

				break;
			case Sky or Ground or Shapes:
				if (!RightButton) {
					options.IncreaseOption(true, BarStep, action);
				} else {
					options.DecreaseOption(true, BarStep, action);
				}

				_repaint[action] = 2;
				break;
			case Done:
				Save();
				Close = 3;
				break;
		}
	}

	/// <summary>
	/// Action 7, CONTROLS: CONTROLS painted (slot <c>+4</c> of widget 7); the controls panel built with no rect, keeping
	/// the screen under itself (<c>ControlsPanel_Ctor</c>(a stack object, 0, 1)), and its loop run, each of its waits
	/// yielded; this panel painted again through its slot <c>+4</c>; the focus on the cancel widget, DONE; and the
	/// controls panel destroyed (<c>ControlsPanel_Dtor</c>(that, 2)).
	/// </summary>
	private IEnumerable<Es1Wait> RunControls() {
		Widgets[Controls].Paint();
		var controls = new Es1ControlsPanel(Globals);
		foreach (var wait in controls.Run(new StrongBox<int>())) {
			yield return wait;
		}

		Paint();
		SetFocus(Cancel);
		controls.Destroy();
	}

	/// <summary>
	/// <c>PreferencesPanel_Save</c> (<c>000483a0</c>): options 0 to 6, <see cref="SaveList"/>, written to
	/// <c>data\prefs.cfg</c> with <c>Prefs_SaveSelectedOptions</c>. <b>This engine's fix</b> (plan decision 13): retail
	/// writes them only when <c>Prefs_OptionChanged</c> finds one of the seven differing from its load-time copy, which
	/// the controls panel's DONE makes current, so a change made before CONTROLS was opened and closed is lost
	/// (docs/es1/simulation/preferences.md#saving); this one writes them whether or not one changed.
	/// </summary>
	private void Save() => Options.SaveSelectedOptions(SaveList);

	/// <summary>
	/// <c>PreferencesPanel_CallDrawHook</c> (<c>00048198</c>), nothing without a <see cref="DrawHook"/>: the page not on
	/// the screen, <c>+0x8c</c>, made the draw page and the hook called on it under the render context
	/// <see cref="Es1AlertPanel.Enter"/> found; the page on the screen made the source page; <see cref="Tick"/> on the
	/// draw page; <c>+0x8c</c> shown, which waits for the next vertical retrace while <c>Display_FlipWithoutPan</c> is
	/// clear, yielded as a retrace wait; <c>+0x88</c> drawn on, the two swapped, and the new <c>+0x88</c>, the page now
	/// on the screen, made the source and draw page. The save of the region with a screen buffer does not run: this
	/// panel keeps none.
	/// </summary>
	private IEnumerable<Es1Wait> CallDrawHook() {
		if (DrawHook == null) {
			yield break;
		}

		var display = Globals.Display;
		display.DrawPage = display.PairDrawn;
		DrawHook();
		display.SourcePage = display.PairVisible;
		Tick();
		if (display.ShowPage(display.PairDrawn)) {
			yield return Es1Wait.For(default);
		}

		display.DrawPage = display.PairVisible;
		(display.PairVisible, display.PairDrawn) = (display.PairDrawn, display.PairVisible);
		display.SourcePage = display.PairVisible;
		display.DrawPage = display.PairVisible;
	}

	/// <summary>
	/// <c>PreferencesPanel_Tick</c> (<c>00047f14</c>): each widget's per-frame call, then the first row whose repaint
	/// counter is not 0 refreshed and 1 taken off its counter.
	/// </summary>
	private void Tick() {
		foreach (var widget in Widgets) {
			widget.Tick();
		}

		for (int row = 0; row < RowCount; row++) {
			if (_repaint[row] != 0) {
				RefreshRow(row);
				_repaint[row]--;
				return;
			}
		}
	}
}
