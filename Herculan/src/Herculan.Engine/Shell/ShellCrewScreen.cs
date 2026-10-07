using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The two portrait banks the crew screen loads in its builder: <c>dba\c_pilots.dba</c>, one frame
/// per roster id, and <c>dba\star.dba</c>, whose single frame stands for the player.
/// </summary>
public sealed record ShellCrewPortraits(DynamixBitmap[]? Pilots, DynamixBitmap[]? Star) {
	/// <summary>Reads both banks. A missing one leaves its portraits blank rather than failing the screen.</summary>
	public static ShellCrewPortraits Load(GameContent content) =>
		new(ShellArt.ReadBankFrames(content, "C_PILOTS"), ShellArt.ReadBankFrames(content, "STAR"));

	/// <summary>A pilot's portrait, the <c>c_pilots</c> frame their roster id names.</summary>
	public DynamixBitmap? Pilot(int rosterId) =>
		Pilots != null && rosterId >= 0 && rosterId < Pilots.Length ? Pilots[rosterId] : null;

	/// <summary>The player's portrait, which is always the star.</summary>
	public DynamixBitmap? Player => Star is { Length: > 0 } ? Star[0] : null;
}

/// <summary>
/// Tab 6, <c>CREW</c> — the <c>PILOT ASSIGNMENT</c> panel: the three squad members as portraits across
/// the top, and four rows below them, the player's and one per squad position, each with a portrait
/// and the pilot's name, skill and machine. Built by <c>Crew_BuildScreen</c> (<c>00440eb8</c>,
/// <c>wcrewi.cpp</c>), entered by <c>Crew_Enter</c> (<c>00441a01</c>), its rows coloured by
/// <c>Crew_ColourRows</c> (<c>00441857</c>), matched to pilots by <c>Crew_MatchRowPilots</c> (<c>00441b08</c>), selected by <c>Crew_SelectRow</c> (<c>00441b85</c>) and
/// filled by <c>Crew_FillRows</c> (<c>00441c4f</c>). See docs/retail/shell/screen-layout.md, "The crew screen".
///
/// <para><b>The screen's widgets are built once and outlive a visit</b>, so one instance serves every
/// entry: <see cref="Enter"/> is the tab's entry, and a lit squad portrait stays lit across visits
/// as the original's does.</para>
///
/// <para><b>Every rect here is a literal in the executable</b> and they are kept parent-relative as
/// the builder writes them: the content panel in the canvas, the portraits, the label and CLEAR in the
/// panel, the four rows in the panel, and each row's portrait and six texts in that row.</para>
///
/// <para><b>The left of the canvas is the squad panel</b> — the bay picture, the pilot readout and the
/// <c>Squad Inventory</c> roster, which <see cref="ShellSquadPanel"/> draws for this tab and three
/// others. This screen decides which bay it shows.</para>
///
/// <para><b>Three clicks change the crew</b>, all against the selected row: a squad portrait moves that
/// squad member into the row, a <c>Squad Inventory</c> row gives its bay to the row's pilot, and
/// <c>CLEAR</c> empties the row. A click on a row selects it and puts its pilot's bay up.</para>
/// </summary>
public sealed class ShellCrewScreen {
	/// <summary>The four rows: the player's, then squad positions 1-3.</summary>
	public const int RowCount = 4;

	/// <summary>The squad members offered across the top.</summary>
	public const int AvailablePilotCount = 3;

	/// <summary>The content panel, in the canvas — the same rect as the repair screen's.</summary>
	public static readonly ShellRect PanelRect = new(0xf1, 0x2b, 0x278, 0x1d9);

	/// <summary>The three squad members' portraits, in the panel, each an image panel with its bitmap one pixel in.</summary>
	private static readonly ShellRect[] AvailablePortraitRects = {
		new(0xbc, 0x19, 0xf7, 0x50),
		new(0xfc, 0x19, 0x137, 0x50),
		new(0x13c, 0x19, 0x177, 0x50),
	};

	private static readonly ShellRect AvailableLabelRect = new(10, 0x1d, 0xb4, 0x29);
	private static readonly ShellRect ClearRect = new(0x99, 0x188, 0xdf, 0x197);

	/// <summary>The rows, in the panel: 71 tall on a 74-pixel pitch.</summary>
	private const int RowLeft = 0xd;
	private const int RowRight = 0x178;
	private const int FirstRowTop = 0x58;
	private const int RowPitch = 0x4a;
	private const int RowBottomOffset = 0x46;

