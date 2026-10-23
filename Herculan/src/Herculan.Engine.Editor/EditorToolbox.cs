using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// What a Toolbox entry adds: a herc (<see cref="HercPlacer"/>), a flyer (<see cref="FlyerPlacer"/>), a ground vehicle
/// (<see cref="VehiclePlacer"/>) or a structure (<see cref="StructurePlacer"/>).
/// </summary>
internal enum ToolboxKind {
	Herc,
	Flyer,
	Vehicle,
	Structure,
}

/// <summary>What the toolbox asks the editor to add: a herc, flyer, ground vehicle or structure type, its side, where, and to which group.</summary>
/// <param name="AtPointer">
/// Whether it goes on the ground under the pointer, where an entry was dragged and let go over the view; otherwise on
/// the ground at the middle of the view.
/// </param>
/// <param name="Group">The row-16 GUID of the group it joins, whose side it takes; null for a group of its own.</param>
internal sealed record ToolboxRequest(ToolboxKind Kind, int TypeIndex, MissionSide Side, bool AtPointer, short? Group);

/// <summary>A Toolbox entry being dragged toward the view: what it would add, and on which side.</summary>
internal readonly record struct ToolboxDrag(ToolboxKind Kind, int TypeIndex, MissionSide Side);

/// <summary>
/// The Toolbox window: what the editor can add to the open mission, the herc, flyer, ground vehicle and structure types of
/// <see cref="UnitTypeCatalog"/>, with the types it leaves out named in a note. An entry dragged out and let go over the view asks for it on the ground under
/// the pointer, a flyer above it; a double-click, or Add with an entry chosen, asks for it at the middle of the view, which
/// a gamepad's pointer reaches as easily. The side it fights on is chosen above the lists, or it joins the selected group
/// instead, a herc a herc group, a flyer a flyer group, and a ground vehicle or a structure a structure group, taking that
/// group's side; while the group cannot take the entry chosen or dragged, by the check its add refuses by, the choice to
/// join is disabled with the reason under it. While an entry is dragged over the view, what it would add is drawn where it would stand
/// (<see cref="DropPreview"/>). What an add writes is <see cref="HercPlacer"/>'s, <see cref="FlyerPlacer"/>'s,
/// <see cref="VehiclePlacer"/>'s and <see cref="StructurePlacer"/>'s. Above the lists the window holds the route tool's
/// New route (<see cref="RoutePlacing"/>) and the trigger-area tool's New box and New circle (<see cref="AreaPlacing"/>).
/// </summary>
internal sealed class EditorToolbox {
	/// <summary>Why the selected group cannot be joined once the <c>.MSN</c> has no such group.</summary>
	public const string GroupGone = "The group is no longer in the mission.";

	// Where it first opens and how big, in pixels on a 100% display (ScaledImGui.Scaled): beside the Mission panel,
	// at the bottom of the window.
	private const float Width = 260f;
	private const float Height = 400f;
	private const float Margin = 12f;

	private readonly (ToolboxKind Kind, int TypeIndex, string Name)[] _entries;
	private MissionSide _side = MissionSide.Human;
	private bool _join;

	// The group the selection is or belongs to, as the last frame drawn was told, and why an entry of each kind cannot
	// join it; and the group an add joins when "Join" is ticked, null while the entry chosen cannot.
	private short? _selectedGroup;
	private Func<ToolboxKind, string?>? _whyNotJoin;
	private short? _joinGroup;
	private (ToolboxKind Kind, int TypeIndex)? _chosen;
	private (ToolboxKind Kind, int TypeIndex)? _dragging;
	private ToolboxRequest? _request;

	// The flyer types not offered, which the install has no data for; null when it has data for each.
	private readonly string? _flyersLeftOut;

	// The vehicle types not offered, which DBSIM builds no object for; null when there are none.
	private readonly string? _vehiclesLeftOut;

	// Why no herc, no flyer, no ground vehicle and no structure can be added, as the last frame drawn was told.
	private string? _whyNoHerc;
	private string? _whyNoFlyer;
	private string? _whyNoVehicle;
	private string? _whyNoStructure;

	public EditorToolbox(GameContent content) {
		var catalog = new UnitTypeCatalog(content);
		_entries = catalog.Entries.ToArray();
		_flyersLeftOut = catalog.FlyersLeftOut;
		_vehiclesLeftOut = catalog.VehiclesLeftOut;
	}

	/// <summary>
	/// What a message from the last add said — why it was refused — or null; shown under the lists until the next
	/// request.
	/// </summary>
	public string? Status { get; set; }

	/// <summary>Whether an entry is being dragged toward the view.</summary>
	public bool Dragging => _dragging != null;

