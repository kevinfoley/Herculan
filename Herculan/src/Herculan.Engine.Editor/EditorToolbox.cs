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

/// <summary>One of the Toolbox's tabs, by what it adds.</summary>
internal enum ToolboxTab {
	Routes,
	TriggerAreas,
	Hercs,
	Flyers,
	GroundVehicles,
	Structures,
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
/// The Toolbox, docked in the bottom half of the left of the window under the Mission panel: what the editor can add to the
/// open mission, one tab each for Routes (the route tool's New route, <see cref="RoutePlacing"/>), Trigger Areas (the
/// trigger-area tool's New box and New circle, <see cref="AreaPlacing"/>), and the herc, flyer, ground vehicle and structure
/// types of <see cref="UnitTypeCatalog"/>, with the types it leaves out named in a note. A placing mode started elsewhere
/// brings its tab up. An entry dragged out and let go over the view asks for it on the ground under
/// the pointer, a flyer above it; a double-click, or Add with an entry of the tab shown chosen, asks for it at the middle of
/// the view, which a gamepad's pointer reaches as easily. The side it fights on is chosen above the list, and a new herc's or flyer's group
/// gets the first order a new group on that side gets (<see cref="HercWorks.Core.Data.File.Msn.HercGroups"/>), which the
/// lists' notes say; or it joins the selected group instead, a herc a herc group, a flyer a flyer group, and a ground vehicle
/// or a structure a structure group, taking that group's side, while the join is ticked for that group, as it reads unticked
/// for any other; while the group cannot take the tab's kind or the entry dragged, by the check its add refuses by, the choice
/// to join is disabled with the reason under it. While an entry is dragged over the view, what it would add is drawn where it
/// would stand (<see cref="DropPreview"/>). What an add writes is <see cref="HercPlacer"/>'s, <see cref="FlyerPlacer"/>'s,
/// <see cref="VehiclePlacer"/>'s and <see cref="StructurePlacer"/>'s.
/// </summary>
internal sealed class EditorToolbox {
	/// <summary>Why the selected group cannot be joined once the <c>.MSN</c> has no such group.</summary>
	public const string GroupGone = "Not found.";

	/// <summary>Why an object of roster row <paramref name="row"/> (12 to 14) cannot be changed once the <c>.MSN</c> has no record for it.</summary>
	public static string ObjectGone(int row) => row switch {
		12 => "Not found.",
		13 => "Not found.",
		_ => "Not found.",
	};

	private readonly (ToolboxKind Kind, int TypeIndex, string Name)[] _entries;
	private MissionSide _side = MissionSide.Human;

	// The group "Join" was ticked for; it reads unticked once the selection's group is another, so an add never joins a group
	// the user did not tick it for.
	private short? _joinTicked;

	// The group the selection is or belongs to, as the last frame drawn was told, and why an entry of each kind cannot
	// join it; and the group an add joins when "Join" is ticked, null while the tab shown, or the entry dragged, cannot.
	private short? _selectedGroup;
	private Func<ToolboxKind, string?>? _whyNotJoin;
	private short? _joinGroup;
	private (ToolboxKind Kind, int TypeIndex)? _chosen;
	private (ToolboxKind Kind, int TypeIndex)? _dragging;
	private ToolboxRequest? _request;

	private static readonly (ToolboxTab Tab, string Label)[] TabLabels = [
		(ToolboxTab.Routes, "Routes"), (ToolboxTab.TriggerAreas, "Trigger Areas"), (ToolboxTab.Hercs, "Hercs"),
		(ToolboxTab.Flyers, "Flyers"), (ToolboxTab.GroundVehicles, "Ground Vehicles"), (ToolboxTab.Structures, "Structures"),
	];

	// The tab shown.
	private ToolboxTab _tab = ToolboxTab.Routes;

