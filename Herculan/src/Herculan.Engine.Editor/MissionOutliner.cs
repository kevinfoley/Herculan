using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Mission panel in the top half of the left of the window: the mission check's findings, then every group (with its members), trigger area,
/// trigger (row-10 action), countdown (row-11 timer), route, win and lose condition (row-17 objective) and random choice as a
/// list, the objective lines the player reads apart from the win and lose conditions, the trigger areas and routes with the
/// buttons that start placing a new one, and the triggers, countdowns and win and lose conditions with the buttons that create
/// one; last, under Advanced, every condition. It is the only way to reach the parts of a mission that are not
/// standing somewhere (a trigger with no area, a route with no waypoints) and the other way
/// to make a selection. A double-click also frames the record in the view (<see cref="SelectionState.Focus"/>).
///
/// <para>Every list is the <c>.MSN</c>'s layout (docs/herculan/handoff-editor-viewing.md#selection-and-panels): every record
/// of its row, a conditional one included, in layout order, each named by <see cref="MissionNames"/> with its number in its
/// row dimmed, cut to fit the panel with the whole name on hover, and selecting its record by GUID or row-17 index. A row
/// whose record is not in every version of the mission is marked <see cref="NotAlways"/>.</para>
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
	private readonly MissionNames _names;
	private MissionLint _lint;
	private readonly SelectionState _selection;

	public MissionOutliner(MissionDocument source, MissionIndex index, MissionNames names, SelectionState selection) {
		_source = source;
		_index = index;
		_names = names;
		_lint = new MissionLint(source, index, names.Types);
		_selection = selection;
	}

	private MissionLayout Layout => _index.Layout;

	/// <summary>Runs the mission check again and lists its findings in place of the last ones, for a mission built again.</summary>
	public void Recheck() => _lint = new MissionLint(_source, _index, _names.Types);

	/// <summary>The route-placing mode the Routes list's New route starts; set once the mission's tools are built.</summary>
	public RoutePlacing? Placing { get; set; }

	/// <summary>The area-placing mode the Trigger areas list's New box and New circle start; set once the mission's tools are built.</summary>
	public AreaPlacing? AreaPlacing { get; set; }

	/// <summary>What the Win and lose conditions list's New win condition asks to create an objective; set once the mission's tools are built.</summary>
	public ObjectiveEditor? Objectives { get; set; }

	/// <summary>What the Triggers list's New trigger asks to create a trigger; set once the mission's tools are built.</summary>
	public ActionEditor? Actions { get; set; }

	/// <summary>What the Countdowns list's New countdown asks to create a countdown; set once the mission's tools are built.</summary>
	public TimerEditor? Timers { get; set; }

	/// <summary>What the Conditions list under Advanced reads; set once the mission's tools are built.</summary>
	public ConditionEditor? Conditions { get; set; }

	/// <summary>What the Random choices list reads; set once the mission's tools are built.</summary>
	public RandomChoices? Choices { get; set; }

	/// <summary>
	/// The panel's height: the top half of the left side below the menu bar, the Toolbox (<see cref="EditorToolbox"/>) docked
	/// in the bottom half.
	/// </summary>
	public static float PanelHeight(Vector2 display, float menuBarHeight) => MathF.Floor((display.Y - menuBarHeight) / 2f);

	public void Draw(Vector2 display, float menuBarHeight) {
		ImGui.SetNextWindowPos(new Vector2(0f, menuBarHeight));
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), PanelHeight(display, menuBarHeight)));
		ImGui.Begin("Mission", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

		DrawProblems();
		DrawGroups();
		DrawAreas();
		DrawTriggers();
		DrawCountdowns();
		DrawRoutes();
		DrawObjectives();
		DrawChoices();
		DrawAdvanced();

		ImGui.End();
	}

	/// <summary>
	/// The mission check's findings, open by default when there are any, each in the editor's words (<see cref="ProblemText"/>).
	/// A finding whose record the editor can show selects it.
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
		foreach (var finding in _lint.Findings) {
			ImGui.BulletText(string.Empty);
			ImGui.SameLine();
			string text = ProblemText.Of(finding, _names, Layout);
			var target = finding.Target;
			if (target == null) {
				EditorText.Wrapped(text);
				continue;
			}

			ImGui.PushStyleColor(ImGuiCol.Text, _selection.Is(target) ? SelectedFindingColor : FindingLinkColor);
			EditorText.Wrapped(text);
			ImGui.PopStyleColor();
			Choose(target, ImGui.IsItemClicked());
		}
	}

	/// <summary>Every row-11 countdown, by its delay and what starts it.</summary>
	private void DrawCountdowns() {
		var countdowns = Layout.Entries<ActionTimer30>();
		if (!ImGui.CollapsingHeader($"Countdowns ({countdowns.Count})###timers")) {
			return;
		}

		if (Timers is { } editor) {
			if (ImGui.SmallButton("New countdown")) {
				editor.RequestCreate();
			}
		}

		foreach (var countdown in countdowns) {
			var selection = new TimerSelection(countdown.Guid);
			Choose(selection, Row(_names.Countdown(countdown.Guid), TimerRow, countdown.Guid, $"timer{countdown.Guid}", selection));
		}
	}

	/// <summary>
	/// The objectives the mission tests (row 17), its win and lose conditions, each by its sentence
	/// (<see cref="MissionNames.Objective(LayoutObjective)"/>).
	/// </summary>
	private void DrawObjectives() {
		var records = Layout.Objectives;
		if (!ImGui.CollapsingHeader($"Win and lose conditions ({records.Count})###objectives")) {
			return;
		}

		// A new objective's subject is the current selection.
		if (Objectives is { } objectives) {
			string? why = objectives.WhyNoCreate(_selection.Current);
			ImGui.BeginDisabled(why != null);
			if (ImGui.SmallButton("New win condition")) {
				objectives.RequestCreate(_selection.Current);
			}

			ImGui.EndDisabled();
			EditorText.Reason(why);
		}

		foreach (var objective in records) {
			var selection = new ObjectiveSelection(objective.Index);
			var name = _names.Objective(objective);
			string marker = objective.Presence.Always ? string.Empty : NotAlways;
			Choose(selection, NameText.Selectable(name with { Text = name.Text + marker }, $"objective{objective.Index}",
				_selection.Is(selection)));
		}
	}

	/// <summary>
	/// Every variant table, in row-1 order, as its Random choice view titles it, with how many records pick from it; selected
	/// while it or a range over it is (docs/herculan/handoff-editor-conditions.md#variants).
	/// </summary>
	private void DrawChoices() {
		if (Choices is not { } choices) {
			return;
		}

		var tables = choices.All;
		if (!ImGui.CollapsingHeader($"Random choices ({tables.Count})###choices")) {
			return;
		}

		short? current = _selection.Current is ConditionSelection { Guid: var guid }
			? VariantEditing.IsTable(_source, guid) ? guid : VariantEditing.TableOfRange(_source, guid)
			: null;
		foreach (short table in tables) {
			if (choices.Read(table) is not { } read) {
				continue;
			}

			int users = choices.Users(read).Count + choices.Others(read);
			Choose(new ConditionSelection(table), EditorText.WrappedSelectable($"{choices.Title(read)} (used by {users})", $"choice{table}",
				current == table));
		}
	}

	/// <summary>
	/// Advanced, collapsed: every row-1 condition, in file order, as the conditions the other records' condition fields
	/// reference.
	/// </summary>
	private void DrawAdvanced() {
		if (Conditions is not { } conditions || !ImGui.CollapsingHeader("Advanced###advanced")) {
			return;
		}

		var records = conditions.Listed;
		if (!ImGui.TreeNode($"Conditions ({records.Count})###conditions")) {
			return;
		}

		var source = conditions.Source;
		for (int i = 0; i < records.Count; i++) {
			var record = records[i];
			var selection = new ConditionSelection(record.GUID);
			Choose(selection, EditorText.WrappedSelectable($"Condition {record.GUID}: {ConditionEditing.Describe(source.Mission, record)}",
				$"condition{i}", _selection.Is(selection)));
		}

		ImGui.TreePop();
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
	/// A row naming the row-<paramref name="row"/> record of <paramref name="guid"/> (<see cref="NameText.Selectable"/>), marked
	/// <see cref="NotAlways"/> when it is not always present; whether it was clicked.
	/// </summary>
	private bool Row(RecordName name, int row, short guid, string id, EditorSelection selection) =>
		NameText.Selectable(name with { Text = name.Text + Marker(row, guid) }, id, _selection.Is(selection));

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
				? $", arrives when trigger {action.Number} fires"
				: string.Empty;

			var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
			var selection = new GroupSelection(group.Guid);
			if (_selection.Is(selection)) {
				flags |= ImGuiTreeNodeFlags.Selected;
			}

			if (members.Count == 0) {
				flags |= ImGuiTreeNodeFlags.Leaf;
			}

			var name = _names.Group(group.Guid);
			string full = $"{name.Full}{waiting}{Marker(GroupRow, group.Guid)}";
			string shown = NameText.Fit(full, ImGui.GetContentRegionAvail().X - ImGui.GetTreeNodeToLabelSpacing());
			bool open = ImGui.TreeNodeEx($"{shown}##group{group.Guid}", flags);
			if (shown != full && ImGui.IsItemHovered()) {
				ImGui.SetTooltip(full);
			}

			Choose(selection, ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen());

			if (!open) {
				continue;
			}

			foreach (var member in members) {
				if (_index.Planned(member) is not { } planned) {
					continue;
				}

				var chosen = new ObjectSelection(planned.Key.Row, planned.Key.Guid);
				Choose(chosen, Row(new RecordName(_names.MemberName(planned.Key)), planned.Key.Row, planned.Key.Guid,
					$"m{planned.Key.Row}.{planned.Key.Guid}", chosen));
			}

			ImGui.TreePop();
		}
	}

	/// <summary>
	/// Every row-9 trigger area, by its shape and who enters it. One whose corner or centre references a point the layout has
	/// none for stands nowhere: it is listed with what is missing, and no action tests it.
	/// </summary>
	private void DrawAreas() {
		var entries = Layout.Entries<TriggerArea12>();
		if (!ImGui.CollapsingHeader($"Trigger areas ({entries.Count})###areas")) {
			return;
		}

		AreaPlacing?.DrawStartButtons(small: true);
		foreach (var entry in entries) {
			var selection = new AreaSelection(entry.Guid);
			Choose(selection, Row(_names.Area(entry.Guid), AreaRow, entry.Guid, $"area{entry.Guid}", selection));
		}
	}

	/// <summary>
	/// Every row-10 trigger, by what fires it (its areas, else the countdowns or objects that do, else that nothing does) and what
	/// it does (<see cref="MissionNames.Trigger"/>).
	/// </summary>
	private void DrawTriggers() {
		var triggers = _index.Actions;
		if (!ImGui.CollapsingHeader($"Triggers ({triggers.Count})###actions")) {
			return;
		}

		if (Actions is { } editor) {
			if (ImGui.SmallButton("New trigger")) {
				editor.RequestCreate();
			}
		}

		foreach (var trigger in triggers) {
			var selection = new ActionSelection(trigger.Guid);
			Choose(selection, Row(_names.Trigger(trigger.Guid), ActionRow, trigger.Guid, $"action{trigger.Guid}", selection));
		}
	}

	/// <summary>
	/// Every row-8 route, by its shape and who walks it. One none of whose waypoints references a point the layout has stands
	/// nowhere, and says so.
	/// </summary>
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

		foreach (var entry in entries) {
			var selection = new RouteSelection(entry.Guid);
			Choose(selection, Row(_names.Route(entry.Guid), RouteRow, entry.Guid, $"route{entry.Guid}", selection));
		}
	}
}
