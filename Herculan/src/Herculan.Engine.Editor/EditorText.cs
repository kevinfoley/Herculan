using System.Numerics;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>How the editor's panels draw their notes: the explanations and asides beside a field or a list.</summary>
internal static class EditorText {
	// Lighter than ImGui's own disabled grey, which is hard to read at the panels' size.
	private static readonly Vector4 NoteColor = new(0.68f, 0.68f, 0.68f, 1f);

	/// <summary>A note, wrapped at the window's edge.</summary>
	public static void Note(string text) {
		ImGui.PushStyleColor(ImGuiCol.Text, NoteColor);
		ImGui.TextWrapped(text);
		ImGui.PopStyleColor();
	}
}
