using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;

namespace HercWorks.UI;

/// <summary>
/// Viewer for .DTS 3D models (mechs, terrain props, etc.) — orbit camera over a software-rendered
/// mesh coloured the way DBSIM colours it (see DtsGeometryBuilder for the poly types and
/// Model3DViewerControl for the rasterizer). That needs three inputs: a theater palette (.DPL), the
/// same theater's ramp (.RMP, found by the palette's basename) and, for textured polys, the bank
/// the shape is bound to. A mech's bank comes from its sim .DAT's TextureGroup — the seven shared
/// atlases in docs/retail/formats/dts-texture-binding.md's "DBSIM's mech-to-texture mapping"; same-basename
/// DBAs like SAMSON.DBA are 2D damage-readout art, not mesh textures. Whatever is missing falls back
/// to placeholder colours. TSBitmapPart billboards are not built.
///
/// <para>It also opens the two .DGS structure libraries, dgs\BASES.DGS and dgs\BHULKS.DGS, whose
/// records each wrap one ordinary DTS object (see BasesDgsTransformer). Each record becomes a part,
/// labelled with its record index, which is the shape index BASES.DAT's +0x02 (or its wreck index
/// +0x04, for BHULKS) selects. Their default bank is BASETEX, which the simulation binds to every
/// wreck and to every structure type whose BASES.DAT +0x32 is zero; the ground vehicles' VEHTEX is
/// in the bank selector.</para>
/// </summary>
public partial class Model3DViewerForm : Form {
	private readonly DTSModelTransformer _dtsTransformer = new();
	private readonly DynamixBitmapArrayTransformer _dbaTransformer = new();
	private readonly DynamixPaletteTransformer _dplTransformer = new();
	private readonly HercSimDataTransformer _hercSimDataTransformer = new();
	private readonly BasesDgsTransformer _dgsTransformer = new();

	// For a .DGS library, the record index of each part, so the selector names a part by the index
	// BASES.DAT uses for it. Null for a .DTS, whose parts are just numbered.
	private int[]? _recordIndices;

	// Kept around (rather than discarded after the initial Build()) so the Detail Level combo can
	// re-walk a single root's own tree on demand — see RefreshDetailLevelSelector/
	// OnDetailLevelSelectionChanged.
	private DynamixThreeSpaceModel? _currentModel;
	private string? _loadedDisplayName;

	// Set by LoadFromVolEntry, cleared by the loose-file OnOpenDts path — lets the texture
	// bank/palette selectors list that VOL's own entries directly instead of only offering a
	// filesystem "Browse..." item, since most real usage is browsing an already-loaded VOL, not
	// separately-extracted loose files.
	private Voln? _sourceVol;

	private DynamixBitmapArray? _loadedTextureBank;
	private DynamixPalette? _loadedPalette;
	private string? _loadedTextureBankName;
	private string? _loadedPaletteName;
	private TerrainRampFile? _loadedRamp;
	private string? _loadedRampName;
	private readonly TerrainRampFileTransformer _rmpTransformer = new();

	public Model3DViewerForm() {
		InitializeComponent();
		SetRenderMode(Model3DRenderMode.ShadedWireframe);
	}

	private void OnClose(object? sender, EventArgs e) => Close();

	private void OnResetView(object? sender, EventArgs e) => _viewerControl.ResetView();

