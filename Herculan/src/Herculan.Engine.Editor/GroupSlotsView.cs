using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The members of a structure group outside the player's squad in its view (docs/herculan/handoff-editor-viewing.md#groups),
/// one row per member slot, which is its place in the group's formation (docs/retail/formats/script-dat.md#placement--the-actual-rule,
/// rule 7): slot 0 stands on the group's position, each later slot on its formation's spot for it
/// (<see cref="BaseFormationTable.OffsetFor"/>), and a slot past the formation's (<see cref="BaseFormationTable.SlotCount"/>) on
/// the group's position again. The rows run to the last slot a record of the group fills, and one more while the formation
/// has a spot for it, so a lone structure shows one empty place rather than its formation's every one.
///
/// <para>Each row has the member's link and a choice: a member's type within its kind (<see cref="UnitTypeCatalog.ChoicesFor"/>),
/// changed through <see cref="RosterTypeEditor"/> in the member's own record, or "Empty", which deletes it as its own Delete
/// does (<see cref="ObjectDeleter"/>), refused as that is; an empty slot's choice of the structure and ground vehicle types adds
/// one there (<see cref="StructurePlacer.RequestJoin"/>). A member whose type is picked by a random choice keeps that type here:
/// its own view (<see cref="RosterFitView"/>) says where a change goes. Every edit waits for the frame's ImGui to be rendered.</para>
/// </summary>
internal sealed class GroupSlotsView {
	private const int BaseRow = 14;

	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float NumberWidth = 24f;
	private const float TypeWidth = 200f;

	private readonly MissionDocument _source;
	private readonly MissionIndex _index;
	private readonly PanelLinks _links;
	private readonly UnitTypeCatalog _catalog;
	private readonly BaseFormationTable _formations;
	private readonly RosterTypeEditor _types;
	private readonly ObjectDeleter _deleter;
	private readonly StructurePlacer _placer;

	public GroupSlotsView(MissionDocument source, MissionIndex index, PanelLinks links, UnitTypeCatalog catalog,
			BaseFormationTable formations, RosterTypeEditor types, ObjectDeleter deleter, StructurePlacer placer) {
		_source = source;
		_index = index;
		_links = links;
		_catalog = catalog;
		_formations = formations;
		_types = types;
		_deleter = deleter;
		_placer = placer;
	}

	/// <summary>Whether <paramref name="group"/> gets these rows: a structure group other than the player's squad.</summary>
	public static bool Covers(PlannedGroup group) => group.Kind == MissionUnitKind.Base && !group.IsSquad;

	/// <summary>Draws <paramref name="group"/>'s slots.</summary>
	public void Draw(PlannedGroup group) {
		int places = _formations.SlotCount(group.Setup.FormationId);
		int last = -1;
		for (int slot = 0; slot < MissionPlacementRules.GroupMemberSlots; slot++) {
			if (!_source.MemberSlotFree(group.Guid, slot)) {
				last = slot;
			}
		}

		var placed = new Dictionary<int, PlannedObject>();
		foreach (var key in group.Members) {
			if (_index.Plan.Object(key) is { } member) {
				placed.TryAdd(member.MemberIndex, member);
			}
		}

		var shown = _index.Layout.Entry<MissionGroup164>(group.Guid)?.Fields.MemberRefs;
		int rows = Math.Min(MissionPlacementRules.GroupMemberSlots, Math.Max(1, last + 1 < places ? last + 2 : last + 1));
		ImGui.TextUnformatted($"Members: {group.Members.Count}");
		EditorText.Tooltip("Each place in the group has its own spot around the group's position, turned with the group's "
			+ "heading. A member with a position of its own stands there instead.");
		ImGui.PushID("slots");
		for (int slot = 0; slot < rows; slot++) {
			ImGui.PushID(slot);
			float start = ImGui.GetCursorPosX();
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted($"{slot + 1}");
			ImGui.SameLine(start + ScaledImGui.Scaled(NumberWidth));
			if (placed.TryGetValue(slot, out var member)) {
				DrawMember(group, member);
			} else if (shown is { } refs && slot < refs.Length && refs[slot] != -1) {
				DrawUnplaced(group, refs[slot]);
			} else {
				DrawEmpty(group, slot);
			}

			if (slot > 0 && slot >= places) {
				EditorText.Note(places <= 1
					? "Stands on the group's position: its formation has one place."
					: $"Stands on the group's position: its formation has {places} places.");
			}

			ImGui.PopID();
		}

		ImGui.PopID();
		if (rows > placed.Count) {
			EditorText.Reason(_placer.WhyNoAdd);
		}

		EditorText.Reason(_types.Failure, "Not changed: ");
		EditorText.Reason(_placer.Failure, "Not added: ");
	}

