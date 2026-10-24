using Herculan.Engine.Host.Localization;
using ImGuiNET;

namespace Herculan.Engine.Host;

public static class LocalizedImGuiHelpers {
	/// <summary>
	/// Draws a row of buttons as <see cref="ImGuiHelpers.DrawButtonRow"/> does, each labelled with its key's string in
	/// <paramref name="table"/>. Never writes to <paramref name="buttons"/> and allocates nothing, so a caller can keep
	/// the array in a field and pass it every frame.
	/// </summary>
	/// <param name="table">The table the labels are read from.</param>
	/// <param name="buttons">Each button's localization key and the action a click runs. The key is pushed as the
	/// button's ImGui ID, so two buttons whose translations match stay distinct.</param>
	public static void DrawButtonRow(LocalizationTable table, params (string key, Action callback)[] buttons) {
		Span<float> widths = stackalloc float[buttons.Length];
		for (int i = 0; i < buttons.Length; i++) {
			widths[i] = ButtonRow.LabelWidth(table.GetStringOrKey(buttons[i].key));
		}

		var row = new ButtonRow(widths);
		foreach (var (key, callback) in buttons) {
			ImGui.PushID(key);
			bool clicked = row.Button(table.GetStringOrKey(key));
			ImGui.PopID();
			if (clicked) {
				callback.Invoke();
			}
		}
	}
}