	/// <summary>A row's portrait, in the row. Its bitmap sits at the corner, under the border.</summary>
	private static readonly ShellRect RowPortraitRect = new(0xb, 9, 0x43, 0x3d);

	/// <summary>The three labels, right-aligned, and the three values, left-aligned, in the row.</summary>
	private static readonly ShellRect[] LabelRects = {
		new(100, 0xb, 0xec, 0x18),
		new(100, 0x18, 0xec, 0x25),
		new(100, 0x25, 0xec, 0x32),
	};

	private static readonly ShellRect[] ValueRects = {
		new(0xef, 0xb, 0x164, 0x18),
		new(0xef, 0x18, 0x164, 0x25),
		new(0xef, 0x25, 0x164, 0x32),
	};

	private readonly ShellBayPictures? _pictures;
	private readonly ShellCrewPortraits? _portraits;
	private ShellHangar _hangar;

	/// <summary>
	/// Builds the screen and enters it with <paramref name="bay"/> selected, the way the previous tab
	/// left <c>SelectedBaySlot</c> (<c>00482ae5</c>); the entry then moves it — see <see cref="SelectedBay"/>.
	/// </summary>
	public ShellCrewScreen(ShellHangar? hangar = null, int bay = -1, ShellBayPictures? pictures = null,
			ShellCrewPortraits? portraits = null) {
		_hangar = hangar ?? ShellHangar.From(null);
		_pictures = pictures;
		_portraits = portraits;
		Enter(_hangar, bay);
	}

	/// <summary>
	/// <c>CrewSelectedRow</c> (<c>004776dc</c>) — which of the four rows is selected. It is 0 in the image, and every entry
	/// leaves it at 0.
	/// </summary>
	public int SelectedRow { get; private set; }

	/// <summary>
	/// Which of the three squad portraits was clicked last, or <c>-1</c>. Its border is lit
	/// (<c>0x29</c>) and the others' are not; <c>Crew_BuildScreen</c> builds all three unlit and no
	/// entry resets them, so the highlight is whatever the last click left.
	/// </summary>
	public int LitPortrait { get; private set; } = -1;

	/// <summary>
	/// <c>SelectedBaySlot</c> (<c>00482ae5</c>), the bay the squad panel shows. <b>Entering the screen leaves it on the last
	/// bay that holds a finished machine</b>: after selecting the player's row, the entry offers every
	/// occupied bay in turn to <c>Squad_SelectBay</c> (<c>0043d64d</c>), and the crew arm accepts each finished one — so the
	/// readout is not the player's until the player's row is clicked. See docs/retail/shell/screen-layout.md,
	/// "The crew screen".
	/// </summary>
	public int SelectedBay { get; private set; }

	/// <summary>
	/// The pilot a row shows — the player on row 0 (the record embedded in the player structure), and
	/// on rows 1-3 the squad member whose position is that row (<c>Crew_MatchRowPilots</c>, <c>00441b08</c>), or null.
	/// </summary>
	public ShellBayPilot? RowPilot(int row) => row == 0 ? _hangar.Player : _hangar.SquadMemberAt(row);

	/// <summary>
	/// <c>Crew_Enter</c> (<c>00441a01</c>), the tab's entry, over <paramref name="hangar"/> with
	/// <paramref name="bay"/> as the bay the previous tab left selected. The tab handler sets
	/// <c>CurrentTabIndex</c> (<c>0047581c</c>) to 6 before calling it — the one tab handler that does so before its builder
	/// rather than after — so every bay move below takes <c>Squad_SelectBay</c> (<c>0043d64d</c>)'s
	/// crew arm.
	/// </summary>
	public void Enter(ShellHangar hangar, int bay) {
		_hangar = hangar;
		SelectedBay = bay;

		for (int row = 0; row < RowCount; row++) {
			SelectRow(row);
		}

		SelectRow(0);

		for (int slot = 0; slot < ShellHangar.BayCount; slot++) {
			if (_hangar.Bay(slot) != null) {
				SelectBay(slot, rosterClick: false);
			}
		}
	}

	/// <summary>
	/// <c>Crew_SelectRow</c> (<c>00441b85</c>), which a row and its portrait both answer with: moves the
	/// bay to the row's pilot's — the player's own bay for row 0, none for a row with no pilot — and
	/// only then makes the row the selected one, so the Razor test inside the move sees the row being
	/// left. It has no early return, so clicking the selected row runs it again.
	/// </summary>
	public void SelectRow(int row) {
		if (row < 0 || row >= RowCount) {
			return;
		}

		SelectBay(RowPilot(row)?.Bay ?? -1, rosterClick: false);
		SelectedRow = row;
	}

