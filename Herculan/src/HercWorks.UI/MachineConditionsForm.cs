using System.ComponentModel;
using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;

namespace HercWorks.UI;

/// <summary>
/// Edits one <c>player.mec</c> entry's condition arrays — <see cref="MecEntry.ExternalConditions"/> (13 external
/// facets), <see cref="MecEntry.InternalConditions"/> (the nine internals, then the machine's overall condition)
/// and <see cref="MecEntry.HardpointConditions"/> (one per hardpoint, in weapon-slot order). They are the HERC
/// record's 66-byte status block, which VSHELL copies into the export verbatim; see
/// docs/retail/formats/save-games.md. Each value is 0-100. Edits are written back only on OK.
/// </summary>
public partial class MachineConditionsForm : Form {
	private const int ExternalCount = 13;
	private const int InternalCount = 10;
	private const int HardpointCount = 10;

	/// <summary>Internal index 9 is the overall condition, not a component.</summary>
	private const int OverallConditionIndex = 9;

	private readonly MecEntry _entry;
	private readonly BindingList<HercPartRow> _externalsRows = new();
	private readonly BindingList<HercPartRow> _internalsRows = new();
	private readonly BindingList<HercPartRow> _hardpointsRows = new();

	public MachineConditionsForm(MecEntry entry, string title) {
		_entry = entry;
		InitializeComponent();
		Text = title;

		for (short i = 0; i < ExternalCount; i++) {
			_externalsRows.Add(new HercPartRow {
				Id = i,
				Label = HercExternals.GetById(i)?.Label ?? $"Facet {i}",
				Health = Read(entry.ExternalConditions, i)
			});
		}

		for (short i = 0; i < InternalCount; i++) {
			_internalsRows.Add(new HercPartRow {
				Id = i,
				Label = i == OverallConditionIndex
					? "Overall condition"
					: HercInternals.GetById(i)?.Label ?? $"Internal {i}",
				Health = Read(entry.InternalConditions, i)
			});
		}

		for (short i = 0; i < HardpointCount; i++) {
			string fitted = i < entry.WeaponRefs.Length
				? WeaponLUT.GetById(entry.WeaponRefs[i])?.Name ?? $"id {entry.WeaponRefs[i]}"
				: "(no slot)";
			_hardpointsRows.Add(new HercPartRow { Id = i, Label = $"Slot {i}: {fitted}", Health = Read(entry.HardpointConditions, i) });
		}

		_externalsGrid.DataSource = _externalsRows;
		_internalsGrid.DataSource = _internalsRows;
		_hardpointsGrid.DataSource = _hardpointsRows;
	}

	private static short Read(byte[] block, int index) =>
		block.Length >= (index + 1) * 2 ? BitConverter.ToInt16(block, index * 2) : (short)0;

	private static void Write(byte[] block, int index, short value) {
		if (block.Length >= (index + 1) * 2) {
			BitConverter.GetBytes(value).CopyTo(block, index * 2);
		}
	}

	private void OnOk(object? sender, EventArgs e) {
		foreach (var grid in new[] { _externalsGrid, _internalsGrid, _hardpointsGrid }) {
			grid.EndEdit();
		}

		foreach (var row in _externalsRows) {
			Write(_entry.ExternalConditions, row.Id, row.Health);
		}
		foreach (var row in _internalsRows) {
			Write(_entry.InternalConditions, row.Id, row.Health);
		}
		foreach (var row in _hardpointsRows) {
			Write(_entry.HardpointConditions, row.Id, row.Health);
		}

		DialogResult = DialogResult.OK;
		Close();
	}

	private void OnCancel(object? sender, EventArgs e) {
		DialogResult = DialogResult.Cancel;
		Close();
	}
}