	/// <summary>What an entry being dragged would add, or null when none is.</summary>
	public ToolboxDrag? Drag => _dragging is var (kind, type) ? new ToolboxDrag(kind, type, _side) : null;

	/// <summary>Whether, at the last frame drawn, a drag was over a panel, where letting go adds nothing.</summary>
	public bool DragOverPanel { get; private set; }

	/// <summary>The request the toolbox made this frame, if any, which it forgets once taken.</summary>
	public ToolboxRequest? TakeRequest() {
		var request = _request;
		_request = null;
		return request;
	}

	/// <param name="enabled">False while nothing can be added, such as during a move.</param>
	/// <param name="selectedGroup">The row-16 GUID of the herc, flyer or structure group the selection is or belongs to, which an add can join; or null.</param>
	/// <param name="selectedGroupName">What <paramref name="selectedGroup"/> is called (<see cref="MissionNames.GroupBrief"/>), or null.</param>
	/// <param name="groupKind">The entry kind <paramref name="selectedGroup"/>'s members are, whose join is checked while no entry is chosen.</param>
	/// <param name="whyNotJoin">
	/// Why <paramref name="selectedGroup"/> cannot take an entry of a kind, or null when it can: the check that kind's add
	/// refuses by. The checkbox is disabled, with the reason under it, while the entry chosen or dragged is refused.
	/// </param>
	/// <param name="whyNoHerc">Why no herc can be added (<see cref="HercPlacer.WhyNoAdd"/>), shown in the Hercs section, or null.</param>
	/// <param name="whyNoFlyer">Why no flyer can be added (<see cref="FlyerPlacer.WhyNoAdd"/>), shown in the Flyers section, or null.</param>
	/// <param name="whyNoVehicle">Why no ground vehicle can be added (<see cref="VehiclePlacer.WhyNoAdd"/>), shown in the Ground vehicles section, or null.</param>
	/// <param name="whyNoStructure">Why no structure can be added (<see cref="StructurePlacer.WhyNoAdd"/>), shown in the Structures section, or null.</param>
	/// <param name="routeEntry">
	/// Draws the placing tools' entries (<see cref="RoutePlacing.DrawToolboxEntry"/>, <see cref="AreaPlacing.DrawToolboxEntry"/>)
	/// above the lists, or null.
	/// </param>
	public void Draw(Vector2 display, bool enabled, short? selectedGroup, string? selectedGroupName, ToolboxKind groupKind,
			Func<ToolboxKind, string?>? whyNotJoin, string? whyNoHerc, string? whyNoFlyer, string? whyNoVehicle,
			string? whyNoStructure, Action? routeEntry = null) {
		_whyNoHerc = whyNoHerc;
		_whyNoFlyer = whyNoFlyer;
		_whyNoVehicle = whyNoVehicle;
		_whyNoStructure = whyNoStructure;
		_selectedGroup = selectedGroup;
		_whyNotJoin = whyNotJoin;
		var joinKind = (_dragging ?? _chosen)?.Kind ?? groupKind;
		string? whyNot = selectedGroup != null ? whyNotJoin?.Invoke(joinKind) : null;
		_joinGroup = selectedGroup != null && whyNot == null ? selectedGroup : null;
		ImGui.SetNextWindowPos(new Vector2(ScaledImGui.Scaled(MissionOutliner.PanelWidth + Margin),
			display.Y - ScaledImGui.Scaled(Height + Margin)), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(Width), ScaledImGui.Scaled(Height)), ImGuiCond.FirstUseEver);
		if (ImGui.Begin("Toolbox")) {
			if (routeEntry != null) {
				routeEntry();
				ImGui.Separator();
			}

			DrawContents(enabled, selectedGroupName, whyNot);
		}

		ImGui.End();

