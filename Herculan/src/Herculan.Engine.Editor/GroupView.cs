using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a group (docs/herculan/handoff-editor-viewing.md#groups), read from the <c>.MSN</c>'s layout
/// and <see cref="MissionIndex"/> by its row-16 GUID, every group included, one the mission as opened leaves out too: its
/// name, Present when and team, the squad's machines for the player's squad (<see cref="SquadMachinePanel"/>); the trigger it
/// waits on; where it starts, as fields that move it, and what else stands there, unless it arrives by drop pod or on foot
/// (<see cref="PlannedGroup.OffScene"/>); its heading, set (<see cref="HeadingField"/>); its formation, set
/// (<see cref="FormationField"/>); and base pad, set for a structure group (<see cref="BasePadEditor"/>); its members, one row per place in its formation for any group but the
/// player's squad (<see cref="GroupSlotsView"/>); the
/// campaign variables it sets when every member is down; the win and lose conditions about it; its orders
/// (<see cref="OrderSlotsView"/>); a Delete group button (<see cref="GroupDeleter"/>); and, collapsed under Advanced, its
/// own route field.
/// </summary>
internal sealed class GroupView {
	private readonly MissionIndex _index;
	private readonly PanelLinks _links;
	private readonly PointMover _mover;
	private readonly TeamField _team;
	private readonly SquadMachinePanel _machine;
	private readonly PresentWhenView _presentWhen;
	private readonly PresenceEditor _presence;
	private readonly ActionEditor _actions;
	private readonly RelatedObjectives _objectives;
	private readonly OrderSlotsView _orders;
	private readonly PointSharers _sharers;
	private readonly HeadingField _heading;
	private readonly FormationField _formation;
	private readonly GroupSlotsView _slots;
	private readonly BasePadEditor _pads;
	private readonly GroupDeleter _deleter;

	// The group's point being typed.
	private readonly PointFields _point = new();

	public GroupView(MissionIndex index, PanelLinks links, PointMover mover, TeamField team, SquadMachinePanel machine,
			PresentWhenView presentWhen, PresenceEditor presence, ActionEditor actions, RelatedObjectives objectives,
			OrderSlotsView orders, PointSharers sharers, HeadingField heading, FormationField formation, GroupSlotsView slots,
			BasePadEditor pads, GroupDeleter deleter) {
		_index = index;
		_links = links;
		_mover = mover;
		_team = team;
		_machine = machine;
		_presentWhen = presentWhen;
		_presence = presence;
		_actions = actions;
		_objectives = objectives;
		_orders = orders;
		_sharers = sharers;
		_heading = heading;
		_formation = formation;
		_slots = slots;
		_pads = pads;
		_deleter = deleter;
	}

	/// <summary>Draws row-16 GUID <paramref name="guid"/>'s view; nothing for a group the layout does not have.</summary>
	public void Draw(short guid) {
		if (_index.Group(guid) is not { } group) {
			return;
		}

		var name = _links.Names.Group(guid);
		EditorText.Wrapped(name.Text);
		if (name.Number is { } number) {
			ImGui.TextDisabled(number);
		}

		_presentWhen.Draw(_presence.GroupTarget(guid));
		_team.Draw(guid, ofMember: false);
		if (group.IsSquad) {
			_machine.DrawSquad();
		}

		ImGui.Separator();
		DrawDeployment(group);

		ImGui.Separator();
		DrawSetup(group);

		ImGui.Separator();
		if (GroupSlotsView.Covers(group)) {
			_slots.Draw(group);
		} else {
			ImGui.Text($"Members: {group.Members.Count}");
			foreach (var member in group.Members) {
				ImGui.PushID($"member{member.Row}.{member.Guid}");
				_links.Object(member);
				ImGui.PopID();
			}
		}

		// Group_ReportIfAllOutOfAction: the last member to go finds every other destroyed or immobilized; a disarmed one
		// counts as standing (docs/retail/simulation/mission-deployment.md#the-out-of-action-report).
		if (_index.Layout.Entry<MissionGroup164>(guid)?.Fields is { } fields) {
			CampaignVariableWrites.DrawReadOnly("When every member is destroyed or immobilized, sets campaign variables",
				WriteLayer.OutOfAction, CampaignVariableWrites.Interleaved(fields.OutOfActionReport));
		}

		_objectives.Draw(_index.ObjectivesAbout(guid), new GroupSelection(guid));
		_orders.Draw(guid);
		DeleteControls(group);
		DrawAdvanced(group);
	}

	/// <summary>
	/// A Delete group button (<see cref="GroupDeleter"/>) and how many members go with it, or why the group cannot be deleted,
	/// with what references or needs it or a member as links. The delete itself waits for the frame's ImGui to be rendered.
	/// </summary>
	private void DeleteControls(PlannedGroup group) {
		ImGui.Separator();
		var blockers = _deleter.BlockersOf(group.Guid);
		if (blockers.Fixed is { } reason) {
			EditorText.Reason(reason, "Cannot be deleted: ");
			return;
		}

		ImGui.PushID("delete");
		if (blockers.None) {
			if (ImGui.Button("Delete group")) {
				_deleter.Request(group.Guid);
			}

			EditorText.Tooltip(blockers.Going.Count switch {
				0 => "Deletes the group, which has no members.",
				1 => "Deletes the group and its one member.",
				int n => $"Deletes the group and its {n} members.",
			} + (group.Setup.PaintsGround && group.Kind == MissionUnitKind.Base ? " Its base pad goes with it." : ""));
			if (blockers.Kept.Count > 0) {
				EditorText.Wrapped("Stays, as another group also places it:");
				foreach (var kept in blockers.Kept) {
					ImGui.PushID($"kept{kept.Guid}");
					_links.Object(kept);
					ImGui.PopID();
				}
			}

			EditorText.Reason(_deleter.Failure, "Not deleted: ");
			ImGui.PopID();
			return;
		}

		EditorText.Wrapped("Cannot be deleted while these reference or need it:");
		_links.References(blockers.Group);
		foreach (var (member, names, fired) in blockers.Members) {
			ImGui.PushID($"member{member.Guid}");
			ImGui.Spacing();
			const string goes = ", a member going with it, is referenced by:";
			_links.Object(member, EditorText.AfterRoom(goes));
			EditorText.After(goes);
			_links.References(names, fired: fired);
			ImGui.PopID();
		}

		ImGui.PopID();
	}

	/// <summary>
	/// The trigger the group waits on before it is in the mission, from the layout, a choice of none or any trigger
	/// (<see cref="ActionEditor.RequestDeployment"/>), with a link and how the group arrives. The player's squad has none: no
	/// retail squad record carries one (<see cref="HercWorks.Core.Data.File.Msn.ActionWiring"/>).
	/// </summary>
	private void DrawDeployment(PlannedGroup group) {
		short waitsOn = group.Waiting ? group.DeploymentAction : (short)-1;
		if (group.IsSquad && waitsOn == -1) {
			ImGui.TextUnformatted("In the mission from the start.");
			return;
		}

		string? why = _actions.WhyNoDeployment(group.Guid);
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("Waits on:");
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ActionChoice.Draw("##deployment", waitsOn, _index, _links.Names) is { } chosen) {
			_actions.RequestDeployment(group.Guid, chosen);
		}

		ImGui.EndDisabled();
		if (waitsOn != -1) {
			_links.Trigger(waitsOn);
		}

		if (group.OffScene) {
			EditorText.Note(MissionText.OffScene(group.ArrivalVerb, member: false));
		} else if (waitsOn != -1) {
			EditorText.Wrapped($"Not in the mission until that trigger activates; then it arrives {MissionText.Arrival(group.ArrivalVerb)}.");
		} else {
			ImGui.TextUnformatted("In the mission from the start.");
		}

		EditorText.Reason(why);
		EditorText.Reason(_actions.Failure);
	}

	/// <summary>
	/// Where the group starts (<see cref="DrawStart"/>), left out for a group kept out of the scene
	/// (<see cref="PlannedGroup.OffScene"/>), which arrives about the player wherever it is placed; then its heading
	/// (<see cref="HeadingField"/>), its formation (<see cref="FormationField"/>) and its base pad.
	/// </summary>
	private void DrawSetup(PlannedGroup group) {
		var setup = group.Setup;
		if (!group.OffScene) {
			DrawStart(group);
		}

		bool alongRoute = FirstRoute(group) is { } leg && _index.Layout.Entry<WaypointGroup>(leg)?.Fields.Waypoints.Length >= 2;
		_heading.DrawGroup(group.Guid, setup.Heading, alongRoute);
		if (group.Pad != null) {
			// The pad lies with the map's axes whatever the heading (docs/retail/rendering/terrain-texturing.md#base-formation-pads).
			EditorText.Note("The base pad does not turn with the group: any heading but 0 moves its structures off the pad.");
		}

		_formation.Draw(group);
		DrawBasePad(group);
	}

	/// <summary>
	/// Where the group starts (docs/retail/formats/script-dat.md#placement--the-actual-rule, rules 2 and 3): its own position,
	/// else the first waypoint of its first order's route, as fields that move it (<see cref="PointMover"/>), and what else
	/// stands on that point.
	/// </summary>
	private void DrawStart(PlannedGroup group) {
		var setup = group.Setup;
		switch (setup.PointSource) {
			case MissionGroupPointSource.OwnPoint:
				ImGui.AlignTextToFramePadding();
				ImGui.TextUnformatted("Starts at: its own position");
				if (_mover.CanReturnToRoute(group.Guid)) {
					ImGui.SameLine();
					if (ImGui.Button("Start at its route")) {
						_mover.ReturnToRoute(group.Guid);
					}

					EditorText.Tooltip("Drops its own position, so it starts at the start of its route again.");
					EditorText.Reason(_mover.Failure);
				}

				break;
			case MissionGroupPointSource.RouteStart:
				ImGui.TextUnformatted("Starts at: the start of its route");
				if (FirstRoute(group) is { } route && _index.Route(route) != null) {
					_links.LayoutRoute(route);
				}

				break;
			default:
				EditorText.Wrapped("Starts at: no position of its own and no route; give it one or the other.");
				EditorText.Note("The editor shows it at the map's corner (0, 0); where it starts in play is not known.");
				break;
		}

		// The authored point, which a move writes; a flyer group's Z is where its first member flies (PointMover.PositionOf).
		var target = new GroupTarget(group.Guid);
		bool movable = _mover.WhyFixedGroup(group.Guid) == null;
		var at = movable ? _mover.PositionOf(target) : setup.AuthoredPoint;
		ImGui.PushID("point");
		var typed = _point.Draw(target, at, movable && _mover.HasOwnHeight(target), enabled: movable);
		ImGui.PopID();
		if (typed is { } draft) {
			_mover.Commit(target, at, draft.X, draft.Y, draft.Z);
		}

		if (_mover.StandsOnPoint(group.Guid)) {
			PointFields.BoxWarning(_mover.LoadedBox, _mover.PositionOf(target));
		}

		DrawMove(group, target);
	}

	// What a drag of the group's move handles moves with it (PointMover), or why it cannot be moved.
	private void DrawMove(PlannedGroup group, GroupTarget target) {
		if (_mover.WhyFixedGroup(group.Guid) is { } reason) {
			EditorText.Reason(reason);
			return;
		}

		if (group.Setup.PointSource == MissionGroupPointSource.None) {
			EditorText.Note("Dragging gives it a position of its own.");
		} else {
			_sharers.Draw(_mover.SharersOf(target));
		}

		var carried = _mover.CarriedMembers(group.Guid);
		if (carried.Count > 0) {
			EditorText.Note(carried.Count == 1
				? "Ctrl+drag also moves its structure that has a position of its own."
				: $"Ctrl+drag also moves its {carried.Count} structures that have positions of their own.");
			_sharers.Draw(_mover.CarriedSharersOf(group.Guid), "Also on their spots, moving with them on Ctrl+drag:", aloneNote: false);
		}
	}

	/// <summary>
	/// Whether the group lays a base pad (docs/retail/formats/script-dat.md#base-formation-terrain,
	/// docs/retail/rendering/terrain-texturing.md#base-formation-pads), a checkbox that sets it (<see cref="BasePadEditor"/>): a
	/// structure group whose record sets it paints the ground tile its position falls in with its formation's concrete pad,
	/// levels the ground under the pad, and stands its structures at its formation's fixed spot in that tile. A formation with
	/// no pad paints nothing, so the checkbox is offered only for one with a pad; a herc or flyer group's flag does nothing.
	/// </summary>
	private void DrawBasePad(PlannedGroup group) {
		bool asked = group.Setup.PaintsGround;
		if (group.Kind != MissionUnitKind.Base || group.IsSquad) {
			return;
		}

		string? why = _pads.WhyFixed(group.Guid);
		bool lays = asked;
		ImGui.BeginDisabled(why != null);
		if (ImGui.Checkbox("Lays a base pad", ref lays)) {
			_pads.Request(group.Guid, lays);
		}

		ImGui.EndDisabled();
		EditorText.Tooltip("A concrete pad painted over the ground tile under the group, with the ground under it levelled; "
			+ "its structures stand at a fixed spot in that tile.");
		if (why != null) {
			EditorText.Reason(why);
		} else if (asked && group.Pad is { } pad) {
			EditorText.Wrapped($"It lays its concrete pad on the ground tile under it, {MissionText.Metres(pad.Layout.TileSize)} square.");
			EditorText.Note("Its structures stand at a fixed spot in that tile, so a move within the tile changes nothing and a "
				+ "move into the next one jumps a whole tile. The ground under the pad is levelled.");
		} else if (asked) {
			EditorText.Note("It lays none while the group has no structure.");
		}

		EditorText.Reason(_pads.Failure, "Not changed: ");
	}

	// Under a collapsed Advanced header: the group record's own route field.
	private void DrawAdvanced(PlannedGroup group) {
		if (!ImGui.CollapsingHeader("Advanced")) {
			return;
		}

		ImGui.PushID("advanced");
		short own = _index.Layout.Entry<MissionGroup164>(group.Guid)?.Fields.RouteRef ?? -1;
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("Own route:");
		ImGui.SameLine();
		if (own != -1 && _index.Layout.Entry<WaypointGroup>(own) != null) {
			_links.LayoutRoute(own);
		} else {
			ImGui.TextUnformatted("none");
		}

		EditorText.Note("Not editable yet. The group walks its first order's route; what reads this one in play is not known.");
		ImGui.PopID();
	}

	// The row-8 GUID of the route the group's first order names, or null.
	private short? FirstRoute(PlannedGroup group) =>
		group.Orders.Count > 0 && group.Orders[0] != -1 && _index.Layout.Entry<MissionOrder22>(group.Orders[0])?.Fields is { RouteRef: not -1 } order
			? order.RouteRef
			: null;
}
