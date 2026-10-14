using System.Text.RegularExpressions;
using HercWorks.Core.Data.Struct;
using FireCycle = HercWorks.UI.WeaponComparison.FireCycle;

namespace HercWorks.UI;

/// <summary>
/// Read-only side-by-side comparison of the weapons (<see cref="WeaponComparison"/>): a checkbox per
/// weapon id across the top, and a grid column per checked one. The checkboxes run the weapons the
/// armory sells, then the pods and the Radar Jammer, then the Bull's armament, then the cut and
/// unbuyable ones, each group in name order; the first group starts checked. "Show corrected
/// values" swaps in the figures the four weapons with shadowed <c>PROJ.DAT</c> records hit for under
/// the engine's Fix weapon damage tweak. The checkboxes and columns follow the data, so they are
/// built at load; the panels and grid they sit in are in WeaponCompareForm.Designer.cs.
/// </summary>
public partial class WeaponCompareForm : Form {
	private readonly List<(CheckBox Box, WeaponComparison Weapon)> _weapons = new();
	private readonly List<StatRow> _rows = new();

	/// <summary>What every rate and damage row assumes, for its tooltip.</summary>
	private const string RateAssumptions =
		" Worked out from the mount's mechanics with the trigger held, at the 25 Hz tick, with the Master Energy Pool above its reserve and an energy weapon at its spawn power level (960).";

	private const string DamageAssumptions = " Before the mission difficulty's damage scale.";

	public WeaponCompareForm() {
		InitializeComponent();
	}

	/// <summary>
	/// One grid row. <paramref name="Value"/> reads the weapon and the fire cycle being shown;
	/// <paramref name="Format"/> formats it; <paramref name="CellToolTip"/> explains a cell whose figure
	/// needs it.
	/// </summary>
	private sealed record StatRow(string Label, string ToolTip, Func<WeaponComparison, FireCycle?, double?> Value,
		string Format = "N0", Func<WeaponComparison, string?>? CellToolTip = null, bool UsesDamageRecord = false);

	protected override void OnLoad(EventArgs e) {
		base.OnLoad(e);

		if (!GamePaths.IsConfigured) {
			_statusLabel.Text = "No Earthsiege 2 folder is set (File > Set Earthsiege 2 Folder).";
			return;
		}

		Cursor = Cursors.WaitCursor;
		try {
			_weaponPanel.SuspendLayout();
			foreach (var weapon in WeaponComparison.LoadAll().OrderBy(Group).ThenBy(w => SortName(w.Weapon), NameOrder.Instance)) {
				var box = new CheckBox {
					AutoSize = true,
					Text = weapon.Weapon.Name,
					Checked = Group(weapon) == 0 && weapon.Fire != null
				};
				box.CheckedChanged += OnOptionChanged;
				_weaponPanel.Controls.Add(box);
				_weapons.Add((box, weapon));
			}
			_weaponPanel.ResumeLayout();
			BuildRows();
		} finally {
			Cursor = Cursors.Default;
		}

		var missing = _weapons.FirstOrDefault().Weapon?.Missing ?? Array.Empty<string>();
		_statusLabel.Text = missing.Count == 0
			? $"Read {_weapons.Count} weapons from SHELL0 and SIMVOL0."
			: $"Read {_weapons.Count} weapons; not found or unreadable: {string.Join(", ", missing)}.";

		RebuildGrid();
	}

	/// <summary>The checkbox groups, in order: armory weapons, pods, the Bull's armament, the rest.</summary>
	private static int Group(WeaponComparison weapon) => weapon switch {
		_ when weapon.Weapon.MountKind == WeaponMountKind.Pod => 1,
		_ when weapon.Weapon.IsBullArmament => 2,
		{ InArmory: true } => 0,
		_ => 3
	};

	/// <summary>
	/// The name a weapon sorts by: its own, except that an upgrade sorts straight after the weapon it
	/// upgrades and the Razor's launcher after the HERC launchers.
	/// </summary>
	private static string SortName(WeaponLUT weapon) =>
		weapon == WeaponLUT.Elf2 ? WeaponLUT.Elfw.Name + "2"
		: weapon == WeaponLUT.Emp2 ? WeaponLUT.Empc.Name + "2"
		: weapon == WeaponLUT.Flymsl ? "MSL~"
		: weapon.Name;

