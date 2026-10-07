using System.ComponentModel;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Data.Struct.Vshell.Sav;

namespace HercWorks.UI;

/// <summary>
/// Per-bay detail editor opened from CampaignResourcesForm's Herc Bay tab — edits one
/// HercBayEntry's per-part health (externals/internals/hardpoints) and equipped weapons in place.
/// Externals/Internals/Hardpoints are fixed-identity rows (Health is the only editable field);
/// Weapons is a free add/remove grid since HercBayEntry.Mounts is a sparse dictionary keyed by
/// hardpoint socket id. Edits are only written back into the live HercBayEntry if the dialog is
/// accepted (OK) — Cancel discards them, matching standard modal-dialog convention.
/// </summary>
public partial class HercBayEditorForm : Form {
	private readonly HercBayEntry _entry;
	private readonly BindingList<HercPartRow> _externalsRows = new();
	private readonly BindingList<HercPartRow> _internalsRows = new();
	private readonly BindingList<HercPartRow> _hardpointsRows = new();
	private readonly BindingList<HercWeaponRow> _weaponsRows = new();

	public HercBayEditorForm(HercBayEntry entry, string title) {
		_entry = entry;
		InitializeComponent();
		Text = title;

		_weaponIdColumn.Items.AddRange(WeaponLUT.Values().Cast<object>().ToArray());
		_weaponMissileColumn.Items.AddRange(MissileType.Values().Cast<object>().ToArray());

		foreach (var external in HercExternals.Values()) {
			var part = entry.ExternalConditions?.GetValueOrDefault(external) ?? new ShellHercPart(external.Id, external.Label);
			_externalsRows.Add(new HercPartRow { Id = external.Id, Label = external.Label, Health = part.Health });
		}

		foreach (var internalPart in HercInternals.Values()) {
			if (internalPart.Id >= HercInternals.ServosLegLeftRear.Id) {
				continue;
			}
			var part = entry.InternalConditions?.GetValueOrDefault(internalPart) ?? new ShellHercPart(internalPart.Id, internalPart.Label);
			// In the save, index 9 is the machine's overall condition rather than a component
			// (docs/retail/formats/save-games.md); HercInternals' own label for it serves the sim's .DMG files.
			string label = internalPart == HercInternals.Pilot ? "Overall condition" : internalPart.Label;
			_internalsRows.Add(new HercPartRow { Id = internalPart.Id, Label = label, Health = part.Health });
		}

		for (short h = 0; h < entry.HardpointConditions.Length; h++) {
			var part = entry.HardpointConditions[h];
			_hardpointsRows.Add(new HercPartRow { Id = h, Label = $"Slot {h}", Health = part?.Health ?? 0 });
		}

		foreach (var kv in entry.Mounts) {
			_weaponsRows.Add(new HercWeaponRow {
				SocketId = kv.Key,
				WeaponId = kv.Value.Id,
				NameId = kv.Value.ClassIndex,
				HealthArmor = kv.Value.FitCondition,
				HealthInternal = kv.Value.Condition,
				MissileType = kv.Value.Guidance
			});
		}

		_externalsGrid.DataSource = _externalsRows;
		_internalsGrid.DataSource = _internalsRows;
		_hardpointsGrid.DataSource = _hardpointsRows;
		_weaponsGrid.DataSource = _weaponsRows;
	}

	private void OnOk(object? sender, EventArgs e) {
		_entry.ExternalConditions ??= new Dictionary<HercExternals, ShellHercPart>();
		foreach (var row in _externalsRows) {
			var external = HercExternals.GetById(row.Id)!;
			_entry.ExternalConditions[external] = new ShellHercPart(external.Id, external.Label, row.Health);
		}

		_entry.InternalConditions ??= new Dictionary<HercInternals, ShellHercPart>();
		foreach (var row in _internalsRows) {
			var internalPart = HercInternals.GetById(row.Id)!;
			_entry.InternalConditions[internalPart] = new ShellHercPart(internalPart.Id, internalPart.Label, row.Health);
		}

		foreach (var row in _hardpointsRows) {
			_entry.HardpointConditions[row.Id] = new ShellHercPart(row.Id, row.Label, row.Health);
		}

		_entry.Mounts.Clear();
		foreach (var row in _weaponsRows) {
			if (row.WeaponId == null || row.MissileType == null) {
				continue;
			}
			_entry.Mounts[row.SocketId] = new ShellWeaponEntry {
				Id = row.WeaponId,
				ClassIndex = row.NameId,
				FitCondition = row.HealthArmor,
				Condition = row.HealthInternal,
				Guidance = row.MissileType
			};
		}

		// MountsOccupied is the on-disk count of mount entries that follow, so it must match the
		// Mounts dictionary actually written.
		_entry.MountsOccupied = (short)_entry.Mounts.Count;

		DialogResult = DialogResult.OK;
		Close();
	}

	private void OnCancel(object? sender, EventArgs e) {
		DialogResult = DialogResult.Cancel;
		Close();
	}

	private void OnAddWeapon(object? sender, EventArgs e) {
		short nextSocket = _weaponsRows.Count == 0 ? (short)0 : (short)(_weaponsRows.Max(r => r.SocketId) + 1);
		_weaponsRows.Add(new HercWeaponRow {
			SocketId = nextSocket,
			WeaponId = WeaponLUT.Values().FirstOrDefault(),
			HealthArmor = 100,
			HealthInternal = 100,
			MissileType = MissileType.None
		});
	}

	private void OnRemoveWeapon(object? sender, EventArgs e) {
		if (_weaponsGrid.CurrentRow?.DataBoundItem is HercWeaponRow row) {
			_weaponsRows.Remove(row);
		}
	}
}
