using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The viewport's bottom-left readout of how many of DBSIM's roster slots the mission takes against each cap, counted as the
/// adds refuse by (<see cref="RosterLimits"/>): hercs with the squad positions group 0 gives, flyers, and structures with
/// ground vehicles, which share block 9's slots; then the positions against what DBSIM's spawn pass reads
/// (<see cref="SkipArrayLimits"/>), and the heads-down map's markers against what it builds (<see cref="MapMarkerLimits"/>).
/// </summary>
internal static class RosterCountsOverlay {
	private const int PointRow = 6;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

	private const float Margin = 12f;

	/// <summary>
	/// Draws the readout over the viewport, its bottom-left corner a margin in from <paramref name="viewportLeft"/> and the
	/// display's bottom, in an input-transparent window so a click there still picks the scene, as the compass's is.
	/// </summary>
	public static void Draw(MissionLayout layout, Vector2 display, float viewportLeft) {
		float margin = ScaledImGui.Scaled(Margin);
		ImGui.SetNextWindowPos(new Vector2(viewportLeft + margin, display.Y - margin), ImGuiCond.Always, new Vector2(0f, 1f));
		ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0.25f));
		ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
		ImGui.Begin("##roster-counts", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs
			| ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
			| ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
		Line(layout, "HERCs", MechRow);
		// Tested by the pointer's position rather than IsItemHovered, which the input-transparent window never reports.
		if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), false)) {
			ImGui.SetTooltip("The player and the player's squadmates take two HERC slots each.");
		}

		Line(layout, "Flyers", FlyerRow);
		Line(layout, "Buildings & vehicles", BaseRow);
		ImGui.TextUnformatted($"Positions: {SkipArrayLimits.Count(layout, PointRow)}/{SkipArrayLimits.Cap(PointRow)}");
		if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), false)) {
			ImGui.SetTooltip("Every position a group, object, waypoint or trigger area stands on, under any condition, a "
				+ "shared one counted once. Past the limit the mission crashes as it loads.");
		}

		ImGui.TextUnformatted($"Map markers: {MapMarkerLimits.Count(layout)}/{MapMarkerLimits.Cap}");
		if (ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), false)) {
			ImGui.SetTooltip("The heads-down map gives every HERC, flyer, structure and vehicle a marker, the squad's machines "
				+ "included, plus one per waypoint of the squad's first order's route after its start (up to nine) and one for "
				+ "the nav marker. Past the limit the mission crashes as it starts.");
		}

		ImGui.End();
		ImGui.PopStyleVar();
		ImGui.PopStyleColor();
	}

	private static void Line(MissionLayout layout, string label, int row) =>
		ImGui.TextUnformatted($"{label}: {RosterLimits.Slots(layout, row)}/{RosterLimits.Cap(row)}");
}
