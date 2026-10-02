using System.ComponentModel;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;

namespace HercWorks.UI;

/// <summary>
/// Editor for SHELL/GAM/HERC_INF.DAT — one row per herc (mass, speed, displayed hardpoint count,
/// price, build time, availability flag). The hardpoint column is only what the Herc Construction
/// screen prints; the capacity the game equips comes from a table in VSHELL's code (see HercLUT). On open it loads the copy GamePaths'
/// GAM search order finds — a loose override, an unpacked SHELL0 tree, or the entry inside
/// SHELL0.VOL — and always saves to a loose .DAT (there's no VOL repacker yet), which the game
/// reads in preference to its packed copy. Save As writes the content-only shape VSHELL reads from
/// that loose override; Save As With VOL Prefix keeps the 9-byte entry prefix of an unpacked archive
/// copy (see VolEntryPrefixCodec). Control layout lives in
/// HercStatsForm.Designer.cs so the form can be opened in the WinForms visual designer; this file
/// holds only state and event-handler logic.
/// </summary>
public partial class HercStatsForm : Form {
	private readonly BindingList<HercStatRow> _rows = new();
	private readonly HercInfoTransformer _transformer = new();

	private GameFile? _loadedFile;

	public HercStatsForm() {
		InitializeComponent();
	}

	private void OnClose(object? sender, EventArgs e) => Close();

	private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e) => _grid.InvalidateRow(e.RowIndex);

	/// <summary>
	/// Distinct per-form/file-type identity — see CampaignResourcesForm's DialogClientGuid for the
	/// full explanation. Shared by Open and both saves since all three deal with HERC_INF.DAT.
	/// </summary>
	private static readonly Guid DialogClientGuid = new("3a72675c-7fc2-4cda-a293-de65df2ee1b0");

	/// <summary>
	/// Opened automatically on startup, found by GamePaths' GAM search order (loose override, then
	/// unpacked SHELL0 tree, then inside SHELL0.VOL) so the editor starts on whichever copy the game
	/// itself would read.
	/// </summary>
	private const string DefaultFileName = "HERC_INF.DAT";

	protected override void OnLoad(EventArgs e) {
		base.OnLoad(e);

		try {
			if (GamePaths.FindGamFile(DefaultFileName) is { } file) {
				LoadGameFile(file);
			}
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load {DefaultFileName} from the game directory:\n{ex.Message}",
				"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnOpen(object? sender, EventArgs e) {
		using var dialog = new OpenFileDialog {
			Filter = "HERC_INF.DAT|HERC_INF.DAT|DAT files (*.dat)|*.dat|All files (*.*)|*.*",
			Title = "Open HERC_INF.DAT",
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

	private void LoadGameFile(GameFile file) {
		try {
			var hercInf = (HercInf?)_transformer.Parse(file.Content);

			if (hercInf == null) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			_rows.Clear();
			foreach (var entry in hercInf.Data) {
				_rows.Add(new HercStatRow {
					HercId = entry.HercId,
					Weight = entry.Weight,
					Speed = entry.Speed,
					HardpointTotal = entry.HardpointTotal,
					SalvageReq = entry.SalvageReq,
					UnknownFlag = entry.Unknown0A,
					BuildMissionCount = entry.BuildMissionCount,
					FlagCampaignStart = entry.AvailabilityFlag
				});
			}

			_loadedFile = file;
			_saveWithPrefixMenuItem.Enabled = file.CompressionType.HasValue && file.MagicPrefix != null;

			string prefixNote = file.CompressionType.HasValue ? " (VOL entry prefix detected)" : "";
			_statusLabel.Text = $"Loaded {file.Location} — {_rows.Count} hercs.{prefixNote}";
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnSaveAs(object? sender, EventArgs e) =>
		Save(withPrefix: false, GamePaths.InitialDirectoryFor("GAM"));

	private void OnSaveWithPrefix(object? sender, EventArgs e) =>
		Save(withPrefix: true, _loadedFile?.LoosePath is { } loosePath
			? Path.GetDirectoryName(loosePath)!
			: GamePaths.GamInitialDirectory);

	private void Save(bool withPrefix, string initialDirectory) {
		if (_rows.Count == 0) {
			MessageBox.Show(this, "Open a HERC_INF.DAT file first.", "Nothing to save",
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		using var dialog = new SaveFileDialog {
			Filter = "HERC_INF.DAT|HERC_INF.DAT|DAT files (*.dat)|*.dat|All files (*.*)|*.*",
			Title = withPrefix ? "Save HERC_INF.DAT with VOL entry prefix" : "Save HERC_INF.DAT",
			FileName = _loadedFile?.FileName ?? DefaultFileName,
			ClientGuid = DialogClientGuid,
			InitialDirectory = initialDirectory
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			var hercInf = new HercInf(_rows.Count);

			for (int i = 0; i < _rows.Count; i++) {
				var row = _rows[i];
				hercInf.Data[i] = new HercInfEntry {
					HercId = row.HercId,
					Weight = row.Weight,
					Speed = row.Speed,
					HardpointTotal = row.HardpointTotal,
					SalvageReq = row.SalvageReq,
					Unknown0A = row.UnknownFlag,
					BuildMissionCount = row.BuildMissionCount,
					AvailabilityFlag = row.FlagCampaignStart
				};
			}

			byte[] content = _transformer.Write(hercInf)!;
			string message;

			if (withPrefix) {
				File.WriteAllBytes(dialog.FileName, VolEntryPrefixCodec.Wrap(content,
					_loadedFile!.CompressionType!.Value, _loadedFile.MagicPrefix!, _loadedFile.HadTrailingByte));
				message = "Saved with the original VOL entry prefix, size field updated — the shape of an " +
					"unpacked archive copy. The game reads a loose override without it; use Save As for that.";
			} else {
				File.WriteAllBytes(dialog.FileName, content);
				message = "Saved in the content-only format the game reads from a loose file.\n\n" +
					"VSHELL opens gam\\herc_inf.dat relative to the install folder before it searches its VOLs, " +
					"so a copy at GAM\\HERC_INF.DAT in your ES2 install overrides the packed one.";
			}

			MessageBox.Show(this, message, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to save file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}
}
