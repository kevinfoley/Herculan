using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;
using Herculan.Engine.Settings;

namespace Herculan.Engine.Shell;

/// <summary>The two buttons on the build screen's lower panel.</summary>
public enum ShellBuildButton {
	Scrap,
	Build,
}

/// <summary>
/// Tab 4, <c>BUILD</c> — the <c>Herc Construction</c> panel: a list of the chassis the player can
/// build, the chosen one's blueprint on a grid and its <c>herc_inf.dat</c> figures, and under it a
/// second panel with the salvage available and the <c>Scrap Herc</c> and <c>Build Herc</c> buttons.
/// Built once by <c>Build_BuildScreen</c> (<c>00445758</c>), entered by <c>Build_Enter</c>
/// (<c>0044690d</c>), its chassis moved by <c>Build_SelectChassis</c> (<c>00446c3b</c>), its figures
/// filled by <c>Herc_BuildScreenRefresh</c> (<c>00446cfa</c>) and its buttons gated by
/// <c>Build_GateButtons</c> (<c>004469d4</c>). See docs/retail/shell/screen-layout.md, "The build screen".
///
/// <para><b>Every rect here is a literal in the executable</b>, kept parent-relative as the builder
/// writes them: the two top-level panels in the canvas, the blueprints, the list, the stats box and
/// the labels in the content panel, and the rest in whichever box holds them.</para>
///
/// <para><b>The left of the canvas is the squad panel</b>, which <see cref="ShellSquadPanel"/> draws.
/// This tab's arm of <c>Squad_SelectBay</c> (<c>0043d64d</c>) takes any bay, empty or unfinished.</para>
///
/// <para>BUILD orders the selected chassis into the selected bay (<see cref="Build"/>); SCRAP opens
/// <see cref="ShellScrapDialog"/>, which the host puts up.</para>
/// </summary>
public sealed class ShellBuildScreen {
	/// <summary>The chassis types the list offers, one row each — every type, the Razor included.</summary>
	public const int ChassisCount = 9;

	/// <summary>The content panel, in the canvas: the repair and crew screens' rect, four pixels wider and ending well short.</summary>
	public static readonly ShellRect PanelRect = new(0xf1, 0x2b, 0x27c, 0x162);

	/// <summary>The lower panel, in the canvas. The builder parents it to the shell's top-level window rather than to the content panel.</summary>
	public static readonly ShellRect LowerPanelRect = new(0xf1, 0x166, 0x27c, 0x1d9);

	/// <summary>The nine blueprints, one per chassis, all at one rect in the content panel.</summary>
	private static readonly ShellRect BlueprintRect = new(0xb2, 0x2d, 0x180, 0x12d);

	/// <summary>The chassis list and the stats box, in the content panel.</summary>
	private static readonly ShellRect ListRect = new(0x16, 0x4b, 0x9a, 0xd1);
	private static readonly ShellRect StatsRect = new(0x60, 0xe3, 0xab, 0x11f);

	/// <summary><c>Select Herc</c> over <c>Type To Build</c>, centred over the list.</summary>
	private static readonly ShellRect[] HeadingRects = {
		new(0x16, 0x28, 0x9a, 0x34),
		new(0x16, 0x34, 0x9a, 0x40),
	};

	/// <summary><c>Mass</c>, <c>Speed</c>, <c>Hardpoints</c> and <c>Salvage Reqd</c>, left of the stats box, in the content panel.</summary>
	private static readonly ShellRect[] StatLabelRects = {
		new(8, 0xe9, 0x5e, 0xf5),
		new(8, 0xf5, 0x5e, 0x101),
		new(8, 0x101, 0x5e, 0x10d),
		new(8, 0x10d, 0x5e, 0x119),
	};

	/// <summary>The four figures, in the stats box.</summary>
	private static readonly ShellRect[] StatValueRects = {
		new(5, 6, 0x45, 0x12),
		new(5, 0x12, 0x45, 0x1e),
		new(5, 0x1e, 0x45, 0x2a),
		new(5, 0x2a, 0x45, 0x36),
	};

