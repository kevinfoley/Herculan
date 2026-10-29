using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Random table view. A random table is a row-1 condition; each of its options is one range under the table, with the
/// template that range conditions (docs/herculan/editor-design.md#random-tables). The Properties panel shows the
/// view when the table or one of its ranges is selected, that range's option then highlighted: its title, each option with
/// its chance, what it is and Remove, the share no option takes, Add option, Used by, a warning when it has no effect in
/// play, Delete random table, and the records' numbers under Advanced (the chances as <see cref="RandomTableChances"/> shows
/// them). Also the Random table line of the view of a squad herc, of a herc, flyer, structure or ground vehicle whose table the
/// editor shows only, and of a route. Every edit goes through <see cref="RandomTableEditor"/>. Everything it
/// shows is the <c>.MSN</c>'s and its layout's (<see cref="RandomTables"/>), so the scene's option is each record's first
/// (<see cref="LayoutPick.Shown"/>). How a record picks is docs/retail/formats/msn-mission-file.md#random-tables.
/// </summary>
internal sealed class RandomTableView {
	private static readonly Vector4 HighlightColor = new(1f, 0.85f, 0.1f, 1f);

	private readonly RandomTableEditor _editor;
	private readonly RandomTables _tables;
	private readonly PanelLinks _links;
	private readonly RandomTableChances _chances = new();

	public RandomTableView(RandomTableEditor editor, RandomTables tables, PanelLinks links) {
		_editor = editor;
		_tables = tables;
		_links = links;
	}

	/// <summary>
	/// Draws the Random table view of row-1 GUID <paramref name="guid"/> when it is a random table, or one of a table's ranges,
	/// that range's option highlighted; answers false, drawing nothing, for any other condition.
	/// </summary>
	public bool DrawTableOf(short guid) {
		var source = _editor.Source;
		short? table = RandomTableEditing.IsTable(source, guid) ? guid : RandomTableEditing.TableOfRange(source, guid);
		if (table is not { } shown || _tables.Read(shown) is not { } read) {
			return false;
		}

		short? highlight = shown == guid ? null : guid;
		if (highlight != null && read.Options.All(option => option.Range != highlight)) {
			return false;
		}

		DrawTable(read, highlight);
		return true;
	}

	private void DrawTable(RandomTable table, short? highlight) {
		var users = _tables.Users(table);
		string? why = table.WhyFixed;
		ImGui.PushID($"table{table.Guid}");
		EditorText.Wrapped(_tables.Title(table));
		if (_tables.NoEffect(table) is { } warning) {
			EditorText.Emphasis(warning);
		}

		EditorText.Reason(why);
		ImGui.Separator();
		DrawOptions(table, users, why, highlight);
		ImGui.Separator();
		DrawUsers(table, users);
		ImGui.Separator();
		DrawDelete(table, why);
		EditorText.Reason(_editor.Failure);
		DrawAdvanced(table);
		ImGui.PopID();
	}

	// Each option with its chance, what it is and Remove; the share no option takes; Add option.
	private void DrawOptions(RandomTable table, IReadOnlyList<RandomTableUser> users, string? why, short? highlight) {
		var source = _editor.Source;
		var percents = RandomTableEditing.Percents(table);
		var inScene = users.Select(user => user.Shown).OfType<int>().ToHashSet();
		ImGui.Text("Options:");
		for (int i = 0; i < table.Options.Count; i++) {
			var option = table.Options[i];
			ImGui.PushID(i);
			ImGui.AlignTextToFramePadding();
			ImGui.Text($"{i + 1}.");
			ImGui.SameLine();
			if (why != null) {
				ImGui.TextUnformatted($"{percents[i]:0.#}%");
			} else {
				ImGui.SetNextItemWidth(ScaledImGui.Scaled(RandomTableChances.FieldWidth));
				if (_chances.Field($"chance{table.Guid}.{i}", percents[i]) is { } typed) {
					var asked = percents.ToArray();
					asked[i] = typed;
					_editor.RequestChances(table.Guid, asked);
				}

				ImGui.SameLine();
				ImGui.TextUnformatted("%");
			}

			ImGui.SameLine();
			bool lit = option.Range == highlight;
			if (lit) {
				ImGui.PushStyleColor(ImGuiCol.Text, HighlightColor);
			}

			// The text wraps short of Remove, so the button stays inside the panel.
			bool removable = why == null && table.Options.Count > 1;
			float remove = removable ? EditorText.ButtonRoom("Remove") : 0f;
			ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - remove);
			EditorText.Wrapped(_tables.Option(table, i) + (inScene.Contains(i) ? " (in the scene)" : ""));
			ImGui.PopTextWrapPos();
			if (lit) {
				ImGui.PopStyleColor();
			}

			if (removable) {
				ImGui.SameLine();
				if (ImGui.SmallButton("Remove")) {
					_editor.RequestRemove(table.Guid, option.Range);
				}
			}

			ImGui.PopID();
		}

