using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File;
using Herculan.Engine.Content;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The objectives panel <c>F11</c> puts up, <c>ObjectivesPanel_Ctor</c> (<c>ES1DBSIM 0004a1b4</c>) and
/// <c>ObjectiveAlertVtable</c> (<c>000955d6</c>) (docs/es1/simulation/alert-panels.md#the-objectives-panel--objectivespanel_ctor-0004a1b4):
/// the title <c>OBJECTIVES</c> and the caption <c>RETURN</c> from <c>str\OBJ_ALRT.STR</c>, as many as seven lines from
/// the one group of <c>data\object.str</c>, which the shell writes from the mission's <c>.ENG</c> file
/// (docs/es1/formats/eng-mission-text.md#the-objectives), left-aligned, on the plate <c>dba\hst_alrt</c>, with one
/// button, <c>RETURN</c>. Its loop answers 1.
/// </summary>
public sealed class Es1ObjectivesPanel : Es1AlertPanel {
	/// <summary>The panel's size, 315 × 109 on the 320 × 200 screen.</summary>
	public const int Width = 315, Height = 109;

	/// <summary>The line labels at <c>+0x382</c>, so the most lines shown.</summary>
	public const int MaxLines = 7;

	/// <summary>The shell's objectives file, file-name entry 6, in the install's <c>data\</c>.</summary>
	public const string ObjectivesName = "object.str";

	/// <summary><c>RETURN</c>'s rect.</summary>
	public static readonly Es1Rect ButtonRect = new(127, 93, 187, 103);

	/// <summary>The lines' labels: <i>x</i> 20 to 200, rows 10 high from <i>y</i> 17.</summary>
	private const int LineLeft = 20, LineRight = 200, LineTop = 17, LineRow = 10;

	/// <summary>The title's rect: from <i>y</i> 3 to 13, as wide as the title.</summary>
	private const int TitleTop = 3, TitleBottom = 13;

	/// <summary>The title bar's rect, −20 in all four words.</summary>
	private const int TitleBarWord = -20;

	/// <summary>The caption's and the title's vertical margin.</summary>
	private const int MarginY = -1;

	/// <summary>The flags the line labels are placed with: neither 2 nor 4, so left-aligned.</summary>
	private const int LineFlags = 1;

	/// <summary><c>str\obj_alrt</c>'s two groups of one string each.</summary>
	private const int TitleGroup = 0, CaptionGroup = 1;

	private readonly Es1Label[] _lines = new Es1Label[MaxLines];

	/// <summary>
	/// <c>ObjectivesPanel_Ctor</c>(panel, <paramref name="rect"/>, <paramref name="keepsScreen"/>): 315 × 109, centered
	/// with no rect given and at the rect's corner with one; the plate; the title and the caption from
	/// <c>str\obj_alrt</c>; the objectives from <c>data\object.str</c> (<see cref="Objectives"/>); the title label in
	/// <c>green6x8</c>, centered with the margin (0, −1) on a rect as wide as the title at <i>y</i> 3 to 13, and the title
	/// bar's rect −20 in all four words, so the frame <see cref="Es1AlertPanel.SetTitleText"/> draws lies outside the
	/// panel and the canvas cuts it all away; <c>RETURN</c>, its caption label in <c>cpylw</c> centered with the margin
	/// (0, −1), registered with the panel's root and made widget 0 and the cancel widget; and the seven line labels in
	/// <c>cpylw</c>, left-aligned.
	/// </summary>
	public Es1ObjectivesPanel(Es1AlertGlobals globals, Es1Rect? rect = null, bool keepsScreen = true)
		: base(globals, rect is { } at ? SetRect(Height, Width, at.Y0, at.X0) : CenterRect(Height, Width), keepsScreen) {
		var content = globals.Content;
		var widgets = globals.Widgets;
		Plate = content != null ? Es1WidgetResources.LoadFrames(content, "HST_ALRT")?.FirstOrDefault() : null;

		// str\obj_alrt, through ResourcePath_BuildFolderName, into the base's string table.
		Strings = content != null ? SimStrings.LoadUntranslated(content, "OBJ_ALRT.STR") : null;
		Title = Strings?.Text(TitleGroup, 0);
		Caption = Strings?.Text(CaptionGroup, 0);
		Objectives = ReadObjectives(globals.InstallRoot);

		TitleLabel = new Es1Label(widgets.Green6x8);
		int titleWidth = widgets.Green6x8?.Measure(Title ?? "") ?? 0;
		int titleLeft = (Width - titleWidth) >> 1;
		TitleLabel.SetRect(Es1Label.Centered, new Es1Rect(titleLeft, TitleTop, titleLeft + titleWidth, TitleBottom), 0, MarginY);
		TitleBar = new Es1Rect(TitleBarWord, TitleBarWord, TitleBarWord, TitleBarWord);

		var button = new Es1PanelButton(0, this, ButtonRect);
		var caption = new Es1Label(widgets.Cpylw);
		caption.SetRect(Es1Label.Centered, ButtonRect, 0, MarginY);
		button.Caption = caption;
		Root.RegisterClickable(button);
		AddWidget(button);

		for (int line = 0; line < MaxLines; line++) {
			_lines[line] = new Es1Label(widgets.Cpylw);
			_lines[line].SetRect(LineFlags, new Es1Rect(LineLeft, LineTop + LineRow * line, LineRight, LineTop + LineRow * (line + 1)));
		}

		Cancel = 0;
	}