	/// <summary>The rows, in the list: 14 tall on a 14-pixel pitch.</summary>
	private const int RowLeft = 10;
	private const int RowRight = 0x6e;
	private const int FirstRowTop = 4;
	private const int RowPitch = 0xe;
	private const int RowBottomOffset = 0xd;

	/// <summary>
	/// Where <c>ESQuad_AddColumns</c> is told to cut a row: the name fills the first column, <c>2</c>
	/// to <c>0x62</c>, centred, and the other three are empty — the next two zero-wide.
	/// </summary>
	private const int NameColumnRight = 0x62;

	/// <summary>In the lower panel: <c>Salvage Available</c>, its box, and the box's figure.</summary>
	private static readonly ShellRect SalvageLabelRect = new(0x7d, 9, 0x10e, 0x15);
	private static readonly ShellRect SalvageBoxRect = new(0x91, 0x1a, 0xfa, 0x2c);
	private static readonly ShellRect SalvageValueRect = new(1, 3, 0x68, 0xf);

	/// <summary>The <c>Scrap Herc</c> and <c>Build Herc</c> boxes, in the lower panel.</summary>
	private static readonly ShellRect ScrapPanelRect = new(0x37, 0x38, 0xc1, 100);
	private static readonly ShellRect BuildPanelRect = new(0xcc, 0x38, 0x156, 100);

	/// <summary>Each box's title, and its button, in the box.</summary>
	private static readonly ShellRect BoxTitleRect = new(8, 6, 0x82, 0x12);
	private static readonly ShellRect BoxButtonRect = new(0xf, 0x17, 0x78, 0x26);

	private readonly IReadOnlyList<HercInfEntry>? _catalog;
	private readonly ShellRepairDiagrams? _blueprints;
	private readonly ShellBayPictures? _pictures;
	private ShellHangar _hangar;

	/// <summary>
	/// Builds the screen and enters it with <paramref name="bay"/> selected, the bay the previous tab
	/// left in <c>SelectedBaySlot</c> (<c>00482ae5</c>).
	/// </summary>
	public ShellBuildScreen(ShellHangar? hangar = null, int bay = -1, IReadOnlyList<HercInfEntry>? catalog = null,
			ShellRepairDiagrams? blueprints = null, ShellBayPictures? pictures = null) {
		_hangar = hangar ?? ShellHangar.From(null);
		_catalog = catalog;
		_blueprints = blueprints;
		_pictures = pictures;
		Enter(_hangar, bay);
	}

	/// <summary>
	/// Reads <c>gam\herc_inf.dat</c>, whose records are the figures the stats box prints and the prices
	/// the BUILD gate compares against. Null when it is missing, and the box then prints nothing.
	/// </summary>
	public static IReadOnlyList<HercInfEntry>? LoadCatalog(GameContent content) =>
		content.Read(ShellRepairCosts.CatalogFolder, ShellRepairCosts.ChassisResourceName) is { } bytes
			&& new HercInfoTransformer().Parse(bytes) is { Data.Length: > 0 } catalog
			? catalog.Data : null;

	/// <summary>
	/// <c>DAT_004786e4</c> — the chassis whose row is lit and whose blueprint and figures are up. It is
	/// <c>-1</c> in the image and only <see cref="SelectChassis"/> writes it, so every entry leaves it at 0.
	/// </summary>
	public int SelectedChassis { get; private set; } = -1;

	/// <summary><c>SelectedBaySlot</c> (<c>00482ae5</c>), the bay the squad panel shows.</summary>
	public int SelectedBay { get; private set; }

	/// <summary>
	/// What the armory's build queue has committed out of the salvage pool. The screen quotes and gates
	/// on the pool net of it (<c>CareerSalvage - Armory_QueuedTotal()</c>). The host sets it from
	/// <see cref="ShellHangar"/>'s queue on every tab entry (<see cref="ShellArmoryCatalog.QueuedTotal"/>).
	/// </summary>
	public int QueuedKilograms { get; set; }

