using System.Numerics;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The Properties panel's view of a herc, flyer, structure or ground vehicle (docs/herculan/handoff-editor-roster.md), read from
/// the <c>.MSN</c>'s layout and <see cref="MissionIndex"/> by the roster record the object stands for, every record included,
/// one the mission as opened leaves out too: its name, its group with a choice that moves it into another
/// (<see cref="MemberMover"/>) and its group's orders, its Present when, with what it does to a group it would leave empty
/// (<see cref="PresentWhenView.DrawGroupLeftEmpty"/>), and for a machine of the squad the random table it picks from; its
/// team; outside the squad its loadout or type, fixed or random (<see cref="LoadoutView"/>); its position as fields that move it, and its heading, set for a structure or ground vehicle (<see cref="HeadingField"/>); a herc's,
/// structure's or ground vehicle's starting condition (<see cref="StartingConditionField"/>) and a herc's radar setting (<see cref="RadarSettingField"/>), or for a machine of the player's squad which Herc it is (<see cref="SquadMachinePanel"/>); the two triggers it
/// fires; the campaign variables it sets when put out of action; the win and lose conditions about it; Delete, or what
/// refuses it; and, collapsed under Advanced, its type number, model, triangle and texture counts and hit radius.
/// </summary>
internal sealed class ObjectView {
	private readonly MissionIndex _index;
	private readonly PanelLinks _links;
	private readonly PointMover _mover;
	private readonly TeamField _team;
	private readonly ObjectDeleter _deleter;
	private readonly MemberMover _members;
	private readonly SquadMachinePanel _machine;
	private readonly LoadoutView _loadout;
	private readonly PresentWhenView _presentWhen;
	private readonly PresenceEditor _presence;
	private readonly RandomTableView _tables;
	private readonly ActionEditor _actions;
	private readonly RelatedObjectives _objectives;
	private readonly StartingConditionField _condition;
	private readonly RadarSettingField _radar;
	private readonly HeadingField _heading;

	// The object's point being typed.
	private readonly PointFields _point = new();

	public ObjectView(MissionIndex index, PanelLinks links, PointMover mover, TeamField team, ObjectDeleter deleter,
			MemberMover members, SquadMachinePanel machine, LoadoutView loadout, PresentWhenView presentWhen, PresenceEditor presence, RandomTableView tables,
			ActionEditor actions, RelatedObjectives objectives, StartingConditionField condition, RadarSettingField radar,
			HeadingField heading) {
		_index = index;
		_links = links;
		_mover = mover;
		_team = team;
		_deleter = deleter;
		_members = members;
		_machine = machine;
		_loadout = loadout;
		_presentWhen = presentWhen;
		_presence = presence;
		_tables = tables;
		_actions = actions;
		_objectives = objectives;
		_condition = condition;
		_radar = radar;
		_heading = heading;
	}

	/// <summary>Draws <paramref name="sel"/>'s view; nothing for an object the layout does not place.</summary>
	public void Draw(SceneObject sel) {
		if (_index.Planned(sel) is not { } planned || _index.Group(planned.Group) is not { } group) {
			return;
		}

		ImGui.TextWrapped(Title(planned));
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Group:");
		ImGui.SameLine();
		_links.ShortLayoutGroup(group.Guid);
		if (!planned.Squad) {
			DrawMoveTo(sel, planned, group);
			DrawOrders(group);
		}

		var presence = _presence.ObjectTarget(sel);
		_presentWhen.Draw(presence);
		_presentWhen.DrawGroupLeftEmpty(presence, group.Guid);
		if (planned.Squad) {
			_tables.DrawPickedTable(planned.Key.Row, planned.Key.Guid);
		}

		ImGui.Separator();
		_team.Draw(group.Guid, ofMember: true);
		_loadout.Draw(sel);

		ImGui.Separator();
		DrawPosition(sel, planned, group);
		_heading.DrawObject(planned.Key, sel.Object.Heading);

		if (!planned.Squad) {
			_condition.Draw(planned.Key);
			_radar.Draw(planned.Key);
			if (planned.Kind == MissionUnitKind.Base && _links.Names.Types.IsInvulnerable(planned.TypeIndex)) {
				EditorText.Note("Can't be destroyed: a structure of its type takes no damage.");
			}
		} else if (planned.MemberIndex == 0) {
			_machine.DrawPlayer();
		} else {
			_machine.DrawSquadmateObject(planned.Key.Guid);
		}

		if (group.OffScene) {
			EditorText.Note(MissionText.OffScene(group.ArrivalVerb, member: true));
		} else if (group.Waiting) {
			ImGui.TextWrapped("Its group is waiting on a trigger: nothing of it is drawn or ticked until "
				+ "that fires, and this position is a placeholder unless the group arrives in place.");
		}

		if (!planned.Squad) {
			DrawOwnActions(planned.Key);
		}

		OutOfActionWrites(planned);
		_objectives.Draw(_index.ObjectivesAbout(planned.Key), new ObjectSelection(planned.Key.Row, planned.Key.Guid));
		DeleteControls(sel);
		DrawAdvanced(sel, planned);
	}

