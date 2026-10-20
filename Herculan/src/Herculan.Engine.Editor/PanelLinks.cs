using System.Numerics;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's links: a clickable label per kind of record that selects it, so a view is also how a mission's
/// wiring is walked. Each label is its own ImGui ID, unique within a view's ID scope (the views push one per repeated block).
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

	/// <summary>A clickable label that selects <paramref name="target"/>.</summary>
	public void Link(string label, EditorSelection target) {
		ImGui.PushStyleColor(ImGuiCol.Text, LinkColor);
		if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(label))) {
			_selection.Current = target;
		}

		ImGui.PopStyleColor();
	}

	public void Object(SceneObject placed) => Link(MissionText.Describe(placed), new ObjectSelection(placed));

	public void Group(int group) => Link(MissionText.DescribeGroup(_index, group), new GroupSelection(group));

	/// <summary>A group by its identity alone, as a reference list shows it.</summary>
	public void ShortGroup(int group) => Link(ShortGroupName(group), new GroupSelection(group));

	/// <summary>A group's identity alone, as a reference list shows it.</summary>
	public static string ShortGroupName(int group) => group == 0 ? "Group 0 (player squad)" : $"Group {group}";

	public void Action(int action) => Link($"Action {action}", new ActionSelection(action));

	public void Area(int area) {
		string shape = _index.Area(area)?.Shape == MissionTriggerShape.Circle ? "circle" : "box";
		Link($"Area {area} ({shape})", new AreaSelection(area));
	}

	public void Route(int route) => Link($"Route {route}", new RouteSelection(route));

	/// <summary>A timer with its delay and what starts it.</summary>
	public void Timer(int timer) =>
		Link($"Timer {timer} ({MissionText.TimerSummary(Mission.ActionTimers[timer])})", new TimerSelection(timer));

	/// <summary>A timer by its identity alone, as a reference list shows it.</summary>
	public void ShortTimer(int timer) => Link($"Timer {timer}", new TimerSelection(timer));

	public void Objective(int objective) =>
		Link(MissionText.DescribeObjective(Mission.Objectives[objective], objective), new ObjectiveSelection(objective));

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
