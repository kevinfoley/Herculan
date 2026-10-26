using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>What the start dialog's buttons ask for.</summary>
internal enum StartChoice {
	/// <summary>File &gt; Load from VOL's list.</summary>
	Vanilla,

	/// <summary>File &gt; Load custom mission's list.</summary>
	Custom,

	/// <summary>File &gt; New mission's dialog.</summary>
	New,
}

/// <summary>
/// The window the editor shows while no mission is open and nothing else asks for one: "Load vanilla mission", "Load custom
/// mission" and "Create new mission", each opening what the File menu's item of the same job opens. The host decides when it
/// is shown and acts on the choice.
/// </summary>
internal static class StartDialog {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 280f;

	/// <summary>Draws the window; the button pressed this frame, or null. Call once per frame, inside the ImGui frame.</summary>
	public static StartChoice? Draw() {
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));

		StartChoice? chosen = null;
		if (ImGui.Begin("Open a Mission", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			var full = new Vector2(-1f, 0f);
			if (ImGui.Button("Load vanilla mission", full)) {
				chosen = StartChoice.Vanilla;
			}

			if (ImGui.Button("Load custom mission", full)) {
				chosen = StartChoice.Custom;
			}

			if (ImGui.Button("Create new mission", full)) {
				chosen = StartChoice.New;
			}
		}

		ImGui.End();
		return chosen;
	}
}
