using Herculan.Engine.Content;

namespace Herculan.Engine.Shell;

/// <summary>
/// The four buttons on the repair screen, in the order its builder constructs them.
/// </summary>
public enum ShellRepairButton {
	/// <summary>Repairs the selected component one level. Gated on affording it.</summary>
	Repair = 0,

	/// <summary>Rebuilds the whole machine to 100. Gated on affording that.</summary>
	RepairAll = 1,

	/// <summary>Scraps the machine for salvage. Dead while exactly one machine in the hangar can deploy, whichever it is.</summary>
	Scrap = 2,

	/// <summary>Puts the pool and the machine back as they stood when the bay was selected. It does not leave the screen.</summary>
	Cancel = 3,
}

/// <summary>
/// Tab 3, <c>REPAIR</c> — the two damage lists, the three readout panels and the machine's own
/// condition. Its widgets are built by <c>Repair_BuildScreen</c> (<c>00432037</c>), the screen is entered by
/// <c>Repair_Enter</c> (<c>004332ec</c>) and torn down by <c>Repair_Leave</c> (<c>004333eb</c>), its rows are filled by
/// <c>Repair_FillRow</c> (<c>004339b2</c>), its component names set by <c>Repair_SetComponentNames</c> (<c>00433cdf</c>), its selection moved by
/// <c>Repair_SelectHotspot</c> (<c>00433eb9</c>) and its detail panels refilled by <c>Repair_RefreshDetail</c> (<c>00433445</c>).
///
/// <para><b>Every rect here is a literal in the executable</b>, four immediates on the builder's own
/// stack, and they are kept parent-relative exactly as it writes them — the content panel in the
/// canvas, the two list panels and the three readout panels in that, and each list's rows in its own
/// panel. See docs/retail/shell/weapons-and-repair.md.</para>
///
/// <para><b>A selection is a <c>(column, row)</c> pair and it resolves into a category.</b> Column 0's
/// sixteen rows are the six external component groups and then ten hardpoints; column 1's nine are
/// the internals. <c>Repair_HotspotCategory</c> (<c>00433410</c>) turns the pair into one of <see cref="ShellRepairCategory"/>'s
/// three and <c>Repair_HotspotIndex</c> (<c>00433431</c>) into an index within it, and those three are the status block's own
/// accessor modes — so one number says which list a row is in, which array holds its condition and
/// which table prices it.</para>
///
/// <para><b>The left of the canvas is mostly not this screen's.</b> The builder puts eight grid
/// widgets there, one per bay, holding the machine's internals diagram; the exploded external picture
/// over the same rect, the pilot readout and the roster below it belong to the squad panel the
/// WEAPONS, BUILD and CREW tabs share. <see cref="ShellRepairDiagrams"/> and
/// <see cref="ShellSquadPanel"/> draw them, and this screen decides which picture is up and which bay
/// the roster has selected.</para>
/// </summary>
public sealed class ShellRepairScreen {
	/// <summary>The six external component groups, column 0's first rows.</summary>
	public const int ExternalRowCount = 6;

	/// <summary>The hardpoint rows that follow them, one per mount slot the widest chassis can carry.</summary>
	public const int HardpointRowCount = 10;

	/// <summary>Column 0's rows in total, and the length of the builder's first handler table.</summary>
	public const int ColumnZeroRowCount = ExternalRowCount + HardpointRowCount;

	/// <summary>The nine internal components, column 1's rows.</summary>
	public const int InternalRowCount = 9;

	/// <summary>
	/// The content panel, in the canvas: <c>{0xf1, 0x2b, 0x278, 0x1d9}</c> — the right two thirds,
	/// leaving the left for the machine's picture and the squad roster.
	/// </summary>
	public static readonly ShellRect PanelRect = new(0xf1, 0x2b, 0x278, 0x1d9);

	private static readonly ShellRect ExternalPanelRect = new(4, 0x1a, 0xe5, 0x105);
	private static readonly ShellRect InternalPanelRect = new(4, 0x10b, 0xe5, 0x1a3);

	private static readonly ShellRect ModeLabelRect = new(0xef, 0x1a, 0x179, 0x26);
	private static readonly ShellRect ModeReadoutRect = new(0xff, 0x2b, 0x168, 0x3e);
	private static readonly ShellRect SalvageLabelRect = new(0xef, 0x48, 0x179, 0x54);
	private static readonly ShellRect SalvageReadoutRect = new(0xff, 0x59, 0x168, 0x6b);

	private static readonly ShellRect ItemPanelRect = new(0xef, 0x77, 0x179, 0xf4);
	private static readonly ShellRect TotalPanelRect = new(0xef, 0xfb, 0x179, 0x14f);
	private static readonly ShellRect ScrapPanelRect = new(0xef, 0x156, 0x179, 0x181);
	private static readonly ShellRect CancelRect = new(0x112, 0x193, 0x156, 0x1a2);