	/// <summary>The salvage the screen quotes and gates on.</summary>
	public int AvailableKilograms => _hangar.SalvageKilograms - QueuedKilograms;

	/// <summary>
	/// <c>Build_Enter</c> (<c>0044690d</c>), the tab's entry. It refreshes the rows' gate, the figures and
	/// the buttons, fills the blueprints, shows the two panels and chassis 0's blueprint, and selects
	/// chassis 0 — which returns at once when chassis 0 is already selected, so the figures and the gate
	/// it refreshed first are the ones that stand. Chassis 0 is selected whether or not it is available.
	/// </summary>
	public void Enter(ShellHangar hangar, int bay) {
		_hangar = hangar;
		SelectedBay = bay;
		_selectedNameLit = false;
		SelectChassis(0);
	}

	/// <summary>
	/// Whether the selected row's name is drawn lit. <c>Build_GateRows</c> (<c>00446835</c>) rewrites
	/// every row's name colour on entry, the selected row's included, and only a selection that moves
	/// lights it again — so re-entering with chassis 0 still selected leaves its border lit and its name
	/// not.
	/// </summary>
	private bool _selectedNameLit;

	/// <summary>
	/// <c>Build_SelectChassis</c> (<c>00446c3b</c>), a row's handler through nine thunks from
	/// <c>Build_OnChassisRow0</c> (<c>00446fbf</c>): unlights the old row and hides its blueprint, lights the new one and shows its
	/// blueprint, then regates the buttons and refreshes the figures. Returns whether the selection moved;
	/// the chassis already selected is a no-op.
	/// </summary>
	public bool SelectChassis(int chassis) {
		if (chassis == SelectedChassis || chassis < 0 || chassis >= ChassisCount) {
			return false;
		}

		SelectedChassis = chassis;
		_selectedNameLit = true;
		return true;
	}

	/// <summary>
	/// <c>Squad_SelectBay</c> (<c>0043d64d</c>)'s build arm: every bay is accepted, empty and unfinished
	/// ones included. Returns whether the bay moved; the bay already selected is a no-op.
	/// </summary>
	public bool ClickRoster(int bay) {
		if (bay == SelectedBay || bay < 0 || bay >= ShellHangar.BayCount) {
			return false;
		}

		SelectedBay = bay;
		return true;
	}

	/// <summary>
	/// <c>Build_GateRows</c> (<c>00446835</c>): a row answers only while its chassis's availability flag
	/// is set. A row that does not is disabled and all four of its columns drawn in <c>0x10</c>, the
	/// background, so the list shows a gap where it is.
	/// </summary>
	public bool IsChassisAvailable(int chassis) => _hangar.IsChassisAvailable(chassis);

	/// <summary>The selected chassis's <c>herc_inf.dat</c> record, or null.</summary>
	public HercInfEntry? SelectedEntry =>
		_catalog != null && SelectedChassis >= 0 && SelectedChassis < _catalog.Count ? _catalog[SelectedChassis] : null;

	/// <summary>
	/// <c>Build_GateButtons</c> (<c>004469d4</c>). An empty bay can be built into and not scrapped, and
	/// BUILD is live only while the salvage available is <i>more</i> than the selected chassis's price —
	/// a pool exactly equal to it is not enough. An occupied bay can be scrapped and not built into, and
	/// SCRAP is dead while the hangar holds exactly one deployable machine, whichever bay it is in, or when
	/// the bay's chassis is not available —
	/// the repair screen's SCRAP test (docs/retail/shell/screen-layout.md#what-the-buttons-are-gated-on).
	///
	/// <para>With no bay selected the original reads the dword before the eight-pointer array as the
	/// bay's machine (docs/retail/shell/screen-layout.md#scrapping-and-building-are-gated-on-the-bay); this engine
	/// greys both buttons instead, which is its own choice.</para>
	/// </summary>
	public bool IsEnabled(ShellBuildButton button) {
		if (SelectedBay < 0) {
			return false;
		}

		var machine = _hangar.Bay(SelectedBay);
		if (machine == null) {
			// The comparison is unsigned in the original, which differs from this only when the queue has
			// committed more than the pool holds.
			return button == ShellBuildButton.Build && SelectedEntry is { } entry
				&& AvailableKilograms > entry.SalvageReq * ShellRepairCosts.KilogramsPerTon;
		}

		return button == ShellBuildButton.Scrap && !_hangar.HasSingleDeployable()
			&& _hangar.IsChassisAvailable(machine.ChassisType);
	}

