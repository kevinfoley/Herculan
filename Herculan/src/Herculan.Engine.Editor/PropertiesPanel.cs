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
/// <para>The object, group, trigger, countdown and objective views and a group's orders (<see cref="ObjectView"/>,
/// <see cref="GroupView"/>, <see cref="TriggerView"/>, <see cref="CountdownView"/>, <see cref="ObjectiveView"/>,
/// <see cref="OrderSlotsView"/>) read the <c>.MSN</c>'s layout by GUID or row-17 index, every record included. The route and
/// trigger area views are drawn for the generated record the selection's <c>.MSN</c> record stands for
/// (<see cref="GeneratedRecords"/>); a route or area the generation has none for shows its kind and that it is not shown in
/// the editor. Every record is named by <see cref="MissionNames"/>.</para>
/// </summary>
internal sealed class PropertiesPanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	public const float PanelWidth = 340f;

	private readonly GeneratedIndex _index;
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

	public PropertiesPanel(GeneratedIndex index, SelectionState selection, MissionDocument source, PointMover mover,
			SideChanger sides, ObjectDeleter deleter, SquadMachinePanel machine, RosterFitView fit, MissionView mission, RouteEditor routes,
			RoutePlacing placing, AreaEditor areas, AreaPlacing areaPlacing, OrderEditor orders, ObjectiveEditor objectives,
			ActionEditor actions, TimerEditor timers, ConditionEditor conditions, PresenceEditor presence, VariantEditor variants,
			RandomChoices choices, TriggerMessages messages, MissionNames names, StringFile? strings) {
		var scene = index.Scene;
		_index = index;
		_selection = selection;
		_mission = mission;
		var links = new PanelLinks(scene, names, selection);
		var sharers = new PointSharers(source, scene, links);
		_trigger = new TriggerView(scene, actions, selection, links, messages);
		_countdown = new CountdownView(scene, timers, links);
		var variantView = new VariantView(variants, choices, links);
		_condition = new ConditionView(conditions, links, variantView);
		var presentWhen = new PresentWhenView(presence, links);
		_route = new RouteView(index, names, selection, mover, routes, placing, links, sharers, variantView.DrawRouteChoice);
		_area = new TriggerAreaView(index, names, mover, areas, areaPlacing, actions, _trigger.DrawSummary,
			namers => links.References(namers), sharers);
		var orderSlots = new OrderSlotsView(scene, orders, links, presentWhen, presence);
		_objectives = objectives;
		_objective = new ObjectiveView(scene, objectives, links, presentWhen, presence);
		var team = new TeamField(scene, sides);
		var related = new RelatedObjectives(objectives, links);
		_object = new ObjectView(scene, links, mover, team, deleter, machine, fit, presentWhen, presence, variantView, actions,
			related, strings);
		_group = new GroupView(scene, links, mover, team, machine, presentWhen, presence, actions, related, orderSlots, sharers);
		machine.References = namers => links.References(namers);
	}

	private GeneratedRecords Records => _index.Records;

	public void Draw(Vector2 display, float menuBarHeight) {
		float width = ScaledImGui.Scaled(PanelWidth);

		ImGui.SetNextWindowPos(new Vector2(display.X - width, menuBarHeight));
		ImGui.SetNextWindowSize(new Vector2(width, display.Y - menuBarHeight));
		ImGui.Begin("Properties", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);

		var scene = _index.Scene;
		switch (_selection.Current) {
			case MissionSelection:
				_mission.Draw();
				break;
			case ObjectSelection selected:
				if (scene.ObjectOf(selected) is { } placed) {
					_object.Draw(placed);
				}

				break;
			case GroupSelection { Guid: var guid }:
				if (scene.Group(guid) != null) {
					_group.Draw(guid);
				} else {
					NotShown("Group");
				}

				break;
			case AreaSelection { Guid: var guid }:
				if (Records.Area(guid) is { } area) {
					_area.Draw(guid, area);
				} else {
					NotShown("Trigger area");
				}

				break;
			case ActionSelection { Guid: var guid }:
				if (scene.Action(guid) != null) {
					_trigger.Draw(guid);
				} else {
					NotShown("Trigger");
				}

				break;
			case RouteSelection route:
				if (Records.Route(route.Guid) is { } reference) {
					_route.Draw(route, reference);
				} else {
					NotShown("Route");
				}

				break;
			case ObjectiveSelection { Record: var record }:
				if (_objectives.WhyFixed(record) == null) {
					_objective.Draw(record);
				} else {
					NotShown("Win or lose condition");
				}

				break;
			case TimerSelection { Guid: var guid }:
				if (scene.Layout.Entry<ActionTimer30>(guid) != null) {
					_countdown.Draw(guid);
				} else {
					NotShown("Countdown");
				}

				break;
			case ConditionSelection condition:
				_condition.Draw(condition.Guid);
				break;
			default:
				ImGui.TextWrapped("Nothing selected. Click a mech, flyer, building, trigger area or waypoint "
					+ "in the scene, or pick from the Mission list.");
				break;
		}

		ImGui.End();
	}

	// The view of a selected record the editor cannot show: a route or area the generation has none for, which those views,
	// reading the generated file, cannot show, or a record no longer in the file.
	private static void NotShown(string kind) {
		ImGui.Text(kind);
		EditorText.Note("Not shown in the editor.");
	}
}
