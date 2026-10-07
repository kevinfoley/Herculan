using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>The four buttons along the foot of the mission tab, in the order the builder constructs them.</summary>
public enum ShellMissionButton {
	Briefing,
	Objectives,
	Intelligence,
	RockAndRoll,
}

/// <summary>
/// The mission tab's eight arrow buttons: six down the right of the <c>Mission Map</c> panel, named for
/// the art they draw in the reference capture, then the <c>Mission Summary</c> panel's two page buttons.
/// </summary>
public enum ShellMissionArrow {
	MapUp,
	MapDown,
	MapLeft,
	MapRight,
	MapInward,
	MapOutward,
	PageUp,
	PageDown,
}

/// <summary>
/// The career's three mission texts as <c>Career_BuildBriefingText</c> (<c>00412f97</c>) assembles them:
/// each the concatenation of the <c>mission.str</c> lines one of the career block's arrays names, in
/// array order, skipping <c>-1</c>. The shell reads <c>data\mission.str</c>, which
/// <c>Career_LoadSlot</c> copies from the slot's <c>sav\missn%d.str</c> and a mission load writes
/// (<see cref="ShellWorkingFiles"/>).
/// </summary>
public sealed record ShellMissionTexts(string Briefing, string Objectives, string Intelligence) {
	public static readonly ShellMissionTexts Empty = new(string.Empty, string.Empty, string.Empty);

	/// <summary>The texts of <paramref name="save"/>, assembled from its working <c>mission.str</c>, or <see cref="Empty"/>.</summary>
	public static ShellMissionTexts Load(ShellWorkingFiles working, PlayerSave? save) {
		string? path = working.Text;
		if (save == null || !File.Exists(path) || SimStrings.Parse(File.ReadAllBytes(path)) is not { GroupCount: > 0 } table) {
			return Empty;
		}

		// The string-table reader takes its count from the first group's header, so only group 0 is read;
		// a slot file can carry a stale tail past its content length, which the table never reaches.
		var lines = table.Group(0);
		string Assemble((short Count, short[] Lines) array) =>
			string.Concat(array.Lines.Where(line => line >= 0 && line < lines.Count).Select(line => lines[line].Text));

		return new ShellMissionTexts(Assemble(save.CareerBriefing), Assemble(save.CareerObjectives),
			Assemble(save.CareerIntelligence));
	}

	/// <summary>
	/// <c>Career_BuildDebriefText</c> (<c>004133d2</c>): the <c>mission.str</c> lines the debrief's thirty-entry
	/// array names, concatenated in array order, skipping <c>-1</c>, out of the <c>mission.str</c> the flown
	/// mission's reload wrote. Empty when that text has no strings.
	/// </summary>
	public static string AssembleDebrief(MissionDebriefText debrief) {
		if (SimStrings.Parse(debrief.MissionText) is not { GroupCount: > 0 } table) {
			return string.Empty;
		}

		var lines = table.Group(0);
		return string.Concat(debrief.Lines.Where(line => line >= 0 && line < lines.Count).Select(line => lines[line].Text));
	}
}

/// <summary>
/// <c>esreport.cpp</c>'s paginated text box: a 0x36-byte object, not a widget, that word-wraps a string
/// into one <c>Text</c> child of its parent per line and shows one page of them at a time. Built by
/// <c>TextBox_Ctor</c> (<c>0040cc0c</c>), filled by <c>TextBox_SetText</c> (<c>0040cc81</c>), and paged by
/// <c>TextBox_PageUp</c> (<c>0040d237</c>) and <c>TextBox_PageDown</c> (<c>0040d258</c>). See
/// docs/retail/shell/screen-layout.md, "The summary text box".
/// </summary>
public sealed class ShellTextBox {
	private List<string> _lines = new();

	/// <param name="rect">The box's rect in its parent, which every line's rect is cut from.</param>
	public ShellTextBox(ShellRect rect) => Rect = rect;

	public ShellRect Rect { get; }

	/// <summary>The wrapped lines, <c>+0x2a</c>.</summary>
	public IReadOnlyList<string> Lines => _lines;

	/// <summary><c>+0x22</c>: the box's height over the font's cell height.</summary>
	public int LinesPerPage { get; private set; }

	/// <summary><c>+0x20</c>, as <c>TextBox_SetText</c> computes it.</summary>
	public int PageCount { get; private set; }

	/// <summary><c>+0x1e</c>, the page shown.</summary>
	public int Page { get; private set; }

	/// <summary><c>+0x18</c>: whether the box shows a page at all.</summary>
	public bool Visible { get; set; }

