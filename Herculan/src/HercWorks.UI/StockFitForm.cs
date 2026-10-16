using System.ComponentModel;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;

namespace HercWorks.UI;

/// <summary>
/// Editor for SHELL/GAM/INI_*.DAT — the stock fit a practice mission's player machine is built with
/// when the practice screen's Herc Type picks the chassis (docs/retail/formats/herc-catalogs.md#gamini_dat--the-stock-fit-per-chassis).
/// Each file is one bare HERC catalog record: the chassis type, build progress, build missions left,
/// and one row per occupied hardpoint. Backed by HercWorks.Core's InitHercTransformer.
///
/// <para>The two build fields are not shown and are saved as loaded (retail: 100 and 0): a practice
/// mission flies the machine built from this record, so a fit still under construction has no use.</para>
///
/// <para>The Game file dropdown opens the nine files by chassis, each found by GamePaths' GAM search
/// order (a loose override, an unpacked SHELL0 tree, or the entry inside SHELL0.VOL); File ▸ Open
/// takes any file. Saves are always to a loose .DAT, as HercStatsForm's are: Save As writes the
/// content-only shape, Save As With VOL Prefix keeps the 9-byte entry prefix of an unpacked archive
/// copy (see VolEntryPrefixCodec).</para>
///
/// <para>Hardpoints are sparse in the file, so a hardpoint with nothing fitted is a missing row; the
/// save writes rows in hardpoint order and refuses a duplicate index or one past the chassis's
/// capacity (HercLUT.HardpointMax, VSHELL's in-code table).</para>
/// </summary>
public partial class StockFitForm : Form {
	private readonly InitHercTransformer _transformer = new();
	private readonly BindingList<StockFitRow> _rows = new();

	private InitHerc? _loaded;
	private GameFile? _loadedFile;

	/// <summary>Set while the form moves the Game file dropdown itself, so that does not reload.</summary>
	private bool _syncingChassisFile;

	/// <summary>
	/// Distinct per-form/file-type identity — see CampaignResourcesForm's DialogClientGuid for the
	/// full explanation. Shared by Open and both saves since all three deal with INI_*.DAT.
	/// </summary>
	private static readonly Guid DialogClientGuid = new("0f04e094-936f-42be-8baf-a8bfa1aa2ace");

	private const string FileFilter = "Stock fit files (INI_*.DAT)|INI_*.DAT|DAT files (*.dat)|*.dat|All files (*.*)|*.*";

	public StockFitForm() {
		InitializeComponent();
		_grid.DataSource = _rows;
		_ammoColumn.DataSource = AmmoTypeOption.Build(Array.Empty<short>());

		for (short type = 0; type < InitHerc.FileStems.Count; type++) {
			_chassisFileCombo.Items.Add($"{HercLUT.GetById(type)!.Name} — {InitHerc.FileNameFor(type)}");
		}
	}

	protected override void OnLoad(EventArgs e) {
		base.OnLoad(e);
		_chassisFileCombo.SelectedIndex = 0;
	}

	private void OnClose(object? sender, EventArgs e) => Close();

