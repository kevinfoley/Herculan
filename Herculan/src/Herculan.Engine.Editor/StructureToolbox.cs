using System.Numerics;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>What the toolbox asks the editor to add: a structure type, its side, and where.</summary>
/// <param name="AtPointer">
/// Whether it goes on the ground under the pointer, where an entry was dragged and let go over the view; otherwise on
/// the ground at the middle of the view.
/// </param>
internal sealed record ToolboxRequest(int TypeIndex, MissionSide Side, bool AtPointer);

/// <summary>
/// The Toolbox window: the structure types the editor can add to the open mission, from <c>dat\BASES.DAT</c>, under
/// the names the MFD status screen prints for them (<see cref="BaseType.SilhouetteIndex"/>). An entry dragged out and
/// let go over the view asks for that structure on the ground under the pointer; a double-click, or Add with an entry
/// chosen, asks for it on the ground at the middle of the view, which a gamepad's pointer reaches as easily. The side
/// the structure fights on is chosen above the list. The mobile ground vehicles (<see cref="BaseType.IsVehicle"/>) are
/// not offered: they drive, and a group of them is given orders the toolbox has no way to set. What the add writes is
/// <see cref="StructurePlacer"/>'s.
/// </summary>
internal sealed class StructureToolbox {
	// Where it first opens and how big, in pixels on a 100% display (ScaledImGui.Scaled): beside the Mission panel,
	// at the bottom of the window.
	private const float Width = 260f;
	private const float Height = 340f;
	private const float Margin = 12f;

	private readonly (int TypeIndex, string Name)[] _entries;
	private MissionSide _side = MissionSide.Human;
	private int _chosen = -1;
	private int _dragging = -1;
	private ToolboxRequest? _request;

	public StructureToolbox(GameContent content) {
		var types = BaseTypeTable.Load(content);
		var strings = SimStrings.Load(content);
		var entries = new List<(int, string)>();
		for (int i = 0; i < types.Count; i++) {
			if (types[i] is { IsVehicle: false } type) {
				string? name = strings?.Text(MfdLayout.StructureNameGroup, type.SilhouetteIndex);
				entries.Add((i, string.IsNullOrWhiteSpace(name) ? $"Type {i}" : $"{name} ({i})"));
			}
		}

		_entries = entries.ToArray();
	}

	/// <summary>
	/// What a message from the last add said — why it was refused — or null; shown under the list until the next
	/// request.
	/// </summary>
	public string? Status { get; set; }

	/// <summary>Whether an entry is being dragged toward the view.</summary>
	public bool Dragging => _dragging >= 0;

	/// <summary>The request the toolbox made this frame, if any, which it forgets once taken.</summary>
	public ToolboxRequest? TakeRequest() {
		var request = _request;
		_request = null;
		return request;
	}

	/// <param name="enabled">False while nothing can be added, such as during a move.</param>
	public void Draw(Vector2 display, bool enabled) {
		ImGui.SetNextWindowPos(new Vector2(ScaledImGui.Scaled(MissionOutliner.PanelWidth + Margin),
			display.Y - ScaledImGui.Scaled(Height + Margin)), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(Width), ScaledImGui.Scaled(Height)), ImGuiCond.FirstUseEver);
		if (ImGui.Begin("Toolbox")) {
			DrawContents(enabled);
		}

		ImGui.End();

		if (_dragging >= 0) {
			ImGui.SetTooltip($"{NameOf(_dragging)}: let go over the ground to add it");
			if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
				// Let go over a panel, the drag adds nothing.
				if (enabled && !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)) {
					Request(_dragging, atPointer: true);
				}

				_dragging = -1;
			}
		}
	}

	private void DrawContents(bool enabled) {
		ImGui.TextWrapped("Drag a structure onto the ground, or double-click it to add it in the middle of the view.");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Side:");
		foreach (var side in Enum.GetValues<MissionSide>()) {
			ImGui.SameLine();
			if (ImGui.RadioButton(side.ToString(), _side == side)) {
				_side = side;
			}
		}

		ImGui.BeginDisabled(!enabled || _chosen < 0);
		if (ImGui.Button("Add in the middle of the view")) {
			Request(_chosen, atPointer: false);
		}

		ImGui.EndDisabled();

		if (Status != null) {
			ImGui.TextWrapped(Status);
		}

		ImGui.Separator();
		ImGui.BeginChild("structures");
		foreach (var (typeIndex, name) in _entries) {
			if (ImGui.Selectable($"{name}##type{typeIndex}", _chosen == typeIndex, ImGuiSelectableFlags.AllowDoubleClick)) {
				_chosen = typeIndex;
				if (enabled && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
					Request(typeIndex, atPointer: false);
				}
			}

			if (enabled && _dragging < 0 && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) {
				_dragging = typeIndex;
				_chosen = typeIndex;
			}
		}

		ImGui.EndChild();
	}

	private void Request(int typeIndex, bool atPointer) {
		_request = new ToolboxRequest(typeIndex, _side, atPointer);
		Status = null;
	}

	private string NameOf(int typeIndex) => Array.Find(_entries, entry => entry.TypeIndex == typeIndex).Name ?? $"Type {typeIndex}";
}
