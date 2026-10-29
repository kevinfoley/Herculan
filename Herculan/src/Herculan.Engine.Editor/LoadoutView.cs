using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.Scene;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Loadout section of the view of a herc outside the player's squad, the Type section of a flyer's, structure's or ground
/// vehicle's (docs/herculan/editor-design.md#random-tables): Fixed or Random. Fixed edits the record's own
/// type and fit (<see cref="RosterFitView"/>). Random lists the options of the random table it picks from, each with its
/// chance (<see cref="RandomTableChances"/>) and what it is (<see cref="RandomTables.Option"/>), one of them selected, the share
/// no option takes, Add option and Remove option, the other records that pick from the table as links, a choice of another
/// table to pick from, and the type and fit of the selected option, which every edit there writes.
///
/// <para>The selected option is this view's own state, by its range's GUID, kept while the view shows the same table: the
/// first option by default, the new one after Add option, and when its range is gone, as after Remove option or an undo, the
/// option before the one it was, or the first. A table the editor shows only (<see cref="RandomTable.WhyFixed"/>) has no
/// Fixed or Random choice: its Random table line (<see cref="RandomTableView.DrawPickedTable"/>) with the reason, and the
/// fit editing what the layout shows. Every edit goes through <see cref="RandomTableEditor"/>.</para>
/// </summary>
internal sealed class LoadoutView {
	private readonly RandomTableEditor _editor;
	private readonly RandomTables _tables;
	private readonly PanelLinks _links;
	private readonly RosterFitView _fit;
	private readonly RandomTableView _tableView;
	private readonly RandomTableChances _chances = new();

	// The selected option: its table, its range and where it stood in the table's options.
	private (short Table, short Range, int At)? _selected;

	public LoadoutView(RandomTableEditor editor, RandomTables tables, PanelLinks links, RosterFitView fit, RandomTableView tableView) {
		_editor = editor;
		_tables = tables;
		_links = links;
		_fit = fit;
		_tableView = tableView;
	}

	/// <summary>Draws the section of <paramref name="placed"/>, an object outside the squad; nothing for one of the squad.</summary>
	public void Draw(SceneObject placed) {
		if (_editor.RecordOf(placed) is not var (row, guid)) {
			return;
		}

		var source = _editor.Source;
		short? current = RandomTableEditing.TableOf(source, row, guid);
		var read = current is { } picked ? _tables.Read(picked) : null;
		ImGui.Separator();
		ImGui.PushID("loadout");
		if (current != null && (read == null || read.WhyFixed != null)) {
			_tableView.DrawPickedTable(row, guid);
			EditorText.Reason(read?.WhyFixed);
			_fit.Draw(placed, null);
			ImGui.PopID();
			return;
		}

		bool herc = row == 12;
		string noun = RandomTableView.Noun(placed);
		string? noSwitch = _editor.WhyNoTables(row, guid);
		ImGui.AlignTextToFramePadding();
		ImGui.Text(herc ? "Loadout:" : "Type:");
		ImGui.SameLine();
		ImGui.BeginDisabled(noSwitch != null);
		int at = read != null ? Selected(read) : 0;
		if (ImGui.RadioButton("Fixed", read == null) && read != null) {
			_editor.RequestFixed(row, guid, read.Options[at].Range);
		}

		EditorText.Tooltip(read == null
			? $"Every load gives this {noun} the {(herc ? "loadout" : "type")} below."
			: $"Keeps option {at + 1} as this {noun}'s own {(herc ? "loadout" : "type")}, and stops picking at random.");
		ImGui.SameLine();
		if (ImGui.RadioButton("Random", read != null) && read == null) {
			_editor.RequestCreate(row, guid);
		}

		EditorText.Tooltip(read == null
			? $"Each load picks one of several {(herc ? "loadouts" : "types")} at random, by the chance you give each. "
				+ $"It starts with one option, the {(herc ? "loadout" : "type")} below."
			: null);
		ImGui.EndDisabled();
		EditorText.Reason(noSwitch);
		if (read == null) {
			EditorText.Reason(_editor.Failure);
			_fit.Draw(placed, null);
			ImGui.PopID();
			return;
		}

		DrawShared(read, row, guid, herc);
		DrawOptions(read, row, guid, at);
		DrawOtherTables(read, row, guid, noun);
		EditorText.Reason(_editor.Failure);
		ImGui.Spacing();
		EditorText.Wrapped($"Editing option {at + 1}: {_tables.Option(read, at)}");
		_fit.Draw(placed, read.Options[at].Template);
		ImGui.PopID();
	}

	// The selected option of <table>, an index into its options; the first when the view showed another table.
	private int Selected(RandomTable table) {
		int at = 0;
		if (_selected is { } selected && selected.Table == table.Guid) {
			int found = table.Options.ToList().FindIndex(option => option.Range == selected.Range);
			at = found >= 0 ? found : Math.Clamp(selected.At - 1, 0, table.Options.Count - 1);
		}

		Select(table, at);
		return at;
	}