	private void OnShowKeyboardShortcuts(object? sender, EventArgs e) {
		MessageBox.Show(this,
			"Left / Right — Previous / Next part\n" +
			"Up / Down — Previous / Next detail level (when the current part has more than one)\n\n" +
			"Mouse drag — Orbit camera (yaw / pitch)\n" +
			"Mouse wheel — Zoom\n" +
			"Middle-drag — Pan camera",
			"Keyboard Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
	}

	private void OnShadedMode(object? sender, EventArgs e) => SetRenderMode(Model3DRenderMode.Shaded);

	private void OnWireframeMode(object? sender, EventArgs e) => SetRenderMode(Model3DRenderMode.Wireframe);

	private void OnShadedWireframeMode(object? sender, EventArgs e) => SetRenderMode(Model3DRenderMode.ShadedWireframe);

	private void SetRenderMode(Model3DRenderMode mode) {
		_viewerControl.RenderMode = mode;
		_shadedModeMenuItem.Checked = mode == Model3DRenderMode.Shaded;
		_wireframeModeMenuItem.Checked = mode == Model3DRenderMode.Wireframe;
		_shadedWireframeModeMenuItem.Checked = mode == Model3DRenderMode.ShadedWireframe;
		_viewerControl.Invalidate();
	}

	/// <summary>
	/// Distinct per-dialog identity — see CampaignResourcesForm's DialogClientGuid for the full
	/// explanation.
	/// </summary>
	private static readonly Guid OpenDtsClientGuid = new("ac716302-ad6d-426b-a513-e488ebe755ea");

	private void OnOpenDts(object? sender, EventArgs e) {
		using var dialog = new OpenFileDialog {
			Filter = "3D Model files (*.dts;*.dgs)|*.dts;*.dgs|DTS 3D Model files (*.dts)|*.dts|"
				+ "DGS structure libraries (*.dgs)|*.dgs|All files (*.*)|*.*",
			Title = "Open 3D model",
			ClientGuid = OpenDtsClientGuid
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			byte[] rawBytes = File.ReadAllBytes(dialog.FileName);
			var prefix = VolEntryPrefixCodec.StripIfPresent(rawBytes);

			bool library = string.Equals(Path.GetExtension(dialog.FileName), ".dgs", StringComparison.OrdinalIgnoreCase);
			var model = ParseModel(prefix.Content, library);
			if (model == null) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			// Loose file, not from a loaded VOL — Load Texture Bank/Palette fall back to their own
			// OpenFileDialog rather than offering a (nonexistent) VOL-folder dropdown.
			_sourceVol = null;
			ApplyModel(model, Path.GetFileName(dialog.FileName));
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load DTS file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// Loads a DTS model still packed inside a loaded VOL — e.g. from MainForm's "View Asset"
	/// button. Unlike the loose-file OpenFileDialog path above, VolEntry.RawBytes is already clean
	/// content (VolFileReader strips the 9-byte per-entry prefix at parse time), so no
	/// VolEntryPrefixCodec step is needed here. sourceVol is kept so Load Texture Bank/Load Palette
	/// can offer a dropdown of that VOL's own .dba/.dpl entries instead of only a filesystem picker.
	/// </summary>
	public void LoadFromVolEntry(VolEntry entry, Voln sourceVol) {
		try {
			var model = ParseModel(entry.RawBytes, entry.Ext == FileType.Dgs);
			if (model == null) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			_sourceVol = sourceVol;
			ApplyModel(model, entry.FileName ?? "(DTS asset)");
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load DTS file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// Parses a .DTS, or a .DGS library into a model whose roots are its records' DTS objects, which
	/// is all the rest of the viewer needs. Sets <see cref="_recordIndices"/> to match. A record with
	/// no geometry is left out; no retail record lacks it.
	/// </summary>
	private DynamixThreeSpaceModel? ParseModel(byte[]? content, bool library) {
		if (!library) {
			var model = (DynamixThreeSpaceModel?)_dtsTransformer.Parse(content);
			if (model != null) {
				_recordIndices = null;
			}
			return model;
		}

		if (_dgsTransformer.Parse(content) is not GridShapeLibrary { Shapes: { } shapes }) {
			return null;
		}

		var roots = new List<TSObject>();
		var indices = new List<int>();
		for (int i = 0; i < shapes.Length; i++) {
			if (shapes[i].Geometry is { } geometry) {
				roots.Add(geometry);
				indices.Add(i);
			}
		}

		_recordIndices = indices.ToArray();
		return new DynamixThreeSpaceModel { Roots = roots };
	}

	/// <summary>How the selector and the status line name a part.</summary>
	private string PartName(int partIndex) =>
		_recordIndices != null && partIndex < _recordIndices.Length
			? $"Record {_recordIndices[partIndex]}"
			: $"Part {partIndex}";

	private static readonly Guid OpenTextureBankClientGuid = new("2c9a2e9f-4b1a-4b6a-9e3a-6f6e0a2e6a1d");
	private static readonly Guid OpenTexturePaletteClientGuid = new("7d4e8c2a-1f9b-4a3d-8c7e-2b5f9d1e4a6c");

	/// <summary>Trailing entry in both selectors — picking it opens a filesystem OpenFileDialog.</summary>
	private const string BrowseSelectorItemText = "Browse...";

	// Mirror of each selector's non-Browse items, in display order, so a SelectedIndex maps back
	// to the VolEntry it came from.
	private List<VolEntry> _textureBankCandidates = new();
	private List<VolEntry> _paletteCandidates = new();

	/// <summary>
	/// Lists every .dba entry actually in the loaded VOL (when there is one) directly in the
	/// combo box — picking an entry loads it immediately. Previously this button opened
	/// VolEntryPickerForm, a modal dialog that was itself just a dropdown + OK button; putting the
	/// same dropdown straight on the panel removes that pointless extra step. "Browse..." is
	/// always the last item and falls back to a filesystem OpenFileDialog, covering both the
	/// no-source-VOL case (a loose .dts opened via File > Open DTS) and picking a texture bank
	/// from outside the loaded VOL entirely.
	/// </summary>
	private void PopulateTextureBankSelector() {
		_textureBankSelector.SelectedIndexChanged -= OnTextureBankSelectionChanged;
		_textureBankSelector.Items.Clear();

		_textureBankCandidates = _sourceVol != null
			? FindVolEntries(_sourceVol, FileType.Dba).OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase).ToList()
			: new List<VolEntry>();

		foreach (var entry in _textureBankCandidates) {
			_textureBankSelector.Items.Add(entry.FileName ?? "(unnamed)");
		}
		_textureBankSelector.Items.Add(BrowseSelectorItemText);

		_textureBankSelector.SelectedIndexChanged += OnTextureBankSelectionChanged;
	}

	/// <summary>Same idea as PopulateTextureBankSelector, for .dpl palettes.</summary>
	private void PopulateTexturePaletteSelector() {
		_texturePaletteSelector.SelectedIndexChanged -= OnTexturePaletteSelectionChanged;
		_texturePaletteSelector.Items.Clear();

		_paletteCandidates = _sourceVol != null
			? FindVolEntries(_sourceVol, FileType.Dpl).OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase).ToList()
			: new List<VolEntry>();

		foreach (var entry in _paletteCandidates) {
			_texturePaletteSelector.Items.Add(entry.FileName ?? "(unnamed)");
		}
		_texturePaletteSelector.Items.Add(BrowseSelectorItemText);

		_texturePaletteSelector.SelectedIndexChanged += OnTexturePaletteSelectionChanged;
	}

	/// <summary>
	/// Reflects _loadedTextureBankName/_loadedPaletteName back onto the selectors after a load —
	/// picking a named VOL entry lands on that same entry (an idempotent no-op, since it's already
	/// selected), while "Browse..." resolves to whichever candidate (if any) matches the loaded
	/// file's name, or blank otherwise, instead of staying stuck showing "Browse...".
	/// </summary>
	private void SyncTextureSelectorsToLoadedState() {
		SelectMatchingItem(_textureBankSelector, _textureBankCandidates, _loadedTextureBankName, OnTextureBankSelectionChanged);
		SelectMatchingItem(_texturePaletteSelector, _paletteCandidates, _loadedPaletteName, OnTexturePaletteSelectionChanged);
	}

	private static void SelectMatchingItem(ComboBox combo, List<VolEntry> candidates, string? loadedName, EventHandler handler) {
		int index = loadedName != null
			? candidates.FindIndex(c => string.Equals(c.FileName, loadedName, StringComparison.OrdinalIgnoreCase))
			: -1;

		combo.SelectedIndexChanged -= handler;
		combo.SelectedIndex = index;
		combo.SelectedIndexChanged += handler;
	}

	private void OnTextureBankSelectionChanged(object? sender, EventArgs e) {
		int index = _textureBankSelector.SelectedIndex;
		if (index < 0) {
			return;
		}

		if (index == _textureBankCandidates.Count) {
			BrowseForTextureBank();
		} else {
			LoadTextureBank(_textureBankCandidates[index].RawBytes, _textureBankCandidates[index].FileName);
		}

		SyncTextureSelectorsToLoadedState();
	}

	private void OnTexturePaletteSelectionChanged(object? sender, EventArgs e) {
		int index = _texturePaletteSelector.SelectedIndex;
		if (index < 0) {
			return;
		}

		if (index == _paletteCandidates.Count) {
			BrowseForPalette();
		} else {
			LoadPalette(_paletteCandidates[index].RawBytes, _paletteCandidates[index].FileName);
		}

		SyncTextureSelectorsToLoadedState();
	}

	private void BrowseForTextureBank() {
		using var dialog = new OpenFileDialog {
			Filter = "Dynamix Bitmap Array files (*.dba)|*.dba|All files (*.*)|*.*",
			Title = "Open DBA texture bank",
			ClientGuid = OpenTextureBankClientGuid
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		byte[] rawBytes = VolEntryPrefixCodec.StripIfPresent(File.ReadAllBytes(dialog.FileName)).Content;
		LoadTextureBank(rawBytes, Path.GetFileName(dialog.FileName));
	}

	private void BrowseForPalette() {
		using var dialog = new OpenFileDialog {
			Filter = "Dynamix palette files (*.dpl)|*.dpl|All files (*.*)|*.*",
			Title = "Open DPL palette file",
			ClientGuid = OpenTexturePaletteClientGuid
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		byte[] rawBytes = VolEntryPrefixCodec.StripIfPresent(File.ReadAllBytes(dialog.FileName)).Content;
		LoadPalette(rawBytes, Path.GetFileName(dialog.FileName), dialog.FileName);
	}

	/// <summary>Triggers RebuildTexturedRoots on success so the newly picked bank actually shows up in the render.</summary>
	private void LoadTextureBank(byte[]? rawBytes, string? label) {
		if (rawBytes is not { Length: > 0 }) {
			return;
		}

		try {
			var bank = (DynamixBitmapArray?)_dbaTransformer.Parse(rawBytes);
			if (bank?.Images is not { Length: > 0 }) {
				MessageBox.Show(this, "File was empty or could not be parsed as a DBA.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			_loadedTextureBank = bank;
			_loadedTextureBankName = label;
			RebuildTexturedRoots();
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load DBA file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void LoadPalette(byte[]? rawBytes, string? label, string? loosePath = null) {
		if (rawBytes is not { Length: > 0 }) {
			return;
		}

		try {
			var palette = (DynamixPalette?)_dplTransformer.Parse(rawBytes);
			if (palette == null) {
				MessageBox.Show(this, "File was empty or could not be parsed as a DPL.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			_loadedPalette = palette;
			_loadedPaletteName = label;
			LoadMatchingRamp(loosePath);
			RebuildTexturedRoots();
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load palette:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// A theater's ramp shares its palette's basename — <c>rmp\WORLD2.RMP</c> beside
	/// <c>dpl\WORLD2.DPL</c>, the pairing the engine's theater loader uses. Looked for in the source
	/// VOL, or for a loose palette beside it and in a sibling <c>rmp</c> folder. Silent when absent:
	/// the solid, shaded and textured chains then keep placeholders.
	/// </summary>
	private void LoadMatchingRamp(string? loosePalettePath) {
		_loadedRamp = null;
		_loadedRampName = null;

		string? baseName = _loadedPaletteName != null ? Path.GetFileNameWithoutExtension(_loadedPaletteName) : null;
		if (baseName == null) {
			return;
		}

		byte[]? bytes = null;
		string? name = null;
		if (_sourceVol != null && FindVolEntries(_sourceVol, FileType.Rmp).FirstOrDefault(e =>
				string.Equals(Path.GetFileNameWithoutExtension(e.FileName ?? ""), baseName, StringComparison.OrdinalIgnoreCase))
				is { } entry) {
			bytes = entry.RawBytes;
			name = entry.FileName;
		} else if (loosePalettePath != null && Path.GetDirectoryName(loosePalettePath) is { } folder) {
			var candidates = new[] {
				Path.Combine(folder, baseName + ".RMP"),
				Path.Combine(Path.GetDirectoryName(folder) ?? folder, "rmp", baseName + ".RMP")
			};
			if (candidates.FirstOrDefault(File.Exists) is { } path) {
				bytes = VolEntryPrefixCodec.StripIfPresent(File.ReadAllBytes(path)).Content;
				name = Path.GetFileName(path);
			}
		}

		try {
			if (bytes is { Length: > 0 } && _rmpTransformer.Parse(bytes) is TerrainRampFile ramp) {
				_loadedRamp = ramp;
				_loadedRampName = name;
			}
		} catch {
			// Best-effort, like the palette default.
		}
	}

	/// <summary>
	/// Everything the builder colours with. Index 0 is a cutout in every bank but the seven mech
	/// skins, which is how the engine decodes them (it treats structures, projectiles and effects
	/// as transparent banks).
	/// </summary>
	private ShapeRenderContext RenderContext() {
		string? bankBase = _loadedTextureBankName != null ? Path.GetFileNameWithoutExtension(_loadedTextureBankName) : null;
		bool mechSkin = bankBase != null && Enumerable.Range(0, 7)
			.Any(id => string.Equals(HercSimDat.TextureGroupDbaBaseName((short)id), bankBase, StringComparison.OrdinalIgnoreCase));
		return new ShapeRenderContext(_loadedTextureBank, _loadedPalette, _loadedRamp, TransparentIndex0: !mechSkin);
	}

	private static List<VolEntry> FindVolEntries(Voln vol, FileType ext) {
		var result = new List<VolEntry>();
		foreach (var folder in vol.Folders.Values) {
			foreach (var entry in folder.Files) {
				if (entry.Ext == ext && entry.RawBytes is { Length: > 0 }) {
					result.Add(entry);
				}
			}
		}
		return result;
	}

	/// <summary>
	/// Mech textures in simvol0/dba share a handful of generic simvol0/dpl palettes (WORLD0-9 — see
	/// TextureViewerForm's doc comment), none named after the mech itself, so there's no reliable
	/// per-model auto-match the way there is for some other asset types. WORLD0 is a reasonable
	/// good-enough default so the viewer shows *something* textured without forcing a manual Load
	/// Palette click first — Load Palette remains available to override.
	/// </summary>
	private const string DefaultPaletteBaseName = "WORLD0";

	private void ApplyModel(DynamixThreeSpaceModel model, string displayName) {
		_currentModel = model;
		_loadedDisplayName = displayName;

		if (_loadedTextureBank == null) {
			TryLoadDefaultTextureBank();
		}
		if (_loadedPalette == null) {
			TryLoadDefaultPalette();
		}

		var roots = DtsGeometryBuilder.Build(model, RenderContext());
		_viewerControl.LoadMeshes(roots);
		PopulatePartSelector(roots);

		PopulateTextureBankSelector();
		PopulateTexturePaletteSelector();
		SyncTextureSelectorsToLoadedState();
	}

	/// <summary>
	/// Silent best-effort default (see DefaultPaletteBaseName) — only runs for VOL-sourced loads,
	/// since there's no VOL to search for a loose .dts opened via File > Open DTS. Any failure
	/// (missing entry, bad bytes) just leaves _loadedPalette null, same as before this existed; only
	/// an explicit Load Palette click surfaces a real error dialog.
	/// </summary>
	private void TryLoadDefaultPalette() {
		if (_sourceVol == null) {
			return;
		}

		var candidate = FindVolEntries(_sourceVol, FileType.Dpl).FirstOrDefault(e =>
			string.Equals(Path.GetFileNameWithoutExtension(e.FileName ?? ""), DefaultPaletteBaseName,
				StringComparison.OrdinalIgnoreCase));
		if (candidate?.RawBytes is not { Length: > 0 } rawBytes) {
			return;
		}

		try {
			_loadedPalette = (DynamixPalette?)_dplTransformer.Parse(rawBytes);
			_loadedPaletteName = _loadedPalette != null ? candidate.FileName : null;
			LoadMatchingRamp(null);
		} catch {
			// Best-effort default — swallow and leave _loadedPalette null.
		}
	}

	/// <summary>
	/// Auto-selects the correct DBA texture bank for a mech model, instead of requiring a manual
	/// "Load Texture Bank" click — the exe itself resolves this per mech type (see
	/// HercSimDat.TextureGroup's doc comment for the confirming Ghidra RE). Looks for a same-basename
	/// simvol0/dat/&lt;mech&gt;.DAT alongside the loaded .dts in the same VOL, reads its TextureGroup,
	/// and maps that to one of the 7 shared atlas DBAs (light/medium/heavy/enemy/apocatex/razortex/
	/// newhercs). Silent best-effort, same convention as TryLoadDefaultPalette: only runs for
	/// VOL-sourced loads, and any failure (no matching .DAT, unmapped TextureGroup, missing .DBA)
	/// just leaves _loadedTextureBank null so the model still renders with the flat placeholder
	/// color — Load Texture Bank remains available as a manual override or fallback. A .DGS
	/// library takes <see cref="StructureBankName"/> instead.
	/// </summary>
	private void TryLoadDefaultTextureBank() {
		if (_sourceVol == null || _loadedDisplayName == null) {
			return;
		}

		if (_recordIndices != null) {
			TryLoadTextureBankNamed(StructureBankName);
			return;
		}

		string mechBaseName = Path.GetFileNameWithoutExtension(_loadedDisplayName);
		var datEntry = FindVolEntries(_sourceVol, FileType.Dat).FirstOrDefault(e =>
			string.Equals(Path.GetFileNameWithoutExtension(e.FileName ?? ""), mechBaseName,
				StringComparison.OrdinalIgnoreCase));
		if (datEntry?.RawBytes is not { Length: > 0 } datBytes) {
			return;
		}

		string? groupName;
		try {
			if (_hercSimDataTransformer.Parse(datBytes) is not HercSimDat simData) {
				return;
			}
			groupName = HercSimDat.TextureGroupDbaBaseName(simData.TextureGroup);
		} catch {
			return;
		}
		if (groupName == null) {
			return;
		}

		TryLoadTextureBankNamed(groupName);
	}

	/// <summary>The bank the structure libraries default to — see the class summary.</summary>
	private const string StructureBankName = "BASETEX";

	/// <summary>Loads the source VOL's .DBA of this basename, silently leaving the bank unset on any failure.</summary>
	private void TryLoadTextureBankNamed(string baseName) {
		if (_sourceVol == null) {
			return;
		}

		var dbaEntry = FindVolEntries(_sourceVol, FileType.Dba).FirstOrDefault(e =>
			string.Equals(Path.GetFileNameWithoutExtension(e.FileName ?? ""), baseName,
				StringComparison.OrdinalIgnoreCase));
		if (dbaEntry?.RawBytes is not { Length: > 0 } dbaBytes) {
			return;
		}

		try {
			var bank = (DynamixBitmapArray?)_dbaTransformer.Parse(dbaBytes);
			if (bank?.Images is not { Length: > 0 }) {
				return;
			}
			_loadedTextureBank = bank;
			_loadedTextureBankName = dbaEntry.FileName;
		} catch {
			// Best-effort default — swallow and leave _loadedTextureBank null.
		}
	}

	/// <summary>
	/// Re-resolves every currently-loaded root's geometry against the current texture bank/palette
	/// — called after Load Texture Bank/Load Palette so a newly-picked DBA actually shows up,
	/// without resetting the camera or losing the current Part/Detail Level selection (ReplaceRoot
	/// preserves both). Each root keeps its own already-chosen detail level except the currently
	/// selected one, which re-reads it from the Detail Level combo in case the user has one picked.
	/// </summary>
	private void RebuildTexturedRoots() {
		if (_currentModel?.Roots is not { } meshes) {
			return;
		}

		int selectedPart = _partSelector.SelectedIndex;
		int? selectedLod = _lodSelector.Visible && _lodSelector.SelectedIndex >= 0 ? _lodSelector.SelectedIndex : null;

		for (int i = 0; i < meshes.Count && i < _viewerControl.Roots.Count; i++) {
			int? lodIndex = i == selectedPart ? selectedLod : null;
			string label = _viewerControl.Roots[i].Mesh.Label;
			var rebuilt = DtsGeometryBuilder.BuildRoot(meshes[i], label, lodIndex, RenderContext());
			_viewerControl.ReplaceRoot(i, rebuilt);
		}

		UpdateStatusForCurrentSelection();
	}

	/// <summary>
	/// A DTS file's top-level entries are each independent objects with no in-file signal for how
	/// they relate to each other (see DtsGeometryBuilder's doc comment) — showing more than one at
	/// once produces garbled overlapping geometry regardless of whether they're really LOD levels
	/// of one thing or several unrelated objects sharing the file. So this always shows exactly
	/// one, defaulting to the first, and lets the user step through the rest one at a time.
	/// </summary>
	private void PopulatePartSelector(List<DtsRootMesh> roots) {
		_partSelector.SelectedIndexChanged -= OnPartSelectionChanged;

		_partSelector.Items.Clear();
		for (int i = 0; i < roots.Count; i++) {
			_partSelector.Items.Add($"{PartName(i)} — {roots[i].Triangles.Count} triangles");
		}

		_partSelector.SelectedIndexChanged += OnPartSelectionChanged;

		bool canNavigate = _partSelector.Items.Count > 1;
		_prevPartButton.Enabled = canNavigate;
		_nextPartButton.Enabled = canNavigate;

		if (_partSelector.Items.Count > 0) {
			_partSelector.SelectedIndex = 0;
		} else {
			RefreshDetailLevelSelector();
		}
	}

	private void OnPreviousPart(object? sender, EventArgs e) => NavigatePart(-1);

	private void OnNextPart(object? sender, EventArgs e) => NavigatePart(1);

	/// <summary>Wraps at both ends: Next from the last part goes to the first, and vice versa.</summary>
	private void NavigatePart(int direction) {
		int count = _partSelector.Items.Count;
		if (count <= 1) {
			return;
		}

		int current = Math.Max(_partSelector.SelectedIndex, 0);
		_partSelector.SelectedIndex = ((current + direction) % count + count) % count;
	}

	/// <summary>
	/// Same idea as NavigatePart but for the Detail Level combo — no dedicated buttons for this
	/// one, keyboard-only. A no-op (not an error) when the current Part has no real detail-level
	/// choice, matching NavigatePart's own "nothing to navigate" guard.
	/// </summary>
	private void NavigateDetailLevel(int direction) {
		int count = _lodSelector.Items.Count;
		if (!_lodSelector.Visible || count <= 1) {
			return;
		}

		int current = Math.Max(_lodSelector.SelectedIndex, 0);
		_lodSelector.SelectedIndex = ((current + direction) % count + count) % count;
	}

	/// <summary>
	/// Left/Right navigate parts, Up/Down cycle the current part's detail levels — both regardless
	/// of which control has focus, including the combos themselves (DropDownList-style, so there's
	/// no text cursor for Left/Right to move, and Up/Down would otherwise just silently do nothing
	/// when the dropdown is closed). Up/Down deliberately fall through to normal handling while
	/// either combo's dropdown is actually open (DroppedDown), so the native "arrow keys browse the
	/// open list" behavior still works instead of being hijacked out from under it.
	/// </summary>
	protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
		if (keyData == Keys.Left) {
			NavigatePart(-1);
			return true;
		}
		if (keyData == Keys.Right) {
			NavigatePart(1);
			return true;
		}
		if ((keyData == Keys.Up || keyData == Keys.Down) && !_partSelector.DroppedDown && !_lodSelector.DroppedDown) {
			NavigateDetailLevel(keyData == Keys.Up ? -1 : 1);
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	private void OnPartSelectionChanged(object? sender, EventArgs e) {
		int selected = _partSelector.SelectedIndex;
		for (int i = 0; i < _viewerControl.Roots.Count; i++) {
			_viewerControl.SetRootVisible(i, i == selected);
		}

		RefreshDetailLevelSelector();
	}

	/// <summary>
	/// DTS does have a real in-file LOD mechanism (TSDetailPart — see DtsGeometryBuilder's doc
	/// comment), but it's per-part, not global: different Parts can have a different number of
	/// detail levels, or none at all. So this dropdown is rebuilt every time the selected Part
	/// changes, scoped to that Part's own TSObject tree, and hidden entirely when that Part has
	/// one level or fewer — showing an always-visible combo with nothing meaningful to pick would
	/// be misleading. Defaults to the highest level, matching DtsGeometryBuilder.Build()'s default.
	/// </summary>
	private void RefreshDetailLevelSelector() {
		_lodSelector.SelectedIndexChanged -= OnDetailLevelSelectionChanged;
		_lodSelector.Items.Clear();

		int partIndex = _partSelector.SelectedIndex;
		int levelCount = _currentModel?.Roots is { } meshes && partIndex >= 0 && partIndex < meshes.Count
			? DtsGeometryBuilder.GetDetailLevelCount(meshes[partIndex])
			: 0;

		bool hasChoice = levelCount > 1;
		_lodLabel.Visible = hasChoice;
		_lodSelector.Visible = hasChoice;

		if (hasChoice) {
			for (int i = 0; i < levelCount; i++) {
				_lodSelector.Items.Add($"Detail {i}");
			}
		}

		_lodSelector.SelectedIndexChanged += OnDetailLevelSelectionChanged;

		if (hasChoice) {
			_lodSelector.SelectedIndex = levelCount - 1;
		} else {
			UpdateStatusForCurrentSelection();
		}
	}

	private void OnDetailLevelSelectionChanged(object? sender, EventArgs e) {
		int partIndex = _partSelector.SelectedIndex;
		int lodIndex = _lodSelector.SelectedIndex;
		if (_currentModel?.Roots is not { } meshes || partIndex < 0 || partIndex >= meshes.Count || lodIndex < 0) {
			return;
		}

		var rebuilt = DtsGeometryBuilder.BuildRoot(meshes[partIndex], PartName(partIndex), lodIndex, RenderContext());
		_viewerControl.ReplaceRoot(partIndex, rebuilt);
		UpdateStatusForCurrentSelection();
	}

	private void UpdateStatusForCurrentSelection() {
		int partIndex = _partSelector.SelectedIndex;
		if (partIndex < 0 || partIndex >= _viewerControl.Roots.Count) {
			_statusLabel.Text = "No model loaded.";
			return;
		}

		int triangleCount = _viewerControl.Roots[partIndex].Mesh.Triangles.Count;
		string lodNote = _lodSelector.Visible && _lodSelector.SelectedIndex >= 0
			? $", detail level {_lodSelector.SelectedIndex} of {_lodSelector.Items.Count}"
			: "";
		string paletteNote = _loadedPaletteName != null
			? $" Palette {_loadedPaletteName}, ramp {_loadedRampName ?? "not found (flat and textured colours are placeholders)"}."
			: " No palette (all colours are placeholders).";
		string textureNote = _loadedTextureBankName != null
			? $" Texture bank {_loadedTextureBankName} ({_loadedTextureBank?.Images?.Length ?? 0} frames)."
			: "";
		textureNote = paletteNote + textureNote;
		_statusLabel.Text =
			$"Loaded {_loadedDisplayName} — {PartName(partIndex).ToLowerInvariant()} ({partIndex + 1} of {_viewerControl.Roots.Count}){lodNote}, {triangleCount} triangle(s).{textureNote}";
	}
}