	/// <summary>
	/// A <c>Squad Inventory</c> row click on this tab: <c>Squad_SelectBay</c> (<c>0043d64d</c>) with
	/// <c>Squad_RosterClickFlag</c> (<c>004765be</c>) set, so a bay the crew arm accepts goes to the selected row's pilot. Returns
	/// whether the bay moved; the bay already selected, and one the arm refuses, change nothing.
	/// </summary>
	public bool ClickRoster(int bay) => SelectBay(bay, rosterClick: true);

	/// <summary>
	/// A squad portrait's handler (<c>Crew_OnSquadPortrait0</c> (<c>00442151</c>), <c>Crew_OnSquadPortrait1</c> (<c>004421fc</c>), <c>Crew_OnSquadPortrait2</c> (<c>004422a7</c>)): lights
	/// portrait <paramref name="member"/> and unlights the other two, then
	/// <c>Crew_AssignSquadMember(member)</c> (<c>00441eb8</c>). On the player's row the portrait
	/// lights and nothing else happens. On a squad row, whoever holds the row's position gives it up,
	/// the member takes it, and the member is then given the selected bay by the same assignment a
	/// roster click makes.
	/// </summary>
	public void ClickPortrait(int member) {
		if (member < 0 || member >= AvailablePilotCount) {
			return;
		}

		LitPortrait = member;
		if (SelectedRow == 0) {
			return;
		}

		int holder = _hangar.SquadMemberIndexAt(SelectedRow);
		if (holder != -1) {
			_hangar.SetSquadPosition(holder, -1);
		}

		_hangar.SetSquadPosition(member, SelectedRow);
		AssignSelectedBay();
	}

	/// <summary>
	/// <c>CLEAR</c>, <c>Crew_ClearRow</c> (<c>00441f94</c>): on a squad row holding a pilot, takes
	/// the pilot out of their bay and off strength, frees the position, and moves the bay to none. On
	/// the player's row, or an empty one, it does nothing.
	/// </summary>
	public void Clear() {
		if (SelectedRow == 0) {
			return;
		}

		int member = _hangar.SquadMemberIndexAt(SelectedRow);
		if (member == -1) {
			return;
		}

		_hangar.UnassignSquadMemberBay(member);
		_hangar.SetSquadPosition(member, -1);

		// The original passes the member's bay, which the line before has just made -1; with the row now
		// empty the assignment inside finds no pilot and gives nothing.
		SelectBay(_hangar.SquadMembers[member].Bay, rosterClick: true);
	}

	/// <summary>
	/// <c>Squad_SelectBay</c> (<c>0043d64d</c>)'s crew arm. Returns at once for the bay already selected;
	/// otherwise takes <c>-1</c> or a bay <see cref="ShellSquadPanel.CanSelectForCrew"/> accepts, and on
	/// a roster click (<c>Squad_RosterClickFlag</c> (<c>004765be</c>) set) gives it to the selected row's pilot.
	/// </summary>
	private bool SelectBay(int bay, bool rosterClick) {
		if (bay == SelectedBay || !ShellSquadPanel.CanSelectForCrew(_hangar, bay, SelectedRow)) {
			return false;
		}

		SelectedBay = bay;
		if (rosterClick) {
			AssignSelectedBay();
		}

		return true;
	}

	/// <summary>
	/// <c>Crew_AssignSelectedBay</c> (<c>00442055</c>): gives <see cref="SelectedBay"/> to the
	/// selected row's pilot, when the row has one. Whoever held the bay loses it first — the player
	/// through <c>Player_SetBay</c> (<c>0040e6c8</c>), a squad member through <c>Squad_SetMemberBay</c> (<c>0040e6d7</c>), which also takes them
	/// off strength — and a squad member's on-strength byte is then recomputed for the row's position.
	/// With no bay selected the bay given is <c>-1</c>, and every squad member without a bay counts as
	/// its holder.
	/// </summary>
	private void AssignSelectedBay() {
		var pilot = RowPilot(SelectedRow);
		if (pilot == null) {
			return;
		}

		if (_hangar.Player is { } player && player.Bay == SelectedBay) {
			ShellHangar.SetBay(player, -1);
		}

		for (int member = 0; member < _hangar.SquadMembers.Count; member++) {
			if (_hangar.SquadMembers[member].Bay == SelectedBay) {
				_hangar.UnassignSquadMemberBay(member);
			}
		}

		ShellHangar.SetBay(pilot, SelectedBay);
		if (SelectedRow != 0) {
			_hangar.UpdateOnStrength(SelectedRow);
		}
	}

