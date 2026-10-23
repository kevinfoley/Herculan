using System.Numerics;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel down the right of the window: one view per kind of
/// <see cref="EditorSelection"/>. Every record a view mentions is a link that selects it, so the
/// panel is also how a mission's wiring is walked — area to action, action to the groups it brings
/// in, group to its route.
///
/// <para>Every view (<see cref="ObjectView"/>, <see cref="GroupView"/>, <see cref="RouteView"/>, <see cref="TriggerAreaView"/>,
/// <see cref="TriggerView"/>, <see cref="CountdownView"/>, <see cref="ObjectiveView"/> and a group's
/// <see cref="OrderSlotsView"/>) reads the <c>.MSN</c>'s layout by GUID or row-17 index, every record included, whatever its
/// condition. A selection whose record the <c>.MSN</c> no longer has shows its kind and that. Every record is named by
/// <see cref="MissionNames"/>.</para>
/// </summary>
internal sealed class PropertiesPanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	public const float PanelWidth = 340f;

	private const int RouteRow = 8;
	private const int AreaRow = 9;

	private readonly MissionIndex _index;
	private readonly SelectionState _selection;
	private readonly MissionView _mission;
	private readonly ObjectView _object;
	private readonly GroupView _group;
	private readonly RouteView _route;
	private readonly TriggerAreaView _area;
	private readonly ObjectiveView _objective;
	private readonly ObjectiveEditor _objectives;
	private readonly TriggerView _trigger;
	private readonly CountdownView _countdown;
	private readonly ConditionView _condition;

	public PropertiesPanel(MissionIndex index, SelectionState selection, MissionDocument source, PointMover mover,
			SideChanger sides, ObjectDeleter deleter, SquadMachinePanel machine, RosterFitView fit, MissionView mission, RouteEditor routes,
			RoutePlacing placing, AreaEditor areas, AreaPlacing areaPlacing, OrderEditor orders, ObjectiveEditor objectives,
			ActionEditor actions, TimerEditor timers, ConditionEditor conditions, PresenceEditor presence, VariantEditor variants,
			RandomChoices choices, TriggerMessages messages, MissionNames names) {
		_index = index;
		_selection = selection;
		_mission = mission;
		var links = new PanelLinks(index, names, selection);
		var sharers = new PointSharers(source, index, links);
		_trigger = new TriggerView(index, actions, selection, links, messages);
		_countdown = new CountdownView(index, timers, links);
		var variantView = new VariantView(variants, choices, links);
		_condition = new ConditionView(conditions, links, variantView);
		var presentWhen = new PresentWhenView(presence, links);
		_route = new RouteView(index, names, selection, mover, routes, placing, links, sharers, variantView.DrawRouteChoice);
		_area = new TriggerAreaView(index, names, mover, areas, areaPlacing, actions, _trigger.DrawSummary,
			namers => links.References(namers), sharers);
		var orderSlots = new OrderSlotsView(index, orders, links, presentWhen, presence);
		_objectives = objectives;
		_objective = new ObjectiveView(index, objectives, links, presentWhen, presence);
		var team = new TeamField(index, sides);
		var related = new RelatedObjectives(objectives, links);
		_object = new ObjectView(index, links, mover, team, deleter, machine, fit, presentWhen, presence, variantView, actions,
			related);
		_group = new GroupView(index, links, mover, team, machine, presentWhen, presence, actions, related, orderSlots, sharers);
		machine.References = namers => links.References(namers);
	}

	public void Draw(Vector2 display, float menuBarHeight) {
		float width = ScaledImGui.Scaled(PanelWidth);

		ImGui.SetNextWindowPos(new Vector2(display.X - width, menuBarHeight));
		ImGui.SetNextWindowSize(new Vector2(width, display.Y - menuBarHeight));
		ImGui.Begin("Properties", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

		switch (_selection.Current) {
			case MissionSelection:
				_mission.Draw();
				break;
			case ObjectSelection selected:
				if (_index.ObjectOf(selected) is { } placed) {
					_object.Draw(placed);
				}

				break;
			case GroupSelection { Guid: var guid }:
				if (_index.Group(guid) != null) {
					_group.Draw(guid);
				} else {
					Gone("Group");
				}

				break;
			case AreaSelection { Guid: var guid }:
				if (_index.Layout.Entry(AreaRow, guid) != null) {
					_area.Draw(guid);
				} else {
					Gone("Trigger area");
				}

				break;
			case ActionSelection { Guid: var guid }:
				if (_index.Action(guid) != null) {
					_trigger.Draw(guid);
				} else {
					Gone("Trigger");
				}

				break;
			case RouteSelection route:
				if (_index.Layout.Entry(RouteRow, route.Guid) != null) {
					_route.Draw(route);
				} else {
					Gone("Route");
				}

				break;
			case ObjectiveSelection { Record: var record }:
				if (_objectives.WhyFixed(record) == null) {
					_objective.Draw(record);
				} else {
					Gone("Win or lose condition");
				}

				break;
			case TimerSelection { Guid: var guid }:
				if (_index.Layout.Entry<ActionTimer30>(guid) != null) {
					_countdown.Draw(guid);
				} else {
					Gone("Countdown");
				}

				break;
			case ConditionSelection condition:
				_condition.Draw(condition.Guid);
				break;
			default:
				ImGui.TextWrapped("Nothing selected. Click a herc, flyer, structure, trigger area or waypoint in the scene, "
					+ "or pick one in the Mission panel on the left.");
				break;
		}

		ImGui.End();
	}

	// The view of a selection whose record the .MSN no longer has, until the next sync clears it.
	private static void Gone(string kind) {
		ImGui.Text(kind);
		EditorText.Note("Not found.");
	}
}