	// A choice of the groups it can move into, of its own roster, the reason each other one is refused on hover.
	private void DrawMoveTo(SceneObject sel, PlannedObject planned, PlannedGroup group) {
		ImGui.SetNextItemWidth(-1f);
		if (EditorText.BeginCombo("##moveTo", "Move to another group...")) {
			bool any = false;
			foreach (var other in _index.Groups) {
				if (other.Guid == group.Guid || other.IsSquad || other.Kind != planned.Kind) {
					continue;
				}

				any = true;
				string? why = _members.WhyNoMove(sel, other.Guid);
				var flags = why != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None;
				var start = ImGui.GetCursorScreenPos();
				float room = ImGui.GetContentRegionAvail().X;
				if (NameText.Selectable(_links.Names.Group(other.Guid), $"to{other.Guid}", false, flags: flags)) {
					_members.Request(sel, other.Guid);
				}

				if (why != null && ImGui.IsMouseHoveringRect(start, new Vector2(start.X + room, ImGui.GetCursorScreenPos().Y))) {
					ImGui.SetTooltip(why);
				}
			}

			if (!any) {
				ImGui.TextDisabled("No other group of its kind.");
			}

			ImGui.EndCombo();
		}

		EditorText.Reason(_members.Failure);
	}

	// What it does: its group's orders, one line each, set in the group's view.
	private void DrawOrders(PlannedGroup group) {
		var names = _links.Names;
		var lines = new List<string>();
		for (int slot = 0; slot < group.Orders.Count; slot++) {
			if (group.Orders[slot] == -1 || _index.Layout.Entry<MissionOrder22>(group.Orders[slot])?.Fields is not { } order) {
				continue;
			}

			string line = $"{MissionText.Ordinal(slot + 1)} order: {MissionText.OrderVerb(order.Verb)}";
			if (slot == 0 && order.RouteRef != -1) {
				line += $", on {names.RouteInSentence(order.RouteRef)}";
			}

			if (order.SubjectKind != -1) {
				line += $", target: {(order.SubjectKind == 0 ? names.GroupInSentence(order.SubjectRef) : names.Subject(order.SubjectKind, order.SubjectRef).Full)}";
			}

			lines.Add(line);
		}

		if (lines.Count == 0) {
			return;
		}

		ImGui.BeginGroup();
		foreach (string line in lines) {
			EditorText.Wrapped(line);
		}

		ImGui.EndGroup();
		EditorText.Tooltip("Orders are controlled by the group.");
	}

	// The object's name: a herc's or flyer's type name, or a structure's or ground vehicle's title (RosterTypeNames.StructureTitle).
	private string Title(PlannedObject planned) =>
		_index.Layout.Entry(planned.Key.Row, planned.Key.Guid)?.Fields is BaseRosterEntry62 structure
			? _links.Names.Types.StructureTitle(structure)
			: _links.Names.TypeName(planned.Key);