		RandomTableChances.DrawRounding(table, why == null);
		RandomTableChances.DrawNoOption(table, percents, _tables.Noun(table));

		if (inScene.Count > 0) {
			EditorText.Note("Each load picks one option at random; the scene shows the first.");
		}

		if (why == null) {
			string? noAdd = RandomTableEditing.WhyNoAdd(source, table.Guid);
			ImGui.BeginDisabled(noAdd != null);
			if (ImGui.Button("Add option")) {
				_editor.RequestAdd(table.Guid, table.Options[inScene.Count > 0 ? inScene.Min() : 0].Range);
			}

			ImGui.EndDisabled();
			EditorText.Tooltip(noAdd == null
				? "Adds a copy of the option the scene shows. The chances are shared out again with it, the share no option takes kept as it is."
				: null);
			EditorText.Reason(noAdd);
		}
	}

	// Every record that picks from it, one identity link per line with the option the scene shows it with, and why it has no
	// effect in play when it has none.
	private void DrawUsers(RandomTable table, IReadOnlyList<RandomTableUser> users) {
		ImGui.Text("Used by:");
		ImGui.PushID("users");
		for (int i = 0; i < users.Count; i++) {
			var user = users[i];
			ImGui.PushID(i);
			string shows = user.Shown is { } shown ? $": shows option {shown + 1}"
				: user.OtherTable ? ": shows another random table's option"
				: ": keeps its own details";
			if (user.Target is { } target) {
				_links.Link(user.Label, target, EditorText.AfterRoom(shows));
			} else {
				ImGui.TextUnformatted(NameText.Fit(user.Label, ImGui.GetContentRegionAvail().X - EditorText.AfterRoom(shows)));
			}

			EditorText.After(shows);
			if (user.Unused is { } unused) {
				EditorText.Note(RandomTables.UnusedNote(unused));
			}

			ImGui.PopID();
		}

		ImGui.PopID();
		int others = _tables.Others(table);
		if (others > 0) {
			ImGui.TextWrapped(others == 1 ? "1 template of another random table" : $"{others} templates of other random tables");
		}

		if (users.Count == 0 && others == 0) {
			EditorText.Note("Nothing picks from it.");
		}
	}

	// Delete random table, or why it cannot be deleted, with what references it as links.
	private void DrawDelete(RandomTable table, string? why) {
		if (why != null) {
			return;
		}

		switch (RandomTableEditing.WhyNoDelete(_editor.Source, table.Guid)) {
			case RandomTableEditing.PickedFrom:
				ImGui.TextWrapped("Cannot be deleted while those under \"Used by\" pick from it.");
				return;
			case RandomTableEditing.Referenced:
				ImGui.TextWrapped("Cannot be deleted while these reference it:");
				ImGui.PushID("referencing");
				foreach (var (label, target) in _tables.Referencing(table)) {
					ImGui.PushID(label);
					if (target != null) {
						_links.Link(label, target);
					} else {
						ImGui.TextUnformatted(label);
					}

					ImGui.PopID();
				}

				ImGui.PopID();
				return;
			case { } reason:
				EditorText.Reason(reason, "Cannot be deleted: ");
				return;
		}

		if (ImGui.Button("Delete random table")) {
			_editor.RequestDelete(table.Guid);
		}
	}

	// The records behind it, for matching against the file: its condition number and key, its requirement, its roll, each
	// option's condition number and range, and the other tables under its key.
	private void DrawAdvanced(RandomTable table) {
		if (!ImGui.CollapsingHeader("Advanced")) {
			return;
		}

		var source = _editor.Source;
		ImGui.TextUnformatted($"Condition {table.Guid}, key {table.Key}");
		ImGui.TextUnformatted(table.Bound > 0 ? $"Roll: a number from 0 to {table.Bound - 1}" : $"Roll: below {table.Bound}");
		if (table.Requires != -1) {
			ImGui.AlignTextToFramePadding();
			ImGui.Text("Requires:");
			ImGui.SameLine();
			_links.Link($"Condition {table.Requires}", new ConditionSelection(table.Requires));
			EditorText.Note("Where that condition fails, nothing picks from this random table.");
		}

		ImGui.PushID("ranges");
		for (int i = 0; i < table.Options.Count; i++) {
			var option = table.Options[i];
			var range = ConditionEditing.Record(source, option.Range);
			ImGui.PushID(i);
			ImGui.Text($"Option {i + 1}:");
			ImGui.SameLine();
			_links.Link($"Condition {option.Range}", new ConditionSelection(option.Range));
			ImGui.SameLine(0f, 0f);
			ImGui.TextUnformatted($", {(ushort)range.FlagIndexOrRangeLower} to {(ushort)range.OperatorOrRangeUpperOrResult}");
			ImGui.PopID();
		}

		ImGui.PopID();
		if (table.SameKey.Count == 0) {
			return;
		}

		ImGui.Text("Also under this key:");
		foreach (short other in table.SameKey) {
			ImGui.PushID($"key{other}");
			_links.Link($"Condition {other}", new ConditionSelection(other));
			ImGui.PopID();
		}
	}

	/// <summary>
	/// The Random table line of the view of roster row <paramref name="row"/>'s GUID <paramref name="guid"/> when it picks from a
	/// table and no choice of it is offered there, a squad herc's or one whose table the editor shows only: a link to the table
	/// and which option the scene shows it with.
	/// </summary>
	public void DrawPickedTable(int row, short guid) {
		if (RandomTableEditing.TableOf(_editor.Source, row, guid) is not { } picked) {
			return;
		}

		ImGui.PushID("table");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Random table:");
		_links.Link(TitleOf(picked), new ConditionSelection(picked));
		DrawShown(row, guid, picked);
		ImGui.PopID();
	}

	/// <summary>
	/// The Random table line of route <paramref name="guid"/>'s view when its waypoints are an option of a random table: which
	/// option, with a link to the table, and that an edit changes that option wherever it is picked.
	/// </summary>
	public void DrawRouteTable(short guid) {
		const int routeRow = 8;
		if (RandomTableEditing.TableOf(_editor.Source, routeRow, guid) is not { } table || _tables.Read(table) is not { } read) {
			return;
		}

		ImGui.PushID("routeTable");
		ImGui.AlignTextToFramePadding();
		ImGui.Text(ShownAt(routeRow, guid, read) is { } at ? $"Its waypoints are option {at + 1} of" : "It picks its waypoints from");
		ImGui.SameLine();
		_links.Link(_tables.Title(read), new ConditionSelection(table));
		EditorText.Note("Editing them changes that option wherever it is picked.");
		ImGui.PopID();
	}

	// Which option of <table> the scene shows record <row>, <guid> with.
	private void DrawShown(int row, short guid, short table) {
		if (_tables.Read(table) is { } read && ShownAt(row, guid, read) is { } at) {
			EditorText.Wrapped($"The scene shows option {at + 1}: {_tables.Option(read, at)}.");
		} else {
			EditorText.Note("The scene shows none of its options, so it keeps its own details.");
		}
	}

	/// <summary>
	/// The option of <paramref name="table"/> the layout shows the key record of roster row <paramref name="row"/>'s GUID
	/// <paramref name="guid"/> with, an index into its options, or null for none of them.
	/// </summary>
	internal int? ShownAt(int row, short guid, RandomTable table) {
		var source = _editor.Source;
		if (RandomTableEditing.KeyRecord(source, row, guid) is not { } key
				|| RandomTableEditing.PickOf(source, key) is not { Table: var picked, Shown: { Record: not null } option } || picked != table.Guid) {
			return null;
		}

		int at = table.Options.ToList().FindIndex(own => own.Range == option.Range);
		return at >= 0 ? at : null;
	}

	private string TitleOf(short table) => _tables.Read(table) is { } read ? _tables.Title(read) : $"Condition {table}";

	/// <summary>What the view of <paramref name="placed"/> calls it: herc, flyer, ground vehicle or structure.</summary>
	internal static string Noun(SceneObject placed) => placed.Placement.Kind switch {
		MissionUnitKind.Mech => "HERC",
		MissionUnitKind.Flyer => "flyer",
		_ when placed.Object is Sim.BaseObject { Type.IsVehicle: true } => "ground vehicle",
		_ => "structure",
	};
}
