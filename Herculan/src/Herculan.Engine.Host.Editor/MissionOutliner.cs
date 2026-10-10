using System.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// The Mission panel down the left of the window: every group (with its members), trigger area,
/// action, timer, route and objective as a list, under the mission check's findings. It is the only way to reach the parts of a mission that are not
/// standing somewhere — an action with no area, a group not yet in the mission — and the other way
/// to make a selection.
/// </summary>
internal sealed class MissionOutliner {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	public const float PanelWidth = 300f;

	private static readonly Vector4 FindingLinkColor = new(0.55f, 0.78f, 1f, 1f);
	private static readonly Vector4 SelectedFindingColor = new(1f, 0.85f, 0.1f, 1f);

	private readonly MissionIndex _index;
	private readonly MissionLint _lint;
	private readonly SelectionState _selection;

	public MissionOutliner(MissionIndex index, MissionLint lint, SelectionState selection) {
		_index = index;
		_lint = lint;
		_selection = selection;
	}

	private Mission Mission => _index.Mission;

	public void Draw(Vector2 display, float menuBarHeight) {
		ImGui.SetNextWindowPos(new Vector2(0f, menuBarHeight));
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), display.Y - menuBarHeight));
		ImGui.Begin("Mission", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

		DrawProblems();
		DrawGroups();
		DrawAreas();
		DrawActions();
		DrawTimers();
		DrawRoutes();
		DrawObjectives();

		ImGui.End();
	}

	/// <summary>
	/// The mission check's findings, open by default when there are any. A finding whose record the
	/// editor can show selects it.
	/// </summary>
	private void DrawProblems() {
		var flags = _lint.Findings.Count > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
		if (!ImGui.CollapsingHeader($"Problems ({_lint.Findings.Count})###problems", flags)) {
			return;
		}

		if (_lint.Findings.Count == 0) {
			ImGui.TextDisabled("None found.");
			return;
		}

		// Wrapped text rather than a Selectable, whose label does not wrap and most messages are
		// wider than the panel.
		foreach (var (finding, target) in _lint.Findings) {
			if (target == null) {
				ImGui.BulletText(string.Empty);
				ImGui.SameLine();
				ImGui.TextWrapped(finding.Message);
				continue;
			}

			ImGui.BulletText(string.Empty);
			ImGui.SameLine();
			ImGui.PushStyleColor(ImGuiCol.Text, _selection.Is(target) ? SelectedFindingColor : FindingLinkColor);
			ImGui.TextWrapped(finding.Message);
			ImGui.PopStyleColor();
			if (ImGui.IsItemClicked()) {
				_selection.Current = target;
			}
		}
	}

	private void DrawTimers() {
		if (!ImGui.CollapsingHeader($"Timers ({Mission.ActionTimers.Count})###timers")) {
			return;
		}

		for (int i = 0; i < Mission.ActionTimers.Count; i++) {
			var timer = Mission.ActionTimers[i];
			var fires = timer.SequenceRefs.Where(reference => reference >= 0 && reference < Mission.Actions.Count);
			var selection = new TimerSelection(i);
			if (ImGui.Selectable($"Timer {i}: {MissionText.TimerSummary(timer)}, fires {string.Join(", ", fires)}##timer{i}",
					_selection.Is(selection))) {
				_selection.Current = selection;
			}
		}
	}

	/// <summary>
	/// The objectives the simulation tests (block 12), under the list the player is shown (block 13).
	/// Nothing reconciles the two, so they are listed apart.
	/// </summary>
	private void DrawObjectives() {
		if (!ImGui.CollapsingHeader($"Objectives ({Mission.Objectives.Count})###objectives")) {
			return;
		}

		if (Mission.ObjectiveTextRefs.Count > 0) {
			ImGui.TextDisabled("Briefing list:");
			foreach (int line in Mission.ObjectiveTextRefs) {
				ImGui.BulletText(Mission.TextAt(line));
			}

			ImGui.Spacing();
		}

		for (int i = 0; i < Mission.Objectives.Count; i++) {
			var selection = new ObjectiveSelection(i);
			if (ImGui.Selectable($"{MissionText.DescribeObjective(Mission.Objectives[i], i)}##objective{i}",
					_selection.Is(selection))) {
				_selection.Current = selection;
			}
		}
	}

	private void DrawGroups() {
		if (!ImGui.CollapsingHeader($"Groups ({_index.GroupMembers.Count})###groups")) {
			return;
		}

		for (int group = 0; group < _index.GroupMembers.Count; group++) {
			var members = _index.GroupMembers[group];
			string waiting = group < Mission.GroupDeploymentActions.Count && Mission.GroupDeploymentActions[group] >= 0
				? $", waits on action {Mission.GroupDeploymentActions[group]}"
				: string.Empty;

			var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
			if (_selection.Is(new GroupSelection(group))) {
				flags |= ImGuiTreeNodeFlags.Selected;
			}

			if (members.Count == 0) {
				flags |= ImGuiTreeNodeFlags.Leaf;
			}

			bool open = ImGui.TreeNodeEx($"{MissionText.DescribeGroup(Mission, group)}: {members.Count}{waiting}##group{group}", flags);
			if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) {
				_selection.Current = new GroupSelection(group);
			}

			if (!open) {
				continue;
			}

			foreach (var member in members) {
				var selection = new ObjectSelection(member);
				if (ImGui.Selectable($"{MissionText.Describe(member)}##m{group}.{member.Placement.SlotIndex}",
						_selection.Is(selection))) {
					_selection.Current = selection;
				}
			}

			ImGui.TreePop();
		}
	}

	private void DrawAreas() {
		if (!ImGui.CollapsingHeader($"Trigger areas ({_index.Areas.Count})###areas")) {
			return;
		}

		foreach (var area in _index.Areas) {
			string shape = area.Shape == MissionTriggerShape.Circle
				? $"circle, {MissionText.Distance(area.Radius)}"
				: "box";
			string actions = string.Join(", ", _index.ActionsTesting(area.Reference));
			var selection = new AreaSelection(area.Reference);
			if (ImGui.Selectable($"Area {area.Reference}: {shape}, action {actions}##area{area.Reference}",
					_selection.Is(selection))) {
				_selection.Current = selection;
			}
		}
	}

	private void DrawActions() {
		if (!ImGui.CollapsingHeader($"Actions ({Mission.Actions.Count})###actions")) {
			return;
		}

		for (int i = 0; i < Mission.Actions.Count; i++) {
			var action = Mission.Actions[i];
			var links = _index.LinksOf(i);
			string trigger = action.Areas.Count > 0 ? $"{action.Areas.Count} area(s)" : "no area";
			string effect = links.WaitingGroups.Count > 0 ? $", brings in {links.WaitingGroups.Count} group(s)" : string.Empty;
			var selection = new ActionSelection(i);
			if (ImGui.Selectable($"Action {i}: {trigger}{effect}##action{i}", _selection.Is(selection))) {
				_selection.Current = selection;
			}
		}
	}

	private void DrawRoutes() {
		if (!ImGui.CollapsingHeader($"Routes ({_index.Routes.Count})###routes")) {
			return;
		}

		foreach (var route in _index.Routes) {
			string walkers = route.Walked ? $"groups {string.Join(", ", route.WalkedBy)}" : "never walked";
			string shape = route.Closed ? "loop" : "open";
			var selection = new RouteSelection(route.Reference);
			if (ImGui.Selectable($"Route {route.Reference}: {shape}, {walkers}##route{route.Reference}",
					_selection.Is(selection))) {
				_selection.Current = selection;
			}
		}
	}
}