	private void Select(RandomTable table, int at) => _selected = (table.Guid, table.Options[at].Range, at);

	// When other records pick from <table> too: that an edit changes it for them, and each as a link, as "Used by" names them.
	private void DrawShared(RandomTable table, int row, short guid, bool herc) {
		var others = _tables.Users(table).Where(user => user.Target is not ObjectSelection own || own.Row != row || own.Guid != guid).ToList();
		int templates = _tables.Others(table);
		if (others.Count == 0 && templates == 0) {
			return;
		}

		string what = herc ? "random loadout" : "random type";
		string noun = _tables.Noun(table);
		EditorText.Wrapped(others.Count == 0
			? $"Templates of other random tables pick this {what} too: editing an option changes it for them as well."
			: $"This {what} is shared with {others.Count} other {noun}{(others.Count == 1 ? "" : "s")}: editing an option changes it for "
				+ $"{(others.Count == 1 ? "both" : "all")} of them.");
		ImGui.PushID("shared");
		for (int i = 0; i < others.Count; i++) {
			ImGui.PushID(i);
			if (others[i].Target is { } target) {
				_links.Link(others[i].Label, target);
			} else {
				ImGui.TextUnformatted(others[i].Label);
			}

			ImGui.PopID();
		}

		ImGui.PopID();
		if (others.Count > 0 && templates > 0) {
			ImGui.TextWrapped(templates == 1 ? "1 template of another random table" : $"{templates} templates of other random tables");
		}
	}

	// Each option on a row that selects it: its number, its chance and what it is; the share no option takes; Add option and
	// Remove option.
	private void DrawOptions(RandomTable table, int row, short guid, int at) {
		var percents = RandomTableEditing.Percents(table);
		int? inScene = _tableView.ShownAt(row, guid, table);
		ImGui.PushID("options");
		for (int i = 0; i < table.Options.Count; i++) {
			ImGui.PushID(i);
			ImGui.AlignTextToFramePadding();
			ImGui.Text($"{i + 1}.");
			ImGui.SameLine();
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(RandomTableChances.FieldWidth));
			if (_chances.Field($"chance{table.Guid}.{i}", percents[i]) is { } typed) {
				var asked = percents.ToArray();
				asked[i] = typed;
				_editor.RequestChances(table.Guid, asked, selectTable: false);
			}

			ImGui.SameLine();
			ImGui.TextUnformatted("%");
			ImGui.SameLine();
			string text = _tables.Option(table, i) + (i == inScene ? " (in the scene)" : "");
			if (EditorText.WrappedSelectable(text, $"option{i}", i == at)) {
				Select(table, i);
			}

			ImGui.PopID();
		}

		ImGui.PopID();
		RandomTableChances.DrawRounding(table, editable: true);
		RandomTableChances.DrawNoOption(table, percents, _tables.Noun(table));
		if (ImGui.Button("Add option")) {
			_editor.RequestAdd(table.Guid, table.Options[at].Range, selectTable: false,
				added: range => _selected = (table.Guid, range, table.Options.Count));
		}

		EditorText.Tooltip($"Adds a copy of option {at + 1}. The chances are shared out again with it, the share no option takes "
			+ "kept as it is.");
		ImGui.SameLine();
		string? noRemove = RandomTableEditing.WhyNoRemove(_editor.Source, table.Guid, table.Options[at].Range);
		ImGui.BeginDisabled(noRemove != null);
		if (ImGui.Button($"Remove option {at + 1}")) {
			_editor.RequestRemove(table.Guid, table.Options[at].Range, selectTable: false);
		}

		ImGui.EndDisabled();
		EditorText.Tooltip(noRemove ?? "Its chance is shared over the other options, in proportion to theirs.");
	}

	// A choice of the other random tables of its kind to pick from instead, by their titles.
	private void DrawOtherTables(RandomTable table, int row, short guid, string noun) {
		var source = _editor.Source;
		var choices = _tables.All.Where(other => other != table.Guid && RandomTableEditing.WhyNoTable(source, row, guid, other) == null).ToList();
		if (choices.Count == 0) {
			return;
		}

		ImGui.SetNextItemWidth(-1f);
		if (EditorText.BeginCombo("##use", "Use a different table")) {
			foreach (short other in choices) {
				if (_tables.Read(other) is { } read && ImGui.Selectable($"{_tables.Title(read)}##{other}")) {
					_editor.RequestTable(row, guid, other);
				}
			}

			ImGui.EndCombo();
		}

		EditorText.Tooltip($"Use an existing random table; note that changes to a shared table apply to every object that uses that table.");
	}
}
