using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The WEAPONS screen's buttons, in the order the builder constructs them: the four guidance kinds,
/// the missile rack's own name, and the two hardpoint steppers.
/// </summary>
public enum ShellWeaponsButton {
	Arm,
	Arh,
	Sarh,
	Eo,
	Weapon,
	PreviousHardpoint,
	NextHardpoint,
}

/// <summary>
/// What the WEAPONS screen loads beside the save: <c>gam\arm_weap.dat</c> and <c>dba\arm_weap.dba</c>,
/// the picture of each weapon and each guidance kind, and <c>wpn_desc.bin</c>, three lines of prose
/// per weapon id. Any of them missing leaves its part of the screen blank rather than failing it.
/// </summary>
public sealed class ShellWeaponsArt {
	private readonly Dictionary<int, UiHardpointGraphic> _weapons = new();
	private readonly Dictionary<int, UiHardpointGraphic> _guidance = new();
	private readonly DynamixBitmap[]? _bank;

	private ShellWeaponsArt(ArmWeap? layout, DynamixBitmap[]? bank, ShellText? descriptions) {
		_bank = bank;
		Descriptions = descriptions;
		foreach (var record in layout?.Entries ?? Array.Empty<UiHardpointGraphic>()) {
			_weapons[record.Id] = record;
		}

		foreach (var record in layout?.GuidancePanels ?? Array.Empty<UiHardpointGraphic>()) {
			_guidance[record.Id] = record;
		}
	}

	/// <summary><c>wpn_desc.bin</c>, opened by <c>Arming_BuildScreen</c> into <c>Arming_DescriptionBin</c> (<c>0048d728</c>).</summary>
	public ShellText? Descriptions { get; }

	/// <summary>How many weapons have a picture — 26 in the retail file.</summary>
	public int WeaponCount => _weapons.Count;

	public static ShellWeaponsArt Load(GameContent content) =>
		new(content.Read(ShellRepairCosts.CatalogFolder, "ARM_WEAP.DAT") is { } bytes
				? new ArmWeapTransformer().Parse(bytes) : null,
			ShellArt.ReadBankFrames(content, "ARM_WEAP"),
			ShellText.Load(content, "WPN_DESC.BIN"));

	/// <summary>A weapon's picture and where it sits in the picture box, or null for an id with no record.</summary>
	public (DynamixBitmap Frame, int X, int Y)? Weapon(int weaponId) => Picture(_weapons, weaponId);

	/// <summary>A guidance kind's picture, by the kind's own id, 0-3.</summary>
	public (DynamixBitmap Frame, int X, int Y)? Guidance(int kind) => Picture(_guidance, kind);

	private (DynamixBitmap Frame, int X, int Y)? Picture(Dictionary<int, UiHardpointGraphic> records, int id) =>
		records.TryGetValue(id, out var record) && _bank is { } bank
			&& record.FrameId >= 0 && record.FrameId < bank.Length
			? (bank[record.FrameId], record.OriginX, record.OriginY) : null;
}

