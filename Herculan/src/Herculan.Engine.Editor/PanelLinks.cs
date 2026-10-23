using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's links: a clickable label per kind of record that selects it, so a view is also how a mission's
/// wiring is walked. A view on the layout names a record by its GUID (<see cref="Trigger"/>, <see cref="Countdown"/>,
/// <see cref="LayoutGroup"/>, <see cref="Object(ObjectKey)"/>, <see cref="LayoutArea"/>), labelled by its place in its row; a
/// view still reading the generation names one by its index in the generated <c>script.dat</c>, and the link selects the
/// <c>.MSN</c> record that created it (<see cref="GeneratedRecords"/>). Each label is its own ImGui ID, unique within a
/// view's ID scope (the views push one per repeated block).
/// </summary>
internal sealed class PanelLinks {
	private static readonly Vector4 LinkColor = new(0.55f, 0.78f, 1f, 1f);

	private readonly GeneratedIndex _index;
	private readonly SelectionState _selection;

	public PanelLinks(GeneratedIndex index, SelectionState selection) {
		_index = index;
		_selection = selection;
	}

	private Mission Mission => _index.Mission;

	private GeneratedRecords Records => _index.Records;

	/// <summary>The scene's index over the layout, which the links by GUID read.</summary>
	private MissionIndex Scene => _index.Scene;

	/// <summary>A clickable label that selects <paramref name="target"/>.</summary>
	public void Link(string label, EditorSelection target) {
		ImGui.PushStyleColor(ImGuiCol.Text, LinkColor);
		if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(label))) {
			_selection.Current = target;
		}

		ImGui.PopStyleColor();
	}

	public void Object(SceneObject placed) {
		if (_index.Scene.SelectionOf(placed) is { } selection) {
			Link(MissionText.Describe(placed), selection);
		} else {
			ImGui.Text(MissionText.Describe(placed));
		}
	}

	/// <summary>Block-11 group <paramref name="group"/>.</summary>
	public void Group(int group) => Link(MissionText.DescribeGroup(_index, group), Records.GroupAt(group));

	/// <summary>A group by its identity alone, as a reference list shows it.</summary>
	public void ShortGroup(int group) => Link(ShortGroupName(group), Records.GroupAt(group));

	/// <summary>A group's identity alone, as a reference list shows it.</summary>
	public static string ShortGroupName(int group) => group == 0 ? "Group 0 (player squad)" : $"Group {group}";

	/// <summary>Block-5 action <paramref name="action"/>, as the trigger its record is (<see cref="Trigger"/>).</summary>
	public void Action(int action) => Trigger(Records.ActionGuid(action));

	/// <summary>Row-10 GUID <paramref name="guid"/>, a trigger, by <see cref="MissionText.TriggerName"/>; a note when the layout has none.</summary>
	public void Trigger(short guid) {
		if (Scene.Action(guid) != null) {
			Link(MissionText.TriggerName(Scene.Layout, guid), new ActionSelection(guid));
		} else {
			EditorText.Note("A trigger not in the mission");
		}
	}

	/// <summary>Row-11 GUID <paramref name="guid"/>, a countdown, with its delay and what starts it.</summary>
	public void Countdown(short guid) {
		if (Scene.Layout.Entry<ActionTimer30>(guid) is { } countdown) {
			Link($"{MissionText.CountdownName(Scene.Layout, guid)} ({MissionText.CountdownSummary(Scene, countdown.Fields)})",
				new TimerSelection(guid));
		}
	}

	/// <summary>A countdown by its identity alone, as a reference list shows it.</summary>
	public void ShortCountdown(short guid) => Link(MissionText.CountdownName(Scene.Layout, guid), new TimerSelection(guid));

	/// <summary>Row-16 GUID <paramref name="guid"/>, a group of the layout, by <see cref="MissionText.GroupName"/>.</summary>
	public void LayoutGroup(short guid) {
		if (Scene.Group(guid) is { } group) {
			Link(MissionText.GroupName(group), new GroupSelection(guid));
		}
	}

	/// <summary>A group of the layout by its identity alone, as a reference list shows it: "Group 5".</summary>
	public void ShortLayoutGroup(short guid) {
		if (Scene.Group(guid) is { } group) {
			Link(group.Number == 0 ? "Group 0 (player squad)" : $"Group {group.Number}", new GroupSelection(guid));
		}
	}

	/// <summary>
	/// The roster record <paramref name="key"/> by <see cref="MissionText.ObjectName"/>, a link when the scene has its object;
	/// plain text otherwise.
	/// </summary>
	public void Object(ObjectKey key) {
		if (Scene.Plan.Object(key) is not { } planned) {
			ImGui.TextUnformatted("A record no group places");
			return;
		}

		string name = MissionText.ObjectName(Scene, planned);
		if (Scene.ObjectOf(key) != null) {
			Link(name, new ObjectSelection(key.Row, key.Guid));
		} else {
			ImGui.TextUnformatted(name);
		}
	}

	/// <summary>Block-4 trigger area <paramref name="area"/>.</summary>
	public void Area(int area) {
		string shape = _index.Area(area)?.Shape == MissionTriggerShape.Circle ? "circle" : "box";
		Link($"Area {area} ({shape})", Records.AreaAt(area));
	}

	/// <summary>Row-9 GUID <paramref name="guid"/>, a trigger area, by its place in row 9 and its shape.</summary>
	public void LayoutArea(short guid) => Link(AreaName(Scene, guid), new AreaSelection(guid));

	/// <summary>Row-9 GUID <paramref name="guid"/> by its place in row 9 and its shape: "Area 3 (circle)".</summary>
	public static string AreaName(MissionIndex index, short guid) {
		int? number = MissionText.NumberIn(index.Layout, 9, guid);
		string shape = index.Area(guid)?.Area.Shape is { } placed
			? placed == MissionTriggerShape.Circle ? "circle" : "box"
			: index.Layout.Entry<TriggerArea12>(guid)?.Fields.Shape is { } stored ? stored != 0 ? "circle" : "box" : "missing";
		return number is { } at ? $"Area {at} ({shape})" : "An area not in the mission";
	}

	/// <summary>Block-3 route <paramref name="route"/>.</summary>
	public void Route(int route) => Link($"Route {route}", Records.RouteAt(route));

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
