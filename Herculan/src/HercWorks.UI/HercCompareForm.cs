using HercWorks.Core.Data.File;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Io.Transform.Common;

namespace HercWorks.UI;

/// <summary>
/// Read-only side-by-side comparison of the chassis' gameplay figures (<see cref="HercComparison"/>):
/// a checkbox per HERC across the top, and a grid column per checked one. Covers the 21 machines of
/// <c>MECHS.NAM</c>; the SKIMMER is a <c>FLYERS.NAM</c> flyer and ships no <c>.GL</c>. The nine player chassis start
/// checked. The checkboxes, columns and rows follow the data, so they are built at load; the panel and
/// grid they sit in are in HercCompareForm.Designer.cs.
/// </summary>
public partial class HercCompareForm : Form {
	private readonly List<(CheckBox Box, HercComparison Herc)> _hercs = new();
	private readonly List<StatRow> _rows = new();
	private readonly List<string> _missing = new();
	private Font? _totalFont;

	public HercCompareForm() {
		InitializeComponent();
	}

	/// <summary>
	/// One grid row. <paramref name="CellToolTip"/> names the part on a chassis that labels it
	/// differently; <paramref name="Placeholder"/> says a value carries nothing, and a row that is a
	/// placeholder on every shown chassis is left out.
	/// </summary>
	private sealed record StatRow(string Label, string ToolTip, Func<HercComparison, int?> Value, bool IsTotal = false,
		Func<HercComparison, string?>? CellToolTip = null, Func<int, bool>? Placeholder = null);

	/// <summary>
	/// <c>STRINGS0.STR</c>'s part-name groups, the labels the cockpit's damage screen uses:
	/// docs/retail/formats/str-strings.md#strings0str-groups.
	/// </summary>
	private const int WalkerComponentGroup = 13, FlyerComponentGroup = 14, WalkerInternalGroup = 15, FlyerInternalGroup = 16;

	/// <summary>How many internal slots a chassis' own systems use; 12-21 are the weapon mounts' (see <see cref="HercInternals"/>).</summary>
	private const int ChassisInternalSlots = HercInternals.FirstWeaponMountId;

	/// <summary>Indent for a breakdown row's label under its total.</summary>
	private const string PartIndent = "      ";

	protected override void OnLoad(EventArgs e) {
		base.OnLoad(e);

		if (!GamePaths.IsConfigured) {
			_statusLabel.Text = "No Earthsiege 2 folder is set (File > Set Earthsiege 2 Folder).";
			return;
		}

		Cursor = Cursors.WaitCursor;
		try {
			_hercPanel.SuspendLayout();
			var priceFilesMissing = new List<string>();
			var prices = HercComparison.ChassisPrices.Load(priceFilesMissing);
			_missing.AddRange(priceFilesMissing);
			foreach (var herc in HercLUT.Values().Where(h => h.Id < HercLUT.Skimmer.Id)) {
				var comparison = HercComparison.Load(herc, prices);
				var box = new CheckBox {
					AutoSize = true,
					Text = comparison.DisplayName,
					Checked = herc.IsPlayerChassis
				};
				box.CheckedChanged += OnHercToggled;
				_hercPanel.Controls.Add(box);
				_hercs.Add((box, comparison));
			}
			_hercPanel.ResumeLayout();
			BuildRows(LoadPartNames());
		} finally {
			Cursor = Cursors.Default;
		}

		var missing = _missing.Concat(_hercs.SelectMany(h => h.Herc.Missing)).ToList();
		_statusLabel.Text = missing.Count == 0
			? $"Read {_hercs.Count} hercs from SIMVOL0."
			: $"Read {_hercs.Count} hercs; not found or unreadable: {string.Join(", ", missing)}.";

		_totalFont = new Font(_grid.Font, FontStyle.Bold);
		RebuildGrid();
	}

	protected override void OnFormClosed(FormClosedEventArgs e) {
		_totalFont?.Dispose();
		base.OnFormClosed(e);
	}

	private static StringFile? LoadPartNames() {
		try {
			return GamePaths.FindSimFile("str", "STRINGS0.STR") is { } file
				? new StringFileTransformer().Parse(file.Content)
				: null;
		} catch (Exception) {
			// Without the game's names the rows fall back to plain ones.
			return null;
		}
	}

