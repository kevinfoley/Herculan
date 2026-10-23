using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A point's X and Y, and optionally Z, as typed fields in world units, which a view applies when one lets go, through
/// <see cref="PointMover.Commit"/> as a drag of the move handles does, as one step of the undo history; and the warning
/// that a point stands outside the mission's play area as opened. The object, group, route and trigger-area views share it.
/// </summary>
internal sealed class PointFields {
	// A point being typed, and what it is for, until its field lets go.
	private object? _draftFor;
	private Vec3i _draft;

	/// <summary>
	/// Draws <paramref name="at"/>'s X and Y, and Z when <paramref name="withZ"/>, as fields for <paramref name="key"/>,
	/// disabled when not <paramref name="enabled"/>. Answers the point typed once a field lets go after an edit that
	/// changes it, Z kept as <paramref name="at"/>'s without <paramref name="withZ"/>; else null. A view drawing several
	/// points gives each its own <paramref name="key"/>, compared by <see cref="object.Equals(object?)"/>, and its own ImGui ID.
	/// </summary>
	public Vec3i? Draw(object key, Vec3i at, bool withZ, bool enabled = true) {
		// Another point's draft is dropped once nothing is being typed: the selection moved on.
		if (_draftFor != null && !_draftFor.Equals(key) && !ImGui.IsAnyItemActive()) {
			_draftFor = null;
		}

		bool drafting = enabled && key.Equals(_draftFor);
		var shown = drafting ? _draft : at;
		int x = shown.X, y = shown.Y, z = shown.Z;
		bool changed = false, done = false;
		ImGui.BeginDisabled(!enabled);
		changed |= Field("X", ref x, ref done);
		changed |= Field("Y", ref y, ref done);
		if (withZ) {
			changed |= Field("Z", ref z, ref done);
		}

		ImGui.EndDisabled();
		if (!enabled) {
			return null;
		}

		if (changed) {
			_draftFor = key;
			_draft = new Vec3i(x, y, withZ ? z : at.Z);
		}

		if (!done || !key.Equals(_draftFor)) {
			return null;
		}

		var draft = _draft;
		_draftFor = null;
		return draft.X != at.X || draft.Y != at.Y || (withZ && draft.Z != at.Z) ? draft : null;
	}

	/// <summary>
	/// Says when <paramref name="at"/> is outside <paramref name="box"/>, the mission box as opened
	/// (<see cref="PointMover.LoadedBox"/>), which a block-1 point outside it grows: a test of the point's X and Y alone, as
	/// the box is of the points alone (<see cref="MissionBox.Of"/>). The UI calls the box the play area: the heads-down map
	/// frames it in red, the player is warned "APPROACHING MISSION ZONE BOUNDARY" once outside it, and the mission is aborted
	/// <see cref="Sim.MissionObjectives.RulesOfEngagementMargin"/> past it (docs/retail/simulation/mission-objectives.md#the-status--mission_status-004135e8).
	/// </summary>
	public static void BoxWarning(MissionBox box, Vec3i at) {
		if (!box.IsEmpty && (at.X < box.MinX || at.X > box.MaxX || at.Y < box.MinY || at.Y > box.MaxY)) {
			EditorText.Note(PlayAreaWarning);
		}
	}

	/// <summary>What <see cref="BoxWarning"/> says.</summary>
	public static string PlayAreaWarning =>
		"Outside the mission's play area: the play area grows to include it, and with it the heads-down map's red border, the "
		+ "edge where the player is warned APPROACHING MISSION ZONE BOUNDARY, and the line "
		+ $"{MissionText.Metres(Sim.MissionObjectives.RulesOfEngagementMargin)} beyond, where the mission is aborted.";

	private static bool Field(string label, ref int value, ref bool done) {
		ImGui.SetNextItemWidth(ScaledImGui.Scaled(140f));
		bool changed = ImGui.InputInt(label, ref value, 0, 0);
		done |= ImGui.IsItemDeactivatedAfterEdit();
		return changed;
	}
}
