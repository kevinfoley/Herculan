using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;

namespace HercWorks.UI;

/// <summary>
/// View-only window for DBA/DBM textures — ImageExportForm already covers PNG export, so this is
/// just preview + palette selection. DBA/DBM never bind their own palette (see DynamixBitmap's doc
/// comment: "the game binaries seem to know which one has which"), and empirically there's no
/// reliable same-name .DPL for most textures (e.g. mech textures in simvol0/dba share a handful of
/// generic simvol0/dpl palettes like WORLD0-9, none named after the mech) — so "automatic"
/// detection is a best-effort same-basename match, falling back to a dropdown of every .DPL found
/// nearby (same loaded VOL, or a sibling "dpl" folder for loose files) plus a manual Open Palette
/// escape hatch.
/// </summary>
public partial class TextureViewerForm : Form {
	private readonly DynamixBitmapArrayTransformer _dbaTransformer = new();
	private readonly DynamixBitmapTransformer _dbmTransformer = new();
	private readonly DynamixPaletteTransformer _dplTransformer = new();

	// Exactly one of these is populated after a successful load.
	private DynamixBitmapArray? _loadedDba;
	private DynamixBitmap? _loadedDbm;

	private DynamixPalette? _loadedPalette;
	private byte[]? _loadedPaletteRawBytes;
	private readonly List<(string Label, byte[] RawBytes)> _paletteCandidates = new();

	private int _paletteOffset = 0;

	public TextureViewerForm() {
		InitializeComponent();
		_previewPanel.Resize += (_, _) => PositionPreviewInPanel();
		_paletteOffsetSlider.ValueChanged += (_, _) => OnPaletteOffsetChanged();
	}

	private void OnPaletteOffsetChanged() {
		_paletteOffset = _paletteOffsetSlider.Value;
		_offsetLabel.Text = $"Palette Offset: {_paletteOffset}";
		RenderCurrentFrame();
	}

	private void OnClose(object? sender, EventArgs e) => Close();

	private void OnFrameChanged(object? sender, EventArgs e) => RenderCurrentFrame();