	private void BuildRows(StringFile? names) {
		string? Name(int group, int index) =>
			names?.Text(group, index)?.TrimEnd('\0') is { Length: > 0 } text ? text : null;

		// The component names all but the brackets and shoulders end in " ARMOR", which the section says.
		string? ComponentName(int group, int index) =>
			Name(group, index) is { } text && text.EndsWith(" ARMOR") ? text[..^" ARMOR".Length] : Name(group, index);

		_rows.Add(new StatRow("Scrap Cost (tons)",
			"The salvage the Herc Construction screen charges for the chassis (gam\\HERC_INF.DAT). Only the nine chassis the shell builds have one.",
			h => h.ScrapCostTons));
		_rows.Add(new StatRow("Sell Value (tons)",
			"The salvage scrapping the chassis yields, built, undamaged and with nothing fitted (gam\\DAMAGE.DAT against the price). A fitted weapon at 80% condition or better goes back to the armory instead.",
			h => h.SellValueTons));
		_rows.Add(new StatRow("Top Speed (km/h)", "The cockpit speed readout at full forward throttle.", h => h.TopSpeedKph));
		_rows.Add(new StatRow("Shield Capacity",
			"The shield array's total charge (dat\\NAME.DAT), front and rear together; the balance setting splits it. It recharges 5 a tick (125 a second) out of what the weapons leave in the Master Energy Pool, shrinks as the shield generator is damaged, and a Shield Pod adds up to the same again.",
			h => h.ShieldCapacity));
		_rows.Add(new StatRow("Reactor (energy/s)",
			"What the reactor adds to the Master Energy Pool, which holds up to 10,000 and keeps 500 in reserve. The same for every machine; an Energy Pod doubles it. Damage to the reactor does not lower it.",
			h => HercComparison.ReactorOutputPerSecond));
		_rows.Add(new StatRow("Hardpoints", "Weapon mounts on the chassis (gl\\NAME.GL).", h => h.Hardpoints));

		_rows.Add(new StatRow("Armor", "Total armour of the chassis' own components (dmg\\NAME.DMG). A fitted weapon adds its mount's.",
			h => h.Armor, IsTotal: true));
		for (int i = 0; i < HercComparison.FirstMountComponent; i++) {
			int index = i;
			_rows.Add(new StatRow(PartIndent + (ComponentName(WalkerComponentGroup, index) ?? $"Component {index}"),
				$"Component {index}'s armour. Retail gives 1 only to a component the chassis does not use, such as a biped's rear legs.",
				h => h.ComponentArmorAt(index),
				CellToolTip: h => h.IsFlyer ? ComponentName(FlyerComponentGroup, index) : null,
				Placeholder: armor => armor <= 1));
		}

		_rows.Add(new StatRow("Internals", "Total of the internal systems' maxima (dmg\\NAME.DMG).", h => h.Internals, IsTotal: true));
		for (int i = 0; i < ChassisInternalSlots; i++) {
			int index = i;
			_rows.Add(new StatRow(PartIndent + (Name(WalkerInternalGroup, index) ?? HercInternals.GetById((short)index)!.Label),
				$"Internal system {index}'s maximum. 0 means the chassis does not have it.",
				h => h.InternalMaximumAt(index),
				CellToolTip: h => h.IsFlyer ? Name(FlyerInternalGroup, index) : null,
				Placeholder: maximum => maximum == 0));
		}
	}

	private void OnHercToggled(object? sender, EventArgs e) => RebuildGrid();

	private void OnGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e) => GridCellNotes.Paint(_grid, e);

	/// <summary>
	/// Rebuilt whole on every toggle: removing a grid's last column also drops its rows, and at a few
	/// dozen rows by at most 21 columns there is nothing to save by patching.
	/// </summary>
	private void RebuildGrid() {
		_grid.SuspendLayout();
		_grid.Rows.Clear();
		_grid.Columns.Clear();

		var shown = _hercs.Where(h => h.Box.Checked).Select(h => h.Herc).ToList();
		foreach (var herc in shown) {
			_grid.Columns.Add(new DataGridViewTextBoxColumn {
				HeaderText = herc.DisplayName,
				SortMode = DataGridViewColumnSortMode.NotSortable,
				AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
			});
		}

		if (shown.Count > 0) {
			foreach (var stat in _rows) {
				var values = shown.Select(stat.Value).ToList();
				if (stat.Placeholder is { } placeholder && values.All(v => v == null || placeholder(v.Value))) {
					continue;
				}

				var row = _grid.Rows[_grid.Rows.Add(values.Select(v => (object)(v?.ToString("N0") ?? "—")).ToArray())];
				row.HeaderCell.Value = stat.Label;
				row.HeaderCell.ToolTipText = stat.ToolTip;
				if (stat.IsTotal) {
					row.DefaultCellStyle.Font = _totalFont;
					row.HeaderCell.Style.Font = _totalFont;
				}

				if (stat.CellToolTip != null) {
					for (int column = 0; column < shown.Count; column++) {
						GridCellNotes.Set(row.Cells[column], stat.CellToolTip(shown[column]));
					}
				}
			}
		}

		_grid.ResumeLayout();
	}
}