	/// <summary>
	/// The object's position, as fields that move it when it can be moved (<see cref="PointMover"/>). A field's edit
	/// is applied when it loses the focus, as one undo step.
	/// </summary>
	private void DrawPosition(SceneObject sel, PlannedObject planned, PlannedGroup group) {
		var pos = sel.Object.Position;
		if (_mover.WhyFixed(sel) is { } reason) {
			ImGui.Text($"Position: {pos.X}, {pos.Y}, {pos.Z} units");
			ImGui.Text($"          {Meters(pos)}");
			EditorText.Reason(reason);
			return;
		}

		bool ownHeight = PointMover.HasOwnHeight(sel);
		ImGui.Text("Position, in world units:");
		ImGui.PushID("position");
		var typed = _point.Draw(sel, pos, ownHeight);
		ImGui.PopID();
		if (!ownHeight) {
			EditorText.Note($"Z: {pos.Z}, from the ground under it");
		}

		EditorText.Note(Meters(pos));
		if (typed is { } draft) {
			_mover.Commit(new ObjectTarget(planned.Key), pos, draft.X, draft.Y, draft.Z);
		}

		PointFields.BoxWarning(_mover.LoadedBox, sel.Object.Position);
		if (_mover.StandsOnOwnPoint(planned.Key)) {
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted("Stands at a position of its own.");
			ImGui.SameLine();
			if (ImGui.Button("Return to formation")) {
				_mover.Reattach(planned.Key);
			}

			EditorText.Tooltip("Drops its own position, so it stands on its place in its group's formation again.");
			EditorText.Reason(_mover.Failure);
		}

		if (planned.Kind is MissionUnitKind.Mech or MissionUnitKind.Flyer && group.Members.Count > 1 && group.Members[0] != planned.Key) {
			ImGui.TextWrapped("Once the mission runs it holds formation on its group's leader, so it can move off a "
				+ "point given here.");
		}
	}

	private static string Meters(Vec3i pos) =>
		$"{pos.X / WorldScale.WorldUnitsPerMeter:F1}, {pos.Y / WorldScale.WorldUnitsPerMeter:F1}, "
			+ $"{pos.Z / WorldScale.WorldUnitsPerMeter:F1} m";

	/// <summary>
	/// The triggers the roster record <paramref name="key"/> fires when engaged and when defeated, from the layout, each a choice
	/// of none or any trigger (<see cref="ActionEditor.RequestObjectAction"/>), with a link, and what engaged and defeated mean
	/// (docs/retail/simulation/mission-deployment.md#an-objects-own-two-actions--0x1b2-and-0x1b6), the engagement distance in
	/// metres. Not for a machine of the player's squad, which is built from <c>player.mec</c>: no retail squad record carries
	/// either.
	/// </summary>
	private void DrawOwnActions(ObjectKey key) {
		(short engaged, short defeated) = _index.Layout.Entry(key.Row, key.Guid)?.Fields switch {
			MechRosterEntry144 herc => (herc.EngagementActionRef, herc.DefeatActionRef),
			FlyerRosterEntry102 flyer => (flyer.EngagementActionRef, flyer.DefeatActionRef),
			BaseRosterEntry62 structure => (structure.EngagementActionRef, structure.DefeatActionRef),
			_ => ((short)-1, (short)-1),
		};

		ImGui.Separator();
		string? why = _actions.WhyNoObjectAction(key);
		OwnAction("When engaged, fires", key, defeated: false, engaged, why,
			$"A trigger set here fires when it is engaged: an enemy within {MissionText.Metres(Sim.Detection.EngagementRange)} "
				+ "spots it, or an enemy shoots it.");
		OwnAction("When defeated, fires", key, defeated: true, defeated, why,
			"A trigger set here fires when it is defeated: destroyed; a HERC also when immobilized or left with no working weapon.");
		EditorText.Reason(why);
		EditorText.Reason(_actions.Failure);
	}

	// One of the two, row-10 GUID <current> or -1: a choice of the triggers, with a link, and when it fires on the label's hover.
	// A ref the layout has no trigger for fires nothing, as the generation resolves it to none.
	private void OwnAction(string label, ObjectKey key, bool defeated, short current, string? why, string when) {
		if (_index.Action(current) == null) {
			current = -1;
		}

		ImGui.PushID(label);
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted($"{label}:");
		EditorText.Tooltip(when);
		string? noNone = _actions.WhyNoNone(key, defeated);
		ImGui.SameLine();
		ImGui.SetNextItemWidth(-1f);
		ImGui.BeginDisabled(why != null);
		if (ActionChoice.Draw("##own", current, _index, _links.Names, offerNone: noNone == null, whyNoNone: noNone) is { } chosen) {
			_actions.RequestObjectAction(key, defeated, chosen);
		}

		ImGui.EndDisabled();
		if (current != -1) {
			_links.Trigger(current);
		}

		ImGui.PopID();
	}