/// <summary>
/// Tab 2, <c>WEAPONS</c> — the arming screen: a picture of the selected weapon over three lines of its
/// description, the <c>Weapons Inventory</c> list of every weapon the armory holds, and the
/// <c>Hard Points</c> steppers. Built once by <c>Arming_BuildScreen</c> (<c>0043e52c</c>,
/// <c>warmingi.cpp</c>), entered by <c>Arming_Enter</c> (<c>0043f548</c>), its row moved by
/// <c>Arming_SelectRow</c> (<c>0043f71c</c>) and its guidance kind by <c>Arming_ShowGuidance</c>
/// (<c>0043fd69</c>). See docs/retail/shell/weapons-and-repair.md, "The weapons screen".
///
/// <para><b>Every rect here is a literal in the executable</b>, kept parent-relative as the builder
/// writes them: the content panel in the canvas; the picture box, the list and the steppers in the
/// content panel; the rows in the list; the pictures, the five buttons and the description in the
/// picture box. A weapon's picture is the exception, placed by its <c>arm_weap.dat</c> record.</para>
///
/// <para><b>The left of the canvas is the squad panel</b>, which <see cref="ShellSquadPanel"/> draws.
/// This tab's arm of <c>Squad_SelectBay</c> (<c>0043d64d</c>) refuses an empty bay and one still being
/// built.</para>
///
/// <para><b>A row fits a weapon only with a hardpoint selected.</b> Every entry and every bay change
/// clears the hardpoint, and the steppers and the hotspots over the bay picture select one
/// (<c>Arming_SelectHardpoint</c>, <c>0043dbb2</c>), lighting the row of what it carries and outlining its
/// socket. With one selected, a row click fits that row's weapon into it through
/// <see cref="ShellHangar.FitMount"/> and a guidance button writes its kind into the mount. With none,
/// a row click only shows the weapon.</para>
/// </summary>
public sealed class ShellWeaponsScreen {
	/// <summary>
	/// <c>0x1b</c> rows, one per weapon the table at <c>004769b0</c> lists, in its order: thirteen in the
	/// left column and fourteen in the right, the last of them <c>None</c>.
	/// </summary>
	public static readonly int[] RowWeapons = {
		1, 2, 3, 4, 5, 6, 22, 7, 23, 18, 8, 9, 10,
		11, 12, 13, 14, 15, 16, 17, 24, 25, 29, 30, 31, 32, 0,
	};

	/// <summary>The rows in the left column; the rest are in the right.</summary>
	private const int LeftColumnRows = 0xd;

	/// <summary>The content panel, in the canvas — the repair and crew screens' rect.</summary>
	public static readonly ShellRect PanelRect = new(0xf1, 0x2b, 0x278, 0x1d9);

	/// <summary>The picture box, a line-filled divider, and the list, in the content panel.</summary>
	private static readonly ShellRect PictureBoxRect = new(6, 0x1a, 0x183, 0xc0);
	private static readonly ShellRect ListRect = new(6, 0xc5, 0x183, 400);

	/// <summary>The five buttons down the picture box's right edge, in the box: ARM, ARH, SARH, EO, and the rack's own name.</summary>
	private static readonly ShellRect[] PictureButtonRects = {
		new(0x152, 5, 0x179, 0x16),
		new(0x152, 0x1c, 0x179, 0x2d),
		new(0x152, 0x33, 0x179, 0x44),
		new(0x152, 0x4a, 0x179, 0x5b),
		new(0x101, 0x61, 0x179, 0x72),
	};

	/// <summary>The guidance kind each of the four buttons shows — its <c>wpn_desc.bin</c> block and its <c>arm_weap.dat</c> record.</summary>
	private static readonly int[] ButtonGuidance = { 2, 1, 0, 3 };

	/// <summary>The three description lines' tops and bottoms, in the box; each runs from 1 to the box's own right edge.</summary>
	private static readonly (int Top, int Bottom)[] DescriptionRows = { (0x7a, 0x88), (0x88, 0x94), (0x94, 0xa0) };

	/// <summary><c>Hard Points</c> and its two steppers, in the content panel.</summary>
	private static readonly ShellRect HardPointsLabelRect = new(0xe, 0x195, 0x76, 0x1a1);
	private static readonly ShellRect PreviousHardpointRect = new(0x7d, 0x194, 0x8b, 0x1a3);
	private static readonly ShellRect NextHardpointRect = new(0x91, 0x194, 0x9f, 0x1a3);

	/// <summary>The rows, in the list: 13 tall on a 13-pixel pitch, so they butt without overlapping.</summary>
	private const int LeftRowLeft = 9;
	private const int LeftRowRight = 0xaf;
	private const int RightRowLeft = 0xbf;
	private const int RightRowRight = 0x165;
	private const int FirstRowTop = 0x14;
	private const int RowPitch = 0xd;
	private const int RowBottomOffset = 0xc;

	/// <summary>
	/// Where <c>ESQuad_AddColumns</c> is told to cut a row: the name left from <c>2</c> to <c>0x8b</c>,
	/// two one-pixel columns holding a space, and the count right-aligned from <c>0x8d</c> to one inside
	/// the row's right edge.
	/// </summary>
	private const int NameRight = 0x8b;
	private const int SecondRight = 0x8c;
	private const int ThirdRight = 0x8d;