		DragOverPanel = false;
		if (_dragging is var (kind, type)) {
			ImGui.SetTooltip($"{NameOf(kind, type)}: let go over the ground to add it");
			DragOverPanel = ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
			if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
				// Let go over a panel, the drag adds nothing.
				if (Allowed(kind, enabled) && !DragOverPanel) {
					Request(kind, type, atPointer: true);
				}

				_dragging = null;
			}
		}
	}

	private void DrawContents(bool enabled, string? selectedGroup, string? whyNotJoin) {
		ImGui.TextWrapped("Drag an entry onto the ground, or double-click it to add it in the middle of the view.");
		// Unticked while it cannot join, since an add then gets a group of its own; ticked again once it can.
		bool canJoin = _joinGroup != null;
		bool join = _join && canJoin;
		ImGui.BeginDisabled(!canJoin);
		if (ImGui.Checkbox(selectedGroup is { } group ? $"Join {group}###join" : "Join the selected group###join", ref join)) {
			_join = join;
		}

		ImGui.EndDisabled();
		if (whyNotJoin != null) {
			EditorText.Reason(whyNotJoin);
		} else if (selectedGroup == null) {
			EditorText.Note("Select a group, or one of its members, to join it.");
		}

		bool joining = _join && _joinGroup != null;
		ImGui.BeginDisabled(joining);
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Team:");
		foreach (var side in Enum.GetValues<MissionSide>()) {
			ImGui.SameLine();
			if (ImGui.RadioButton(side.ToString(), _side == side)) {
				_side = side;
			}
		}

		ImGui.EndDisabled();
		if (joining) {
			EditorText.Note("It takes the group's team.");
		}

		ImGui.BeginDisabled(_chosen is not var (chosenKind, _) || !Allowed(chosenKind, enabled));
		if (ImGui.Button("Add in the middle of the view") && _chosen is var (kind, type)) {
			Request(kind, type, atPointer: false);
		}

		ImGui.EndDisabled();

		if (Status != null) {
			ImGui.TextWrapped(Status);
		}

		ImGui.Separator();
		ImGui.BeginChild("entries");
		if (ImGui.CollapsingHeader("Hercs", ImGuiTreeNodeFlags.DefaultOpen)) {
			EditorText.Note("A new herc gets a group of its own, which hunts the player's squad from where it stands.");
			EditorText.Reason(_whyNoHerc);
			DrawEntries(ToolboxKind.Herc, Allowed(ToolboxKind.Herc, enabled));
		}

		if (ImGui.CollapsingHeader("Flyers", ImGuiTreeNodeFlags.DefaultOpen)) {
			EditorText.Note("A new flyer gets a group of its own, which hunts the player's squad from its route.");
			EditorText.Reason(_whyNoFlyer);
			DrawEntries(ToolboxKind.Flyer, Allowed(ToolboxKind.Flyer, enabled));
			if (_flyersLeftOut != null) {
				EditorText.Note(_flyersLeftOut);
			}
		}

		if (ImGui.CollapsingHeader("Ground vehicles", ImGuiTreeNodeFlags.DefaultOpen)) {
			EditorText.Note("A new ground vehicle gets a convoy of its own, which drives its route. Add waypoints to its route to set it moving.");
			EditorText.Reason(_whyNoVehicle);
			DrawEntries(ToolboxKind.Vehicle, Allowed(ToolboxKind.Vehicle, enabled));
			if (_vehiclesLeftOut != null) {
				EditorText.Note(_vehiclesLeftOut);
			}
		}

		if (ImGui.CollapsingHeader("Structures", ImGuiTreeNodeFlags.DefaultOpen)) {
			EditorText.Reason(_whyNoStructure);
			DrawEntries(ToolboxKind.Structure, Allowed(ToolboxKind.Structure, enabled));
		}

		ImGui.EndChild();
	}

	private void DrawEntries(ToolboxKind kind, bool enabled) {
		foreach (var (entryKind, typeIndex, name) in _entries) {
			if (entryKind != kind) {
				continue;
			}

			var entry = (kind, typeIndex);
			if (ImGui.Selectable($"{name}##{kind}{typeIndex}", _chosen == entry, ImGuiSelectableFlags.AllowDoubleClick)) {
				_chosen = entry;
				if (enabled && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
					Request(kind, typeIndex, atPointer: false);
				}
			}

			if (enabled && _dragging == null && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) {
				_dragging = entry;
				_chosen = entry;
			}
		}
	}

	// Whether an entry of <kind> can be added now: the toolbox is <enabled> and that kind's placer refuses nothing.
	private bool Allowed(ToolboxKind kind, bool enabled) =>
		enabled && kind switch {
			ToolboxKind.Herc => _whyNoHerc,
			ToolboxKind.Flyer => _whyNoFlyer,
			ToolboxKind.Vehicle => _whyNoVehicle,
			_ => _whyNoStructure
		} == null;

	// Asks for an entry of <kind> to be added, joining the selected group when the checkbox is ticked and enabled as drawn.
	// A double-click can choose an entry of another kind in the frame it asks for it, so the join is checked again for
	// <kind>, and a refusal is shown rather than adding it in a group of its own.
	private void Request(ToolboxKind kind, int typeIndex, bool atPointer) {
		short? group = null;
		if (_join && _joinGroup is { } joining) {
			if (_selectedGroup != null && _whyNotJoin?.Invoke(kind) is { } refused) {
				Status = refused;
				return;
			}

			group = joining;
		}

		_request = new ToolboxRequest(kind, typeIndex, _side, atPointer, group);
		Status = null;
	}

	private string NameOf(ToolboxKind kind, int typeIndex) =>
		Array.Find(_entries, entry => entry.Kind == kind && entry.TypeIndex == typeIndex).Name ?? $"Type {typeIndex}";
}
