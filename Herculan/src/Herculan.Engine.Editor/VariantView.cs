using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Random choice view, in the Properties panel's view of a variant table's condition and of a range over one, that option
/// then highlighted (docs/herculan/handoff-editor-conditions.md#variants): its title, each option with its chance, what it is
/// and Remove, Nothing with what is left over, Add option, Used by, a warning when it has no effect in play, Delete random
/// choice, and the records' numbers under Advanced; and the Random choice line of the view of a herc, flyer, structure or
/// ground vehicle and of a route. Each choice is an edit (<see cref="VariantEditor"/>). Everything it shows is the
/// <c>.MSN</c>'s and its layout's (<see cref="RandomChoices"/>), so the scene's option is each record's first
/// (<see cref="LayoutDraw.Shown"/>). How a record picks is docs/retail/formats/msn-mission-file.md#variants.
/// </summary>
internal sealed class VariantView {
	private static readonly Vector4 HighlightColor = new(1f, 0.85f, 0.1f, 1f);

	private readonly VariantEditor _editor;
	private readonly RandomChoices _choices;
	private readonly PanelLinks _links;

	// A chance being typed, and the field it is for, until the field lets go.
	private string? _draftFor;
	private float _draft;

	public VariantView(VariantEditor editor, RandomChoices choices, PanelLinks links) {
		_editor = editor;
		_choices = choices;
		_links = links;
	}

	/// <summary>
	/// Draws the Random choice view of row-1 GUID <paramref name="guid"/> when it is a variant table, or a range over one choosing
	/// an option the table reads, that option highlighted; answers false, drawing nothing, for any other condition.
	/// </summary>
	public bool DrawChoice(short guid) {
		var source = _editor.Source;
		short? table = VariantEditing.IsTable(source, guid) ? guid : VariantEditing.TableOfRange(source, guid);
		if (table is not { } shown || _choices.Read(shown) is not { } read) {
			return false;
		}

		short? highlight = shown == guid ? null : guid;
		if (highlight != null && read.Alternatives.All(alternative => alternative.Range != highlight)) {
			return false;
		}

		DrawTable(read, highlight);
		return true;
	}

