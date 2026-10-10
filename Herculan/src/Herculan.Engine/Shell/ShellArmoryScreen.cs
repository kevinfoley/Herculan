using HercWorks.Core.Io.Transform.Shell;
using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>The ARMORY screen's two live buttons, in the order the builder constructs them.</summary>
public enum ShellArmoryButton {
	Clear,
	Scrap,
}

/// <summary>
/// What the ARMORY screen reads beside the save and the weapons screen's pictures: each weapon's price
/// out of <c>gam\weapons.dat</c>, its name out of <c>weapons.bin</c> — the pointer <c>LoadWeaponsDat</c>
/// stores at record <c>+0x10</c> — and <c>wpn_info.bin</c>, five lines per armory row. Any of them
/// missing leaves its part of the screen blank.
/// </summary>
public sealed class ShellArmoryCatalog {
	/// <summary>How many catalog ids there are, and so how many ranks the auto-fill walks.</summary>
	private const int CatalogSize = 0x21;

	/// <summary>The rank <c>WeaponsDat_IdAtRank</c> (<c>0041230c</c>) skips: never built automatically.</summary>
	private const int NeverAutoBuilt = 99;

	/// <summary>How many of a weapon the auto-fill keeps in stock: it queues one the armory holds fewer of.</summary>
	private const int AutoFillStock = 2;

	private readonly Dictionary<int, int> _priceTons;
	private readonly Dictionary<int, int> _rank;

	private ShellArmoryCatalog(Dictionary<int, int> priceTons, Dictionary<int, int> rank, ShellText? names,
			ShellText? info) {
		_priceTons = priceTons;
		_rank = rank;
		Names = names;
		Info = info;
	}

	/// <summary><c>weapons.bin</c>, by weapon id.</summary>
	public ShellText? Names { get; }

	/// <summary><c>wpn_info.bin</c>, opened by the builder into <c>DAT_0048db18</c>.</summary>
	public ShellText? Info { get; }

	public static ShellArmoryCatalog Load(GameContent content) {
		var prices = new Dictionary<int, int>();
		var ranks = new Dictionary<int, int>();
		if (content.Read(ShellRepairCosts.CatalogFolder, "WEAPONS.DAT") is { } bytes
				&& new WeaponsDatTransformer().Parse(bytes) is { } catalog) {
			foreach (var entry in catalog.Data) {
				prices[entry.Id] = entry.SalvageCost;
				ranks[entry.Id] = entry.AutobuildPriority;
			}
		}

		return new ShellArmoryCatalog(prices, ranks, ShellText.Load(content, "WEAPONS.BIN"),
			ShellText.Load(content, "WPN_INFO.BIN"));
	}

	/// <summary>
	/// <c>Armory_ScrapValueTons</c> (<c>0041266a</c>) — what scrapping the armory's whole stock of a weapon
	/// yields, in tons: a tenth of the price of every unit held, the price truncated to tons first, and at
	/// least 1 for a stock that is not empty. The weapon scrap dialog quotes it and its ACCEPT pays it.
	/// </summary>
	public int ScrapValueTons(ShellHangar hangar, int weaponId) {
		int owned = hangar.WeaponsOwned(weaponId);
		if (owned == 0) {
			return 0;
		}

		int value = PriceKilograms(weaponId) / ShellRepairCosts.KilogramsPerTon * owned / 10;
		return (short)value < 1 ? 1 : value;
	}

	/// <summary>
	/// <c>Armory_RefreshQueue</c> (<c>00412413</c>), which the weapon scrap runs after paying out: with
	/// weapons built by hand the queue is trimmed to the pool (<see cref="ShellHangar.TrimQueueToBudget"/>),
	/// and built automatically it is refilled from scratch (<see cref="AutoFillQueue"/>).
	/// </summary>
	public void RefreshQueue(ShellHangar hangar, bool manualBuild) {
		if (manualBuild) {
			hangar.TrimQueueToBudget(PriceKilograms);
		} else {
			AutoFillQueue(hangar);
		}
	}