	/// <summary>
	/// The campaign variables an object sets when it goes out of the fight, read-only, from its record in the layout, headed by
	/// what sends it out (docs/retail/simulation/mission-deployment.md#the-out-of-action-report): a herc's report runs the first
	/// time it is destroyed or immobilized, a flyer's, a structure's and a ground vehicle's when it is destroyed. Nothing when it
	/// sets none. A squad herc's record sets none in play, since the squad's machines start with empty slots, so its view says
	/// so where its record lists any.
	/// </summary>
	private void OutOfActionWrites(PlannedObject planned) {
		string heading = planned.Kind == MissionUnitKind.Mech
			? "When destroyed or immobilized, sets campaign variables"
			: "When destroyed, sets campaign variables";
		short[] words = _index.Layout.Entry(planned.Key.Row, planned.Key.Guid)?.Fields switch {
			MechRosterEntry144 herc => herc.OutOfActionReport,
			FlyerRosterEntry102 flyer => flyer.OutOfActionReport,
			BaseRosterEntry62 structure => structure.OutOfActionReport,
			_ => [],
		};

		var writes = CampaignVariableWrites.Interleaved(words);
		if (!writes.Any(write => write.Variable >= 0)) {
			return;
		}

		if (planned.Squad) {
			ImGui.Separator();
			ImGui.TextWrapped($"{heading}:");
			EditorText.Note("None in play: a HERC of the player's squad sets none of its own, whatever its record lists.");
			return;
		}

		CampaignVariableWrites.DrawReadOnly(heading, WriteLayer.OutOfAction, writes);
	}

	/// <summary>
	/// A Delete button (<see cref="ObjectDeleter"/>), or why the object cannot be deleted, with what references or needs it
	/// as links. The delete itself waits for the frame's ImGui to be rendered (<see cref="ObjectDeleter.Request"/>).
	/// </summary>
	private void DeleteControls(SceneObject sel) {
		ImGui.Separator();
		var blockers = _deleter.BlockersOf(sel);
		if (blockers.None) {
			if (ImGui.Button("Delete")) {
				_deleter.Request(sel);
			}

			foreach (var (group, _) in blockers.RemovedGroups) {
				ImGui.PushID($"goes{group}");
				ImGui.AlignTextToFramePadding();
				ImGui.Text("Deleted with it, as it is the last member:");
				_links.LayoutGroup(group);
				ImGui.PopID();
			}

			return;
		}

		if (blockers.Fixed is { } reason) {
			EditorText.Reason(reason, "Cannot be deleted: ");
			return;
		}

		ImGui.PushID("delete");
		ImGui.TextWrapped("Cannot be deleted while these reference or need it:");
		_links.References(blockers.Object, fired: blockers.OwnTriggers);
		foreach (var (group, names) in blockers.RemovedGroups.Where(removed => removed.Names.Any)) {
			ImGui.PushID($"last{group}");
			ImGui.Spacing();
			ImGui.TextWrapped("It is the last member of its group, which would go with it. These reference the group:");
			_links.References(names);
			ImGui.PopID();
		}

		ImGui.PopID();
	}

	// Under a collapsed Advanced header, what matches the object against the file and the renderer: its type's number, its
	// model and that model's triangle and texture counts, and its hit radius.
	private static void DrawAdvanced(SceneObject sel, PlannedObject planned) {
		if (!ImGui.CollapsingHeader("Advanced")) {
			return;
		}

		ImGui.Text($"Type number: {planned.TypeIndex}");
		if (sel.Model is { } model) {
			ImGui.Text($"Model: {model.Key}");
			ImGui.Text($"Triangles: {model.TriangleVertexCount / 3}");
			ImGui.Text(model.Atlas is { } atlas
				? $"Texture: {atlas.FrameCount} frames ({atlas.Width}x{atlas.Height})"
				: "Texture: none");
		}

		ImGui.Text($"Hit radius: {sel.Object.HitRadius} units");
	}
}