	/// <summary>Name order with the digits read as numbers, so <c>ATC20</c> comes before <c>ATC100</c>.</summary>
	private sealed class NameOrder : IComparer<string> {
		public static readonly NameOrder Instance = new();

		private static readonly Regex Parts = new(@"\d+|\D+", RegexOptions.Compiled);

		public int Compare(string? x, string? y) {
			var left = Parts.Matches(x ?? string.Empty);
			var right = Parts.Matches(y ?? string.Empty);
			for (int i = 0; i < Math.Min(left.Count, right.Count); i++) {
				string a = left[i].Value, b = right[i].Value;
				int order = char.IsDigit(a[0]) && char.IsDigit(b[0])
					? long.Parse(a).CompareTo(long.Parse(b))
					: string.CompareOrdinal(a, b);
				if (order != 0) {
					return order;
				}
			}

			return left.Count.CompareTo(right.Count);
		}
	}

	private void BuildRows() {
		_rows.Add(new StatRow("Scrap Cost (tons)", "The salvage the armory charges for one unit (gam\\WEAPONS.DAT).",
			(w, _) => w.ScrapCostTons));
		_rows.Add(new StatRow("Sell Value (tons)",
			"The salvage scrapping one unit yields: a tenth of its cost, at least 1. The armory scraps a weapon's whole stock at once, and a unit's condition does not count.",
			(w, _) => w.SellValueTons));
		_rows.Add(new StatRow("Energy / Shot",
			"What the Master Energy Pool pays per shot. The reactor refills the pool at 150 a second." + RateAssumptions,
			(_, fire) => fire?.EnergyPerShot, "N0",
			w => w.Weapon == WeaponLUT.Plas && w.Fire != null
				? "The Plasma Cannon stores only half of what it draws from the pool, so it pays twice the charge it fires." : null));
		_rows.Add(new StatRow("Range (m)",
			"The weapon's range (dat\\WEAPONS.DAT). A beam reaches this far; beyond it the weapon's lamp goes red and the fire chain skips it, though the armed weapon still fires. A launcher's is how far its missile flies.",
			(w, _) => w.FlightRangeMetres ?? w.RangeMetres, "N0",
			w => w.FlightRangeMetres != null
				? $"How far the missile flies before it burns out, launched from a standing HERC and flown straight; a moving launcher adds its own speed to the missile's, and homing turns cost distance. The weapon's lamp goes green within {w.RangeMetres} m."
				: null));
		_rows.Add(new StatRow("Ammo", "The magazine, in the rounds the cockpit gauge counts.",
			(w, _) => w.Magazine, "N0",
			w => w.RoundsPerShot is { } perShot and > 1 && w.Magazine is { } magazine
				? $"{magazine / perShot:N0} shots." : null));
		_rows.Add(new StatRow("Ammo Per Shot",
			"The rounds one shot takes off the magazine. An autocannon's shot is still one projectile.",
			(w, _) => w.RoundsPerShot));
		_rows.Add(new StatRow("Projectiles Per Shot",
			"Projectiles or beams one shot puts out.",
			(_, fire) => fire?.ProjectilesPerShot, "N0", ProjectilesNote));
		_rows.Add(new StatRow("Fire Rate (shots/min)",
			"Shots a minute." + RateAssumptions,
			(_, fire) => fire?.ShotsPerMinute, "N1", FireRateNote));
		_rows.Add(new StatRow("Shield Dmg", "Shield damage per projectile." + DamageAssumptions,
			(_, fire) => fire?.ShieldDamage, "N0", DamageNote, UsesDamageRecord: true));
		_rows.Add(new StatRow("Shield DPS", "Shield damage a second with the trigger held." + DamageAssumptions + RateAssumptions,
			(_, fire) => fire?.ShieldDps, "N0", UsesDamageRecord: true));
		_rows.Add(new StatRow("Armor Dmg", "Armour damage per projectile, what gets past the shields." + DamageAssumptions,
			(_, fire) => fire?.ArmorDamage, "N0", DamageNote, UsesDamageRecord: true));
		_rows.Add(new StatRow("Armor DPS", "Armour damage a second with the trigger held." + DamageAssumptions + RateAssumptions,
			(_, fire) => fire?.ArmorDps, "N0", UsesDamageRecord: true));
		_rows.Add(new StatRow("Splash Dmg",
			"The part of a projectile's armour damage that goes off as an explosion on the machine it strikes rather than into the part it hits; the shields take it again." + DamageAssumptions,
			(_, fire) => fire?.SplashDamage, "N0",
			w => w.IsPlasma ? "The whole projectile is a blast 4000 units (24 m) across, which shields absorb first and which hits everything in it, the firer included." : null,
			UsesDamageRecord: true));
	}

