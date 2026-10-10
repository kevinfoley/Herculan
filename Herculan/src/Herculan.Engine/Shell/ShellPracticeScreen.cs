using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The practice screen's seven buttons: the five parameter labels down the right-hand box, in the order
/// the builder constructs them, then the two under the boxes.
/// </summary>
public enum ShellPracticeButton {
	/// <summary><c>Damage</c>, <c>PracticeScreen_OnDamage</c> (<c>0044bf29</c>): steps option <c>0x26</c>, <c>Vulnerable</c>/<c>Invulnerable</c>.</summary>
	Damage = 0,

	/// <summary><c>Ammo</c>, <c>PracticeScreen_OnAmmo</c> (<c>0044bfe6</c>): steps option <c>0x25</c>, <c>Limited</c>/<c>Unlimited</c>.</summary>
	Ammo = 1,

	/// <summary><c>Mission Difficulty</c>, <c>PracticeScreen_OnDifficulty</c> (<c>0044c0a3</c>): steps option <c>0x27</c>, <c>ROOKIE</c> to <c>ELITE</c>.</summary>
	Difficulty = 2,

	/// <summary><c>Time of Day</c>, <c>PracticeScreen_OnTimeOfDay</c> (<c>0044c160</c>): steps option <c>0x29</c>, <c>Day</c>/<c>Night</c>.</summary>
	TimeOfDay = 3,

	/// <summary><c>Herc Type</c>, <c>PracticeScreen_OnHercType</c> (<c>0044c21d</c>): steps option <c>0x28</c> through the nine chassis. Live from row 4 down.</summary>
	HercType = 4,

	/// <summary><c>Main Menu</c>, <c>0044c2da</c>: hides the screen and puts the main menu back up.</summary>
	MainMenu = 5,

	/// <summary><c>Begin Mission</c>, <c>0044c396</c>.</summary>
	BeginMission = 6,
}

/// <summary>
/// The screen the main menu's <c>PRACTICE MISSIONS</c> opens — a list of the eight practice missions
/// beside a box of five mission parameters, each a <c>prefs.cfg</c> option. Built once at startup by
/// <c>PracticeScreen_Build</c> (<c>0044ac80</c>), put up by <c>PracticeScreen_Show</c>
/// (<c>0044bc92</c>) and taken down by <c>PracticeScreen_Hide</c> (<c>0044bcb3</c>); its selection is
/// moved by <c>PracticeScreen_SelectRow</c> (<c>0044bd7c</c>). See
/// docs/retail/shell/main-menu.md#the-practice-missions-screen.
///
/// <para>Every rect is a literal in the executable, kept parent-relative as the builder writes it: the
/// content panel in the canvas, the two boxes and the two lower buttons in the panel, and the rest in
/// whichever box holds them.</para>
/// </summary>
public sealed class ShellPracticeScreen {
	/// <summary>How many practice missions the list offers — rows 0-7, <c>estext.bin</c> <c>0xf2</c>-<c>0xf9</c>.</summary>
	public const int RowCount = 8;

	/// <summary>The content panel, in the canvas.</summary>
	public static readonly ShellRect PanelRect = new(0x4d, 0xa2, 0x232, 0x170);

	/// <summary>The mission list and the parameter box, in the content panel.</summary>
	private static readonly ShellRect ListRect = new(6, 0x1a, 0xcf, 0xad);
	private static readonly ShellRect ParameterRect = new(0xd4, 0x1a, 0x1df, 0xad);

	/// <summary><c>Main Menu</c> and <c>Begin Mission</c>, in the content panel.</summary>
	private static readonly ShellRect MainMenuRect = new(0x56, 0xb5, 0xb8, 0xc4);
	private static readonly ShellRect BeginMissionRect = new(0x12e, 0xb5, 400, 0xc4);

	/// <summary><c>Practice Missions:</c>, in the list; <c>Mission Parameters</c>, in the parameter box.</summary>
	private static readonly ShellRect ListTitleRect = new(0x28, 4, 0xa2, 0x10);
	private static readonly ShellRect ParameterTitleRect = new(0x49, 6, 0xcf, 0x12);

	/// <summary>
	/// The five parameter rows, in the parameter box: a live label button, a centred colon, and a dead
	/// button used as a readout, each row <c>0x16</c> below the last from <c>0x1d</c>. The readouts sit
	/// one pixel lower and one taller than the labels.
	/// </summary>
	private const int LabelLeft = 6;
	private const int LabelRight = 0x90;
	private const int ColonLeft = 0x93;
	private const int ColonRight = 0x99;
	private const int ReadoutLeft = 0x9d;
	private const int ReadoutRight = 0x104;
	private const int FirstParameterTop = 0x1d;
	private const int ParameterPitch = 0x16;
	private const int LabelHeight = 0xf;
	private const int FirstReadoutTop = 0x1c;
	private const int ReadoutHeight = 0x11;