	/// <summary>
	/// <c>TextBox_SetText</c>: clears the box, which hides it and puts it back on page 0, wraps the text to
	/// the box's width and counts its pages. The page count is <c>lines / perPage</c>, plus one unless
	/// <c>lines + 1 == perPage</c> — so a text exactly filling its pages gains an empty one, and a text one
	/// line short of filling its first page has none, which leaves the page buttons inert on it.
	/// </summary>
	public void SetText(string? text, HudFont? font) {
		Visible = false;
		Page = 0;
		if (font == null || font.CellHeight <= 0) {
			_lines = new List<string>();
			LinesPerPage = 0;
			PageCount = 0;
			return;
		}

		LinesPerPage = (Rect.Y1 - Rect.Y0) / font.CellHeight;
		_lines = Wrap(text ?? string.Empty, font, Rect.X1 - Rect.X0);
		PageCount = LinesPerPage > 0 ? _lines.Count / LinesPerPage + (_lines.Count + 1 != LinesPerPage ? 1 : 0) : 0;
	}

	/// <summary><c>TextBox_PageUp</c>: back a page while the box is visible and not on the first.</summary>
	public bool PageUp() {
		if (!Visible || Page <= 0) {
			return false;
		}

		Page--;
		return true;
	}

	/// <summary><c>TextBox_PageDown</c>: on a page while the box is visible and not on the last.</summary>
	public bool PageDown() {
		if (!Visible || Page >= PageCount - 1) {
			return false;
		}

		Page++;
		return true;
	}

	/// <summary>
	/// <c>TextBox_ShowPage</c> (<c>0040cee2</c>) and the lines' own paint: line <c>i</c> sits in row
	/// <c>i % perPage</c>, one cell tall, left-aligned in <c>0x28</c> over a backing it clears to
	/// <c>0x10</c>, and only the current page's are shown.
	/// </summary>
	public void Paint(ShellSurface surface, ShellRect parent, HudFont? font) {
		if (!Visible || font == null || LinesPerPage <= 0) {
			return;
		}

		int cell = font.CellHeight;
		for (int i = Page * LinesPerPage; i < _lines.Count && i / LinesPerPage == Page; i++) {
			int top = parent.Y0 + Rect.Y0 + i % LinesPerPage * cell;
			ShellChrome.PaintText(surface, new ShellRect(parent.X0 + Rect.X0, top, parent.X0 + Rect.X1, top + cell), font,
				_lines[i], ShellTextAlign.Left, LineColor, ShellChrome.InteriorColor);
		}
	}

	/// <summary>
	/// <c>TextBox_Wrap</c> (<c>0040d0d6</c>), over a copy of the string as the original works in place: a
	/// newline ends a line; any other control character becomes a space, without being tested as one;
	/// and at each space the line so far is measured, and when it is wider than the box it is broken at
	/// the last break, which becomes the new line's start. The break is then left where it was rather
	/// than moved to this space, and a word the break leaves at the start of a line is not measured
	/// again until the next space. After the last character the final line gets one more such test.
	/// </summary>
	internal static List<string> Wrap(string text, HudFont font, int width) {
		char[] buffer = text.ToCharArray();
		var starts = new List<int> { 0 };
		int lastBreak = 0;

		for (int p = 0; p < buffer.Length; p++) {
			char c = buffer[p];
			if (c == '\n') {
				buffer[p] = '\0';
				starts.Add(p + 1);
				lastBreak = p;
			} else if (c < ' ') {
				buffer[p] = ' ';
			} else if (c == ' ') {
				buffer[p] = '\0';
				int next = p;
				if (width < font.Measure(LineAt(buffer, starts[^1]))) {
					if (buffer[lastBreak] == ' ') {
						buffer[lastBreak] = '\0';
					}

					starts.Add(lastBreak + 1);
					next = lastBreak;
				}

				lastBreak = next;
				buffer[p] = ' ';
			}
		}

		if (width < font.Measure(LineAt(buffer, starts[^1]))) {
			buffer[lastBreak] = '\0';
			starts.Add(lastBreak + 1);
		}

		return starts.ConvertAll(start => LineAt(buffer, start));
	}

	/// <summary>The string a line pointer names: from <paramref name="start"/> to the next terminator.</summary>
	private static string LineAt(char[] buffer, int start) {
		int end = start;
		while (end < buffer.Length && buffer[end] != '\0') {
			end++;
		}

		return start < buffer.Length ? new string(buffer, start, end - start) : string.Empty;
	}

	private const byte LineColor = 0x28;
}

