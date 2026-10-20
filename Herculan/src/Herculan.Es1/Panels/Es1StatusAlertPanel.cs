using System.Runtime.CompilerServices;
using Herculan.Engine.Content;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The status panel, <c>StatusAlertPanel_Ctor</c> (<c>ES1DBSIM 00046efc</c>) and <c>GeneralAlertVtable</c>
/// (<c>0009507e</c>) (docs/es1/simulation/alert-panels.md#the-status-panel--statusalertpanel_ctor-00046efc): the pause
/// prompt (status 0), the quit prompt (status 1) and every mission alert, a title, up to four lines of text and one or
/// two buttons, all picked by the status from <c>str\GNL_ALRT.STR</c>, on a plate from <c>dba\gnl2alrt</c> for
/// statuses 0 and 1 and <c>dba\gnl_alrt</c> for the rest. Its loop answers 0 for the left button and 1 for the right.
/// </summary>
public sealed class Es1StatusAlertPanel : Es1AlertPanel {
	/// <summary>The panel's size, 222 × 109 on the 320 × 200 screen.</summary>
	public const int Width = 222, Height = 109;

	/// <summary>The rows of <c>str\GNL_ALRT.STR</c>, one a status: 19 titles, then two captions and four body lines each.</summary>
	public const int StatusCount = 19;

	/// <summary><c>P</c>'s status, <c>PAUSE</c>, and <c>Ctrl+Q</c>'s and <c>Ctrl+Esc</c>'s, <c>QUIT TO DOS?</c>.</summary>
	public const int PauseStatus = 0, QuitStatus = 1;

	/// <summary>The buttons' size and rows: 80 × 20, from <i>y</i> 70 to 90.</summary>
	public const int ButtonWidth = 80, ButtonTop = 70, ButtonBottom = 90;

	/// <summary>The body's four labels: <i>x</i> 50 to 172, rows 10 high from <i>y</i> 30.</summary>
	private const int BodyLeft = 50, BodyRight = 172, BodyTop = 30, BodyRow = 10, BodyLines = 4;

	/// <summary>The title bar: from <i>y</i> 4, 17 high, 20 wider than the title.</summary>
	private const int TitleTop = 4, TitleBarHeight = 0x11, TitleBarMargin = 20;

	/// <summary>The captions' and the title's vertical margins.</summary>
	private const int CaptionMarginY = -3, TitleMarginY = -11;

	/// <summary>The string table's three groups.</summary>
	private const int TitleGroup = 0, CaptionGroup = 1, BodyGroup = 2;

	private readonly Es1Label[] _body = new Es1Label[BodyLines];

	/// <summary>
	/// <c>StatusAlertPanel_Ctor</c>(panel, <paramref name="rect"/>, <paramref name="keepsScreen"/>,
	/// <paramref name="status"/>): 222 × 109, centered with no rect given and at the rect's corner with one; the string
	/// table's three groups; the buttons for the status (<see cref="ButtonColumns"/>), each with its caption label in
	/// <c>cpylw</c>, centered on it with the margin (0, −3), registered with the panel's root; the plate; the title label
	/// in <c>title</c> on a bar 20 pixels wider than the title, centered with the margin (0, −11); the four body labels in
	/// <c>green6x8</c>, centered; widget 0 the cancel widget; and for status 2 the alert cursor left up on leaving.
	/// </summary>
	public Es1StatusAlertPanel(Es1AlertGlobals globals, int status, Es1Rect? rect = null, bool keepsScreen = true)
		: base(globals, rect is { } at ? SetRect(Height, Width, at.Y0, at.X0) : CenterRect(Height, Width), keepsScreen) {
		Status = status;
		var content = globals.Content;
		var widgets = globals.Widgets;
		// str\gnl_alrt, through ResourcePath_BuildFolderName, into the base's string table.
		Strings = content != null ? SimStrings.LoadUntranslated(content, "GNL_ALRT.STR") : null;

		int[] columns = ButtonColumns(status);
		Plate = content != null
			? Es1WidgetResources.LoadFrames(content, status is PauseStatus or QuitStatus ? "GNL2ALRT" : "GNL_ALRT")?.FirstOrDefault()
			: null;
		for (int index = 0; index < columns.Length; index++) {
			var buttonRect = new Es1Rect(columns[index], ButtonTop, columns[index] + ButtonWidth, ButtonBottom);
			var button = new Es1PanelButton(index, this, buttonRect);
			var caption = new Es1Label(widgets.Cpylw);
			caption.SetRect(Es1Label.Centered, buttonRect, 0, CaptionMarginY);
			button.Caption = caption;
			Root.RegisterClickable(button);
			AddWidget(button);
		}

		Title = Text(TitleGroup, status);
		TitleLabel = new Es1Label(widgets.Title);
		int titleWidth = widgets.Title?.Measure(Title ?? "") ?? 0;
		int barLeft = (Region.X1 - Region.X0 - titleWidth - TitleBarMargin) >> 1;
		TitleBar = new Es1Rect(barLeft, TitleTop, barLeft + titleWidth + TitleBarMargin, TitleTop + TitleBarHeight);
		TitleLabel.SetRect(Es1Label.Centered, TitleBar, 0, TitleMarginY);

		for (int line = 0; line < BodyLines; line++) {
			_body[line] = new Es1Label(widgets.Green6x8);
			_body[line].SetRect(Es1Label.Centered,
				new Es1Rect(BodyLeft, BodyTop + BodyRow * line, BodyRight, BodyTop + BodyRow * (line + 1)));
		}

		Cancel = 0;
		if (status == 2) {
			RestoreCursorOnLeave = false;
		}
	}