	/// <summary>The weapons that take a guidance kind: the three racks and the Razor's launcher.</summary>
	private const int FirstMissileRack = 0xd;
	private const int LastMissileRack = 0x10;

	/// <summary>Where the guidance kinds' prose starts in <c>wpn_desc.bin</c>, past three lines for each of the 33 weapon ids.</summary>
	private const int FirstGuidanceDescription = 99;
	private const int DescriptionLines = 3;

	private readonly ShellWeaponsArt? _art;
	private readonly ShellBayPictures? _pictures;
	private ShellHangar _hangar;

	/// <summary>
	/// Builds the screen and enters it with <paramref name="bay"/> selected, the bay the previous tab
	/// left in <c>SelectedBaySlot</c> (<c>00482ae5</c>).
	/// </summary>
	public ShellWeaponsScreen(ShellHangar? hangar = null, int bay = -1, ShellWeaponsArt? art = null,
			ShellBayPictures? pictures = null) {
		_hangar = hangar ?? ShellHangar.From(null);
		_art = art;
		_pictures = pictures;
		Enter(_hangar, bay);
	}

	/// <summary><c>SelectedBaySlot</c> (<c>00482ae5</c>), the bay the squad panel shows.</summary>
	public int SelectedBay { get; private set; } = -1;

	/// <summary>
	/// <c>Arming_LitRow</c> (<c>00476d5a</c>), the lit row, <c>-1</c> in the image. The widgets outlive a visit, so it does too.
	/// </summary>
	public int SelectedRow { get; private set; } = -1;

	/// <summary>
	/// <c>Arming_ShownGuidance</c> (<c>00476d5c</c>), the guidance kind last put up, <c>-1</c> in the image. Selecting a missile rack
	/// hides its picture and leaves the value, so it is not the same thing as <see cref="ShowingGuidance"/>.
	/// </summary>
	public int ShownGuidance { get; private set; } = -1;

	/// <summary>Whether the picture box shows <see cref="ShownGuidance"/>'s picture rather than the selected weapon's.</summary>
	public bool ShowingGuidance { get; private set; }

	/// <summary><c>Arming_GuidanceButtonsUp</c> (<c>004769e6</c>) — whether the five buttons are up. Only a missile rack's row puts them up.</summary>
	public bool GuidanceButtonsShown { get; private set; }

	/// <summary>The guidance button whose border is lit <c>0x20</c>, as an index into <see cref="ButtonGuidance"/>, or <c>-1</c>.</summary>
	private int _litGuidanceButton = -1;

	/// <summary>The first of the three <c>wpn_desc.bin</c> lines under the picture, or <c>-1</c> before any row is selected.</summary>
	private int _description = -1;

	/// <summary>
	/// <c>ArmingSelectedHardpoint</c>, the mount a row click fits into, <c>-1</c> for none. Only
	/// <see cref="SelectHardpoint"/> sets it; every entry and every bay change clears it.
	/// </summary>
	public int SelectedHardpoint { get; private set; } = -1;

	/// <summary>
	/// Part slot 12 of the bay picture, the socket outline <c>Arming_MarkHardpoint</c> (<c>004155db</c>) last
	/// drew. A bay change clears it and a mount with no outline record leaves it, so it can outline a
	/// socket other than the selected one. Leaving the tab clears every part of every bay picture
	/// (<c>Squad_FreeTabPictures</c>, <c>0043c95a</c>), so it does not outlive a visit.
	/// </summary>
	private ShellGridPart? _outline;

	/// <summary>What <c>Arming_RefreshRows</c> (<c>0043fbc6</c>) last made of each row.</summary>
	private enum RowState {
		Locked,
		Live,
		Dead,
	}

	/// <summary>
	/// Each row's state as the last <c>Arming_RefreshRows</c> left it. The refresh runs only inside a row
	/// selection that goes through, so a selection that returns early leaves the rows gated for whatever
	/// hardpoint was selected when it last ran — as the original's widgets do.
	/// </summary>
	private readonly RowState[] _rowState = Enumerable.Repeat(RowState.Live, RowWeapons.Length).ToArray();