/// <summary>
/// The career's text for the campaign map view: <c>Campaign_LoadStageText</c> (<c>0040f775</c>) reads
/// <c>campaign.str</c> and keeps string <c>stage - 1</c> of its first group. See
/// docs/retail/shell/screen-layout.md, "The summary text box".
///
/// <para>v1.0 reads it from <c>LANG0.VOL</c>'s <c>ENG</c> folder, its literal at <c>0046f5be</c>; v1.10 from the
/// shell language's folder. v1.10 appends the folder to a buffer it never clears, which can leave the file unfound
/// (docs/retail/retail-builds.md#v110s-shell-reads-the-language-twice-more); this names the folder alone.</para>
/// </summary>
public static class ShellCampaignText {
	private const string ResourceName = "CAMPAIGN.STR";

	/// <summary>
	/// Stage <paramref name="stage"/>'s text, or null when the file is missing or has no string for the
	/// stage. The original leaves its result pointer null in that case and hands it to
	/// <c>TextBox_SetText</c>; what that does with it is not read, and here the box stays empty.
	/// </summary>
	public static string? Load(GameContent content, int stage) =>
		content.Read(Folder(content), ResourceName) is { } bytes && SimStrings.Parse(bytes) is { } table
			? table.Text(0, stage - 1) : null;

	/// <summary>The <c>LANG0.VOL</c> folder <c>campaign.str</c> is read from.</summary>
	internal static string Folder(GameContent content) =>
		content.IsV110 ? ShellText.LanguageFolder(content.Language) : ShellText.EnglishFolder;
}

/// <summary>The mission tab's banks: the arrow faces, the <c>TERRA DEFENSE</c> plate and the campaign map's two pictures.</summary>
public sealed class ShellMissionArt {
	private readonly DynamixBitmap[]? _arrows;

	private ShellMissionArt(DynamixBitmap[]? arrows, DynamixBitmap? plate, DynamixBitmap? earth, DynamixBitmap? moon) {
		_arrows = arrows;
		Plate = plate;
		Earth = earth;
		Moon = moon;
	}

	/// <summary><c>dba\terradef.dba</c> frame 0, which <c>Mission_LoadPictures</c> (<c>00443f33</c>) puts in the Telecomm picture.</summary>
	public DynamixBitmap? Plate { get; }

	/// <summary><c>dba\th_earth.dba</c> frame 0, the map grid's part 0 in the campaign map view below stage 5.</summary>
	public DynamixBitmap? Earth { get; }

	/// <summary><c>dba\th_moon.dba</c> frame 0, the same from stage 5.</summary>
	public DynamixBitmap? Moon { get; }

	/// <summary><c>dba\miss_arw.dba</c> frame <paramref name="index"/>, or null.</summary>
	public DynamixBitmap? Arrow(int index) => _arrows is { } frames && index >= 0 && index < frames.Length ? frames[index] : null;

	public static ShellMissionArt Load(GameContent content) =>
		new(ShellArt.ReadBankFrames(content, "MISS_ARW"), FirstFrame(content, "TERRADEF"), FirstFrame(content, "TH_EARTH"),
			FirstFrame(content, "TH_MOON"));

	private static DynamixBitmap? FirstFrame(GameContent content, string bank) =>
		ShellArt.ReadBankFrames(content, bank) is { Length: > 0 } frames ? frames[0] : null;
}

/// <summary>
/// Tab 7, <c>MISSION</c>, in its three views: the <c>Telecomm</c> picture, the map panel, and the summary
/// text. The briefing adds the six map buttons, the summary's two page buttons and the button bar; the
/// campaign map shows none of them, and puts the stage's picture in the map panel and the stage's text in
/// a taller summary; the debrief shows the page buttons alone, the flown mission's debrief text, and the
/// mission report's twenty texts in the map panel. Built once by <c>Mission_BuildScreen</c>
/// (<c>00442534</c>), put up in a view by <c>Mission_Show</c> (<c>004441e3</c>) and taken down by
/// <c>Mission_Leave</c> (<c>00444a05</c>). See docs/retail/shell/screen-layout.md, "The mission screen".
///
/// <para><b>Every rect here is a literal in the executable</b>, kept parent-relative as the builder
/// writes them: the four panels in the canvas, everything else in the panel holding it.</para>
///
/// <para>In the briefing, the map inside the panel is the shell's map object, <see cref="ShellMap"/>, drawn
/// over this screen and moved by its six buttons; <c>Rock &amp; Roll &gt;</c> is <see cref="ShellMissionLaunch"/>'s.
/// Each view's movies are the host's to queue, into <see cref="ShellMovieQueue"/>.</para>
/// </summary>
public sealed class ShellMissionScreen {
	/// <summary>The four panels, in the canvas, each parented to the top-level window.</summary>
	public static readonly ShellRect TelecommRect = new(7, 0x2b, 0x113, 0x12b);
	public static readonly ShellRect SummaryRect = new(7, 0x133, 0x278, 0x1a7);
	public static readonly ShellRect ButtonBarRect = new(7, 0x1b1, 0x278, 0x1d9);

