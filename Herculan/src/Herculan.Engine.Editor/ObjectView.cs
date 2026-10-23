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
/// one the mission as opened leaves out too: its name, its group, its Present when and the random choice it picks from; its
/// team, type and fit (<see cref="RosterFitView"/>); its position as fields that move it, and its heading; its starting
/// condition, or for a machine of the player's squad which Herc it is (<see cref="SquadMachinePanel"/>); the two triggers it
/// fires; the campaign variables it sets when put out of action; the win and lose conditions about it; Delete, or what
/// refuses it; and, collapsed under Advanced, its type number, model, triangle and texture counts and hit radius.
/// </summary>
internal sealed class ObjectView {
	private readonly MissionIndex _index;
	private readonly PanelLinks _links;
	private readonly PointMover _mover;
	private readonly TeamField _team;
	private readonly ObjectDeleter _deleter;
	private readonly SquadMachinePanel _machine;
	private readonly RosterFitView _fit;
	private readonly PresentWhenView _presentWhen;
	private readonly PresenceEditor _presence;
	private readonly VariantView _variants;
	private readonly ActionEditor _actions;
	private readonly RelatedObjectives _objectives;
	private readonly StringFile? _strings;

	// The object's point being typed.
	private readonly PointFields _point = new();

	public ObjectView(MissionIndex index, PanelLinks links, PointMover mover, TeamField team, ObjectDeleter deleter,
			SquadMachinePanel machine, RosterFitView fit, PresentWhenView presentWhen, PresenceEditor presence, VariantView variants,
			ActionEditor actions, RelatedObjectives objectives, StringFile? strings) {
		_index = index;
		_links = links;
		_mover = mover;
		_team = team;
		_deleter = deleter;
		_machine = machine;
		_fit = fit;
		_presentWhen = presentWhen;
		_presence = presence;
		_variants = variants;
		_actions = actions;
		_objectives = objectives;
		_strings = strings;
	}

	/// <summary>Draws <paramref name="sel"/>'s view; nothing for an object the layout does not place.</summary>
	public void Draw(SceneObject sel) {
		if (_index.Planned(sel) is not { } planned || _index.Group(planned.Group) is not { } group) {
			return;
		}

		ImGui.TextWrapped(Title(sel, planned));
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Group:");
		ImGui.SameLine();
		_links.ShortLayoutGroup(group.Guid);
		_presentWhen.Draw(_presence.ObjectTarget(sel));
		_variants.DrawObjectChoice(sel);
		ImGui.Separator();
		_team.Draw(group.Guid, ofMember: true);
		_fit.Draw(sel);

		ImGui.Separator();
		DrawPosition(sel, planned, group);
		ImGui.Text($"Heading: {MissionText.Heading(sel.Object.Heading)}");

		if (!planned.Squad) {
			ImGui.TextUnformatted($"Starting condition: {planned.StartingCondition}%");
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

	// The object's name: a herc's or flyer's type name, or for a structure or ground vehicle its type and the name the MFD's
	// status screen prints for it (MfdStatusSubject.BaseName).
	private string Title(SceneObject sel, PlannedObject planned) {
		if (sel.Object is Sim.BaseObject structure) {
			string kind = structure.Type.IsVehicle ? "Ground vehicle" : "Base";
			string? name = MfdStatusSubject.BaseName(structure.Type, _strings);
			return $"{kind} type {planned.TypeIndex}: {(string.IsNullOrWhiteSpace(name) ? "(no name)" : name)}";
		}

		return planned.TypeName ?? $"{planned.Kind} type {planned.TypeIndex}";
	}

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
		OwnAction("When engaged, fires", key, defeated: false, engaged, why);
		OwnAction("When defeated, fires", key, defeated: true, defeated, why);
		EditorText.Note($"A trigger set here fires when it is engaged: an enemy within {MissionText.Metres(Sim.Detection.EngagementRange)} "
			+ "spots it, or an enemy shoots it. Or when it is defeated: destroyed; a herc also when immobilized or left with no "
			+ "working weapon.");
		EditorText.Reason(why);
		EditorText.Reason(_actions.Failure);
	}

	// One of the two, row-10 GUID <current> or -1: a choice of the triggers, with a link. A ref the layout has no trigger for
	// fires nothing, as the generation resolves it to none.
	private void OwnAction(string label, ObjectKey key, bool defeated, short current, string? why) {
		if (_index.Action(current) == null) {
			current = -1;
		}

		ImGui.PushID(label);
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted($"{label}:");
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

		ImGui.Text($"Type index: {planned.TypeIndex}");
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
