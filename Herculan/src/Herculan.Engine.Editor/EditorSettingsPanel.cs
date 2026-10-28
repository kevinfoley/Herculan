using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;

namespace Herculan.Engine.Editor;

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
		// whatever height its settings need, at least as wide as the Cybrid Loadouts line.
		float width = MathF.Max(ScaledImGui.Scaled(PanelWidth), CybridLoadoutsWidth() + ImGui.GetStyle().WindowPadding.X * 2f);
		ImGui.SetNextWindowSize(new Vector2(width, 0f));
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

			ImGui.AlignTextToFramePadding();
			ImGui.Text(CybridLoadoutsLabel);
			ImGui.SameLine();
			if (ImGui.RadioButton("Vanilla", _settings.CybridLoadouts == CybridLoadouts.Vanilla)) {
				_settings.CybridLoadouts = CybridLoadouts.Vanilla;
			}

			EditorText.Tooltip("Each Cybrid hardpoint offers the weapons retail missions fit on it.");
			ImGui.SameLine();
			if (ImGui.RadioButton("Unrestricted", _settings.CybridLoadouts == CybridLoadouts.Unrestricted)) {
				_settings.CybridLoadouts = CybridLoadouts.Unrestricted;
			}

			EditorText.Tooltip("Each Cybrid hardpoint offers any HERC weapon. PITBULL's own weapons stay on the PITBULL, and it takes no other.");
			ImGui.Separator();

			Span<float> widths = [ButtonRow.LabelWidth("Save"), ButtonRow.LabelWidth("Cancel")];
			var row = new ButtonRow(widths);
			if (row.Button("Save")) {
				_settings.Save();
				_snapshot = null;
			}

			if (row.Button("Cancel")) {
				Cancel();
			}
		}

		ImGui.End();

		if (!stayOpen && _snapshot != null) {
			Cancel();
		}
	}

	private const string CybridLoadoutsLabel = "Cybrid Loadouts:";

	// The Cybrid Loadouts line's width: its label and both radio buttons, each a frame-high circle and its text, side by side.
	private static float CybridLoadoutsWidth() {
		var style = ImGui.GetStyle();
		float Radio(string label) => ImGui.GetFrameHeight() + style.ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;
		return ImGui.CalcTextSize(CybridLoadoutsLabel).X + style.ItemSpacing.X + Radio("Vanilla") + style.ItemSpacing.X
			+ Radio("Unrestricted");
	}

	private void Cancel() {
		if (_snapshot != null) {
			_settings.CopyFrom(_snapshot);
			_snapshot = null;
		}
	}
}