	/// <summary>The status the panel shows.</summary>
	public int Status { get; }

	/// <summary><c>+0x318</c>: the title, <c>+0x334</c>[status].</summary>
	public string? Title { get; }

	/// <summary><c>+0x324</c>: the four body labels.</summary>
	public IReadOnlyList<Es1Label> BodyLabels => _body;

	/// <summary>
	/// The buttons' left columns for <paramref name="status"/>, the jump table <c>000950c0</c> behind an unsigned bound
	/// against <c>0x12</c>: one at <i>x</i> 71 for 0, 2, 3, 7, 8, <c>0x11</c> and <c>0x12</c>; two at 22 and 120 for 1, 4
	/// to 6, 9, 10 and <c>0xb</c> to <c>0x10</c>; none for any other.
	/// </summary>
	public static int[] ButtonColumns(int status) => (uint)status switch {
		0 or 2 or 3 or 7 or 8 or 0x11 or 0x12 => [71],
		1 or 4 or 5 or 6 or 9 or 10 or (>= 0xb and <= 0x10) => [22, 120],
		_ => [],
	};

	/// <summary>Slot <c>+0xc</c>, <c>StatusAlertPanel_RunModal</c> (<c>0004730c</c>): the base's loop.</summary>
	public override IEnumerable<Es1Wait> Run(StrongBox<int> answer) => RunModal(answer);

	/// <summary><c>StatusAlertPanel_GetButtonCaption</c> (<c>0004755c</c>)(panel, <paramref name="index"/>): the status's caption <paramref name="index"/>, <c>+0x31c</c>[index].</summary>
	public override string? GetButtonCaption(int index) => Text(CaptionGroup, 2 * Status + index);

	/// <summary>The status's body line <paramref name="line"/>, of <c>+0x320</c>.</summary>
	public string? BodyLine(int line) => Text(BodyGroup, BodyLines * Status + line);

	/// <summary>
	/// <c>StatusAlertPanel_OnChildClick</c> (<c>000474e4</c>)(panel, flags, <paramref name="child"/>): the close byte 2
	/// for widget 0 and 3 for widget 1, so the loop answers 0 for the left button and 1 for the right.
	/// </summary>
	public override void OnChildClick(short flags, Es1Widget child) {
		switch (IndexOf(child)) {
			case 0:
				Close = 2;
				break;
			case 1:
				Close = 3;
				break;
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>StatusAlertPanel_Paint</c> (<c>000473c4</c>): the plate blitted with flags 0 at the panel's
	/// corner; the title through slot <c>+8</c>; the body's lines up to the first empty one, at most four, line
	/// <i>i</i> on label <i>i</i> + 1 when there is one line and on label <i>i</i> otherwise, so a single line stands on
	/// the second row; then each widget's paint.
	/// </summary>
	protected override void Paint() {
		Canvas.Blit(Plate, Region.X0, Region.Y0);
		SetTitleText(Title);
		int lines = 0;
		while (lines < BodyLines && !string.IsNullOrEmpty(BodyLine(lines))) {
			lines++;
		}

		int first = lines == 1 ? 1 : 0;
		for (int line = 0; line < lines; line++) {
			_body[first + line].SetText(Canvas, BodyLine(line));
		}

		foreach (var widget in Widgets) {
			widget.Paint();
		}
	}

	/// <summary>String <paramref name="index"/> of the table's group <paramref name="group"/>; none when the table or the string is missing.</summary>
	private string? Text(int group, int index) => Strings?.Text(group, index);
}