	/// <summary>
	/// The row under a canvas point, or null — its panel and everything drawn in it: the texts take no mouse
	/// events, so a click on one reaches the row, and the portrait carries the row's own handler
	/// (docs/retail/shell/screen-layout.md#which-widget-a-click-reaches).
	/// </summary>
	public static int? RowAt(float canvasX, float canvasY) {
		for (int row = 0; row < RowCount; row++) {
			if (RowRect(row).Contains(canvasX, canvasY)) {
				return row;
			}
		}

		return null;
	}

	/// <summary>The squad portrait under a canvas point, or null.</summary>
	public static int? PortraitAt(float canvasX, float canvasY) {
		for (int member = 0; member < AvailablePilotCount; member++) {
			if (Inside(PanelRect, AvailablePortraitRects[member]).Contains(canvasX, canvasY)) {
				return member;
			}
		}

		return null;
	}

	/// <summary>Whether a canvas point is on <c>CLEAR</c>.</summary>
	public static bool IsClearAt(float canvasX, float canvasY) =>
		Inside(PanelRect, ClearRect).Contains(canvasX, canvasY);

	/// <summary>
	/// What the pointer hits on the panel: a squad portrait, <c>CLEAR</c>, a row's portrait, or a row and
	/// which of its six texts. The portraits are image panels, which act on a left release alone; a
	/// row's portrait carries the row's handler and is still a widget of its own.
	/// </summary>
	public static ShellHit? HitAt(float canvasX, float canvasY) {
		if (PortraitAt(canvasX, canvasY) is { } member) {
			return new ShellHit(new ShellWidget(ShellWidgetKind.CrewSquadPortrait, member), ShellHandler.ImagePanel);
		}

		if (IsClearAt(canvasX, canvasY)) {
			return ShellHit.Button(new ShellWidget(ShellWidgetKind.CrewClear, 0), Inside(PanelRect, ClearRect),
				canvasX, canvasY);
		}

		if (RowAt(canvasX, canvasY) is not { } row) {
			return null;
		}

		var rect = RowRect(row);
		if (Inside(rect, RowPortraitRect).Contains(canvasX, canvasY)) {
			return new ShellHit(new ShellWidget(ShellWidgetKind.CrewRowPortrait, row), ShellHandler.ImagePanel);
		}

		// Crew_BuildScreen (00440eb8) builds each row's three labels and then its three values, so a label
		// sharing an edge row with the one below it gives way to it.
		return new ShellHit(new ShellWidget(ShellWidgetKind.CrewRow, row), ShellHandler.Control,
			ShellHit.LeafAt(rect, [.. LabelRects, .. ValueRects], canvasX, canvasY));
	}

	/// <summary>One row's rect, in the canvas.</summary>
	public static ShellRect RowRect(int row) =>
		new(PanelRect.X0 + RowLeft, PanelRect.Y0 + row * RowPitch + FirstRowTop,
			PanelRect.X0 + RowRight, PanelRect.Y0 + row * RowPitch + FirstRowTop + RowBottomOffset);

