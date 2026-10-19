using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A variant table's view, in the Properties panel's view of its condition: its key and bound, each alternative with its
/// chance, a summary and Remove, Add alternative, what is left over, which alternative the editor shows, the records drawing
/// from it, the other tables of its key, and Delete; and, in the view of a herc, flyer, structure or ground vehicle outside
/// the squad, which table it is drawn from (docs/herculan/handoff-editor-mission-editing.md#in-place, "Variants"). Each
/// choice is an edit (<see cref="VariantEditor"/>) or, for the alternative shown, the campaign state's
/// (<see cref="CampaignState.RequestShown"/>). How a record draws is docs/retail/formats/msn-mission-file.md#variants.
/// </summary>
internal sealed class VariantView {
	private readonly VariantEditor _editor;
	private readonly CampaignState _campaign;
	private readonly ConditionEditor _conditions;
	private readonly PanelLinks _links;
	private readonly Func<MapObject, string> _summary;

	// A chance being typed, and the field it is for, until the field lets go.
	private string? _draftFor;
	private int _draft;

	/// <param name="summary">What an alternative is, as its kind's view titles it (<see cref="Summaries"/>).</param>
	public VariantView(VariantEditor editor, CampaignState campaign, ConditionEditor conditions, PanelLinks links,
			Func<MapObject, string> summary) {
		_editor = editor;
		_campaign = campaign;
		_conditions = conditions;
		_links = links;
		_summary = summary;
	}

	/// <summary>
	/// What an alternative is: a herc's chassis name and a flyer's type name as <c>nam\MECHS.NAM</c> and <c>nam\FLYERS.NAM</c>
	/// list them, a structure's or ground vehicle's type and the name the MFD prints, as the object view titles each, and a
	/// route's waypoint count.
	/// </summary>
	public static Func<MapObject, string> Summaries(GameContent content) {
		var mechs = UnitTypeNames.LoadMechs(content);
		var flyers = UnitTypeNames.LoadFlyers(content);
		var bases = BaseTypeTable.Load(content);
		var strings = SimStrings.Load(content);
		return record => record switch {
			MechRosterEntry144 mech => mechs[mech.TypeIndex] ?? $"Herc type {mech.TypeIndex}",
			FlyerRosterEntry102 flyer => flyers[flyer.TypeIndex] ?? $"Flyer type {flyer.TypeIndex}",
			BaseRosterEntry62 structure when bases[structure.TypeIndex] is { } type =>
				$"{(type.IsVehicle ? "Ground vehicle" : "Base")} type {structure.TypeIndex}: "
					+ (MfdStatusSubject.BaseName(type, strings) is { Length: > 0 } name && !string.IsNullOrWhiteSpace(name) ? name : "(no name)"),
			BaseRosterEntry62 structure => $"Base type {structure.TypeIndex}",
			WaypointGroup route => $"A route of {route.Waypoints.Length} waypoint(s)",
			_ => record.GetType().Name,
		};
	}

	/// <summary>Draws table <paramref name="guid"/>'s view, after the condition view's title.</summary>
	public void DrawTable(short guid) {
		if (_editor.Source is not { } source || VariantEditing.Table(source, guid) is not { } table) {
			return;
		}

		string? why = _editor.WhyFixed(table);
		ImGui.PushID($"table{guid}");
		ImGui.Text($"Key: {table.Key}");
		ImGui.Text($"Draws below: {table.Bound}");
		if (table.Requires != -1) {
			ImGui.AlignTextToFramePadding();
			ImGui.Text("Requires:");
			ImGui.SameLine();
			_links.Link($"Condition {table.Requires}", new ConditionSelection(table.Requires));
			EditorText.Note("Where that condition fails, nothing draws from this table.");
		}

		if (!ConditionEditing.Outcome(source, guid).Survived) {
			EditorText.Emphasis("Left out in the campaign state shown: nothing draws from it there.");
		}

		EditorText.Reason(why);
		ImGui.Separator();
		DrawAlternatives(source, table, why);
		ImGui.Separator();
		DrawShown(source, table, why);
		ImGui.Separator();
		DrawDrawers(source, table);
		DrawSameKey(source, table);
		ImGui.Separator();
		DrawDelete(source, table, why);
		EditorText.Reason(_editor.Failure);
		ImGui.PopID();
	}