	/// <summary>The rows, in the list: 13 tall on a 12-pixel pitch, so the lower row owns the shared line.</summary>
	private const int RowLeft = 0xe;
	private const int RowRight = 200;
	private const int FirstRowTop = 0x15;
	private const int RowPitch = 0xc;
	private const int RowBottomOffset = 0xc;

	/// <summary>
	/// Where <c>ESQuad_AddColumns</c> is told to cut a row: the name fills the first column, <c>2</c> to
	/// <c>0xb8</c>, left-aligned, and the other three are empty — the next two zero-wide.
	/// </summary>
	private const int NameColumnRight = 0xb8;

	/// <summary>
	/// The <c>prefs.cfg</c> option each parameter steps, its modulus, and the first <c>estext.bin</c>
	/// entry of the run its readout prints — the value is added to it. Indexed by
	/// <see cref="ShellPracticeButton"/>. See docs/retail/shell/main-menu.md, "The parameters".
	/// </summary>
	private static readonly (int Option, int Modulus, int FirstText)[] Parameters = {
		(0x26, 2, 0x128),
		(0x25, 2, 0x12a),
		(0x27, 4, 0x35),
		(0x29, 2, 0x12c),
		(HercTypeOption, 9, 0x6e),
	};

	/// <summary><c>prefs.cfg</c> option 40, the chassis the player takes on a practice row from 4 down.</summary>
	public const int HercTypeOption = 0x28;

	/// <summary>
	/// The chassis each row writes into <see cref="HercTypeOption"/> when it is selected — the low byte of
	/// each <c>int16</c> at <c>00479bba</c>, which <c>0044bd7c</c> reads as a byte at <c>row * 2</c>. The
	/// eight practice rows, then the three <c>INSTANT ACTION</c> plays past them.
	/// </summary>
	private static readonly byte[] RowChassis = { 0, 0, 1, 8, 4, 2, 7, 3, 5, 7, 3 };

	/// <summary>
	/// How many rows <see cref="SelectRow"/> takes: the eight on the list and <c>INSTANT ACTION</c>'s three,
	/// stage 0 of <c>gam\career.dat</c> in order.
	/// </summary>
	public const int SelectableRowCount = 11;

	/// <summary>The first row whose Herc Type is the player's choice; rows above it grey the label out.</summary>
	private const int FirstChassisChoiceRow = 4;

	/// <summary>
	/// Herc Type's enable flag, which <see cref="SelectRow"/> writes for a listed row and leaves alone for
	/// the three past the list.
	/// </summary>
	private bool _hercTypeEnabled;

	private readonly SimulatorPreferences _options;

	/// <summary>Builds the screen over the shell's option array, which its five parameters step.</summary>
	public ShellPracticeScreen(SimulatorPreferences options) => _options = options;

	/// <summary>
	/// <c>PracticeScreen_SelectedRow</c> (<c>00479bb8</c>) — the practice row that is lit. <c>-1</c> in the image, so the first
	/// <see cref="Show"/> lights row 0. A new career in training mode starts on this mission of stage 0
	/// (<c>Career_SeedPosition</c> (<c>00412a2f</c>)).
	/// </summary>
	public int SelectedRow { get; private set; } = -1;

	/// <summary>
	/// <c>PracticeScreen_Show</c> (<c>0044bc92</c>): shows the root and the panel and selects row 0 — so
	/// the screen always comes up on <c>Basic Training 1</c>.
	/// </summary>
	public void Show() => SelectRow(0);

	/// <summary>
	/// <c>PracticeScreen_SelectRow</c> (<c>0044bd7c</c>), which row <c>i</c>'s handler
	/// <c>PracticeScreen_OnRow0</c>-<c>7</c> (<c>0044c413</c>-<c>0044c6ba</c>) calls. The row already lit is a no-op. Otherwise the old row goes back to <c>0x27</c>,
	/// the new one is lit <c>0x29</c>, <c>Herc Type</c> is greyed for rows 0-3 and lit from 4, and the
	/// row's chassis is written into option 40 — whatever Herc Type was stepped to before. Returns
	/// whether the selection moved.
	///
	/// <para>A row past the list, which only <c>INSTANT ACTION</c> selects, puts the old row out, lights
	/// none and leaves Herc Type's greying as it was; its chassis still goes into option 40.</para>
	/// </summary>
	public bool SelectRow(int row) {
		if (row == SelectedRow || row < 0 || row >= SelectableRowCount) {
			return false;
		}

		if (row < RowCount) {
			_hercTypeEnabled = row >= FirstChassisChoiceRow;
		}

		SelectedRow = row;
		_options.Set(HercTypeOption, RowChassis[row]);
		return true;
	}