	private static string? FireRateNote(WeaponComparison weapon) {
		if (weapon.Fire == null) {
			return null;
		}

		if (weapon.Weapon == WeaponLUT.Elfw) {
			return "Fires every tick while the capacitor lasts, then recharges. Each trigger pull first spins up for about 0.3 s.";
		}

		return weapon.Weapon == WeaponLUT.Elf2 ? "Fires every tick while the capacitor lasts, then recharges." : null;
	}

	private static string? ProjectilesNote(WeaponComparison weapon) {
		if (weapon.Fire == null) {
			return null;
		}

		if (weapon.Weapon == WeaponLUT.Bemp) {
			return "Three barrels, fired together.";
		}

		return weapon.Weapon == WeaponLUT.Emp2
			? "Two volleys off one charge, the second a quarter of a refire delay after the first." : null;
	}

	private string? DamageNote(WeaponComparison weapon) {
		if (weapon.ShadowedRecord is { } shadowed && weapon.OwnRecordDamage is { } own) {
			return ShowCorrected && weapon.IsCorrected
				? $"Corrected: its own PROJ.DAT record {shadowed.Own}, {own.Shield} shield / {own.Armor} armour. Retail applies record {shadowed.Applied}'s instead."
				: $"Retail applies PROJ.DAT record {shadowed.Applied}'s figures, which share this weapon's type and subtype id; its own record {shadowed.Own} says {own.Shield} shield / {own.Armor} armour.";
		}

		if (weapon.IsPlasma) {
			return "The blast's damage, the armour figure: shields absorb it and the rest goes to armour.";
		}

		return weapon.IsLauncher ? "Loaded with ARH, the guidance kind the armory delivers." : null;
	}

	private bool ShowCorrected => _correctedBox.Checked;

	private void OnOptionChanged(object? sender, EventArgs e) => RebuildGrid();

	private void OnGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e) => GridCellNotes.Paint(_grid, e);

	/// <summary>Rebuilt whole on every toggle, as the Compare Hercs grid is.</summary>
	private void RebuildGrid() {
		_grid.SuspendLayout();
		_grid.Rows.Clear();
		_grid.Columns.Clear();

		var shown = _weapons.Where(w => w.Box.Checked).Select(w => w.Weapon).ToList();
		foreach (var weapon in shown) {
			_grid.Columns.Add(new DataGridViewTextBoxColumn {
				HeaderText = weapon.Weapon.Name,
				SortMode = DataGridViewColumnSortMode.NotSortable,
				AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
			});
		}

		if (shown.Count > 0) {
			foreach (var stat in _rows) {
				var cells = shown.Select(w => (object)(stat.Value(w, w.FireFor(ShowCorrected))?.ToString(stat.Format) ?? "—")).ToArray();
				var row = _grid.Rows[_grid.Rows.Add(cells)];
				row.HeaderCell.Value = stat.Label;
				row.HeaderCell.ToolTipText = stat.ToolTip;

				for (int column = 0; column < shown.Count; column++) {
					GridCellNotes.Set(row.Cells[column], stat.CellToolTip?.Invoke(shown[column]));

					if (stat.UsesDamageRecord && ShowCorrected && shown[column].IsCorrected) {
						row.Cells[column].Style.ForeColor = SystemColors.HotTrack;
					}
				}
			}
		}

		_grid.ResumeLayout();
	}
}
