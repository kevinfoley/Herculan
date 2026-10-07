namespace Herculan.Engine.Cockpit;

/// <summary>
/// Where every piece of the CONTROLS panel sits — <c>ctl_alrt</c> (<c>ControlsPanel_Ctor</c>,
/// <c>00457d1c</c>), what the [F12] preferences panel's CONTROLS button raises. 370x372, centred on
/// the 640x480 screen over whatever is already there, so the preferences strip stays visible below
/// it.
///
/// <para>Its constructor takes an optional screen origin and falls back to
/// <c>AlertPanel_CenterRect</c> when it is given none; every reachable call site gives it none, so
/// the panel is centred. Every number below is an authored 320-wide coordinate doubled, as
/// <see cref="PreferencesPanelLayout"/>'s are.</para>
///
/// <para>Fourteen buttons — four axis rows, eight button rows, then RECOMMEND and DONE — over three
/// widget sizes, each with its own four-frame set in the panel's own bank. Twelve of them carry a
/// value readout. To their right sits the OPTIONS list, a title and twelve more labels that hold the
/// action list of whichever button row is selected.</para>
/// </summary>
public static class ControlsPanelLayout {
	/// <summary>The panel's declared width, <c>0xb9</c> doubled. The plate is 372 — two columns wider.</summary>
	public const int Width = 370;

	/// <summary>The panel's declared height, <c>0xba</c> doubled. The plate is 374.</summary>
	public const int Height = 372;

	/// <summary>The plate, <c>hba\CTL_ALRT.HBA</c> frame 0. The two group boxes and every readout's black well are painted into it.</summary>
	public const string PlateBank = "CTL_ALRT";

	/// <inheritdoc cref="PlateBank"/>
	public const int PlateFrame = 0;

	/// <summary>The title bar's height — the band the title is vertically centred in.</summary>
	public const int TitleHeight = 16;

	/// <summary>How many buttons the panel builds.</summary>
	public const int ButtonCount = 14;

	/// <summary>How many of those carry a value readout: the four axis rows and the eight button rows.</summary>
	public const int RowCount = 12;

	/// <summary>How many of the rows are axis assignments. They come first.</summary>
	public const int AxisRowCount = 4;

	/// <summary>Index of the RECOMMEND button, which writes the recommended set over all twelve options.</summary>
	public const int RecommendButton = 12;

	/// <summary>
	/// Index of the DONE button, and the panel's cancel widget — the constructor sets <c>+0x2fb</c> to
	/// the last widget it built, so [Esc] presses this one.
	/// </summary>
	public const int DoneButton = 13;

	/// <summary>The readouts' and the option list's font, <c>DAT_004d1eb0</c>.</summary>
	public const string ValueFont = PreferencesPanelLayout.ValueFont;

	/// <summary>
	/// Both the readouts and the option rows are placed with this margin, the constructor's
	/// <c>2 &lt;&lt; XCoordShift</c> in <c>DAT_004d1fb4</c> — as a left inset for the readouts, which
	/// are left-aligned, and as a centre nudge for the option rows, which are not.
	/// </summary>
	public const int LabelMarginX = 4;

	/// <summary>How many rows the OPTIONS list holds. A button row with more actions than this cannot show them all.</summary>
	public const int OptionRowCount = 12;

	/// <summary>Button left edges, <c>DAT_0049e510</c> doubled.</summary>
	private static readonly int[] ButtonX =
		{ 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 180, 274 };

	/// <summary>Button top edges, <c>DAT_0049e52c</c> doubled. The axis rows are 24 apart and so are the button rows.</summary>
	private static readonly int[] ButtonY =
		{ 28, 52, 76, 100, 136, 160, 184, 208, 232, 256, 280, 304, 340, 340 };

	/// <summary>Button widths, <c>DAT_0049e548</c> doubled — the three sizes the bank carries art for.</summary>
	private static readonly int[] ButtonWidth =
		{ 136, 136, 136, 136, 68, 68, 68, 68, 68, 68, 68, 68, 90, 90 };

	/// <summary>Every button's height, <c>9</c> doubled. The art is 22 rows and overhangs it by 4.</summary>
	public const int ButtonHeight = 18;

	/// <summary>
	/// The first of each button's four plate frames, <c>DAT_0049e580</c> — 5 for the 138-wide axis
	/// art, 1 for the 70-wide button art and 9 for the 92-wide pair at the bottom. A button draws
	/// <c>base + state</c>, and the three sets do <b>not</b> order their four colours alike: 1-4 and
	/// 5-8 run rest, held, disabled, option, while 9-12 run option, rest, held, disabled. That is a
	/// property of the art, not of the paint, which indexes all three the same way.
	/// </summary>
	private static readonly int[] ButtonFirstFrame =
		{ 5, 5, 5, 5, 1, 1, 1, 1, 1, 1, 1, 1, 9, 9 };