	/// <summary>
	/// <c>Armory_AutoFillQueue</c> (<c>00412341</c>) — the queue emptied, then every rank from 0 walked
	/// while a slot is free, queueing the weapon at that rank when it is unlocked, the armory holds fewer
	/// than two, and the running total plus its price is <i>less than</i> the pool, compared unsigned.
	///
	/// <para>A rank no weapon holds resolves to id <c>-1</c>, and retail then reads the record before the
	/// catalog, whose unlock byte is the high byte of the Razor's <c>herc_inf.dat</c> price — 0 for its
	/// 120 tons — so it is never queued. This skips it outright.</para>
	/// </summary>
	public void AutoFillQueue(ShellHangar hangar) {
		hangar.ResetQueue();
		int total = 0;
		for (int rank = 0; rank < CatalogSize; rank++) {
			if (hangar.QueueFreeSlots == 0) {
				return;
			}

			int weapon = IdAtRank(rank);
			if (weapon == -1 || !hangar.IsWeaponUnlocked(weapon) || hangar.WeaponsOwned(weapon) >= AutoFillStock) {
				continue;
			}

			int price = PriceKilograms(weapon);
			if ((uint)(price + total) < (uint)hangar.SalvageKilograms) {
				total += price;
				hangar.Enqueue(weapon);
			}
		}
	}

	/// <summary><c>WeaponsDat_IdAtRank</c> (<c>0041230c</c>) — the lowest id holding a rank, never one ranked 99; <c>-1</c> when none does.</summary>
	private int IdAtRank(int rank) {
		for (int id = 0; id < CatalogSize; id++) {
			if (_rank.TryGetValue(id, out int held) && held != NeverAutoBuilt && held == rank) {
				return id;
			}
		}

		return -1;
	}

	/// <summary>
	/// Record <c>+0x14</c>, the price in kilograms: the file's tons times 1000, which
	/// <c>WeaponsDat_ReadRecord</c> (<c>00411d57</c>) stores as a <c>uint16</c>.
	/// </summary>
	public int PriceKilograms(int weaponId) =>
		(ushort)(_priceTons.GetValueOrDefault(weaponId) * ShellRepairCosts.KilogramsPerTon);

	/// <summary><c>Armory_QueuedTotal</c> (<c>00412586</c>) — the price of every occupied queue slot, summed.</summary>
	public int QueuedTotal(ShellHangar hangar) => hangar.QueuedWeapons.Where(id => id != 0).Sum(PriceKilograms);
}

/// <summary>
/// Tab 5, <c>ARMORY</c> — the weapon build queue: the <c>Armaments Inventory</c> list of every weapon the
/// armory sells, with how many are queued, how many are held and what each costs; a picture of the
/// selected weapon over five lines of its <c>wpn_info.bin</c> prose; and the queue's workspace and
/// salvage figures. Built once by <c>Armory_BuildScreen</c> (<c>00447e34</c>, <c>warmoryi.cpp</c>),
/// entered by <c>Armory_Enter</c> (<c>004494f7</c>) and hidden by <c>Armory_Leave</c>
/// (<c>004495b6</c>), its rows gated by <c>Armory_RefreshRows</c> (<c>00449329</c>), its row moved by
/// <c>Armory_ClickRow</c> (<c>0044969f</c>) and its figures refilled by <c>Armory_RefreshReadout</c>
/// (<c>00449cab</c>). See docs/retail/shell/build-and-armory.md, "The armory screen".
///
/// <para><b>Every rect here is a literal in the executable</b>, kept parent-relative as the builder
/// writes them: the content panel in the canvas, which it spans edge to edge because this tab has no
/// squad panel; the list, the picture box and the two framed panels in the content panel; everything
/// else in whichever box holds it. A weapon's picture is the exception, placed by its
/// <c>arm_weap.dat</c> record.</para>
///
/// <para>Clicking the lit row again queues a unit with the left button and takes one off with the
/// right when <c>prefs.cfg</c> option 45 says weapons are built by hand, and <c>Clear</c> takes all of
/// them off, all on <see cref="ShellHangar"/>'s queue. <c>Scrap</c> puts up the weapon
/// <see cref="ShellScrapDialog"/>, which sells the lit weapon's whole stock.</para>
/// </summary>
public sealed class ShellArmoryScreen {
	/// <summary>
	/// The 26 rows: the first 26 entries of the table at <c>004769b0</c> — every weapon but <c>None</c>,
	/// in <c>arm_weap.dat</c>'s order, which is also <c>wpn_info.bin</c>'s.
	/// </summary>
	public const int RowCount = 0x1a;

