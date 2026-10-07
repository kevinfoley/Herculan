using System.ComponentModel;
using System.Text;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Io.Transform.Shell;
using HercWorks.Vol;

namespace HercWorks.UI;

/// <summary>
/// Editor for SHELL/GAM/WEAPONS.DAT's weapon catalog (id, catalog code, price, unlock flag, rank)
/// — one row per weapon. The file's trailing section, the armory's starting stock
/// (StartingWeapons), isn't shown here, but its bytes are carried through unchanged on save so
/// nothing is lost. Follows the same pattern as HercStatsForm: opens whichever copy GamePaths' GAM
/// search order finds. Save As writes the content-only shape VSHELL reads from a loose override;
/// Save As With VOL Prefix keeps the 9-byte entry prefix of an unpacked archive copy (see
/// VolEntryPrefixCodec). Layout lives in WeaponStatsForm.Designer.cs for the WinForms visual designer.
/// </summary>
public partial class WeaponStatsForm : Form {
	private readonly BindingList<WeaponStatRow> _rows = new();
	private readonly WeaponsDatTransformer _transformer = new();

	private GameFile? _loadedFile;

	// The armory's starting stock, carried through unchanged — not edited by this form.
	private short _loadedStartWeaponTotal;
	private UiWeaponEntry[]? _loadedStartingWeapons;

	public WeaponStatsForm() {
		InitializeComponent();
	}

	private void OnClose(object? sender, EventArgs e) => Close();

	private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e) => _grid.InvalidateRow(e.RowIndex);

	/// <summary>
	/// Distinct per-form/file-type identity — see CampaignResourcesForm's DialogClientGuid for the
	/// full explanation. Shared by Open and both saves since all three deal with WEAPONS.DAT.
	/// </summary>
	private static readonly Guid DialogClientGuid = new("b5228457-50b0-4667-b328-07a17f72c4d1");

	/// <summary>
	/// Opened automatically on startup, found by GamePaths' GAM search order — same situation as
	/// HercStatsForm's: a loose override wins, otherwise this comes out of SHELL0.VOL.
	/// </summary>
	private const string DefaultFileName = "WEAPONS.DAT";

	/// <summary>
	/// The longest catalog code the game can hold: VSHELL reads it, NUL included, into the 16-byte
	/// head of its in-memory record. See <c>docs/retail/formats/weapons-dat.md</c>.
	/// </summary>
	private const int MaxCodeLength = 15;

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
			Filter = "WEAPONS.DAT|WEAPONS.DAT|DAT files (*.dat)|*.dat|All files (*.*)|*.*",
			Title = "Open WEAPONS.DAT",
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
			var weaponsDat = (WeaponsDat?)_transformer.Parse(file.Content);

			if (weaponsDat == null) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			_rows.Clear();
			foreach (var entry in weaponsDat.Data) {
				_rows.Add(new WeaponStatRow {
					Id = entry.Id,
					Name = DecodeName(entry.Name),
					SalvageCost = entry.SalvageCost,
					StartUnlock = entry.StartUnlock,
					AutobuildPriority = entry.AutobuildPriority
				});
			}

			_loadedStartWeaponTotal = weaponsDat.StartWeaponTotal;
			_loadedStartingWeapons = weaponsDat.StartingWeapons;

			_loadedFile = file;
			_saveWithPrefixMenuItem.Enabled = file.CompressionType.HasValue && file.MagicPrefix != null;

			string prefixNote = file.CompressionType.HasValue ? " (VOL entry prefix detected)" : "";
			_statusLabel.Text = $"Loaded {file.Location} — {_rows.Count} weapons.{prefixNote}";
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
			MessageBox.Show(this, "Open a WEAPONS.DAT file first.", "Nothing to save",
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		var tooLong = _rows
			.Where(row => row.Name.Length > MaxCodeLength)
			.Select(row => $"Weapon {row.Id}: \"{row.Name}\" ({row.Name.Length} characters)")
			.ToList();

		if (tooLong.Count > 0) {
			MessageBox.Show(this,
				$"A catalog code is at most {MaxCodeLength} characters — VSHELL reads it, NUL included, into a " +
				"16-byte buffer, so a longer one overruns the record.\n\n" + string.Join("\n", tooLong),
				"Cannot save", MessageBoxButtons.OK, MessageBoxIcon.Error);
			return;
		}

		using var dialog = new SaveFileDialog {
			Filter = "WEAPONS.DAT|WEAPONS.DAT|DAT files (*.dat)|*.dat|All files (*.*)|*.*",
			Title = withPrefix ? "Save WEAPONS.DAT with VOL entry prefix" : "Save WEAPONS.DAT",
			FileName = _loadedFile?.FileName ?? DefaultFileName,
			ClientGuid = DialogClientGuid,
			InitialDirectory = initialDirectory
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			var weaponsDat = new WeaponsDat(_rows.Count) {
				StartWeaponTotal = _loadedStartWeaponTotal,
				StartingWeapons = _loadedStartingWeapons
			};

			for (int i = 0; i < _rows.Count; i++) {
				var row = _rows[i];
				byte[] nameBytes = EncodeName(row.Name);

				var entry = weaponsDat.AddEntry(i);
				entry.Id = row.Id;
				entry.NameLen = (short)nameBytes.Length;
				entry.Name = nameBytes;
				entry.SalvageCost = row.SalvageCost;
				entry.StartUnlock = row.StartUnlock;
				entry.AutobuildPriority = row.AutobuildPriority;
			}

			byte[] content = _transformer.Write(weaponsDat)!;
			string message;

			if (withPrefix) {
				File.WriteAllBytes(dialog.FileName, VolEntryPrefixCodec.Wrap(content,
					_loadedFile!.CompressionType!.Value, _loadedFile.MagicPrefix!, _loadedFile.HadTrailingByte));
				message = "Saved with the original VOL entry prefix, size field updated — the shape of an " +
					"unpacked archive copy. The game reads a loose override without it; use Save As for that.";
			} else {
				File.WriteAllBytes(dialog.FileName, content);
				message = "Saved in the content-only format the game reads from a loose file.\n\n" +
					"VSHELL opens gam\\weapons.dat relative to the install folder before it searches its VOLs, " +
					"so a copy at GAM\\WEAPONS.DAT in your ES2 install overrides the packed one.";
			}

			MessageBox.Show(this, message, "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to save file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	// Weapon names are stored as raw length-prefixed bytes, one byte per char (matching how
	// ThreeSpaceByteTransformer.IndexString reads other strings elsewhere in Core) — not a
	// .NET string encoding. Latin1 gives an exact one-byte-per-char round trip for that shape.
	private static string DecodeName(byte[]? nameBytes) {
		if (nameBytes == null) {
			return string.Empty;
		}
		string raw = Encoding.Latin1.GetString(nameBytes);
		int nullIndex = raw.IndexOf('\0');
		return nullIndex >= 0 ? raw[..nullIndex] : raw;
	}

	private static byte[] EncodeName(string name) => Encoding.Latin1.GetBytes(name + "\0");
}