	/// <summary>The weapon id a row lists.</summary>
	public static int WeaponOfRow(int row) => row >= 0 && row < RowWeapons.Length ? RowWeapons[row] : -1;

	/// <summary><c>Arming_RowOfWeapon</c> (<c>0043f6f7</c>) — the row listing a weapon id, or <c>-1</c>.</summary>
	public static int RowOfWeapon(int weaponId) => Array.IndexOf(RowWeapons, weaponId);

	/// <summary>The selected row's weapon id, or <c>-1</c>.</summary>
	public int SelectedWeapon => WeaponOfRow(SelectedRow);

	/// <summary>The machine in <see cref="SelectedBay"/>, or null.</summary>
	public ShellBayMachine? Machine => _hangar.Bay(SelectedBay);

	/// <summary>
	/// <c>Arming_Enter</c> (<c>0043f548</c>). When the bay it inherits is none, empty or unfinished it
	/// selects the first bay holding a finished machine (<c>Herc_FirstBuiltBay</c>, <c>00410c2a</c>); then it
	/// clears the hardpoint and selects row 0.
	///
	/// <para>The original selects that bay through <c>Squad_SelectBay</c> under the previous tab's rule,
	/// because the tab handler stores 2 in <c>CurrentTabIndex</c> (<c>0047581c</c>) only after the entry — a retail bug, recorded
	/// in KNOWN_ISSUES.md. This engine applies the arming tab's rule, which takes any finished machine.</para>
	/// </summary>
	public void Enter(ShellHangar hangar, int bay) {
		_hangar = hangar;
		SelectedBay = hangar.Bay(bay) is { IsBuilt: true } ? bay : hangar.FirstBuiltBay();
		_outline = null;
		SelectedHardpoint = -1;
		SelectRow(0, fit: false);
	}

	/// <summary>
	/// <c>Squad_SelectBay</c> (<c>0043d64d</c>)'s arming arm: an empty bay and an unfinished machine are
	/// refused. Otherwise the hardpoint and the old bay's outline are cleared, row 0 selected, and the
	/// bay taken. Returns whether the bay moved.
	/// </summary>
	public bool ClickRoster(int bay) {
		if (bay == SelectedBay || bay < 0 || bay >= ShellHangar.BayCount || _hangar.Bay(bay) is not { IsBuilt: true }) {
			return false;
		}

		SelectedHardpoint = -1;
		_outline = null;
		SelectRow(0, fit: false);
		SelectedBay = bay;
		return true;
	}

	/// <summary>
	/// <c>Arming_SelectRow</c> (<c>0043f71c</c>). A row's own handler, through the 27 thunks from
	/// <c>Arming_OnRow00</c> (<c>00440300</c>), is the only caller that passes <paramref name="fit"/> — the thunk's
	/// <c>Arming_FitArmed</c> — so a selection made by the entry, a bay change, a hardpoint or the rack
	/// button fits nothing.
	///
	/// <para>It is a no-op for the row already lit unless a guidance picture has been put up since or a
	/// hardpoint is selected. With a hardpoint it refuses a weapon the armory holds none of unless the
	/// mount already carries it; the test reads the mount's guidance kind first, so a mount whose kind
	/// equals the weapon's id lets it through. It then fits the weapon, regates the rows, lights the row
	/// and shows its weapon's picture — a blank box for <c>None</c> — and three lines of
	/// <c>wpn_desc.bin</c> from <c>id * 3</c>. A missile rack's row puts up the four guidance buttons and a
	/// fifth captioned with the rack's name, and with a hardpoint lights the mount's kind; any other row
	/// takes all five down and forgets the guidance kind. Returns whether it went through.</para>
	/// </summary>
	public bool SelectRow(int row, bool fit) {
		if (row < 0 || row >= RowWeapons.Length
			|| (row == SelectedRow && ShownGuidance == -1 && SelectedHardpoint == -1)) {
			return false;
		}

		int weapon = RowWeapons[row];
		if (SelectedHardpoint != -1 && weapon != 0 && Machine is { } machine) {
			int kind = machine.Mount(SelectedHardpoint)?.Guidance ?? ShellWeaponUnit.NoGuidance;
			if (kind != weapon && _hangar.WeaponsOwned(weapon) == 0 && machine.WeaponAt(SelectedHardpoint) != weapon) {
				return false;
			}
		}

		// Arming_FitSelected (0043dc44).
		if (SelectedHardpoint != -1) {
			if (fit && Machine is { } fitted) {
				_hangar.FitMount(fitted, SelectedHardpoint, weapon);
			}

			MarkHardpoint(SelectedHardpoint);
		}

		RefreshRows();
		SelectedRow = row;
		ShowingGuidance = false;
		_description = weapon * DescriptionLines;

		if (weapon is >= FirstMissileRack and <= LastMissileRack) {
			GuidanceButtonsShown = true;
			if (SelectedHardpoint != -1 && Machine is { } racked) {
				ShowGuidanceKind(racked.Mount(SelectedHardpoint)?.Guidance ?? ShellWeaponUnit.NoGuidance, show: false);
			}
		} else {
			GuidanceButtonsShown = false;
			_litGuidanceButton = -1;
			ShownGuidance = -1;
		}

		return true;
	}

