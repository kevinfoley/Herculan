using System.ComponentModel;
using HercWorks.Core.Data.File.Msn.Script;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;

namespace HercWorks.UI;

/// <summary>
/// Editor for <c>data\script.dat</c> — the file VSHELL writes after parsing a mission's <c>.msn</c>
/// and DBSIM reads to actually build the world, so this is the handoff that decides what a mission
/// contains and where it stands. Backed by HercWorks.Core's ScriptDatTransformer (byte-exact
/// round-trip verified against all 10 real sample files); see docs/retail/formats/script-dat.md for the
/// format itself. Follows the same shape as CampaignResourcesForm: a tab per block, loose-file
/// Open/Save As, layout in MissionScriptForm.Designer.cs. Save As writes the content-only shape
/// DBSIM reads; Save As With VOL Prefix keeps the 9-byte entry prefix of a file that was opened with
/// one (see VolEntryPrefixCodec).
///
/// <para><b>Records are edited in place, not added or removed.</b> Every block past the first two
/// addresses the others by array index — a group's member list indexes the Herc roster, a roster
/// slot's position indexes the point table — so inserting or deleting a record would silently
/// repoint every ref after it. The one exception is the objective-line block, which nothing
/// references and which is therefore rebuilt wholesale from its grid.</para>
///
/// <para>The Hercs tab is master-detail rather than one wide grid: the roster on top, and below it
/// the selected Herc's ten hardpoints one row each, with its weapon and (for launchers only) its
/// ammunition type picked by name — see <see cref="WeaponFitOption"/> and
/// <see cref="AmmoTypeOption"/>. Both loadout arrays are edited there; the roster's own fit column
/// is a read-only summary.</para>
///
/// <para>Herc types are named, via <see cref="HercTypeOption"/>'s HercLUT-to-MECHS.NAM equivalence.
/// Flyer and base types stay raw indexes: their names live in <c>nam\FLYERS.NAM</c> and
/// <c>dat\BASES.DAT</c> inside the game's VOLs, which this form has no loaded VOL to resolve
/// against, and no equivalent hardcoded LUT exists for them.</para>
/// </summary>
public partial class MissionScriptForm : Form {
	private readonly ScriptDatTransformer _transformer = new();

	private readonly BindingList<ScriptPointRow> _pointRows = new();
	private readonly BindingList<ScriptHeadingRow> _headingRows = new();
	private readonly BindingList<ScriptRouteRow> _routeRows = new();
	private readonly BindingList<ScriptTriggerAreaRow> _linkRows = new();
	private readonly BindingList<ScriptActionRow> _actionRows = new();
	private readonly BindingList<ScriptActionTimerRow> _actionTimerRows = new();
	private readonly BindingList<ScriptMechRow> _mechRows = new();
	private readonly BindingList<ScriptWeaponSlotRow> _slotRows = new();
	private readonly BindingList<ScriptFlyerRow> _flyerRows = new();
	private readonly BindingList<ScriptBaseRow> _baseRows = new();
	private readonly BindingList<ScriptOrderRow> _routeLinkRows = new();
	private readonly BindingList<ScriptGroupRow> _groupRows = new();
	private readonly BindingList<ScriptObjectiveRow> _entityLinkRows = new();
	private readonly BindingList<ScriptObjectiveLineRow> _unlockRows = new();

	private ScriptDat? _loaded;
	private GameFile? _loadedFile;

	public MissionScriptForm() {
		InitializeComponent();

		_pointsGrid.DataSource = _pointRows;
		_headingsGrid.DataSource = _headingRows;
		_routesGrid.DataSource = _routeRows;
		_linksGrid.DataSource = _linkRows;
		_actionsGrid.DataSource = _actionRows;
		_actionTimersGrid.DataSource = _actionTimerRows;
		_mechsGrid.DataSource = _mechRows;
		_loadoutGrid.DataSource = _slotRows;
		_flyersGrid.DataSource = _flyerRows;
		_basesGrid.DataSource = _baseRows;
		_routeLinksGrid.DataSource = _routeLinkRows;
		_groupsGrid.DataSource = _groupRows;
		_entityLinksGrid.DataSource = _entityLinkRows;
		_unlocksGrid.DataSource = _unlockRows;
	}

