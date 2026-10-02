using System.ComponentModel;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;

namespace HercWorks.UI;

/// <summary>
/// Editor for DBSIM's DAT/PROJ.DAT — one grid row per projectile record. Opens whichever copy
/// GamePaths' simulator dat search order finds. Save As writes the content-only shape DBSIM reads
/// from a loose file; Save As With VOL Prefix keeps the 9-byte entry prefix of an unpacked archive
/// copy (see VolEntryPrefixCodec). The record count is fixed: weapon templates name records by
/// index, so rows are edited in place rather than added or removed.
/// </summary>
public partial class ProjectileDataForm : Form {
	private readonly BindingList<ProjectileRow> _rows = new();
	private readonly ProjectileDataTransformer _transformer = new();

	private GameFile? _loadedFile;

	/// <summary>
	/// Distinct per-form/file-type identity — see CampaignResourcesForm's DialogClientGuid for the
	/// full explanation. Shared by Open and both saves since all three deal with PROJ.DAT.
	/// </summary>
	private static readonly Guid DialogClientGuid = new("3c8e41d2-7a65-4f1b-b0d9-58e2a6c17f43");

	private const string DefaultFileName = "PROJ.DAT";
	private const string FileFilter = "PROJ.DAT|PROJ.DAT|DAT files (*.dat)|*.dat|All files (*.*)|*.*";

	public ProjectileDataForm() {
		InitializeComponent();
	}

	private void OnClose(object? sender, EventArgs e) => Close();

	private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e) => _grid.InvalidateRow(e.RowIndex);

	/// <summary>
	/// Every editable field is an int16; text that doesn't parse as one would otherwise surface as
	/// the grid's default exception dialog.
	/// </summary>
	private void OnGridDataError(object? sender, DataGridViewDataErrorEventArgs e) {
		e.ThrowException = false;
		e.Cancel = true;
		string column = _grid.Columns[e.ColumnIndex].HeaderText;
		MessageBox.Show(this, $"{column} must be a whole number from {short.MinValue} to {short.MaxValue}.",
			"Invalid value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
	}

	protected override void OnLoad(EventArgs e) {
		base.OnLoad(e);

		try {
			if (GamePaths.FindSimDatFile(DefaultFileName) is { } file) {
				LoadGameFile(file);
			}
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load {DefaultFileName} from the game directory:\n{ex.Message}",
				"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnOpen(object? sender, EventArgs e) {
		using var dialog = new OpenFileDialog {
			Filter = FileFilter,
			Title = "Open PROJ.DAT",
			ClientGuid = DialogClientGuid,
			InitialDirectory = GamePaths.SimDatInitialDirectory
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

	private void LoadGameFile(GameFile file) {
		try {
			var projData = _transformer.Parse(file.Content);

			if (projData?.Data == null) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			var rows = projData.Data.Select((record, index) => ProjectileRow.FromRecord(index, record)).ToList();

			_rows.Clear();
			foreach (var row in rows) {
				_rows.Add(row);
			}

			_loadedFile = file;
			_saveWithPrefixMenuItem.Enabled = file.CompressionType.HasValue && file.MagicPrefix != null;

			string prefixNote = file.CompressionType.HasValue ? " (VOL entry prefix detected)" : "";
			_statusLabel.Text = $"Loaded {file.Location} — {_rows.Count} records.{prefixNote}";
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnSaveAs(object? sender, EventArgs e) =>
		Save(withPrefix: false, GamePaths.InitialDirectoryFor("DAT"));

	private void OnSaveWithPrefix(object? sender, EventArgs e) =>
		Save(withPrefix: true, _loadedFile?.LoosePath is { } loosePath
			? Path.GetDirectoryName(loosePath)!
			: GamePaths.SimDatInitialDirectory);

	private void Save(bool withPrefix, string initialDirectory) {
		if (_rows.Count == 0) {
			MessageBox.Show(this, "Open a PROJ.DAT file first.", "Nothing to save",
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		using var dialog = new SaveFileDialog {
			Filter = FileFilter,
			Title = withPrefix ? "Save PROJ.DAT with VOL entry prefix" : "Save PROJ.DAT",
			FileName = _loadedFile?.FileName ?? DefaultFileName,
			ClientGuid = DialogClientGuid,
			InitialDirectory = initialDirectory
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			var projData = new ProjectileData {
				Total = (short)_rows.Count,
				Data = _rows.Select(row => row.ToRecord()).ToArray()
			};

			byte[] content = _transformer.Write(projData)!;
			string message;

			if (withPrefix) {
				File.WriteAllBytes(dialog.FileName, VolEntryPrefixCodec.Wrap(content,
					_loadedFile!.CompressionType!.Value, _loadedFile.MagicPrefix!, _loadedFile.HadTrailingByte));
				message = "Saved with the original VOL entry prefix, size field updated — the shape of an " +
					"unpacked archive copy. The game reads a loose override without it; use Save As for that.";
			} else {
				File.WriteAllBytes(dialog.FileName, content);
				message = "Saved in the content-only format the game reads from a loose file.\n\n" +
					"DBSIM opens dat\\proj.dat relative to the install folder before it searches its VOLs, " +
					"so a copy at DAT\\PROJ.DAT in your ES2 install overrides the packed one.";
			}

			MessageBox.Show(this, message, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to save file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}
}
