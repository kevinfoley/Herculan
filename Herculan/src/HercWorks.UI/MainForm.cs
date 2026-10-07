using HercWorks.Core.Io.Transform;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using HercWorks.Vol.Util;
using System.Diagnostics;
using System.Media;

namespace HercWorks.UI;

/// <summary>
/// Starting shell for the HercWorks MDK WinForms port. Currently supports opening
/// and browsing a .vol archive, unpacking it to a folder, editing HERC_INF.DAT via
/// the Herc Stats editor, editing WEAPONS.DAT via the Item Stats editor, editing a
/// player .sav file's salvage/workshop slots via the Campaign Resources editor, and
/// exporting DBA/DBM/DPL Dynamix bitmap data to PNG via the Tools menu's Image
/// Export dialog, and viewing .DTS 3D models with an orbit camera via the Tools
/// menu's 3D Model Viewer (all built directly on HercWorks.Core — no ES2TransferApi
/// dependency). The VOL browser's right-hand side is a fixed-height Metadata list
/// (the raw entry info — offset, size, compression, magic prefix — plus a "File Type"
/// row with a short human-readable description whenever TransformerRegistry recognizes
/// the file) stacked above a Content tree that fills the remaining space: when the
/// selected file's type is recognized by TransformerRegistry, Content shows its actual parsed,
/// human-readable data (fully expanded) via ContentTreeRenderer; unrecognized types
/// just show a "no parser available" note there. Below Content, a "View Asset" button
/// (enabled only for DTS/DBA/DBM/WAV entries) opens the selected file directly in the 3D
/// Model Viewer or the Texture Viewer (HercWorks.UI.TextureViewerForm — preview
/// only, best-effort automatic palette matching with a manual dropdown fallback,
/// since DBA/DBM never embed which palette they use), reading straight from the
/// loaded VOL rather than requiring an already-extracted loose file. A WAV entry is
/// instead played directly through System.Media.SoundPlayer — SIMSOUND.VOL's samples
/// are plain uncompressed RIFF/WAVE, so the entry's bytes need no decoding step of
/// their own. Mission editing
/// covers data\script.dat (the VSHELL→DBSIM handoff DBSIM actually simulates) via the
/// Edit menu's Mission Script editor and data\player.mec (the player's own squad, which
/// script.dat deliberately does not carry) via its Player Squad editor; the source .msn
/// files script.dat is generated from are still a stubbed disabled menu entry. Control layout lives in MainForm.Designer.cs so the form can
/// be opened in the WinForms visual designer; this file holds only state and
/// event-handler logic.
/// </summary>
public partial class MainForm : Form {
	private Voln? _currentVol;
	private VolEntry? _selectedEntry;

	/// <summary>
	/// Backs the WAV branch of OnViewAsset. Kept as a field rather than a local so a second "View
	/// Asset" click cuts off whatever the first is still playing instead of overlapping it, and so
	/// the clip's backing MemoryStream — which SoundPlayer reads from lazily — stays alive for as
	/// long as SoundPlayer might still need it.
	/// </summary>
	private readonly SoundPlayer _soundPlayer = new();
	private MemoryStream? _soundStream;

	public MainForm() {
		InitializeComponent();
		FormClosed += (_, _) => {
			_soundPlayer.Stop();
			_soundPlayer.Dispose();
			_soundStream?.Dispose();
		};
	}

	protected override void OnLoad(EventArgs e) {
		base.OnLoad(e);
		ShowGameDirectoryStatus();
	}

	/// <summary>
	/// Asks for the install folder on first run. Deliberately in OnShown rather than OnLoad or
	/// Program.Main: the window has to exist and be visible first, both so the dialog is owned by it
	/// (which keeps the app in the foreground when the dialog closes) and so the user can see what
	/// is asking.
	/// </summary>
	protected override void OnShown(EventArgs e) {
		base.OnShown(e);

		if (GamePaths.EnsureConfigured(this)) {
			ShowGameDirectoryStatus();
		}

		Activate();
	}

	private void OnExit(object? sender, EventArgs e) => Close();

	/// <summary>
	/// Lets the user point the app at a different install (or set one after cancelling the startup
	/// prompt) — the editors' default files and every dialog's start folder hang off this.
	/// </summary>
	private void OnSetGameFolder(object? sender, EventArgs e) {
		if (GamePaths.Prompt(this)) {
			ShowGameDirectoryStatus();
		}
	}

