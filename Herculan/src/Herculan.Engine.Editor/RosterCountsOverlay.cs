using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The viewport's bottom-left readout of how many of DBSIM's roster slots the mission takes against each cap, counted as the
/// adds refuse by (<see cref="RosterLimits"/>): hercs with the squad positions group 0 gives, flyers, and structures with
/// ground vehicles, which share block 9's slots; then the positions against what DBSIM's spawn pass reads
/// (<see cref="SkipArrayLimits"/>), the heads-down map's markers against what it builds (<see cref="MapMarkerLimits"/>), and
/// the objects against what the per-object tables hold (<see cref="ObjectLimits"/>). For a mission meant for HERCULAN alone
/// (<see cref="MissionLayout.HerculanOnly"/>) each limit is shown as retail's, since HERCULAN has none of them. A line past
/// its limit is red for a mission meant for retail too, and yellow for one meant for HERCULAN alone.
/// </summary>
internal static class RosterCountsOverlay {
	private const int PointRow = 6;
	private const int MechRow = 12;
	private const int FlyerRow = 13;
	private const int BaseRow = 14;

	private const float Margin = 12f;

	private static readonly Vector4 OverInRetailColor = new(0.9f, 0.35f, 0.3f, 1f);
	private static readonly Vector4 OverInHerculanColor = new(0.9f, 0.78f, 0.25f, 1f);

	// What passing a roster's slots does, retail's slot arrays against HERCULAN's loader, which has none
	// (docs/retail/formats/script-dat.md#the-two-pass-read--and-what-it-means-for-dbsim-keeps).
	private const string PastRosterLimit = "In retail, past the limit the mission's load overwrites other data. HERCULAN has "
		+ "no limit, but the editor adds none past it, so the mission stays playable in retail, unless Mission Settings > General "
		+ "has it playable in HERCULAN only.";

	// Where a tooltip's text wraps, in pixels at 100% scale.
	private const float TooltipWidth = 400f;

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
		Tooltip($"Every HERC the mission places, under any condition. The player and the player's squadmates take two HERC "
			+ $"slots each. {PastRosterLimit}");
		Line(layout, "Flyers", FlyerRow);
		Tooltip($"Every flyer the mission places, under any condition. {PastRosterLimit}");
		Line(layout, "Buildings & vehicles", BaseRow);
		Tooltip($"Every structure and ground vehicle the mission places, under any condition; the two share these slots. "
			+ PastRosterLimit);
		Count(layout, "Positions", SkipArrayLimits.Count(layout, PointRow), SkipArrayLimits.Cap(PointRow));
		Tooltip("Every position a group, object, waypoint or trigger area stands on, under any condition, a shared one "
			+ "counted once, and every position left unused, which Edit > \"Remove unused positions\" removes. In retail, past "
			+ "the limit the mission crashes as it loads; HERCULAN loads it.");
		Count(layout, "Map markers", MapMarkerLimits.Count(layout), MapMarkerLimits.Cap);
		Tooltip("The heads-down map gives every HERC, flyer, structure and vehicle a marker, the squad's machines included, "
			+ "plus one per waypoint of the squad's first order's route after its start (up to nine) and one for the nav "
			+ "marker. In retail, past the limit the mission crashes as it starts; HERCULAN runs it and draws every marker.");
		Count(layout, "Objects", ObjectLimits.Count(layout), ObjectLimits.Cap);
		Tooltip("Every HERC, flyer, structure and vehicle the mission places, under any condition, the squad's machines "
			+ "included. Each keeps track of which others it has spotted and can see, with room for this many. In retail, past "
			+ "the limit those records spill into other objects' data, which can crash the mission soon after it starts; "
			+ "HERCULAN runs it.");
		ImGui.End();
		ImGui.PopStyleVar();
		ImGui.PopStyleColor();
	}

	// The last line's explanation while the pointer is over it, wrapped at TooltipWidth. Tested by the pointer's position rather
	// than IsItemHovered, which the input-transparent window never reports.
	private static void Tooltip(string text) {
		if (!ImGui.IsMouseHoveringRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), false)) {
			return;
		}

		ImGui.BeginTooltip();
		ImGui.PushTextWrapPos(ScaledImGui.Scaled(TooltipWidth));
		ImGui.TextUnformatted(text);
		ImGui.PopTextWrapPos();
		ImGui.EndTooltip();
	}

	private static void Line(MissionLayout layout, string label, int row) =>
		Count(layout, label, RosterLimits.Slots(layout, row), RosterLimits.Cap(row));

	// One count against its limit; for a mission meant for HERCULAN alone, which has none of them, the limit marked as retail's.
	// A count past its limit is red while the mission is meant for retail too, and yellow while it is meant for HERCULAN alone.
	private static void Count(MissionLayout layout, string label, int count, int cap) {
		bool over = count > cap;
		if (over) {
			ImGui.PushStyleColor(ImGuiCol.Text, layout.HerculanOnly ? OverInHerculanColor : OverInRetailColor);
		}

		ImGui.TextUnformatted(layout.HerculanOnly ? $"{label}: {count} (retail limit {cap})" : $"{label}: {count}/{cap}");
		if (over) {
			ImGui.PopStyleColor();
		}
	}
}
