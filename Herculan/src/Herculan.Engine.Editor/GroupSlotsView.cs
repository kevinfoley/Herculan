using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The members of a herc, flyer or structure group outside the player's squad in its view
/// (docs/herculan/editor-design.md#group-sizes-and-roster-limits), one row per member slot, which is its place in the group's formation
/// (docs/retail/formats/script-dat.md#placement--the-actual-rule, rule 7): slot 0 stands on the group's position and each
/// later slot on its formation's spot for it. A structure group has as many places as its formation
/// (<see cref="BaseFormationTable.SlotCount"/>), a slot past them standing on the group's position again
/// (<see cref="BaseFormationTable.OffsetFor"/>); a herc or flyer group as many as this editor joins one to
/// (<see cref="HercGroups.LastJoinSlot"/>, <see cref="FlyerGroups.LastJoinSlot"/>). The rows run to the last slot a record of
/// the group fills, and one more while the group has a place for it, so a lone member shows one empty place rather than its
/// formation's every one.
///
/// <para>Each row has the member's link and a choice: a member's type within its kind, those offered for the group's side
/// (<see cref="UnitTypeCatalog.Of(ToolboxKind, MissionSide)"/>), changed in the member's own record, a herc's chassis with the
/// fit a new herc of it gets (<see cref="MachineFitter.RequestChassis"/>) and any other's type through
/// <see cref="RosterTypeEditor"/>; or "Empty", which deletes it as its own Delete does (<see cref="ObjectDeleter"/>), refused as
/// that is. An empty slot's choice of the kind's types adds one there: a herc (<see cref="HercPlacer.RequestJoin"/>), a flyer
/// (<see cref="FlyerPlacer.RequestJoin"/>), or a structure or ground vehicle (<see cref="StructurePlacer.RequestJoin"/>). A
/// member whose type is picked by a random table keeps that type here: its own view sets it (<see cref="LoadoutView"/>). Every edit waits for the frame's ImGui to be rendered.</para>
/// </summary>
internal sealed class GroupSlotsView {
	private const int MechRow = 12;
	private const int FlyerRow = 13;
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
	private readonly MachineFitter _fitter;
	private readonly ObjectDeleter _deleter;
	private readonly StructurePlacer _placer;
	private readonly HercPlacer _hercs;
	private readonly FlyerPlacer _flyers;

	public GroupSlotsView(MissionDocument source, MissionIndex index, PanelLinks links, UnitTypeCatalog catalog,
			BaseFormationTable formations, RosterTypeEditor types, MachineFitter fitter, ObjectDeleter deleter,
			StructurePlacer placer, HercPlacer hercs, FlyerPlacer flyers) {
		_source = source;
		_index = index;
		_links = links;
		_catalog = catalog;
		_formations = formations;
		_types = types;
		_fitter = fitter;
		_deleter = deleter;
		_placer = placer;
		_hercs = hercs;
		_flyers = flyers;
	}

	/// <summary>Whether <paramref name="group"/> gets these rows: any group but the player's squad.</summary>
	public static bool Covers(PlannedGroup group) => !group.IsSquad;

	/// <summary>Draws <paramref name="group"/>'s slots.</summary>
	public void Draw(PlannedGroup group) {
		int places = Places(group);
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

			if (group.Kind == MissionUnitKind.Base && slot > 0 && slot >= places) {
				EditorText.Note(places <= 1
					? "Stands on the group's position: its formation has one place."
					: $"Stands on the group's position: its formation has {places} places.");
			}

			ImGui.PopID();
		}

		ImGui.PopID();
		if (rows > placed.Count) {
			EditorText.Reason(WhyNoAdd(group));
		}