	/// <summary>The content panel, in the canvas, parented to the top-level window.</summary>
	public static readonly ShellRect PanelRect = new(0, 0x2d, 0x27f, 0x1d9);

	/// <summary>The list, the picture box and the two framed panels, in the content panel.</summary>
	private static readonly ShellRect ListRect = new(0xd, 0x1a, 0x117, 0x1a3);
	private static readonly ShellRect PictureBoxRect = new(0x121, 0x1a, 0x272, 0xf3);
	private static readonly ShellRect ReadoutPanelRect = new(0x121, 0xf8, 0x272, 0x14e);
	private static readonly ShellRect ButtonPanelRect = new(0x121, 0x153, 0x272, 0x18c);

	/// <summary>
	/// The column headings, in the list: <c>Num to</c> over <c>build</c>, <c>Type</c>, <c>avail.</c>,
	/// all left-aligned, and <c>Salv.</c> over <c>req.</c> right-aligned to nine inside the list's right
	/// edge.
	/// </summary>
	private static readonly (int Left, int Top, int Right, int Bottom, int Text, ShellTextAlign Align)[] Headings = {
		(10, 0x19, 0x4b, 0x25, 0xd7, ShellTextAlign.Left),
		(10, 0x25, 0x4b, 0x31, 0xd8, ShellTextAlign.Left),
		(0x4c, 0x25, 0xaa, 0x31, 0xd9, ShellTextAlign.Left),
		(0xb4, 0x25, 0xdc, 0x31, 0xda, ShellTextAlign.Left),
		(0xdc, 0x19, -9, 0x25, 0xdb, ShellTextAlign.Right),
		(0xdc, 0x25, -9, 0x31, 0xdc, ShellTextAlign.Right),
	};

	/// <summary>The rows, in the list: 13 tall on a 12-pixel pitch, so each shares a line with the next.</summary>
	private const int RowLeft = 8;
	private const int RowRight = 0xf8;
	private const int FirstRowTop = 0x3c;
	private const int RowPitch = 0xc;
	private const int RowBottomOffset = 0xc;

	/// <summary>
	/// Where <c>ESQuad_AddColumns</c> is told to cut a row: the queued count from <c>2</c> to
	/// <c>0x32</c>, the name to <c>0xb4</c>, both left-aligned; the count held to <c>0xc9</c> and the price
	/// in tons to one inside the row's right edge, both right-aligned.
	/// </summary>
	private const int QueuedRight = 0x32;
	private const int NameRight = 0xb4;
	private const int HeldRight = 0xc9;

	/// <summary>The five lines of <c>wpn_info.bin</c> in the picture box, each from 0 to the box's own right edge.</summary>
	private static readonly (int Top, int Bottom)[] InfoRows = { (0x8a, 0x96), (0x96, 0xa2), (0xa2, 0xae), (0xae, 0xbc), (0xbc, 200) };
	private const int InfoLines = 5;

	/// <summary>In the readout panel: the two workspace labels and their figures, the two salvage labels, and the two boxes they head.</summary>
	private static readonly ShellRect FreeLabelRect = new(0x4a, 9, 0xdc, 0x15);
	private static readonly ShellRect FreeValueRect = new(0xdc, 9, 0xeb, 0x15);
	private static readonly ShellRect InUseLabelRect = new(0x4a, 0x15, 0xdc, 0x21);
	private static readonly ShellRect InUseValueRect = new(0xdc, 0x15, 0xeb, 0x21);
	private static readonly ShellRect SalvageLabelRect = new(0x28, 0x2b, 0xa2, 0x37);
	private static readonly ShellRect AllocatedLabelRect = new(0xa2, 0x2b, 0xfe, 0x37);
	private static readonly ShellRect SalvageBoxRect = new(0x31, 0x3d, 0x93, 0x4e);
	private static readonly ShellRect AllocatedBoxRect = new(0xaf, 0x3d, 0x111, 0x4e);