	/// <summary>
	/// <c>Arming_SelectHardpoint</c> (<c>0043dbb2</c>), a hotspot's handler through the ten thunks
	/// <c>Arming_OnHardpoint0</c>-<c>Arming_OnHardpoint9</c> (<c>0043e15f</c>-<c>0043e4c8</c>), and what both steppers call: a no-op for the hardpoint already selected, otherwise
	/// it selects the row of the weapon the mount carries — <c>None</c>'s for an empty one — without
	/// fitting it, and outlines the socket. Returns whether the hardpoint moved.
	///
	/// <para>With no bay selected the original reads the mount through the pointer before the bay array
	/// (<c>00482abf</c>); this engine does nothing.</para>
	/// </summary>
	public bool SelectHardpoint(int hardpoint) {
		if (hardpoint == SelectedHardpoint || Machine is not { } machine) {
			return false;
		}

		SelectedHardpoint = hardpoint;
		SelectRow(RowOfWeapon(machine.WeaponAt(hardpoint)), fit: false);
		MarkHardpoint(hardpoint);
		return true;
	}

	/// <summary>
	/// <c>&gt;</c>'s handler (<c>Arming_OnNextHardpoint</c>, <c>004402a2</c>), <c>Arming_NextHardpoint</c> (<c>0043dd09</c>): the next mount
	/// modulo the capacity, so the first with none selected.
	/// </summary>
	public bool NextHardpoint() =>
		Machine is { MountCapacity: > 0 } machine && SelectHardpoint((SelectedHardpoint + 1) % machine.MountCapacity);

	/// <summary>
	/// <c>&lt;</c>'s handler (<c>Arming_OnPreviousHardpoint</c>, <c>00440244</c>), <c>Arming_PreviousHardpoint</c> (<c>0043dd49</c>): the mount
	/// before, wrapping from the first — or from none — to the last.
	/// </summary>
	public bool PreviousHardpoint() =>
		Machine is { MountCapacity: > 0 } machine
		&& SelectHardpoint((SelectedHardpoint < 1 ? machine.MountCapacity : SelectedHardpoint) - 1);

	/// <summary>
	/// <c>Arming_ShowGuidance(kind, 1)</c> (<c>0043fd69</c>) as a guidance button calls it: lights the
	/// button, swaps the weapon's picture for the kind's, prints the kind's three lines from
	/// <c>99 + kind * 3</c>, and writes the kind into the selected hardpoint's mount.
	/// </summary>
	public void ShowGuidance(int button) {
		if (button >= 0 && button < ButtonGuidance.Length) {
			ShowGuidanceKind(ButtonGuidance[button], show: true);
		}
	}