	/// <summary>
	/// The readout rects inside a framed panel. The item panel and the total panel share the first
	/// two, which is why the builder writes the same four immediates twice.
	/// </summary>
	private static readonly ShellRect PanelTitleRect = new(0, 6, 0, 0x12);
	private static readonly ShellRect PanelCostLabelRect = new(0, 0x12, 0, 0x1e);
	private static readonly ShellRect PanelCostReadoutRect = new(0xf, 0x24, 0x78, 0x36);
	private static readonly ShellRect ItemConditionLabelRect = new(0, 0x3a, 0, 0x47);
	private static readonly ShellRect ItemConditionReadoutRect = new(0xf, 0x4b, 0x78, 0x5d);
	private static readonly ShellRect ItemRepairButtonRect = new(0xf, 0x68, 0x78, 0x77);
	private static readonly ShellRect TotalRepairButtonRect = new(0xf, 0x3f, 0x78, 0x4e);
	private static readonly ShellRect ScrapButtonRect = new(0xf, 0x17, 0x78, 0x26);

	private readonly ShellHangar _hangar;
	private readonly ShellRepairCosts? _costs;
	private readonly ShellRepairDiagrams? _diagrams;
	private int _column;
	private int _row;

	public ShellRepairScreen(ShellHangar? hangar = null, ShellRepairCosts? costs = null, int bay = 0,
			ShellRepairDiagrams? diagrams = null) {
		_hangar = hangar ?? ShellHangar.From(null);
		_costs = costs;
		_diagrams = diagrams;

		// Repair_Enter (004332ec): entering the screen on a bay that holds nothing, or holds something still under
		// construction, moves the selection to the first bay that holds a finished machine.
		SelectedBay = _hangar.Bay(bay) is { IsBuilt: true } ? bay : _hangar.FirstBuiltBay();
	}

	/// <summary>
	/// Which hangar bay the screen is working on — <c>SelectedBaySlot</c> (<c>00482ae5</c>), <c>-1</c> for none. It is
	/// shared with the arming, build and crew tabs rather than owned by this one.
	/// </summary>
	public int SelectedBay { get; private set; }

	/// <summary>Which list the selection is in: 0 the picture-side list, 1 the internals.</summary>
	public int SelectedColumn => _column;

	/// <summary>Which row of that list, counting from zero.</summary>
	public int SelectedRow => _row;

	/// <summary>The machine the screen is showing, or null when the bay is empty.</summary>
	public ShellBayMachine? Machine => _hangar.Bay(SelectedBay);

	/// <summary>
	/// What the armory's build queue has already committed out of the salvage pool. The repair screen
	/// quotes the pool <i>net</i> of it (<c>CareerSalvage - Armory_QueuedTotal()</c>) and gates both
	/// repair buttons on that net figure, so a queued weapon is salvage the repair bay cannot see. The
	/// host sets it from <see cref="ShellHangar"/>'s queue on every tab entry (<see cref="ShellArmoryCatalog.QueuedTotal"/>).
	/// </summary>
	public int QueuedKilograms { get; set; }

	/// <summary>The salvage the screen quotes and gates on: the pool less what the build queue holds.</summary>
	public int AvailableKilograms => _hangar.SalvageKilograms - QueuedKilograms;

	/// <summary>
	/// <c>ShellOption_RepairMode</c> (<c>004824e4</c>), <c>prefs.cfg</c> option 44: 0 repairs every machine
	/// automatically at debrief, 1 and 2 leave the player's own machine to this screen
	/// (docs/retail/simulation/preferences.md). Only the preferences screen changes it.
	/// </summary>
	public int RepairMode { get; set; } = AutoRepairMode;

	/// <summary>The mode that repairs every machine at debrief.</summary>
	public const int AutoRepairMode = 0;

	/// <summary>The mode that leaves the player's machine to be repaired by hand.</summary>
	public const int ManualRepairMode = 1;

	/// <summary>
	/// The word the mode readout holds. The builder writes <c>Manual Repair</c> and <c>Repair_Enter</c>
	/// (<c>004332ec</c>) rewrites it for modes 0 and 1 only, so mode 2 keeps whatever was there last.
	/// </summary>
	private int _modeText = ManualRepairText;

	/// <summary>
	/// <c>Repair_Snapshot</c> (<c>004338f6</c>)'s copies, <c>RepairSnapshotSalvage</c> (<c>0048d25c</c>) and the 66 bytes at
	/// <c>RepairSnapshotStatus</c> (<c>0048d260</c>): the pool and the selected machine's status block as they stood when the screen
	/// was entered or the bay last changed. CANCEL puts both back.
	/// </summary>
	private int _snapshotSalvage;
	private ShellMachineStatus? _snapshotStatus;

	/// <summary>
	/// <c>Repair_HotspotCategory</c> (<c>00433410</c>) — which of the three condition arrays a <c>(column, row)</c> pair addresses.
	/// </summary>
	public static ShellRepairCategory CategoryOf(int column, int row) =>
		column != 0 ? ShellRepairCategory.Internal
			: row >= ExternalRowCount ? ShellRepairCategory.Hardpoint : ShellRepairCategory.ExternalGroup;