	/// <summary><c>Clear</c> and <c>Scrap</c>, in the button panel — the same two columns as the boxes above them.</summary>
	private static readonly ShellRect ClearRect = new(0x31, 0x14, 0x93, 0x23);
	private static readonly ShellRect ScrapRect = new(0xaf, 0x14, 0x111, 0x23);

	private readonly ShellArmoryCatalog? _catalog;
	private readonly ShellWeaponsArt? _art;
	private ShellHangar _hangar;

	/// <summary>
	/// Each row's four text colours as the original leaves them, since three functions write them and
	/// none rewrites them all: <see cref="RefreshRows"/> on entry, and a selection on the rows it moves between.
	/// </summary>
	private readonly byte[] _rowColor = new byte[RowCount];

	/// <summary>
	/// Each row's count-held colour, which <c>Armory_RefreshReadout</c> (<c>00449cab</c>) also writes: it
	/// rewrites the lit row's count in <c>0x29</c> on every refresh. It parts from the row's colour only
	/// after a weapon scrap, whose row refresh puts the lit row back to <c>0x27</c> just before.
	/// </summary>
	private readonly byte[] _heldColor = new byte[RowCount];

	/// <summary>Builds the screen and enters it.</summary>
	public ShellArmoryScreen(ShellHangar? hangar = null, bool manualBuild = false, ShellArmoryCatalog? catalog = null,
			ShellWeaponsArt? art = null) {
		_hangar = hangar ?? ShellHangar.From(null);
		_catalog = catalog;
		_art = art;
		Enter(_hangar, manualBuild);
	}

	/// <summary>
	/// <c>Armory_LitRow</c> (<c>00479174</c>), the lit row: <c>-1</c> in the image, and put back to it by
	/// <c>Armory_Leave</c>, so every entry selects afresh.
	/// </summary>
	public int SelectedRow { get; private set; } = -1;

	/// <summary><c>ShellOption_WeaponsBuildMode</c>, <c>prefs.cfg</c> option 45: 1 <c>Manually Build Weapons</c>, 0 <c>AutoBuild Weapons</c>.</summary>
	public bool ManualBuild { get; private set; }

	/// <summary>The weapon id a row lists.</summary>
	public static int WeaponOfRow(int row) => row >= 0 && row < RowCount ? ShellWeaponsScreen.RowWeapons[row] : -1;

	/// <summary>The selected row's weapon id, or <c>-1</c>.</summary>
	public int SelectedWeapon => WeaponOfRow(SelectedRow);

	/// <summary>The salvage the <c>Salvage Available:</c> box quotes: the pool less what the queue has committed.</summary>
	public int AvailableKilograms => _hangar.SalvageKilograms - AllocatedKilograms;

	/// <summary>What the queue has committed, the <c>Allocated:</c> box.</summary>
	public int AllocatedKilograms => _catalog?.QueuedTotal(_hangar) ?? 0;

	/// <summary>
	/// <c>Armory_Enter</c> (<c>004494f7</c>): gates <c>Clear</c> on the build mode, refreshes the rows and
	/// the figures, and selects row 0. The previous visit's <c>Armory_Leave</c> put the selection back to
	/// <c>-1</c>, so that select always goes through.
	/// </summary>
	public void Enter(ShellHangar hangar, bool manualBuild) {
		_hangar = hangar;
		ManualBuild = manualBuild;
		SelectedRow = -1;
		RefreshRows();
		ClickRow(0);
	}