	private void DrawTable(VariantTable table, short? highlight) {
		var users = _choices.Users(table);
		string? why = table.WhyFixed;
		ImGui.PushID($"table{table.Guid}");
		EditorText.Wrapped(_choices.Title(table));
		if (_choices.NoEffect(table) is { } warning) {
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

	// Each option with its chance, what it is and Remove; Nothing; Add option.
	private void DrawOptions(VariantTable table, IReadOnlyList<ChoiceUser> users, string? why, short? highlight) {
		var source = _editor.Source;
		var percents = VariantEditing.Percents(table);
		var inScene = users.Select(user => user.Shown).OfType<int>().ToHashSet();
		ImGui.Text("Options:");
		for (int i = 0; i < table.Alternatives.Count; i++) {
			var alternative = table.Alternatives[i];
			ImGui.PushID(i);
			ImGui.AlignTextToFramePadding();
			ImGui.Text($"{i + 1}.");
			ImGui.SameLine();
			if (why != null) {
				ImGui.TextUnformatted($"{percents[i]:0.#}%");
			} else {
				ImGui.SetNextItemWidth(ScaledImGui.Scaled(56f));
				if (ChanceField($"chance{table.Guid}.{i}", percents[i]) is { } typed) {
					var asked = percents.ToArray();
					asked[i] = typed;
					_editor.RequestChances(table.Guid, asked);
				}

				ImGui.SameLine();
				ImGui.TextUnformatted("%");
			}

			ImGui.SameLine();
			bool lit = alternative.Range == highlight;
			if (lit) {
				ImGui.PushStyleColor(ImGuiCol.Text, HighlightColor);
			}

			EditorText.Wrapped(_choices.Option(table, i) + (inScene.Contains(i) ? " (in the scene)" : ""));
			if (lit) {
				ImGui.PopStyleColor();
			}

			if (why == null && table.Alternatives.Count > 1) {
				ImGui.SameLine();
				if (ImGui.SmallButton("Remove")) {
					_editor.RequestRemove(table.Guid, alternative.Range);
				}
			}

			ImGui.PopID();
		}

		if (!VariantEditing.Exact(table)) {
			string exact = string.Join(", ", table.Alternatives.Select(alternative => $"{table.Percent(alternative.Draws):0.###}%"));
			EditorText.Note(why == null ? $"Rounded from {exact}: a change writes every chance as shown." : $"Rounded from {exact}.");
		}

		if (table.Leftover > 0) {
			EditorText.Wrapped($"Nothing: {Math.Max(100m - percents.Sum(), 0m):0.#}%");
			if (table.Row is >= 12 and <= 14) {
				// Its references stay the .MSN's own numbers, which the load reads as list positions (MissionGenerator.ReadHercs and
				// its siblings, docs/herculan/handoff-editor-conditions.md#variants).
				EditorText.Note($"A load that lands here picks no option: the {_choices.Noun(table)} keeps its own details, but its "
					+ "position, heading and triggers can come out wrong in play.");
			}
		}

		if (inScene.Count > 0) {
			EditorText.Note("Each load picks one option at random; the scene shows the first.");
		}

		if (why == null) {
			string? noAdd = VariantEditing.WhyNoAdd(source, table.Guid);
			ImGui.BeginDisabled(noAdd != null);
			if (ImGui.Button("Add option")) {
				_editor.RequestAdd(table.Guid, table.Alternatives[inScene.Count > 0 ? inScene.Min() : 0].Range);
			}

			ImGui.EndDisabled();
			EditorText.Note(noAdd ?? "Adds a copy of the option the scene shows, with half the largest chance.");
		}
	}

	// Every record that picks from it, one identity link per line with the option the scene shows it with, and why it has no
	// effect in play when it has none.
	private void DrawUsers(VariantTable table, IReadOnlyList<ChoiceUser> users) {
		ImGui.Text("Used by:");
		ImGui.PushID("users");
		for (int i = 0; i < users.Count; i++) {
			var user = users[i];
			ImGui.PushID(i);
			if (user.Target is { } target) {
				_links.Link(user.Label, target);
			} else {
				ImGui.TextUnformatted(user.Label);
			}

			ImGui.SameLine(0f, 0f);
			ImGui.TextUnformatted(user.Shown is { } shown ? $": shows option {shown + 1}"
				: user.OtherChoice ? ": shows another random choice's option"
				: ": keeps its own details");
			if (user.Unused is { } unused) {
				EditorText.Note(RandomChoices.UnusedNote(unused));
			}

			ImGui.PopID();
		}

		ImGui.PopID();
		int others = _choices.Others(table);
		if (others > 0) {
			ImGui.TextWrapped(others == 1 ? "1 option of another random choice" : $"{others} options of other random choices");
		}

		if (users.Count == 0 && others == 0) {
			EditorText.Note("Nothing picks from it.");
		}
	}

	// Delete random choice, or why it cannot be deleted, with what references it as links.
	private void DrawDelete(VariantTable table, string? why) {
		if (why != null) {
			return;
		}

		switch (VariantEditing.WhyNoDelete(_editor.Source, table.Guid)) {
			case VariantEditing.PickedFrom:
				ImGui.TextWrapped("Cannot be deleted while those under \"Used by\" pick from it.");
				return;
			case VariantEditing.Referenced:
				ImGui.TextWrapped("Cannot be deleted while these reference it:");
				ImGui.PushID("referencing");
				foreach (var (label, target) in _choices.Referencing(table)) {
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

		if (ImGui.Button("Delete random choice")) {
			_editor.RequestDelete(table.Guid);
		}
	}

	// The records behind it, for matching against the file: its condition number and key, its requirement, its roll, each
	// option's condition number and range, and the other tables under its key.
	private void DrawAdvanced(VariantTable table) {
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
			EditorText.Note("Where that condition fails, nothing picks from this random choice.");
		}

		ImGui.PushID("ranges");
		for (int i = 0; i < table.Alternatives.Count; i++) {
			var alternative = table.Alternatives[i];
			var range = ConditionEditing.Record(source, alternative.Range);
			ImGui.PushID(i);
			ImGui.Text($"Option {i + 1}:");
			ImGui.SameLine();
			_links.Link($"Condition {alternative.Range}", new ConditionSelection(alternative.Range));
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
	/// The Random choice line of a herc's, flyer's, structure's or ground vehicle's view: outside the squad a choice of None,
	/// each random choice of its kind by its title, or New random choice from this herc (flyer, structure, ground vehicle); for
	/// a squad herc the choice it picks from, shown only. Then a link to the choice and which option the scene shows.
	/// </summary>
	public void DrawObjectChoice(SceneObject placed) {
		var source = _editor.Source;
		var index = _choices.Index;
		if (index.KeyOf(placed) is not { } key || index.Plan.Object(key) is not { } planned) {
			return;
		}

		int row = key.Row;
		short guid = key.Guid;
		short? current = VariantEditing.TableOf(source, row, guid);
		if (planned.Squad && current == null) {
			return;
		}

		ImGui.PushID("choice");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Random choice:");
		if (!planned.Squad) {
			string? why = VariantEditing.WhyNoTables(source, row, guid);
			ImGui.SameLine();
			ImGui.SetNextItemWidth(-1f);
			ImGui.BeginDisabled(why != null);
			if (ImGui.BeginCombo("##choice", current is { } named ? TitleOf(named) : "None")) {
				if (ImGui.Selectable("None", current == null) && current != null) {
					_editor.RequestTable(row, guid, -1);
				}

				foreach (short table in _choices.All) {
					if (VariantEditing.WhyNoTable(source, row, guid, table) == null
							&& ImGui.Selectable($"{TitleOf(table)}##{table}", table == current) && table != current) {
						_editor.RequestTable(row, guid, table);
					}
				}

				if (ImGui.Selectable($"New random choice from this {Noun(placed)}")) {
					_editor.RequestCreate(row, guid);
				}

				ImGui.EndCombo();
			}

			ImGui.EndDisabled();
			EditorText.Reason(why);
		}

		if (current is { } chosen) {
			_links.Link(TitleOf(chosen), new ConditionSelection(chosen));
			DrawShown(row, guid, chosen);
		}

		EditorText.Reason(_editor.Failure);
		ImGui.PopID();
	}

	/// <summary>
	/// The Random choice line of route <paramref name="guid"/>'s view when its waypoints are an option of a random choice: which
	/// option, with a link to the choice, and that an edit changes that option wherever it is picked.
	/// </summary>
	public void DrawRouteChoice(short guid) {
		const int routeRow = 8;
		if (VariantEditing.TableOf(_editor.Source, routeRow, guid) is not { } table || _choices.Read(table) is not { } read) {
			return;
		}

		ImGui.PushID("routeChoice");
		ImGui.AlignTextToFramePadding();
		ImGui.Text(ShownAt(routeRow, guid, read) is { } at ? $"Its waypoints are option {at + 1} of" : "It picks its waypoints from");
		ImGui.SameLine();
		_links.Link(_choices.Title(read), new ConditionSelection(table));
		EditorText.Note("Editing them changes that option wherever it is picked.");
		ImGui.PopID();
	}

	// Which option of <table> the scene shows record <row>, <guid> with.
	private void DrawShown(int row, short guid, short table) {
		if (_choices.Read(table) is { } read && ShownAt(row, guid, read) is { } at) {
			EditorText.Wrapped($"The scene shows option {at + 1}: {_choices.Option(read, at)}.");
		} else {
			EditorText.Note("The scene shows none of its options, so it keeps its own details.");
		}
	}

	// The option of <table> the layout shows the key record of <row>, <guid> with, or null for none of its options.
	private int? ShownAt(int row, short guid, VariantTable table) {
		var source = _editor.Source;
		if (VariantEditing.KeyRecord(source, row, guid) is not { } key
				|| VariantEditing.DrawOf(source, key) is not { Table: var drawn, Shown: { Record: not null } option } || drawn != table.Guid) {
			return null;
		}

		int at = table.Alternatives.ToList().FindIndex(alternative => alternative.Range == option.Range);
		return at >= 0 ? at : null;
	}

	private string TitleOf(short table) => _choices.Read(table) is { } read ? _choices.Title(read) : $"Condition {table}";

	/// <summary>What the view of <paramref name="placed"/> calls it: herc, flyer, ground vehicle or structure.</summary>
	internal static string Noun(SceneObject placed) => placed.Placement.Kind switch {
		MissionUnitKind.Mech => "herc",
		MissionUnitKind.Flyer => "flyer",
		_ when placed.Object is Sim.BaseObject { Type.IsVehicle: true } => "ground vehicle",
		_ => "structure",
	};

	// A typed percent to one decimal, 0 to 100; answers the value when the field lets go after a change.
	private decimal? ChanceField(string id, decimal current) {
		if (_draftFor != null && _draftFor != id && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		float shown = _draftFor == id ? _draft : (float)current;
		if (ImGui.InputFloat($"##{id}", ref shown, 0f, 0f, "%.1f")) {
			_draftFor = id;
			_draft = shown;
		}

		decimal? typed = null;
		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFor == id) {
			_draftFor = null;
			decimal value = float.IsFinite(_draft) ? decimal.Round((decimal)Math.Clamp(_draft, 0f, 100f), 1) : current;
			if (value != current) {
				typed = value;
			}
		}

		return typed;
	}
}