	/// <summary>
	/// Draws the whole screen into <paramref name="surface"/>, the squad panel included. The caller
	/// clears it first; the content panel's body is filled, as the repair screen's is.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellSquadPanel.Paint(surface, font, text, _hangar, SelectedBay, _pictures);

		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, ShellChrome.InteriorColor,
			TitleHeight, headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: true);
		ShellChrome.PaintText(surface,
			new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight), font,
			text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		var squad = _hangar.SquadMembers;
		for (int i = 0; i < AvailablePilotCount; i++) {
			ShellChrome.PaintImagePanel(surface, Inside(PanelRect, AvailablePortraitRects[i]),
				i < squad.Count ? _portraits?.Pilot(squad[i].RosterId) : null, 1, 1,
				i == LitPortrait ? LitBorder : UnlitBorder);
		}

		PaintClearButton(surface, font, text);
		ShellChrome.PaintText(surface, Inside(PanelRect, AvailableLabelRect), font,
			text?.Text(AvailablePilotsText), ShellTextAlign.Right, ShellChrome.FontInkColor);

		for (int row = 0; row < RowCount; row++) {
			PaintRow(surface, font, text, row);
		}
	}

	/// <summary>
	/// One row: a line-filled box with an inner border, lit when it is the selected row, holding a
	/// portrait and three label/value pairs. Rows inside the squad's positions (<c>Crew_ColourRows</c>, <c>00441857</c>)
	/// take a <c>0x25</c> body, greyer labels and value backing to match; the rest keep the shell's
	/// background. The values are drawn in <c>0x29</c> whichever: <c>Crew_ColourRows</c> writes them
	/// <c>0x16</c> or <c>0x17</c>, and <c>Crew_FillRows</c> (<c>00441c4f</c>)'s <c>ESMessage_SetString</c> then overwrites that
	/// with its own <c>0x29</c>.
	/// </summary>
	private void PaintRow(ShellSurface surface, HudFont? font, ShellText? text, int row) {
		var rect = RowRect(row);
		bool inPlay = row < _hangar.SquadPositions;
		byte border = row == SelectedRow ? LitBorder : UnlitBorder;
		byte body = inPlay ? InPlayBody : ShellChrome.InteriorColor;
		byte labelColor = inPlay ? InPlayLabelColor : LabelColor;

		ShellChrome.PaintHatchedDivider(surface, rect, border, body, firstLine: 0, innerBorder: true);

		var pilot = RowPilot(row);
		var portrait = row == 0 ? _portraits?.Player : pilot != null ? _portraits?.Pilot(pilot.RosterId) : null;
		ShellChrome.PaintImagePanel(surface, Inside(rect, RowPortraitRect), portrait, 0, 0, border);

		// Crew_FillRows (00441c4f): a row with no pilot has all three values set to estext.bin entry 0, the empty
		// string, and a pilot with no machine reads None.
		string?[] values = pilot == null
			? new string?[] { null, null, null }
			: new[] {
				pilot.Name,
				text?.Text(FirstSkillText + pilot.Skill),
				text?.Text(_hangar.Bay(pilot.Bay) is { } machine
					? FirstChassisNameText + machine.ChassisType : NoHercText),
			};

		for (int i = 0; i < LabelRects.Length; i++) {
			ShellChrome.PaintText(surface, Inside(rect, LabelRects[i]), font, text?.Text(FirstLabelText + i),
				ShellTextAlign.Right, labelColor);
			ShellChrome.PaintText(surface, Inside(rect, ValueRects[i]), font, values[i], ShellTextAlign.Left,
				ShellChrome.FontInkColor, body);
		}
	}

	/// <summary>
	/// CLEAR, a live button no code greys. Its caption is the <c>Text</c> child <c>ESButtonFont_Ctor</c>
	/// builds at <c>{1, 0, w, h}</c> in the button, so it is centred one pixel right of the button's
	/// own rect.
	/// </summary>
	private static void PaintClearButton(ShellSurface surface, HudFont? font, ShellText? text) {
		var rect = Inside(PanelRect, ClearRect);
		ShellChrome.PaintButton(surface, rect, ButtonBorder);
		ShellChrome.PaintText(surface, new ShellRect(rect.X0 + 1, rect.Y0, rect.X1, rect.Y1), font,
			text?.Text(ClearText), ShellTextAlign.Center, ShellChrome.FontInkColor);
	}

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary>The content panel's fields, written by the builder over the class defaults.</summary>
	private const byte PanelBorder = 0x27;
	private const byte PanelFace = 0x25;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x7e;
	private const int TitlePlateLast = 0x108;

	/// <summary>The border every row and portrait takes, and the one the selected row and its portrait take.</summary>
	private const byte UnlitBorder = 0x21;
	private const byte LitBorder = 0x29;

	private const byte ButtonBorder = 0x22;

	/// <summary><c>Crew_ColourRows</c> (<c>00441857</c>)'s two sets: a row inside the squad's positions, and one outside them.</summary>
	private const byte InPlayBody = 0x25;
	private const byte InPlayLabelColor = 0x19;
	private const byte LabelColor = 0x1a;

	/// <summary><c>estext.bin</c> indices the screen prints.</summary>
	private const int FirstSkillText = 0x35;
	private const int FirstChassisNameText = 0x6e;
	private const int NoHercText = 0x7e;
	private const int TitleText = 0xa8;
	private const int ClearText = 0xa9;
	private const int AvailablePilotsText = 0xab;

	/// <summary><c>Name:</c>, <c>Skill:</c> and <c>Herc:</c>, consecutive from here.</summary>
	private const int FirstLabelText = 0xac;
}
