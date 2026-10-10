using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The squad panel <c>wsquadi.cpp</c> shares across the WEAPONS, REPAIR, BUILD and CREW tabs — the
/// whole left-hand column: the picture of the selected bay, the <c>Pilot:</c>/<c>Skill:</c>/
/// <c>Condition:</c> readout under it, and the eight-row <c>Squad Inventory</c> list that selects the
/// bay. Built once by <c>Squad_BuildRosterList</c> (<c>0043c999</c>), shown by <c>Squad_ShowPanel</c>
/// (<c>0043cfe7</c>), its readout filled by <c>Squad_RefreshReadout</c> (<c>0043d38a</c>) and its rows by <c>Squad_RefreshRowNames</c> (<c>0043da47</c>)
/// and <c>Squad_RefreshRowCrew</c> (<c>0043dad7</c>). The rects are literals in the canvas. See docs/retail/shell/squad-and-crew.md,
/// "The squad panel".
///
/// <para><b>What the picture shows depends on the tab.</b> WEAPONS, BUILD and CREW fill it with the
/// three-quarter view <see cref="ShellBayPictures"/> draws; the repair tab refills the same widgets with
/// its damage diagram, which <see cref="ShellRepairDiagrams"/> draws, and passes no pictures here.</para>
///
/// <para><b>Each tab takes a roster click its own way</b>, all inside <c>Squad_SelectBay</c> (<c>0043d64d</c>).
/// <see cref="CanSelect"/> is the repair tab's rule and <see cref="CanSelectForCrew"/> the crew
/// tab's.</para>
/// </summary>
public static class ShellSquadPanel {
	/// <summary>The <c>Squad Inventory</c> panel, in the canvas.</summary>
	public static readonly ShellRect PanelRect = new(6, 0x154, 0xec, 0x1da);

	/// <summary>The band the six readout fields below fill between them, from the pilot's label to the condition's value.</summary>
	public static ShellRect ReadoutRect => new(PilotLabelRect.X0, PilotLabelRect.Y0, ConditionValueRect.X1,
		ConditionValueRect.Y1);

	private static readonly ShellRect PilotLabelRect = new(5, 0x134, 0x25, 0x141);
	private static readonly ShellRect PilotValueRect = new(0x28, 0x134, 0x82, 0x141);
	private static readonly ShellRect SkillLabelRect = new(5, 0x141, 0x25, 0x14e);
	private static readonly ShellRect SkillValueRect = new(0x28, 0x141, 0x82, 0x14e);
	private static readonly ShellRect ConditionLabelRect = new(0x8d, 0x134, 0xea, 0x141);
	private static readonly ShellRect ConditionValueRect = new(0x8d, 0x141, 0xea, 0x14e);

	/// <summary>The rows, in the panel: 14 tall on a 14-pixel pitch, so unlike the repair lists they do not overlap.</summary>
	private const int RowLeft = 9;
	private const int RowRight = 0xe2;
	private const int FirstRowTop = 0x16;
	private const int RowPitch = 0xe;
	private const int RowBottomOffset = 0xd;

	/// <summary>
	/// The four text columns <c>ESQuad_AddColumns</c> is given: the number centred in <c>2</c>-<c>0x21</c>,
	/// the chassis name left in <c>0x21</c>-<c>0x70</c>, a dash centred in <c>0x70</c>-<c>0x7b</c>, and
	/// the pilot left from <c>0x7b</c> to two inside the row.
	/// </summary>
	private const int NumberLeft = 2;
	private const int NameLeft = 0x21;
	private const int DashLeft = 0x70;
	private const int PilotLeft = 0x7b;

	private const byte PanelBorder = 0x27;
	private const byte HeaderFace = 0x24;
	private const int TitleHeight = 0x13;
	private const byte LabelColor = 0x1a;
	private const byte ValueColor = 0x17;
	private const byte RowTextColor = 0x27;
	private const byte SelectedRowBorder = 0x29;

	/// <summary>The colour an unfinished machine's <c>N% Complete</c> is printed in, a literal in <c>Squad_RefreshReadout</c> (<c>0043d38a</c>).</summary>
	private const byte BuildingColor = 0x20;