	/// <summary>
	/// <c>Arming_ShowGuidance(kind, show)</c>. The kind's button is lit, and none for the unguided kind 5.
	/// With <paramref name="show"/> clear the pictures are left as the row selection put them. Either way
	/// <c>Arming_SetMountGuidance</c> (<c>0043dcb6</c>) writes the kind into the selected hardpoint's mount
	/// when there is one and it is fitted.
	/// </summary>
	private void ShowGuidanceKind(int kind, bool show) {
		_litGuidanceButton = Array.IndexOf(ButtonGuidance, kind);
		if (show) {
			ShowingGuidance = true;
			ShownGuidance = kind;
			_description = FirstGuidanceDescription + kind * DescriptionLines;
		}

		if (SelectedHardpoint != -1 && Machine?.Mount(SelectedHardpoint) is { } unit) {
			unit.Guidance = kind;
		}
	}

	/// <summary>
	/// <c>Arming_MarkHardpoint</c> (<c>004155db</c>)'s outline. The weapon part it redraws is the one
	/// <see cref="ShellBayPictures"/> already draws from the mount.
	/// </summary>
	private void MarkHardpoint(int hardpoint) {
		if (Machine is { } machine && _pictures?.Outline(machine, hardpoint) is { } outline) {
			_outline = outline;
		}
	}

	/// <summary>
	/// <c>Arming_RefreshRows</c> (<c>0043fbc6</c>): a locked weapon's row is disabled with every column in
	/// <c>0x10</c>, the background; an unlocked one is live in <c>0x27</c> while <c>Arming_RowLive</c>
	/// (<c>004149fb</c>) holds and dead in <c>0x25</c> when it does not. With no hardpoint selected every
	/// unlocked row is live; with one, only a weapon the chassis's layout has a record for in that socket.
	/// </summary>
	private void RefreshRows() {
		for (int row = 0; row < RowWeapons.Length; row++) {
			int weapon = RowWeapons[row];
			_rowState[row] = !_hangar.IsWeaponUnlocked(weapon) ? RowState.Locked
				: SelectedHardpoint == -1 || Machine is not { } machine
					|| (_pictures?.HasSocket(machine.ChassisType, weapon, SelectedHardpoint) ?? true)
					? RowState.Live : RowState.Dead;
		}
	}

	/// <summary>Whether a row takes a click: the lit row always, any other as the last refresh left it.</summary>
	public bool IsRowEnabled(int row) => row == SelectedRow || (row >= 0 && row < RowWeapons.Length
		&& _rowState[row] == RowState.Live);

	/// <summary>One row's rect, in the canvas.</summary>
	public static ShellRect RowRect(int row) {
		var list = Inside(PanelRect, ListRect);
		bool left = row < LeftColumnRows;
		int top = (left ? row : row - LeftColumnRows) * RowPitch + FirstRowTop;
		return new ShellRect(list.X0 + (left ? LeftRowLeft : RightRowLeft), list.Y0 + top,
			list.X0 + (left ? LeftRowRight : RightRowRight), list.Y0 + top + RowBottomOffset);
	}