	/// <summary>
	/// The map panel. The builder starts it on row <c>0x2b</c> in a window and on <c>0x2a</c> when the
	/// shell runs full-screen (<c>Display_FullScreen</c> (<c>00481e68</c>)); this engine's shell is a window.
	/// </summary>
	public static readonly ShellRect MapRect = new(0x117, 0x2b, 0x278, 0x12b);

	/// <summary>The Telecomm picture, in its panel.</summary>
	private static readonly ShellRect PlateRect = new(10, 0x14, 0xf9, 0x100);

	/// <summary>
	/// The arrows, in their panels: the map's six in a column from <c>x = 0x137</c>, each 31 pixels
	/// square, and the summary's two from <c>x = 0x247</c>. Each draws its first frame unlit and its
	/// second lit.
	/// </summary>
	private static readonly (int Top, int Unlit, int Lit)[] Arrows = {
		(0x18, 1, 0), (0x3e, 3, 2), (0x64, 10, 8), (0x8a, 11, 9), (0xb5, 7, 6), (0xdb, 5, 4),
		(0x18, 1, 0), (0x51, 3, 2),
	};

	private const int MapArrowLeft = 0x137;
	private const int PageArrowLeft = 0x247;
	private const int ArrowSize = 0x1e;

	/// <summary>The four buttons, in the button bar.</summary>
	private static readonly ShellRect[] ButtonRects = {
		new(0xe, 0x10, 0x96, 0x23), new(0x9d, 0x10, 0x125, 0x23), new(0x12d, 0x10, 0x1b5, 0x23),
		new(0x1ea, 0x10, 0x263, 0x23),
	};

	/// <summary>The briefing, objectives and intelligence text boxes, all at one rect in the summary panel.</summary>
	private static readonly ShellRect TextRect = new(10, 0x15, 0x23f, 0x73);

	/// <summary>
	/// The summary in the campaign map view, which <c>Mission_Show</c> moves it to: down over the row the
	/// button bar holds in the briefing.
	/// </summary>
	public static readonly ShellRect MapSummaryRect = new(7, 0x133, 0x278, 0x1dc);

	/// <summary>Text box 0, the campaign map's, in the summary: wider and taller than the other four.</summary>
	private static readonly ShellRect MapTextRect = new(10, 0x15, 0x265, 0xa8);

	/// <summary>The map grid, in the map panel. The builder turns its grid lines off.</summary>
	private static readonly ShellRect GridRect = new(0xb, 0x18, 0x131, 0xf9);

	private readonly ShellMissionArt? _art;
	private readonly ShellTextBox[] _boxes = { new(TextRect), new(TextRect), new(TextRect) };
	private readonly ShellTextBox _mapBox = new(MapTextRect);
	private readonly ShellTextBox _debriefBox = new(TextRect);
	private string? _mapTitle;
	private DynamixBitmap? _mapPicture;

	/// <summary>The report texts' strings, in <see cref="ReportTexts"/> order; each figure starts on the empty entry 0.</summary>
	private readonly string?[] _reportStrings = new string?[ReportTexts.Length];

	/// <summary>The figures' alignment, which <c>ESMessage_SetString</c> rewrites with the string.</summary>
	private readonly ShellTextAlign[] _reportAlign = Array.ConvertAll(ReportTexts, text => text.Align);

	public ShellMissionScreen(ShellMissionArt? art = null) => _art = art;

	/// <summary><c>MissionScreenView</c> (<c>0048106c</c>), the view up.</summary>
	public ShellMissionView View { get; private set; } = ShellMissionView.Briefing;

	/// <summary>The campaign map view's text box, box 0.</summary>
	public ShellTextBox MapBox => _mapBox;

	/// <summary>The debrief view's text box, box 4.</summary>
	public ShellTextBox DebriefBox => _debriefBox;

	/// <summary>
	/// Whether the report texts are up with the map panel. They are built visible and only
	/// <c>Mission_HideReportTexts</c> (<c>00444914</c>) takes them down — in the map and briefing views and on
	/// leaving the tab — so the debrief shows them only as the screen's first view since the shell started.
	/// </summary>
	public bool ReportTextsUp { get; private set; } = true;

	/// <summary>
	/// <c>DAT_004780a4</c>, which text is up: the briefing's, the objectives' or the intelligence
	/// report's. Each is also the button lit in <see cref="ViewLitColor"/>.
	/// </summary>
	public ShellMissionButton ShownText { get; private set; } = ShellMissionButton.Briefing;