	private void OnClose(object? sender, EventArgs e) => Close();

	/// <summary>
	/// Distinct per-form/file-type identity so Windows remembers this dialog's last-visited folder
	/// separately from every other Open/Save dialog in the app — see CampaignResourcesForm's
	/// DialogClientGuid for the full explanation. Shared by Open and both saves here since all three
	/// deal with the same script.dat file type.
	/// </summary>
	private static readonly Guid DialogClientGuid = new("6c1e0d54-3a7b-4f92-8c1d-2f5b7a9e4d31");

	/// <summary>
	/// The copy DBSIM actually reads, relative to the game directory — opened automatically so the
	/// editor starts on the live mission rather than an empty grid.
	/// </summary>
	private static readonly string[] DefaultFile = { "DATA", "SCRIPT.DAT" };

	protected override void OnLoad(EventArgs e) {
		base.OnLoad(e);

		if (GamePaths.Resolve(DefaultFile) is { } path) {
			LoadFile(path);
		}
	}

	private void OnOpen(object? sender, EventArgs e) {
		using var dialog = new OpenFileDialog {
			Filter = "Mission script files (script*.dat)|script*.dat|DAT files (*.dat)|*.dat|All files (*.*)|*.*",
			Title = "Open mission script file",
			ClientGuid = DialogClientGuid,
			InitialDirectory = GamePaths.InitialDirectoryFor("DATA")
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		LoadFile(dialog.FileName);
	}

	private void LoadFile(string path) {
		try {
			var file = GameFile.FromLooseFile(path);
			var script = (ScriptDat?)_transformer.Parse(file.Content);

			if (script == null) {
				MessageBox.Show(this, "File was empty or could not be parsed.", "Error",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			_loaded = script;
			Populate(script);

			_loadedFile = file;
			_saveWithPrefixMenuItem.Enabled = file.CompressionType.HasValue && file.MagicPrefix != null;

			string prefixNote = file.CompressionType.HasValue ? " (VOL entry prefix detected)" : "";
			_statusLabel.Text =
				$"Loaded {file.FileName} —{script.Coordinates.Length} points, " +
				$"{script.Mechs.Length} hercs, {script.Flyers.Length} flyers, " +
				$"{script.Bases.Length} bases, {script.Groups.Length} groups.{prefixNote}";
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to load file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	private void Populate(ScriptDat script) {
		_theaterInput.Value = Clamp(script.TheaterIndex, _theaterInput);
		_zoneInput.Value = Clamp(script.ZoneIndex, _zoneInput);
		_variantInput.Value = Clamp(script.TheaterVariant, _variantInput);
		_objectiveTypeInput.Value = Clamp(script.ObjectiveType, _objectiveTypeInput);
		_trainingInput.Value = Clamp(script.TrainingMissionNumber, _trainingInput);
		_unlimitedCheck.Checked = script.UnlimitedAmmunition == 1;
		_invulnerableCheck.Checked = script.PlayerInvulnerable == 1;
		_difficultyInput.Value = Clamp(script.Difficulty, _difficultyInput);
		_headerRawText.Text = string.Join(" ", script.HeaderBytes.Select(b => b.ToString("X2")));
		UpdateWorldLabel();

		Refill(_pointRows, script.Coordinates, (src, i) => new ScriptPointRow { Index = i, Source = src });
		Refill(_headingRows, script.Headings, (src, i) => new ScriptHeadingRow { Index = i, Source = src });
		Refill(_routeRows, script.WaypointGroups, (src, i) => new ScriptRouteRow { Index = i, Source = src });
		Refill(_linkRows, script.TriggerAreas, (src, i) => new ScriptTriggerAreaRow { Index = i, Source = src });
		Refill(_actionRows, script.Actions, (src, i) => new ScriptActionRow { Index = i, Source = src });
		Refill(_actionTimerRows, script.ActionTimers, (src, i) => new ScriptActionTimerRow { Index = i, Source = src });
		// A combo column rejects a value it has no item for, so any type or weapon id the file
		// carries that MECHS.NAM/WeaponLUT has no name for needs an entry in the list first. Order
		// matters both ways: the rows still bound here are the previously loaded file's, whose
		// values the new lists may not cover, so they have to go before the swap.
		_mechRows.Clear();
		_slotRows.Clear();
		_mechTypeColumn.DataSource = HercTypeOption.Build(script.Mechs.Select(r => r.TypeIndex));
		_slotWeaponColumn.DataSource = WeaponFitOption.Build(script.Mechs.SelectMany(r => r.WeaponRefs), includeEmptySlot: true);
		_slotAmmoColumn.DataSource = AmmoTypeOption.Build(script.Mechs.SelectMany(r => r.WeaponSecondary));

		Refill(_mechRows, script.Mechs, (src, i) => new ScriptMechRow { Index = i, Source = src });
		Refill(_flyerRows, script.Flyers, (src, i) => new ScriptFlyerRow { Index = i, Source = src });
		Refill(_baseRows, script.Bases, (src, i) => new ScriptBaseRow { Index = i, Source = src });
		Refill(_routeLinkRows, script.Orders, (src, i) => new ScriptOrderRow { Index = i, Source = src });
		Refill(_groupRows, script.Groups, (src, i) => new ScriptGroupRow { Index = i, Source = src });
		Refill(_entityLinkRows, script.Objectives, (src, i) => new ScriptObjectiveRow { Index = i, Source = src });

		_unlockRows.Clear();
		foreach (short value in script.ObjectiveTextRefs) {
			_unlockRows.Add(new ScriptObjectiveLineRow { Value = value });
		}

		BindLoadout();
	}

	private static void Refill<TSource, TRow>(BindingList<TRow> rows, TSource[] source, Func<TSource, int, TRow> makeRow) {
		rows.Clear();
		for (int i = 0; i < source.Length; i++) {
			rows.Add(makeRow(source[i], i));
		}
	}

	private static decimal Clamp(short value, NumericUpDown input) =>
		Math.Clamp(value, input.Minimum, input.Maximum);

	/// <summary>
	/// The theater/variant pair selects a world file by <c>theater * 2 + variant</c> — showing the
	/// resolved name makes an edit here checkable against the texture bank it actually picks.
	/// </summary>
	private void OnWorldSelectionChanged(object? sender, EventArgs e) => UpdateWorldLabel();

	private void UpdateWorldLabel() =>
		_worldValueLabel.Text = $"wld\\world{(int)_theaterInput.Value * 2 + (int)_variantInput.Value}.wld";

	/// <summary>
	/// The loadout panel edits whichever Herc the roster grid is on, so the slot rows are rebuilt
	/// on every selection change.
	/// </summary>
	private void OnMechSelectionChanged(object? sender, EventArgs e) => BindLoadout();

	/// <summary>
	/// Points the loadout grid at the selected Herc's ten hardpoints. The panel is disabled rather
	/// than left showing a stale fit when nothing is selected.
	/// </summary>
	private void BindLoadout() {
		var mech = _mechsGrid.CurrentRow?.DataBoundItem as ScriptMechRow;

		_slotRows.Clear();
		if (mech != null) {
			for (int slot = 0; slot < mech.Source.WeaponRefs.Length; slot++) {
				_slotRows.Add(new ScriptWeaponSlotRow { Source = mech.Source, Slot = slot });
			}
		}

		_loadoutGroupBox.Enabled = mech != null;
		_loadoutGroupBox.Text = mech == null
			? "Weapon fit"
			: $"Weapon fit — Herc {mech.Index}";
	}

	/// <summary>
	/// A combo cell normally only commits when focus leaves it, which would leave the ammunition
	/// column and the roster's fit summary a step behind the weapon just picked.
	/// </summary>
	private void OnLoadoutCellDirtyStateChanged(object? sender, EventArgs e) {
		if (_loadoutGrid.IsCurrentCellDirty) {
			_loadoutGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
		}
	}

	/// <summary>Ammunition is a launcher-only field — see WeaponFitOption.IsLauncher.</summary>
	private void OnLoadoutCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e) {
		if (e.ColumnIndex == _slotAmmoColumn.Index && SlotAt(e.RowIndex) is { IsLauncher: false }) {
			e.Cancel = true;
		}
	}

	/// <summary>
	/// Greys the ammunition cell of every slot that is not a launcher, so a value that has no effect
	/// does not read as one that does. The value itself is still shown rather than blanked — it is
	/// real data in the file, and retail's own filler 5 is what belongs there.
	/// </summary>
	private void OnLoadoutCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e) {
		if (e.ColumnIndex == _slotAmmoColumn.Index && e.CellStyle is { } style
			&& SlotAt(e.RowIndex) is { IsLauncher: false }) {
			style.ForeColor = SystemColors.GrayText;
			style.BackColor = SystemColors.Control;
		}
	}

	private ScriptWeaponSlotRow? SlotAt(int rowIndex) =>
		rowIndex >= 0 && rowIndex < _loadoutGrid.Rows.Count
			? _loadoutGrid.Rows[rowIndex].DataBoundItem as ScriptWeaponSlotRow
			: null;

	/// <summary>
	/// Both the roster's fit summary and the ammunition cell's own enabled look are derived from the
	/// weapon just picked, and neither is a bound property that would repaint on its own.
	/// </summary>
	private void OnLoadoutCellValueChanged(object? sender, DataGridViewCellEventArgs e) {
		if (e.RowIndex < 0) {
			return;
		}

		_loadoutGrid.InvalidateRow(e.RowIndex);

		if (_mechsGrid.CurrentRow is { } row) {
			_mechsGrid.InvalidateRow(row.Index);
		}
	}

	/// <summary>Waypoint lists are variable-length, so the count column has to follow the edit.</summary>
	private void OnRouteCellChanged(object? sender, DataGridViewCellEventArgs e) {
		if (e.RowIndex >= 0) {
			_routesGrid.InvalidateRow(e.RowIndex);
		}
	}

	/// <summary>
	/// Rejected cell edits surface here — the row property setters throw FormatException for a
	/// malformed or wrong-length ref list. Report and keep the old value rather than letting
	/// WinForms rethrow into an unhandled crash.
	/// </summary>
	private void OnGridDataError(object? sender, DataGridViewDataErrorEventArgs e) {
		e.ThrowException = false;
		e.Cancel = true;
		MessageBox.Show(this, e.Exception?.Message ?? "That value could not be applied.",
			"Invalid value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
	}

	/// <summary>Every grid under a control, however deeply nested.</summary>
	private static IEnumerable<DataGridView> GridsIn(Control? root) =>
		root == null
			? []
			: root.Controls.OfType<Control>()
				.SelectMany(child => child is DataGridView grid
					? Enumerable.Repeat(grid, 1)
					: GridsIn(child));

	private void OnAddUnlock(object? sender, EventArgs e) => _unlockRows.Add(new ScriptObjectiveLineRow());

	private void OnRemoveUnlock(object? sender, EventArgs e) {
		if (_unlocksGrid.CurrentRow?.DataBoundItem is ScriptObjectiveLineRow row) {
			_unlockRows.Remove(row);
		}
	}

	private void OnSaveAs(object? sender, EventArgs e) => Save(withPrefix: false);

	private void OnSaveWithPrefix(object? sender, EventArgs e) => Save(withPrefix: true);

	private void Save(bool withPrefix) {
		if (_loaded == null) {
			MessageBox.Show(this, "Open a script.dat file first.", "Nothing to save",
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		// Commit whatever cell is still being edited — grid edits write straight through to the
		// model, but only once the cell is committed. Walks the whole tab: the Hercs tab nests its
		// two grids inside a split container rather than parenting them to the page.
		foreach (var grid in GridsIn(_tabs.SelectedTab)) {
			grid.EndEdit();
		}

		ApplyHeader(_loaded);
		_loaded.ObjectiveTextRefs = _unlockRows.Select(r => r.Value).ToArray();

		var warnings = ScriptDatLint.Check(_loaded).Select(finding => finding.Message).ToList();
		if (warnings.Count > 0 && !ConfirmDespiteWarnings(warnings)) {
			return;
		}

		using var dialog = new SaveFileDialog {
			Filter = "Mission script files (*.dat)|*.dat|All files (*.*)|*.*",
			Title = withPrefix ? "Save mission script file with VOL entry prefix" : "Save mission script file",
			FileName = _loadedFile?.FileName ?? "SCRIPT.DAT",
			ClientGuid = DialogClientGuid,
			InitialDirectory = _loadedFile?.LoosePath is { } loosePath
				? Path.GetDirectoryName(loosePath)!
				: GamePaths.InitialDirectoryFor("DATA")
		};

		if (dialog.ShowDialog(this) != DialogResult.OK) {
			return;
		}

		try {
			byte[] content = _transformer.Write(_loaded)!;
			byte[] outBytes = withPrefix
				? VolEntryPrefixCodec.Wrap(content,
					_loadedFile!.CompressionType!.Value, _loadedFile.MagicPrefix!, _loadedFile.HadTrailingByte)
				: content;

			File.WriteAllBytes(dialog.FileName, outBytes);

			string formatNote = withPrefix
				? "Saved with the original VOL entry prefix, size field updated. DBSIM reads data\\script.dat " +
					"without it; use Save As for that."
				: "Saved in the content-only format DBSIM reads from data\\script.dat.";

			// The retail file is a fixed 13,520-byte preallocated buffer whose tail is stale
			// leftovers; DBSIM stops at block 13's declared end and ignores the rest, so the shorter
			// unpadded write is correct — worth saying, since the size differing from retail's is
			// otherwise an alarming thing to notice.
			MessageBox.Show(this,
				$"{formatNote}\n\n" +
				$"Written as {outBytes.Length:N0} bytes — the game's own files are padded out to a fixed " +
				"13,520-byte buffer with stale trailing data, which readers stop short of and ignore.",
				"Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
		} catch (Exception ex) {
			MessageBox.Show(this, $"Failed to save file:\n{ex.Message}", "Error",
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// Writes the edited header fields back into the raw 20 bytes — see docs/retail/formats/script-dat.md's
	/// header table. +4 (DBSIM stores 0 over it before anything reads it) and +16 (never read) are
	/// left exactly as loaded.
	/// </summary>
	private void ApplyHeader(ScriptDat script) {
		script.TheaterIndex = (short)_theaterInput.Value;
		script.ZoneIndex = (short)_zoneInput.Value;
		script.TheaterVariant = (short)_variantInput.Value;
		script.ObjectiveType = (short)_objectiveTypeInput.Value;
		script.TrainingMissionNumber = (short)_trainingInput.Value;
		script.UnlimitedAmmunition = (short)(_unlimitedCheck.Checked ? 1 : 0);
		script.PlayerInvulnerable = (short)(_invulnerableCheck.Checked ? 1 : 0);
		script.Difficulty = (short)_difficultyInput.Value;
		_headerRawText.Text = string.Join(" ", script.HeaderBytes.Select(b => b.ToString("X2")));
	}

	private bool ConfirmDespiteWarnings(List<string> warnings) {
		const int shown = 12;
		string detail = string.Join("\n", warnings.Take(shown));
		if (warnings.Count > shown) {
			detail += $"\n… and {warnings.Count - shown} more.";
		}

		return MessageBox.Show(this,
			$"{warnings.Count} problem(s) found:\n\n{detail}\n\nSave anyway?",
			"Mission check", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
	}
}
