using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Sim;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// A herc's Radar in its view, from its GUID's base, set through <see cref="RadarSettingEditor"/> as one undo step: on, or off
/// outside a fight, and what that does to who sees it on radar (docs/retail/simulation/sensors.md#how-an-ai-machines-radar-is-set,
/// docs/retail/simulation/sensors.md#detection_sweep-004128f8-ranges). Then one sentence for each other version of the mission
/// that states the other setting.
/// </summary>
internal sealed class RadarSettingField {
	private const int HercRow = 12;

	private readonly RadarSettingEditor _editor;

	public RadarSettingField(RadarSettingEditor editor) {
		_editor = editor;
	}

	/// <summary>Draws <paramref name="key"/>'s radar setting; nothing for anything but a herc the layout still has.</summary>
	public void Draw(ObjectKey key) {
		var source = _editor.Source;
		if (key.Row != HercRow || source.Layout.Entry(key.Row, key.Guid) == null) {
			return;
		}

		bool on = RadarSettings.Current(source, key.Guid);
		string? why = _editor.WhyFixed(key.Guid);
		ImGui.PushID("radar");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Radar:");
		ImGui.SameLine();
		ImGui.BeginDisabled(why != null);
		ImGui.SetNextItemWidth(-1f);
		if (EditorText.BeginCombo("##radar", Label(on))) {
			foreach (bool option in new[] { true, false }) {
				if (ImGui.Selectable(Label(option), option == on) && option != on) {
					_editor.Request(key.Guid, option);
				}
			}

			ImGui.EndCombo();
		}

		ImGui.EndDisabled();
		EditorText.Note(on
			? $"Its radar is on outside a fight too, so an enemy within {MissionText.Metres(Detection.ScannerRange)} with a clear "
				+ "line of sight sees it on radar."
			: $"Its radar stays off until it fights. Until then it shows on enemy radar only within "
				+ $"{MissionText.Metres(Detection.PassiveRadarRange)} of an enemy whose own radar is on.");
		EditorText.Reason(why);
		DrawOverrides(source, key.Guid);
		EditorText.Reason(_editor.Failure);
		ImGui.PopID();
	}

	private static string Label(bool on) => on ? "On" : "Off outside a fight";

	// One sentence per other version that states another setting, from the records of the GUID under other conditions.
	private static void DrawOverrides(MissionDocument source, short guid) {
		var overrides = RadarSettings.Overrides(source, guid);
		if (overrides.Count == 0) {
			return;
		}

		foreach (var set in overrides) {
			var rule = set.Condition == RecordConditions.Always ? PresenceRule.Always : PresenceRules.ReadCondition(source, set.Condition).Rule;
			EditorText.Wrapped($"Its radar is \"{Label(set.Value != 0)}\" {PresenceSentences.Occasion(rule)}.");
		}

		EditorText.Note("A change here does not change it in those versions.");
	}
}