	/// <summary>
	/// <c>Armory_RefreshRows</c> (<c>00449329</c>): a weapon still locked has its row disabled and its
	/// four columns set to <c>0x10</c>, the background, so the list shows a gap; every other row is live
	/// in <c>0x27</c>.
	/// </summary>
	private void RefreshRows() {
		for (int row = 0; row < RowCount; row++) {
			_rowColor[row] = _hangar.IsWeaponUnlocked(WeaponOfRow(row)) ? RowColor : ShellChrome.InteriorColor;
			_heldColor[row] = _rowColor[row];
		}
	}

	/// <summary>
	/// <c>Armory_ClickRow</c> (<c>0044969f</c>), a row's left release. On a row that is not the lit one,
	/// or on any row while weapons are built automatically, it selects (<see cref="SelectRow"/>). On the
	/// lit row with weapons built by hand it queues one unit, while a queue slot is free and what the
	/// queue has committed plus the price is <i>less than</i> the pool, compared unsigned. Returns whether
	/// anything changed.
	/// </summary>
	public bool ClickRow(int row) {
		if (row < 0 || row >= RowCount) {
			return false;
		}

		if (!ManualBuild || row != SelectedRow) {
			return SelectRow(row);
		}

		int weapon = WeaponOfRow(row);
		int price = _catalog?.PriceKilograms(weapon) ?? 0;
		if (_hangar.QueueFreeSlots == 0 || (uint)(AllocatedKilograms + price) >= (uint)_hangar.SalvageKilograms) {
			return false;
		}

		_hangar.Enqueue(weapon);
		return true;
	}

	/// <summary>
	/// <c>Armory_RightClickRow</c> (<c>004499de</c>), a row's right release. It selects as the left does;
	/// on the lit row with weapons built by hand it takes one unit off by taking every unit off
	/// (<see cref="Clear"/>) and queueing one fewer back through <see cref="ClickRow"/>, each of which tests
	/// the slot and the pool again. The units put back take the first empty slots.
	/// </summary>
	public bool RightClickRow(int row) {
		if (row < 0 || row >= RowCount) {
			return false;
		}

		if (!ManualBuild || row != SelectedRow) {
			return SelectRow(row);
		}

		int keep = _hangar.QueuedCount(WeaponOfRow(row)) - 1;
		Clear();
		for (int unit = 0; unit < keep; unit++) {
			ClickRow(row);
		}

		return true;
	}

	/// <summary>
	/// <c>Clear</c>'s handler (<c>00449ef4</c>), <c>Armory_DequeueLit</c> (<c>0044963c</c>): with a row lit,
	/// every queued unit of its weapon comes off.
	/// </summary>
	public void Clear() {
		if (SelectedRow != -1) {
			_hangar.Dequeue(SelectedWeapon);
		}
	}

	/// <summary>
	/// The end of the weapon scrap's ACCEPT (<c>WeaponScrapDialog_OnAccept</c>, <c>00447db7</c>):
	/// <c>Armory_RefreshRows</c>, which puts every live row back to <c>0x27</c>, the lit one included, and
	/// <c>Armory_RefreshReadout</c>, which rewrites the lit row's count held in <c>0x29</c>. The row stays
	/// the lit one.
	/// </summary>
	public void RefreshAfterScrap() {
		RefreshRows();
		if (SelectedRow != -1) {
			_heldColor[SelectedRow] = LitColor;
		}
	}

	/// <summary>
	/// The selecting half of both handlers: the old row goes back to <c>0x27</c> and its picture is
	/// hidden, the new one is lit <c>0x29</c> with its picture and five lines of <c>wpn_info.bin</c> from
	/// <c>row * 5</c>. Returns whether the selection moved; the original runs it again on the lit row
	/// while weapons are built automatically, which changes nothing.
	/// </summary>
	private bool SelectRow(int row) {
		if (row == SelectedRow) {
			return false;
		}

		if (SelectedRow != -1) {
			_rowColor[SelectedRow] = RowColor;
			_heldColor[SelectedRow] = RowColor;
		}

		_rowColor[row] = LitColor;
		_heldColor[row] = LitColor;
		SelectedRow = row;
		return true;
	}