	private void OnChassisFileSelected(object? sender, EventArgs e) {
		if (_syncingChassisFile || _chassisFileCombo.SelectedIndex < 0) {
			return;
		}

		string fileName = InitHerc.FileNameFor(_chassisFileCombo.SelectedIndex);
		try {
			if (GamePaths.FindGamFile(fileName) is { } file) {
				LoadGameFile(file);
			} else {
				_statusLabel.Text = $"{fileName} not found — set the Earthsiege 2 folder, or use File ▸ Open.";
			}
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load {fileName} from the game directory:\n{ex.Message}",
				"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnOpen(object? sender, EventArgs e) {
		using var dialog = new OpenFileDialog {
			Filter = FileFilter,
			Title = "Open INI_*.DAT",
			ClientGuid = DialogClientGuid,
			InitialDirectory = GamePaths.GamInitialDirectory
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			LoadGameFile(GameFile.FromLooseFile(dialog.FileName));
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// The dropdowns are rebuilt before any row is bound — a combo rejects a value it has no item
	/// for, so a weapon id or type the file carries without a name has to be in the list first.
	/// </summary>
	private void LoadGameFile(GameFile file) {
		var initHerc = (InitHerc?)_transformer.Parse(file.Content);
		if (initHerc?.Data is not { } herc) {
			MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
			return;
		}

		var hardpoints = herc.Hardpoints ?? new Dictionary<short, UiWeaponEntry>();

		_typeCombo.DataSource = HercTypeOption.Build(new[] { herc.HercId })
			.Where(o => HercLUT.GetById(o.Id) is { IsPlayerChassis: true } || o.Id == herc.HercId)
			.ToList();
		_typeCombo.SelectedValue = herc.HercId;

		_rows.Clear();
		_weaponColumn.DataSource = WeaponFitOption.Build(hardpoints.Values.Select(w => w.WeaponId), includeEmptySlot: false);
		foreach (var (hardpoint, weapon) in hardpoints) {
			_rows.Add(new StockFitRow {
				Hardpoint = hardpoint,
				WeaponId = weapon.WeaponId,
				Condition = weapon.Condition,
				AmmoType = (short)(weapon.Guidance ?? MissileType.None).Id
			});
		}

		_loaded = initHerc;
		_loadedFile = file;
		_saveWithPrefixMenuItem.Enabled = file.CompressionType.HasValue && file.MagicPrefix != null;
		SyncChassisFile(file.FileName);

		string prefixNote = file.CompressionType.HasValue ? " (VOL entry prefix detected)" : "";
		_statusLabel.Text = $"Loaded {file.Location} — {_rows.Count} hardpoints fitted.{prefixNote}";
	}

	/// <summary>Points the Game file dropdown at the file just loaded, or at nothing for another name.</summary>
	private void SyncChassisFile(string fileName) {
		int index = Enumerable.Range(0, InitHerc.FileStems.Count)
			.FirstOrDefault(t => string.Equals(InitHerc.FileNameFor(t), fileName, StringComparison.OrdinalIgnoreCase), -1);

		_syncingChassisFile = true;
		_chassisFileCombo.SelectedIndex = index;
		_syncingChassisFile = false;
	}

	private short SelectedType => _typeCombo.SelectedValue is short type ? type : (short)-1;

	/// <summary>Adds the lowest hardpoint index the fit leaves empty, holding NONE until a weapon is picked.</summary>
	private void OnAddHardpoint(object? sender, EventArgs e) {
		if (_loaded == null) {
			return;
		}

		_grid.EndEdit();
		var used = _rows.Select(r => r.Hardpoint).ToHashSet();
		short free = 0;
		while (used.Contains(free)) {
			free++;
		}

		if (HercLUT.GetById(SelectedType) is { IsPlayerChassis: true } herc && free >= herc.HardpointMax) {
			MessageBox.Show(this, $"Every hardpoint of the {herc.Name} ({herc.HardpointMax}) is already fitted.",
				"No free hardpoint", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		_rows.Add(new StockFitRow {
			Hardpoint = free,
			WeaponId = (short)WeaponLUT.None.Id,
			Condition = 100,
			AmmoType = AmmoTypeOption.Filler
		});
	}

	private void OnRemoveHardpoint(object? sender, EventArgs e) {
		if (_grid.CurrentRow?.DataBoundItem is StockFitRow row) {
			_rows.Remove(row);
		}
	}

	/// <summary>
	/// A combo cell normally only commits when focus leaves it, which would leave the ammunition
	/// cell's launcher-only look a step behind the weapon just picked.
	/// </summary>
	private void OnGridCurrentCellDirtyStateChanged(object? sender, EventArgs e) {
		if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell) {
			_grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
		}
	}

	private void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e) {
		if (e.RowIndex >= 0) {
			_grid.InvalidateRow(e.RowIndex);
		}
	}

	/// <summary>Ammunition is a launcher-only field — see WeaponFitOption.IsLauncher.</summary>
	private void OnGridCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e) {
		if (e.ColumnIndex == _ammoColumn.Index && RowAt(e.RowIndex) is { IsLauncher: false }) {
			e.Cancel = true;
		}
	}

	/// <summary>
	/// Greys the ammunition cell of every hardpoint that is not a launcher, its filler reading (none) rather
	/// than the SARH it means on a launcher, as PlayerSquadForm does.
	/// </summary>
	private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e) {
		if (e.ColumnIndex != _ammoColumn.Index || RowAt(e.RowIndex) is not { } row) {
			return;
		}

		if (row.IsLauncher) {
			return;
		}

		AmmoTypeOption.FormatNonLauncherCell(e, row.AmmoType);
		if (e.CellStyle is { } style) {
			style.ForeColor = SystemColors.GrayText;
			style.BackColor = SystemColors.Control;
		}
	}

	private StockFitRow? RowAt(int rowIndex) =>
		rowIndex >= 0 && rowIndex < _grid.Rows.Count ? _grid.Rows[rowIndex].DataBoundItem as StockFitRow : null;

	/// <summary>A malformed number in a text cell: report it and keep the old value.</summary>
	private void OnGridDataError(object? sender, DataGridViewDataErrorEventArgs e) {
		e.ThrowException = false;
		e.Cancel = true;
		MessageBox.Show(this, e.Exception?.Message ?? "That value could not be applied.",
			"Invalid value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
	}

	private void OnSaveAs(object? sender, EventArgs e) =>
		Save(withPrefix: false, GamePaths.InitialDirectoryFor("GAM"));

	private void OnSaveWithPrefix(object? sender, EventArgs e) =>
		Save(withPrefix: true, _loadedFile?.LoosePath is { } loosePath
			? Path.GetDirectoryName(loosePath)!
			: GamePaths.GamInitialDirectory);

	/// <summary>The reasons the fit cannot be written, or none.</summary>
	private List<string> FitProblems() {
		var problems = _rows
			.GroupBy(r => r.Hardpoint)
			.Where(g => g.Count() > 1)
			.Select(g => $"Hardpoint {g.Key} is listed {g.Count()} times.")
			.ToList();

		if (HercLUT.GetById(SelectedType) is { IsPlayerChassis: true } herc) {
			problems.AddRange(_rows
				.Where(r => r.Hardpoint < 0 || r.Hardpoint >= herc.HardpointMax)
				.Select(r => $"Hardpoint {r.Hardpoint} is outside the {herc.Name}'s 0-{herc.HardpointMax - 1}."));
		}

		return problems;
	}

	private void Save(bool withPrefix, string initialDirectory) {
		if (_loaded == null) {
			MessageBox.Show(this, "Open an INI_*.DAT file first.", "Nothing to save",
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		_grid.EndEdit();
		if (FitProblems() is { Count: > 0 } problems) {
			MessageBox.Show(this, string.Join("\n", problems), "Cannot save",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
			return;
		}

		using var dialog = new SaveFileDialog {
			Filter = FileFilter,
			Title = withPrefix ? "Save INI_*.DAT with VOL entry prefix" : "Save INI_*.DAT",
			FileName = _loadedFile?.FileName ?? InitHerc.FileNameFor(Math.Max((short)0, SelectedType)),
			ClientGuid = DialogClientGuid,
			InitialDirectory = initialDirectory
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			var herc = new ShellHercData {
				HercId = SelectedType,
				BuildPercent = _loaded.Data!.BuildPercent,
				BuildMissionsLeft = _loaded.Data.BuildMissionsLeft,
				Hardpoints = _rows
					.OrderBy(r => r.Hardpoint)
					.ToDictionary(r => r.Hardpoint, r => new UiWeaponEntry(r.WeaponId, r.Condition, MissileType.GetById(r.AmmoType)!))
			};
			byte[] content = _transformer.Write(new InitHerc { Data = herc })!;
			string message;

			if (withPrefix) {
				File.WriteAllBytes(dialog.FileName, VolEntryPrefixCodec.Wrap(content,
					_loadedFile!.CompressionType!.Value, _loadedFile.MagicPrefix!, _loadedFile.HadTrailingByte));
				message = "Saved with the original VOL entry prefix, size field updated — the shape of an " +
					"unpacked archive copy. The game reads a loose file without it; use Save As for that.";
			} else {
				File.WriteAllBytes(dialog.FileName, content);
				message = "Saved in the content-only format the game reads from a loose file.\n\n" +
					"VSHELL opens gam\\ini_*.dat relative to the install folder before it searches its VOLs, " +
					"so a copy in GAM\\ in your ES2 install overrides the packed one. It is read only for the " +
					"Strike, Escort, Recon and Scramble practice missions.";
			}

			MessageBox.Show(this, message, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to save file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}
}