	private const int TitleText = 0x64;
	private const int PilotLabelText = 0x65;
	private const int SkillLabelText = 0x66;
	private const int ConditionLabelText = 0x67;
	private const int CompleteText = 0x6c;
	private const int UnassignedText = 0x6d;
	private const int FirstSkillText = 0x35;

	/// <summary><c>HercRecord_ResolveName</c>'s run: a machine's name is <c>estext.bin</c> <c>0x6e + type</c>.</summary>
	private const int FirstChassisNameText = 0x6e;

	/// <summary>The dash between a row's machine and its pilot, the string at <c>004765b8</c>.</summary>
	private const string Dash = "-";

	/// <summary>One row's rect, in the canvas.</summary>
	public static ShellRect RowRect(int bay) =>
		new(PanelRect.X0 + RowLeft, PanelRect.Y0 + bay * RowPitch + FirstRowTop,
			PanelRect.X0 + RowRight, PanelRect.Y0 + bay * RowPitch + FirstRowTop + RowBottomOffset);

	/// <summary>The bay whose row is under a canvas point, or null.</summary>
	public static int? RowAt(float canvasX, float canvasY) {
		for (int bay = 0; bay < ShellHangar.BayCount; bay++) {
			if (RowRect(bay).Contains(canvasX, canvasY)) {
				return bay;
			}
		}

		return null;
	}

	/// <summary>What the pointer hits on the roster: a row, and which of its four text columns.</summary>
	public static ShellHit? HitAt(float canvasX, float canvasY) =>
		RowAt(canvasX, canvasY) is { } bay
			? ShellHit.ListRow(new ShellWidget(ShellWidgetKind.SquadRow, bay), RowRect(bay), NameLeft, DashLeft,
				PilotLeft, canvasX, canvasY)
			: null;

	/// <summary>
	/// <c>Squad_SelectBay</c> (<c>0043d64d</c>)'s repair-tab arm: a row answers only when its bay holds a finished machine.
	/// Clicking an empty bay or one still being built does nothing at all.
	/// </summary>
	public static bool CanSelect(ShellHangar hangar, int bay) => hangar.Bay(bay) is { IsBuilt: true };

	/// <summary>
	/// <c>Squad_SelectBay</c> (<c>0043d64d</c>)'s crew-tab arm: no bay at all, or a bay holding a finished machine — and not
	/// the Razor unless <paramref name="crewRow"/> is the player's own row, 0, since a squad member
	/// cannot fly it. The row it tests is the crew screen's selection at the moment of the call, which
	/// on a crew-row click is still the row being left.
	/// </summary>
	public static bool CanSelectForCrew(ShellHangar hangar, int bay, int crewRow) =>
		bay == -1 || (hangar.Bay(bay) is { IsBuilt: true } machine
			&& (crewRow == 0 || machine.ChassisType != ShellRepairScreen.FlyerChassisType));