	/// <summary>
	/// <c>Clear</c> is live only with weapons built by hand: <c>Armory_Enter</c> writes the greying trio at it
	/// from the build mode on every entry. <c>Scrap</c> is never gated.
	/// </summary>
	public bool IsEnabled(ShellArmoryButton button) => button != ShellArmoryButton.Clear || ManualBuild;

	/// <summary>
	/// Whether a row takes clicks. <c>ESQuad_AddColumns</c> builds every row disabled and
	/// <c>Armory_RefreshRows</c> enables the unlocked ones; selecting never touches it.
	/// </summary>
	public bool IsRowEnabled(int row) => _hangar.IsWeaponUnlocked(WeaponOfRow(row));

	/// <summary>One row's rect, in the canvas.</summary>
	public static ShellRect RowRect(int row) {
		var list = Inside(PanelRect, ListRect);
		int top = list.Y0 + row * RowPitch + FirstRowTop;
		return new ShellRect(list.X0 + RowLeft, top, list.X0 + RowRight, top + RowBottomOffset);
	}

	/// <summary>One button's rect, in the canvas.</summary>
	public static ShellRect ButtonRect(ShellArmoryButton button) =>
		Inside(Inside(PanelRect, ButtonPanelRect), button == ShellArmoryButton.Clear ? ClearRect : ScrapRect);

