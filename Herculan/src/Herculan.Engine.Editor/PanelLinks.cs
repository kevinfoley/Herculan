using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Scene;
using ImGuiNET;

namespace Herculan.Engine.Editor;

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

	/// <summary>A clickable <paramref name="label"/> that selects <paramref name="target"/>, cut to fit as a name is.</summary>
	public void Link(string label, EditorSelection target, float reserve = 0f) => Link(new RecordName(label), target, reserve);

	/// <summary>
	/// A clickable <paramref name="name"/> that selects <paramref name="target"/>: its text cut to the space left on the line
	/// and its number dimmed after it, the whole name on hover when cut. <paramref name="reserve"/> is the width kept free
	/// after it for what the caller draws on the same line (<see cref="EditorText.ButtonRoom"/>, <see cref="EditorText.AfterRoom"/>):
	/// without it, that runs past the panel's edge.
	/// </summary>
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

	/// <summary>Row-10 GUID <paramref name="guid"/>, a trigger; a note when the layout has none.</summary>
	public void Trigger(short guid, float reserve = 0f) {
		if (_scene.Action(guid) != null) {
			Link(Names.Trigger(guid), new ActionSelection(guid), reserve);
		} else {
			EditorText.Note("A trigger not in the mission");
		}
	}

	/// <summary>Row-11 GUID <paramref name="guid"/>, a countdown, by its delay and what starts it.</summary>
	public void Countdown(short guid, float reserve = 0f) {
		if (_scene.Layout.Entry<ActionTimer30>(guid) != null) {
			Link(Names.Countdown(guid), new TimerSelection(guid), reserve);
		}
	}

	/// <summary>Row-16 GUID <paramref name="guid"/>, a group, by its team, roster and members.</summary>
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
	/// The roster record <paramref name="key"/> by <see cref="MissionNames.Object"/>, a link when the scene has its object;
	/// plain text otherwise, a record no group places included.
	/// </summary>
	public void Object(ObjectKey key, float reserve = 0f) {
		var name = Names.Object(key);
		if (_scene.ObjectOf(key) != null) {
			Link(name, new ObjectSelection(key.Row, key.Guid), reserve);
		} else {
			NameText.Draw(name, reserve);
		}
	}

	/// <summary>The scene object <paramref name="placed"/>, by the record it stands for.</summary>
	public void Object(SceneObject placed, float reserve = 0f) {
		if (_scene.KeyOf(placed) is { } key) {
			Object(key, reserve);
		}
	}

	/// <summary>Row-9 GUID <paramref name="guid"/>, a trigger area, by its shape and who enters it.</summary>
	public void LayoutArea(short guid) => Link(Names.Area(guid), new AreaSelection(guid));

	/// <summary>Row-8 GUID <paramref name="guid"/>, a route, by its shape and who walks it.</summary>
	public void LayoutRoute(short guid) => Link(Names.Route(guid), new RouteSelection(guid));

	/// <summary>Row-17 record <paramref name="record"/>, an objective, as its sentence.</summary>
	public void Objective(int record) => Link(Names.Objective(record), new ObjectiveSelection(record));

	/// <summary>
	/// What references something, one short identity line per record (docs/herculan/editor-design.md#deleting):
	/// each objective and trigger by its link; each trigger of <paramref name="fired"/>, those an object fires when engaged or
	/// defeated, by its link and ": fired by it"; and each group by its link and what of it references, its orders and, for a
	/// group of <paramref name="ownRoute"/>, its own route field, which no view edits, on its one line. Then the options of
	/// random tables and the orders no group runs, counted.
	/// </summary>
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