	// Whether the route and the area placing modes were on at the last frame drawn, so the frame one starts brings its tab up.
	private bool _routesWereActive;
	private bool _areasWereActive;

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
	/// <param name="whyNotJoin">
	/// Why <paramref name="selectedGroup"/> cannot take an entry of a kind, or null when it can: the check that kind's add
	/// refuses by. The checkbox is disabled, with the reason under it, while the tab shown, or the entry dragged, is refused.
	/// </param>
	/// <param name="whyNoHerc">Why no herc can be added (<see cref="HercPlacer.WhyNoAdd"/>), shown in the Hercs tab, or null.</param>
	/// <param name="whyNoFlyer">Why no flyer can be added (<see cref="FlyerPlacer.WhyNoAdd"/>), shown in the Flyers tab, or null.</param>
	/// <param name="whyNoVehicle">Why no ground vehicle can be added (<see cref="VehiclePlacer.WhyNoAdd"/>), shown in the Ground vehicles tab, or null.</param>
	/// <param name="whyNoStructure">Why no structure can be added (<see cref="StructurePlacer.WhyNoAdd"/>), shown in the Structures tab, or null.</param>
	/// <param name="routes">The route-placing mode, whose entry (<see cref="RoutePlacing.DrawToolboxEntry"/>) the Routes tab holds.</param>
	/// <param name="areas">The area-placing mode, whose entry (<see cref="AreaPlacing.DrawToolboxEntry"/>) the Trigger Areas tab holds.</param>
	public void Draw(Vector2 display, float menuBarHeight, bool enabled, short? selectedGroup, string? selectedGroupName,
			Func<ToolboxKind, string?>? whyNotJoin, string? whyNoHerc, string? whyNoFlyer, string? whyNoVehicle,
			string? whyNoStructure, RoutePlacing routes, AreaPlacing areas) {
		_whyNoHerc = whyNoHerc;
		_whyNoFlyer = whyNoFlyer;
		_whyNoVehicle = whyNoVehicle;
		_whyNoStructure = whyNoStructure;
		_selectedGroup = selectedGroup;
		_whyNotJoin = whyNotJoin;
		_joinGroup = null;

		float top = menuBarHeight + MissionOutliner.PanelHeight(display, menuBarHeight);
		ImGui.SetNextWindowPos(new Vector2(0f, top));
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(MissionOutliner.PanelWidth), display.Y - top));
		// A placing mode started elsewhere (the Mission panel's lists, a route view) brings its tab up, which holds its Finish
		// and Cancel.
		if (Opened(routes.Active, ref _routesWereActive)) {
			_tab = ToolboxTab.Routes;
		}

		if (Opened(areas.Active, ref _areasWereActive)) {
			_tab = ToolboxTab.TriggerAreas;
		}

		if (ImGui.Begin("Toolbox", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse)) {
			DrawTabs();
			switch (_tab) {
				case ToolboxTab.Routes:
					routes.DrawToolboxEntry();
					break;
				case ToolboxTab.TriggerAreas:
					areas.DrawToolboxEntry();
					break;
				case ToolboxTab.Hercs:
					DrawKind(ToolboxKind.Herc, enabled, selectedGroupName);
					break;
				case ToolboxTab.Flyers:
					DrawKind(ToolboxKind.Flyer, enabled, selectedGroupName);
					break;
				case ToolboxTab.GroundVehicles:
					DrawKind(ToolboxKind.Vehicle, enabled, selectedGroupName);
					break;
				default:
					DrawKind(ToolboxKind.Structure, enabled, selectedGroupName);
					break;
			}
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

	// The tabs as a row of buttons coloured as ImGui colours its tabs, wrapping onto another line where the panel is too
	// narrow for the next, since an ImGui tab bar only shrinks or scrolls its tabs; then a separator.
	private void DrawTabs() {
		var style = ImGui.GetStyle();
		float right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
		for (int i = 0; i < TabLabels.Length; i++) {
			var (tab, label) = TabLabels[i];
			bool shown = tab == _tab;
			ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(shown ? ImGuiCol.TabActive : ImGuiCol.Tab));
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetColorU32(ImGuiCol.TabHovered));
			ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGui.GetColorU32(ImGuiCol.TabActive));
			if (ImGui.Button(label)) {
				_tab = tab;
			}