	private void OnShowKeyboardShortcuts(object? sender, EventArgs e) {
		MessageBox.Show(this,
			"Left / Right — Previous / Next frame\n" +
			"Up / Down — Previous / Next palette",
			"Keyboard Shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Information);
	}

	/// <summary>
	/// Left/Right step through frames, Up/Down step through palettes — both regardless of which
	/// control has focus. Up/Down deliberately fall through to normal handling while the palette
	/// combo's dropdown is actually open, so the native "arrow keys browse the open list" behavior
	/// still works instead of being hijacked out from under it (same guard as Model3DViewerForm's
	/// Part/Detail Level navigation).
	/// </summary>
	protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
		if (keyData == Keys.Left) {
			NavigateFrame(-1);
			return true;
		}
		if (keyData == Keys.Right) {
			NavigateFrame(1);
			return true;
		}
		if ((keyData == Keys.Up || keyData == Keys.Down) && !_paletteSelector.DroppedDown) {
			NavigatePalette(keyData == Keys.Up ? -1 : 1);
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	/// <summary>Wraps at both ends, matching Model3DViewerForm.NavigatePart's convention.</summary>
	private void NavigateFrame(int direction) {
		var frames = Frames;
		if (frames == null || frames.Length <= 1) {
			return;
		}

		int current = (int)_frameSelector.Value;
		_frameSelector.Value = ((current + direction) % frames.Length + frames.Length) % frames.Length;
	}

	/// <summary>Wraps at both ends, matching NavigateFrame above.</summary>
	private void NavigatePalette(int direction) {
		int count = _paletteSelector.Items.Count;
		if (count <= 1) {
			return;
		}

		int current = Math.Max(_paletteSelector.SelectedIndex, 0);
		_paletteSelector.SelectedIndex = ((current + direction) % count + count) % count;
	}

	private DynamixBitmap[]? Frames => _loadedDba?.Images ?? (_loadedDbm != null ? new[] { _loadedDbm } : null);

	/// <summary>
	/// Distinct per-purpose identities — see CampaignResourcesForm's DialogClientGuid for the full
	/// explanation. Kept separate from ImageExportForm's own GUIDs for the same file types since
	/// each is its own independent entry point with its own last-visited-folder expectation.
	/// </summary>
	private static readonly Guid OpenImageClientGuid = new("0b195bb6-3cf3-403e-a830-90f4ad962fcd");
	private static readonly Guid OpenPaletteClientGuid = new("f59f5bee-1b59-439d-96e6-e06514e8691c");

	private void OnOpenImage(object? sender, EventArgs e) {
		using var dialog = new OpenFileDialog {
			Filter = "Dynamix bitmap files (*.dba;*.dbm)|*.dba;*.dbm|All files (*.*)|*.*",
			Title = "Open DBA or DBM file",
			ClientGuid = OpenImageClientGuid
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			byte[] rawBytes = File.ReadAllBytes(dialog.FileName);
			var prefix = VolEntryPrefixCodec.StripIfPresent(rawBytes);

			if (prefix.Content.Length < 4) {
				MessageBox.Show(this, "File is too short to be a valid DBA/DBM.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			byte[] fileHeader = prefix.Content[..4];
			bool isArray = fileHeader.SequenceEqual(DynamixBitmapArray.HeaderMagic);
			bool isSingle = fileHeader.SequenceEqual(DynamixBitmap.HeaderMagic);

			if (!isArray && !isSingle) {
				MessageBox.Show(this,
					"File header doesn't match a known DBA or DBM magic value — this may not be one of those formats.",
					"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			if (isArray) {
				_loadedDba = (DynamixBitmapArray?)_dbaTransformer.Parse(prefix.Content);
				_loadedDbm = null;
			} else {
				_loadedDbm = (DynamixBitmap?)_dbmTransformer.Parse(prefix.Content);
				_loadedDba = null;
			}

			if (Frames is not { Length: > 0 }) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			LoadPaletteCandidatesFromDisk(dialog.FileName);
			FinishLoad(Path.GetFileName(dialog.FileName), dialog.FileName, PreferredPaletteFor(Path.GetExtension(dialog.FileName)));
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// Loads a texture still packed inside a loaded VOL — e.g. from MainForm's "View Asset"
	/// button. VolEntry.RawBytes is already clean content (no per-entry prefix to strip), and
	/// entry.Ext reliably says which format it is (VOL folders are organized by extension), so
	/// this skips the magic-byte sniffing the loose-file path needs.
	/// </summary>
	public void LoadFromVolEntry(VolEntry entry, Voln sourceVol) {
		try {
			if (entry.RawBytes is not { Length: > 0 }) {
				MessageBox.Show(this, "File has no data to parse.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			// .HBA, .HB0/.HB1/.HB2, and .DB0/.DB1/.DB2 are all byte-identical to the .DBA container
			// format (see TransformerRegistry's doc comment) — same transformer handles all of them.
			bool isDbaLike = entry.Ext is FileType.Dba or FileType.Hba or FileType.Hb0 or FileType.Hb1 or FileType.Hb2
				or FileType.Db0 or FileType.Db1 or FileType.Db2;

			if (isDbaLike) {
				_loadedDba = (DynamixBitmapArray?)_dbaTransformer.Parse(entry.RawBytes);
				_loadedDbm = null;
			} else if (entry.Ext == FileType.Dbm) {
				_loadedDbm = (DynamixBitmap?)_dbmTransformer.Parse(entry.RawBytes);
				_loadedDba = null;
			} else {
				MessageBox.Show(this, "Selected entry is not a DBA, DBM, HBA, HB0/HB1/HB2, or DB0/DB1/DB2 texture.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			if (Frames is not { Length: > 0 }) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			LoadPaletteCandidatesFromVol(sourceVol);
			string? preferredPalette = entry.Ext is FileType.Hba or FileType.Hb0 or FileType.Hb1 or FileType.Hb2
				or FileType.Db0 or FileType.Db1 or FileType.Db2 ? "COCKPIT" : null;
			FinishLoad(entry.FileName ?? "(texture asset)", entry.FileName ?? "", preferredPalette);
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnOpenPalette(object? sender, EventArgs e) {
		using var dialog = new OpenFileDialog {
			Filter = "Dynamix palette files (*.dpl)|*.dpl|All files (*.*)|*.*",
			Title = "Open DPL palette file",
			ClientGuid = OpenPaletteClientGuid
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			byte[] rawBytes = File.ReadAllBytes(dialog.FileName);
			var prefix = VolEntryPrefixCodec.StripIfPresent(rawBytes);
			var palette = (DynamixPalette?)_dplTransformer.Parse(prefix.Content);

			if (palette == null) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			string label = Path.GetFileName(dialog.FileName);
			_paletteCandidates.Add((label, prefix.Content));
			_paletteSelector.Items.Add(label);
			// Triggers OnPaletteSelectionChanged, which stores raw bytes and re-renders.
			_paletteSelector.SelectedIndex = _paletteSelector.Items.Count - 1;
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load palette:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void OnPaletteSelectionChanged(object? sender, EventArgs e) {
		int index = _paletteSelector.SelectedIndex;
		if (index <= 0) {
			_loadedPalette = null;
			_loadedPaletteRawBytes = null;
		} else {
			var candidate = _paletteCandidates[index - 1]; // -1 offsets the leading "(None)"
			_loadedPaletteRawBytes = candidate.RawBytes;
			_loadedPalette = (DynamixPalette?)_dplTransformer.Parse(candidate.RawBytes);
		}

		RenderCurrentFrame();
	}

	private void LoadPaletteCandidatesFromDisk(string textureFilePath) {
		_paletteCandidates.Clear();

		string? dir = Path.GetDirectoryName(textureFilePath);
		if (dir == null) {
			return;
		}

		var searchDirs = new List<string> { dir };

		// Real retail layouts keep DBA and DPL in sibling folders (e.g. simvol0/dba, simvol0/dpl)
		// rather than the same one, so check for that shape too.
		string? parent = Path.GetDirectoryName(dir);
		if (parent != null) {
			string siblingDpl = Path.Combine(parent, "dpl");
			if (Directory.Exists(siblingDpl)) {
				searchDirs.Add(siblingDpl);
			}
		}

		foreach (var searchDir in searchDirs.Distinct(StringComparer.OrdinalIgnoreCase)) {
			foreach (var file in Directory.GetFiles(searchDir, "*.dpl")) {
				byte[] rawBytes = File.ReadAllBytes(file);
				var prefix = VolEntryPrefixCodec.StripIfPresent(rawBytes);
				_paletteCandidates.Add((Path.GetFileName(file), prefix.Content));
			}
		}
	}

	private void LoadPaletteCandidatesFromVol(Voln vol) {
		_paletteCandidates.Clear();

		foreach (var folder in vol.Folders.Values) {
			foreach (var candidate in folder.Files) {
				if (candidate.Ext == FileType.Dpl && candidate.RawBytes is { Length: > 0 }) {
					_paletteCandidates.Add((candidate.FileName ?? "(unnamed)", candidate.RawBytes));
				}
			}
		}
	}

	/// <summary>
	/// .HB0/.HB1/.HB2/.HBA/.DB0/.DB1/.DB2 textures don't bind a same-name palette (they're named
	/// after the herc, not a palette) but empirically always look right under COCKPIT.DPL — all of
	/// them are cockpit interior backgrounds (at 640x480 or 320x240) or cockpit gauge/HUD sprites.
	/// Returns null for every other extension, leaving the existing same-basename auto-match as the
	/// only default.
	/// </summary>
	private static string? PreferredPaletteFor(string? extension) {
		string ext = (extension ?? "").TrimStart('.');
		return ext is "hba" or "hb0" or "hb1" or "hb2" or "db0" or "db1" or "db2" ? "COCKPIT" : null;
	}

	private void FinishLoad(string sourceLabel, string paletteMatchName, string? preferredPaletteBaseName = null) {
		var frames = Frames!;
		_frameSelector.Minimum = 0;
		_frameSelector.Maximum = frames.Length - 1;
		_frameSelector.Value = 0;

		// Reset palette offset when loading a new image.
		_paletteOffset = 0;
		_paletteOffsetSlider.Value = 0;
		_offsetLabel.Text = "Palette Offset: 0";

		bool autoMatched = PopulatePaletteSelector(paletteMatchName, preferredPaletteBaseName);

		string kind = _loadedDba != null ? "DBA" : "DBM";
		string paletteNote = autoMatched
			? " — auto-matched a palette."
			: _paletteCandidates.Count > 0
				? " — no matching palette found automatically, pick one from the dropdown."
				: " — no palettes found nearby to try.";
		_statusLabel.Text = $"Loaded {sourceLabel} ({kind}, {frames.Length} frame(s)){paletteNote}";

		RenderCurrentFrame();
	}

	/// <summary>
	/// Returns true if a palette was found and auto-selected. Tries the preferred palette
	/// first (e.g. "COCKPIT" for HB0/HB1/HB2/HBA — see PreferredPaletteFor), then falls back to a
	/// same-basename-as-texture match (the original DBA/DBM behavior).
	/// </summary>
	/// <param name="textureFileName">The texture's file name.</param>
	/// <param name="preferredPaletteBaseName">The preferred palette's base name, if any.</param>
	private bool PopulatePaletteSelector(string textureFileName, string? preferredPaletteBaseName) {
		_paletteSelector.Items.Clear();
		_paletteSelector.Items.Add("(None)");

		string baseName = Path.GetFileNameWithoutExtension(textureFileName);
		int preferredIndex = 0;
		int sameNameIndex = 0;

		foreach (var candidate in _paletteCandidates) {
			_paletteSelector.Items.Add(candidate.Label);
			int itemIndex = _paletteSelector.Items.Count - 1;
			string candidateBase = Path.GetFileNameWithoutExtension(candidate.Label);

			if (preferredIndex == 0 && preferredPaletteBaseName != null &&
				string.Equals(candidateBase, preferredPaletteBaseName, StringComparison.OrdinalIgnoreCase)) {
				preferredIndex = itemIndex;
			}
			if (sameNameIndex == 0 && string.Equals(candidateBase, baseName, StringComparison.OrdinalIgnoreCase)) {
				sameNameIndex = itemIndex;
			}
		}

		int matchIndex = preferredIndex != 0 ? preferredIndex : sameNameIndex;
		_paletteSelector.SelectedIndex = matchIndex;
		return matchIndex != 0;
	}

	private void RenderCurrentFrame() {
		var frames = Frames;
		if (frames == null || frames.Length == 0) {
			_resolutionStatusLabel.Text = "";
			_preview.Image?.Dispose();
			_preview.Image = null;
			_preview.Size = Size.Empty;
			return;
		}

		int index = Math.Clamp((int)_frameSelector.Value, 0, frames.Length - 1);
		var frame = frames[index];
		var paletteToRender = _paletteOffset != 0 && _loadedPalette != null
			? CreateOffsetPalette(_loadedPalette)
			: _loadedPalette;
		var bitmap = DynamixImageRenderer.RenderFrame(frame, paletteToRender);

		_preview.Image?.Dispose();
		// Clear the picture box before measuring so a scrollbar left over from the previous
		// (possibly larger) frame doesn't shrink _previewPanel.ClientSize and throw off the fit check.
		_preview.Size = Size.Empty;
		GrowWindowToFitImage(bitmap.Size);

		_preview.Image = bitmap;
		_preview.Size = bitmap.Size;
		PositionPreviewInPanel();

		_resolutionStatusLabel.Text = $"{frame.Cols} x {frame.Rows}";
	}

	private DynamixPalette? CreateOffsetPalette(DynamixPalette palette) {
		if (_loadedPaletteRawBytes == null) {
			return palette;
		}

		try {
			// Try to find the palette data within the raw bytes (may have a header).
			// Look for a 256-color palette: 256 * bytesPerColor bytes, where bytesPerColor is 3 or 4.
			int headerSize = 0;
			int bytesPerColor = 0;

			// Try common byte depths: 4 bytes (BGRA), 3 bytes (RGB)
			for (int bpc = 4; bpc >= 3; bpc--) {
				int paletteSize = 256 * bpc;
				if (_loadedPaletteRawBytes.Length >= paletteSize) {
					// Try with no header first
					if (_loadedPaletteRawBytes.Length == paletteSize) {
						headerSize = 0;
						bytesPerColor = bpc;
						break;
					}
					// Try with a header
					int potentialHeader = _loadedPaletteRawBytes.Length - paletteSize;
					if (potentialHeader <= 64) { // Reasonable header size
						headerSize = potentialHeader;
						bytesPerColor = bpc;
						break;
					}
				}
			}

			if (bytesPerColor == 0) {
				return palette; // Couldn't determine format
			}

			// Rotate palette entries by the offset amount.
			int paletteDataSize = 256 * bytesPerColor;
			var rotatedBytes = new byte[_loadedPaletteRawBytes.Length];

			// Copy header unchanged
			Buffer.BlockCopy(_loadedPaletteRawBytes, 0, rotatedBytes, 0, headerSize);

			// Rotate palette data
			for (int i = 0; i < 256; i++) {
				int sourceIndex = (i + _paletteOffset) % 256;
				Buffer.BlockCopy(_loadedPaletteRawBytes, headerSize + sourceIndex * bytesPerColor,
					rotatedBytes, headerSize + i * bytesPerColor, bytesPerColor);
			}

			return (DynamixPalette?)_dplTransformer.Parse(rotatedBytes);
		} catch {
			return palette;
		}
	}

	/// <summary>
	/// Grows the window just enough that the full image fits in the preview panel without
	/// scrollbars. Never shrinks the window — loading a smaller image afterward leaves the current
	/// size alone, and a user who's manually shrunk the window below what the image needs just gets
	/// scrollbars (via _previewPanel's AutoScroll) instead of being resized out from under them.
	/// </summary>
	private void GrowWindowToFitImage(Size imageSize) {
		int widthGrowth = Math.Max(0, imageSize.Width - _previewPanel.ClientSize.Width);
		int heightGrowth = Math.Max(0, imageSize.Height - _previewPanel.ClientSize.Height);
		if (widthGrowth > 0 || heightGrowth > 0) {
			Size = new Size(Width + widthGrowth, Height + heightGrowth);
		}
	}

	/// <summary>
	/// Centers the image in the panel when it's smaller than the visible area; pins it to the
	/// top-left (the natural AutoScroll origin) when it's larger and needs scrollbars.
	/// </summary>
	private void PositionPreviewInPanel() {
		if (_preview.Image == null) {
			return;
		}

		var panelSize = _previewPanel.ClientSize;
		int x = _preview.Width < panelSize.Width ? (panelSize.Width - _preview.Width) / 2 : 0;
		int y = _preview.Height < panelSize.Height ? (panelSize.Height - _preview.Height) / 2 : 0;
		_preview.Location = new Point(x, y);
	}
}