		EditorText.Reason(group.Kind == MissionUnitKind.Mech ? _fitter.Failure : _types.Failure, "Not changed: ");
		EditorText.Reason(group.Kind switch {
			MissionUnitKind.Mech => _hercs.Failure,
			MissionUnitKind.Flyer => _flyers.Failure,
			_ => _placer.Failure,
		}, "Not added: ");
	}

	// How many places <group> offers members: a structure group its formation's, a herc or flyer group as many as a join fills.
	private int Places(PlannedGroup group) => group.Kind switch {
		MissionUnitKind.Mech => HercGroups.LastJoinSlot + 1,
		MissionUnitKind.Flyer => FlyerGroups.LastJoinSlot + 1,
		_ => _formations.SlotCount(group.Setup.FormationId),
	};

	// Why no member of <group>'s kind can be added at all: its roster block is full.
	private string? WhyNoAdd(PlannedGroup group) => group.Kind switch {
		MissionUnitKind.Mech => _hercs.WhyNoAdd,
		MissionUnitKind.Flyer => _flyers.WhyNoAdd,
		_ => _placer.WhyNoAdd,
	};

	// A member this group places: its type, a choice within its kind or Empty, which deletes it, and its link.
	private void DrawMember(PlannedGroup group, PlannedObject member) {
		var key = member.Key;
		var sceneObject = _index.ObjectOf(key);
		bool isVehicle = sceneObject?.Object is Sim.BaseObject { Type.IsVehicle: true };
		var kind = UnitTypeCatalog.KindOf(group.Kind, isVehicle);
		bool herc = key.Row == MechRow;
		string? fixedType = herc ? _fitter.WhyFixed(key.Guid) : _types.WhyFixed(key.Row, key.Guid);
		string? drawn = (herc ? _fitter.PickedTemplate(key.Guid) : _types.PickedTemplate(key.Row, key.Guid)) != null
			? "Its type is picked by a random table: change it in its own view."
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
		if (EditorText.BeginCombo("##type", _catalog.NameOf(kind, member.TypeIndex))) {
			if (Entry("Empty: delete it", false, noEmpty, goes) && sceneObject != null) {
				_deleter.Request(sceneObject);
			}

			ImGui.Separator();
			foreach (int type in _catalog.Of(kind, group.Side)) {
				if (Entry(_catalog.NameOf(kind, type), type == member.TypeIndex, drawn) && type != member.TypeIndex) {
					if (herc) {
						_fitter.RequestChassis(key.Guid, type);
					} else {
						_types.RequestType(key.Row, key.Guid, type);
					}
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
		if (OwnPoint(key)) {
			EditorText.Note("Stands at its own position, not on this place's spot.");
		}
	}

	// A slot whose record this group does not place: one referencing no record the mission has, or one an earlier group places.
	private void DrawUnplaced(PlannedGroup group, short guid) {
		var key = new ObjectKey(Row(group), guid);
		var planned = _index.Plan.Object(key);
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(TypeWidth));
		ImGui.BeginDisabled();
		if (EditorText.BeginCombo("##type", planned != null ? _links.Names.TypeName(key) : "Missing")) {
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
			EditorText.Note(group.Kind switch {
				MissionUnitKind.Mech => "References a HERC the mission does not have.",
				MissionUnitKind.Flyer => "References a flyer the mission does not have.",
				_ => "References a structure the mission does not have.",
			});
		}
	}

	// An empty slot: a choice of the types the group's kind takes, which adds one there. Why none can be added to the group at
	// all is said once, under the rows.
	private void DrawEmpty(PlannedGroup group, int slot) {
		string? why = group.Kind switch {
			MissionUnitKind.Mech => _hercs.WhyNotFillSlot(group.Guid, slot),
			MissionUnitKind.Flyer => _flyers.WhyNotFillSlot(group.Guid, slot),
			_ => _placer.WhyNotFillSlot(group.Guid, slot),
		};

		ImGui.SetNextItemWidth(ScaledImGui.Scaled(TypeWidth));
		ImGui.BeginDisabled(why != null);
		if (EditorText.BeginCombo("##type", "Empty")) {
			switch (group.Kind) {
				case MissionUnitKind.Mech:
					foreach (int type in _catalog.Of(ToolboxKind.Herc, group.Side)) {
						if (Entry(_catalog.NameOf(ToolboxKind.Herc, type), false, null)) {
							_hercs.RequestJoin(group.Guid, slot, type);
						}
					}

					break;
				case MissionUnitKind.Flyer:
					foreach (int type in _catalog.Of(ToolboxKind.Flyer)) {
						if (Entry(_catalog.NameOf(ToolboxKind.Flyer, type), false, null)) {
							_flyers.RequestJoin(group.Guid, slot, type);
						}
					}

					break;
				default:
					DrawStructureChoices(group, slot);
					break;
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		if (why != WhyNoAdd(group)) {
			EditorText.Reason(why);
		}
	}

	// An empty structure-group slot's choices: the structure types, then the ground vehicle types.
	private void DrawStructureChoices(PlannedGroup group, int slot) {
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
	}

	// One choice of a list: disabled, saying <why> on hover, when <why> is set; else saying <hover> on hover, if set.
	private static bool Entry(string label, bool selected, string? why, string? hover = null) {
		bool chosen = ImGui.Selectable(label, selected, why != null ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None);
		if ((why ?? hover) is { } tip && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
			ImGui.SetTooltip(tip);
		}

		return chosen && why == null;
	}

	// The roster row <group>'s members are records of.
	private static int Row(PlannedGroup group) => group.Kind switch {
		MissionUnitKind.Mech => MechRow,
		MissionUnitKind.Flyer => FlyerRow,
		_ => BaseRow,
	};

	// Whether <key>'s record names a point of its own the layout has, which it stands on.
	private bool OwnPoint(ObjectKey key) =>
		_index.Layout.Entry(key.Row, key.Guid)?.Fields switch {
			MechRosterEntry144 herc => herc.PositionRef,
			FlyerRosterEntry102 flyer => flyer.PositionRef,
			BaseRosterEntry62 structure => structure.PositionRef,
			_ => (short)-1,
		} is var point and not -1 && _index.Layout.Entry<MapPoint22>(point) != null;
}