	/// <summary>
	/// A parameter label's handler: the left release steps its option forward and any other release
	/// steps it back (<c>ShellOptions_StepOption</c>, <c>ShellOptions_StepOptionBack</c>), and the
	/// readout is rewritten. The array is only written to <c>prefs.cfg</c> by <c>Begin Mission</c>.
	/// </summary>
	public void Step(ShellPracticeButton button, bool forward) {
		if (button > ShellPracticeButton.HercType) {
			return;
		}

		var (option, modulus, _) = Parameters[(int)button];
		_options.Step(option, modulus, forward);
	}

	/// <summary>The value a parameter's readout prints, as its <c>estext.bin</c> index.</summary>
	public int ValueText(ShellPracticeButton button) {
		var (option, _, firstText) = Parameters[(int)button];
		return firstText + _options[option];
	}

	/// <summary>
	/// Whether a button answers a click. Only <c>Herc Type</c> is ever greyed, by the greying trio
	/// <c>0044bd7c</c> writes on it for rows 0-3, whose missions bring their own machine.
	/// </summary>
	public bool IsEnabled(ShellPracticeButton button) =>
		button != ShellPracticeButton.HercType || _hercTypeEnabled;

	/// <summary>One practice row's rect, in the canvas.</summary>
	public static ShellRect RowRect(int row) {
		var list = Inside(PanelRect, ListRect);
		return new ShellRect(list.X0 + RowLeft, list.Y0 + row * RowPitch + FirstRowTop,
			list.X0 + RowRight, list.Y0 + row * RowPitch + FirstRowTop + RowBottomOffset);
	}