	/// <summary><c>+0x328</c>: the title, <c>OBJECTIVES</c> in the retail table.</summary>
	public string? Title { get; }

	/// <summary><c>+0x32c</c>: the button's caption, <c>RETURN</c> in the retail table.</summary>
	public string? Caption { get; }

	/// <summary>
	/// The second string table <c>+0x318</c>'s group 0, which the pointers from <c>+0x330</c> point into, its count the
	/// <c>int16</c> at <c>+0x380</c>. Retail's pointers have room for 20 and the load writes as many as the file holds; the
	/// retail <c>.ENG</c> files' blocks hold 12 at most, and here the list holds the file's count.
	/// </summary>
	public IReadOnlyList<string> Objectives { get; }

	/// <summary><c>+0x382</c>: the seven line labels.</summary>
	public IReadOnlyList<Es1Label> LineLabels => _lines;

	/// <summary>
	/// What <c>ObjectivesPanel_Paint</c> puts on the line labels, in their order: each of <paramref name="objectives"/>
	/// that is not empty, until seven are set, so an empty line leaves no gap and an eighth is not shown.
	/// </summary>
	public static IReadOnlyList<string> LinesShown(IReadOnlyList<string> objectives) =>
		objectives.Where(line => line.Length != 0).Take(MaxLines).ToArray();

	/// <summary>Slot <c>+0xc</c>, <c>ObjectivesPanel_RunModal</c> (<c>0004a4ec</c>): the base's loop.</summary>
	public override IEnumerable<Es1Wait> Run(StrongBox<int> answer) => RunModal(answer);

	/// <summary><c>ObjectivesPanel_GetButtonCaption</c> (<c>0004a710</c>)(panel, index): <see cref="Caption"/>, whatever the index.</summary>
	public override string? GetButtonCaption(int index) => Caption;

	/// <summary>
	/// <c>ObjectivesPanel_OnChildClick</c> (<c>0004a6a0</c>)(panel, flags, <paramref name="child"/>): the close byte 1 for
	/// widget 0, so the loop answers 1.
	/// </summary>
	public override void OnChildClick(short flags, Es1Widget child) {
		if (IndexOf(child) == 0) {
			Close = 1;
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>ObjectivesPanel_Paint</c> (<c>0004a5a4</c>): the plate blitted with flags 0 at the panel's
	/// corner, its columns past the panel cut away by the canvas; the title through slot <c>+8</c>; the lines
	/// <see cref="LinesShown"/> gives on the line labels in order; then each widget's paint.
	/// </summary>
	protected override void Paint() {
		Canvas.Blit(Plate, Region.X0, Region.Y0);
		SetTitleText(Title);
		var shown = LinesShown(Objectives);
		for (int line = 0; line < shown.Count; line++) {
			_lines[line].SetText(Canvas, shown[line]);
		}

		foreach (var widget in Widgets) {
			widget.Paint();
		}
	}

	/// <summary>
	/// <c>data\object.str</c> read with <c>StringTable_LoadFromFile</c> as a string table, and its group 0 taken with
	/// <c>StringTable_LoadGroup</c>. PLACEHOLDER: with no install, or with the file missing or not a string table, no
	/// lines: what retail shows with the file missing is docs/es1/simulation/alert-panels.md#open's item on a missing
	/// <c>data\object.str</c>, and a file that does not parse is not read there.
	/// </summary>
	private static IReadOnlyList<string> ReadObjectives(string? installRoot) {
		if (installRoot == null) {
			return [];
		}

		string path = Es1MissionFiles.DataPath(installRoot, ObjectivesName);
		StringFile? table = File.Exists(path) ? SimStrings.Parse(File.ReadAllBytes(path)) : null;
		return table?.Group(0).Select(entry => entry.Text).ToArray() ?? [];
	}
}
