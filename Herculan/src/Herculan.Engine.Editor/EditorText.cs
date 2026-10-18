using System.Numerics;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>How the editor's panels draw their notes: the explanations and asides beside a field or a list.</summary>
internal static class EditorText {
	// Lighter than ImGui's own disabled grey, which is hard to read at the panels' size.
	private static readonly Vector4 NoteColor = new(0.68f, 0.68f, 0.68f, 1f);

	/// <summary>The bold face to draw <see cref="Emphasis"/> in, read each frame since the fonts are rebuilt with the window's scale.</summary>
	public static Func<ImFontPtr?>? BoldFont { get; set; }

	/// <summary>A note the user must not miss, in the bold face (the ordinary one when there is none), wrapped.</summary>
	public static void Emphasis(string text) {
		var bold = BoldFont?.Invoke();
		if (bold is { } font) {
			ImGui.PushFont(font);
		}

		ImGui.TextWrapped(text);
		if (bold != null) {
			ImGui.PopFont();
		}
	}

	/// <summary>
	/// Why a control is disabled, as a note led by <paramref name="lead"/>; nothing for null, or for a plain
	/// <c>script.dat</c>'s read-only reason (<see cref="PointMover.ReadOnlyReason"/>), which the viewport's banner says once
	/// for every control (<see cref="EditorHost"/>).
	/// </summary>
	public static void Reason(string? reason, string lead = "") {
		if (reason != null && !IsReadOnly(reason)) {
			Note(lead + reason);
		}
	}

	/// <summary>Whether <paramref name="reason"/> is a plain <c>script.dat</c>'s read-only reason, which no view repeats.</summary>
	public static bool IsReadOnly(string? reason) => reason == PointMover.ReadOnlyReason;

	/// <summary>A note, wrapped at the window's edge.</summary>
	public static void Note(string text) {
		ImGui.PushStyleColor(ImGuiCol.Text, NoteColor);
		ImGui.TextWrapped(text);
		ImGui.PopStyleColor();
	}

	/// <summary>Why an input is refused, wrapped, in a strong red, under the field <see cref="PushProblemField"/> tinted.</summary>
	public static void Problem(string text) {
		ImGui.PushStyleColor(ImGuiCol.Text, ProblemColor);
		ImGui.TextWrapped(text);
		ImGui.PopStyleColor();
	}

	/// <summary>Tints the next field's background as the one to change; <see cref="PopProblemField"/> after it.</summary>
	public static void PushProblemField() {
		ImGui.PushStyleColor(ImGuiCol.FrameBg, ProblemFieldColor);
		ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, ProblemFieldHoveredColor);
		ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ProblemFieldHoveredColor);
	}

	public static void PopProblemField() => ImGui.PopStyleColor(3);

	private static readonly Vector4 ProblemColor = new(1f, 0.27f, 0.2f, 1f);
	private static readonly Vector4 ProblemFieldColor = new(0.36f, 0.2f, 0.2f, 1f);
	private static readonly Vector4 ProblemFieldHoveredColor = new(0.44f, 0.25f, 0.25f, 1f);
}