	private void ShowGameDirectoryStatus() {
		_statusLabel.Text = GamePaths.IsConfigured
			? $"Earthsiege 2 directory: {GamePaths.GameDirectory}"
			: "No Earthsiege 2 directory set — use File ▸ Set Earthsiege 2 Folder to pick ES.EXE.";

		if (_volTree.GetNodeCount(false) == 0) {
			LoadVolList();
		}
	}

	/// <summary>
	/// Distinct per-dialog identity so Windows remembers this dialog's last-visited folder
	/// separately from other Open/Save dialogs in the app — see CampaignResourcesForm's
	/// DialogClientGuid for the full explanation.
	/// </summary>
	private static readonly Guid OpenVolClientGuid = new("af529f56-306a-4c02-9f85-feb11bdd75a1");

	/// <summary>
	/// Distinct per-dialog identity so Windows remembers this dialog's last-visited folder
	/// separately from other Open/Save dialogs in the app — see CampaignResourcesForm's
	/// DialogClientGuid for the full explanation.
	/// </summary>
	private static readonly Guid ExportSelectedFileClientGuid = new("d3f8c2b1-7e4a-4b6d-9c3f-1a2e5d8f0b6c");

	/// <summary>
	/// Load a list of VOL files in the game directory and display each one in the VOL tree.
	/// </summary>
	private void LoadVolList() {
		_volTree.Nodes.Clear();
		if (GamePaths.IsConfigured) {
			var path = Path.Join(GamePaths.GameDirectory, "VOL");
			var filePaths = Directory.GetFiles(path);
			foreach (var file in filePaths) {
				if (Path.GetExtension(file).ToUpper() == ".VOL") {
					string fileName = Path.GetFileName(file);
					var node = new TreeNode(fileName) { Name = fileName, Tag = file };
					_volTree.Nodes.Add(node);

					// Add a placeholder child node so that the + (expand) icon appears next
					// to the VOL node. When the VOL node is expanded, we'll remove the
					// placeholder and load the VOL's actual file list.
					var placeholderChild = new TreeNode("...");
					node.Nodes.Add(placeholderChild);
				}
			}
		}
	}