	/// <summary>
	/// The Drawn from of a herc, flyer, structure or ground vehicle outside the squad: None, a table of its kind's
	/// alternatives, or New table from it, with a link to the table and what it draws in the campaign state shown.
	/// </summary>
	public void DrawDrawnFrom(SceneObject placed) {
		if (_editor.Source is not { } source || _editor.RecordOf(placed) is not var (row, guid)) {
			return;
		}

		string kind = Noun(placed);
		string? why = _editor.WhyNoTables(row, guid);
		short? current = VariantEditing.TableOf(source, row, guid);
		ImGui.PushID("drawnFrom");
		ImGui.Text("Drawn from:");
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ImGui.BeginCombo("##table", current is { } shown ? TableName(source, shown) : "None")) {
			if (ImGui.Selectable("None", current == null) && current != null) {
				_editor.RequestTable(row, guid, -1);
			}

			foreach (short table in VariantEditing.Tables(source)) {
				if (VariantEditing.WhyNoTable(source, row, guid, table) == null
						&& ImGui.Selectable(TableName(source, table), table == current) && table != current) {
					_editor.RequestTable(row, guid, table);
				}
			}

			if (ImGui.Selectable($"New table from this {kind}")) {
				_editor.RequestCreate(row, guid);
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Reason(why);
		if (current is { } drawn) {
			_links.Link($"Condition {drawn}", new ConditionSelection(drawn));
			if (VariantEditing.KeyRecord(source, row, guid) is { } key) {
				DrawPick(source, key, drawn);
			}
		}

		if (why == null) {
			EditorText.Note("Choosing a table, or None, changes what every later random pick comes out as.");
		}

		ImGui.PopID();
	}

	// What record <key> drew from table <table> in the state shown.
	private void DrawPick(MissionDocument source, MsnRecordId key, short table) {
		var read = VariantEditing.Table(source, table);
		short? range = VariantEditing.RangeDrawn(source, key);
		int at = read?.Alternatives.ToList().FindIndex(alternative => alternative.Range == range) ?? -1;
		if (read != null && at >= 0) {
			ImGui.TextWrapped($"In the campaign state shown it draws alternative {at + 1}: {_summary(source.RecordAt(read.Alternatives[at].Record)!)}.");
		} else if (!ConditionEditing.Outcome(source, table).Survived) {
			EditorText.Note("In the campaign state shown it draws nothing: the table is left out there, so it keeps its own "
				+ "fields, and the point, heading and actions those reference are not looked up.");
		} else {
			EditorText.Note("In the campaign state shown its draw takes no alternative, so it keeps its own fields, and the "
				+ "point, heading and actions those reference are not looked up.");
		}
	}

	// Each alternative with its chance, its summary and Remove; what is left over; Add alternative.
	private void DrawAlternatives(MissionDocument source, VariantTable table, string? why) {
		var percents = VariantEditing.Percents(table);
		bool whole = table.Bound == VariantEditing.PercentBound
			&& table.Alternatives.All(alternative => table.Percent(alternative.Draws) % 1 == 0);
		ImGui.Text("Alternatives:");
		for (int i = 0; i < table.Alternatives.Count; i++) {
			var alternative = table.Alternatives[i];
			ImGui.PushID(i);
			ImGui.AlignTextToFramePadding();
			ImGui.Text($"{i + 1}.");
			ImGui.SameLine();
			ImGui.SetNextItemWidth(ScaledImGui.Scaled(48f));
			if (PercentField($"chance{table.Guid}.{i}", percents[i], why != null) is { } typed) {
				var asked = percents.ToArray();
				asked[i] = typed;
				_editor.RequestChances(table.Guid, asked);
			}

			ImGui.SameLine();
			ImGui.Text("%");
			ImGui.SameLine();
			ImGui.TextWrapped(_summary(source.RecordAt(alternative.Record)!));
			if (why == null && table.Alternatives.Count > 1) {
				ImGui.SameLine();
				if (ImGui.SmallButton("Remove")) {
					_editor.RequestRemove(table.Guid, alternative.Range);
				}
			}

			ImGui.PopID();
		}

		if (!whole) {
			string exact = string.Join(", ", table.Alternatives.Select(alternative => $"{table.Percent(alternative.Draws):0.#}%"));
			EditorText.Note($"Its chances now: {exact}, of draws below {table.Bound}. A change writes every chance as the "
				+ "whole percent shown, of draws below 100, which changes no other draw.");
		}

		if (table.Leftover > 0) {
			string noun = table.Row switch { 12 => "herc", 13 => "flyer", 14 => "structure", _ => "record" };
			ImGui.TextWrapped($"Left over: {table.Percent(table.Leftover):0.#}%");
			EditorText.Note($"A draw there takes no alternative: a {noun} drawing it keeps its own fields, and the point, "
				+ "heading and actions those reference are not looked up.");
		}

		if (why == null) {
			string? noAdd = VariantEditing.WhyNoAdd(source, table.Guid);
			ImGui.BeginDisabled(noAdd != null);
			if (ImGui.Button("Add alternative")) {
				_editor.RequestAdd(table.Guid, ShownRange(source, table));
			}

			ImGui.EndDisabled();
			EditorText.Note(noAdd ?? "A copy of the alternative the editor shows, taking half the largest chance.");
		}
	}

	// Which alternative the editor shows in place of the table's draw.
	private void DrawShown(MissionDocument source, VariantTable table, string? why) {
		short? shown = _campaign.ShownAlternative(table.Guid);
		int at = table.Alternatives.ToList().FindIndex(alternative => alternative.Range == shown);
		ImGui.Text("Shown in the editor:");
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ImGui.BeginCombo("##shown", at >= 0 ? AlternativeName(source, table, at) : "As drawn")) {
			if (ImGui.Selectable("As drawn", at < 0) && at >= 0) {
				_campaign.RequestShown(table.Guid, null);
			}

			for (int i = 0; i < table.Alternatives.Count; i++) {
				if (ImGui.Selectable(AlternativeName(source, table, i), i == at) && i != at) {
					_campaign.RequestShown(table.Guid, table.Alternatives[i].Range);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Note("Every record drawing from it shows that alternative, here only: it is not saved, and Draw again "
			+ "goes back to the draws.");
		EditorText.Reason(_campaign.Failure);
	}

	// The records drawing from the table's key, one identity link per line with what each drew in the state shown.
	private void DrawDrawers(MissionDocument source, VariantTable table) {
		ImGui.Text("Drawn by:");
		var drawers = VariantEditing.Drawers(source, table.Key);
		int leftOut = 0;
		ImGui.PushID("drawers");
		foreach (var drawer in drawers) {
			if (source.Provenance.ExportedSlot(drawer) is not { } slot) {
				leftOut++;
				continue;
			}

			var use = _conditions.Identity(drawer.Row, slot.Index);
			ImGui.PushID($"{drawer.Row}.{drawer.Index}");
			if (use.Target is { } target) {
				_links.Link(use.Label, target);
			} else {
				ImGui.Text(use.Label);
			}

			int at = table.Alternatives.ToList().FindIndex(alternative => alternative.Range == VariantEditing.RangeDrawn(source, drawer));
			ImGui.SameLine(0f, 0f);
			ImGui.Text(at >= 0 ? $": alternative {at + 1}" : ": none");
			ImGui.PopID();
		}

		ImGui.PopID();
		if (drawers.Count == 0) {
			EditorText.Note("Nothing.");
		} else if (leftOut > 0) {
			ImGui.TextWrapped($"{leftOut} more, which this state of the mission leaves out");
		}
	}

	// The other tables under its key, as links.
	private void DrawSameKey(MissionDocument source, VariantTable table) {
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

	// Delete, or why it cannot be deleted.
	private void DrawDelete(MissionDocument source, VariantTable table, string? why) {
		if (why != null) {
			return;
		}

		if (VariantEditing.WhyNoDelete(source, table.Guid) is { } reason) {
			ImGui.TextWrapped(reason == "Records draw from it."
				? "Cannot be deleted while the records under Drawn by draw from it."
				: $"Cannot be deleted: {reason}");
			return;
		}

		if (ImGui.Button("Delete")) {
			_editor.RequestDelete(table.Guid);
		}
	}

	// The range of the alternative the editor shows for <table>: the one chosen to show, else the one the first record drawing
	// from it drew in the state shown, else the first.
	private short ShownRange(MissionDocument source, VariantTable table) {
		var ranges = table.Alternatives.Select(alternative => alternative.Range).ToHashSet();
		return _campaign.ShownAlternative(table.Guid)
			?? VariantEditing.Drawers(source, table.Key).Select(drawer => VariantEditing.RangeDrawn(source, drawer))
				.FirstOrDefault(range => range is { } drawn && ranges.Contains(drawn))
			?? table.Alternatives[0].Range;
	}

	private string AlternativeName(MissionDocument source, VariantTable table, int at) =>
		$"Alternative {at + 1}: {_summary(source.RecordAt(table.Alternatives[at].Record)!)}";

	private static string TableName(MissionDocument source, short table) =>
		VariantEditing.Table(source, table) is { } read
			? $"Condition {table}: table of {read.Alternatives.Count} alternative(s)"
			: $"Condition {table}";

	/// <summary>What the view of <paramref name="placed"/> calls it: herc, flyer, ground vehicle or structure.</summary>
	internal static string Noun(SceneObject placed) => placed.Placement.Kind switch {
		MissionUnitKind.Mech => "herc",
		MissionUnitKind.Flyer => "flyer",
		_ when placed.Object is Sim.BaseObject { Type.IsVehicle: true } => "ground vehicle",
		_ => "structure",
	};

	// A typed whole percent, 0 to 100; answers the value when the field lets go after a change.
	private int? PercentField(string id, int current, bool locked) {
		if (_draftFor != null && _draftFor != id && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		int shown = _draftFor == id ? _draft : current;
		ImGui.BeginDisabled(locked);
		if (ImGui.InputInt($"##{id}", ref shown, 0, 0)) {
			_draftFor = id;
			_draft = shown;
		}

		int? typed = null;
		if (ImGui.IsItemDeactivatedAfterEdit() && _draftFor == id) {
			_draftFor = null;
			int value = Math.Clamp(_draft, 0, 100);
			if (value != current) {
				typed = value;
			}
		}

		ImGui.EndDisabled();
		return typed;
	}
}
