using ImGuiNET;
using System.Numerics;

namespace Herculan.Engine;

public static class ImGuiHelpers {
	/// <summary>Draws a row of equal-width buttons that fill the available width, running a button's callback when it is clicked.</summary>
	public static void DrawButtonRow(params (string label, Action callback)[] buttons) {
		var buttonSize = ButtonRowSize(buttons.Length);
		for (int i = 0; i < buttons.Length; i++) {
			var button = buttons[i];
			if (ImGui.Button(button.label, buttonSize)) {
				button.callback.Invoke();
			}
			if (i < buttons.Length - 1) {
				ImGui.SameLine();
			}
		}
	}

	/// <summary>The size of each button in a row of <paramref name="count"/> equal-width buttons that fills the available width.</summary>
	public static Vector2 ButtonRowSize(int count) {
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		return new Vector2((ImGui.GetContentRegionAvail().X - spacing * (count - 1)) / count, 0f);
	}
}