	private void OnOpenVol(object? sender, EventArgs e) {
		using var dialog = new OpenFileDialog {
			Filter = "Earthsiege 2 VOL files (*.vol)|*.vol|All files (*.*)|*.*",
			Title = "Open VOL file",
			ClientGuid = OpenVolClientGuid,
			InitialDirectory = GamePaths.InitialDirectoryFor("VOL")
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		TryLoadVol(dialog.FileName);
	}

	private bool TryLoadVol(string path) {
		try {
			_currentVol = VolFileReader.ParseVolFile(path);
			PopulateTree(_currentVol);
			return true;
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load VOL file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
			return false;
		}
	}

	private void OnExpandTreeNode(object sender, TreeViewCancelEventArgs e) {
		if (e.Node is not null) {
			if (e.Node.Tag is string fileName) {
				TryLoadVol(fileName);
			}
		}
	}

	private void PopulateTree(Voln vol) {
		Debug.Assert(!string.IsNullOrEmpty(vol.FileName), $"Invalid vol {vol}");

		TreeNode root;
		var nodes = _volTree.Nodes.Find(vol.FileName, true);

		if (nodes.Length == 0) {
			root = new TreeNode(vol.FileName);
			_volTree.Nodes.Add(root);
		} else {
			if (nodes.Length > 1) {
				Debug.WriteLine($"Warning: Found multiple nodes matching {vol.FileName}");
			}
			root = nodes[0];
		}

		root.Tag = vol;
		// Remove placeholder node.
		if (root.GetNodeCount(false) == 1 && root.Nodes[0].Tag is null) {
			root.Nodes.RemoveAt(0);
		}

		// Populate child nodes now if not already loaded
		if (root.GetNodeCount(false) == 0) {
			foreach (var kv in vol.Folders.OrderBy(f => f.Key)) {
				var dirNode = new TreeNode(kv.Value.Label) { Tag = kv.Value };
				foreach (var file in kv.Value.Files) {
					dirNode.Nodes.Add(new TreeNode(file.FileName) { Tag = file });
				}
				root.Nodes.Add(dirNode);
			}
			_statusLabel.Text = $"Loaded {vol.FileName} — {vol.FilesSet.Length} files, {vol.Folders.Count} folders.";
		}
	}

	private void OnTreeSelect(object? sender, TreeViewEventArgs e) {
		_fileDetails.Items.Clear();
		_contentTree.Nodes.Clear();

		if (e.Node is not null) {
			if (e.Node.Tag is VolEntry entry) {
				_currentVol = FindParentVol(e.Node);
				SelectVolEntry(entry);
			} else if (e.Node.Tag is string fileName) {
				TryLoadVol(fileName);
			} else if (e.Node.Tag is Voln vol) {
				_currentVol = vol;
			}
		}
	}

	private Voln? FindParentVol(TreeNode treeNode) {
		TreeNode currentNode = treeNode.Parent;
		while (currentNode is not null) {
			if (currentNode.Tag is Voln vol) {
				return vol;
			}
			currentNode = currentNode.Parent;
		}
		return null;
	}

	private void SelectVolEntry(VolEntry? entry) {
		_selectedEntry = entry;
		UpdateViewAssetButtonState();
		_exportSelectedFileMenuItem.Enabled = _selectedEntry is { RawBytes.Length: > 0 };

		if (entry is null) {
			return;
		}

		AddDetail("File Name", entry.FileName ?? string.Empty);
		AddDetail("Directory", entry.Dir?.Val() ?? "(unknown)");
		AddDetail("Extension", entry.Ext?.Val() ?? "(unknown)");
		if (TransformerRegistry.FindLabel(entry) is { } typeLabel) {
			AddDetail("File Type", typeLabel);
		}
		AddDetail("Offset In VOL", entry.VolOffsetValue.ToString());
		AddDetail("Size (bytes)", (entry.RawBytes?.Length ?? 0).ToString());
		AddDetail("Compression Type", entry.FileCompressionType.ToString());
		AddDetail("Magic Prefix", ByteOps.ToHex(entry.MagicPrefix));

		PopulateContent(entry);
	}

	/// <summary>
	/// "View Asset" is enabled only for types that actually have a viewer: DTS and DGS (3D model),
	/// DBA/DBM/HBA/HB0-2/DB0-2 (texture — the HBx/DBx types are byte-identical to the DBA container
	/// format, see TransformerRegistry's doc comment), and WAV (sound — see SoundPlayerForm). A DPL
	/// alone isn't a texture, so it's intentionally excluded here.
	/// </summary>
	private void UpdateViewAssetButtonState() {
		if (_selectedEntry != null && _selectedEntry.RawBytes?.Length > 0) {
			var entry = _selectedEntry;
			_viewAssetButton.Enabled = entry.Ext is FileType.Dts or FileType.Dgs or FileType.Dba or FileType.Dbm or FileType.Hba or FileType.Hb0 or FileType.Hb1 or FileType.Hb2
				or FileType.Db0 or FileType.Db1 or FileType.Db2 or FileType.Wav;

			if (entry.Ext is FileType.Wav) {
				_viewAssetButton.Text = "Play";
			} else {
				_viewAssetButton.Text = "View Asset";
			}
		} else {
			_viewAssetButton.Enabled = false;
		}
	}

	private void PopulateContent(VolEntry entry) {
		var transformer = TransformerRegistry.FindTransformer(entry);
		if (transformer == null) {
			_contentTree.Nodes.Add(new TreeNode(
				"No parser available for this file type yet — showing metadata only."));
			return;
		}

		if (entry.RawBytes == null || entry.RawBytes.Length == 0) {
			_contentTree.Nodes.Add(new TreeNode("File has no data to parse."));
			return;
		}

		try {
			var parsed = transformer.ParseToObject(entry.RawBytes);
			if (parsed == null) {
				_contentTree.Nodes.Add(new TreeNode("Parser returned no data for this file."));
				return;
			}

			string label = TransformerRegistry.FindLabel(entry) ?? entry.FileName ?? "Content";
			ContentTreeRenderer.Populate(_contentTree, label, parsed);
		} catch (Exception ex) {
			_contentTree.Nodes.Add(new TreeNode($"Failed to parse this file:\n{ex.Message}"));
		}
	}

	private void AddDetail(string label, string value) {
		_fileDetails.Items.Add(new ListViewItem(new[] { label, value }));
	}

	/// <summary>
	/// Opens the currently selected VOL-tree entry in whichever viewer matches its type — the
	/// 3D model viewer for DTS and the DGS structure libraries, the texture viewer for DBA/DBM — or, for WAV, plays it directly
	/// rather than opening a viewer window (see PlaySelectedWav). _viewAssetButton.Enabled already
	/// guarantees _selectedEntry and _currentVol are usable here (see UpdateViewAssetButtonState).
	/// </summary>
	private void OnViewAsset(object? sender, EventArgs e) {
		if (_selectedEntry == null || _currentVol == null) {
			return;
		}

		if (_selectedEntry.Ext is FileType.Dts or FileType.Dgs) {
			using var form = new Model3DViewerForm();
			form.LoadFromVolEntry(_selectedEntry, _currentVol);
			form.ShowDialog(this);
		} else if (_selectedEntry.Ext is FileType.Dba or FileType.Dbm or FileType.Hba or FileType.Hb0 or FileType.Hb1 or FileType.Hb2
			or FileType.Db0 or FileType.Db1 or FileType.Db2) {
			using var form = new TextureViewerForm();
			form.LoadFromVolEntry(_selectedEntry, _currentVol);
			form.ShowDialog(this);
		} else if (_selectedEntry.Ext == FileType.Wav) {
			PlaySelectedWav(_selectedEntry);
		}
	}

	/// <summary>
	/// Plays a WAV entry's bytes as-is: SIMSOUND.VOL's samples are plain uncompressed RIFF/WAVE (see
	/// Herculan.Engine.Audio.WaveSample's doc comment), and VolEntry.RawBytes is already the clean
	/// file content with the VOL entry prefix stripped, so SoundPlayer needs no decoding step of its
	/// own. Playback is async and fire-and-forget; a second call — including on a different entry —
	/// simply cuts off whatever is still playing.
	/// </summary>
	private void PlaySelectedWav(VolEntry entry) {
		if (entry.RawBytes is not { Length: > 0 } bytes) {
			MessageBox.Show(this, "File has no data to play.", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
			return;
		}

		_soundPlayer.Stop();
		_soundStream?.Dispose();
		_soundStream = new MemoryStream(bytes);

		try {
			_soundPlayer.Stream = _soundStream;
			_soundPlayer.Play();
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to play file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnUnpackVol(object? sender, EventArgs e) {
		if (_currentVol == null) {
			MessageBox.Show(this, "Open a VOL file first.", "No VOL loaded",
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		using var dialog = new FolderBrowserDialog { Description = "Choose a destination folder" };
		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			VolFileWriter.UnpackVol(_currentVol, dialog.SelectedPath);
			MessageBox.Show(this, "Unpack complete.", "Done",
				MessageBoxButtons.OK, MessageBoxIcon.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to unpack VOL file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// Exports the currently selected VOL-tree entry's decoded content to a standalone file,
	/// without requiring a full "Unpack VOL To Folder". _exportSelectedFileMenuItem.Enabled is
	/// kept in sync with _selectedEntry in OnTreeSelect, so a null/empty check here is just defense.
	/// </summary>
	private void OnExportSelectedFile(object? sender, EventArgs e) {
		if (_selectedEntry is not { RawBytes: { } bytes } entry) {
			return;
		}

		using var dialog = new SaveFileDialog {
			FileName = entry.FileName ?? string.Empty,
			Title = "Export selected file",
			ClientGuid = ExportSelectedFileClientGuid
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			File.WriteAllBytes(dialog.FileName, bytes);
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to export file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnOpenHercStats(object? sender, EventArgs e) {
		using var form = new HercStatsForm();
		form.ShowDialog(this);
	}

	private void OnOpenItemStats(object? sender, EventArgs e) {
		using var form = new WeaponStatsForm();
		form.ShowDialog(this);
	}

	private void OnOpenProjectiles(object? sender, EventArgs e) {
		using var form = new ProjectileDataForm();
		form.ShowDialog(this);
	}

	private void OnOpenCampaignResources(object? sender, EventArgs e) {
		using var form = new CampaignResourcesForm();
		form.ShowDialog(this);
	}

	private void OnOpenMissionScript(object? sender, EventArgs e) {
		using var form = new MissionScriptForm();
		form.ShowDialog(this);
	}

	private void OnOpenPlayerSquad(object? sender, EventArgs e) {
		using var form = new PlayerSquadForm();
		form.ShowDialog(this);
	}

	private void OnOpenImageExport(object? sender, EventArgs e) {
		using var form = new ImageExportForm();
		form.ShowDialog(this);
	}

	private void OnOpenModelViewer(object? sender, EventArgs e) {
		using var form = new Model3DViewerForm();
		form.ShowDialog(this);
	}
}