	/// <summary>The text box <paramref name="text"/> fills.</summary>
	public ShellTextBox Box(ShellMissionButton text) => _boxes[(int)text];

	/// <summary>
	/// <c>Mission_Show(1)</c>: fills the three text boxes, which puts each back on its first page,
	/// lights <c>Mission Briefing</c> and shows the briefing.
	/// </summary>
	public void EnterBriefing(ShellMissionTexts texts, HudFont? font) {
		View = ShellMissionView.Briefing;
		ReportTextsUp = false;
		_mapBox.Visible = false;
		_debriefBox.Visible = false;
		Box(ShellMissionButton.Briefing).SetText(texts.Briefing, font);
		Box(ShellMissionButton.Objectives).SetText(texts.Objectives, font);
		Box(ShellMissionButton.Intelligence).SetText(texts.Intelligence, font);
		ShowText(ShellMissionButton.Briefing);
	}

	/// <summary>
	/// <c>Mission_Show(0)</c>: <c>Mission_LoadPictures</c> puts <c>th_earth</c> in the map grid below
	/// stage 5 and <c>th_moon</c> from it, as part 0 at the grid's origin with no remap; the stage's text
	/// fills box 0 and goes up; and the map panel is titled <c>"%s %s"</c> of the sector's name,
	/// <c>estext.bin</c> <c>0x76 + stage</c>, and <c>0x7c</c> <c>Sector</c>.
	/// </summary>
	public void EnterMap(int stage, string? campaignText, ShellText? text, HudFont? font) {
		View = ShellMissionView.Map;
		ReportTextsUp = false;
		_mapPicture = stage < LunarStage ? _art?.Earth : _art?.Moon;
		_mapTitle = text?.Text(FirstSectorText + stage) is { } sector && text.Text(SectorText) is { } word
			? $"{sector} {word}" : null;
		_mapBox.SetText(campaignText, font);
		Box(ShownText).Visible = false;
		_debriefBox.Visible = false;
		_mapBox.Visible = true;
	}

	/// <summary>
	/// <c>Mission_Show(4)</c>: box 4 filled with the flown mission's debrief (<c>Career_DebriefText</c>,
	/// <c>004135d4</c>) and put up, the map panel titled <c>0xb9</c> <c>Mission Report</c>, and the report
	/// texts left as they are. <c>Rock &amp; Roll &gt;</c> is greyed before the bar that holds it is hidden,
	/// which leaves nothing on the screen but the page buttons to click.
	/// </summary>
	public void EnterDebrief(string? debriefText, HudFont? font) {
		View = ShellMissionView.Debriefing;
		_mapBox.Visible = false;
		Box(ShownText).Visible = false;
		_debriefBox.SetText(debriefText, font);
		_debriefBox.Visible = true;
	}

	/// <summary><c>Mission_Leave</c> (<c>00444a05</c>)'s <c>Mission_HideReportTexts</c>: the report texts down for good.</summary>
	public void Leave() => ReportTextsUp = false;

	/// <summary>
	/// <c>Debrief_WriteReport</c> (<c>0040f34c</c>): the ten figures. The outcome is <c>estext.bin</c>
	/// <c>0x146 + outcome</c>, <c>Failure</c> or <c>Success</c>; the salvage <c>"%d %s"</c> of the award in tons
	/// and <c>0x149</c> <c>Tons</c>; the rest <c>"%d"</c>. The first three are left-aligned beside their labels
	/// and the rest right-aligned under the column heads, all in <c>0x29</c>
	/// (docs/retail/shell/screen-layout.md#the-mission-report).
	/// </summary>
	public void WriteReport(ShellDebriefReport report, ShellText? text) {
		void Set(int index, string? value, ShellTextAlign align) {
			_reportStrings[index] = value;
			_reportAlign[index] = align;
		}

		Set(OutcomeFigure, text?.Text(FirstOutcomeText + report.Outcome), ShellTextAlign.Left);
		Set(SalvageFigure, $"{report.SalvageAwarded / ShellRepairCosts.KilogramsPerTon} {text?.Text(TonsText)}", ShellTextAlign.Left);
		Set(WeaponsFigure, $"{report.WeaponsRecovered}", ShellTextAlign.Left);
		Set(PlayerHercs, $"{report.PlayerKills.Hercs}", ShellTextAlign.Right);
		Set(SquadHercs, $"{report.SquadKills.Hercs}", ShellTextAlign.Right);
		Set(PlayerBases, $"{report.PlayerKills.Bases}", ShellTextAlign.Right);
		Set(SquadBases, $"{report.SquadKills.Bases}", ShellTextAlign.Right);
		Set(PlayerFlyers, $"{report.PlayerKills.Flyers}", ShellTextAlign.Right);
		Set(SquadFlyers, $"{report.SquadKills.Flyers}", ShellTextAlign.Right);
		Set(LossesFigure, $"{report.PilotsLost}", ShellTextAlign.Right);
	}

