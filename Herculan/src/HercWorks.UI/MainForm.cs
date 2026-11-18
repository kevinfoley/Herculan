using HercWorks.Core.Io.Transform;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using HercWorks.Vol.Util;
using System.Media;

namespace HercWorks.UI;

/// <summary>
/// Starting shell for the HercWorks MDK WinForms port. Currently supports opening
/// and browsing a .vol archive, unpacking it to a folder, editing HERC_INF.DAT via
/// the Herc Stats editor, editing the INI_*.DAT stock fits via the Stock Fits editor, editing WEAPONS.DAT via the Item Stats editor, editing a
/// player .sav file's salvage/workshop slots via the Campaign Resources editor, and
/// exporting DBA/DBM/DPL Dynamix bitmap data to PNG via the Tools menu's Image
/// Export dialog, and viewing .DTS 3D models with an orbit camera via the Tools
/// menu's 3D Model Viewer, and comparing chassis cost, speed, hardpoints, armour and internals
/// side by side via the Tools menu's Compare Hercs screen, and weapons' cost, ammunition, fire
/// rate and damage via its Compare Weapons screen (all built directly on HercWorks.Core — no ES2TransferApi
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
/// files script.dat is generated from are still a stubbed disabled menu entry. The filter box above the VOL tree
/// narrows it to the files whose name contains the typed text (see RebuildVolTree). Control layout lives in MainForm.Designer.cs so the form can
/// be opened in the WinForms visual designer; this file holds only state and
/// event-handler logic.
/// </summary>
public partial class MainForm : Form {
	private Voln? _currentVol;
	private VolEntry? _selectedEntry;

	/// <summary>
	/// One archive the VOL tree lists: a <c>.VOL</c> in the install's <c>VOL</c> folder, or one opened from elsewhere
	/// through File ▸ Open VOL. It is read on first expand or select, or when the filter needs every archive's file
	/// names, and kept from then on. The tree is rebuilt from these on each filter change, so they, not the tree
	/// nodes, are what holds the parsed archives.
	/// </summary>
	private sealed class VolSource(string path) {
		public string Path { get; } = path;
		public string FileName => System.IO.Path.GetFileName(Path);
		public Voln? Vol { get; set; }

		/// <summary>The last read failed. The filter skips it rather than retrying on every keystroke; a user's expand, select or open retries.</summary>
		public bool LoadFailed { get; set; }
	}

	private readonly List<VolSource> _volSources = new();

	/// <summary>The filter the tree currently shows, trimmed; empty for the full listing.</summary>
	private string _shownFilter = string.Empty;

	/// <summary>
	/// The tags of the nodes expanded in the full listing, captured when a filter starts so clearing it puts the
	/// tree back as the user left it.
	/// </summary>
	private readonly HashSet<object> _unfilteredExpansion = new(ReferenceEqualityComparer.Instance);

	/// <summary>The tag of the node the details pane shows, reselected when the tree is rebuilt.</summary>
	private object? _selectedTag;

	/// <summary>Set while RebuildVolTree replaces the nodes, so the selection and expansion it restores do not reload the details pane or read archives.</summary>
	private bool _rebuildingTree;

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
		_statusLabel.Text = GameDirectoryStatusText();

