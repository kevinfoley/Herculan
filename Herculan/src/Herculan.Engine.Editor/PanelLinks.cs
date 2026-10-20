using System.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's links: a clickable label per kind of record that selects it, so a view is also how a mission's
/// wiring is walked. A view names a record by its index in the generated <c>script.dat</c>, and the link selects the
/// <c>.MSN</c> record that created it (<see cref="GeneratedRecords"/>). Each label is its own ImGui ID, unique within a
/// view's ID scope (the views push one per repeated block).
/// </summary>
internal sealed class PanelLinks {
	private static readonly Vector4 LinkColor = new(0.55f, 0.78f, 1f, 1f);

	private readonly MissionIndex _index;
	private readonly SelectionState _selection;

	public PanelLinks(MissionIndex index, SelectionState selection) {
		_index = index;
		_selection = selection;
	}

	private Mission Mission => _index.Mission;

	private GeneratedRecords Records => _index.Records;

	/// <summary>A clickable label that selects <paramref name="target"/>.</summary>
	public void Link(string label, EditorSelection target) {
		ImGui.PushStyleColor(ImGuiCol.Text, LinkColor);
		if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(label))) {
			_selection.Current = target;
		}

		ImGui.PopStyleColor();
	}

	public void Object(SceneObject placed) => Link(MissionText.Describe(placed), new ObjectSelection(placed));

	/// <summary>Block-11 group <paramref name="group"/>.</summary>
	public void Group(int group) => Link(MissionText.DescribeGroup(_index, group), Records.GroupAt(group));

	/// <summary>A group by its identity alone, as a reference list shows it.</summary>
	public void ShortGroup(int group) => Link(ShortGroupName(group), Records.GroupAt(group));

	/// <summary>A group's identity alone, as a reference list shows it.</summary>
	public static string ShortGroupName(int group) => group == 0 ? "Group 0 (player squad)" : $"Group {group}";

	/// <summary>Block-5 action <paramref name="action"/>.</summary>
	public void Action(int action) => Link($"Action {action}", Records.ActionAt(action));

	/// <summary>Block-4 trigger area <paramref name="area"/>.</summary>
	public void Area(int area) {
		string shape = _index.Area(area)?.Shape == MissionTriggerShape.Circle ? "circle" : "box";
		Link($"Area {area} ({shape})", Records.AreaAt(area));
	}

	/// <summary>Block-3 route <paramref name="route"/>.</summary>
	public void Route(int route) => Link($"Route {route}", Records.RouteAt(route));

	/// <summary>Block-6 timer <paramref name="timer"/>, with its delay and what starts it.</summary>
	public void Timer(int timer) =>
		Link($"Timer {timer} ({MissionText.TimerSummary(Mission.ActionTimers[timer])})", Records.TimerAt(timer));

	/// <summary>A timer by its identity alone, as a reference list shows it.</summary>
	public void ShortTimer(int timer) => Link($"Timer {timer}", Records.TimerAt(timer));

	/// <summary>Block-12 objective <paramref name="objective"/>.</summary>
	public void Objective(int objective) =>
		Link(MissionText.DescribeObjective(Mission.Objectives[objective], objective), Records.ObjectiveAt(objective));

	/// <summary>An objective by its identity alone, as a reference list shows it.</summary>
	public void ShortObjective(int objective) => Link($"Objective {objective}", Records.ObjectiveAt(objective));

	/// <summary>The object in roster slot <paramref name="slot"/> of <paramref name="kind"/>, or a note when nothing was placed there.</summary>
	public void Slot(MissionUnitKind kind, int slot) {
		if (_index.ObjectAt(kind, slot) is { } placed) {
			Object(placed);
		} else {
			EditorText.Note($"{kind.ToString().ToLowerInvariant()} slot {slot} (not placed)");
		}
	}

	/// <summary>An order's subject.</summary>
	public void Subject(MissionOrderSubject kind, int reference) {
		switch (kind) {
			case MissionOrderSubject.Group:
				Group(reference);
				break;
			case MissionOrderSubject.Mech:
				Slot(MissionUnitKind.Mech, reference);
				break;
			case MissionOrderSubject.Flyer:
				Slot(MissionUnitKind.Flyer, reference);
				break;
			default:
				Slot(MissionUnitKind.Base, reference);
				break;
		}
	}
}