	/// <summary>
	/// Draws the picture of <paramref name="selectedBay"/> when <paramref name="pictures"/> is given, with
	/// the weapons screen's socket <paramref name="outline"/> on it, then the readout for it and the eight
	/// rows under it.
	/// </summary>
	public static void Paint(ShellSurface surface, HudFont? font, ShellText? text, ShellHangar hangar,
			int selectedBay, ShellBayPictures? pictures = null, ShellGridPart? outline = null) {
		pictures?.Paint(surface, hangar, selectedBay, outline);
		PaintReadout(surface, font, text, hangar, selectedBay);

		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, HeaderFace, ShellChrome.InteriorColor,
			TitleHeight, headerChrome: false, 0, 0, fill: true);
		ShellChrome.PaintText(surface,
			new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight), font,
			text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		for (int bay = 0; bay < ShellHangar.BayCount; bay++) {
			PaintRow(surface, font, text, hangar, bay, bay == selectedBay);
		}
	}

	/// <summary>
	/// <c>Squad_RefreshReadout</c> (<c>0043d38a</c>). The pilot is the bay's assigned pilot or <c>Unassigned</c>; the skill is
	/// theirs or blank. The condition is the band word for the machine's overall condition in the
	/// band's colour, or <c>N% Complete</c> while it is still being built.
	/// </summary>
	private static void PaintReadout(ShellSurface surface, HudFont? font, ShellText? text,
			ShellHangar hangar, int bay) {
		var machine = hangar.Bay(bay);
		var pilot = hangar.PilotFor(bay);

		string? pilotName = pilot?.Name ?? (machine != null ? text?.Text(UnassignedText) : null);
		string? skill = pilot != null ? text?.Text(FirstSkillText + pilot.Skill) : null;

		string? condition = null;
		byte conditionColor = ValueColor;
		if (machine is { IsBuilt: true }) {
			int overall = machine.OverallCondition;
			condition = text?.Text(ShellRepairScreen.ConditionWordText(overall));
			conditionColor = ShellRepairScreen.BandColor(overall);
		} else if (machine != null) {
			condition = $"{machine.BuildPercent}{text?.Text(CompleteText)}";
			conditionColor = BuildingColor;
		}

		ShellChrome.PaintText(surface, PilotLabelRect, font, text?.Text(PilotLabelText), ShellTextAlign.Left,
			LabelColor);
		ShellChrome.PaintText(surface, PilotValueRect, font, pilotName, ShellTextAlign.Left, ValueColor,
			ShellChrome.InteriorColor);
		ShellChrome.PaintText(surface, SkillLabelRect, font, text?.Text(SkillLabelText), ShellTextAlign.Left,
			LabelColor);
		ShellChrome.PaintText(surface, SkillValueRect, font, skill, ShellTextAlign.Left, ValueColor,
			ShellChrome.InteriorColor);
		ShellChrome.PaintText(surface, ConditionLabelRect, font, text?.Text(ConditionLabelText),
			ShellTextAlign.Right, LabelColor);
		ShellChrome.PaintText(surface, ConditionValueRect, font, condition, ShellTextAlign.Right,
			conditionColor, ShellChrome.InteriorColor);
	}

	/// <summary>
	/// One roster row. The machine's name is in its overall condition's band colour, whether or not it
	/// is finished (<c>Squad_RefreshRowNames</c>, <c>0043da47</c>); the last column is the pilot, or <c>N% Complete</c> for a
	/// machine still being built (<c>Squad_RefreshRowCrew</c>, <c>0043dad7</c>). The selected row's border is lit.
	/// </summary>
	private static void PaintRow(ShellSurface surface, HudFont? font, ShellText? text, ShellHangar hangar,
			int bay, bool selected) {
		var rect = RowRect(bay);
		var machine = hangar.Bay(bay);
		ShellChrome.PaintPanel(surface, rect, selected ? SelectedRowBorder : ShellChrome.InteriorColor,
			fill: true);

		string? name = machine != null ? text?.Text(FirstChassisNameText + machine.ChassisType) : null;
		string? crew = machine is { IsBuilt: false }
			? $"{machine.BuildPercent}{text?.Text(CompleteText)}"
			: hangar.PilotFor(bay)?.Name;
		byte nameColor = ShellRepairScreen.BandColor(machine?.OverallCondition ?? 100);

		Column(surface, font, rect, NumberLeft, NameLeft, $"{bay + 1}.", ShellTextAlign.Center,
			ShellChrome.FontInkColor);
		Column(surface, font, rect, NameLeft, DashLeft, name, ShellTextAlign.Left, nameColor);
		Column(surface, font, rect, DashLeft, PilotLeft, Dash, ShellTextAlign.Center, ShellChrome.FontInkColor);
		Column(surface, font, rect, PilotLeft, rect.Width - 2, crew, ShellTextAlign.Left, RowTextColor);
	}

	private static void Column(ShellSurface surface, HudFont? font, ShellRect row, int left, int right,
			string? value, ShellTextAlign align, byte color) =>
		ShellChrome.PaintText(surface,
			new ShellRect(row.X0 + left, row.Y0, row.X0 + right, row.Y0 + row.Height - 1), font, value, align,
			color, ShellChrome.InteriorColor);
}