	/// <summary>
	/// BUILD's handler (<c>Build_OnBuild</c>, <c>00446f3e</c>): the selected chassis is ordered into the selected bay and its
	/// price comes off the pool (<see cref="ShellHangar.Order"/>); the roster, the readout, the salvage
	/// figure and the gate then refresh. The handler tests nothing itself — the gate is what stops it.
	/// </summary>
	public void Build() {
		if (SelectedEntry is { } entry) {
			_hangar.Order(SelectedBay, SelectedChassis, entry.SalvageReq, entry.BuildMissionCount);
		}
	}

	/// <summary>One chassis row's rect, in the canvas.</summary>
	public static ShellRect RowRect(int chassis) {
		var list = Inside(PanelRect, ListRect);
		return new ShellRect(list.X0 + RowLeft, list.Y0 + chassis * RowPitch + FirstRowTop,
			list.X0 + RowRight, list.Y0 + chassis * RowPitch + FirstRowTop + RowBottomOffset);
	}

	/// <summary>One button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellBuildButton button) =>
		Inside(Inside(LowerPanelRect, button == ShellBuildButton.Scrap ? ScrapPanelRect : BuildPanelRect),
			BoxButtonRect);

	/// <summary>
	/// What the pointer hits: a chassis row and which of its text columns, or a live button. A row whose
	/// chassis is unavailable and a gated button are disabled and swallow a click, which here is the
	/// same as hitting nothing.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		for (int chassis = 0; chassis < ChassisCount; chassis++) {
			var rect = RowRect(chassis);
			if (rect.Contains(canvasX, canvasY)) {
				return IsChassisAvailable(chassis)
					? ShellHit.ListRow(new ShellWidget(ShellWidgetKind.BuildChassisRow, chassis), rect,
						NameColumnRight, NameColumnRight, NameColumnRight, canvasX, canvasY)
					: null;
			}
		}

		foreach (var button in Enum.GetValues<ShellBuildButton>()) {
			if (IsEnabled(button) && ButtonRect(button).Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.BuildButton, (int)button),
					ButtonRect(button), canvasX, canvasY);
			}
		}

		return null;
	}

	/// <summary>
	/// Draws the whole screen into <paramref name="surface"/>, the squad panel included. The caller
	/// clears it first. The content panel's body is dithered, where the repair and crew screens fill
	/// theirs: the builder writes <c>+0x59</c> to 0 and the dither colour <c>+0x5d</c> to <c>0x25</c>.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellSquadPanel.Paint(surface, font, text, _hangar, SelectedBay, _pictures);

		ShellChrome.PaintTitledPanel(surface, PanelRect, Border, PanelFace, PanelFace, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface, new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight),
			font, text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		_blueprints?.PaintBlueprint(surface, Inside(PanelRect, BlueprintRect), SelectedChassis, Border,
			BlueprintLineColor);

		PaintList(surface, font, text);

		ShellChrome.PaintFramedPanel(surface, Inside(PanelRect, StatsRect), Border, ShellChrome.InteriorColor,
			fill: true);

		for (int i = 0; i < HeadingRects.Length; i++) {
			ShellChrome.PaintText(surface, Inside(PanelRect, HeadingRects[i]), font, text?.Text(FirstHeadingText + i),
				ShellTextAlign.Center, LabelColor);
		}

		for (int i = 0; i < StatLabelRects.Length; i++) {
			ShellChrome.PaintText(surface, Inside(PanelRect, StatLabelRects[i]), font,
				text?.Text(StatLabelTexts[i]), ShellTextAlign.Left, LabelColor);
		}

		PaintStats(surface, font, text);
		PaintLowerPanel(surface, font, text);
	}

	/// <summary>
	/// The chassis list: a flat framed box of nine rows, one per chassis type, each named in its first
	/// column. The selected row's border is lit <c>0x29</c>, and its name too once the selection has
	/// moved (<see cref="_selectedNameLit"/>); the rest take a <c>0x10</c> border — the background, so
	/// none shows — and a <c>0x27</c> name; an unavailable chassis's name is <c>0x10</c> as well.
	/// </summary>
	private void PaintList(ShellSurface surface, HudFont? font, ShellText? text) {
		ShellChrome.PaintFramedPanel(surface, Inside(PanelRect, ListRect), Border, ShellChrome.InteriorColor,
			fill: true);

		for (int chassis = 0; chassis < ChassisCount; chassis++) {
			var rect = RowRect(chassis);
			bool selected = chassis == SelectedChassis;
			ShellChrome.PaintPanel(surface, rect, selected ? LitColor : ShellChrome.InteriorColor, fill: true);

			byte nameColor = selected && _selectedNameLit ? LitColor
				: IsChassisAvailable(chassis) ? RowColor : ShellChrome.InteriorColor;
			ShellChrome.PaintText(surface, new ShellRect(rect.X0 + 2, rect.Y0, rect.X0 + NameColumnRight, rect.Y1),
				font, text?.Text(FirstChassisNameText + chassis), ShellTextAlign.Center, nameColor,
				ShellChrome.InteriorColor);
		}
	}

	/// <summary>
	/// <c>Herc_BuildScreenRefresh</c> (<c>00446cfa</c>): the selected chassis's mass and price as
	/// <c>"%d TONS"</c>, its speed as <c>"%d KPH"</c> and its hardpoint count bare, each an opaque
	/// left-aligned figure in <c>0x17</c>.
	/// </summary>
	private void PaintStats(ShellSurface surface, HudFont? font, ShellText? text) {
		var box = Inside(PanelRect, StatsRect);
		var entry = SelectedEntry;
		string? tons = text?.Text(TonsText);

		short speed = -1;
		short hardpointTotal = -1;
		if (entry != null) {
			speed = entry.Speed;
			hardpointTotal = entry.HardpointTotal;
			if (TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ShowCorrectStats)) {
				if (entry.HercId == HercInfEntry.OutlawHercId) {
					speed = 100;
				}
				if (entry.HercId == HercInfEntry.RaptorIIHercId) {
					hardpointTotal = 5;
				}
			}
		}

		string?[] values = entry == null ? new string?[StatValueRects.Length] : new[] {
			$"{entry.Weight} {tons}",
			$"{speed} {text?.Text(KphText)}",
			$"{hardpointTotal}",
			$"{entry.SalvageReq} {tons}",
		};

		for (int i = 0; i < StatValueRects.Length; i++) {
			ShellChrome.PaintText(surface, Inside(box, StatValueRects[i]), font, values[i], ShellTextAlign.Left,
				ValueColor, ShellChrome.InteriorColor);
		}
	}

	/// <summary>
	/// The lower panel: <c>Salvage Available</c> over a box holding the pool in tons — <c>"%ld %s"</c>
	/// of the net kilograms divided by 1000 (<c>Build_RefreshSalvage</c>, <c>00446e71</c>) — and the two
	/// button boxes, whose bodies keep a visible <c>0x25</c> checkerboard.
	/// </summary>
	private void PaintLowerPanel(ShellSurface surface, HudFont? font, ShellText? text) {
		ShellChrome.PaintFramedPanel(surface, LowerPanelRect, Border, ShellChrome.InteriorColor, fill: true);
		ShellChrome.PaintText(surface, Inside(LowerPanelRect, SalvageLabelRect), font, text?.Text(SalvageAvailableText),
			ShellTextAlign.Center, LabelColor);

		var salvageBox = Inside(LowerPanelRect, SalvageBoxRect);
		ShellChrome.PaintFramedPanel(surface, salvageBox, Border, ShellChrome.InteriorColor, fill: true);
		ShellChrome.PaintText(surface, Inside(salvageBox, SalvageValueRect), font,
			$"{(uint)AvailableKilograms / ShellRepairCosts.KilogramsPerTon} {text?.Text(TonsText)}",
			ShellTextAlign.Center, ValueColor, ShellChrome.InteriorColor);

		PaintButtonBox(surface, font, text, ScrapPanelRect, ScrapHercText, ShellBuildButton.Scrap, ScrapText);
		PaintButtonBox(surface, font, text, BuildPanelRect, BuildHercText, ShellBuildButton.Build, BuildText);
	}

	/// <summary>
	/// One button box: a checkered framed panel, its title, and the button, greyed with its caption when
	/// its gate is shut. The caption is the <c>Text</c> child <c>ESButtonFont_Ctor</c> builds at
	/// <c>{1, 0, w, h}</c>, so it is centred one pixel right of the button's own rect.
	/// </summary>
	private void PaintButtonBox(ShellSurface surface, HudFont? font, ShellText? text, ShellRect boxRect,
			int titleText, ShellBuildButton button, int captionText) {
		var box = Inside(LowerPanelRect, boxRect);
		ShellChrome.PaintFramedPanel(surface, box, Border, ButtonBoxFace, fill: true);
		ShellChrome.PaintText(surface, Inside(box, BoxTitleRect), font, text?.Text(titleText), ShellTextAlign.Center,
			LabelColor);

		bool enabled = IsEnabled(button);
		var rect = ButtonRect(button);
		ShellChrome.PaintButton(surface, rect, enabled ? ButtonBorder : DisabledColor);
		ShellChrome.PaintText(surface, new ShellRect(rect.X0 + 1, rect.Y0, rect.X1, rect.Y1), font,
			text?.Text(captionText), ShellTextAlign.Center, enabled ? ShellChrome.FontInkColor : DisabledColor);
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary>The border the builder writes over the class default on every panel, box and blueprint.</summary>
	private const byte Border = 0x27;

	/// <summary>The content panel's header face and body dither.</summary>
	private const byte PanelFace = 0x25;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x7f;
	private const int TitlePlateLast = 0x10a;

	/// <summary>The blueprints' grid lines, <c>+0x6e6</c>.</summary>
	private const byte BlueprintLineColor = 0x18;

	/// <summary>The button boxes keep a visible checkerboard, where every other box here is flattened to <c>0x10</c>.</summary>
	private const byte ButtonBoxFace = 0x25;

	private const byte LabelColor = 0x1a;
	private const byte ValueColor = 0x17;
	private const byte RowColor = 0x27;
	private const byte LitColor = 0x29;
	private const byte ButtonBorder = 0x22;
	private const byte DisabledColor = 0x26;

	/// <summary><c>estext.bin</c> indices the screen prints.</summary>
	private const int FirstChassisNameText = 0x6e;
	private const int TitleText = 0xba;
	private const int FirstHeadingText = 0xbc;
	private const int SalvageAvailableText = 0xc1;
	private const int ScrapHercText = 0xc2;
	private const int BuildHercText = 0xc3;
	private const int ScrapText = 0xc4;
	private const int BuildText = 0xc5;
	private const int TonsText = 0xc6;
	private const int KphText = 0xc7;

	/// <summary><c>Mass</c>, then <c>Speed</c>, <c>Hardpoints</c> and <c>Salvage Reqd</c> — <c>0xbb</c> and then <c>0xbe</c>-<c>0xc0</c>, around the two headings.</summary>
	private static readonly int[] StatLabelTexts = { 0xbb, 0xbe, 0xbf, 0xc0 };
}
