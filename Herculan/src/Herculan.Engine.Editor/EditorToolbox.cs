using System.Numerics;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// What a Toolbox entry adds: a herc (<see cref="HercPlacer"/>), a flyer (<see cref="FlyerPlacer"/>) or a structure
/// (<see cref="StructurePlacer"/>).
/// </summary>
internal enum ToolboxKind {
	Herc,
	Flyer,
	Structure,
}

/// <summary>What the toolbox asks the editor to add: a herc, flyer or structure type, its side, where, and to which group.</summary>
/// <param name="AtPointer">
/// Whether it goes on the ground under the pointer, where an entry was dragged and let go over the view; otherwise on
/// the ground at the middle of the view.
/// </param>
/// <param name="Group">The group it joins, whose side it takes; null for a group of its own.</param>
internal sealed record ToolboxRequest(ToolboxKind Kind, int TypeIndex, MissionSide Side, bool AtPointer, int? Group);

/// <summary>A Toolbox entry being dragged toward the view: what it would add, and on which side.</summary>
internal readonly record struct ToolboxDrag(ToolboxKind Kind, int TypeIndex, MissionSide Side);

/// <summary>
/// The Toolbox window: what the editor can add to the open mission. The hercs are the mech types of <c>nam\MECHS.NAM</c>
/// under the names the MFD prints for them (<see cref="MfdStatusSubject"/>); the flyers the types of <c>nam\FLYERS.NAM</c>
/// the install has a <c>dat\&lt;type&gt;.DAT</c> for, under the names the MFD prints (<see cref="MfdStatusSubject.FlyerTypeName"/>),
/// the others named in a note: with no data they cannot fly or be shot (docs/retail/simulation/ai-flyers.md); and the
/// structures the types of <c>dat\BASES.DAT</c> under the names the MFD status screen prints
/// (<see cref="BaseType.SilhouetteIndex"/>). An entry dragged out and let go over the view asks for it on the ground under
/// the pointer, a flyer above it; a double-click, or Add with an entry chosen, asks for it at the middle of the view, which
/// a gamepad's pointer reaches as easily. The side it fights on is chosen above the lists, or it joins the selected group
/// instead, a herc a herc group, a flyer a flyer group and a structure a structure group, taking that group's side. While
/// an entry is dragged over the view, what it would add is drawn where it would stand (<see cref="DropPreview"/>). The
/// mobile ground vehicles (<see cref="BaseType.IsVehicle"/>) are not offered: they drive, and their groups are a step of
/// their own (docs/herculan/handoff-editor-authoring.md#suggested-sequencing). What an add writes is
/// <see cref="HercPlacer"/>'s, <see cref="FlyerPlacer"/>'s and <see cref="StructurePlacer"/>'s. Above the lists the window
/// holds the route tool's New route (<see cref="RoutePlacing"/>).
/// </summary>
internal sealed class EditorToolbox {
	// Where it first opens and how big, in pixels on a 100% display (ScaledImGui.Scaled): beside the Mission panel,
	// at the bottom of the window.
	private const float Width = 260f;
	private const float Height = 400f;
	private const float Margin = 12f;

	private readonly (ToolboxKind Kind, int TypeIndex, string Name)[] _entries;
	private MissionSide _side = MissionSide.Human;
	private bool _join;
	private int? _joinGroup;
	private (ToolboxKind Kind, int TypeIndex)? _chosen;
	private (ToolboxKind Kind, int TypeIndex)? _dragging;
	private ToolboxRequest? _request;

	// The flyer types not offered, which the install has no data for; null when it has data for each.
	private readonly string? _flyersLeftOut;

	// Why no herc, no flyer and no structure can be added, as the last frame drawn was told.
	private string? _whyNoHerc;
	private string? _whyNoFlyer;
	private string? _whyNoStructure;