	/// <summary>
	/// <c>Repair_HotspotIndex</c> (<c>00433431</c>) — the row's index within its own category. Only the hardpoint rows are
	/// offset, by the six group rows above them.
	/// </summary>
	public static int IndexOf(ShellRepairCategory category, int row) =>
		category == ShellRepairCategory.Hardpoint ? row - ExternalRowCount : row;

	/// <summary>The category the current selection addresses.</summary>
	public ShellRepairCategory SelectedCategory => CategoryOf(_column, _row);

	/// <summary>The index within that category.</summary>
	public int SelectedIndex => IndexOf(SelectedCategory, _row);

	/// <summary>
	/// <c>Repair_SelectHotspot</c> (<c>00433eb9</c>) — moves the selection, and reports whether it moved. A click on the row
	/// already selected is a no-op, and so is a hardpoint row that is past the machine's mount capacity
	/// or holds no weapon: <b>an unfitted hardpoint cannot be selected on this screen at all</b>, and
	/// the refusal is silent — nothing repaints and the detail panel keeps the last selection.
	/// </summary>
	public bool Select(int column, int row) {
		if (column == _column && row == _row) {
			return false;
		}

		var category = CategoryOf(column, row);
		if (category == ShellRepairCategory.Hardpoint) {
			int slot = IndexOf(category, row);
			var machine = Machine;
			if (machine == null || slot >= machine.MountCapacity || machine.WeaponAt(slot) == 0) {
				return false;
			}
		}

		_column = column;
		_row = row;
		return true;
	}

	/// <summary>
	/// <c>Repair_Enter</c> (<c>004332ec</c>), run on every entry: the mode readout is rewritten, a bay
	/// that holds nothing or holds something still under construction gives way to the first bay holding
	/// a finished machine, the snapshot CANCEL restores is taken and the selection goes back to
	/// <c>(0, 0)</c>.
	///
	/// <para>The original selects that bay through <c>Squad_SelectBay</c> under the previous tab's rule,
	/// as <c>Arming_Enter</c> does (docs/retail/shell/weapons-and-repair.md#entering-the-weapons-screen); this engine
	/// applies the repair tab's.</para>
	/// </summary>
	public void Enter() {
		if (RepairMode == AutoRepairMode) {
			_modeText = AutoRepairText;
		} else if (RepairMode == ManualRepairMode) {
			_modeText = ManualRepairText;
		}

		if (Machine is not { IsBuilt: true }) {
			SelectBay(_hangar.FirstBuiltBay());
		}

		TakeSnapshot();
		_column = 0;
		_row = 0;
	}

	/// <summary>
	/// REPAIR, <c>Repair_OnRepair</c> (<c>00434b2d</c>): the selected component's one-level cost comes
	/// off the pool and the component is set to <see cref="ShellRepairCosts.TargetForLevel"/> of its
	/// level. The cost is taken off the whole pool, not the net figure the button is gated on. Returns
	/// the cost.
	/// </summary>
	public int Repair() {
		if (Machine is not { } machine) {
			return 0;
		}

		int cost = SelectionCost;
		_hangar.SalvageKilograms -= cost;
		machine.SetCondition(SelectedCategory, SelectedIndex,
			ShellRepairCosts.TargetForLevel(ShellRepairCosts.LevelForCondition(SelectionCondition)));
		return cost;
	}

	/// <summary>
	/// REPAIR ALL, <c>Repair_OnRepairAll</c> (<c>00434c59</c>): <see cref="MachineCost"/> comes off the
	/// pool and <c>Repair_Apply(herc, 100)</c> (<c>004113af</c>) sets the whole machine to 100. Returns
	/// the cost.
	/// </summary>
	public int RepairAll() {
		if (Machine is not { } machine) {
			return 0;
		}

		int cost = MachineCost;
		_hangar.SalvageKilograms -= cost;
		machine.ApplyRepair(ShellRepairCosts.RepairTarget[0]);
		return cost;
	}

	/// <summary>
	/// CANCEL, <c>Repair_OnCancel</c> (<c>00434d73</c>): the pool goes back to the snapshot outright, so
	/// anything else that moved it since is undone too, and the snapshot's status block is copied over
	/// the selected machine. With no machine selected the original copies it over whatever
	/// <c>00482abf</c> points at; this engine restores only the pool (docs/retail/shell/weapons-and-repair.md#open).
	/// </summary>
	public void Cancel() {
		_hangar.SalvageKilograms = _snapshotSalvage;
		if (Machine is { } machine && _snapshotStatus != null) {
			machine.RestoreStatus(_snapshotStatus);
		}
	}

	/// <summary>
	/// <c>Repair_Snapshot</c> (<c>004338f6</c>). With no bay selected the pool is copied and the status
	/// copy keeps the last bay's.
	/// </summary>
	private void TakeSnapshot() {
		_snapshotSalvage = _hangar.SalvageKilograms;
		if (SelectedBay != -1 && Machine is { } machine) {
			_snapshotStatus = machine.CaptureStatus();
		}
	}