	/// <summary>
	/// What the pointer hits: a live button, or a live row and which of its text columns. Rows overlap
	/// on their shared line and the later-built, lower row answers there. A locked row, the two figure
	/// boxes and the picture box are disabled and swallow a click, which here is the same as hitting
	/// nothing.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		foreach (var button in Enum.GetValues<ShellArmoryButton>()) {
			if (IsEnabled(button) && ButtonRect(button).Contains(canvasX, canvasY)) {
				return ShellHit.Button(new ShellWidget(ShellWidgetKind.ArmoryButton, (int)button), ButtonRect(button),
					canvasX, canvasY);
			}
		}

		for (int row = RowCount - 1; row >= 0; row--) {
			var rect = RowRect(row);
			if (rect.Contains(canvasX, canvasY)) {
				return IsRowEnabled(row)
					? ShellHit.ListRow(new ShellWidget(ShellWidgetKind.ArmoryRow, row), rect, QueuedRight, NameRight,
						HeldRight, canvasX, canvasY)
					: null;
			}
		}

		return null;
	}

	/// <summary>
	/// Draws the whole screen into <paramref name="surface"/>. The caller clears it first. <paramref name="lit"/>
	/// is the widget a press has lit.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellChrome.PaintTitledPanel(surface, PanelRect, Border, PanelFace, ShellChrome.InteriorColor, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: true);
		ShellChrome.PaintText(surface, TitleRect(PanelRect), font, text?.Text(TitleText), ShellTextAlign.Center,
			ShellChrome.FontInkColor);

		PaintList(surface, font, text);
		PaintPictureBox(surface, font);
		PaintReadout(surface, font, text);
		PaintButtons(surface, font, text, lit);
	}

	/// <summary>
	/// The <c>Armaments Inventory</c> list: a flat header with no hatch over a filled body, six column
	/// headings, and the 26 rows. A row draws no border — the builder clears its <c>+0x51</c> — so the
	/// lit row shows only in its text. Its first column reads <c>"[ %1d ]"</c> of the weapon's queued
	/// count, or <c>"[   ]"</c> for none; its third the count held; its fourth the price in tons, written
	/// once by the builder. A locked row keeps the builder's texts, drawn in the background.
	/// </summary>
	private void PaintList(ShellSurface surface, HudFont? font, ShellText? text) {
		var list = Inside(PanelRect, ListRect);
		ShellChrome.PaintTitledPanel(surface, list, Border, ListHeaderFace, ShellChrome.InteriorColor, TitleHeight,
			headerChrome: false, 0, 0, fill: true);
		ShellChrome.PaintText(surface, TitleRect(list), font, text?.Text(ListTitleText), ShellTextAlign.Center,
			ShellChrome.FontInkColor);

		foreach (var (left, top, right, bottom, index, align) in Headings) {
			var rect = new ShellRect(list.X0 + left, list.Y0 + top, right < 0 ? list.X1 + right : list.X0 + right,
				list.Y0 + bottom);
			ShellChrome.PaintText(surface, rect, font, text?.Text(index), align, ShellChrome.FontInkColor);
		}

		for (int row = 0; row < RowCount; row++) {
			int weapon = WeaponOfRow(row);
			bool unlocked = _hangar.IsWeaponUnlocked(weapon);
			int queued = _hangar.QueuedCount(weapon);
			byte color = _rowColor[row];

			// The row's own paint (0040a600): its interior cleared to 0x10, then the four columns.
			var rect = RowRect(row);
			surface.Fill(rect.X0 + 1, rect.Y0 + 1, rect.X1 - 1, rect.Y1 - 1, ShellChrome.InteriorColor);
			Column(surface, font, rect, 2, QueuedRight, unlocked && queued > 0 ? $"[ {queued} ]" : NoneQueued,
				ShellTextAlign.Left, color);
			Column(surface, font, rect, QueuedRight, NameRight, _catalog?.Names?.Text(weapon), ShellTextAlign.Left, color);
			Column(surface, font, rect, NameRight, HeldRight, unlocked ? $"{_hangar.WeaponsOwned(weapon)}" : null,
				ShellTextAlign.Right, _heldColor[row]);
			Column(surface, font, rect, HeldRight, rect.Width - 2,
				_catalog != null ? $"{_catalog.PriceKilograms(weapon) / ShellRepairCosts.KilogramsPerTon}" : null,
				ShellTextAlign.Right, color);
		}
	}

	/// <summary>
	/// The picture box: black above row <c>0x77</c> and a solid <c>0x0f</c> band below it. The selected
	/// weapon's picture sits in the black with no border, and five lines of <c>wpn_info.bin</c> are
	/// centred in the band, each clearing its own rect to the band's colour.
	/// </summary>
	private void PaintPictureBox(ShellSurface surface, HudFont? font) {
		var box = Inside(PanelRect, PictureBoxRect);
		ShellChrome.PaintHatchedDivider(surface, box, PictureBoxBorder, InfoBand, InfoFirstLine, innerBorder: false);

		if (SelectedWeapon > 0 && _art?.Weapon(SelectedWeapon) is { } picture) {
			var (frame, x, y) = picture;
			var rect = new ShellRect(box.X0 + x, box.Y0 + y, box.X0 + x + frame.Cols, box.Y0 + y + frame.Rows);
			ShellChrome.PaintImagePanel(surface, rect, frame, 0, 0, Border, border: false);
		}

		for (int line = 0; line < InfoRows.Length; line++) {
			var (top, bottom) = InfoRows[line];
			ShellChrome.PaintText(surface, new ShellRect(box.X0, box.Y0 + top, box.X1, box.Y0 + bottom), font,
				SelectedRow >= 0 ? _catalog?.Info?.Text(SelectedRow * InfoLines + line) : null, ShellTextAlign.Center,
				ShellChrome.FontInkColor, InfoBand);
		}
	}

	/// <summary>
	/// The readout panel, as <c>Armory_RefreshReadout</c> (<c>00449cab</c>) fills it: the queue's free and
	/// used slots, centred beside their labels, and two disabled buttons holding <c>"%ld kg"</c> of the
	/// pool net of the queue and <c>"%d kg"</c> of what the queue has committed. Their captions are the only
	/// ones on the screen whose <c>+0xc1</c> the builder sets, so they clear behind themselves.
	/// </summary>
	private void PaintReadout(ShellSurface surface, HudFont? font, ShellText? text) {
		var panel = Inside(PanelRect, ReadoutPanelRect);
		ShellChrome.PaintFramedPanel(surface, panel, FramedBorder, FramedFace, fill: true);

		ShellChrome.PaintText(surface, Inside(panel, FreeLabelRect), font, text?.Text(FreeText), ShellTextAlign.Left,
			ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(panel, FreeValueRect), font, $"{_hangar.QueueFreeSlots}",
			ShellTextAlign.Center, ShellChrome.FontInkColor, ShellChrome.InteriorColor);
		ShellChrome.PaintText(surface, Inside(panel, InUseLabelRect), font, text?.Text(InUseText), ShellTextAlign.Left,
			ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(panel, InUseValueRect), font,
			$"{ShellHangar.QueueSlots - _hangar.QueueFreeSlots}", ShellTextAlign.Center, ShellChrome.FontInkColor,
			ShellChrome.InteriorColor);

		ShellChrome.PaintText(surface, Inside(panel, SalvageLabelRect), font, text?.Text(SalvageText), ShellTextAlign.Left,
			ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(panel, AllocatedLabelRect), font, text?.Text(AllocatedText),
			ShellTextAlign.Right, ShellChrome.FontInkColor);

		string? kg = text?.Text(KilogramsText);
		ShellChrome.PaintButton(surface, Inside(panel, SalvageBoxRect), ReadoutBorder, font, $"{AvailableKilograms} {kg}",
			ShellChrome.FontInkColor, backingColor: ShellChrome.InteriorColor);
		ShellChrome.PaintButton(surface, Inside(panel, AllocatedBoxRect), ReadoutBorder, font, $"{AllocatedKilograms} {kg}",
			ShellChrome.FontInkColor, backingColor: ShellChrome.InteriorColor);
	}

	/// <summary><c>Clear</c> and <c>Scrap</c> in their own framed panel, <c>Clear</c> greyed <c>0x26</c> while weapons are built automatically.</summary>
	private void PaintButtons(ShellSurface surface, HudFont? font, ShellText? text, ShellWidget? lit) {
		ShellChrome.PaintFramedPanel(surface, Inside(PanelRect, ButtonPanelRect), FramedBorder, FramedFace, fill: true);

		foreach (var button in Enum.GetValues<ShellArmoryButton>()) {
			bool enabled = IsEnabled(button);
			ShellChrome.PaintButton(surface, ButtonRect(button), enabled ? ButtonBorder : DisabledColor, font,
				text?.Text(button == ShellArmoryButton.Clear ? ClearText : ScrapText),
				enabled ? ShellChrome.FontInkColor : DisabledColor,
				pressed: enabled && lit == new ShellWidget(ShellWidgetKind.ArmoryButton, (int)button));
		}
	}

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
	private const int TitlePlateFirst = 0xf1;
	private const int TitlePlateLast = 0x18c;

	/// <summary>The list's header face, which the builder writes over the class's.</summary>
	private const byte ListHeaderFace = 0x24;

	/// <summary>The picture box's border, and the row its line fill starts on and the colour of those lines.</summary>
	private const byte PictureBoxBorder = 0x15;
	private const int InfoFirstLine = 0x77;
	private const byte InfoBand = 0x0f;

	/// <summary>The two framed panels: <c>ESRegionFill_Ctor</c>'s border argument, and the checkerboard the builder writes over its <c>0x25</c>.</summary>
	private const byte FramedBorder = 0x15;
	private const byte FramedFace = 0x0f;

	private const byte RowColor = 0x27;
	private const byte LitColor = 0x29;
	private const byte ButtonBorder = 0x22;
	private const byte ReadoutBorder = 0x13;
	private const byte DisabledColor = 0x26;

	/// <summary>The first column's text for a weapon with nothing queued, at <c>0047916b</c>.</summary>
	private const string NoneQueued = "[   ]";

	/// <summary><c>estext.bin</c> indices the screen prints.</summary>
	private const int TitleText = 0xcf;
	private const int ListTitleText = 0xd0;
	private const int ClearText = 0xd1;
	private const int ScrapText = 0xd2;
	private const int FreeText = 0xd3;
	private const int InUseText = 0xd4;
	private const int SalvageText = 0xd5;
	private const int AllocatedText = 0xd6;
	private const int KilogramsText = 0xc8;
}
