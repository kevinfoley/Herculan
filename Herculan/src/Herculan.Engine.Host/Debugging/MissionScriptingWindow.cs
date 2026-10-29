using System.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Sim;
using ImGuiNET;

namespace Herculan.Engine.Host.Debugging;

/// <summary>
/// The "Mission Scripting" window, opened from the menu bar's item of that name under <c>--developer</c>: every trigger of
/// the running mission, each with an "Activate" that fires it, and a warp for the player's HERC to each point of its
/// route. This engine's own, not retail's. A button only queues its change, which <see cref="ApplyPending"/> makes from
/// the host's update, so the simulation is never changed while a frame is being drawn.
/// </summary>
sealed class MissionScriptingWindow {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float Width = 300f;
	private const float MaxHeight = 560f;
	private const float ButtonColumn = 110f;

	private Action<SimWorld>? _pending;

	/// <summary>Whether the window is up. Set by the menu bar (<see cref="Settings.HostMenuBar"/>), cleared by the window's own close.</summary>
	public bool IsOpen { get; set; }

	/// <summary>Builds this frame's ImGui draw list for the window. Does nothing while <see cref="IsOpen"/> is false.</summary>
	public void Draw(SimWorld world, int windowHeight) {
		if (!IsOpen) {
			return;
		}

		float inset = ScaledImGui.Scaled(16f);
		ImGui.SetNextWindowPos(new Vector2(inset, inset), ImGuiCond.FirstUseEver);
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(Width), MathF.Min(windowHeight - 2f * inset, ScaledImGui.Scaled(MaxHeight))),
			ImGuiCond.FirstUseEver);
		bool open = true;
		if (ImGui.Begin("Mission Scripting###mission-scripting", ref open)) {
			DrawTriggers(world);
			ImGui.Separator();
			DrawRoute(world);
		}

		ImGui.End();
		if (!open) {
			IsOpen = false;
		}
	}

	/// <summary>Makes the change a button queued since the last call, if any. Call from the host's update, before the frame's ticks.</summary>
	public void ApplyPending(SimWorld world) {
		var pending = _pending;
		_pending = null;
		pending?.Invoke(world);
	}

	// One row a trigger, numbered from 1 in the action array's order. That is the editor's numbering of a mission's triggers
	// too: script.dat's block 5 holds one record for each row-10 GUID, in the order of its first record, which is the order
	// the editor counts them in, as long as no row-10 record carries a condition, as none in retail does and the editor
	// gives none (docs/retail/formats/msn-mission-file.md). An activated trigger's button is greyed, since an action
	// activates once (MissionActionState.Activated).
	private void DrawTriggers(SimWorld world) {
		var actions = world.Mission.Actions;
		if (actions.Count == 0) {
			ImGui.TextUnformatted("This mission has no triggers.");
			return;
		}

		for (int i = 0; i < actions.Count; i++) {
			var action = actions[i];
			ImGui.AlignTextToFramePadding();
			ImGui.TextUnformatted($"Trigger {i + 1}");
			ImGui.SameLine(ScaledImGui.Scaled(ButtonColumn));
			ImGui.BeginDisabled(action.Activated);
			if (ImGui.Button($"Activate##trigger{i}")) {
				_pending = activateIn => action.Activate(activateIn);
			}

			ImGui.EndDisabled();
		}
	}

	// One button a point of the player's route, its group's (docs/retail/simulation/player-waypoints.md), named as the
	// editor names them: the first is where the mission starts the player, the rest are Waypoint 1 on.
	private void DrawRoute(SimWorld world) {
		if (world.PlayerMech is not { Destroyed: false } player) {
			ImGui.TextUnformatted("No player HERC to warp.");
			return;
		}

		var route = player.Group?.Route ?? [];
		if (route.Count == 0) {
			ImGui.TextUnformatted("The player's route has no waypoints.");
			return;
		}

		for (int i = 0; i < route.Count; i++) {
			var point = route[i];
			if (ImGui.Button(i == 0 ? "Warp to Start" : $"Warp to Waypoint {i}##waypoint{i}")) {
				_pending = warpIn => {
					if (warpIn.PlayerMech is { Destroyed: false } warped) {
						warped.WarpTo(warpIn, point.X, point.Y);
					}
				};
			}
		}
	}
}