	/// <summary>Readout left edges, <c>DAT_0049e5b8</c> doubled — three device pixels past their button's right edge.</summary>
	private static readonly int[] ValueX =
		{ 158, 158, 158, 158, 90, 90, 90, 90, 90, 90, 90, 90 };

	/// <summary>Readout top edges, <c>DAT_0049e5d0</c> doubled — each two rows below its button's.</summary>
	private static readonly int[] ValueY =
		{ 30, 54, 78, 102, 138, 162, 186, 210, 234, 258, 282, 306 };

	/// <summary>Readout widths, <c>DAT_0049e5e8</c> doubled.</summary>
	private static readonly int[] ValueWidth =
		{ 194, 194, 194, 194, 128, 128, 128, 128, 128, 128, 128, 128 };

	/// <summary>Every readout's height, <c>7</c> doubled.</summary>
	public const int ValueHeight = 14;

	/// <summary>The OPTIONS list's left and right edges, <c>0x71</c> and <c>0xb1</c> doubled.</summary>
	public const int OptionsLeft = 226;

	/// <inheritdoc cref="OptionsLeft"/>
	public const int OptionsRight = 354;

	/// <summary>
	/// The OPTIONS title's top edge, <c>0x45</c> doubled, and its height, <c>5</c> doubled. Unlike
	/// every other label on this panel it is given no background colour, which is why it can overlap
	/// the top border the plate paints across that band without erasing it.
	/// </summary>
	public const int OptionsTitleY = 138;

	/// <inheritdoc cref="OptionsTitleY"/>
	public const int OptionsTitleHeight = 10;

	/// <summary>The pitch between option rows, <c>7</c> doubled. The first row sits one pitch below the title.</summary>
	public const int OptionRowPitch = 14;

	/// <summary>The rect of button <paramref name="index"/>.</summary>
	public static AlertPanelLayout.Rect Button(int index) {
		int i = Math.Clamp(index, 0, ButtonCount - 1);
		return new AlertPanelLayout.Rect(ButtonX[i], ButtonY[i],
			ButtonX[i] + ButtonWidth[i], ButtonY[i] + ButtonHeight);
	}

	/// <summary>The rect of row <paramref name="index"/>'s value readout.</summary>
	public static AlertPanelLayout.Rect Value(int index) {
		int i = Math.Clamp(index, 0, RowCount - 1);
		return new AlertPanelLayout.Rect(ValueX[i], ValueY[i],
			ValueX[i] + ValueWidth[i], ValueY[i] + ValueHeight);
	}

	/// <summary>The OPTIONS list's title rect.</summary>
	public static AlertPanelLayout.Rect OptionsTitle() =>
		new(OptionsLeft, OptionsTitleY, OptionsRight, OptionsTitleY + OptionsTitleHeight);

	/// <summary>
	/// The rect of option row <paramref name="index"/>. The constructor advances its running rect
	/// <i>before</i> each <c>Label_SetRect</c> rather than after, so row 0 sits one whole pitch below
	/// the title rather than directly under it.
	/// </summary>
	public static AlertPanelLayout.Rect OptionRow(int index) {
		int y = OptionsTitleY + OptionRowPitch * (Math.Clamp(index, 0, OptionRowCount - 1) + 1);
		return new AlertPanelLayout.Rect(OptionsLeft, y, OptionsRight, y + OptionsTitleHeight);
	}

	/// <summary>The plate frame button <paramref name="index"/> draws in widget state <paramref name="state"/>.</summary>
	public static int ButtonFrame(int index, int state) =>
		ButtonFirstFrame[Math.Clamp(index, 0, ButtonCount - 1)] + Math.Clamp(state, 0, 3);

	/// <summary>The title's rect, sized to the measured text and centred in the panel's local width.</summary>
	public static AlertPanelLayout.Rect Title(int measuredWidth) {
		int x0 = (Width - measuredWidth) / 2;
		return new AlertPanelLayout.Rect(x0, 0, x0 + measuredWidth, TitleHeight);
	}

	/// <summary>Where the panel lands in a window of the given size.</summary>
	public static AlertPanelLayout.Placement Place(int windowWidth, int windowHeight) =>
		AlertPanelLayout.Placement.Create(windowWidth, windowHeight, Width, Height);
}