	/// <summary>
	/// Moves to another hangar bay, as <c>Squad_SelectBay</c> (<c>0043d64d</c>) does for the repair tab, and reports whether
	/// it moved. A bay that is empty or still being built refuses, silently.
	/// </summary>
	public bool SelectBay(int bay) {
		if (bay == SelectedBay || bay < -1 || bay >= ShellHangar.BayCount
			|| (bay != -1 && !ShellSquadPanel.CanSelect(_hangar, bay))) {
			return false;
		}

		SelectedBay = bay;

		// The original resets the selection to (0, 0) on a bay change, before refilling the rows and the
		// detail panel, so a hardpoint row selected on the last machine cannot outlive it. The snapshot
		// CANCEL restores is retaken last, so CANCEL only ever undoes work on the bay now selected.
		_column = 0;
		_row = 0;
		TakeSnapshot();
		return true;
	}

	/// <summary>
	/// The <c>(column, row)</c> a canvas point selects, or null: a row of either list, or a hotspot over
	/// the external picture, which is only there to click while that picture is the one up. Rows overlap
	/// by their border line, which goes to the lower row because it was built later
	/// (docs/retail/shell/widgets.md#which-widget-a-click-reaches).
	/// </summary>
	public (int Column, int Row)? RowAt(float canvasX, float canvasY) {
		if (_column == 0 && _diagrams?.HotspotAt(Machine, canvasX, canvasY) is { } hotspot) {
			return (0, hotspot);
		}

		for (int row = ColumnZeroRowCount - 1; row >= 0; row--) {
			if (RowRect(0, row).Contains(canvasX, canvasY)) {
				return (0, row);
			}
		}

		for (int row = InternalRowCount - 1; row >= 0; row--) {
			if (RowRect(1, row).Contains(canvasX, canvasY)) {
				return (1, row);
			}
		}

		return null;
	}

