using System.Numerics;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.World;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// Edit &gt; "Remove unused positions": removes every position nothing uses (<see cref="UnusedPoints"/>), as one step of
/// <see cref="PointMover"/>'s undo history, which brings the Positions count of the viewport's readout down
/// (<see cref="RosterCountsOverlay"/>). When removing those on the play area's edge would shrink it, a prompt first asks
/// whether they go too, saying how far, since the play area frames the heads-down map and sets the mission's boundary
/// (<see cref="PointFields.PlayAreaWarning"/>).
///
/// <para>The edit runs when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class UnusedPointRemover {
	private const string PromptTitle = "Remove unused positions###unused-positions";

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private bool _requested;
	private bool _edges;

	// The prompt is up, and whether ImGui has been asked to open it.
	private bool _asking;
	private bool _raised;

	public UnusedPointRemover(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>How many positions the edit can remove now, those on the play area's edge included.</summary>
	public int Count => UnusedPoints.Find(_source, edges: true).Count;

	/// <summary>
	/// Asks for the unused positions to be removed: at once, those on the play area's edge included, when removing them leaves
	/// the play area as it is; otherwise once the prompt (<see cref="Draw"/>) has asked whether those go too.
	/// </summary>
	public void Request() {
		_edges = true;
		if (PlayArea([]) != PlayArea(UnusedPoints.Find(_source, edges: true))) {
			_asking = true;
			_raised = false;
			return;
		}

		_requested = true;
	}

	/// <summary>Draws the prompt, if <see cref="Request"/> put it up. Call once per frame, inside the ImGui frame, outside any menu.</summary>
	public void Draw() {
		if (!_asking) {
			return;
		}

		if (!_raised) {
			ImGui.OpenPopup(PromptTitle);
			_raised = true;
		}

		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PropertiesPanel.PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(PromptTitle, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			_asking = false;
			return;
		}

		var all = UnusedPoints.Find(_source, edges: true);
		int inner = UnusedPoints.Find(_source).Count;
		int edge = all.Count - inner;
		EditorText.Wrapped(edge == 1
			? "One unused position is on the edge of the play area."
			: $"{edge} unused positions are on the edge of the play area.");
		EditorText.Wrapped(Shrink(all));
		if (inner > 0) {
			EditorText.Wrapped(inner == 1
				? "One other unused position is removed either way."
				: $"{inner} other unused positions are removed either way.");
		}

		ImGui.Separator();
		int choice = -1;
		if (ImGui.Button(inner > 0 ? "Remove them too" : "Remove them")) {
			choice = 1;
		}

		if (inner > 0) {
			ImGui.SameLine();
			if (ImGui.Button("Keep them")) {
				choice = 0;
			}
		}

		ImGui.SameLine();
		if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
			choice = 2;
		}

		if (choice is 0 or 1) {
			_edges = choice == 1;
			_requested = true;
		}

		if (choice != -1) {
			_asking = false;
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();
	}

	// The play area once the positions <removed> are gone: the box of every other position the layout has.
	private MissionBox PlayArea(IReadOnlyList<short> removed) =>
		MissionBox.Of(_source.Layout.Entries<MapPoint22>()
			.Where(entry => !removed.Contains(entry.Guid))
			.Select(entry => new Vec3i(entry.Fields.X, entry.Fields.Y, 0))
			.ToList());

	// How the play area changes once <removed> are gone: its size now and after, in metres, and what moves with it.
	private string Shrink(IReadOnlyList<short> removed) {
		var now = PlayArea([]);
		var after = PlayArea(removed);
		string moves = "the heads-down map's red border, the edge where the player is warned APPROACHING MISSION ZONE BOUNDARY, "
			+ $"and the line {MissionText.Metres(Sim.MissionObjectives.RulesOfEngagementMargin)} beyond it where the mission is "
			+ "aborted";
		return after.IsEmpty
			? $"Removing them leaves the mission with no positions, and so no play area: no {moves}."
			: $"Removing them shrinks the play area from {Size(now)} to {Size(after)}, which moves {moves}.";
	}

	private static string Size(MissionBox box) => $"{MissionText.Metres(box.Width)} × {MissionText.Metres(box.Height)}";

	/// <summary>Removes the unused positions, if asked to since the last call and there are any.</summary>
	public void RunRequest() {
		if (!_requested) {
			return;
		}

		_requested = false;
		bool edges = _edges;
		if (UnusedPoints.Find(_source, edges).Count == 0) {
			return;
		}

		var source = _source;
		_mover.Record(new RecordsEdit(_sync, _sync.Run(() => UnusedPoints.Remove(source, edges))));
	}
}