	// A member this group places: its type, a choice within its kind or Empty, which deletes it, and its link.
	private void DrawMember(PlannedGroup group, PlannedObject member) {
		var key = member.Key;
		var sceneObject = _index.ObjectOf(key);
		bool isVehicle = sceneObject?.Object is Sim.BaseObject { Type.IsVehicle: true };
		var kind = UnitTypeCatalog.KindOf(MissionUnitKind.Base, isVehicle);
		string? fixedType = _types.WhyFixed(BaseRow, key.Guid);
		string? drawn = _types.DrawnAlternative(BaseRow, key.Guid) != null
			? "Its type is picked by a random choice: change it in its own view."
			: null;
		var blockers = sceneObject != null ? _deleter.BlockersOf(sceneObject) : null;
		string? noEmpty = blockers == null ? "It is not in the scene."
			: blockers.None ? null
			: blockers.Fixed ?? "The mission references or needs it; its own view says what.";
		string? goes = blockers?.RemovedGroups.Any(removed => removed.Group == group.Guid) == true
			? "It is the group's last member, so the group is deleted with it."
			: null;

		ImGui.SetNextItemWidth(ScaledImGui.Scaled(TypeWidth));
		ImGui.BeginDisabled(fixedType != null);
		if (ImGui.BeginCombo("##type", _catalog.NameOf(kind, member.TypeIndex))) {
			if (Entry("Empty: delete it", false, noEmpty, goes) && sceneObject != null) {
				_deleter.Request(sceneObject);
			}

			ImGui.Separator();
			foreach (int type in _catalog.ChoicesFor(MissionUnitKind.Base, isVehicle)) {
				if (Entry(_catalog.NameOf(kind, type), type == member.TypeIndex, drawn) && type != member.TypeIndex) {
					_types.RequestType(BaseRow, key.Guid, type, alternative: false);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		ImGui.SameLine();
		if (sceneObject != null) {
			_links.Link(new RecordName(_links.Names.MemberName(key)), new ObjectSelection(key.Row, key.Guid));
		} else {
			NameText.Draw(new RecordName(_links.Names.MemberName(key)));
		}

		EditorText.Reason(fixedType);
		if (OwnPoint(key.Guid)) {
			EditorText.Note("Stands at its own position, not on this place's spot.");
		}
	}

	// A slot whose record this group does not place: one referencing no structure the mission has, or one an earlier group places.
	private void DrawUnplaced(PlannedGroup group, short guid) {
		var key = new ObjectKey(BaseRow, guid);
		var planned = _index.Plan.Object(key);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(TypeWidth));
		ImGui.BeginDisabled();
		if (ImGui.BeginCombo("##type", planned != null ? _links.Names.TypeName(key) : "Missing")) {
			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (planned != null) {
			ImGui.SameLine();
			_links.Object(key);
			EditorText.Note(planned.Group != group.Guid
				? "An earlier group also references it, and places it."
				: "It is not in the scene.");
		} else {
			EditorText.Note("References a structure the mission does not have.");
		}
	}

	// An empty slot: a choice of the structure and ground vehicle types, which adds one there. Why none can be added to the
	// group at all is said once, under the rows.
	private void DrawEmpty(PlannedGroup group, int slot) {
		string? why = _placer.WhyNotFillSlot(group.Guid, slot);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(TypeWidth));
		ImGui.BeginDisabled(why != null);
		if (ImGui.BeginCombo("##type", "Empty")) {
			ImGui.TextDisabled("Structures");
			foreach (int type in _catalog.Of(ToolboxKind.Structure)) {
				if (Entry(_catalog.NameOf(ToolboxKind.Structure, type), false, null)) {
					_placer.RequestJoin(group.Guid, slot, type);
				}
			}

			ImGui.Separator();
			ImGui.TextDisabled("Ground vehicles");
			foreach (int type in _catalog.Of(ToolboxKind.Vehicle)) {
				if (Entry(_catalog.NameOf(ToolboxKind.Vehicle, type), false, _placer.WhyNotJoinSlot(group.Guid, slot, type))) {
					_placer.RequestJoin(group.Guid, slot, type);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (why != _placer.WhyNoAdd) {
			EditorText.Reason(why);
		}
	}

	// One choice of a list: disabled, saying <why> on hover, when <why> is set; else saying <hover> on hover, if set.
	private static bool Entry(string label, bool selected, string? why, string? hover = null) {
		bool chosen = ImGui.Selectable(label, selected, why != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None);
		if ((why ?? hover) is { } tip && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(tip);
		}

		return chosen && why == null;
	}

	// Whether row-14 GUID <guid>'s record names a point of its own the layout has, which it stands on.
	private bool OwnPoint(short guid) =>
		_index.Layout.Entry<BaseRosterEntry62>(guid)?.Fields.PositionRef is { } point and not -1
			&& _index.Layout.Entry<MapPoint22>(point) != null;
}