	public EditorToolbox(GameContent content) {
		var entries = new List<(ToolboxKind, int, string)>();
		var mechs = UnitTypeNames.LoadMechs(content);
		for (int i = 0; i < mechs.Count; i++) {
			entries.Add((ToolboxKind.Herc, i, mechs[i] is { Length: > 0 } name ? $"{name.ToUpperInvariant()} ({i})" : $"Type {i}"));
		}

		var flyers = UnitTypeNames.LoadFlyers(content);
		var leftOut = new List<string>();
		for (int i = 0; i < flyers.Count; i++) {
			if (flyers[i] is not { Length: > 0 } flyer) {
				continue;
			}

			if (content.Read("dat", flyer + ".DAT") is not { } data) {
				leftOut.Add(flyer.ToUpperInvariant());
				continue;
			}

			entries.Add((ToolboxKind.Flyer, i, $"{MfdStatusSubject.FlyerTypeName(new FlyerSimDataTransformer().Parse(data), flyer)} ({i})"));
		}

		_flyersLeftOut = leftOut.Count > 0
			? $"Not offered: {string.Join(", ", leftOut)}. The install has no data for them to fly or be shot with."
			: null;

		var types = BaseTypeTable.Load(content);
		var strings = SimStrings.Load(content);
		for (int i = 0; i < types.Count; i++) {
			if (types[i] is { IsVehicle: false } type) {
				string? name = strings?.Text(MfdLayout.StructureNameGroup, type.SilhouetteIndex);
				entries.Add((ToolboxKind.Structure, i, string.IsNullOrWhiteSpace(name) ? $"Type {i}" : $"{name} ({i})"));
			}
		}

		_entries = entries.ToArray();
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
	/// <param name="selectedGroup">The herc, flyer or structure group the selection is or belongs to, which an add can join; or null.</param>
	/// <param name="whyNotJoin">Why <paramref name="selectedGroup"/> cannot take another member, or null when it can.</param>
	/// <param name="whyNoHerc">Why no herc can be added (<see cref="HercPlacer.WhyNoAdd"/>), shown in the Hercs section, or null.</param>
	/// <param name="whyNoFlyer">Why no flyer can be added (<see cref="FlyerPlacer.WhyNoAdd"/>), shown in the Flyers section, or null.</param>
	/// <param name="whyNoStructure">Why no structure can be added (<see cref="StructurePlacer.WhyNoAdd"/>), shown in the Structures section, or null.</param>
	/// <param name="routeEntry">Draws the route tool's entry (<see cref="RoutePlacing.DrawToolboxEntry"/>) above the lists, or null.</param>
	public void Draw(Vector2 display, bool enabled, int? selectedGroup, string? whyNotJoin, string? whyNoHerc,
			string? whyNoFlyer, string? whyNoStructure, Action? routeEntry = null) {
		_whyNoHerc = whyNoHerc;
		_whyNoFlyer = whyNoFlyer;
		_whyNoStructure = whyNoStructure;
		_joinGroup = selectedGroup != null && whyNotJoin == null ? selectedGroup : null;
		ImGui.SetNextWindowPos(new Vector2(ScaledImGui.Scaled(MissionOutliner.PanelWidth + Margin),
			display.Y - ScaledImGui.Scaled(Height + Margin)), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(Width), ScaledImGui.Scaled(Height)), ImGuiCond.FirstUseEver);
		if (ImGui.Begin("Toolbox")) {
			if (routeEntry != null) {
				routeEntry();
				ImGui.Separator();
			}

			DrawContents(enabled, selectedGroup, whyNotJoin);
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

	private void DrawContents(bool enabled, int? selectedGroup, string? whyNotJoin) {
		ImGui.TextWrapped("Drag a herc, a flyer or a structure onto the ground, or double-click it to add it in the middle of the view.");
		ImGui.BeginDisabled(_joinGroup == null);
		ImGui.Checkbox(selectedGroup is { } group ? $"Join the selected group {group}" : "Join the selected group", ref _join);
		ImGui.EndDisabled();
		if (whyNotJoin != null) {
			EditorText.Reason(whyNotJoin);
		} else if (selectedGroup == null) {
			EditorText.Note("Select a herc, flyer or structure group, or one of its members, to join it.");
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
			_ => _whyNoStructure
		} == null;

	private void Request(ToolboxKind kind, int typeIndex, bool atPointer) {
		_request = new ToolboxRequest(kind, typeIndex, _side, atPointer, _join ? _joinGroup : null);
		Status = null;
	}

	private string NameOf(ToolboxKind kind, int typeIndex) =>
		Array.Find(_entries, entry => entry.Kind == kind && entry.TypeIndex == typeIndex).Name ?? $"Type {typeIndex}";
}