			ImGui.PopStyleColor(3);
			if (i + 1 < TabLabels.Length) {
				float next = ImGui.CalcTextSize(TabLabels[i + 1].Label).X + style.FramePadding.X * 2f;
				if (ImGui.GetItemRectMax().X + style.ItemSpacing.X + next <= right) {
					ImGui.SameLine();
				}
			}
		}

		ImGui.Separator();
	}

	// Whether a placing mode has just become <active>, by what the last frame drawn saw in <was>.
	private static bool Opened(bool active, ref bool was) {
		bool opened = active && !was;
		was = active;
		return opened;
	}

	// A tab of entries of <kind>: the shared add controls, then what a new one of the kind gets, why none can be added, and
	// the entries, which scroll under the controls.
	private void DrawKind(ToolboxKind kind, bool enabled, string? selectedGroupName) {
		string? whyNot = _selectedGroup != null ? _whyNotJoin?.Invoke(_dragging?.Kind ?? kind) : null;
		_joinGroup = _selectedGroup != null && whyNot == null ? _selectedGroup : null;
		DrawContents(kind, enabled, selectedGroupName, whyNot);
	}

	private void DrawContents(ToolboxKind kind, bool enabled, string? selectedGroup, string? whyNotJoin) {
		ImGui.TextWrapped("Drag an entry onto the ground, or double-click it to add it in the middle of the view.");
		// Unticked while it cannot join, since an add then gets a group of its own, and for any group but the one it was ticked for.
		bool canJoin = _joinGroup != null;
		bool join = Joining;
		ImGui.BeginDisabled(!canJoin);
		if (ImGui.Checkbox(selectedGroup is { } group ? $"Join {group}###join" : "Join the selected group###join", ref join)) {
			_joinTicked = join ? _joinGroup : null;
		}

		ImGui.EndDisabled();
		if (whyNotJoin != null) {
			EditorText.Reason(whyNotJoin);
		} else if (selectedGroup == null) {
			EditorText.Note("Select a group, or one of its members, to join it.");
		}

		bool joining = Joining;
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

		// Only an entry of this tab's kind: one chosen in another tab is out of sight.
		bool allowed = Allowed(kind, enabled);
		ImGui.BeginDisabled(_chosen is not var (chosenKind, _) || chosenKind != kind || !allowed);
		if (ImGui.Button("Add in the middle of the view") && _chosen is var (_, type)) {
			Request(kind, type, atPointer: false);
		}

		ImGui.EndDisabled();

		if (Status != null) {
			ImGui.TextWrapped(Status);
		}

		ImGui.Separator();
		switch (kind) {
			case ToolboxKind.Herc:
				EditorText.Note(_side == MissionSide.Cybrid
					? "A new herc gets a group of its own, which hunts the player's squad from where it stands."
					: "A new herc gets a group of its own, which patrols where it stands, its leader fighting any enemy it finds.");
				break;
			case ToolboxKind.Flyer:
				EditorText.Note(_side == MissionSide.Cybrid
					? "A new flyer gets a group of its own, which hunts the player's squad from its route."
					: "A new flyer gets a group of its own, which patrols its route and attacks any HERC or structure it finds.");
				break;
			case ToolboxKind.Vehicle:
				EditorText.Note("A new ground vehicle gets a convoy of its own, which drives its route. Add waypoints to its route to set it moving.");
				break;
		}

		EditorText.Reason(WhyNo(kind));
		ImGui.BeginChild("entries");
		DrawEntries(kind, allowed);
		string? leftOut = kind switch {
			ToolboxKind.Flyer => _flyersLeftOut,
			ToolboxKind.Vehicle => _vehiclesLeftOut,
			_ => null
		};

		if (leftOut != null) {
			EditorText.Note(leftOut);
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
	private bool Allowed(ToolboxKind kind, bool enabled) => enabled && WhyNo(kind) == null;

	// Why no entry of <kind> can be added, as the last frame drawn was told, or null.
	private string? WhyNo(ToolboxKind kind) => kind switch {
		ToolboxKind.Herc => _whyNoHerc,
		ToolboxKind.Flyer => _whyNoFlyer,
		ToolboxKind.Vehicle => _whyNoVehicle,
		_ => _whyNoStructure
	};

	// Asks for an entry of <kind> to be added, joining the selected group when the checkbox is ticked and enabled as drawn.
	// A double-click can choose an entry of another kind in the frame it asks for it, so the join is checked again for
	// <kind>, and a refusal is shown rather than adding it in a group of its own.
	private void Request(ToolboxKind kind, int typeIndex, bool atPointer) {
		short? group = null;
		if (Joining && _joinGroup is { } joining) {
			if (_selectedGroup != null && _whyNotJoin?.Invoke(kind) is { } refused) {
				Status = refused;
				return;
			}

			group = joining;
		}

		_request = new ToolboxRequest(kind, typeIndex, _side, atPointer, group);
		Status = null;
	}

	// Whether an add joins the selected group: "Join" was ticked for it, and it can take the entry.
	private bool Joining => _joinGroup != null && _joinTicked == _joinGroup;

	private string NameOf(ToolboxKind kind, int typeIndex) =>
		Array.Find(_entries, entry => entry.Kind == kind && entry.TypeIndex == typeIndex).Name ?? $"Type {typeIndex}";
}