	/// <summary>
	/// What the pointer hits: a hotspot, a list row and which of its text columns, or a live button. A
	/// hotspot and the list row with the same <c>(column, row)</c> share a handler and are still two
	/// widgets. A gated button swallows a click, which is the same as hitting nothing here.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		if (_column == 0 && _diagrams?.HotspotAt(Machine, canvasX, canvasY) is { } hotspot) {
			return new ShellHit(new ShellWidget(ShellWidgetKind.RepairHotspot, 0, hotspot), ShellHandler.Control);
		}

		if (RowAt(canvasX, canvasY) is { } cell) {
			return ShellHit.ListRow(new ShellWidget(ShellWidgetKind.RepairRow, cell.Column, cell.Row),
				RowRect(cell.Column, cell.Row), NameColumnRight, MountColumnLeft, MountColumnRight, canvasX, canvasY);
		}

		return ButtonAt(canvasX, canvasY) is { } button
			? ShellHit.Button(new ShellWidget(ShellWidgetKind.RepairButton, (int)button), ButtonRect(button),
				canvasX, canvasY)
			: null;
	}

	/// <summary>The button at a canvas point, or null. A gated button does not answer.</summary>
	public ShellRepairButton? ButtonAt(float canvasX, float canvasY) {
		foreach (var button in Enum.GetValues<ShellRepairButton>()) {
			if (IsEnabled(button) && ButtonRect(button).Contains(canvasX, canvasY)) {
				return button;
			}
		}

		return null;
	}

	/// <summary>
	/// Whether a button answers a click, which is the same test that greys its caption — the trio
	/// <c>Repair_RefreshDetail</c> (<c>00433445</c>) writes at each one: the border colour, the caption colour and the enable
	/// flag together.
	///
	/// <para>SCRAP also needs the machine's chassis to be available — <c>(&amp;DAT_00483b62)[type * 8]</c>,
	/// the flag <see cref="ShellHangar.IsChassisAvailable"/> reads.</para>
	/// </summary>
	public bool IsEnabled(ShellRepairButton button) => button switch {
		// Both repair gates compare as unsigned in the original, which only differs from this when the
		// build queue has committed more than the pool holds.
		ShellRepairButton.Repair => Machine != null && AvailableKilograms >= SelectionCost,
		ShellRepairButton.RepairAll => Machine != null && AvailableKilograms >= MachineCost,
		ShellRepairButton.Scrap => Machine is { } machine && !_hangar.HasSingleDeployable()
			&& _hangar.IsChassisAvailable(machine.ChassisType),
		_ => true,
	};

	/// <summary>What repairing the selected component one level costs, in kilograms.</summary>
	public int SelectionCost => _costs?.SelectionCost(Machine, SelectedCategory, SelectedIndex) ?? 0;

	/// <summary>What rebuilding the whole machine to 100 costs, in kilograms.</summary>
	public int MachineCost => _costs?.HercCost(Machine) ?? 0;

	/// <summary>The condition of whatever is selected — 100 when there is no machine, as the original reads it.</summary>
	public int SelectionCondition => Machine?.Condition(SelectedCategory, SelectedIndex) ?? 100;

	/// <summary>One list row's rect, in the canvas.</summary>
	public ShellRect RowRect(int column, int row) {
		if (column != 0) {
			return RowIn(Inside(PanelRect, InternalPanelRect), row * RowPitch + FirstRowY);
		}

		return row < ExternalRowCount
			? RowIn(Inside(PanelRect, ExternalPanelRect), row * RowPitch + FirstRowY)
			: RowIn(Inside(PanelRect, ExternalPanelRect),
				(row - ExternalRowCount) * RowPitch + FirstHardpointRowY);
	}

	/// <summary>One button's rect, in the canvas. Three sit inside a framed panel and CANCEL in the content panel.</summary>
	public ShellRect ButtonRect(ShellRepairButton button) => button switch {
		ShellRepairButton.Repair => Inside(Inside(PanelRect, ItemPanelRect), ItemRepairButtonRect),
		ShellRepairButton.RepairAll => Inside(Inside(PanelRect, TotalPanelRect), TotalRepairButtonRect),
		ShellRepairButton.Scrap => Inside(Inside(PanelRect, ScrapPanelRect), ScrapButtonRect),
		_ => Inside(PanelRect, CancelRect),
	};

	/// <summary>
	/// Draws the whole screen into <paramref name="surface"/>. The caller clears it first; the content
	/// panel's body is filled rather than dithered here, so unlike the save screen nothing of the
	/// backdrop shows through it — the builder leaves <c>+0x59</c> at the constructor's 1. <paramref name="lit"/>
	/// is the widget a press has lit.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		_diagrams?.Paint(surface, Machine, _column);
		ShellSquadPanel.Paint(surface, font, text, _hangar, SelectedBay);

		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace,
			ShellChrome.InteriorColor, TitleHeight, headerChrome: true, TitlePlateFirst, TitlePlateLast,
			fill: true);
		ShellChrome.PaintText(surface, HeaderRect(PanelRect), font, text?.Text(TitleText),
			ShellTextAlign.Center, ShellChrome.FontInkColor);

		PaintList(surface, font, text, column: 0, ExternalPanelRect, ExternalListTitleText);
		PaintList(surface, font, text, column: 1, InternalPanelRect, InternalListTitleText);

		// The mode box and the salvage box: a centred label over a dead button used as a readout.
		PaintLabel(surface, font, text?.Text(ModeLabelText), Inside(PanelRect, ModeLabelRect));
		PaintReadout(surface, font, Inside(PanelRect, ModeReadoutRect),
			text?.Text(_modeText));
		PaintLabel(surface, font, text?.Text(SalvageLabelText), Inside(PanelRect, SalvageLabelRect));
		PaintReadout(surface, font, Inside(PanelRect, SalvageReadoutRect),
			WithKilograms(AvailableKilograms, text));

		PaintItemPanel(surface, font, text, lit);
		PaintTotalPanel(surface, font, text, lit);
		PaintScrapPanel(surface, font, text, lit);
		PaintButton(surface, font, text?.Text(CancelText), ShellRepairButton.Cancel, lit);
	}

	/// <summary>
	/// One of the two damage lists: a titled panel with no header chrome — the builder clears
	/// <c>+0x65</c> on both, so their headers are a flat band with no hatch and no title plate — and
	/// then its rows.
	/// </summary>
	private void PaintList(ShellSurface surface, HudFont? font, ShellText? text, int column,
			ShellRect panelRect, int titleIndex) {
		var panel = Inside(PanelRect, panelRect);
		ShellChrome.PaintTitledPanel(surface, panel, PanelBorder, ListHeaderFace,
			ShellChrome.InteriorColor, TitleHeight, headerChrome: false, 0, 0, fill: true);
		ShellChrome.PaintText(surface, HeaderRect(panel), font, text?.Text(titleIndex),
			ShellTextAlign.Center, ShellChrome.FontInkColor);

		int rows = column == 0 ? ColumnZeroRowCount : InternalRowCount;
		for (int row = 0; row < rows; row++) {
			if (column != _column || row != _row) {
				PaintRow(surface, font, text, column, row);
			}
		}

		// Rows overlap by a pixel, so whichever paints second owns the shared border row. The original
		// repaints the incoming selection after the outgoing one (Repair_SelectHotspot), which leaves the
		// highlight whole; painting it last here keeps it so on a full repaint too.
		if (column == _column) {
			PaintRow(surface, font, text, column, _row);
		}
	}

	/// <summary>
	/// One list row — <c>Repair_FillRow</c> (<c>004339b2</c>) filling the panel <c>ESQuad_AddColumns</c> (<c>0040a310</c>) built. The row is a
	/// plain panel whose border colour is the selection highlight, carrying a name at its left, an
	/// optional mount number, and the condition right-aligned in the colour of its damage band.
	/// </summary>
	private void PaintRow(ShellSurface surface, HudFont? font, ShellText? text, int column, int row) {
		var rect = RowRect(column, row);
		bool selected = column == _column && row == _row;
		ShellChrome.PaintPanel(surface, rect, selected ? SelectedRowColor : ShellChrome.InteriorColor,
			fill: true);

		var category = CategoryOf(column, row);
		int index = IndexOf(category, row);
		var machine = Machine;

		// A hardpoint row past the machine's capacity is blanked rather than left showing the last
		// machine's fitting: all three of its columns are set to the string table's single space.
		if (category == ShellRepairCategory.Hardpoint
			&& (machine == null || index >= machine.MountCapacity)) {
			return;
		}

		int condition = machine?.Condition(category, index) ?? 100;
		byte bandColor = BandColor(condition);
		byte nameColor = selected ? SelectedRowColor : RowColor;
		string? conditionText = $"{condition}%";

		string? name;
		if (category == ShellRepairCategory.Hardpoint) {
			int weaponId = machine!.WeaponAt(index);
			name = text?.Text(weaponId == 0 ? EmptyMountText : FirstWeaponNameText + weaponId);
			if (weaponId == 0) {
				conditionText = EmptyMountCondition;
			}

			RowText(surface, font, rect, MountColumnLeft, MountColumnRight, (index + 1).ToString(),
				ShellTextAlign.Left, RowColor);
		} else {
			name = text?.Text(ComponentNameText(machine?.ChassisType ?? 0, category, index));
		}

		RowText(surface, font, rect, NameColumnLeft, NameColumnRight, name, ShellTextAlign.Left,
			nameColor);
		RowText(surface, font, rect, ConditionColumnLeft, rect.Width - 2, conditionText,
			ShellTextAlign.Right, bandColor);
	}

	/// <summary>One of a row's four text children, placed by its own left and right edge within the row.</summary>
	private static void RowText(ShellSurface surface, HudFont? font, ShellRect row, int left, int right,
			string? value, ShellTextAlign align, byte color) =>
		ShellChrome.PaintText(surface,
			new ShellRect(row.X0 + left, row.Y0, row.X0 + right, row.Y0 + row.Height - 1), font, value,
			align, color, ShellChrome.InteriorColor);

	/// <summary>The <c>Selected Item</c> panel: this component's repair bill, its condition word, and REPAIR.</summary>
	private void PaintItemPanel(ShellSurface surface, HudFont? font, ShellText? text, ShellWidget? lit) {
		var panel = Inside(PanelRect, ItemPanelRect);
		ShellChrome.PaintFramedPanel(surface, panel, FrameBorder, FrameFace, fill: true);
		PaintLabel(surface, font, text?.Text(SelectedItemText), FullWidth(panel, PanelTitleRect));
		PaintLabel(surface, font, text?.Text(SalvageRequiredText), FullWidth(panel, PanelCostLabelRect));
		PaintReadout(surface, font, Inside(panel, PanelCostReadoutRect),
			WithKilograms(SelectionCost, text));
		PaintLabel(surface, font, text?.Text(ConditionText), FullWidth(panel, ItemConditionLabelRect));

		// The condition word is drawn in the readout grey like every other box, not in its damage band's
		// colour. Repair_RefreshDetail (00433445) does look the band colour up and write it to the widget's +0xb5 — and then
		// calls ESMessage_SetString with 0x17, which overwrites +0xb5 before the paint. The write is dead; see
		// docs/retail/shell/weapons-and-repair.md, "The repair screen".
		PaintReadout(surface, font, Inside(panel, ItemConditionReadoutRect),
			text?.Text(ConditionWordText(SelectionCondition)));
		PaintButton(surface, font, text?.Text(RepairText), ShellRepairButton.Repair, lit);
	}

	/// <summary>The <c>Total</c> panel: a full rebuild's bill and REPAIR ALL.</summary>
	private void PaintTotalPanel(ShellSurface surface, HudFont? font, ShellText? text, ShellWidget? lit) {
		var panel = Inside(PanelRect, TotalPanelRect);
		ShellChrome.PaintFramedPanel(surface, panel, FrameBorder, FrameFace, fill: true);
		PaintLabel(surface, font, text?.Text(TotalText), FullWidth(panel, PanelTitleRect));
		PaintLabel(surface, font, text?.Text(SalvageRequiredText), FullWidth(panel, PanelCostLabelRect));
		PaintReadout(surface, font, Inside(panel, PanelCostReadoutRect), WithKilograms(MachineCost, text));
		PaintButton(surface, font, text?.Text(RepairAllText), ShellRepairButton.RepairAll, lit);
	}

	/// <summary>The <c>Scrap Herc</c> panel: a title and the button, with no readout of its own.</summary>
	private void PaintScrapPanel(ShellSurface surface, HudFont? font, ShellText? text, ShellWidget? lit) {
		var panel = Inside(PanelRect, ScrapPanelRect);
		ShellChrome.PaintFramedPanel(surface, panel, FrameBorder, FrameFace, fill: true);
		PaintLabel(surface, font, text?.Text(ScrapHercText), FullWidth(panel, PanelTitleRect));
		PaintButton(surface, font, text?.Text(ScrapText), ShellRepairButton.Scrap, lit);
	}

	private static void PaintLabel(ShellSurface surface, HudFont? font, string? value, ShellRect rect) =>
		ShellChrome.PaintText(surface, rect, font, value, ShellTextAlign.Center, LabelColor);

	/// <summary>
	/// A readout box: a <c>Button</c> the builder immediately disables and gives a quieter border and
	/// caption colour than a live one. It is a box with a figure in it, not a control.
	/// </summary>
	private static void PaintReadout(ShellSurface surface, HudFont? font, ShellRect rect, string? value) =>
		ShellChrome.PaintButton(surface, rect, ReadoutBorder, font, value, ReadoutTextColor,
			backingColor: ShellChrome.InteriorColor);

	/// <summary>One live button, greyed together with its caption when its gate is shut.</summary>
	private void PaintButton(ShellSurface surface, HudFont? font, string? caption,
			ShellRepairButton button, ShellWidget? lit) {
		bool enabled = IsEnabled(button);
		ShellChrome.PaintButton(surface, ButtonRect(button), enabled ? ButtonBorder : DisabledColor, font, caption,
			enabled ? ShellChrome.FontInkColor : DisabledColor,
			pressed: enabled && lit == new ShellWidget(ShellWidgetKind.RepairButton, (int)button));
	}

	/// <summary>
	/// Which <c>estext.bin</c> word names one component. <c>Repair_SetComponentNames</c> (<c>00433cdf</c>) holds two fifteen-entry
	/// tables — six group names then nine internal names — and picks the second whenever the machine is
	/// chassis type 8, the Razor, whose parts are nacelles and wings rather than torsos and legs.
	///
	/// <para>The original runs it only from <c>Squad_SelectBay</c>'s repair arm, so a machine selected on
	/// another tab keeps the names this tab last set; this engine names every machine by its own chassis
	/// (docs/retail/shell/weapons-and-repair.md#which-names-a-chassis-shows).</para>
	/// </summary>
	public static int ComponentNameText(int chassisType, ShellRepairCategory category, int index) {
		int[] table = chassisType == FlyerChassisType ? FlyerComponentNames : WalkerComponentNames;
		int slot = category == ShellRepairCategory.Internal ? ExternalRowCount + index : index;
		return slot >= 0 && slot < table.Length ? table[slot] : 0;
	}

	/// <summary>
	/// <c>Repair_DamageLevelColor</c> (<c>0043da0f</c>) — the colour a condition is printed in, one per damage band. The bands are
	/// the repair ladder's own, so the colour and the word beside it always agree.
	/// </summary>
	public static byte BandColor(int condition) =>
		BandColors[ShellRepairCosts.LevelForCondition(condition)];

	/// <summary>
	/// The <c>estext.bin</c> word the Condition readout prints for a condition: the damage level's
	/// offset into the run at <see cref="FirstConditionWordText"/>. Levels 4 and 5 index past that run,
	/// and this reproduces the overrun rather than clamping it.
	/// </summary>
	public static int ConditionWordText(int condition) =>
		FirstConditionWordText + ShellRepairCosts.LevelForCondition(condition);

	/// <summary>A figure with the string table's <c>kg</c> after it — the screen's own <c>"%ld %s"</c>.</summary>
	private static string WithKilograms(int value, ShellText? text) =>
		$"{value} {text?.Text(KilogramsText) ?? string.Empty}".TrimEnd();

	/// <summary>A titled panel's caption rect: its full width by its header height.</summary>
	private static ShellRect HeaderRect(ShellRect panel) =>
		new(panel.X0, panel.Y0, panel.X0 + panel.Width - 1, panel.Y0 + TitleHeight);

	/// <summary>
	/// A framed panel's full-width label. The builder writes the right edge as the panel's own inclusive
	/// width rather than as a literal, so the label is centred over the whole box whatever it is.
	/// </summary>
	private static ShellRect FullWidth(ShellRect panel, ShellRect rect) =>
		new(panel.X0, panel.Y0 + rect.Y0, panel.X0 + panel.Width, panel.Y0 + rect.Y1);

	private ShellRect RowIn(ShellRect panel, int top) =>
		new(panel.X0 + RowLeft, panel.Y0 + top, panel.X0 + RowRight, panel.Y0 + top + RowHeight - 1);

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary>The row geometry both lists share: 13 tall on a 12-pixel pitch, so each row overlaps its neighbour's border.</summary>
	private const int RowLeft = 0xc;
	private const int RowRight = 0xd5;
	private const int RowPitch = 0xc;
	private const int RowHeight = 13;
	private const int FirstRowY = 0x1c;

	/// <summary>Where the ten hardpoint rows start, below a gap that separates them from the six groups.</summary>
	private const int FirstHardpointRowY = 0x72;

	/// <summary>The four text columns <c>ESQuad_AddColumns</c> (<c>0040a310</c>) divides a row into. The second is zero-wide and unused.</summary>
	private const int NameColumnLeft = 2;
	private const int NameColumnRight = 0x94;
	private const int MountColumnLeft = 0x94;
	private const int MountColumnRight = 0xab;
	private const int ConditionColumnLeft = 0xab;

	/// <summary>The content panel's colour fields, written by the builder over the class defaults.</summary>
	private const byte PanelBorder = 0x27;
	private const byte PanelFace = 0x25;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x7f;
	private const int TitlePlateLast = 0x10a;

	/// <summary>The two list panels keep the constructor's header face, which the content panel does not.</summary>
	private const byte ListHeaderFace = 0x24;

	/// <summary>The three readout panels' — a visible checkerboard, where the save screen flattens its own.</summary>
	private const byte FrameBorder = 0x15;
	private const byte FrameFace = 0x25;

	private const byte ButtonBorder = 0x22;
	private const byte ReadoutBorder = 0x13;
	private const byte ReadoutTextColor = 0x17;
	private const byte DisabledColor = 0x26;
	private const byte RowColor = 0x27;
	private const byte SelectedRowColor = 0x29;
	private const byte LabelColor = 0x1a;

	/// <summary>The chassis type whose component names are the flyer set — the Razor.</summary>
	public const int FlyerChassisType = 8;

	/// <summary>
	/// The colour per damage band, the in-image table at <c>004765ca</c>. The last two belong to bands
	/// whose words are a retail mistake; see Herculan/KNOWN_ISSUES.md.
	/// </summary>
	private static readonly byte[] BandColors = { 14, 13, 12, 11, 10, 39 };

	/// <summary>
	/// A walker's component names: the six groups <c>Cockpit</c> to <c>Right Leg</c>, then the nine
	/// internals. These are the indices the builder itself constructs each row with, <c>0x4e + row</c>
	/// and <c>0x54 + row</c>.
	/// </summary>
	private static readonly int[] WalkerComponentNames = {
		0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53,
		0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x5b, 0x5c,
	};

	/// <summary>
	/// The Razor's, substituting <c>estext.bin</c> <c>0x5d</c>-<c>0x63</c> — two nacelles, a fuselage,
	/// two wings and two wing servos — for the walker's torsos, chassis, legs and leg servos. Read from
	/// the table at <c>0047401e</c>; the cockpit and the last seven internals are shared with the walker
	/// set above.
	/// </summary>
	private static readonly int[] FlyerComponentNames = {
		0x4e, 0x5d, 0x5e, 0x5f, 0x60, 0x61,
		0x62, 0x63, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x5b, 0x5c,
	};

	/// <summary>
	/// What the condition column shows for a mount that is fitted with nothing: the two bytes at
	/// <c>0047465c</c> are <c>20 00</c>, a single space, so the column reads blank rather than showing
	/// the 100% an empty slot's condition entry actually holds.
	/// </summary>
	private const string EmptyMountCondition = " ";

	/// <summary><c>estext.bin</c> indices the screen prints.</summary>
	private const int TitleText = 0x3d;
	private const int ExternalListTitleText = 0x3e;
	private const int InternalListTitleText = 0x3f;
	private const int ModeLabelText = 0x40;
	private const int ManualRepairText = 0x41;
	private const int AutoRepairText = 0x42;
	private const int SalvageLabelText = 0x43;
	private const int CancelText = 0x44;
	private const int ScrapText = 0x46;
	private const int ScrapHercText = 0x47;
	private const int TotalText = 0x48;
	private const int SalvageRequiredText = 0x49;
	private const int RepairAllText = 0x4a;
	private const int SelectedItemText = 0x4b;
	private const int ConditionText = 0x4c;
	private const int RepairText = 0x4d;
	private const int EmptyMountText = 0x7d;
	private const int KilogramsText = 0xc8;

	/// <summary>
	/// The weapon names, which the screen indexes by weapon id. They start one past <c>--Empty--</c>,
	/// so id 0 lands on <c>None</c> and a fitted weapon on its own name.
	/// </summary>
	private const int FirstWeaponNameText = 0x7e;

	/// <summary>
	/// The condition words, which the screen indexes by damage level. The run is four words long —
	/// <c>Nominal</c>, <c>Light</c>, <c>Moderate</c>, <c>Heavy</c> — for six levels, so level 4 reads
	/// <c>0x6c</c> <c>% Complete</c> and level 5 <c>0x6d</c> <c>Unassigned</c>, the two unrelated
	/// entries that follow. See Herculan/KNOWN_ISSUES.md and docs/retail/shell/weapons-and-repair.md, "The Condition
	/// readout".
	/// </summary>
	private const int FirstConditionWordText = 0x68;
}
