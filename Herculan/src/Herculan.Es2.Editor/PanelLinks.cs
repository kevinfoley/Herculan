using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Es2.Scene;
using ImGuiNET;

namespace Herculan.Es2.Editor;

/// <summary>
/// The Properties panel's links: a clickable label per kind of record that selects it, so a view is also how a mission's
/// wiring is walked. Each record is named by <see cref="MissionNames"/>, from the layout by its GUID, its text cut to fit the
/// line and its number dimmed after it (<see cref="NameText"/>). Each label is its own ImGui ID, unique within a view's ID
/// scope (the views push one per repeated block).
/// </summary>
internal sealed class PanelLinks {
	private static readonly Vector4 LinkColor = new(0.55f, 0.78f, 1f, 1f);

	private readonly MissionIndex _scene;
	private readonly SelectionState _selection;

	public PanelLinks(MissionIndex scene, MissionNames names, SelectionState selection) {
		_scene = scene;
		Names = names;
		_selection = selection;
	}

	/// <summary>What the links name records by.</summary>
	public MissionNames Names { get; }

	/// <summary>A clickable label that selects a target, cut to fit as a name is.</summary>
	/// <param name="label">The label.</param>
	/// <param name="target">The selection.</param>
	/// <param name="reserve">The width kept free after it for what the caller draws on the same line.</param>
	public void Link(string label, EditorSelection target, float reserve = 0f) => Link(new RecordName(label), target, reserve);

	/// <summary>
	/// A clickable name that selects a target: its text cut to the space left on the line
	/// and its number dimmed after it, the whole name on hover when cut.
	/// </summary>
	/// <param name="name">The name.</param>
	/// <param name="target">The selection.</param>
	/// <param name="reserve">The width kept free after it for what the caller draws on the same line (<see cref="EditorText.ButtonRoom"/>, <see cref="EditorText.AfterRoom"/>):
	/// without it, that runs past the panel's edge.</param>
	public void Link(RecordName name, EditorSelection target, float reserve = 0f) {
		string text = NameText.FitText(name, ImGui.GetContentRegionAvail().X - reserve);
		ImGui.PushStyleColor(ImGuiCol.Text, LinkColor);
		if (ImGui.Selectable($"{text}##{name.Full}", false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(text))) {
			_selection.Current = target;
		}

		ImGui.PopStyleColor();
		NameText.Hover(name, text);
		if (name.Number is { } number) {
			ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
			ImGui.TextDisabled(number);
		}
	}

	// Each record's link below keeps <reserve> pixels free after it, as Link does.

	/// <summary>A row-10 trigger; a note when the layout has none.</summary>
	/// <param name="guid">The trigger's row-10 GUID.</param>
	/// <param name="reserve">The width kept free after it.</param>
	public void Trigger(short guid, float reserve = 0f) {
		if (_scene.Action(guid) != null) {
			Link(Names.Trigger(guid), new ActionSelection(guid), reserve);
		} else {
			EditorText.Note("A trigger not in the mission");
		}
	}

	/// <summary>A row-11 countdown, by its delay and what starts it.</summary>
	/// <param name="guid">The countdown's row-11 GUID.</param>
	/// <param name="reserve">The width kept free after it.</param>
	public void Countdown(short guid, float reserve = 0f) {
		if (_scene.Layout.Entry<ActionTimer30>(guid) != null) {
			Link(Names.Countdown(guid), new TimerSelection(guid), reserve);
		}
	}

	/// <summary>A row-16 group, by its team, roster and members.</summary>
	/// <param name="guid">The group's row-16 GUID.</param>
	/// <param name="reserve">The width kept free after it.</param>
	public void LayoutGroup(short guid, float reserve = 0f) {
		if (_scene.Group(guid) != null) {
			Link(Names.Group(guid), new GroupSelection(guid), reserve);
		}
	}

	/// <summary>A group by its team and roster alone, as a reference list or a sentence shows it: "Cybrid hercs (group 5)".</summary>
	public void ShortLayoutGroup(short guid, float reserve = 0f) {
		if (_scene.Group(guid) != null) {
			Link(Names.GroupBrief(guid), new GroupSelection(guid), reserve);
		}
	}

