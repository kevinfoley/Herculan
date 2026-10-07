namespace Herculan.Engine.Cockpit;

/// <summary>
/// Where every piece of the mission-status alert sits — <c>gnl_alrt</c>
/// (<c>StatusAlertPanel_Ctor</c>, <c>00455934</c>), the panel [Q] raises and the one a decided
/// mission raises for itself. Same arrangement as <see cref="ObjectivesPanelLayout"/>: the
/// constructor writes the block into <c>.bss</c> (<c>DAT_004d1f20</c>..<c>DAT_004d1f42</c>) once as
/// <c>value &lt;&lt; VideoMode_?CoordShift</c>, so every number here is an authored 320-wide
/// coordinate doubled. See docs/retail/simulation/alert-panels.md.
/// </summary>
public static class StatusAlertPanelLayout {
	/// <summary>The panel's declared width, <c>0xde</c> doubled. Also the plate's own width.</summary>
	public const int Width = 444;

	/// <summary>
	/// The panel's declared height, <c>0x6d</c> doubled. The plate is 214, four rows shorter — the
	/// same slack <see cref="ObjectivesPanelLayout.Height"/> carries, and the panel is centred by
	/// this number rather than by the art.
	/// </summary>
	public const int Height = 218;

	/// <summary>The background plate's height, from <c>hba\GNL_ALRT.HBA</c> frame 0.</summary>
	public const int PlateHeight = 214;

	/// <summary>The panel's own plate bank. Frame 0 is this panel; frame 1 is the small pause panel's.</summary>
	public const string PlateBank = "GNL_ALRT";

	/// <summary>Frame of <see cref="PlateBank"/> this panel blits.</summary>
	public const int PlateFrame = 0;

	/// <summary>The title bar's height — the band the title is vertically centred in.</summary>
	public const int TitleHeight = 16;

	/// <summary>Body rows the panel builds labels for. A status names at most four lines.</summary>
	public const int BodyRowCount = 4;

	/// <summary>The body block's left and right edges, <c>0x32</c> and <c>0x32 + 0x7a</c> doubled.</summary>
	public const int BodyX0 = 100;

	/// <inheritdoc cref="BodyX0"/>
	public const int BodyX1 = BodyX0 + 244;

	/// <summary>First body row's top edge.</summary>
	public const int FirstBodyY = 60;

	/// <summary>A body row's height, and the pitch between them. Both are 10 doubled.</summary>
	public const int BodyRowHeight = 20;

	/// <summary>
	/// The body's font — <c>DAT_004d1eb0</c>, the green 6x8 face, which is why this panel's text is
	/// green where the objectives panel's is yellow.
	/// </summary>
	public const string BodyFont = "GREEN6X8";

	/// <summary>Every button's top edge, height and width: <c>0x50</c>, <c>9</c> and <c>0x3d</c> doubled.</summary>
	public const int ButtonY = 160;

	/// <inheritdoc cref="ButtonY"/>
	public const int ButtonHeight = 18;

	/// <inheritdoc cref="ButtonY"/>
	public const int ButtonWidth = 122;

	/// <summary>The one button's left edge when the status has only one.</summary>
	public const int SingleButtonX = 160;

	/// <summary>The two buttons' left edges when it has two.</summary>
	public static readonly int[] PairButtonX = { 82, 240 };

	/// <summary>The panel's left edge on the screen.</summary>
	public const int ScreenLeft = (AlertPanelLayout.ScreenWidth - Width) / 2;

	/// <summary>
	/// The rect of button <paramref name="index"/> of <paramref name="buttonCount"/>. The
	/// constructor picks the x set from the button count and gives every button the same y and size.
	/// </summary>
	public static AlertPanelLayout.Rect Button(int index, int buttonCount) {
		int x0 = buttonCount <= 1 ? SingleButtonX : PairButtonX[Math.Clamp(index, 0, 1)];
		return new AlertPanelLayout.Rect(x0, ButtonY, x0 + ButtonWidth, ButtonY + ButtonHeight);
	}

	/// <summary>The rect body row <paramref name="index"/> is centred in.</summary>
	public static AlertPanelLayout.Rect BodyRow(int index) =>
		new(BodyX0, FirstBodyY + BodyRowHeight * index,
			BodyX1, FirstBodyY + BodyRowHeight * index + BodyRowHeight);

	/// <summary>The title's rect, sized to the measured text and centred in the panel's local width.</summary>
	public static AlertPanelLayout.Rect Title(int measuredWidth) {
		int x0 = (Width - measuredWidth) / 2;
		return new AlertPanelLayout.Rect(x0, 0, x0 + measuredWidth, TitleHeight);
	}

	/// <summary>
	/// Which body row a body of <paramref name="lineCount"/> lines starts on. A single line starts on
	/// row 1 rather than row 0, so it sits nearer the middle of the plate instead of at the top of
	/// the block; anything longer starts on row 0. <c>StatusAlertPanel_Paint</c>'s own
	/// <c>lineCount == 1</c> test.
	/// </summary>
	public static int FirstRowFor(int lineCount) => lineCount == 1 ? 1 : 0;

	/// <summary>Where the panel lands in a window of the given size.</summary>
	public static AlertPanelLayout.Placement Place(int windowWidth, int windowHeight) =>
		AlertPanelLayout.Placement.Create(windowWidth, windowHeight, Width, Height);
}