	/// <summary>One button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellWeaponsButton button) => button switch {
		ShellWeaponsButton.PreviousHardpoint => Inside(PanelRect, PreviousHardpointRect),
		ShellWeaponsButton.NextHardpoint => Inside(PanelRect, NextHardpointRect),
		_ => Inside(Inside(PanelRect, PictureBoxRect), PictureButtonRects[(int)button]),
	};

	private bool IsShown(ShellWeaponsButton button) =>
		button is ShellWeaponsButton.PreviousHardpoint or ShellWeaponsButton.NextHardpoint || GuidanceButtonsShown;

	/// <summary>
	/// What the pointer hits: a hotspot over the bay picture, a live row and which of its text columns,
	/// or a button that is up. A disabled row swallows a click, and so does the picture box, whose builder
	/// clears its enable flag — here the same as hitting nothing.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		if (_pictures?.HotspotAt(Machine, canvasX, canvasY) is { } hardpoint) {
			return new ShellHit(new ShellWidget(ShellWidgetKind.WeaponsHotspot, hardpoint), ShellHandler.Control);
		}

		foreach (var button in Enum.GetValues<ShellWeaponsButton>()) {
			if (IsShown(button) && ButtonRect(button).Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.WeaponsButton, (int)button), ButtonRect(button),
					canvasX, canvasY);
			}
		}

		for (int row = 0; row < RowWeapons.Length; row++) {
			var rect = RowRect(row);
			if (rect.Contains(canvasX, canvasY)) {
				return IsRowEnabled(row)
					? ShellHit.ListRow(new ShellWidget(ShellWidgetKind.WeaponsRow, row), rect, NameRight, SecondRight,
						ThirdRight, canvasX, canvasY)
					: null;
			}
		}

		return null;
	}

	/// <summary>
	/// Draws the whole screen into <paramref name="surface"/>, the squad panel included. The caller clears it first.
	/// <paramref name="lit"/> is the widget a press has lit.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellSquadPanel.Paint(surface, font, text, _hangar, SelectedBay, _pictures, _outline);

		ShellChrome.PaintTitledPanel(surface, PanelRect, Border, PanelFace, ShellChrome.InteriorColor, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: true);
		ShellChrome.PaintText(surface, TitleRect(PanelRect), font, text?.Text(TitleText), ShellTextAlign.Center,
			ShellChrome.FontInkColor);

		PaintList(surface, font, text);
		PaintPictureBox(surface, font, text, lit);

		ShellChrome.PaintText(surface, Inside(PanelRect, HardPointsLabelRect), font, text?.Text(HardPointsText),
			ShellTextAlign.Right, ShellChrome.FontInkColor);
		PaintButton(surface, font, ShellWeaponsButton.PreviousHardpoint, "<", ButtonBorder, lit);
		PaintButton(surface, font, ShellWeaponsButton.NextHardpoint, ">", ButtonBorder, lit);
	}

	/// <summary>
	/// The <c>Weapons Inventory</c> list: a flat header with no hatch over a filled body, and the 27 rows.
	/// A row reads the weapon's name and, right-aligned, how many the armory holds — two spaces for
	/// <c>None</c>. A locked weapon's row is all background, a dead one <c>0x25</c>; the lit row's border and
	/// text are <c>0x29</c>.
	/// </summary>
	private void PaintList(ShellSurface surface, HudFont? font, ShellText? text) {
		var list = Inside(PanelRect, ListRect);
		ShellChrome.PaintTitledPanel(surface, list, Border, ListHeaderFace, ShellChrome.InteriorColor, TitleHeight,
			headerChrome: false, 0, 0, fill: true);
		ShellChrome.PaintText(surface, TitleRect(list), font, text?.Text(ListTitleText), ShellTextAlign.Center,
			ShellChrome.FontInkColor);

		for (int row = 0; row < RowWeapons.Length; row++) {
			int weapon = RowWeapons[row];
			bool selected = row == SelectedRow;
			bool unlocked = _hangar.IsWeaponUnlocked(weapon);
			byte color = selected ? LitColor : _rowState[row] switch {
				RowState.Live => RowColor,
				RowState.Dead => DeadRowColor,
				_ => ShellChrome.InteriorColor,
			};
			string count = !unlocked ? LockedCount : weapon == 0 ? NoneCount : $"{_hangar.WeaponsOwned(weapon)}";

			var rect = RowRect(row);
			ShellChrome.PaintPanel(surface, rect, selected ? LitColor : ShellChrome.InteriorColor, fill: true);
			Column(surface, font, rect, 2, NameRight, text?.Text(FirstWeaponNameText + weapon), ShellTextAlign.Left, color);
			Column(surface, font, rect, NameRight, SecondRight, " ", ShellTextAlign.Left, color);
			Column(surface, font, rect, SecondRight, ThirdRight, " ", ShellTextAlign.Center, color);
			Column(surface, font, rect, ThirdRight, rect.Width - 2, count, ShellTextAlign.Right, color);
		}
	}

	/// <summary>
	/// The picture box: black above row <c>0x76</c> and a solid <c>0x25</c> band below it, the builder's
	/// first line and line colour. The selected weapon's picture — or the guidance kind's — sits in the
	/// black with no border, the three description lines are centred in the band, and a missile rack
	/// puts its five buttons up the right-hand side.
	/// </summary>
	private void PaintPictureBox(ShellSurface surface, HudFont? font, ShellText? text, ShellWidget? lit) {
		var box = Inside(PanelRect, PictureBoxRect);
		ShellChrome.PaintHatchedDivider(surface, box, PictureBoxBorder, DescriptionBand, DescriptionFirstLine,
			innerBorder: false);

		var picture = ShowingGuidance ? _art?.Guidance(ShownGuidance)
			: SelectedWeapon > 0 ? _art?.Weapon(SelectedWeapon) : null;
		if (picture is { } shown) {
			var (frame, x, y) = shown;
			var rect = new ShellRect(box.X0 + x, box.Y0 + y, box.X0 + x + frame.Cols, box.Y0 + y + frame.Rows);
			ShellChrome.PaintImagePanel(surface, rect, frame, 0, 0, Border, border: false);
		}

		if (GuidanceButtonsShown) {
			for (int button = 0; button < ButtonGuidance.Length; button++) {
				PaintButton(surface, font, (ShellWeaponsButton)button, text?.Text(FirstGuidanceText + button),
					button == _litGuidanceButton ? LitGuidanceBorder : ButtonBorder, lit);
			}

			PaintButton(surface, font, ShellWeaponsButton.Weapon, text?.Text(FirstWeaponNameText + SelectedWeapon),
				ButtonBorder, lit);
		}

		for (int line = 0; line < DescriptionRows.Length; line++) {
			var (top, bottom) = DescriptionRows[line];
			ShellChrome.PaintText(surface, new ShellRect(box.X0 + 1, box.Y0 + top, box.X1, box.Y0 + bottom), font,
				_description >= 0 ? _art?.Descriptions?.Text(_description + line) : null, ShellTextAlign.Center,
				ShellChrome.FontInkColor, DescriptionBand);
		}
	}

	/// <summary>One button: its double-bordered box in <paramref name="border"/> and its caption in <c>0x29</c>.</summary>
	private static void PaintButton(ShellSurface surface, HudFont? font, ShellWeaponsButton button, string? caption,
			byte border, ShellWidget? lit) =>
		ShellChrome.PaintButton(surface, ButtonRect(button), border, font, caption, ShellChrome.FontInkColor,
			pressed: lit == new ShellWidget(ShellWidgetKind.WeaponsButton, (int)button));

	private static void Column(ShellSurface surface, HudFont? font, ShellRect row, int left, int right,
			string? value, ShellTextAlign align, byte color) =>
		ShellChrome.PaintText(surface,
			new ShellRect(row.X0 + left, row.Y0, row.X0 + right, row.Y0 + row.Height - 1), font, value, align,
			color, ShellChrome.InteriorColor);

	private static ShellRect TitleRect(ShellRect panel) => new(panel.X0, panel.Y0, panel.X1, panel.Y0 + TitleHeight);

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	private const byte Border = 0x27;
	private const byte PanelFace = 0x25;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x66;
	private const int TitlePlateLast = 0x116;

	/// <summary><c>ESTitle_Ctor</c>'s own header face, which the builder leaves on the list.</summary>
	private const byte ListHeaderFace = 0x24;

	/// <summary>The picture box's border, and the row its line fill starts on and the colour of those lines.</summary>
	private const byte PictureBoxBorder = 0x15;
	private const int DescriptionFirstLine = 0x76;
	private const byte DescriptionBand = 0x25;

	private const byte RowColor = 0x27;
	private const byte DeadRowColor = 0x25;
	private const byte LitColor = 0x29;
	private const byte ButtonBorder = 0x22;
	private const byte LitGuidanceBorder = 0x20;

	/// <summary><c>"%d"</c> of the count at <c>00477428</c>; <c>None</c>'s two spaces at <c>00477425</c>; and the builder's <c>"0"</c>, which a locked row keeps.</summary>
	private const string NoneCount = "  ";
	private const string LockedCount = "0";

	/// <summary><c>estext.bin</c> indices the screen prints.</summary>
	private const int TitleText = 0x9f;
	private const int ListTitleText = 0xa0;
	private const int FirstGuidanceText = 0xa1;
	private const int HardPointsText = 0xa5;

	/// <summary>A weapon's name is <c>estext.bin</c> <c>0x7e + id</c>; <c>0x7e</c> itself is <c>None</c>.</summary>
	private const int FirstWeaponNameText = 0x7e;
}