	/// <summary>
	/// A roster record by <see cref="MissionNames.Object"/>, a link when the scene has its object;
	/// plain text otherwise, a record no group places included.
	/// </summary>
	/// <param name="key">The roster record.</param>
	/// <param name="reserve">The width kept free after it.</param>
	public void Object(ObjectKey key, float reserve = 0f) {
		var name = Names.Object(key);
		if (_scene.ObjectOf(key) != null) {
			Link(name, new ObjectSelection(key.Row, key.Guid), reserve);
		} else {
			NameText.Draw(name, reserve);
		}
	}

	/// <summary>A scene object, by the record it stands for.</summary>
	/// <param name="placed">The object.</param>
	/// <param name="reserve">The width kept free after it.</param>
	public void Object(SceneObject placed, float reserve = 0f) {
		if (_scene.KeyOf(placed) is { } key) {
			Object(key, reserve);
		}
	}

	/// <summary>A row-9 trigger area, by its shape and who enters it.</summary>
	/// <param name="guid">The area's row-9 GUID.</param>
	public void LayoutArea(short guid) => Link(Names.Area(guid), new AreaSelection(guid));

	/// <summary>A row-8 route, by its shape and who walks it.</summary>
	/// <param name="guid">The route's row-8 GUID.</param>
	public void LayoutRoute(short guid) => Link(Names.Route(guid), new RouteSelection(guid));

	/// <summary>A row-17 objective, as its sentence.</summary>
	/// <param name="record">The record's index in row 17.</param>
	public void Objective(int record) => Link(Names.Objective(record), new ObjectiveSelection(record));

	/// <summary>
	/// What references something, one short identity line per record (docs/herculan/editor-design.md#deleting):
	/// each objective and trigger by its link; each trigger a fired list names, those an object fires when engaged or
	/// defeated, by its link and ": fired by it"; and each group by its link and what of it references, its orders and, for a
	/// group of an own-route list, its own route field, which no view edits, on its one line. Then the options of
	/// random tables and the orders no group runs, counted.
	/// </summary>
	/// <param name="names">What references it.</param>
	/// <param name="ownRoute">The groups whose own route field references it, or null for none.</param>
	/// <param name="fired">The triggers an object fires, or null for none.</param>
	public void References(DeleteNamers names, IReadOnlyList<short>? ownRoute = null, IReadOnlyList<short>? fired = null) {
		foreach (int objective in names.Objectives) {
			ImGui.PushID($"objective{objective}");
			Objective(objective);
			ImGui.PopID();
		}

		foreach (short trigger in names.Triggers) {
			ImGui.PushID($"target{trigger}");
			Trigger(trigger);
			ImGui.PopID();
		}

		foreach (short trigger in fired ?? []) {
			const string firedBy = ": fired by it";
			ImGui.PushID($"fired{trigger}");
			Trigger(trigger, EditorText.AfterRoom(firedBy));
			EditorText.After(firedBy);
			ImGui.PopID();
		}

		var groups = names.Orders.Select(order => order.Group).Concat(ownRoute ?? []).Distinct().ToList();
		foreach (short group in groups) {
			var parts = names.Orders.Where(order => order.Group == group).Select(order => $"{MissionText.Ordinal(order.Slot + 1)} order").ToList();
			if (ownRoute?.Contains(group) == true) {
				parts.Add("its own route (not editable yet)");
			}

			string what = $": {string.Join(", ", parts)}";
			ImGui.PushID($"group{group}");
			ShortLayoutGroup(group, EditorText.AfterRoom(what));
			EditorText.After(what);
			ImGui.PopID();
		}

		Counted(names.Templates, "template of a random table", "templates of random tables");
		Counted(names.UnrunOrders, "order no group runs", "orders no group runs");
	}

	// "1 template of a random table", "3 templates of random tables"; nothing for none.
	private static void Counted(int count, string one, string several) {
		if (count > 0) {
			ImGui.TextWrapped(count == 1 ? $"1 {one}" : $"{count} {several}");
		}
	}
}