	/// <summary>One button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellPracticeButton button) => button switch {
		ShellPracticeButton.MainMenu => Inside(PanelRect, MainMenuRect),
		ShellPracticeButton.BeginMission => Inside(PanelRect, BeginMissionRect),
		_ => ParameterRow((int)button, LabelLeft, LabelRight, FirstParameterTop, LabelHeight),
	};

	/// <summary>A parameter's readout box, in the canvas.</summary>
	private static ShellRect ReadoutRect(ShellPracticeButton button) =>
		ParameterRow((int)button, ReadoutLeft, ReadoutRight, FirstReadoutTop, ReadoutHeight);

	private static ShellRect ColonRect(ShellPracticeButton button) =>
		ParameterRow((int)button, ColonLeft, ColonRight, FirstParameterTop, LabelHeight);

	private static ShellRect ParameterRow(int row, int left, int right, int firstTop, int height) {
		var box = Inside(PanelRect, ParameterRect);
		int top = firstTop + row * ParameterPitch;
		return new ShellRect(box.X0 + left, box.Y0 + top, box.X0 + right, box.Y0 + top + height);
	}

	/// <summary>
	/// What the pointer hits: a practice row and which of its text columns, or a live button. Rows are hit
	/// newest first, so the lower of two answers on the line they share. A greyed Herc Type and the five
	/// readouts are disabled and swallow a click, which here is the same as hitting nothing.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		for (int row = RowCount - 1; row >= 0; row--) {
			var rect = RowRect(row);
			if (rect.Contains(canvasX, canvasY)) {
				return ShellHit.ListRow(new ShellWidget(ShellWidgetKind.PracticeRow, row), rect,
					NameColumnRight, NameColumnRight, NameColumnRight, canvasX, canvasY);
			}
		}

		foreach (var button in Enum.GetValues<ShellPracticeButton>()) {
			if (IsEnabled(button) && ButtonRect(button).Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.PracticeButton, (int)button),
					ButtonRect(button), canvasX, canvasY);
			}
		}

		return null;
	}

	/// <summary>
	/// Draws the screen into <paramref name="surface"/>. The caller clears it first; the content panel's
	/// dithered body leaves every other pixel to the backdrop, as the main menu's does.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellChrome.PaintTitledPanel(surface, PanelRect, Border, PanelFace, ShellChrome.InteriorColor, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface, new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight),
			font, text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		PaintList(surface, font, text);
		PaintParameters(surface, font, text, lit);

		PaintButton(surface, font, text, ShellPracticeButton.MainMenu, MainMenuText, lit);
		PaintButton(surface, font, text, ShellPracticeButton.BeginMission, BeginMissionText, lit);
	}

	/// <summary>
	/// The mission list: a framed box whose face the builder flattens to <c>0x10</c>, its opaque title,
	/// and the eight rows. A row's border is <c>0x10</c> lit or not, so only its name shows the
	/// selection. The lit row is painted last, as the original's incoming repaint lands last.
	/// </summary>
	private void PaintList(ShellSurface surface, HudFont? font, ShellText? text) {
		var list = Inside(PanelRect, ListRect);
		ShellChrome.PaintFramedPanel(surface, list, Border, ShellChrome.InteriorColor, fill: true);
		ShellChrome.PaintText(surface, Inside(list, ListTitleRect), font, text?.Text(ListTitleText), ShellTextAlign.Center,
			LabelColor, ShellChrome.InteriorColor);

		for (int row = 0; row < RowCount; row++) {
			if (row != SelectedRow) {
				PaintRow(surface, font, text, row);
			}
		}

		if (SelectedRow is >= 0 and < RowCount) {
			PaintRow(surface, font, text, SelectedRow);
		}
	}

	/// <summary>
	/// One row — <c>ESQuad_Paint</c> (<c>0040a600</c>): the interior cleared, the <c>0x10</c> border,
	/// and the name in its opaque first column.
	/// </summary>
	private void PaintRow(ShellSurface surface, HudFont? font, ShellText? text, int row) {
		var rect = RowRect(row);
		ShellChrome.PaintPanel(surface, rect, ShellChrome.InteriorColor, fill: true);
		ShellChrome.PaintText(surface, new ShellRect(rect.X0 + 2, rect.Y0, rect.X0 + NameColumnRight, rect.Y1), font,
			text?.Text(FirstRowText + row), ShellTextAlign.Left, row == SelectedRow ? LitColor : RowColor,
			ShellChrome.InteriorColor);
	}

	/// <summary>
	/// The parameter box: a framed box keeping a visible <c>0x0f</c> checkerboard, its title with no
	/// backing, and five rows of label, colon and readout. A readout is a <c>Button</c> the builder
	/// disables and gives border <c>0x13</c> and an opaque caption in <c>0x17</c>.
	/// </summary>
	private void PaintParameters(ShellSurface surface, HudFont? font, ShellText? text, ShellWidget? lit) {
		var box = Inside(PanelRect, ParameterRect);
		ShellChrome.PaintFramedPanel(surface, box, Border, ParameterFace, fill: true);
		ShellChrome.PaintText(surface, Inside(box, ParameterTitleRect), font, text?.Text(ParameterTitleText),
			ShellTextAlign.Center, LabelColor);

		for (var button = ShellPracticeButton.Damage; button <= ShellPracticeButton.HercType; button++) {
			PaintButton(surface, font, text, button, FirstParameterText + (int)button, lit);
			ShellChrome.PaintText(surface, ColonRect(button), font, Colon, ShellTextAlign.Center, ColonColor);

			ShellChrome.PaintButton(surface, ReadoutRect(button), ReadoutBorder, font, text?.Text(ValueText(button)),
				ReadoutTextColor, backingColor: ShellChrome.InteriorColor);
		}
	}

	/// <summary>A live button, greyed with its caption when it is not enabled.</summary>
	private void PaintButton(ShellSurface surface, HudFont? font, ShellText? text, ShellPracticeButton button,
			int captionText, ShellWidget? lit) {
		bool enabled = IsEnabled(button);
		ShellChrome.PaintButton(surface, ButtonRect(button), enabled ? ButtonBorder : DisabledColor, font,
			text?.Text(captionText), enabled ? ShellChrome.FontInkColor : DisabledColor,
			pressed: enabled && lit == new ShellWidget(ShellWidgetKind.PracticeButton, (int)button));
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary>The border on the content panel and both boxes.</summary>
	private const byte Border = 0x15;

	/// <summary>The content panel's header face; its body is dithered in <c>0x10</c> over the backdrop.</summary>
	private const byte PanelFace = 0x25;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0xaa;
	private const int TitlePlateLast = 0x13a;

	/// <summary>The parameter box's checkerboard, where the list's is flattened to <c>0x10</c>.</summary>
	private const byte ParameterFace = 0x0f;

	/// <summary>The five colons, the two-byte strings from <c>00479b95</c>, centred in <c>0x28</c>.</summary>
	private const string Colon = ":";
	private const byte ColonColor = 0x28;

	private const byte LabelColor = 0x1a;
	private const byte RowColor = 0x27;
	private const byte LitColor = 0x29;
	private const byte ButtonBorder = 0x22;
	private const byte DisabledColor = 0x26;
	private const byte ReadoutBorder = 0x13;
	private const byte ReadoutTextColor = 0x17;

	/// <summary><c>estext.bin</c> indices the screen prints.</summary>
	private const int TitleText = 0xf0;
	private const int ListTitleText = 0xf1;
	private const int FirstRowText = 0xf2;
	private const int ParameterTitleText = 0xfa;
	private const int FirstParameterText = 0xfb;
	private const int MainMenuText = 0x100;
	private const int BeginMissionText = 0x102;
}