		if (_volSources.Count == 0) {
			LoadVolList();
		}
	}

	private static string GameDirectoryStatusText() => GamePaths.IsConfigured
		? $"Earthsiege 2 directory: {GamePaths.GameDirectory}"
		: "No Earthsiege 2 directory set — use File ▸ Set Earthsiege 2 Folder to pick ES.EXE.";

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
		_volSources.Clear();
		if (GamePaths.IsConfigured) {
			var path = Path.Join(GamePaths.GameDirectory, "VOL");
			foreach (var file in Directory.GetFiles(path)) {
				if (Path.GetExtension(file).Equals(".VOL", StringComparison.OrdinalIgnoreCase)) {
					_volSources.Add(new VolSource(file));
				}
			}
		}
		RebuildVolTree();
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

		string fullPath = Path.GetFullPath(dialog.FileName);
		var source = _volSources.Find(s => string.Equals(Path.GetFullPath(s.Path), fullPath, StringComparison.OrdinalIgnoreCase));
		if (source is null) {
			source = new VolSource(fullPath);
			_volSources.Add(source);
		}

		if (!EnsureLoaded(source, userAsked: true)) {
			return;
		}

		_currentVol = source.Vol;
		RebuildVolTree();
		if (FindNodeByTag(_volTree.Nodes, source) is { } node) {
			_volTree.SelectedNode = node;
			node.EnsureVisible();
		}
	}

	/// <summary>
	/// Reads <paramref name="source"/>'s archive unless it is already read. <paramref name="userAsked"/> is set when
	/// the user expanded, selected or opened this archive: then a failure is shown in a message box and a success in
	/// the status bar. The filter reads quietly and does not retry an archive that has already failed.
	/// </summary>
	private bool EnsureLoaded(VolSource source, bool userAsked) {
		if (source.Vol is not null) {
			return true;
		}
		if (source.LoadFailed && !userAsked) {
			return false;
		}

		try {
			var vol = VolFileReader.ParseVolFile(source.Path);
			source.Vol = vol;
			source.LoadFailed = false;
			if (userAsked) {
				_statusLabel.Text = $"Loaded {source.FileName} — {vol.FilesSet.Length} files, {vol.Folders.Count} folders.";
			}
			return true;
		} catch (Exception ex) {
			source.LoadFailed = true;
			if (userAsked) {
				MessageBox.Show(this, $"Failed to load VOL file:\n{ex.Message}", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			return false;
		}
	}

	private void OnExpandTreeNode(object sender, TreeViewCancelEventArgs e) {
		if (!_rebuildingTree && e.Node is { Tag: VolSource { Vol: null } source } node && !LoadVolNode(node, source)) {
			e.Cancel = true;
		}
	}

	/// <summary>Reads an archive the tree shows unread and swaps its placeholder child for its folders.</summary>
	private bool LoadVolNode(TreeNode node, VolSource source) {
		bool loaded = EnsureLoaded(source, userAsked: true);
		_volTree.BeginUpdate();
		try {
			node.Nodes.Clear();
			if (source.Vol is { } vol) {
				AddFolderNodes(node, vol, _shownFilter, matchedWithin: null);
			}
		} finally {
			_volTree.EndUpdate();
		}
		return loaded;
	}

	private void OnTreeSelect(object? sender, TreeViewEventArgs e) {
		if (_rebuildingTree) {
			return;
		}

		_fileDetails.Items.Clear();
		_contentTree.Nodes.Clear();
		_selectedTag = e.Node?.Tag;

		if (e.Node?.Tag is VolEntry entry) {
			_currentVol = FindParentVol(e.Node);
			SelectVolEntry(entry);
		} else if (e.Node?.Tag is VolSource source) {
			if (source.Vol is null) {
				LoadVolNode(e.Node, source);
			}
			_currentVol = source.Vol;
		}
	}

	private static Voln? FindParentVol(TreeNode treeNode) {
		for (var node = treeNode.Parent; node is not null; node = node.Parent) {
			if (node.Tag is VolSource { Vol: { } vol }) {
				return vol;
			}
		}
		return null;
	}

	/// <summary>Restarts the debounce, so the tree is rebuilt once typing pauses rather than on every keystroke.</summary>
	private void OnVolFilterChanged(object? sender, EventArgs e) {
		_volFilterTimer.Stop();
		_volFilterTimer.Start();
	}

	private void OnVolFilterTimerTick(object? sender, EventArgs e) {
		_volFilterTimer.Stop();
		if (_volFilter.Text.Trim() != _shownFilter) {
			RebuildVolTree();
		}
	}

	/// <summary>Escape clears the filter; Enter applies it at once instead of waiting for the debounce.</summary>
	private void OnVolFilterKeyDown(object? sender, KeyEventArgs e) {
		if (e.KeyCode == Keys.Escape && _volFilter.TextLength > 0) {
			_volFilter.Clear();
			e.SuppressKeyPress = true;
		} else if (e.KeyCode == Keys.Enter) {
			OnVolFilterTimerTick(sender, e);
			e.SuppressKeyPress = true;
		}
	}

	/// <summary>
	/// Rebuilds the VOL tree for the filter box's text. An empty filter lists every archive, restoring the expansion
	/// the full listing had when a filter started. Otherwise every archive is read first, and the tree keeps a file
	/// whose name contains the text (case-insensitive), along with its folder and archive, expanded to show it; a folder
	/// or archive whose own name matches keeps all its files and stays collapsed. The selected node is reselected
	/// when it is still listed.
	/// </summary>
	private void RebuildVolTree() {
		string filter = _volFilter.Text.Trim();
		if (filter.Length > 0) {
			LoadAllVols();
		}

		if (_shownFilter.Length == 0) {
			_unfilteredExpansion.Clear();
			CollectExpanded(_volTree.Nodes, _unfilteredExpansion);
		}

		var matchedWithin = new List<TreeNode>();
		int fileCount = 0;
		TreeNode? selected = null;

		_rebuildingTree = true;
		_volTree.BeginUpdate();
		try {
			_volTree.Nodes.Clear();
			foreach (var source in _volSources) {
				if (BuildVolNode(source, filter, matchedWithin, ref fileCount) is { } node) {
					_volTree.Nodes.Add(node);
				}
			}

			var toExpand = filter.Length > 0
				? matchedWithin
				: AllNodes(_volTree.Nodes).Where(n => n.Tag is not null && _unfilteredExpansion.Contains(n.Tag)).ToList();
			foreach (var node in toExpand) {
				node.Expand();
			}

			if (_selectedTag is not null && FindNodeByTag(_volTree.Nodes, _selectedTag) is { } match) {
				_volTree.SelectedNode = match;
				selected = match;
			}
		} finally {
			_volTree.EndUpdate();
			_rebuildingTree = false;
		}
		selected?.EnsureVisible();

		if (filter.Length > 0) {
			string unread = string.Join(", ", _volSources.Where(s => s.LoadFailed).Select(s => s.FileName));
			_statusLabel.Text = $"{fileCount} files match \"{filter}\"." + (unread.Length > 0 ? $" Could not read {unread}." : string.Empty);
		} else if (_shownFilter.Length > 0) {
			_statusLabel.Text = GameDirectoryStatusText();
		}
		_shownFilter = filter;
	}

	/// <summary>Reads every listed archive not read yet, for a filter that has to see all their file names.</summary>
	private void LoadAllVols() {
		if (_volSources.All(s => s.Vol is not null || s.LoadFailed)) {
			return;
		}

		_statusLabel.Text = "Reading VOL files…";
		_statusStrip.Update();
		Cursor.Current = Cursors.WaitCursor;
		try {
			foreach (var source in _volSources) {
				EnsureLoaded(source, userAsked: false);
			}
		} finally {
			Cursor.Current = Cursors.Default;
		}
	}

	/// <summary>
	/// The node for <paramref name="source"/> under <paramref name="filter"/>, or null when nothing in it matches.
	/// Nodes kept only because something inside them matched go in <paramref name="matchedWithin"/> to be expanded.
	/// </summary>
	private static TreeNode? BuildVolNode(VolSource source, string filter, List<TreeNode> matchedWithin, ref int fileCount) {
		var node = new TreeNode(source.FileName) { Name = source.FileName, Tag = source };
		bool showAll = filter.Length == 0 || NameMatches(source.FileName, filter);

		if (source.Vol is null) {
			if (!source.LoadFailed) {
				// Placeholder child, so the archive shows an expand icon before it is read.
				node.Nodes.Add(new TreeNode("..."));
			}
			return showAll ? node : null;
		}

		fileCount += AddFolderNodes(node, source.Vol, showAll ? string.Empty : filter, matchedWithin);
		if (showAll) {
			return node;
		}
		if (node.Nodes.Count == 0) {
			return null;
		}
		matchedWithin.Add(node);
		return node;
	}

	/// <summary>Adds <paramref name="vol"/>'s folders and files under <paramref name="volNode"/>, filtered; returns the number of files added.</summary>
	private static int AddFolderNodes(TreeNode volNode, Voln vol, string filter, List<TreeNode>? matchedWithin) {
		int fileCount = 0;
		foreach (var folder in vol.Folders.OrderBy(f => f.Key).Select(f => f.Value)) {
			bool showAll = filter.Length == 0 || NameMatches(folder.Label, filter);
			var folderNode = new TreeNode(folder.Label) { Tag = folder };
			foreach (var file in folder.Files) {
				if (showAll || NameMatches(file.FileName, filter)) {
					folderNode.Nodes.Add(new TreeNode(file.FileName) { Tag = file });
				}
			}

			if (showAll || folderNode.Nodes.Count > 0) {
				volNode.Nodes.Add(folderNode);
				fileCount += folderNode.Nodes.Count;
				if (!showAll) {
					matchedWithin?.Add(folderNode);
				}
			}
		}
		return fileCount;
	}

	private static bool NameMatches(string? name, string filter) =>
		name is not null && name.Contains(filter, StringComparison.OrdinalIgnoreCase);

	private static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes) {
		foreach (TreeNode node in nodes) {
			yield return node;
			foreach (var child in AllNodes(node.Nodes)) {
				yield return child;
			}
		}
	}

	private static void CollectExpanded(TreeNodeCollection nodes, HashSet<object> expanded) {
		foreach (var node in AllNodes(nodes)) {
			if (node.IsExpanded && node.Tag is not null) {
				expanded.Add(node.Tag);
			}
		}
	}

	private static TreeNode? FindNodeByTag(TreeNodeCollection nodes, object tag) =>
		AllNodes(nodes).FirstOrDefault(n => ReferenceEquals(n.Tag, tag));

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
		if (TransformerRegistry.FindLabel(entry, CurrentGame) is { } typeLabel) {
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

	/// <summary>
	/// The game of the install the selected entry's archive sits in, whose readers the browser uses: the archive's own
	/// location rather than the configured folder, so a <c>.VOL</c> opened from another install gets that game's.
	/// </summary>
	private RetailGame CurrentGame => RetailGameDetector.DetectFromArchive(_currentVol?.FilePath);

	private void PopulateContent(VolEntry entry) {
		var game = CurrentGame;
		var transformer = TransformerRegistry.FindTransformer(entry, game);
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

			string label = TransformerRegistry.FindLabel(entry, game) ?? entry.FileName ?? "Content";
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

	private void OnOpenStockFits(object? sender, EventArgs e) {
		using var form = new StockFitForm();
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

	private void OnOpenCompareHercs(object? sender, EventArgs e) {
		using var form = new HercCompareForm();
		form.ShowDialog(this);
	}

	private void OnOpenCompareWeapons(object? sender, EventArgs e) {
		using var form = new WeaponCompareForm();
		form.ShowDialog(this);
	}
}