	/// <summary>
	/// A text button — <c>Mission_OnBriefing</c> (<c>004453c1</c>), <c>Mission_OnObjectives</c>
	/// (<c>00445437</c>) or <c>Mission_OnIntelligence</c> (<c>004454a0</c>) — through
	/// <c>Mission_LightViewButton</c> (<c>00444c49</c>) and <c>Mission_ShowTextBox</c> (<c>00444cb3</c>):
	/// the box that was up is hidden and this one shown on the page it was left on.
	/// </summary>
	public void ShowText(ShellMissionButton text) {
		if (text == ShellMissionButton.RockAndRoll) {
			return;
		}

		Box(ShownText).Visible = false;
		Box(text).Visible = true;
		ShownText = text;
	}

	/// <summary><c>Mission_OnPageUp</c> (<c>004455e9</c>) and <c>Mission_OnPageDown</c> (<c>0044569a</c>), on the box that is up.</summary>
	public bool Page(bool down) => down ? UpBox.PageDown() : UpBox.PageUp();

	/// <summary><c>DAT_004780a4</c>'s box: the debrief's in that view, otherwise the text <see cref="ShownText"/> names.</summary>
	public ShellTextBox UpBox => View == ShellMissionView.Debriefing ? _debriefBox : Box(ShownText);

	/// <summary>One button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellMissionButton button) => Inside(ButtonBarRect, ButtonRects[(int)button]);

	/// <summary>One arrow's rect, in the canvas.</summary>
	public static ShellRect ArrowRect(ShellMissionArrow arrow) {
		int index = (int)arrow;
		bool page = arrow >= ShellMissionArrow.PageUp;
		int left = page ? PageArrowLeft : MapArrowLeft;
		int top = Arrows[index].Top;
		return Inside(page ? SummaryRect : MapRect, new ShellRect(left, top, left + ArrowSize, top + ArrowSize));
	}

	/// <summary>
	/// What the pointer hits: a button or an arrow. The four panels are built disabled —
	/// <c>ESTitle_Ctor</c> clears <c>+0x49</c> and the builder clears the button bar's — and the
	/// Telecomm picture's handler (<c>Mission_OnTelecommPicture</c> (<c>00444e28</c>)) returns at once, so a click anywhere else is
	/// swallowed, which here is the same as hitting nothing.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		// The campaign map hides every button and arrow, so there is nothing to hit.
		if (View == ShellMissionView.Map) {
			return null;
		}

