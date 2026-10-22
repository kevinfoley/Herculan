using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Mission panel down the left of the window: the mission itself (its theater and time of day,
/// <see cref="MissionView"/>), then the mission check's findings, then every group (with its members), trigger area,
/// action, timer, route, objective and condition as a list, the trigger areas and routes with the buttons that start placing a new
/// one, and the actions, timers, objectives and conditions with the buttons that create one. It is the only way to reach the parts of a mission that are not
/// standing somewhere — an action with no area, a route with no waypoints — and the other way
/// to make a selection. A double-click also frames the record in the view (<see cref="SelectionState.Focus"/>).
///
/// <para>Every list is the <c>.MSN</c>'s layout (docs/herculan/handoff-editor-viewing.md#selection-and-panels): every record
/// of its row, a conditional one included, in layout order, numbered by its place in its row, each selecting its record by
/// GUID or row-17 index. A row whose record is not in every version of the mission is marked <see cref="NotAlways"/>.</para>
/// </summary>
internal sealed class MissionOutliner {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	public const float PanelWidth = 300f;

	private const int RouteRow = 8;
	private const int AreaRow = 9;
	private const int ActionRow = 10;
	private const int TimerRow = 11;
	private const int GroupRow = 16;

	/// <summary>
	/// What a row whose record is present only under a condition carries after its text, the words the "Present when" field
	/// every view opens with uses for the other case.
	/// </summary>
	private const string NotAlways = " (not always present)";

	private static readonly Vector4 FindingLinkColor = new(0.55f, 0.78f, 1f, 1f);
	private static readonly Vector4 SelectedFindingColor = new(1f, 0.85f, 0.1f, 1f);

	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private MissionLint _lint;
	private readonly SelectionState _selection;

	public MissionOutliner(MissionDocument source, MissionIndex index, MissionLint lint, SelectionState selection) {
		_source = source;
		_index = index;
		_lint = lint;
		_selection = selection;
	}

	private MissionLayout Layout => _index.Layout;

	/// <summary>Lists <paramref name="lint"/>'s findings in place of the last ones, for a mission built again.</summary>
	public void Recheck(MissionLint lint) => _lint = lint;

	/// <summary>The route-placing mode the Routes list's New route starts; set once the mission's tools are built.</summary>
	public RoutePlacing? Placing { get; set; }

	/// <summary>The area-placing mode the Trigger areas list's New box and New circle start; set once the mission's tools are built.</summary>
	public AreaPlacing? AreaPlacing { get; set; }

	/// <summary>What the Objectives list's New objective asks to create an objective; set once the mission's tools are built.</summary>
	public ObjectiveEditor? Objectives { get; set; }

	/// <summary>What the Actions list's New action asks to create an action; set once the mission's tools are built.</summary>
	public ActionEditor? Actions { get; set; }

	/// <summary>What the Timers list's New timer asks to create a timer; set once the mission's tools are built.</summary>
	public TimerEditor? Timers { get; set; }

	/// <summary>What the Conditions list reads and its New flag test and New draw ask; set once the mission's tools are built.</summary>
	public ConditionEditor? Conditions { get; set; }

	public void Draw(Vector2 display, float menuBarHeight) {
		ImGui.SetNextWindowPos(new Vector2(0f, menuBarHeight));
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), display.Y - menuBarHeight));
		ImGui.Begin("Mission", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

		var mission = new MissionSelection();
		Choose(mission, ImGui.Selectable("Mission##mission", _selection.Is(mission)));
		DrawProblems();
		DrawGroups();
		DrawAreas();
		DrawActions();
		DrawTimers();
		DrawRoutes();
		DrawObjectives();
		DrawConditions();

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
			EditorText.Note("None found.");
			return;
		}

		// Wrapped text rather than a Selectable, whose label does not wrap and most messages are
		// wider than the panel.
		foreach (var (finding, target) in _lint.Findings) {
			ImGui.BulletText(string.Empty);
			ImGui.SameLine();
			if (target == null) {
				EditorText.Wrapped(finding.Message);
				continue;
			}

			ImGui.PushStyleColor(ImGuiCol.Text, _selection.Is(target) ? SelectedFindingColor : FindingLinkColor);
			EditorText.Wrapped(finding.Message);
			ImGui.PopStyleColor();
			Choose(target, ImGui.IsItemClicked());
		}
	}

	private void DrawTimers() {
		var timers = Layout.Entries<ActionTimer30>();
		if (!ImGui.CollapsingHeader($"Timers ({timers.Count})###timers")) {
			return;
		}

		if (Timers is { } editor) {
			if (ImGui.SmallButton("New timer")) {
				editor.RequestCreate();
			}
		}

		for (int number = 0; number < timers.Count; number++) {
			var timer = timers[number];
			var fires = timer.Fields.SequenceRefs.Select(reference => _index.Action(reference)?.Number).OfType<int>().ToList();
			string fired = fires.Count > 0 ? $"fires {string.Join(", ", fires)}" : "fires nothing";
			var selection = new TimerSelection(timer.Guid);
			Choose(selection, ImGui.Selectable(
				$"Timer {number}: {MissionText.TimerSummary(_index, timer.Fields)}, {fired}{Marker(TimerRow, timer.Guid)}##timer{timer.Guid}",
				_selection.Is(selection)));
		}
	}

	/// <summary>
	/// The objectives the simulation tests (row 17), under the list the player is shown (the text package's objective lines).
	/// Nothing reconciles the two, so they are listed apart.
	/// </summary>
	private void DrawObjectives() {
		var records = Layout.Objectives;
		if (!ImGui.CollapsingHeader($"Objectives ({records.Count})###objectives")) {
			return;
		}

		// A new objective's subject is the current selection.
		if (Objectives is { } objectives) {
			string? why = objectives.WhyNoCreate(_selection.Current);
			ImGui.BeginDisabled(why != null);
			if (ImGui.SmallButton("New objective")) {
				objectives.RequestCreate(_selection.Current);
			}

			ImGui.EndDisabled();
			EditorText.Reason(why);
		}

		// Both wrapped, as their lines run well past the panel's width.
		var lines = MissionTextEditing.Filled(_source, MissionTextList.Objectives);
		if (lines.Count > 0) {
			EditorText.Note("Objective lines (F11), edited in the Mission view:");
			foreach (var (_, id) in lines) {
				ImGui.BulletText(string.Empty);
				ImGui.SameLine();
				EditorText.Wrapped(MissionTextEditing.LineText(_source, id) ?? string.Empty);
			}

			ImGui.Spacing();
		}

		for (int number = 0; number < records.Count; number++) {
			var objective = records[number];
			var selection = new ObjectiveSelection(objective.Index);
			string marker = objective.Presence.Always ? string.Empty : NotAlways;
			Choose(selection, WrappedSelectable(MissionText.DescribeObjective(_index, objective, number) + marker,
				$"objective{objective.Index}", _selection.Is(selection)));
		}
	}

	/// <summary>
	/// Every row-1 condition, in file order, as the conditions the other records' condition fields reference.
	/// </summary>
	private void DrawConditions() {
		if (Conditions is not { } conditions) {
			return;
		}

		var records = conditions.Listed;
		if (!ImGui.CollapsingHeader($"Conditions ({records.Count})###conditions")) {
			return;
		}

		var source = conditions.Source;
		if (ImGui.SmallButton("New flag test")) {
			conditions.RequestCreateFlagTest();
		}

		ImGui.SameLine();
		if (ImGui.SmallButton("New draw")) {
			conditions.RequestCreateDraw();
		}

		for (int i = 0; i < records.Count; i++) {
			var record = records[i];
			var selection = new ConditionSelection(record.GUID);
			Choose(selection, WrappedSelectable($"Condition {record.GUID}: {ConditionEditing.Describe(source.Mission, record)}",
				$"condition{i}", _selection.Is(selection)));
		}
	}

	/// <summary>
	/// Selects <paramref name="selection"/> when the item just drawn was <paramref name="clicked"/>, and frames it in the
	/// view when it was double-clicked.
	/// </summary>
	private void Choose(EditorSelection selection, bool clicked) {
		if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
			_selection.Focus(selection);
		} else if (clicked) {
			_selection.Current = selection;
		}
	}

	/// <summary>
	/// A <see cref="ImGui.Selectable(string, bool)"/> whose label wraps at the panel's edge: a label-less selectable
	/// as tall as the wrapped text, with the text drawn over it.
	/// </summary>
	private static bool WrappedSelectable(string text, string id, bool selected) {
		float width = ImGui.GetContentRegionAvail().X;
		var start = ImGui.GetCursorScreenPos();
		float height = ImGui.CalcTextSize(text, false, width).Y;
		bool clicked = ImGui.Selectable($"##{id}", selected, ImGuiSelectableFlags.None, new Vector2(0f, height));
		var next = ImGui.GetCursorScreenPos();
		ImGui.SetCursorScreenPos(start);
		ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
		ImGui.TextUnformatted(text);
		ImGui.PopTextWrapPos();
		ImGui.SetCursorScreenPos(next);
		return clicked;
	}

	/// <summary><see cref="NotAlways"/> when row <paramref name="row"/>'s entry for <paramref name="guid"/> is present only under a condition, else nothing.</summary>
	private string Marker(int row, short guid) => Layout.Entry(row, guid)?.Presence.Always == false ? NotAlways : string.Empty;

	private void DrawGroups() {
		var groups = _index.Groups;
		if (!ImGui.CollapsingHeader($"Groups ({groups.Count})###groups")) {
			return;
		}

		foreach (var group in groups) {
			var members = _index.MembersOf(group.Guid);
			string waiting = group.Waiting && _index.Action(group.DeploymentAction) is { } action
				? $", waits on action {action.Number}"
				: string.Empty;

			var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
			var selection = new GroupSelection(group.Guid);
			if (_selection.Is(selection)) {
				flags |= ImGuiTreeNodeFlags.Selected;
			}

			if (members.Count == 0) {
				flags |= ImGuiTreeNodeFlags.Leaf;
			}

			bool open = ImGui.TreeNodeEx(
				$"{GroupName(group)}: {members.Count}{waiting}{Marker(GroupRow, group.Guid)}##group{group.Guid}", flags);
			Choose(selection, ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen());

			if (!open) {
				continue;
			}

			foreach (var member in members) {
				if (_index.Planned(member) is not { } planned) {
					continue;
				}

				var chosen = new ObjectSelection(planned.Key.Row, planned.Key.Guid);
				Choose(chosen, ImGui.Selectable(
					$"{MissionText.MemberName(_index, planned)}{Marker(planned.Key.Row, planned.Key.Guid)}##m{planned.Key.Row}.{planned.Key.Guid}",
					_selection.Is(chosen)));
			}

			ImGui.TreePop();
		}
	}

	// A group: its display number, roster and side, as the outliner has always named it.
	private static string GroupName(PlannedGroup group) {
		if (group.Number == 0) {
			return "Group 0 (player squad)";
		}

		string kind = Enum.IsDefined(group.Kind) ? group.Kind.ToString() : "?";
		return $"Group {group.Number} ({kind}, {group.Side})";
	}

	/// <summary>
	/// Every row-9 trigger area. One whose corner or centre references a point the layout has none for stands nowhere: it is
	/// listed with what is missing, and no action tests it.
	/// </summary>
	private void DrawAreas() {
		var entries = Layout.Entries<TriggerArea12>();
		if (!ImGui.CollapsingHeader($"Trigger areas ({entries.Count})###areas")) {
			return;
		}

		AreaPlacing?.DrawStartButtons(small: true);
		for (int number = 0; number < entries.Count; number++) {
			var entry = entries[number];
			string text;
			if (_index.Area(entry.Guid) is { } area) {
				string shape = area.Area.Shape == MissionTriggerShape.Circle
					? $"circle, {MissionText.Distance(area.Area.Radius)}"
					: "box";
				var testing = area.TestedBy.Select(guid => _index.Action(guid)?.Number).OfType<int>().ToList();
				string actions = testing.Count > 0 ? $"action {string.Join(", ", testing)}" : "no action";
				text = $"{shape}, {actions}";
			} else {
				text = entry.Fields.Shape != 0 ? "circle, its center is missing" : "box, a corner is missing";
			}

			var selection = new AreaSelection(entry.Guid);
			Choose(selection, ImGui.Selectable($"Area {number}: {text}{Marker(AreaRow, entry.Guid)}##area{entry.Guid}",
				_selection.Is(selection)));
		}
	}

	private void DrawActions() {
		var actions = _index.Actions;
		if (!ImGui.CollapsingHeader($"Actions ({actions.Count})###actions")) {
			return;
		}

		if (Actions is { } editor) {
			if (ImGui.SmallButton("New action")) {
				editor.RequestCreate();
			}
		}

		foreach (var action in actions) {
			int waiting = _index.Groups.Count(group => group.Waiting && group.DeploymentAction == action.Guid);
			string trigger = action.Areas.Count > 0 ? $"{action.Areas.Count} area(s)" : "no area";
			string effect = waiting > 0 ? $", brings in {waiting} group(s)" : string.Empty;
			var selection = new ActionSelection(action.Guid);
			Choose(selection, ImGui.Selectable($"Action {action.Number}: {trigger}{effect}{Marker(ActionRow, action.Guid)}##action{action.Guid}",
				_selection.Is(selection)));
		}
	}

	/// <summary>Every row-8 route. One none of whose waypoints references a point the layout has stands nowhere, and says so.</summary>
	private void DrawRoutes() {
		var entries = Layout.Entries<WaypointGroup>();
		if (!ImGui.CollapsingHeader($"Routes ({entries.Count})###routes")) {
			return;
		}

		if (Placing is { } placing) {
			ImGui.BeginDisabled(placing.Active || placing.WhyNoStart != null);
			if (ImGui.SmallButton("New route")) {
				placing.BeginNew();
			}

			ImGui.EndDisabled();
		}

		for (int number = 0; number < entries.Count; number++) {
			var entry = entries[number];
			string text;
			if (_index.Route(entry.Guid) is { } route) {
				var walkers = route.WalkedBy.Select(guid => _index.Group(guid)?.Number).OfType<int>().ToList();
				string walked = walkers.Count > 0 ? $"groups {string.Join(", ", walkers)}" : "never walked";
				text = $"{(route.Closed ? "loop" : "open")}, {walked}";
			} else {
				text = "no waypoints";
			}

			var selection = new RouteSelection(entry.Guid);
			Choose(selection, ImGui.Selectable($"Route {number}: {text}{Marker(RouteRow, entry.Guid)}##route{entry.Guid}",
				_selection.Is(selection)));
		}
	}
}
