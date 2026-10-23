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

	/// <summary>A clickable label that selects <paramref name="target"/>.</summary>
	public void Link(string label, EditorSelection target) {
		ImGui.PushStyleColor(ImGuiCol.Text, LinkColor);
		if (ImGui.Selectable(label, false, ImGuiSelectableFlags.None, ImGui.CalcTextSize(label))) {
			_selection.Current = target;
		}

		ImGui.PopStyleColor();
	}

	/// <summary>
	/// A clickable <paramref name="name"/> that selects <paramref name="target"/>: its text cut to the space left on the line
	/// and its number dimmed after it, the whole name on hover when cut.
	/// </summary>
	public void Link(RecordName name, EditorSelection target) {
		string text = NameText.FitText(name, ImGui.GetContentRegionAvail().X);
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

	/// <summary>Row-10 GUID <paramref name="guid"/>, a trigger; a note when the layout has none.</summary>
	public void Trigger(short guid) {
		if (_scene.Action(guid) != null) {
			Link(Names.Trigger(guid), new ActionSelection(guid));
		} else {
			EditorText.Note("A trigger not in the mission");
		}
	}

	/// <summary>Row-11 GUID <paramref name="guid"/>, a countdown, by its delay and what starts it.</summary>
	public void Countdown(short guid) {
		if (_scene.Layout.Entry<ActionTimer30>(guid) != null) {
			Link(Names.Countdown(guid), new TimerSelection(guid));
		}
	}

	/// <summary>Row-16 GUID <paramref name="guid"/>, a group, by its team, roster and members.</summary>
	public void LayoutGroup(short guid) {
		if (_scene.Group(guid) != null) {
			Link(Names.Group(guid), new GroupSelection(guid));
		}
	}

	/// <summary>A group by its team and roster alone, as a reference list or a sentence shows it: "Cybrid hercs (group 5)".</summary>
	public void ShortLayoutGroup(short guid) {
		if (_scene.Group(guid) != null) {
			Link(Names.GroupBrief(guid), new GroupSelection(guid));
		}
	}

	/// <summary>
	/// The roster record <paramref name="key"/> by <see cref="MissionNames.Object"/>, a link when the scene has its object;
	/// plain text otherwise, a record no group places included.
	/// </summary>
	public void Object(ObjectKey key) {
		var name = Names.Object(key);
		if (_scene.ObjectOf(key) != null) {
			Link(name, new ObjectSelection(key.Row, key.Guid));
		} else {
			NameText.Draw(name);
		}
	}

	/// <summary>The scene object <paramref name="placed"/>, by the record it stands for.</summary>
	public void Object(SceneObject placed) {
		if (_scene.KeyOf(placed) is { } key) {
			Object(key);
		}
	}

	/// <summary>Row-9 GUID <paramref name="guid"/>, a trigger area, by its shape and who enters it.</summary>
	public void LayoutArea(short guid) => Link(Names.Area(guid), new AreaSelection(guid));

	/// <summary>Row-8 GUID <paramref name="guid"/>, a route, by its shape and who walks it.</summary>
	public void LayoutRoute(short guid) => Link(Names.Route(guid), new RouteSelection(guid));

	/// <summary>Row-17 record <paramref name="record"/>, an objective, as its sentence.</summary>
	public void Objective(int record) => Link(Names.Objective(record), new ObjectiveSelection(record));

	/// <summary>
	/// What references something, one short identity line per record (docs/herculan/handoff-editor.md#what-every-edit-does):
	/// each objective and trigger by its link; each trigger of <paramref name="fired"/>, those an object fires when engaged or
	/// defeated, by its link and ": fired by it"; and each group by its link and what of it references, its orders and, for a
	/// group of <paramref name="ownRoute"/>, its own route field, which no view edits, on its one line. Then the options of
	/// random choices and the orders no group runs, counted.
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
			ImGui.PushID($"fired{trigger}");
			Trigger(trigger);
			ImGui.SameLine(0f, 0f);
			ImGui.TextUnformatted(": fired by it");
			ImGui.PopID();
		}

		var groups = names.Orders.Select(order => order.Group).Concat(ownRoute ?? []).Distinct().ToList();
		foreach (short group in groups) {
			var parts = names.Orders.Where(order => order.Group == group).Select(order => $"{MissionText.Ordinal(order.Slot + 1)} order").ToList();
			if (ownRoute?.Contains(group) == true) {
				parts.Add("its own route (not editable yet)");
			}

			ImGui.PushID($"group{group}");
			ShortLayoutGroup(group);
			ImGui.SameLine(0f, 0f);
			ImGui.TextUnformatted($": {string.Join(", ", parts)}");
			ImGui.PopID();
		}

		Counted(names.Options, "option of a random choice", "options of random choices");
		Counted(names.UnrunOrders, "order no group runs", "orders no group runs");
	}

	// "1 option of a random choice", "3 options of random choices"; nothing for none.
	private static void Counted(int count, string one, string several) {
		if (count > 0) {
			ImGui.TextWrapped(count == 1 ? $"1 {one}" : $"{count} {several}");
		}
	}
}
