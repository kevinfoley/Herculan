using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Host.Editor;

/// <summary>
/// The floating "Editor Settings" window opened from the menu bar.
///
/// <para>Edits land on the live <see cref="EditorSettings"/> immediately, so a checkbox shows its
/// effect in the viewport while the panel is still open. What makes Cancel possible is the snapshot
/// taken when the panel opens: Save writes the current values to disk and closes, Cancel restores
/// the snapshot and closes, and closing the window by its title-bar button is a Cancel.</para>
/// </summary>
public sealed class EditorSettingsPanel {
	// In pixels on a 100% display (ScaledImGui.Scaled).
	private const float PanelWidth = 300f;

	private readonly EditorSettings _settings;

	/// <summary>The values as they stood when the panel opened; non-null exactly while it is open.</summary>
	private EditorSettings? _snapshot;

	public EditorSettingsPanel(EditorSettings settings) {
		_settings = settings;
	}

	/// <summary>
	/// Opens the panel, taking the snapshot Cancel restores. Opening an already-open panel keeps the
	/// original snapshot rather than adopting the half-made edits as the new baseline.
	/// </summary>
	public void Open() {
		_snapshot ??= _settings.Clone();
	}

	/// <summary>Draws the panel, if it is open. Call once per frame, inside the ImGui frame.</summary>
	public void Draw() {
		if (_snapshot == null) {
			return;
		}

		// A zero component means "fit the content", so the panel keeps a fixed width and grows to
		// whatever height its settings need.
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

		bool stayOpen = true;
		if (ImGui.Begin("Editor Settings", ref stayOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize)) {
			bool renderFog = _settings.RenderFog;
			if (ImGui.Checkbox("Render Fog", ref renderFog)) {
				_settings.RenderFog = renderFog;
			}

			bool showGrid = _settings.ShowGrid;
			if (ImGui.Checkbox("Show Ground Grid", ref showGrid)) {
				_settings.ShowGrid = showGrid;
			}

			ImGui.Separator();

			float spacing = ImGui.GetStyle().ItemSpacing.X;
			float buttonWidth = (ImGui.GetContentRegionAvail().X - spacing) * 0.5f;
			var buttonSize = new Vector2(buttonWidth, 0f);

			if (ImGui.Button("Save", buttonSize)) {
				_settings.Save();
				_snapshot = null;
			}

			ImGui.SameLine();

			if (ImGui.Button("Cancel", buttonSize)) {
				Cancel();
			}
		}

		ImGui.End();

		if (!stayOpen && _snapshot != null) {
			Cancel();
		}
	}

	private void Cancel() {
		if (_snapshot != null) {
			_settings.CopyFrom(_snapshot);
			_snapshot = null;
		}
	}
}