		// The debrief hides the bar and the map's six, and leaves the page buttons.
		bool debrief = View == ShellMissionView.Debriefing;
		foreach (var button in Enum.GetValues<ShellMissionButton>()) {
			var rect = ButtonRect(button);
			if (!debrief && rect.Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.MissionButton, (int)button), rect, canvasX, canvasY);
			}
		}

		foreach (var arrow in Enum.GetValues<ShellMissionArrow>()) {
			if ((!debrief || arrow >= ShellMissionArrow.PageUp) && ArrowRect(arrow).Contains(canvasX, canvasY)) {
				return new ShellHit(new ShellWidget(ShellWidgetKind.MissionArrow, (int)arrow), ShellHandler.RepeatButtonIcon, 0);
			}
		}

		return null;
	}

	/// <summary>Draws the view that is up into <paramref name="surface"/>, with <paramref name="lit"/> drawn pressed. The caller clears it first.</summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit = null) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		// Telecomm: a flat header over a filled body, and the plate in it as a bare bitmap.
		PaintPanel(surface, TelecommRect, font, text?.Text(TelecommText), PanelFace, headerChrome: false, 0, 0);
		ShellChrome.PaintImagePanel(surface, Inside(TelecommRect, PlateRect), _art?.Plate, 0, 0, Border, border: false);

		// The map panel, titled by the view, with the hatch and a plate the builder writes.
		bool map = View == ShellMissionView.Map;
		bool debrief = View == ShellMissionView.Debriefing;
		string? title = map ? _mapTitle : text?.Text(debrief ? ReportTitleText : MapTitleText);
		PaintPanel(surface, MapRect, font, title, MapFace, headerChrome: true, MapPlateFirst, MapPlateLast);

		// Each report text is a label in 0x1a or a figure in 0x29, and none clears a backing.
		if (ReportTextsUp) {
			for (int i = 0; i < ReportTexts.Length; i++) {
				var report = ReportTexts[i];
				string? value = report.Label is { } label ? text?.Text(label) : _reportStrings[i];
				ShellChrome.PaintText(surface, Inside(MapRect, report.Rect), font, value, _reportAlign[i],
					report.Label != null ? ReportLabelColor : ShellChrome.FontInkColor);
			}
		}

		if (debrief) {
			PaintPanel(surface, SummaryRect, font, text?.Text(SummaryText), PanelFace, headerChrome: false, 0, 0);
			_debriefBox.Paint(surface, SummaryRect, font);
			PaintArrows(surface, lit, ShellMissionArrow.PageUp);
			return;
		}

		// The campaign map: the grid with the stage's picture, and the taller summary with the stage's text.
		if (map) {
			ShellGrid.Paint(surface, Inside(MapRect, GridRect), gridLines: false,
				_mapPicture is { } picture ? new ShellGridPart?[] { new(picture, 0, 0, 0, Array.Empty<(byte, byte)>()) } : Array.Empty<ShellGridPart?>());
			PaintPanel(surface, MapSummaryRect, font, text?.Text(SummaryText), PanelFace, headerChrome: false, 0, 0);
			_mapBox.Paint(surface, MapSummaryRect, font);
			return;
		}

		PaintPanel(surface, SummaryRect, font, text?.Text(SummaryText), PanelFace, headerChrome: false, 0, 0);
		Box(ShownText).Paint(surface, SummaryRect, font);
		PaintArrows(surface, lit, ShellMissionArrow.MapUp);

		ShellChrome.PaintPanel(surface, ButtonBarRect, ButtonBorder, fill: true);
		foreach (var button in Enum.GetValues<ShellMissionButton>()) {
			PaintButton(surface, font, ButtonRect(button), text?.Text(FirstButtonText + (int)button + (button > 0 ? 1 : 0)),
				button == ShownText ? ViewLitColor : ButtonBorder);
		}
	}

	/// <summary>The arrows from <paramref name="first"/> on, each in its lit face while <paramref name="lit"/> is it.</summary>
	private void PaintArrows(ShellSurface surface, ShellWidget? lit, ShellMissionArrow first) {
		foreach (var arrow in Enum.GetValues<ShellMissionArrow>()) {
			if (arrow < first) {
				continue;
			}

			var (_, unlit, litFrame) = Arrows[(int)arrow];
			bool pressed = lit is { Kind: ShellWidgetKind.MissionArrow } widget && widget.Index == (int)arrow;
			var rect = ArrowRect(arrow);
			var clip = surface.PushClip(rect);
			if (_art?.Arrow(pressed ? litFrame : unlit) is { } face) {
				surface.Blit(face, rect.X0, rect.Y0);
			}

			surface.PopClip(clip);
		}
	}

	private static void PaintPanel(ShellSurface surface, ShellRect rect, HudFont? font, string? title, byte face,
			bool headerChrome, int plateFirst, int plateLast) {
		ShellChrome.PaintTitledPanel(surface, rect, Border, face, ShellChrome.InteriorColor, TitleHeight, headerChrome,
			plateFirst, plateLast, fill: true);
		ShellChrome.PaintText(surface, new ShellRect(rect.X0, rect.Y0, rect.X1, rect.Y0 + TitleHeight), font, title,
			ShellTextAlign.Center, ShellChrome.FontInkColor);
	}

	/// <summary>One button: its double-bordered box in <paramref name="border"/> and its caption, centred in <c>0x29</c>.</summary>
	private static void PaintButton(ShellSurface surface, HudFont? font, ShellRect rect, string? caption, byte border) {
		ShellChrome.PaintButton(surface, rect, border);
		ShellChrome.PaintText(surface, new ShellRect(rect.X0 + 1, rect.Y0, rect.X1, rect.Y1), font, caption,
			ShellTextAlign.Center, ShellChrome.FontInkColor);
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	private const byte Border = 0x27;
	private const int TitleHeight = 0x13;

	/// <summary><c>ESTitle_Ctor</c>'s header face, which the builder leaves on Telecomm and the summary and writes over on the map panel.</summary>
	private const byte PanelFace = 0x24;
	private const byte MapFace = 0x25;
	private const int MapPlateFirst = 0x26;
	private const int MapPlateLast = 0x13b;

	private const byte ButtonBorder = 0x22;

	/// <summary>The border <c>Mission_LightViewButton</c> writes on the button of the text that is up.</summary>
	private const byte ViewLitColor = 0x20;

	/// <summary><c>estext.bin</c> indices the screen prints. The buttons are <c>0xb4</c> and <c>0xb6</c>-<c>0xb8</c>.</summary>
	private const int SummaryText = 0xaf;
	private const int TelecommText = 0xb0;
	private const int MapTitleText = 0xb3;
	private const int FirstButtonText = 0xb4;

	/// <summary>
	/// The sector names run from <c>0x77</c>, reached by <c>0x76 + stage</c> for stages 1-5, and the word
	/// the campaign map's title puts after one.
	/// </summary>
	private const int FirstSectorText = 0x76;
	private const int SectorText = 0x7c;

	/// <summary>The stage from which <c>Mission_LoadPictures</c> loads <c>th_moon</c> rather than <c>th_earth</c>.</summary>
	private const int LunarStage = 5;

	/// <summary>The debrief view's map-panel title, <c>0xb9</c> <c>Mission Report</c>.</summary>
	private const int ReportTitleText = 0xb9;

	/// <summary>
	/// The twenty report texts, <c>DAT_0048d838</c>-<c>DAT_0048d884</c>, in the map panel, in the order the
	/// builder makes them: a label names its <c>estext.bin</c> entry and is right-aligned in
	/// <see cref="ReportLabelColor"/>; a figure has none until <see cref="WriteReport"/> writes it. The labels
	/// are <c>Mission Outcome:</c>, <c>Salvage Recovered:</c> and <c>Weapons Recovered:</c> with their figures
	/// beside them; then a table of <c>Kills:</c> by <c>Hercs:</c>, <c>Bases:</c> and <c>Flyers:</c>, one row
	/// for <c>You:</c> and one for <c>Squad:</c>; then <c>Losses:</c>.
	/// </summary>
	private static readonly (ShellRect Rect, int? Label, ShellTextAlign Align)[] ReportTexts = {
		(new(0x19, 0x2e, 0xba, 0x3c), 0x145, ShellTextAlign.Right),
		(new(0x19, 0x41, 0xba, 0x4f), 0x148, ShellTextAlign.Right),
		(new(0x19, 0x53, 0xba, 0x61), 0x14a, ShellTextAlign.Right),
		(new(0xc6, 0x2e, 0x127, 0x3c), null, ShellTextAlign.Left),
		(new(0xc6, 0x41, 0x127, 0x4f), null, ShellTextAlign.Left),
		(new(0xc6, 0x53, 0x127, 0x61), null, ShellTextAlign.Left),
		(new(0x19, 0x74, 0x65, 0x82), 0x14c, ShellTextAlign.Right),
		(new(0x66, 0x74, 0xa8, 0x82), 0x150, ShellTextAlign.Right),
		(new(0xa9, 0x74, 0xe7, 0x82), 0x151, ShellTextAlign.Right),
		(new(0xe8, 0x74, 0x127, 0x82), 0x152, ShellTextAlign.Right),
		(new(0x19, 0x8c, 0x65, 0x9a), 0x14e, ShellTextAlign.Right),
		(new(0x19, 0xa2, 0x65, 0xb0), 0x14f, ShellTextAlign.Right),
		(new(0x19, 0xc3, 0x65, 0xd1), 0x14d, ShellTextAlign.Right),
		(new(0x66, 0x8c, 0x9a, 0x9a), null, ShellTextAlign.Right),
		(new(0x66, 0xa2, 0x9a, 0xb0), null, ShellTextAlign.Right),
		(new(0x9b, 0x8c, 0xd9, 0x9a), null, ShellTextAlign.Right),
		(new(0x9b, 0xa2, 0xd9, 0xb0), null, ShellTextAlign.Right),
		(new(0xda, 0x8c, 0x118, 0x9a), null, ShellTextAlign.Right),
		(new(0xda, 0xa2, 0x118, 0xb0), null, ShellTextAlign.Right),
		(new(0x66, 0xc3, 0x9a, 0xd1), null, ShellTextAlign.Right),
	};

	/// <summary>Where <see cref="WriteReport"/>'s figures sit in <see cref="ReportTexts"/>.</summary>
	private const int OutcomeFigure = 3;
	private const int SalvageFigure = 4;
	private const int WeaponsFigure = 5;
	private const int PlayerHercs = 13;
	private const int SquadHercs = 14;
	private const int PlayerBases = 15;
	private const int SquadBases = 16;
	private const int PlayerFlyers = 17;
	private const int SquadFlyers = 18;
	private const int LossesFigure = 19;

	/// <summary>The report labels' colour.</summary>
	private const byte ReportLabelColor = 0x1a;

	/// <summary><c>0x146</c> <c>Failure</c>, then <c>0x147</c> <c>Success</c>, indexed by the outcome.</summary>
	private const int FirstOutcomeText = 0x146;

	/// <summary><c>0x149</c> <c>Tons</c>, after the salvage figure.</summary>
	private const int TonsText = 0x149;
}
