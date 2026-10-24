using ImGuiNET;

namespace Herculan.Engine;

public static class ImGuiHelpers {
	/// <summary>Draws a <see cref="ButtonRow"/>, running a button's callback when it is clicked.</summary>
	public static void DrawButtonRow(params (string label, Action callback)[] buttons) {
		Span<float> widths = stackalloc float[buttons.Length];
		for (int i = 0; i < buttons.Length; i++) {
			widths[i] = ButtonRow.LabelWidth(buttons[i].label);
		}

		var row = new ButtonRow(widths);
		foreach (var (label, callback) in buttons) {
			if (row.Button(label)) {
				callback.Invoke();
			}
		}
	}
}
